import { isSessionId } from './chatSource';
import { isTerminalName } from './chatStore';
import { NO_NAMES, SessionNames, collectNames } from './claudeQuestion';
import { projectDirNames } from './claudeSessions';
import { REGISTRY_MAX, REMOTE_PROTOCOL, RemoteCall, TAIL_MAX, isFolderName } from './remoteScript';

/**
 * The parser of what a VM answers (E2.S2, C2): the frame of marker lines and data lines that `REMOTE_SCRIPT` prints becomes
 * ONE arm of `RemoteRead`. Pure — no process, no `vscode` — so every row of the decision table is a test over a string.
 *
 * <p><b>The script reports facts and this module decides.</b> Every data line starts with `:` and every other line of the
 * frame starts with `COAI-`, so no byte of a transcript, a registry file, a folder name or a title row can end a section
 * or look like a marker. The frame starts at the LAST `COAI-BEGIN 1` (a banner may hold the line) and ends at the FIRST
 * `COAI-END` after it; a line that is neither a marker nor data, an unknown marker, a marker with words it does not take,
 * or a part the decision needs and does not find, makes the answer `unreadable`.</p>
 *
 * <p>`readRemote` never throws and no arm holds an error's message (a V8 `JSON.parse` error quotes its input): every fault is
 * a `Fault` of a closed list, caught once at the edge, and the one `JSON.parse` has a `catch` that returns nothing.</p>
 */

/** One candidate Claude process of a tmux session, with a registry file that names it. */
export interface RemoteCandidate {
  /** `<window>.<pane>`, for the pick list. */
  readonly pane: string;
  /** The top-most `claude` process. */
  readonly pid: number;
  /** Valid by `isSessionId` — a candidate with no valid registry file is not in the list. */
  readonly sessionId: string;
  /** The registry's name, or empty when it has none. */
  readonly name: string;
  readonly cwd: string;
}

/** One session file of the title scan. */
export interface TitledSession {
  readonly sessionId: string;
  readonly folder: string;
  /** Seconds; 0 when the VM has no `stat -c`. */
  readonly mtime: number;
  /** By `collectNames()` over the rows — the one rule. */
  readonly names: SessionNames;
  /** Over 64 MiB or not readable: its names were NOT read. */
  readonly skipped: boolean;
}

/** Why an answer cannot be used. A closed list; `UNREADABLE_SAID` has one clause for each. */
export type Unreadable =
  | 'exit' | 'no-sentinel' | 'version' | 'cut' | 'frame'
  | 'operands' | 'home' | 'tool' | 'panes' | 'too-many' | 'cwd' | 'transcript'
  | 'registry-denied' | 'registry-big' | 'registry-bad' | 'registry-stale' | 'id-mismatch' | 'folder';

/** What the VM said — one arm for each outcome, no arm an optional field to interpret. A cut tail is a FACT (`found.cut`); what it means is S3's. */
export type RemoteRead =
  | { readonly kind: 'found'; readonly via: 'registry' | 'id'; readonly sessionId: string; readonly cwd: string;
      readonly name: string; readonly folder: string; readonly lines: readonly string[]; readonly cut: boolean }
  | { readonly kind: 'fallback'; readonly paneCwd: string; readonly folders: readonly string[];
      readonly skipped: number; readonly more: boolean }
  | { readonly kind: 'titles'; readonly sessions: readonly TitledSession[];
      readonly scan: { readonly why: 'all' | 'count' | 'time'; readonly tooLong: number; readonly more: boolean } }
  | { readonly kind: 'gone'; readonly sessionId: string }
  | { readonly kind: 'absent'; readonly what: 'session' | 'transcript' }
  | { readonly kind: 'ambiguous'; readonly why: 'panes' | 'processes' | 'transcripts';
      readonly candidates: readonly RemoteCandidate[]; readonly unnamed: number }
  | { readonly kind: 'unreadable'; readonly why: Unreadable };

/** One short clause for each fault, all different, for ONE "unreadable" sentence. None holds data from the answer. */
export const UNREADABLE_SAID: Readonly<Record<Unreadable, string>> = {
  'exit': 'the remote command ended with an error',
  'no-sentinel': 'the answer did not start as expected',
  'version': 'the answer is in a form this version does not know',
  'cut': 'the answer stopped before its end',
  'frame': 'the answer was not in the expected form',
  'operands': 'the remote script refused what it was asked',
  'home': 'the remote account has no home folder',
  'tool': 'a tool the read needs (tmux, ps, sed, cut, head, tail, wc, grep, awk, ls or date) is not on the PATH of an ssh command there',
  'panes': 'tmux answered in a form that was not expected',
  'too-many': 'the tmux session has too many panes or processes to look through',
  'cwd': 'tmux gave no folder for the pane',
  'transcript': 'the session file could not be opened',
  'registry-denied': 'the registry file of the session could not be opened',
  'registry-big': 'the registry file of the session is larger than 64 KiB',
  'registry-bad': 'the registry file of the session is not in the expected form',
  'registry-stale': 'the registry file of the session belongs to an older process',
  'id-mismatch': 'the session file that was read is not the one the registry names',
  'folder': 'the session file is not in the folder that belongs to the session\'s working folder',
};

/** A fault of the closed list, thrown inside this module and caught ONCE, in `readRemote`. It carries the word and nothing else. */
class Fault extends Error {
  readonly why: Unreadable;

  constructor(why: Unreadable) {
    super(why);
    this.why = why;
  }
}

function fail(why: Unreadable): never {
  throw new Fault(why);
}

/**
 * What the VM answered, as one arm. The caller passes the `answered` run and the SAME `call` it built the operands from; an
 * exit code that is not 0 is `unreadable` here, so the caller never reads `code`.
 */
export function readRemote(run: { readonly code: number; readonly stdout: string }, call: RemoteCall): RemoteRead {
  if (run.code !== 0) {
    return unreadable('exit');
  }
  try {
    return decide(recordsOf(framedLines(run.stdout)), call);
  } catch (reason) {
    // A fault of the list says which; anything else is a frame this module could not follow. Never the message.
    return unreadable(reason instanceof Fault ? reason.why : 'frame');
  }
}

const unreadable = (why: Unreadable): RemoteRead => ({ kind: 'unreadable', why });

/** The lines between the LAST `COAI-BEGIN 1` and the first `COAI-END` after it. */
function framedLines(stdout: string): readonly string[] {
  const lines = stdout.split('\n');
  const begin = lines.lastIndexOf(`COAI-BEGIN ${REMOTE_PROTOCOL}`);
  if (begin < 0) {
    return fail(lines.some((line) => line.startsWith('COAI-BEGIN ')) ? 'version' : 'no-sentinel');
  }
  const end = lines.indexOf('COAI-END', begin + 1);

  return end < 0 ? fail('cut') : lines.slice(begin + 1, end);
}

/** A marker and the data lines after it. */
interface Rec {
  readonly marker: string;
  readonly words: readonly string[];
  readonly data: readonly string[];
}

/**
 * The frame as records. The data array of the LAST record is appended to in place — a local accumulator that never leaves
 * this function; a copy for each line would be quadratic over a 4 MiB tail.
 */
function recordsOf(lines: readonly string[]): readonly Rec[] {
  const recs: { marker: string; words: readonly string[]; data: string[] }[] = [];
  for (const line of lines) {
    if (line.startsWith('COAI-')) {
      const [marker, ...words] = line.split(' ');
      recs.push({ marker, words, data: [] });
    } else {
      lastOf(recs).data.push(dataOf(line));
    }
  }

  return recs;
}

/** The text of a data line (the `:` taken off), or a fault: any other line of the frame is not part of it. */
function dataOf(line: string): string {
  return line.startsWith(':') ? line.slice(1) : fail('frame');
}

function lastOf<T>(recs: readonly T[]): T {
  return recs.length === 0 ? fail('frame') : recs[recs.length - 1];
}

type Pattern = RegExp | ((word: string) => boolean);

const matches = (pattern: Pattern, word: string): boolean => (pattern instanceof RegExp ? pattern.test(word) : pattern(word));

/** A check that the words are exactly these shapes, in this order. */
const shaped = (...patterns: readonly Pattern[]) => (words: readonly string[]): boolean =>
  words.length === patterns.length && patterns.every((pattern, index) => matches(pattern, words[index]));

/** Which calls a marker may answer: the two that look at a tmux session, the title call, or any. */
type Forms = 'any' | 'session' | 'titles';

interface Spec {
  readonly forms: Forms;
  readonly words: (words: readonly string[]) => boolean;
  readonly data: (rec: Rec) => boolean;
}

const noData = (rec: Rec): boolean => rec.data.length === 0;
const oneLine = (rec: Rec): boolean => rec.data.length === 1;
const anyData = (): boolean => true;
/** The registry's text is there when its state is `ok`, and only then. */
const registryData = (rec: Rec): boolean => rec.words[1] === 'ok' || rec.data.length === 0;
/** A session's title rows are there when it was read (`rows`), and only then. */
const sessionData = (rec: Rec): boolean => rec.words[3] === 'rows' || rec.data.length === 0;

const DIGITS = /^\d+$/u;
const DIGITS_OR_DASH = /^(\d+|-)$/u;
const PANE_NUMBER = /^\d+\.\d+$/u;

/** What `COAI-BAD` can say: the faults the script itself reports. */
const BAD_WORDS: readonly Unreadable[] = ['operands', 'home', 'tool', 'panes', 'cwd', 'transcript'];

/** The table of markers (S2.4): the words each takes, its data lines and the calls it may appear in. A marker not here is a fault. */
const SPECS: ReadonlyMap<string, Spec> = new Map<string, Spec>([
  ['COAI-BAD', { forms: 'any', words: shaped((word) => BAD_WORDS.some((bad) => bad === word)), data: noData }],
  ['COAI-NO-SESSION', { forms: 'session', words: shaped(), data: noData }],
  ['COAI-OVER', { forms: 'session', words: shaped(/^(panes|walk)$/u), data: noData }],
  ['COAI-PANE', { forms: 'session', words: shaped(PANE_NUMBER, /^%\d+$/u, DIGITS), data: noData }],
  ['COAI-CLAUDE', { forms: 'session', words: shaped(PANE_NUMBER, DIGITS, DIGITS_OR_DASH), data: noData }],
  ['COAI-REGISTRY', { forms: 'session', words: shaped(DIGITS, /^(ok|none|denied)$/u), data: registryData }],
  ['COAI-TRANSCRIPT', { forms: 'session', words: shaped(isSessionId, DIGITS), data: noData }],
  ['COAI-FILE', { forms: 'session', words: shaped(DIGITS), data: oneLine }],
  ['COAI-TAIL', { forms: 'session', words: shaped(), data: anyData }],
  ['COAI-FILE-END', { forms: 'session', words: shaped(DIGITS), data: noData }],
  ['COAI-CWD', { forms: 'session', words: shaped(), data: oneLine }],
  ['COAI-FOLDERS', { forms: 'session', words: shaped(), data: anyData }],
  ['COAI-FOLDERS-END', { forms: 'session', words: shaped(DIGITS, /^[01]$/u), data: noData }],
  ['COAI-FOLDER', { forms: 'titles', words: shaped(isFolderName, /^(ok|none)$/u), data: noData }],
  ['COAI-SESSION', { forms: 'titles', words: shaped(isSessionId, DIGITS_OR_DASH, DIGITS, /^(rows|skipped)$/u), data: sessionData }],
  ['COAI-ROWS', { forms: 'titles', words: shaped(DIGITS, /^[01]$/u), data: noData }],
  ['COAI-SCAN', { forms: 'titles', words: shaped(DIGITS, /^(all|count|time)$/u), data: noData }],
]);

/** Whether a marker of this spec may appear in an answer to this call. */
const allowed = (spec: Spec | undefined, call: RemoteCall): spec is Spec =>
  spec !== undefined && (spec.forms === 'any' || spec.forms === (call.kind === 'titles' ? 'titles' : 'session'));

function checkRecord(rec: Rec, call: RemoteCall): void {
  const spec = SPECS.get(rec.marker);
  if (!allowed(spec, call) || !spec.words(rec.words) || !spec.data(rec)) {
    fail('frame');
  }
}

/** The first record of this marker, or a fault: a part the rows below need and is not there. */
const first = (recs: readonly Rec[], marker: string): Rec => recs.find((rec) => rec.marker === marker) ?? fail('frame');

/** Rows 2 and 3 of the decision table, then the form of the call. */
function decide(recs: readonly Rec[], call: RemoteCall): RemoteRead {
  recs.forEach((rec) => checkRecord(rec, call));
  stoppedBy(recs);

  return call.kind === 'titles' ? titlesRead(recs, call) : sessionRead(recs, call);
}

/** Row 3: a `COAI-BAD <what>` names its fault, a `COAI-OVER` is `too-many`. */
function stoppedBy(recs: readonly Rec[]): void {
  const stop = recs.find((rec) => rec.marker === 'COAI-BAD' || rec.marker === 'COAI-OVER');
  if (stop !== undefined) {
    fail(stop.marker === 'COAI-OVER' ? 'too-many' : (BAD_WORDS.find((why) => why === stop.words[0]) ?? 'frame'));
  }
}

type SessionCall = Exclude<RemoteCall, { readonly kind: 'titles' }>;

/** What the registry file of one candidate says — valid, none, or the fault it has. */
type Registry =
  | { readonly kind: 'valid'; readonly sessionId: string; readonly cwd: string; readonly name: string }
  | { readonly kind: 'none' }
  | { readonly kind: 'fault'; readonly why: Unreadable };

/** One `claude` process of the frame, with what its registry file says. */
interface Seen {
  readonly pane: string;
  readonly pid: number;
  readonly start: string;
  readonly state: string;
  readonly text: string;
  readonly registry: Registry;
}

interface Ctx {
  readonly recs: readonly Rec[];
  readonly call: SessionCall;
  readonly paneCount: number;
  readonly cands: readonly Seen[];
}

const ABSENT_TRANSCRIPT: RemoteRead = { kind: 'absent', what: 'transcript' };
const MANY_TRANSCRIPTS: RemoteRead = { kind: 'ambiguous', why: 'transcripts', candidates: [], unnamed: 0 };

/** Row 4, then the panes and the processes of a `session` or `id` call. */
function sessionRead(recs: readonly Rec[], call: SessionCall): RemoteRead {
  return recs.some((rec) => rec.marker === 'COAI-NO-SESSION') ? { kind: 'absent', what: 'session' } : listed(recs, call);
}

function listed(recs: readonly Rec[], call: SessionCall): RemoteRead {
  const paneCount = recs.filter((rec) => rec.marker === 'COAI-PANE').length;
  if (paneCount === 0) {
    return fail('frame');
  }
  const cands = recs.flatMap((rec, index) => (rec.marker === 'COAI-CLAUDE' ? [candidateAt(recs, index)] : []));
  const ctx: Ctx = { recs, call, paneCount, cands };

  return paneCount >= 2 || cands.length >= 2 ? several(ctx) : single(ctx);
}

/** A `COAI-CLAUDE` line, the pane it belongs to, and the registry record right after it. */
function candidateAt(recs: readonly Rec[], index: number): Seen {
  const [pane, pid, start] = recs[index].words;
  const registry = registryAfter(recs, index, pid);
  if (!recs.some((rec) => rec.marker === 'COAI-PANE' && rec.words[0] === pane)) {
    return fail('frame');
  }
  const seen = { pane, pid: Number(pid), start, state: registry.words[1], text: registry.data.join('\n') };

  return { ...seen, registry: registryOf(seen) };
}

/** The `COAI-REGISTRY` record right after a `COAI-CLAUDE` line, for the same pid — the script prints one after each. */
function registryAfter(recs: readonly Rec[], index: number, pid: string): Rec {
  const registry = recs[index + 1];

  return registry?.marker === 'COAI-REGISTRY' && registry.words[0] === pid ? registry : fail('frame');
}

const fault = (why: Unreadable): Registry => ({ kind: 'fault', why });

/** The registry's state is `ok` (read and checked), `none` (no file: not a fault) or `denied` (the file could not be opened). */
function registryOf(seen: Omit<Seen, 'registry'>): Registry {
  if (seen.state === 'ok') {
    return checkedRegistry(seen);
  }

  return seen.state === 'none' ? { kind: 'none' } : fault('registry-denied');
}

/** Whether this is a JSON object (not null, not an array). */
const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

/** The text as a JSON object, or nothing. The `catch` returns nothing of the error: a V8 parse error quotes its input. */
function jsonObject(text: string): Record<string, unknown> | undefined {
  try {
    const value: unknown = JSON.parse(text);

    return isPlainObject(value) ? value : undefined;
  } catch {
    return undefined;
  }
}

/** The registry file's checks, in order: its size, its shape, then whether it belongs to this process. */
function checkedRegistry(seen: Omit<Seen, 'registry'>): Registry {
  if (Buffer.byteLength(seen.text) > REGISTRY_MAX) {
    return fault('registry-big');
  }
  const shape = shapeOf(seen.text, seen.pid);
  if (shape === undefined) {
    return fault('registry-bad');
  }

  return olderProcess(shape.file, seen.start) ? fault('registry-stale') : { kind: 'valid', ...shape.fields };
}

/** The registry file as an object and its own fields, or nothing when it is not a session's registry. */
function shapeOf(text: string, pid: number): { file: Record<string, unknown>; fields: { sessionId: string; cwd: string; name: string } } | undefined {
  const file = jsonObject(text);
  const fields = file && fieldsOf(file, pid);

  return file !== undefined && fields !== undefined ? { file, fields } : undefined;
}

/** The registry's own fields, or nothing when one is not what a session's registry holds. */
function fieldsOf(file: Record<string, unknown>, pid: number): { sessionId: string; cwd: string; name: string } | undefined {
  const { sessionId, cwd, name } = file;
  if (!isMine(file['pid'], pid) || !isId(sessionId) || !isAbsoluteLine(cwd)) {
    return undefined;
  }

  return { sessionId, cwd, name: nameOf(name) };
}

/** A registry file with no `pid` is not contradicted; one with another pid is not this process's. */
const isMine = (value: unknown, pid: number): boolean => value === undefined || value === pid;

/** The registry's name when it is one line of at most 500 units, else none. */
const nameOf = (name: unknown): string => (isTerminalName(name) ? name : '');

const isId = (value: unknown): value is string => typeof value === 'string' && isSessionId(value);

/** An absolute folder that is one line of at most 500 units (`isTerminalName`). */
const isAbsoluteLine = (value: unknown): value is string => typeof value === 'string' && value.startsWith('/') && isTerminalName(value);

/** Whether the file's `procStart` and the frame's start time are both digits and differ — then the file is an older process's. */
const olderProcess = (file: Record<string, unknown>, start: string): boolean =>
  typeof file['procStart'] === 'string' && DIGITS.test(file['procStart']) && DIGITS.test(start) && file['procStart'] !== start;

/** Rows 5 to 7 for one pane and at most one `claude`. */
function single(ctx: Ctx): RemoteRead {
  const registry = ctx.cands[0]?.registry;

  return registry === undefined ? withoutRegistry(ctx, undefined) : registryRoad(ctx, registry);
}

function registryRoad(ctx: Ctx, registry: Registry): RemoteRead {
  if (registry.kind === 'fault') {
    return fail(registry.why);
  }

  return registry.kind === 'valid' ? viaRegistry(ctx, registry) : withoutRegistry(ctx, undefined);
}

/** The `COAI-TRANSCRIPT` record, or a fault: the script prints one whenever it chose a transcript. */
const transcriptRec = (recs: readonly Rec[]): Rec => first(recs, 'COAI-TRANSCRIPT');

/** How many files carry that id: none, one, or several. */
function filesOf(transcript: Rec): 'none' | 'one' | 'many' {
  const files = Number(transcript.words[1]);
  if (files === 0) {
    return 'none';
  }

  return files === 1 ? 'one' : 'many';
}

/** Row 6: the registry names the session, and the transcript read is the registry's. */
function viaRegistry(ctx: Ctx, registry: Extract<Registry, { kind: 'valid' }>): RemoteRead {
  const transcript = transcriptRec(ctx.recs);

  return transcript.words[0] === registry.sessionId ? registryFound(ctx.recs, transcript, registry) : fail('id-mismatch');
}

function registryFound(recs: readonly Rec[], transcript: Rec, registry: Extract<Registry, { kind: 'valid' }>): RemoteRead {
  const files = filesOf(transcript);
  if (files !== 'one') {
    return files === 'none' ? ABSENT_TRANSCRIPT : MANY_TRANSCRIPTS;
  }
  const read = foundRead(recs, 'registry', registry.sessionId, registry);

  return projectDirNames(registry.cwd).includes(read.folder) ? read : fail('folder');
}

type Found = Extract<RemoteRead, { readonly kind: 'found' }>;

/** The tail of the transcript as a `found`. A cut tail loses its first line: it is a part of a line. */
function foundRead(recs: readonly Rec[], via: Found['via'], sessionId: string, who: { readonly cwd: string; readonly name: string }): Found {
  const file = first(recs, 'COAI-FILE');
  const tail = first(recs, 'COAI-TAIL');
  const biggest = Math.max(Number(file.words[0]), Number(first(recs, 'COAI-FILE-END').words[0]));
  const cut = biggest > TAIL_MAX;
  const lines = (cut ? tail.data.slice(1) : tail.data).filter((line) => line.length > 0);

  return { kind: 'found', via, sessionId, cwd: who.cwd, name: who.name, folder: file.data[0], lines, cut };
}

/** Row 7 (and row 8's "as row 7"): no registry file to go by — the fallback for a session call, the file of the picked id for an id call. */
function withoutRegistry(ctx: Ctx, known: { readonly cwd: string; readonly name: string } | undefined): RemoteRead {
  return ctx.call.kind === 'session' ? fallbackRead(ctx.recs) : byId(ctx.recs, ctx.call.id, known);
}

function byId(recs: readonly Rec[], id: string, known: { readonly cwd: string; readonly name: string } | undefined): RemoteRead {
  const transcript = transcriptRec(recs);

  return transcript.words[0] === id ? byIdFiles(recs, transcript, id, known ?? { cwd: '', name: '' }) : fail('frame');
}

function byIdFiles(recs: readonly Rec[], transcript: Rec, id: string, who: { readonly cwd: string; readonly name: string }): RemoteRead {
  const files = filesOf(transcript);
  if (files === 'none') {
    return { kind: 'gone', sessionId: id };
  }

  return files === 'many' ? MANY_TRANSCRIPTS : foundRead(recs, 'id', id, who);
}

/** The first call of the fallback: the pane's folder and the folder names. */
function fallbackRead(recs: readonly Rec[]): RemoteRead {
  const paneCwd = first(recs, 'COAI-CWD').data[0];
  const folders = first(recs, 'COAI-FOLDERS').data;
  if (!paneCwd.startsWith('/') || !folders.every(isFolderName)) {
    return fail('frame');
  }
  const end = first(recs, 'COAI-FOLDERS-END');

  return { kind: 'fallback', paneCwd, folders, skipped: Number(end.words[0]), more: end.words[1] === '1' };
}

/** A candidate whose registry file is valid, as the pick list shows it. */
const namedCandidate = (seen: Seen): RemoteCandidate[] =>
  seen.registry.kind === 'valid'
    ? [{ pane: seen.pane, pid: seen.pid, sessionId: seen.registry.sessionId, name: seen.registry.name, cwd: seen.registry.cwd }]
    : [];

/** Row 8: several panes or several processes. A `session` call gets the list; an `id` call may be settled by the pick. */
function several(ctx: Ctx): RemoteRead {
  const candidates = ctx.cands.flatMap(namedCandidate);
  const list: Ambiguous = {
    kind: 'ambiguous',
    why: ctx.paneCount >= 2 ? 'panes' : 'processes',
    candidates,
    unnamed: ctx.cands.length - candidates.length,
  };

  return ctx.call.kind === 'session' ? list : picked(ctx, ctx.call.id, list);
}

type Ambiguous = Extract<RemoteRead, { readonly kind: 'ambiguous' }>;

/**
 * An `id` call among several candidates. Settled by the pick when it is a named candidate, or when an unnamed candidate may
 * be it, or when nothing is named. Else every candidate is named and none is the pick: the pick is not one of the live
 * sessions, so the list again.
 */
function picked(ctx: Ctx, id: string, list: Ambiguous): RemoteRead {
  const pick = list.candidates.find((candidate) => candidate.sessionId === id);
  const settled = pick !== undefined || list.unnamed > 0 || list.candidates.length === 0;

  return settled ? byId(ctx.recs, id, pick) : list;
}

/** The `titles` call: each folder, its sessions newest first, and how the scan ended. */
function titlesRead(recs: readonly Rec[], call: Extract<RemoteCall, { readonly kind: 'titles' }>): RemoteRead {
  recs.filter((rec) => rec.marker === 'COAI-FOLDER').forEach((rec) => {
    if (!call.folders.includes(rec.words[0])) {
      fail('frame');
    }
  });
  const scan = first(recs, 'COAI-SCAN');
  const rows = recs.filter((rec) => rec.marker === 'COAI-ROWS');

  return {
    kind: 'titles',
    sessions: recs.flatMap((rec, index) => (rec.marker === 'COAI-SESSION' ? [sessionAt(recs, index)] : [])),
    scan: {
      why: scan.words[1] === 'count' || scan.words[1] === 'time' ? scan.words[1] : 'all',
      tooLong: rows.reduce((sum, row) => sum + Number(row.words[0]), 0),
      more: rows.some((row) => row.words[1] === '1'),
    },
  };
}

/** The folder marker before a record: a session belongs to it. */
function folderBefore(recs: readonly Rec[], index: number): string {
  for (let before = index - 1; before >= 0; before -= 1) {
    if (recs[before].marker === 'COAI-FOLDER') {
      return recs[before].words[0];
    }
  }

  return fail('frame');
}

const lacksRows = (recs: readonly Rec[], index: number): boolean => recs[index + 1]?.marker !== 'COAI-ROWS';

/** One session of the scan. A session that was read has its `COAI-ROWS` line right after its rows. */
function sessionAt(recs: readonly Rec[], index: number): TitledSession {
  const rec = recs[index];
  const skipped = rec.words[3] === 'skipped';
  if (!skipped && lacksRows(recs, index)) {
    return fail('frame');
  }

  return { sessionId: rec.words[0], folder: folderBefore(recs, index), mtime: mtimeOf(rec.words[1]), names: skipped ? NO_NAMES : namesOf(rec.data), skipped };
}

/** Seconds, or 0 when the VM had no `stat -c` (the script says `-`). */
const mtimeOf = (word: string): number => (word === '-' ? 0 : Number(word));

/** The names a session's title rows give, by the one rule (`collectNames`). */
function namesOf(rows: readonly string[]): SessionNames {
  const collected = collectNames();
  rows.forEach((row) => collected.take(row));

  return collected.names();
}
