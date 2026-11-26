#!/bin/bash
# usage: probe-hidden.sh <vendor>   From inside a unit with the CHECK unit's properties (both network states) a stat of every sign-in copy, vendor home,
# guard folder and real vendor home must FAIL. Exit 0 only when every path is hidden and the positive controls (the worktree, node) are visible.
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
v=$1; C=$(job_root "$v"); W=$(job_tree "$v")
HIDDEN=("$CMP_SCRATCH" "$CMP_SCRATCH/copies" "$CMP_SCRATCH/out" "$CMP_SCRATCH/copies/codex/home/auth.json" "$CMP_SCRATCH/copies/agy/hm/.gemini/antigravity-cli/antigravity-oauth-token"
  "$CMP_SCRATCH/copies/agy/guard/.agents/hooks.json"
  "$HOME/.codex" "$HOME/.codex/auth.json" "$HOME/.gemini" "$HOME/.gemini/antigravity-cli/antigravity-oauth-token" "$HOME/.claude" "$HOME/.config" "$HOME/coai-t3/data"
  "$C/coder/home/.codex/auth.json" "$C/coder/home/.codex/config.toml" "$C/coder/home/.gemini/antigravity-cli/antigravity-oauth-token" "$C/coder/guard/.agents/hooks.json")
RAW=$CMP_OUT/probe-$v-$(date -u +%H%M%S).txt; : > "$RAW"
for net in private host; do
  echo "== network $net" >> "$RAW"
  run_check_unit "$v" $net "probe-$net" 60 -- /bin/sh -c '
    w=$1; shift
    for p in "$@"; do if stat "$p" >/dev/null 2>&1; then echo "VISIBLE $p"; else echo "hidden  $p"; fi; done
    stat "$w/README.md" >/dev/null 2>&1 && echo "control worktree-visible OK" || echo "control worktree-visible FAILED"
    node --version >/dev/null 2>&1 && echo "control node-runs OK" || echo "control node-runs FAILED"
    echo "mnt-entries $(ls -A /mnt 2>/dev/null | wc -l)"' sh "$W" "${HIDDEN[@]}" >> "$RAW" 2>&1 || echo "unit rc=$?" >> "$RAW"
  # the mountpoint folders of the vendor homes exist inside the job folder (empty): say what they hold
  run_check_unit "$v" $net "probe2-$net" 60 -- /bin/sh -c 'for d in "$@"; do echo "dir $d entries=$(ls -A "$d" 2>/dev/null | wc -l)"; done' sh "$C/coder/home/.codex" "$C/coder/home/.gemini" "$C/coder/guard" >> "$RAW" 2>&1 || echo "unit2 rc=$?" >> "$RAW"
done
sed "s#$HOME#~#g" "$RAW"
grep -q '^VISIBLE' "$RAW" && { echo "PROBE FAILED: a path is visible"; exit 1; }
grep -q 'FAILED\|rc=' "$RAW" && { echo "PROBE FAILED: a control failed or a unit did not run"; exit 2; }
grep -q '^dir .*entries=[1-9]' "$RAW" && { echo "PROBE FAILED: a vendor mountpoint folder is not empty"; exit 3; }
echo "PROBE OK: every sign-in copy, vendor home and guard path is hidden from the check unit in both network states"
