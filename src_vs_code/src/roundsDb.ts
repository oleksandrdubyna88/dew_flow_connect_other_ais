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

export interface DbLog {
  readonly rounds: readonly DbRound[];
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
}

export const EMPTY_LOG: DbLog = {
  rounds: [], blindSpots: [], defended: [], totals: EMPTY_TOTALS, paged: false, read: false,
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
      blindSpots: (raw.blindSpots ?? []).filter((s) => typeof s?.name === 'string'),
      defended: (raw.defended ?? []).map(finding),
      totals: totalsOf(raw.totals),
      paged,
      read: true,
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
    const raw = JSON.parse(text) as { findings?: Partial<DbFinding>[] };

    return Array.isArray(raw?.findings) ? raw.findings.map(finding) : undefined;
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
  };
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
