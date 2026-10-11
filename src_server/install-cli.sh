#!/bin/sh
# install-cli.sh <url> <shell> — fetch a vendor CLI's installer, check it, then run it.
#
# The image used to run each installer as `curl -fsSL <url> | bash`. On 2026-10-10 (release run 38075423539,
# server-v0.11.0) the amd64 image failed on ONE bad answer: bash was handed something that was not a script
# ("syntax error near unexpected token `)'" — an HTML page, most likely), while the arm64 image built from the same
# URL in the same run. A pipe cannot look before it runs and cannot try again. This script:
#
#   - fetches to a file, each attempt bounded (a server that accepts and then stalls must not hang the build);
#   - accepts the file only when its first line is a shebang;
#   - tries three times with a short pause, saying each time what arrived instead (its first bytes);
#   - then runs it with the shell the caller names, and fails the build if no attempt was a script —
#     a persistent bad answer must still be a red release, only a transient one is absorbed.
#
# POSIX sh and curl only: the runtime image is runtime-deps plus curl and ca-certificates, nothing else.
# INSTALL_CLI_PAUSE (seconds, default 5) exists for the test suite, which asks for no pause.
set -eu

url=${1:?usage: install-cli.sh <url> <shell>}
shell=${2:?usage: install-cli.sh <url> <shell>}
pause=${INSTALL_CLI_PAUSE:-5}
file=$(mktemp)
trap 'rm -f "$file"' EXIT

attempt=1
while [ "$attempt" -le 3 ]; do
    : >"$file"
    if curl -fsSL --connect-timeout 20 --max-time 300 "$url" -o "$file" && [ "$(head -c 2 "$file")" = '#!' ]; then
        "$shell" "$file"
        exit 0
    fi
    printf 'install-cli: %s did not answer with a script (attempt %s of 3): %s\n' \
        "$url" "$attempt" "$(head -c 120 "$file" | tr '\r\n' '  ')" >&2
    attempt=$((attempt + 1))
    [ "$attempt" -le 3 ] && sleep "$pause"
done

exit 1
