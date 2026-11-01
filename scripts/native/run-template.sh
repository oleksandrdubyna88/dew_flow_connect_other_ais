#!/bin/bash
# run-template.sh — the run script of ONE phase of a native-Linux series (plan: todo/PLAN_kimi_and_vibe_on_native_linux.md §6,
# DN11). It is a TEMPLATE: scripts/lib/native-run.mjs `renderRunScript` fills the three @…@ marks (a series folder and a phase that
# passed their patterns, and the probe command made from a probe file name and arguments that passed theirs) and the lead copies the
# result to <series>/run/<phase>.sh with scp. It is started BY ITS PATH, never as an inline `bash -c` string:
#   /usr/bin/nohup /usr/bin/setsid /bin/bash -- <series>/run/<phase>.sh
# (nohup makes SIGHUP ignored on entry, and bash cannot trap a signal that was ignored on entry — so the HUP trap below is the
# defence for a start WITHOUT nohup; with it, an abort ends the run by TERM, as abort.sh does.)
#
# Nothing of the host is written into this text: the host's PATH and node's absolute path are read from two DATA files that
# make-series.sh wrote, into quoted variables, and are only ever passed on as an environment value / a program path.
set -eu
export PATH=/usr/bin:/bin                                                     # this script's OWN helpers; the host PATH below is the probe's only
SERIES="$(cat '@SERIES@/SERIES_PATH')"                                        # the one literal, read once
PHASE='@PHASE@'
exec > "$SERIES/logs/$PHASE.log" 2>&1 < /dev/null
finish() { rc=$?; echo "$rc" > "$SERIES/logs/$PHASE.exit"; touch "$SERIES/logs/$PHASE.done"; }
trap finish EXIT
trap 'exit 143' TERM; trap 'exit 130' INT; trap 'exit 129' HUP              # a signal becomes an exit, so `finish` runs on an abort too
stat="$(cat /proc/$$/stat)"; set -- ${stat##*\) }                             # the fields after comm: $3 = pgrp (field 5), ${20} = start time (field 22)
echo "$$ $3 ${20}" > "$SERIES/logs/$PHASE.handle"                             # setsid made $$ the group leader; the abort checks all three
HERE="$(cd "$(dirname -- "$0")/.." && pwd -P)"
if [ "$HERE" != "$SERIES" ]; then echo "this script does not live in the series it names" >&2; exit 78; fi
NODE="$(cat "$SERIES/run/node.txt")"                                          # node's ABSOLUTE path, resolved once by make-series.sh
HOST_PATH="$(cat "$SERIES/run/path.txt")"                                     # the VS Code server's PATH value from make-series.sh, one line, as DATA
mkdir -p -m 700 -- "$SERIES/$PHASE"
cd "$SERIES/checkout"
/usr/bin/env -i HOME=/root USER=root LOGNAME=root SHELL=/bin/bash LANG=C.UTF-8 TMPDIR="$SERIES/tmp" PATH="$HOST_PATH" \
  "$NODE" @PROBE_COMMAND@ --scratch "$SERIES/$PHASE" &
wait "$!"                                                                     # bash defers a trap while a FOREGROUND child runs; `wait` does not
