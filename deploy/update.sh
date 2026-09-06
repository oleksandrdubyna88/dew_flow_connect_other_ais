#!/usr/bin/env bash
# Pull the image named in .env, recreate the container, and WAIT for it to be healthy.
#
# The waiting is the point. `docker compose up -d` returns while the process is still
# starting, so a script that stops there reports success for a container that is about to
# crash — and the operator finds out from a user.
set -euo pipefail

cd "$(dirname "$0")"

if [ ! -f .env ]; then
  echo "no .env here — cp .env.example .env and fill in the required values" >&2
  exit 1
fi

echo "pulling…"
docker compose pull coai

echo "recreating…"
docker compose up -d

echo "waiting for healthy…"
container="$(docker compose ps -q coai)"
for _ in $(seq 1 90); do
  status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")"
  case "$status" in
    healthy) echo "healthy"; docker compose ps; exit 0 ;;
    none)    echo "this image declares no healthcheck; not waiting" >&2; exit 0 ;;
  esac
  sleep 2
done

# Three minutes is long past a cold start of a Native AOT binary. Print WHY rather than a
# bare failure: the logs are the only thing that says which of the many first-boot problems
# this is.
echo "still not healthy after 180s — the last 50 log lines follow" >&2
docker compose logs --tail 50 coai >&2
exit 1
