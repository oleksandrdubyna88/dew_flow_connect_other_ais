import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ROUND_WINDOWS, serverRounds, Usage } from '../teamServerApi';
import { teamRoundsBlock } from '../teamRounds';
import { pageTree } from './pageTree';

/**
 * The Team server's own round counts, read and drawn (story 5.2 of todo/PLAN_every_round_is_counted.md).
 *
 * <p>An older server is judged by the SHAPE of its answer, never its status: today's server binds only `window`, so it
 * answers `?windows=5h,24h,week` with 200 and today's range — no `windows`, no count fields. Every test of "unavailable"
 * here is built on exactly that 200, never on a refusal.</p>
 */

const counts = (rounds: number, extra: Partial<Record<string, number>> = {}) => ({
  rounds,
  runsWithoutRound: 0,
  retries: 0,
  repairs: 0,
  followUps: 0,
  cancelled: 0,
  noBudget: 0,
  launchesByKind: { review: rounds },
  ...extra,
});

/** A newer server's company answer: its window, and the three round windows beside it. */
function newer(): Usage {
  const codex = (rounds: number, extra: Partial<Record<string, number>> = {}) => ({
    vendor: 'codex', runs: rounds * 4, failed: 0, tokensIn: 1, tokensOut: 1, seconds: 1, models: [], ...counts(rounds, extra),
  });
  const window = (name: string, rounds: number, extra: Partial<Record<string, number>> = {}) => ({
    window: name,
    fromUtc: '2026-10-11T07:00:01+00:00',
    toUtc: '2026-10-11T12:00:01+00:00',
    vendors: [codex(rounds, extra)],
    people: [{ email: 'dev@example.com', vendors: [codex(rounds, extra)] }],
  });

  return {
    window: 'week',
    vendors: [codex(40, { runsWithoutRound: 3, retries: 2, repairs: 1, followUps: 4, cancelled: 5, noBudget: 6 })],
    daily: { fromUtc: '', toUtc: '', days: [] },
    windows: [
      window('5h', 3),
      window('24h', 12),
      window('week', 40, { runsWithoutRound: 3, retries: 2, repairs: 1, followUps: 4, cancelled: 5, noBudget: 6 }),
    ],
  };
}

/** What a server older than story 5.1 answers to the same request: 200, TODAY's range, no windows and no counts. */
function older(): Usage {
  return {
    window: 'today',
    fromUtc: '2026-10-11T00:00:00+00:00',
    toUtc: '2026-10-11T12:00:01+00:00',
    vendors: [{ vendor: 'codex', runs: 7, failed: 0, tokensIn: 1, tokensOut: 1, seconds: 1, models: [] }],
    daily: { fromUtc: '', toUtc: '', days: [] },
  };
}

test('the parser reads every count of every round window by value, the people\'s rows too', () => {
  const read = serverRounds(newer());

  assert.equal(read.kind, 'counted');
  assert.deepEqual(read.windows.map((one) => [one.window, one.vendors[0]?.rounds]), [['5h', 3], ['24h', 12], ['week', 40]]);
  const week = read.windows[2]!.vendors[0]!;
  assert.deepEqual(
    [week.runsWithoutRound, week.retries, week.repairs, week.followUps, week.cancelled, week.noBudget],
    [3, 2, 1, 4, 5, 6]);
  assert.deepEqual(week.launchesByKind, { review: 40 });
  assert.equal(read.windows[0]!.people[0]!.vendors[0]!.rounds, 3);
  assert.deepEqual([...ROUND_WINDOWS], ['5h', '24h', 'week']);
});

test('AnOlderServer_ShowsUnavailable_NotZero — a 200 with today\'s range and no counts is "count unavailable", never 0', () => {
  const read = serverRounds(older());
  const text = pageTree(teamRoundsBlock(older(), () => 'var(--c)')).text();

  assert.equal(read.kind, 'unavailable');
  assert.match(text, /count unavailable/);
  assert.doesNotMatch(text, /\b0\b/, 'a server that cannot count rounds drew a zero');
  assert.match(text, /does not count rounds/, 'the reason is said, not left to guess');
});

test('a window missing among the three is unavailable, not a row of zeroes', () => {
  const partial = { ...newer(), windows: newer().windows!.filter((one) => one.window !== '24h') };

  assert.equal(serverRounds(partial).kind, 'unavailable');
});

test('a vendor row without its counts, or with a count that is not a whole number, makes the read unavailable', () => {
  const withoutCounts = newer();
  const bare = { ...withoutCounts, windows: withoutCounts.windows!.map((one) => ({ ...one, vendors: [{ vendor: 'codex', runs: 1, failed: 0, tokensIn: 1, tokensOut: 1, seconds: 1 }] })) };
  const wrong = { ...newer(), windows: newer().windows!.map((one) => ({ ...one, vendors: one.vendors.map((row) => ({ ...row, rounds: '3' })) })) };

  assert.equal(serverRounds(bare).kind, 'unavailable');
  assert.equal(serverRounds(wrong as unknown).kind, 'unavailable');
  assert.equal(serverRounds(undefined).kind, 'unavailable');
});

test('a window the server answered with no vendor in it is a counted zero — the server measured it', () => {
  const quiet = { ...newer(), windows: newer().windows!.map((one) => (one.window === '5h' ? { ...one, vendors: [], people: [] } : one)) };

  const text = pageTree(teamRoundsBlock(quiet, () => 'var(--c)')).text();

  assert.match(text, /5 h 0 · 24 h 12 · 7 d 40/);
});

test('the block draws server rounds per vendor for 5 h / 24 h / 7 d, and the week\'s requests and refusals apart', () => {
  const text = pageTree(teamRoundsBlock(newer(), () => 'var(--c)')).text();

  assert.match(text, /codex/);
  assert.match(text, /5 h 3 · 24 h 12 · 7 d 40/);
  assert.match(text, /3 runs without a round/);
  assert.match(text, /2 retries · 1 repair · 4 follow-ups · 5 cancelled · 6 budget refusals/);
});

test('a hostile vendor id renders as text', () => {
  const hostile = newer();
  const evil = '<img src=x onerror=alert(1)>';
  const swapped = { ...hostile, windows: hostile.windows!.map((one) => ({ ...one, vendors: one.vendors.map((row) => ({ ...row, vendor: evil })) })) };

  const tree = pageTree(teamRoundsBlock(swapped, () => 'var(--c)'));

  assert.equal(tree.find((node) => node.tagName === 'IMG').length, 0);
  assert.match(tree.text(), /<img src=x/);
});
