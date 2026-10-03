import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { test } from 'node:test';

import { runConsultantCheck } from '../consultantCheckRun';
import { parseConsultantsAnswer } from '../consultantHealth';
import { CALLER_KINDS } from '../consultSettings';

/**
 * The Consultant tab's two modes, against the REAL `coai-mcp` this checkout built (E5.T's contract half).
 *
 * <p>The parser and the run are unit-tested against fixtures written from the server's records; this proves the two
 * agree with what the binary actually prints. No model is called: `--consultants` asks each available consultant's
 * CLI for its version, and the check below is refused before anything is launched.</p>
 *
 * <p>It skips when the binary is not built, as `bugzLiveContract.test.ts` does and for its reason: an unbuilt server is
 * an ordinary state of a checkout, and a test that goes red for it is a test people learn to ignore. What it does NOT
 * prove: that an OLD binary answers 64 to `--check-consultant` — there is no old binary here. It proves the two halves
 * of that rule that can be run: a binary that has never heard of a mode exits 64, which the run reads as too old; and
 * a binary that KNOWS the mode refuses a bad request with 65, never 64 (`.agents/PROJECT.md`).</p>
 */

function server(): string {
  const exe = process.platform === 'win32' ? 'coai-mcp.exe' : 'coai-mcp';

  return path.join(process.cwd(), '..', 'src_mcp', 'src', 'bin', 'Debug', 'net10.0', exe);
}

const built = fs.existsSync(server());

function spawnIn(data: string, args: readonly string[]): { code: number; output: string } {
  const ran = spawnSync(server(), [...args], { encoding: 'utf8', env: { ...process.env, COAI_DATA_DIR: data }, timeout: 120_000 });

  return { code: ran.status ?? 1, output: ran.stdout ?? '' };
}

function withData<T>(work: (data: string) => T): T {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-consultants-'));
  try {
    return work(data);
  } finally {
    fs.rmSync(data, { recursive: true, force: true });
  }
}

test('the real --consultants answers every caller kind, with the staleness it publishes', { skip: built ? false : 'the server is not built', timeout: 150_000 }, () => {
  withData((data) => {
    const { code, output } = spawnIn(data, ['--consultants']);
    const answer = parseConsultantsAnswer(code, output);

    assert.equal(answer.kind, 'answered', `the panel could not read the binary's own answer (exit ${code})`);
    const report = answer.kind === 'answered' ? answer.report : undefined;
    assert.deepEqual(report?.consultants.map((row) => row.callerKind), CALLER_KINDS.map((kind) => kind.id),
      'a caller kind the panel draws a row for is missing from the server\'s answer, or the other way round');
    assert.equal(report?.heartbeatStaleAfterSeconds, 60, 'the other side judges a heartbeat by this, and the server stopped publishing it');
  });
});

test('a binary that knows --check-consultant refuses a bad request with 65 — read as a refusal, never as too old', { skip: built ? false : 'the server is not built', timeout: 150_000 }, async () => {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-consultants-'));
  try {
    const result = await runConsultantCheck('', {
      run: (args) => Promise.resolve(spawnIn(data, args)),
      readState: () => Promise.resolve(undefined),
      nowMs: Date.now,
      every: () => () => undefined,
    });

    assert.equal(result.kind, 'refused');
  } finally {
    fs.rmSync(data, { recursive: true, force: true });
  }
});

test('a binary that has never heard of a mode exits 64, and the run reads that as a server too old', { skip: built ? false : 'the server is not built', timeout: 150_000 }, async () => {
  const data = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-consultants-'));
  try {
    // The mode renamed into one this binary cannot know — what an older binary is to --check-consultant.
    const result = await runConsultantCheck('claude', {
      run: (args) => Promise.resolve(spawnIn(data, [`${args[0] ?? ''}-from-a-later-release`, ...args.slice(1)])),
      readState: () => Promise.resolve(undefined),
      nowMs: Date.now,
      every: () => () => undefined,
    });

    assert.deepEqual(result, { kind: 'too-old' });
  } finally {
    fs.rmSync(data, { recursive: true, force: true });
  }
});
