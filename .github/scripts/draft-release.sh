#!/usr/bin/env bash
# Create the release for a tag as a DRAFT, once — or reuse the draft that is already there.
#
#   draft-release.sh <tag> <title> <notes>
#
# ONE implementation for both release lines, for the reason its sibling
# `verify-and-publish-release.sh` is one: this was written twice, and the create/reuse policy is
# exactly the kind of thing that gets tightened in one copy and left in the other. Raised on the
# code round, by the same rule.
#
# Why a draft at all: a release used to be created by whichever matrix leg finished FIRST and was
# published from that moment, while five siblings were still uploading — so the extension's update
# check offered links that answered 404. Measured twice, and both times a person found it.
#
# Why ONCE, in a job of its own: measured against the live API, with a real existing tag,
#
#   gh release create <tag> --draft   ->  releases/tag/untagged-4edbbf29...   exit 0
#   gh release create <tag> --draft   ->  releases/tag/untagged-ebd3e8b4...   exit 0
#
# A draft carries no tag of its own yet, so GitHub makes a SECOND one rather than refusing. Six
# matrix legs racing this would have produced six drafts holding one asset each.
set -euo pipefail

TAG=${1:?usage: draft-release.sh <tag> <title> <notes>}
TITLE=${2:?usage: draft-release.sh <tag> <title> <notes>}
NOTES=${3:?usage: draft-release.sh <tag> <title> <notes>}

# Reused only while it is still a DRAFT. `gh release view` succeeds for a PUBLISHED release too, so
# a re-run against a tag somebody had published by hand would otherwise send every leg uploading
# into a release clients can already see — the exact window this whole shape removes.
if EXISTING=$(gh release view "$TAG" --json isDraft --jq '.isDraft' 2>/dev/null); then
  if [ "$EXISTING" = "true" ]; then
    echo "$TAG is already a draft; reusing it"
    exit 0
  fi

  echo "$TAG is already PUBLISHED — refusing to upload into a visible release."
  echo "Cut a new tag, or delete that release if it was published in error."
  exit 1
fi

gh release create "$TAG" --draft --title "$TITLE" --notes "$NOTES"
echo "drafted $TAG"
