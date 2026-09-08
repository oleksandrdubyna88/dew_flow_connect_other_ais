#!/usr/bin/env bash
# Verify a draft release carries every archive its line promises, then publish it.
#
# ONE implementation for both release lines. It was written twice — the mcp line and the server
# line, sixty lines of identical polling and publishing — and the repository's own reuse-first rule
# names that a defect from the moment it compiles, because the two drift and nothing notices. The
# code round said so; this is the extraction.
#
#   verify-and-publish-release.sh <product> <tag>
#
# `product` is the archive's middle word: `mcp` or `server`. The tag is what names the release.
#
# Why this exists at all: a release used to be created by whichever matrix leg finished FIRST and
# was published from that moment, while five siblings were still uploading — so the extension's
# update check offered links that answered 404 honestly. Measured twice, on `mcp-v0.16.0` and
# `mcp-v0.18.13`, and both times a person found it rather than CI. Every line now drafts, and this
# script is the only thing that publishes.
set -euo pipefail

PRODUCT=${1:?usage: verify-and-publish-release.sh <product> <tag> [rid...]}
TAG=${2:?usage: verify-and-publish-release.sh <product> <tag> [rid...]}
shift 2

# The platforms THIS line builds, passed by its caller. Defaulted to the six both lines build
# today, because that is what they build — but a parameter, because the two lines have independent
# lifecycles and a server-only RID must not silently become an mcp release's expectation. Raised by
# two reviewers on the code round. The test suite holds each caller's list against its own matrix.
RIDS=("$@")
[ ${#RIDS[@]} -gt 0 ] || RIDS=(linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64)

# Named, because a bare `seq 1 10` beside a bare `sleep 6` is two numbers nobody can weigh.
POLL_ATTEMPTS=10        # `needs` proves the uploads returned, not that the listing caught up
POLL_SECONDS=6
PUBLISH_ATTEMPTS=3      # everything has already passed by then; this is transport, not truth
PUBLISH_SECONDS=5

version() {
  printf '%s' "${TAG#"$PRODUCT"-v}"
}

# Every archive and checksum this line promises, one per line.
expected() {
  local rid ext
  for rid in "${RIDS[@]}"; do
    case "$rid" in win-*) ext=zip ;; *) ext=tar.gz ;; esac
    printf '%s\n' "coai-$PRODUCT-$(version)-$rid.$ext"
    printf '%s\n' "coai-$PRODUCT-$(version)-$rid.$ext.sha256"
  done
}

# What the release is missing, as names. One pass over both lists rather than a grep per asset:
# twelve names against six RIDs was up to 120 processes per attempt, ten times over.
missing_from() {
  local assets=$1
  printf '%s\n' "$assets" | awk -v want="$(expected)" '
    { have[$0] = 1 }
    END {
      n = split(want, names, "\n")
      for (i = 1; i <= n; i++) {
        if (names[i] != "" && !(names[i] in have)) { print names[i] }
      }
    }'
}

echo "expecting ${#RIDS[@]} platforms for $TAG:"
expected | sed 's/^/  /'

MISSING=""
ASSETS=""
for attempt in $(seq 1 "$POLL_ATTEMPTS"); do
  ASSETS=$(gh release view "$TAG" --json assets --jq '.assets[].name')
  MISSING=$(missing_from "$ASSETS")
  [ -z "$MISSING" ] && break
  echo "attempt $attempt of $POLL_ATTEMPTS — still missing:"
  printf '%s\n' "$MISSING" | sed 's/^/  /'
  sleep "$POLL_SECONDS"
done

echo "the release holds:"
printf '%s\n' "$ASSETS" | sed 's/^/  /'

if [ -n "$MISSING" ]; then
  echo "the release is incomplete — missing:"
  printf '%s\n' "$MISSING" | sed 's/^/  /'
  exit 1
fi

echo "complete: all ${#RIDS[@]} platforms, each with its checksum"

# Retried AND re-read inside the loop. Reading `isDraft` once immediately after the edit races
# GitHub's own listing, and a release that published perfectly well would then be reported as one
# that could not be — sending somebody to publish by hand what is already live. Raised on the code
# round.
for attempt in $(seq 1 "$PUBLISH_ATTEMPTS"); do
  gh release edit "$TAG" --draft=false >/dev/null 2>&1 || true
  if [ "$(gh release view "$TAG" --json isDraft --jq '.isDraft')" = "false" ]; then
    echo "published $TAG"
    exit 0
  fi
  echo "publish attempt $attempt of $PUBLISH_ATTEMPTS did not take; retrying"
  sleep "$PUBLISH_SECONDS"
done

echo "$TAG is complete but could not be published — every asset is there, so publish it by hand:"
echo "  gh release edit $TAG --draft=false"
exit 1
