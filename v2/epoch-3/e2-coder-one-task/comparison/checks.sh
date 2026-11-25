#!/bin/bash
# usage: checks.sh <vendor> <round>   One round of the repository's checks (.coai/toolchain.yaml AT THE BASE) on the worktree's HEAD, each step in its own
# check-unit replica (unitlib.sh): build with the host network, test with a private one, the build first and the first failing step ends the round.
# The hidden-path probe runs first and stops everything when a sign-in copy would be visible. Writes $CMP_OUT/checks-<vendor>-r<round>/.
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
v=$1; r=$2; W=$(job_tree "$v"); L=$CMP_OUT/checks-$v-r$r; mkdir -p "$L"
"$(dirname "$0")/probe-hidden.sh" "$v" > "$L/probe.txt" 2>&1; prc=$?
cat "$L/probe.txt" | tail -3
[ $prc -eq 0 ] || { echo "STOP: probe rc=$prc, no check was run"; exit 90; }
head=$(git -C "$W" rev-parse HEAD); before=$(git -C "$W" status --porcelain=v1 -z --untracked-files=all | tr '\0' '\n')
# the toolchain at the base commit (never the worktree's file)
git -C "$CMP_CLONE" show "$CMP_BASE_SHA:.coai/toolchain.yaml" > "$L/toolchain.base.yaml"
state=green; : > "$L/steps.tsv"
step() { # <n> <name> <net> <timeout-s> -- argv...
  local n=$1 name=$2 net=$3 tmo=$4; shift 5
  local t0 t1 rc; t0=$(date +%s.%N)
  run_check_unit "$v" "$net" "s$n" "$tmo" -- "$@" > "$L/0$n-$name.log" 2>&1; rc=$?
  t1=$(date +%s.%N)
  printf '%s\t%s\t%s\t%s\t%.1f\n' "$n" "$name" "$net" "$rc" "$(echo "$t1 - $t0" | bc)" | tee -a "$L/steps.tsv"
  return $rc
}
step 1 build host 900 -- dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:2 || state=failed
if [ $state = green ]; then step 2 test private 1800 -- "$W/src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests" || state=failed; fi
after=$(git -C "$W" status --porcelain=v1 -z --untracked-files=all | tr '\0' '\n'); [ "$(git -C "$W" rev-parse HEAD)" = "$head" ] || state=tree_changed
[ "$before" = "$after" ] || { state=tree_changed; echo "tree changed by the checks:"; diff <(echo "$before") <(echo "$after") | head; }
echo "$state" > "$L/state.txt"; echo "ROUND $r head=$head state=$state"
[ $state = green ]
