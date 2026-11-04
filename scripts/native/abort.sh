#!/bin/bash
# abort.sh <phase> — ends every process an owned phase of a series started, and nothing else (parent plan §6 "the abort", DN11).
# Copied into <series>/run/ and run by its path: `/bin/bash -- <series>/run/abort.sh <phase>`. It never ends anything by NAME
# (no pkill, no killall): only a (pid, start time) it proved to be the phase's own.
#
#   1. the phase's .handle (written by the run script: pid, process-group id, start time = field 22 of /proc/<pid>/stat). setsid made the
#      run script its own group leader, so the pid MUST equal the group id (and the group id is never 0 or 1) — a handle that says
#      otherwise names a group the run script never led, and nothing is signalled (exit 4). If the leader lives, its start time must
#      EQUAL the recorded one — else the pid was reused — and it must still lead that group (its current pgrp = the recorded one). If the
#      leader is gone, the group may still live (a group id is not reused while a member holds it), and every member must have started
#      at or after the recorded time — else the group id was reused and nothing is signalled;
#   2. the OWNED set is snapshotted BEFORE any signal: every process of the group, plus every descendant (by parent pid) of the leader
#      and of each member — which catches a child that called setsid and left the group while its parent still lived — each as
#      (pid, start time);
#   3. TERM to the GROUP, and TERM to each owned pid outside it (never twice to a group member: a second TERM can cut the run
#      script's own EXIT trap short, and it is what writes .done);
#   4. after the grace period (10 s) — and ONLY while the group still has a live member or a snapshotted (pid, start time) lives —
#      a second snapshot (union) and KILL to the group and to each owned pid still alive. The test is the group's and the set's
#      EXISTENCE, never the leader's: the leader may be gone while a child that ignores TERM lives on. A zombie is not alive;
#   5. it prints what it signalled and what, if anything, survived. Exit 0: nothing owned is left. 3: something survived. 4: the
#      handle did not prove the processes ours — nothing was signalled. 65: bad phase. 78: this place is wrong.
#
# What it cannot find: a process that left the group AND lost its parent before the snapshot (a double-forked daemon). No probe
# here daemonizes (parent plan §6).
# Defence in depth (the lead's own `stat` checks run first): it also refuses a series folder not owned by the expected uid or group- or world-writable, and a series ROOT not
# owned by it or group- or world-writable without the sticky bit.
# Test seams (the shipped defaults are /tmp, 10 and 0): COAI_NATIVE_SERIES_ROOT, COAI_NATIVE_ABORT_GRACE (seconds), COAI_NATIVE_SERIES_OWNER_UID.
# The series is the ONE BOUNDED EXCEPTION of the test-leftovers family (E5 S4): no test-leftovers prefix, no owner, no claim — see make-series.sh.
set -eu
export PATH=/usr/bin:/bin
export LC_ALL=C                                                               # the bracket ranges of the shape tests below are locale-dependent ([0-9a-f] in a UTF-8 locale can match more)

SERIES_ROOT="${COAI_NATIVE_SERIES_ROOT:-/tmp}"
GRACE="${COAI_NATIVE_ABORT_GRACE:-10}"
OWNER_UID="${COAI_NATIVE_SERIES_OWNER_UID:-0}"
SHAPE='native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}'

refuse() { printf 'refused: %s\n' "$*" >&2; exit 78; }

PHASE="${1:-}"
if ! [[ $PHASE =~ ^[a-z0-9-]{1,32}$ ]]; then echo "usage: abort.sh <phase>  (phase: [a-z0-9-]{1,32})" >&2; exit 65; fi
if ! [[ $GRACE =~ ^[0-9]{1,3}$ ]]; then echo "the grace period must be whole seconds" >&2; exit 65; fi
case "$SERIES_ROOT" in /*) ;; *) refuse "the series root '$SERIES_ROOT' is not absolute" ;; esac

SERIES="$(cd -- "$(dirname -- "$0")/.." && pwd -P)"
NAME="${SERIES#"$SERIES_ROOT"/}"
if [ "$NAME" = "$SERIES" ]; then refuse "'$SERIES' is not under $SERIES_ROOT"; fi
[[ $NAME =~ ^${SHAPE}$ ]] || refuse "'$NAME' does not match $SHAPE"
if [ "$(cat -- "$SERIES/SERIES_PATH" 2>/dev/null)" != "$SERIES" ]; then refuse "SERIES_PATH does not name '$SERIES'"; fi
if ! [[ $OWNER_UID =~ ^[0-9]{1,10}$ ]]; then refuse "COAI_NATIVE_SERIES_OWNER_UID '$OWNER_UID' is not a uid"; fi
# The seam exists so the tests can run as their own user. As root it would make every check below accept a folder of THAT user: a root run never takes it.
if [ "$(id -u)" = 0 ] && [ "$OWNER_UID" != 0 ]; then refuse "running as root with COAI_NATIVE_SERIES_OWNER_UID=$OWNER_UID: the test seam is not for a root run"; fi
FACTS="$(stat -c '%u %a' -- "$SERIES")"
if [ "${FACTS% *}" != "$OWNER_UID" ]; then refuse "'$SERIES' is owned by uid ${FACTS% *}, not by uid $OWNER_UID"; fi
if (( (8#${FACTS#* } & 8#022) != 0 )); then refuse "'$SERIES' is mode ${FACTS#* }: group- or world-writable"; fi
ROOT_FACTS="$(stat -c '%u %a' -- "$SERIES_ROOT")"
if [ "${ROOT_FACTS% *}" != "$OWNER_UID" ]; then refuse "the series root '$SERIES_ROOT' is owned by uid ${ROOT_FACTS% *}, not by uid $OWNER_UID"; fi
if (( (8#${ROOT_FACTS#* } & 8#022) != 0 && (8#${ROOT_FACTS#* } & 8#1000) == 0 )); then refuse "the series root '$SERIES_ROOT' is group- or world-writable (mode ${ROOT_FACTS#* }) without the sticky bit"; fi

HANDLE="$SERIES/logs/$PHASE.handle"
if [ ! -f "$HANDLE" ]; then echo "abort $PHASE: no handle — the run script never started, nothing to end"; exit 0; fi
read -r PID PGID START < "$HANDLE" || true
for NUMBER in "${PID:-}" "${PGID:-}" "${START:-}"; do
  if ! [[ $NUMBER =~ ^[0-9]+$ ]]; then echo "abort $PHASE: the handle does not hold three integers — nothing signalled" >&2; exit 4; fi
done
if [ "$PGID" -le 1 ]; then echo "abort $PHASE: a process-group id of $PGID is never ours — nothing signalled" >&2; exit 4; fi
if [ "$PID" != "$PGID" ]; then echo "abort $PHASE: the handle's pid $PID is not its process-group id $PGID — the run script leads its own group; nothing signalled" >&2; exit 4; fi

# An associative array that is declared and NEVER assigned is "unbound" to `set -u` on bash 5.3 (`${#OWNED[@]}` then aborts the script), and
# the owned set CAN be empty: every member of the group can end between the listing and the snapshot. So both owned tables are assigned empty at once.
declare -A PPID_OF PGRP_OF START_OF STATE_OF OWNED OWNED_GROUP
OWNED=(); OWNED_GROUP=()

# The keys of an associative array, one per line; an empty table gives one empty line, which the word splitting of `$(keys_of ...)` drops.
keys_of() { local -n _table="$1"; printf '%s\n' "${!_table[@]}"; }

# One pass over /proc into the four tables. A process that ends between the listing and its read is skipped.
scan() {
  PPID_OF=(); PGRP_OF=(); START_OF=(); STATE_OF=()
  local file line rest pid
  local -a F
  for file in /proc/[0-9]*/stat; do
    line=""
    { read -r line < "$file"; } 2>/dev/null || continue
    pid="${file#/proc/}"; pid="${pid%/stat}"
    rest="${line##*\) }"
    F=($rest)
    if [ "${#F[@]}" -lt 20 ]; then continue; fi
    STATE_OF[$pid]="${F[0]}"; PPID_OF[$pid]="${F[1]}"; PGRP_OF[$pid]="${F[2]}"; START_OF[$pid]="${F[19]}"
  done
}

# Is (pid, start time) still the same live, non-zombie process?
alive() {
  local line rest
  local -a F
  { read -r line < "/proc/$1/stat"; } 2>/dev/null || return 1
  rest="${line##*\) }"
  F=($rest)
  [ "${F[19]:-}" = "$2" ] && [ "${F[0]:-Z}" != Z ]
}

own() { OWNED["$1:${START_OF[$1]}"]="$1"; OWNED_GROUP["$1:${START_OF[$1]}"]="${PGRP_OF[$1]}"; }
is_owned() { [ -n "${START_OF[$1]:-}" ] && [ -n "${OWNED[$1:${START_OF[$1]}]+x}" ]; }

# Adds to the owned set (a union with what earlier snapshots found): the group's members, then descendants until nothing new appears.
snapshot() {
  scan
  local pid parent changed=1
  for pid in $(keys_of PGRP_OF); do
    if [ "${PGRP_OF[$pid]}" = "$PGID" ] && [ "$pid" != "$$" ]; then own "$pid"; fi
  done
  if [ -n "${START_OF[$PID]:-}" ] && [ "${START_OF[$PID]}" = "$START" ]; then own "$PID"; fi
  while [ "$changed" = 1 ]; do
    changed=0
    for pid in $(keys_of PPID_OF); do
      parent="${PPID_OF[$pid]}"
      if [ "$pid" != "$$" ] && ! is_owned "$pid" && is_owned "$parent"; then own "$pid"; changed=1; fi
    done
  done
}

# Live (non-zombie) members of the group. `group_members_all` is every one; `group_members` keeps only those that started at or after the handle's
# start time — a member that is older cannot be the leader's, so a reused group id never makes a stranger ours.
group_members_all() { local pid; for pid in $(keys_of PGRP_OF); do if [ "${PGRP_OF[$pid]}" = "$PGID" ] && [ "${STATE_OF[$pid]}" != Z ]; then echo "$pid"; fi; done; }
group_members() { local pid; for pid in $(group_members_all); do if [ "${START_OF[$pid]}" -ge "$START" ]; then echo "$pid"; fi; done; }
owned_alive() { local key; for key in $(keys_of OWNED); do if alive "${OWNED[$key]}" "${key#*:}"; then echo "${OWNED[$key]}"; fi; done; }

# Step 1: identity.
scan
if [ -n "${START_OF[$PID]:-}" ]; then
  if [ "${START_OF[$PID]}" != "$START" ]; then
    echo "abort $PHASE: pid $PID is alive with start time ${START_OF[$PID]}, the handle says $START — the pid was reused; nothing signalled"
    exit 4
  fi
  if [ "${PGRP_OF[$PID]}" != "$PGID" ]; then
    echo "abort $PHASE: pid $PID is alive with the right start time but leads group ${PGRP_OF[$PID]}, not $PGID — nothing signalled"
    exit 4
  fi
  echo "abort $PHASE: leader $PID is alive and its start time matches"
else
  MEMBERS="$(group_members_all)"
  if [ -z "$MEMBERS" ]; then echo "abort $PHASE: the leader and its group are gone — nothing to end"; exit 0; fi
  for MEMBER in $MEMBERS; do
    if [ "${START_OF[$MEMBER]}" -lt "$START" ]; then
      echo "abort $PHASE: group $PGID holds pid $MEMBER, started before the recorded leader — not ours; nothing signalled"
      exit 4
    fi
  done
  echo "abort $PHASE: leader $PID is gone; group $PGID has members that all started at or after it"
fi

# Step 2: the owned set, before any signal.
snapshot
echo "abort $PHASE: owned set of ${#OWNED[@]} (pid:start) = ${!OWNED[*]}"

# Step 3: TERM the group, then each owned pid outside it.
if [ -n "$(group_members)" ]; then
  kill -TERM -- "-$PGID" 2>/dev/null || true
  echo "abort $PHASE: TERM to group $PGID"
fi
for KEY in $(keys_of OWNED); do
  if [ "${OWNED_GROUP[$KEY]}" != "$PGID" ] && alive "${OWNED[$KEY]}" "${KEY#*:}"; then
    kill -TERM -- "${OWNED[$KEY]}" 2>/dev/null || true
    echo "abort $PHASE: TERM to pid ${OWNED[$KEY]} (outside the group)"
  fi
done

# Step 4: wait out the grace period; escalate while the GROUP or the owned set still exists.
pending() { scan; [ -n "$(group_members)" ] || [ -n "$(owned_alive)" ]; }
DEADLINE=$((SECONDS + GRACE))
while pending && [ "$SECONDS" -lt "$DEADLINE" ]; do sleep 0.2; done
if pending; then
  snapshot
  if [ -n "$(group_members)" ]; then
    kill -KILL -- "-$PGID" 2>/dev/null || true
    echo "abort $PHASE: KILL to group $PGID (still alive after ${GRACE}s)"
  fi
  for KEY in $(keys_of OWNED); do
    if alive "${OWNED[$KEY]}" "${KEY#*:}"; then
      kill -KILL -- "${OWNED[$KEY]}" 2>/dev/null || true
      echo "abort $PHASE: KILL to pid ${OWNED[$KEY]}"
    fi
  done
  AFTER=$((SECONDS + 5))
  while pending && [ "$SECONDS" -lt "$AFTER" ]; do sleep 0.2; done
fi

# Step 5: what is left.
scan
LEFT="$(owned_alive) $(group_members)"
LEFT="$(printf '%s\n' $LEFT | sort -un | tr '\n' ' ')"
if [ -z "${LEFT// /}" ]; then echo "abort $PHASE: survived: none"; exit 0; fi
echo "abort $PHASE: survived: $LEFT"
exit 3
