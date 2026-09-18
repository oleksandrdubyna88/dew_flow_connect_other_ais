#!/bin/sh
# Does this publish output carry every file the package is about to be built from?
#
#   publish-output-carries.sh <dir> <pattern>...
#
# Each pattern is matched as a shell glob against the BASENAME of each regular file in <dir>, so
# `coai-mcp` means a file called exactly that and `'*e_sqlite3*'` means one whose name contains it —
# which is what the SQLite library needs, since it ships as `libe_sqlite3.so` on one rid and
# `e_sqlite3.so` on another.
#
# ---------------------------------------------------------------------------
# WHY THIS IS A SCRIPT AND NOT FOUR LINES INSIDE `release.yml`.
#
# It was four lines inside `release.yml`, three times over, and the three copies had already drifted:
#
#     NATIVE=$(ls out/*e_sqlite3* 2>/dev/null | head -1)
#     if [ -z "$NATIVE" ]; then …
#
# `ls … | head -1` answers the same empty string whether the glob matched nothing or the directory
# could not be read, and it TRUNCATES a name containing a newline — measured: with a file called
# `a<newline>b_e_sqlite3.so` it set NATIVE to `out/a`, and the release would have shipped without the
# library the step had just said it verified.
#
# The defect is not the pattern; it is that release-only shell runs once, in the dark, on the day it
# matters. Here it is a file the ordinary suite executes on every push, against directories built to
# be wrong on purpose — the same move `archive-carries.sh` made after #328, and for the same reason.
# (CodeRabbit, #370.)
#
# Exit codes: 0 everything is there · 1 something is missing · 2 called wrongly.
# ---------------------------------------------------------------------------
set -eu

DIR=${1:-}
[ -n "$DIR" ] || { printf 'usage: publish-output-carries.sh <dir> <pattern>...\n' >&2; exit 2; }
shift
[ "$#" -gt 0 ] || { printf 'publish-output-carries.sh: nothing to look for\n' >&2; exit 2; }
[ -d "$DIR" ] || { printf 'publish-output-carries.sh: %s is not a directory\n' "$DIR" >&2; exit 2; }

missing=0
for WANTED in "$@"; do
    found=''
    # An unmatched glob leaves the pattern itself in the loop, which `-f` then answers no to — so
    # "nothing matched" and "matched a directory" both fall through to the same honest report.
    for ENTRY in "$DIR"/*; do
        [ -f "$ENTRY" ] || continue

        BASE=${ENTRY##*/}
        # The pattern is deliberately UNQUOTED here: that is what makes `*e_sqlite3*` a glob.
        # shellcheck disable=SC2254
        case "$BASE" in
            $WANTED) found=yes; break ;;
            *) ;;
        esac
    done

    if [ -z "$found" ]; then
        printf 'the publish output does not carry %s\n' "$WANTED" >&2
        missing=1
    fi
done

if [ "$missing" -ne 0 ]; then
    printf -- '--- what it does carry ---\n' >&2
    ls -la "$DIR" >&2
    exit 1
fi
