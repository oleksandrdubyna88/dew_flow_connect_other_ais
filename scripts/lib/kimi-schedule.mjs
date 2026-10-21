// lib/kimi-schedule.mjs — the pure half of N-K2's abrupt-death arm in scripts/probe-kimi-tree-hold.mjs (DN5 item 4 of
// todo/PLAN_kimi_and_vibe_on_native_linux.md; cadence consult e9a9b20e, decision 2). Three things the probe must get right, each testable without a
// shim, a kimi or a process:
//   * the SIGNAL a killed arm sends — the `--ask-acp` shim handles SIGTERM (Program.cs:442-451 through StopSignals.Into) and its `finally` ends the agent's
//     tree, so a clean next launch after a TERM proves nothing about an ABANDONED survivor marker; only SIGKILL (`killed-hard`) is abrupt;
//   * the SCHEDULE — every killed arm's repetition is a PAIR, the kill and the very next launch (a `normal` one), nothing between them, which is where
//     KimiSurvivorGate's reconcile is observed;
//   * the ROWS — the survivor marker seen at the kill (a SIGKILL row counts only with one), each orphan's NEW parent, and the marker before and after
//     the next launch.
// Reads names and modification times of the lock folder, never a content.
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';

/** The arms that end the shim abruptly: the signal each sends and the process the probe waits for before sending it. */
export const KILL_ARMS = {
  killed: { signal: 'SIGTERM', after: 'kimi' },
  'killed-hard': { signal: 'SIGKILL', after: 'kimi' },
  'killed-rg': { signal: 'SIGTERM', after: 'rg' },
};

export const isKilledArm = (name) => name in KILL_ARMS;
export const killSignal = (name) => KILL_ARMS[name].signal;

/**
 * What runs, in order: repeat-outer, and each killed arm's repetition `<arm>-<n>` immediately followed by `<arm>-<n>-next`, a `normal` launch that
 * records what the kill left behind. Each entry is `{ arm, n, label, kind: 'arm' | 'next', after? }`; `after` names the kill a `next` follows.
 */
export function buildSchedule(armNames, repeats) {
  return Array.from({ length: repeats }, (_, i) => i + 1).flatMap((n) => armNames.flatMap((arm) => {
    const label = `${arm}-${n}`;
    const first = { arm, n, label, kind: 'arm' };
    return isKilledArm(arm) ? [first, { arm: 'normal', n, label: `${label}-next`, kind: 'next', after: label }] : [first];
  }));
}

/** A credential slot's / lock key's hex is not recorded (DN9): runs of 8+ hex digits become `<hex>`. */
const redacted = (name) => name.replaceAll(/[0-9a-f]{8,}/giu, '<hex>');

/** The survivor markers (`<key>.survivors.json`, KimiCredentialsLock.MarkerPathFor) in a lock folder: name (hex redacted) and mtime — no content. */
export function survivorMarkers(locksDir) {
  if (!existsSync(locksDir)) return [];
  return readdirSync(locksDir).filter((n) => n.endsWith('.survivors.json')).sort((a, b) => a.localeCompare(b))
    .map((n) => ({ name: redacted(n), mtimeMs: Math.round(statSync(join(locksDir, n)).mtimeMs) }));
}

/** `survivor` (`{ pid, comm, name }`, as seen while it ran) with its NEW parent, read from the `listing` taken after the kill. */
export function orphanRecord(survivor, listing) {
  const now = listing.find((p) => p.pid === survivor.pid);
  if (!now) return { pid: survivor.pid, comm: survivor.comm, exe: survivor.name, newParent: null, gone: true };
  const parent = listing.find((p) => p.pid === now.ppid);
  return {
    pid: survivor.pid, comm: survivor.comm, exe: survivor.name,
    newParent: { pid: now.ppid, comm: parent?.comm ?? '', exe: parent?.name ?? '' },
  };
}

/** Whether a row counts: only a SIGKILL needs the evidence that there was a marker to abandon. */
function evidenceOf(arm, markers) {
  if (arm !== 'killed-hard') return 'n/a: SIGTERM — the shim handles it and ends the agent itself';
  return markers.length > 0
    ? 'counts: a survivor marker existed at the kill'
    : 'not evidence: no *.survivors.json existed at the kill, so nothing was abandoned';
}

/** The kill's part of a row: the signal, the marker seen just before it, whether that makes the row count, and every survivor with its new parent. */
export function killRecord({ arm, markers, survivors, listing }) {
  return {
    signal: killSignal(arm),
    markerAtKill: markers,
    evidence: evidenceOf(arm, markers),
    orphans: survivors.map((s) => orphanRecord(s, listing)),
  };
}

/** The `-next` row's part: the marker before the launch, the shim's report, the marker after — and whether the marker it found is gone. */
export function nextRow({ markerBefore, report, markerAfter }) {
  return { markerBefore, report, markerAfter, markerFoundAndGone: markerBefore.length > 0 && markerAfter.length === 0 };
}
