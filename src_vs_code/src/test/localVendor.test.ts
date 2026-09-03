import assert from 'node:assert/strict';
import { test } from 'node:test';
import { LocalEngine, noEngine, OLLAMA_PROBE, openAiBaseOf } from '../localEngines';
import { modelsFor, modelsProvenance } from '../models';
import { panelHtml } from '../panelView';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { DEFAULTS } from '../settingsShape';
import { LOCAL_PRESET, Vendor, VENDOR_PRESETS } from '../vendors';

/**
 * A local engine is a third reviewer in the panel, named `local`, listing the models this machine
 * actually has.
 *
 * <p>The models come from the ENGINE, never from a table shipped here. That is the whole difference
 * from every other vendor: what a person can pick is what is installed on their own machine this
 * minute, and a list compiled at build time would be wrong on every machine including this one.</p>
 */

const ENGINE: LocalEngine = {
  kind: 'ollama',
  probeUrl: OLLAMA_PROBE,
  apiBaseUrl: openAiBaseOf(OLLAMA_PROBE),
  reachable: true,
  status: '0.33.2',
  models: [
    { id: 'qwen2.5-coder-14b-uncensored_64kv:latest', detail: '14.8B · Q6_K · 12.1 GB' },
    { id: 'Qwen3.5-35B-A3B-Q5_vk64:latest', detail: '34.7B · Q5_K_M · 24.8 GB' },
  ],
};

function localVendor(over: Partial<Vendor> = {}): Vendor {
  return { ...LOCAL_PRESET, ...over };
}

function html(vendors: readonly Vendor[], engine: LocalEngine): string {
  return panelHtml({
    settings: DEFAULTS,
    vendors,
    codexModels: [],
    localEngines: Object.fromEntries(vendors.filter((v) => v.runtime === 'local').map((v) => [v.id, engine])),
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    latestServerVersion: '',
    questions: [],
    sessions: [],
    openSections: ['reviewers'],
    openRounds: [],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  }, 'nonce');
}

test('the preset list offers a local engine', () => {
  const local = VENDOR_PRESETS.find((p) => p.runtime === 'local');

  assert.ok(local !== undefined, 'nothing in the Add-a-reviewer list adds one');
  assert.equal(local?.id, 'local');
  // The engines are named in the LABEL, which is what the picker shows; the hint says what it
  // is rather than which servers it speaks to.
  assert.match(local?.label ?? '', /Ollama|vLLM/);
  assert.match(local?.hint ?? '', /no CLI, no key, no bill/);
});

test('the model dropdown lists what the engine reported', () => {
  const page = html([localVendor()], ENGINE);

  assert.match(page, /qwen2\.5-coder-14b-uncensored_64kv:latest/);
  assert.match(page, /Qwen3\.5-35B-A3B-Q5_vk64:latest/);
  assert.match(page, /14\.8B · Q6_K · 12\.1 GB/, 'the detail is what somebody chooses by');
});

test('the hint says which engine answered and how many models', () => {
  const page = html([localVendor()], ENGINE);

  assert.match(page, /ollama 0\.33\.2 · 2 models on this machine/);
});

test('with nothing running the row says where it looked, not nothing', () => {
  const page = html([localVendor()], noEngine('connection refused'));

  assert.match(page, /No local engine answered/);
  assert.match(page, /11434/);
});

test('a local row has no run or install button, and no CLI update', () => {
  // Those are a CLI's — run it, sign in, install it, update it. There is no CLI here, and a dead
  // button is worse than an absent one: this product has already shipped one of each.
  const page = html([localVendor()], ENGINE);
  const row = page.slice(page.indexOf('data-vendor="local"') - 400, page.indexOf('data-command="removeVendor" data-id="local"'));

  assert.doesNotMatch(row, /data-command="runVendor" data-id="local"/);
  assert.doesNotMatch(row, /data-command="installVendorCli" data-id="local"/);
  assert.doesNotMatch(row, /data-command="updateVendorCli" data-id="local"/);
});

test('but it DOES have a re-probe control, because the cache needs a way out', () => {
  // Left out of the first version as "a CLI's button", and the gate reviewing this feature made the
  // point that a cache with no way to clear it is a stale list with no way out: a model you just
  // pulled is not there until the minute expires.
  const page = html([localVendor()], ENGINE);

  assert.match(page, /data-command="reprobeLocal" data-id="local"/);
  assert.match(page, /aria-label="Look for local models again"/);
});

test('the other vendors keep their buttons', () => {
  // The absence above must be about the local runtime, not about the row rendering losing them.
  const codex = VENDOR_PRESETS.find((p) => p.runtime === 'codex')!;
  const page = html([localVendor(), { ...codex }], ENGINE);

  assert.match(page, /data-command="runVendor" data-id="codex"/);
  assert.match(page, /data-command="updateVendorCli" data-id="codex"/);
});

test('the endpoint field is always shown for a local vendor', () => {
  // For every other vendor the base-url field appears only when one is set. A local reviewer is the
  // case where somebody NEEDS to type one — a vLLM on another port, a box on the network — and a
  // field that appears only once it is filled cannot be filled.
  const page = html([localVendor()], noEngine('connection refused'));

  assert.match(page, /data-setting="baseUrl" data-vendor="local"/);
});

test('models for a local runtime come from the engine, not from a shipped list', () => {
  assert.deepEqual(
    modelsFor('local', [], '', ENGINE).map((m) => m.id),
    ENGINE.models.map((m) => m.id),
  );
  assert.deepEqual(modelsFor('local', [], '', noEngine('nothing')), []);
});

test('the provenance line names the engine', () => {
  assert.match(modelsProvenance('local', [], ENGINE), /ollama/);
});
