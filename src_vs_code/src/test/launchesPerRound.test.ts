import assert from 'node:assert/strict';
import { test } from 'node:test';

import { launchesPerRound, launchFact, LaunchLine, LaunchRow } from '../launchesPerRound';
import { isLaunchLine } from '../roundMoney';
import { KEY_A, KEY_B, THIS_STORE, WSL_STORE } from './roundCountsFixtures';

/**
 * Launches per round (E6.S3): the launch lines that carry a round key, over the rounds whose lines carry one. A launch is
 * `isLaunchLine` — whatever it says on main — of THIS row's vendor, of a round in the row's keys, from the store the answer
 * attributes the key to. Membership in the keys is the window's cut, so there is no second time comparison.
 */

const KEY_C = '5d6c7b8a9f0e1d2c3b4a59687766554f';

const row = (over: Partial<LaunchRow> = {}): LaunchRow => ({ provider: 'codex', roundKeys: [KEY_A, KEY_B], roundsWithoutKey: 0, ...over });
const line = (round: string | undefined, over: Partial<LaunchLine> = {}): LaunchLine => ({
  provider: 'codex', attempt: 'first', cli: 'codex', ...(round === undefined ? {} : { round }), ...over,
});

test('LaunchesPerRound_CountsOnlyRoundsWithIds — 5 launches over the 2 keyed rounds is 2.5 a round', () => {
  const lines: readonly LaunchLine[] = [
    line(KEY_A), line(KEY_A, { attempt: 'retry' }), line(KEY_A, { attempt: 'repair' }),
    line(KEY_B), line(KEY_B, { attempt: 'retry' }),
    { provider: 'codex', round: KEY_A }, // a NoBudget line: it launched nothing, so it says neither an attempt nor a runtime
    line(KEY_B, { cli: 'remote' }), // a Team-server job's total, written locally — never a launch
    line(undefined), // a launch with no round key: not a keyed round's
    line(KEY_A, { provider: 'gemini' }), // the same round's launch by ANOTHER vendor row: that row's figure, not this one's
  ];

  const figure = launchesPerRound(row(), lines);

  assert.deepEqual(figure, { launches: 5, rounds: 2, perRound: 2.5, keyedWithoutLaunch: 0, withoutKey: 0 });
});

test('ARoundWhoseKeyIsOnNoLine_IsReportedApart_AndOnlyAKeylessRoundSaysBefore0602', () => {
  const figure = launchesPerRound(row({ roundKeys: [KEY_A, KEY_B, KEY_C], roundsWithoutKey: 1 }), [line(KEY_A), line(KEY_A, { attempt: 'retry' })]);
  const fact = launchFact(figure);

  assert.equal(figure.keyedWithoutLaunch, 2);
  assert.equal(figure.withoutKey, 1);
  assert.match(fact, /2 rounds carry no launch count/u);
  assert.match(fact, /1 round written before coai-mcp 0\.60\.2 carries no round key/u);
  assert.doesNotMatch(fact, /3 rounds|2 rounds written before/u, 'the keyed rounds B and C were blamed on a coai-mcp that predates the key');
});

test('a row with no keyless round says nothing of 0.60.2, and a row where every keyed round has launches says nothing at all', () => {
  assert.doesNotMatch(launchFact(launchesPerRound(row(), [line(KEY_A)])), /0\.60\.2/u);
  assert.equal(launchFact(launchesPerRound(row(), [line(KEY_A), line(KEY_B)])), '');
});

test('a row with no keyed round has no per-round figure — a dash, never a division by nothing', () => {
  const figure = launchesPerRound(row({ roundKeys: [], roundsWithoutKey: 4 }), [line(KEY_A)]);

  assert.equal(figure.rounds, 0);
  assert.equal(figure.perRound, undefined);
  assert.equal(figure.launches, 0, 'a launch of a round the row does not count was counted');
});

test('AnOldFollowUpLine_IsNotALaunch_AndANewOneIs — exactly isLaunchLine\'s answer, whatever it is on main', () => {
  const candidates: readonly LaunchLine[] = [
    line(KEY_A, { attempt: 'followUp' }),
    { ...line(KEY_A, { attempt: 'followUp' }), usageBasis: 'difference' } as LaunchLine,
    line(KEY_A, { attempt: 'first' }),
    line(KEY_A, { attempt: 'rotated' }),
    line(KEY_A, { attempt: 'a-word-from-the-future' }),
  ];

  const figure = launchesPerRound(row(), candidates);

  assert.equal(figure.launches, candidates.filter((one) => isLaunchLine(one)).length, 'the figure has its own idea of a launch');
  assert.ok(figure.launches >= 1, 'a first launch must count');
});

test('a launch counts only from the store the answer attributes its key to — a copied store\'s lines do not double it', () => {
  const owners = { [KEY_A]: THIS_STORE, [KEY_B]: WSL_STORE };
  const lines = [
    line(KEY_A, { store: THIS_STORE }), line(KEY_A, { store: WSL_STORE }), // a copy of A in the other store
    line(KEY_B, { store: WSL_STORE }), line(KEY_B, { store: WSL_STORE, attempt: 'retry' }), // two retries in B's owning store: both count
  ];

  const figure = launchesPerRound(row({ keyStore: owners }), lines);

  assert.equal(figure.launches, 3);
  assert.equal(figure.perRound, 1.5);
});

test('a keyed line whose key has no keyStore entry is counted wherever it was read — the single-store and older-binary answers', () => {
  assert.equal(launchesPerRound(row(), [line(KEY_A, { store: WSL_STORE }), line(KEY_B)]).launches, 2);
});
