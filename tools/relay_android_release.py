"""Publish a same-run artifact through the existing Hong Kong SSH host.

GitHub credentials stay on the runner. A short-lived artifact URL and storage
credentials travel over SSH stdin, never in command arguments or disk files.
"""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import urllib.error
import urllib.request
import urllib.parse
import zipfile

import publish_android_release as release

FILES = {"PassingTrace.apk", "latest.json", "build.json", "sha256.txt"}
MAX_BYTES = 600 * 1024 * 1024
ENV_KEYS = ("GITHUB_SHA", "GITHUB_RUN_ID", "ANDROID_SIGNING_SHA256",
            "S3_ENDPOINT", "S3_BUCKET", "S3_REGION", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def api(path):
    request = urllib.request.Request("https://api.github.com/" + path, headers={
        "Authorization": "Bearer " + os.environ["GH_TOKEN"],
        "Accept": "application/vnd.github+json", "User-Agent": "PassingTrace-Android-Relay"})
    return request


def artifact_url():
    repo, run = os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_RUN_ID"]
    with urllib.request.urlopen(api(f"repos/{repo}/actions/runs/{run}/artifacts?per_page=100"), timeout=30) as response:
        artifacts = json.load(response)["artifacts"]
    matches = [a for a in artifacts if a["name"] == os.environ["RELEASE_ARTIFACT_NAME"] and not a["expired"]]
    if len(matches) != 1 or matches[0]["size_in_bytes"] > MAX_BYTES:
        raise release.ReleaseError("本次运行的发布产物缺失或大小异常。")
    try:
        urllib.request.build_opener(NoRedirect()).open(
            api(f"repos/{repo}/actions/artifacts/{int(matches[0]['id'])}/zip"), timeout=30)
    except urllib.error.HTTPError as error:
        if error.code == 302:
            url = error.headers["Location"]
            if urllib.parse.urlparse(url).scheme == "https":
                return url
        raise
    raise release.ReleaseError("未取得临时产物下载链接。")


def extract_artifact(archive, destination):
    """Never extract paths supplied by the archive or unbounded extra files."""
    with zipfile.ZipFile(archive) as bundle:
        entries = bundle.infolist()
        if (len(entries) != len(FILES) or {x.filename for x in entries} != FILES
                or sum(x.file_size for x in entries) > MAX_BYTES):
            raise release.ReleaseError("发布产物文件结构无效。")
        destination.mkdir()
        for entry in entries:
            with bundle.open(entry) as source, (destination / entry.filename).open("wb") as target:
                shutil.copyfileobj(source, target)


def remote():
    import fcntl
    payload = json.load(sys.stdin)
    for key in ENV_KEYS:
        os.environ[key] = payload["environment"][key]
    os.environ["GITHUB_STEP_SUMMARY"] = str(Path.cwd() / "summary.md")
    # Serialize even if an SSH connection drops while a remote publisher lives.
    lock_path = Path.home() / ".cache/passingtrace-android-publish.lock"
    lock_path.parent.mkdir(exist_ok=True)
    with lock_path.open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        with tempfile.TemporaryDirectory(prefix="release-", dir=Path.cwd()) as tmp:
            root = Path(tmp)
            with release.stage("香港服务器下载 GitHub 产物"):
                url = payload["url"]
                if urllib.parse.urlparse(url).scheme != "https":
                    raise release.ReleaseError("产物下载地址必须使用 HTTPS。")
                with urllib.request.urlopen(url, timeout=60) as response, (root / "artifact.zip").open("wb") as target:
                    total = 0
                    while chunk := response.read(1024 * 1024):
                        total += len(chunk)
                        if total > MAX_BYTES:
                            raise release.ReleaseError("发布产物下载大小异常。")
                        target.write(chunk)
            extract_artifact(root / "artifact.zip", root / "release")
            import boto3
            from boto3.s3.transfer import TransferConfig
            from botocore.config import Config
            client = boto3.client("s3", endpoint_url=os.environ["S3_ENDPOINT"],
                region_name=os.environ.get("S3_REGION") or "us-east-1",
                config=Config(**release.CLIENT_OPTIONS))
            release.publish(client, os.environ["S3_BUCKET"], root / "release",
                            TransferConfig(**release.transfer_options()))


def runner():
    tools = Path(__file__).resolve().parent
    host = os.environ["DEPLOY_USER"] + "@" + os.environ["DEPLOY_HOST"]
    args = ["-i", os.path.expanduser("~/.ssh/passingtrace_ci"), "-o", "IdentitiesOnly=yes",
            "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=15"]
    ssh = ["ssh", *args, host]
    remote_dir = None
    try:
        with tempfile.TemporaryDirectory() as temp, release.stage("香港中转发布总耗时"):
            root = Path(temp)
            with release.stage("上传依赖准备"):
                subprocess.run([sys.executable, "-m", "pip", "install", "--disable-pip-version-check", "--quiet",
                    "--target", str(root / "sdk"), "-r", str(tools / "android-release-requirements.txt")], check=True)
                with tarfile.open(root / "bundle.tar.gz", "w:gz") as bundle:
                    bundle.add(root / "sdk", arcname="sdk")
                    for name in ("relay_android_release.py", "publish_android_release.py"):
                        bundle.add(tools / name, arcname=name)
            remote_dir = subprocess.check_output(ssh + ["umask 077; mktemp -d /tmp/passingtrace-android-publish.XXXXXXXX"], text=True).strip()
            if not re.fullmatch(r"/tmp/passingtrace-android-publish\.[A-Za-z0-9]{8}", remote_dir):
                remote_dir = None
                raise release.ReleaseError("服务器未返回有效临时目录。")
            subprocess.run(["scp", *args, str(root / "bundle.tar.gz"), f"{host}:{remote_dir}/bundle.tar.gz"], check=True)
            payload = {"url": artifact_url(), "environment": {k: os.environ.get(k, "") for k in ENV_KEYS}}
            try:
                subprocess.run(ssh + [f"cd {remote_dir} && tar -xzf bundle.tar.gz && "
                    "PYTHONPATH=sdk timeout --signal=TERM --kill-after=30s 1500s python3 -u relay_android_release.py --remote"],
                    input=json.dumps(payload), text=True, check=True, timeout=1560)
            finally:
                result = subprocess.run(ssh + [f"cat {remote_dir}/summary.md"], capture_output=True, text=True, timeout=30)
                if result.returncode == 0 and os.environ.get("GITHUB_STEP_SUMMARY"):
                    with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as summary:
                        summary.write(result.stdout)
    finally:
        if remote_dir:
            subprocess.run(ssh + [f"test \"$(readlink -f {remote_dir})\" = {remote_dir} && rm -rf -- {remote_dir}"],
                           check=True, timeout=30)


if __name__ == "__main__":
    try:
        remote() if sys.argv[1:] == ["--remote"] else runner()
    except Exception as error:
        # Do not stringify exceptions: download URLs carry temporary credentials.
        print(f"::error::香港中转发布失败: {release.safe_error_details(error)}", flush=True)
        sys.exit(1)
