import assert from 'node:assert/strict';
import test from 'node:test';
import { parseManyFindings } from '../roundsDb';
import { Found, manyCapMs, readManyFindings, RoundKey, Run, WithKeysFile } from '../roundsDbRead';

/**
 * Reading MANY rounds' findings in one spawn — story C2.
 *
 * <p>A bulk export of five hundred rounds used to start five hundred processes. The behaviour these
 * tests pin is what makes one process safe to substitute for them: the answers are matched by KEY
 * rather than by position, anything unanswered is FAILED rather than clean, and exactly one exit
 * code — 64, `unknown argument` — means "this server is older than the mode" and falls back.</p>
 */

/**
 * Three rounds that differ from each other in exactly ONE field at a time.
 *
 * <p>The plan round caught this: the first version of these keys differed in both stage AND number,
 * so a client matching on session+number alone — or on session+stage alone — would still have told
 * them apart, and the reordering test below would have passed while the key was incomplete. Now the
 * first two share a session and a number and differ only in STAGE, and the second and third share a
 * session and a stage and differ only in NUMBER. Nothing but the whole tuple separates them.</p>
 */
const KEYS: readonly RoundKey[] = [
  { sessionId: 's1', stage: 'PlanReview', number: 1 },
  { sessionId: 's1', stage: 'CodeReview', number: 1 },
  { sessionId: 's1', stage: 'CodeReview', number: 2 },
];

/** Records every spawn and every keys file, and answers whatever the test lined up. */
function calls(...answers: readonly { code: number; output: string }[]): {
  run: Run;
  seen: string[][];
  wrote: string[];
  removed: string[];
  keysFile: WithKeysFile;
} {
  const seen: string[][] = [];
  const wrote: string[] = [];
  const removed: string[] = [];
  let at = 0;

  return {
    seen,
    wrote,
    removed,
    run: async (args) => {
      seen.push([...args]);
      const answer = answers[Math.min(at, answers.length - 1)];
      at += 1;

      return answer ?? { code: 1, output: '' };
    },
    keysFile: async (json, use) => {
      wrote.push(json);
      try {
        return await use('/tmp/keys.json');
      } finally {
        removed.push('/tmp/keys.json');
      }
    },
  };
}

function answer(rounds: readonly unknown[]): string {
  return JSON.stringify({ rounds });
}

const ALL_THREE = answer([
  { sessionId: 's1', stage: 'PlanReview', number: 1, known: true, findings: [{ ordinal: 0, title: 'one' }] },
  { sessionId: 's1', stage: 'CodeReview', number: 1, known: true, findings: [] },
  { sessionId: 's1', stage: 'CodeReview', number: 2, known: false, findings: [] },
]);

function states(found: readonly Found[]): string[] {
  return found.map((one) => one.state);
}

test('a selection of any size is ONE spawn, and the keys travel in a file', async () => {
  const { run, seen, wrote, removed, keysFile } = calls({ code: 0, output: ALL_THREE });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.equal(seen.length, 1, 'three rounds must not be three processes');
  assert.deepEqual(seen[0], ['--findings-many', '--keys-file', '/tmp/keys.json']);
  assert.deepEqual(JSON.parse(wrote[0] ?? ''), [
    { session: 's1', stage: 'PlanReview', number: 1 },
    { session: 's1', stage: 'CodeReview', number: 1 },
    { session: 's1', stage: 'CodeReview', number: 2 },
  ]);
  assert.deepEqual(removed, ['/tmp/keys.json'], 'the keys file is taken away again');
  assert.deepEqual(states(found), ['loaded', 'loaded', 'absent']);
  assert.equal(found[0]?.findings[0]?.title, 'one');
});

test('the keys file is removed even when the read throws', async () => {
  const { wrote, removed, keysFile } = calls();
  const angry: Run = async () => {
    throw new Error('the binary went away mid-read');
  };

  await assert.rejects(readManyFindings('coai.exe', KEYS, angry, keysFile));

  assert.equal(wrote.length, 1);
  assert.deepEqual(removed, ['/tmp/keys.json']);
});

test('answers are matched BY KEY, so a reordered reply cannot attach findings to the wrong round', async () => {
  const { run, keysFile } = calls({
    code: 0,
    output: answer([
      { sessionId: 's1', stage: 'CodeReview', number: 2, known: true, findings: [{ ordinal: 0, title: 'third' }] },
      { sessionId: 's1', stage: 'CodeReview', number: 1, known: true, findings: [{ ordinal: 0, title: 'second' }] },
      { sessionId: 's1', stage: 'PlanReview', number: 1, known: true, findings: [{ ordinal: 0, title: 'first' }] },
    ]),
  });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.deepEqual(found.map((one) => one.findings[0]?.title), ['first', 'second', 'third']);
});

test('a round the answer never mentions is FAILED, and never a round that found nothing', async () => {
  const { run, keysFile } = calls({
    code: 0,
    output: answer([
      { sessionId: 's1', stage: 'PlanReview', number: 1, known: true, findings: [] },
      { sessionId: 's1', stage: 'CodeReview', number: 2, known: true, findings: [] },
    ]),
  });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.deepEqual(states(found), ['loaded', 'failed', 'loaded'], 'the missing one keeps its slot');
});

test('a server too old for the mode exits 64, and every round is read the old way', async () => {
  const { run, seen, keysFile } = calls(
    { code: 64, output: 'unknown argument' },
    { code: 0, output: JSON.stringify({ findings: [{ ordinal: 0, title: 'the old road' }] }) },
  );

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.equal(seen.length, 4, 'the batch attempt, then one spawn per round');
  assert.deepEqual(seen[1], [
    '--findings', '--session', 's1', '--stage', 'PlanReview', '--number', '1',
  ]);
  assert.deepEqual(states(found), ['loaded', 'loaded', 'loaded']);
  assert.equal(found[2]?.findings[0]?.title, 'the old road');
});

test('exit 69 does NOT fall back — it comes from a server that knows the mode', async () => {
  const { run, seen, keysFile } = calls({ code: 69, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.equal(seen.length, 1, 'a real failure must not become five hundred spawns failing the same way');
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('a request this server refused (65) is NOT read as an old server, and is not retried', async () => {
  const { run, seen, keysFile } = calls({ code: 65, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.equal(seen.length, 1,
    'EX_DATAERR comes from a binary that knows the mode: falling back would hide a bad request '
    + 'behind five hundred spawns and report a successful export');
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('a database that could not be read is failed for every round, and is not retried', async () => {
  const { run, seen, keysFile } = calls({ code: 74, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, run, keysFile);

  assert.equal(seen.length, 1);
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('an answer nobody can read fails every round rather than clearing them all', async () => {
  const { run, keysFile } = calls({ code: 0, output: '{"rounds": ' });

  assert.deepEqual(states(await readManyFindings('coai.exe', KEYS, run, keysFile)), ['failed', 'failed', 'failed']);
});

test('no server at all is a failed read for every round, and nothing is spawned', async () => {
  const { run, seen, wrote, keysFile } = calls({ code: 0, output: ALL_THREE });

  assert.deepEqual(states(await readManyFindings('', KEYS, run, keysFile)), ['failed', 'failed', 'failed']);
  assert.equal(seen.length, 0);
  assert.equal(wrote.length, 0, 'no keys file is written for a read that cannot happen');
});

test('an empty selection asks nothing and answers nothing', async () => {
  const { run, seen, wrote, keysFile } = calls({ code: 0, output: ALL_THREE });

  assert.deepEqual(await readManyFindings('coai.exe', [], run, keysFile), []);
  assert.equal(seen.length, 0);
  assert.equal(wrote.length, 0);
});

test('the deadline grows with the batch and is still bounded', () => {
  assert.equal(manyCapMs(1), 8_050);
  assert.equal(manyCapMs(1000), 58_000);
  assert.equal(manyCapMs(1_000_000), 120_000, 'proportionate, but never unbounded');
});

test('a batch answer with no rounds array at all is unreadable, not empty', () => {
  assert.equal(parseManyFindings('{"findings": []}'), undefined);
  assert.equal(parseManyFindings('not json'), undefined);
  assert.deepEqual(parseManyFindings('{"rounds": []}'), []);
});

test('a batch answer fills in what it left out, and believes only a real `known`', () => {
  const parsed = parseManyFindings(answer([{ sessionId: 's1', stage: 'CodeReview', number: 4, known: 'yes' }]));

  assert.deepEqual(parsed, [{ sessionId: 's1', stage: 'CodeReview', number: 4, known: false, findings: [] }]);
});
