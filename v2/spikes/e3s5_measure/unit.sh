#!/bin/bash
# usage: unit.sh <row> <vendor: codex|agy|none> <unitname> <timeout-s> -- <command and args...>
# Runs <command> inside an E2-style coder unit. Fixtures: $F/<row>/{main,cr/wt,cr/other,c}.
# knobs: UNIT_HIDE=1 (main checkout hidden except its .git; other jobs' worktrees hidden), UNIT_TMPFS=1 (/tmp is a tmpfs, not a bind of the job's tmp),
#        UNIT_NET (yes|no => PrivateNetwork), UNIT_SECRETS (names passed by NAME), UNIT_ENVS (extra NAME=value for env -i), UNIT_CWD, UNIT_EXTRA (extra systemd-run words), UNIT_OMIT (exact -p words dropped)
F=${E3S4_FIXTURES:-$HOME/coai-e3s4-fx}; S=${E3S4_SCRATCH:?set E3S4_SCRATCH}
row=$1; vendor=$2; uname=$3; tmo=$4; shift 5
R=$F/$row; M=$R/main; CR=$R/cr; W=$CR/wt; C=$R/c
P=(ProtectSystem=strict ProtectHome=tmpfs TemporaryFileSystem=/run/user TemporaryFileSystem=/mnt TemporaryFileSystem=/dev/shm InaccessiblePaths=-/run/WSL)
if [ -n "$UNIT_RW" ]; then P+=(BindPaths=$M BindPaths=$CR)
elif [ -n "$UNIT_HIDE" ]; then P+=(TemporaryFileSystem=$M BindReadOnlyPaths=$M/.git TemporaryFileSystem=$CR BindPaths=$W BindReadOnlyPaths=$W/.git)
else P+=(BindReadOnlyPaths=$M BindReadOnlyPaths=$CR BindPaths=$W BindReadOnlyPaths=$W/.git); fi
P+=(BindPaths=$C BindPaths=$C/vartmp:/var/tmp BindReadOnlyPaths=/mnt/wsl/resolv.conf)
if [ -n "$UNIT_TMPFS" ]; then P+=(TemporaryFileSystem=/tmp); else P+=(BindPaths=$C/tmp:/tmp); fi
P+=(NoNewPrivileges=yes PrivateUsers=yes RestrictSUIDSGID=yes LockPersonality=yes ProtectKernelTunables=yes ProtectControlGroups=yes Nice=10
   PrivateNetwork=${UNIT_NET:-no} RuntimeMaxSec=$((tmo+60)) MemoryMax=4000M MemorySwapMax=0)
PROGDIR=""; ENVS=()   # UNIT_PROGDIR: the folder of the program to bind read-only instead of the installed one (agy only)
case $vendor in
 codex) PROGDIR=$(dirname "$(readlink -f $HOME/.local/bin/codex)"); mkdir -p $C/home/.codex
        P+=(BindPaths=${UNIT_CXHOME:-$S/copies/codex/home}:$C/home/.codex)
        ENVS+=(CODEX_HOME=$C/home/.codex CODEX_REFRESH_TOKEN_URL_OVERRIDE=http://127.0.0.1:9/oauth/token);;
 agy)   PROGDIR=${UNIT_PROGDIR:-$(dirname "$(readlink -f $HOME/.local/bin/agy)")}; mkdir -p $C/home/.gemini
        P+=(BindPaths=${UNIT_AGYHOME:-$S/copies/agy/hm/.gemini}:$C/home/.gemini);;
esac
[ -n "$PROGDIR" ] && P+=(BindReadOnlyPaths=$PROGDIR)
PA=(); for p in "${P[@]}"; do if [ -n "$UNIT_OMIT" ] && [[ " $UNIT_OMIT " == *" $p "* ]]; then continue; fi; PA+=(-p "$p"); done
SETENV=(); for n in $UNIT_SECRETS; do SETENV+=(--setenv=$n); done
exec systemd-run --user --wait --pipe --collect --quiet --unit "$uname" --working-directory "${UNIT_CWD:-$W}" "${PA[@]}" ${UNIT_EXTRA} "${SETENV[@]}" -- \
  /bin/bash -c 'exec /usr/bin/env -i PATH="$0" HOME="$1" TMPDIR=/tmp LANG=C.UTF-8 TERM=dumb NO_COLOR=1 GIT_TERMINAL_PROMPT=0 ${COAI_CANARY_ENV:+COAI_CANARY_ENV="$COAI_CANARY_ENV"} "${@:2}"' \
  "/usr/local/bin:/usr/bin:/bin${PROGDIR:+:$PROGDIR}" "$C/home" "${ENVS[@]}" $UNIT_ENVS "$@"
