#!/bin/bash
# usage: cxrun.sh <row> <label> <mode: exec|resume:<thread>> <prompt-file> [extra codex args...]   (env: CX_FLAGS to override base flags; UNIT_* pass through)
# Runs ONE paid codex call inside the coder unit; stores out/cx_<label>.jsonl/.err; ledger line; tree diff vs the snapshot taken just before.
S=${E3S4_SCRATCH:?set E3S4_SCRATCH to your scratch folder}; F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}
row=$1; label=$2; mode=$3; pf=$4; shift 4
DIS=$(cat $S/out/cx_disable_set.txt)
BASE=(--json -m gpt-6-luna -c model_reasoning_effort=low $DIS -c 'approval_policy="never"' -c 'shell_environment_policy.inherit="core"')
[ -n "$CX_FLAGS" ] && BASE=($CX_FLAGS)
cp $pf $F/$row/c/prompt.txt
$S/tools/snap.sh $row $S/out/cx_${label}_before.txt
t0=$(date +%s)
if [ "$mode" = exec ]; then ARGS=(codex exec "${BASE[@]}" "$@" -); else ARGS=(codex exec resume "${BASE[@]}" "$@" "${mode#resume:}" -); fi
UNIT_HIDE=${UNIT_HIDE-1} UNIT_TMPFS=${UNIT_TMPFS-1} $S/tools/unit.sh $row codex coai-e3s4-$label 240 -- "${ARGS[@]}" < $pf > $S/out/cx_$label.jsonl 2> $S/out/cx_$label.err
rc=$?; t1=$(date +%s)
$S/tools/ledger.sh codex "$label" $rc $((t1-t0)) "$(echo "${ARGS[@]}" | cut -c1-120)"
$S/tools/snap.sh $row $S/out/cx_${label}_after.txt
echo "rc=$rc secs=$((t1-t0)) lines=$(wc -l < $S/out/cx_$label.jsonl)"; diff $S/out/cx_${label}_before.txt $S/out/cx_${label}_after.txt | head -20
