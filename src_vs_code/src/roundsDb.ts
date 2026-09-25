/**
 * The rounds database, read through the server that owns it.
 *
 * <p><b>Why not SQLite in here.</b> The alternative was a WebAssembly build of SQLite in the VSIX or
 * a native module per platform, to ask questions of a file the server already writes and whose
 * schema it owns. The server answers `--log` with JSON instead: no dependency, no platform build,
 * and every query stays beside its table.</p>
 *
 * <p><b>Version skew is normal and is not an error.</b> A server older than the database answers
 * `unknown argument` and exit 64; one that has never run a round answers an empty log. Both mean the
 * same thing to the page — no findings to show — and the page keeps rendering everything it builds
 * from the session files, which is what it always had.</p>
 */

/** One finding, as the log page shows it. */
export interface DbFinding {
  readonly ordinal: number;
  readonly severity: string;
  readonly category: string;
  readonly file: string;
  readonly line: number;
  readonly title: string;
  readonly why: string;
  readonly fix: string;
  readonly role: string;
  readonly isGating: boolean;
  /** Comma-separated, as the database stores them: one vendor in practice. */
  readonly providers: string;
  /** `accept`, `reject`, or empty while nobody has decided. */
  readonly resolution: string;
  readonly reason: string;
  /** The caller had already rejected this, and a reviewer raised it again. */
  readonly reRaised: boolean;
}

export interface DbRound {
  readonly repoPath: string;
  readonly branch: string;
  readonly stage: string;
  readonly number: number;
  readonly startedUtc: string;
  /** Which session it belonged to — the half of the key that makes it unique. */
  readonly sessionId: string;
  /** How the caller closed the gate; -1 until it did. */
  readonly accepted: number;
  readonly rejected: number;
  /**
   * The findings themselves — only in the OLD shape, which a server before 0.19 answers.
   *
   * <p>They used to ride every listed round: measured at <b>3.78 MB of a 3.83 MB payload</b>, for
   * rounds nobody had opened. A paged server sends none of them here and answers `--findings` for
   * the one round somebody clicked on.</p>
   */
  readonly findings: readonly DbFinding[];
  /** Where the next page starts. Empty from a server that does not page. */
  readonly cursor: string;
  /**
   * How many findings this round produced.
   *
   * <p>Load-bearing rather than decoration: the page tells a gate still open from a round that
   * raised nothing, and the only evidence for the second is that no finding exists. Once the list
   * stopped carrying findings, this is what carries that fact.</p>
   */
  readonly foundCount: number;
  /**
   * When this round was LAST decided, or empty when nobody has.
   *
   * <p>The other half of what a round cost. `startedUtc` to the session file's `completedUtc` is
   * the reviewers running; this is how long the deciding took. A server older than the field sends
   * nothing and the page shows one time, exactly as it did.</p>
   */
  readonly resolvedUtc: string;
}

/**
 * What the whole table adds up to — counted by SQL, never by the array that was sent.
 *
 * <p>From the operator, 2026-09-09: «суммы - скл счиатть (сколько всего и тд.)». A count taken over
 * a page is a count of the page, and the page is not the question anybody is asking.</p>
 */
export interface DbTotals {
  readonly rounds: number;
  readonly findings: number;
  readonly accepted: number;
  readonly rejected: number;
  readonly gating: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number;
}

export const EMPTY_TOTALS: DbTotals = {
  rounds: 0, findings: 0, accepted: 0, rejected: 0, gating: 0, tokensIn: 0, tokensOut: 0, costUsd: 0,
};

/** How often one kind of thing was accepted — by category, by role, or by vendor. */
export interface BlindSpot {
  readonly kind: string;
  readonly name: string;
  readonly accepted: number;
  readonly total: number;
}

/**
 * One consultation as the log lists it — who asked, who answered, and how it ended.
 *
 * <p>The turn-by-turn conversation is deliberately not here: the sidebar shows a consultation while
 * it runs, from the record file, and this is the history. What a person asks of history is what it
 * was about, what it cost, and whether the advice was worth having.</p>
 */
export interface DbConsultation {
  readonly id: string;
  readonly callerKind: string;
  readonly repoPath: string;
  readonly branch: string;
  readonly vendor: string;
  readonly model: string;
  readonly turns: number;
  readonly status: string;
  readonly reason: string;
  readonly startedUtc: string;
  readonly endedUtc: string;
  readonly seconds: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
  /**
   * How it ENDED, as against why it stopped — see `outcomeSaid`.
   *
   * <p>Optional, and never defaulted: a record from a server that predates the field carries
   * nothing, and nothing is not a verdict. The status beside it is the server's own and is shown as
   * given, so an `open` consultation from an old server still reads `open`. (issue #309.)</p>
   */
  readonly outcome?: string | undefined;
  /**
   * WHO said so — `caller`, `person`, the server's own `server`, or nothing.
   *
   * <p>Optional for the same reason as `outcome` and one release later: a server that predates the
   * column sends nothing, and nothing is not an author. A verdict with no author is still a verdict
   * — every record written before the field existed is in that state — so this is shown beside the
   * word when it is there and simply absent when it is not, never guessed at from the status.</p>
   */
  readonly outcomeBy?: string | undefined;
  readonly problem: string;
  readonly advice: string;
  readonly alert: string;
}

export interface DbLog {
  readonly rounds: readonly DbRound[];
  /**
   * The consultations, newest first.
   *
   * <p>Absent from a server too old to have the table, and that is why it is a list rather than an
   * optional: an absent list is an EMPTY one on both sides of this seam, never an error. The two
   * halves of this product update separately and always have.</p>
   */
  readonly consultations: readonly DbConsultation[];
  readonly blindSpots: readonly BlindSpot[];
  readonly defended: readonly DbFinding[];
  readonly totals: DbTotals;
  /**
   * Whether the server that answered can page at all.
   *
   * <p>False for one too old to know `--paged`, and the page says so instead of showing a Next
   * button that would answer with the same rows. The two halves of this product update separately,
   * so both directions of skew are ordinary.</p>
   */
  readonly paged: boolean;
  /**
   * Whether the database was ASKED at all.
   *
   * <p>The load-bearing difference, and the one the code round caught: a log nobody has read yet and
   * a log read from an empty database are otherwise the same value. The page is painted BEFORE the
   * first read on purpose — reading spawns a process, and nobody should wait on one to see their log
   * — so without this every row on that first paint concluded the database had no record of it, and
   * the tick that knew better was then held off by the row it had already drawn.</p>
   */
  readonly read: boolean;
  /**
   * The instant the server counted the blind spots and the defended list from, or `''` for all time.
   *
   * <p>The server's own ECHO of `--since`, and the only proof it applied one: a coai-mcp 0.36.0 given
   * the flag was measured to ignore it and answer all time with exit 0. The Review rounds page's period
   * switch on *What it keeps missing* reads this to say when its server could not split by period.</p>
   */
  readonly spotsSince: string;
}

export const EMPTY_LOG: DbLog = {
  rounds: [], consultations: [], blindSpots: [], defended: [], totals: EMPTY_TOTALS, paged: false, read: false,
  spotsSince: '',
};

/**
 * The server's JSON, believed only as far as its shape.
 *
 * <p>Pure, so it is a test rather than a hope: this reads a file written by another program, and a
 * page that throws on one unexpected field is a page that goes blank for a reason nobody can see
 * from the outside.</p>
 */
export function parseLog(text: string, paged = false): DbLog {
  try {
    const raw = JSON.parse(text) as Partial<DbLog>;

    return {
      rounds: (raw.rounds ?? []).map(round),
      consultations: (raw.consultations ?? []).filter((one) => typeof one?.id === 'string').map(consultation),
      blindSpots: (raw.blindSpots ?? []).filter((s) => typeof s?.name === 'string'),
      defended: (raw.defended ?? []).map(finding),
      totals: totalsOf(raw.totals),
      paged,
      read: true,
      spotsSince: typeof (raw as { since?: unknown }).since === 'string' ? (raw as { since: string }).since : '',
    };
  } catch {
    return EMPTY_LOG;
  }
}

/**
 * One round's findings, as `--findings` answers them — or NOTHING when the answer was not that.
 *
 * <p>The distinction is the point. `parseLog` turns a malformed answer into an empty log, which is
 * legible and says nothing untrue. An empty findings list is a CLAIM: it says the round was clean.
 * So text that is not JSON, or JSON without a findings array, comes back as `undefined` and the
 * caller reports a failed read. (Code round, codex.)</p>
 */
export function parseFindings(text: string): readonly DbFinding[] | undefined {
  try {
    const raw = JSON.parse(text) as { findings?: unknown[] };
    if (!Array.isArray(raw?.findings)) {
      return undefined;
    }
    // The WHOLE list, or nothing. Dropping a malformed entry would hand back a round with fewer
    // findings than it had, which reads as a cleaner round — the same lie by a quieter route.
    if (!raw.findings.every(isFinding)) {
      return undefined;
    }

    return raw.findings.map(finding);
  } catch {
    return undefined;
  }
}

/**
 * What a round ORDERED its caller to do, and the size it measured (issue #131).
 *
 * <p>Absent from an answer when the round was recorded before this was written down, or by a server
 * or a database too old to hold it — which is NOT RECORDED, a different fact from an empty list.</p>
 */
export interface RoundOrders {
  readonly commands: readonly string[];
  readonly planShape: string;
}

/**
 * The `orders` of a `--findings` answer, or nothing when there are none or they are not that shape.
 *
 * <p>Nothing rather than a guess: orders are context beside the findings, never a reason to refuse
 * them, so a malformed member costs the orders block and nothing else.</p>
 */
export function parseOrders(text: string): RoundOrders | undefined {
  try {
    const orders: unknown = (JSON.parse(text) as { orders?: unknown }).orders;

    return isOrders(orders) ? { commands: orders.commands, planShape: orders.planShape } : undefined;
  } catch {
    return undefined;
  }
}

function isOrders(value: unknown): value is RoundOrders {
  const orders = value as Partial<Record<keyof RoundOrders, unknown>> | null | undefined;

  return typeof orders?.planShape === 'string' && isStrings(orders.commands);
}

function isStrings(value: unknown): value is readonly string[] {
  return Array.isArray(value) && value.every((one) => typeof one === 'string');
}

/**
 * One round's entry in a BATCH answer: the key it was asked about, and what was found.
 *
 * <p>The key is echoed by the server and checked here rather than paired up by position. A batch
 * read that attaches one round's findings to another round's row would write a CSV that is wrong in
 * the one way nobody can see by looking at it.</p>
 */
export interface ManyFound {
  readonly sessionId: string;
  readonly stage: string;
  readonly number: number;
  /** The database has a record of this round. `false` is "never heard of it", not "found nothing". */
  readonly known: boolean;
  readonly findings: readonly DbFinding[];
}

/**
 * The answer to a batch findings read, or `undefined` when it cannot be read at all.
 *
 * <p>`undefined` is not an empty list: a truncated pipe parsing as nothing would otherwise tell
 * somebody that five hundred rounds were clean. The same distinction {@link parseFindings} makes,
 * for the same reason.</p>
 */
export function parseManyFindings(text: string): readonly ManyFound[] | undefined {
  try {
    const raw = JSON.parse(text) as { rounds?: Partial<ManyFound>[] };
    if (!Array.isArray(raw?.rounds)) {
      return undefined;
    }

    const rounds: ManyFound[] = [];
    for (const one of raw.rounds) {
      // A KNOWN round must arrive with an array of findings. An entry that says the round exists and
      // then omits `findings`, or sends something that is not an array, is a shape nobody intended —
      // and normalising it to `[]` would publish that round as clean, which is the exact lie the
      // three-state answer exists to prevent. Fail the whole answer instead, which every caller
      // already turns into "every round failed". (Code round, codex.)
      if (one.known === true && !Array.isArray(one.findings)) {
        return undefined;
      }
      // And `known` itself must BE a boolean. `one.known === true` alone turns `"known": "yes"` into
      // `false`, which the reader then records as `absent` — "the database has never heard of this
      // round" — about a round the server was in fact telling us something else about. A shape
      // nobody intended must not become a confident claim. (CodeRabbit, on the pull request.)
      if (typeof one.known !== 'boolean') {
        return undefined;
      }
      // Every finding must carry its identity here too, for the reason above: a fabricated row is
      // worse in a bulk export than in one opened row, because nobody is looking at it.
      const found: unknown[] = Array.isArray(one.findings) ? one.findings : [];
      if (!found.every(isFinding)) {
        return undefined;
      }
      rounds.push({
        sessionId: one.sessionId ?? '',
        stage: one.stage ?? '',
        number: one.number ?? 0,
        known: one.known === true,
        findings: found.map(finding),
      });
    }

    return rounds;
  } catch {
    return undefined;
  }
}

function number(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function totalsOf(raw: Partial<DbTotals> | undefined): DbTotals {
  return raw === undefined || raw === null
    ? EMPTY_TOTALS
    : {
      rounds: number(raw.rounds),
      findings: number(raw.findings),
      accepted: number(raw.accepted),
      rejected: number(raw.rejected),
      gating: number(raw.gating),
      tokensIn: number(raw.tokensIn),
      tokensOut: number(raw.tokensOut),
      costUsd: number(raw.costUsd),
    };
}

/**
 * One consultation, believed only as far as its shape — the rule {@link round} already applied.
 *
 * <p>They used to pass through untouched, and the price is what that cost. The server writes `--log`
 * with `WhenWritingNull`, so a consultation nobody priced arrives with NO `costUsd` key; `undefined`
 * then slipped past `costUsd !== null` into `toFixed`, and the throw landed before the log page's
 * push — every tab stopped updating from the first unpriced consultation on (2026-09-23). Absent,
 * null and anything that is not a finite number are all the same thing here: no price.</p>
 *
 * <p>`outcome` and `outcomeBy` stay optional rather than defaulted, for the reason on
 * {@link DbConsultation}: nothing is not a verdict.</p>
 */
function consultation(raw: Partial<DbConsultation>): DbConsultation {
  return {
    id: text(raw.id),
    callerKind: text(raw.callerKind),
    repoPath: text(raw.repoPath),
    branch: text(raw.branch),
    vendor: text(raw.vendor),
    model: text(raw.model),
    turns: number(raw.turns),
    status: text(raw.status),
    reason: text(raw.reason),
    startedUtc: text(raw.startedUtc),
    endedUtc: text(raw.endedUtc),
    seconds: number(raw.seconds),
    tokensIn: number(raw.tokensIn),
    tokensOut: number(raw.tokensOut),
    costUsd: price(raw.costUsd),
    ...verdictOf(raw),
    problem: text(raw.problem),
    advice: text(raw.advice),
    alert: text(raw.alert),
  };
}

/** A price, or `null` for everything that is not one — absent included, which is what the wire sends. */
function price(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** Only the verdict fields that were actually SENT: an absent outcome stays absent, never a word. */
function verdictOf(raw: Partial<DbConsultation>): Pick<DbConsultation, 'outcome' | 'outcomeBy'> {
  return {
    ...(typeof raw.outcome === 'string' ? { outcome: raw.outcome } : {}),
    ...(typeof raw.outcomeBy === 'string' ? { outcomeBy: raw.outcomeBy } : {}),
  };
}

function round(raw: Partial<DbRound>): DbRound {
  return {
    repoPath: raw.repoPath ?? '',
    branch: raw.branch ?? '',
    stage: raw.stage ?? '',
    number: raw.number ?? 0,
    startedUtc: raw.startedUtc ?? '',
    sessionId: raw.sessionId ?? '',
    accepted: raw.accepted ?? -1,
    rejected: raw.rejected ?? -1,
    findings: (raw.findings ?? []).map(finding),
    cursor: raw.cursor ?? '',
    // An OLD server carries the findings and no count, so the count is what it carries. A round
    // with neither is a round that found nothing, which is what zero says.
    foundCount: raw.foundCount ?? (raw.findings ?? []).length,
    resolvedUtc: raw.resolvedUtc ?? '',
  };
}

/**
 * Whether this is a finding AT ALL, as opposed to an object that will acquire defaults.
 *
 * <p><b>`finding()` fills every field it is not given, which means it turns `{}` into a real-looking
 * finding numbered 0 with no title and no resolution — and the CSV publishes that as an OPEN finding
 * nobody raised.</b> The operator ruled on 2026-09-14 that the system must not synthesise an entity
 * out of an empty object: no data is a validation failure, never a fabricated row.</p>
 *
 * <p>The test is the ORDINAL, because that is a finding's identity — the number a `resolve` decision
 * is keyed by, and the one field that cannot be defaulted without inventing a claim. Everything else
 * stays defaultable on purpose: demanding every field would make this build reject a record the
 * server has legitimately widened, and the two halves ship separately.</p>
 */
function isFinding(raw: unknown): raw is Partial<DbFinding> {
  if (typeof raw !== 'object' || raw === null) {
    return false;
  }
  const ordinal = (raw as Record<string, unknown>)['ordinal'];

  return typeof ordinal === 'number' && Number.isInteger(ordinal) && ordinal >= 0;
}

function finding(raw: Partial<DbFinding>): DbFinding {
  return {
    ordinal: raw.ordinal ?? 0,
    severity: raw.severity ?? '',
    category: raw.category ?? '',
    file: raw.file ?? '',
    line: raw.line ?? 0,
    title: raw.title ?? '',
    why: raw.why ?? '',
    fix: raw.fix ?? '',
    role: raw.role ?? '',
    isGating: raw.isGating === true,
    providers: raw.providers ?? '',
    resolution: raw.resolution ?? '',
    reason: raw.reason ?? '',
    reRaised: raw.reRaised === true,
  };
}

/**
 * The key a round is found by, from either side.
 *
 * <p>The page builds its rows from the session files and the database knows nothing of them, so the
 * two are matched on what both record: the SESSION, the repository, the branch, the stage and the
 * round number. Paths are compared the way this family compares them everywhere — separators
 * normalised, case ignored, because Windows writes the same folder three ways in one afternoon.</p>
 *
 * <p><b>The session is part of it because round numbers restart.</b> One repository and branch
 * reviewed twice has two "CodeReview round 1" records; keyed without the session, the second
 * overwrote the first and a row showed another review's findings. Two of the gate's reviewers found
 * that independently.</p>
 */
export function roundKeyOf(
  sessionId: string,
  repoPath: string,
  branch: string,
  stage: string,
  number: number,
): string {
  return [
    sessionId,
    repoPath.replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase(),
    branch.toLowerCase(),
    stage.toLowerCase().replace(/\s+/g, ''),
    number,
  ].join('|');
}

/** Every round's findings, by that key. */
export function findingsByRound(log: DbLog): Map<string, readonly DbFinding[]> {
  const byRound = new Map<string, readonly DbFinding[]>();
  for (const one of log.rounds) {
    byRound.set(roundKeyOf(one.sessionId, one.repoPath, one.branch, one.stage, one.number), one.findings);
  }

  return byRound;
}

/**
 * What each round's gate CLOSED at, by the same key.
 *
 * <p>Separate from the findings because it answers a different question: the findings say what was
 * raised, and this says whether anybody has decided about them. The server writes -1 until a resolve
 * lands, which is the only honest way to tell "nothing was accepted" from "nobody has said yet" —
 * and the log page read neither, so a round with thirteen open findings displayed as `done`.</p>
 */
/**
 * How many findings each round produced, by the same key.
 *
 * <p>Separate from the findings themselves because the LIST no longer carries those: a row needs
 * the number to say whether a gate is still open, and the sentences only when somebody opens it.</p>
 */
export function resolvedByRound(log: DbLog): Map<string, string> {
  const byRound = new Map<string, string>();
  for (const one of log.rounds) {
    byRound.set(roundKeyOf(one.sessionId, one.repoPath, one.branch, one.stage, one.number), one.resolvedUtc);
  }

  return byRound;
}

export function countsByRound(log: DbLog): Map<string, number> {
  const byRound = new Map<string, number>();
  for (const one of log.rounds) {
    byRound.set(roundKeyOf(one.sessionId, one.repoPath, one.branch, one.stage, one.number), one.foundCount);
  }

  return byRound;
}

export function decisionsByRound(log: DbLog): Map<string, { accepted: number; rejected: number }> {
  const byRound = new Map<string, { accepted: number; rejected: number }>();
  for (const one of log.rounds) {
    byRound.set(
      roundKeyOf(one.sessionId, one.repoPath, one.branch, one.stage, one.number),
      { accepted: one.accepted, rejected: one.rejected });
  }

  return byRound;
}

/**
 * One collector run, as `--bugs-json` reports it.
 *
 * <p>An empty {@link id} means NO RUN HAS EVER STARTED here, which is a state the section renders
 * and is not the same as "the server could not say". An older server emits no `lastRun` key at all,
 * and {@link parseBugs} maps that absence onto this same empty shape — the two halves of this
 * product have shipped out of step before, so a new panel must read an old answer.</p>
 */
export interface CollectRun {
  readonly id: string;
  readonly startedUtc: string;
  readonly finishedUtc: string;
  readonly state: string;
  readonly model: string;
  readonly candidates: number;
  readonly picked: number;
  readonly collected: number;
  readonly skipped: number;
  readonly failed: number;
  readonly reasons: string;
}

/**
 * One SEND, as `--bugs-json` reports it.
 *
 * <p>It exists because the funnel cannot answer "is a send happening". A pair is marked sent only on
 * the server's acknowledgement — the rule that makes a killed upload safe to retry — so for the whole
 * of a multi-minute run the counts say exactly what they said before it started. A panel reading them
 * shows an idle button, a reload shows an idle button, and a second Send starts a second process
 * against the same waiting pairs. (Plan round, all three reviewers.)</p>
 *
 * <p>An empty {@link id} means NO SEND HAS EVER HAPPENED here, and an older server emits no
 * `lastSend` key at all — {@link parseBugs} maps that absence onto this same empty shape.</p>
 */
export interface SendRun {
  readonly id: string;
  readonly startedUtc: string;
  readonly finishedUtc: string;
  readonly state: string;
  readonly server: string;
  readonly offered: number;
  readonly sent: number;
  /** How many the server ALREADY HELD. A success, and not the same one as {@link sent}. */
  readonly duplicate: number;
  readonly refused: number;
  /** The CLI's own sentence when a run could not finish. Empty when nothing went wrong. */
  readonly trouble: string;
}

/** How far the filter narrows, step by step — the only readable form of a skip rate. */
export interface BugFunnel {
  readonly all: number;
  readonly onCode: number;
  readonly accepted: number;
  readonly gating: number;
  readonly runtime: number;
  readonly located: number;
  readonly unprocessed: number;
  /** How many findings have a collected pair, across every run there has ever been. */
  readonly collected: number;
}

/** What the corpus has to offer, and what the last run made of it. */
export interface BugCorpus {
  readonly funnel: BugFunnel;
  readonly lastRun: CollectRun;
  /** The most recent send, or the empty shape when none has ever happened. */
  readonly lastSend: SendRun;
  /**
   * How many kept pairs are UNSENT — the number a send would actually offer.
   *
   * <p>Not `funnel.collected`, which counts every pair ever kept and is therefore unchanged by a
   * successful send: a button reading that offers to send what has already gone.</p>
   *
   * <p><b>UNDEFINED means the server did not say</b>, which is a different thing from zero and has
   * to stay different. An older `coai-mcp` emits no `sendable` key, and collapsing that into 0
   * disabled the Send button for ever — so the person never pressed it, never reached the exit-64
   * answer, and never learned that the thing to do was update the server. (Code round 2, codex.)</p>
   */
  readonly sendable: number | undefined;
  /**
   * The vendors THIS server will accept for a ranking pass.
   *
   * <p>Read from the server rather than hardcoded beside the picker, because an installed
   * extension and an installed server can be of different ages — a repository-level check keeps
   * two source files honest and says nothing about two installed binaries. Empty means the server
   * is too old to say, and the panel falls back to its own list.</p>
   */
  readonly rankingVendors: readonly string[];
  /** Whether the server answered at all. False is "we do not know", never "there is nothing". */
  readonly read: boolean;
}

const NO_RUN: CollectRun = {
  id: '',
  startedUtc: '',
  finishedUtc: '',
  state: '',
  model: '',
  candidates: 0,
  picked: 0,
  collected: 0,
  skipped: 0,
  failed: 0,
  reasons: '',
};

const NO_SEND: SendRun = {
  id: '',
  startedUtc: '',
  finishedUtc: '',
  state: '',
  server: '',
  offered: 0,
  sent: 0,
  duplicate: 0,
  refused: 0,
  trouble: '',
};

const NO_FUNNEL: BugFunnel = {
  all: 0, onCode: 0, accepted: 0, gating: 0, runtime: 0, located: 0, unprocessed: 0, collected: 0,
};

/** Nothing known — which the section renders as "not asked yet", not as "no material". */
export const EMPTY_CORPUS: BugCorpus = {
  funnel: NO_FUNNEL,
  lastRun: NO_RUN,
  lastSend: NO_SEND,
  // Nothing has been asked, so nothing is known — which is not the same as a queue of zero.
  sendable: undefined,
  rankingVendors: [],
  read: false,
};

/**
 * The states that mean a send is over, spelled the way the server writes them.
 *
 * <p>ENDINGS rather than beginnings, and everything is read against it: a state this build has never
 * heard of is treated as STILL GOING, because the cost of a stale line is a stale line and the cost
 * of declaring a live send finished is a second process sending the same pairs. `UploadRunState` in
 * the server carries the same list for the same reason.</p>
 */
const SEND_IS_OVER: readonly string[] = ['done', 'failed', 'interrupted'];

/** One send, with every field checked — an older server may send none of them. */
function readSend(raw: unknown): SendRun {
  if (typeof raw !== 'object' || raw === null) {
    return NO_SEND;
  }

  const held = raw as Record<string, unknown>;

  return {
    id: text(held.id),
    startedUtc: text(held.startedUtc),
    finishedUtc: text(held.finishedUtc),
    state: text(held.state),
    server: text(held.server),
    offered: whole(held.offered),
    sent: whole(held.sent),
    duplicate: whole(held.duplicate),
    refused: whole(held.refused),
    trouble: text(held.trouble),
  };
}

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

const whole = (value: unknown): number =>
  (typeof value === 'number' && Number.isFinite(value) ? value : 0);

/** Whether a send is happening — what the Send button is disabled by, across a reload. */
export function sending(send: SendRun): boolean {
  return send.id.length > 0 && !SEND_IS_OVER.includes(send.state);
}

/** Whether a run is happening — what the Collect button is disabled by. */
/**
 * The states that mean nothing more will happen.
 *
 * <p>A list of ENDINGS, so a state this build has never heard of reads as still going. An equality
 * test against `running` would call a later paused-for-approval state FINISHED, re-enable Collect,
 * and let a second run start beside the first. Waiting too long costs a stale line; declaring a
 * working run finished costs a duplicate collection.</p>
 */
export const FINISHED_STATES: readonly string[] = ['done', 'failed', 'interrupted'];

/** Whether a run is happening — what the Collect button is disabled by. */
export const isRunning = (run: CollectRun): boolean =>
  run.id.length > 0 && !FINISHED_STATES.includes(run.state);

/** Whether any run has ever been recorded. */
export const hasRun = (run: CollectRun): boolean => run.id.length > 0;

/**
 * What the server said about the corpus, or nothing known.
 *
 * <p>Defensive in the same way {@link parseLog} is: anything malformed is an EMPTY corpus with
 * `read: false`, because a panel that renders zeroes as fact would tell somebody there is no
 * material when the truth is that nothing was asked. The one piece that is allowed to be missing
 * without that verdict is `lastRun` — a server older than the run table simply has none.</p>
 */
export function parseBugs(text: string): BugCorpus {
  try {
    const raw = JSON.parse(text) as Partial<BugCorpus>;
    if (raw?.funnel === undefined) {
      return EMPTY_CORPUS;
    }

    return {
      funnel: { ...NO_FUNNEL, ...raw.funnel },
      // An absent `lastRun` is an older server, and it means the same thing an empty id means.
      lastRun: { ...NO_RUN, ...(raw.lastRun ?? {}) },
      // And an absent `lastSend` is a server from before sends were recorded at all: no send, which
      // is what an empty id means too.
      // READ rather than spread. `lastRun` above is older and shares the weakness; this one decides
      // whether a BUTTON is disabled, so a number where its `state` belongs would throw inside
      // `sending()` and take the whole section's repaint with it.
      lastSend: readSend(raw.lastSend),
      // Absent stays ABSENT. See the field's own note: an older server saying nothing must not read
      // as "nothing to send".
      sendable: typeof raw.sendable === 'number' ? raw.sendable : undefined,
      // Absent means a server too old to say, NOT a server that allows nothing — the difference
      // decides whether the picker falls back to its own list or offers nothing at all.
      rankingVendors: Array.isArray(raw.rankingVendors) ? raw.rankingVendors : [],
      read: true,
    };
  } catch {
    return EMPTY_CORPUS;
  }
}
