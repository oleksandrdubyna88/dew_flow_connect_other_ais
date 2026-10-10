import { ChatTurnRecord } from './chatUsage';
import { chatRowProvider, ChatVendorOf } from './chatSpendRows';
import { costText, Estimate, LineRate, lineCost, shortNumber, totalCost, uncaptured, UNKNOWN, UsageEntry } from './usage';

/**
 * Tokens and money beside every count (story 2.5 of todo/PLAN_every_round_is_counted.md).
 *
 * <p>Per vendor row × model × kind — review, consult, question, chat — inside one window: tokens in / cached / out /
 * reasoning, the recorded turns and the failed ones, the median input and output per RECORDED TURN, and the money. And
 * the division the Rounds block (story 2.3) shows beside them: ~$ per round, per consultation, per question, given the
 * counts `coai-mcp --round-counts` answers (story 2.1) — this module is handed them and never reads `coai.db` itself.</p>
 *
 * <p><b>Read from the ledger the panel already holds</b> (`usage.jsonl` through `parseUsage`, the chat ledger through
 * {@link chatLedgerLines}); no second scan of either file. Pure and `vscode`-free: the price lookup is handed in, as
 * `priceOfLine` over the panel's rows and lists, so a row's typed rate wins for the row's own lines.</p>
 *
 * <p><b>Absent is not zero</b>, everywhere here:</p>
 * <ul>
 *   <li>a line whose usage was not captured adds NOTHING to a token total and marks it a floor (`≥ 1.2M`), and a total
 *       made only of such lines is unknown — never 0;</li>
 *   <li>money follows `lineCost`: a reported cost is the fact, otherwise a list-price estimate of the line at its own
 *       model, and a line nothing prices, or whose usage was not captured, makes its total a floor;</li>
 *   <li>a per-round figure divides by EVERY round it covers, carries the total's floor, is a dash when nothing was
 *       priced, and over zero rounds says *no rounds in this window* rather than dividing by nothing.</li>
 * </ul>
 *
 * <p><b>A ledger line is a recorded TURN, not a launch — for the lines written before story 3.3.</b> Those hold a repair
 * or a follow-up as two launches summed on one line; lines written since are one per launch, linked by their `turn`
 * (`usage.ts` reads it). A seven-day window can hold both, so a per-launch figure is still not claimed — the medians say
 * *per recorded turn*.</p>
 */

/** One ledger line as this module reads it — a parsed `usage.jsonl` line, or a chat turn read into the same shape. */
export type LedgerLine = Pick<UsageEntry, 'utc' | 'provider' | 'model' | 'tokensIn' | 'tokensOut' | 'costUsd' | 'outcome'
  | 'usageNote' | 'tokensCached' | 'tokensReasoning' | 'kind'>;

/** A trailing window, both ends inclusive, in epoch milliseconds — the caller picks 5 h, 24 h or 7 d from one clock. */
export interface TimeWindow {
  readonly fromMs: number;
  readonly toMs: number;
}

/** What one line costs per million tokens, by the vendor row that recorded it and the model that answered it. */
export type RateOf = (provider: string, model: string) => LineRate | undefined;

/** The four token columns of a set of lines, and whether they are the whole of it. */
export interface TokenTotals {
  readonly in: number;
  readonly cached: number;
  readonly out: number;
  readonly reasoning: number;
  /** False when every line was uncaptured: nothing is known, and the totals must not be shown as 0. */
  readonly known: boolean;
  /** True when any line was uncaptured: the totals are at least what is shown. */
  readonly floor: boolean;
  /** How many lines ended before their vendor reported what they consumed. */
  readonly uncaptured: number;
}

/** What a set of lines adds up to — a model's line, a vendor row, or the whole window. */
export interface Rollup {
  readonly turns: number;
  readonly failed: number;
  readonly tokens: TokenTotals;
  readonly money: Estimate;
}

/** One vendor row × model × kind inside the window. */
export interface MoneyGroup extends Rollup {
  readonly provider: string;
  readonly model: string;
  readonly kind: string;
  /** The median input per recorded turn over the captured lines, or null when none was captured. */
  readonly medianIn: number | null;
  /** The median output per recorded turn over the captured lines, or null when none was captured. */
  readonly medianOut: number | null;
}

/** What a figure is divided by, and what one of it is called. */
export interface Divisor {
  readonly per: string;
  readonly count: number;
}

/** A divided figure: its text (`≥ ~$0.31`, or a dash), what it is per, and the sentence its tooltip carries. */
export interface PerUnit {
  readonly text: string;
  readonly per: string;
  readonly note: string;
}

/** The unit of a figure that could not be divided by what the kind counts: one ledger line. */
export const PER_RECORDED_TURN = 'recorded turn';

/** The medians' label — a recorded turn, never a launch: one line can hold a repair or a follow-up (story 3.3). */
export const MEDIAN_OUTPUT_LABEL = `median output per ${PER_RECORDED_TURN}`;

/** The order a report reads best in — `UsageKinds.Known` in `src_mcp`, with anything newer after them. */
const KIND_ORDER: readonly string[] = ['review', 'consult', 'question', 'chat', 'probe'];

/** What one of each kind is called, for the denominator a caller supplies; a chat turn and a probe completion ARE their line. */
const UNIT_OF: Readonly<Record<string, string>> = {
  review: 'round', consult: 'consultation', question: 'question', chat: PER_RECORDED_TURN, probe: PER_RECORDED_TURN,
};

/** The lines inside the window, grouped per vendor row × model × kind, rows and models in name order. */
export function moneyGroups(lines: readonly LedgerLine[], window: TimeWindow, rateOf: RateOf): MoneyGroup[] {
  const groups = new Map<string, LedgerLine[]>();
  for (const one of lines.filter((candidate) => inside(candidate, window))) {
    const key = groupKey(one);
    groups.set(key, [...(groups.get(key) ?? []), one]);
  }

  return [...groups.values()].map((members) => group(members, rateOf)).sort(byRowModelKind);
}

function inside(line: LedgerLine, window: TimeWindow): boolean {
  const at = Date.parse(line.utc);

  return Number.isFinite(at) && at >= window.fromMs && at <= window.toMs;
}

function kindOf(line: Pick<LedgerLine, 'kind'>): string {
  return line.kind ?? 'review';
}

function groupKey(line: LedgerLine): string {
  return JSON.stringify([line.provider, line.model, kindOf(line)]);
}

function group(members: readonly LedgerLine[], rateOf: RateOf): MoneyGroup {
  const [first] = members as readonly [LedgerLine, ...LedgerLine[]];
  const captured = members.filter((one) => !uncaptured(one));

  return {
    provider: first.provider,
    model: first.model,
    kind: kindOf(first),
    turns: members.length,
    failed: members.filter((one) => one.outcome !== 'ok').length,
    tokens: tokenTotals(members),
    medianIn: median(captured.map((one) => one.tokensIn)),
    medianOut: median(captured.map((one) => one.tokensOut)),
    money: totalCost(members.map((one) => lineCost(one, rateOf(one.provider, one.model)))),
  };
}

function byRowModelKind(a: MoneyGroup, b: MoneyGroup): number {
  return a.provider.localeCompare(b.provider) || a.model.localeCompare(b.model) || kindRank(a.kind) - kindRank(b.kind);
}

function kindRank(kind: string): number {
  const at = KIND_ORDER.indexOf(kind);

  return at < 0 ? KIND_ORDER.length : at;
}

/** The token columns: captured lines summed, uncaptured lines counted apart and adding nothing. */
function tokenTotals(lines: readonly LedgerLine[]): TokenTotals {
  const captured = lines.filter((one) => !uncaptured(one));
  const missing = lines.length - captured.length;

  return {
    in: sum(captured.map((one) => one.tokensIn)),
    cached: sum(captured.map((one) => one.tokensCached ?? 0)),
    out: sum(captured.map((one) => one.tokensOut)),
    reasoning: sum(captured.map((one) => one.tokensReasoning ?? 0)),
    known: captured.length > 0,
    floor: missing > 0,
    uncaptured: missing,
  };
}

/**
 * Several groups as one — a vendor row from its models and kinds, the window from every row. The medians do not add
 * up, so a roll-up carries none. Nothing to roll up is nothing known: no tokens, and money unknown.
 */
export function rollUp(parts: readonly Rollup[]): Rollup {
  return {
    turns: sum(parts.map((one) => one.turns)),
    failed: sum(parts.map((one) => one.failed)),
    tokens: tokensOf(parts.map((one) => one.tokens)),
    money: parts.length === 0 ? UNKNOWN : totalCost(parts.map((one) => one.money)),
  };
}

function tokensOf(parts: readonly TokenTotals[]): TokenTotals {
  return {
    in: sum(parts.map((one) => one.in)),
    cached: sum(parts.map((one) => one.cached)),
    out: sum(parts.map((one) => one.out)),
    reasoning: sum(parts.map((one) => one.reasoning)),
    known: parts.some((one) => one.known),
    floor: parts.some((one) => one.floor),
    uncaptured: sum(parts.map((one) => one.uncaptured)),
  };
}

/** One token column as a person reads it: `1.2M`, `≥ 1.2M` beside an uncaptured line, `unknown` when nothing was. */
export function tokenText(tokens: TokenTotals, field: 'in' | 'cached' | 'out' | 'reasoning'): string {
  if (!tokens.known) {
    return 'unknown';
  }

  return `${tokens.floor ? '≥ ' : ''}${shortNumber(tokens[field])}`;
}

/**
 * What a figure of this kind is divided by: the count the caller knows — the rounds that used a model, every round of
 * a vendor row or of the window, the consultations, the questions — or, when it knows none, the recorded turns, so the
 * figure is labelled per recorded turn rather than per something it is not.
 */
export function unitDivisor(kind: string, count: number | undefined, turns: number): Divisor {
  return count === undefined ? { per: PER_RECORDED_TURN, count: turns } : { per: UNIT_OF[kind] ?? PER_RECORDED_TURN, count };
}

/**
 * The money divided by its divisor. EVERY unit is the denominator — never only the priced ones — and the total's floor
 * is carried: `≥ ~$0.31` per round. Nothing priced is a dash, never an average of the priced few; zero units is a dash
 * saying so, never a division by zero. How many of the N rounds were FULLY priced is not knowable until a ledger line
 * carries its round (story 4.1), so the tooltip says that some of it was not, rather than a count it does not have.
 */
export function perUnit(money: Estimate, divisor: Divisor): PerUnit {
  const units = `${divisor.per}s`;
  if (divisor.count <= 0) {
    return { text: '—', per: divisor.per, note: `no ${units} in this window` };
  }
  if (!money.known) {
    return { text: '—', per: divisor.per, note: `nothing in these ${divisor.count} ${units} was priced` };
  }
  const over = `over ${divisor.count} ${units}`;

  return {
    text: costText({ ...money, usd: money.usd / divisor.count }),
    per: divisor.per,
    note: money.floor ? `${over}; a floor — some of it could not be priced, or its usage was not captured` : over,
  };
}

/**
 * The chat ledger's turns as ledger lines of kind `chat`: on the vendor the line recorded, else the vendor its preset
 * resolves to (`chatRowProvider`, the spending tab's rule), at the model that answered. An answered or a stopped turn
 * is not a failure; anything else is. The chat ledger records no cached or reasoning count — zero, the vendor did not
 * say — and no usage note, so its lines are always captured.
 */
export function chatLedgerLines(turns: readonly ChatTurnRecord[], vendorOf: ChatVendorOf = (id) => id): LedgerLine[] {
  return turns.map((turn) => ({
    utc: turn.utc,
    provider: chatRowProvider(turn.provider, turn.vendor, vendorOf),
    model: turn.model,
    tokensIn: turn.tokensIn,
    tokensOut: turn.tokensOut,
    costUsd: turn.costUsd,
    outcome: CHAT_SETTLED.has(turn.outcome) ? 'ok' : turn.outcome,
    tokensCached: 0,
    tokensReasoning: 0,
    kind: 'chat',
  }));
}

const CHAT_SETTLED: ReadonlySet<string> = new Set(['answered', 'stopped']);

/** The middle value, the mean of the two middle ones for an even count, or null for none. */
function median(values: readonly number[]): number | null {
  if (values.length === 0) {
    return null;
  }
  const sorted = [...values].sort((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);

  return sorted.length % 2 === 1 ? sorted[middle]! : (sorted[middle - 1]! + sorted[middle]!) / 2;
}

function sum(values: readonly number[]): number {
  return values.reduce((total, one) => total + one, 0);
}
