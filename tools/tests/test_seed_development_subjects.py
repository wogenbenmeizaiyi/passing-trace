import copy
import json
import pathlib
import sys
import unittest
import uuid

sys.path.insert(0, str(pathlib.Path(__file__).parents[1]))
from seed_development_demo import HttpStatusError, SeedError
from seed_development_subjects import PREFIX, catalog, field_id, seed_subjects


class LocalApi:
    """Stateful application API stub: keyed writes replay current saved results."""
    api_url = "http://localhost:54934"
    api_opener = object()
    access_token = "private-fixture-token"

    def __init__(self):
        self.self_id = str(uuid.uuid4())
        self.subjects = {self.self_id: {"id": self.self_id, "isSelf": True}}
        self.keys = {}
        self.writes = []
        self.deleted = set()
        self.lifecycle_calls = 0

    def _request(self, opener, method, url, operation, *, data=None, headers=None, expected=(200,)):
        assert opener is self.api_opener
        assert headers["Authorization"] == "Bearer " + self.access_token
        assert url.startswith(self.api_url + "/api/v1/")
        path = url.removeprefix(self.api_url)
        if method == "GET":
            if path == "/api/v1/subjects":
                response = [value for key, value in self.subjects.items() if key not in self.deleted]
            else:
                key = path.rsplit("/", 1)[-1]
                if key in self.deleted:
                    raise HttpStatusError(operation, 404)
                response = self.subjects[key]
        else:
            body = json.loads(data)
            key = headers.get("Idempotency-Key")
            if key and key in self.keys:
                old_body, target_id = self.keys[key]
                assert body == old_body, "Replay changed the original fixture request."
                response = self.subjects[target_id] if path == "/api/v1/subjects" else {"id": target_id}
            else:
                self.writes.append((path, copy.deepcopy(body), dict(headers)))
                if path == "/api/v1/subjects":
                    target_id = str(uuid.uuid4())
                    response = {**body, "id": target_id, "isSelf": False, "version": 1, "state": 0}
                    self.subjects[target_id] = response
                elif path.endswith("/lifecycle"):
                    self.lifecycle_calls += 1
                    target_id = path.split("/")[-2]
                    response = self.subjects[target_id]
                    response["state"] = 1
                    response["version"] += 1
                else:
                    target_id = str(uuid.uuid4())
                    response = {"id": target_id}
                    if path.endswith("/entries"):
                        self.subjects[path.split("/")[-2]]["version"] += 1
                if key:
                    self.keys[key] = (copy.deepcopy(body), target_id)
        return json.dumps(response).encode(), {}


class SubjectDemoSeedTests(unittest.TestCase):
    def test_seeds_connected_private_subjects_and_two_separate_content_sources(self):
        api = LocalApi()
        result = seed_subjects(api)
        self.assertEqual({"createdSubjects": 7, "existingSubjects": 0, "skippedSubjects": 0, "entryFixtures": 11}, result)
        self.assertEqual(8, len(api.subjects))
        self.assertEqual(1, api.lifecycle_calls)
        subject_writes = [body for path, body, _ in api.writes if path == "/api/v1/subjects"]
        self.assertEqual({0, 1, 2}, {body["kind"] for body in subject_writes})
        self.assertTrue(all(body["relations"] for body in subject_writes))
        entries = [body for path, body, _ in api.writes if path.endswith("/entries")]
        birth = next(body for body in entries if "接生" in body["title"])
        self.assertEqual(2, len(birth["markedSubjectIds"]))
        self.assertNotIn(api.self_id, birth["markedSubjectIds"])
        plan = next(body for body in entries if body["title"] == "月底汽车保养")
        self.assertEqual(1, plan["kind"])
        self.assertEqual([], plan["markedSubjectIds"])
        self.assertEqual({field_id("car", "mileage"): 16000}, plan["fieldChanges"])
        original = [body for path, body, _ in api.writes if path == "/api/v1/events"]
        self.assertEqual(1, len(original))
        self.assertEqual(2, len(original[0]["subjectIds"]))
        end = next(body for path, body, _ in api.writes if path.endswith("/lifecycle"))
        self.assertEqual([], end["cancelPlans"])

    def test_replay_preserves_edits_and_resumed_lifecycle_without_duplicate_writes(self):
        api = LocalApi()
        seed_subjects(api)
        bicycle = next(s for s in api.subjects.values() if s.get("itemType") == "bicycle")
        bicycle["name"] = "我重新命名的自行车"
        bicycle["state"] = 0
        bicycle["version"] += 1
        count = len(api.writes)
        replay = seed_subjects(api)
        self.assertEqual(0, replay["createdSubjects"])
        self.assertEqual(7, replay["existingSubjects"])
        self.assertEqual(count, len(api.writes))
        self.assertEqual("我重新命名的自行车", bicycle["name"])
        self.assertEqual(0, bicycle["state"])
        self.assertEqual(1, api.lifecycle_calls)

    def test_deleted_fixtures_and_dependent_graph_are_not_restored(self):
        api = LocalApi()
        seed_subjects(api)
        friend = next(s for s in api.subjects.values() if s.get("name", "").startswith("小林"))
        api.deleted.add(friend["id"])
        count = len(api.writes)
        replay = seed_subjects(api)
        self.assertEqual(3, replay["skippedSubjects"])
        self.assertEqual(count, len(api.writes))
        self.assertIn(friend["id"], api.deleted)

    def test_interrupted_initial_seed_can_finish_untouched_lifecycle(self):
        api = LocalApi()
        original = api._request

        def interrupt(opener, method, url, operation, **kwargs):
            if url.endswith("/api/v1/events"):
                raise SeedError("Interrupted local seed")
            return original(opener, method, url, operation, **kwargs)

        api._request = interrupt
        with self.assertRaises(SeedError):
            seed_subjects(api)
        self.assertEqual(0, api.lifecycle_calls)
        api._request = original
        seed_subjects(api)
        self.assertEqual(1, api.lifecycle_calls)

    def test_no_writes_without_authentication_and_stable_field_identifiers(self):
        api = LocalApi()
        api.access_token = None
        with self.assertRaises(SeedError):
            seed_subjects(api)
        self.assertEqual([], api.writes)
        all_fields = [field_id(spec["key"], key) for spec in catalog() for key, *_ in spec["fields"]]
        self.assertEqual(len(all_fields), len(set(all_fields)))
        self.assertTrue(PREFIX.startswith("development-"))


if __name__ == "__main__":
    unittest.main()
