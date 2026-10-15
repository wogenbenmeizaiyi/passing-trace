#!/usr/bin/env python3
"""Opt-in live-model smoke check against loopback Development services only.

Consumes the configured model's quota. Creates and then soft-deletes only its own
temporary AI conversation. Never prints tokens, private evidence, or model text.
"""
import argparse
import json
import os
import urllib.parse
import uuid
from seed_development_demo import DevelopmentDemoClient, SeedError


def parse_sse(payload):
    for frame in payload.decode("utf-8").replace("\r\n", "\n").split("\n\n"):
        event = None
        data = []
        for line in frame.splitlines():
            if line.startswith("event:"):
                event = line[6:].strip()
            elif line.startswith("data:"):
                data.append(line[5:].strip())
        if event and data:
            yield event, json.loads("\n".join(data))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live", action="store_true", help="Explicitly allow a few configured-model requests.")
    parser.add_argument("--identity-url", default=os.getenv("DEVELOPMENT_IDENTITY_URL", "http://localhost:56229"))
    parser.add_argument("--api-url", default=os.getenv("DEVELOPMENT_API_URL", "http://localhost:54934"))
    args = parser.parse_args()
    if not args.live:
        parser.error("--live is required: this check uses the configured model and its quota.")
    client = DevelopmentDemoClient(args.identity_url, args.api_url)
    client.wait_until_ready()
    client.authenticate()

    def request(method, path, body=None, timeout=30):
        data, _ = client._request(client.api_opener, method, client.api_url + path,
            "Development AI scenario check", data=None if body is None else json.dumps(body).encode(),
            headers={"Authorization": "Bearer " + client.access_token, "Content-Type": "application/json"},
            expected=(200, 201, 204), timeout=timeout)
        return data

    # The shared OIDC helper intentionally reads bodies only for HTTP 200; CreatedAtAction
    # supplies the new identifier in Location, so do not try to parse an empty HTTP 201 body.
    _, created_headers = client._request(client.api_opener, "POST", client.api_url + "/api/v1/ai/conversations",
        "Create temporary AI scenario conversation", data=json.dumps({"title": "AI 场景回归（临时）"}).encode(),
        headers={"Authorization": "Bearer " + client.access_token, "Content-Type": "application/json"}, expected=(201,))
    location = urllib.parse.urlsplit(urllib.parse.urljoin(client.api_url, created_headers.get("Location", "")))
    if (location.netloc != urllib.parse.urlsplit(client.api_url).netloc
            or not location.path.startswith("/api/v1/ai/conversations/") or location.query or location.fragment):
        raise SeedError("Unexpected temporary conversation location.")
    try:
        conversation = str(uuid.UUID(location.path.rsplit("/", 1)[-1]))
    except ValueError:
        raise SeedError("Invalid temporary conversation identifier.") from None
    base = "/api/v1/ai/conversations/" + conversation
    try:
        cases = [
            ("casual-chat", "今天有点累，想随便聊聊。"),
            ("user-provided-facts", "我今天已经跑完步了，明天想去公园。"),
            ("conversation-summary", "把刚才聊的整理成一份简短总结。"),
            ("planning-draft", "再帮我整理成计划和已完成事项的草稿。"),
            ("nearby-clarification", "换个话题，我附近有什么好吃的？"),
        ]
        for name, question in cases:
            frames = list(parse_sse(request("POST", base + "/messages",
                {"content": question, "timezone": "Asia/Shanghai"}, timeout=90)))
            errors = [data for event, data in frames if event == "error"]
            if errors:
                # Codes are server-controlled; response text may contain private data and is never logged.
                code = str(errors[-1].get("code", "unknown"))
                if not code.replace("_", "").isalnum():
                    code = "unknown"
                raise SeedError(f"{name}: server error ({code}); stopped without retry.")
            if not any(event == "done" for event, _ in frames):
                raise SeedError(f"{name}: incomplete stream; stopped without retry.")
            answer = ""
            for event, data in frames:
                if event == "delta":
                    answer = data.get("text", "") if data.get("replacement") else answer + data.get("text", "")
                if event == "action":
                    raise SeedError(f"{name}: unexpected navigation action.")
                if event == "evidence":
                    fields = ("records", "memories", "places", "storylines", "friends", "sharedContents", "amapPlaces", "amapResults", "actions", "aggregate", "friendActivities")
                    if any(data.get(field) for field in fields):
                        raise SeedError(f"{name}: unexpected data lookup evidence.")
            if not answer.strip() or "我无法从你当前可检索的记录或记忆" in answer:
                raise SeedError(f"{name}: normal answer was empty or replaced by evidence fallback.")
            if name == "planning-draft" and not any(word in answer for word in ("草稿", "未保存", "没有保存")):
                raise SeedError("planning-draft: draft status was not stated.")
            print(f"{name}: completed; no record evidence or navigation action", flush=True)
    finally:
        request("DELETE", base)
        print("Temporary smoke-test conversation hidden; existing conversations untouched.", flush=True)


if __name__ == "__main__":
    try:
        main()
    except SeedError as error:
        raise SystemExit(str(error)) from None
