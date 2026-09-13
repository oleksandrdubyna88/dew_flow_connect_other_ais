import { ConversationMeta, KEEP_FOR_MS, isSafeId } from './chatStore';

/**
 * What the conversation store retires, and when — every rule, and no disk.
 *
 * <p>Epic A moved conversations out of the memento and onto disk, and the memento's seven-day,
 * twenty-record cut went with it: until this story the store kept everything for ever, a debt A4
 * wrote down on purpose. This module is the judgement half of paying it. `chatStoreKeeper.ts` does
 * the things that need a directory and asks here for every decision; the same split
 * `chatStore.ts` has against `chatStoreFile.ts`, for the same reason — a rule that deletes by clock
 * must be a value a test can pin, never a branch inside I/O.</p>
 *
 * <h2>What is retired</h2>
 *
 * <p>A conversation whose record was last written more than {@link KEEP_FOR_MS} ago — ninety days —
 * both files. A `.tmp` older than {@link DEBRIS_AGE_MS}: the atomic write stages there, so a young one
 * may be in flight and an old one is a crash. A `.lock` older than the same hour, collected through
 * the store's own lock module and never by a rule of this file's (see below). A metadata file whose
 * record is gone — a row that would open onto nothing. A transcript with no metadata beside it,
 * QUARANTINED rather than deleted, and only after {@link ORPHAN_GRACE_MS}: it is the one rule here that
 * would destroy the sole copy of somebody's words, and a record is legitimately alone for the instant
 * between the store's two writes, so an unbounded rule deletes conversations that are being created.
 * A quarantined value older than ninety days. And a heartbeat nobody has refreshed.</p>
 *
 * <h2>Why liveness is announced, not assumed</h2>
 *
 * <p>The directory is shared by every window, so a window-scoped "skip what I have open" protects
 * nothing: window A leaves a conversation open and untouched, window B activates, sees no such id
 * among its own tabs, and deletes it. And `updatedAt` cannot be the signal, because A4 made a reload
 * deliberately NOT a use — a tab open for three months has an old timestamp and is a legitimate target
 * by age alone. So each window writes a small HEARTBEAT naming the conversations it holds, refreshed
 * every {@link HEARTBEAT_EVERY_MS} while it lives, and the sweep skips every id a heartbeat younger than
 * {@link HEARTBEAT_STALE_MS} names. It is the shape the orphan ledger uses for vendor processes
 * (`chatOrphans.ts`, one file per host, named for its pid), and a heartbeat nobody refreshed is collected
 * by the same sweep. A window that reloads is not a window that closed: its old heartbeat goes on
 * protecting what it held for the stale window, which is what covers the tabs the new host is still
 * restoring — so a heartbeat is never removed by its own writer, only aged out.</p>
 *
 * <h2>What this module does NOT decide</h2>
 *
 * <p>Whether a conversation is deleted. It NOMINATES one, by what the metadata said at the time of
 * the listing; the store then takes the conversation's lock, re-reads, and deletes only if the record
 * is still expired and unchanged (`ChatStoreFile.retireIfExpired`). Observing and then deleting is the
 * check-then-act that cost epic A two rounds, and a save landing in between would be destroyed. Nor
 * does it break locks: `chatStoreLock.ts` owns that rule with its own window and states its residual in
 * its own header, and a second breaking path with a different window would be two answers to one
 * question. A lock far older than that window is collected by CLAIMING through the store, which runs
 * the one breaking path there is.</p>
 *
 * <h2>One window sweeps, not six — a claim while it runs, a day's marker only once it has done its work</h2>
 *
 * <p>Six windows opening together is the ordinary case on this machine. One marker file in the store's
 * housekeeping directory carries two different things in turn. A CLAIM is written before the work, so
 * the windows opening beside this one stand down — but only for {@link SWEEP_CLAIM_MS}, as long as a
 * sweep can still be running. What holds for {@link SWEEP_EVERY_MS} is the FINISHED marker, and it is
 * written only by a sweep that walked its whole plan. So a survey that fails after the claim, or a
 * budget that runs out on a backlog, leaves a claim that ages out in minutes rather than a marker that
 * stands a day on the strength of work that was never done — the shape the code round asked for. Two
 * windows that read "due" in the same instant both claim and both sweep, and that is the accepted
 * residual: every deletion is re-checked under the conversation's lock, so the second finds nothing
 * left to do.</p>
 *
 * <p>Everything here is a value over a pinned clock. No `vscode`, no `node:fs`.</p>
 */

/** How old a `.tmp` or a `.lock` must be before it is debris rather than a write in flight: an hour. */
export const DEBRIS_AGE_MS = 60 * 60 * 1000;

/**
 * How long a transcript may stand with no metadata beside it before it is set aside: a day.
 *
 * <p>Long, on purpose. Between a save's two writes the gap is milliseconds; after a `partial` save it
 * lasts until the next push regenerates the index, which a live tab does on every change; and a tab
 * that was closed right after a `partial` is protected by its window's heartbeat for the stale window
 * and then by nothing. A day is generous against all three, and what waits at the end of it is a
 * quarantine, not a deletion.</p>
 */
export const ORPHAN_GRACE_MS = 24 * 60 * 60 * 1000;

/** How often a window refreshes its heartbeat. About two hundred bytes a minute. */
export const HEARTBEAT_EVERY_MS = 60_000;

/**
 * How old a heartbeat may be before its window is presumed gone: thirty minutes.
 *
 * <p>Thirty refreshes wide, so a laptop waking from sleep, a host paused under a debugger, or a
 * window that has just reloaded is still believed. The cost of the width is that a killed window's
 * heartbeat protects what it held for half an hour, and that costs nothing anybody can see.</p>
 */
export const HEARTBEAT_STALE_MS = 30 * 60_000;

/** How often the directory is swept at most, across every window that opens: once a day. */
export const SWEEP_EVERY_MS = 24 * 60 * 60 * 1000;

/**
 * How long one sweep may run. What is not reached stays for the next; nothing that runs at
 * activation may run for minutes because a directory is slow.
 */
export const SWEEP_BUDGET_MS = 60_000;

/**
 * How long a claim stands before the window that took it is presumed to have died holding it: twice
 * the budget. A sweep still running is inside it; a host killed mid-sweep is past it in minutes.
 */
export const SWEEP_CLAIM_MS = 2 * SWEEP_BUDGET_MS;

/** The subdirectory of the store that holds the windows' heartbeats and the marker of the last sweep. */
export const HOUSEKEEPING_DIR = 'housekeeping';

/** The marker's file name, inside {@link HOUSEKEEPING_DIR}. */
export const SWEEP_MARKER = 'sweep.json';

/** The shape this module writes into a heartbeat and a marker. Bumping it discards older ones. */
export const HOUSEKEEPING_VERSION = 1;

/** A file's two cheap facts: when it was last written, and how big it is. */
export interface Stamp {
  readonly mtimeMs: number;
  readonly size: number;
}

/** A file named for what it is, with its stamp. */
export interface Named {
  readonly name: string;
  readonly stamp: Stamp;
}

/** One window's announcement of what it holds open, as written to disk. */
export interface Heartbeat {
  readonly pid: number;
  /** When it was written — epoch milliseconds, parsed from the UTC instant in the file. */
  readonly at: number;
  readonly ids: readonly string[];
}

/** A heartbeat file as the survey found it: parsed when it could be, always with its name and stamp. */
export interface HeartbeatFile extends Named {
  readonly beat: Heartbeat | undefined;
}

/**
 * The marker of the last sweep, as one of two things — the header's *one window sweeps* says why.
 *
 * <p>`claimed` is written BEFORE the work and stands for {@link SWEEP_CLAIM_MS}; `finished` is written
 * only by a sweep that walked its whole plan and stands for {@link SWEEP_EVERY_MS}. A sweep that failed
 * or ran out of budget leaves the claim, which ages out, and the store stays due.</p>
 */
export type SweepMarker =
  | { readonly kind: 'claimed'; readonly pid: number; readonly began: number }
  | { readonly kind: 'finished'; readonly pid: number; readonly began: number; readonly finished: number };

/**
 * The store's directory, classified — what the sweep and the index decide over.
 *
 * <p>Built by `ChatStoreKeeper.survey()` from one listing of the root and one of each housekeeping
 * subdirectory. Nothing in it comes from opening a transcript.</p>
 */
export interface Survey {
  /** Every `<id>.json`, by id. */
  readonly records: ReadonlyMap<string, Stamp>;
  /** Every `<id>.meta.json`, by id. */
  readonly metas: ReadonlyMap<string, Stamp>;
  /** Every `<id>.lock`, by id. */
  readonly locks: ReadonlyMap<string, Stamp>;
  /** Every `*.tmp` in the root. */
  readonly temps: readonly Named[];
  /** Every file under the quarantine subdirectory. */
  readonly quarantined: readonly Named[];
  /** Every heartbeat file under the housekeeping subdirectory. */
  readonly heartbeats: readonly HeartbeatFile[];
  /** Names in the root that are none of ours. Skipped, and reported so a person can see what is there. */
  readonly foreign: readonly string[];
}

/** What one sweep decided to do, before any of it is done. Ids and names, never paths. */
export interface SweepPlan {
  /** Conversations the listing showed as expired, with the revision it showed — the store re-checks both. */
  readonly expired: readonly { readonly id: string; readonly rev: number }[];
  /** Metadata whose record is gone. */
  readonly orphanMetas: readonly string[];
  /** Transcripts with no metadata beside them, past the grace period. Quarantined, never deleted. */
  readonly orphanRecords: readonly string[];
  readonly staleTemps: readonly string[];
  /** Locks older than the debris age, collected by claiming through the store. */
  readonly abandonedLocks: readonly string[];
  /** Quarantined values older than the retention window. */
  readonly oldQuarantine: readonly string[];
  /** Heartbeats nobody has refreshed. */
  readonly staleHeartbeats: readonly string[];
}

/** The empty plan: a sweep that found nothing to do. */
export const NOTHING_TO_SWEEP: SweepPlan = {
  expired: [],
  orphanMetas: [],
  orphanRecords: [],
  staleTemps: [],
  abandonedLocks: [],
  oldQuarantine: [],
  staleHeartbeats: [],
};

/** The name of one window's heartbeat, named for its pid the way the orphan ledger names its file. */
export function heartbeatName(pid: number): string {
  return `window-${pid}.json`;
}

/** The pid a heartbeat file belongs to, or 0 when the name is not one of ours. */
export function heartbeatOwner(name: string): number {
  const found = /^window-(\d{1,10})\.json$/u.exec(name);

  return found === null ? 0 : Number(found[1]);
}

/** The instant a quarantined value was set aside, read from its name — or nothing for a name not of that shape. */
export function quarantinedAt(name: string): number | undefined {
  const found = /-(\d{1,16})\.json$/u.exec(name);

  return found === null ? undefined : Number(found[1]);
}

/** Epoch milliseconds from a UTC instant as a file carries it, or nothing for anything else. */
const instantOf = (value: unknown): number | undefined => {
  const at = typeof value === 'string' ? Date.parse(value) : Number.NaN;

  return Number.isFinite(at) ? at : undefined;
};

/** Whether a value is a pid as a file may name one: a positive whole number. */
const isPid = (value: unknown): value is number => typeof value === 'number' && Number.isInteger(value) && value > 0;

/** What a heartbeat must look like, said once, for every parse failure to name. */
export const HEARTBEAT_SHAPE =
  `a heartbeat is {version: ${HOUSEKEEPING_VERSION}, pid: a positive whole number, at: a UTC instant, ids: conversation ids of letters, digits, dash and underscore}`;

/** A heartbeat read back, or WHY it is not one. */
export type HeartbeatParse =
  | { readonly kind: 'heartbeat'; readonly beat: Heartbeat }
  | { readonly kind: 'invalid'; readonly reason: string };

/**
 * One heartbeat from what a file held. Strict, because what it says protects records from deletion
 * and what it fails to say leaves them exposed: a torn file is `invalid`, and a caller ages it by its
 * mtime rather than believing or ignoring it.
 */
export function parseHeartbeat(value: unknown): HeartbeatParse {
  const row = value as { version?: unknown; pid?: unknown; at?: unknown; ids?: unknown } | null;
  if (row === null || typeof row !== 'object') {
    return { kind: 'invalid', reason: `not an object; ${HEARTBEAT_SHAPE}` };
  }
  if (row.version !== HOUSEKEEPING_VERSION) {
    return { kind: 'invalid', reason: `version ${String(row.version)} is not ${HOUSEKEEPING_VERSION}; ${HEARTBEAT_SHAPE}` };
  }
  if (!isPid(row.pid)) {
    return { kind: 'invalid', reason: `pid ${String(row.pid)} is not a positive whole number; ${HEARTBEAT_SHAPE}` };
  }
  const at = instantOf(row.at);
  if (at === undefined) {
    return { kind: 'invalid', reason: `at ${String(row.at)} is not a UTC instant; ${HEARTBEAT_SHAPE}` };
  }
  if (!Array.isArray(row.ids)) {
    return { kind: 'invalid', reason: `ids is not an array; ${HEARTBEAT_SHAPE}` };
  }
  const unsafe = row.ids.find((id) => typeof id !== 'string' || !isSafeId(id));
  if (unsafe !== undefined) {
    return { kind: 'invalid', reason: `id ${JSON.stringify(unsafe)} is not a conversation id; ${HEARTBEAT_SHAPE}` };
  }

  return { kind: 'heartbeat', beat: { pid: row.pid, at, ids: row.ids as string[] } };
}

/** A heartbeat as it is written: the same shape {@link parseHeartbeat} reads, with the instant in UTC. */
export function heartbeatText(pid: number, ids: readonly string[], now: number): string {
  return JSON.stringify({ version: HOUSEKEEPING_VERSION, pid, at: new Date(now).toISOString(), ids: [...ids] });
}

/** What a marker must look like, said once, for every parse failure to name. */
export const MARKER_SHAPE =
  `a sweep marker is {version: ${HOUSEKEEPING_VERSION}, pid: a positive whole number, began: a UTC instant, finished: a UTC instant, or absent for a claim}`;

/** A marker read back, or WHY it is not one. */
export type MarkerParse =
  | { readonly kind: 'marker'; readonly marker: SweepMarker }
  | { readonly kind: 'invalid'; readonly reason: string };

/**
 * One marker from what a file held — or the reason it is not one, for the caller to say with the
 * path. A marker of another shape used to read silently as "no marker", and a file somebody could
 * have hand-edited must not vanish into that; it is reported, and the sweep still runs, because a
 * marker nobody can read protects nothing. (The code round.)
 */
export function parseMarker(value: unknown): MarkerParse {
  const row = value as { version?: unknown; pid?: unknown; began?: unknown; finished?: unknown } | null;
  if (row === null || typeof row !== 'object') {
    return { kind: 'invalid', reason: `not an object; ${MARKER_SHAPE}` };
  }
  if (row.version !== HOUSEKEEPING_VERSION) {
    return { kind: 'invalid', reason: `version ${String(row.version)} is not ${HOUSEKEEPING_VERSION}; ${MARKER_SHAPE}` };
  }
  if (!isPid(row.pid)) {
    return { kind: 'invalid', reason: `pid ${String(row.pid)} is not a positive whole number; ${MARKER_SHAPE}` };
  }
  const began = instantOf(row.began);
  if (began === undefined) {
    return { kind: 'invalid', reason: `began ${String(row.began)} is not a UTC instant; ${MARKER_SHAPE}` };
  }
  if (row.finished === undefined) {
    return { kind: 'marker', marker: { kind: 'claimed', pid: row.pid, began } };
  }
  const finished = instantOf(row.finished);
  if (finished === undefined) {
    return { kind: 'invalid', reason: `finished ${String(row.finished)} is not a UTC instant; ${MARKER_SHAPE}` };
  }

  return { kind: 'marker', marker: { kind: 'finished', pid: row.pid, began, finished } };
}

/** A marker as it is written: the same shape {@link parseMarker} reads, every instant in UTC. */
export function markerText(marker: SweepMarker): string {
  return JSON.stringify({
    version: HOUSEKEEPING_VERSION,
    pid: marker.pid,
    began: new Date(marker.began).toISOString(),
    ...(marker.kind === 'finished' ? { finished: new Date(marker.finished).toISOString() } : {}),
  });
}

/**
 * Whether a heartbeat still speaks for its window.
 *
 * <p>Younger than the stale window is live. So is one dated in the FUTURE: a clock that stepped is a
 * clock that stepped, and the safe reading of a heartbeat nobody can date is that its window is alive
 * — over-protecting a few records for a while is the cheap side of that error. Such a file is collected
 * once the clock has caught up with it.</p>
 */
export const liveHeartbeat = (beat: Heartbeat, now: number): boolean => now - beat.at < HEARTBEAT_STALE_MS;

/**
 * Every conversation id some live window holds open — announced by a heartbeat, or held by THIS
 * window right now. The union, because this window's own heartbeat may be a minute behind its tabs.
 */
export function protectedIds(heartbeats: readonly HeartbeatFile[], now: number, held: Iterable<string>): ReadonlySet<string> {
  const ids = new Set<string>(held);
  for (const file of heartbeats) {
    if (file.beat !== undefined && liveHeartbeat(file.beat, now)) {
      for (const id of file.beat.ids) {
        ids.add(id);
      }
    }
  }

  return ids;
}

/**
 * Every conversation some OTHER live window holds open — the same announcement, asked the other way.
 *
 * <p>{@link protectedIds} asks "may the sweep delete this", and answers for every window including
 * this one. The picker asks a different question — "would reopening this give a record a second
 * writer" — and this window's own tabs are not an obstacle to that: they are the case the picker
 * REVEALS. So this reads the same heartbeats and leaves out the file this window wrote, which is also
 * the one that may be a minute behind its own tabs; believing it about ourselves would draw a
 * conversation closed a moment ago as open somewhere and refuse to reopen it.</p>
 *
 * <p>Liveness is judged against `now` at the moment of asking rather than at the moment of the
 * survey, so a picker left open does not go on believing a window that has since gone quiet.</p>
 *
 * <p><b>And the writer must still exist.</b> A RELOAD is the case that forces this: the heartbeat of
 * the host being replaced is deliberately left on disk — `chatStoreHeartbeat.ts` says why, and the
 * sweep needs it to go on protecting the tabs the new host is still restoring — but the new host has
 * a new pid, so without asking the operating system its own predecessor reads as a live rival for
 * the whole stale window. Every conversation that window held would then be undeletable and
 * unopenable for half an hour after every reload, which is a command people press daily.
 * {@link protectedIds} deliberately does NOT ask: the sweep's question is "might anybody still hold
 * this", where a generous answer costs nothing, and the two questions part company exactly here.
 * (CodeRabbit, on the pull request.)</p>
 *
 * @param alive whether a process is still running — injected, because this module owns no I/O; the
 *   index passes `processAlive`, and a test passes a set
 */
export function heldElsewhere(
  heartbeats: readonly HeartbeatFile[],
  now: number,
  ownPid: number,
  alive: (pid: number) => boolean,
): ReadonlySet<string> {
  const ids = new Set<string>();
  for (const file of heartbeats) {
    if (!ourOwn(file, ownPid) && file.beat !== undefined && liveHeartbeat(file.beat, now) && alive(file.beat.pid)) {
      for (const id of file.beat.ids) {
        ids.add(id);
      }
    }
  }

  return ids;
}

/**
 * Whether a heartbeat file is THIS window's — by either of the two things that name a window.
 *
 * <p>A file says whose it is twice: in its name, which is `window-<pid>.json`, and in the `pid` its
 * body carries. They agree in every file this code writes. This asks whether EITHER says ours, which
 * is the safe direction — a file that might be ours is left out of what "somebody else holds", and
 * the cost of being wrong that way is a conversation this window can reopen rather than a second tab
 * on a record another window is writing.</p>
 *
 * <p>It is a named function because the same condition spelled inline — as a conjunction of two
 * not-equals, which is what De Morgan turns this into — was misread as its own opposite by two
 * reviewers in one round. The semantics never changed; the spelling did.</p>
 */
const ourOwn = (file: HeartbeatFile, ownPid: number): boolean =>
  file.beat?.pid === ownPid || heartbeatOwner(file.name) === ownPid;

/**
 * Whether a sweep should run, given the marker the last one left.
 *
 * <p>No marker is due. A claim is not due while it is younger than {@link SWEEP_CLAIM_MS} — a window is
 * sweeping, or was until a moment ago — and due once it is older: nobody finished it. A finished
 * marker holds for {@link SWEEP_EVERY_MS}. Both windows are symmetric in time: a marker from the future
 * is a clock that stepped, and over-caution for one window costs nothing, while a marker a window or
 * more ahead would otherwise block sweeping for as long as the clock was wrong.</p>
 */
export const sweepDue = (marker: SweepMarker | undefined, now: number): boolean =>
  marker === undefined
  || (marker.kind === 'claimed' ? Math.abs(now - marker.began) >= SWEEP_CLAIM_MS : Math.abs(now - marker.finished) >= SWEEP_EVERY_MS);

/** Whether a heartbeat FILE is one the sweep collects: stale by its own instant, or torn and old by its mtime. */
function staleHeartbeatFile(file: HeartbeatFile, now: number, ownPid: number): boolean {
  if (heartbeatOwner(file.name) === ownPid) {
    return false;
  }
  const at = file.beat === undefined ? file.stamp.mtimeMs : file.beat.at;

  return now - at >= HEARTBEAT_STALE_MS;
}

/**
 * The plan: what the directory as surveyed, and the listing as read, say should go.
 *
 * <p>Nominations only. An expired conversation is one whose METADATA said so and whose record is there;
 * the store re-checks the record under its lock before anything is deleted, and the revision the
 * listing showed travels with the nomination so "unchanged" is a fact and not a guess. A metadata file
 * whose record is missing is an orphan whatever its age. A record whose metadata is missing is an
 * orphan only past the grace period, and only if no live window holds it. Nothing protected is
 * nominated for anything.</p>
 *
 * @param held what this window holds open right now — read live, so a tab registered a moment ago counts
 */
export function planSweep(
  survey: Survey,
  metas: readonly ConversationMeta[],
  now: number,
  held: Iterable<string>,
  ownPid: number,
): SweepPlan {
  const safe = protectedIds(survey.heartbeats, now, held);
  const expired = metas
    .filter((meta) => survey.records.has(meta.id) && !safe.has(meta.id) && now - meta.updatedAt > KEEP_FOR_MS)
    .map((meta) => ({ id: meta.id, rev: meta.rev }));
  const orphanMetas = [...survey.metas.keys()].filter((id) => !survey.records.has(id) && !safe.has(id));
  const orphanRecords = [...survey.records]
    .filter(([id, stamp]) => !survey.metas.has(id) && !safe.has(id) && now - stamp.mtimeMs > ORPHAN_GRACE_MS)
    .map(([id]) => id);
  const staleTemps = survey.temps.filter((file) => now - file.stamp.mtimeMs > DEBRIS_AGE_MS).map((file) => file.name);
  const abandonedLocks = [...survey.locks].filter(([, stamp]) => now - stamp.mtimeMs > DEBRIS_AGE_MS).map(([id]) => id);
  const oldQuarantine = survey.quarantined
    .filter((file) => {
      const at = quarantinedAt(file.name);

      return at !== undefined && now - at > KEEP_FOR_MS;
    })
    .map((file) => file.name);
  const staleHeartbeats = survey.heartbeats.filter((file) => staleHeartbeatFile(file, now, ownPid)).map((file) => file.name);

  return { expired, orphanMetas, orphanRecords, staleTemps, abandonedLocks, oldQuarantine, staleHeartbeats };
}

/** Whether a plan asks for anything at all. */
export const planIsEmpty = (plan: SweepPlan): boolean =>
  Object.values(plan).every((list: readonly unknown[]) => list.length === 0);

/**
 * Why a sweep did not run, when it did not. `sweeping` and `unclaimed` are the claim's two answers;
 * `unannounced` is the housekeeping's — a window whose own heartbeat could not be written.
 */
export type SweepSkipped = 'unavailable' | 'empty' | 'recent' | 'sweeping' | 'unclaimed' | 'unannounced' | 'unmigrated';

/** What one sweep came to: either it did not run, and why, or these counts. */
export type SweepReport =
  | { readonly kind: 'skipped'; readonly why: SweepSkipped; readonly reason: string }
  | {
    readonly kind: 'swept';
    readonly retired: number;
    readonly keptUnderLock: number;
    readonly orphanMetasRemoved: number;
    readonly recordsQuarantined: number;
    readonly tempsRemoved: number;
    readonly locksCollected: number;
    readonly quarantineRemoved: number;
    readonly heartbeatsRemoved: number;
    readonly failed: number;
    /** Whether the budget ran out before the plan did; what was not reached waits for the next sweep. */
    readonly unfinished: boolean;
  };

/** One line for the console, so a person can see what the sweep did without reading a directory. */
export function describeSweep(report: SweepReport): string {
  if (report.kind === 'skipped') {
    const why: Record<SweepSkipped, string> = {
      unavailable: 'the store could not be read',
      empty: 'there is no store yet',
      recent: 'another window swept recently',
      sweeping: 'another window is sweeping now',
      unclaimed: 'this window could not claim the store, and another window may be sweeping it',
      unannounced: 'this window could not announce what it holds open, and a window that cannot say so deletes nothing',
      unmigrated: 'the migration into the store did not finish, and nothing is retired by age until it has',
    };

    return `ConnectOtherAIs: the conversation sweep did not run — ${why[report.why]}${report.reason.length > 0 ? ` (${report.reason})` : ''}`;
  }

  return 'ConnectOtherAIs: the conversation sweep '
    + `retired ${report.retired} expired, kept ${report.keptUnderLock} that had changed under the lock, `
    + `removed ${report.orphanMetasRemoved} orphaned index entries, set aside ${report.recordsQuarantined} orphaned transcripts, `
    + `removed ${report.tempsRemoved} stale temporaries, collected ${report.locksCollected} abandoned locks, `
    + `removed ${report.quarantineRemoved} old quarantined values and ${report.heartbeatsRemoved} stale heartbeats`
    + `${report.failed > 0 ? `; ${report.failed} could not be done and will be retried` : ''}`
    + `${report.unfinished ? '; the budget ran out and the rest waits for the next sweep, which stays due' : ''}`;
}
