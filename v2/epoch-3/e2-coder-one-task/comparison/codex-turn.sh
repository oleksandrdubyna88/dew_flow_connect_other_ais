#!/bin/bash
# usage: codex-turn.sh <label> <prompt-file> [<thread-id>]   ONE paid codex call (turn 0, or a resumed fix turn) in the coder unit, flag set CX1 of RESULTS_codex_coder.md section 2
# (+ the profile in the job's config.toml). Stores $CMP_OUT/cx_<label>.jsonl/.err and a ledger line (wall seconds). Model gpt-6.1-terra, effort low (the model's own default, pinned).
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
label=$1; pf=$2; thread=${3:-}; C=$(job_root codex)
DIS=(); for f in apps plugins remote_plugin browser_use browser_use_external browser_use_full_cdp_access computer_use image_generation view_image multi_agent goals sleep_tool tool_suggest skill_search skill_mcp_dependency_install hooks in_app_browser in_app_chat in_app_local_automation; do DIS+=(--disable $f); done
FLAGS=(--json -m gpt-6.1-terra -c model_reasoning_effort=low "${DIS[@]}" -c 'approval_policy="never"' -c 'shell_environment_policy.inherit="core"' -c 'web_search="disabled"' --output-schema "$C/coder/report.flat.schema.json")
if [ -z "$thread" ]; then ARGS=(codex exec "${FLAGS[@]}" -); else ARGS=(codex exec resume "${FLAGS[@]}" "$thread" -); fi
echo "ARGV: ${ARGS[*]}" | sed "s#$HOME#~#g" > "$CMP_OUT/cx_$label.argv"
t0=$(date +%s.%N)
"$(dirname "$0")/coder-unit.sh" codex "$label" 900 "$pf" "$CMP_OUT/cx_$label.jsonl" "$CMP_OUT/cx_$label.err" -- "${ARGS[@]}"; rc=$?
t1=$(date +%s.%N)
printf 'codex\t%s\trc=%s\twall=%.1fs\tlines=%s\n' "$label" "$rc" "$(echo "$t1 - $t0" | bc)" "$(wc -l < "$CMP_OUT/cx_$label.jsonl")" | tee -a "$CMP_OUT/calls.tsv"
