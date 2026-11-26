#!/bin/bash
# usage: arg0watch.sh <out> [n]  : while a call runs, lists what is under CODEX_HOME/tmp/arg0-linux (name, type, link target); no content
S=${E3S4_SCRATCH:?}; out=$1; n=${2:-300}; : > $out
for i in $(seq 1 $n); do for d in $S/copies/codex/home/tmp/arg0-linux/*/; do [ -d "$d" ] && find "$d" -mindepth 1 -printf '%P %y %l\n' 2>/dev/null; done | sort -u >> $out; sleep 0.3; done
