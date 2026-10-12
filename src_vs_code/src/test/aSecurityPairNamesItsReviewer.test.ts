import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { DEFAULT_SECURITY, type SecurityLane, type SecurityRun } from '../securityLane';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { type PanelState } from '../panelView';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * research/PLAN_names_and_a_missing_root.md, story 1: the Security lane's Reviewer select drew every row by its raw id
 * (`securityLaneView.ts`), a switched-off row had no mark, a `vendor: ''` pair drew an empty option and the warning
 * "Reviewer  is unavailable" with no name in it, and there was no *Offered:* line. It now reads like the question
 * consultant's picker since #748 (`catalogPicks.ts`): the display name with the id beside it, the id as the VALUE, and a
 * pick whose row is GONE says what the server does with it — it drops the pair (`SecurityLaneSetting.cs`, `Pair`).
 */

const ASTRA_ID = 'chat-preset-sec-1';
const astra: Vendor = { ...DEFAULT_VENDORS[0]!, id: ASTRA_ID, name: 'GPT-6-Astra', plan: false, code: false, enabled: true, uses: ['security'] };
const sleepy: Vendor = { ...DEFAULT_VENDORS[1]!, id: 'chat-preset-sec-2', name: 'Sleepy Guard', plan: false, code: false, enabled: false, uses: ['security'] };
/** On Models, but ticked for something else — a pick of it is stranded, not gone. */
const parked: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'chat-preset-old', name: 'Old Luna', plan: false, code: false, uses: ['chat'] };
const ROWS: readonly Vendor[] = [...DEFAULT_VENDORS, astra, sleepy, parked];

const pair = (vendor: string): SecurityRun => ({ vendor, prompt: 'redteam-authz' });

/** The Security lane pane, from the page as it is RUN. */
function paneWith(...runs: readonly SecurityRun[]): PageNode {
  const lane: SecurityLane = { ...DEFAULT_SECURITY, runs: [...runs] };
  const state: PanelState = { ...panelState('securityLane', { settings: { ...DEFAULTS, securityLane: lane } }), catalogRows: ROWS };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'security') });

  return pageTree(page.html).one((node) => node.dataset['pane'] === 'security', 'Security lane pane');
}

/** What the first pair's Reviewer select offers, as a person reads it — value and label — and which value is chosen. */
function reviewerOf(pane: PageNode): { offered: readonly (readonly [string, string])[]; chosen: string; chosenLabel: string } {
  const options = pane.one((node) => node.tagName === 'SELECT' && node.dataset['securityField'] === 'run:0:vendor', 'the first pair\'s Reviewer')
    .find((node) => node.tagName === 'OPTION');
  const selected = options.find((option) => 'selected' in option.attrs);

  return {
    offered: options.map((option) => [option.attrs['value'] ?? '', option.text().trim()] as const),
    chosen: selected?.attrs['value'] ?? '<none selected>',
    chosenLabel: selected?.text().trim() ?? '<none selected>',
  };
}

const pairOf = (pane: PageNode): PageNode => pane.one((node) => node.tagName === 'FIELDSET' && node.find((one) => one.dataset['securityField'] === 'run:0:vendor').length > 0, 'the first pair');

test('a ticked row is named by its display name, with its id as the value; a switched-off row says so', () => {
  const pane = paneWith(pair(ASTRA_ID));
  const { offered, chosen } = reviewerOf(pane);

  assert.ok(offered.some(([value, label]) => value === ASTRA_ID && label === `GPT-6-Astra (${ASTRA_ID})`), `offered: ${JSON.stringify(offered)}`);
  assert.ok(offered.some(([value, label]) => value === sleepy.id && label === `Sleepy Guard (${sleepy.id}) (switched off)`), `offered: ${JSON.stringify(offered)}`);
  assert.equal(chosen, ASTRA_ID);
});

test('a pick of a row on Models but not ticked is kept, marked stranded and named in a sentence', () => {
  const pane = paneWith(pair(parked.id));
  const { offered, chosen } = reviewerOf(pane);

  assert.ok(offered.some(([value, label]) => value === parked.id && label === `Old Luna (${parked.id}) (stranded)`), `offered: ${JSON.stringify(offered)}`);
  assert.equal(chosen, parked.id);
  assert.match(pairOf(pane).text(), /Old Luna \(chat-preset-old\) is not ticked for the security lane on Models/);
});

test('a pick whose row is gone says the server drops the pair — never that the pair keeps asking for it', () => {
  const pane = paneWith(pair('sec-gone'));
  const said = pairOf(pane).text();

  assert.ok(reviewerOf(pane).offered.some(([value, label]) => value === 'sec-gone' && label === 'sec-gone (stranded)'));
  assert.match(said, /sec-gone is no longer on Models\. The server drops this pair until you pick another reviewer\./);
  assert.doesNotMatch(said, /keeps asking for it/);
});

test('a pair that names no reviewer draws no empty option and asks for one', () => {
  const pane = paneWith(pair(''));
  const { offered, chosen, chosenLabel } = reviewerOf(pane);
  const said = pairOf(pane).text();

  assert.ok(offered.every(([, label]) => label.length > 0), `an option with no text: ${JSON.stringify(offered)}`);
  // Selected and saying so: without it the select would SHOW the first row while the pair names none.
  assert.deepEqual([chosen, chosenLabel], ['', 'no reviewer — pick one']);
  assert.doesNotMatch(said, /Reviewer {2}is/);
  assert.match(said, /This pair names no reviewer — pick one\./);
});

test('a pair whose reviewer is switched off names it in the warning', () => {
  assert.match(pairOf(paneWith(pair(sleepy.id))).text(), /Reviewer Sleepy Guard \(chat-preset-sec-2\) is switched off/);
});

test('the pairs carry an Offered line with the way to change it on Models, and the prompt cards name their reviewers', () => {
  const pane = paneWith(pair(ASTRA_ID));
  const offeredLine = pane.one((node) => node.className === 'hint offered', 'the Offered line');
  const tick = pane.find((node) => node.tagName === 'LABEL' && node.find((one) => one.dataset['securityField'] === `pair:${ASTRA_ID}:redteam-authz`).length > 0);

  assert.match(offeredLine.text(), /Offered: the models ticked security lane on Models\./);
  assert.equal(offeredLine.find((node) => node.dataset['modelsUses'] === 'security').length, 1);
  assert.equal(tick.length, 1, 'the prompt card has no tick for the ticked row');
  assert.equal(tick[0]!.text().trim(), `GPT-6-Astra (${ASTRA_ID})`);
});
