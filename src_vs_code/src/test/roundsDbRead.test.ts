import assert from 'node:assert/strict';
import test from 'node:test';
import { DEFAULT_LIMIT, readFindings, readLog, Run } from '../roundsDbRead';

/**
 * Reading the rounds database — and the four version pairings of doing so.
 *
 * <p>The extension and `coai-mcp` update separately, so all four happen in the field. The hinge is
 * one flag: a new extension asks with `--paged`, a server too old for it exits 64, and the extension
 * asks again without it. A new server given no `--paged` answers exactly what it answered
 * yesterday.</p>
 */

/** Records every call, and answers whatever the test lined up for it. */
function calls(...answers: readonly { code: number; output: string }[]): { run: Run; seen: string[][] } {
  const seen: string[][] = [];
  let at = 0;

  return {
    seen,
    run: async (args) => {
      seen.push([...args]);
      const answer = answers[Math.min(at, answers.length - 1)];
      at += 1;

      return answer ?? { code: 1, output: '' };
    },
  };
}

const PAGE = JSON.stringify({
  rounds: [{
    repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', number: 1,
    startedUtc: '2026-09-09T10:00:00.000Z', sessionId: 's1', accepted: 1, rejected: 0,
    cursor: '2026-09-09T10:00:00.000Z|7', foundCount: 3,
  }],
  blindSpots: [],
  defended: [],
  totals: { rounds: 250, findings: 3484, accepted: 900, rejected: 2000, gating: 700, tokensIn: 5, tokensOut: 6, costUsd: 1.5 },
});

const OLD_SHAPE = JSON.stringify({
  rounds: [{
    repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', number: 1,
    startedUtc: '2026-09-09T10:00:00.000Z', sessionId: 's1', accepted: 1, rejected: 0,
    findings: [{ ordinal: 0, title: 'it carried them inline', severity: 'Major' }],
  }],
  blindSpots: [],
  defended: [],
});

test('a paged server answers a page, its totals, and no findings at all', async () => {
  const { run, seen } = calls({ code: 0, output: PAGE });

  const log = await readLog('coai-mcp.exe', {}, run);

  assert.deepEqual(seen, [['--log', '--paged', '--limit', String(DEFAULT_LIMIT)]]);
  assert.equal(log.paged, true);
  assert.equal(log.rounds.length, 1);
  assert.equal(log.rounds[0]?.findings.length, 0);
  assert.equal(log.rounds[0]?.foundCount, 3, 'how many it found, without what it found');
  assert.equal(log.rounds[0]?.cursor, '2026-09-09T10:00:00.000Z|7');
  assert.equal(log.totals.rounds, 250, 'counted by the database, not by the array that was sent');
  assert.equal(log.totals.findings, 3484);
});

test('a cursor is passed on, opaquely, and only when there is one', async () => {
  const { run, seen } = calls({ code: 0, output: PAGE });

  await readLog('coai-mcp.exe', { limit: 50, before: '2026-09-09T10:00:00.000Z|7' }, run);

  assert.deepEqual(seen, [
    ['--log', '--paged', '--limit', '50', '--before', '2026-09-09T10:00:00.000Z|7'],
  ]);
});

test('a server too old to page is asked again without the flag, and its answer still renders', async () => {
  // The pairing that matters most: a NEW extension on a machine whose server has not been updated.
  // Exit 64 is `unknown argument`, which is what that server says and all it says.
  const { run, seen } = calls({ code: 64, output: '' }, { code: 0, output: OLD_SHAPE });

  const log = await readLog('coai-mcp.exe', { limit: 300 }, run);

  assert.equal(seen.length, 2);
  assert.deepEqual(seen[1], ['--log', '--limit', '300'], 'the same read it answered yesterday');
  assert.equal(log.paged, false, 'so the page offers no Next button it cannot honour');
  assert.equal(log.rounds[0]?.findings.length, 1, 'the old shape carries them inline');
  assert.equal(log.rounds[0]?.foundCount, 1, 'and the count is what it carries');
});

test('a failure that is not "unknown argument" is an empty log, and is not retried', async () => {
  const { run, seen } = calls({ code: 1, output: 'the database could not be read' });

  const log = await readLog('coai-mcp.exe', {}, run);

  assert.equal(seen.length, 1, 'one refusal is not a reason to ask a second way');
  assert.equal(log.rounds.length, 0);
  assert.equal(log.totals.rounds, 0);
});

test('no server at all is an empty log, and nothing is spawned', async () => {
  const { run, seen } = calls({ code: 0, output: PAGE });

  const log = await readLog('', {}, run);

  assert.deepEqual(seen, []);
  assert.equal(log.rounds.length, 0);
});

// ---------- one round's findings, and the three things "none" can mean ----------

const FOUND = JSON.stringify({
  known: true,
  findings: [
    { ordinal: 0, severity: 'Blocking', title: 'the fan rebuilt its buffer', why: 'O(n squared)' },
    { ordinal: 1, severity: 'Minor', title: 'a name could be clearer', why: 'it reads oddly' },
  ],
});

test('an opened row asks for exactly one round, by the three fields the database keys it with', async () => {
  const { run, seen } = calls({ code: 0, output: FOUND });

  const found = await readFindings('coai-mcp.exe', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

  assert.deepEqual(seen, [['--findings', '--session', 's1', '--stage', 'CodeReview', '--number', '2']]);
  assert.equal(found.state, 'loaded');
  assert.equal(found.findings.length, 2);
  assert.equal(found.findings[0]?.title, 'the fan rebuilt its buffer');
});

test('a round the database has never heard of is ABSENT, which is not "it found nothing"', async () => {
  const { run } = calls({ code: 69, output: '' });

  const found = await readFindings('coai-mcp.exe', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

  assert.equal(found.state, 'absent');
  assert.equal(found.findings.length, 0);
});

test('a read that failed is FAILED, and never a clean round', async () => {
  // The state all three reviewers of the plan round asked for, independently. A timed-out read that
  // answered "no findings" would tell somebody a round was clean because a process did not finish.
  const { run } = calls({ code: -1, output: '' });

  const found = await readFindings('coai-mcp.exe', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

  assert.equal(found.state, 'failed');
});

test('no server installed is a failed read, not an empty round', async () => {
  const { run, seen } = calls({ code: 0, output: FOUND });

  const found = await readFindings('', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

  assert.deepEqual(seen, []);
  assert.equal(found.state, 'failed');
});
