import base64
import contextlib
import hashlib
import http.client
import importlib.util
import io
import json
import pathlib
import sys
import unittest
import urllib.error
import urllib.parse
import urllib.request
from email.message import Message
from unittest import mock


MODULE_PATH = pathlib.Path(__file__).parents[1] / "seed_development_demo.py"
SPEC = importlib.util.spec_from_file_location("seed_development_demo", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)

IDENTITY = "http://localhost:56229"
API = "http://localhost:54934"
TOTALS = {
    "createdRecords": 36, "existingRecords": 0,
    "createdStorylines": 4, "existingStorylines": 0, "skippedStorylines": 0,
}


class Response(io.BytesIO):
    def __init__(self, payload=None, status=200, location=None):
        super().__init__(json.dumps(payload).encode() if payload is not None else b"")
        self.status = status
        self.headers = Message()
        self.headers["Content-Type"] = "application/json"
        if location is not None:
            self.headers["Location"] = location

    def getcode(self):
        return self.status


class DevelopmentDemoSeedTests(unittest.TestCase):
    def test_only_loopback_origins_are_accepted(self):
        for value in (IDENTITY, "http://127.0.0.1:1234/", "https://[::1]:443", "http://127.10.2.3"):
            with self.subTest(value=value):
                self.assertEqual(value.rstrip("/"), MODULE.loopback_origin(value))
        for value in (
            "https://passingtrace.com", "http://192.168.1.2", "http://0.0.0.0", "http://localhost.evil",
            "http://localhost@evil", "http://user:secret@localhost", "http://localhost/path",
            "http://localhost?token=secret", "http://localhost?", "http://localhost#", "file://localhost",
            "http://localhost:70000", "http://localhost:0", "http://localhost\\@evil", "\nhttp://localhost",
            "http://127.1", "http://2130706433", "http://[invalid]", "http://localhost:bad",
        ):
            with self.subTest(value=value), self.assertRaises(MODULE.SeedError) as caught:
                MODULE.loopback_origin(value)
            self.assertNotIn("secret", str(caught.exception))

    def test_no_redirect_is_followed_even_to_same_origin(self):
        handler = MODULE.NoRedirect()
        request = urllib.request.Request(IDENTITY)
        for location in (IDENTITY + "/account/qr-login/x", MODULE.REDIRECT_URI, "https://evil.invalid"):
            with self.subTest(location=location):
                self.assertIsNone(handler.redirect_request(request, None, 302, "Found", {}, location))

    def test_callback_requires_exact_target_matching_state_and_one_code(self):
        callback = MODULE.REDIRECT_URI + "?state=expected&code=one-time-code"
        self.assertEqual("one-time-code", MODULE.callback_code(callback, "expected"))
        for value in (
            callback.replace("localhost", "127.0.0.1"), callback.replace("http:", "https:"),
            callback.replace("/auth/callback", "/auth/callback/extra"), callback + "#fragment",
            callback + "&state=expected", callback + "&code=duplicate", callback.replace("expected", "wrong"),
            MODULE.REDIRECT_URI + "?state=expected", MODULE.REDIRECT_URI + "?state=expected&code=",
            MODULE.REDIRECT_URI + "?state=expected&error=login_required&error_description=secret",
        ):
            with self.subTest(value=value), self.assertRaises(MODULE.SeedError) as caught:
                MODULE.callback_code(value, "expected")
            self.assertNotIn("one-time-code", str(caught.exception))
            self.assertNotIn("secret", str(caught.exception))

    def test_authorization_code_pkce_and_seed_headers(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        authorization = {}
        identity_requests = []

        def identity_open(request, timeout):
            identity_requests.append(request)
            self.assertNotIn("Authorization", dict(request.header_items()))
            if request.method == "GET":
                authorization.update(urllib.parse.parse_qs(urllib.parse.urlsplit(request.full_url).query))
                self.assertEqual([MODULE.CLIENT_ID], authorization["client_id"])
                self.assertEqual([MODULE.REDIRECT_URI], authorization["redirect_uri"])
                self.assertEqual(["code"], authorization["response_type"])
                self.assertEqual([MODULE.SCOPE], authorization["scope"])
                self.assertEqual(["S256"], authorization["code_challenge_method"])
                self.assertTrue(authorization["nonce"][0])
                return Response(status=302, location=MODULE.REDIRECT_URI + "?" + urllib.parse.urlencode({
                    "state": authorization["state"][0], "code": "one-time-code",
                }))
            self.assertEqual(IDENTITY + "/connect/token", request.full_url)
            self.assertEqual("application/x-www-form-urlencoded", request.get_header("Content-type"))
            form = urllib.parse.parse_qs(request.data.decode())
            self.assertEqual(["authorization_code"], form["grant_type"])
            self.assertEqual(["one-time-code"], form["code"])
            self.assertEqual([MODULE.CLIENT_ID], form["client_id"])
            self.assertEqual([MODULE.REDIRECT_URI], form["redirect_uri"])
            verifier = form["code_verifier"][0]
            self.assertTrue(43 <= len(verifier) <= 128)
            challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
            self.assertEqual([challenge], authorization["code_challenge"])
            return Response({"access_token": "private-access-token", "token_type": "Bearer"})

        def api_open(request, timeout):
            self.assertEqual(API + MODULE.SEED_PATH, request.full_url)
            self.assertEqual("POST", request.method)
            self.assertEqual(b"{}", request.data)
            self.assertEqual("Bearer private-access-token", request.get_header("Authorization"))
            self.assertEqual("application/json", request.get_header("Content-type"))
            self.assertIsNone(request.get_header("Cookie"))
            return Response(TOTALS)

        client.identity_opener.open = mock.Mock(side_effect=identity_open)
        client.api_opener.open = mock.Mock(side_effect=api_open)
        client.authenticate()
        self.assertEqual(TOTALS, client.seed())
        self.assertEqual(2, len(identity_requests))
        client.api_opener.open.assert_called_once()

    def test_identity_cookies_are_isolated_from_api_and_proxies_disabled(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        self.assertTrue(any(isinstance(handler, urllib.request.HTTPCookieProcessor) for handler in client.identity_opener.handlers))
        self.assertFalse(any(isinstance(handler, urllib.request.HTTPCookieProcessor) for handler in client.api_opener.handlers))
        for opener in (client.identity_opener, client.api_opener):
            self.assertFalse(any(isinstance(handler, urllib.request.ProxyHandler) and handler.proxies for handler in opener.handlers))

    def test_disabled_auto_login_reports_help_without_following_qr_redirect(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.identity_opener.open = mock.Mock(return_value=Response(
            status=302, location="/account/qr-login/session?code=private-qr-code",
        ))
        with self.assertRaisesRegex(MODULE.SeedError, "DevelopmentAutoLogin") as caught:
            client.authenticate()
        self.assertNotIn("private-qr-code", str(caught.exception))
        client.identity_opener.open.assert_called_once()

    def test_malformed_redirect_is_sanitized_without_following(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.identity_opener.open = mock.Mock(return_value=Response(
            status=302, location="http://[invalid?code=secret",
        ))
        with self.assertRaisesRegex(MODULE.SeedError, "unexpected OIDC callback") as caught:
            client.authenticate()
        self.assertNotIn("secret", str(caught.exception))
        client.identity_opener.open.assert_called_once()

    def test_waits_for_startup_with_read_only_requests(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.identity_opener.open = mock.Mock(side_effect=[
            urllib.error.URLError("private network details"),
            Response({"authorization_endpoint": IDENTITY + "/connect/authorize", "token_endpoint": IDENTITY + "/connect/token"}),
        ])
        client.api_opener.open = mock.Mock(side_effect=[
            urllib.error.HTTPError(API, 503, "Unavailable", {}, io.BytesIO(b"secret")),
            urllib.error.HTTPError(API, 405, "Not Allowed", {}, io.BytesIO()),
        ])
        with mock.patch.object(MODULE.time, "sleep") as sleep:
            client.wait_until_ready()
        self.assertEqual(2, sleep.call_count)
        for opener in (client.identity_opener, client.api_opener):
            for call in opener.open.call_args_list:
                self.assertEqual("GET", call.args[0].method)
                self.assertIsNone(call.args[0].data)

    def test_readiness_timeout_is_bounded_and_sanitized(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.identity_opener.open = mock.Mock(side_effect=urllib.error.URLError("secret"))
        with mock.patch.object(MODULE.time, "monotonic", side_effect=[0, 0, 120, 120]), \
                mock.patch.object(MODULE.time, "sleep"), \
                self.assertRaisesRegex(MODULE.SeedError, "Timed out") as caught:
            client.wait_until_ready()
        self.assertNotIn("secret", str(caught.exception))
        client.identity_opener.open.assert_called_once()

    def test_discovery_cannot_direct_authentication_outside_identity(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.identity_opener.open = mock.Mock(return_value=Response({
            "authorization_endpoint": "https://evil.invalid", "token_endpoint": IDENTITY + "/connect/token",
        }))
        client.api_opener.open = mock.Mock()
        with self.assertRaisesRegex(MODULE.SeedError, "expected local OIDC"):
            client.wait_until_ready()
        client.api_opener.open.assert_not_called()

    def test_failed_seed_is_never_retried_and_never_prints_sensitive_details(self):
        for failure in (
            urllib.error.HTTPError(API + "?code=secret", 403, "secret", {}, io.BytesIO(b"secret")),
            urllib.error.URLError("secret"),
            http.client.IncompleteRead(b"secret"),
        ):
            with self.subTest(failure=type(failure)):
                client = MODULE.DevelopmentDemoClient(IDENTITY, API)
                client.access_token = "secret"
                client.api_opener.open = mock.Mock(side_effect=failure)
                with self.assertRaises(MODULE.SeedError) as caught:
                    client.seed()
                self.assertNotIn("secret", str(caught.exception))
                self.assertNotIn(API, str(caught.exception))
                client.api_opener.open.assert_called_once()

    def test_seed_validates_totals_and_requires_authentication(self):
        client = MODULE.DevelopmentDemoClient(IDENTITY, API)
        client.api_opener.open = mock.Mock()
        with self.assertRaises(MODULE.SeedError):
            client.seed()
        client.api_opener.open.assert_not_called()
        client.access_token = "token"
        for payload in ({}, [], {**TOTALS, "createdRecords": True}, {**TOTALS, "existingRecords": -1}):
            client.api_opener.open = mock.Mock(return_value=Response(payload))
            with self.subTest(payload=payload), self.assertRaises(MODULE.SeedError):
                client.seed()
            client.api_opener.open.assert_called_once()

    def test_main_uses_environment_and_prints_only_summary(self):
        stdout = io.StringIO()
        with mock.patch.dict(MODULE.os.environ, {"DEVELOPMENT_IDENTITY_URL": IDENTITY, "DEVELOPMENT_API_URL": API}), \
                mock.patch.object(MODULE, "DevelopmentDemoClient") as constructor, contextlib.redirect_stdout(stdout):
            constructor.return_value.seed.return_value = TOTALS
            self.assertEqual(0, MODULE.main([]))
        constructor.assert_called_once_with(IDENTITY, API)
        constructor.return_value.wait_until_ready.assert_called_once_with()
        constructor.return_value.authenticate.assert_called_once_with()
        self.assertIn("records created=36, existing=0; storylines created=4, existing=0, skipped=0", stdout.getvalue())
        self.assertNotIn("token", stdout.getvalue())

    def test_main_rejects_nonlocal_urls_before_any_network_operation(self):
        stderr = io.StringIO()
        with mock.patch.object(MODULE.urllib.request, "build_opener") as build, contextlib.redirect_stderr(stderr):
            self.assertEqual(1, MODULE.main(["--identity-url", "https://example.invalid?code=secret"]))
        build.assert_not_called()
        self.assertNotIn("secret", stderr.getvalue())


if __name__ == "__main__":
    unittest.main()
