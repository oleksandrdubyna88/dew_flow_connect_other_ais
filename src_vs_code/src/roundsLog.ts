import { asText } from './asText';
import { Escalation } from './escalations';
import { roundKey, usageRegion } from './panelView';
import { TeamServerState } from './teamServerView';
import { ModelPrice } from './modelPrices';
import { UsageEntry, Window, WINDOWS } from './usage';
import { Vendor } from './vendors';
import { MAX_PLAUSIBLE_SECONDS, reviewerLines, reviewerRows, RoundRecord, SessionFile, stageName } from './rounds';
import { vendorPalette, VendorPalette } from './vendorColour';
import {
  BlindSpot, countsByRound, DbFinding, DbLog, DbTotals, decisionsByRound, EMPTY_LOG, EMPTY_TOTALS,
  findingsByRound, roundKeyOf,
} from './roundsDb';
import { escapeHtml, jsonForScript } from './webviewHtml';

/**
 * The rounds log: every round of every session, as rows a table can sort, filter and search.
 *
 * <p>Asked for on 2026-09-05 over a screenshot of `rounds.md`: fifty-three lines of markdown
 * tables, one per session, each row one unwrapped line running off the right edge, no way to sort
 * by time across sessions, no way to find "every round on branch X". A log is a table. The same
 * ruling left the sidebar with only what is running (`activeRounds.test.ts`); this is where the
 * rest went.</p>
 *
 * <p><b>The predicates are the page's.</b> {@link compareRows} and {@link rowMatches} reference
 * nothing outside their own parameters, and their source is embedded into the webview script
 * verbatim — so the function the tests exercise is the function the table sorts with. The
 * alternative was a TypeScript version here and a hand-copied JavaScript one in the page, which is
 * two implementations of one rule and the usual way one of them ends up wrong.</p>
 *
 * <p>`vscode`-free, like every other page module.</p>
 */

/** One round, flattened with its session, every column derived once. */
export interface LogRow {
  readonly key: string;
  /** ISO, or empty when the server that wrote the round did not record it. Never the epoch. */
  readonly startedUtc: string;
  readonly completedUtc: string;
  readonly repoPath: string;
  /** The folder's own name — for a column; the path is what the filter matches on. */
  readonly repoName: string;
  readonly branch: string;
  /** As the sidebar words it: "plan review", "code review". */
  readonly stage: string;
  readonly number: number;
  readonly subject: string;
  /**
   * `awaiting` is a finished round whose findings nobody has decided about yet — `done` is about the
   * REVIEWERS having answered, and whether the gate was closed is a different fact.
   */
  readonly status: 'running' | 'done' | 'interrupted' | 'awaiting';

  /**
   * How the gate closed: accepted and rejected counts, or `null` for a round the database has never
   * heard of. `-1` in either field is the server saying nobody has decided yet.
   */
  readonly decided: { readonly accepted: number; readonly rejected: number } | null;
  readonly verdict: string;
  readonly gating: number;
  /** The sum over its reviewers, or null when the file recorded none — absent is not zero. */
  readonly findings: number | null;
  /** Completed minus started; so far, for a running round; null for one that died. */
  readonly seconds: number | null;
  readonly tokensIn: number | null;
  readonly tokensOut: number | null;
  readonly costUsd: number | null;
  /** What the round's reviewers READ, in dollars — null when nothing could be priced. */
  readonly costInUsd: number | null;
  /** What they WROTE. */
  readonly costOutUsd: number | null;
  /** The two added up, or what a vendor actually billed when it says so. */
  readonly costTotalUsd: number | null;
  /** True when the number is derived from a public price list rather than billed. */
  readonly costIsEstimate: boolean;
  /** True when at least one reviewer's model had no listed price, so the total is a floor. */
  readonly costPartial: boolean;
  /** How many answered, in the server's own words ("7 of 9 reviewers answered"). */
  readonly answered: string;
  readonly vendors: readonly string[];
  /** One line per reviewer — the same lines the sidebar shows, from the same builder. */
  readonly reviewers: readonly string[];
  /** Parallel to `reviewers`: the vendor word's colour, so the page needs no palette of its own. */
  readonly reviewerColours: readonly string[];
  /**
   * What this round FOUND, from the rounds database — empty when the server predates it.
   *
   * <p>The session files record that a reviewer produced four findings and not what they were, so
   * the Findings column was a number with nothing behind it. These are the sentences. Named apart
   * from `findings`, which is that count and stays what it is.</p>
   */
  readonly found: readonly DbFinding[];
  /**
   * How many it found, which the row knows even before anybody asks WHAT it found.
   *
   * <p>The status column depends on it: a gate still open and a round that raised nothing look the
   * same from the outside, and the only thing that tells them apart is whether a finding exists.</p>
   */
  readonly foundCount: number;
  /**
   * Why the findings are not on screen, when they are not.
   *
   * <p>Five states, because four of them used to be one blank and a blank reads as "clean":
   * `unasked` — nobody has opened this row; `asking` — the read is in flight; `absent` — no database
   * row exists for this round, so its findings were recorded nowhere; `failed` — the read errored or
   * timed out, and there is a retry; `loaded` — `found` is what it found, however few.</p>
   */
  readonly foundState: 'unasked' | 'asking' | 'loaded' | 'absent' | 'failed';
  /**
   * Which half of the merge this row came from.
   *
   * <p>The page is built from the SESSION files and the database only enriches them, so a row can
   * exist that the database has never heard of. That is the difference between a round that found
   * nothing and one whose findings were never written down, and it is knowable here for free.</p>
   */
  readonly origin: 'db' | 'session';
  /**
   * The three fields the rounds database keys this round by.
   *
   * <p>Carried because the page holds only a row KEY, and the read an opened row makes needs the
   * session, the RAW stage and the number. `stage` above is the display name — "code review" — while
   * the database stores "CodeReview"; matching on the wrong one answers "no such round", which the
   * page would then draw as findings that were never recorded.</p>
   */
  readonly dbKey: { readonly sessionId: string; readonly stage: string; readonly number: number };
}

/** The columns a header click can sort by. */
export type SortKey =
  | 'startedUtc' | 'repoName' | 'branch' | 'stage' | 'number' | 'subject' | 'status' | 'verdict'
  | 'gating' | 'findings' | 'seconds' | 'tokensIn' | 'tokensOut' | 'costTotalUsd' | 'answered';

/** The facets a select can narrow by. Empty or absent means "any". */
export interface LogFilters {
  readonly repoPath?: string;
  readonly branch?: string;
  readonly stage?: string;
  readonly status?: string;
  readonly verdict?: string;
  readonly vendor?: string;
  /**
   * Inclusive bounds on WHEN the round started (or finished, when it never recorded a start).
   *
   * <p>Either an instant (`2026-09-05T14:30:00.000Z`, what the page sends once a time is picked) or a
   * bare day (`2026-09-05`). A bare `to` means the END of that day, not its midnight — otherwise
   * "to today" would exclude everything that happened today, which is the whole of today.</p>
   */
  readonly from?: string;
  readonly to?: string;
}

/**
 * What one reviewer's run costs per million tokens, or nothing when nothing prices it.
 *
 * <p>Both halves of the ledger line are passed, and each answers a different question. The MODEL is
 * what a public price list knows, and it is the model the line RECORDED — a round priced from what
 * codex happens to be set to today would change its cost the moment somebody switched models. The
 * VENDOR is what the operator's own typed rate is attached to: a flat subscription, a negotiated
 * rate, a local engine no list has ever heard of. The specific statement wins over the general one,
 * which is the rule the spending tab already used and this now shares.</p>
 */
export type PriceOfModel = (model: string, provider: string) => { inPerMillion: number; outPerMillion: number } | undefined;

/** Every round of every session, newest first. */
export function rowsFrom(
  sessions: readonly SessionFile[],
  nowMs: number = Date.now(),
  priceOf: PriceOfModel = () => undefined,
  usage: readonly UsageEntry[] = [],
  log: DbLog = EMPTY_LOG,
  vendorIds: readonly string[] = [],
): LogRow[] {
  const byRound = findingsByRound(log);
  const counts = countsByRound(log);
  const decided = decisionsByRound(log);
  // Whether the window covers the whole table. When it does, a row the list does not name is a row
  // the database has never heard of, and saying so costs nothing. When it does NOT, the same row
  // might simply be older than the window — and guessing "never recorded" would be a claim about a
  // round nobody checked, so it is asked for instead and the server answers authoritatively.
  // `read` first: an unread log is not an authority on anything, and treating it as one made every
  // row on the page's first paint say the database had no record of it. (Code round, CodeRabbit.)
  const whole = log.read && log.rounds.length >= log.totals.rounds;
  // A server too old to page sent every finding it had, so an empty list from IT is the whole truth
  // about that round — asking again would send `--findings` to a binary that does not know the flag,
  // exit 64, and turn an honestly clean round into a failed read. (Code round, codex.)
  const inline = !log.paged;
  // The CONFIGURED vendors, the same list the panel's cards are coloured from — not the providers
  // these rounds happen to name. A log holding a vendor somebody has since removed still colours it,
  // from its own name, and only such a stray may share a hue with a live reviewer.
  const colour = vendorPalette(vendorIds);

  return sessions
    .flatMap((session) =>
      session.rounds.map((round) =>
        rowFrom(
          session, round, nowMs, priceOf, usage,
          { byRound, counts, decidedBy: decided, colour, whole, inline })))
    .sort((a, b) => (b.startedUtc || b.completedUtc).localeCompare(a.startedUtc || a.completedUtc));
}

/**
 * What every row of one call needs and none of them computes for itself.
 *
 * <p>Grouped rather than passed as three more parameters: the database lookups and the palette are
 * all "worked out once for the whole call", which is a different kind of argument from the session
 * and the round a row IS. `rowFrom` had reached eight parameters, which is where a reader stops
 * being able to check the call site by eye.</p>
 */
interface RowContext {
  readonly byRound: Map<string, readonly DbFinding[]>;
  readonly counts: Map<string, number>;
  readonly decidedBy: Map<string, { accepted: number; rejected: number }>;
  readonly colour: VendorPalette;
  /** Whether the list covers every round the database holds. See `rowsFrom`. */
  readonly whole: boolean;
  /** Whether the list already carried the findings — the shape a server before 0.18.15 answers. */
  readonly inline: boolean;
}

/**
 * A row built from the session files alone — no database, no palette of its own.
 *
 * <p>A named constant rather than a literal in the parameter list: a default object literal is a
 * fresh allocation on every call that omits the argument, and this one holds three maps and a
 * palette. It is never mutated — `rowFrom` only reads from it — so one is enough for all of them.
 * (SonarCloud, on the pull request.)</p>
 */
const NO_DATABASE: RowContext = {
  byRound: new Map(), counts: new Map(), decidedBy: new Map(), colour: vendorPalette([]),
  whole: true, inline: false,
};

function rowFrom(
  session: SessionFile,
  round: RoundRecord,
  nowMs: number,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  context: RowContext = NO_DATABASE,
): LogRow {
  const { byRound, counts, decidedBy, colour, whole, inline } = context;
  const cost = costOf(round, priceOf, usage, nowMs);
  const key = roundKeyOf(
    session.state.sessionId, session.state.repoPath, session.state.branch, round.stage, round.number);
  // A paged server sends no findings with the list, so `found` is empty until a row is opened; an
  // older one sends them all and the row is `loaded` from the start. `counts` is what both have.
  const found = byRound.get(key) ?? [];
  const known = counts.has(key);
  const foundCount = counts.get(key) ?? found.length;
  const decided = decidedBy.get(key) ?? null;
  const status = statusOf(round, foundCount, decided);
  const states = round.reviewerStates ?? [];
  const rows = reviewerRows(round);

  return {
    key: roundKey({ ...round, branch: session.state.branch }),
    startedUtc: round.startedUtc ?? '',
    completedUtc: round.completedUtc,
    repoPath: session.state.repoPath,
    repoName: repoNameOf(session.state.repoPath),
    branch: session.state.branch,
    stage: stageName(round.stage),
    number: round.number,
    subject: round.subject ?? '',
    status,
    decided,
    verdict: round.verdict,
    gating: round.gatingCount,
    findings: states.length === 0 ? null : states.reduce((sum, s) => sum + s.findings, 0),
    seconds: secondsOf(round, status, nowMs),
    tokensIn: round.tokensIn ?? null,
    tokensOut: round.tokensOut ?? null,
    costUsd: round.costUsd ?? null,
    ...cost,
    answered: round.reviewers,
    vendors: [...new Set(states.map((s) => s.provider))],
    reviewers: reviewerLines(round),
    reviewerColours: rows.map((r) => colour(r.provider)),
    found,
    foundCount,
    foundState: foundState(known, whole, inline, found),
    origin: known ? 'db' : 'session',
    dbKey: { sessionId: session.state.sessionId, stage: round.stage, number: round.number },
  };
}

/**
 * What an unopened row already knows about its own findings.
 *
 * <p>A round the database has never heard of is `absent` from the start — asking for it would spawn
 * a process to be told what this already knows. One it holds is `unasked` until somebody opens it,
 * unless an older server already sent the findings, in which case they are here and it is
 * `loaded`.</p>
 */
function foundState(
  known: boolean,
  whole: boolean,
  inline: boolean,
  found: readonly DbFinding[],
): LogRow['foundState'] {
  if (!known) {
    // Only when the list covered everything is "the list did not name it" the same as "the database
    // does not hold it". Otherwise the row asks, and the server answers with the truth.
    return whole ? 'absent' : 'unasked';
  }
  if (inline || found.length > 0) {
    // Either the sentences are here, or they were never coming: a server that answers the old shape
    // sends all of them with the list, so an empty list from it means the round found nothing.
    return 'loaded';
  }

  return 'unasked';
}

/**
 * What the row's Status column says.
 *
 * <p>`done` used to be everything that was not running or dead, and that read as finished while the
 * gate was still open: a screenshot of a round with thirteen `open` findings showed Status `done`,
 * Verdict `good_enough`. Both were true. Together they said the opposite of what was happening.</p>
 *
 * <p>So a fourth state, from the fact the server already records: `accepted` stays −1 until a
 * resolve lands. A round that raised NOTHING is exempt — there is no resolve to make, so its −1
 * never moves and it would sit in "awaiting" for ever. A round the database has never heard of is
 * exempt too: an older server wrote no database, and accusing it of an unclosed gate invents a fact
 * about a round nobody can resolve any more.</p>
 */
function statusOf(
  round: RoundRecord,
  foundCount: number,
  decided: { accepted: number; rejected: number } | null,
): LogRow['status'] {
  if (round.status === 'running') {
    return 'running';
  }
  if (round.status === 'interrupted') {
    return 'interrupted';
  }

  return decided !== null && decided.accepted < 0 && foundCount > 0 ? 'awaiting' : 'done';
}

/**
 * What a round cost, from the USAGE LEDGER.
 *
 * <p>Not from the round's reviewer states: those record `{provider, role, status, findings, note,
 * seconds}` and no tokens at all, which is why the first version of this column was empty on every
 * row in the installed extension. And not from the round's own totals either — they are a sum over
 * vendors whose prices differ by an order of magnitude, so one multiplication over them is a number
 * with no meaning.</p>
 *
 * <p>The ledger has exactly what is needed: one line per reviewer run, with the tokens it used and
 * the MODEL that answered. Each line is priced at its own model's rate, so a round keeps the cost
 * it had when it ran even after somebody points the vendor at a different model.</p>
 *
 * <p>A line with no listed price, or with no tokens recorded, contributes nothing and sets
 * `costPartial`: a total that quietly leaves somebody out is worse than one that says it is a
 * floor. So does a total whose tokens do not add up to what the round itself recorded — two rounds
 * of one stage running at once can each match the other's lines by time alone, and that is the
 * honest way to say the figure may not be only this round's.</p>
 *
 * <p>A vendor that BILLED the round wins over anything worked out from a price list — the two are
 * not the same kind of number, and `costIsEstimate` says which one this is.</p>
 */
function costOf(
  round: RoundRecord,
  priceOf: PriceOfModel,
  usage: readonly UsageEntry[],
  nowMs: number,
): Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'> {
  const billed = round.costUsd ?? null;
  let inUsd = 0;
  let outUsd = 0;
  let priced = 0;
  let unpriced = 0;
  let tokensIn = 0;
  let tokensOut = 0;
  for (const line of linesOf(round, usage, nowMs)) {
    const price = priceOf(line.model, line.provider);
    if (price === undefined || line.tokensIn === undefined || line.tokensOut === undefined) {
      unpriced += 1;
      continue;
    }
    priced += 1;
    tokensIn += line.tokensIn;
    tokensOut += line.tokensOut;
    inUsd += (line.tokensIn / 1_000_000) * price.inPerMillion;
    outUsd += (line.tokensOut / 1_000_000) * price.outPerMillion;
  }
  const nothingPriced = priced === 0;

  return {
    costInUsd: nothingPriced ? null : round4(inUsd),
    costOutUsd: nothingPriced ? null : round4(outUsd),
    costTotalUsd: billed ?? (nothingPriced ? null : round4(inUsd + outUsd)),
    costIsEstimate: billed === null && !nothingPriced,
    costPartial: !nothingPriced && (unpriced > 0 || drifted(round, tokensIn + tokensOut)),
  };
}

/**
 * The ledger lines that belong to this round: written while it ran, and of its own stage.
 *
 * <p>By time and stage because that is all there is to match on — a ledger line names no round.
 * (The local database will carry the round's id on every line and end the guessing; until then this
 * is exact for one round at a time and marked partial when the tokens disagree.)</p>
 */
function linesOf(round: RoundRecord, usage: readonly UsageEntry[], nowMs: number): readonly UsageEntry[] {
  const from = round.startedUtc ?? '';
  if (from.length === 0 || usage.length === 0) {
    return [];
  }
  const to = round.completedUtc.length > 0 ? round.completedUtc : new Date(nowMs).toISOString();

  return usage.filter(
    (line) => line.utc >= from && line.utc <= to && line.stage.toLowerCase() === round.stage.toLowerCase());
}

/** Whether the priced lines add up to what the round said it used, within a fiftieth. */
function drifted(round: RoundRecord, counted: number): boolean {
  const recorded = (round.tokensIn ?? 0) + (round.tokensOut ?? 0);

  return recorded > 0 && Math.abs(counted - recorded) > recorded / 50;
}

/** Cents-and-a-bit, so a sum of many small numbers does not drift into float noise. */
function round4(value: number): number {
  return Math.round(value * 10_000) / 10_000;
}

export function repoNameOf(repoPath: string): string {
  // Coerced for the reason the escapers are: this reads `session.state.repoPath` straight out of a
  // JSON file that nothing validates, and `.replace` on a number is the error that stopped a person
  // opening the log on 2026-09-08 — at the moment a question was waiting on them.
  const text = asText(repoPath);
  const parts = text.replace(/\\/g, '/').replace(/\/+$/, '').split('/');
  return parts[parts.length - 1] ?? text;
}

/**
 * How long a round took, or has taken — and no number at all for one that died.
 *
 * <p>An interrupted round was never written a completion time; measuring it against `now` would
 * report how long ago it started, which is what the sidebar did for a day and called `361m 40s`.</p>
 */
function secondsOf(round: RoundRecord, status: LogRow['status'], nowMs: number): number | null {
  const started = Date.parse(round.startedUtc ?? '');
  if (Number.isNaN(started)) {
    return null;
  }
  if (status === 'running') {
    return Math.max(0, Math.round((nowMs - started) / 1000));
  }
  if (status === 'interrupted') {
    return null;
  }
  const completed = Date.parse(round.completedUtc);
  if (Number.isNaN(completed)) {
    return null;
  }
  const seconds = Math.round((completed - started) / 1000);
  // .NET's default date is year ONE, and a server that never recorded a start wrote exactly that.
  // Subtracting it from a real completion time produced "1065396701m 44s" once; the sidebar's
  // `elapsed` refuses the same way, with the same cap.
  return seconds < 0 || seconds > MAX_PLAUSIBLE_SECONDS ? null : seconds;
}

// ---------------------------------------------------------------------------------------------
// The two functions below run in the PAGE. They must reference nothing outside their parameters
// — no import, no module constant, no helper — and use no template literal, because their source
// is embedded into a script that itself lives inside one. `roundsLog.test.ts` asserts both.
// ---------------------------------------------------------------------------------------------

/**
 * Orders two rows by one column. A blank sorts after every real value in BOTH directions: a
 * missing number is not a small one, and "oldest first" must not begin with rounds that have no
 * date at all.
 */
export function compareRows(a: LogRow, b: LogRow, key: SortKey, dir: 'asc' | 'desc'): number {
  const x = (a as unknown as Record<string, unknown>)[key];
  const y = (b as unknown as Record<string, unknown>)[key];
  const xBlank = x === null || x === undefined || x === '';
  const yBlank = y === null || y === undefined || y === '';
  if (xBlank && yBlank) {
    return 0;
  }
  if (xBlank) {
    return 1;
  }
  if (yBlank) {
    return -1;
  }
  const sign = dir === 'asc' ? 1 : -1;
  if (typeof x === 'number' && typeof y === 'number') {
    return (x - y) * sign;
  }
  return String(x).localeCompare(String(y)) * sign;
}

/**
 * A wall-clock value from a `datetime-local` input, as the instant the filter compares against.
 *
 * <p>What the input holds is WALL CLOCK and what a round records is UTC, so comparing the two as
 * strings is wrong by the reader's offset. `endOfMinute` includes the minute the bound names, which
 * is what a minute-granularity picker means by it — without it "to 23:59" ends at 23:59:00.000 and
 * the last minute of today falls outside the range the page opens on.</p>
 */
export function asInstant(localValue: string, endOfMinute: boolean): string {
  if (!localValue) {
    return '';
  }
  var at = new Date(localValue).getTime();

  return isNaN(at) ? '' : new Date(at + (endOfMinute ? 59999 : 0)).toISOString();
}

/**
 * What a figure in a money column says, or an em dash where there is no figure.
 *
 * <p>Embedded into the page by its source text, like every function below it — so it references
 * nothing outside itself. {@link cost3} and {@link costTitle} take it as an ARGUMENT rather than
 * calling it by name: a function embedded by its text lands in a scope where only its own name was
 * re-declared, so a call to a module-level binding arrives under whatever the minifier called it
 * (0.29.12 shipped exactly that, and the page died with "R is not defined"). An argument cannot be
 * renamed out from under it, and there is one copy of the formatter rather than three.</p>
 */
export function money(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—';
  }

  return '$' + value.toFixed(value < 1 ? 3 : 2);
}

/**
 * The three figures the column is named for: what the round READ, what it WROTE, and the sum.
 *
 * <p>Always three, always with the slashes, an em dash standing in for anything unknown — a vendor
 * that bills one total and reports no split reads `— / — / $0.42` rather than a lone number the
 * header promises to be three. And a round nothing could price is a dash, never an empty cell: an
 * empty cell says "nothing to see", which is a different claim from "nobody knows".</p>
 *
 * <p>A tilde marks a total worked out from a public price list rather than one a vendor billed; a
 * plus marks a total that had to leave a reviewer out, so it is a floor.</p>
 */
export function cost3(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return '—';
  }

  return (row.costIsEstimate ? '~' : '')
    + figure(row.costInUsd) + ' / ' + figure(row.costOutUsd) + ' / ' + figure(row.costTotalUsd)
    + (row.costPartial ? '+' : '');
}

/** The same thing in words, for the cell's tooltip — the column is narrow and the marks are one character. */
export function costTitle(row: Costed, figure: Money): string {
  if (typeof row.costTotalUsd !== 'number') {
    return 'No price is listed for these models, so this round has no cost figure.';
  }
  var how = row.costIsEstimate
    ? 'Worked out from a public price list, not billed.'
    : 'Reported by the vendor.';
  var part = row.costPartial
    ? ' Some of it could not be priced, so the total is a floor.'
    : '';

  return 'Input ' + figure(row.costInUsd) + ' + output ' + figure(row.costOutUsd)
    + ' = ' + figure(row.costTotalUsd) + '. ' + how + part;
}

/** How a figure is written. Passed IN, never reached for — see the remark on {@link money}. */
export type Money = (value: number | null | undefined) => string;

/** Just the cost fields, because these three run in the page against plain row objects. */
export type Costed = Pick<LogRow, 'costInUsd' | 'costOutUsd' | 'costTotalUsd' | 'costIsEstimate' | 'costPartial'>;

/** Whether a row survives the selects and the search box. A blank search is no search. */
export function rowMatches(row: LogRow, filters: LogFilters, search: string): boolean {
  if (filters.repoPath && row.repoPath !== filters.repoPath) {
    return false;
  }
  if (filters.branch && row.branch !== filters.branch) {
    return false;
  }
  if (filters.stage && row.stage !== filters.stage) {
    return false;
  }
  if (filters.status && row.status !== filters.status) {
    return false;
  }
  if (filters.verdict && row.verdict !== filters.verdict) {
    return false;
  }
  if (filters.vendor && row.vendors.indexOf(filters.vendor) < 0) {
    return false;
  }
  // The date range. ISO-8601 instants of the same shape compare correctly as plain strings, so the
  // bounds are inclusive by comparison. A bare day as the UPPER bound means the end of that day: an
  // upper bound of 2026-09-05 against 2026-09-05T14:30Z would otherwise exclude the whole of today,
  // which is exactly the range somebody picking today wants. A round with no date at all cannot be
  // shown to be inside a bounded range, so a bound drops it; no bound keeps it.
  const at = row.startedUtc || row.completedUtc;
  if ((filters.from || filters.to) && at.length === 0) {
    return false;
  }
  if (filters.from && at < filters.from) {
    return false;
  }
  if (filters.to && at > (filters.to.indexOf('T') < 0 ? filters.to + 'T23:59:59.999Z' : filters.to)) {
    return false;
  }
  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return true;
  }
  const haystack = [row.subject, row.branch, row.repoPath, row.repoName].concat(row.reviewers).join(' ').toLowerCase();
  return haystack.indexOf(needle) >= 0;
}

// ---------------------------------------------------------------------------------------------
// The page.
// ---------------------------------------------------------------------------------------------

/** The open questions, as the block at the top of the page — the same block the tick re-posts. */
export function questionsHtml(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const items = questions.map((q) => {
    const findings = q.openFindings
      .map((f) => `<li><code>${escapeHtml(f.severity)}</code> ${escapeHtml(f.category)} `
        + `${f.file ? `<code>${escapeHtml(f.file)}:${f.line ?? ''}</code> ` : ''}— ${escapeHtml(f.title)}</li>`)
      .join('');
    return `<div class="question">
  <div class="where">${escapeHtml(q.branch)} · ${escapeHtml(q.repoPath)} · asked ${escapeHtml(q.askedUtc)}</div>
  <p>${escapeHtml(q.question)}</p>
  ${findings.length > 0 ? `<div class="gating">Still gating:</div><ul>${findings}</ul>` : '<div class="gating">No findings attached.</div>'}
  <button type="button" data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
</div>`;
  });

  return `<h2>Open questions — a review is waiting on you</h2>\n${items.join('\n')}`;
}

/**
 * The spending tab's body: the window buttons and the per-vendor region the sidebar used to carry.
 *
 * <p>Moved here from the sidebar on 2026-09-05 — "перенеси отдельной табой в Review rounds и убери из
 * дерева" — with Today as the default window, since midnight. The region itself is the function the
 * sidebar rendered, so there is one renderer for a vendor's row.</p>
 */
export function usageTabHtml(
  usage: readonly UsageEntry[],
  window: Window,
  vendors: readonly Vendor[],
  prices: Readonly<Record<string, ModelPrice>>,
  teamServers: readonly TeamServerState[] = [],
  usageScope: 'me' | 'company' = 'me',
): string {
  const buttons = WINDOWS
    .map((w) => `<button type="button" class="tab${w.id === window ? ' on' : ''}" data-command="usageWindow" data-id="${w.id}">${escapeHtml(w.label)}</button>`)
    .join('');

  return `<div class="windows">${buttons}</div>` + '\n' + `<div class="usage-rows">${usageRegion(usage, window, vendors, prices, teamServers, usageScope)}</div>`;
}

const COLUMNS: ReadonlyArray<{ key: SortKey; label: string; numeric?: boolean }> = [
  { key: 'startedUtc', label: 'When' },
  { key: 'repoName', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'number', label: 'Round', numeric: true },
  { key: 'subject', label: 'What' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'gating', label: 'Gating', numeric: true },
  { key: 'findings', label: 'Findings', numeric: true },
  { key: 'seconds', label: 'Took', numeric: true },
  { key: 'tokensIn', label: 'Tokens in', numeric: true },
  { key: 'tokensOut', label: 'Tokens out', numeric: true },
  { key: 'costTotalUsd', label: 'Cost', numeric: true },
  { key: 'answered', label: 'Reviewers' },
];

type Facet = 'repoPath' | 'branch' | 'stage' | 'status' | 'verdict' | 'vendor';

const FACETS: ReadonlyArray<{ key: Facet; label: string }> = [
  { key: 'repoPath', label: 'Repository' },
  { key: 'branch', label: 'Branch' },
  { key: 'stage', label: 'Stage' },
  { key: 'status', label: 'Status' },
  { key: 'verdict', label: 'Verdict' },
  { key: 'vendor', label: 'Vendor' },
];

/** A select's options for one facet, from the rows themselves — a filter offers only what exists. */
function facetOptions(rows: readonly LogRow[], key: Facet): string {
  const values = key === 'vendor'
    ? [...new Set(rows.flatMap((r) => r.vendors))]
    : [...new Set(rows.map((r) => r[key]))];
  return values
    .filter((v) => v.length > 0)
    .sort((a, b) => a.localeCompare(b))
    .map((v) => `<option value="${escapeHtml(v)}">${escapeHtml(key === 'repoPath' ? repoNameOf(v) : v)}</option>`)
    .join('');
}

/**
 * The whole page: questions, a toolbar of filters and a search box, and the table.
 *
 * <p>The table BODY is rendered by the page from the rows as JSON — sorting, filtering and
 * searching are page state, and a live push of new rows re-renders the body alone, so the sort,
 * the filters, the search text, the scroll position and the expanded rows all survive it. The
 * questions block is rendered here and re-posted as HTML by the same function, so there is one
 * renderer for it.</p>
 */
/**
 * The two questions this data exists to answer, as a region of the page.
 *
 * <p>Operator, 2026-09-05: <i>"я хочу сначала идентифицировать [белые пятна], а потом уже или рагом
 * или математикой дать инструмент, который закроет эти пятна"</i>. Identifying them is this.</p>
 *
 * <p><b>What it accepted</b> is the blind-spot corpus: a finding the caller took is by definition
 * something it had not seen and then agreed was worth having. Shown as accepted OVER total, because
 * a category that produces fifty findings and gets two taken says something quite different from
 * one that produces two and gets both — the second is the blind spot; the first is noise.</p>
 *
 * <p><b>What it argued with and got again</b> is the shorter and sharper list: the caller rejected
 * it with a reason, the rejection still stood, and a reviewer raised it anyway.</p>
 */
export function blindSpotsHtml(log: DbLog): string {
  if (log.blindSpots.length === 0 && log.defended.length === 0) {
    return '<div class="empty">Nothing decided yet. This fills in as gates are closed —'
      + ' every accepted finding is something the AI had not seen and then agreed was worth having.</div>';
  }

  return '<div class="spots">'
    + spotTable('By category', log.blindSpots.filter((s) => s.kind === 'category'))
    + spotTable('By reviewer role', log.blindSpots.filter((s) => s.kind === 'role'))
    + spotTable('By vendor', log.blindSpots.filter((s) => s.kind === 'providers'))
    + '</div>'
    + defendedHtml(log.defended);
}

function spotTable(title: string, spots: readonly BlindSpot[]): string {
  if (spots.length === 0) {
    return '';
  }
  const rows = [...spots]
    .sort((a, b) => b.accepted - a.accepted || b.total - a.total)
    .map((s) => `<tr><td>${escapeHtml(s.name)}</td><td class="num">${s.accepted}</td>`
      + `<td class="num">${s.total}</td><td class="num">${share(s)}</td></tr>`)
    .join('');

  return `<div><h2>${escapeHtml(title)}</h2><table><thead><tr><th></th>`
    + '<th class="num">taken</th><th class="num">of</th><th class="num">%</th>'
    + `</tr></thead><tbody>${rows}</tbody></table></div>`;
}

function share(spot: BlindSpot): string {
  return spot.total === 0 ? '—' : `${Math.round((100 * spot.accepted) / spot.total)}`;
}

/** What the server caps that list at; it sends one more so the page can say there are more. */
const DEFENDED_CAP = 200;

function defendedHtml(defended: readonly DbFinding[]): string {
  if (defended.length === 0) {
    return '';
  }
  const items = defended
    .slice(0, DEFENDED_CAP)
    .map((f) => `<div class="finding declined"><span class="sev">${escapeHtml(f.severity)}</span> `
      + `<span class="where">${escapeHtml(f.file ? `${f.file}:${f.line}` : 'no file')}</span> `
      + `<b>${escapeHtml(f.title)}</b><div class="why">${escapeHtml(f.why)}</div>`
      + `<div class="verdict">${escapeHtml(f.providers)} / ${escapeHtml(f.role)}`
      + `${f.reason ? ` &middot; the standing reason: ${escapeHtml(f.reason)}` : ''}</div></div>`)
    .join('');

  // The server fetches one more than it will show, so "exactly two hundred" and "more than that"
  // are different answers. A list that was cut and looks whole is worse than no list.
  const cut = defended.length > DEFENDED_CAP
    ? ` The most recent ${DEFENDED_CAP} of them; there are more.`
    : '';

  return '<h2>Rejected, and raised again anyway</h2>'
    + '<div class="hint">A disagreement the caller is defending — the rejection still stood when'
    + ` another reviewer made the same case.${cut}</div>`
    + `<div class="findings">${items}</div>`;
}

/**
 * What a tab shows between the first paint and its first push.
 *
 * <p>The first paint is deliberately database-free — reading the log spawns a process, and nobody
 * should wait on one to see their log — so both of these regions open empty and are filled a moment
 * later. An empty div said nothing, which is precisely how a push that never arrived came to look
 * identical to one that simply had not arrived yet: the tab the operator opened on 2026-09-08 was
 * blank, and blank is also what it correctly shows for the first half-second of every open.</p>
 *
 * <p>It names no region, because the reader is standing on the tab that names itself — and because
 * a template that took one produced "Reading the log for what it keeps missing…". (The code
 * round, twice.) A section still reading this after `STILL_NOTHING_MS` replaces it with what went
 * wrong; see the page script.</p>
 */
function waitingFor(): string {
  return '<div class="empty">Reading the log…</div>';
}

/**
 * How many rows one page holds.
 *
 * <p>Two hundred, from the operator on 2026-09-09: «у нас есть пагинаций. 200 на стр достаточно.»
 * The same number the server pages its own list at, and it is a constant rather than a setting on
 * purpose — a configurable page size is the first step towards a page-number strip, which is the
 * growth this deliberately does not have.</p>
 */
export const PAGE_SIZE = 200;

export function roundsLogHtml(
  rows: readonly LogRow[],
  questions: readonly Escalation[],
  nonce: string,
  usageHtml = '',
  spotsHtml = '',
  totals: DbTotals = EMPTY_TOTALS,
): string {
  const headers = COLUMNS
    .map((c) => `<th data-sort="${c.key}"${c.numeric ? ' class="num"' : ''}>${c.label}</th>`)
    .join('');
  const filters = FACETS
    .map((f) => `<label>${f.label} <select data-filter="${f.key}"><option value="">any</option>${facetOptions(rows, f.key)}</select></label>`)
    .join('\n      ');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>ConnectOtherAIs — review rounds</title>
<style>
  body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 16px 20px; }
  h1 { font-size: 1.3em; margin: 0 0 12px; }
  h2 { font-size: 1.1em; margin: 16px 0 8px; }
  .question { border: 1px solid var(--vscode-inputValidation-warningBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .question .where { opacity: .75; font-size: .92em; margin-bottom: 4px; }
  .question .gating { margin-top: 6px; opacity: .85; }
  .question ul { margin: 4px 0 8px 18px; padding: 0; }
  .toolbar { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin: 12px 0; }
  .toolbar label { display: inline-flex; gap: 6px; align-items: center; font-size: .92em; }
  .toolbar input, .toolbar select { background: var(--vscode-input-background); color: var(--vscode-input-foreground); border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px; padding: 3px 6px; font: inherit; }
  .toolbar input { min-width: 220px; }
  #count { opacity: .75; margin-left: auto; font-size: .92em; }
  button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: none; border-radius: 2px; padding: 4px 10px; font: inherit; cursor: pointer; }
  button.secondary { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
  .wrap { overflow-x: auto; }
  table { border-collapse: collapse; width: 100%; font-size: .95em; }
  th, td { text-align: left; padding: 5px 8px; border-bottom: 1px solid var(--vscode-panel-border); white-space: nowrap; vertical-align: top; }
  th { position: sticky; top: 0; background: var(--vscode-editor-background); cursor: pointer; user-select: none; }
  th.num, td.num { text-align: right; font-variant-numeric: tabular-nums; }
  td.cost { white-space: nowrap; }
  th[data-sort].asc::after { content: " ▲"; opacity: .7; }
  th[data-sort].desc::after { content: " ▼"; opacity: .7; }
  td.what { white-space: normal; min-width: 260px; max-width: 560px; }
  /* Long cells are cut with an ellipsis rather than pushing the table sideways; the full text is
     the cell's own title, so hovering reads it. */
  td.who-answered { max-width: 320px; overflow: hidden; text-overflow: ellipsis; }
  td[title] { cursor: help; }
  tr[data-key] { cursor: pointer; }
  tr[data-key]:hover td { background: var(--vscode-list-hoverBackground); }
  tr.detail td { white-space: normal; background: var(--vscode-editorWidget-background); padding: 6px 8px 8px 28px; cursor: default; }
  .reviewer { font-size: .95em; margin: 1px 0; }
  .who { font-weight: 600; }
  .badge { display: inline-block; padding: 0 6px; border-radius: 8px; font-size: .88em; }
  .badge.running { background: var(--vscode-charts-blue); color: var(--vscode-editor-background); }
  .badge.interrupted { background: var(--vscode-charts-orange); color: var(--vscode-editor-background); }
  .badge.done { background: var(--vscode-badge-background); color: var(--vscode-badge-foreground); }
  .badge.awaiting { background: var(--vscode-charts-purple); color: var(--vscode-editor-background); }
  .decided { margin-left: 6px; opacity: .85; white-space: nowrap; }
  .empty { opacity: .75; padding: 24px 0; }
  .failed { border: 1px solid var(--vscode-inputValidation-errorBorder, #c33); background: var(--vscode-inputValidation-errorBackground, transparent); padding: 8px 12px; margin: 0 0 12px; white-space: pre-wrap; }
  .tabs { display: flex; gap: 6px; margin: 4px 0 10px; border-bottom: 1px solid var(--vscode-panel-border); }
  .tabs .tab, .windows .tab { background: transparent; color: var(--vscode-foreground); border: none; border-bottom: 2px solid transparent; border-radius: 0; padding: 6px 10px; opacity: .75; }
  .tabs .tab.on, .windows .tab.on { opacity: 1; border-bottom-color: var(--vscode-focusBorder); }
  .windows { display: flex; gap: 6px; margin: 0 0 10px; }
  .spend { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 8px 12px; margin: 0 0 8px; max-width: 640px; }
  .spend .head { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .spend .name { font-weight: 600; }
  .spend .cost { margin-left: auto; }
  .spend .bar { height: 6px; background: var(--vscode-progressBar-background, var(--vscode-panel-border)); border-radius: 3px; margin: 6px 0; overflow: hidden; }
  .spend .bar span { display: block; height: 100%; background: var(--vscode-charts-blue); }
  .spend .figures { font-size: .95em; }
  .spend .hint, .total { margin-top: 2px; }
  .warn { color: var(--vscode-charts-yellow); }
  .link { background: none; border: none; color: var(--vscode-textLink-foreground); padding: 0 4px; }
  .hint { opacity: .65; font-size: .9em; margin-top: 10px; }
  .findings { margin-top: 8px; }
  .finding { border-left: 3px solid var(--vscode-panel-border); padding: 2px 0 6px 10px; margin: 6px 0; }
  .finding.took { border-left-color: var(--vscode-charts-green, var(--vscode-charts-blue)); }
  .finding.declined { border-left-color: var(--vscode-charts-orange); }
  .finding .sev { font-weight: 600; }
  .finding .where { opacity: .75; font-family: var(--vscode-editor-font-family); font-size: .92em; }
  .finding .why { opacity: .85; margin: 2px 0 0; }
  .finding .verdict { opacity: .75; font-size: .92em; margin-top: 3px; }
  .finding .again { color: var(--vscode-charts-orange); font-size: .9em; }
  .pager { display: flex; align-items: center; gap: 8px; margin: 10px 0 0; }
  .pager #pageinfo { opacity: .75; font-size: .92em; }
  .await, .none, .nokeep, .broke { opacity: .8; font-size: .92em; margin-top: 8px; }
  .broke { color: var(--vscode-charts-orange); }
  .spots { display: flex; flex-wrap: wrap; gap: 18px; }
  .spots table { width: auto; min-width: 260px; }
</style>
</head>
<body>
<h1>Review rounds</h1>
<div id="failed" class="failed" hidden></div>
<div id="questions">${questionsHtml(questions)}</div>
<div class="tabs"><button type="button" class="tab on" data-tab="rounds">Rounds</button><button type="button" class="tab" data-tab="usage">What each AI has used</button><button type="button" class="tab" data-tab="spots">What it keeps missing</button></div>
<section id="tab-rounds">
<div class="toolbar">
      <input id="search" type="search" placeholder="Search subject, branch, repository, reviewers…" autocomplete="off">
      <label>From <input id="from" type="datetime-local" step="60"></label>
      <label>To <input id="to" type="datetime-local" step="60"></label>
      <button type="button" class="secondary" id="today">Today</button>
      <button type="button" class="secondary" id="alldates">All dates</button>
      ${filters}
      <button type="button" class="secondary" id="clear">Clear</button>
      <span id="count"></span>
</div>
<div class="wrap">
<table id="log">
  <thead><tr>${headers}</tr></thead>
  <tbody id="rows"></tbody>
</table>
</div>
<div id="empty" class="empty"${rows.length === 0 ? '' : ' hidden'}>No rounds yet. A session appears once an AI calls <code>open</code> for a repository and branch.</div>
<div class="pager">
  <button type="button" class="secondary" id="prev">◀ Newer</button>
  <button type="button" class="secondary" id="next">Older ▶</button>
  <span id="pageinfo"></span>
</div>
<div id="recorded" class="hint"></div>
<div class="hint">Showing <b>today</b> — <b>All dates</b> clears the range, and the pickers take a time as well as a day. Cost is <b>in / out / total</b> — <code>~</code> means worked out from a public price list rather than billed, <code>+</code> means one reviewer's model had no listed price so the total is a floor. Click a column to sort, a row to see its reviewers. The table advances by itself while a round runs; your sort, filters and search stay.</div>
</section>
<section id="tab-usage" hidden><div id="usage-body">${usageHtml || waitingFor()}</div></section>
<section id="tab-spots" hidden><div id="spots-body">${spotsHtml || waitingFor()}</div></section>
<script nonce="${nonce}">
(function () {
  // A page that fails must say so on the page. The first release of this page came up as a header
  // row over nothing inside VS Code's webview, while the same HTML rendered every row in node and
  // in headless Chromium; whatever it was, it said nothing. Now it says what and where.
  function failed(message) {
    var box = document.getElementById('failed');
    if (box) {
      box.hidden = false;
      box.textContent = 'This page hit an error and stopped: ' + message + '. Reload the window (Developer: Reload Window); if it comes back, copy this text into an issue.';
    }
  }
  window.onerror = function (message, source, line, column) {
    failed(String(message) + ' (line ' + line + ':' + column + ')');
  };
  var vscode = acquireVsCodeApi();
  // Which sections opened on the "Reading the log…" placeholder, stated by the render rather than
  // read back out of the DOM: what a section CONTAINS is HTML from the database, and searching it
  // for the placeholder's own words is a check that a blind spot titled "Reading the log" would
  // defeat.
  var WAITING = ${JSON.stringify({ usage: usageHtml === '', spots: spotsHtml === '' })};
  var ROWS = ${jsonForScript(rows)};
  // Assigned, never declared: the extension ships BUNDLED and minified, and a minifier renames a
  // function that is not a top-level export — so the declaration this embedded read
  // "function m(a, b, c, d)" in the VSIX while the page called rowMatches(). It worked from the
  // unbundled out/ in node and in headless Chromium, and failed only in the installed extension,
  // which is why the page now reports its own errors (0.29.10 found this one in a minute).
  var compareRows = ${compareRows.toString()};
  var rowMatches = ${rowMatches.toString()};
  var money = ${money.toString()};
  var cost3 = ${cost3.toString()};
  var costTitle = ${costTitle.toString()};

  var state = { sortKey: 'startedUtc', dir: 'desc', filters: {}, search: '', expanded: {}, page: 0 };
  var PAGE_SIZE = ${PAGE_SIZE};
  // What SQL counted over the WHOLE table, which is a different number from the length of what was
  // sent. The operator asked for this in as many words: «суммы - скл счиатть (сколько всего и тд.)».
  var TOTALS = ${jsonForScript(totals)};
  // Every filter, the search text and the sort all return to the first page. "Next page" of a
  // client-side filter over a server-side window is a promise nothing can keep: the cursor moves in
  // the unfiltered stream and rows skip or repeat across the boundary. (Plan round, gemini.)
  function firstPage() { state.page = 0; }
  function localDay(d) {
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate());
  }
  var asInstant = ${asInstant.toString()};

  function esc(value) {
    return String(value)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }
  function when(iso) {
    if (!iso) { return ''; }
    var d = new Date(iso);
    if (isNaN(d.getTime())) { return esc(iso); }
    var p = function (n) { return (n < 10 ? '0' : '') + n; };
    return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes());
  }
  function num(n) {
    if (n === null || n === undefined) { return ''; }
    if (n >= 1000000) { return (n / 1000000).toFixed(1) + 'M'; }
    if (n >= 1000) { return (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k'; }
    return String(n);
  }
  function took(s) {
    if (s === null || s === undefined) { return ''; }
    if (s < 60) { return s + ' s'; }
    var m = Math.floor(s / 60);
    return m < 60 ? m + 'm ' + (s % 60) + 's' : Math.floor(m / 60) + 'h ' + (m % 60) + 'm';
  }

  function badge(status) {
    var said = status === 'awaiting' ? 'awaiting decisions' : status;
    return '<span class="badge ' + esc(status) + '">' + esc(said) + '</span>';
  }
  function decided(row) {
    // Nothing for a round the database never saw, and nothing while the gate is still open - the
    // badge already says that. A closed gate shows what it closed AT, which is the whole reason
    // these two numbers are recorded.
    if (!row.decided || row.decided.accepted < 0) { return ''; }
    var a = row.decided.accepted, r = row.decided.rejected;
    return '<span class="decided" title="' + a + ' accepted, ' + r + ' rejected">'
      + a + ' ✓ ' + r + ' ✗</span>';
  }
  function detail(row) {
    if (row.reviewers.length === 0) {
      return '<div class="reviewer">This round recorded no reviewer detail — it was written by an older server.</div>';
    }
    var lines = '';
    for (var i = 0; i < row.reviewers.length; i++) {
      var line = row.reviewers[i];
      var slash = line.indexOf('/');
      var who = slash < 0 ? line : line.slice(0, slash);
      var rest = slash < 0 ? '' : line.slice(slash);
      lines += '<div class="reviewer"><span class="who" style="color:' + esc(row.reviewerColours[i] || 'inherit') + '">' + esc(who) + '</span>' + esc(rest) + '</div>';
    }
    return lines + foundHtml(row);
  }
  // What the round FOUND, under the reviewers that found it. Severity first because that is how a
  // person triages, then where, then what it said and what was decided about it - an accepted
  // finding is something this repository's author had not seen, which is the whole point of keeping
  // them.
  function foundHtml(row) {
    // Five states, and each draws its OWN element. Four of them used to be one blank, and a blank
    // reads as "this round was clean" — which is a lie about three of them.
    if (row.foundState === 'asking') {
      return '<div class="await">Reading what this round found…</div>';
    }
    if (row.foundState === 'failed') {
      return '<div class="broke">Its findings could not be read. '
        + '<button type="button" class="link" data-retry="' + esc(row.key) + '">Try again</button></div>';
    }
    if (row.foundState === 'absent') {
      return '<div class="nokeep">The rounds database has no record of this round, so what it found '
        + 'was never written down — it ran before the database existed.</div>';
    }
    if (!row.found || row.found.length === 0) {
      // A row can be loaded with nothing in it while the count says otherwise: the count comes
      // from the list and the sentences from a later read, and the database moves between them.
      // Without this it drew "Reading…" for ever, because ask() refuses a loaded row. (CodeRabbit.)
      return row.foundCount > 0
        ? '<div class="broke">This round recorded ' + row.foundCount
          + ' findings, and the read came back with none. '
          + '<button type="button" class="link" data-retry="' + esc(row.key) + '">Try again</button></div>'
        : '<div class="none">This round found nothing.</div>';
    }
    var out = '<div class="findings">';
    for (var i = 0; i < row.found.length; i++) {
      var f = row.found[i];
      var mark = f.resolution === 'accept' ? 'took' : f.resolution === 'reject' ? 'declined' : 'open';
      out += '<div class="finding ' + esc(mark) + '">'
        + '<span class="sev">' + esc(f.severity) + '</span> '
        + '<span class="where">' + esc(f.file ? f.file + ':' + f.line : 'no file') + '</span> '
        + '<b>' + esc(f.title) + '</b>'
        + (f.reRaised ? ' <span class="again">raised again</span>' : '')
        + '<div class="why">' + esc(f.why) + '</div>'
        + (f.fix ? '<div class="why"><i>fix:</i> ' + esc(f.fix) + '</div>' : '')
        + '<div class="verdict">' + esc(f.providers) + ' / ' + esc(f.role) + ' &middot; <b>' + esc(mark) + '</b>'
        + (f.reason ? ': ' + esc(f.reason) : '') + '</div>'
        + '</div>';
    }
    return out + '</div>';
  }
  function render() {
    var matched = ROWS.filter(function (r) { return rowMatches(r, state.filters, state.search); });
    matched.sort(function (a, b) { return compareRows(a, b, state.sortKey, state.dir); });
    var pages = Math.max(1, Math.ceil(matched.length / PAGE_SIZE));
    if (state.page > pages - 1) { state.page = pages - 1; }
    if (state.page < 0) { state.page = 0; }
    var from = state.page * PAGE_SIZE;
    var shown = matched.slice(from, from + PAGE_SIZE);
    var html = '';
    for (var i = 0; i < shown.length; i++) {
      var r = shown[i];
      html += '<tr data-key="' + esc(r.key) + '">'
        + '<td>' + when(r.startedUtc || r.completedUtc) + '</td>'
        + '<td title="' + esc(r.repoPath) + '">' + esc(r.repoName) + '</td>'
        + '<td title="' + esc(r.branch) + '">' + esc(r.branch) + '</td>'
        + '<td>' + esc(r.stage) + '</td>'
        + '<td class="num">' + r.number + '</td>'
        + '<td class="what" title="' + esc(r.subject) + '">' + esc(r.subject) + '</td>'
        + '<td>' + badge(r.status) + decided(r) + '</td>'
        + '<td>' + esc(r.verdict) + '</td>'
        + '<td class="num">' + r.gating + '</td>'
        + '<td class="num">' + num(r.findings) + '</td>'
        + '<td class="num">' + took(r.seconds) + '</td>'
        + '<td class="num">' + num(r.tokensIn) + '</td>'
        + '<td class="num">' + num(r.tokensOut) + '</td>'
        + '<td class="num cost" title="' + esc(costTitle(r, money)) + '">' + cost3(r, money) + '</td>'
        + '<td class="who-answered" title="' + esc(r.answered) + '">' + esc(r.answered) + '</td>'
        + '</tr>';
      if (state.expanded[r.key]) {
        html += '<tr class="detail"><td colspan="15">' + detail(r) + '</td></tr>';
      }
    }
    document.getElementById('rows').innerHTML = html;
    document.getElementById('empty').hidden = ROWS.length > 0;
    document.getElementById('count').textContent = matched.length === ROWS.length
      ? ROWS.length + ' round' + (ROWS.length === 1 ? '' : 's')
      : matched.length + ' of ' + ROWS.length + ' rounds match';
    document.getElementById('prev').disabled = state.page === 0;
    document.getElementById('next').disabled = state.page >= pages - 1;
    // Says which rows these are, never a bare count that could be read as a count of everything.
    document.getElementById('pageinfo').textContent = matched.length === 0
      ? 'nothing to show'
      : 'rows ' + (from + 1) + '–' + (from + shown.length) + ' of ' + matched.length
        + (pages > 1 ? ' · page ' + (state.page + 1) + ' of ' + pages : '');
    document.getElementById('recorded').textContent = TOTALS.rounds === 0
      ? ''
      : 'The database has ' + TOTALS.rounds + ' rounds and ' + TOTALS.findings + ' findings — '
        + TOTALS.accepted + ' accepted, ' + TOTALS.rejected + ' rejected, ' + TOTALS.gating
        + ' gating. Counted by the database, not by this page.';
    var ths = document.querySelectorAll('th[data-sort]');
    for (var t = 0; t < ths.length; t++) {
      ths[t].className = ths[t].className.replace(/\\b(asc|desc)\\b/g, '').trim();
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className += ' ' + state.dir; }
      if (ths[t].getAttribute('data-sort') === state.sortKey) { ths[t].className = ths[t].className.trim(); }
    }
  }

  document.addEventListener('click', function (event) {
    var target = event.target;
    var tab = target.closest('[data-tab]');
    if (tab) {
      var which = tab.getAttribute('data-tab');
      var tabs = document.querySelectorAll('[data-tab]');
      for (var i = 0; i < tabs.length; i++) {
        tabs[i].className = tabs[i].getAttribute('data-tab') === which ? 'tab on' : 'tab';
      }
      document.getElementById('tab-rounds').hidden = which !== 'rounds';
      document.getElementById('tab-usage').hidden = which !== 'usage';
      document.getElementById('tab-spots').hidden = which !== 'spots';
      return;
    }
    var button = target.closest('[data-command]');
    if (button) {
      vscode.postMessage({ type: 'command', command: button.getAttribute('data-command'), id: button.getAttribute('data-id') });
      return;
    }
    var th = target.closest('th[data-sort]');
    if (th) {
      var key = th.getAttribute('data-sort');
      if (state.sortKey === key) { state.dir = state.dir === 'asc' ? 'desc' : 'asc'; }
      else { state.sortKey = key; state.dir = key === 'startedUtc' ? 'desc' : 'asc'; }
      firstPage();
      render();
      return;
    }
    var retry = target.closest('[data-retry]');
    if (retry) {
      ask(retry.getAttribute('data-retry'), true);
      return;
    }
    var tr = target.closest('tr[data-key]');
    if (tr) {
      var k = tr.getAttribute('data-key');
      if (state.expanded[k]) { delete state.expanded[k]; } else { state.expanded[k] = true; ask(k, false); }
      render();
    }
  });

  // Asked ONCE. A row keeps what it was told, so closing and reopening costs nothing — except after
  // a failure, which is not cached, because a retry that answers from the failure is not a retry.
  function ask(key, again) {
    for (var i = 0; i < ROWS.length; i++) {
      if (ROWS[i].key !== key) { continue; }
      var state_ = ROWS[i].foundState;
      // A loaded row with nothing in it and a count that says otherwise is retryable too — it is a
      // disagreement, not an answer. (CodeRabbit, on the pull request.)
      var mismatched = state_ === 'loaded' && ROWS[i].foundCount > 0
        && (!ROWS[i].found || ROWS[i].found.length === 0);
      if (state_ !== 'unasked' && !(again && (state_ === 'failed' || mismatched))) { return; }
      ROWS[i] = Object.assign({}, ROWS[i], { foundState: 'asking' });
      // The dbKey travels WITH the request. The extension host used to look the row up in a
      // module-level copy of the last rows it built, which is a shared mutable global three
      // reviewers objected to and which answers wrongly for any row a later refresh dropped.
      vscode.postMessage({
        type: 'command', command: 'findings', id: key,
        session: ROWS[i].dbKey.sessionId, stage: ROWS[i].dbKey.stage, number: ROWS[i].dbKey.number,
      });
      render();
      return;
    }
  }
  var selects = document.querySelectorAll('[data-filter]');
  for (var s = 0; s < selects.length; s++) {
    selects[s].addEventListener('change', function (event) {
      state.filters[event.target.getAttribute('data-filter')] = event.target.value;
      firstPage();
      render();
    });
  }
  document.getElementById('search').addEventListener('input', function (event) {
    state.search = event.target.value;
    firstPage();
    render();
  });
  document.getElementById('prev').addEventListener('click', function () {
    state.page = state.page - 1;
    render();
  });
  document.getElementById('next').addEventListener('click', function () {
    state.page = state.page + 1;
    render();
  });
  var fromInput = document.getElementById('from');
  var toInput = document.getElementById('to');
  function readDates() {
    state.filters.from = asInstant(fromInput.value, false);
    // The upper bound INCLUDES the minute it names: the picker has minute granularity, so "to 23:59"
    // that stopped at 23:59:00.000 dropped the last minute of the day the page opens on.
    state.filters.to = asInstant(toInput.value, true);
    firstPage();
    render();
  }
  // Today, from its first minute to its last — the range the page opens on, because the question
  // somebody has when they open it is almost always "what happened today".
  function setToday() {
    var now = new Date();
    fromInput.value = localDay(now) + 'T00:00';
    toInput.value = localDay(now) + 'T23:59';
    readDates();
  }
  fromInput.addEventListener('change', readDates);
  toInput.addEventListener('change', readDates);
  document.getElementById('today').addEventListener('click', setToday);
  document.getElementById('alldates').addEventListener('click', function () {
    fromInput.value = '';
    toInput.value = '';
    readDates();
  });
  document.getElementById('clear').addEventListener('click', function () {
    state.filters = {};
    state.search = '';
    document.getElementById('search').value = '';
    fromInput.value = '';
    toInput.value = '';
    for (var c = 0; c < selects.length; c++) { selects[c].value = ''; }
    render();
  });
  // Which sections have actually been told something. A section still reading "Reading the log…"
  // after STILL_NOTHING_MS is a section nothing ever reached, and it says so rather than promising
  // for ever that something is coming.
  var told = {};
  window.addEventListener('message', function (event) {
    var message = event.data;
    if (!message) { return; }
    if (message.type === 'spots' && typeof message.html === 'string') {
      told.spots = true;
      document.getElementById('spots-body').innerHTML = message.html;
      return;
    }
    if (message.type === 'usage' && typeof message.html === 'string') {
      told.usage = true;
      document.getElementById('usage-body').innerHTML = message.html;
      return;
    }
    if (message.type === 'totals' && message.totals) {
      // The page is painted before the database is read, so the line under the table opens on
      // nothing and is filled by this. It used to be stored on the panel and pushed nowhere, which
      // left the whole SQL-totals line permanently empty. (Code round, CodeRabbit.)
      TOTALS = message.totals;
      try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
      return;
    }
    if (message.type === 'found' && typeof message.id === 'string') {
      for (var f = 0; f < ROWS.length; f++) {
        if (ROWS[f].key === message.id) {
          ROWS[f] = Object.assign({}, ROWS[f], {
            found: message.findings || [], foundState: message.state || 'failed',
          });
        }
      }
      try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
      return;
    }
    if (message.type !== 'rows') { return; }
    // A tick rebuilds every row from the session files, and those rows know nothing about findings
    // somebody has already opened. Carry them across, or a five-second tick would close every
    // expanded row's list and ask for it again.
    // Only what a READ produced is carried: loaded because the sentences are here, asking
    // because a request is in flight, failed because the retry button must survive a tick. NOT
    // absent and not unasked — those are derived from the log, and a fresh row derived from a
    // fresher log is the better answer. Holding absent is how a first paint with no database
    // outlived every push that knew better. (Code round, CodeRabbit.)
    var held = {};
    for (var h = 0; h < ROWS.length; h++) {
      var was = ROWS[h].foundState;
      if (was === 'loaded' || was === 'asking' || was === 'failed') { held[ROWS[h].key] = ROWS[h]; }
    }
    ROWS = (message.rows || []).map(function (r) {
      var was = held[r.key];
      return was === undefined
        ? r
        : Object.assign({}, r, { found: was.found, foundState: was.foundState });
    });
    if (typeof message.questions === 'string') {
      document.getElementById('questions').innerHTML = message.questions;
    }
    try { render(); } catch (e) { failed(String(e && e.message ? e.message : e)); }
  });
  // The listener is attached, so from this moment a push can actually be RECEIVED. Until this word
  // the panel sends nothing: postMessage answers true for a webview that merely exists, and one
  // exists from the moment its html is assigned — a message sent in that window reaches nobody, and
  // that is what left this page without its decision counts and with an empty spots tab. Sent again
  // every time VS Code rebuilds the page, which is what makes the surface recoverable rather than
  // one-shot.
  vscode.postMessage({ type: 'ready' });
  // And the page's own deadline, longer than the panel's five-second fallback so that fallback has
  // its chance first. A loading state that never resolves is WORSE than the empty tab it replaced,
  // because it promises something is coming — and the only diagnostic the extension side can leave
  // is a console.warn in the extension host, which nobody opens. Four findings across both remote
  // vendors and three roles said so on the code round.
  setTimeout(function () {
    var sections = [['usage', 'usage-body'], ['spots', 'spots-body']];
    for (var w = 0; w < sections.length; w++) {
      // Only a section that OPENED on the placeholder, and only one nothing ever reached. The
      // spending tab is painted with real numbers on the first paint, and replacing those with an
      // error because no push happened to change them would be a lie.
      if (told[sections[w][0]] || !WAITING[sections[w][0]]) { continue; }
      document.getElementById(sections[w][1]).innerHTML = '<div class="empty">This section never'
        + ' received its data. Reload the window (Developer: Reload Window); if it comes back, copy'
        + ' this into an issue.</div>';
    }
  }, 15000);
  try {
    // Today by default. Everything older is one click away on "All dates"; the hint says so.
    setToday();
  } catch (e) {
    failed(String(e && e.message ? e.message : e));
  }
})();
</script>
</body>
</html>`;
}
