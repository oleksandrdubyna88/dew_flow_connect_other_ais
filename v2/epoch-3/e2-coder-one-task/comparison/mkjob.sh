#!/bin/bash
# usage: mkjob.sh <vendor: codex|agy>   makes the NEW worktree of the clone at the base (branch <vendor>-t3), the submodule mount, and the job's folders.
set -euo pipefail
. "$(dirname "$0")/env.sh"
v=$1; W=$(job_tree "$v"); C=$(job_root "$v"); MT=$(job_mounts "$v")
mkdir -p "$CMP_CR" "$C" "$MT"
# testsToDelete-exempt: raw-worktree — a manual comparison harness, not a test: the worktree is the run's own evidence and the harness removes it at its cleanup
git -C "$CMP_CLONE" worktree add -b "$v-t3" "$W" "$CMP_BASE_SHA" >/dev/null
# the submodule's content AT THE BASE PIN, offline, from the clone's module store (what CoderSubmodules does): a sibling of the job folder, bound read-only by the units
pin=$(git -C "$CMP_CLONE" ls-tree "$CMP_BASE_SHA" .agents/conventions | awk '{print $3}')
store=$(git -C "$CMP_CLONE" rev-parse --git-path "modules/.claude/rules/shared"); store=$(cd "$CMP_CLONE" && realpath "$store")
mkdir -p "$MT/m0"; git --git-dir="$store" archive "$pin" | tar -x -C "$MT/m0"
mkdir -p "$C"/{home,tmp,vartmp,nuget-packages,npm-cache,dotnet-home,coder/home,coder/tmp,coder/vartmp,coder/guard,coder/home/.codex,coder/home/.gemini}
chmod -R go-rwx "$C" "$MT"
echo "worktree $W at $(git -C "$W" rev-parse HEAD) branch $(git -C "$W" branch --show-current); mount pin $pin ($(find "$MT/m0" -type f | wc -l) files); job folder $C"
