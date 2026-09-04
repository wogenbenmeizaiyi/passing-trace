import datetime as dt
import importlib.util
import pathlib
import sys
import unittest


MODULE_PATH = pathlib.Path(__file__).parents[1] / "seed_production_demo.py"
SPEC = importlib.util.spec_from_file_location("seed_production_demo", MODULE_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class DemoSeedTests(unittest.TestCase):
    def test_builds_requested_number_of_deterministic_events(self):
        now = dt.datetime(2026, 9, 1, 12, tzinfo=dt.timezone(dt.timedelta(hours=8)))
        events = MODULE.build_events(12, now)

        self.assertEqual(12, len(events))
        self.assertEqual("production-demo-v1-01", events[0]["seedKey"])
        self.assertEqual("2026-09-01T12:00:00+08:00", events[0]["happenedAt"])
        self.assertEqual("scenery", events[0]["classification"]["primaryCategoryKey"])
        self.assertEqual("GCJ02", events[0]["locations"][0]["coordinateSystem"])
        self.assertEqual(12, len({event["title"] for event in events}))

    def test_rejects_count_outside_curated_dataset(self):
        with self.assertRaises(ValueError):
            MODULE.build_events(0)
        with self.assertRaises(ValueError):
            MODULE.build_events(len(MODULE.RECORD_TEMPLATES) + 1)

    def test_every_image_targets_an_existing_record(self):
        titles = {record["title"] for record in MODULE.RECORD_TEMPLATES}
        self.assertTrue({image.event_title for image in MODULE.IMAGES}.issubset(titles))

    def test_all_taxonomy_keys_match_server_vocabulary(self):
        categories = {"food", "shopping", "travel", "scenery", "entertainment", "exercise", "work", "study", "social", "home", "health", "transport", "other"}
        tags = {"dining", "restaurant", "cooking", "takeout", "coffee", "baking", "daily-goods", "clothing", "digital", "home-goods", "gift", "city-walk", "attraction", "museum", "photography", "camping", "business-trip", "movie", "music", "ktv", "gaming", "show", "fitness", "running", "walking", "cycling", "hiking", "swimming", "meeting", "coding", "writing", "reading", "course", "friends", "family", "date", "cleaning", "repair", "sleep", "medical", "commute", "driving", "public-transit"}
        for record in MODULE.RECORD_TEMPLATES:
            self.assertIn(record["category"], categories)
            self.assertTrue(set(record["tags"]).issubset(tags))

    def test_strips_html_from_commons_attribution(self):
        self.assertEqual("Tyler Valentine", MODULE._plain_metadata('<a href="/wiki/User:X">Tyler</a> Valentine'))

    def test_bearer_token_is_only_sent_to_events_host(self):
        client = MODULE.PassingTraceClient("https://auth.example", "https://api.example")
        client.access_token = "private-token"
        seen_headers = []

        class Response:
            headers = {"Content-Type": "application/json"}
            def getcode(self): return 200
            def read(self): return b"{}"

        def open_request(request, timeout):
            seen_headers.append(dict(request.header_items()))
            return Response()

        client.opener.open = open_request
        client.request("GET", "https://commons.wikimedia.org/w/api.php")
        client.request("GET", "https://api.example/api/v1/events")

        self.assertNotIn("Bearer private-token", seen_headers[0].values())
        self.assertIn("Bearer private-token", seen_headers[1].values())


if __name__ == "__main__":
    unittest.main()
