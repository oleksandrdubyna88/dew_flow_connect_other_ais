#!/bin/bash
# usage: redcheck.sh <vendor>   The new case test against the BASE code: a second worktree (job "<vendor>red") at the base, only the vendor's tests file copied in
# from the vendor's HEAD; build (host network) and run the test class (private network), both in the check-unit replica. The class's result is the answer.
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
v=$1; r=${v}red; TESTS=src_mcp/tests/AWorktreePathIsComparedByItsRealPathTests.cs
[ -d "$(job_tree "$r")" ] || "$(dirname "$0")/mkjob.sh" "$r" >/dev/null
git -C "$(job_tree "$v")" show "HEAD:$TESTS" > "$(job_tree "$r")/$TESTS"
git -C "$(job_tree "$r")" status --short
L=$CMP_OUT/redcheck-$v; mkdir -p "$L"
run_check_unit "$r" host b1 900 -- dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:2 > "$L/build.log" 2>&1; echo "build rc=$?"; tail -3 "$L/build.log"
run_check_unit "$r" private t1 600 -- "$(job_tree "$r")/src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests" --filter-class "$2" > "$L/test.log" 2>&1; echo "test rc=$?"
grep -E "failed|passed|Passed|Failed|total|Total" "$L/test.log" | tail -12
