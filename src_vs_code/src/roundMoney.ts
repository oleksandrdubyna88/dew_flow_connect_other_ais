import { ChatTurnRecord } from './chatUsage';
import { chatRowProvider, ChatVendorOf } from './chatSpendRows';
import { costText, Estimate, LineRate, lineCost, shortNumber, totalCost, uncaptured, UNKNOWN, UsageEntry } from './usage';

/**
 * Tokens and money beside every count (story 2.5 of research/PLAN_every_round_is_counted.md).
 *
 * <p>Per vendor row × model × kind — review, consult, question, chat — inside one window: tokens in / cached / out /
 * reasoning, the recorded turns and the failed ones, the median input and output per ledger line, and the money. And
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
 * <p><b>A ledger line is one LAUNCH only when it says so.</b> Since coai-mcp 0.48.0 (story 3.3) a review run on this
 * machine writes one line per launch — a first try, a retry, a repair and a follow-up each get their own, linked by
 * their `turn` and carrying their `attempt` (`usage.ts` reads both). A line from before that has no `attempt` and may
 * hold a repair or a follow-up summed with its first launch; a Team-server row's local line (`cli: remote`) is the
 * job's total; a follow-up in the same conversation may be cumulative; a line with no `cli` says nowhere it ran. What
 * is claimed is decided by the lines themselves ({@link isLaunchLine}), never by a row looked up: a group whose EVERY
 * line is a launch line says *per launch* ({@link Rollup.linesAreLaunches}), any other says *per recorded turn*.
 * Consultation, question, chat and probe lines stay one line per turn. A consult line written since the coai-mcp release that added the field carries its
 * `consultation` (E3.S4 of todo/PLAN_the_round_counter_tail_E3.md), so consult money divides by DISTINCT consultations
 * ({@link consultationUnits}); a line without one is its own unit. Question, chat and probe stay *per recorded turn*. The
 * median column's header follows the groups it heads ({@link medianOutputLabel}): one shared unit is named, mixed units are not.</p>
 */

/** One ledger line as this module reads it — a parsed `usage.jsonl` line, or a chat turn read into the same shape. */
export type LedgerLine = Pick<UsageEntry, 'utc' | 'provider' | 'model' | 'tokensIn' | 'tokensOut' | 'costUsd' | 'outcome'
  | 'usageNote' | 'tokensCached' | 'tokensReasoning' | 'kind' | 'attempt' | 'cli' | 'consultation'>;

/**
 * The attempts coai-mcp writes for ONE local launch (`LaunchAttempts`, `src_mcp/runners/Reviewers/LaunchParts.cs:22–37`): the
 * turn's first launch, a rate-limit retry and a repair — each its own paid launch on its own ledger line.
 *
 * <p>`followUp` is OUT on purpose: an antigravity follow-up continues one conversation, and antigravity may report usage
 * cumulatively in it, so the line may include the first launch (todo/PLAN_the_reviewer_follow_up_is_billed_once.md
 * measures that). `rotated` is a Team-server attempt, which also says `cli: remote`.</p>
 *
 * <p>An allow-list, not a deny-list: an attempt word this build does not know — a newer coai-mcp's, a typo — is NOT
 * claimed as one launch. The safe answer to "is this one launch?" is no, which only costs the label *per recorded turn*.
 * Together with {@link NOT_ONE_LAUNCH_ATTEMPTS} it must name every word of `LaunchAttempts`: a contract test reads that
 * class, so a word coai-mcp adds fails the suite until somebody decides which side of the line it is on.</p>
 */
export const ONE_LAUNCH_ATTEMPTS: ReadonlySet<string> = new Set(['first', 'retry', 'repair']);

/** The attempts coai-mcp names that are NOT one launch, each with why — the other half of the contract test's vocabulary. */
export const NOT_ONE_LAUNCH_ATTEMPTS: Readonly<Record<string, string>> = {
  followUp: 'an antigravity follow-up continues one conversation and its usage may be cumulative, so the line may include the first launch',
  rotated: "the Team server's own attempt word (a job that took more than one account), written with `cli: remote` — never a local line",
};

/**
 * Whether one ledger line is ONE launch: its attempt is one of {@link ONE_LAUNCH_ATTEMPTS} (so not a follow-up, not a word
 * this build does not know, and not absent), and it names the runtime it ran on, which is not `remote` (a Team-server job
 * is written locally as one line holding the whole job). A line from before coai-mcp 0.48.0, a chat turn and anything that
 * says nothing of where it ran are not claimed.
 */
export function isLaunchLine(line: Pick<LedgerLine, 'attempt' | 'cli'>): boolean {
  return ONE_LAUNCH_ATTEMPTS.has(line.attempt ?? '') && says(line.cli) && line.cli !== 'remote';
}

function says(value: string | undefined): boolean {
  return (value ?? '') !== '';
}

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
  /**
   * True when EVERY line behind this is a launch line ({@link isLaunchLine}), captured or not. False for an empty set —
   * an empty set is not claimed as launches — so a label falls back to the safe *per recorded turn*.
   */
  readonly linesAreLaunches: boolean;
}

/** The consultations behind a consult group's lines: each distinct id once, and the lines that carry none. */
export interface ConsultationIds {
  readonly ids: readonly string[];
  /** Lines with no `consultation` — a line older than the field, or a consultant check — each its own unit. */
  readonly withoutId: number;
}

/** What a consult figure is divided by: the distinct consultations plus the lines without an id, one each. */
export interface ConsultationUnits {
  readonly count: number;
  readonly withoutId: number;
}

/** One vendor row × model × kind inside the window. */
export interface MoneyGroup extends Rollup {
  readonly provider: string;
  readonly model: string;
  readonly kind: string;
  /** The median input per ledger line over the captured lines, or null when none was captured. */
  readonly medianIn: number | null;
  /** The median output per ledger line over the captured lines, or null when none was captured. */
  readonly medianOut: number | null;
  /** The consultations behind a consult group; empty for every other kind. */
  readonly consultations: ConsultationIds;
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

/** The unit of a figure whose every line is one launch ({@link isLaunchLine}) — a review line written since coai-mcp 0.48.0. */
export const PER_LAUNCH = 'launch';

/**
 * The unit a group's median output is in: per launch when every line of the group is one launch
 * ({@link Rollup.linesAreLaunches}), else per recorded turn — a line of it can hold more than one launch.
 */
export function medianUnitOf(group: Pick<Rollup, 'linesAreLaunches'>): string {
  return unitOfLines(group.linesAreLaunches);
}

/**
 * The unit the median column's header names, from the groups the block actually draws a median for: the one unit they
 * all share, or an empty string when they differ — or when there are none, so nothing is claimed about a cell that is
 * not drawn. A cell names its own unit only when it differs from this one.
 */
export function medianHeaderUnit(groups: readonly Pick<Rollup, 'linesAreLaunches'>[]): string {
  const units = new Set(groups.map(medianUnitOf));

  return units.size === 1 ? [...units][0]! : '';
}

/** The median column's header for one header unit ({@link medianHeaderUnit}): *median output per launch*, or the neutral one for ''. */
export function medianLabelOfUnit(unit: string): string {
  return unit === '' ? 'median output' : `median output per ${unit}`;
}

/** The median column's header: *per launch*, *per recorded turn*, or the neutral *median output* ({@link medianHeaderUnit}). */
export function medianOutputLabel(groups: readonly Pick<Rollup, 'linesAreLaunches'>[]): string {
  return medianLabelOfUnit(medianHeaderUnit(groups));
}

/** Plurals that are not `+s`. */
const PLURAL_OF: Readonly<Record<string, string>> = { [PER_LAUNCH]: 'launches' };

function plural(unit: string): string {
  return PLURAL_OF[unit] ?? `${unit}s`;
}

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

/** Whether a line falls inside the window — the one test the groups and the Models card line both use. */
export function inside(line: Pick<LedgerLine, 'utc'>, window: TimeWindow): boolean {
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
    linesAreLaunches: members.length > 0 && members.every(isLaunchLine),
    consultations: kindOf(first) === 'consult' ? consultationsOf(members) : NO_CONSULTATIONS,
  };
}

const NO_CONSULTATIONS: ConsultationIds = { ids: [], withoutId: 0 };

function consultationsOf(members: readonly LedgerLine[]): ConsultationIds {
  return {
    ids: [...new Set(members.map((one) => one.consultation ?? '').filter(says))],
    withoutId: members.filter((one) => !says(one.consultation)).length,
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
    linesAreLaunches: parts.length > 0 && parts.every((one) => one.linesAreLaunches),
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
 * a vendor row or of the window, the consultations, the questions — or, when it knows none, the lines it holds: per
 * launch when every one of them is a launch line (`linesAreLaunches`, off by default), else per recorded turn, so the
 * figure is never labelled per something it is not.
 */
export function unitDivisor(kind: string, count: number | undefined, turns: number, linesAreLaunches = false): Divisor {
  return count === undefined ? { per: unitOfLines(linesAreLaunches), count: turns } : { per: UNIT_OF[kind] ?? PER_RECORDED_TURN, count };
}

function unitOfLines(linesAreLaunches: boolean): string {
  return linesAreLaunches ? PER_LAUNCH : PER_RECORDED_TURN;
}

/**
 * What the consult groups of one vendor row are divided by (E3.S4 of todo/PLAN_the_round_counter_tail_E3.md): every DISTINCT
 * consultation once — the same id in two model groups is one consultation — plus each line that carries no id, which is
 * its own unit because nothing says it belongs to another. `count` is the divisor; `withoutId` is what the label and its
 * tooltip say was counted one each.
 */
export function consultationUnits(groups: readonly Pick<MoneyGroup, 'consultations'>[]): ConsultationUnits {
  const withoutId = sum(groups.map((one) => one.consultations.withoutId));

  return { count: new Set(groups.flatMap((one) => one.consultations.ids)).size + withoutId, withoutId };
}

/** The unit beside a consult figure: *per consultation*, or the qualified one when any line of the window carries no id (D9). */
export function consultationLabel(units: Pick<ConsultationUnits, 'withoutId'>): string {
  return units.withoutId > 0 ? 'per consultation (older lines per turn)' : 'per consultation';
}

/** The tooltip's extra sentence: how many lines were counted one each and why — empty when every line carried an id. */
export function consultationNote(units: Pick<ConsultationUnits, 'withoutId'>): string {
  const lines = units.withoutId === 1 ? '1 line' : `${units.withoutId} lines`;

  return units.withoutId === 0
    ? ''
    : `${lines} without a consultation id counted one each — a line written before the field, or a consultant check`;
}

/**
 * The money divided by its divisor. EVERY unit is the denominator — never only the priced ones — and the total's floor
 * is carried: `≥ ~$0.31` per round. Nothing priced is a dash, never an average of the priced few; zero units is a dash
 * saying so, never a division by zero. How many of the N rounds were FULLY priced is not knowable until a ledger line
 * carries its round (story 4.1), so the tooltip says that some of it was not, rather than a count it does not have.
 */
export function perUnit(money: Estimate, divisor: Divisor): PerUnit {
  const units = plural(divisor.per);
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
    ...(turn.cli === undefined ? {} : { cli: turn.cli }),
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
