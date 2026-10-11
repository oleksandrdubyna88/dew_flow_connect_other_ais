import assert from 'node:assert/strict';
import { test } from 'node:test';

import { COUNT_UNAVAILABLE, parseRoundCounts, roundCountsFrom, rowIn, totalRounds } from '../roundCounts';
import { readRoundCounts } from '../roundCountsRead';
import { ANSWER_JSON } from './roundCountsFixtures';

/**
 * Reading `coai-mcp --round-counts` (story 2.2 of todo/PLAN_every_round_is_counted.md): an answer that parses is a
 * count, nought included; exit 64 (an older binary), 65, 74, a timeout (-1) and an answer this build cannot read are
 * `unavailable` with their reason — never a zero.
 */

test('an exit-0 answer in the contract is a count, every window and stage read', () => {
  const read = roundCountsFrom({ code: 0, output: ANSWER_JSON });

  assert.equal(read.state, 'counted', `the contract fixture was refused: ${JSON.stringify(read)}`);
  if (read.state !== 'counted') { return; }
  const codex = rowIn(read.answer, '7d', 'codex');
  assert.deepEqual(codex?.rounds, { plan: 5, code: 8, document: 1, feature: 0 });
  assert.equal(totalRounds(codex!), 14);
  assert.equal(codex?.reviewers, 42);
  assert.deepEqual(codex?.models.map((one) => one.model), ['gpt-6-luna', 'gpt-6-terra']);
  assert.equal(rowIn(read.answer, '5h', 'gemini'), undefined, 'a row with no round in a window is absent, not a fake zero row');
});

test('a machine that never ran a round answers rows [] — a count of nought, not unavailable', () => {
  const empty = JSON.stringify({ nowUtc: '2026-10-10T12:00:00Z', windows: ['5h', '24h', '7d'].map((name) => ({ name, fromUtc: '', toUtc: '', rows: [] })), modelsKnownSince: null });

  assert.equal(roundCountsFrom({ code: 0, output: empty }).state, 'counted');
});

test('64, 65, 74, a timeout and an unreadable answer are each unavailable, with the reason — never a count of 0', () => {
  const cases: readonly (readonly [number, string, RegExp])[] = [
    [64, '', /older than --round-counts/u],
    [65, '', /refused/u],
    [74, '', /could not read its rounds database/u],
    [-1, '', /did not answer in time/u],
    [0, 'not json', /cannot read/u],
    [0, JSON.stringify({ nowUtc: 'x', windows: [{ name: '3h', rows: [] }] }), /cannot read/u],
    [3, '', /exited 3/u],
  ];
  for (const [code, output, why] of cases) {
    const read = roundCountsFrom({ code, output });
    assert.equal(read.state, 'unavailable', `exit ${code} with ${JSON.stringify(output)} read as a count`);
    assert.match(read.state === 'unavailable' ? read.why : '', why);
  }
  assert.equal(COUNT_UNAVAILABLE, 'count unavailable');
});

test('a row the parser cannot read refuses the whole answer rather than counting part of it', () => {
  assert.equal(parseRoundCounts(JSON.stringify({ nowUtc: 'x', windows: [{ name: '7d', rows: [{ rounds: {} }] }] })), undefined);
});

test('readRoundCounts asks exactly --round-counts through the run it is handed, and no binary is no count', async () => {
  const asked: string[][] = [];
  const read = await readRoundCounts('coai-mcp', async (args) => {
    asked.push([...args]);

    return { code: 0, output: ANSWER_JSON };
  });

  assert.deepEqual(asked, [['--round-counts']]);
  assert.equal(read.state, 'counted');
  assert.equal((await readRoundCounts('', async () => ({ code: 0, output: ANSWER_JSON }))).state, 'unavailable');
});
