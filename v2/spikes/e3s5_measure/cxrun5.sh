#!/bin/bash
# usage: cxrun5.sh <row> <label> <mode: exec|resume:<thread>> <prompt-file> [extra codex args...]
# env: CX_SAMPLE=1 (process sampler), CX_FLAGS (replace base flags), UNIT_* (pass through to unit.sh), CX_NOSTDIN=1
# ONE call: stored as out/cx_<label>.jsonl/.err, a ledger line with tokens, the tree diff of the row. CX1 base flags (RESULTS_codex_coder.md section 2) incl. web_search="disabled".
S=${E3S4_SCRATCH:?}; F=${E3S4_FIXTURES:?}
row=$1; label=$2; mode=$3; pf=$4; shift 4
DIS=$(cat $S/out/cx_disable_set.txt)
BASE=(--json -m gpt-6-luna -c model_reasoning_effort=low $DIS -c 'approval_policy="never"' -c 'shell_environment_policy.inherit="core"' -c 'web_search="disabled"')
[ -n "$CX_FLAGS" ] && BASE=($CX_FLAGS)
cp $pf $F/$row/c/prompt.txt
$S/tools/snap.sh $row $S/out/cx_${label}_before.txt
[ -n "$CX_SAMPLE" ] && { $S/tools/cgsample.sh coai-e3s5-$label $S/out/cx_${label}_procs.txt 700 & SP=$!; }
t0=$(date +%s)
if [ "$mode" = exec ]; then ARGS=(codex exec "${BASE[@]}" "$@" -); else ARGS=(codex exec resume "${BASE[@]}" "$@" "${mode#resume:}" -); fi
UNIT_HIDE=${UNIT_HIDE-1} UNIT_TMPFS=${UNIT_TMPFS-1} $S/tools/unit.sh $row codex coai-e3s5-$label ${CX_TMO:-240} -- "${ARGS[@]}" < $pf > $S/out/cx_$label.jsonl 2> $S/out/cx_$label.err
rc=$?; t1=$(date +%s)
[ -n "$SP" ] && { sleep 1; kill $SP 2>/dev/null; }
usage=$(node -e 'const fs=require("fs");let u="-";for(const l of fs.readFileSync(process.argv[1],"utf8").split("\n")){try{const e=JSON.parse(l);if(e.type==="turn.completed")u=JSON.stringify(e.usage)}catch{}}console.log(u)' $S/out/cx_$label.jsonl)
$S/tools/ledger.sh codex "$label" $rc $((t1-t0)) "usage=$usage ${mode%%:*}"
$S/tools/snap.sh $row $S/out/cx_${label}_after.txt
echo "rc=$rc secs=$((t1-t0)) lines=$(wc -l < $S/out/cx_$label.jsonl) usage=$usage"; diff $S/out/cx_${label}_before.txt $S/out/cx_${label}_after.txt | head -20
