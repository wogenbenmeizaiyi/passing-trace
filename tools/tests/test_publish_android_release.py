import copy
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import publish_android_release as release


class StorageError(Exception):
    def __init__(self, code):
        self.response = {"Error": {"Code": code}}


class FakeS3:
    def __init__(self):
        self.objects = {}
        self.calls = []
        self.fail_upload = False
        self.fail_manifest = False
        self.corrupt = False
        self.after_upload = None
        self.before_put = None

    def get_object(self, Bucket, Key):
        self.calls.append(("get", Key))
        if Key not in self.objects:
            raise StorageError("NoSuchKey")
        content, _ = self.objects[Key]
        return {"Body": io.BytesIO(content), "ETag": hashlib.sha256(content).hexdigest()}

    def head_object(self, Bucket, Key):
        self.calls.append(("head", Key))
        if Key not in self.objects:
            raise StorageError("404")
        content, metadata = self.objects[Key]
        return {"ContentLength": len(content), "Metadata": metadata}

    def upload_file(self, Filename, Bucket, Key, ExtraArgs, Config, Callback):
        self.calls.append(("upload", Key))
        if self.fail_upload:
            raise StorageError("Timeout")
        content = Path(Filename).read_bytes()
        self.objects[Key] = (content, {} if self.corrupt else ExtraArgs["Metadata"])
        Callback(len(content))
        if self.after_upload:
            self.after_upload()

    def put_object(self, **kwargs):
        self.calls.append(("put", kwargs["Key"]))
        if self.fail_manifest:
            raise StorageError("Timeout")
        if self.before_put:
            self.before_put()
        current = self.objects.get(kwargs["Key"])
        if kwargs.get("IfNoneMatch") == "*" and current:
            raise StorageError("PreconditionFailed")
        if "IfMatch" in kwargs and (not current or hashlib.sha256(current[0]).hexdigest() != kwargs["IfMatch"]):
            raise StorageError("PreconditionFailed")
        self.objects[kwargs["Key"]] = (kwargs["Body"], {})


class AndroidReleaseTests(unittest.TestCase):
    def test_slow_link_upload_settings_are_bounded_and_consistent(self):
        transfer = release.transfer_options()
        self.assertEqual(5 * 1024 * 1024, transfer['multipart_chunksize'])
        self.assertEqual(3, transfer['max_concurrency'])
        self.assertEqual('classic', transfer['preferred_transfer_client'])
        self.assertEqual(3, release.CLIENT_OPTIONS['max_pool_connections'])
        self.assertEqual(120, release.CLIENT_OPTIONS['connect_timeout'])
        self.assertEqual(300, release.CLIENT_OPTIONS['read_timeout'])
        self.assertEqual(6, release.CLIENT_OPTIONS['retries']['total_max_attempts'])

    def test_error_diagnostics_keep_causes_but_never_exception_payloads(self):
        inner = StorageError('RequestTimeout')
        inner.response['Error']['Message'] = 'secret-key signed-url'
        inner.response['ResponseMetadata'] = {'HTTPStatusCode': 408, 'RetryAttempts': 5,
                                              'HTTPHeaders': {'Authorization': 'secret-key'}}
        outer = RuntimeError('secret-key signed-url')
        outer.__cause__ = inner
        details = release.safe_error_details(outer)
        self.assertIn('RequestTimeout', details)
        self.assertIn('408', details)
        self.assertNotIn('secret-key', details)
        self.assertNotIn('signed-url', details)
        inner.__cause__ = outer
        self.assertEqual(2, len(json.loads(release.safe_error_details(outer))))
        self.assertNotIn('secret-key', release.safe_error_details(StorageError('secret-key')))

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.environment = patch.dict(os.environ, {
            "GITHUB_SHA": "test-sha", "GITHUB_RUN_ID": "123",
            "ANDROID_SIGNING_SHA256": "test-certificate",
            "RELEASE_VERSION_NAME": "1.0.20", "RELEASE_VERSION_CODE": "20",
            "RELEASE_MINIMUM_SUPPORTED_VERSION_CODE": "1", "RELEASE_NOTES": "测试",
            "GITHUB_STEP_SUMMARY": str(self.root / "summary.md"),
        })
        self.environment.start()
        self.addCleanup(self.environment.stop)
        self.apk = self.root / "input.apk"
        self.apk.write_bytes(b"verified apk contents")
        self.directory = self.root / "release"
        release.prepare(self.apk, self.directory)
        self.manifest = release.load_artifact(self.directory)
        self.client = FakeS3()

    def install(self, code):
        manifest = copy.deepcopy(self.manifest)
        manifest["versionCode"] = code
        manifest["versionName"] = f"1.0.{code}"
        manifest["objectKey"] = f"releases/android/PassingTrace-1.0.{code}-{code}.apk"
        self.client.objects[release.MANIFEST_KEY] = (json.dumps(manifest).encode(), {})
        return manifest

    def publish(self):
        return release.publish(self.client, "test-bucket", self.directory)

    def test_upload_verify_then_manifest_and_idempotent_rerun(self):
        self.assertEqual("published", self.publish())
        calls = self.client.calls
        uploaded = calls.index(("upload", self.manifest["objectKey"]))
        verified = calls.index(("head", self.manifest["objectKey"]), uploaded)
        committed = calls.index(("put", release.MANIFEST_KEY))
        self.assertLess(uploaded, verified)
        self.assertLess(verified, committed)
        self.assertEqual("same", self.publish())
        self.assertEqual(1, len([x for x in calls if x[0] == "upload"]))

    def test_upload_failure_keeps_previous_entry(self):
        self.install(19)
        previous = self.client.objects[release.MANIFEST_KEY]
        self.client.fail_upload = True
        with self.assertRaises(StorageError):
            self.publish()
        self.assertEqual(previous, self.client.objects[release.MANIFEST_KEY])
        self.assertNotIn(("put", release.MANIFEST_KEY), self.client.calls)

    def test_remote_checksum_failure_keeps_previous_entry(self):
        self.install(19)
        previous = self.client.objects[release.MANIFEST_KEY]
        self.client.corrupt = True
        with self.assertRaises(release.ReleaseError):
            self.publish()
        self.assertEqual(previous, self.client.objects[release.MANIFEST_KEY])

    def test_manifest_retry_reuses_uploaded_apk(self):
        self.install(19)
        self.client.fail_manifest = True
        with self.assertRaises(StorageError):
            self.publish()
        self.client.fail_manifest = False
        self.assertEqual("published", self.publish())
        self.assertEqual(1, len([x for x in self.client.calls if x[0] == "upload"]))

    def test_manual_old_version_never_uploads(self):
        self.install(21)
        self.assertEqual("stale", self.publish())
        self.assertFalse(any(x[0] in ("upload", "put") for x in self.client.calls))

    def test_newer_release_appearing_during_upload_wins(self):
        self.install(19)
        self.client.after_upload = lambda: self.install(21)
        self.assertEqual("stale", self.publish())
        self.assertNotIn(("put", release.MANIFEST_KEY), self.client.calls)

    def test_conditional_write_closes_last_read_write_race(self):
        self.install(19)
        self.client.before_put = lambda: self.install(21)
        with self.assertRaises(StorageError):
            self.publish()
        latest, _ = release.remote_manifest(self.client, "test-bucket")
        self.assertEqual(21, latest["versionCode"])

    def test_same_version_different_binary_is_rejected(self):
        manifest = self.install(20)
        manifest["sha256"] = "f" * 64
        self.client.objects[release.MANIFEST_KEY] = (json.dumps(manifest).encode(), {})
        with self.assertRaises(release.ReleaseError):
            self.publish()
        self.assertFalse(any(x[0] == "upload" for x in self.client.calls))

    def test_artifact_tampering_and_cross_run_are_rejected(self):
        (self.directory / "PassingTrace.apk").write_bytes(b"changed")
        with self.assertRaises(release.ReleaseError):
            self.publish()
        release.prepare(self.apk, self.directory)
        with patch.dict(os.environ, {"GITHUB_RUN_ID": "456"}):
            with self.assertRaises(release.ReleaseError):
                self.publish()
        self.assertEqual([], self.client.calls)

    def test_unreadable_remote_manifest_fails_closed(self):
        self.client.objects[release.MANIFEST_KEY] = (b"not-json", {})
        with self.assertRaises(ValueError):
            self.publish()
        self.assertFalse(any(x[0] in ("upload", "put") for x in self.client.calls))

    def test_timed_command_failure_keeps_exit_status(self):
        timed = Path(release.__file__).with_name("ci_timed.py")
        result = subprocess.run([sys.executable, str(timed), "test", sys.executable, "-c", "raise SystemExit(7)"], capture_output=True)
        self.assertEqual(7, result.returncode)
        self.assertIn("失败", (self.root / "summary.md").read_text(encoding="utf-8"))

    def test_workflow_has_two_independent_gates_and_same_run_artifact(self):
        workflow = (Path(release.__file__).parents[1] / ".github/workflows/release-android.yml").read_text(encoding="utf-8")
        verify = workflow.split("  verify:")[1].split("  build:")[0]
        build = workflow.split("  build:")[1].split("  publish:")[0]
        publish = workflow.split("  publish:")[1]
        self.assertNotIn("needs:", verify + build)
        self.assertNotIn("secrets.", verify)
        self.assertIn("needs: [verify, build]", publish)
        self.assertNotIn("flutter build", publish)
        self.assertIn("cancel-in-progress: false", publish)
        self.assertIn("android-production-${{ github.run_id }}", build)
        self.assertIn("android-production-${{ github.run_id }}", publish)
        self.assertLess(build.index("Verify production signing certificate"), build.index("actions/upload-artifact"))
        self.assertIn("flutter test --no-pub", verify)
        self.assertIn("--enforce-lockfile", verify)
        self.assertIn("--enforce-lockfile", build)


if __name__ == "__main__":
    unittest.main()
