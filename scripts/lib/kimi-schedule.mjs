// lib/kimi-schedule.mjs — the pure half of N-K2's abrupt-death arms in scripts/probe-kimi-tree-hold.mjs (DN5 item 4 of
// todo/PLAN_kimi_and_vibe_on_native_linux.md; cadence consult e9a9b20e, decision 2). Things the probe must get right, each testable without a
// shim, a kimi or a process:
//   * the SIGNAL a killed arm sends — the `--ask-acp` shim handles SIGTERM (Program.cs:442-451 through StopSignals.Into) and its `finally` ends the agent's
//     tree, so a clean next launch after a TERM proves nothing about an ABANDONED survivor marker; only SIGKILL (`killed-hard`) is abrupt;
//   * the SCHEDULE — every killed arm's repetition is a PAIR, the kill and the very next launch (a `normal` one), nothing between them, which is where
//     KimiSurvivorGate's reconcile is observed; the LIVE variant (`killed-hard-live`) is a TRIPLE — the next launch starts while the killed arm's own
//     orphan still runs (the gate must refuse it), the third once the orphan has ended (the gate reconciles and clears);
//   * the ROWS — the survivor marker seen at the kill (a SIGKILL row counts only with one), each orphan's NEW parent, the marker before and after
//     the next launch, and the verdict of the live pair.
// Reads names and modification times of the lock folder, never a content.
import { existsSync, readdirSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { isKimi } from './proc.mjs';

/**
 * The arms that end the shim abruptly: the signal each sends and the process the probe waits for before sending it. `live` marks the arm that leaves the
 * killed arm's own orphan running into the next launch — it can put two refreshers on one sign-in if the product's gate fails, so it runs only when asked
 * for (`--allow-live-survivor`), on a host where the owner allowed that window.
 */
export const KILL_ARMS = {
  killed: { signal: 'SIGTERM', after: 'kimi' },
  'killed-hard': { signal: 'SIGKILL', after: 'kimi' },
  'killed-hard-live': { signal: 'SIGKILL', after: 'kimi', live: true },
  'killed-rg': { signal: 'SIGTERM', after: 'rg' },
};

export const isKilledArm = (name) => name in KILL_ARMS;
export const isLiveArm = (name) => KILL_ARMS[name]?.live === true;
export const killSignal = (name) => KILL_ARMS[name].signal;

/** The arms that run when none are named: every one but the live variant, which must be asked for. */
export const defaultArms = (names) => names.filter((name) => !isLiveArm(name));

/**
 * The sentence that refuses an arm list, or '' — the live arm needs `--allow-live-survivor` (a failure of the product's gate would put two refreshers
 * on one sign-in, so it runs only where the owner allowed the window) and Linux (it identifies the orphan by /proc pid and start time).
 */
export function armsRefusal(names, { allowLive, platform }) {
  if (!names.some(isLiveArm)) return '';
  if (!allowLive) return 'killed-hard-live launches the next turn while the killed arm\'s own kimi still runs; if the product\'s gate failed, two refreshers of one rotating token would sign the person out — pass --allow-live-survivor only on a host where the owner allowed that window';
  return platform === 'linux' ? '' : 'killed-hard-live names the orphan by its /proc pid and start time, so it runs on Linux only';
}

/**
 * What runs, in order: repeat-outer, and each killed arm's repetition `<arm>-<n>` immediately followed by `<arm>-<n>-next`, a `normal` launch that
 * records what the kill left behind; a live arm's by a third, `<arm>-<n>-after`, once the orphan has ended. Each entry is
 * `{ arm, n, label, kind: 'arm' | 'next' | 'next-live' | 'after-live', after? }`; `after` names the kill a follower follows.
 */
export function buildSchedule(armNames, repeats) {
  return Array.from({ length: repeats }, (_, i) => i + 1).flatMap((n) => armNames.flatMap((arm) => {
    const label = `${arm}-${n}`;
    const first = { arm, n, label, kind: 'arm' };
    const follower = (suffix, kind) => ({ arm: 'normal', n, label: `${label}-${suffix}`, kind, after: label });
    if (isLiveArm(arm)) return [first, follower('next', 'next-live'), follower('after', 'after-live')];
    return isKilledArm(arm) ? [first, follower('next', 'next')] : [first];
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

/** `survivor` (`{ pid, comm, name, startTime? }`, as seen while it ran) with its NEW parent, read from the `listing` taken after the kill. */
export function orphanRecord(survivor, listing) {
  const now = listing.find((p) => p.pid === survivor.pid);
  if (!now) return { pid: survivor.pid, comm: survivor.comm, exe: survivor.name, newParent: null, gone: true };
  const parent = listing.find((p) => p.pid === now.ppid);
  return {
    pid: survivor.pid, comm: survivor.comm, exe: survivor.name,
    ...(survivor.startTime ? { startTime: survivor.startTime } : {}),
    newParent: { pid: now.ppid, comm: parent?.comm ?? '', exe: parent?.name ?? '' },
  };
}

/** Whether a row counts: only a SIGKILL needs the evidence that there was a marker to abandon. */
function evidenceOf(arm, markers) {
  if (killSignal(arm) !== 'SIGKILL') return 'n/a: SIGTERM — the shim handles it and ends the agent itself';
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

/**
 * The killed arm's own kimi, as the identity the live variant leaves alone — `{ pid, name, startTime }` — or null: no kimi among the orphans, one that
 * already left the listing, or one whose start time could not be read (no identity, nothing to leave alone; the probe then records that the live
 * variant did not happen). A helper such as rg is never it.
 */
export function liveSurvivor(orphans) {
  const kimi = orphans.find((o) => !o.gone && o.startTime && isKimi({ name: o.exe, comm: o.comm }));
  return kimi ? { pid: kimi.pid, name: kimi.exe, startTime: kimi.startTime } : null;
}

/**
 * What the live `-next` launch showed, against what the cornerstone says: the product's gate refuses it Busy, NAMING the survivor, starts nothing, leaves
 * the marker standing and the survivor untouched. `not evidence` when the survivor had already ended before the launch (nothing was there to refuse);
 * `UNEXPECTED: …` for anything else, saying which part differed — the numbers are recorded either way.
 */
export function liveVerdict({ markerAfter, survivor, survivorAliveBefore, survivorAliveAfter, shimExit, report, text }) {
  if (!survivorAliveBefore) return 'not evidence: the survivor had already ended when the next launch started, so the gate had nothing to refuse';
  if (shimExit === 0 || report?.end === 'answered') return 'UNEXPECTED: the next launch started (it answered) while the survivor lived — two refreshers on one sign-in';
  if (!survivorAliveAfter) return 'UNEXPECTED: the survivor no longer ran after the next launch (the gate ended it, or it ended by itself) — read the refusal text and the marker';
  if (markerAfter.length === 0) return 'UNEXPECTED: the marker was cleared while its survivor lived';
  if (!text.includes(String(survivor.pid))) return `UNEXPECTED: the refusal does not name the survivor (pid ${survivor.pid})`;
  return 'as expected: refused Busy naming the survivor, nothing started, the marker stands, the survivor untouched';
}

/**
 * The words of a refused launch, out of the shim's one JSON report (`failure`, `reason`) or, when stdout is no report, its stderr — hex redacted (the lock
 * key is derived from the sign-in's identity) and capped at 1500 characters.
 */
export function refusalText(stdout, stderr) {
  let fromReport = '';
  try {
    const j = JSON.parse(String(stdout ?? '').trim());
    fromReport = [j.failure, j.reason].filter(Boolean).join(': ');
  } catch {
    fromReport = '';
  }
  return redacted(fromReport || String(stderr ?? '')).slice(0, 1500);
}
