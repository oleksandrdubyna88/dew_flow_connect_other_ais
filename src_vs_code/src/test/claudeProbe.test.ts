import assert from 'node:assert/strict';
import { test } from 'node:test';

import { PROBE_PROMPT, ProbePorts, probeClaudeModels, probeToKeep } from '../claudeProbe';
import type { ProbeResult } from '../claudeModels';

/**
 * The probe run, against a fake CLI — because a real one costs a billed request per candidate and
 * would make this suite depend on somebody's allowance.
 *
 * <p>The payloads are the real `--output-format json` shape, recorded from the installed CLI on
 * 2026-09-16.</p>
 */

function reply(model: string): string {
  return JSON.stringify({
    type: 'result', subtype: 'success', is_error: false, result: 'Hi!',
    usage: { input_tokens: 4, output_tokens: 11 },
    modelUsage: { [model]: { inputTokens: 4, outputTokens: 11 } },
  });
}

/** A CLI that answers each candidate with whatever the table says, and records what it was asked. */
function cli(answers: Readonly<Record<string, string | number>>, version = '2.1.0'): ProbePorts & { asked: string[][] } {
  const asked: string[][] = [];

  return {
    asked,
    cliVersion: () => Promise.resolve(version),
    now: () => Date.parse('2026-09-16T10:00:00.000Z'),
    run: (args) => {
      asked.push([...args]);
      const model = args[args.indexOf('--model') + 1] ?? '';
      const answer = answers[model];

      return Promise.resolve(typeof answer === 'number'
        ? { code: answer, output: '' }
        : { code: 0, output: reply(answer ?? 'claude-opus-5[1m]') });
    },
  };
}

test('each candidate is asked once, with the json format the answer is read from', async () => {
  const fake = cli({ haiku: 'claude-haiku-4-5', sonnet: 'claude-sonnet-5' });

  await probeClaudeModels(fake, ['haiku', 'sonnet']);

  assert.equal(fake.asked.length, 2, 'a candidate was asked about more than once, or not at all');
  assert.deepEqual(fake.asked[0], ['--model', 'haiku', '-p', PROBE_PROMPT, '--output-format', 'json']);
});

test('a candidate answered by ANOTHER model is recorded as unverified, not dropped', async () => {
  // The measured behaviour of an unknown name: the CLI exits 0 and the DEFAULT answers. The run has
  // to keep the fact rather than the silence, or the next run asks again for nothing.
  const found = await probeClaudeModels(cli({ fable: 'claude-opus-5[1m]' }), ['fable']);

  assert.ok(found);
  assert.deepEqual(found.models, [{ asked: 'fable', answered: 'claude-opus-5[1m]', verified: false }]);
});

test('a real family is confirmed, and carries the model that answered', async () => {
  const found = await probeClaudeModels(cli({ fable: 'claude-fable-5-1' }), ['fable']);

  assert.ok(found);
  assert.deepEqual(found.models, [{ asked: 'fable', answered: 'claude-fable-5-1', verified: true }]);
  assert.equal(found.cliVersion, '2.1.0', 'the answer must name the binary that gave it');
});

test('one candidate that could not be started costs its own candidate and nothing else', async () => {
  // `capture` reports -1 for a throw, a spawn error, a timeout and an unreadable exit alike, so this
  // cannot tell an absent binary from one request that hung. It used to treat both as the first and
  // abandon the run — which meant a transient hiccup on the FIRST candidate left every other family
  // unasked for a week. Only a CLI that fails to run twice running is given up on. (Code round.)
  const fake = cli({ haiku: -1, sonnet: 'claude-sonnet-5' });

  const found = await probeClaudeModels(fake, ['haiku', 'sonnet']);

  assert.equal(fake.asked.length, 2, 'the candidate after a failure was never asked');
  assert.deepEqual(found?.models.map((m) => m.asked), ['sonnet'], 'and what DID answer is the answer');
});

test('a CLI with no version is not asked anything', async () => {
  const fake = cli({}, '');

  assert.equal(await probeClaudeModels(fake), undefined);
  assert.equal(fake.asked.length, 0, 'an answer that cannot be attributed to a binary is not worth a request');
});

test('a run that failed keeps the answer that was already held', async () => {
  // The guarantee the plan round pressed hardest on: an allowance that is spent must not empty the
  // dropdown, and issue #165 has just measured that this installation reaches that state.
  const held: ProbeResult = {
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [{ asked: 'fable', answered: 'claude-fable-5-1', verified: true }],
  };

  assert.equal(probeToKeep(undefined, held), held, 'a failed run threw away a good answer');
  assert.equal(probeToKeep(undefined, undefined), undefined);

  const fresh: ProbeResult = { ...held, checkedUtc: '2026-09-17T10:00:00.000Z' };
  // deepEqual rather than equal: a run that reached every candidate it held supersedes it entirely,
  // but the merge builds the answer rather than handing back the object it was given.
  assert.deepEqual(probeToKeep(fresh, held), fresh, 'a successful run must replace what it supersedes');
});

// ---------------------------------------------------------------------------------------------
// What a run that could NOT ask everything is allowed to do to what is already known
// (the code round: codex SecurityReliability + UxDxPerformance, gemini SecurityReliability)

const HELD: ProbeResult = {
  cliVersion: '2.1.0',
  checkedUtc: '2026-09-15T10:00:00.000Z',
  models: [
    { asked: 'haiku', answered: 'claude-haiku-4-5', verified: true },
    { asked: 'sonnet', answered: 'claude-sonnet-5', verified: true },
    { asked: 'opus', answered: 'claude-opus-5', verified: true },
    { asked: 'fable', answered: 'claude-fable-5-1', verified: true },
  ],
};

test('one candidate that cannot be asked does not abandon the ones after it', async () => {
  // A timeout and a CLI that will not start both answer -1, and they are not the same event: a
  // transient hiccup on 'haiku' must not leave sonnet, opus and fable unasked for a week.
  const fake = cli({ haiku: -1, sonnet: 'claude-sonnet-5', opus: 'claude-opus-5', fable: 'claude-fable-5-1' });
  const found = await probeClaudeModels(fake);

  assert.deepEqual(fake.asked.map((args) => args[args.indexOf('--model') + 1]),
    ['haiku', 'sonnet', 'opus', 'fable'], 'every candidate is still asked');
  assert.equal(found?.models.filter((m) => m.verified).length, 3, 'the three that answered are verified');
});

test('a CLI that will not start at all is given up on, not asked four times', async () => {
  const fake = cli({ haiku: -1, sonnet: -1, opus: -1, fable: -1 });

  assert.equal(await probeClaudeModels(fake), undefined, 'nothing was learned, so there is no answer');
  assert.ok(fake.asked.length < 4, `it kept asking a CLI that never ran: ${fake.asked.length} times`);
});

test('a partial run MERGES into what was known, and never subtracts from it', () => {
  const partial: ProbeResult = {
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [{ asked: 'haiku', answered: 'claude-haiku-4-5', verified: true }],
  };
  const kept = probeToKeep(partial, HELD);

  assert.equal(kept?.models.length, 4, 'three verified families were dropped by a run that never asked them');
  assert.equal(kept?.models.find((m) => m.asked === 'opus')?.verified, true,
    'opus was confirmed yesterday and this run did not reach it');
  assert.equal(kept?.checkedUtc, '2026-09-16T10:00:00.000Z', 'but what WAS asked is the newer answer');
});

test('a merge only carries forward an answer from the SAME CLI', () => {
  const newer: ProbeResult = {
    cliVersion: '2.2.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [{ asked: 'haiku', answered: 'claude-haiku-4-5', verified: true }],
  };
  const kept = probeToKeep(newer, HELD);

  assert.deepEqual(kept?.models.map((m) => m.asked), ['haiku'],
    'a new binary may reach different families, so nothing is inherited across one');
});

test('the probe stops when whoever asked for it has gone away', async () => {
  let gone = false;
  const fake = cli({ haiku: 'claude-haiku-4-5', sonnet: 'claude-sonnet-5' });
  const watched: ProbePorts = {
    ...fake,
    run: (args) => { gone = true; return fake.run(args); },
  };

  const found = await probeClaudeModels(watched, undefined, () => gone);
  assert.equal(found?.models.length, 1, 'four candidates are up to a hundred seconds of billed requests');
});

// ---------------------------------------------------------------------------------------------
// Round 2: what an answer that could not be CONFIRMED may do to one that was

test('an unconfirmed answer never displaces a confirmed one', () => {
  // The gap round 1's merge left. A candidate whose request FAILED with an ordinary non-zero exit —
  // a spent allowance is exactly this — is recorded as asked-and-unverified, not skipped. The merge
  // then let that unverified entry win over yesterday's confirmed one, so every family a person had
  // silently went back to "not asked yet" after waiting through a probe. (local Architecture.)
  const spent: ProbeResult = {
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-17T10:00:00.000Z',
    models: HELD.models.map((m) => ({ asked: m.asked, answered: '', verified: false })),
  };
  const kept = probeToKeep(spent, HELD);

  assert.equal(kept?.models.filter((m) => m.verified).length, 4,
    'a run that confirmed nothing took away four families that were confirmed');
  assert.equal(kept?.models.find((m) => m.asked === 'sonnet')?.answered, 'claude-sonnet-5',
    'and the concrete id it had resolved to went with them');
});

test('but a CONFIRMED answer does replace the one it supersedes', () => {
  const moved: ProbeResult = {
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-17T10:00:00.000Z',
    models: [{ asked: 'sonnet', answered: 'claude-sonnet-6', verified: true }],
  };

  assert.equal(probeToKeep(moved, HELD)?.models.find((m) => m.asked === 'sonnet')?.answered, 'claude-sonnet-6',
    'a family that answered as something new must say so');
});

test('the answer records WHICH binary it is about', async () => {
  const fake = cli({ haiku: 'claude-haiku-4-5' });
  const found = await probeClaudeModels({ ...fake, executable: 'C:/tools/claude.cmd' }, ['haiku']);

  assert.equal(found?.executable, 'C:/tools/claude.cmd',
    'two Claude CLIs on one machine are two accounts, and a record that cannot say which is evidence about neither');
});
