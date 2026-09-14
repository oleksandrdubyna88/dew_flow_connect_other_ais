import assert from 'node:assert/strict';
import test from 'node:test';
import { parseManyFindings } from '../roundsDb';
import { FoundRound, manyCapMs, readManyFindings, RoundKey, Run, WithKeysFile } from '../roundsDbRead';

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

function states(found: readonly FoundRound[]): string[] {
  return found.map((one) => one.found.state);
}

/** Every answer, in the order asked, still naming the round it is about. */
function keysOf(found: readonly FoundRound[]): string[] {
  return found.map((one) => `${one.key.sessionId}|${one.key.stage}|${one.key.number}`);
}

test('a selection of any size is ONE spawn, and the keys travel in a file', async () => {
  const { run, seen, wrote, removed, keysFile } = calls({ code: 0, output: ALL_THREE });

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.equal(seen.length, 1, 'three rounds must not be three processes');
  assert.deepEqual(seen[0], ['--findings-many', '--keys-file', '/tmp/keys.json']);
  assert.deepEqual(JSON.parse(wrote[0] ?? ''), [
    { sessionId: 's1', stage: 'PlanReview', number: 1 },
    { sessionId: 's1', stage: 'CodeReview', number: 1 },
    { sessionId: 's1', stage: 'CodeReview', number: 2 },
  ], 'the keys file names the session exactly as every other side of this seam does');
  assert.deepEqual(removed, ['/tmp/keys.json'], 'the keys file is taken away again');
  assert.deepEqual(states(found), ['loaded', 'loaded', 'absent']);
  assert.deepEqual(keysOf(found), ['s1|PlanReview|1', 's1|CodeReview|1', 's1|CodeReview|2'],
    'every answer names the round it is about, so nobody downstream has to pair by position');
  assert.equal(found[0]?.found.findings[0]?.title, 'one');
});

test('the keys file is removed even when the read throws', async () => {
  const { wrote, removed, keysFile } = calls();
  const angry: Run = async () => {
    throw new Error('the binary went away mid-read');
  };

  await assert.rejects(readManyFindings('coai.exe', KEYS, keysFile, angry));

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

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.deepEqual(found.map((one) => one.found.findings[0]?.title), ['first', 'second', 'third']);
});

test('a round the answer never mentions is FAILED, and never a round that found nothing', async () => {
  const { run, keysFile } = calls({
    code: 0,
    output: answer([
      { sessionId: 's1', stage: 'PlanReview', number: 1, known: true, findings: [] },
      { sessionId: 's1', stage: 'CodeReview', number: 2, known: true, findings: [] },
    ]),
  });

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.deepEqual(states(found), ['loaded', 'failed', 'loaded'], 'the missing one keeps its slot');
});

test('a server too old for the mode exits 64, and every round is read the old way', async () => {
  const { run, seen, keysFile } = calls(
    { code: 64, output: 'unknown argument' },
    { code: 0, output: JSON.stringify({ findings: [{ ordinal: 0, title: 'the old road' }] }) },
  );

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.equal(seen.length, 4, 'the batch attempt, then one spawn per round');
  assert.deepEqual(keysOf(found), ['s1|PlanReview|1', 's1|CodeReview|1', 's1|CodeReview|2'],
    'the fallback answers in the order asked too, however it batched the reads');
  assert.deepEqual(seen[1], [
    '--findings', '--session', 's1', '--stage', 'PlanReview', '--number', '1',
  ]);
  assert.deepEqual(states(found), ['loaded', 'loaded', 'loaded']);
  assert.equal(found[2]?.found.findings[0]?.title, 'the old road');
});

test('exit 69 does NOT fall back — it comes from a server that knows the mode', async () => {
  const { run, seen, keysFile } = calls({ code: 69, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.equal(seen.length, 1, 'a real failure must not become five hundred spawns failing the same way');
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('a request this server refused (65) is NOT read as an old server, and is not retried', async () => {
  const { run, seen, keysFile } = calls({ code: 65, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.equal(seen.length, 1,
    'EX_DATAERR comes from a binary that knows the mode: falling back would hide a bad request '
    + 'behind five hundred spawns and report a successful export');
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('a database that could not be read is failed for every round, and is not retried', async () => {
  const { run, seen, keysFile } = calls({ code: 74, output: '' });

  const found = await readManyFindings('coai.exe', KEYS, keysFile, run);

  assert.equal(seen.length, 1);
  assert.deepEqual(states(found), ['failed', 'failed', 'failed']);
});

test('an answer nobody can read fails every round rather than clearing them all', async () => {
  const { run, keysFile } = calls({ code: 0, output: '{"rounds": ' });

  assert.deepEqual(states(await readManyFindings('coai.exe', KEYS, keysFile, run)), ['failed', 'failed', 'failed']);
});

test('no server at all is a failed read for every round, and nothing is spawned', async () => {
  const { run, seen, wrote, keysFile } = calls({ code: 0, output: ALL_THREE });

  assert.deepEqual(states(await readManyFindings('', KEYS, keysFile, run)), ['failed', 'failed', 'failed']);
  assert.equal(seen.length, 0);
  assert.equal(wrote.length, 0, 'no keys file is written for a read that cannot happen');
});

test('an empty selection asks nothing and answers nothing', async () => {
  const { run, seen, wrote, keysFile } = calls({ code: 0, output: ALL_THREE });

  assert.deepEqual(await readManyFindings('coai.exe', [], keysFile, run), []);
  assert.equal(seen.length, 0);
  assert.equal(wrote.length, 0);
});

test('the old-server fallback reads FOUR at a time, as the release before it did', async () => {
  // Reading them one after another would make the old-server path four times slower than the
  // version this replaced, which is the opposite of "an older server gets last release's
  // behaviour". (Code round, codex.)
  const many: RoundKey[] = Array.from({ length: 8 }, (_at, at) => (
    { sessionId: 's1', stage: 'CodeReview', number: at }));
  let running = 0;
  let most = 0;
  const seen: string[][] = [];
  let first = true;
  const run: Run = async (args) => {
    seen.push([...args]);
    if (first) {
      first = false;

      return { code: 64, output: 'unknown argument' };
    }
    running += 1;
    most = Math.max(most, running);
    await new Promise((done) => setTimeout(done, 5));
    running -= 1;

    return { code: 0, output: JSON.stringify({ findings: [] }) };
  };
  const keysFile: WithKeysFile = (_json, use) => use('/tmp/keys.json');

  const found = await readManyFindings('coai.exe', many, keysFile, run);

  assert.equal(most, 4, 'four in flight, never one and never all eight');
  assert.equal(found.length, 8);
  assert.deepEqual(keysOf(found), many.map((key) => `${key.sessionId}|${key.stage}|${key.number}`));
});

test('a cancelled fallback still answers for every round, and the unread ones are failed', async () => {
  const many: RoundKey[] = Array.from({ length: 8 }, (_at, at) => (
    { sessionId: 's1', stage: 'CodeReview', number: at }));
  let calls = 0;
  let cancelled = false;
  const run: Run = async (args) => {
    if (args[0] === '--findings-many') {
      return { code: 64, output: '' };
    }
    calls += 1;
    if (calls >= 4) {
      cancelled = true;
    }

    return { code: 0, output: JSON.stringify({ findings: [] }) };
  };
  const keysFile: WithKeysFile = (_json, use) => use('/tmp/keys.json');

  const found = await readManyFindings('coai.exe', many, keysFile, run, undefined, () => cancelled);

  assert.equal(found.length, 8, 'a row without an answer is a row the export cannot describe');
  assert.deepEqual(keysOf(found), many.map((key) => `${key.sessionId}|${key.stage}|${key.number}`));
  assert.ok(states(found).includes('failed'), 'what was never read is failed, never clean');
});

test('a known round arriving without an array of findings fails the WHOLE answer', async () => {
  // Normalising it to an empty list would publish that round as clean, which is the one lie the
  // three-state answer exists to prevent. (Code round, codex.)
  const { run, keysFile } = calls({
    code: 0,
    output: answer([
      { sessionId: 's1', stage: 'PlanReview', number: 1, known: true },
      { sessionId: 's1', stage: 'CodeReview', number: 1, known: true, findings: [] },
      { sessionId: 's1', stage: 'CodeReview', number: 2, known: true, findings: [] },
    ]),
  });

  assert.deepEqual(states(await readManyFindings('coai.exe', KEYS, keysFile, run)),
    ['failed', 'failed', 'failed']);
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
