import assert from 'node:assert/strict';
import { test } from 'node:test';

import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';

/**
 * The Vendor keys tab, as the Settings page draws it: whether it says a key is needed, and for whom.
 *
 * <p>Reported from a screenshot on 2026-10-02 (todo/PLAN_model_search_and_busy_marks.md, symptom 4): with an
 * `openrouter` row on the Reviewers tab — switched off — this tab read <i>"Every reviewer you have signs in through
 * its own CLI, so none of them needs an API key"</i>. `keysBody` counted only ENABLED rows with a base URL, so a row
 * that cannot work without a key was described as not needing one.</p>
 *
 * <p>The sentence is host-drawn markup, so the test reads the rendered tab — narrowed to the `keys` pane, never the
 * whole page, because another tab could carry the same words.</p>
 */

const OPENROUTER = 'https://openrouter.ai/api/v1';

function codexRow(overrides: Partial<Vendor>): Vendor {
  const codex = DEFAULT_VENDORS.find((one) => one.runtime === 'codex');
  assert.ok(codex !== undefined, 'the shipped vendors carry a codex row');

  return { ...codex, ...overrides };
}

/** The markup of the Vendor keys pane alone, as the Settings tab rendered it for these rows. */
function keysPane(vendors: readonly Vendor[]): string {
  const html = runPanel(panelState('keys', { vendors })).html;
  const panes = html.split('data-pane="').slice(1).filter((one) => one.startsWith('keys"'));
  assert.equal(panes.length, 1, 'the Settings page draws one Vendor keys pane');

  const pane = panes[0] ?? '';

  return pane.slice(0, pane.indexOf('</section>'));
}

test('a switched-off OpenRouter row is named as needing a key once it is switched on', () => {
  const pane = keysPane([codexRow({ id: 'codex' }), codexRow({ id: 'openrouter', baseUrl: OPENROUTER, enabled: false })]);

  assert.doesNotMatch(pane, /Nothing to fill in yet/u, 'a row that cannot run without a key is not "nobody needs one"');
  assert.doesNotMatch(pane, /none of them needs an API key/u);
  assert.match(pane, /openrouter/u, 'the row is named');
  assert.match(pane, /once (it is|they are) switched on/u, 'and said to need the key when it runs, not now');
});

test('an enabled endpoint row still needs its key now, in the sentence it had', () => {
  const pane = keysPane([codexRow({ id: 'openrouter', baseUrl: OPENROUTER, enabled: true })]);

  assert.match(pane, /openrouter reaches an endpoint of its own, so it needs an API key/u);
  assert.match(pane, /Enable Code Access/u, 'and how to mint one');
});

test('with no endpoint row at all there is nothing to fill in', () => {
  const pane = keysPane([codexRow({ id: 'codex' })]);

  assert.match(pane, /Nothing to fill in yet/u);
  assert.match(pane, /placeholder="not needed yet"/u, 'and the box says so too');
});

test('an api row is an endpoint row too, enabled or not', () => {
  const pane = keysPane([codexRow({ id: 'mistral', runtime: 'api', baseUrl: 'https://api.mistral.ai/v1', enabled: false })]);

  assert.doesNotMatch(pane, /Nothing to fill in yet/u);
  assert.match(pane, /mistral/u);
});

test('a local engine with its own address is not asked for a key', () => {
  // The old count was `enabled && baseUrl`, so an Ollama row given the address it listens on was told it "reaches an
  // endpoint of its own, so it needs an API key". A local engine needs none (LOCAL_PRESET: "no CLI, no key, no
  // bill"); the decision is asksAnEndpoint's, which a local row never satisfies.
  const pane = keysPane([codexRow({ id: 'local', runtime: 'local', baseUrl: 'http://127.0.0.1:11434/v1', enabled: true })]);

  assert.match(pane, /Nothing to fill in yet/u);
  assert.doesNotMatch(pane, /local reaches an endpoint of its own/u);
});
