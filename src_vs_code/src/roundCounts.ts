/**
 * What `coai-mcp --round-counts` answers, read as data (story 2.2 of todo/PLAN_every_round_is_counted.md).
 *
 * <p>Types and pure functions only, so the page and the tests can import it: the spawn is `roundCountsRead.ts`, through
 * `serverRun`. The contract is the mode's (`src_mcp/src/Server/RoundCountsMode.cs`, documented in
 * `research/module_server.md`): `{nowUtc, windows: [{name, fromUtc, toUtc, rows: [{provider, rounds: {plan, code,
 * document, feature}, reviewers, models: [{model, rounds}]}]}], modelsKnownSince}`, windows half-open `(from, to]`.</p>
 *
 * <p><b>A count, or unavailable — never a zero standing in for "nobody knows".</b> Exit 0 with an answer that parses is
 * a count (a machine that never ran a round answers rows `[]`, which IS nought). Exit 64 is a `coai-mcp` older than the
 * mode; 65 a refused request; 74 a database that will not read; -1 a timeout or a spawn that failed; an exit-0 answer
 * that does not parse is a binary saying something this build cannot read. Every one of those is `unavailable`, with
 * its reason, and the Rounds block and the card line say *count unavailable* rather than a number.</p>
 */

/** The three trailing windows, in the order a person reads them. */
export const WINDOW_NAMES = ['5h', '24h', '7d'] as const;

export type WindowName = (typeof WINDOW_NAMES)[number];

/** What each window is called where a person reads it. */
export const WINDOW_LABEL: Readonly<Record<WindowName, string>> = { '5h': '5 h', '24h': '24 h', '7d': '7 d' };

/** How long each window reaches back — what a window is when no count says, so the ledger is still cut the same way. */
export const WINDOW_MS: Readonly<Record<WindowName, number>> = { '5h': 5 * 3_600_000, '24h': 24 * 3_600_000, '7d': 7 * 24 * 3_600_000 };

/** The review stages a round can be, as the mode names them. */
export const STAGES = ['plan', 'code', 'document', 'feature'] as const;

export type Stage = (typeof STAGES)[number];

/** One vendor row's rounds inside one window. */
export interface RoundsRow {
  readonly provider: string;
  readonly rounds: Readonly<Record<Stage, number>>;
  /** The reviewer rows those rounds launched — the numerator of "reviewers per round". */
  readonly reviewers: number;
  /** Rounds per reviewer MODEL; only rounds written since `modelsKnownSince` have one. */
  readonly models: readonly { readonly model: string; readonly rounds: number }[];
}

export interface RoundsWindow {
  readonly name: WindowName;
  readonly fromUtc: string;
  readonly toUtc: string;
  readonly rows: readonly RoundsRow[];
}

export interface RoundCountsAnswer {
  readonly nowUtc: string;
  readonly windows: readonly RoundsWindow[];
  /** From when reviewer rows carry their model — null when no round has one yet. */
  readonly modelsKnownSince: string | null;
  /**
   * Why this machine's answer carries no vendor gauge figure — the shared derivation's code (`local-capture-deferred`
   * today: story 6.1b is deferred), read from `gauge.unavailable` (story 6.2 of todo/PLAN_every_round_is_counted.md).
   * Absent from a `coai-mcp` older than the field, and then nothing is said about a gauge.
   */
  readonly gaugeUnavailable?: string;
}

/** What a read came to: a count, or why there is none. */
export type RoundCountsRead =
  | { readonly state: 'counted'; readonly answer: RoundCountsAnswer }
  | { readonly state: 'unavailable'; readonly why: string };

/** What the Rounds block and the card line say in place of a number nobody has. */
export const COUNT_UNAVAILABLE = 'count unavailable';

/** Why an exit code is not a count, by code. */
const WHY_BY_CODE: Readonly<Record<number, string>> = {
  64: 'this coai-mcp is older than --round-counts — update it in Setup › MCP server',
  65: 'coai-mcp refused the request',
  74: 'coai-mcp could not read its rounds database',
  [-1]: 'coai-mcp did not answer in time',
};

/** No server installed on this side: nothing could be asked. */
export const NO_SERVER: RoundCountsRead = { state: 'unavailable', why: 'no coai-mcp is installed on this side' };

/** One spawn's exit and output, as a read. */
export function roundCountsFrom(ran: { readonly code: number; readonly output: string }): RoundCountsRead {
  if (ran.code !== 0) {
    return { state: 'unavailable', why: WHY_BY_CODE[ran.code] ?? `coai-mcp exited ${ran.code}` };
  }
  const answer = parseRoundCounts(ran.output);

  return answer === undefined ? { state: 'unavailable', why: 'coai-mcp answered something this build cannot read' } : { state: 'counted', answer };
}

/** The mode's JSON, checked field by field — or nothing, when any part of it is not the contract. */
export function parseRoundCounts(output: string): RoundCountsAnswer | undefined {
  const value = jsonOf(output);

  return isAnswerShape(value) ? answerOf(value) : undefined;
}

interface Shaped extends Record<string, unknown> {
  readonly nowUtc: string;
  readonly windows: readonly unknown[];
}

function isAnswerShape(value: unknown): value is Shaped {
  return isRecord(value) && typeof value['nowUtc'] === 'string' && Array.isArray(value['windows']);
}

function answerOf(value: Shaped): RoundCountsAnswer | undefined {
  const windows = value.windows.map(windowOf);

  return windows.includes(undefined) ? undefined
    : { nowUtc: value.nowUtc, windows: windows as RoundsWindow[], modelsKnownSince: textOrNull(value['modelsKnownSince']), ...gaugeReasonOf(value['gauge']) };
}

/** The local answer's gauge reason (story 6.2), present only when the binary said one — an older one says nothing. */
function gaugeReasonOf(gauge: unknown): Pick<RoundCountsAnswer, 'gaugeUnavailable'> {
  const said = isRecord(gauge) ? gauge['unavailable'] : undefined;

  return typeof said === 'string' && said.length > 0 ? { gaugeUnavailable: said } : {};
}

function textOrNull(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function jsonOf(output: string): unknown {
  try {
    return JSON.parse(output);
  } catch {
    return undefined;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

interface ShapedWindow extends Record<string, unknown> {
  readonly name: WindowName;
  readonly rows: readonly unknown[];
}

function isWindowShape(raw: unknown): raw is ShapedWindow {
  return isRecord(raw) && isWindowName(raw['name']) && Array.isArray(raw['rows']);
}

function windowOf(raw: unknown): RoundsWindow | undefined {
  return isWindowShape(raw) ? windowFrom(raw) : undefined;
}

function windowFrom(raw: ShapedWindow): RoundsWindow | undefined {
  const rows = raw.rows.map(rowOf);

  return rows.includes(undefined) ? undefined
    : { name: raw.name, fromUtc: text(raw['fromUtc']), toUtc: text(raw['toUtc']), rows: rows as RoundsRow[] };
}

function isWindowName(value: unknown): value is WindowName {
  return typeof value === 'string' && (WINDOW_NAMES as readonly string[]).includes(value);
}

interface ShapedRow extends Record<string, unknown> {
  readonly provider: string;
  readonly rounds: Record<string, unknown>;
}

function isRowShape(raw: unknown): raw is ShapedRow {
  return isRecord(raw) && typeof raw['provider'] === 'string' && isRecord(raw['rounds']);
}

function rowOf(raw: unknown): RoundsRow | undefined {
  return isRowShape(raw) ? rowFrom(raw) : undefined;
}

function rowFrom(raw: ShapedRow): RoundsRow {
  const rounds = raw.rounds;

  return {
    provider: raw.provider,
    rounds: { plan: count(rounds['plan']), code: count(rounds['code']), document: count(rounds['document']), feature: count(rounds['feature']) },
    reviewers: count(raw['reviewers']),
    models: Array.isArray(raw['models']) ? raw['models'].flatMap(modelOf) : [],
  };
}

function modelOf(raw: unknown): readonly { readonly model: string; readonly rounds: number }[] {
  return isRecord(raw) && typeof raw['model'] === 'string' ? [{ model: raw['model'], rounds: count(raw['rounds']) }] : [];
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : 0;
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** A row's rounds, every stage together — a round has one stage, so the sum counts each once. */
export function totalRounds(row: Pick<RoundsRow, 'rounds'>): number {
  return STAGES.reduce((total, stage) => total + row.rounds[stage], 0);
}

/** One window of an answer, or nothing when the answer does not carry it. */
export function windowNamed(answer: RoundCountsAnswer, name: WindowName): RoundsWindow | undefined {
  return answer.windows.find((one) => one.name === name);
}

/** One vendor row inside one window of an answer — absent means it ran no round there, which is a count of nought. */
export function rowIn(answer: RoundCountsAnswer, name: WindowName, provider: string): RoundsRow | undefined {
  return windowNamed(answer, name)?.rows.find((one) => one.provider === provider);
}

/**
 * Where a window starts, in epoch ms: the mode's own `fromUtc` when it counted, so the ledger is cut at exactly the
 * instant the rounds were, else the trailing span from `nowMs` — one clock either way.
 */
export function windowStartMs(read: RoundCountsRead, name: WindowName, nowMs: number): number {
  const from = countedStart(read, name);

  return Number.isFinite(from) ? from : nowMs - WINDOW_MS[name];
}

function countedStart(read: RoundCountsRead, name: WindowName): number {
  return read.state === 'counted' ? Date.parse(windowNamed(read.answer, name)?.fromUtc ?? '') : Number.NaN;
}

/** The instant the windows end: the mode's `nowUtc` when it counted, else `nowMs`. */
export function windowEndMs(read: RoundCountsRead, nowMs: number): number {
  const at = read.state === 'counted' ? Date.parse(read.answer.nowUtc) : Number.NaN;

  return Number.isFinite(at) ? at : nowMs;
}
