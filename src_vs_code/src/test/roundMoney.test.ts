import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ChatTurnRecord } from '../chatUsage';
import {
  chatLedgerLines, LedgerLine, MEDIAN_OUTPUT_LABEL, moneyGroups, perUnit, rollUp, tokenText, unitDivisor,
} from '../roundMoney';
import { LineRate, parseUsage } from '../usage';

/**
 * Tokens and money beside every count (story 2.5 of todo/PLAN_every_round_is_counted.md): per vendor row × model × kind,
 * tokens in / cached / out / reasoning with the uncaptured lines counted apart, recorded turns, medians per RECORDED
 * TURN, money reported or estimated with its floor — and the per-round division the Rounds block will show once the
 * round counts arrive (stories 2.1–2.3).
 */

const WINDOW = { fromMs: Date.parse('2026-10-09T00:00:00Z'), toMs: Date.parse('2026-10-10T00:00:00Z') };

const line = (over: Partial<LedgerLine> = {}): LedgerLine => ({
  utc: '2026-10-09T10:00:00Z', provider: 'codex', model: 'gpt-6-luna', tokensIn: 1_000_000, tokensOut: 0,
  costUsd: null, outcome: 'ok', tokensCached: 0, tokensReasoning: 0, kind: 'review', ...over,
});

const RATES: Readonly<Record<string, LineRate>> = {
  'gpt-6-luna': { in: 2, out: 8, cached: 0.2 },
  'gpt-6-astra': { in: 1, out: 4 },
};
const rateOf = (_provider: string, model: string): LineRate | undefined => RATES[model];

test('the groups are per vendor row, model and kind, and only inside the window', () => {
  const groups = moneyGroups([
    line(),
    line({ model: 'gpt-6-astra', kind: 'consult' }),
    line({ model: 'gpt-6-astra', kind: 'consult' }),
    line({ provider: 'work-codex', model: 'gpt-6-luna' }),
    line({ utc: '2026-10-08T23:59:59Z' }),
    line({ utc: '2026-10-10T00:00:01Z' }),
  ], WINDOW, rateOf);

  assert.deepEqual(
    groups.map((one) => [one.provider, one.model, one.kind, one.turns]),
    [['codex', 'gpt-6-astra', 'consult', 2], ['codex', 'gpt-6-luna', 'review', 1], ['work-codex', 'gpt-6-luna', 'review', 1]],
    'a line outside the window was counted, or a Team-server row was folded into this machine\'s own',
  );
});

test('the tokens are summed in all four columns, cached and reasoning included, and failures are counted', () => {
  const [group] = moneyGroups([
    line({ tokensIn: 1000, tokensCached: 800, tokensOut: 300, tokensReasoning: 120 }),
    line({ tokensIn: 500, tokensCached: 0, tokensOut: 100, tokensReasoning: 0, outcome: 'timeout' }),
  ], WINDOW, rateOf);

  assert.deepEqual([group?.tokens.in, group?.tokens.cached, group?.tokens.out, group?.tokens.reasoning], [1500, 800, 400, 120]);
  assert.equal(group?.failed, 1, 'a reviewer that did not answer still burned tokens and must be counted');
  assert.equal(group?.tokens.floor, false, 'nothing was uncaptured — the positive case the floor tests below need');
});

test('AnUncapturedLine_MakesATokenTotalAFloor_NeverZero — an uncaptured line adds nothing and marks ≥; only uncaptured lines are unknown', () => {
  const [mixed] = moneyGroups([
    line({ tokensIn: 1200, tokensOut: 30 }),
    line({ tokensIn: 50, tokensOut: 0, usageNote: 'usage not captured', outcome: 'timeout' }),
  ], WINDOW, rateOf);
  const [only] = moneyGroups([line({ tokensIn: 0, tokensOut: 0, usageNote: 'usage not captured', outcome: 'cancelled' })], WINDOW, rateOf);

  assert.equal(mixed?.tokens.in, 1200, 'the uncaptured line\'s partial count was added as though it were the whole');
  assert.equal(mixed?.tokens.floor, true);
  assert.equal(mixed?.tokens.uncaptured, 1);
  assert.equal(tokenText(mixed!.tokens, 'in'), '≥ 1.2k');
  assert.equal(only?.tokens.known, false, 'a total made only of uncaptured lines is unknown');
  assert.equal(tokenText(only!.tokens, 'in'), 'unknown', 'only uncaptured lines rendered a count — never 0');
  assert.equal(only?.money.known, false, 'and its money is unknown, never $0');
});

test('ARepairedTurn_IsOneRecordedTurn_NotOneLaunch — a repair is two launches on one line, so it is one recorded turn and the median says so', () => {
  // A repair re-asks the reviewer for well-formed JSON; until story 3.3 both launches are summed on ONE ledger line.
  const [group] = moneyGroups([
    line({ tokensIn: 60_000, tokensOut: 4_000 }), // the repaired turn: first launch + repair, one line
    line({ tokensIn: 30_000, tokensOut: 1_000 }),
    line({ tokensIn: 20_000, tokensOut: 2_000 }),
  ], WINDOW, rateOf);

  assert.equal(group?.turns, 3, 'the repaired turn was counted as two launches');
  assert.equal(group?.medianOut, 2_000);
  assert.equal(group?.medianIn, 30_000);
  assert.match(MEDIAN_OUTPUT_LABEL, /per recorded turn/u);
  assert.doesNotMatch(MEDIAN_OUTPUT_LABEL, /launch/u, 'a per-launch median cannot be computed from the ledger and must not be claimed');
});

test('the money is reported where the vendor billed it, estimated per line otherwise, and a floor when a line was unpriced', () => {
  const [billed] = moneyGroups([line({ provider: 'claude', model: 'claude-opus-5', costUsd: 0.42 })], WINDOW, rateOf);
  const [cached] = moneyGroups([line({ tokensIn: 1_000_000, tokensCached: 800_000 })], WINDOW, rateOf);
  const [unpriced] = moneyGroups([line(), line({ model: 'gpt-6-luna' }), line({ model: 'gpt-6-luna', tokensIn: 0, usageNote: 'usage not captured' })], WINDOW, rateOf);

  assert.deepEqual(billed?.money, { usd: 0.42, known: true, floor: false, estimated: false });
  assert.ok(Math.abs((cached?.money.usd ?? 0) - 0.56) < 1e-9, `cached input at the cached rate: ${cached?.money.usd}`);
  assert.equal(unpriced?.money.floor, true, 'an uncaptured line made no floor on the money');
  assert.ok(Math.abs((unpriced?.money.usd ?? 0) - 4) < 1e-9);
});

test('APerRoundFigure_CountsEveryRound_AndCarriesTheFloor — every round is the denominator, never only the priced ones', () => {
  const groups = moneyGroups([
    line(), // $2
    line(), // $2
    line({ model: 'a-model-nobody-lists' }), // unpriced: the row is a floor
  ], WINDOW, rateOf);
  const row = rollUp(groups.filter((one) => one.provider === 'codex'));
  const figure = perUnit(row.money, unitDivisor('review', 4, row.turns));

  assert.equal(figure.text, '≥ ~$1.00', '$4 over FOUR rounds, a floor — not $4 over the two priced lines');
  assert.equal(figure.per, 'round');
  assert.match(figure.note, /4 rounds/u);
  assert.match(figure.note, /not all priced|could not be priced/u, 'the tooltip does not say why the figure is a floor');

  const nothing = rollUp(moneyGroups([line({ model: 'a-model-nobody-lists' })], WINDOW, rateOf));
  assert.equal(perUnit(nothing.money, unitDivisor('review', 4, nothing.turns)).text, '—', 'nothing priced is a dash, never an average of nothing');
});

test('AModelLine_DividesByTheRoundsThatUsedIt — a model\'s line divides by ITS rounds, or is per recorded turn when they are unknown', () => {
  const groups = moneyGroups([line(), line(), line(), line({ model: 'gpt-6-astra' }), line({ model: 'gpt-6-astra' })], WINDOW, rateOf);
  const luna = groups.find((one) => one.model === 'gpt-6-luna')!;
  const astra = groups.find((one) => one.model === 'gpt-6-astra')!;

  assert.deepEqual(
    perUnit(luna.money, unitDivisor('review', 3, luna.turns)),
    { text: '~$2.00', per: 'round', note: 'over 3 rounds' },
    'three rounds used luna: $6 / 3',
  );
  const perTurn = perUnit(astra.money, unitDivisor('review', undefined, astra.turns));
  assert.equal(perTurn.per, 'recorded turn', 'a model whose rounds are unknown was divided by something it is not');
  assert.equal(perTurn.text, '~$1.00', '$2 over its 2 recorded turns');
});

test('AConsultationOnlyWindow_ShowsNoPerRoundFigure — priced consultations and zero rounds: a dash that says why, never a division by zero', () => {
  const groups = moneyGroups([line({ model: 'gpt-6-astra', kind: 'consult' }), line({ model: 'gpt-6-astra', kind: 'consult' })], WINDOW, rateOf);
  const row = rollUp(groups);
  const perRound = perUnit(row.money, unitDivisor('review', 0, row.turns));

  assert.equal(row.money.known, true, 'the window has priced usage — the case this test is about');
  assert.equal(perRound.text, '—');
  assert.equal(perRound.note, 'no rounds in this window');
  assert.equal(perUnit(row.money, unitDivisor('consult', 2, row.turns)).text, '~$1.00', 'per consultation still reads as money');
});

test('chat turns join as kind chat, on the vendor their line names, at their own model', () => {
  const turn = (over: Partial<ChatTurnRecord>): ChatTurnRecord => ({
    utc: '2026-10-09T11:00:00Z', provider: 'preset-1', model: 'gpt-6-astra', tokensIn: 1_000_000, tokensOut: 0,
    costUsd: null, seconds: 3, outcome: 'answered', conversation: 'c1', title: 't', ...over,
  });
  const lines = chatLedgerLines([turn({ vendor: 'codex' }), turn({ outcome: 'stopped' }), turn({ outcome: 'failed' })], (id) => `vendor-of-${id}`);
  const groups = moneyGroups(lines, WINDOW, rateOf);

  assert.deepEqual(groups.map((one) => [one.provider, one.kind, one.turns, one.failed]), [
    ['codex', 'chat', 1, 0],
    ['vendor-of-preset-1', 'chat', 2, 1],
  ], 'a stopped turn is not a failure; a recorded vendor wins over the preset\'s');
});

test('a line parsed from the real ledger shape reaches the groups with its cached count and kind', () => {
  const parsed = parseUsage('{"utc":"2026-10-09T10:00:00Z","provider":"codex","model":"gpt-6-luna","tokensIn":1000000,'
    + '"tokensOut":0,"tokensCached":800000,"kind":"question","outcome":"ok"}');
  const [group] = moneyGroups(parsed, WINDOW, rateOf);

  assert.equal(group?.kind, 'question');
  assert.equal(group?.tokens.cached, 800_000);
});
