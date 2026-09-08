import { money, shortDuration } from './usage';
/**
 * Reading the server's own session files, so the rounds view shows what actually happened rather
 * than what the extension guessed. Pure apart from the read: the shapes and the rendering are
 * tested, the directory walk is three lines.
 */

/** Where one reviewer got to. Written by the server while the round is still open. */
export interface ReviewerState {
  readonly provider: string;
  readonly role: string;
  readonly status: string;
  readonly findings: number;
  readonly note: string;
  /**
   * How long THIS reviewer ran. Absent in files written by a server older than the field.
   *
   * <p>A round is as slow as its slowest reviewer, so the round's own "11m 2s" says nothing about
   * which of nine cost the eleven minutes. The scheduler times each one anyway; this is that number
   * arriving instead of being dropped at the session boundary.</p>
   */
  readonly seconds?: number;
  /** What this reviewer read and wrote, when the server recorded it per reviewer. */
  readonly tokensIn?: number;
  readonly tokensOut?: number;
}

export interface RoundRecord {
  readonly stage: string;
  readonly number: number;
  readonly verdict: string;
  readonly gatingCount: number;
  readonly reviewers: string;
  readonly completedUtc: string;
  /** `running` | `done` | `interrupted`. Absent in files written by an older server. */
  readonly status?: string;
  readonly startedUtc?: string;
  readonly reviewerStates?: readonly ReviewerState[];
  /** What the round was about — the plan's file name or title. Absent in older files. */
  readonly subject?: string;
  readonly tokensIn?: number;
  readonly tokensOut?: number;
  /** Only vendors that price their own runs report this; absent is "unknown", never "free". */
  readonly costUsd?: number | null;
}

export interface SessionFile {
  readonly state: {
    readonly sessionId: string;
    readonly repoPath: string;
    readonly branch: string;
    readonly stage: string;
    readonly awaitingResolve: boolean;
  };
  readonly rounds: readonly RoundRecord[];
}

/** The default data dir the server uses when `COAI_DATA_DIR` is unset. */
export function defaultDataDir(localAppData: string): string {
  return `${localAppData}/coai-mcp`;
}

/** `running` while the fan-out is in flight; a file from an older server has no status at all. */
export function isRunning(round: RoundRecord): boolean {
  return round.status === 'running';
}

/**
 * What a round consumed, as one short phrase.
 *
 * <p>Tokens come from every vendor that reports them; money only from vendors that price their own
 * run. "no cost reported" is deliberate wording: the alternative — printing $0.00 — would claim
 * the round was free, and a silent vendor is unknown, not free.</p>
 */
export function costPhrase(round: RoundRecord): string {
  const inTokens = round.tokensIn ?? 0;
  const outTokens = round.tokensOut ?? 0;
  if (inTokens === 0 && outTokens === 0) {
    return round.costUsd == null ? 'no usage reported' : money(round.costUsd);
  }
  const tokens = `${thousands(inTokens)} in / ${thousands(outTokens)} out`;
  return round.costUsd == null ? `${tokens} · no cost reported` : `${tokens} · ${money(round.costUsd)}`;
}


function thousands(count: number): string {
  return count >= 1000 ? `${(count / 1000).toFixed(count >= 10000 ? 0 : 1)}k` : String(count);
}

/** How long a round has been going, or how long it took — the live part people watch. */
export function elapsed(round: RoundRecord, nowMs: number): string {
  // An INTERRUPTED round has no duration, and every number available for one is a lie. It was never
  // written a completion time, so this used to fall back to "now" and show how long ago it STARTED —
  // `361m 40s` beside an interrupted badge on a machine whose reviewer timeout is ten minutes. Once a
  // restart sweeps it the sweep stamps the moment it noticed, which measures how long nobody looked.
  // The per-reviewer times are real and stay; the round's own total is not a number anybody has.
  if (round.status === 'interrupted') {
    return '';
  }
  const started = round.startedUtc === undefined ? NaN : Date.parse(round.startedUtc);
  if (Number.isNaN(started)) {
    return '';
  }
  const endMs = isRunning(round) ? nowMs : Date.parse(round.completedUtc);
  const seconds = Math.max(0, Math.round(((Number.isNaN(endMs) ? nowMs : endMs) - started) / 1000));

  // A round written before this field existed carries .NET's default date — year ONE — and the
  // subtraction produced "1065396701m 44s", a billion minutes, in the panel and in the file
  // alike. A duration longer than any review could take is a missing start, not a long round.
  if (seconds > MAX_PLAUSIBLE_SECONDS) {
    return '';
  }
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}

/** A day. The reviewer timeout is minutes; anything past this is a clock, not a review. */
export const MAX_PLAUSIBLE_SECONDS = 24 * 60 * 60;

/** `PlanReview` -> `plan review`: both renderers speak the way a person would say it. */
export function stageName(stage: string): string {
  return stage === 'PlanReview' ? 'plan review' : stage === 'CodeReview' ? 'code review' : stage;
}

/**
 * One reviewer's row, split at the vendor's name.
 *
 * <p>Split rather than composed, because the panel colours the vendor word and the markdown export
 * must not. Two renderers building the same sentence independently would drift; one builder with a
 * seam in it cannot.</p>
 */
export interface ReviewerRow {
  readonly provider: string;
  /** Everything after the vendor's name, starting at the slash. */
  readonly rest: string;
}

/** The reviewers of a running round, as "codex/Architecture — running" lines. */
export function reviewerLines(round: RoundRecord): readonly string[] {
  return reviewerRows(round).map((row) => `${row.provider}${row.rest}`);
}

export function reviewerRows(round: RoundRecord): readonly ReviewerRow[] {
  return (round.reviewerStates ?? []).map((s) => {
    const detail = [
      s.status === 'done' ? `${s.findings} finding${s.findings === 1 ? '' : 's'}` : '',
      // A queued reviewer's note says what it is waiting for — "2 ahead on this engine, about
      // 4 min". "queued" alone cannot tell ten seconds from ten minutes, and the server knows.
      s.status === 'failed' || s.status === 'queued' ? s.note : '',
      // Each part is present only when the server recorded it. A round from an older server says
      // nothing about time or tokens rather than saying zero — which would be a measurement.
      reviewerTime(s),
      reviewerTokens(s),
    ].filter((part) => part.length > 0);

    return {
      provider: s.provider,
      rest: `/${s.role} — ${s.status}${detail.length > 0 ? ` (${detail.join(', ')})` : ''}`,
    };
  });
}

/**
 * One reviewer's own duration, in the same words the spending view uses for a run.
 *
 * <p>`shortDuration` takes SECONDS — it reads "38 s", "9.8 min", "1.2 h" — which is the vocabulary
 * a person already sees per vendor in *What each AI has used*. The round's own total is formatted
 * differently (`11m 2s`) because it is a stopwatch over the whole fan-out; using one for the other
 * would be a third spelling of time in one card.</p>
 */
function reviewerTime(state: ReviewerState): string {
  const seconds = state.seconds ?? 0;
  // A session file is JSON somebody else wrote: NaN or Infinity would render as "NaN s", which
  // reads as a broken panel rather than as a missing measurement. Not a number is not a duration.
  return !Number.isFinite(seconds) || seconds <= 0 ? '' : shortDuration(seconds);
}

/** What one reviewer read and wrote, when the server recorded it. */
function reviewerTokens(state: ReviewerState): string {
  const inTokens = state.tokensIn ?? 0;
  const outTokens = state.tokensOut ?? 0;

  return inTokens === 0 && outTokens === 0
    ? ''
    : `${thousands(inTokens)} in / ${thousands(outTokens)} out`;
}

/** Parse one session file; a torn or foreign file is skipped rather than crashing the view. */
export function parseSession(text: string): SessionFile | undefined {
  try {
    const parsed = JSON.parse(text) as Partial<SessionFile>;
    return parsed.state?.sessionId !== undefined && Array.isArray(parsed.rounds)
      ? (parsed as SessionFile)
      : undefined;
  } catch {
    return undefined;
  }
}
