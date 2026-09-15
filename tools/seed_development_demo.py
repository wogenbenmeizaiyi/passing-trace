#!/usr/bin/env python3
"""Initialize local demo fixtures using the configured development auto-login.

Run automatically by the Development AppHost, or manually with Python 3.
Only loopback HTTP(S) origins are accepted. No password, privileged database
connection, production account, or third-party service is used by this client.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import http.client
import http.cookiejar
import ipaddress
import json
import os
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from typing import Any


CLIENT_ID = "passingtrace-web"
REDIRECT_URI = "http://localhost:5173/auth/callback"
SCOPE = "openid profile passingtrace.api"
SEED_PATH = "/api/v1/development/demo-data"
STARTUP_TIMEOUT_SECONDS = 120
REQUEST_TIMEOUT_SECONDS = 15
USER_AGENT = "PassingTrace-DevelopmentDemoSeeder/1.0"
SUMMARY_FIELDS = (
    "createdRecords", "existingRecords", "createdStorylines",
    "existingStorylines", "skippedStorylines",
)


class SeedError(RuntimeError):
    """An actionable error safe to print without URLs or response bodies."""


class ConnectionUnavailable(SeedError):
    pass


class HttpStatusError(SeedError):
    def __init__(self, operation: str, status: int):
        self.status = status
        super().__init__(f"{operation} returned HTTP {status}.")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    """Intercept the OIDC callback; never forward cookies or tokens on redirects."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def loopback_origin(value: str) -> str:
    """Validate a literal loopback origin before opening any connection."""
    message = "Service URLs must be loopback HTTP(S) origins without credentials, paths, queries, or fragments."
    try:
        if not value or any(character.isspace() or ord(character) < 32 for character in value):
            raise ValueError
        if any(character in value for character in ("\\", "?", "#")):
            raise ValueError
        parsed = urllib.parse.urlsplit(value)
        if (parsed.scheme not in ("http", "https") or not parsed.hostname
                or parsed.username is not None or parsed.password is not None
                or parsed.path not in ("", "/") or parsed.port == 0):
            raise ValueError
        if parsed.hostname.lower() != "localhost" and not ipaddress.ip_address(parsed.hostname).is_loopback:
            raise ValueError
    except ValueError:
        raise SeedError(message) from None
    return urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, "", "", ""))


def _b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def callback_code(location: str, expected_state: str) -> str:
    """Accept one code and matching state from the exact registered callback."""
    try:
        parsed = urllib.parse.urlsplit(location)
        callback = urllib.parse.urlsplit(REDIRECT_URI)
        if (parsed.scheme, parsed.netloc, parsed.path) != (callback.scheme, callback.netloc, callback.path):
            raise ValueError
        if parsed.fragment or "#" in location or "\\" in location or any(character.isspace() for character in location):
            raise ValueError
        query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True, max_num_fields=20)
    except ValueError:
        raise SeedError("Identity returned an unexpected OIDC callback.") from None
    if query.get("state") != [expected_state]:
        raise SeedError("OIDC state validation failed.")
    if "error" in query:
        raise SeedError("Development auto-login was not authorized; check DevelopmentAutoLogin and the local account status.")
    codes = query.get("code", [])
    if len(codes) != 1 or not codes[0] or any(ord(character) < 32 for character in codes[0]):
        raise SeedError("OIDC callback did not contain exactly one authorization code.")
    return codes[0]


def _json_object(payload: bytes, operation: str) -> dict[str, Any]:
    try:
        result = json.loads(payload)
    except (ValueError, UnicodeError):
        raise SeedError(f"{operation} returned invalid JSON.") from None
    if not isinstance(result, dict):
        raise SeedError(f"{operation} returned an unexpected response.")
    return result


class DevelopmentDemoClient:
    def __init__(self, identity_url: str, api_url: str):
        self.identity_url = loopback_origin(identity_url)
        self.api_url = loopback_origin(api_url)
        # Disable environment proxies even for loopback URLs. Cookie jars do not
        # isolate ports, so the API deliberately has its own cookie-free opener.
        self.identity_opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}),
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
            NoRedirect(),
        )
        self.api_opener = urllib.request.build_opener(
            urllib.request.ProxyHandler({}), NoRedirect(),
        )
        self.access_token: str | None = None

    @staticmethod
    def _request(opener, method: str, url: str, operation: str, *,
                 data: bytes | None = None, headers: dict[str, str] | None = None,
                 expected: tuple[int, ...] = (200,),
                 timeout: float = REQUEST_TIMEOUT_SECONDS) -> tuple[bytes, Any]:
        request = urllib.request.Request(url, data=data, method=method, headers={
            "Accept": "application/json", "User-Agent": USER_AGENT, **(headers or {}),
        })
        try:
            try:
                response = opener.open(request, timeout=timeout)
            except urllib.error.HTTPError as error:
                if error.code not in expected:
                    error.close()
                    raise HttpStatusError(operation, error.code) from None
                response = error
            with response:
                if response.getcode() not in expected:
                    raise HttpStatusError(operation, response.getcode())
                # Neither the callback nor the readiness probe needs a body.
                payload = response.read(1_048_577) if response.getcode() == 200 else b""
                if len(payload) > 1_048_576:
                    raise SeedError(f"{operation} returned an oversized response.")
                return payload, response.headers
        except (urllib.error.URLError, OSError, http.client.HTTPException):
            raise ConnectionUnavailable(f"{operation} could not reach the local service.") from None

    def _wait_for(self, opener, url: str, operation: str,
                  expected: tuple[int, ...]) -> bytes:
        deadline = time.monotonic() + STARTUP_TIMEOUT_SECONDS
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise SeedError(f"Timed out waiting for {operation}; start the local Development AppHost and try again.")
            try:
                payload, _ = self._request(
                    opener, "GET", url, operation, expected=expected,
                    timeout=min(REQUEST_TIMEOUT_SECONDS, remaining),
                )
                return payload
            except ConnectionUnavailable:
                pass
            except HttpStatusError as error:
                if error.status not in (408, 429, 500, 502, 503, 504):
                    raise
            time.sleep(min(1, max(0, deadline - time.monotonic())))

    def wait_until_ready(self) -> None:
        discovery = _json_object(self._wait_for(
            self.identity_opener, f"{self.identity_url}/.well-known/openid-configuration",
            "Identity discovery", (200,),
        ), "Identity discovery")
        if (discovery.get("authorization_endpoint") != f"{self.identity_url}/connect/authorize"
                or discovery.get("token_endpoint") != f"{self.identity_url}/connect/token"):
            raise SeedError("Identity discovery does not advertise the expected local OIDC endpoints.")
        # The development endpoint supports POST only. This read-only probe waits
        # for routing to start without creating fixtures or requiring a token.
        self._wait_for(self.api_opener, f"{self.api_url}{SEED_PATH}", "Events API", (401, 403, 405))

    def authenticate(self) -> None:
        verifier = _b64url(secrets.token_bytes(48))
        state = _b64url(secrets.token_bytes(24))
        authorize_url = f"{self.identity_url}/connect/authorize?" + urllib.parse.urlencode({
            "response_type": "code", "client_id": CLIENT_ID,
            "redirect_uri": REDIRECT_URI, "scope": SCOPE, "state": state,
            "nonce": _b64url(secrets.token_bytes(24)),
            "code_challenge": _b64url(hashlib.sha256(verifier.encode("ascii")).digest()),
            "code_challenge_method": "S256",
        })
        _, headers = self._request(
            self.identity_opener, "GET", authorize_url, "Development authorization", expected=(302, 303),
        )
        location = headers.get("Location", "")
        try:
            resolved = urllib.parse.urljoin(self.identity_url, location)
            parsed = urllib.parse.urlsplit(resolved)
        except ValueError:
            raise SeedError("Identity returned an unexpected OIDC callback.") from None
        if parsed.netloc == urllib.parse.urlsplit(self.identity_url).netloc and parsed.path.startswith("/account/"):
            raise SeedError("Development auto-login is unavailable; enable DevelopmentAutoLogin in the local Development Identity service and check the account status.")
        code = callback_code(location, state)
        payload, _ = self._request(
            self.identity_opener, "POST", f"{self.identity_url}/connect/token", "OIDC token exchange",
            data=urllib.parse.urlencode({
                "grant_type": "authorization_code", "client_id": CLIENT_ID,
                "redirect_uri": REDIRECT_URI, "code": code, "code_verifier": verifier,
            }).encode("ascii"),
            headers={"Content-Type": "application/x-www-form-urlencoded"},
        )
        token = _json_object(payload, "OIDC token exchange")
        access_token = token.get("access_token")
        if (not isinstance(access_token, str) or not access_token or not access_token.isascii()
                or any(character.isspace() or ord(character) < 32 for character in access_token)
                or str(token.get("token_type", "")).lower() != "bearer"):
            raise SeedError("OIDC token exchange did not return a valid bearer access token.")
        self.access_token = access_token

    def seed(self) -> dict[str, int]:
        if self.access_token is None:
            raise SeedError("Authenticate with local development auto-login before seeding.")
        # Never retry this POST automatically: a lost response may already have
        # committed. The server's stable fixture identifiers make manual reruns safe.
        payload, _ = self._request(
            self.api_opener, "POST", f"{self.api_url}{SEED_PATH}", "Development demo initialization",
            data=b"{}", headers={
                "Content-Type": "application/json", "Authorization": f"Bearer {self.access_token}",
            },
        )
        result = _json_object(payload, "Development demo initialization")
        if any(type(result.get(field)) is not int or result[field] < 0 for field in SUMMARY_FIELDS):
            raise SeedError("Development demo initialization returned invalid totals; rerunning is safe.")
        return {field: result[field] for field in SUMMARY_FIELDS}


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Initialize local demo fixtures using development auto-login.")
    parser.add_argument("--identity-url", default=os.getenv("DEVELOPMENT_IDENTITY_URL", "http://localhost:56229"))
    parser.add_argument("--api-url", default=os.getenv("DEVELOPMENT_API_URL", "http://localhost:54934"))
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        client = DevelopmentDemoClient(args.identity_url, args.api_url)
        print("Waiting for local development services...", flush=True)
        client.wait_until_ready()
        client.authenticate()
        result = client.seed()
        print(
            f"Development demo ready: records created={result['createdRecords']}, existing={result['existingRecords']}; "
            f"storylines created={result['createdStorylines']}, existing={result['existingStorylines']}, "
            f"skipped={result['skippedStorylines']}.", flush=True,
        )
        return 0
    except SeedError as error:
        print(f"ERROR: {error}", file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
