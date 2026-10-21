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
#   4. audits that PATH: an EMPTY or RELATIVE entry, or a directory that is world-writable or owned by neither uid 0 nor the user
#      running this script, at or BEFORE the directory a used tool (node, vibe, kimi, rg, fd) resolves to, refuses with exit 77 and
#      names the entry — a root-run step would otherwise run a stranger's program;
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
for TOOL in node vibe kimi rg fd; do
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
for ((J = 0; J <= LIMIT; J++)); do
  ENTRY="${ENTRIES[$J]}"; WHY=""
  if [ -z "$ENTRY" ]; then
    WHY="an empty entry (the current folder)"
  elif [ "${ENTRY#/}" = "$ENTRY" ]; then
    WHY="a relative entry"
  elif [ -d "$ENTRY" ]; then
    DIRSTAT="$(stat -c '%a %u' -- "$ENTRY")"
    if (( (8#${DIRSTAT% *} & 8#002) != 0 )); then
      WHY="world-writable (mode ${DIRSTAT% *})"
    elif [ "${DIRSTAT#* }" != 0 ] && [ "${DIRSTAT#* }" != "$ME" ]; then
      WHY="owned by uid ${DIRSTAT#* }"
    fi
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
