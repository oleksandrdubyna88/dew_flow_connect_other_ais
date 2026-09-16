#!/bin/sh
# Does this archive carry every file it is supposed to?
#
#   archive-carries.sh <archive.tar.gz> <pattern>...
#
# Each pattern is matched as a shell glob against the BASENAME of each entry, so `coai-bugs` means
# a file called exactly that and `'*e_sqlite3*'` means one whose name contains it — which is what
# the SQLite library needs, since it ships as `libe_sqlite3.so` on one rid and `e_sqlite3.so` on
# another. Exact by default is the point of the file.
#
# ---------------------------------------------------------------------------
# WHY THIS IS A SCRIPT AND NOT SIX LINES INSIDE `release.yml`.
#
# It was six lines inside `release.yml`, and they had a hole nothing could have found there:
#
#     grep -q "$NAME/[^/]*$WANTED"
#
# looks for the name ANYWHERE in an entry, and `coai-bugs` appears in `coai-bugs.dbg`. An archive
# carrying the debug symbols and NOT the executable passed the one check whose entire job is to
# prove the executable is in the archive — verified by building exactly that archive and watching
# the loop answer `found coai-bugs` with no `coai-bugs` in it.
#
# The defect is not the pattern; it is that release-only shell runs once, in the dark, on the day
# it matters. Here it is a file the ordinary suite executes on every push, against archives built
# to be wrong on purpose. (CodeRabbit, #328.)
#
# Exit codes: 0 everything is there · 1 something is missing · 2 called wrongly.
# ---------------------------------------------------------------------------
set -eu

ARCHIVE=${1:-}
[ -n "$ARCHIVE" ] || { printf 'usage: archive-carries.sh <archive.tar.gz> <pattern>...\n' >&2; exit 2; }
shift
[ "$#" -gt 0 ] || { printf 'archive-carries.sh: nothing to look for\n' >&2; exit 2; }
[ -f "$ARCHIVE" ] || { printf 'archive-carries.sh: %s is not a file\n' "$ARCHIVE" >&2; exit 2; }

LISTING=$(tar tzf "$ARCHIVE") || { printf 'archive-carries.sh: %s could not be listed\n' "$ARCHIVE" >&2; exit 2; }

missing=0
for WANTED in "$@"; do
    found=''
    # The listing has one entry per line and tar never emits a bare newline in these names.
    for ENTRY in $LISTING; do
        # Directories end in a slash and are not what anybody is asking about.
        case "$ENTRY" in
            */) continue ;;
            *) ;;
        esac

        BASE=${ENTRY##*/}
        # The pattern is deliberately UNQUOTED here: that is what makes `*e_sqlite3*` a glob.
        # shellcheck disable=SC2254
        case "$BASE" in
            $WANTED) found=yes; break ;;
            *) ;;
        esac
    done

    if [ -z "$found" ]; then
        printf 'the archive does not carry %s\n' "$WANTED" >&2
        missing=1
    fi
done

if [ "$missing" -ne 0 ]; then
    printf -- '--- what it does carry ---\n' >&2
    printf '%s\n' "$LISTING" >&2
    exit 1
fi
