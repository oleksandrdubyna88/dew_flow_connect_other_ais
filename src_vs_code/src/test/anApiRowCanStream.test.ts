import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { parseCheckDocument } from '../consultantHealth';
import type { ConsultantHealthState } from '../consultantHealthState';
import { HELP } from '../help';
import { ignoredSaid } from '../modelCard';
import { checkFactsOf } from '../modelCardWorld';
import { NEW_CONTROLS } from '../newTags';
import { settingsHtml, type PanelState } from '../panelView';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * An api row can ask for its answer as a stream (todo/PLAN_api_streaming.md): a `stream` field on the row, sent to
 * coai-mcp only when the installed binary lists `apiStream`, switched on the NEW Settings page's model card — and nowhere
 * on the current page (the owner's rule of 2026-10-06: new controls go on the new page only).
 */

const API_ROW = {
  id: 'qwen', runtime: 'api', model: 'qwen3.8-flash', baseUrl: 'https://token-plan.example/compatible-mode/v1', dialect: 'dashscope',
  enabled: true, plan: true, code: true, executablePath: '', pricePerMillionIn: 0, pricePerMillionOut: 0,
};

const api = (extra: Record<string, unknown> = {}): Vendor => vendorsFrom([{ ...API_ROW, ...extra }])[0]!;
const claude: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'claude', runtime: 'claude', model: 'opus' };

const crossed = (row: Vendor, features: readonly string[]): unknown =>
  (JSON.parse(vendorsEnv([row], '0.50.0', () => undefined, features)) as Record<string, unknown>[])[0]?.['stream'];

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return {
    ...panelState('reviewers'),
    vendors,
    server: { kind: 'known', version: '0.50.0', remembered: false, updateOffered: false },
    ...overrides,
  };
}

/** The stream switches the running page holds, by the row each writes. */
const switches = (page: ReturnType<typeof runPanel>): readonly string[] =>
  page.controls.filter((one) => one.dataset['setting'] === 'stream').map((one) => one.dataset['vendor'] ?? '');

test('the row keeps its stream switch when it says true; false, absent or a CLI row is no switch at all', () => {
  assert.equal(api({ stream: true }).stream, true);
  assert.equal(api({ stream: false }).stream, undefined, 'a switched-off row reads as one that never had it');
  assert.equal(api().stream, undefined);
  assert.equal(vendorsFrom([{ ...API_ROW, runtime: 'claude', stream: true }])[0]!.stream, undefined, 'only an api row streams');
});

test('the switch crosses to coai-mcp only when the binary lists apiStream, and only when it is on', () => {
  assert.equal(crossed(api({ stream: true }), ['apiStream']), true);
  assert.equal(crossed(api({ stream: true }), []), undefined, 'an older binary would skip the field while the card says it is on');
  assert.equal(crossed(api(), ['apiStream']), undefined);
});

test('an api row\'s card draws the switch and it writes the row; a CLI row\'s card has none', () => {
  const state = stateWith([api(), claude], { serverFeatures: ['apiStream'] });
  const html = catalogHtml(state, 'test-nonce', 'models');
  const page = runPanel(state, { html });
  const control = page.controls.find((one) => one.dataset['setting'] === 'stream' && one.dataset['vendor'] === 'qwen');

  assert.deepEqual(switches(page), ['qwen'], 'one switch, on the api row’s card only');
  assert.ok(control !== undefined, 'the running page has no stream switch');
  control.checked = true;
  control.fire('change');
  const write = lastWrite(page);
  assert.deepEqual({ key: write['key'], value: write['value'], vendor: write['vendor'] }, { key: 'stream', value: true, vendor: 'qwen' });
});

test('a binary whose settled list lacks apiStream gets the card\'s skew note; an unsettled list says nothing', () => {
  // The note is the renderer's decision, not the page script's, so it is asserted as the value it is (CodeRabbit on #689).
  const said = (features: readonly string[] | undefined, row = api({ stream: true })): string =>
    ignoredSaid(row, { installed: true, features }).join(' ');

  assert.match(said(['systemPrompt']), /does not take streaming the answer yet/u);
  assert.equal(said(undefined), '', 'a cold start is not an older binary');
  assert.equal(said(['apiStream']), '', 'a binary that takes it is told nothing');
  assert.equal(said(['systemPrompt'], api()), '', 'a row that does not stream says nothing about streams');
});

test('the switch is new, has its own help, and the current Settings page draws no stream control', () => {
  assert.ok(NEW_CONTROLS.includes('model.stream'));
  assert.match(HELP.apiStream, /stream/u);
  const state = stateWith([api({ stream: true })], { serverFeatures: ['apiStream'] });
  assert.deepEqual(switches(runPanel(state, { html: settingsHtml(state, 'test-nonce', 'reviewers') })), []);
});

/** The card's check badge for a row whose last ✓ Check landed with `streamed` as given ('' = the field absent). */
function badgeAfter(streamed: string): { said: string; tone: string } {
  const answered = parseCheckDocument(JSON.stringify({ callerKind: 'model-qwen', state: 'answered', streamed, finishedUtc: '2026-10-06T09:59:00.000Z' }))!;
  const health: ConsultantHealthState = {
    thisSide: { label: 'Windows', probe: { kind: 'asking' }, files: { report: undefined, checks: { 'model-qwen': { kind: 'found', value: answered } } }, checking: [], runs: {} },
    otherSides: [],
    nowMs: Date.parse('2026-10-06T10:00:00.000Z'),
  };
  const facts = checkFactsOf(health, 'qwen');

  return { said: facts.said, tone: facts.tone };
}

test('the check says whether a row that asked to stream got a stream — the plan gate\'s finding 1', () => {
  assert.deepEqual(badgeAfter('streamed'), { said: 'checked: it answered · streamed', tone: 'ok' });

  const ignored = badgeAfter('not-streamed');
  assert.equal(ignored.tone, 'warn', 'a stream asked for and not got reads as fine');
  assert.match(ignored.said, /NOT streamed/u);

  assert.deepEqual(badgeAfter(''), { said: 'checked: it answered', tone: 'ok' }, 'a row that did not ask is told nothing about streams');
});
