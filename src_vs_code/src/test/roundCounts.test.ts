import assert from 'node:assert/strict';
import { test } from 'node:test';

import { COUNT_UNAVAILABLE, parseRoundCounts, roundCountsFrom, rowIn, totalRounds } from '../roundCounts';
import { ANSWER_JSON, KEY_A, KEY_B, MERGED_JSON, THIS_STORE, WSL_SNAPSHOT, WSL_SNAPSHOT_OLD_SPELLING, WSL_STORE } from './roundCountsFixtures';

/**
 * Reading `coai-mcp --round-counts` (story 2.2 of research/PLAN_every_round_is_counted.md): an answer that parses is a
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

test('AMergedAnswer_IsParsed_WithItsStoresPartsAndKeys (E6.S1) — every field of the 4.4 document typed and whole', () => {
  const answer = parseRoundCounts(MERGED_JSON);

  assert.ok(answer !== undefined, 'the 4.4 document was refused by the parser');
  assert.equal(answer.stores?.length, 2, 'stores[] was not read');
  assert.deepEqual(answer.stores?.map((one) => [one.id, one.live, one.host, one.wslDistro, one.quiet, one.truncated]),
    [[THIS_STORE, true, 'windows', '', false, false], [WSL_STORE, false, 'wsl', 'Ubuntu', false, false]]);
  assert.deepEqual(answer.stores?.[1]?.paths, [WSL_SNAPSHOT, WSL_SNAPSHOT_OLD_SPELLING], 'both spellings of one store');
  assert.equal(answer.stores?.[1]?.directory, '/home/me/.local/share/coai-mcp');
  assert.equal(answer.stores?.[0]?.coaiMcpVersion, '0.63.0');
  assert.deepEqual(answer.storesNotIncluded?.map((one) => one.reason), ['missing', 'newer-schema', 'a-word-from-the-future'],
    'a reason outside the three words must be kept as text');
  assert.match(answer.storesNotIncluded?.[1]?.detail ?? '', /schema 2/u);
  const row = rowIn(answer, '7d', 'codex');
  assert.deepEqual(row?.roundKeys, [KEY_A, KEY_B]);
  assert.deepEqual(row?.keyStore, { [KEY_A]: THIS_STORE, [KEY_B]: WSL_STORE });
  assert.equal(row?.roundsWithoutKey, 1);
  assert.deepEqual(row?.parts?.map((one) => [one.store, totalRounds(one), one.reviewers]), [[THIS_STORE, 2, 4], [WSL_STORE, 1, 3]]);
  assert.equal(answer.gaugeUnavailable, 'local-capture-deferred', 'the plain answer\'s fields read with no new rule');
});

test('AnAnswerWithoutStores_ReadsAsToday — the plain fixture has no stores, no parts, and keyless rows', () => {
  const answer = parseRoundCounts(ANSWER_JSON);

  assert.ok(answer !== undefined);
  assert.equal(answer.stores, undefined);
  assert.equal(answer.storesNotIncluded, undefined);
  const row = rowIn(answer, '7d', 'codex');
  assert.deepEqual(row?.rounds, { plan: 5, code: 8, document: 1, feature: 0 });
  assert.equal(row?.parts, undefined);
  assert.equal(row?.keyStore, undefined);
  assert.deepEqual(row?.roundKeys, []);
  assert.equal(row?.roundsWithoutKey, 0);
});

test('the plain answer\'s two trailing fields (D6) are read: roundKeys and roundsWithoutKey, no stores', () => {
  const plain = JSON.parse(ANSWER_JSON) as { windows: { rows: Record<string, unknown>[] }[] };
  plain.windows[2]!.rows[0]!['roundKeys'] = [KEY_A];
  plain.windows[2]!.rows[0]!['roundsWithoutKey'] = 13;

  const row = rowIn(parseRoundCounts(JSON.stringify(plain))!, '7d', 'codex');

  assert.deepEqual(row?.roundKeys, [KEY_A]);
  assert.equal(row?.roundsWithoutKey, 13);
  assert.equal(row?.keyStore, undefined, 'the plain mode has no store to attribute a key to');
});
