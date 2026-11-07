import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import {
  answerPair, contractVerdict, disagreements, foreignSnapshot, neverWritten, OTHER_KEY, OTHER_STORE, valueVerdict,
} from '../../scripts/seam-round-counts-merged.mjs';

/**
 * The round-counts leg of the seam, held to what it claims (E6.S5 of todo/PLAN_the_round_counter_tail_E6.md).
 *
 * <p>The leg runs the real `coai-mcp`; these run its VERDICTS over answers written the way the real mode writes them, so a leg
 * that went blind — comparing only what is present, or reading a collapsed count as fine — is a red test here and not a green
 * seam that checks nothing.</p>
 */

const LIVE_STORE = 'a1b2c3d4e5f60718293a4b5c6d7e8f90';
const LIVE_KEY = '9b1f6e2d4c8a7b5e3f1d0c9a8b7e6d5c';
const FOREIGN = '/seam/other/round-counts-snapshot.json';
const MISSING = '/seam/nowhere/round-counts-snapshot.json';
const LIVE = { storeId: LIVE_STORE, id: 1, key: LIVE_KEY, stage: 'PlanReview', provider: 'codex' };
const STAGES = (plan) => ({ plan, code: 0, document: 0, feature: 0 });
const NOW = '2026-10-21T10:00:00.0000000Z';

const store = (id, live, over = {}) => ({
  id, live, platform: 'linux', host: 'wsl', wslDistro: live ? '' : 'SeamDistro', side: '', directory: '/seam', paths: [], asOf: NOW,
  quiet: false, truncated: false, coaiMcpVersion: '9.9.9', conflicts: 0, ...over,
});

/** The merged answer as the real mode writes it for the leg's fixture: two stores, two rounds from one integer id, one path missing. */
function mergedRaw(edit = () => undefined) {
  const row = {
    provider: 'codex', rounds: STAGES(2), reviewers: 2, models: [], roundKeys: [LIVE_KEY, OTHER_KEY],
    keyStore: { [LIVE_KEY]: LIVE_STORE, [OTHER_KEY]: OTHER_STORE }, roundsWithoutKey: 0,
    parts: [{ store: LIVE_STORE, rounds: STAGES(1), reviewers: 1 }, { store: OTHER_STORE, rounds: STAGES(1), reviewers: 1 }],
  };
  const answer = {
    nowUtc: NOW, windows: [{ name: '7d', fromUtc: NOW, toUtc: NOW, rows: [row] }], modelsKnownSince: null,
    gauge: { ratios: [], unavailable: 'local-capture-deferred' },
    stores: [store(LIVE_STORE, true), store(OTHER_STORE, false, { paths: [FOREIGN] })],
    storesNotIncluded: [{ path: MISSING, reason: 'missing', detail: MISSING }],
  };
  edit(answer, row);

  return answer;
}

function plainRaw(edit = () => undefined) {
  const row = { provider: 'codex', rounds: STAGES(1), reviewers: 1, models: [], roundKeys: [LIVE_KEY], roundsWithoutKey: 0 };
  const answer = { nowUtc: NOW, windows: [{ name: '7d', fromUtc: NOW, toUtc: NOW, rows: [row] }], modelsKnownSince: null, gauge: { ratios: [], unavailable: 'local-capture-deferred' } };
  edit(answer, row);

  return answer;
}

const pair = (raw) => answerPair(JSON.stringify(raw));
const verdicts = (merged, plain) => ({
  contract: contractVerdict(merged, plain), value: valueVerdict(merged, plain, LIVE, MISSING, FOREIGN),
});

test('answers the real mode wrote cross whole: every key read back, every tracked key written, every number by value', () => {
  const { contract, value } = verdicts(pair(mergedRaw()), pair(plainRaw()));

  assert.equal(contract, '');
  assert.equal(value, '');
});

test('a key the parser drops is named — the binary wrote it and parseRoundCounts did not give it back', () => {
  const merged = pair(mergedRaw());
  const stripped = { raw: merged.raw, parsed: { ...merged.parsed, stores: merged.parsed.stores.map((one) => Object.fromEntries(Object.entries(one).filter(([key]) => key !== 'conflicts'))) } };

  assert.match(contractVerdict(stripped, pair(plainRaw())), /store 0: conflicts is on the wire and absent from the parsed answer/u);
  assert.ok(disagreements(stripped).length > 0);
});

test('a key the binary never writes is named — a renamed C# property leaves nothing to compare, and that is the failure', () => {
  const renamed = (name) => pair(mergedRaw((_answer, row) => { delete row[name]; }));

  for (const key of ['roundKeys', 'keyStore', 'parts']) {
    assert.match(contractVerdict(renamed(key), pair(plainRaw())), new RegExp(`never wrote .*rows\\[\\]\\.${key}`, 'u'), key);
  }
  const noStores = pair(mergedRaw((answer) => { delete answer.stores; }));
  assert.match(contractVerdict(noStores, pair(plainRaw())), /answer\.stores/u);
  const noConflicts = pair(mergedRaw((answer) => { for (const one of answer.stores) { delete one.conflicts; } }));
  assert.ok(neverWritten(noConflicts).includes('stores[].conflicts'));
});

test('roundsWithoutKey written as 0 is written — a number is a statement, an absent key is not', () => {
  const merged = pair(mergedRaw());

  assert.deepEqual(neverWritten(merged), []);
  assert.ok(neverWritten(pair(mergedRaw((_answer, row) => { delete row.roundsWithoutKey; }))).includes('rows[].roundsWithoutKey'));
});

test('two rounds that share an integer id and differ in key are two rounds — a collapse to one is named by value', () => {
  const collapsed = pair(mergedRaw((_answer, row) => { row.rounds = STAGES(1); row.roundKeys = [LIVE_KEY]; }));

  assert.match(verdicts(collapsed, pair(plainRaw())).value, /the plan rounds of codex in 7 d \(two rounds, one integer id\): expected 2, read 1/u);
});

test('the missing path is not included, never a store — and a store counted for it is named', () => {
  const counted = pair(mergedRaw((answer) => { answer.stores.push(store('ffffffffffffffffffffffffffffffff', false)); answer.storesNotIncluded = []; }));

  assert.match(verdicts(counted, pair(plainRaw())).value, /the stores counted/u);
  assert.match(verdicts(counted, pair(plainRaw())).value, /the stores not included/u);
});

test('the plain answer is the live store\'s part: a plain count that includes the other store is named', () => {
  const plain = pair(plainRaw((_answer, row) => { row.rounds = STAGES(2); row.roundKeys = [LIVE_KEY, OTHER_KEY]; }));

  assert.match(verdicts(pair(mergedRaw()), plain).value, /the plain plan rounds of codex: expected 1, read 2/u);
  assert.match(verdicts(pair(mergedRaw()), plain).value, /the plain row's round keys/u);
});

test('a plain answer that names stores is named — an older reader must see no new top-level shape', () => {
  const plain = pair(plainRaw((answer) => { answer.stores = [store(LIVE_STORE, true)]; }));

  assert.match(verdicts(pair(mergedRaw()), plain).value, /the plain answer's stores/u);
});

test('the leg is wired into the seam runner beside the ledger-fields leg — an unregistered leg checks nothing', () => {
  const runner = readFileSync(join(import.meta.dirname, '..', '..', 'scripts', 'run-seam.mjs'), 'utf8');

  assert.match(runner, /const \{ roundCountsMergedSeam \} = await import\('\.\/seam-round-counts-merged\.mjs'\);/u);
  assert.match(runner, /await roundCountsMergedSeam\(\{ binary, fakeCli: fakeCli\(\), fail, timeoutMs: TIMEOUT_MS \}\);/u);
  assert.ok(runner.indexOf('roundCountsMergedSeam({') > runner.indexOf('ledgerFieldsSeam({'));
});

test('the hand-written snapshot is schema 1 with the real round\'s integer id and another key, written a few minutes before now', () => {
  const now = Date.parse(NOW);
  const written = JSON.parse(foreignSnapshot(LIVE, now));

  assert.equal(written.schema, 1);
  assert.equal(written.store.id, OTHER_STORE);
  assert.equal(written.rounds[0].id, LIVE.id);
  assert.notEqual(written.rounds[0].key, LIVE.key);
  assert.equal(written.rounds[0].stage, LIVE.stage);
  assert.ok(Date.parse(written.writtenUtc) < now && Date.parse(written.rounds[0].startedUtc) < now);
});
