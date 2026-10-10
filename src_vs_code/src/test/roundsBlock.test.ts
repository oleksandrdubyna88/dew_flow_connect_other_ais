import assert from 'node:assert/strict';
import { before, test } from 'node:test';

import { modelsTabHtml } from '../modelsTab';
import { otherStores } from '../roundCountsRead';
import { roundsBlock, RoundsBlockInput, storeSentence } from '../roundsBlock';
import { LineRate } from '../usage';
import { DEFAULT_VENDORS } from '../vendors';
import { panelState } from './panelPageHarness';
import { BundledLog, bundledLogModule, runningDomPage } from './roundsLogPageHarness';
import { counted, hoursAgo, NO_CHAT, snapshot, UNAVAILABLE, usageLine } from './roundCountsFixtures';

/**
 * The Spending tab's first block, Rounds (story 2.3 of todo/PLAN_every_round_is_counted.md), and the Models card's line
 * (story 2.4): counts per vendor row over three windows and by stage, model lines, the kinds beside, tokens and money
 * with their floors, the store named — and an unavailable read drawn as *count unavailable*, never a zero.
 */

const RATES: Readonly<Record<string, LineRate>> = { 'gpt-6-luna': { in: 2, out: 8 }, 'gpt-6-astra': { in: 1, out: 4 } };
const STORE = { side: 'WSL: Ubuntu', directory: '/home/me/.local/share/coai-mcp', others: [] };

function input(over: Partial<RoundsBlockInput> = {}): RoundsBlockInput {
  return {
    snapshot: snapshot(counted()),
    chat: NO_CHAT,
    rateOf: (_provider, model) => RATES[model],
    ranOn: (provider) => (provider === 'work-codex' ? 'work' : ''),
    store: STORE,
    ...over,
  };
}

/** The cells of the `tr` whose first cell names `label`. */
function rowCells(html: string, label: string): readonly string[] {
  const rows = html.split('<tr').filter((one) => one.includes(label));
  assert.ok(rows.length > 0, `the block has no line named ${label}`);

  return [...rows[0]!.matchAll(/<td>([\s\S]*?)<\/td>/gu)].map((match) => match[1]!);
}

test('a vendor row shows its rounds in 5 h, 24 h and 7 d, its 7 d stages and its reviewers per round', () => {
  const cells = rowCells(roundsBlock(input()), '<b>codex</b>');

  assert.deepEqual(cells.slice(1, 4), ['2', '5', '14'], 'the three windows are not the counts the mode answered');
  assert.equal(cells[4], 'plan 5 · code 8 · document 1 · feature 0');
  assert.equal(cells[6], '3.0', '42 reviewer rows over 14 rounds');
});

test('a model change inside the window is two model lines, each with its own rounds and its own per-round figure', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [usageLine(), usageLine({ model: 'gpt-6-terra' })]) }));
  const luna = rowCells(html, '>gpt-6-luna<');
  const terra = rowCells(html, '>gpt-6-terra<');

  assert.deepEqual(luna.slice(1, 4), ['2', '5', '9']);
  assert.deepEqual(terra.slice(1, 4), ['0', '0', '5'], 'the model the row moved away from vanished, or took the other\'s rounds');
  assert.match(luna[10]!, /~\$0\.2/u, '$2.016 over the 9 rounds that used luna');
  assert.match(terra[10]!, /per recorded turn|—/u, 'terra has no price: a dash, never ~$0');
});

test('an unavailable read says count unavailable in every count cell and never draws a zero', () => {
  const html = roundsBlock(input({ snapshot: snapshot(UNAVAILABLE) }));
  const cells = rowCells(html, '<b>codex</b>');

  assert.match(html, /Rounds: count unavailable — this coai-mcp is older than --round-counts/u);
  assert.deepEqual(cells.slice(1, 5), ['count unavailable', 'count unavailable', 'count unavailable', 'count unavailable']);
  assert.doesNotMatch(html, /<td>0<\/td>|plan 0|>0 rounds/u, 'an unavailable count was drawn as a zero');
  assert.match(cells[8]!, /in 1\.0M/u, 'the ledger\'s tokens still show — they do not depend on the count');
});

test('AnUnreadableLedger_IsUnavailableSpend_NeverAZero — every spend cell says unavailable; the counts still show', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [], 'EACCES: permission denied, open usage.jsonl') }));
  const row = rowCells(html, '<b>codex</b>');
  const model = rowCells(html, '>gpt-6-luna<');

  assert.match(html, /Tokens and money: unavailable — the usage ledger could not be read: EACCES: permission denied/u);
  assert.deepEqual(row.slice(1, 4), ['2', '5', '14'], 'the counts come from the database, not the ledger');
  assert.deepEqual([row[5], row[8], row[9], row[10]], ['unavailable', 'unavailable', 'unavailable', 'unavailable'],
    'a ledger that would not read was drawn as nothing spent');
  assert.deepEqual(model.slice(7, 11), ['unavailable', 'unavailable', 'unavailable', 'unavailable']);
  assert.doesNotMatch(html, /in 0 ·|~\$0(?!\.)|\$0\.00/u, 'a zero stands in for a ledger that did not read');
});

test('before the first read the block is counting, never zero', () => {
  const html = roundsBlock(input({ snapshot: undefined }));

  assert.match(html, /Counting…/u);
  assert.doesNotMatch(html, /<td>/u);
});

test('consultations, questions and chat sit beside the rounds as recorded turns, priced per recorded turn', () => {
  const lines = [usageLine(), usageLine({ model: 'gpt-6-astra', kind: 'consult' }), usageLine({ model: 'gpt-6-astra', kind: 'consult' }), usageLine({ kind: 'question' })];
  const chat = [{ ...usageLine({ model: 'gpt-6-astra' }), kind: 'chat' }];
  const html = roundsBlock(input({ snapshot: snapshot(counted(), lines), chat }));

  assert.equal(rowCells(html, '<b>codex</b>')[5], 'consult 2 · question 1 · chat 1 recorded turns');
  const consult = rowCells(html, '>consult · 2 recorded turns<');
  assert.match(consult[10]!, /~\$1\.0\d*<\/span> <span class="hint">per recorded turn/u, '$2 over 2 consult turns');
});

test('AConsultationOnlyWindow_ShowsNoPerRoundFigure — consultations and no rounds: a dash saying why', () => {
  const none = JSON.stringify({ nowUtc: hoursAgo(0), windows: ['5h', '24h', '7d'].map((name) => ({ name, fromUtc: hoursAgo(name === '5h' ? 5 : name === '24h' ? 24 : 168), toUtc: hoursAgo(0), rows: [] })), modelsKnownSince: null });
  const html = roundsBlock(input({ snapshot: snapshot(counted(none), [usageLine({ model: 'gpt-6-astra', kind: 'consult' })]) }));
  const cells = rowCells(html, '<b>codex</b>');

  assert.deepEqual(cells.slice(1, 4), ['0', '0', '0'], 'counted nought is a real 0');
  assert.match(cells[10]!, /title="no rounds in this window">—/u);
});

test('a Team-server row is its own row, labelled ran on its server', () => {
  const cells = rowCells(roundsBlock(input()), '<b>work-codex</b>');

  assert.match(cells[0]!, /ran on work/u);
  assert.deepEqual(cells.slice(1, 4), ['0', '2', '2']);
});

test('TheOtherSidesStore_IsNamedNotOpened — the other store is found by existence alone and named as not included', () => {
  const asked: string[] = [];
  const others = otherStores(['D:\\coai-win', '\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp'], (file) => {
    asked.push(file);

    return file.includes('wsl.localhost');
  });
  const sentence = storeSentence({ ...STORE, side: '', directory: 'C:\\Users\\me\\AppData\\Local\\coai-mcp', others });

  assert.deepEqual(asked.map((one) => one.endsWith('coai.db')), [true, true], 'something other than the store file\'s existence was asked');
  assert.match(sentence, /^Counted from this side's store only — this machine, C:\\Users\\me\\AppData\\Local\\coai-mcp\./u);
  assert.match(sentence, /wsl\.localhost.*holds another installation's store; its rounds are not included\./u);
  assert.doesNotMatch(sentence, /coai-win/u, 'a directory with no store was named as one');
});

test('the Models card carries its row\'s line from the cached snapshot (story 2.4), and nothing before a read', () => {
  const codex = { ...DEFAULT_VENDORS[0]!, id: 'codex' };
  const withRounds = modelsTabHtml({ ...panelState('reviewers'), vendors: [codex], roundsSnapshot: snapshot(counted(), [usageLine({ kind: 'consult' })]) });
  const without = modelsTabHtml({ ...panelState('reviewers'), vendors: [codex] });
  const unavailable = modelsTabHtml({ ...panelState('reviewers'), vendors: [codex], roundsSnapshot: snapshot(UNAVAILABLE) });

  assert.match(withRounds, /<div class="rounds-line">7 days: 14 rounds · 1 consult turns · 5 h: 2<\/div>/u);
  assert.doesNotMatch(without, /rounds-line/u);
  assert.match(unavailable, /<div class="rounds-line">Rounds: count unavailable<\/div>/u);
});

test('the card line says consult turns are unavailable when the ledger would not read — never 0 consult turns', () => {
  const codex = { ...DEFAULT_VENDORS[0]!, id: 'codex' };
  const html = modelsTabHtml({ ...panelState('reviewers'), vendors: [codex], roundsSnapshot: snapshot(counted(), [], 'EACCES') });

  assert.match(html, /<div class="rounds-line">7 days: 14 rounds · consult turns unavailable · 5 h: 2<\/div>/u);
});

let bundle: BundledLog & { usageTabHtml?: (...args: unknown[]) => string; roundsBlock?: (block: RoundsBlockInput) => string };
before(() => { bundle = bundledLogModule({ 'roundsLog.ts': ['usageTabHtml'], 'roundsBlock.ts': ['roundsBlock'] }) as typeof bundle; });

test('the shipped Review rounds page draws the Rounds block first on its spending tab — counts, and unavailable as words', () => {
  for (const [read, expected, absent] of [
    [counted(), /Rounds[\s\S]*codex[\s\S]*14/u, /count unavailable/u],
    [UNAVAILABLE, /count unavailable/u, /<td>0<\/td>/u],
  ] as const) {
    const page = runningDomPage(bundle);
    const block = bundle.roundsBlock!(input({ snapshot: snapshot(read) }));
    page.message({ type: 'usage', html: bundle.usageTabHtml!([], 'day', [], {}, { rounds: block }) });
    const body = page.element('usage-body');

    assert.match(body.innerHTML, expected, 'the page did not draw the block the host rendered');
    assert.doesNotMatch(body.innerHTML, absent);
    assert.ok(body.innerHTML.indexOf('rounds-block') < body.innerHTML.indexOf('class="windows"'), 'the Rounds block is not the first block');
  }
});
