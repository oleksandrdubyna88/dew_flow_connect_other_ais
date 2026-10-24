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
 * A claude row's reviewer safe mode (todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md, E5.S2): two states — on (the
 * default, stored as nothing) and off — kept only on a claude row, sent to coai-mcp only when the binary lists `safeMode`, and switched
 * on the NEW Settings page's model card only (the owner's rule: new controls go on the new page). A setting, not an action.
 */

const row = (extra: Record<string, unknown>): Vendor =>
  vendorsFrom([{ ...DEFAULT_VENDORS[0]!, id: 'claude', runtime: 'claude', model: 'claude-opus-5-5', enabled: true, ...extra }])[0]!;

const crossed = (vendor: Vendor, features: readonly string[]): unknown =>
  (JSON.parse(vendorsEnv([vendor], '0.50.0', () => undefined, features)) as Record<string, unknown>[])[0]?.['safeMode'];

function stateWith(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): PanelState {
  return {
    ...panelState('reviewers'),
    vendors,
    server: { kind: 'known', version: '0.50.0', remembered: false, updateOffered: false },
    ...overrides,
  };
}

/** The safe-mode selects the running page holds, by the row each writes. */
const selects = (page: ReturnType<typeof runPanel>): readonly string[] =>
  page.controls.filter((one) => one.dataset['setting'] === 'safeMode').map((one) => one.dataset['vendor'] ?? '');

test('a claude row keeps Off; On, the default, keeps nothing; no other runtime has the switch', () => {
  assert.equal(row({ safeMode: 'off' }).safeMode, 'off');
  assert.equal(row({ safeMode: 'on' }).safeMode, undefined, 'On is the default and is stored as nothing');
  assert.equal(row({}).safeMode, undefined);
  assert.equal(row({ safeMode: 'whatever' }).safeMode, undefined, 'a hand edit that is not off reads as the safe default');
  assert.equal(row({ safeMode: 'off', runtime: 'codex', model: 'gpt-6.1-sol' }).safeMode, undefined, 'codex has no such flag');
  assert.equal(row({ safeMode: 'off', runtime: 'antigravity', model: 'gemini-3.7-flash-high' }).safeMode, undefined);
});

test('Off crosses to coai-mcp only when the binary lists safeMode', () => {
  assert.equal(crossed(row({ safeMode: 'off' }), ['safeMode']), 'off');
  assert.equal(crossed(row({ safeMode: 'off' }), []), undefined, 'an older binary would skip the field while the card says it is off');
  assert.equal(crossed(row({}), ['safeMode']), undefined, 'On crosses as nothing — the binary\'s own default is on');
});

test('a claude card draws the select, On by default, and it writes the row; another runtime\'s card has none', () => {
  const codex = row({ id: 'codex', runtime: 'codex', model: 'gpt-6.1-sol' });
  const state = stateWith([row({}), codex], { serverFeatures: ['safeMode'] });
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'models') });
  const control = page.controls.find((one) => one.dataset['setting'] === 'safeMode' && one.dataset['vendor'] === 'claude');

  assert.deepEqual(selects(page), ['claude'], 'one select, on the card of the claude row');
  assert.ok(control !== undefined, 'the running page has no safe-mode select');
  assert.equal(control.value, '', 'On is the default');
  assert.deepEqual(control.options.map((one) => one.value), ['', 'off']);
  control.value = 'off';
  control.fire('change');
  const write = lastWrite(page);
  assert.deepEqual({ key: write['key'], value: write['value'], vendor: write['vendor'] }, { key: 'safeMode', value: 'off', vendor: 'claude' });
});

test('the card says that safe mode also switches off the person\'s own user hooks', () => {
  const state = stateWith([row({})], { serverFeatures: ['safeMode'] });
  const html = catalogHtml(state, 'test-nonce', 'models');

  assert.match(html, /switches off your own user hooks for this reviewer/u);
  assert.match(HELP.safeMode, /USER hooks/u);
  assert.match(HELP.safeMode, /not --bare|NOT --bare/u, 'and why it is not --bare: the browser sign-in');
});

test('a binary that does not take it says so on the card, for a state that is not the default', () => {
  const said = (features: readonly string[] | undefined, vendor = row({ safeMode: 'off' })): string =>
    ignoredSaid(vendor, { installed: true, features }).join(' ');

  assert.match(said(['apiStream']), /does not take its reviewer safe-mode switch yet/u);
  assert.equal(said(undefined), '', 'a cold start is not an older binary');
  assert.equal(said(['safeMode']), '');
  assert.equal(said(['apiStream'], row({})), '', 'On, the default, says nothing');
});

test('the select is new and has its own help', () => {
  assert.ok(NEW_CONTROLS.includes('model.safeMode'));
  assert.match(HELP.safeMode, /safe mode/u);
});
