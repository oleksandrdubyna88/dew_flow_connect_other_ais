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
 * <p>The real tree must pass: every input of dist/extension.js is one of shell/entry.ts, lazy.ts, ids.ts; its run-time imports are
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
