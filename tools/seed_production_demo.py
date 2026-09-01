#!/usr/bin/env python3
"""Seed a scoped PassingTrace demo account through the public production APIs.

The script deliberately does not connect to PostgreSQL or S3 with privileged
credentials. It authenticates as the demo account, uploads media through the
normal pre-signed upload flow, and creates records through the Events API.
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import hashlib
import html
import http.cookiejar
import json
import os
import re
import secrets
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from typing import Any


CLIENT_ID = "passingtrace-mobile"
REDIRECT_URI = "com.passingtrace.mobile:/oauth2redirect"
TIMEZONE = "Asia/Shanghai"
CONFIRMATION = "seed-passingtrace-production-demo"
USER_AGENT = "PassingTrace-DemoSeeder/1.0 (+https://passingtrace.com)"


class SeedError(RuntimeError):
    pass


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: ANN001
        return None


@dataclass(frozen=True)
class DemoImage:
    file_title: str
    event_title: str


IMAGES = (
    DemoImage("File:Chinese food in Harbin.jpg", "周末和朋友吃东北菜"),
    DemoImage("File:Chinese Food in Street Market of Hong Kong.jpg", "逛夜市尝了几样小吃"),
    DemoImage("File:Kiosk of rental bikes.jpg", "沿江骑行十二公里"),
    DemoImage("File:Design Museum interior (30764512834).jpg", "下午去看设计展"),
    DemoImage("File:DC Coffee Shop.jpg", "在咖啡店读完一本书"),
    DemoImage("File:West lake twilight.jpg", "傍晚在西湖边散步"),
)


RECORD_TEMPLATES: tuple[dict[str, Any], ...] = (
    {"title": "傍晚在西湖边散步", "body": "下班后沿着湖边慢慢走，晚霞很漂亮，风也很舒服。", "category": "scenery", "tags": ["walking", "photography"], "place": ("西湖风景名胜区", "杭州市西湖区龙井路1号", 30.2467, 120.1500)},
    {"title": "周末和朋友吃东北菜", "body": "点了锅包肉、地三鲜和酸菜白肉，大家边吃边聊到很晚。", "category": "food", "tags": ["dining", "friends"]},
    {"title": "完成首页下载入口", "body": "把网页版的 Android 安装包下载链路跑通，并检查了生产域名和版本信息。", "category": "work", "tags": ["coding", "writing"]},
    {"title": "沿江骑行十二公里", "body": "天气凉快，沿江骑了十二公里。前半程稍快，回程放慢速度看夜景。", "category": "exercise", "tags": ["cycling"]},
    {"title": "在咖啡店读完一本书", "body": "下午找了个安静角落，读完剩下的几章，还顺手记了三条读书笔记。", "category": "study", "tags": ["coffee", "reading"]},
    {"title": "给家里做了一顿晚饭", "body": "第一次做番茄炖牛腩，火候比预想中好，家人都说可以再做一次。", "category": "home", "tags": ["cooking", "family"]},
    {"title": "下午去看设计展", "body": "展览里最喜欢关于城市公共空间的部分，拍了几张有意思的装置。", "category": "entertainment", "tags": ["museum", "photography"]},
    {"title": "晨跑五公里", "body": "七点出门，配速不快但全程没有停，跑完精神很好。", "category": "exercise", "tags": ["running"]},
    {"title": "修好了图片上传问题", "body": "排查了预签名地址和 MIME 校验，真机重新上传后流程恢复正常。", "category": "work", "tags": ["coding"]},
    {"title": "和老朋友通了电话", "body": "聊了近况和以前上学时的事情，约好下个月找时间见面。", "category": "social", "tags": ["friends"]},
    {"title": "补充一周采购", "body": "买了牛奶、水果、洗衣液和一些早餐食材，控制在预算以内。", "category": "shopping", "tags": ["daily-goods"]},
    {"title": "整理书桌和文件", "body": "把堆了很久的纸质资料分类收好，桌面终于空出来了。", "category": "home", "tags": ["cleaning"]},
    {"title": "看了一部老电影", "body": "节奏比现在的电影慢很多，但人物和对白很耐看。", "category": "entertainment", "tags": ["movie"]},
    {"title": "学习 Agent Framework", "body": "梳理了 Context Provider、Typed Tools 和会话状态，准备做一个小实验。", "category": "study", "tags": ["coding", "course"]},
    {"title": "午休散步三十分钟", "body": "绕公司附近走了一圈，晒了会儿太阳，下午状态好了不少。", "category": "health", "tags": ["walking"]},
    {"title": "逛夜市尝了几样小吃", "body": "尝了烤串、豆花和一份炒粉，最喜欢摊位现做的豆花。", "category": "food", "tags": ["restaurant", "city-walk"]},
    {"title": "项目阶段复盘", "body": "把身份认证、记录、媒体和 AI 检索四部分重新画了一遍，下一步先完善体验。", "category": "work", "tags": ["meeting", "writing"]},
    {"title": "周末睡了个好觉", "body": "没有设闹钟，自然醒后感觉最近积累的疲劳少了很多。", "category": "health", "tags": ["sleep"]},
    {"title": "陪家人去公园", "body": "在公园坐了会儿，沿湖走了一圈，顺便拍了几张树影。", "category": "social", "tags": ["family", "walking"]},
    {"title": "公交通勤意外顺利", "body": "今天换乘衔接得很好，比平时早到了十五分钟。", "category": "transport", "tags": ["public-transit", "commute"]},
    {"title": "第一次做蘑菇汤", "body": "用了口蘑、洋葱和牛奶，味道比较清淡，下次可以多加一点胡椒。", "category": "food", "tags": ["cooking"]},
    {"title": "健身房练背", "body": "完成高位下拉、坐姿划船和硬拉，动作比上周稳定。", "category": "exercise", "tags": ["fitness"]},
    {"title": "读完数据库索引章节", "body": "重点复习了组合索引、覆盖索引和查询计划，整理了几条实践建议。", "category": "study", "tags": ["reading", "coding"]},
    {"title": "买了一副新耳机", "body": "对比了几款之后选了佩戴更舒服的一款，通勤时听音乐用。", "category": "shopping", "tags": ["digital"]},
    {"title": "雨天在家听音乐", "body": "整理房间时循环听了一张老专辑，安静地过了一个下午。", "category": "entertainment", "tags": ["music", "cleaning"]},
    {"title": "和同事讨论搜索排序", "body": "讨论了全文、向量和结构化过滤的融合方式，决定先用 RRF 合并候选。", "category": "work", "tags": ["meeting", "coding"]},
    {"title": "去社区诊所复查", "body": "医生说恢复情况正常，继续保持规律作息和适量运动。", "category": "health", "tags": ["medical"]},
    {"title": "计划周末短途旅行", "body": "想去附近古镇住一晚，先列了交通、住宿和想看的几个地方。", "category": "travel", "tags": ["attraction"]},
    {"title": "在阳台种下香草", "body": "种了薄荷和罗勒，希望过几周做饭时能用上。", "category": "home", "tags": ["home-goods"]},
    {"title": "晚饭后散步", "body": "没有带耳机，沿着熟悉的小路走了四十分钟。", "category": "exercise", "tags": ["walking"]},
    {"title": "给朋友挑生日礼物", "body": "最后选了一本摄影集和一张手写卡片，希望他会喜欢。", "category": "shopping", "tags": ["gift", "friends"]},
    {"title": "整理本月照片", "body": "按日期整理了手机相册，删掉重复照片，并选出十几张值得留下的。", "category": "home", "tags": ["photography", "cleaning"]},
    {"title": "完成一次长距离步行", "body": "从市中心一路走到江边，全程大约九公里，沿途发现几家小店。", "category": "exercise", "tags": ["walking", "city-walk"]},
    {"title": "尝试手冲咖啡", "body": "调整了水温和研磨度，第二杯比第一杯干净很多。", "category": "food", "tags": ["coffee"]},
    {"title": "写下下个月的重点", "body": "把工作、运动和生活各留一个重点，不再给自己排太多任务。", "category": "work", "tags": ["writing"]},
    {"title": "周日晚上的家庭聚餐", "body": "大家一起吃饭，聊了最近各自遇到的新鲜事。", "category": "social", "tags": ["dining", "family"]},
)


def _b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _plain_metadata(value: str) -> str:
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", "", value))).strip()


def build_events(count: int, now: dt.datetime | None = None) -> list[dict[str, Any]]:
    if count < 1 or count > len(RECORD_TEMPLATES):
        raise ValueError(f"count must be between 1 and {len(RECORD_TEMPLATES)}")
    now = now or dt.datetime.now(dt.timezone(dt.timedelta(hours=8)))
    events: list[dict[str, Any]] = []
    image_titles = {image.event_title for image in IMAGES}
    for index, template in enumerate(RECORD_TEMPLATES[:count]):
        happened = (now - dt.timedelta(days=index, hours=(index * 3) % 11)).replace(microsecond=0)
        raw_content = template["body"]
        item = {
            "title": template["title"],
            "rawContent": raw_content,
            "kind": 0,
            "happenedAt": happened.isoformat(),
            "plannedAt": None,
            "timezone": TIMEZONE,
            "mediaIds": [],
            "classification": {
                "primaryCategoryKey": template["category"],
                "tags": [{"taxonomyKey": tag, "name": None} for tag in template["tags"]],
                "suppressedAiTagKeys": [],
            },
            "locations": [],
            "seedKey": f"production-demo-v1-{index + 1:02d}",
            "needsImage": template["title"] in image_titles,
        }
        if place := template.get("place"):
            item["locations"] = [{
                "name": place[0], "address": place[1], "province": "浙江省", "city": "杭州市",
                "district": "西湖区", "adCode": "330106", "providerPoiId": None,
                "poiType": "风景名胜", "latitude": place[2], "longitude": place[3],
                "accuracyMeters": 20, "coordinateSystem": "GCJ02", "source": 4,
                "capturedAt": happened.isoformat(),
            }]
        events.append(item)
    return events


class PassingTraceClient:
    def __init__(self, identity_url: str, events_url: str):
        self.identity_url = identity_url.rstrip("/")
        self.events_url = events_url.rstrip("/")
        cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(cookies), NoRedirect())
        self.access_token: str | None = None

    def request(self, method: str, url: str, *, json_body: Any = None,
                form: dict[str, str] | None = None, headers: dict[str, str] | None = None,
                expected: tuple[int, ...] = (200,)) -> tuple[int, Any, dict[str, str]]:
        body = None
        request_headers = {"User-Agent": USER_AGENT, "Accept": "application/json"}
        if json_body is not None:
            body = json.dumps(json_body, ensure_ascii=False).encode("utf-8")
            request_headers["Content-Type"] = "application/json; charset=utf-8"
        elif form is not None:
            body = urllib.parse.urlencode(form).encode("ascii")
            request_headers["Content-Type"] = "application/x-www-form-urlencoded"
        if self.access_token:
            request_headers["Authorization"] = f"Bearer {self.access_token}"
        request_headers.update(headers or {})
        request = urllib.request.Request(url, data=body, headers=request_headers, method=method)
        try:
            response = self.opener.open(request, timeout=60)
        except urllib.error.HTTPError as error:
            if error.code in expected:
                response = error
            else:
                detail = error.read().decode("utf-8", "replace")
                raise SeedError(f"{method} {url} returned HTTP {error.code}: {detail[:1000]}") from error
        status = response.getcode()
        payload_bytes = response.read()
        if status not in expected:
            raise SeedError(f"{method} {url} returned unexpected HTTP {status}")
        payload: Any = None
        if payload_bytes:
            content_type = response.headers.get("Content-Type", "")
            payload = json.loads(payload_bytes) if "json" in content_type else payload_bytes
        return status, payload, dict(response.headers.items())

    def authenticate(self, username: str, password: str, bootstrap_code: str) -> None:
        verifier = _b64url(secrets.token_bytes(48))
        challenge = _b64url(hashlib.sha256(verifier.encode("ascii")).digest())
        state = _b64url(secrets.token_bytes(24))
        auth_common = {
            "username": username, "clientId": CLIENT_ID, "redirectUri": REDIRECT_URI,
            "codeChallenge": challenge, "state": state, "nonce": _b64url(secrets.token_bytes(24)),
            "deviceName": "Production Demo Seeder",
        }
        try:
            _, launch, _ = self.request("POST", f"{self.identity_url}/api/mobile/logins",
                                      json_body={**auth_common, "password": password}, expected=(200, 201))
            print(f"Reusing demo account: {username}")
        except SeedError as login_error:
            if "HTTP 401" not in str(login_error):
                raise
            _, intent, _ = self.request(
                "POST", f"{self.identity_url}/api/mobile/registration-intents",
                json_body={key: auth_common[key] for key in (
                    "username", "clientId", "redirectUri", "codeChallenge", "state", "nonce")},
                expected=(200, 201))
            _, launch, _ = self.request(
                "POST", f"{self.identity_url}/api/mobile/registrations",
                json_body={"intentId": intent["intentId"], "username": username, "password": password,
                           "bootstrapCode": bootstrap_code, "deviceName": "Production Demo Seeder"},
                expected=(201,))
            print(f"Created demo account: {username}")

        _, _, redirect_headers = self.request("GET", launch["authorizeUrl"], expected=(302,))
        callback = redirect_headers.get("Location") or redirect_headers.get("location")
        if not callback:
            raise SeedError("Identity authorize response did not include a callback Location")
        query = urllib.parse.parse_qs(urllib.parse.urlparse(callback).query)
        if query.get("state", [None])[0] != state:
            raise SeedError("OIDC state mismatch")
        code = query.get("code", [None])[0]
        if not code:
            raise SeedError(f"OIDC callback did not include a code: {callback}")
        _, token, _ = self.request("POST", f"{self.identity_url}/connect/token", form={
            "grant_type": "authorization_code", "client_id": CLIENT_ID, "code": code,
            "redirect_uri": REDIRECT_URI, "code_verifier": verifier,
        })
        self.access_token = token["access_token"]

    def existing_titles(self) -> set[str]:
        _, payload, _ = self.request("GET", f"{self.events_url}/api/v1/events?limit=100")
        return {item["title"] for item in payload["items"] if item.get("title")}

    def upload_image(self, image: DemoImage) -> tuple[str, str]:
        api_url = "https://commons.wikimedia.org/w/api.php?" + urllib.parse.urlencode({
            "action": "query", "format": "json", "prop": "imageinfo", "iiprop": "url|mime|extmetadata",
            "iiurlwidth": "1600", "titles": image.file_title,
        })
        _, metadata, _ = self.request("GET", api_url)
        page = next(iter(metadata["query"]["pages"].values()))
        info = page["imageinfo"][0]
        download_url = info.get("thumburl") or info["url"]
        content_type = info["mime"].lower()
        if content_type not in ("image/jpeg", "image/png", "image/webp"):
            raise SeedError(f"Unsupported image MIME from Commons: {content_type}")
        _, content, _ = self.request("GET", download_url)
        if not isinstance(content, bytes) or not content:
            raise SeedError(f"Wikimedia returned no bytes for {image.file_title}")
        extension = {"image/jpeg": ".jpg", "image/png": ".png", "image/webp": ".webp"}[content_type]
        sha256 = hashlib.sha256(content).hexdigest()
        source_name = image.file_title[5:]
        source_stem = source_name.rsplit(".", 1)[0]
        _, upload, _ = self.request("POST", f"{self.events_url}/api/v1/media/uploads", json_body={
            "fileName": re.sub(r"[^A-Za-z0-9._-]+", "-", source_stem).strip("-") + extension,
            "contentType": content_type, "size": len(content), "sha256": sha256,
        }, expected=(201,))
        put = urllib.request.Request(upload["uploadUrl"], data=content,
                                     headers={"Content-Type": content_type}, method="PUT")
        try:
            with urllib.request.urlopen(put, timeout=120) as response:
                if response.status not in (200, 201, 204):
                    raise SeedError(f"S3 upload returned HTTP {response.status}")
        except urllib.error.HTTPError as error:
            raise SeedError(f"S3 upload returned HTTP {error.code}: {error.read()[:500]!r}") from error
        self.request("POST", f"{self.events_url}/api/v1/media/{upload['id']}/confirm",
                     json_body={"parts": None})
        ext = info.get("extmetadata", {})
        license_name = ext.get("LicenseShortName", {}).get("value", "Wikimedia Commons")
        artist = _plain_metadata(ext.get("Artist", {}).get("value", "Wikimedia contributor"))
        source_page = "https://commons.wikimedia.org/wiki/" + urllib.parse.quote(image.file_title.replace(" ", "_"), safe=":()_-.")
        return upload["id"], f"图片来源：Wikimedia Commons；作者：{artist}；许可：{license_name}\n{source_page}"

    def create_event(self, event: dict[str, Any]) -> None:
        payload = {key: value for key, value in event.items() if key not in ("seedKey", "needsImage")}
        self.request("POST", f"{self.events_url}/api/v1/events", json_body=payload,
                     headers={"Idempotency-Key": event["seedKey"]}, expected=(201,))


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Seed a production demo account using public APIs")
    parser.add_argument("--identity-url", default=os.getenv("PASSINGTRACE_IDENTITY_URL", "https://auth.passingtrace.com"))
    parser.add_argument("--events-url", default=os.getenv("PASSINGTRACE_EVENTS_URL", "https://passingtrace.com"))
    parser.add_argument("--username", default=os.getenv("DEMO_ACCOUNT_USERNAME", "passingtrace-demo"))
    parser.add_argument("--password", default=os.getenv("DEMO_ACCOUNT_PASSWORD"))
    parser.add_argument("--bootstrap-code", default=os.getenv("REGISTRATION_BOOTSTRAP_CODE"))
    parser.add_argument("--count", type=int, default=int(os.getenv("DEMO_RECORD_COUNT", "36")))
    parser.add_argument("--confirm", default=os.getenv("DEMO_SEED_CONFIRM"))
    parser.add_argument("--dry-run", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    events = build_events(args.count)
    if args.dry_run:
        print(json.dumps(events, ensure_ascii=False, indent=2))
        return 0
    if args.confirm != CONFIRMATION:
        raise SeedError(f"Refusing production write: pass --confirm {CONFIRMATION}")
    if not args.password or len(args.password) < 8:
        raise SeedError("DEMO_ACCOUNT_PASSWORD must contain at least 8 characters")
    if not args.bootstrap_code:
        raise SeedError("REGISTRATION_BOOTSTRAP_CODE is required when the demo account does not exist")

    client = PassingTraceClient(args.identity_url, args.events_url)
    client.authenticate(args.username, args.password, args.bootstrap_code)
    existing = client.existing_titles()
    images = {item.event_title: item for item in IMAGES}
    created = skipped = 0
    for event in events:
        if event["title"] in existing:
            print(f"SKIP {event['title']}")
            skipped += 1
            continue
        if event["needsImage"]:
            media_id, attribution = client.upload_image(images[event["title"]])
            event["mediaIds"] = [media_id]
            event["rawContent"] += f"\n\n{attribution}"
        client.create_event(event)
        print(f"CREATE {event['title']}")
        created += 1
    print(f"Done: created={created}, skipped={skipped}, requested={len(events)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (SeedError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
