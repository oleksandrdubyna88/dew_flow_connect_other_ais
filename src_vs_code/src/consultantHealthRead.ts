import { type CheckRecord, type ConsultantsReport, type HealthFile, beatKey, checkStateOf, parseCheckFile, parseConsultantsFile } from './consultantHealth';

/**
 * One side's `consultations/health/`, read through ports — `consultants.json` and every caller kind's
 * `<kind>.check.json`, and on THIS side its outcome files too (E5.1 of
 * `research/PLAN_the_consultant_works_on_every_vendor.md`).
 *
 * <p>Ports rather than `vscode.workspace.fs`, for the reason `consultationWatcher.ts` gives about its own read: the
 * rule worth getting right is the distinction between ABSENT and COULD NOT BE READ. Absent is ordinary — a side whose
 * server never wrote a file, a kind nobody checked — and reads as nothing there. A read that failed is a rename landing
 * on an open handle (the ordinary Windows case), or a WSL share whose distribution is not running; answering anything
 * for it would blank a block a person is reading, at random. So it answers `undefined`, and the caller keeps the last
 * snapshot it had.</p>
 */

/** What one file read came to — the host maps `FileNotFound` to `absent` and every other failure to `failed`. */
export type FileRead =
  | { readonly kind: 'bytes'; readonly bytes: Uint8Array }
  | { readonly kind: 'absent' }
  | { readonly kind: 'failed'; readonly why: string };

export interface HealthReadPorts {
  readonly read: (path: string) => Promise<FileRead>;
}

/** What is on disk for one side: its consultants file (if any), and each caller kind's check that was there. */
export interface SideFiles {
  readonly report: HealthFile<ConsultantsReport> | undefined;
  /** Only the kinds whose file exists — a kind nobody ever checked is simply not here. */
  readonly checks: Readonly<Record<string, HealthFile<CheckRecord>>>;
  /**
   * THIS side's `<kind>.answer.json` and `<kind>.failure.json`, as one string of what they hold — so a consultation that
   * answered or failed re-asks the server (the whole-branch review, A2). Empty for another side, whose survey carries
   * them, and absent from a snapshot read without them.
   */
  readonly outcomes?: string;
  /**
   * When THIS window first saw each check's heartbeat hold its current value, keyed by {@link beatKey} — the clock another
   * side's `checking` is judged by (the whole-branch review, O). Filled by the watcher ({@link withBeatsSeen}); a
   * snapshot without it judges every heartbeat as first seen now.
   */
  readonly beatsSeenMs?: Readonly<Record<string, number>>;
}

/** Where a side's files are, under a data directory as this host reaches it. */
export interface HealthPaths {
  readonly consultants: string;
  readonly check: (kind: string) => string;
  readonly answer: (kind: string) => string;
  readonly failure: (kind: string) => string;
}

/**
 * Where the files are, under a data directory as this host reaches it.
 *
 * <p>Joined with a forward slash, which Windows accepts and POSIX requires, after a trailing separator of either kind
 * is trimmed — the shape `answerPaths` in `escalationDirs.ts` settled on after a mixed-separator path reached a
 * rename.</p>
 */
export function healthPaths(dir: string): HealthPaths {
  const health = `${dir.trim().replace(/[\\/]+$/u, '')}/consultations/health`;

  return {
    consultants: `${health}/consultants.json`,
    check: (kind) => `${health}/${kind}.check.json`,
    answer: (kind) => `${health}/${kind}.answer.json`,
    failure: (kind) => `${health}/${kind}.failure.json`,
  };
}

/**
 * One side as it is on disk now — or `undefined` when any read failed, so the last snapshot stands.
 *
 * @param withOutcomes THIS side: read its outcome files as well. Another side's survey carries them (its server rewrites
 *   that row on every outcome), and reading eight more files across a `\\wsl.localhost` share every poll buys nothing.
 */
export async function readSide(dir: string, kinds: readonly string[], ports: HealthReadPorts, withOutcomes = false): Promise<SideFiles | undefined> {
  const paths = healthPaths(dir);
  const outcomePaths = withOutcomes ? kinds.flatMap((kind) => [paths.answer(kind), paths.failure(kind)]) : [];
  const [report, ...rest] = await Promise.all([paths.consultants, ...kinds.map((kind) => paths.check(kind)), ...outcomePaths].map((path) => ports.read(path)));
  const checks = rest.slice(0, kinds.length);
  const all: readonly (FileRead | undefined)[] = [report, ...rest];

  return all.some((one) => one === undefined || one.kind === 'failed')
    ? undefined
    : {
      report: bytesOf(report, parseConsultantsFile),
      checks: Object.fromEntries(kinds.flatMap((kind, at) => checkEntry(kind, checks[at]))),
      outcomes: outcomeSignature(outcomePaths, rest.slice(kinds.length)),
    };
}

/** What a read's bytes parse to, or nothing for a file that is not there. */
function bytesOf<T>(read: FileRead | undefined, parse: (bytes: Uint8Array) => HealthFile<T>): HealthFile<T> | undefined {
  return read?.kind === 'bytes' ? parse(read.bytes) : undefined;
}

/** One caller kind's check as an entry — none at all for a kind whose file is not there. */
function checkEntry(kind: string, read: FileRead | undefined): [string, HealthFile<CheckRecord>][] {
  const parsed = bytesOf(read, parseCheckFile);

  return parsed === undefined ? [] : [[kind, parsed]];
}

/** The outcome files as one string: each path with what it holds now, or that it is not there. */
function outcomeSignature(paths: readonly string[], reads: readonly (FileRead | undefined)[]): string {
  return paths.map((path, at) => `${path}=${contentOf(reads[at])}`).join('|');
}

function contentOf(read: FileRead | undefined): string {
  return read?.kind === 'bytes' ? new TextDecoder().decode(read.bytes) : 'absent';
}

/** Every check of a side whose heartbeat matters — its state files and the checks its survey carries — as sighting keys. */
function beatKeysOf(files: SideFiles): readonly string[] {
  const fromFiles = Object.entries(files.checks).flatMap(([kind, read]) => (read.kind === 'found' ? [beatKey(kind, read.value.heartbeatUtc)] : []));
  const rows = files.report?.kind === 'found' ? files.report.value.consultants : [];
  const fromReport = rows.flatMap((row) => (row.check === undefined ? [] : [beatKey(row.callerKind, row.check.heartbeatUtc)]));

  return [...new Set([...fromFiles, ...fromReport])];
}

/**
 * The side with when THIS window first saw each heartbeat value — the sighting a previous read already had, else now.
 *
 * <p>Pure, so the clock rule is a test: a heartbeat that moved is a new key, first seen now; one that stood still keeps
 * the instant it was first seen, and {@link checkStateOf} counts this window's own seconds from there. Keys no longer on
 * disk are dropped, so the map holds one entry per check and no more.</p>
 */
export function withBeatsSeen(files: SideFiles, previous: SideFiles | undefined, nowMs: number): SideFiles {
  const before = previous?.beatsSeenMs ?? {};

  return { ...files, beatsSeenMs: Object.fromEntries(beatKeysOf(files).map((key) => [key, before[key] ?? nowMs])) };
}

/** When this window first saw that check's current heartbeat — now, when it never has. */
export function seenOf(files: SideFiles | undefined, kind: string, record: CheckRecord, nowMs: number): number {
  return files?.beatsSeenMs?.[beatKey(kind, record.heartbeatUtc)] ?? nowMs;
}

/**
 * What a person would SEE of one side, as one string — so a poll that found nothing new repaints nothing.
 *
 * <p>On THIS side the checks and the outcome files are in it, and `consultants.json` is not ({@link NOT_READ_HERE}).
 * The heartbeat is NOT in it: a running check rewrites it every fifteen seconds, and a repaint per beat rebuilds the
 * Settings tab under whoever is editing it. What IS in it is each check as it must be JUDGED now — on the other side a
 * heartbeat that stood still turns `checking` into `abandoned` with nothing on disk moving, so only the clock can make
 * that change visible, and the judged word is what carries it.</p>
 */
export function sideSignature(files: SideFiles | undefined, side: 'this' | 'other', nowMs: number): string {
  if (files === undefined) {
    return 'unread';
  }
  const checks = Object.entries(files.checks)
    .map(([kind, read]) => checkSignature(files, kind, read, side, nowMs))
    .sort((one, other) => one.localeCompare(other));

  return [side === 'this' ? `${NOT_READ_HERE}:${files.outcomes ?? ''}` : reportSignature(files.report), ...checks].join('|');
}

/**
 * THIS side's `consultants.json` is not part of what its block shows — that block reads the probe — and every
 * `--consultants` run rewrites it with a fresh `utc`. Counting it would make the probe's own answer ask the probe again,
 * for ever, while the Settings tab is open (epic 5's review, both reviewers, Blocking). The outcome files ARE counted:
 * only a consultation writes them, never the probe, so a new answer or failure re-asks once and the loop cannot form
 * (the whole-branch review, A2).
 */
const NOT_READ_HERE = 'report-read-by-the-probe';

function reportSignature(report: HealthFile<ConsultantsReport> | undefined): string {
  if (report === undefined) {
    return 'no-report';
  }

  return report.kind === 'found' ? `report:${report.value.utc}:${JSON.stringify(report.value.consultants)}` : `report-unreadable:${report.why}`;
}

/** A check as JUDGED now, with its start and end — never its heartbeat. */
function checkSignature(files: SideFiles, kind: string, read: HealthFile<CheckRecord>, side: 'this' | 'other', nowMs: number): string {
  return read.kind === 'found'
    ? `${kind}:${checkStateOf(read.value, side, nowMs, seenOf(files, kind, read.value, nowMs)).state}:${read.value.startedUtc}:${read.value.finishedUtc}`
    : `${kind}:unreadable:${read.why}`;
}
