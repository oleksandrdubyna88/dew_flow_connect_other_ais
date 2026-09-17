#!/bin/sh
# The first usable key of a delivered administrator list, read from stdin as base64.
#
# ---------------------------------------------------------------------------
# WHY THIS IS A FILE OF ITS OWN.
#
# `deploy-cmd.sh admin-check` needs one key to make one authenticated request with. Picking it means
# knowing which line of the list is a key, and the SERVER already knows: `AdminKeys.Lines` trims each
# line, drops the blank ones and the `#` comments, and hashes the rest. Two implementations of that
# rule is one implementation and one thing that drifts from it — and when it drifts, a deployment
# fails on a list the server was perfectly happy with, which is the worst kind of red.
#
# So the rule is HERE, in one place, and `src_bugs/tests/TheDeliveryAgreesWithTheServerTests.cs` runs
# THIS FILE over a table of lists and asserts the server accepts whatever it picks. A code round
# found both halves of that: the drift, and that nothing would have noticed it.
#
# THE ORDER MATTERS: trim, then drop comments. `AdminKeys.Lines` splits with `TrimEntries` and only
# then asks whether a line starts with `#`, so an indented `  # alice` is a comment there. Filtering
# before trimming made it a KEY here, and the character check below then refused it.
# ---------------------------------------------------------------------------
set -eu

refuse() { printf '%s\n' "$1" >&2; exit 1; }

# Bounded, and the line endings a Windows secret box adds are not part of the value.
BLOB=$(head -c 65536 | tr -d '\r\n')

# The alphabet BEFORE the decode: `base64 -d` emits partial output for partial input, and a refusal
# computed afterwards blamed the key's characters for a value nobody had encoded at all.
case "$BLOB" in
  ''|*[!A-Za-z0-9+/=]*) refuse "the key list is not base64" ;;
esac

LIST=$(printf '%s' "$BLOB" | base64 -d 2>/dev/null || true)
[ -n "$LIST" ] || refuse "the key list is not base64"

# `|| true` because `grep -v` exits 1 when it filters everything out, which is a list of nothing but
# comments — a case with its own sentence below rather than a bare exit code.
KEY=$(printf '%s\n' "$LIST" \
      | tr -d '\r' \
      | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' \
      | grep -v '^#' \
      | grep -v '^$' \
      | head -n 1 || true)
[ -n "$KEY" ] || refuse "the key list holds no key: every line is blank or a comment"

# WHAT A KEY MAY CONTAIN, checked before it is written anywhere. curl's config format gives meaning
# to quotes and backslash escapes inside a quoted value, so a key holding one could change what the
# rest of the line means. Issued keys are hex; this admits the wider set an operator might reasonably
# choose and refuses everything that could be punctuation in a config file.
case "$KEY" in
  *[!A-Za-z0-9_.:+/=-]*) refuse "the first key holds a character a credential should not" ;;
esac

printf '%s\n' "$KEY"
