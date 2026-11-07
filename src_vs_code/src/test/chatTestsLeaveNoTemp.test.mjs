import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { owned } from '../../scripts/owned.mjs';

/**
 * A test that makes a temp folder removes it (`todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md`, D6): the extension's four chat test
 * files that did not.
 *
 * <p>Found by reading the machine's temp folder: `coai-chat-store-` (318 folders), `coai-chat-ledger-` (165), `coai-chat-lock-` (160) and
 * `coai-chat-fast-` (159) had never been removed — each test removed the sub-folder it NAMED and never the `mkdtemp` parent that held it,
 * and the fast-mode test called the product's per-process folder without ever removing it.</p>
 *
 * <p>Each file is run as `node --test` in a CHILD whose `TEMP`/`TMP`/`TMPDIR` name a private empty folder, and the folder must be empty
 * afterwards. Since E5 S4 the child's private temp is an owned folder of the wrap (`owned.ownedDir`, removed with its claim by the test's context) and the child is told so — a
 * test file refuses to run outside a run folder, so the child's environment carries the marker the guard reads, with its claim beside it. <b>Limits:</b> it names the four files; a NEW leaking test file is not found by it — the real-temp counts in
 * `research/module_tests.md` are that check. It needs the compiled tests (`npm test` compiles first).</p>
 */

const here = path.dirname(fileURLToPath(import.meta.url));
const compiled = path.join(here, '..', '..', 'out', 'test');
const files = ['chatStoreFile', 'chatStoreLock', 'chatUsageFile', 'aChatRunsOnItsRowsFastMode'];

for (const name of files) {
  test(`${name}.test.js removes every temp folder it makes`, (t) => {
    const file = path.join(compiled, `${name}.test.js`);
    assert.ok(fs.existsSync(file), `${file} is not compiled — run the compile step first`);
    const privateTemp = owned.ownedDir(t, 'chatleak');
    const env = { ...process.env };
    delete env.NODE_TEST_CONTEXT;
    const result = spawnSync(process.execPath, ['--test', file], {
      encoding: 'utf8',
      env: { ...env, TEMP: privateTemp, TMP: privateTemp, TMPDIR: privateTemp, COAI_TEST_RUN_FOLDER: privateTemp },
      timeout: 120_000,
    });

    assert.equal(result.status, 0, `the file's own tests must pass first: ${result.stdout}${result.stderr}`.slice(0, 2000));
    assert.deepEqual(fs.readdirSync(privateTemp), [], `${name} left these in its temp folder`);
  });
}
