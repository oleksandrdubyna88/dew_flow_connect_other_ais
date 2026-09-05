import assert from 'node:assert/strict';
import { test } from 'node:test';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { LocalEngine, OLLAMA_PROBE, openAiBaseOf } from '../localEngines';
import { panelHtml } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { LOCAL_PRESET, Vendor, VENDOR_PRESETS } from '../vendors';

/**
 * A local reviewer has no CLI, no key and no bill, and the panel must not tell it that it has.
 *
 * <p>Three sentences were written for vendors that are CLIs and read as nonsense on a local row:
 * "the CLI's default" as the empty option, "Empty means the CLI's own default" in the model
 * tooltip, and a price tooltip asking the person to supply rates for a model nobody charges them
 * for. None of them is a crash, all of them are the product telling somebody something untrue about
 * their own machine.</p>
 */

const ENGINE: LocalEngine = {
  kind: 'ollama', probeUrl: OLLAMA_PROBE, apiBaseUrl: openAiBaseOf(OLLAMA_PROBE),
  reachable: true, status: '0.33.2',
  models: [{ id: 'qwen2.5-coder-14b-uncensored_64kv:latest', detail: '14.8B · Q6_K · 12.1 GB' }],
};

function html(vendors: readonly Vendor[]): string {
  return panelHtml({
    settings: DEFAULTS, vendors, codexModels: [], agyModels: [],
    localEngines: Object.fromEntries(vendors.filter((v) => v.runtime === 'local').map((v) => [v.id, ENGINE])),
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '', latestServerVersion: '', questions: [], sessions: [],
    openSections: ['reviewers'],
    usage: [], usageWindow: 'week', cliStatus: {}, modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

/** The markup of one vendor's row, so another row's wording cannot satisfy an assertion. */
function rowOf(page: string, id: string): string {
  const start = page.indexOf(`data-setting="enabled" data-vendor="${id}"`);
  assert.notEqual(start, -1, `${id} has no row`);
  const end = page.indexOf(`data-command="removeVendor" data-id="${id}"`);

  return page.slice(start - 300, page.indexOf('</div>', end + 2500) + 6);
}

test('a local row does not offer "the CLI\'s default"', () => {
  const row = rowOf(html([{ ...LOCAL_PRESET }]), 'local');

  assert.doesNotMatch(row, /the CLI&#39;s default|the CLI's default/);
  assert.match(row, /whatever the engine answers with/i, 'and says what empty DOES mean');
});

test('a CLI vendor still offers it, because for them it is true', () => {
  const codex = VENDOR_PRESETS.find((p) => p.runtime === 'codex')!;
  const row = rowOf(html([{ ...codex }]), 'codex');

  assert.match(row, /the CLI&#39;s default|the CLI's default/);
});

test('the model tooltip on a local row talks about the engine, not a CLI', () => {
  const row = rowOf(html([{ ...LOCAL_PRESET }]), 'local');
  const title = /data-setting="model" data-vendor="local" title="([^"]*)"/.exec(row)?.[1] ?? '';

  assert.notEqual(title, '');
  assert.doesNotMatch(title, /CLI/);
  assert.match(title, /engine/i);
});

test('the price tooltip on a local row says there is nothing to price', () => {
  // Tokens on your own GPU are electricity, not a bill. Asking somebody to supply a rate "because
  // no public list carries one" is true of a hosted model and absurd of a local one.
  const row = rowOf(html([{ ...LOCAL_PRESET }]), 'local');
  const title = /data-setting="pricePerMillionIn" data-vendor="local"[^>]*title="([^"]*)"/s.exec(row)?.[1] ?? '';

  assert.notEqual(title, '');
  assert.match(title, /no token bill|nothing to bill|your own hardware/i);
});

test('the hosted price tooltip is a sentence', () => {
  // It read "No public list prices this model, so this one has to come from you" — a verb short of
  // English, shipped this morning. Caught by reading the rendered row rather than the source.
  const codex = VENDOR_PRESETS.find((p) => p.runtime === 'codex')!;
  const row = rowOf(html([{ ...codex, model: 'gpt-5.6-luna' }]), 'codex');
  const title = /data-setting="pricePerMillionIn" data-vendor="codex"[^>]*title="([^"]*)"/s.exec(row)?.[1] ?? '';

  assert.doesNotMatch(title, /No public list prices (this model|gpt)/, 'the sentence has no verb');
  assert.match(title, /No public list carries a price/);
});
