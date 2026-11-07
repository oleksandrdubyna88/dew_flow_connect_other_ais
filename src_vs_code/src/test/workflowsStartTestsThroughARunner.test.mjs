import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

/**
 * No workflow and no package script starts a test file with a bare `node --test` (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.5, F1-F3; the plan's
 * `sonarcloudRunsThroughTheRunner` is F1's first case). A bare start has no run folder: a test file refuses it (the guard of its first import), so a step that
 * kept one would fail, and the one before the guard would leave its files in the machine's temp. Every start goes through `scripts/run-tests.mjs` or
 * `scripts/run-lib-tests.mjs`, which make the folder, move TEMP, TMP and TMPDIR and fail closed.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.join(here, '..', '..', '..');
const workflows = path.join(repoRoot, '.github', 'workflows');
const read = (...parts) => fs.readFileSync(path.join(repoRoot, ...parts), 'utf8');
const codeLines = (text) => text.split('\n').map((line, index) => ({ line, number: index + 1 })).filter(({ line }) => !/^\s*#/.test(line));

test('F1 sonarcloudRunsThroughTheRunner: no step starts a test with a bare node --test', () => {
  const found = [];
  for (const name of fs.readdirSync(workflows).filter((file) => file.endsWith('.yml'))) {
    for (const { line, number } of codeLines(read('.github', 'workflows', name))) {
      if (/\bnode\s+--test\b/.test(line)) found.push(`.github/workflows/${name}:${number}: ${line.trim()}`);
    }
  }
  const scripts = JSON.parse(read('src_vs_code', 'package.json')).scripts;
  for (const [key, command] of Object.entries(scripts)) {
    if (/\bnode\s+--test\b/.test(command)) found.push(`src_vs_code/package.json scripts.${key}: ${command}`);
  }
  assert.deepEqual(found, [], 'these start tests without a run folder');

  const sonar = read('.github', 'workflows', 'sonarcloud.yml');
  assert.match(sonar, /c8 [^\n]*\bnode scripts\/run-tests\.mjs /, 'the coverage step runs the runner under c8');
});

/** The text of one top-level job of a workflow file. */
function jobText(text, job) {
  const start = text.search(new RegExp(`^  ${job}:\\s*$`, 'm'));
  assert.ok(start >= 0, `job ${job}`);
  const rest = text.slice(start + 1);
  const next = rest.search(/^ {2}[A-Za-z0-9_-]+:\s*$/m);
  return next < 0 ? rest : rest.slice(0, next);
}

const NODE_STEP = 'run: node scripts/run-tests.mjs src/test/leftoversSweep.test.mjs src/test/runFolder.test.mjs src/test/owned.test.mjs src/test/runTests.test.mjs src/test/testLeftovers.test.mjs';
const LIB_STEP = 'run: node scripts/run-lib-tests.mjs scripts/runFolder.test.mjs';

test('F2 the Windows step runs the Node proofs through the runner (and the macOS job runs them too)', () => {
  const ci = read('.github', 'workflows', 'ci.yml');
  for (const job of ['windows', 'macos']) {
    const text = jobText(ci, job);
    assert.ok(text.includes(NODE_STEP), `${job}: the extension proofs`);
    assert.ok(text.includes(LIB_STEP), `${job}: the scripts' proof`);
    assert.match(text, /working-directory: src_vs_code\n\s+run: node scripts\/run-tests\.mjs/, `${job}: the extension step runs in src_vs_code`);
    assert.match(text, /actions\/setup-node@[0-9a-f]{40} # v[0-9.]+\n\s+with:\n\s+node-version: 22/, `${job}: Node 22, pinned`);
  }
});

test('F3 release.yml makes its temp folders in RUNNER_TEMP', () => {
  const bad = codeLines(read('.github', 'workflows', 'release.yml'))
    .filter(({ line }) => /\bmktemp\b/.test(line) && !line.includes('"$RUNNER_TEMP/'))
    .map(({ line, number }) => `release.yml:${number}: ${line.trim()}`);
  assert.deepEqual(bad, []);
});

// The baseline's creation flag (E5 S6, the brief's § 12.4): the ONE creation of `raw-temp-baseline.json` ran in the creation PR with a temporary step, and that step is gone. No workflow keeps the flag,
// or a later run could pass a re-creation. The flag is written in two pieces here: this file is read by the same tools that read the workflows.
test('F4 no workflow holds the creation flag', () => {
  const flag = `--allow-${'create'}`;
  const found = [];
  for (const name of fs.readdirSync(workflows).filter((file) => /\.ya?ml$/u.test(file))) {
    for (const { line, number } of codeLines(read('.github', 'workflows', name))) {
      if (line.includes(flag)) found.push(`.github/workflows/${name}:${number}: ${line.trim()}`);
    }
  }
  assert.deepEqual(found, [], 'the creation step of the baseline is removed once it has run; a standing flag lets a re-creation pass');
  assert.ok(fs.readdirSync(workflows).length > 3, 'the scan above read the workflows');
});
