#!/bin/bash
# usage: realsnap.sh <tag>   prints sha256 (first 12 hex only), size and mtime of the REAL sign-in files; writes $CMP_OUT/real_<tag>.txt
. "$(dirname "$0")/env.sh"
for f in "$HOME/.codex/auth.json" "$HOME/.gemini/antigravity-cli/antigravity-oauth-token" "$HOME/.gemini/jetski-standalone-oauth-token"; do
  if [ -f "$f" ]; then
    echo "$(basename "$f") sha256-12=$(sha256sum "$f" | cut -c1-12) size=$(stat -c %s "$f") mtime=$(stat -c %y "$f")"
  else echo "$(basename "$f") absent"; fi
done | tee "$CMP_OUT/real_$1.txt"
