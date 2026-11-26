import { humanSaid, lastAsked } from './claudeQuestion';
import { namedAmong, projectDirNames } from './claudeSessions';
import { NOTHING_OF_YOURS, promptsIn } from './sessionPrompts';
import { CredsName, credsSession } from './credsSession';
import { oneAtATime } from './oneAtATime';
import { withoutControls } from './remoteAccountsReply';
import { RemoteCandidate, RemoteRead, TitledSession, UNREADABLE_SAID, Unreadable, readRemote } from './remoteFrame';
import { FOLDERS_MAX, REMOTE_SCRIPT, RemoteCall, isFolderName, operandsOf } from './remoteScript';
import type { HostStanding } from './remoteHosts';
import { HOSTS_SAID } from './remoteHostsSaid';
import type { PermissionAsked, SessionAnswer } from './remotePermission';
import type { RemoteRun } from './remoteSsh';
import { LiveTerminal, NamedTerminal, oneLiveNamed } from './terminalSource';

/**
 * One road from a terminal to its remote Claude session (E2.S3, contract C4).
 *
 * <p>Pure: every effect — the person's answer, the answer store, `runRemote`, the pick list, the progress line, the terminal's
 * live name — is a SEAM, so the whole road runs in a unit test. `remoteSessionHost.ts` only supplies the `vscode` seams. Nothing
 * else in the extension spawns ssh or reads the consent.</p>
 *
 * <p><b>The order</b> is fixed: the terminal's name (`credsSession`), then — just before EACH spawn — the one door (`may`, E2.S1; only the FIRST
 * door of a press may ask the one question, and that press reads nothing), a re-check that the terminal is still open and still has the name,
 * then `runRemote`, then `readRemote` over the same `RemoteCall` that made the operands, then `nextStep`. At most three calls in one press, and a
 * list whenever more than one session is possible: nothing is chosen by itself. What a cut tail MEANS is decided once, in
 * `answerFrom`, by the purpose.</p>
 *
 * <p>No `console` and no `notify` here, and no sentence holds stdout: the sentences name a host, a tmux session, a session id the
 * person chose, the pane's folder (control characters taken out) and a fixed clause of `UNREADABLE_SAID`.</p>
 */

/** What the road is read for: the waiting question, or the prompts a person wrote (the Asked note). */
export type Purpose = 'question' | 'prompts';

/**
 * The id the person picked for a terminal, and the folders a `found` with `via: 'id'` may come from (X9b). It carries the host and the
 * tmux session it was picked for: a terminal renamed to another `Creds: <host> / <session>` does not send it to the new host.
 */
export interface Picked {
  readonly id: string;
  readonly folders: readonly string[];
  readonly host: string;
  readonly session: string;
}

export type RemoteAnswer =
  /** `note` is the cut note for *Asked*, `''` otherwise. */
  | { readonly kind: 'lines'; readonly lines: readonly string[]; readonly note: string }
  | { readonly kind: 'refused'; readonly refusal: string };

type Found = Extract<RemoteRead, { readonly kind: 'found' }>;

const refuse = (refusal: string): RemoteAnswer => ({ kind: 'refused', refusal });

/** The longest piece of a VM's text a sentence quotes, in characters. */
const SHOWN_MAX = 80;

/** Text from the VM with its control and format characters (bidi marks, zero-width space) taken out: what a person reads is what is there. */
const plainOf = (text: string): string => withoutControls(text);

/** Text from the VM for a sentence: control and format characters taken out, cut with `…`. */
function shown(text: string): string {
  const plain = [...plainOf(text)];

  return plain.length > SHOWN_MAX ? `${plain.slice(0, SHOWN_MAX).join('')}…` : plain.join('');
}

/** Every sentence of the road. A function of what it names; none holds stdout. */
const SAID = {
  notCreds: 'This is not a creds terminal, so it has no Claude session to read.',
  turnedOff: (host: string): string =>
    `Reading the Claude session on “${host}” is turned off. Only the text you copied can be sent; turn it on in CoAI: hosts read over ssh.`,
  cancelled: 'No session was chosen, so nothing was read.',
  stillReading: 'The Claude session of this terminal is still being read — the second press was ignored.',
  renamed: 'The terminal’s name changed, or it was closed, so nothing was read. Press again.',
  folder: (host: string): string =>
    `The session file chosen for this terminal is not in a folder of that session on “${host}”, so it was not read.`,
  gone: (host: string, id: string): string =>
    `The session you chose for this terminal (${id}) is no longer on “${host}”. Press again to look for it.`,
  tooMany: (host: string): string =>
    `The Claude session on “${host}” could not be settled in three reads, so nothing was read. Press again.`,
  absentSession: (host: string, session: string): string =>
    `There is no tmux session called “${session}” on “${host}” that ssh can see, so nothing was read.`,
  absentTranscript: (host: string, session: string): string =>
    `The Claude session of tmux session “${session}” on “${host}” has no session file, so there is nothing to read.`,
  unreadable: (host: string, session: string, why: Unreadable): string =>
    `The Claude session of tmux session “${session}” on “${host}” could not be read: ${UNREADABLE_SAID[why]}.`,
  ambiguousNone: (host: string, session: string): string =>
    `More than one Claude session may run in tmux session “${session}” on “${host}”, and none of them could be told apart by name, so nothing was chosen.`,
  noFolder: (host: string, session: string, paneCwd: string, partial: boolean): string =>
    `No Claude project folder was found for the working folder “${shown(paneCwd)}” of tmux session “${session}” on “${host}”, so its session was not read.${partial ? ' Some folders could not be listed.' : ''}`,
  noTitle: (host: string, session: string, partial: boolean): string =>
    `No Claude session on “${host}” has the name of tmux session “${session}”.${partial ? ' Not every session was read, so one of that name may still exist.' : ''}`,
  truncated: (host: string): string =>
    `No question was found in the last 4 MiB of the session on “${host}” that were read; an older one may be beyond the cut.`,
  cutNote: 'Only the last 4 MiB of this session were read, so its first prompts are not shown.',
  notOpen: (name: string): string => `The terminal “${shown(name)}” is not open — open it to read its Claude session.`,
  several: (count: number, name: string): string =>
    `${count} terminals are called “${shown(name)}” — open the one you mean and press again.`,
};

// ─────────────────────────────── The consent ───────────────────────────────

/**
 * Whether the terminal's two overflow items are shown (the context key `coai.terminalReadsSession`): a name that starts with
 * `Creds: ` and whose host is not answered "no" — in the refusals file, in the list, or in what waits to be saved (`standings`). A name that is not well made is TRUE on
 * purpose — its press says which part failed — and a name that does not start so is false. A damaged list reads as no answer, so
 * the items stay.
 */
export function readsKey(name: string, standings: readonly HostStanding[]): boolean {
  const creds = credsSession(name);

  return creds.kind === 'malformed' || (creds.kind === 'session' && !standings.some((one) => one.host === creds.host && one.answer === 'no'));
}

// ─────────────────────────────── The identity ───────────────────────────────

/** One row of the pick list. The host draws it; the order (newest first) and the folders it carries are decided here. */
export interface PickRow {
  readonly sessionId: string;
  readonly label: string;
  readonly description: string;
  readonly detail: string;
  /** Seconds; 0 when it is not known. */
  readonly at: number;
  /** The folders a `found` by this id may come from. */
  readonly folders: readonly string[];
}

/** What the road does next, decided from one answer of the VM. */
export type Step =
  /** `drop`: the picked id, if there is one, is dropped. */
  | { readonly kind: 'done'; readonly found: Found; readonly drop: boolean }
  | { readonly kind: 'fallback-titles'; readonly folders: readonly string[] }
  | { readonly kind: 'fallback-tail'; readonly picked: Picked }
  /** `partial`: the rows come from a scan that did not read every session, so the list may lack the right one. */
  | { readonly kind: 'pick'; readonly rows: readonly PickRow[]; readonly partial: boolean }
  | { readonly kind: 'refused'; readonly refusal: string; readonly drop: boolean };

/** Where a press reads: the host and tmux session its terminal's name gives. */
export interface Where {
  readonly host: string;
  readonly session: string;
}

interface Context extends Where {
  readonly picked: Picked | undefined;
}

const stop = (refusal: string, drop = false): Step => ({ kind: 'refused', refusal, drop });

/** A table of one function for each `kind` of a union, so a dispatch is a lookup and a new arm is a compile error. */
type Table<T extends { readonly kind: string }, A extends readonly unknown[], R> =
  { readonly [K in T['kind']]: (value: Extract<T, { readonly kind: K }>, ...args: A) => R };

function through<T extends { readonly kind: string }, A extends readonly unknown[], R>(table: Table<T, A, R>, value: T, ...args: A): R {
  // The one place a correlated union is narrowed by hand: the table's type has already proved each entry takes its own arm.
  return (table[value.kind as T['kind']] as (value: T, ...args: A) => R)(value, ...args);
}

const labelOf = (name: string, id: string): string => plainOf(name) || id.slice(0, 8);

const timeOf = (seconds: number): string => (seconds > 0 ? new Date(seconds * 1000).toISOString() : 'time unknown');

/** Newest first; a stable sort, so rows with no time keep the order the VM gave. */
const newestFirst = (rows: readonly PickRow[]): readonly PickRow[] => [...rows].sort((a, b) => b.at - a.at);

const candidateRow = (one: RemoteCandidate): PickRow => ({
  sessionId: one.sessionId, label: labelOf(one.name, one.sessionId), description: plainOf(one.cwd), detail: `tmux pane ${plainOf(one.pane)}`, at: 0, folders: projectDirNames(one.cwd),
});

const titledRow = (one: TitledSession): PickRow => ({
  sessionId: one.sessionId, label: labelOf(one.names.latest, one.sessionId), description: plainOf(one.folder), detail: `${timeOf(one.mtime)} · ${one.sessionId}`, at: one.mtime, folders: [one.folder],
});

/** The registry road answers `done` and drops the pick; a file found by id is read only from a folder the pick came with (J9). */
function foundStep(read: Found, context: Context): Step {
  if (read.via === 'registry') {
    return { kind: 'done', found: read, drop: true };
  }

  return context.picked?.folders.includes(read.folder) === true ? { kind: 'done', found: read, drop: false } : stop(SAID.folder(context.host));
}

/** The folders of the pane's working folder that the VM has, at most three, in `projectDirNames` order. */
const foldersFor = (paneCwd: string, listed: readonly string[]): readonly string[] =>
  projectDirNames(paneCwd).filter((name) => listed.includes(name) && isFolderName(name)).slice(0, FOLDERS_MAX);

function fallbackStep(read: Extract<RemoteRead, { readonly kind: 'fallback' }>, context: Context): Step {
  const folders = foldersFor(read.paneCwd, read.folders);

  return folders.length > 0
    ? { kind: 'fallback-titles', folders }
    : stop(SAID.noFolder(context.host, context.session, read.paneCwd, read.skipped > 0 || read.more));
}

/** A scan that did not read everything: its end was not `all`, it had more to say, or a session's names were not read. */
const notEverything = (read: Extract<RemoteRead, { readonly kind: 'titles' }>): boolean =>
  read.scan.why !== 'all' || read.scan.more || read.sessions.some((one) => one.skipped);

/**
 * The one session of the name, read by itself only when the scan read every session: in a cut scan the right one may be
 * among the sessions not read, so even one is offered as a list that says so (plan entry 3.1, the feature review 2026-11-17).
 */
const readAlone = <T>(named: readonly T[], partial: boolean): T | undefined => (partial || named.length !== 1 ? undefined : named[0]);

function titlesStep(read: Extract<RemoteRead, { readonly kind: 'titles' }>, context: Context): Step {
  const named = namedAmong(read.sessions, (one) => one.names, context.session);
  const partial = notEverything(read);
  const only = readAlone(named, partial);
  if (only !== undefined) {
    return { kind: 'fallback-tail', picked: { id: only.sessionId, folders: [only.folder], host: context.host, session: context.session } };
  }

  return named.length > 0
    ? { kind: 'pick', rows: newestFirst(named.map(titledRow)), partial }
    : stop(SAID.noTitle(context.host, context.session, partial));
}

/** Candidates are ALWAYS a list, even of one: nothing is chosen by itself. */
const ambiguousStep = (read: Extract<RemoteRead, { readonly kind: 'ambiguous' }>, context: Context): Step =>
  (read.candidates.length > 0
    ? { kind: 'pick', rows: newestFirst(read.candidates.map(candidateRow)), partial: false }
    : stop(SAID.ambiguousNone(context.host, context.session)));

const ARMS: Table<RemoteRead, readonly [Context], Step> = {
  found: foundStep,
  fallback: fallbackStep,
  titles: titlesStep,
  gone: (read, context) => stop(SAID.gone(context.host, read.sessionId), true),
  absent: (read, context) => stop(read.what === 'session' ? SAID.absentSession(context.host, context.session) : SAID.absentTranscript(context.host, context.session)),
  ambiguous: ambiguousStep,
  unreadable: (read, context) => stop(SAID.unreadable(context.host, context.session, read.why)),
};

/** What to do with one answer of the VM, by its arm. Never more than three calls is the road's rule, not this function's. */
export function nextStep(read: RemoteRead, picked: Picked | undefined, where: Where): Step {
  return through(ARMS, read, { host: where.host, session: where.session, picked });
}

/**
 * What a cut tail means for this purpose — the ONE place it is decided. For a question, a cut tail that holds no
 * `AskUserQuestion` is not "nothing was asked": an older one may be beyond the cut. For prompts the cut is a note.
 */
export function answerFrom(found: Found, purpose: Purpose, host: string): RemoteAnswer {
  return purpose === 'prompts' ? { kind: 'lines', lines: found.lines, note: found.cut ? SAID.cutNote : '' } : questionFrom(found, host);
}

/** A cut tail with no question in it says so: the person is not told "nothing was asked" about a session that was only half read. */
function questionFrom(found: Found, host: string): RemoteAnswer {
  return found.cut && lastAsked(found.lines).kind === 'nothing' ? refuse(SAID.truncated(host)) : { kind: 'lines', lines: found.lines, note: '' };
}

/**
 * What *Asked* shows for a terminal's conversation, from the road's answer: what the person wrote in the lines that were read — the
 * local reader's own rule, over lines — and the answer's note beside it. A read of only the last part of a session that holds no
 * prompt says so by the note, and never that nothing was written; a refusal of the road is handed on as it is.
 */
export function promptsSaid(answer: RemoteAnswer): { readonly asked: readonly string[]; readonly refusal: string } {
  if (answer.kind === 'refused') {
    return { asked: [], refusal: answer.refusal };
  }
  const asked = promptsIn(answer.lines, humanSaid);

  return { asked, refusal: answer.note.length > 0 ? answer.note : asked.length > 0 ? '' : NOTHING_OF_YOURS };
}

// ─────────────────────────────────── The road ───────────────────────────────────

/** The effects of the road. The host supplies the real ones; a test supplies fakes. */
export interface RoadSeams {
  readonly store: {
    /** The one door, asked before EVERY spawn of a press (E2.S1, § P9). It reads the side afresh and never asks a person. */
    readonly may: (host: string) => PromiseLike<SessionAnswer>;
  };
  /** The ONE question. Called only at the first door of a press, and only on `no-permission`; that press then spawns nothing. */
  readonly askPermission: () => PromiseLike<PermissionAsked>;
  /** The terminal's LIVE name, or `undefined` when it is no longer open. Asked just before EACH spawn. */
  readonly liveName: () => string | undefined;
  /** `runRemote` — S2 owns it, and nothing else here spawns. */
  readonly run: (host: string, session: string, operands: readonly string[], script: string) => PromiseLike<RemoteRun>;
  /** The list: the chosen `sessionId`, or `undefined` for a cancel. */
  /** `partial`: not every session was read, so the list may lack the right one and must say so. */
  readonly pick: (rows: readonly PickRow[], partial: boolean) => PromiseLike<string | undefined>;
  /** The progress line around one spawn. */
  readonly progress: <T>(host: string, work: () => PromiseLike<T>) => PromiseLike<T>;
  /** The terminal's picked id changed: set, or `undefined` for dropped. */
  readonly keep: (picked: Picked | undefined) => void;
}

/** A press in flight. Immutable: a later leg is a copy with its own `picked` and `calls`. */
interface Trip extends Where {
  readonly seams: RoadSeams;
  readonly purpose: Purpose;
  readonly picked: Picked | undefined;
  /** The spawns this press has made. */
  readonly calls: number;
}

/** The most spawns in one press: the registry path, the titles of the folders, the tail by id. */
const CALLS_MAX = 3;

/** What the door's answer says when it is not `yes`: the sentence of that answer, for a host and the press's place (`later`: not the first door). */
const SENTENCES: Readonly<Record<Exclude<SessionAnswer, 'yes' | 'no-permission'>, (host: string) => string>> = {
  no: (host) => SAID.turnedOff(host),
  damaged: () => HOSTS_SAID.cannotRead,
  waiting: () => HOSTS_SAID.waiting,
};

/** The first door, `no-permission`: the ONE question, and a sentence whatever it is answered — this press reads nothing (W15). */
async function questioned(seams: RoadSeams): Promise<string> {
  const answer = await seams.askPermission();

  return answer === 'given' ? HOSTS_SAID.permissionGiven : answer === 'not-now' ? HOSTS_SAID.notNow : answer.said;
}

/** The door asked before one spawn: `undefined` on `yes`, else the sentence that ends the press. Only the first door of a press may ask the question. */
async function doorSaid(trip: Trip): Promise<string | undefined> {
  const answer = await trip.seams.store.may(trip.host);
  if (answer === 'yes') {
    return undefined;
  }
  if (answer === 'no-permission') {
    return trip.calls === 0 ? questioned(trip.seams) : HOSTS_SAID.withdrawnMeanwhile;
  }

  return SENTENCES[answer](trip.host);
}

/** Whether the terminal is still open and its live name still gives this host and session. */
function stillThere(seams: RoadSeams, where: Where): boolean {
  const live = credsSession(seams.liveName() ?? '');

  return live.kind === 'session' && live.host === where.host && live.session === where.session;
}

/**
 * One spawn: the door FIRST (E2.S1: before every spawn, so a "no" or a withdrawal saved during an earlier one stops the next), then the re-check — the
 * door can take time and the terminal may have been renamed meanwhile — then `runRemote` under the progress line, then the parser over the SAME call.
 */
async function readOnce(trip: Trip, call: RemoteCall): Promise<RemoteRead | string> {
  const refusal = await doorSaid(trip);
  if (refusal !== undefined) {
    return refusal;
  }
  if (!stillThere(trip.seams, trip)) {
    return SAID.renamed;
  }
  const run = await trip.seams.progress(trip.host, () => trip.seams.run(trip.host, trip.session, operandsOf(call), REMOTE_SCRIPT));

  return run.kind === 'refused' ? run.refusal : readRemote(run, call);
}

const idCall = (trip: Trip, id: string): RemoteCall => ({ kind: 'id', session: trip.session, id });

/** The picked id is dropped only when a step says so; and only when there is one to drop. */
function forget(trip: Trip, drop: boolean): void {
  if (drop && trip.picked !== undefined) {
    trip.seams.keep(undefined);
  }
}

function remember(trip: Trip, picked: Picked): Trip {
  trip.seams.keep(picked);

  return { ...trip, picked };
}

/** The next spawn, unless this press has made its three. */
const within = (trip: Trip, next: () => Promise<RemoteAnswer>): Promise<RemoteAnswer> =>
  (trip.calls >= CALLS_MAX ? Promise.resolve(refuse(SAID.tooMany(trip.host))) : next());

async function chosen(trip: Trip, rows: readonly PickRow[], partial: boolean): Promise<RemoteAnswer> {
  const sessionId = await trip.seams.pick(rows, partial);
  const row = rows.find((one) => one.sessionId === sessionId);

  return row === undefined ? refuse(SAID.cancelled) : leg(remember(trip, { id: row.sessionId, folders: row.folders, host: trip.host, session: trip.session }), idCall(trip, row.sessionId));
}

const FOLLOW: Table<Step, readonly [Trip], Promise<RemoteAnswer>> = {
  'done': async (step, trip) => {
    forget(trip, step.drop);

    return answerFrom(step.found, trip.purpose, trip.host);
  },
  'refused': async (step, trip) => {
    forget(trip, step.drop);

    return refuse(step.refusal);
  },
  'fallback-titles': (step, trip) => within(trip, () => leg(trip, { kind: 'titles', session: trip.session, folders: step.folders })),
  'fallback-tail': (step, trip) => within(trip, () => leg(remember(trip, step.picked), idCall(trip, step.picked.id))),
  'pick': (step, trip) => within(trip, () => chosen(trip, step.rows, step.partial)),
};

/** One call of the press and what follows from its answer. */
async function leg(trip: Trip, call: RemoteCall): Promise<RemoteAnswer> {
  const read = await readOnce(trip, call);
  if (typeof read === 'string') {
    return refuse(read);
  }

  return through(FOLLOW, nextStep(read, trip.picked, trip), { ...trip, calls: trip.calls + 1 });
}

/** The sentence for a name that is not a session: the malformed name's own, or "not a creds terminal". */
const notSession = (name: Exclude<CredsName, { readonly kind: 'session' }>): string =>
  (name.kind === 'malformed' ? name.refusal : SAID.notCreds);

/** The kept id, only when it was picked for THIS host and session. Another one is not sent and not dropped: it stays for the terminal it belongs to. */
const pickedFor = (picked: Picked | undefined, where: Where): Picked | undefined =>
  (picked?.host === where.host && picked.session === where.session ? picked : undefined);

/** The first call of a press: the registry path, or — when this terminal kept an id — that id. */
const firstCall = (trip: Trip): RemoteCall => (trip.picked === undefined ? { kind: 'session', session: trip.session } : idCall(trip, trip.picked.id));

/**
 * The road from a terminal's name to the lines of its remote session, or the sentence that says why not.
 *
 * <p>`picked` is the id this terminal kept from an earlier press, for the host and session it was picked for: the first call sends it. It is changed ONLY through
 * `seams.keep` — set on a pick, dropped on a registry success and on `gone`, kept through everything else.</p>
 */
export async function remoteRoad(seams: RoadSeams, terminalName: string, purpose: Purpose, picked?: Picked): Promise<RemoteAnswer> {
  const name = credsSession(terminalName);
  if (name.kind !== 'session') {
    return refuse(notSession(name));
  }
  const trip: Trip = { seams, host: name.host, session: name.session, purpose, picked: pickedFor(picked, name), calls: 0 };

  return leg(trip, firstCall(trip));
}

/** One read per terminal at a time: a second press while one runs is answered with a sentence, and starts nothing. */
export function onePerTerminal<K extends object>(): (key: K, work: () => PromiseLike<RemoteAnswer>) => Promise<RemoteAnswer> {
  const latches = new WeakMap<K, (work: () => PromiseLike<RemoteAnswer>) => Promise<RemoteAnswer>>();

  return (key, work) => {
    const latch = latches.get(key) ?? oneAtATime<RemoteAnswer>(refuse(SAID.stillReading));
    latches.set(key, latch);

    return latch(work);
  };
}

// ─────────────────────────────────── The reach ───────────────────────────────────

/** The terminal a conversation reads, with its LIVE name; or why there is none. */
export type Reach =
  | { readonly kind: 'terminal'; readonly key: object; readonly name: string }
  | { readonly kind: 'refused'; readonly refusal: string };

const missing = (named: NamedTerminal, recorded: string): Reach => ({
  kind: 'refused',
  refusal: named.kind === 'several' ? SAID.several(named.count, recorded) : SAID.notOpen(recorded),
});

/**
 * Which live terminal a conversation reads (for *Asked*): the one it is keyed by while that is open and has not exited — under
 * its NEW name if it was renamed — else the ONE live terminal with the RECORDED name. The host is always read from a live
 * terminal's name, never from the record.
 */
export function reachOf(live: readonly LiveTerminal[], keyed: object | undefined, recorded: string): Reach {
  const mine = live.find((one) => keyed !== undefined && one.key === keyed && !one.exited);
  if (mine !== undefined) {
    return { kind: 'terminal', key: mine.key, name: mine.name };
  }
  const named = oneLiveNamed(live, recorded);

  return named.kind === 'one' ? { kind: 'terminal', key: named.key, name: recorded } : missing(named, recorded);
}
