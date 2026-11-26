#!/bin/bash
# usage: coder-unit.sh <vendor> <part> <timeout-s> <stdin-file> <stdout-file> <stderr-file> -- argv...
# One coder launch in the coder unit: E2's CoderUnit properties (RESULTS_coder_proof.md unit-c1.json) plus what the E3 s4 measure found a codex/agy
# coder needs: the main checkout and the other worktrees HIDDEN (TemporaryFileSystem + only the worktree and the .git entries bound), /tmp a tmpfs,
# the vendor's program folder read-only, the job's vendor home (a COPY of the sign-in, scratch, never a bind of the real file) bound into the job's home.
# The coder gets NO ~/.nvm and NO check folders; the environment is env -i with an allow-list; nothing secret is in an argv.
. "$(dirname "$0")/env.sh"; . "$(dirname "$0")/unitlib.sh"
v=$1; part=$2; tmo=$3; fin=$4; fout=$5; ferr=$6; shift 7
W=$(job_tree "$v"); C=$(job_root "$v"); M=$CMP_CLONE
P=("${ISOLATION[@]}" "TemporaryFileSystem=$M" "BindReadOnlyPaths=$M/.git" "TemporaryFileSystem=$CMP_CR" "BindPaths=$W" "BindReadOnlyPaths=$W/.git"
   "BindReadOnlyPaths=$(job_mounts "$v")/m0:$W/.agents/conventions" "BindPaths=$C/coder" "TemporaryFileSystem=/tmp" "BindPaths=$C/coder/vartmp:/var/tmp")
ENVS=()
case $v in
 codex) PROG=$(dirname "$(readlink -f "$HOME/.local/bin/codex")")
        P+=("BindPaths=$CMP_SCRATCH/copies/codex/home:$C/coder/home/.codex" "BindReadOnlyPaths=$PROG")
        ENVS=("CODEX_HOME=$C/coder/home/.codex" "CODEX_REFRESH_TOKEN_URL_OVERRIDE=http://127.0.0.1:9/oauth/token") ; UPATH=$SYSTEM_PATH:$PROG; CWD=$W;;
 agy)   PROG=$CMP_BASE/prog/agy
        P+=("BindPaths=$CMP_SCRATCH/copies/agy/hm/.gemini:$C/coder/home/.gemini" "BindReadOnlyPaths=$CMP_SCRATCH/copies/agy/guard:$C/coder/guard" "BindReadOnlyPaths=$PROG")
        UPATH=$SYSTEM_PATH:$PROG; CWD=$C/coder/guard;;   # the working directory is the guard folder, never the worktree (G7)
esac
[ -n "$RESOLVER" ] && P+=("BindReadOnlyPaths=$RESOLVER")
P+=("${HARDENING[@]}" PrivateNetwork=no "RuntimeMaxSec=$((tmo+60))" "MemoryMax=${MEMORY_MB}M" MemorySwapMax=0)
printf '%s\n' "${P[@]}" | sed "s#$HOME#~#g" > "$CMP_OUT/coder-unit-props-$v-$part.txt"
PA=(); for p in "${P[@]}"; do PA+=(-p "$p"); done
exec systemd-run --user --wait --pipe --collect --quiet --unit "cmp3-coder-$v-$part" --working-directory "$CWD" "${PA[@]}" -- \
  /usr/bin/env -i "PATH=$UPATH" "HOME=$C/coder/home" TMPDIR=/tmp LANG=C.UTF-8 TERM=dumb NO_COLOR=1 GIT_TERMINAL_PROMPT=0 "${ENVS[@]}" "$@" < "$fin" > "$fout" 2> "$ferr"
