import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { HELP } from '../help';
import { ignoredSaid } from '../modelCard';
import { NEW_CONTROLS } from '../newTags';
import { type PanelState } from '../panelView';
import { DEFAULT_VENDORS, vendorsFrom, type Vendor } from '../vendors';
import { vendorsEnv } from '../vendorsWire';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * A row's fast mode (research/PLAN_fast_mode.md, Story B): three states — Off (the default, stored as nothing), On, and
 * "As the CLI is set" — kept only on a row whose runtime and model have a fast tier (the data in
 * shared/feature-availability.json), sent to coai-mcp only when the binary lists `fastMode`, and switched on the NEW
 * Settings page's model card only (the owner's rule: new controls go on the new page).
 */

const row = (extra: Record<string, unknown>): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'codex', runtime: 'codex', model: 'gpt-6.1-sol', enabled: true, ...extra }])[0]!;

const crossed = (vendor: Vendor, features: readonly string[]): unknown =>
  (JSON.parse(vendorsEnv([vendor], '0.50.0', () => undefined, features)) as Record<string, unknown>[])[0]?.['fast'];

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return {
    ...panelState('reviewers'),
    vendors,
    server: { kind: 'known', version: '0.50.0', remembered: false, updateOffered: false },
    ...overrides,
  };
}

/** The fast-mode selects the running page holds, by the row each writes. */
const selects = (page: ReturnType<typeof runPanel>): readonly string[] =>
  page.controls.filter((one) => one.dataset['setting'] === 'fast').map((one) => one.dataset['vendor'] ?? '');

test('a row keeps On or As the CLI is set where it has a tier; Off, or a row without the tier, keeps nothing', () => {
  assert.equal(row({ fast: 'on' }).fast, 'on');
  assert.equal(row({ fast: 'cli' }).fast, 'cli');
  assert.equal(row({ fast: 'off' }).fast, undefined, 'Off is the default and is stored as nothing');
  assert.equal(row({ fast: 'on', baseUrl: 'https://openrouter.example/api/v1' }).fast, undefined, 'a codex row on another endpoint has no codex tier');
  assert.equal(row({ fast: 'on', runtime: 'claude', model: 'claude-opus-5-5' }).fast, 'on');
  assert.equal(row({ fast: 'on', runtime: 'claude', model: 'claude-sonnet-5' }).fast, undefined, 'only an Opus model has the tier');
  assert.equal(row({ fast: 'on', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }).fast, undefined);
});

test('the state crosses to coai-mcp only when the binary lists fastMode', () => {
  assert.equal(crossed(row({ fast: 'on' }), ['fastMode']), 'on');
  assert.equal(crossed(row({ fast: 'cli' }), ['fastMode']), 'cli');
  assert.equal(crossed(row({ fast: 'on' }), []), undefined, 'an older binary would skip the field while the card says it is on');
  assert.equal(crossed(row({}), ['fastMode']), undefined, 'Off crosses as nothing — the binary\'s own default is Off');
});

test('a card with a tier draws the select, Off by default, and it writes the row; a card without one has none', () => {
  const agy = row({ id: 'antigravity', runtime: 'antigravity', model: 'gemini-3.7-flash-high' });
  const state = stateWith([row({}), agy], { serverFeatures: ['fastMode'] });
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });
  const control = page.controls.find((one) => one.dataset['setting'] === 'fast' && one.dataset['vendor'] === 'codex');

  assert.deepEqual(selects(page), ['codex'], 'one select, on the card of the row with a tier');
  assert.ok(control !== undefined, 'the running page has no fast-mode select');
  assert.equal(control.value, '', 'Off is the default');
  assert.deepEqual(control.options.map((one) => one.value), ['', 'on', 'cli']);
  control.value = 'on';
  control.fire('change');
  const write = lastWrite(page);
  assert.deepEqual({ key: write['key'], value: write['value'], vendor: write['vendor'] }, { key: 'fast', value: 'on', vendor: 'codex' });
});

test('a binary that does not take it says so on the card, for a state that is not the default', () => {
  const said = (features: readonly string[] | undefined, vendor = row({ fast: 'on' })): string =>
    ignoredSaid(vendor, { installed: true, features }).join(' ');

  assert.match(said(['apiStream']), /does not take its fast mode yet/u);
  assert.equal(said(undefined), '', 'a cold start is not an older binary');
  assert.equal(said(['fastMode']), '');
  assert.equal(said(['apiStream'], row({})), '', 'Off, the default, says nothing');
});

test('the select is new and has its own help', () => {
  assert.ok(NEW_CONTROLS.includes('model.fast'));
  assert.match(HELP.fastMode, /fast/u);
});
