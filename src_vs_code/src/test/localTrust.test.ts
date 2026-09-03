import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { isLoopback, LocalEngine, OLLAMA_PROBE, openAiBaseOf, remoteWarning } from '../localEngines';
import { panelHtml } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { LOCAL_PRESET, Vendor } from '../vendors';

/**
 * An endpoint somebody typed can be anywhere, and a review prompt is their source code.
 *
 * <p>Found by this product's own gate reviewing the plan for this feature, and it is the finding of
 * the round: the endpoint field is presented as supporting "a box on the network", so somebody can
 * paste any URL — or have one arrive in workspace settings from a repository they cloned — and the
 * whole review prompt is POSTed there. Diffs, file contents, the plan. No confirmation, no
 * indication, nothing in the panel that said the data was leaving the machine.</p>
 *
 * <p>Loopback is the case the feature is FOR and stays silent. Everything else is announced, in the
 * row and once as a modal, and the announcement names what would be sent rather than saying
 * "external endpoint" and leaving somebody to guess what that means.</p>
 */

const LOCAL: LocalEngine = {
  kind: 'ollama', probeUrl: OLLAMA_PROBE, apiBaseUrl: openAiBaseOf(OLLAMA_PROBE),
  reachable: true, status: '0.33.2', models: [{ id: 'qwen:latest', detail: '7B · Q6_K · 6.3 GB' }],
};

function html(vendor: Vendor): string {
  return panelHtml({
    settings: DEFAULTS, vendors: [vendor], codexModels: [], localEngines: { [vendor.id]: LOCAL },
    serverInstalled: false, serverVersion: '', latestServerVersion: '', questions: [], sessions: [],
    openSections: ['reviewers'], usage: [], usageWindow: 'week', cliStatus: {}, modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

test('this machine is this machine, by every name it has', () => {
  for (const url of [
    'http://127.0.0.1:11434/v1',
    'http://localhost:8000/v1',
    'http://[::1]:11434/v1',
    'http://127.5.5.5:1234/v1',
  ]) {
    assert.equal(isLoopback(url), true, url);
  }
});

test('anything else is not, including a plausible-looking neighbour', () => {
  for (const url of [
    'http://192.168.1.50:11434/v1',      // the LAN box the field is advertised for
    'https://api.example.com/v1',
    'http://ollama.internal:11434/v1',
    'http://127.0.0.1.evil.test/v1',     // a hostname that merely starts like one
    'not a url at all',
  ]) {
    assert.equal(isLoopback(url), false, url);
  }
});

test('the warning names what would be sent, not just that something would', () => {
  const warning = remoteWarning('https://api.example.com/v1');

  assert.match(warning, /api\.example\.com/, 'it must name the host');
  assert.match(warning, /diff|source|prompt/i, 'and what leaves the machine');
});

test('a loopback endpoint is silent', () => {
  assert.equal(remoteWarning('http://127.0.0.1:11434/v1'), '');
  assert.equal(remoteWarning(''), '', 'empty means "whatever the probe found", which is local');
});

test('the row announces a remote endpoint', () => {
  const page = html({ ...LOCAL_PRESET, baseUrl: 'https://api.example.com/v1' });

  assert.match(page, /api\.example\.com/);
  // The class is `stale remote`, so an anchored `class="stale"` would not match it — the first
  // version of this assertion failed against correct markup.
  assert.match(page, /class="stale remote"/, 'and it is visible, not only in a tooltip');
});

test('the row says nothing extra for a local endpoint', () => {
  const page = html({ ...LOCAL_PRESET, baseUrl: 'http://127.0.0.1:11434/v1' });

  assert.doesNotMatch(page, /leaves this machine/i);
});

test('a selection the engine no longer lists is marked, not shown as current', () => {
  // Raised by the gate on this plan: dropping it would silently switch the reviewer to another
  // model, and showing it plainly would let a round be sent for a model that 404s.
  const page = html({ ...LOCAL_PRESET, model: 'a-model-that-was-deleted' });

  assert.match(page, /a-model-that-was-deleted — NOT on this engine any more/);
});

test('a selection the engine DOES list is not marked', () => {
  const page = html({ ...LOCAL_PRESET, model: 'qwen:latest' });

  assert.doesNotMatch(page, /NOT on this engine/);
});
