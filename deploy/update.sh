#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT_DIR"

DEPLOY_BRANCH=${DEPLOY_BRANCH:-codex/media-ai-memory}
git fetch origin "$DEPLOY_BRANCH"
git checkout "$DEPLOY_BRANCH"
git pull --ff-only origin "$DEPLOY_BRANCH"

docker compose --env-file deploy/.env -f deploy/compose.yml --profile setup run --rm certificates
docker compose --env-file deploy/.env -f deploy/compose.yml build --pull
docker compose --env-file deploy/.env -f deploy/compose.yml up -d --remove-orphans
docker compose --env-file deploy/.env -f deploy/compose.yml ps
