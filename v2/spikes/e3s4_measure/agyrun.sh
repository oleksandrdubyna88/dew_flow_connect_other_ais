#!/bin/bash
# usage: agyrun.sh <row> <label> <prompt-file> [agy args...]   (env UNIT_* knobs pass through; AGY_NOADD=1 to omit --add-dir; AGY_CWD to change cwd)
S=${E3S4_SCRATCH:?set E3S4_SCRATCH to your scratch folder}; F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}
row=$1; label=$2; pf=$3; shift 3
R=$F/$row; W=$R/cr/wt
node -e 'const fs=require("fs"); for(const t of fs.readFileSync(process.argv[1],"utf8").split("=====TURN=====\n").filter(x=>x.trim())) process.stdout.write(JSON.stringify({event:"user",message:{role:"user",content:t}})+"\n")' $pf > $R/c/stdin_$label.json
ADD=(--add-dir $W); [ -n "$AGY_NOADD" ] && ADD=()
$S/tools/snap.sh $row $S/out/agy_${label}_before.txt
: > $R/c/hooklog_$label.jsonl; cp $R/c/hooklog.jsonl $R/c/hooklog_prev.jsonl 2>/dev/null; : > $R/c/hooklog.jsonl
t0=$(date +%s)
UNIT_CWD=${AGY_CWD:-$R/c/agyhome} UNIT_HIDE=${UNIT_HIDE-1} UNIT_TMPFS=${UNIT_TMPFS-1} timeout -s KILL $(( ${AGY_TMO:-150} + 40 )) $S/tools/unit.sh $row agy coai-e3s4-agy-$label ${AGY_TMO:-150} -- agy --print= --input-format stream-json --output-format stream-json --model gemini-3.8-flash-low "${ADD[@]}" "$@" < $R/c/stdin_$label.json > $S/out/agy_$label.jsonl 2> $S/out/agy_$label.err
rc=$?; t1=$(date +%s)
cp $R/c/hooklog.jsonl $S/out/agy_${label}_hooklog.jsonl
$S/tools/ledger.sh agy "$label" $rc $((t1-t0)) "$(echo "$@" | cut -c1-100)"
$S/tools/snap.sh $row $S/out/agy_${label}_after.txt
echo "rc=$rc secs=$((t1-t0)) lines=$(wc -l < $S/out/agy_$label.jsonl) hooklines=$(wc -l < $S/out/agy_${label}_hooklog.jsonl)"; diff $S/out/agy_${label}_before.txt $S/out/agy_${label}_after.txt | head -20
