#!/usr/bin/env bash
# smoke-image.sh <image> — RUN a built coai-server image and USE it: the pull-request image job's delivery check.
#
# A build that ends in `--version` proves three binaries start. It does not prove the image serves: that the server
# starts with a real configuration, accepts a sign-in, and reads its own stores. This does, against the image the
# job just built, and pushes nothing:
#
#   1. a throwaway data directory seeded exactly as the contract runner seeds its own (http/run-contracts.mjs): ONE
#      vendor in vendors.json and ONE ledger line in usage.jsonl, the developer's, stamped now;
#   2. the container started with that runner's Local sign-in configuration: a fresh random HMAC key for THIS run
#      (never a constant), allowed domain example.com, admin boss@example.com, no Microsoft, no Google, no proxy;
#   3. /api/health waited for, bounded (SMOKE_WAIT_SECONDS, default 60), giving up early if the container is gone;
#   4. a token minted the way that runner mints it (HS256, iss coai-local, the developer's email, one hour), and
#      GET /api/usage?window=year asserted to carry the seeded launch: vendor codex, model gpt-5.6-luna, scope me;
#   5. `agy --version`, `codex --version` and `claude --version` run INSIDE the running container as the app user.
#
# The container runs with docker's own --rm and is stopped on every way out; nothing here stops anything it did
# not start. Its log is streamed to a file from the moment it starts — with --rm a container that crashed is gone,
# and so is the log `docker logs` would have read — and printed when anything fails.
#
# The key goes to docker by NAME (`-e Auth__Local__SigningKey`), from the environment, and the token through a
# header file: neither is ever on an argument list. On GitHub both are masked as well.
#
# Sourcing it defines the functions without running anything; the server suite drives them that way
# (TheImageSmokeUsesTheServerTests). SMOKE_PORT (default 18090) is the loopback port it publishes.

readonly SMOKE_DOMAIN=example.com
readonly SMOKE_ADMIN="boss@${SMOKE_DOMAIN}"
readonly SMOKE_DEVELOPER="dev@${SMOKE_DOMAIN}"

say() { printf 'smoke: %s\n' "$*" >&2; }

b64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }

# A fresh key for one run: 48 random bytes, base64url — 64 characters, twice the 32 bytes the server requires.
new_key() { openssl rand 48 | b64url; }

# mint <key> <email> — a JWT the server's Local scheme accepts, the shape run-contracts.mjs mints.
mint() {
    local header payload signature
    header=$(printf '%s' '{"alg":"HS256","typ":"JWT"}' | b64url)
    payload=$(printf '{"iss":"coai-local","email":"%s","exp":%s}' "$2" "$(($(date +%s) + 3600))" | b64url)
    signature=$(printf '%s' "$header.$payload" | openssl dgst -sha256 -hmac "$1" -binary | b64url)
    printf '%s.%s.%s\n' "$header" "$payload" "$signature"
}

# seed <dir> — what run-contracts.mjs writes into its data directory, byte for byte in shape.
seed() {
    printf '%s' '[{"id":"codex","runtime":"codex","models":["gpt-5.6-luna"],"slots":["a"]}]' >"$1/vendors.json"
    printf '{"utc":"%s","provider":"codex","model":"gpt-5.6-luna","role":"Architecture","stage":"TeamServer","seconds":5,"tokensIn":100,"tokensOut":10,"costUsd":null,"outcome":"ok","email":"%s"}\n' \
        "$(date -u +%Y-%m-%dT%H:%M:%S.000Z)" "$SMOKE_DEVELOPER" >"$1/usage.jsonl"
}

# check_usage <body> — the developer's answer carries the seeded launch under its vendor and model.
check_usage() {
    grep -q '"scope":"me"' <<<"$1" \
        && grep -q '"vendor":"codex"' <<<"$1" \
        && grep -q '"model":"gpt-5.6-luna"' <<<"$1"
}

main() {
    set -euo pipefail
    local image=${1:-}
    if [ -z "$image" ]; then
        printf 'usage: smoke-image.sh <image>\n' >&2
        return 2
    fi
    local port=${SMOKE_PORT:-18090} bound=${SMOKE_WAIT_SECONDS:-60}
    local base="http://127.0.0.1:${port}"
    # GLOBALS, not locals: `finish` runs from the EXIT trap, after main has returned and its locals are gone.
    work='' id='' logs_pid='' ok=false

    work=$(mktemp -d)
    mkdir "$work/data"
    # The app runs as uid 10001, not as the runner's user, and must be able to write here (health probes it).
    chmod 0777 "$work/data"
    seed "$work/data"

    Auth__Local__SigningKey=$(new_key)
    export Auth__Local__SigningKey
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        printf '::add-mask::%s\n' "$Auth__Local__SigningKey"
    fi

    finish() {
        if [ -n "$id" ]; then
            docker stop --time 10 "$id" >/dev/null 2>&1 || true
        fi
        if [ -n "$logs_pid" ]; then
            # Our own `docker logs -f` child; it ends with the container, and this only bounds the wait for it.
            for _ in 1 2 3 4 5; do kill -0 "$logs_pid" 2>/dev/null || break; sleep 1; done
            kill "$logs_pid" 2>/dev/null || true
        fi
        if [ "$ok" != true ] && [ -f "$work/container.log" ]; then
            say "the container's log:"
            cat "$work/container.log" >&2
        fi
        if [ -n "$work" ]; then
            rm -rf "$work" 2>/dev/null || true
        fi
    }
    trap finish EXIT

    say "starting $image on $base"
    id=$(docker run -d --rm \
        -p "127.0.0.1:${port}:8080" \
        -v "$work/data:/data" \
        -e Coai__AllowedDomains="$SMOKE_DOMAIN" \
        -e Coai__Admins="$SMOKE_ADMIN" \
        -e Coai__RequireForwardedHttps=false \
        -e Auth__Local__SigningKey \
        -e Auth__Microsoft__Tenant= \
        -e Auth__Microsoft__Audiences= \
        -e Auth__Google__Enabled=false \
        "$image")
    docker logs -f "$id" >"$work/container.log" 2>&1 &
    logs_pid=$!

    say "waiting for /api/health (at most ${bound}s)"
    local deadline=$((SECONDS + bound)) health=''
    until health=$(curl -fsS --max-time 5 "$base/api/health" 2>/dev/null) && grep -q '"ok":true' <<<"$health"; do
        if [ "$SECONDS" -ge "$deadline" ]; then
            say "/api/health did not answer ok within ${bound}s"
            return 1
        fi
        if ! docker container inspect "$id" >/dev/null 2>&1; then
            say "the container exited before /api/health answered"
            return 1
        fi
        sleep 1
    done
    say "health: $health"

    local token
    token=$(mint "$Auth__Local__SigningKey" "$SMOKE_DEVELOPER")
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        printf '::add-mask::%s\n' "$token"
    fi
    printf 'Authorization: Bearer %s\n' "$token" >"$work/auth"
    chmod 0600 "$work/auth"
    local usage
    usage=$(curl -fsS --max-time 10 -H @"$work/auth" "$base/api/usage?window=year")
    say "usage: $usage"
    if ! check_usage "$usage"; then
        say "/api/usage did not return the seeded launch (codex / gpt-5.6-luna for $SMOKE_DEVELOPER): the ledger was not read"
        return 1
    fi

    local cli version
    for cli in agy codex claude; do
        # An assignment on its own line, so a CLI that does not start fails the job (set -e) rather than printing.
        version=$(docker exec --user 10001:10001 "$id" "$cli" --version 2>&1)
        say "$cli --version (in the container, as 10001): $version"
    done

    ok=true
    say "the image starts, signs a request in, reads its own ledger, and carries three CLIs that start"
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    main "$@"
fi
