import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * The entry scripts (E5 S4, `todo/PLAN_test_leftovers_E5_S4_brief.md` § 4.4, § 6.4, E1-E3): every runner, live-run and measure script owns ONE run folder, made as the
 * FIRST statement after its imports, with TEMP, TMP and TMPDIR moved there (K8). The libraries they import (the twelve `seam-*.mjs` legs, `browserLayout.mjs`) get
 * none: their entry's folder contains them. `run-tests.mjs` and `run-host.mjs` have their own test files.
 *
 * E3 spawns an entry ONLY after E2 proved that its run begins before anything else: the live scripts call paid vendors, and a script that did not start its run
 * first must never be started here.
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const pkg = path.join(here, '..', '..');
const repoRoot = path.join(pkg, '..');
const SELF_TEST = 'COAI_RUNTEMP_SELFTEST';
const MARKER = 'COAI_TEST_RUN_FOLDER';

const inPkg = (file, what) => ({ file: `src_vs_code/scripts/${file}`, what });
const ENTRIES = Object.freeze([
  inPkg('run-seam.mjs', 'seam'), inPkg('run-contract.mjs', 'contract'), inPkg('run-parity.mjs', 'parity'),
  inPkg('live-agent.mjs', 'live-agent'), inPkg('live-api-settings-compat.mjs', 'compat-api'), inPkg('live-chat.mjs', 'live-chat'),
  inPkg('live-claude-models.mjs', 'live-models'), inPkg('live-close-consult-compat.mjs', 'compat-close'),
  inPkg('live-command-models-compat.mjs', 'compat-cmd-models'), inPkg('live-feature-schema-compat.mjs', 'compat-schema'),
  inPkg('live-feature-vendor-compat.mjs', 'compat-vendor'), inPkg('live-fresh.mjs', 'live-fresh'), inPkg('live-gauge-compat.mjs', 'compat-gauge'),
  inPkg('live-paths-per-side-compat.mjs', 'compat-paths'), inPkg('live-security-settings-compat.mjs', 'compat-security'),
  inPkg('live-vibe-runtime-compat.mjs', 'compat-vibe'), inPkg('measure-stream.mjs', 'measure-stream'), inPkg('measure-image.mjs', 'measure-image'),
  inPkg('measure-append.mjs', 'measure-append'), inPkg('measure-consultant-skew.mjs', 'measure-skew'), inPkg('measure-help-clicks.mjs', 'measure-clicks'),
  inPkg('measure-failure-layout.mjs', 'measure-failure'), inPkg('measure-answer-code-layout.mjs', 'measure-code'), inPkg('render-page.mjs', 'render-page'),
  { file: 'http/run-contracts.mjs', what: 'http-contracts' },
  { file: 'scripts/live-consult-all.mjs', what: 'live-consult' },
  { file: 'scripts/measure-idle-server.mjs', what: 'measure-idle' },
]);
const OWN_TESTS = new Set(['src_vs_code/scripts/run-tests.mjs', 'src_vs_code/scripts/run-host.mjs']);

test('E1 every runner, live and measure script is in the list', () => {
  const named = fs.readdirSync(path.join(pkg, 'scripts'))
    .filter((name) => /^(run-|live-|measure-).*\.mjs$|^render-page\.mjs$/.test(name))
    .map((name) => `src_vs_code/scripts/${name}`)
    .filter((file) => !OWN_TESTS.has(file));
  const expected = [...named, 'http/run-contracts.mjs', 'scripts/live-consult-all.mjs', 'scripts/measure-idle-server.mjs'].sort();
  assert.deepEqual(ENTRIES.map((entry) => entry.file).sort(), expected);
  for (const { file, what } of ENTRIES) {
    assert.ok(fs.existsSync(path.join(repoRoot, file)), file);
    assert.ok(what.length <= 17, `${file}: \`what\` ${what} is within the reference's budget`);
  }
});

/** The text after the LAST static import (a side-effect import included), past blank lines and comments. */
function firstStatementAfterImports(text) {
  const imports = [...text.matchAll(/^import\s[^;]*;[^\n]*$/gm)];
  const end = imports.length === 0 ? 0 : imports.at(-1).index + imports.at(-1)[0].length;
  return text.slice(end).replace(/^(\s+|\/\/[^\n]*|\/\*[\s\S]*?\*\/)+/, '');
}

function passesE2(entry) {
  const statement = firstStatementAfterImports(fs.readFileSync(path.join(repoRoot, entry.file), 'utf8'));
  return statement.startsWith(`const run = await startOwnedRun({ what: '${entry.what}'`);
}

for (const entry of ENTRIES) {
  test(`E2 ${entry.file} starts its owned run before anything else`, () => {
    const statement = firstStatementAfterImports(fs.readFileSync(path.join(repoRoot, entry.file), 'utf8'));
    assert.ok(statement.startsWith(`const run = await startOwnedRun({ what: '${entry.what}'`), `the first statement after the imports is: ${statement.slice(0, 100)}`);
  });

  test(`E3 ${entry.file}, stopped right after its run began, leaves nothing`, (t) => {
    if (!passesE2(entry)) {
      assert.fail('E2 failed for this entry: it is never spawned here (the live scripts call paid vendors)');
    }
    const root = owned.ownedDir(t, 'e3');
    const env = { ...process.env };
    for (const name of ['NODE_TEST_CONTEXT', MARKER, 'TESTSTODELETE_RUN']) delete env[name];

    const result = spawnSync(process.execPath, [path.join(repoRoot, entry.file)], {
      cwd: path.dirname(path.join(repoRoot, entry.file)), encoding: 'utf8', timeout: 60_000, env: { ...env, TEMP: root, TMP: root, TMPDIR: root, [SELF_TEST]: 'exit-after-begin' },
    });

    assert.equal(result.status, 3, `${result.stdout}${result.stderr}`.slice(0, 1000));
    assert.deepEqual(fs.readdirSync(root), [], 'the run folder and its claim are gone');
    assert.equal(result.stderr, '');
  });
}
