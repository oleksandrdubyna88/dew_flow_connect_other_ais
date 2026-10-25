import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { before, test } from 'node:test';

import { modelsTabHtml } from '../modelsTab';
import { StoreReach } from '../storeReach';
import { ranOnOf, roundsBlock, RoundsBlockInput, storeSentence } from '../roundsBlock';
import { LineRate } from '../usage';
import { DEFAULT_VENDORS } from '../vendors';
import { panelState } from './panelPageHarness';
import { BundledLog, bundledLogModule, runningDomPage } from './roundsLogPageHarness';
import { counted, hoursAgo, NO_CHAT, snapshot, UNAVAILABLE, usageLine } from './roundCountsFixtures';

/**
 * The Spending tab's first block, Rounds (story 2.3 of research/PLAN_every_round_is_counted.md), and the Models card's line
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
  assert.match(luna[11]!, /~\$0\.2/u, '$2.016 over the 9 rounds that used luna');
  assert.match(terra[11]!, /per recorded turn|—/u, 'terra has no price: a dash, never ~$0');
});

test('an unavailable read says count unavailable in every count cell and never draws a zero', () => {
  const html = roundsBlock(input({ snapshot: snapshot(UNAVAILABLE) }));
  const cells = rowCells(html, '<b>codex</b>');

  assert.match(html, /Rounds: count unavailable — this coai-mcp is older than --round-counts/u);
  assert.deepEqual(cells.slice(1, 5), ['count unavailable', 'count unavailable', 'count unavailable', 'count unavailable']);
  assert.doesNotMatch(html, /<td>0<\/td>|plan 0|>0 rounds/u, 'an unavailable count was drawn as a zero');
  assert.match(cells[9]!, /in 1\.0M/u, 'the ledger\'s tokens still show — they do not depend on the count');
});

test('AnUnreadableLedger_IsUnavailableSpend_NeverAZero — every spend cell says unavailable; the counts still show', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [], 'EACCES: permission denied, open usage.jsonl') }));
  const row = rowCells(html, '<b>codex</b>');
  const model = rowCells(html, '>gpt-6-luna<');

  assert.match(html, /Tokens and money: unavailable — the usage ledger could not be read: EACCES: permission denied/u);
  assert.deepEqual(row.slice(1, 4), ['2', '5', '14'], 'the counts come from the database, not the ledger');
  assert.deepEqual([row[5], row[9], row[10], row[11]], ['unavailable', 'unavailable', 'unavailable', 'unavailable'],
    'a ledger that would not read was drawn as nothing spent');
  assert.deepEqual(model.slice(8, 12), ['unavailable', 'unavailable', 'unavailable', 'unavailable']);
  assert.doesNotMatch(html, /in 0 ·|~\$0(?!\.)|\$0\.00/u, 'a zero stands in for a ledger that did not read');
});

test('before the first read the block is counting, never zero', () => {
  const html = roundsBlock(input({ snapshot: undefined }));

  assert.match(html, /Counting…/u);
  assert.doesNotMatch(html, /<td>/u);
});

test('consultations, questions and chat sit beside the rounds as recorded turns; consult lines with no id are one consultation each', () => {
  const lines = [usageLine(), usageLine({ model: 'gpt-6-astra', kind: 'consult' }), usageLine({ model: 'gpt-6-astra', kind: 'consult' }), usageLine({ kind: 'question' })];
  const chat = [{ ...usageLine({ model: 'gpt-6-astra' }), kind: 'chat' }];
  const html = roundsBlock(input({ snapshot: snapshot(counted(), lines), chat }));

  assert.equal(rowCells(html, '<b>codex</b>')[5], 'consult 2 · question 1 · chat 1 recorded turns');
  const consult = rowCells(html, '>consult · 2 recorded turns<');
  // E3.S4: two consult lines that carry no consultation id are two units — the older-line reading, said in the label.
  assert.match(consult[11]!, /~\$1\.0\d*<\/span> <span class="hint">per consultation \(some lines per turn\)/u, '$2 over 2 consult turns');
});

test('AProbe_IsItsOwnKind_NeverARound — a --probe-api completion sits beside the rounds as a probe, on no model line', () => {
  // Story 3.3 of research/PLAN_every_round_is_counted.md: `coai-mcp --probe-api --model` now writes a line per paid
  // completion, of kind `probe`. Its money is the row's, but it is not a review — it must show as its own kind.
  const lines = [usageLine(), usageLine({ model: 'gpt-6-astra', kind: 'probe', role: 'json_schema' })];
  const html = roundsBlock(input({ snapshot: snapshot(counted(), lines) }));

  assert.equal(rowCells(html, '<b>codex</b>')[5], 'probe 1 recorded turns', 'the probe spent the row and was not said beside its rounds');
  const probe = rowCells(html, '>probe · 1 recorded turns<');
  assert.match(probe[9]!, /in 1\.0M/u, 'the probe\'s tokens are on its own line');
  assert.doesNotMatch(html, />gpt-6-astra</u, 'a probe of a model is not a round of it — no model line for it');
});

test('AConsultationOnlyWindow_ShowsNoPerRoundFigure — consultations and no rounds: a dash saying why', () => {
  const none = JSON.stringify({ nowUtc: hoursAgo(0), windows: ['5h', '24h', '7d'].map((name) => ({ name, fromUtc: hoursAgo(name === '5h' ? 5 : name === '24h' ? 24 : 168), toUtc: hoursAgo(0), rows: [] })), modelsKnownSince: null });
  const html = roundsBlock(input({ snapshot: snapshot(counted(none), [usageLine({ model: 'gpt-6-astra', kind: 'consult' })]) }));
  const cells = rowCells(html, '<b>codex</b>');

  assert.deepEqual(cells.slice(1, 4), ['0', '0', '0'], 'counted nought is a real 0');
  assert.match(cells[11]!, /title="no rounds in this window">—/u);
});

/** A review line the way coai-mcp 0.48.0 and later writes it: one launch, with the attempt and the runtime it ran on. */
const launchLine = (over: Parameters<typeof usageLine>[0] = {}) => usageLine({ attempt: 'first', cli: 'codex', ...over });

/** The text of the median column's header, whole. */
function medianHeader(html: string): string {
  const header = /<th scope="col">(median output[^<]*)<\/th>/u.exec(html);
  assert.ok(header !== null, 'the block draws no median column header');

  return header[1]!;
}

const medianCellOf = (html: string, model: string): string => rowCells(html, model)[8]!;

test('TheMedianHeader_SaysPerLaunch_WhenEveryMedianIsPerLaunch_AndNoCellRepeatsIt', () => {
  const lines = [launchLine(), launchLine({ attempt: 'retry' }), launchLine({ model: 'gpt-6-astra', attempt: 'repair' })];
  const html = roundsBlock(input({ snapshot: snapshot(counted(), lines) }));

  assert.equal(medianHeader(html), 'median output per launch');
  for (const model of ['>gpt-6-luna<', '>gpt-6-astra<']) {
    assert.match(medianCellOf(html, model), /^\d/u, 'the launch lines have a median');
    assert.doesNotMatch(medianCellOf(html, model), /hint|recorded turn|per launch/u, 'a cell repeated the unit its header already says');
  }
});

test('TheMedianHeader_SaysPerRecordedTurn_WhenNoLineIsALaunch_AndNoCellRepeatsIt — an all Team-server page, and an all pre-0.48.0 page', () => {
  const remote = (over: Parameters<typeof usageLine>[0] = {}) => usageLine({ provider: 'work-codex', attempt: 'first', cli: 'remote', ...over });
  const teamServer = roundsBlock(input({ snapshot: snapshot(counted(), [remote({ model: 'gpt-6-nova' }), remote({ model: 'gpt-6-astra' })]), ranOn: ranOnOf([]) }));
  const older = roundsBlock(input({ snapshot: snapshot(counted(), [usageLine(), usageLine({ model: 'gpt-6-astra' })]) }));

  for (const [html, models] of [[teamServer, ['>gpt-6-nova<', '>gpt-6-astra<']], [older, ['>gpt-6-luna<', '>gpt-6-astra<']]] as const) {
    assert.equal(medianHeader(html), 'median output per recorded turn', 'the header claimed a unit every cell contradicts');
    for (const model of models) {
      assert.match(medianCellOf(html, model), /^\d/u);
      assert.doesNotMatch(medianCellOf(html, model), /hint|recorded turn|per launch/u, 'a cell repeated the unit its header already says');
    }
  }
});

test('TheMedianHeader_IsNeutral_OnAMixedPage_AndEveryCellNamesItsUnit', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine(), usageLine({ model: 'gpt-6-astra' })]) }));

  assert.equal(medianHeader(html), 'median output', 'a mixed page named one unit in its header');
  assert.match(medianCellOf(html, '>gpt-6-luna<'), /^\d[^<]* <span class="hint">per launch<\/span>$/u, 'the launch cell did not name its unit');
  assert.match(medianCellOf(html, '>gpt-6-astra<'), /<span title="[^"]*recorded turn[^"]*">[^<]+<\/span> <span class="hint">per recorded turn<\/span>/u,
    'the older cell did not name its unit, with a tooltip');
});

test('AnOlderLineInAGroup_SaysPerRecordedTurnBesideItsNumber_WhileAnotherGroupIsPerLaunch', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine(), usageLine(), launchLine({ model: 'gpt-6-astra' })]) }));

  assert.match(medianCellOf(html, '>gpt-6-luna<'), /<span title="[^"]*recorded turn[^"]*">[^<]+<\/span> <span class="hint">per recorded turn<\/span>/u,
    'one older line in the group must say per recorded turn beside the number, with a tooltip');
  assert.doesNotMatch(medianCellOf(html, '>gpt-6-astra<'), /recorded turn/u);
});

test('TheMedianHeader_NamesNoUnit_WhenTheLedgerDidNotRead — no median is drawn, so none is claimed', () => {
  // Launch lines ARE in the snapshot beside the failure: usage can read while the probe ledger fails (roundCountsRead.ts), and
  // then the groups exist but no cell is drawn from them. With no lines at all the empty set would pass without the guard.
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine(), launchLine({ attempt: 'retry' })], 'EACCES') }));

  assert.equal(medianHeader(html), 'median output', 'a header claimed a unit for medians that the unreadable ledger did not draw');
});

test('TheMedianHeader_IgnoresAGroupWithNoCapturedLine — a group with no median is not a cell, so it cannot set the header', () => {
  // astra's only line is an older one whose usage was not captured: its group has no median and draws a dash.
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine(), usageLine({ model: 'gpt-6-astra', usageNote: 'ended before usage' })]) }));

  assert.equal(medianHeader(html), 'median output per launch', 'a group with no median set the header to a neutral one');
  assert.equal(medianCellOf(html, '>gpt-6-astra<'), '—');
});

test('TheMedianHeader_IgnoresAGroupWithNoModel — a group that is never drawn must not set the header', () => {
  // A line with an empty model has no model line (modelsOf drops it), so no median cell is drawn for it.
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine(), launchLine({ attempt: 'retry' }), usageLine({ model: '' })]) }));

  assert.equal(medianHeader(html), 'median output per launch', 'an old line with no model made the header neutral for a cell nobody sees');
  assert.doesNotMatch(medianCellOf(html, '>gpt-6-luna<'), /hint/u);
});

test('AModelWithUnknownRounds_AndAMixedWeek_IsDividedPerRecordedTurn — one older line and one launch line, rounds unavailable', () => {
  const lines = [launchLine({ model: 'gpt-6-astra' }), usageLine({ model: 'gpt-6-astra' })];
  const cells = rowCells(roundsBlock(input({ snapshot: snapshot(counted(), lines) })), '>gpt-6-astra<');

  assert.match(cells[11]!, /over 2 recorded turns/u, 'the fallback divided by the wrong unit');
  assert.match(cells[11]!, /<span class="hint">per recorded turn<\/span>/u);
  assert.doesNotMatch(cells[11]!, /launch/u);
});

test('AModelWithUnknownRounds_AndOnlyLaunchLines_IsDividedPerLaunch — the tooltip says launches, never launchs', () => {
  const lines = [launchLine({ model: 'gpt-6-astra' }), launchLine({ model: 'gpt-6-astra', attempt: 'repair' })];
  const cells = rowCells(roundsBlock(input({ snapshot: snapshot(counted(), lines) })), '>gpt-6-astra<');

  assert.match(cells[11]!, /over 2 launches/u);
  assert.match(cells[11]!, /<span class="hint">per launch<\/span>/u);
  assert.doesNotMatch(cells[11]!, /launchs|recorded turn/u);
});

test('ATeamServerLine_StaysPerRecordedTurn_EvenWithItsRowDeleted — the line says it ran on a Team server, whatever the settings say now', () => {
  const remote = (over: Parameters<typeof usageLine>[0] = {}) => usageLine({ provider: 'work-codex', model: 'gpt-6-astra', attempt: 'first', cli: 'remote', ...over });
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [remote(), remote(), launchLine()]), ranOn: ranOnOf([]) }));
  const cells = rowCells(html, '>gpt-6-astra<');

  assert.match(cells[8]!, /<span class="hint">per recorded turn<\/span>/u, 'a Team-server job total was claimed as one launch');
  assert.match(cells[11]!, /<span class="hint">per recorded turn<\/span>/u);
  assert.doesNotMatch(cells[11]!, /launch/u);
});

test('AnAntigravityFollowUp_KeepsItsModelPerRecordedTurn — a follow-up may be cumulative, so one in the group takes median and division back', () => {
  const local = (over: Parameters<typeof usageLine>[0] = {}) => usageLine({ model: 'gpt-6-astra', attempt: 'first', cli: 'antigravity', ...over });
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [local(), local({ attempt: 'followUp' }), launchLine()]), ranOn: ranOnOf([]) }));
  const cells = rowCells(html, '>gpt-6-astra<');

  assert.match(cells[8]!, /<span class="hint">per recorded turn<\/span>/u, 'a possibly cumulative follow-up was claimed as a launch in the median');
  assert.match(cells[11]!, /over 2 recorded turns/u);
  assert.match(cells[11]!, /<span class="hint">per recorded turn<\/span>/u);
  assert.doesNotMatch(cells[11]!, /launch/u);
});

test('ALocalRow_StaysPerLaunch_AfterItsRowIsDeleted — the decision is the lines own, no row is looked up', () => {
  const html = roundsBlock(input({ snapshot: snapshot(counted(), [launchLine({ model: 'gpt-6-astra' })]), ranOn: ranOnOf([]) }));
  const cells = rowCells(html, '>gpt-6-astra<');

  assert.doesNotMatch(cells[8]!, /recorded turn/u);
  assert.match(cells[11]!, /<span class="hint">per launch<\/span>/u);
});

test('TheFootnote_SaysAReviewLineIsOneLaunch — and does not claim it always is', () => {
  const html = roundsBlock(input());

  assert.match(html, /From coai-mcp 0\.48\.0 a review run on this machine writes one line per launch/u);
  assert.match(html, /a Team-server row's line and a follow-up in the same conversation can hold more than one launch/u);
  assert.doesNotMatch(html, /a per-launch figure is not claimed/u);
  assert.doesNotMatch(html, /\balways\b/iu);
});

test('a Team-server row is its own row, labelled ran on its server', () => {
  const cells = rowCells(roundsBlock(input()), '<b>work-codex</b>');

  assert.match(cells[0]!, /ran on work/u);
  assert.deepEqual(cells.slice(1, 4), ['0', '2', '2']);
});

test('TheOtherSidesStore_IsNamedNotOpened — the other store is found by existence alone and named as not included', async () => {
  const asked: string[] = [];
  const directories = ['D:\\coai-win', '\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp'];
  const reach = new StoreReach(async (file) => {
    asked.push(file);

    return file.includes('wsl.localhost');
  });
  await reach.refresh(directories);
  const others = reach.current(directories).filter((one) => one.reach === 'here').map((one) => one.directory);
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

test('the card counts only the consult turns inside the same 7 days as the block (CodeRabbit on #750)', () => {
  // The tail read can start mid-chunk and the snapshot is held for a minute, so the ledger can carry a line older than
  // the window; the block cut it, the card did not, and the two "7 days" figures disagreed.
  const codex = { ...DEFAULT_VENDORS[0]!, id: 'codex' };
  const lines = [usageLine({ kind: 'consult' }), usageLine({ kind: 'consult', utc: hoursAgo(24 * 8) })];
  const html = modelsTabHtml({ ...panelState('reviewers'), vendors: [codex], roundsSnapshot: snapshot(counted(), lines) });

  assert.match(html, /<div class="rounds-line">7 days: 14 rounds · 1 consult turns · 5 h: 2<\/div>/u,
    'a consult turn eight days old was counted in the card\'s 7 days');
});

test('a render asks for the round counts only while the Settings page that draws them is open (CodeRabbit on #750)', () => {
  // The Models cards on the Settings page are the panel's only surface that draws `roundsSnapshot`; the Review rounds
  // page refreshes on its own tick. A render for the sidebar alone used to start a `--round-counts` spawn and a ledger
  // tail read every minute for nothing. Read, not run: `panelProvider.ts` imports `vscode` — comments stripped first,
  // and the WHOLE guard pinned, so the assertion cannot match the prose about it (bugzReviewWiring.test.ts).
  const text = readFileSync(join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//gu, ' ')
    .replace(/(^|[^:])\/\/.*$/gmu, '$1');

  assert.match(text, /if \(this\.settingsTab\.view !== undefined\) \{\s*void this\.refreshRoundCounts\(\);\s*\}/u,
    'a render reads the round counts whether or not any open surface draws them');
  assert.strictEqual(text.match(/void this\.refreshRoundCounts\(\)/gu)?.length, 1, 'an unguarded call is still there beside the guarded one');
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

test('TheConsultLine_SaysPerConsultation — identified consult lines divide by consultations; question and chat stay per recorded turn', () => {
  const lines = [
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'A' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'A' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'B' }),
    usageLine({ kind: 'question' }),
  ];
  const chat = [{ ...usageLine({ model: 'gpt-6-astra' }), kind: 'chat' }];
  const html = roundsBlock(input({ snapshot: snapshot(counted(), lines), chat }));

  const consult = rowCells(html, '>consult · 3 recorded turns<');
  assert.match(consult[11]!, /~\$1\.5\d*<\/span> <span class="hint">per consultation<\/span>/u, '$3 over 2 consultations, not 3 turns');
  assert.match(consult[11]!, /over 2 consultations/u, 'the tooltip names the divisor');
  assert.match(rowCells(html, '>question · 1 recorded turns<')[11]!, /per recorded turn/u);
  assert.match(rowCells(html, '>chat · 1 recorded turns<')[11]!, /per recorded turn/u);
});

test('ACheckLineAndARefusedConsultation_AreNamedInTheTooltip_NotCalledOlderLines', () => {
  const lines = [
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'A' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult', role: 'consult-check' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult', outcome: 'refused before the launch' }),
  ];
  const consult = rowCells(roundsBlock(input({ snapshot: snapshot(counted(), lines) })), '>consult · 3 recorded turns<');

  assert.match(consult[11]!, /per consultation \(some lines per turn\)<\/span>/u);
  assert.match(consult[11]!, /2 lines without a consultation id counted one each/u);
  assert.match(consult[11]!, /consultant checks/u);
  assert.match(consult[11]!, /refused before their launch/u);
  assert.doesNotMatch(consult[11]!, /written before the consultation field existed/u, 'no older line is in the window');
});

test('AMixedConsultWindow_SaysSomeLinesPerTurn_AndTheTooltipSaysHowManyWereCountedOneEach', () => {
  const lines = [
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'A' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult', consultation: 'A' }),
    usageLine({ model: 'gpt-6-astra', kind: 'consult' }),
  ];
  const consult = rowCells(roundsBlock(input({ snapshot: snapshot(counted(), lines) })), '>consult · 3 recorded turns<');

  assert.match(consult[11]!, /per consultation \(some lines per turn\)<\/span>/u);
  assert.match(consult[11]!, /1 line without a consultation id counted one each/u);
  assert.match(consult[11]!, /over 2 consultations/u, 'A is one consultation, the line with no id is another');
});

test('TheFootnote_SaysConsultationsArePerConsultation', () => {
  const html = roundsBlock(input());

  assert.match(html, /Consultations are counted per consultation/u);
  assert.match(html, /Questions and chat are counted in recorded turns/u);
});
