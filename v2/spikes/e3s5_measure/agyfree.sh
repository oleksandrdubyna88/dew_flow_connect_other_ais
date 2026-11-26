#!/bin/bash
# usage: agyfree.sh <row> <label> [agy args...]  : an agy start with standard input closed (NO model turn when agy prints its init line first); inside the unit, cwd = the guard folder
S=${E3S4_SCRATCH:?}; F=${E3S4_FIXTURES:?}; row=$1; label=$2; shift 2; R=$F/$row; W=$R/cr/wt
UNIT_CWD=$R/c/agyhome UNIT_HIDE=1 UNIT_TMPFS=1 timeout -s KILL 100 $S/tools/unit.sh $row agy coai-e3s6-$label 40 -- agy --print= --input-format stream-json --output-format stream-json --model gemini-3.8-flash-low --add-dir $W "$@" < /dev/null > $S/out/free_$label.jsonl 2> $S/out/free_$label.err
rc=$?; echo "[$label] rc=$rc lines=$(wc -l < $S/out/free_$label.jsonl) err=$(wc -l < $S/out/free_$label.err) args: $*"
node -e 'for(const l of require("fs").readFileSync(process.argv[1],"utf8").split("\n").filter(Boolean)){try{const e=JSON.parse(l);if(e.event==="init")console.log("  init permission_mode="+e.init.permission_mode+" model="+e.init.model+" tools="+(e.init.tools||[]).length);else console.log("  event "+e.event)}catch{console.log("  raw "+l.slice(0,150))}}' $S/out/free_$label.jsonl; head -c 300 $S/out/free_$label.err
