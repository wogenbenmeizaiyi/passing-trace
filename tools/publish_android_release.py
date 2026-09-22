"""Publish a verified same-run Android artifact; latest.json is the final commit.

No credentials, endpoints or exception payloads are printed. Unit tests use an
in-memory S3 substitute; production credentials are read only in main().
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import threading
import time

MANIFEST_KEY = "releases/android/latest.json"
CHUNK_SIZE = 8 * 1024 * 1024


class ReleaseError(Exception):
    pass


@contextmanager
def stage(name):
    started = time.monotonic()
    outcome = "通过"
    try:
        yield
    except BaseException:
        outcome = "失败"
        raise
    finally:
        elapsed = time.monotonic() - started
        print(f"{name}: {outcome}，{elapsed:.1f} 秒", flush=True)
        if summary := os.environ.get("GITHUB_STEP_SUMMARY"):
            with open(summary, "a", encoding="utf-8") as stream:
                stream.write(f"- {name}: {outcome}，{elapsed:.1f} 秒\n")


class UploadProgress:
    def __init__(self, total):
        self.total = total
        self.transferred = 0
        self.lock = threading.Lock()
        self.stopped = threading.Event()
        self.started = time.monotonic()
        self.thread = threading.Thread(target=self._report, daemon=True)

    def __call__(self, amount):
        with self.lock:
            self.transferred += amount

    def report(self):
        with self.lock:
            amount = self.transferred
        elapsed = max(time.monotonic() - self.started, 0.001)
        print(f"APK 上传: {amount / 1048576:.1f}/{self.total / 1048576:.1f} MiB "
              f"({min(100, amount * 100 / self.total):.1f}%)，平均 {amount / 1048576 / elapsed:.2f} MiB/s",
              flush=True)

    def _report(self):
        while not self.stopped.wait(15):
            self.report()

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.stopped.set()
        self.thread.join(timeout=1)
        self.report()


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_manifest(value):
    if not isinstance(value, dict):
        raise ReleaseError("更新清单格式无效。")
    code, minimum = value.get("versionCode"), value.get("minimumSupportedVersionCode")
    name = value.get("versionName", "")
    if (type(code) is not int or not 0 < code <= 2100000000 or
            type(minimum) is not int or not 0 < minimum <= code or
            not isinstance(name, str) or not re.fullmatch(r"[0-9]+(?:\.[0-9]+){1,3}(?:[-.][A-Za-z0-9]+)*", name)):
        raise ReleaseError("版本信息无效。")
    if (value.get("objectKey") != f"releases/android/PassingTrace-{name}-{code}.apk" or
            not re.fullmatch(r"[a-f0-9]{64}", str(value.get("sha256", ""))) or
            type(value.get("size")) is not int or value["size"] <= 0):
        raise ReleaseError("安装包信息无效。")


def prepare(apk, directory):
    import shutil
    name = os.environ["RELEASE_VERSION_NAME"]
    code = int(os.environ["RELEASE_VERSION_CODE"])
    manifest = {
        "versionName": name, "versionCode": code,
        "publishedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "objectKey": f"releases/android/PassingTrace-{name}-{code}.apk",
        "sha256": sha256(apk), "size": apk.stat().st_size,
        "notes": os.environ["RELEASE_NOTES"],
        "minimumSupportedVersionCode": int(os.environ["RELEASE_MINIMUM_SUPPORTED_VERSION_CODE"]),
    }
    validate_manifest(manifest)
    directory.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(apk, directory / "PassingTrace.apk")
    (directory / "latest.json").write_text(json.dumps(manifest, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    (directory / "build.json").write_text(json.dumps({
        "commit": os.environ["GITHUB_SHA"], "runId": os.environ["GITHUB_RUN_ID"],
        "certificateSha256": os.environ["ANDROID_SIGNING_SHA256"],
    }), encoding="utf-8")
    (directory / "sha256.txt").write_text(manifest["sha256"] + "  PassingTrace.apk\n", encoding="utf-8")


def load_artifact(directory):
    manifest = read_json(directory / "latest.json")
    validate_manifest(manifest)
    build = read_json(directory / "build.json")
    if (build.get("commit") != os.environ["GITHUB_SHA"] or build.get("runId") != os.environ["GITHUB_RUN_ID"] or
            build.get("certificateSha256") != os.environ["ANDROID_SIGNING_SHA256"]):
        raise ReleaseError("安装包不是本次运行的已验签产物。")
    apk = directory / "PassingTrace.apk"
    if apk.stat().st_size != manifest["size"] or sha256(apk) != manifest["sha256"]:
        raise ReleaseError("安装包完整性校验失败。")
    return manifest


def is_missing(error):
    return getattr(error, "response", {}).get("Error", {}).get("Code") in ("NoSuchKey", "404", "NotFound")


def remote_manifest(client, bucket):
    try:
        response = client.get_object(Bucket=bucket, Key=MANIFEST_KEY)
    except Exception as error:
        if is_missing(error):
            return None, None
        raise
    try:
        value = json.loads(response["Body"].read())
    finally:
        response["Body"].close()
    validate_manifest(value)
    return value, response["ETag"]


def version_decision(current, candidate):
    if current is None or current["versionCode"] < candidate["versionCode"]:
        return "new"
    if current["versionCode"] > candidate["versionCode"]:
        return "stale"
    if all(current[key] == candidate[key] for key in ("sha256", "objectKey", "size", "versionName")):
        return "same"
    raise ReleaseError("同一版本号已有不同安装包，请使用递增的版本号。")


def matching_object(head, candidate):
    return (head.get("ContentLength") == candidate["size"] and
            head.get("Metadata", {}).get("sha256") == candidate["sha256"])


def publish(client, bucket, directory, transfer_config=None):
    with stage("产物检查"):
        manifest = load_artifact(directory)
    with stage("发布前版本检查"):
        current, _ = remote_manifest(client, bucket)
        decision = version_decision(current, manifest)
    if decision == "stale":
        print("跳过旧版本：线上版本更高，原下载入口保持不变。", flush=True)
        return "stale"
    if decision == "same":
        # An uncertain/retried publish must still verify the referenced object.
        if not matching_object(client.head_object(Bucket=bucket, Key=manifest["objectKey"]), manifest):
            raise ReleaseError("线上安装包检查失败，未修改更新入口。")
        print("本版本已经发布，校验通过，无需重复上传。", flush=True)
        return "same"
    try:
        existing = client.head_object(Bucket=bucket, Key=manifest["objectKey"])
    except Exception as error:
        if not is_missing(error):
            raise
        existing = None
    if existing is not None and not matching_object(existing, manifest):
        raise ReleaseError("同名安装包内容不同，拒绝覆盖。")
    with stage("APK 上传"):
        if existing is None:
            with UploadProgress(manifest["size"]) as progress:
                client.upload_file(
                    str(directory / "PassingTrace.apk"), bucket, manifest["objectKey"],
                    ExtraArgs={"ContentType": "application/vnd.android.package-archive",
                               "Metadata": {"sha256": manifest["sha256"]}},
                    Config=transfer_config, Callback=progress,
                )
        else:
            print("已找到本次安装包，跳过重复上传。", flush=True)
    with stage("远端安装包检查"):
        if not matching_object(client.head_object(Bucket=bucket, Key=manifest["objectKey"]), manifest):
            raise ReleaseError("远端大小或校验元数据不一致，未修改更新入口。")
    with stage("更新下载入口"):
        current, etag = remote_manifest(client, bucket)
        decision = version_decision(current, manifest)
        if decision != "new":
            print("另一个发布已更新入口，本次不覆盖。", flush=True)
            return decision
        # Fail closed if the storage service cannot guarantee a conditional write.
        # Never fall back to an unconditional PUT after a conflict.
        condition = {"IfMatch": etag} if current else {"IfNoneMatch": "*"}
        client.put_object(
            Bucket=bucket, Key=MANIFEST_KEY,
            Body=json.dumps(manifest, ensure_ascii=False, separators=(",", ":")).encode("utf-8"),
            ContentType="application/json; charset=utf-8", CacheControl="no-cache",
            **condition,
        )
    return "published"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("prepare", "publish"))
    parser.add_argument("--directory", type=Path, default=Path("release"))
    parser.add_argument("--apk", type=Path, default=Path("passingtrace-mobile/build/app/outputs/flutter-apk/app-production-release.apk"))
    args = parser.parse_args()
    try:
        if args.mode == "prepare":
            prepare(args.apk, args.directory)
            return 0
        import boto3
        from boto3.s3.transfer import TransferConfig
        from botocore.config import Config
        client = boto3.client("s3", endpoint_url=os.environ["S3_ENDPOINT"],
                             region_name=os.environ.get("S3_REGION") or "us-east-1",
                             config=Config(s3={"addressing_style": "virtual"},
                                           retries={"mode": "standard", "total_max_attempts": 3},
                                           connect_timeout=15, read_timeout=90,
                                           max_pool_connections=10,
                                           request_checksum_calculation="when_required",
                                           response_checksum_validation="when_required"))
        publish(client, os.environ["S3_BUCKET"], args.directory,
                TransferConfig(multipart_threshold=CHUNK_SIZE, multipart_chunksize=CHUNK_SIZE,
                               max_concurrency=10, num_download_attempts=3))
        return 0
    except ReleaseError as error:
        print(f"::error::{error}", flush=True)
    except Exception:
        print("::error::发布阶段未完成。请检查该阶段的网络、授权或存储条件写入支持；未进行无条件清单覆盖。", flush=True)
    return 1


if __name__ == "__main__":
    sys.exit(main())
