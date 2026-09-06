#!/usr/bin/env bash
# Tar the data directory, timestamped.
#
# /data is the signed-in vendor accounts, vendors.json, the sessions and usage.jsonl.
# Losing it means every account signs in again and the spending history is gone. Nothing
# here is unrecoverable — and nothing is automatic either, which is why this is a script an
# operator runs rather than a promise this stack makes.
set -euo pipefail

# The archive holds signed-in vendor credentials and live session tokens. Default permissions make
# it world-readable, which on a shared host hands every local account the whole team's access.
# Raised on the code round.
umask 077

cd "$(dirname "$0")"

# shellcheck disable=SC1091
[ -f .env ] && set -a && . ./.env && set +a

data="${DATA_DIR:-./data}"
out="${BACKUP_DIR:-./backup}"
stamp="$(date -u +%Y%m%d-%H%M%S)"

if [ ! -d "$data" ]; then
  echo "no data directory at $data — nothing to back up" >&2
  exit 1
fi

mkdir -p "$out"
archive="$out/coai-data-$stamp.tar.gz"

# Stopped, not live: usage.jsonl is appended to and a slot's lock file is held open, so a
# hot copy can catch a half-written line. A Team server is idle most of the time; the few
# seconds this costs are cheaper than a backup nobody can trust.
echo "stopping the server for a consistent copy…"
docker compose stop coai
trap 'docker compose start coai' EXIT

tar -czf "$archive" -C "$(dirname "$data")" "$(basename "$data")"

echo "wrote $archive ($(du -h "$archive" | cut -f1))"
