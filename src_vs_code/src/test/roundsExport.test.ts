import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ASK_ABOVE, ExportPorts, exportRounds, inBatches, oneAtATime, readAndExport, suggestedName,
} from '../roundsExport';
import { ExportableRow, ExportRound } from '../roundsCsv';
import { LogRow } from '../roundsLog';

/**
 * The three ways an export ends, and the one thing each must not do.
 *
 * <p>Every one of these was raised by the code round over the plan, by three vendors independently,
 * because an undefined ending is how a control ends up stuck on <i>Exporting…</i> and how somebody
 * is told a file was written that is not there.</p>
 */

/**
 * A row as the page sends it.
 *
 * <p>Built as a `LogRow` first and handed out as an `ExportableRow`: the compiler still checks the
 * real shape, so adding a field to `LogRow` is a red build here, while the value handed to the CSV
 * writer has the same untyped shape a webview message actually has.</p>
 */
function row(over: Partial<LogRow> = {}): ExportableRow {
  const typed: LogRow = {
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

  return { ...typed };
}

/**
 * A round WITH its findings, as the exporter takes them.
 *
 * <p>The state travels with the list because an empty list means two different things — nothing was
 * found, and nothing could be read — and the file must not render them the same way.</p>
 */
function round(
  over: Partial<LogRow> = {},
  found: ExportRound['found'] = { state: 'loaded', findings: [] },
): ExportRound {
  return { row: row(over), found };
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

  const outcome = await exportRounds([round(), round({ key: 'k2' })], port);

  assert.equal(outcome, 'written');
  assert.equal(port.written.length, 1);
  assert.equal(port.written[0]!.path, 'D:/out/rounds.csv');
  assert.match(port.said[0]!, /2 rounds written to D:\/out\/rounds\.csv/);
  assert.deepEqual(port.complained, []);
});

test('one round is called a round, not "1 rounds"', async () => {
  const port = ports();

  await exportRounds([round()], port);

  assert.match(port.said[0]!, /^1 round written/);
});

test('a cancelled dialog writes nothing, says nothing, and is not an error', async () => {
  // The person changed their mind. Reporting a failure would be a lie about their own decision,
  // and reporting success would be a lie about a file.
  const port = ports({ pickPath: async () => undefined });

  const outcome = await exportRounds([round()], port);

  assert.equal(outcome, 'cancelled');
  assert.deepEqual(port.written, []);
  assert.deepEqual(port.said, []);
  assert.deepEqual(port.complained, []);
});

test('a write that throws is reported as a failure and NEVER as a success', async () => {
  // A read-only drive, a file another program holds, a path that stopped existing between the
  // dialog and the write. The one outcome that must be impossible is being told it worked.
  const port = ports({ write: async () => { throw new Error('EACCES: permission denied'); } });

  const outcome = await exportRounds([round()], port);

  assert.equal(outcome, 'failed');
  assert.deepEqual(port.said, [], 'no success message for a file that is not there');
  assert.match(port.complained[0]!, /could not be written to D:\/out\/rounds\.csv/);
  assert.match(port.complained[0]!, /permission denied/, 'and it says what actually failed');
});

test('a rejection that is not an Error still reaches the person in words', async () => {
  const port = ports({ write: async () => { throw 'the host went away'; } });

  await exportRounds([round()], port);

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

  assert.equal(suggestedName(1, day), 'coai-round-2026-09-14.csv');
  assert.equal(suggestedName(2, day), 'coai-rounds-2026-09-14-2.csv');
});

test('what is written is the CSV of exactly those rounds', async () => {
  const port = ports();

  await exportRounds([round({ subject: 'the one I picked' })], port);

  assert.match(port.written[0]!.text, /the one I picked/);
  assert.equal(port.written[0]!.text.startsWith('\uFEFF'), true);
});

test('a dialog that throws is a failure, not an escaped promise', () => {
  // The plan named three endings and two more could slip between them: a dialog whose window went
  // away, and a serialisation that threw. Either one escaping leaves a control running and a person
  // told nothing at all. (Plan round, codex.)
  const port = ports({ pickPath: async () => { throw new Error('the window went away'); } });

  return exportRounds([round()], port).then((outcome) => {
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
    queue(() => exportRounds([round()], slow)),
    queue(() => exportRounds([round(), round({ key: 'k2' })], slow)),
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

  const first = await queue(() => exportRounds([round()], broken));
  const second = await queue(() => exportRounds([round()], fine));

  assert.equal(first, 'failed');
  assert.equal(second, 'written', 'one failure must not poison every later export');
});

test('a run that REJECTS does not break the queue, and the next one still waits', async () => {
  // Two reviewers read `queue = mine.catch(() => undefined)` as setting the queue to `undefined`.
  // It does not: `.catch()` answers a PROMISE that resolves to undefined, so the chain survives.
  // `exportRounds` never rejects — it reports and returns an outcome — so this exercises the case
  // through the queue directly, which is the only way to reach it at all.
  const queue = oneAtATime();
  const order: string[] = [];

  const first = queue(async () => { order.push('first'); throw new Error('boom'); });
  const second = queue(async () => {
    order.push('second');

    return 'written' as const;
  });

  await assert.rejects(() => first, /boom/);
  assert.equal(await second, 'written');
  assert.deepEqual(order, ['first', 'second'], 'the second still ran, and ran after the first');
});

// ---------- the reads are a child process each, so they go a few at a time ----------

test('reads run four at a time, never all at once', async () => {
  // A few hundred child processes started together exhausts handles, so rounds that were perfectly
  // readable time out and the export fails on trouble it caused itself. Three reviewers raised it.
  let running = 0;
  let mostAtOnce = 0;
  const items = Array.from({ length: 20 }, (_, at) => at);

  const batched = await inBatches(items, async (item) => {
    running += 1;
    mostAtOnce = Math.max(mostAtOnce, running);
    await new Promise((ready) => setTimeout(ready, 1));
    running -= 1;

    return item * 2;
  }, () => -1);
  const done = batched.results;

  assert.equal(mostAtOnce, 4, 'four in flight at the peak');
  assert.deepEqual(done, items.map((at) => at * 2), 'and the order is preserved');
});

test('an empty list of work is no batches and no error', async () => {
  assert.deepEqual((await inBatches([], async (x) => x, () => 0)).results, []);
});

test('an export that could read only SOME rounds still writes, and says which it could not', async () => {
  const port = ports();

  const outcome = await exportRounds([
    round({ key: 'good' }, { state: 'loaded', findings: [] }),
    round({ key: 'bad' }, { state: 'failed', findings: [] }),
  ], port);

  assert.equal(outcome, 'written');
  assert.match(port.said[0]!, /could not be read/);
  assert.match(port.said[0]!, /bad/, 'and names them, so nobody reads those rows as clean');
});

test('an export where NOTHING could be read writes no file and opens no dialog', async () => {
  let asked = false;
  const port = ports({ pickPath: async () => { asked = true; return 'D:/out/rounds.csv'; } });

  const outcome = await exportRounds([round({ key: 'bad' }, { state: 'failed', findings: [] })], port);

  assert.equal(outcome, 'failed');
  assert.equal(asked, false, 'the person is not asked for a path to a file that cannot be written');
  assert.deepEqual(port.written, []);
  assert.match(port.complained[0]!, /could not be read/);
});

test('one job rejecting does not abort its batch or the ones after it', async () => {
  // `Promise.all` rejects at the first failure and leaves its siblings running with nobody awaiting
  // them. This is a generic helper and its next caller will not know that, so every item answers
  // for itself. (Code round, gemini, twice.)
  const { results: done } = await inBatches(
    [1, 2, 3, 4, 5, 6],
    async (item) => {
      if (item % 2 === 0) {
        throw new Error(`no ${item}`);
      }

      return `ok ${item}`;
    },
    (item) => `failed ${item}`,
    2);

  assert.deepEqual(done, ['ok 1', 'failed 2', 'ok 3', 'failed 4', 'ok 5', 'failed 6'],
    'every item has a result, in order');
});

test('a batch size that could never advance the loop does not hang', async () => {
  // Zero or a negative would never move the index and the export would look permanently stuck.
  for (const atOnce of [0, -3, Number.NaN, Number.POSITIVE_INFINITY]) {
    assert.deepEqual((await inBatches([1, 2], async (x) => x * 10, () => 0, atOnce)).results, [10, 20],
      `atOnce=${atOnce} must still finish`);
  }
});

test('readAndExport reads every row and writes them, all inside one call', async () => {
  const port = ports();
  const asked: string[] = [];

  const outcome = await readAndExport(
    [row({ key: 'a' }), row({ key: 'b' })],
    async (one) => {
      asked.push(String(one.key));

      return { state: 'loaded' as const, findings: [] };
    },
    port);

  assert.equal(outcome, 'written');
  assert.deepEqual(asked, ['a', 'b']);
  assert.equal(port.written.length, 1);
});

test('a reader that THROWS makes that round failed, not absent, and not an exception', async () => {
  const port = ports();

  const outcome = await readAndExport(
    [row({ key: 'a' }), row({ key: 'b' })],
    async (one) => {
      if (one.key === 'b') {
        throw new Error('the server went away');
      }

      return { state: 'loaded' as const, findings: [] };
    },
    port);

  assert.equal(outcome, 'written', 'the readable round is still written');
  assert.match(port.said[0]!, /could not be read/);
  assert.equal(port.said[0]!.endsWith(': b.'), true, port.said[0]!);
});

// ---------- a selection is a job, so it reports and can be stopped ----------

test('progress is reported once per round read, in order', async () => {
  const seen: string[] = [];
  const port = ports({ progress: (done, total) => seen.push(`${done}/${total}`) });

  await readAndExport(
    [row({ key: 'a' }), row({ key: 'b' }), row({ key: 'c' })],
    async () => ({ state: 'loaded' as const, findings: [] }),
    port);

  assert.deepEqual(seen, ['1/3', '2/3', '3/3']);
});

test('a cancelled export writes nothing and says nothing', async () => {
  // The person stopped it. Telling them they stopped it is noise, exactly as with a dismissed
  // save dialog.
  let asked = false;
  const port = ports({
    cancelled: () => true,
    pickPath: async () => { asked = true; return 'D:/out/rounds.csv'; },
  });

  const outcome = await readAndExport([row()], async () => ({ state: 'loaded' as const, findings: [] }), port);

  assert.equal(outcome, 'cancelled');
  assert.equal(asked, false, 'no dialog for a job that was stopped');
  assert.deepEqual(port.written, []);
  assert.deepEqual(port.said, []);
  assert.deepEqual(port.complained, []);
});

test('cancelling stops the reads rather than merely discarding them', async () => {
  let read = 0;
  let stop = false;
  const port = ports({ cancelled: () => stop });

  await readAndExport(
    Array.from({ length: 40 }, (_, at) => row({ key: `k${at}` })),
    async () => {
      read += 1;
      stop = true;

      return { state: 'loaded' as const, findings: [] };
    },
    port);

  assert.ok(read <= 8, `the reads stop within a batch or two, read ${read} of 40`);
});

test('an unusually large selection is confirmed before anything is read', async () => {
  let read = 0;
  const port = ports({
    confirmLarge: async () => false,
    progress: () => { read += 1; },
  });

  const outcome = await readAndExport(
    Array.from({ length: ASK_ABOVE + 1 }, (_, at) => row({ key: `k${at}` })),
    async () => ({ state: 'loaded' as const, findings: [] }),
    port);

  assert.equal(outcome, 'cancelled');
  assert.equal(read, 0, 'saying no means nothing is read at all');
});

test('a selection at or under the cap is not confirmed', async () => {
  let asked = false;
  const port = ports({ confirmLarge: async () => { asked = true; return true; } });

  await readAndExport(
    Array.from({ length: 3 }, (_, at) => row({ key: `k${at}` })),
    async () => ({ state: 'loaded' as const, findings: [] }),
    port);

  assert.equal(asked, false);
});

test('a cancelled batch says so as a VALUE, so it cannot be read as a complete one', () => {
  // The first shape returned the results read so far and left the caller to ask the token again.
  // A caller that forgets writes a truncated file and reports success. (Plan round, two vendors.)
  let stop = false;

  return inBatches(
    [1, 2, 3, 4, 5, 6, 7, 8],
    async (item) => { stop = true; return item; },
    () => 0,
    2,
    () => stop,
  ).then((batched) => {
    assert.equal(batched.done, false, 'the run did not finish, and says so');
    assert.ok(batched.results.length < 8, 'and it carries only what it read');
  });
});

test('a batch that finishes says done, whatever the stop callback answers afterwards', async () => {
  const batched = await inBatches([1, 2], async (x) => x, () => 0, 2, () => false);

  assert.equal(batched.done, true);
  assert.deepEqual(batched.results, [1, 2]);
});
