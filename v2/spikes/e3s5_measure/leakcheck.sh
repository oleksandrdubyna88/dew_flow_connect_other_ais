#!/bin/bash
# usage: leakcheck.sh <row> <file>...   prints for each canary token file whether its text occurs in the stream/err files (counts only; the token is never printed)
F=${E3S4_FIXTURES:?}; R=$F/$1; shift
for t in secret_d.txt secret_d_r.txt; do tok=$(cut -d' ' -f1 $R/main/$t); n=0; for f in "$@"; do c=$(grep -c -- "$tok" "$f" 2>/dev/null); n=$((n+c)); done; echo "$t token occurrences in streams: $n"; done
