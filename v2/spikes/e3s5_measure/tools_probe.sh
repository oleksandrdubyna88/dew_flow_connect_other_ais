#!/bin/bash
# usage: tools_probe.sh <label> [extra codex flags...]   model-free: `codex debug prompt-input x` with the CX1 flags + extras; prints the tool names
S=${E3S4_SCRATCH:?}; l=$1; shift
DIS=$(cat $S/out/cx_disable_set.txt)
cd $S/h0/cwd
timeout 90 $S/tools/cx0.sh debug prompt-input $DIS -c 'approval_policy="never"' -c 'shell_environment_policy.inherit="core"' -c 'web_search="disabled"' "$@" x > $S/out/pi_$l.json 2> $S/out/pi_$l.err
rc=$?; echo "[$l] rc=$rc $*"; node $S/tools/toolnames.mjs $S/out/pi_$l.json 2>&1 | cut -c1-600
