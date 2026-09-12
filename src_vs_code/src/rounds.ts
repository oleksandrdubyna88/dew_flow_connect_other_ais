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
  /**
   * The model it was LAUNCHED with — absent in rounds written before 0.18.11.
   *
   * <p>What was asked for, not necessarily what answered: a Team server chooses the model for the
   * account it claims and reports none back. The log shows this because a round that names its
   * vendor and not its model cannot answer the question people actually ask about a slow or a weak
   * reviewer.</p>
   */
  readonly model?: string;
  /**
   * The reasoning effort this launch APPLIED — absent for every reviewer that applied none.
   *
   * <p>Which today is every hosted one: no hosted adapter puts a reasoning flag on its CLI's
   * command line, so recording an effort for one would claim something that did not happen.
   * Antigravity's effort is inside its model id (`gemini-3.7-flash-high`) rather than beside it.</p>
   */
  readonly effort?: string;
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
  /** The role and the model — what this reviewer IS. Starts at the slash, and carries no status. */
  readonly rest: string;
  /**
   * The status and its detail — what this reviewer is DOING. Empty when the file records no status.
   *
   * <p>No leading dash: whoever joins the halves owns the joiner, because the sidebar puts these on
   * two lines and has no use for one.</p>
   */
  readonly said: string;
}

/**
 * The reviewers of a running round, as "codex/Architecture — running" lines.
 *
 * <p>ONE line, which is what the rounds log page shows, searches and exports. The second seam is the
 * sidebar's alone: there the row is two lines, because a model id made one of them long.</p>
 */
export function reviewerLines(round: RoundRecord): readonly string[] {
  // A reviewer whose file records no status used to end in a dangling ` — `. It ends at the model
  // now; raised on the plan round, and it is the one case where this function's output changed.
  return reviewerRows(round).map((row) =>
    `${row.provider}${row.rest}${row.said.length > 0 ? ` — ${row.said}` : ''}`);
}

/**
 * Everything after the provider: the role, the model when there is one, the status, the detail.
 *
 * <p>A function rather than one template because it was three nested ones, which Sonar flags and
 * which had genuinely stopped being readable — the model's separator and the detail's brackets were
 * both conditional inside a literal that was itself inside a literal.</p>
 */
/**
 * One reviewer's row, in its two halves — never one string that is split back apart.
 *
 * <p>Both are BUILT from the fields. A model id is arbitrary text and `Qwen — custom` is a legal
 * one, so a reader that looked for the dash would cut a model in half and call the remainder a
 * status. The plan round raised that four times, each time assuming the opposite; the test with a
 * dash in the model id is what keeps it true.</p>
 *
 * <p>Returns a fresh `{ rest, said }` — what the reviewer IS, and what it is DOING. `said` is empty
 * when the session file records neither a status nor any detail, and the callers then write neither
 * a dangling dash nor an indented empty line.</p>
 */
function restOf(state: ReviewerState, model: string, detail: readonly string[]): { rest: string; said: string } {
  // The effort belongs with the model: both are part of WHAT ran rather than of what it did. It is
  // there only when the launch actually applied one, which today means a local engine; a hosted
  // reviewer has none and reads exactly as it did (issue #129).
  //
  // It survives a MISSING model, though. A local vendor may be configured with no model at all —
  // "whatever the engine answers with" is a first-class choice — and it still runs at an effort, so
  // hanging the effort off the model would hide something that was applied. Raised on the code round.
  const effort = usableString(state.effort);
  const badge = effort ? ` (effort: ${effort})` : '';
  const named = model ? ` · ${model}${badge}` : badge;
  const brackets = detail.length > 0 ? ` (${detail.join(', ')})` : '';
  // Normalised exactly as the model is, and for the same reason: an interface is not runtime
  // validation. A session file that omits `status`, or carries a number, reached `.length` here and
  // threw while the panel was being built — one malformed reviewer blanking the whole Active rounds
  // view. Whitespace is not a status either. Raised on the code round of the two-line row.
  const status = usableString(state.status);

  // The detail survives a missing status: findings and a duration are facts the file still records,
  // and suppressing them because the status is blank hides information rather than tidying it.
  return { rest: `/${state.role}${named}`, said: `${status}${brackets}`.trim() };
}

/** The model this state names, or empty — anything that is not a usable string is absent. */
function modelOf(state: ReviewerState): string {
  return usableString(state.model);
}

/**
 * A field of a session file as a usable string, or empty.
 *
 * <p>One function for the model and the effort, because they need the same distrust for the same
 * reason: an interface is not runtime validation, a hand-edited or foreign session reaches the
 * renderer as-is, and `.trim()` on a number throws while the log is being built — blanking a page
 * to render one row. Whitespace is not a value either, or a separator appears with nothing after
 * it.</p>
 */
function usableString(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
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
      // The model rides with the ROLE rather than in the detail list: it is what the reviewer IS,
      // like its vendor and its role, not something it did. An older round carries none and reads
      // exactly as it did.
      //
      // NORMALISED here as well as where it is written. The writer trims because a configured model
      // of " " is not a model; this side does not trust that, because a state can arrive from an
      // older server or a hand-edited file — and a bare truthiness check turned "   " into a
      // separator with nothing after it, which is what the test below caught.
      //
      // The TYPE check is the same distrust one step further: an interface is not runtime
      // validation, and a session carrying `model: 42` would reach `.trim()` and throw while the
      // log was being built — blanking a page to render one row. Anything that is not a string is
      // absent. Raised on the code round.
      ...restOf(s, modelOf(s), detail),
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
