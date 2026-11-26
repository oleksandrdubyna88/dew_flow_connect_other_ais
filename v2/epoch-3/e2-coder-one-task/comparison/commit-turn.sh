#!/bin/bash
# usage: commit-turn.sh <vendor> <message>   Commits the worktree's changes as ONE commit (as CoderCommits does: git add -A, commit --no-verify --no-gpg-sign). The identity is the clone's own.
. "$(dirname "$0")/env.sh"
v=$1; W=$(job_tree "$v")
git -C "$W" add -A && git -C "$W" commit --no-verify --no-gpg-sign -q -m "$2" && git -C "$W" rev-parse HEAD
git -C "$W" show --stat --format=%h%n%s HEAD | head -8
