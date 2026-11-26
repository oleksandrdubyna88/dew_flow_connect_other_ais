#!/bin/bash
# usage: agymodels.sh <label> <agyhome-dir (the folder that holds .gemini)> : `agy models` in a unit with no repository (cwd = the job folder), prints rc, the number of models, stderr (first line); then the token file's fingerprints
S=${E3S4_SCRATCH:?}; F=${E3S4_FIXTURES:?}; l=$1; H=$2
UNIT_AGYHOME=$H/.gemini UNIT_CWD=$F/g5/c UNIT_HIDE=1 UNIT_TMPFS=1 timeout 100 $S/tools/unit.sh g5 agy coai-e3s6-m$l 60 -- agy models > $S/out/models_$l.txt 2> $S/out/models_$l.err; rc=$?
echo "[$l] rc=$rc models=$(grep -c . $S/out/models_$l.txt) stderr: $(head -c 200 $S/out/models_$l.err | tr '\n' ' ')"
node $S/tools/agyfp.mjs $H/.gemini/antigravity-cli/antigravity-oauth-token | node -e 'const j=JSON.parse(require("fs").readFileSync(0,"utf8"));console.log("   size",j.size,"keys",j.tokenKeys,"access_fp",j.access_fp,"refresh_fp",j.refresh_fp,"expiry",j.expiry)'
