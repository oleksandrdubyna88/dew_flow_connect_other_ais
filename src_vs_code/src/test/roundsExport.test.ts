import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ExportPorts, exportRounds, oneAtATime, suggestedName } from '../roundsExport';
import { LogRow } from '../roundsLog';

/**
 * The three ways an export ends, and the one thing each must not do.
 *
 * <p>Every one of these was raised by the code round over the plan, by three vendors independently,
 * because an undefined ending is how a control ends up stuck on <i>Exporting…</i> and how somebody
 * is told a file was written that is not there.</p>
 */

function row(over: Partial<LogRow> = {}): LogRow {
  return {
    key: 'k1', kind: 'review',
    startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE', status: 'done', decided: null, verdict: 'proceed', gating: 1,
    findings: 1, seconds: 130, decideSeconds: null, tokensIn: null, tokensOut: null,
    costUsd: null, costInUsd: null, costOutUsd: null, costTotalUsd: null,
    costIsEstimate: false, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'], reviewers: [], reviewerColours: [],
    found: [], foundCount: 0, foundState: 'unasked', origin: 'db',
    dbKey: { sessionId: 's1', stage: 'CodeReview', number: 1 },
    ...over,
  };
}

/** The host's three jobs, recorded rather than done. */
function ports(over: Partial<ExportPorts> = {}): ExportPorts & {
  readonly written: { path: string; text: string }[];
  readonly said: string[];
  readonly complained: string[];
} {
  const written: { path: string; text: string }[] = [];
  const said: string[] = [];
  const complained: string[] = [];

  return {
    written, said, complained,
    pickPath: async () => 'D:/out/rounds.csv',
    write: async (path, text) => { written.push({ path, text }); },
    report: (message) => said.push(message),
    reportError: (message) => complained.push(message),
    ...over,
  };
}

test('a written export says how many rounds and where, once', async () => {
  const port = ports();

  const outcome = await exportRounds([row(), row({ key: 'k2' })], port);

  assert.equal(outcome, 'written');
  assert.equal(port.written.length, 1);
  assert.equal(port.written[0]!.path, 'D:/out/rounds.csv');
  assert.match(port.said[0]!, /2 rounds written to D:\/out\/rounds\.csv/);
  assert.deepEqual(port.complained, []);
});

test('one round is called a round, not "1 rounds"', async () => {
  const port = ports();

  await exportRounds([row()], port);

  assert.match(port.said[0]!, /^1 round written/);
});

test('a cancelled dialog writes nothing, says nothing, and is not an error', async () => {
  // The person changed their mind. Reporting a failure would be a lie about their own decision,
  // and reporting success would be a lie about a file.
  const port = ports({ pickPath: async () => undefined });

  const outcome = await exportRounds([row()], port);

  assert.equal(outcome, 'cancelled');
  assert.deepEqual(port.written, []);
  assert.deepEqual(port.said, []);
  assert.deepEqual(port.complained, []);
});

test('a write that throws is reported as a failure and NEVER as a success', async () => {
  // A read-only drive, a file another program holds, a path that stopped existing between the
  // dialog and the write. The one outcome that must be impossible is being told it worked.
  const port = ports({ write: async () => { throw new Error('EACCES: permission denied'); } });

  const outcome = await exportRounds([row()], port);

  assert.equal(outcome, 'failed');
  assert.deepEqual(port.said, [], 'no success message for a file that is not there');
  assert.match(port.complained[0]!, /could not be written to D:\/out\/rounds\.csv/);
  assert.match(port.complained[0]!, /permission denied/, 'and it says what actually failed');
});

test('a rejection that is not an Error still reaches the person in words', async () => {
  const port = ports({ write: async () => { throw 'the host went away'; } });

  await exportRounds([row()], port);

  assert.match(port.complained[0]!, /the host went away/);
});

test('an empty selection opens no dialog at all', async () => {
  let asked = false;
  const port = ports({ pickPath: async () => { asked = true; return 'D:/out/rounds.csv'; } });

  const outcome = await exportRounds([], port);

  assert.equal(outcome, 'cancelled');
  assert.equal(asked, false, 'a save dialog for an empty file is a dialog nobody wants');
});

test('the suggested name carries the day and, for a bulk export, the count', () => {
  const day = new Date(2026, 8, 14);

  assert.equal(suggestedName([row()], day), 'coai-round-2026-09-14.csv');
  assert.equal(suggestedName([row(), row()], day), 'coai-rounds-2026-09-14-2.csv');
});

test('what is written is the CSV of exactly those rounds', async () => {
  const port = ports();

  await exportRounds([row({ subject: 'the one I picked' })], port);

  assert.match(port.written[0]!.text, /the one I picked/);
  assert.equal(port.written[0]!.text.startsWith('\uFEFF'), true);
});

test('a dialog that throws is a failure, not an escaped promise', () => {
  // The plan named three endings and two more could slip between them: a dialog whose window went
  // away, and a serialisation that threw. Either one escaping leaves a control running and a person
  // told nothing at all. (Plan round, codex.)
  const port = ports({ pickPath: async () => { throw new Error('the window went away'); } });

  return exportRounds([row()], port).then((outcome) => {
    assert.equal(outcome, 'failed');
    assert.deepEqual(port.said, []);
    assert.match(port.complained[0]!, /could not be prepared/);
    assert.match(port.complained[0]!, /the window went away/);
  });
});

test('two exports of the same file do not race, and both still answer', async () => {
  // Two buttons clicked in quick succession, the same destination chosen in both dialogs. Without
  // serialisation the two atomic writes race and the file holds whichever finished last while BOTH
  // report success.
  const order: string[] = [];
  const slow = ports({
    write: async (_path, text) => {
      order.push('start:' + text.length);
      await new Promise((done) => setTimeout(done, 10));
      order.push('end:' + text.length);
    },
  });
  const queue = oneAtATime();

  const both = await Promise.all([
    queue(() => exportRounds([row()], slow)),
    queue(() => exportRounds([row(), row({ key: 'k2' })], slow)),
  ]);

  assert.deepEqual(both, ['written', 'written']);
  assert.equal(order.length, 4);
  assert.equal(order[0]!.startsWith('start:'), true);
  assert.equal(order[1]!.startsWith('end:'), true, 'the first write finishes before the second starts');
  assert.equal(order[2]!.startsWith('start:'), true);
});

test('a failed export does not break the queue for the next one', async () => {
  const queue = oneAtATime();
  const broken = ports({ write: async () => { throw new Error('nope'); } });
  const fine = ports();

  const first = await queue(() => exportRounds([row()], broken));
  const second = await queue(() => exportRounds([row()], fine));

  assert.equal(first, 'failed');
  assert.equal(second, 'written', 'one failure must not poison every later export');
});
