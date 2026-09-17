import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawnSync } from 'node:child_process';

import { parseBugs } from '../roundsDb';
import { mayRank } from '../bugzView';
import { EXITS, outcomeOf, readSummary } from '../bugsSend';

/**
 * The REAL binary's answer, parsed by the REAL reader.
 *
 * <p><b>Why hand-written JSON was not enough.</b> Every other test of this contract feeds `parseBugs`
 * a fixture somebody typed, so both sides can agree perfectly with each other and disagree with what
 * `coai-mcp` actually prints. A field renamed on the server — `unprocessed` to something else — would
 * leave `parseBugs` spreading its defaults and returning `read: true`, and the panel would show a
 * corpus of zero candidates with a Collect button that looks fine. This product has shipped its two
 * halves out of step before; a contract nothing exercises end to end is a contract nobody is holding.
 * (Code round, codex, twice.)</p>
 *
 * <p><b>It skips when the binary is not built</b> rather than failing. The extension suite must run in
 * a checkout where nobody has built the server — that is an ordinary state, not a defect — and a test
 * that goes red for it would be a test people learn to ignore. When the binary IS there, which is
 * every CI run and every local run after a build, it is exercised.</p>
 */

/** The debug build, beside this package. */
function server(): string {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';

  return path.join(process.cwd(), '..', 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);
}

const built = fs.existsSync(server());

test('the real --bugs-json output parses, and says what the panel needs', { skip: built ? false : 'the server is not built' }, () => {
  // A data directory of its own: this asks the binary a question, and it must not be answered from
  // whatever database the person running the suite happens to have.
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-contract-'));
  const answer = spawnSync(server(), ['--bugs-json'], {
    encoding: 'utf8',
    env: { ...process.env, COAI_DATA_DIR: data },
    timeout: 60_000,
  });

  assert.equal(answer.status, 0, `--bugs-json exited ${answer.status}: ${answer.stderr}`);

  const corpus = parseBugs(answer.stdout);

  assert.ok(corpus.read, 'the panel could not understand what the server printed');

  // The fields the section actually renders. Named one by one rather than deep-equalled, because a
  // field ADDED to the server is not a breakage and must not fail this.
  assert.equal(typeof corpus.funnel.unprocessed, 'number', 'the waiting count drives the first line');
  assert.equal(typeof corpus.funnel.collected, 'number', 'and the corpus size gates the Review button');
  assert.equal(typeof corpus.lastRun.state, 'string');
  assert.equal(typeof corpus.lastRun.collected, 'number');
  assert.equal(corpus.lastRun.id, '', 'a fresh data directory has no runs');

  // The allowlist rides along, and what it says must be usable by the picker's own filter.
  assert.ok(corpus.rankingVendors.length > 0, 'the server should say which vendors may rank');
  for (const vendor of corpus.rankingVendors) {
    assert.ok(mayRank(`${vendor}/some-model`),
      `the server allows '${vendor}' but the panel would filter it out`);
  }

  fs.rmSync(data, { recursive: true, force: true });
});

/**
 * And the REAL summary, read by the REAL reader.
 *
 * <p>The same argument as the test above, for the other half of the same boundary: every other test
 * of the send feeds `readSummary` a fixture somebody typed, so both sides can agree perfectly with
 * each other and disagree with what `coai-mcp` actually prints. A property renamed in `UploadSummary`
 * would leave the TypeScript spreading its defaults and the section reporting "0 sent" for a send
 * that worked. A plan reviewer named exactly this, and this product has shipped its two halves out
 * of step before.</p>
 *
 * <p><b>A fresh data directory sends nothing</b>, which is the point: the run still prints its
 * summary, so the CONTRACT is exercised without a server, a key, or a pair. The address is one that
 * cannot resolve and is never reached — the run ends before it would need to.</p>
 */
test('the real --upload-pairs summary is the one the panel reads', { skip: built ? false : 'the server is not built' }, () => {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-send-contract-'));
  const answer = spawnSync(server(), ['--upload-pairs', '--server', 'https://bugs.invalid'], {
    encoding: 'utf8',
    // The key is never an argument, here least of all: this is the contract test for the path that
    // carries one.
    env: { ...process.env, COAI_DATA_DIR: data, COAI_BUGS_KEY: 'a-key-for-a-run-that-sends-nothing' },
    timeout: 60_000,
  });

  assert.equal(answer.status, EXITS.fine,
    `--upload-pairs exited ${answer.status}: ${answer.stderr}`);

  const summary = readSummary(answer.stdout);

  assert.ok(summary !== undefined,
    `the panel could not read what the server printed: ${JSON.stringify(answer.stdout)}`);
  // Named one by one rather than deep-equalled: a field ADDED to the server is not a breakage.
  assert.equal(typeof summary.offered, 'number', 'the denominator the button shows');
  assert.equal(typeof summary.accepted, 'number', 'what "sent" means in the section');
  assert.equal(typeof summary.duplicate, 'number', 'already held is a success and is counted apart');
  assert.equal(typeof summary.refused, 'number');
  assert.equal(typeof summary.trouble, 'string', 'the sentence the failure face renders');
  assert.equal(summary.offered, 0, 'a fresh data directory has nothing to send');

  // And the whole outcome, so the words a person reads are produced from the real thing.
  const outcome = outcomeOf(answer.status ?? 0, answer.stdout);
  assert.equal(outcome.kind, 'sent');
  assert.match(outcome.said, /Nothing was waiting/u);

  fs.rmSync(data, { recursive: true, force: true });
});

/** An old server exits 64, and that must read as "update it", never as "your pairs were refused". */
test('an unknown mode really does exit 64', { skip: built ? false : 'the server is not built' }, () => {
  const answer = spawnSync(server(), ['--send-the-pairs-please'], { encoding: 'utf8', timeout: 60_000 });

  assert.equal(answer.status, EXITS.tooOld,
    'the extension maps 64 onto "this machine\u2019s coai-mcp is older than sending"');
  assert.equal(outcomeOf(answer.status ?? 0, answer.stdout).kind, 'too-old');
});
