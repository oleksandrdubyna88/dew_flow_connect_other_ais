#!/bin/bash
# usage: mkrow.sh <rowname>   builds fixtures under $F/<row>: main (git), cr/wt (worktree), cr/other (other job's worktree), c/ (job folder)
set -e
F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}; S=${E3S4_SCRATCH:?set E3S4_SCRATCH}
R=$F/$1; rm -rf "$R"; mkdir -p "$R/main" "$R/cr" "$R/c/home" "$R/c/tmp" "$R/c/vartmp"
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null
G="git -c user.name=t -c user.email=t@t -c init.defaultBranch=main -c commit.gpgsign=false"
cd "$R/main"; $G init -q .
echo CANARY > canary.txt; echo "TOKEN-$(head -c8 /dev/urandom | od -An -tx1 | tr -d ' \n')" > secret_d.txt; echo "TOKEN-$(head -c8 /dev/urandom | od -An -tx1 | tr -d ' \n')" > secret_d_r.txt
$G add -A; $G commit -q -m base
# testsToDelete-exempt: raw-worktree — a fixture of the s4 measure, run by hand and by no test: it lives in the row folder $R, which this script removes at its start
$G worktree add -q "$R/cr/wt" -b coder/t >/dev/null 2>&1
# testsToDelete-exempt: raw-worktree — the second fixture worktree of the same row folder $R (another job's), removed with it at the next start of this script
$G worktree add -q "$R/cr/other" -b coder/other >/dev/null 2>&1
echo ORIGINAL > "$R/cr/wt/a.txt"; echo ORIGINAL > "$R/cr/wt/a_r.txt"
echo OTHERJOB > "$R/cr/other/other_canary.txt"; echo BESIDE > "$R/cr/beside_canary.txt"
echo "{\"id\":\"$(echo $1)\"}" > "$R/info.json"
( cd "$R" && sha256sum main/canary.txt main/secret_d.txt main/secret_d_r.txt cr/other/other_canary.txt cr/beside_canary.txt | cut -c1-12,65- ) > "$R/hashes_before.txt"
echo built $R
