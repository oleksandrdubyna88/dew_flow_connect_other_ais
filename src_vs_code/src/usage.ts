import { PriceTier } from './modelPrices';
import { Vendor } from './vendors';
/**
 * What each AI has consumed, read from the server's append-only ledger.
 *
 * <p>Pure: the file read lives in the provider, the arithmetic and the shapes are here, so the
 * numbers on the chart are a test rather than something checked by squinting at a sidebar.</p>
 */

export interface UsageEntry {
  readonly utc: string;
  readonly provider: string;
  readonly model: string;
  readonly role: string;
  readonly stage: string;
  readonly seconds: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
  readonly outcome: string;
  /**
   * `no price set` when the server ran a METERED reviewer — an `api` row, billed per token — whose row
   * carried no rate (PLAN_feature_review.md S3.7); absent otherwise. It is what separates "unpriced" from
   * a CLI that never reports money, which a null cost alone cannot.
   */
  readonly costNote?: string;
  /**
   * `usage not captured` when the call ENDED before its vendor reported what it consumed — killed on its
   * deadline, cancelled with the round, or an `api` call whose connection dropped; absent otherwise. Its
   * `tokensIn`/`tokensOut` are then only what was counted before that, usually nothing — and the rest is
   * UNKNOWN, which a zero priced from a list would render as `$0`.
   */
  readonly usageNote?: string;
  /** How many of `tokensIn` the vendor served from its prompt cache — a SUBSET of the input, never added to it. */
  readonly tokensCached?: number;
  /** How many of `tokensOut` the vendor reported as reasoning — for the record; the bill is `tokensOut`. */
  readonly tokensReasoning?: number;
  /** What the line WAS — `review`, `consult`, `question`, `chat`, `probe`; a line older than the field is a review. */
  readonly kind?: string;
  /**
   * The id every launch of one reviewer turn shares (story 3.3 of todo/PLAN_every_round_is_counted.md) — a repair, a
   * follow-up, a retry and a rotated Team-server attempt each write a line of their own, and this ties them back to
   * their turn. Absent on a line older than the field, and on a kind with no turns.
   */
  readonly turn?: string;
  /** What the launch was inside its turn — `first`, `repair`, `followUp`, `retry`, `rotated`; absent like `turn`. */
  readonly attempt?: string;
  /**
   * What the launch RAN WITH (story 3.4 of todo/PLAN_every_round_is_counted.md): the runtime that ran it, the first line
   * of that CLI's `--version` where one was already asked, the effort and the tier each with its source (`row`, `server`,
   * `ignored`, `dropped`, `cli-config` — `OptionSources` in `src_mcp/runners/Reviewers/LaunchOptions.cs`), and on a Team
   * server the job and the account slot by its name. Each is absent on a line older than the field, and where the line
   * says nothing — an empty value is "not recorded", never a value.
   */
  readonly cli?: string;
  readonly cliVersion?: string;
  readonly effort?: string;
  readonly effortFrom?: string;
  readonly tier?: string;
  readonly tierFrom?: string;
  readonly job?: string;
  readonly slot?: string;
  /** `ignored` / `dropped` when the asked-for effort did not reach the CLI — apart from `effort`, which says what ran. */
  readonly effortNotSent?: string;
  readonly tierNotSent?: string;
  /**
   * WHY a cancelled Team-server launch was cancelled (story 3.2 of todo/PLAN_every_round_is_counted.md) — `withdrawn`,
   * `abandoned`, `backstop`, `queueExpired`, `hostStopped` (`EndCauses` in `src_mcp/runners/Reviewers/EndCauses.cs`).
   * Absent on a line older than the field, on every other ending, and on a local line, which does not know which
   * token fired. The outcome stays `Cancelled`; this is the column that tells a withdrawal from the server's backstop.
   */
  readonly endCause?: string;
  /**
   * The id of the ROUND the launch belonged to (story 4.2 of todo/PLAN_every_round_is_counted.md) — 32 hex characters the
   * client mints per round, written by the Team server on every line of a job whose request carried one. Absent on a line
   * older than the field, on one whose request sent none, and where the server dropped a malformed id.
   */
  readonly round?: string;
  /**
   * What the CLIENT called the request — `first`, `retry`, `repair`, `followUp` — beside the server's own `attempt`
   * (`first`, `rotated`, `followUp` inside the job), never over it. Absent like `round`, and on a local line, whose
   * `attempt` already is the client's word.
   */
  readonly clientAttempt?: string;
  /**
   * The instant the vendor STATED its quota comes back, in UTC (story 5.3b of todo/PLAN_every_round_is_counted.md) —
   * written by `coai-mcp` on the last line of a turn that ended rate-limited, read from the refusal by the one parser both
   * halves use (`CooldownParser.StatedReset`). Absent on every other line, on a refusal that stated no time, and on a
   * line older than the field; `statedReset.ts` turns it into the words a person reads.
   */
  readonly resetsUtc?: string;
  /**
   * The vendor's own gauge as read on the Team server's account right after this launch (story 6.1a of
   * todo/PLAN_every_round_is_counted.md): when, the plan word, and EVERY window of EVERY limit it reported (epic 6's code
   * round — the first shape kept one limit's two windows and lost the rest). Each field is present only when the line
   * says it: absent on a line older than the fields, on a local line, and on a job after which nothing was read; a window
   * the vendor did not report, or one that is not whole, is not in the list — never a 0 %.
   */
  readonly gaugeObservedUtc?: string;
  readonly gaugePlan?: string;
  readonly gaugeWindows?: readonly GaugeWindowOnLine[];
}

export type Window = 'day' | 'week' | 'month' | 'year';

export const WINDOWS: readonly { readonly id: Window; readonly label: string; readonly days: number }[] = [
  { id: 'day', label: 'Today', days: 1 },
  { id: 'week', label: 'Week', days: 7 },
  { id: 'month', label: 'Month', days: 30 },
  { id: 'year', label: 'Year', days: 365 },
];

export interface VendorTotals {
  readonly provider: string;
  readonly runs: number;
  readonly failed: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  /** Only from vendors that price their own runs; null when nobody in this row reported money. */
  readonly costUsd: number | null;
  /**
   * What this row cost according to the RATES the person entered, when the vendor reported none.
   *
   * <p>Null when there is no rate, and null when the vendor reported a real cost — an estimate
   * beside a bill is noise, and the bill is the fact. Marked with a tilde wherever it is shown,
   * because a number derived from tokens is not the same kind of thing as a number a vendor
   * charged.</p>
   */
  readonly estimatedUsd: number | null;
  /**
   * The money is AT LEAST what is shown: a line no rate prices, or a line whose usage was not captured, adds nothing
   * to it and is not free. Rendered `≥`.
   */
  readonly floor: boolean;
  readonly seconds: number;
  readonly averageSeconds: number;
}

/** One ledger line; a torn or foreign line is skipped rather than breaking the chart. */
export function parseUsageLine(line: string): UsageEntry | undefined {
  try {
    const parsed = JSON.parse(line) as Partial<UsageEntry>;
    if (typeof parsed.utc !== 'string' || typeof parsed.provider !== 'string') {
      return undefined;
    }
    // Every metric is coerced, because ONE missing number turns a sum into NaN and the whole
    // chart reads "NaN tokens" — a line written by an older server, or a torn one, must cost its
    // own row and nothing else. Caught by a reviewer on the commit that added this file.
    return {
      utc: parsed.utc,
      provider: parsed.provider,
      model: typeof parsed.model === 'string' ? parsed.model : '',
      role: typeof parsed.role === 'string' ? parsed.role : '',
      stage: typeof parsed.stage === 'string' ? parsed.stage : '',
      seconds: number(parsed.seconds),
      tokensIn: number(parsed.tokensIn),
      tokensOut: number(parsed.tokensOut),
      costUsd: typeof parsed.costUsd === 'number' && Number.isFinite(parsed.costUsd) ? parsed.costUsd : null,
      outcome: typeof parsed.outcome === 'string' ? parsed.outcome : 'ok',
      ...(typeof parsed.costNote === 'string' && parsed.costNote.length > 0 ? { costNote: parsed.costNote } : {}),
      ...(typeof parsed.usageNote === 'string' && parsed.usageNote.length > 0 ? { usageNote: parsed.usageNote } : {}),
      ...ledgerExtras(parsed),
      ...launchFields(parsed),
      ...ranWithFields(parsed),
      ...endCauseOf(parsed.endCause),
      ...roundFields(parsed),
      ...resetOf(parsed.resetsUtc),
      ...gaugeFields(parsed),
    };
  } catch {
    return undefined;
  }
}

/** One window of one limit, as a Team-server ledger line carries it (epic 6's code round). */
export interface GaugeWindowOnLine {
  readonly limitId: string;
  readonly kind: string;
  readonly mins: number;
  readonly usedPercent: number;
  readonly resetsUtc: string;
}

/** The gauge's text fields, each present only when the line says it. */
const GAUGE_TEXT_FIELDS = ['gaugeObservedUtc', 'gaugePlan'] as const;

type GaugeTextField = (typeof GAUGE_TEXT_FIELDS)[number];

/**
 * The vendor's gauge as read after a Team-server launch (story 6.1a of todo/PLAN_every_round_is_counted.md), by
 * `launchFields`' rule: a text field is present only when non-empty, and the windows only when the line carries some.
 */
function gaugeFields(parsed: Partial<UsageEntry>): Pick<UsageEntry, GaugeTextField | 'gaugeWindows'> {
  return { ...gaugeTexts(parsed), ...gaugeWindowsOf(parsed.gaugeWindows) };
}

/** The line's windows, each one WHOLE — a window missing a number is dropped, never coerced to 0 as `number()` would. */
function gaugeWindowsOf(raw: unknown): Pick<UsageEntry, 'gaugeWindows'> {
  const windows = Array.isArray(raw) ? raw.flatMap(gaugeWindowOf) : [];

  return windows.length > 0 ? { gaugeWindows: windows } : {};
}

function gaugeWindowOf(raw: unknown): readonly GaugeWindowOnLine[] {
  const row = recordOf(raw);

  return WINDOW_CHECKS.every((check) => check(row)) ? [{
    limitId: String(row['limitId']), kind: String(row['kind']), mins: Number(row['mins']), usedPercent: Number(row['usedPercent']),
    resetsUtc: textOrEmpty(row['resetsUtc']),
  }] : [];
}

function recordOf(raw: unknown): Record<string, unknown> {
  return typeof raw === 'object' && raw !== null ? raw as Record<string, unknown> : {};
}

function textOrEmpty(value: unknown): string {
  return saidText(value) ? value : '';
}

/** What a window must be to be read: named, with a length and a use that are finite numbers. */
const WINDOW_CHECKS: readonly ((row: Record<string, unknown>) => boolean)[] = [
  (row) => typeof row['limitId'] === 'string' && typeof row['kind'] === 'string',
  (row) => finite(row['mins']),
  (row) => finite(row['usedPercent']),
];

function finite(value: unknown): boolean {
  return typeof value === 'number' && Number.isFinite(value);
}

function gaugeTexts(parsed: Partial<UsageEntry>): Partial<Record<GaugeTextField, string>> {
  const said: Partial<Record<GaugeTextField, string>> = {};
  for (const name of GAUGE_TEXT_FIELDS) {
    const value: unknown = parsed[name];
    if (saidText(value)) {
      said[name] = value;
    }
  }
  return said;
}


/**
 * The three fields the panel did not read until story 2.5 of todo/PLAN_every_round_is_counted.md, though `coai-mcp` has
 * written them for months: without the cached subset every input token was priced at the full rate, and without the
 * kind a consultation could not be told from a review. An old line has none of them: no cache count is zero — the
 * vendor did not say — and no kind is a review, because a review was all a line could be before the field existed
 * (`UsageKinds` in `src_mcp/runners/Reviewers/UsageLedger.cs` reads it the same way).
 */
function ledgerExtras(parsed: Partial<UsageEntry>): Required<Pick<UsageEntry, 'tokensCached' | 'tokensReasoning' | 'kind'>> {
  return {
    tokensCached: number(parsed.tokensCached),
    tokensReasoning: number(parsed.tokensReasoning),
    kind: typeof parsed.kind === 'string' && parsed.kind.length > 0 ? parsed.kind : 'review',
  };
}

/**
 * The two fields story 3.3 of todo/PLAN_every_round_is_counted.md added — the turn a launch shares and what the launch
 * was — present only when the line says them. An older line has neither, and an empty one is absent rather than an id:
 * every old line with an empty turn would otherwise read as one turn.
 */
function launchFields(parsed: Partial<UsageEntry>): Pick<UsageEntry, 'turn' | 'attempt'> {
  return { ...turnOf(parsed.turn), ...attemptOf(parsed.attempt) };
}

/** A text field the line actually says — a non-empty string; anything else is silence. */
function saidText(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function turnOf(value: unknown): Pick<UsageEntry, 'turn'> {
  return saidText(value) ? { turn: value } : {};
}

function attemptOf(value: unknown): Pick<UsageEntry, 'attempt'> {
  return saidText(value) ? { attempt: value } : {};
}

/**
 * Why a cancelled Team-server launch was cancelled (story 3.2 of todo/PLAN_every_round_is_counted.md) — present only
 * when the line says it, by `launchFields`' rule: an older line, every other ending and a local line say nothing, and an
 * empty value is absent rather than a cause.
 */
function endCauseOf(value: unknown): Pick<UsageEntry, 'endCause'> {
  return saidText(value) ? { endCause: value } : {};
}

/**
 * The reset a refusal stated (story 5.3b of todo/PLAN_every_round_is_counted.md) — present only when the line says it,
 * by `launchFields`' rule: an empty value is absent rather than an instant.
 */
function resetOf(value: unknown): Pick<UsageEntry, 'resetsUtc'> {
  return saidText(value) ? { resetsUtc: value } : {};
}

/**
 * The round a Team-server launch belonged to and the client's word for its request (story 4.2 of
 * todo/PLAN_every_round_is_counted.md) — each present only when the line says it, by `launchFields`' rule: an empty round
 * is absent rather than an id every old client's line would share.
 */
function roundFields(parsed: Partial<UsageEntry>): Pick<UsageEntry, 'round' | 'clientAttempt'> {
  return {
    ...(saidText(parsed.round) ? { round: parsed.round } : {}),
    ...(saidText(parsed.clientAttempt) ? { clientAttempt: parsed.clientAttempt } : {}),
  };
}

/**
 * The text fields story 3.4 of todo/PLAN_every_round_is_counted.md added — what the launch ran with — and the two
 * not-sent facts the consultation of 2026-10-11 split off them.
 */
const RAN_WITH_FIELDS = [
  'cli', 'cliVersion', 'effort', 'effortFrom', 'tier', 'tierFrom', 'job', 'slot', 'effortNotSent', 'tierNotSent',
] as const;

type RanWithField = (typeof RAN_WITH_FIELDS)[number];

/**
 * What the launch ran with, each field present only when the line says it — the rule `launchFields` keeps: an older line
 * has none, and an empty value ("not recorded") is absent rather than a value nothing chose.
 */
function ranWithFields(parsed: Partial<UsageEntry>): Pick<UsageEntry, RanWithField> {
  const said: Partial<Record<RanWithField, string>> = {};
  for (const name of RAN_WITH_FIELDS) {
    const value: unknown = parsed[name];
    if (saidText(value)) {
      said[name] = value;
    }
  }
  return said;
}

function number(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

export function parseUsage(text: string): UsageEntry[] {
  return text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
    .map(parseUsageLine)
    .filter((e): e is UsageEntry => e !== undefined);
}

/**
 * The entries inside a window.
 *
 * <p>Generic over anything with a `utc`, because a second ledger wanted exactly this rule: what
 * "this week" means must be one answer for both halves of the spending page, not two that drift.</p>
 *
 * <p><b>"Today" is since local midnight.</b> It was the last 24 hours — "a review at 23:50 and one
 * at 00:10 belong to the same piece of work" — and the operator overruled it on 2026-09-05: what
 * today cost is a question about the calendar day, and a rolling day answers a different one. Week,
 * month and year stay rolling: a quiet Monday morning must still show last week's work.</p>
 */
export function windowStart(window: Window, now: Date): number {
  return startOfPeriodMs(window, now.getTime(), DAYS_OF);
}

/** How many days each window spans, keyed by its id — `WINDOWS`, as data a page can be handed. */
export const DAYS_OF: Readonly<Record<string, number>> = Object.fromEntries(WINDOWS.map((w) => [w.id, w.days]));

/**
 * THE rule for where a window starts — the spending tab's, and the Review rounds page's period switch.
 *
 * <p>Self-contained on purpose, with no import reaching inside it: the Review rounds page runs THIS
 * function, handed its source the way `asInstant` is, so the page and the host cannot drift into two
 * answers for "this week". A second hand-written copy in the page was the code round's finding
 * (gemini and local, 2026-09-25). `all` has no start; an unknown id spans one day, as it always did.</p>
 */
export function startOfPeriodMs(period: string, nowMs: number, days: Readonly<Record<string, number>>): number {
  if (period === 'all') {
    return Number.NEGATIVE_INFINITY;
  }
  const now = new Date(nowMs);

  return period === 'day'
    ? new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
    : nowMs - (days[period] ?? 1) * 24 * 60 * 60 * 1000;
}

export function within<T extends { readonly utc: string }>(
  entries: readonly T[],
  window: Window,
  now: Date,
): T[] {
  const cutoff = windowStart(window, now);
  return entries.filter((e) => {
    const at = Date.parse(e.utc);
    return Number.isFinite(at) && at >= cutoff;
  });
}

/** Per vendor, biggest spender first — the row order a person actually scans for. */
export function totalsByVendor(
  entries: readonly UsageEntry[],
  vendors: readonly Vendor[] = [],
  listed: PriceLookup = () => undefined,
): VendorTotals[] {
  const byProvider = new Map<string, UsageEntry[]>();
  for (const e of entries) {
    byProvider.set(e.provider, [...(byProvider.get(e.provider) ?? []), e]);
  }

  const rateOf = (line: UsageEntry): LineRate | undefined => priceOfLine(line.provider, line.model, vendors, listed);

  return [...byProvider.entries()]
    .map(([provider, rows]) => vendorTotals(provider, rows, rateOf))
    .sort((a, b) => b.tokensIn + b.tokensOut - (a.tokensIn + a.tokensOut));
}

/**
 * One vendor row's totals, its money priced LINE BY LINE (story 2.5 of todo/PLAN_every_round_is_counted.md).
 *
 * <p>It priced the row's whole history at the row's CURRENT model, all input at the full rate and never at a
 * long-context tier: after the Gemini row moved to `flash-medium` on 2026-10-02, every earlier `flash-low` line was
 * priced as `flash-medium`. Each line now goes through {@link lineCost} at its own model's rate, and the halves stay
 * apart — what vendors billed in `costUsd`, what the lists work out in `estimatedUsd` — because a sum of the two is a
 * number nobody can check. A line that could not be priced makes the row a floor.</p>
 */
function vendorTotals(provider: string, rows: readonly UsageEntry[], rateOf: (line: UsageEntry) => LineRate | undefined): VendorTotals {
  const costs = rows.map((row) => lineCost(row, rateOf(row)));
  const billed = costs.filter((one) => one.known && !one.estimated);
  const guessed = costs.filter((one) => one.estimated);
  const seconds = sum(rows.map((r) => r.seconds));

  return {
    provider,
    runs: rows.length,
    failed: rows.filter((r) => r.outcome !== 'ok').length,
    tokensIn: sum(rows.map((r) => r.tokensIn)),
    tokensOut: sum(rows.map((r) => r.tokensOut)),
    costUsd: billed.length === 0 ? null : sum(billed.map((one) => one.usd)),
    estimatedUsd: guessed.length === 0 ? null : round4(sum(guessed.map((one) => one.usd))),
    floor: costs.some((one) => one.floor),
    seconds,
    averageSeconds: rows.length === 0 ? 0 : seconds / rows.length,
  };
}

/** One figure, and what it is: known at all, a floor, and whether any of it is an estimate rather than a bill. */
export interface Estimate {
  readonly usd: number;
  readonly known: boolean;
  readonly floor: boolean;
  readonly estimated: boolean;
}

/** Nothing priced it: a dash, and — when added to something that is priced — a floor. */
export const UNKNOWN: Estimate = { usd: 0, known: false, floor: true, estimated: false };

/** Nothing was spent and nothing priced it: neither a figure nor a floor — a line that consumed no tokens. */
const NOTHING: Estimate = { usd: 0, known: false, floor: false, estimated: false };

/** Several figures added up: known when any part is, a floor when any part is unknown or a floor itself. */
export function totalCost(parts: readonly Estimate[]): Estimate {
  if (parts.length === 0) {
    return UNKNOWN;
  }

  return {
    usd: parts.reduce((total, part) => total + part.usd, 0),
    known: parts.some((part) => part.known),
    floor: parts.some((part) => part.floor),
    estimated: parts.some((part) => part.estimated),
  };
}

/** `~$3.00` for an estimate, `$0.4200` for a bill, `≥ ` before a floor, and a dash for nothing known. */
export function costText(estimate: Estimate): string {
  if (!estimate.known) {
    return '—';
  }

  return `${estimate.floor ? '≥ ' : ''}${estimate.estimated ? '~' : ''}${money(estimate.usd)}`;
}

/** The ledger fields one line is priced from — a `UsageEntry`, or a chat turn read into the same shape. */
export type PricedLine = Pick<UsageEntry, 'tokensIn' | 'tokensOut' | 'costUsd' | 'usageNote' | 'tokensCached'>;

/**
 * What one ledger line cost: what its vendor BILLED, else its own model's rate applied to its own tokens, else unknown.
 *
 * <p>A reported cost is never re-estimated — it is the fact. A line whose usage was not captured adds nothing and is a
 * floor: its counted tokens are whatever arrived before it ended, usually nothing, and the rest is unknown rather than
 * free. A line no rate prices is a floor too — unless it consumed nothing, which is neither a figure nor a floor.</p>
 */
export function lineCost(line: PricedLine, rate: LineRate | undefined): Estimate {
  if (typeof line.costUsd === 'number') {
    return { usd: line.costUsd, known: true, floor: uncaptured(line), estimated: false };
  }
  if (uncaptured(line)) {
    return UNKNOWN;
  }

  return rate === undefined ? unpriced(line) : { usd: estimateUsd(line, rate), known: true, floor: false, estimated: true };
}

/** Whether the line's launch ended before its vendor reported what it consumed (`usage not captured`). */
export function uncaptured(line: Pick<UsageEntry, 'usageNote'>): boolean {
  return line.usageNote !== undefined;
}

function unpriced(line: PricedLine): Estimate {
  return line.tokensIn + line.tokensOut > 0 ? UNKNOWN : NOTHING;
}

/**
 * `(in − cached)·in + cached·cached + out·out`, per million, at the long-context tier's rates once THIS line's input
 * reaches it — `TokenPrice.CostOf` in `src_mcp/core/Findings/TokenPrice.cs`, the same arithmetic on both halves.
 *
 * <p>The cached count is a SUBSET of the input (codex's `cached_input_tokens`, measured in `UsageAccountingTests`; the
 * parser files claude's cache reads inside the input too), so it is taken out of the fresh input and clamped to it. A
 * rate that states no cached price prices cached input at the input rate: the list did not say, and nothing is not
 * what it said. The tier is per REQUEST — one line is one request — so it is never applied to a sum.</p>
 */
function estimateUsd(line: PricedLine, rate: LineRate): number {
  const rates = ratesAt(rate, line.tokensIn);
  const cached = Math.min(Math.max(line.tokensCached ?? 0, 0), Math.max(line.tokensIn, 0));
  const fresh = Math.max(0, line.tokensIn - cached);
  const cachedRate = rates.cached > 0 ? rates.cached : rates.in;

  return (fresh * rates.in + cached * cachedRate + line.tokensOut * rates.out) / 1_000_000;
}

/** The rates one request is billed at: the tier's from its threshold on, else the base ones. Cached 0 = not stated. */
function ratesAt(rate: LineRate, tokensIn: number): { readonly in: number; readonly out: number; readonly cached: number } {
  const tier = rate.tier;
  if (tier !== undefined && tokensIn >= tier.fromTokens) {
    return { in: tier.inPerMillion, out: tier.outPerMillion, cached: tier.cachedPerMillion };
  }

  return { in: rate.in, out: rate.out, cached: rate.cached ?? 0 };
}

function sum(values: readonly number[]): number {
  return values.reduce((total, v) => total + v, 0);
}

/** 12 345 → "12.3k"; a chart row has no space for six digits and nobody counts them. */
export function shortNumber(value: number): string {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`;
  }
  if (value >= 1_000) {
    return `${(value / 1_000).toFixed(1)}k`;
  }
  return String(Math.round(value));
}

export function shortDuration(seconds: number): string {
  if (seconds >= 3600) {
    return `${(seconds / 3600).toFixed(1)} h`;
  }
  if (seconds >= 60) {
    return `${(seconds / 60).toFixed(1)} min`;
  }
  return `${Math.round(seconds)} s`;
}

/**
 * Money, or an honest dash.
 *
 * <p>A vendor that does not price its own runs contributes tokens and no money, and that MUST NOT
 * render as `$0.00`: free and unreported are different facts, and only one of them is good news.</p>
 */
export function money(costUsd: number | null): string {
  if (costUsd === null) {
    return '—';
  }

  // Cents are not enough. A single round is fractions of a dollar, and `toFixed(2)` turns $0.0004
  // into "$0.00", which reads as free. There were TWO of this function — this one rounding to cents
  // and the one in rounds.ts using four decimals, whose comment warned about exactly the bug this
  // one had. One concept, one implementation.
  return costUsd >= 1 ? `$${costUsd.toFixed(2)}` : `$${costUsd.toFixed(4)}`;
}

/** The bar width for one row, as a percentage of the busiest row. */
export function barWidth(value: number, max: number): number {
  return max <= 0 ? 0 : Math.max(2, Math.round((value / max) * 100));
}

/**
 * This vendor's rates, or nothing when it has none.
 *
 * <p>Found by ID, because that is what the ledger records and what the panel row is named by — the
 * model can change between runs and the rate follows the vendor, not the model.</p>
 */
export function priceOf(
  provider: string,
  vendors: readonly Vendor[],
  listed: PriceLookup = () => undefined,
): LineRate | undefined {
  const vendor = vendors.find((v) => v.id === provider);

  return vendor === undefined ? undefined : rateFrom(vendor, listed(vendor.model));
}

/**
 * Dollars per million tokens for one line: input, output, the CACHED input rate when one is stated, and a long-context
 * tier. `cached` and `tier` are present only when known — absent means "not said", which is never zero.
 */
export interface LineRate {
  readonly in: number;
  readonly out: number;
  readonly cached?: number;
  readonly tier?: PriceTier;
}

/**
 * A row's typed rate over a list price, per field — or the list price alone.
 *
 * <p>A TYPED rate is a fact about this account — a flat subscription, a negotiated rate, a free tier — and a published
 * list price is a general estimate. The specific statement wins over the general one, per field: somebody who filled
 * in only the input rate keeps it, and the output rate falls back rather than the whole vendor going dark. A typed
 * row takes no tier and no list cached rate: those describe the list's own prices, not the ones somebody typed.</p>
 */
function rateFrom(vendor: Vendor | undefined, published: ListedRate | undefined): LineRate | undefined {
  return vendor !== undefined && typedAtAll(vendor) ? typedOver(vendor, published) : listRate(published);
}

function typedAtAll(vendor: Vendor): boolean {
  return vendor.pricePerMillionIn > 0 || vendor.pricePerMillionOut > 0;
}

function typedOver(vendor: Vendor, published: ListedRate | undefined): LineRate {
  return {
    in: typedOr(vendor.pricePerMillionIn, published?.inPerMillion),
    out: typedOr(vendor.pricePerMillionOut, published?.outPerMillion),
    ...cachedPart(vendor.pricePerMillionCached),
  };
}

function typedOr(typed: number, listed: number | undefined): number {
  return typed > 0 ? typed : (listed ?? 0);
}

/** The cached rate, only when one is stated: absent means "not said", which is not zero. */
function cachedPart(rate: number | undefined): { readonly cached?: number } {
  return rate !== undefined && rate > 0 ? { cached: rate } : {};
}

function listRate(published: ListedRate | undefined): LineRate | undefined {
  if (published === undefined) {
    return undefined;
  }

  return {
    in: published.inPerMillion,
    out: published.outPerMillion,
    ...cachedPart(published.cachedPerMillion),
    ...(published.tier === undefined ? {} : { tier: published.tier }),
  };
}

/**
 * What one recorded line costs per million tokens: its vendor ROW's rate, or the list price of the
 * model that actually answered it.
 *
 * <p>A ledger line is permanent and a vendor row is not — its id is a person's own text, editable and
 * deletable in the panel. Pricing only by the row meant that renaming a reviewer erased the money
 * from every round and every conversation it had ever answered: the tokens stayed and the cost became
 * a dash, retroactively, for work that really had been paid for. The MODEL is recorded on the line
 * and cannot be edited afterwards, so it is what answers when the row is gone. (codex, the second
 * code round.)</p>
 *
 * <p>A row's TYPED rate still wins, and that order is deliberate: a typed rate is a fact about THIS
 * account — a flat subscription, a negotiated rate, a local engine no list has heard of — and a
 * published list price is a general estimate about a model.</p>
 *
 * <p><b>The list price is the LINE's model, never the row's current one</b> (story 2.5 of
 * todo/PLAN_every_round_is_counted.md). It used to fall back to the row's current model whenever the
 * row still existed, so a row that changed model re-priced its whole history at the new one.</p>
 */
export function priceOfLine(
  provider: string,
  model: string,
  vendors: readonly Vendor[],
  listed: PriceLookup = () => undefined,
): LineRate | undefined {
  return rateFrom(vendors.find((v) => v.id === provider), listed(model));
}

/** A list's rates for one model — `ModelPrice`, or anything with its shape; the cached rate and tier when it states them. */
export interface ListedRate {
  readonly inPerMillion: number;
  readonly outPerMillion: number;
  readonly cachedPerMillion?: number;
  readonly tier?: PriceTier;
}

/**
 * What a model costs per million tokens according to a public list, or nothing.
 *
 * <p>A function rather than a table so this file stays free of the fetching: the panel holds the
 * lists, `usage.ts` holds the arithmetic, and a test can answer for one model without a network.</p>
 */
export type PriceLookup = (modelId: string) => ListedRate | undefined;

/**
 * What a consultation's Cost column says: the money, a marked estimate, or a dash.
 *
 * <p>The reported symptom of issue #309 was 245.7k tokens beside a dash. Nothing was broken on the
 * way there: the vendor was codex, which prints no price, and both halves behaved as designed —
 * `UsageParser` refuses to ship a price table, and the server returns null rather than a zero that
 * would read as "this was free". The column simply never asked the question the panel already
 * answers for every reviewer round and every conversation in the ledger.</p>
 *
 * <p><b>The three states stay three.</b> Real money is never marked as an estimate; an estimate
 * always wears its tilde; and no rate, or no tokens, is a dash rather than a zero — because a zero
 * is a measurement and this is the absence of one.</p>
 */
export function consultationCost(
  costUsd: number | null,
  tokensIn: number,
  tokensOut: number,
  rate: { readonly in: number; readonly out: number } | undefined,
): { readonly text: string; readonly estimated: boolean } {
  if (costUsd !== null) {
    return { text: money(costUsd), estimated: false };
  }
  if (rate === undefined || tokensIn + tokensOut <= 0) {
    return { text: money(null), estimated: false };
  }

  return {
    text: estimated(round4((tokensIn * rate.in + tokensOut * rate.out) / 1_000_000)),
    estimated: true,
  };
}

/** An estimate, marked as one. The tilde is the whole point: this is not what anybody billed. */
export function estimated(usd: number): string {
  return `~${money(usd)}`;
}

/** Four decimals, so a fraction of a cent does not become zero on the way through. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
}
