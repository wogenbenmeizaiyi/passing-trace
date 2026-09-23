import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import relay_android_release as relay


class RelayTests(unittest.TestCase):
    def archive(self, names):
        content = io.BytesIO()
        with zipfile.ZipFile(content, "w") as archive:
            for name in names:
                archive.writestr(name, b"test")
        content.seek(0)
        return content

    def test_extract_expected_files_only(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / "release"
            relay.extract_artifact(self.archive(relay.FILES), target)
            self.assertEqual(relay.FILES, {p.name for p in target.iterdir()})

    def test_reject_traversal_extra_missing_and_duplicate_files(self):
        for names in (["../PassingTrace.apk"], [*relay.FILES, "secret"],
                      ["PassingTrace.apk"], [*relay.FILES, "PassingTrace.apk"]):
            with self.subTest(names=names), tempfile.TemporaryDirectory() as temp:
                target = Path(temp) / "release"
                with self.assertRaises(relay.release.ReleaseError):
                    relay.extract_artifact(self.archive(names), target)
                self.assertFalse(target.exists())

    def test_reject_oversized_uncompressed_artifact(self):
        with tempfile.TemporaryDirectory() as temp, patch.object(relay, "MAX_BYTES", 1):
            with self.assertRaises(relay.release.ReleaseError):
                relay.extract_artifact(self.archive(relay.FILES), Path(temp) / "release")

    def test_url_is_for_current_run_and_does_not_forward_token(self):
        environment = {"GH_TOKEN": "test-token", "GITHUB_REPOSITORY": "owner/repo",
                       "GITHUB_RUN_ID": "123", "RELEASE_ARTIFACT_NAME": "android-production-123"}
        listing = {"artifacts": [{"id": 42, "name": "android-production-123", "expired": False, "size_in_bytes": 10}]}
        with patch.dict(os.environ, environment), patch.object(relay.urllib.request, "urlopen") as listing_open, \
                patch.object(relay.urllib.request, "build_opener") as opener:
            listing_open.return_value.__enter__.return_value = io.BytesIO(json.dumps(listing).encode())
            opener.return_value.open.side_effect = urllib.error.HTTPError(
                "https://api.github.com", 302, "redirect", {"Location": "https://blob.example/artifact?signed=value"}, None)
            self.assertEqual("https://blob.example/artifact?signed=value", relay.artifact_url())
            self.assertIn("/runs/123/artifacts", listing_open.call_args.args[0].full_url)
            self.assertIn("/artifacts/42/zip", opener.return_value.open.call_args.args[0].full_url)
            self.assertNotIn("GH_TOKEN", relay.ENV_KEYS)

    def test_expired_or_foreign_artifact_rejected(self):
        for name, expired in (("android-production-other", False), ("android-production-123", True)):
            with patch.dict(os.environ, {"GH_TOKEN": "x", "GITHUB_REPOSITORY": "o/r", "GITHUB_RUN_ID": "123",
                                       "RELEASE_ARTIFACT_NAME": "android-production-123"}), \
                    patch.object(relay.urllib.request, "urlopen") as request:
                request.return_value.__enter__.return_value = io.BytesIO(json.dumps({"artifacts": [
                    {"id": 1, "name": name, "expired": expired, "size_in_bytes": 1}]}).encode())
                with self.assertRaises(relay.release.ReleaseError):
                    relay.artifact_url()

    def test_workflow_uses_relay_without_rebuilding_or_downloading_apk_on_runner(self):
        workflow = (Path(relay.__file__).parents[1] / ".github/workflows/release-android.yml").read_text(encoding="utf-8")
        publish = workflow.split("  publish:")[1]
        self.assertIn("actions: read", publish)
        self.assertIn("python3 tools/relay_android_release.py", publish)
        self.assertNotIn("actions/download-artifact", publish)
        self.assertNotIn("flutter build", publish)
        self.assertIn('test "$actual" = "$EXPECTED_HOST_FINGERPRINT"', publish)


if __name__ == "__main__":
    unittest.main()
