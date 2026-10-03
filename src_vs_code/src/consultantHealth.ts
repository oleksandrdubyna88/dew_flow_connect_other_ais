/**
 * The Consultant tab's source of truth, as the SERVER states it — types, parsing and wording, and nothing else.
 *
 * <p>Epic 5 (E5.1) of `research/PLAN_the_consultant_works_on_every_vendor.md`. The panel owns none of these facts:
 * `coai-mcp --consultants` says what each caller kind's consultant resolves to, what its CLI said about itself and
 * what it can be kept from reading on this side; `--check-consultant` runs one paid turn and says what came of it;
 * and both leave files under `consultations/health/` so a window on the OTHER side of the Windows/WSL seam can read
 * them through `coai.alsoWatchDataDirectories`. This module reads those answers and puts words to them.</p>
 *
 * <p>Pure and `vscode`-free, like `providers.ts`, and for the same reason: the page bundle imports it, so a spawn or
 * a file read here would drag `node:*` into the webview. `consultantsProbe.ts` spawns, `consultantCheckRun.ts`
 * runs a check, and `consultantHealthRead.ts` reads the files through ports.</p>
 *
 * <p><b>Absent is not empty.</b> The two halves ship separately. A server from before `--consultants` exits 64, and a
 * body without the `consultants` field came from a server that predates it — both are "update the MCP server",
 * never an empty tab. An EMPTY list is an answer. A check file without `heartbeatStaleAfterSeconds` came from a
 * server before epic 5, and is read as "cannot tell", never as zero.</p>
 */

/**
 * How large a health file may be before it is refused unparsed.
 *
 * <p>`consultants.json` is two to six kilobytes and a check record about two; a file a hundred times that is not
 * one the server wrote, and the other side's store is read across a network filesystem, so the cap is applied to
 * the BYTES, before anything is decoded or parsed.</p>
 */
export const HEALTH_FILE_CAP_BYTES = 256 * 1024;

/**
 * A check on the OTHER side whose liveness cannot be judged from here — its file carries no staleness, because the
 * server that wrote it predates epic 5. Never shown as a spinner, and never judged by a number the panel made up.
 */
export const LIVENESS_UNKNOWN = 'liveness-unknown';

/** The exit code that means "this binary has never heard of that mode" (`.agents/PROJECT.md`), and nothing else. */
export const MODE_UNKNOWN = 64;

/** What the CLI said about itself — never whether anyone is signed in. */
export interface ConsultantCli {
  /** The CLI was asked — false for a row that is not available, where nothing was run. */
  readonly probed: boolean;
  /** The executable could be started. */
  readonly found: boolean;
  readonly version: string;
  /** `own auth`, `vault key` or `unavailable` — where a credential would come from, not a sign-in. */
  readonly authSource: string;
  readonly note: string;
}

/** What this consultant can be kept from reading on the side that answered (`shared/consultant-limitations.json`). */
export interface ConsultantLimitation {
  /** `confined`, `unconfined`, `default-deny` or `unmeasured`. */
  readonly standing: string;
  readonly text: string;
  readonly capability: string;
  /** `measured` or `source` — which of the two kinds of backing the row has. */
  readonly evidence: string;
  readonly measuredDate: string;
  readonly measuredCliVersion: string;
  readonly measuredCells: string;
  readonly measuredDocument: string;
  readonly source: string;
}

/** Where agy reads its allow rules on that side, and on Linux/WSL a rule to paste with the warning that travels with it. */
export interface ConsultantAgy {
  readonly settingsPath: string;
  readonly snippet: string;
  readonly snippetWarning: string;
}

/** The last consultation a caller kind's consultant answered. */
export interface HealthAnswer {
  readonly utc: string;
  readonly consultationId: string;
  readonly vendor: string;
  readonly runtime: string;
  readonly model: string;
  readonly confinement: string;
}

/** The last consultation turn a caller kind's consultant failed, classified, with its cure. */
export interface HealthFailure {
  readonly utc: string;
  readonly consultationId: string;
  readonly vendor: string;
  readonly runtime: string;
  readonly model: string;
  /** A word of `shared/consult-failure-kinds.json`. */
  readonly kind: string;
  readonly what: string;
  readonly cure: string;
  /** Where the transcript was kept — a path on the side that WROTE it. */
  readonly evidence: string;
  readonly side: string;
  readonly confinement: string;
}

/** One consultant check: `<kind>.check.json`, and the one document `--check-consultant` prints. */
export interface CheckRecord {
  readonly callerKind: string;
  /** `checking`, `answered`, `failed`, `unavailable`, `already-checking`, `abandoned` or `unreadable`. */
  readonly state: string;
  readonly startedUtc: string;
  /** When the check will have ended whatever happens — the panel's kill cap is derived from it. */
  readonly deadlineUtc: string;
  readonly heartbeatUtc: string;
  readonly finishedUtc: string;
  /** The staleness the server PUBLISHES for its heartbeat — absent from a server before epic 5. */
  readonly heartbeatStaleAfterSeconds: number | undefined;
  readonly side: string;
  readonly vendor: string;
  readonly runtime: string;
  readonly model: string;
  readonly confinement: string;
  readonly answered: boolean;
  readonly markerRead: boolean;
  /** A word of {@link CANARY_READINGS}, or empty when the consultant answered nothing. */
  readonly canary: string;
  readonly deniedActions: readonly string[];
  readonly seconds: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly failureKind: string;
  readonly failureWhat: string;
  readonly failureCure: string;
  readonly evidence: string;
  readonly reason: string;
}

/** One caller kind's consultant — what it resolves to and everything the server knows about it. */
export interface ConsultantRow {
  readonly callerKind: string;
  readonly available: boolean;
  /** Empty when available; otherwise exactly the sentence `consult` would refuse with. */
  readonly reason: string;
  readonly vendor: string;
  readonly runtime: string;
  readonly model: string;
  readonly executablePath: string;
  readonly cli: ConsultantCli;
  /** For claude: `restricted`, `no-restricted` or `unknown`. Empty otherwise. */
  readonly capability: string;
  readonly limitation: ConsultantLimitation | undefined;
  readonly agy: ConsultantAgy | undefined;
  readonly lastAnswer: HealthAnswer | undefined;
  readonly lastFailure: HealthFailure | undefined;
  /**
   * The server's one rule (`ConsultHealth.Current`): the failure is THIS row's consultant's (vendor and model), and that
   * consultant has not answered since. The panel shows the verdict and decides nothing about it.
   */
  readonly failureCurrent: boolean;
  /**
   * Why a health file of this caller kind could not be read — empty when both read or are absent. A row with this set is
   * never shown as healthy: its outcome is not known. Absent from a server before the whole-branch review.
   */
  readonly healthUnreadable: string;
  /** The last check, settled by the server that answered — undefined when none was ever run. */
  readonly check: CheckRecord | undefined;
}

/** `--consultants`'s answer, and `consultants.json`'s contents. */
export interface ConsultantsReport {
  readonly utc: string;
  /** `windows`, `wsl`, `linux`, `macos` or `other`. */
  readonly side: string;
  readonly distro: string;
  readonly heartbeatStaleAfterSeconds: number | undefined;
  readonly consultants: readonly ConsultantRow[];
}

/** What one `--consultants` spawn came to. */
export type ConsultantsAnswer =
  | { readonly kind: 'answered'; readonly report: ConsultantsReport }
  /** Exit 64, or a body from before the `consultants` field: the cure is a version. */
  | { readonly kind: 'too-old' }
  | { readonly kind: 'unanswered'; readonly why: string };

/** A health file that was there: parsed, or refused with the reason. "Not there" is the caller's to say. */
export type HealthFile<T> = { readonly kind: 'found'; readonly value: T } | { readonly kind: 'unreadable'; readonly why: string };

/** A check as a reader must show it: the state word, why (for abandoned), and the record it came from. */
export interface JudgedCheck {
  readonly state: string;
  readonly reason: string;
  readonly record: CheckRecord;
}

/** What one Check press came to, read off the process — the host's half of `--check-consultant`. */
export type CheckRunResult =
  /** Exit 0 and one JSON document: every classified outcome, `unavailable` and `already-checking` included. */
  | { readonly kind: 'reported'; readonly record: CheckRecord }
  /** Exit 64: the binary has never heard of the mode. */
  | { readonly kind: 'too-old' }
  /** Exit 65: the request was refused. */
  | { readonly kind: 'refused'; readonly said: string }
  /** Anything else: killed at its cap, a crash, a body that is not a result. */
  | { readonly kind: 'crashed'; readonly why: string };

/**
 * `--consultants`'s exit code and stdout, read.
 *
 * <p>64 is a server too old for the mode. A JSON object WITHOUT a `consultants` array is a server from before the
 * field — the same cure. Anything else that is not exit 0 and an object is unanswered, and says the exit code.</p>
 */
export function parseConsultantsAnswer(code: number, output: string): ConsultantsAnswer {
  if (code === MODE_UNKNOWN) {
    return { kind: 'too-old' };
  }

  return code === 0 ? answerIn(output) : { kind: 'unanswered', why: `the server exited ${code} when it was asked about its consultants` };
}

/** Exit 0's body: a report, a body from before the field (too old), or something that is not an answer. */
function answerIn(output: string): ConsultantsAnswer {
  const parsed = cappedText(output);
  if (parsed.kind === 'unreadable') {
    return { kind: 'unanswered', why: `the server's answer ${parsed.why}` };
  }

  return Array.isArray(parsed.value['consultants']) ? { kind: 'answered', report: reportOf(parsed.value) } : { kind: 'too-old' };
}

/** `consultations/health/consultants.json`, read — capped, then parsed; a file without rows is not a report. */
export function parseConsultantsFile(bytes: Uint8Array): HealthFile<ConsultantsReport> {
  const parsed = cappedObject(bytes);
  if (parsed.kind === 'unreadable') {
    return parsed;
  }

  return Array.isArray(parsed.value['consultants'])
    ? { kind: 'found', value: reportOf(parsed.value) }
    : { kind: 'unreadable', why: 'holds no consultants — it was not written by a server that knows them' };
}

/** `<kind>.check.json`, read — capped, then parsed; a document with no state word is not a check. */
export function parseCheckFile(bytes: Uint8Array): HealthFile<CheckRecord> {
  const parsed = cappedObject(bytes);
  if (parsed.kind === 'unreadable') {
    return parsed;
  }
  const record = checkOf(parsed.value);

  return record === undefined ? { kind: 'unreadable', why: 'holds no check state' } : { kind: 'found', value: record };
}

/** The one document `--check-consultant` prints, or undefined when the output is not one. */
export function parseCheckDocument(output: string): CheckRecord | undefined {
  const parsed = cappedText(output);

  return parsed.kind === 'found' ? checkOf(parsed.value) : undefined;
}

/**
 * A check as it must be shown, from the side the record is read on.
 *
 * <p><b>This side:</b> the server settled it — by asking the lock, which only it can — so it is shown exactly as
 * given. A TypeScript reading of the heartbeat here would overrule the one witness that knows.</p>
 *
 * <p><b>The other side:</b> the lock does not cross the 9P seam, so a `checking` there is judged by its heartbeat —
 * against the staleness the server PUBLISHED in that same record, and by THIS window's own clock: `seenMs` is when this
 * window first saw the heartbeat hold its current value (the watcher keeps it, `withBeatsSeen`), and the check is
 * abandoned once that value has stood still for longer than the published margin. Never this clock minus the other
 * side's stamp: a WSL distribution keeps its own clock, which drifts from the Windows host's by more than the margin
 * (the whole-branch review, O; plan §2 item 11). A heartbeat this window has not seen yet counts as seen now. A record
 * with no published staleness came from a server before epic 5, and whether it still runs cannot be told from here:
 * {@link LIVENESS_UNKNOWN}, never a guess.</p>
 */
export function checkStateOf(record: CheckRecord, side: 'this' | 'other', nowMs: number, seenMs: number = nowMs): JudgedCheck {
  return side === 'other' && record.state === 'checking'
    ? acrossTheSeam(record, nowMs, seenMs)
    : { state: record.state, reason: record.reason, record };
}

/**
 * A `checking` THIS side's own server wrote, judged by its own stamp — the reader and the writer share one clock here
 * (the panel's extension host runs beside this side's server, in a Remote-WSL window as in a plain one). Used only for a
 * state file no server answer has settled yet (`consultantHealthState.ts`).
 */
export function checkStateOnThisDisk(record: CheckRecord, nowMs: number): JudgedCheck {
  const stale = record.heartbeatStaleAfterSeconds;
  if (record.state !== 'checking' || stale === undefined) {
    return checkStateOf(record, 'this', nowMs);
  }

  return beatSince(record.heartbeatUtc, nowMs - stale * 1000)
    ? { state: 'checking', reason: '', record }
    : { state: 'abandoned', reason: `the check stopped refreshing its heartbeat (last ${lastBeat(record.heartbeatUtc)}), so nothing is running it`, record };
}

/** Where a heartbeat value's first sighting is kept: the caller kind and the value, which changes on every beat. */
export function beatKey(kind: string, heartbeatUtc: string): string {
  return `${kind}|${heartbeatUtc}`;
}

/** A `checking` read across the seam: how long THIS window has seen its heartbeat stand still, against the staleness its server published. */
function acrossTheSeam(record: CheckRecord, nowMs: number, seenMs: number): JudgedCheck {
  const stale = record.heartbeatStaleAfterSeconds;
  if (stale === undefined) {
    return { state: LIVENESS_UNKNOWN, reason: '', record };
  }
  const stillFor = Math.max(0, nowMs - seenMs);

  return stillFor <= stale * 1000
    ? { state: 'checking', reason: '', record }
    : {
      state: 'abandoned',
      reason: `the check's side has not moved its heartbeat (last ${lastBeat(record.heartbeatUtc)}) for ${Math.round(stillFor / 1000)} s of this window's clock, so nothing is running it`,
      record,
    };
}

/** A heartbeat a reader can parse, no older than `sinceMs` — the server's `BeatSince`, for a stamp of this side's own clock. */
function beatSince(stamp: string, sinceMs: number): boolean {
  const beat = Date.parse(stamp);

  return Number.isFinite(beat) && beat >= sinceMs;
}

function lastBeat(stamp: string): string {
  return stamp.length > 0 ? utcShort(stamp) : 'never';
}

/**
 * The label of every failure word — the shared catalogue's own (`shared/consult-failure-kinds.json`).
 *
 * <p>A copy, because the page bundle cannot read a file; and asserted against that file word for word by
 * `consultantHealth.test.ts`, so a word added on one side and not this one goes red here rather than reaching a
 * person as a bare identifier.</p>
 */
const FAILURE_LABELS: Readonly<Record<string, string>> = {
  'command-denied': 'Shell command denied',
  'read-denied': 'Read outside the checkout denied',
  'empty': 'Answered nothing',
  'quota': 'Quota spent',
  'rate-limited': 'Rate limited',
  'vendor-refused': "Refused by the vendor's CLI",
  'cli-not-found': 'CLI not found',
  'timeout': 'Timed out',
  'deadline': 'Turn deadline passed',
  'exit': 'CLI exited with an error',
  'tree-changed': 'Working tree changed',
  'conversation-dropped': 'Conversation dropped by the vendor',
  'record-failed': 'Answer could not be recorded',
  'cancelled': 'Cancelled by the caller',
};

/** A failure word as a person reads it; a word this build does not know is NAMED, never dropped. */
export function failureLabel(kind: string): string {
  return FAILURE_LABELS[kind] ?? `A failure this panel does not know (“${kind}”)`;
}

/**
 * What became of the canary, worded — and only ONE reading is called observed confinement.
 *
 * <p>The cadence consultation's rule (435b1b25): a model that declines on its own proves compliance, not
 * confinement, and a refusal the CLI cannot attribute to the canary proves nothing about it. Only a refusal the CLI
 * RECORDED against the canary's own path is observed confinement; the test holds every other reading to not saying
 * so — and holds these readings to `shared/consult-check-words.json`, the catalogue the server is held to as well.</p>
 */
const CANARY_WORDS: Readonly<Record<string, string>> = {
  'read': 'LEAKED — the consultant read the canary file outside the checkout, so it is NOT confined on this side',
  'denied-by-cli': 'the CLI refused the read of the canary outside the checkout — observed confinement',
  'denied-by-cli-unattributed': 'the CLI refused something it does not name, so the refusal cannot be tied to the canary — not proof that reads are confined',
  'not-attempted': 'the consultant did not try to read the canary, or declined on its own — that is compliance, not proof that reads are confined',
};

/** The readings the server writes, in the order a person is told about them. */
export const CANARY_READINGS: readonly string[] = Object.keys(CANARY_WORDS);

export function canaryWording(reading: string): string {
  if (reading.length === 0) {
    return '';
  }

  return CANARY_WORDS[reading] ?? `a canary reading this panel does not know (“${reading}”)`;
}

/**
 * A stamp as a person reads it, absolute and in UTC — `2026-10-03 10:00 UTC`.
 *
 * <p>Never relative: "3 minutes ago" changes the page's markup on every repaint, and the Settings tab repaints only
 * when what it draws changed. A stamp that does not parse is shown as it is, and an empty one says so.</p>
 */
export function utcShort(stamp: string): string {
  if (stamp.length === 0) {
    return 'an unknown time';
  }
  const at = Date.parse(stamp);

  return Number.isFinite(at) ? `${new Date(at).toISOString().slice(0, 16).replace('T', ' ')} UTC` : stamp;
}

type Fields = Readonly<Record<string, unknown>>;

/** The bytes capped, decoded and parsed into an object — or why not. */
function cappedObject(bytes: Uint8Array): HealthFile<Fields> {
  return bytes.byteLength > HEALTH_FILE_CAP_BYTES ? TOO_LARGE : cappedText(new TextDecoder().decode(bytes));
}

const TOO_LARGE: HealthFile<Fields> = { kind: 'unreadable', why: `is larger than ${HEALTH_FILE_CAP_BYTES} bytes, so it was not read` };

/** Text — stdout, or a decoded file — capped, then parsed into an object, or why not. */
function cappedText(text: string): HealthFile<Fields> {
  if (text.length > HEALTH_FILE_CAP_BYTES) {
    return TOO_LARGE;
  }
  const parsed = parsedObject(text);

  return parsed === undefined ? { kind: 'unreadable', why: 'is not a JSON object' } : { kind: 'found', value: parsed };
}

function parsedObject(text: string): Fields | undefined {
  try {
    return objectOf(JSON.parse(text));
  } catch {
    return undefined;
  }
}

function objectOf(value: unknown): Fields | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as Fields) : undefined;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function flag(value: unknown): boolean {
  return value === true;
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

/** A published number, or absent — never a default that would read as the server having said it. */
function published(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : undefined;
}

function reportOf(fields: Fields): ConsultantsReport {
  const rows = Array.isArray(fields['consultants']) ? fields['consultants'] : [];

  return {
    utc: text(fields['utc']),
    side: text(fields['side']),
    distro: text(fields['distro']),
    heartbeatStaleAfterSeconds: published(fields['heartbeatStaleAfterSeconds']),
    consultants: rows.flatMap((one: unknown) => {
      const row = objectOf(one);
      return row === undefined ? [] : [rowOf(row)];
    }),
  };
}

function rowOf(fields: Fields): ConsultantRow {
  return {
    callerKind: text(fields['callerKind']),
    available: flag(fields['available']),
    reason: text(fields['reason']),
    vendor: text(fields['vendor']),
    runtime: text(fields['runtime']),
    model: text(fields['model']),
    executablePath: text(fields['executablePath']),
    cli: cliOf(objectOf(fields['cli'])),
    capability: text(fields['capability']),
    limitation: limitationOf(objectOf(fields['limitation'])),
    agy: agyOf(objectOf(fields['agy'])),
    lastAnswer: answerOf(objectOf(fields['lastAnswer'])),
    lastFailure: failureOf(objectOf(fields['lastFailure'])),
    failureCurrent: flag(fields['failureCurrent']),
    healthUnreadable: text(fields['healthUnreadable']),
    check: checkOf(objectOf(fields['check'])),
  };
}

function cliOf(raw: Fields | undefined): ConsultantCli {
  const fields = raw ?? {};

  return {
    probed: flag(fields['probed']),
    found: flag(fields['found']),
    version: text(fields['version']),
    authSource: text(fields['authSource']),
    note: text(fields['note']),
  };
}

function limitationOf(fields: Fields | undefined): ConsultantLimitation | undefined {
  if (fields === undefined || text(fields['standing']).length === 0) {
    return undefined;
  }

  return {
    standing: text(fields['standing']),
    text: text(fields['text']),
    capability: text(fields['capability']),
    evidence: text(fields['evidence']),
    measuredDate: text(fields['measuredDate']),
    measuredCliVersion: text(fields['measuredCliVersion']),
    measuredCells: text(fields['measuredCells']),
    measuredDocument: text(fields['measuredDocument']),
    source: text(fields['source']),
  };
}

function agyOf(fields: Fields | undefined): ConsultantAgy | undefined {
  return fields === undefined
    ? undefined
    : { settingsPath: text(fields['settingsPath']), snippet: text(fields['snippet']), snippetWarning: text(fields['snippetWarning']) };
}

function answerOf(fields: Fields | undefined): HealthAnswer | undefined {
  if (fields === undefined || text(fields['utc']).length === 0) {
    return undefined;
  }

  return {
    utc: text(fields['utc']),
    consultationId: text(fields['consultationId']),
    vendor: text(fields['vendor']),
    runtime: text(fields['runtime']),
    model: text(fields['model']),
    confinement: text(fields['confinement']),
  };
}

function failureOf(fields: Fields | undefined): HealthFailure | undefined {
  if (fields === undefined || text(fields['kind']).length === 0) {
    return undefined;
  }

  return {
    utc: text(fields['utc']),
    consultationId: text(fields['consultationId']),
    vendor: text(fields['vendor']),
    runtime: text(fields['runtime']),
    model: text(fields['model']),
    kind: text(fields['kind']),
    what: text(fields['what']),
    cure: text(fields['cure']),
    evidence: text(fields['evidence']),
    side: text(fields['side']),
    confinement: text(fields['confinement']),
  };
}

function checkOf(fields: Fields | undefined): CheckRecord | undefined {
  if (fields === undefined || text(fields['state']).length === 0) {
    return undefined;
  }
  const denied = Array.isArray(fields['deniedActions']) ? fields['deniedActions'] : [];

  return {
    callerKind: text(fields['callerKind']),
    state: text(fields['state']),
    startedUtc: text(fields['startedUtc']),
    deadlineUtc: text(fields['deadlineUtc']),
    heartbeatUtc: text(fields['heartbeatUtc']),
    finishedUtc: text(fields['finishedUtc']),
    heartbeatStaleAfterSeconds: published(fields['heartbeatStaleAfterSeconds']),
    side: text(fields['side']),
    vendor: text(fields['vendor']),
    runtime: text(fields['runtime']),
    model: text(fields['model']),
    confinement: text(fields['confinement']),
    answered: flag(fields['answered']),
    markerRead: flag(fields['markerRead']),
    canary: text(fields['canary']),
    deniedActions: denied.filter((one: unknown): one is string => typeof one === 'string'),
    seconds: count(fields['seconds']),
    tokensIn: count(fields['tokensIn']),
    tokensOut: count(fields['tokensOut']),
    failureKind: text(fields['failureKind']),
    failureWhat: text(fields['failureWhat']),
    failureCure: text(fields['failureCure']),
    evidence: text(fields['evidence']),
    reason: text(fields['reason']),
  };
}
