#!/bin/bash
# usage: ledger.sh vendor label rc seconds note   (appends to calls.tsv)
S=${E3S4_SCRATCH:?set E3S4_SCRATCH to your scratch folder}
n=$(grep -c "^$1	" $S/out/calls.tsv 2>/dev/null); n=$((n+1))
printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$1" "$n" "$2" "$3" "$4" "$5" >> $S/out/calls.tsv
