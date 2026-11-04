#!/bin/bash
# remove-series.sh <folder> — removes ONE series folder by the path it recorded for itself (parent plan §6 "Clean-up", DN11).
# Copied into <series>/run/ and run by its path: `/bin/bash -- <series>/run/remove-series.sh <folder>`. The retyped-literal rule is
# withdrawn: a retyped path is a second copy that can differ from the one the series used. In order:
#
#   1. reads <folder>/SERIES_PATH and refuses (exit 78) unless it matches the series shape AND equals the folder it was given AND
#      equals the folder it was read from after realpath — a SERIES_PATH that says anything else ends the script before any rm;
#   2. lists every symlink in the folder (`find -type l`);
#   3. stats ~/.vibe/.env (lstat facts only — it is never opened) and lists ~/.kimi-code/credentials (names, the slot hex redacted);
#   4. `rm -rf -- "$SERIES"` — the checked variable, never a glob (GNU rm -r unlinks a symlink, it never follows it);
#   5. the same stat and listing again, and the verdict: identical, or DIFFERENT (exit 3).
#
# Everything it says goes to STDOUT, which the lead's ssh captures — never into the folder it removes. Step 4 removes this very
# file, and step 5 still runs: bash reads a script through a descriptor it already holds, and on Linux an unlinked file stays
# readable through one (the E1 plan round, finding 3, rejected on exactly that ground; native.test.mjs asserts step 5's output).
# Exit codes: 0 removed and the sign-ins identical; 3 removed but a sign-in fact changed; 65 usage; 78 this place is wrong.
# Defence in depth (the lead's own `stat` checks run first and are the ones that protect: this copy lives IN the folder it judges): it also refuses a folder that is not
# owned by the expected uid or is group- or world-writable, and a series ROOT that is not owned by that uid or is world-writable without the sticky bit.
# Test seams (the shipped defaults are /tmp and 0): COAI_NATIVE_SERIES_ROOT, COAI_NATIVE_SERIES_OWNER_UID (the uid the series, its root and its files must belong to;
# root on the host — the tests run as the CI user).
# THE SERIES IS THE ONE BOUNDED EXCEPTION of the test-leftovers family (E5 S4): no test-leftovers prefix, no owner segment, no claim; THIS script is the only thing
# that removes it (see make-series.sh). `ls -d <root>/native-*` finds a forgotten one.
set -eu
export PATH=/usr/bin:/bin
export LC_ALL=C                                                               # the bracket ranges of the shape tests below are locale-dependent ([0-9a-f] in a UTF-8 locale can match more)

SERIES_ROOT="${COAI_NATIVE_SERIES_ROOT:-/tmp}"
SHAPE='native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}'
OWNER_UID="${COAI_NATIVE_SERIES_OWNER_UID:-0}"
TAB=$'\t'

refuse() { printf 'refused: %s\n' "$*"; exit 78; }

FOLDER="${1:-}"
if [ -z "$FOLDER" ] || [ "$#" -ne 1 ]; then echo "usage: remove-series.sh <folder>"; exit 65; fi
case "$SERIES_ROOT" in /*) ;; *) refuse "the series root '$SERIES_ROOT' is not absolute" ;; esac

# Step 1.
if [ -L "$FOLDER" ] || [ ! -d "$FOLDER" ]; then refuse "'$FOLDER' is not a plain folder"; fi
if [ ! -f "$FOLDER/SERIES_PATH" ]; then refuse "'$FOLDER' has no SERIES_PATH"; fi
SERIES="$(cat -- "$FOLDER/SERIES_PATH")"
REAL="$(realpath -e -- "$FOLDER")"
NAME="${SERIES#"$SERIES_ROOT"/}"
if [ "$NAME" = "$SERIES" ]; then refuse "SERIES_PATH '$SERIES' is not under $SERIES_ROOT"; fi
[[ $NAME =~ ^${SHAPE}$ ]] || refuse "SERIES_PATH '$SERIES' does not match $SHAPE"
if [ "$SERIES" != "$FOLDER" ]; then refuse "SERIES_PATH says '$SERIES' but the folder given is '$FOLDER'"; fi
if [ "$SERIES" != "$REAL" ]; then refuse "SERIES_PATH says '$SERIES' but the folder it was read from is '$REAL'"; fi
# Defence in depth: who owns and who can write what `rm -rf` is about to remove.
if ! [[ $OWNER_UID =~ ^[0-9]{1,10}$ ]]; then refuse "COAI_NATIVE_SERIES_OWNER_UID '$OWNER_UID' is not a uid"; fi
FACTS="$(stat -c '%u %a' -- "$FOLDER")"
if [ "${FACTS% *}" != "$OWNER_UID" ]; then refuse "'$FOLDER' is owned by uid ${FACTS% *}, not by uid $OWNER_UID"; fi
if (( (8#${FACTS#* } & 8#022) != 0 )); then refuse "'$FOLDER' is mode ${FACTS#* }: group- or world-writable"; fi
ROOT_FACTS="$(stat -c '%u %a' -- "$SERIES_ROOT")"
if [ "${ROOT_FACTS% *}" != "$OWNER_UID" ]; then refuse "the series root '$SERIES_ROOT' is owned by uid ${ROOT_FACTS% *}, not by uid $OWNER_UID"; fi
if (( (8#${ROOT_FACTS#* } & 8#002) != 0 && (8#${ROOT_FACTS#* } & 8#1000) == 0 )); then refuse "the series root '$SERIES_ROOT' is world-writable (mode ${ROOT_FACTS#* }) without the sticky bit"; fi
echo "step 1: SERIES_PATH names this folder: $SERIES"

# Step 2.
echo "step 2: symlinks in the folder:"
find "$SERIES" -type l -printf '  %p -> %l\n'

# Steps 3 and 5 take the same reading: lines of "LABEL<TAB>facts", lstat facts only — nothing is opened.
signin_facts() {
  local env_file="$HOME/.vibe/.env" creds="$HOME/.kimi-code/credentials"
  if [ -e "$env_file" ] || [ -L "$env_file" ]; then
    stat -c ".env${TAB}%F size=%s mode=%a uid=%u mtime=%Y" -- "$env_file"
  else
    printf '.env%sabsent\n' "$TAB"
  fi
  if [ -d "$creds" ]; then
    find "$creds" -mindepth 1 -maxdepth 1 -printf "credentials/%f${TAB}%y size=%s mode=%m uid=%U mtime=%T@\n" | sort
  else
    printf 'credentials%sabsent\n' "$TAB"
  fi
}
# What is printed: the same lines with the slot's hex redacted in the LABEL (a hex run in the facts is a timestamp, not a slot).
show() {
  local label facts
  while IFS="$TAB" read -r label facts; do
    printf '  %s %s\n' "$(printf '%s' "$label" | sed -E 's/[0-9a-f]{8,}/<hex>/g')" "$facts"
  done
}
BEFORE="$(signin_facts)"
echo "step 3: sign-in facts before:"
printf '%s\n' "$BEFORE" | show

# Step 4 — this removes the script itself.
rm -rf -- "$SERIES"
echo "step 4: removed $SERIES"

# Step 5.
AFTER="$(signin_facts)"
echo "step 5: sign-in facts after:"
printf '%s\n' "$AFTER" | show
if [ -e "$SERIES" ]; then echo "step 5: the folder is STILL THERE"; exit 3; fi
echo "step 5: the folder is gone"
if [ "$BEFORE" != "$AFTER" ]; then echo "step 5: the sign-in facts are DIFFERENT"; exit 3; fi
echo "step 5: the sign-in facts are identical"
