#!/usr/bin/env python3
"""Seed private subject examples through authenticated local application APIs.

Uses the development Web login, never a database connection or stored password.
Stable keys keep retries safe and preserve edited or soft-deleted fixtures.
"""
from __future__ import annotations

import json
import sys
import uuid
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from seed_development_demo import DevelopmentDemoClient

NAMESPACE = uuid.UUID("17c5bb93-84ee-4182-aef1-bf94c7e058c2")
PREFIX = "development-subject-demo-v1"
ZONE = "Asia/Shanghai"


def field_id(subject: str, key: str) -> str:
    return str(uuid.uuid5(NAMESPACE, f"{subject}:{key}"))


def catalog() -> list[dict]:
    # All dates and values are fixed so replay request hashes do not change.
    return [
        {"key": "friend", "kind": 0, "name": "小林 · 演示朋友", "since": "2023-04-01",
         "relations": [("self", "朋友", False, None)],
         "fields": [("birthday", "生日", "date", None, "1997-06-15"),
                    ("height", "身高", "number", "cm", 172),
                    ("phone", "电话号码", "phone", None, "00000000000")]},
        {"key": "mother", "kind": 0, "name": "妈妈 · 演示亲人", "since": "1990-01-01",
         "relations": [("self", "母子／母女", False, None)],
         "fields": [("birthday", "生日", "date", None, "1968-03-12"),
                    ("height", "身高", "number", "cm", 160),
                    ("phone", "电话号码", "phone", None, "00000000001")]},
        {"key": "cat", "kind": 1, "name": "奶糖 · 演示母猫", "since": "2025-04-12",
         "relations": [("friend", "饲养者", True, None)],
         "fields": [("birthday", "生日", "date", None, "2024-11-20"),
                    ("arrival", "到家日期", "date", None, "2025-04-12"),
                    ("breed", "品种", "text", None, "中华田园猫"),
                    ("weight", "体重", "number", "kg", 2.8),
                    ("vaccinated", "本次疫苗已完成", "boolean", None, False)]},
        {"key": "kitten", "kind": 1, "name": "芝麻 · 演示小猫", "since": "2026-09-20",
         "relations": [("cat", "母亲", True, None), ("self", "饲养者", True, None)],
         "fields": [("birthday", "生日", "date", None, "2026-09-20"),
                    ("breed", "品种", "text", None, "中华田园猫"),
                    ("weight", "体重", "number", "kg", 0.12)]},
        {"key": "car", "kind": 2, "itemType": "vehicle", "name": "小白 · 演示汽车", "since": "2025-05-18",
         "relations": [("self", "归属", True, None), ("mother", "共同购买", False, None)],
         "fields": [("model", "品牌型号", "text", None, "演示家用轿车"),
                    ("plate", "车牌", "text", None, "演示车牌"),
                    ("purchase", "购入日期", "date", None, "2025-05-18"),
                    ("price", "购入价格", "number", "元", 128000),
                    ("mileage", "里程", "number", "km", 0)]},
        {"key": "bicycle", "kind": 2, "itemType": "bicycle", "name": "小蓝 · 演示自行车", "since": "2024-03-09",
         "relations": [("self", "原主人", True, "2026-09-30")],
         "fields": [("model", "品牌型号", "text", None, "演示城市自行车"),
                    ("frame", "车架号", "text", None, "DEMO-BIKE-001"),
                    ("price", "购入价格", "number", "元", 2600)]},
        {"key": "home", "kind": 2, "itemType": "property", "name": "晴川小屋 · 演示房屋", "since": "2023-08-01",
         "relations": [("self", "居住", False, None), ("mother", "共同居住", False, "2025-12-31")],
         "fields": [("address", "地址", "text", None, "虚构示例地址，不对应真实房屋"),
                    ("area", "面积", "number", "m²", 88),
                    ("purchase", "购入日期", "date", None, "2023-08-01")]},
    ]


def seed_subjects(client: DevelopmentDemoClient) -> dict[str, int]:
    from seed_development_demo import HttpStatusError, SeedError

    if not client.access_token:
        raise SeedError("Authenticate with local development auto-login before seeding subjects.")

    def request(path: str, body: dict | None = None, *, key: str | None = None,
                version: int | None = None):
        headers = {"Authorization": "Bearer " + client.access_token, "Content-Type": "application/json"}
        if key is not None:
            headers["Idempotency-Key"] = PREFIX + "-" + key
        if version is not None:
            headers["If-Match"] = f'"{version}"'
        payload, _ = client._request(client.api_opener, "GET" if body is None else "POST",
            client.api_url + path, "Subject demo initialization",
            data=None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8"),
            headers=headers, expected=(200, 201))
        try:
            return json.loads(payload)
        except (ValueError, UnicodeError):
            raise SeedError("Subject demo initialization returned invalid JSON.") from None

    initial = request("/api/v1/subjects")
    self = next(s for s in initial if s["isSelf"])
    initial_ids = {s["id"] for s in initial}
    subjects = {"self": self}
    active: set[str] = {"self"}
    result = {"createdSubjects": 0, "existingSubjects": 0, "skippedSubjects": 0, "entryFixtures": 0}
    for spec in catalog():
        key = spec["key"]
        if any(target not in active for target, *_ in spec["relations"]):
            result["skippedSubjects"] += 1
            continue
        fields = [{"id": field_id(key, fkey), "key": fkey, "name": name, "type": kind, "unit": unit}
                  for fkey, name, kind, unit, _ in spec["fields"]]
        subject = request("/api/v1/subjects", {
            "kind": spec["kind"], "name": spec["name"], "itemType": spec.get("itemType"),
            "description": "本地演示档案，所有姓名、联系方式及资产信息均为虚构，可自由编辑。",
            "startedAt": spec["since"] + "T09:00:00+08:00", "timezone": ZONE,
            "fields": fields,
            "values": {field_id(key, fkey): value for fkey, _, _, _, value in spec["fields"]},
            "relations": [{"toSubjectId": subjects[target]["id"], "label": label, "directed": directed,
                           "startedAt": spec["since"] + "T09:00:00+08:00",
                           "endedAt": ended + "T18:00:00+08:00" if ended else None}
                          for target, label, directed, ended in spec["relations"]],
        }, key="subject-" + key)
        # An idempotent replay can return a soft-deleted fixture. Do not restore it.
        try:
            request("/api/v1/subjects/" + subject["id"])
        except HttpStatusError as error:
            if error.status != 404:
                raise
            result["skippedSubjects"] += 1
            continue
        subjects[key] = subject
        active.add(key)
        result["existingSubjects" if subject["id"] in initial_ids else "createdSubjects"] += 1

    entries = [
        ("cat", "first-weight", "第一次称重", "到家一个月后体重为 3.1 kg。", "2025-05-12", 0, [], {"weight": 3.1}),
        ("cat", "recent-weight", "产后体重复查", "实际称重 4.6 kg，更新母猫档案当前体重。", "2026-09-26", 0, [], {"weight": 4.6}),
        ("cat", "vaccine", "下个月打疫苗", "专属计划；完成时确认实际日期及疫苗字段，不提前修改当前值。", "2026-11-05", 1, ["friend"], {"vaccinated": True}),
        ("friend", "kitten-birth", "带奶糖去宠物医院接生芝麻", "朋友的专属记录，只进入朋友和明确标记的两只猫的时间轴，不进入我的总记录。", "2026-09-20", 0, ["cat", "kitten"], {}),
        ("kitten", "growth", "芝麻开始稳定增重", "补录小猫的体重变化。", "2026-09-28", 0, [], {"weight": 0.21}),
        ("car", "mileage", "记录当前里程", "汽车专属记录，当前里程 15,360 km。", "2026-09-14", 0, [], {"mileage": 15360}),
        ("car", "maintenance", "月底汽车保养", "汽车自己的待执行计划，不进入原总记录。里程为预期值。", "2026-10-31", 1, [], {"mileage": 16000}),
        ("bicycle", "repair", "更换自行车链条", "保存维修历史，后续出售也保留该记录。", "2026-08-16", 0, [], {}),
        ("bicycle", "paperwork", "补齐自行车转让凭证", "档案结束后仍保留后续安排，默认不取消此计划。", "2026-10-12", 1, [], {}),
        ("home", "inspection", "检查小屋水电", "房屋专属待执行计划。", "2026-10-15", 1, [], {}),
        ("mother", "visit", "周末一起吃饭", "这条亲人专属计划明确标记自己，因此也出现在自己的时间轴。", "2026-10-04", 1, ["self"], {}),
    ]
    for owner, key, title, content, date, kind, marked, changes in entries:
        if owner not in active or any(target not in active for target in marked):
            continue
        request(f'/api/v1/subjects/{subjects[owner]["id"]}/entries', {
            "kind": kind, "title": title, "content": content, "timezone": ZONE,
            "plannedAt" if kind else "happenedAt": date + "T10:00:00+08:00",
            "markedSubjectIds": [subjects[target]["id"] for target in marked],
            "fieldChanges": {field_id(owner, field): value for field, value in changes.items()},
        }, key="entry-" + key)
        result["entryFixtures"] += 1

    if {"car", "mother"} <= active:
        request("/api/v1/events", {
            "kind": 0, "title": "陪妈妈一起送小白做检查 · 人物演示",
            "rawContent": "从原记录模块创建，明确标记演示汽车和妈妈；同一份内容同时出现在原总记录及这两份档案。",
            "happenedAt": "2026-09-18T14:00:00+08:00", "timezone": ZONE,
            "subjectIds": [subjects["car"]["id"], subjects["mother"]["id"]],
        }, key="event-car-check")

    # Lifecycle operations have versions rather than create keys. Only finish an
    # untouched initial fixture transition, never redo a user's lifecycle edit.
    if "bicycle" in active:
        bicycle = request("/api/v1/subjects/" + subjects["bicycle"]["id"])
        # Creation starts at version 1, then the two keyed bicycle entries each
        # advance it once. Version 3 can finish an interrupted initial seed;
        # user edits or lifecycle changes advance it further and are preserved.
        if bicycle["state"] == 0 and bicycle["version"] == 3:
            request(f'/api/v1/subjects/{bicycle["id"]}/lifecycle', {
                "operation": "end", "effectiveAt": "2026-09-30T18:00:00+08:00", "reason": "sold",
                "note": "本地演示：自行车已出售，资料和历史记录继续保留。", "cancelPlans": [],
            }, version=bicycle["version"])
    return result


def main(argv: list[str] | None = None) -> int:
    from seed_development_demo import DevelopmentDemoClient, SeedError, parse_args
    try:
        args = parse_args(argv)
        client = DevelopmentDemoClient(args.identity_url, args.api_url)
        print("Waiting for local development services...", flush=True)
        client.wait_until_ready()
        client.authenticate()
        print("Subject demo ready: " + json.dumps(seed_subjects(client), ensure_ascii=False), flush=True)
        return 0
    except SeedError as error:
        print(f"ERROR: {error}", file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
