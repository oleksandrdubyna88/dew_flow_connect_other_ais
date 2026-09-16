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

test('a CLI that cannot be started ends the run, and answers nothing at all', async () => {
  // `capture` reports -1 for a throw, a spawn error, a timeout and an unreadable exit alike. Three
  // more of the same failure teaches nothing and costs three more launches.
  const fake = cli({ haiku: -1, sonnet: 'claude-sonnet-5' });

  const found = await probeClaudeModels(fake, ['haiku', 'sonnet']);

  assert.equal(found, undefined, 'a run that learned nothing must not be written down as an answer');
  assert.equal(fake.asked.length, 1, 'it kept asking after the CLI had already failed to start');
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
  assert.equal(probeToKeep(fresh, held), fresh, 'a successful run must replace what it supersedes');
});
