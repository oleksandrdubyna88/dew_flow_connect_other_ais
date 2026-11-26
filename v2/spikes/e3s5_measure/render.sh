#!/bin/bash
# usage: render.sh <template> <row> [R-suffix-for-resume: 1]  -> stdout; replaces @M@ @W@ @C@ @CR@ @SUF@ and the secret file name
F=${E3S4_FIXTURES:?}; R=$F/$2; suf=${3:+_r}
sed -e "s#@M@#$R/main#g" -e "s#@W@#$R/cr/wt#g" -e "s#@C@#$R/c#g" -e "s#@CR@#$R/cr#g" -e "s#@SUF@#$suf#g" "$1"
