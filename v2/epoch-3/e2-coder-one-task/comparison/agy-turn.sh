#!/bin/bash
# usage: agy-turn.sh <label> <prompt-file> [<conversation-id>]   ONE agy turn (turn 0, or a resumed fix turn) in the coder unit, flag set AG1 of RESULTS_agy_coder.md section 2
# (--mode accept-edits, --agent coai-coder on EVERY argv, the worktree only as --add-dir, the cwd the guard folder). Model gemini-4-argon-medium.
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
label=$1; pf=$2; conv=${3:-}; W=$(job_tree agy)
node -e 'const fs=require("fs"); process.stdout.write(JSON.stringify({event:"user",message:{role:"user",content:fs.readFileSync(process.argv[1],"utf8")}})+"\n")' "$pf" > "$CMP_OUT/agy_$label.stdin"
ARGS=(agy --print= --input-format stream-json --output-format stream-json --mode accept-edits --agent coai-coder --model gemini-4-argon-medium --add-dir "$W")
[ -n "$conv" ] && ARGS+=(--conversation "$conv")
echo "ARGV: ${ARGS[*]}" | sed "s#$HOME#~#g" > "$CMP_OUT/agy_$label.argv"
: > "$CMP_SCRATCH/copies/agy/hm/.gemini/coai-hooklog.jsonl"
t0=$(date +%s.%N)
"$(dirname "$0")/coder-unit.sh" agy "$label" 900 "$CMP_OUT/agy_$label.stdin" "$CMP_OUT/agy_$label.jsonl" "$CMP_OUT/agy_$label.err" -- "${ARGS[@]}"; rc=$?
t1=$(date +%s.%N)
cp "$CMP_SCRATCH/copies/agy/hm/.gemini/coai-hooklog.jsonl" "$CMP_OUT/agy_${label}_hooklog.tsv" 2>/dev/null
printf 'agy\t%s\trc=%s\twall=%.1fs\tlines=%s\n' "$label" "$rc" "$(echo "$t1 - $t0" | bc)" "$(wc -l < "$CMP_OUT/agy_$label.jsonl")" | tee -a "$CMP_OUT/calls.tsv"
