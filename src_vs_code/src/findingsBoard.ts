import { resolve, sep } from 'node:path';
import type { NotificationRecord } from './notifications';
import { type PathFinding, type Presence, isAboutAMissingPath, missingTitle } from './pathFindings';
import { DATA_DIRECTORY } from './pathSettings';
import { finishedInTime } from './withinTheClock';

/**
 * What this window knows about its paths right now — the findings the host last published — and the ONE storage-write
 * policy built on them (todo/PLAN_paths_per_side.md E5.0, decided after the gate's cadence consultant verified the
 * trap on E1: `notify` → `recordNotification` → `appendLine` → `mkdir(dirname(path), { recursive: true })`, so the notice
 * about a missing NAMED data directory would itself create it, and hide the very condition it reports).
 *
 * <h2>The policy</h2>
 *
 * <p>While the named data root is missing, <b>nothing this window writes creates it</b>. Every writer that would make the
 * directory asks {@link refuseUnderMissingRoot} first and is refused with the finding's own sentence — the ledgers
 * (`jsonlLedger.ts`), the atomic file write (`atomicFile.ts`), the settings file and its lock (`extension.ts`), the probe
 * cache, the token file, the prompt overrides, the backups (`everyWriterOfTheDataDirectory.test.ts` derives the list and
 * holds each to a guard or a stated reason). A refused notification is a LOST one, counted into the ledger's own write
 * gap and written down by the first append that lands once the folder exists — except the notice about a missing path
 * itself, which is <b>held in memory</b> ({@link holdNotice}): the toast is shown, the notifications page shows the held
 * record for this window run, and no ledger is written. Why not the default location's ledger: that is not where this
 * window's notifications page reads, a row there would be invisible AND split the history across two folders; the notice
 * is re-raised at every window start while the condition holds (D3), so persistence adds nothing a person needs; and
 * writing nothing is the only choice that cannot touch a bare `/mnt/z` mount point.</p>
 *
 * <h2>Lifecycle and cap</h2>
 *
 * <p>At most ONE held notice per setting key — a new missing path for the same key replaces it — and a held notice is
 * cleared the moment its path changes or the folder is found again ({@link publishFindings} keeps only what is still
 * missing). The list is therefore bounded by the number of path settings in the registry, never by how often a person
 * retypes a path (E1's plan round).</p>
 *
 * <p>Module state, on `dataDir.ts`'s `useStorageSettings` precedent: there is one answer to "is this window's data root
 * missing" and the ledger gate, the page and the host all read it. `vscode`-free, so the pure half is tested here; the
 * host that publishes is `pathWatch.ts`.</p>
 */

/** One held notice: the setting key it is about and the record the ledger did not take. */
export interface HeldNotice {
  readonly key: string;
  readonly record: NotificationRecord;
}

let published: readonly PathFinding[] = [];
let held: readonly HeldNotice[] = [];
let listeners: readonly (() => void)[] = [];

/**
 * The first answer about the data root, which the ledger gate waits for before it decides — resolved from the start,
 * so a window with no host watch (a test) is never held, and bounded ({@link SETTLE_CEILING_MS}) so a probe that never
 * answers cannot hold every toast of the window.
 */
let settling: Promise<unknown> = Promise.resolve();

/**
 * Whether the first data-root answer has landed (or nothing was ever expected): a writer that can decide synchronously —
 * a render's spawn, which must start in the same turn — asks this before it waits, so the wait is only ever real.
 */
let known = true;

/** How long the ledger gate waits for the first data-root answer at most — the probe's own deadline, with room. */
export const SETTLE_CEILING_MS = 10_000;

/**
 * The named root the disk last SAID is absent — the storage guard's own state, kept apart from the published findings.
 *
 * <p>Why apart (the risk consultation of story 5.7, item D, measured RED): a settings change drops the disk's answers and
 * asks again, so the data root reads `pending` — no finding — until the new answer lands, and a probe past its deadline
 * reads `unknown`, no finding either. A guard that read only the findings let a ledger append or the heartbeat create the
 * very folder the notice says is missing in that gap. So an absence is kept until a probe CONFIRMS the root is there, or
 * the setting names another root; a pending or unknown answer never lifts it.</p>
 */
let absentRoot: PathFinding | undefined;

/**
 * The watch's answer about the named data root, on every refresh: `here` lifts a kept absence, another root (or none)
 * drops it, `pending` and `unknown` leave it as it is. Asked BEFORE {@link publishFindings}, which keeps a new absence.
 */
export function noteDataRoot(root: string | undefined, presence: Presence): void {
  if (absentRoot === undefined) {
    return;
  }
  if (root !== absentRoot.value || presence === 'here') {
    absentRoot = undefined;
  }
}

/** The host has decided what it knows: the page reads it, the gate reads it, held notices still missing stay held. */
export function publishFindings(findings: readonly PathFinding[]): void {
  published = findings;
  absentRoot = missingDataRoot() ?? absentRoot;
  held = stillHeld(held, findings);
  for (const listener of listeners) {
    listener();
  }
}

export function currentFindings(): readonly PathFinding[] {
  return published;
}

/** Told after every publish — the panel repaints from it. */
export function onFindings(listener: () => void): void {
  listeners = [...listeners, listener];
}

/** The host says which probe answers the data-root question first; the gate waits for it (bounded) before deciding. */
export function expectDataRoot(answer: Promise<unknown>): void {
  known = false;
  settling = answer.then(() => { known = true; }, () => { known = true; });
}

/** Resolved once the data root has been asked about (or the ceiling passed) — what `recordNotification` awaits. */
export async function dataRootSettled(): Promise<void> {
  await finishedInTime(settling, SETTLE_CEILING_MS);
  known = true;
}

/** Whether the board has its first data-root answer — true when no host watch ever asked for one (a test). */
export function dataRootKnown(): boolean {
  return known;
}

/** The finding that THIS window's named data root is missing, or nothing — the default location is never one (D3). */
export function missingDataRoot(): PathFinding | undefined {
  return published.find((one) => one.key === DATA_DIRECTORY.id && one.state === 'missing');
}

/**
 * The named data root the storage guard refuses: missing now, or said absent and not yet CONFIRMED back — a settings
 * change's re-ask (`pending`) and a probe past its deadline (`unknown`) keep it ({@link noteDataRoot}).
 */
export function absentDataRoot(): PathFinding | undefined {
  return missingDataRoot() ?? absentRoot;
}

/** The refused data root when `path` is at or under it — the write that would create it — or nothing. */
export function refusedDataRoot(path: string): PathFinding | undefined {
  const missing = absentDataRoot();

  return missing !== undefined && under(path, missing.value) ? missing : undefined;
}

/**
 * The guard every writer that makes the data directory calls before its `mkdir`: refused, with the finding's sentence,
 * when the write would create a missing named root. A caller's ordinary failure handling takes it from there.
 */
export function refuseUnderMissingRoot(path: string): void {
  const missing = refusedDataRoot(path);
  if (missing !== undefined) {
    throw missingRootRefusal(missing);
  }
}

/**
 * The guard for a writer that can WAIT: the first data-root answer first (bounded by {@link SETTLE_CEILING_MS}), then
 * the refusal. A write at activation runs beside the probe — the chat store's heartbeat, the probe cache — and a guard
 * that read the board before the disk had answered let the heartbeat create the missing root on E5's first run in a
 * real editor. Every async writer asks this one; only a caller that cannot wait asks {@link refuseUnderMissingRoot}.
 */
export async function refuseUnderMissingRootOnceSettled(path: string): Promise<void> {
  const missing = await refusedDataRootOnceSettled(path);
  if (missing !== undefined) {
    throw missingRootRefusal(missing);
  }
}

/** {@link refusedDataRoot}, once the first data-root answer has landed (bounded) — for a caller that answers rather than throws. */
export async function refusedDataRootOnceSettled(path: string): Promise<PathFinding | undefined> {
  await dataRootSettled();

  return refusedDataRoot(path);
}

/** The refusal a writer meets, in the finding's own words — one sentence for the guard and for a lost notification. */
export function missingRootRefusal(missing: PathFinding): Error {
  return new Error(`${missingTitle(missing)} Nothing is written under it until it exists.`);
}

/** Whether `path` is `root` or inside it — spelled as the resolver spells both, case folded where the platform folds it. */
function under(path: string, root: string): boolean {
  const child = comparable(path);
  const parent = comparable(root);

  return child === parent || child.startsWith(parent.endsWith(sep) ? parent : `${parent}${sep}`);
}

function comparable(path: string): string {
  const full = resolve(path);

  return process.platform === 'win32' ? full.toLowerCase() : full;
}

/** A notice the ledger would not take, kept for this window run — one per setting key, the newest replacing the older. */
export function holdNotice(record: NotificationRecord): void {
  held = withHeld(held, record);
}

/** The held notices, newest last — what the notifications page shows for this window run beside the ledger's rows. */
export function heldNotices(): readonly NotificationRecord[] {
  return held.map((one) => one.record);
}

/** The setting key a missing-path notice is about — the half of its subject before the value. */
export function keyOfSubject(subject: string | undefined): string {
  return (subject ?? '').split('|')[0] ?? '';
}

/** `list` with `record` held — replacing a notice already held for the same setting key, so a key is held once. */
export function withHeld(list: readonly HeldNotice[], record: NotificationRecord): readonly HeldNotice[] {
  const key = keyOfSubject(record.subject);

  return [...list.filter((one) => one.key !== key), { key, record }];
}

/** The held notices whose path is STILL missing in `findings` — a path that changed or came back releases its notice. */
export function stillHeld(list: readonly HeldNotice[], findings: readonly PathFinding[]): readonly HeldNotice[] {
  const missing = new Set(findings.filter((one) => one.state === 'missing').map((one) => `${one.key}|${one.value}`));

  return list.filter((one) => missing.has(one.record.subject ?? ''));
}

/** Whether a record is one the gate holds rather than loses. Re-exported so the gate's callers need one import. */
export { isAboutAMissingPath };

/** For a test: nothing published, nothing held, nothing awaited. */
export function forgetFindingsBoard(): void {
  published = [];
  absentRoot = undefined;
  held = [];
  listeners = [];
  settling = Promise.resolve();
  known = true;
}
