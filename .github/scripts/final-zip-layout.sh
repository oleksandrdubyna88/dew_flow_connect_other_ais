#!/bin/sh
# Are the binary and the SQLite library at the TOP LEVEL of the package folder of a Windows zip?
#
#   final-zip-layout.sh <coai-mcp-<version>-<rid>.zip>
#
# `archive-carries.sh` answers "is there a file with this name anywhere in the archive", by BASENAME,
# and that contract stays: coai-bugs and the tar check rely on it. It cannot see DEPTH. A zip with
# `coai-mcp.exe` and `e_sqlite3.dll` one directory too deep, or at its root with no package folder,
# passes it — and the Windows loader looks for the library BESIDE the binary, so that package starts
# and then fails on its first database write. (CodeRabbit, #944.)
#
# The package folder is not passed in: it is read from the zip's own name, `coai-mcp-<v>-<rid>.zip`
# holds `coai-mcp-<v>-<rid>/`, which is how the shared signing workflow packs it. So a zip whose
# folder is another package's is wrong too. Entries are listed with `unzip -Z1`, one bare name per line.
#
# Exit codes: 0 the layout is right · 1 something is not where it must be · 2 called wrongly, or the
# zip could not be read. The last is never a pass: a check that cannot look must not say "fine".
set -eu

ZIP=${1:-}
[ -n "$ZIP" ] || { printf 'usage: final-zip-layout.sh <coai-mcp-<version>-<rid>.zip>\n' >&2; exit 2; }
[ -f "$ZIP" ] || { printf 'final-zip-layout.sh: %s is not a file\n' "$ZIP" >&2; exit 2; }

BASE=${ZIP##*/}
case "$BASE" in
    coai-mcp-*.zip) PKG=${BASE%.zip} ;;
    *) printf 'final-zip-layout.sh: %s is not named coai-mcp-<version>-<rid>.zip, so it names no package folder\n' "$BASE" >&2; exit 2 ;;
esac

LISTING=$(unzip -Z1 "$ZIP") || { printf 'final-zip-layout.sh: %s could not be listed\n' "$ZIP" >&2; exit 2; }

# Does an entry directly inside `PKG/` (no further slash) match? `mode` is `exact` or `contains`.
# awk compares text and never reads the name as a pattern, so the dots of a version are only dots.
has_at_top() {
    printf '%s\n' "$LISTING" | awk -v pkg="$PKG/" -v want="$1" -v mode="$2" '
        BEGIN { n = length(pkg) }
        substr($0, 1, n) == pkg {
            rest = substr($0, n + 1)
            if (rest == "" || index(rest, "/") > 0) next
            if (mode == "exact" ? rest == want : index(rest, want) > 0) found = 1
        }
        END { exit found ? 0 : 1 }'
}

missing=0
if ! has_at_top coai-mcp.exe exact; then
    printf 'the zip does not carry %s/coai-mcp.exe directly in its package folder\n' "$PKG" >&2
    missing=1
fi
if ! has_at_top e_sqlite3 contains; then
    printf 'the zip does not carry %s/*e_sqlite3* directly in its package folder\n' "$PKG" >&2
    missing=1
fi

if [ "$missing" -ne 0 ]; then
    printf -- '--- what it does carry ---\n' >&2
    printf '%s\n' "$LISTING" >&2
    exit 1
fi
