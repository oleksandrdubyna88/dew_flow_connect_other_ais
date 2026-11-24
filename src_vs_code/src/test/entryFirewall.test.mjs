import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { checkAll, checkBundle } from '../../scripts/entry-firewall.mjs';
import { readEntries, ROOT } from '../../scripts/bundle-lib.mjs';

/**
 * The entry firewall is a CI step, so it is tested by FAILING (v2 extension.md § 2, epic E2 story S1 task t3).
 *
 * <p>The real tree must pass: every input of dist/extension.js is one of shell/entry.ts, lazy.ts, ids.ts, measure.ts; its run-time imports are
 * `vscode` and `node:path`; it fits 16 KiB. Then each way of breaking it is PLANTED into the entry's text inside the build (the
 * tree is not touched) and the firewall must report it — `outputs[*].imports` for a builtin, which is external and so absent from
 * `inputs`, the allow-list for a sibling file, the `src/` rule for the old code, and the byte budget. Every one of those is a build
 * that must stay red in CI, which is why they are cases here and not just a script.</p>
 */

const config = readEntries();
const entry = config.bundles.find((bundle) => bundle.id === 'extension');
const source = readFileSync(join(ROOT, entry.entry), 'utf8');

test('the real entry passes the firewall and its budget; the budget is 16 KiB', async () => {
  assert.equal(entry.budgetBytes, 16384);
  assert.deepEqual(await checkAll(config), []);
});

test('a planted require of node:fs fails, and so do child_process, http, https and net', async () => {
  for (const name of ['node:fs', 'fs', 'node:child_process', 'child_process', 'node:http', 'https', 'node:net']) {
    const problems = await checkBundle(entry, config, `${source}\nexport const planted = require('${name}');\n`);

    assert.ok(problems.some((one) => one.includes(`imports ${name}`)), `a planted require('${name}') was not reported: ${JSON.stringify(problems)}`);
  }
});

test('a planted import of the old code under src/ fails, naming src/', async () => {
  const problems = await checkBundle(entry, config, `${source}\nimport { activate as old } from '../src/extension';\nexport const planted = old;\n`);

  assert.ok(problems.some((one) => /input\(s\) under src\//u.test(one)), JSON.stringify(problems));
});

test('a planted import of a shell file outside the allow-list fails', async () => {
  const problems = await checkBundle(entry, config, `${source}\nimport { start } from './modules/legacy';\nexport const planted = start;\n`);

  assert.ok(problems.some((one) => one.includes('not on the allow-list')), JSON.stringify(problems));
});

test('a planted 20 KiB literal fails the byte budget', async () => {
  const noise = Array.from({ length: 4000 }, (_, at) => (at * 2654435761 % 4294967296).toString(36)).join('');
  const problems = await checkBundle(entry, config, `${source}\nexport const planted = '${noise}';\n`);

  assert.ok(problems.some((one) => one.includes('over its budget of 16384')), JSON.stringify(problems));
});

const sidebar = config.bundles.find((bundle) => bundle.id === 'sidebar');
const sidebarSource = readFileSync(join(ROOT, sidebar.entry), 'utf8');

test('the sidebar module is under a firewall and a budget of its own: 96 KiB, the menu\'s small files of src/, and Node\'s crypto only (no fs: it replays no snapshot)', async () => {
  assert.equal(sidebar.budgetBytes, 98304);
  assert.deepEqual(sidebar.firewall.inputs, ['shell/modules/sidebar.ts', 'shell/measure.ts', 'src/sidebarMenu.ts', 'src/panelSurface.ts', 'src/escapeHtml.ts', 'src/asText.ts', 'src/roleTone.ts', 'src/webviewHtml.ts', 'src/webviewNonce.ts']);
  assert.deepEqual(sidebar.firewall.imports, ['vscode', 'node:crypto', 'crypto']);
  assert.deepEqual(await checkBundle(sidebar, config), []);
});

test('the sidebar module may not reach the old code, a process, the network or a second file of src/', async () => {
  const old = await checkBundle(sidebar, config, `${sidebarSource}\nimport { activate as old } from '../../src/extension';\nexport const planted = old;\n`);
  assert.ok(old.some((one) => /input\(s\) under src\//u.test(one)), JSON.stringify(old));
  const panel = await checkBundle(sidebar, config, `${sidebarSource}\nimport { PanelProvider } from '../../src/panelProvider';\nexport const planted = PanelProvider;\n`);
  assert.ok(panel.some((one) => /input\(s\) under src\//u.test(one)), JSON.stringify(panel));
  for (const name of ['node:child_process', 'node:http', 'node:https', 'node:net']) {
    const problems = await checkBundle(sidebar, config, `${sidebarSource}\nexport const planted = require('${name}');\n`);

    assert.ok(problems.some((one) => one.includes(`imports ${name}`)), `a planted require('${name}') was not reported: ${JSON.stringify(problems)}`);
  }
});

test('a planted 100 KiB literal fails the sidebar module\'s budget', async () => {
  const noise = Array.from({ length: 20000 }, (_, at) => (at * 2654435761 % 4294967296).toString(36)).join('');
  const problems = await checkBundle(sidebar, config, `${sidebarSource}\nexport const planted = '${noise}';\n`);

  assert.ok(problems.some((one) => one.includes('over its budget of 98304')), JSON.stringify(problems));
});

// ── The once-per-host state (v2 E4 s4-chat-carried t1, extension.md § 10 row 7) ──

const chatBundle = config.bundles.find((bundle) => bundle.id === 'chat');
const runtimeBundle = config.bundles.find((bundle) => bundle.id === 'runtime');
const chatSource = readFileSync(join(ROOT, chatBundle.entry), 'utf8');

test('the chat module has a byte budget and forbids the runtime state; the runtime module is small and may import only node:crypto', async () => {
  assert.equal(typeof chatBundle.budgetBytes, 'number', 'the chat module has no byte budget');
  assert.deepEqual(chatBundle.forbidInputs, ['src/runtimeState.ts', 'shell/modules/runtime.ts']);
  assert.ok(runtimeBundle.budgetBytes <= 16384, 'the runtime module is not small');
  assert.deepEqual(runtimeBundle.firewall.imports, ['node:crypto', 'crypto']);
  assert.deepEqual(await checkBundle(chatBundle, config), []);
  assert.deepEqual(await checkBundle(runtimeBundle, config), []);
  // Every module bundle forbids the runtime sources; only the runtime bundle itself holds them.
  for (const bundle of config.bundles.filter((one) => one.id !== 'runtime')) {
    assert.deepEqual(bundle.forbidInputs, ['src/runtimeState.ts', 'shell/modules/runtime.ts'], `${bundle.id} does not forbid the runtime state`);
  }
});

test('a planted import of the runtime state into the chat module fails: it would be a second copy of the notify suppressor, the data root and the findings board', async () => {
  const problems = await checkBundle(chatBundle, config, `${chatSource}\nimport { createRuntime } from '../../../src/runtimeState';\nexport const planted = createRuntime;\n`);

  assert.ok(problems.some((one) => one.includes('src/runtimeState.ts') && one.includes('never be bundled twice')), JSON.stringify(problems));
});

test('a planted import of the runtime module into the legacy module fails', async () => {
  const legacy = config.bundles.find((bundle) => bundle.id === 'legacy');
  const text = readFileSync(join(ROOT, legacy.entry), 'utf8');
  const problems = await checkBundle(legacy, config, `${text}\nimport { runtime } from './runtime';\nexport const planted = runtime;\n`);

  assert.ok(problems.some((one) => one.includes('shell/modules/runtime.ts')), JSON.stringify(problems));
});
