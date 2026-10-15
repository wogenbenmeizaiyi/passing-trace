#!/usr/bin/env python3
"""Read-only local social API smoke checks; never print tokens or content."""
import json
import uuid
from seed_development_demo import DevelopmentDemoClient, SeedError, parse_args


def main():
    args = parse_args()
    client = DevelopmentDemoClient(args.identity_url, args.api_url)
    client.wait_until_ready()
    client.authenticate()

    def request(path, body=None):
        data, _ = client._request(client.api_opener, "GET" if body is None else "POST",
            client.api_url + path, path.split("?")[0],
            data=None if body is None else json.dumps(body).encode(),
            headers={"Authorization": "Bearer " + client.access_token,
                     "Content-Type": "application/json"})
        return json.loads(data)

    friends = request('/api/v1/friends')
    conversations = request('/api/v1/conversations?limit=20')
    print(f"friends={len(friends)}; conversation summaries={len(conversations['items'])}")
    for kind, endpoint, field in [('record', 'events', 'eventId'), ('storyline', 'storylines', 'storylineId')]:
        page = request(f'/api/v1/{endpoint}?limit=1')
        if not page['items']:
            continue
        key = page['items'][0]['id']
        request(f'/api/v1/shares?{field}={key}')
        preview = request('/api/v1/shares/preview', {
            'clientMessageId': str(uuid.uuid4()), 'kind': kind, field: key,
        })
        assert preview['kind'] == kind and preview['available']
        print(f'{kind} share management + preview: OK')


if __name__ == '__main__':
    try:
        main()
    except SeedError as error:
        raise SystemExit(str(error)) from None
