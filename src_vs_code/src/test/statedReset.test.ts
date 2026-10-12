import assert from 'node:assert/strict';
import { before, test } from 'node:test';

import { modelsTabHtml } from '../modelsTab';
import { roundsBlock, RoundsBlockInput } from '../roundsBlock';
import { cardResetLine, resetLine } from '../statedReset';
import { parseUsageLine, UsageEntry } from '../usage';
import { DEFAULT_VENDORS } from '../vendors';
import { panelState } from './panelPageHarness';
import { BundledLog, bundledLogModule, runningDomPage } from './roundsLogPageHarness';
import { counted, hoursAgo, NO_CHAT, NOW, snapshot, usageLine } from './roundCountsFixtures';

/**
 * A stated reset is read locally (story 5.3b of todo/PLAN_every_round_is_counted.md): the ledger line's `resetsUtc` —
 * written by `coai-mcp` from the refusal, through the one parser both halves use — becomes
 * `gemini: quota out — resets in 89 h (Wed 04:49)` on the Spending tab's Rounds block and on the row's model card. It is
 * shown only while it is ahead, and only while the row's LAST line is that refusal: a later answer means the account
 * works again, whatever the vendor said before.
 */

/** NOW + 88h49m9s — agy's verbatim reset, as `coai-mcp` writes it. */
const AGY_RESET = new Date(NOW + ((88 * 3600) + (49 * 60) + 9) * 1000).toISOString();

function refused(over: Partial<UsageEntry> = {}): UsageEntry {
  return usageLine({
    provider: 'gemini', model: 'gemini-3.8-flash-low', utc: hoursAgo(0.5), resetsUtc: AGY_RESET,
    outcome: 'rate limited (after 1 attempt): error: Individual quota reached. Resets in 88h49m9s.', ...over,
  });
}

test('TheParser_KeepsTheStatedReset — present when the line says it, absent when empty or older than the field', () => {
  const said = parseUsageLine(`{"utc":"2026-10-10T11:30:00Z","provider":"gemini","outcome":"rate limited","resetsUtc":"${AGY_RESET}"}`);
  const empty = parseUsageLine('{"utc":"2026-10-10T11:30:00Z","provider":"gemini","outcome":"ok","resetsUtc":""}');
  const old = parseUsageLine('{"utc":"2026-10-10T11:30:00Z","provider":"gemini","outcome":"ok"}');

  assert.equal(said?.resetsUtc, AGY_RESET, 'the stated reset was dropped on the way in');
  assert.equal(empty?.resetsUtc, undefined, 'an empty reset is absent, never an instant');
  assert.equal(old?.resetsUtc, undefined);
});

test('ARowWhoseLastLineStatedAReset_SaysWhenItComesBack — hours rounded UP, and the local day and time', () => {
  assert.equal(resetLine([usageLine(), refused()], 'gemini', NOW, 'UTC'), 'quota out — resets in 89 h (Wed 04:49)',
    '88h49m left must never read as a shorter wait than the vendor stated');
});

test('AResetUnderAnHour_IsSaidInMinutes', () => {
  const soon = new Date(NOW + ((33 * 60) + 52) * 1000).toISOString();

  assert.equal(resetLine([refused({ resetsUtc: soon })], 'gemini', NOW, 'UTC'), 'quota out — resets in 34 min (Sat 12:33)');
});

test('AResetThatHasPassed_IsNotShown', () => {
  assert.equal(resetLine([refused({ resetsUtc: hoursAgo(0.1) })], 'gemini', NOW, 'UTC'), '');
});

test('ALineWhoseUtcIsNoDate_NeverHidesTheNewestRefusal — a torn or hand-edited line is not "newest" (CodeRabbit on #773)', () => {
  // `parseUsageLine` only asks that `utc` be a string. A line whose date does not parse compared as NaN, every comparison
  // with NaN is false, so a FIRST such line stayed "newest" for good and the refusal after it was never read.
  const torn = usageLine({ provider: 'gemini', utc: 'not a date' });

  assert.equal(resetLine([torn, refused()], 'gemini', NOW, 'UTC'), 'quota out — resets in 89 h (Wed 04:49)',
    'the refusal is the newest line with a date; a line without one decides nothing');
  assert.equal(resetLine([refused(), torn], 'gemini', NOW, 'UTC'), 'quota out — resets in 89 h (Wed 04:49)',
    'and it does not decide when it comes last either');
});

test('ALaterAnswer_HidesAnEarlierReset — the account works again, whatever the vendor said before', () => {
  const answered = usageLine({ provider: 'gemini', utc: hoursAgo(0.1) });

  const answeredBefore = usageLine({ provider: 'gemini', utc: hoursAgo(2) });

  assert.equal(resetLine([refused(), answered], 'gemini', NOW, 'UTC'), '');
  assert.equal(resetLine([answered, refused()], 'gemini', NOW, 'UTC'), '', 'a later answer read first still hides the reset');
  assert.equal(resetLine([refused(), answeredBefore], 'gemini', NOW, 'UTC'), 'quota out — resets in 89 h (Wed 04:49)',
    'the order lines arrive in is not their order in time — the newest by its utc decides');
});

test('AnotherRowsReset_IsNotThisRows', () => {
  assert.equal(resetLine([refused()], 'codex', NOW, 'UTC'), '');
});

test('the Rounds block says the reset beside the row it belongs to, and nowhere else', () => {
  const block: RoundsBlockInput = {
    snapshot: snapshot(counted(), [usageLine(), refused()]), chat: NO_CHAT, rateOf: () => undefined, ranOn: () => '',
    store: { side: '', directory: '/d', others: [] },
  };
  const html = roundsBlock(block);
  const gemini = html.split('<tr').find((row) => row.includes('<b>gemini</b>')) ?? '';
  const codex = html.split('<tr').find((row) => row.includes('<b>codex</b>')) ?? '';

  assert.match(gemini, /<span class="warn reset">quota out — resets in 89 h \(\w{3} \d{2}:\d{2}\)<\/span>/u);
  assert.doesNotMatch(codex, /quota out/u, 'one row\'s reset was drawn on another');
});

test('the model card carries its row\'s reset from the cached snapshot, and nothing before a read', () => {
  const gemini = { ...DEFAULT_VENDORS[0]!, id: 'gemini' };
  const withReset = modelsTabHtml({ ...panelState('reviewers'), vendors: [gemini], roundsSnapshot: snapshot(counted(), [refused()]) });
  const without = modelsTabHtml({ ...panelState('reviewers'), vendors: [gemini] });

  assert.match(withReset, /<div class="reset-line">quota out — resets in 89 h \(\w{3} \d{2}:\d{2}\)<\/div>/u);
  assert.doesNotMatch(without, /reset-line/u);
  assert.equal(cardResetLine(undefined, 'gemini'), '', 'a card before the first read claims nothing');
});

let bundle: BundledLog & { usageTabHtml?: (...args: unknown[]) => string; roundsBlock?: (block: RoundsBlockInput) => string };
before(() => { bundle = bundledLogModule({ 'roundsLog.ts': ['usageTabHtml'], 'roundsBlock.ts': ['roundsBlock'] }) as typeof bundle; });

test('the shipped Review rounds page draws the stated reset on its spending tab', () => {
  const page = runningDomPage(bundle);
  const block = bundle.roundsBlock!({
    snapshot: snapshot(counted(), [usageLine(), refused()]), chat: NO_CHAT, rateOf: () => undefined, ranOn: () => '',
    store: { side: '', directory: '/d', others: [] },
  });
  page.message({ type: 'usage', html: bundle.usageTabHtml!([], 'day', [], {}, { rounds: block }) });

  assert.match(page.element('usage-body').innerHTML, /gemini[\s\S]*quota out — resets in 89 h/u, 'the page did not draw the reset the host rendered');
});
