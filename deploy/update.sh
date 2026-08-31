#!/bin/sh
set -eu

ROOT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$ROOT_DIR"

DEPLOY_BRANCH=${DEPLOY_BRANCH:-codex/media-ai-memory}
git fetch origin "$DEPLOY_BRANCH"
git checkout "$DEPLOY_BRANCH"
git pull --ff-only origin "$DEPLOY_BRANCH"

docker compose --env-file deploy/.env -f deploy/compose.yml --profile setup run --rm certificates
if [ "${DEPLOY_PREBUILT_IMAGES:-false}" = "true" ]; then
  docker compose --env-file deploy/.env -f deploy/compose.yml pull
  docker compose --env-file deploy/.env -f deploy/compose.yml up -d --no-build --remove-orphans
else
  docker compose --env-file deploy/.env -f deploy/compose.yml build --pull
  docker compose --env-file deploy/.env -f deploy/compose.yml up -d --remove-orphans
fi
docker compose --env-file deploy/.env -f deploy/compose.yml ps
