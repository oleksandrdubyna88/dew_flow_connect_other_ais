import assert from 'node:assert/strict';
import { test } from 'node:test';

import {
  ASKING_CLAUDE,
  CLAUDE_CANDIDATES,
  PROBE_GOOD_FOR_MS,
  answeredAsAsked,
  claudeModels,
  claudeNote,
  familyOf,
  modelThatAnswered,
  stillGood,
} from '../claudeModels';
import { CURATED_CLAUDE_MODELS, modelsProvenance } from '../models';

/**
 * Asking the CLI which Claude models this machine can reach.
 *
 * <p>Every fixture here is a real answer, recorded on 2026-09-16 by running the installed CLI. The
 * measurement is the whole design: an unknown model name is silently ignored and the default replies,
 * so the question is never whether the call succeeded.</p>
 */

/** The `--output-format json` reply, with the fields this reads and a few of its real neighbours. */
function reply(model: string): string {
  return JSON.stringify({
    type: 'result',
    subtype: 'success',
    is_error: false,
    result: 'Hi! What can I help you with today?',
    session_id: '0f0f',
    duration_ms: 3120,
    total_cost_usd: 0.0004,
    usage: { input_tokens: 4, output_tokens: 11 },
    modelUsage: { [model]: { inputTokens: 4, outputTokens: 11 } },
  });
}

// ---------- the family, which is the unit the question is asked in ----------

test('a model belongs to a family, whatever version or window it wears', () => {
  assert.equal(familyOf('claude-fable-5-1'), 'fable');
  assert.equal(familyOf('claude-opus-5[1m]'), 'opus', 'a context-window suffix is not a family');
  assert.equal(familyOf('claude-sonnet-5'), 'sonnet');
  assert.equal(familyOf('opus'), 'opus', 'a bare alias is its own family');
  assert.equal(familyOf('  Sonnet  '), 'sonnet', 'and it is not case or padding');
  assert.equal(familyOf(''), '');
});

test('a candidate is confirmed only when its own family answered', () => {
  // MEASURED, and both halves matter. Exact equality would reject the real Fable, because `fable` is
  // answered by `claude-fable-5-1`; a prefix or substring test would accept the invented name,
  // because every Claude model starts with `claude-`. Two reviewers raised this independently.
  assert.equal(answeredAsAsked('fable', 'claude-fable-5-1'), true, 'the real one must be accepted');
  assert.equal(answeredAsAsked('sonnet', 'claude-sonnet-5'), true);
  assert.equal(
    answeredAsAsked('definitely-not-a-model-xyz', 'claude-opus-5[1m]'), false,
    'an invented name answered by the DEFAULT must never count as available',
  );
  assert.equal(answeredAsAsked('opus', 'claude-sonnet-5'), false, 'a sibling family is not the one asked for');
  assert.equal(answeredAsAsked('', 'claude-opus-5'), false, 'nothing asked is nothing confirmed');
});

// ---------- reading the answer ----------

test('the model that answered is read off modelUsage, which is where the CLI puts it', () => {
  assert.equal(modelThatAnswered(reply('claude-fable-5-1')), 'claude-fable-5-1');
});

test('an answer this cannot read confirms nothing, rather than confirming the candidate', () => {
  // Each of these is a shape a future CLI could hand back. None of them may read as "available":
  // that is the direction that silently runs the wrong model.
  for (const [what, output] of [
    ['not json at all', 'Hi! What can I help you with today?'],
    ['json with no modelUsage', JSON.stringify({ result: 'hi', usage: { input_tokens: 4 } })],
    ['modelUsage that is not an object', JSON.stringify({ modelUsage: 'claude-opus-5' })],
    ['modelUsage naming two models', JSON.stringify({ modelUsage: { a: {}, b: {} } })],
    ['nothing at all', ''],
  ] as const) {
    assert.equal(modelThatAnswered(output), '', `${what} must confirm nothing`);
  }
});

// ---------- how long an answer is trusted ----------

test('an answer is trusted for a week, and not across a different CLI', () => {
  const at = Date.parse('2026-09-16T10:00:00.000Z');
  const probe = { cliVersion: '2.1.0', checkedUtc: '2026-09-16T10:00:00.000Z', models: [] };

  assert.equal(stillGood(probe, '2.1.0', at + 1000), true);
  assert.equal(stillGood(probe, '2.1.0', at + PROBE_GOOD_FOR_MS - 1), true);
  assert.equal(stillGood(probe, '2.1.0', at + PROBE_GOOD_FOR_MS), false, 'a week is the bound');
  assert.equal(
    stillGood(probe, '2.2.0', at + 1000), false,
    'another binary may reach another set of models, so its answer is not this one',
  );
  assert.equal(stillGood(undefined, '2.1.0', at), false);
  assert.equal(
    stillGood({ ...probe, checkedUtc: 'not a date' }, '2.1.0', at), false,
    'an unreadable stamp is not a fresh one',
  );
});

// ---------- what the dropdown offers ----------

test('a probe that never ran leaves the list no shorter than it already was', () => {
  // The guarantee that makes this safe to ship: an account whose allowance is spent, a CLI that is
  // not installed and a first run all look the same to this function, and none of them may take a
  // model away from the person.
  const offered = claudeModels(undefined, CURATED_CLAUDE_MODELS);

  assert.deepEqual(
    offered.map((m) => m.id),
    CURATED_CLAUDE_MODELS.map((m) => m.id),
    'a discovery that could not run subtracted from the list',
  );
  assert.ok(offered.every((m) => /not asked yet/.test(m.label)), 'and it must not claim they were confirmed');
});

test('fable is offered whether or not anything was asked', () => {
  // The reported symptom of #301, fixed on its own line so it survives the probe never running.
  assert.ok(
    CURATED_CLAUDE_MODELS.some((m) => m.id === 'fable'),
    'the curated list is missing the family the issue is about',
  );
  assert.ok(CLAUDE_CANDIDATES.includes('fable'), 'and nothing would ever ask about it');
});

test('a confirmed model names the one the CLI resolved it to', () => {
  const probe = {
    cliVersion: '2.1.0',
    checkedUtc: new Date().toISOString(),
    models: [
      { asked: 'fable', answered: 'claude-fable-5-1', verified: true },
      { asked: 'sonnet', answered: 'claude-sonnet-5', verified: true },
      { asked: 'haiku', answered: 'claude-opus-5[1m]', verified: false },
    ],
  };

  const offered = claudeModels(probe, CURATED_CLAUDE_MODELS);
  const label = (id: string): string => offered.find((m) => m.id === id)?.label ?? '';

  assert.match(label('fable'), /claude-fable-5-1/, 'a confirmed model must say what it resolved to');
  assert.match(label('sonnet'), /claude-sonnet-5/);
  assert.match(
    label('haiku'), /not asked yet/,
    'a candidate answered by ANOTHER model was offered as though it had been confirmed',
  );
  assert.deepEqual(
    offered.map((m) => m.id),
    CURATED_CLAUDE_MODELS.map((m) => m.id),
    'a probe removed a model from the list',
  );
});

test('a Claude dropdown says it is being asked, and only while it is', () => {
  assert.equal(claudeNote('claude', true), ASKING_CLAUDE);
  assert.equal(claudeNote('claude', false), '', 'nothing is running, so there is nothing to say');
  assert.equal(claudeNote('codex', true), '', 'the probe asks the Claude CLI and nothing else');
  assert.equal(claudeNote('', true), '', 'a row with no runtime is not waiting for anything');
});

// ---------------------------------------------------------------------------------------------
// What the caption under the dropdown is allowed to claim

test('the caption says it is asking while the probe runs', () => {
  assert.equal(modelsProvenance('claude', [], undefined, [], undefined, undefined, true), ASKING_CLAUDE);
});

test('the caption reports what the CLI answered, not what this build believes', () => {
  const probe = {
    cliVersion: '2.0.44',
    checkedUtc: '2026-09-16T08:00:00.000Z',
    models: [
      { asked: 'haiku', answered: 'claude-haiku-4-5-20251001', verified: true },
      { asked: 'sonnet', answered: 'claude-sonnet-5', verified: true },
      { asked: 'fable', answered: 'claude-opus-5', verified: false },
    ],
  };

  const said = modelsProvenance('claude', [], undefined, [], undefined, probe);
  assert.match(said, /2 families/, `the caption did not count what answered; it said "${said}"`);
  assert.match(said, /2026-09-16/, 'a week-old answer must say when it was taken');
});

test('a caption with no probe behind it does not claim the CLI was asked', () => {
  const said = modelsProvenance('claude', [], undefined, [], undefined, undefined);

  assert.ok(!said.includes('answered'), `nothing was asked; it said "${said}"`);
  assert.match(said, /not been asked/, 'so it says so, rather than stating a resolution as fact');
});
