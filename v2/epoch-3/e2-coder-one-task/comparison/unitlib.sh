# Property lists of the two kinds of unit, as the product describes them (BoundaryUnit.cs / CoderUnit.cs of feat/v2-ep3-e2-coder), plus what the
# E3 s4 measure found a codex or agy coder's unit needs (the main checkout and the other worktrees HIDDEN, not read-only; /tmp a tmpfs).
# Sourced after env.sh. Nothing here reads or prints a credential.

ISOLATION=(ProtectSystem=strict ProtectHome=tmpfs TemporaryFileSystem=/run/user TemporaryFileSystem=/mnt TemporaryFileSystem=/dev/shm InaccessiblePaths=-/run/WSL)
HARDENING=(NoNewPrivileges=yes PrivateUsers=yes RestrictSUIDSGID=yes LockPersonality=yes ProtectKernelTunables=yes ProtectControlGroups=yes Nice=10 LimitCORE=0)
MEMORY_MB=6000
RESOLVER=$(readlink -f /etc/resolv.conf); case $RESOLVER in /mnt/*) ;; *) RESOLVER="";; esac
NUGET_SHARED=$HOME/.nuget/packages
NVM_NODE_BIN=$(ls -d "$HOME"/.nvm/versions/node/*/bin 2>/dev/null | head -1)
SYSTEM_PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin

# check_props <vendor> <host|private> <runtime-max-seconds>  -> array CHECK_PROPS  (BoundaryUnit.Properties)
check_props() {
  local v=$1 net=$2 rt=$3 W C M; W=$(job_tree "$v"); C=$(job_root "$v"); M=$CMP_CLONE
  CHECK_PROPS=("${ISOLATION[@]}" "BindPaths=$W" "BindReadOnlyPaths=$M" "BindReadOnlyPaths=$W/.git"
    "BindReadOnlyPaths=$(job_mounts "$v")/m0:$W/.agents/conventions"
    "BindPaths=$C" "BindPaths=$C/tmp:/tmp" "BindPaths=$C/vartmp:/var/tmp")
  [ -d "$NUGET_SHARED" ] && CHECK_PROPS+=("BindReadOnlyPaths=$NUGET_SHARED")
  [ -n "$NVM_NODE_BIN" ] && CHECK_PROPS+=("BindReadOnlyPaths=$HOME/.nvm")
  if [ "$net" = host ] && [ -n "$RESOLVER" ]; then CHECK_PROPS+=("BindReadOnlyPaths=$RESOLVER"); fi
  CHECK_PROPS+=("${HARDENING[@]}")
  if [ "$net" = private ]; then CHECK_PROPS+=(PrivateNetwork=yes); else CHECK_PROPS+=(PrivateNetwork=no); fi
  CHECK_PROPS+=("RuntimeMaxSec=$rt" "MemoryMax=${MEMORY_MB}M" MemorySwapMax=0)
}

# check_env <vendor> -> array CHECK_ENV  (BoundaryUnit.Environment + the dotnet switches of CheckRunner)
check_env() {
  local C; C=$(job_root "$1")
  CHECK_ENV=("PATH=$SYSTEM_PATH${NVM_NODE_BIN:+:$NVM_NODE_BIN}" "HOME=$C/home" "DOTNET_CLI_HOME=$C/dotnet-home" "NUGET_PACKAGES=$C/nuget-packages"
    "npm_config_cache=$C/npm-cache" TMPDIR=/tmp LANG=C.UTF-8 TERM=dumb GIT_TERMINAL_PROMPT=0)
  [ -d "$NUGET_SHARED" ] && CHECK_ENV+=("NUGET_FALLBACK_PACKAGES=$NUGET_SHARED")
  CHECK_ENV+=(NO_COLOR=1 DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1)
}

# run_check_unit <vendor> <host|private> <part> <timeout-s> -- cmd...   (CheckBoundary.RunAsync: one named user unit, env -i inside)
run_check_unit() {
  local v=$1 net=$2 part=$3 tmo=$4; shift 5
  check_props "$v" "$net" $((tmo+60)); check_env "$v"
  local pa=(); for p in "${CHECK_PROPS[@]}"; do pa+=(-p "$p"); done
  systemd-run --user --wait --pipe --collect --quiet --unit "cmp3-check-$v-$part" --working-directory "$(job_tree "$v")" "${pa[@]}" -- /usr/bin/env -i "${CHECK_ENV[@]}" "$@"
}
