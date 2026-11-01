#!/bin/bash
# make-series.sh — prepares ONE series folder on the native Linux host (todo/PLAN_kimi_and_vibe_on_native_linux.md §6, DN11, E1.S1).
#
# The lead makes the BARE folder first (`/bin/mkdir -m 700 -- <folder>`, no -p, so an existing folder fails), copies this file into
# it with scp and runs it by its path: `/bin/bash -- <folder>/make-series.sh`. It takes no argument and reads no host data into
# any command line — the host's PATH travels only as a data file.
#
# It, in order:
#   1. checks its OWN place: the folder it sits in matches the series shape below, is not a symlink, is mode 700 and is owned by the
#      user running it (root on the host);
#   2. makes checkout/ tmp/ run/ logs/ bin/ (mode 700) and writes SERIES_PATH — the only place the path is ever written; every later
#      step reads it from there;
#   3. writes run/path.txt: the VALUE of the `PATH=` entry of the VS Code server's environment (the server is the process whose exe
#      lies under ~/.vscode-server/) — that one variable and nothing else of the environment is kept. No server → exit 69, "Remote-SSH
#      must be connected";
#   4. audits that PATH: an EMPTY or RELATIVE entry, an absolute entry that is MISSING, or a directory that is world-writable,
#      group-writable (unless the group is root's) or owned by neither uid 0 nor the user running this script, at or BEFORE the directory
#      a used tool (node, vibe, kimi, rg, fd, git, python3) resolves to, refuses with exit 77 and names the entry — a root-run step
#      would otherwise run a stranger's program. A symlinked entry is judged twice and never refused for being a link (Ubuntu's
#      merged-usr /bin is one): its realpath TARGET, and the folder that HOLDS the link (whoever can write that folder can repoint it);
#   5. resolves node ONCE through that PATH, realpath, into run/node.txt — every root-run step runs THAT path, never `node` by PATH.
#
# Exit codes: 0 made; 69 no VS Code server; 77 PATH refused; 78 this place is wrong.
# Test seams (the shipped default is /tmp): COAI_NATIVE_SERIES_ROOT names the folder the series sits in (native.test.mjs sets it).
set -eu
umask 077
export PATH=/usr/bin:/bin

SERIES_ROOT="${COAI_NATIVE_SERIES_ROOT:-/tmp}"
SHAPE='testsToDelete-native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}'

refuse() { printf 'refused: %s\n' "$*" >&2; exit 78; }

case "$SERIES_ROOT" in /*) ;; *) refuse "the series root '$SERIES_ROOT' is not absolute" ;; esac
case "$SERIES_ROOT" in */) refuse "the series root '$SERIES_ROOT' ends with a slash" ;; esac

HERE="$(dirname -- "$0")"
if [ -L "$HERE" ]; then refuse "this script's folder is a symlink"; fi
SERIES="$(cd -- "$HERE" && pwd -P)"
NAME="${SERIES#"$SERIES_ROOT"/}"
if [ "$NAME" = "$SERIES" ]; then refuse "'$SERIES' is not under $SERIES_ROOT"; fi
[[ $NAME =~ ^${SHAPE}$ ]] || refuse "'$NAME' does not match $SHAPE"
STAT="$(stat -c '%a %u' -- "$SERIES")"
if [ "${STAT% *}" != 700 ]; then refuse "'$SERIES' is mode ${STAT% *}, not 700"; fi
if [ "${STAT#* }" != "$(id -u)" ]; then refuse "'$SERIES' is owned by uid ${STAT#* }, not by the user running this script"; fi
if [ -e "$SERIES/SERIES_PATH" ]; then refuse "'$SERIES' was made already"; fi

mkdir -m 700 -- "$SERIES/checkout" "$SERIES/tmp" "$SERIES/run" "$SERIES/logs" "$SERIES/bin"
printf '%s\n' "$SERIES" > "$SERIES/SERIES_PATH"

# 3. the VS Code server's PATH value — read as data, from the one variable, out of the first server process that has it.
SERVER_PATH=""
for exe in /proc/[0-9]*/exe; do
  pid="${exe#/proc/}"; pid="${pid%/exe}"
  target="$(readlink -- "$exe" 2>/dev/null)" || continue
  case "$target" in "${HOME:?}"/.vscode-server/*) ;; *) continue ;; esac
  while IFS= read -r -d '' pair; do
    case "$pair" in PATH=*) SERVER_PATH="${pair#PATH=}"; break ;; esac
  done 2>/dev/null < "/proc/$pid/environ" || true
  if [ -n "$SERVER_PATH" ]; then break; fi
done
if [ -z "$SERVER_PATH" ]; then
  echo "Remote-SSH must be connected: no VS Code server process with a PATH was found under $HOME/.vscode-server/" >&2
  exit 69
fi
case "$SERVER_PATH" in *$'\n'*) refuse "the VS Code server's PATH holds a newline" ;; esac
printf '%s\n' "$SERVER_PATH" > "$SERIES/run/path.txt"

# 4. the audit: split with empty entries kept ("a::b" has three; a trailing colon is an empty entry).
ENTRIES=(); REST="$SERVER_PATH:"
while [ -n "$REST" ]; do ENTRIES+=("${REST%%:*}"); REST="${REST#*:}"; done

LIMIT=-1; NODE_CANDIDATE=""
for TOOL in node vibe kimi rg fd git python3; do
  I=0
  for ENTRY in "${ENTRIES[@]}"; do
    CANDIDATE="${ENTRY:-.}/$TOOL"
    if [ -f "$CANDIDATE" ] && [ -x "$CANDIDATE" ]; then
      if [ "$I" -gt "$LIMIT" ]; then LIMIT=$I; fi
      if [ "$TOOL" = node ]; then NODE_CANDIDATE="$CANDIDATE"; fi
      break
    fi
    I=$((I + 1))
  done
done
if [ -z "$NODE_CANDIDATE" ]; then echo "refused: node is on no entry of the VS Code server's PATH" >&2; exit 78; fi

ME="$(id -u)"

# Why a directory cannot be trusted to hold a program a root-run step will run — or nothing, when it can. `stat -L` reads where a symlink POINTS.
# World-writable: anyone can plant a file. Group-writable: so can the group, unless it is root's own (gid 0). Owned by neither root nor the user
# running this script: the owner can plant one.
untrusted() {
  local facts mode owner group
  facts="$(stat -L -c '%a %u %g' -- "$1" 2>/dev/null)" || { echo "unreadable"; return 0; }
  read -r mode owner group <<< "$facts"
  if (( (8#$mode & 8#002) != 0 )); then
    echo "world-writable (mode $mode)"
  elif (( (8#$mode & 8#020) != 0 )) && [ "$group" != 0 ]; then
    echo "group-writable (mode $mode, group $group)"
  elif [ "$owner" != 0 ] && [ "$owner" != "$ME" ]; then
    echo "owned by uid $owner"
  fi
  return 0
}

# One absolute, existing PATH entry. A symlink is judged TWICE: where it points (the realpath target) and the folder that HOLDS the link,
# because whoever can write that folder can repoint the link later. A link is not refused for being a link — Ubuntu's merged-usr /bin is
# one, in a root-owned folder, pointing at a root-owned one.
entry_problem() {
  local plain="${1%/}" real why HOLDER=""
  plain="${plain:-/}"
  real="$(realpath -e -- "$1" 2>/dev/null)" || { echo "unresolvable"; return 0; }
  why="$(untrusted "$real")"
  if [ -n "$why" ]; then
    if [ "$real" = "$plain" ]; then echo "$why"; else echo "a link to $real, which is $why"; fi
    return 0
  fi
  if [ -L "$plain" ] || [ "$real" != "$plain" ]; then HOLDER="$(dirname -- "$plain")"; fi
  if [ -n "$HOLDER" ]; then
    why="$(untrusted "$HOLDER")"
    if [ -n "$why" ]; then echo "a link whose folder $HOLDER is $why"; fi
  fi
  return 0
}

for ((J = 0; J <= LIMIT; J++)); do
  ENTRY="${ENTRIES[$J]}"; WHY=""
  if [ -z "$ENTRY" ]; then
    WHY="an empty entry (= the current directory)"
  elif [ "${ENTRY#/}" = "$ENTRY" ]; then
    WHY="a relative entry"
  elif [ ! -d "$ENTRY" ]; then
    WHY="missing (no such directory)"
  else
    WHY="$(entry_problem "$ENTRY")"
  fi
  if [ -n "$WHY" ]; then
    printf 'refused: PATH entry %d %q is %s\n' "$J" "$ENTRY" "$WHY" >&2
    exit 77
  fi
done

# 5. node, once, absolute.
NODE_ABS="$(realpath -e -- "$NODE_CANDIDATE")"
printf '%s\n' "$NODE_ABS" > "$SERIES/run/node.txt"
printf 'series: %s\nnode: %s\nPATH entries: %d (audited up to %d)\n' "$SERIES" "$NODE_ABS" "${#ENTRIES[@]}" "$((LIMIT + 1))"
