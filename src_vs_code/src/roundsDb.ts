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
  /** How the caller closed the gate; -1 until it did. */
  readonly accepted: number;
  readonly rejected: number;
  readonly findings: readonly DbFinding[];
}

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
}

export const EMPTY_LOG: DbLog = { rounds: [], blindSpots: [], defended: [] };

/**
 * The server's JSON, believed only as far as its shape.
 *
 * <p>Pure, so it is a test rather than a hope: this reads a file written by another program, and a
 * page that throws on one unexpected field is a page that goes blank for a reason nobody can see
 * from the outside.</p>
 */
export function parseLog(text: string): DbLog {
  try {
    const raw = JSON.parse(text) as Partial<DbLog>;

    return {
      rounds: (raw.rounds ?? []).map(round),
      blindSpots: (raw.blindSpots ?? []).filter((s) => typeof s?.name === 'string'),
      defended: (raw.defended ?? []).map(finding),
    };
  } catch {
    return EMPTY_LOG;
  }
}

function round(raw: Partial<DbRound>): DbRound {
  return {
    repoPath: raw.repoPath ?? '',
    branch: raw.branch ?? '',
    stage: raw.stage ?? '',
    number: raw.number ?? 0,
    startedUtc: raw.startedUtc ?? '',
    accepted: raw.accepted ?? -1,
    rejected: raw.rejected ?? -1,
    findings: (raw.findings ?? []).map(finding),
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
 * two are matched on what both record: the repository, the branch, the stage and the round number.
 * Paths are compared the way this family compares them everywhere — separators normalised, case
 * ignored, because Windows writes the same folder three ways in one afternoon.</p>
 */
export function roundKeyOf(repoPath: string, branch: string, stage: string, number: number): string {
  return [
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
    byRound.set(roundKeyOf(one.repoPath, one.branch, one.stage, one.number), one.findings);
  }

  return byRound;
}
