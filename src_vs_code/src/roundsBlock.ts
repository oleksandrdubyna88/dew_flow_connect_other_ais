import { escapeHtml } from './escapeHtml';
import {
  COUNT_UNAVAILABLE, RoundCountsAnswer, RoundCountsRead, rowIn, STAGES, totalRounds, WINDOW_LABEL, WINDOW_NAMES,
  WindowName, windowEndMs, windowNamed, windowStartMs,
} from './roundCounts';
import { RoundsSnapshot } from './roundCountsCache';
import {
  inside, LedgerLine, MEDIAN_OUTPUT_LABEL, MoneyGroup, moneyGroups, PER_RECORDED_TURN, perUnit, RateOf, rollUp, Rollup,
  TimeWindow, tokenText, unitDivisor,
} from './roundMoney';
import { cardResetLine } from './statedReset';
import { costText, shortNumber } from './usage';
import type { Vendor } from './vendors';

/**
 * The Spending tab's first block: Rounds (story 2.3 of todo/PLAN_every_round_is_counted.md), and the Models card's
 * line beside it (story 2.4).
 *
 * <p>Pure: the counts (`--round-counts`, through the cache), the last seven days of the ledger, the chat lines, the
 * price lookup and the store are handed in. Per vendor row: rounds over 5 h / 24 h / 7 d, the 7 d rounds by stage,
 * the consultations, questions and chat that spent the same vendor beside them (as recorded turns — a ledger line is a
 * turn, and one consultation can be several), reviewers per round from the database's history, tokens in / cached /
 * out / reasoning and the money over 7 d with `roundMoney`'s floors, and ~$ per round. Under each row its MODEL lines —
 * a model change inside a window is two lines — each with its own rounds, the median output per recorded turn, and
 * ~$ per round over the rounds that used it; and its consult / question / chat lines, per recorded turn.</p>
 *
 * <p><b>Unavailable is never 0.</b> Before the first read the block says it is counting; a read that failed says
 * *count unavailable* in every cell a count would fill, and why, once. A Team-server row is its own row, labelled
 * *ran on &lt;server&gt;*, never added to this machine's. The block names the store it counted and, when another
 * installation's store is known to exist, says that store's rounds are not included.</p>
 */

/** The store the counts came from, and the other installations' stores that are known to exist and are not counted. */
export interface StoreFacts {
  /** This side as a person names it — `WSL: Ubuntu`, or empty on the machine's own side. */
  readonly side: string;
  readonly directory: string;
  readonly others: readonly string[];
}

export interface RoundsBlockInput {
  readonly snapshot: RoundsSnapshot | undefined;
  /** The chat ledger's turns as ledger lines (`chatLedgerLines`); cut to the window here. */
  readonly chat: readonly LedgerLine[];
  readonly rateOf: RateOf;
  /** The Team server a row ran on — empty for a row that runs on this machine. */
  readonly ranOn: (provider: string) => string;
  readonly store: StoreFacts;
}

/**
 * The Team server a row runs on, by its row: a `remote` row's server id (or *a Team server* when it names none), and
 * nothing for a row that runs here. The row ids of the ledger and of the counts are the panel's row ids.
 */
export function ranOnOf(vendors: readonly Vendor[]): (provider: string) => string {
  return (provider) => {
    const row = vendors.find((one) => one.id === provider);

    return row?.runtime === 'remote' ? serverOf(row) : '';
  };
}

function serverOf(row: Vendor): string {
  const id = row.teamServerId ?? '';

  return id.length > 0 ? id : 'a Team server';
}

/**
 * The kinds that spend a vendor beside its rounds, and what one of their lines is called.
 *
 * <p>`probe` since story 3.3 of todo/PLAN_every_round_is_counted.md: `coai-mcp --probe-api --model` writes a line per
 * paid completion. Its money is the row's — a row's total holds every kind — and it is said here as its own kind, so it
 * is never read as a round: it has no model line and no round.</p>
 */
const BESIDE: readonly (readonly [string, string])[] = [['consult', 'consult'], ['question', 'question'], ['chat', 'chat'], ['probe', 'probe']];

/** The block, whole. */
export function roundsBlock(input: RoundsBlockInput): string {
  const head = `<section class="rounds-block"><h3 class="ledger">Rounds</h3><p class="hint">${escapeHtml(storeSentence(input.store))}</p>`;
  if (input.snapshot === undefined) {
    return `${head}<p class="hint">Counting…</p></section>`;
  }

  return `${head}${unavailableNote(input.snapshot.read)}${ledgerNote(input.snapshot.ledgerFailure)}${body(input, input.snapshot)}${FOOTNOTE}</section>`;
}

/** What a cell drawn from the ledger says when the ledger did not read. */
const LEDGER_UNAVAILABLE = 'unavailable';

function ledgerNote(failure: string): string {
  return failure.length === 0
    ? ''
    : `<p class="warn">Tokens and money: ${LEDGER_UNAVAILABLE} — the usage ledger could not be read: ${escapeHtml(failure)}.</p>`;
}

/** A cell drawn from the ledger: *unavailable* when the ledger did not read — never a zero standing in for it. */
function spent(facts: Facts, cell: () => string): string {
  return facts.ledgerFailure.length > 0 ? LEDGER_UNAVAILABLE : cell();
}

/** Which store was counted, and which known stores were not. */
export function storeSentence(store: StoreFacts): string {
  const side = store.side.length > 0 ? store.side : 'this machine';
  const others = store.others.map((one) => ` ${one} holds another installation's store; its rounds are not included.`).join('');

  return `Counted from this side's store only — ${side}, ${store.directory}.${others}`;
}

function unavailableNote(read: RoundCountsRead): string {
  return read.state === 'counted' ? '' : `<p class="warn">Rounds: ${COUNT_UNAVAILABLE} — ${escapeHtml(read.why)}.</p>`;
}

const FOOTNOTE = '<p class="hint">Tokens and money are the last 7 days, read from this side\'s usage ledger. '
  + `Consultations, questions and chat are counted in recorded turns, and the median output is per ${PER_RECORDED_TURN}: `
  + 'one ledger line can hold a repair or a follow-up, so a per-launch figure is not claimed. Reviewers per round is from '
  + 'the rounds database\'s history. ≥ means some of it could not be priced or its usage was not captured.</p>';

/** What the block draws from one snapshot. */
interface Facts {
  readonly read: RoundCountsRead;
  readonly window: TimeWindow;
  readonly groups: readonly MoneyGroup[];
  readonly providers: readonly string[];
  readonly input: RoundsBlockInput;
  readonly ledgerFailure: string;
  /** The snapshot itself — what a row's stated reset is read from (story 5.3b), at the snapshot's own clock. */
  readonly snapshot: RoundsSnapshot;
}

function body(input: RoundsBlockInput, snapshot: RoundsSnapshot): string {
  const facts = factsOf(input, snapshot);
  if (facts.providers.length === 0) {
    return '<p class="hint">No rounds, consultations, questions or chat in the last 7 days.</p>';
  }

  const rows = facts.providers.map((provider) => rowBlock(facts, provider)).join('');

  return `<div class="table-scroll"><table class="rounds"><thead><tr>${HEADER_CELLS}</tr></thead><tbody>${rows}</tbody></table></div>`;
}

const HEADERS: readonly string[] = [
  'Vendor row / model', ...WINDOW_NAMES.map((name) => WINDOW_LABEL[name]), 'By stage (7 d)', 'Beside (7 d)',
  'Reviewers per round', escapeHtml(MEDIAN_OUTPUT_LABEL), 'Tokens (7 d)', 'Money (7 d)', 'Per round (7 d)',
];

const HEADER_CELLS = HEADERS.map(headerCell).join('');

function headerCell(label: string): string {
  return `<th scope="col">${label}</th>`;
}

/** The 7 d window the block and the card line both cut the ledger to — the counts' own edges when they counted. */
function sevenDays(snapshot: RoundsSnapshot): TimeWindow {
  // `(from, to]`, as the counts are: a millisecond past the start, so a line on the edge is in the same window as a round.
  return { fromMs: windowStartMs(snapshot.read, '7d', snapshot.atMs) + 1, toMs: windowEndMs(snapshot.read, snapshot.atMs) };
}

function factsOf(input: RoundsBlockInput, snapshot: RoundsSnapshot): Facts {
  const window = sevenDays(snapshot);
  const groups = moneyGroups([...snapshot.ledger, ...input.chat], window, input.rateOf);

  return {
    read: snapshot.read, window, groups, providers: providersOf(snapshot.read, groups), input, ledgerFailure: snapshot.ledgerFailure, snapshot,
  };
}

function providersOf(read: RoundCountsRead, groups: readonly MoneyGroup[]): readonly string[] {
  const counted = read.state === 'counted' ? (windowNamed(read.answer, '7d')?.rows ?? []).map((one) => one.provider) : [];

  return [...new Set([...counted, ...groups.map((one) => one.provider)])].sort((a, b) => a.localeCompare(b));
}

function rowBlock(facts: Facts, provider: string): string {
  const mine = facts.groups.filter((one) => one.provider === provider);

  return rowLine(facts, provider, mine)
    + modelsOf(facts, provider, mine).map((model) => modelLine(facts, provider, model, mine)).join('')
    + BESIDE.map(([kind, word]) => kindLine(mine, kind, word)).join('');
}

function rowLine(facts: Facts, provider: string, mine: readonly MoneyGroup[]): string {
  const ranOn = facts.input.ranOn(provider);
  const name = `<b>${escapeHtml(provider)}</b>${ranOnHint(ranOn)}${resetHint(cardResetLine(facts.snapshot, provider))}`;
  const all = rollUp(mine);

  return tr('row', [
    name, ...WINDOW_NAMES.map((window) => roundsCell(facts.read, window, provider)), stagesCell(facts.read, provider),
    spent(facts, () => besideCell(mine)), reviewersCell(facts.read, provider), '', spent(facts, () => tokensCell(all)),
    spent(facts, () => costText(all.money)), spent(facts, () => perRoundCell(all, rowRounds(facts.read, provider))),
  ]);
}

/** The row's stated reset beside its name (story 5.3b) — `quota out — resets in 89 h (Wed 04:49)` — or nothing. */
function resetHint(said: string): string {
  return said.length > 0 ? ` <span class="warn reset">${escapeHtml(said)}</span>` : '';
}

/** *ran on &lt;server&gt;* beside a Team-server row's name; nothing for a row that runs here. */
function ranOnHint(ranOn: string): string {
  return ranOn.length > 0 ? ` <span class="hint">ran on ${escapeHtml(ranOn)}</span>` : '';
}

function tr(kind: string, cells: readonly string[]): string {
  return `<tr class="${kind}">${cells.map(dataCell).join('')}</tr>`;
}

function dataCell(cell: string): string {
  return `<td>${cell}</td>`;
}

/** A row's rounds in one window: a count (nought included, when counted) or *count unavailable*. */
function roundsCell(read: RoundCountsRead, window: WindowName, provider: string): string {
  if (read.state !== 'counted') {
    return COUNT_UNAVAILABLE;
  }
  const row = rowIn(read.answer, window, provider);

  return String(row === undefined ? 0 : totalRounds(row));
}

function stagesCell(read: RoundCountsRead, provider: string): string {
  if (read.state !== 'counted') {
    return COUNT_UNAVAILABLE;
  }
  const row = rowIn(read.answer, '7d', provider);

  return STAGES.map((stage) => `${stage} ${row === undefined ? 0 : row.rounds[stage]}`).join(' · ');
}

function besideCell(mine: readonly MoneyGroup[]): string {
  const said = BESIDE.map(([kind, word]) => [word, turnsOf(mine, kind)] as const).filter(([, turns]) => turns > 0);

  const list = said.map(([word, turns]) => `${word} ${turns}`).join(' · ');

  return said.length === 0 ? '—' : `${list} recorded turns`;
}

function turnsOf(groups: readonly MoneyGroup[], kind: string): number {
  return groups.filter((one) => one.kind === kind).reduce((total, one) => total + one.turns, 0);
}

/** Reviewer rows per round over 7 d — from the database's history, so it exists for rounds the ledger cannot tie. */
function reviewersCell(read: RoundCountsRead, provider: string): string {
  if (read.state !== 'counted') {
    return COUNT_UNAVAILABLE;
  }
  const row = rowIn(read.answer, '7d', provider);
  const rounds = row === undefined ? 0 : totalRounds(row);

  return rounds === 0 ? '—' : (row!.reviewers / rounds).toFixed(1);
}

function tokensCell(all: Rollup): string {
  const tokens = all.tokens;

  return `in ${tokenText(tokens, 'in')} · cached ${tokenText(tokens, 'cached')} · out ${tokenText(tokens, 'out')} · reasoning ${tokenText(tokens, 'reasoning')}`;
}

/** The 7 d rounds a row took part in, or nothing when the count is unavailable. */
function rowRounds(read: RoundCountsRead, provider: string): number | undefined {
  if (read.state !== 'counted') {
    return undefined;
  }
  const row = rowIn(read.answer, '7d', provider);

  return row === undefined ? 0 : totalRounds(row);
}

/** ~$ per round with its tooltip — a dash saying the count is unavailable when there is no count to divide by. */
function perRoundCell(part: Rollup, rounds: number | undefined): string {
  if (rounds === undefined) {
    return `<span title="${COUNT_UNAVAILABLE}">—</span>`;
  }
  const figure = perUnit(part.money, unitDivisor('review', rounds, part.turns));

  return `<span title="${escapeHtml(figure.note)}">${escapeHtml(figure.text)}</span>`;
}

/** Every model the row's rounds or its review lines name in the 7 d window, in name order. */
function modelsOf(facts: Facts, provider: string, mine: readonly MoneyGroup[]): readonly string[] {
  const counted = facts.read.state === 'counted' ? (rowIn(facts.read.answer, '7d', provider)?.models ?? []).map((one) => one.model) : [];
  const recorded = mine.filter((one) => one.kind === 'review').map((one) => one.model);

  return [...new Set([...counted, ...recorded])].filter((model) => model.length > 0).sort((a, b) => a.localeCompare(b));
}

function modelLine(facts: Facts, provider: string, model: string, mine: readonly MoneyGroup[]): string {
  const group = mine.find((one) => one.kind === 'review' && one.model === model);
  const part = group ?? rollUp([]);

  return tr('model', [
    `<span class="indent">${escapeHtml(model)}</span>`,
    ...WINDOW_NAMES.map((window) => modelRoundsCell(facts.read, window, provider, model)),
    '', '', '', spent(facts, () => medianCell(group)), spent(facts, () => tokensCell(part)), spent(facts, () => costText(part.money)),
    spent(facts, () => modelPerRoundCell(part, modelRounds(facts.read, '7d', provider, model))),
  ]);
}

function modelRoundsCell(read: RoundCountsRead, window: WindowName, provider: string, model: string): string {
  if (read.state !== 'counted') {
    return COUNT_UNAVAILABLE;
  }
  const rounds = modelRounds(read, window, provider, model);

  return rounds === undefined ? modelsUnknown(read.answer) : String(rounds);
}

/** Rounds per model exist only from the step that recorded a reviewer's model on. */
function modelsUnknown(answer: RoundCountsAnswer): string {
  return answer.modelsKnownSince === null ? 'not recorded' : '0';
}

function modelRounds(read: RoundCountsRead, window: WindowName, provider: string, model: string): number | undefined {
  return read.state === 'counted' ? rowIn(read.answer, window, provider)?.models.find((one) => one.model === model)?.rounds : undefined;
}

/** A model line divides by the rounds that used it when they are known, else per recorded turn — labelled either way. */
function modelPerRoundCell(part: Rollup, rounds: number | undefined): string {
  const figure = perUnit(part.money, unitDivisor('review', rounds, part.turns));
  const per = figure.per === PER_RECORDED_TURN ? ` <span class="hint">per ${PER_RECORDED_TURN}</span>` : '';

  return `<span title="${escapeHtml(figure.note)}">${escapeHtml(figure.text)}</span>${per}`;
}

function medianCell(group: MoneyGroup | undefined): string {
  return group?.medianOut === null || group === undefined ? '—' : shortNumber(group.medianOut);
}

/** One line per kind that spent the row beside its rounds — every model together, per recorded turn. */
function kindLine(mine: readonly MoneyGroup[], kind: string, word: string): string {
  const groups = mine.filter((one) => one.kind === kind);
  if (groups.length === 0) {
    return '';
  }
  const part = rollUp(groups);
  const figure = perUnit(part.money, unitDivisor(kind, undefined, part.turns));

  return tr('kind', [
    `<span class="indent">${word} · ${part.turns} recorded turns</span>`, '', '', '', '', '', '', '',
    tokensCell(part), costText(part.money), `<span title="${escapeHtml(figure.note)}">${escapeHtml(figure.text)}</span> <span class="hint">per ${PER_RECORDED_TURN}</span>`,
  ]);
}

/**
 * The Models card's line (story 2.4): `7 days: 214 rounds · 31 consult turns · 5 h: 12`, from the cached snapshot —
 * empty before the first read, *count unavailable* when the read failed, never a zero standing in for either.
 */
export function cardRoundsLine(snapshot: RoundsSnapshot | undefined, provider: string): string {
  if (snapshot === undefined) {
    return '';
  }
  if (snapshot.read.state !== 'counted') {
    return `Rounds: ${COUNT_UNAVAILABLE}`;
  }

  return `7 days: ${rowRounds(snapshot.read, provider) ?? 0} rounds · ${consultTurns(snapshot, provider)} · 5 h: ${roundsCell(snapshot.read, '5h', provider)}`;
}

/** The card's consult turns, from the cached ledger — *unavailable* when it did not read, never 0 standing in. */
function consultTurns(snapshot: RoundsSnapshot, provider: string): string {
  if (snapshot.ledgerFailure.length > 0) {
    return `consult turns ${LEDGER_UNAVAILABLE}`;
  }

  // Cut to the block's own 7 days: the tail read can start mid-chunk and the snapshot is held a minute, so the ledger
  // can carry an older line, and the card and the block must not disagree about one "7 days" (CodeRabbit on #750).
  const window = sevenDays(snapshot);

  return `${snapshot.ledger.filter((one) => one.provider === provider && one.kind === 'consult' && inside(one, window)).length} consult turns`;
}
