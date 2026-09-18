import assert from 'node:assert/strict';
import test from 'node:test';
import { TOO_OLD_FOR_THE_REAL_METHOD } from '../realMethodView';
import { DEFAULT_LIMIT, readFileAt, readFindings, readLog, readPairs, readRealMethod, Run } from '../roundsDbRead';
import { TOO_OLD_FOR_THE_REVISION } from '../openAtRevision';

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

test('an answer nobody can read is a FAILED read, not a round that found nothing', async () => {
  // Exit 0 with a truncated pipe. Turning that into an empty list would tell somebody a round was
  // clean because a process was killed halfway through writing about it. (Code round, codex.)
  for (const output of ['{"findings": [{"ordi', 'not json at all', '{"rounds":[]}']) {
    const { run } = calls({ code: 0, output });
    const found = await readFindings('coai-mcp.exe', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

    assert.equal(found.state, 'failed', output);
  }
});

test('a database that could not be READ is failed, and is not "no such round"', async () => {
  // 74 is EX_IOERR — the file, not the round. It used to share 69 with "never recorded", which would
  // have told somebody a round was never written down because a file was momentarily locked.
  const { run } = calls({ code: 74, output: '' });

  const found = await readFindings('coai-mcp.exe', { sessionId: 's1', stage: 'CodeReview', number: 2 }, run);

  assert.equal(found.state, 'failed');
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

// --------------------------------------------------------------------------------------------
// The pairs — and the two shapes a server of either age answers with (story 2.1).
// --------------------------------------------------------------------------------------------

/** The nine fields every server has ever sent. */
const NINE = {
  findingId: 7, symbolName: 'GetOrAdd', language: 'CSharp',
  skeletonBefore: 'method_1() { }', skeletonAfter: 'method_1() { lock { } }',
  keep: -1, severity: 'Major', category: 'Reliability', title: 'a race',
};

/** The seven a server from story 2.1 onwards adds. */
const SEVEN = {
  repoPath: 'D:/repo', headSha: 'aaaa111', fixSha: 'bbbb222', file: 'src/Totals.cs', line: 5,
  why: 'it races', fix: 'hold the lock',
};

const pairsOf = (...items: readonly Record<string, unknown>[]): string => JSON.stringify({ items });

test('a server that says where a pair was is read field by field, and the limit travels', async () => {
  const { run, seen } = calls({ code: 0, output: pairsOf({ ...NINE, ...SEVEN }) });

  const read = await readPairs('coai-mcp.exe', 200, run);

  assert.deepEqual(seen, [['--pairs-json', '--limit', '200']]);
  assert.ok(read.ok);
  assert.deepEqual(read.pairs, [{ ...NINE, ...SEVEN }]);
});

/**
 * An older server is a normal Tuesday, not a failure.
 *
 * <p>The two halves ship out of step, and a server too old for the seven fields sends the nine it
 * always did. Nothing is invented for the missing ones: empty text and a line of 0, which the page
 * renders as "none recorded" — and NOT as a malformed page, which is what a reader that required
 * them would have made of every installation with a lagging server.</p>
 */
test('a server too old to say where a pair was still answers a page, with nothing invented', async () => {
  const { run } = calls({ code: 0, output: pairsOf(NINE) });

  const read = await readPairs('coai-mcp.exe', 200, run);

  assert.ok(read.ok, 'an older server is not a failed read');
  assert.deepEqual(read.pairs, [{
    ...NINE, repoPath: '', headSha: '', fixSha: '', file: '', line: 0, why: '', fix: '',
  }]);
});

test('a line that is not a whole non-negative number is no line at all', async () => {
  for (const bad of ['5', -3, 2.5, null, true]) {
    const { run } = calls({ code: 0, output: pairsOf({ ...NINE, ...SEVEN, line: bad }) });

    const read = await readPairs('coai-mcp.exe', 200, run);

    assert.ok(read.ok, String(bad));
    assert.equal(read.pairs[0]?.line, 0, `${String(bad)} was read as a line`);
  }
});

// --------------------------------------------------------------------------------------------
// The real method (story 2.3) — one pair, both commits, and the one exit code that means "update".
// --------------------------------------------------------------------------------------------

const SIDE = { reason: '', source: 'int GetOrAdd() { return _items[key]; }', className: 'Totals', kind: 'method_declaration', startLine: 7, endLine: 15 };
const METHOD = { findingId: 7, language: 'CSharp', name: 'GetOrAdd', reason: '', before: SIDE, after: { ...SIDE, endLine: 18 } };

test('the real method is read field by field, and the id travels', async () => {
  const { run, seen } = calls({ code: 0, output: JSON.stringify(METHOD) });

  const read = await readRealMethod('coai-mcp.exe', 7, run);

  assert.deepEqual(seen, [['--real-method', '--id', '7']]);
  assert.ok(read.ok);
  assert.deepEqual(read.method, METHOD);
});

test('a server too old for the view is told apart from one that could not read it', async () => {
  const { run } = calls({ code: 64, output: 'unknown argument' });

  const read = await readRealMethod('coai-mcp.exe', 7, run);

  assert.equal(read.ok, false);
  assert.equal(read.ok ? '' : read.tooOld, true, '64 is the one code that means "this server does not have the mode"');
  assert.equal(read.ok ? '' : read.why, TOO_OLD_FOR_THE_REAL_METHOD);

  const failed = await readRealMethod('coai-mcp.exe', 7, calls({ code: 74, output: 'the rounds database could not be read' }).run);
  assert.equal(failed.ok, false);
  assert.equal(failed.ok ? '' : failed.tooOld, false, 'any other code is a failure of THIS request, never "update the server"');
  assert.equal(failed.ok ? '' : failed.why, 'the rounds database could not be read');
});

test('a domain reason is a successful read with the reason on the method', async () => {
  const notFound = { ...METHOD, reason: 'pair_not_found', before: { ...SIDE, reason: 'pair_not_found', source: '' }, after: { ...SIDE, reason: 'pair_not_found', source: '' } };
  const { run } = calls({ code: 0, output: JSON.stringify(notFound) });

  const read = await readRealMethod('coai-mcp.exe', 7, run);

  assert.ok(read.ok, 'the request was fine; what the reason says is the page’s to render');
  assert.equal(read.method.reason, 'pair_not_found');
});

test('an answer that is not JSON, or has no sides, is a failed read and not a method', async () => {
  const junk = await readRealMethod('coai-mcp.exe', 7, calls({ code: 0, output: '{ not json' }).run);
  assert.equal(junk.ok, false);
  assert.match(junk.ok ? '' : junk.why, /not JSON/u);

  const halfless = await readRealMethod('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify({ findingId: 7, before: SIDE }) }).run);
  assert.equal(halfless.ok, false, 'a document without an after side is malformed, not an older server');
  assert.match(halfless.ok ? '' : halfless.why, /does not understand/u);
});

test('a line span that is not a whole non-negative number is no span at all', async () => {
  const { run } = calls({ code: 0, output: JSON.stringify({ ...METHOD, before: { ...SIDE, startLine: '7', endLine: -1 } }) });

  const read = await readRealMethod('coai-mcp.exe', 7, run);

  assert.ok(read.ok);
  assert.deepEqual([read.method.before.startLine, read.method.before.endLine], [0, 0]);
});

test('a pair with no id is malformed whatever else it carries, and the read says how many', async () => {
  const noId = Object.fromEntries(Object.entries({ ...NINE, ...SEVEN }).filter(([key]) => key !== 'findingId'));
  const { run } = calls({ code: 0, output: pairsOf({ ...NINE, ...SEVEN }, noId) });

  const read = await readPairs('coai-mcp.exe', 200, run);

  assert.equal(read.ok, false);
  assert.match(read.ok ? '' : read.why, /1 of 2 pairs were malformed/u);
});


test('a real-method document for ANOTHER finding is refused, not rendered', () => {
  // Code round, codex. The reader asked for finding 7 and trusted whatever `findingId` came back.
  // A mismatched or stale answer would then be drawn under the wrong row — which on THIS page means
  // one method's un-anonymised source shown beside another finding's decision buttons.
  const answered = {
    findingId: 8,
    language: 'CSharp',
    name: 'GetOrAdd',
    reason: '',
    before: { reason: '', source: 'int GetOrAdd() { }', className: 'Totals', kind: 'method_declaration', startLine: 7, endLine: 9 },
    after: { reason: '', source: 'int GetOrAdd(int n) { }', className: 'Totals', kind: 'method_declaration', startLine: 7, endLine: 9 },
  };

  return readRealMethod('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify(answered) }).run)
    .then((read) => {
      assert.equal(read.ok, false, 'a document about finding 8 is not an answer about finding 7');
      assert.equal(read.tooOld, false);
    });
});

test('the finding it WAS asked about still reads', () => {
  // The companion: refusing everything would pass the test above.
  const answered = {
    findingId: 7,
    language: 'CSharp',
    name: 'GetOrAdd',
    reason: '',
    before: { reason: '', source: 'a', className: 'Totals', kind: 'method_declaration', startLine: 1, endLine: 2 },
    after: { reason: '', source: 'b', className: 'Totals', kind: 'method_declaration', startLine: 1, endLine: 2 },
  };

  return readRealMethod('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify(answered) }).run)
    .then((read) => {
      assert.equal(read.ok, true);
    });
});

// ---------- the file at its revision (story 3.1), and the same three things "no" can mean ----------

const FILE_AT = {
  findingId: 7,
  sha: 'aaaa111bbbb2222cccc3333dddd4444eeee5555f',
  path: 'src/Totals.cs',
  reason: '',
  text: 'public class Totals { }\n',
};

test('the file at its revision is read field by field, by the mode and the id the row names', async () => {
  const { run, seen } = calls({ code: 0, output: JSON.stringify(FILE_AT) });

  const read = await readFileAt('coai-mcp.exe', 7, run);

  assert.deepEqual(seen, [['--file-at', '--id', '7']], 'one mode, one id — the row supplies the rest server-side');
  assert.equal(read.ok, true);
  assert.deepEqual(read.ok ? read.file : undefined, FILE_AT);
});

test('a server too old to open a file at its revision is told apart from one that could not read it', async () => {
  const old = await readFileAt('coai-mcp.exe', 7, calls({ code: 64, output: 'unknown argument' }).run);
  assert.equal(old.ok, false);
  assert.equal(old.ok ? false : old.tooOld, true, '64 is the one code that means "update the server"');
  assert.equal(old.ok ? '' : old.why, TOO_OLD_FOR_THE_REVISION);

  const failed = await readFileAt('coai-mcp.exe', 7, calls({ code: 74, output: 'the rounds database could not be read' }).run);
  assert.equal(failed.ok, false);
  assert.equal(failed.ok ? true : failed.tooOld, false, 'a database that will not read is not an old server');
  assert.match(failed.ok ? '' : failed.why, /could not be read/u);

  const faulted = await readFileAt('coai-mcp.exe', 7, calls({ code: 65, output: '--file-at needs --id' }).run);
  assert.equal(faulted.ok ? true : faulted.tooOld, false, 'a request fault must not send somebody to update a server that is fine');
});

test('a domain reason is a successful read with the reason on the file', async () => {
  const read = await readFileAt('coai-mcp.exe', 7, calls({
    code: 0, output: JSON.stringify({ ...FILE_AT, reason: 'commit_unreachable', text: '' }),
  }).run);

  assert.equal(read.ok, true, 'the request was fine; the answer is what the row says');
  assert.equal(read.ok ? read.file.reason : '', 'commit_unreachable');
  assert.equal(read.ok ? read.file.text : 'x', '');
});

test('a document about another finding, or without the names it will be shown by, is refused', async () => {
  const other = await readFileAt('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify({ ...FILE_AT, findingId: 8 }) }).run);
  assert.equal(other.ok, false, 'a file about finding 8 is not an answer about finding 7');

  const nameless = await readFileAt('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify({ findingId: 7, text: 'x' }) }).run);
  assert.equal(nameless.ok, false, 'without a sha and a path the document cannot be named');

  const junk = await readFileAt('coai-mcp.exe', 7, calls({ code: 0, output: '{ not json' }).run);
  assert.equal(junk.ok, false);
  assert.match(junk.ok ? '' : junk.why, /not JSON/u);
});

test('an empty file is a file, not a failure', async () => {
  const read = await readFileAt('coai-mcp.exe', 7, calls({ code: 0, output: JSON.stringify({ ...FILE_AT, text: '' }) }).run);

  assert.equal(read.ok, true);
  assert.equal(read.ok ? read.file.text : 'x', '');
});
