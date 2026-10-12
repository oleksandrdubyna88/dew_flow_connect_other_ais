// The http contract suite's verdict (http/verdict.mjs) against the pinned httpyac's OWN exit codes.
//
// httpyac 6.16.7 (http/package.json) sets `process.exitCode = 20` in its `testExitCode` hook when a request's test
// results hold a FAILED assertion, and 10 when a request errors — read off its dist/index.js. The runner used to treat
// only 1 as a failed assertion, so every real contract regression was reported as exit 4, "the suite is
// misconfigured, not the API": the one answer that sends a reader away from the server that broke.
import assert from 'node:assert/strict';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const VERDICT = pathToFileURL(path.resolve(HERE, '..', '..', '..', 'http', 'verdict.mjs')).href;

// The runner's codes are asserted as the LITERAL numbers http/README.md documents (0 pass · 1 contract · 3 environment
// · 4 configuration), never through the module's own constants — a changed constant would otherwise keep this green
// while changing the contract every caller reads (CodeRabbit on #771).
void test('a failed assertion (httpyac exits 20) is a CONTRACT regression, never a misconfigured suite', async () => {
  const { verdictOf } = await import(VERDICT);

  assert.equal(verdictOf(20).code, 1, 'httpyac 6.16.7 exits 20 when a test() assertion failed');
  assert.equal(verdictOf(1).code, 1, 'and 1 stays a contract regression for a version that answered failures with 1');
});

void test('a pass is a pass, a kill is the environment, and an unknown status is the configuration', async () => {
  const { verdictOf } = await import(VERDICT);

  assert.equal(verdictOf(0).code, 0);
  assert.equal(verdictOf(0).message, '');
  assert.equal(verdictOf(null).code, 3);
  assert.equal(verdictOf(2).code, 4);
  assert.match(verdictOf(2).message, /misconfigured/);
});
