import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { spawnSync } from 'node:child_process';

import { parseBugs } from '../roundsDb';
import { mayRank } from '../bugzView';

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
