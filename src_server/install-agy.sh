#!/bin/sh
# install-agy.sh — install the agy (Antigravity) CLI from Google's release MANIFEST, checked, into $HOME/.local/bin/agy.
#
# agy used to go through install-cli.sh like the other two, fetching https://antigravity.google/cli/install.sh. On
# GitHub's runners that page answered HTML instead of the script (release run 38075423539), while it serves text/x-sh
# elsewhere, so the image (built only by the release workflow) broke a release. Google's install.sh does not need that
# page for the real download. Read on 2026-10-11 (never run), it:
#
#   1. detects the platform: linux_amd64 / linux_arm64, with a _musl suffix on a musl libc;
#   2. fetches <updater>/manifests/<platform>.json -> {"version", "url", "sha512"};
#   3. downloads the payload the manifest names and checks its SHA-512 against the manifest;
#   4. takes the `antigravity` binary out of the .tar.gz (or the payload itself when it is not one);
#   5. copies it to $HOME/.local/bin/agy, chmod +x;
#   6. runs `agy install` (shell set-up) and IGNORES its result (`|| true`).
#
# This script is steps 1-5, from the SAME endpoints, with what install-cli.sh added: each fetch bounded and announced
# BEFORE it starts, three attempts with a pause, a manifest accepted only when it is JSON carrying a url and a
# 128-hex sha512 (otherwise its first bytes are shown), and a payload whose hash differs refused outright: that is
# never a transient to absorb, it is the one answer that must stop the build. Nothing is installed until the hash
# matches, and the file appears under its final name only once it is complete and executable.
#
# Step 6 is deliberately NOT run. It edits shell start-up files so an interactive shell finds agy on PATH; the image sets
# PATH with ENV, no shell start-up file is ever read by the server, and Google's own script already treats the step as
# optional by ignoring its exit status. Running a just-downloaded, unpinned binary's set-up command in the build would
# add a dependency on whatever it does that day for nothing the image uses. The Dockerfile's `agy --version` is the
# proof that the binary starts.
#
# Linux only (the image). POSIX sh, curl, tar, gzip and coreutils: all present in runtime-deps:10.0-noble.
#   AGY_MANIFEST_BASE   the manifests folder (default: Google's updater); the test suite points it at file:// fixtures.
#   INSTALL_CLI_PAUSE   seconds between attempts (default 5), shared with install-cli.sh; the suite asks for none.
set -eu

base=${AGY_MANIFEST_BASE:-https://antigravity-cli-auto-updater-974169037036.us-central1.run.app/manifests}
pause=${INSTALL_CLI_PAUSE:-5}
target="$HOME/.local/bin/agy"

say() { printf 'install-agy: %s\n' "$*" >&2; }

# ---- 1. the platform -------------------------------------------------------------------------------------------------
os=$(uname -s)
if [ "$os" != Linux ]; then
    say "this installer is for the Linux image; this machine is $os"
    exit 1
fi
machine=$(uname -m)
case $machine in
    x86_64 | amd64) arch=amd64 ;;
    aarch64 | arm64) arch=arm64 ;;
    *)
        say "Google publishes no agy build for this machine: $machine"
        exit 1
        ;;
esac
if [ -f /lib/libc.musl-x86_64.so.1 ] || [ -f /lib/libc.musl-aarch64.so.1 ] || ldd /bin/ls 2>&1 | grep -q musl; then
    platform="linux_${arch}_musl"
else
    platform="linux_${arch}"
fi
say "platform $platform"

stage=$(mktemp -d)
trap 'rm -rf "$stage"' EXIT

# fetch <url> <file> <check> <refusal> — up to three bounded attempts; the attempt counts only when <check> accepts the
# file. A refused attempt says what arrived instead (its first bytes). Returns non-zero when no attempt was accepted.
fetch() {
    attempt=1
    while [ "$attempt" -le 3 ]; do
        say "fetching $1 (attempt $attempt of 3)"
        : >"$2"
        if curl -fsSL --connect-timeout 20 --max-time 300 "$1" -o "$2" && "$3" "$2"; then
            return 0
        fi
        say "$1 $4 (attempt $attempt of 3): $(head -c 120 "$2" | tr '\r\n' '  ')"
        attempt=$((attempt + 1))
        if [ "$attempt" -le 3 ]; then
            sleep "$pause"
        fi
    done
    return 1
}

# The value of one string key of a JSON object flattened onto one line (no jq in the image). Empty when absent.
key() { printf '%s\n' "$flat" | sed -n 's/.*"'"$1"'":"\([^"]*\)".*/\1/p'; }

# A manifest is a JSON object with a url and a 128-hex sha512. Whitespace is dropped first: Google pretty-prints it,
# and none of the three values can contain any.
is_manifest() {
    flat=$(tr -d ' \t\r\n' <"$1")
    case $flat in
        '{'*'}') ;;
        *) return 1 ;;
    esac
    url=$(key url)
    sha512=$(key sha512 | tr 'A-F' 'a-f')
    version=$(key version)
    [ -n "$url" ] && [ "${#sha512}" -eq 128 ] && case $sha512 in *[!0-9a-f]*) false ;; *) true ;; esac
}

accept_any() { true; }

# ---- 2. the manifest -------------------------------------------------------------------------------------------------
manifest="$stage/manifest.json"
fetch "$base/$platform.json" "$manifest" is_manifest "did not answer with a release manifest" || exit 1
say "version ${version:-unknown} from $url"

# ---- 3. the payload, and its hash ------------------------------------------------------------------------------------
payload="$stage/payload"
fetch "$url" "$payload" accept_any "could not be downloaded" || exit 1
say "checking the download's SHA-512 against the manifest"
if command -v sha512sum >/dev/null 2>&1; then
    actual=$(sha512sum "$payload" | cut -d' ' -f1)
else
    actual=$(shasum -a 512 "$payload" | cut -d' ' -f1)
fi
if [ "$actual" != "$sha512" ]; then
    say "the download's SHA-512 does not match the manifest - refused, nothing installed"
    say "  manifest: $sha512"
    say "  download: $actual"
    exit 1
fi
say "SHA-512 verified"

# ---- 4. the binary ---------------------------------------------------------------------------------------------------
case $url in
    *.tar.gz*)
        say "extracting antigravity from the archive"
        # Relative names inside the stage: GNU tar reads "D:/..." as a remote host, and bsdtar has no --force-local.
        (cd "$stage" && tar -xzf payload antigravity)
        binary="$stage/antigravity"
        ;;
    *) binary="$payload" ;;
esac

# ---- 5. installed under its final name only once complete and executable ---------------------------------------------
say "installing $target"
mkdir -p "$(dirname "$target")"
cp "$binary" "$target.partial"
chmod 755 "$target.partial"
mv -f "$target.partial" "$target"
say "installed agy ${version:-unknown} at $target"
