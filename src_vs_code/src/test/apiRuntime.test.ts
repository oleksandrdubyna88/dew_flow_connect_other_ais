import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { CHAT_RUNTIMES } from '../cliChatLaunch';
import { CONSULTING_RUNTIMES } from '../consultSettings';
import { RUNTIMES, modelsFor, modelsProvenance } from '../models';

// Written as a value rather than as the literal `'api'` at each use, and it was a cast once: before
// RUNTIMES carried the name, the literal was a type error, and a type error is a build that did not
// run — not a test that saw the missing runtime. The RED of 2026-09-25 was observed through this
// value (RUNTIMES without api → false; 0 api presets; the model list falling through to gemini's).
const API = 'api' as const;
import { VENDOR_PRESETS, vendorsFrom } from '../vendors';

/**
 * The `api` runtime on the panel's side (PLAN_feature_review.md, story S1.2 part iv): a hosted
 * OpenAI-compatible endpoint reached directly with a key from the vault — a reviewer, never a chat
 * partner or a consultant in v1.
 *
 * <p>The ground this is written on has already carried two defects of the same shape: a runtime the
 * type knew and the parser did not turned every saved row of it into a codex one, silently, under its
 * own name — once here and once in coai-mcp. So the first thing pinned is the round trip.</p>
 */

const manifest = JSON.parse(readFileSync(join(__dirname, '..', '..', 'package.json'), 'utf8')) as {
  contributes: { configuration: { properties: Record<string, { items?: { properties?: Record<string, { enum?: string[] }> } }> } };
};

test('api is a runtime this build knows, and a saved api row survives being read back', () => {
  assert.ok((RUNTIMES as readonly string[]).includes('api'), 'RUNTIMES has no api — the parser would rewrite it to codex');

  const [vendor] = vendorsFrom([
    { id: 'grok', runtime: 'api', model: 'grok-4', baseUrl: 'https://api.x.ai/v1', enabled: true },
  ]);

  assert.equal(vendor.runtime, 'api', 'an api row that comes back as codex would ride the Codex CLI to a hosted API');
  assert.equal(vendor.baseUrl, 'https://api.x.ai/v1');
});

test('the manifest lets a person write runtime: api in coai.vendors', () => {
  const runtime = manifest.contributes.configuration.properties['coai.vendors']?.items?.properties?.['runtime'];

  assert.ok(runtime?.enum !== undefined, 'coai.vendors[].runtime has no enum in package.json');
  for (const name of RUNTIMES) {
    assert.ok(runtime.enum.includes(name), `package.json refuses runtime "${name}" while the code knows it`);
  }
});

test('api is neither a chat partner nor a consultant', () => {
  assert.ok(!(CHAT_RUNTIMES as readonly string[]).includes('api'), 'the chat speaks to CLIs; an API has no conversation to resume');
  assert.ok(!(CONSULTING_RUNTIMES as readonly string[]).includes('api'), 'coai-mcp refuses an api consultant by name in v1');
});

test('the catalogue offers the generic api preset and the two MEASURED vendor presets, each with its dialect', () => {
  // S1.2 part (iii): a vendor preset arrives only with a dialect row measured on the wire
  // (shared/api-dialects.json, RESULTS_feature_reviewer_models.md, 2026-09-26) — xAI and the Alibaba
  // Model Studio compatible-mode endpoint landed that day; the generic entry stays for any other /v1.
  const api = VENDOR_PRESETS.filter((preset) => preset.runtime === API);

  assert.deepEqual(api.map((preset) => preset.id), ['api', 'grok', 'qwen']);
  const [generic, xai, dashscope] = api as [typeof api[0], typeof api[0], typeof api[0]];
  assert.equal(generic.baseUrl, '', 'the endpoint is the person’s to fill in');
  assert.match(generic.label, /API/u);
  assert.equal(xai.baseUrl, 'https://api.x.ai/v1');
  assert.equal(xai.dialect, 'xai');
  assert.equal(xai.model, 'grok-4.7', 'the MAX model the key offered on 2026-09-26 (GET /models)');
  assert.equal(dashscope.baseUrl, 'https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1',
    'the Token Plan host, not the general dashscope-intl one — Q5 of the plan; data a person can change');
  assert.equal(dashscope.dialect, 'dashscope');
  assert.equal(dashscope.model, 'qwen3.8-max');
  for (const preset of [xai, dashscope]) {
    assert.ok(preset.plan && preset.code, `${preset.id} reviews plans and code like every preset`);
    assert.equal(preset.executablePath, '', 'no CLI in between');
  }
});

test('an api row’s model list is what the person typed — the endpoint is not asked from here', () => {
  assert.deepEqual(modelsFor(API, [{ id: 'gpt-5', label: 'GPT-5' }], 'grok-4').map((m) => m.id), ['grok-4'],
    'the Codex cache is not an API vendor’s list');
  assert.deepEqual(modelsFor(API, [{ id: 'gpt-5', label: 'GPT-5' }], ''), []);
  assert.match(modelsProvenance(API, [{ id: 'gpt-5', label: 'GPT-5' }]), /probe-api/u,
    'the caption tells a person how to learn the ids the key can call');
});
