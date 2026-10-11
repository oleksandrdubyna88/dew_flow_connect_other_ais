import assert from 'node:assert/strict';
import { test } from 'node:test';

import { parseRoundCounts, RoundCountsRead, roundCountsFrom } from '../roundCounts';
import { RoundCountsCache, ROUNDS_MAX_AGE_MS } from '../roundCountsCache';
import { cardRoundsLine } from '../roundsBlock';
import { counted, usageLine } from './roundCountsFixtures';

/**
 * Bounded freshness (story 2.2 of todo/PLAN_every_round_is_counted.md), on a fake clock: a render never spawns, a read
 * is at most once a minute, an ending — a round, a consultation, a question — brings the next read forward, and a
 * trailing window falls while nothing at all happens.
 */

class Clock {
  constructor(public now: number) {}

  readonly read = (): number => this.now;
}

/** A stand-in `coai-mcp` that counts its spawns and answers ONE round, five hours deep, as the mode would at `nowMs`. */
function binary(roundAtMs: number): { spawns: number; load: (nowMs: number) => Promise<{ read: RoundCountsRead; ledger: []; ledgerFailure: string }> } {
  const state = { spawns: 0 };

  return Object.assign(state, {
    load: async (nowMs: number) => {
      state.spawns += 1;
      const inFiveHours = roundAtMs > nowMs - 5 * 3_600_000 && roundAtMs <= nowMs ? 1 : 0;
      const window = (name: string, rounds: number) => ({
        name, fromUtc: '', toUtc: '', rows: rounds === 0 ? [] : [{ provider: 'codex', rounds: { plan: rounds, code: 0, document: 0, feature: 0 }, reviewers: rounds, models: [] }],
      });
      const json = JSON.stringify({ nowUtc: new Date(nowMs).toISOString(), windows: [window('5h', inFiveHours), window('24h', 1), window('7d', 1)], modelsKnownSince: null });
      assert.ok(parseRoundCounts(json) !== undefined, 'the stand-in answers outside the contract');

      return { read: roundCountsFrom({ code: 0, output: json }), ledger: [] as [], ledgerFailure: '' };
    },
  });
}

const T0 = Date.parse('2026-10-10T12:00:00Z');

/** One render as the panel does it: the line from what is cached, then a refresh that reads only when due. */
async function render(cache: RoundCountsCache): Promise<string> {
  const line = cardRoundsLine(cache.current(), 'codex');
  await cache.refresh();

  return line;
}

test('TheCardLine_DoesNotSpawnPerRender — twenty renders inside the minute are one spawn', async () => {
  const clock = new Clock(T0);
  const server = binary(T0 - 3_600_000);
  const cache = new RoundCountsCache(server.load, clock.read);

  assert.equal(await render(cache), '', 'before the first read the card says nothing — never a zero');
  for (let i = 0; i < 20; i += 1) {
    clock.now += 2_000;
    await render(cache);
  }

  assert.equal(server.spawns, 1, `a render spawned coai-mcp ${server.spawns} times inside one minute`);
  assert.equal(await render(cache), '7 days: 1 rounds · 0 consult turns · 5 h: 1');
});

test('AConsultationOnlySession_RefreshesTheCount — an ending brings the read forward, inside the minute', async () => {
  const clock = new Clock(T0);
  const server = binary(T0 - 3_600_000);
  const cache = new RoundCountsCache(server.load, clock.read);
  await cache.refresh();

  clock.now += 5_000;
  cache.forget(); // a consultation (or a question) ended: no round did
  assert.equal(await cache.refresh(), true, 'the ending did not make the count due');
  assert.equal(server.spawns, 2);
  clock.now += 5_000;
  assert.equal(await cache.refresh(), false, 'one ending is one read, not a read per render after it');
});

test('AnIdleWindow_FallsOnAFakeClock — with nothing running, the 5 h window lets a round go once it is older than 5 h', async () => {
  const clock = new Clock(T0);
  const server = binary(T0 - (5 * 3_600_000 - 30_000)); // thirty seconds inside the 5 h window
  const cache = new RoundCountsCache(server.load, clock.read);
  await cache.refresh();
  assert.match(cardRoundsLine(cache.current(), 'codex'), /5 h: 1$/u);

  clock.now += ROUNDS_MAX_AGE_MS; // a minute, and nothing happened at all
  await cache.refresh();

  assert.equal(server.spawns, 2, 'an idle minute did not read again, so the window could never fall');
  assert.match(cardRoundsLine(cache.current(), 'codex'), /5 h: 0$/u, 'the round stayed in the 5 h window after it left it');
});

test('a round ending (the session files hold one more finished round) reads again; the first sighting only remembers', async () => {
  const clock = new Clock(T0);
  const server = binary(T0 - 3_600_000);
  const cache = new RoundCountsCache(server.load, clock.read);
  cache.noteFinishedRounds(10);
  await cache.refresh();
  cache.noteFinishedRounds(10);
  clock.now += 1_000;
  assert.equal(await cache.refresh(), false, 'nothing ended, and the minute is not up');

  cache.noteFinishedRounds(11);
  assert.equal(await cache.refresh(), true, 'a round ended and the count stayed a minute old');
});

test('a read that throws is a snapshot saying so — stamped, so it is not retried on every render', async () => {
  const clock = new Clock(T0);
  let calls = 0;
  const cache = new RoundCountsCache(async () => { calls += 1; throw new Error('boom'); }, clock.read);
  await cache.refresh();
  await cache.refresh();

  assert.equal(calls, 1);
  assert.equal(cache.current()?.read.state, 'unavailable');
  assert.match(cache.current()?.ledgerFailure ?? '', /the read failed: Error: boom/u, 'a thrown load left the money looking like nothing spent');
  assert.equal(cardRoundsLine(cache.current(), 'codex'), 'Rounds: count unavailable');
});

test('the card counts consult turns from the cached ledger, for its own row only — and an unavailable count stays unavailable', () => {
  const consults = [usageLine({ kind: 'consult' }), usageLine({ kind: 'consult' }), usageLine({ provider: 'gemini', kind: 'consult' }), usageLine()];

  assert.equal(cardRoundsLine({ read: counted(), ledger: consults, ledgerFailure: '', atMs: T0 }, 'codex'), '7 days: 14 rounds · 2 consult turns · 5 h: 2');
  assert.equal(cardRoundsLine({ read: counted(), ledger: consults, ledgerFailure: '', atMs: T0 }, 'nobody'), '7 days: 0 rounds · 0 consult turns · 5 h: 0',
    'a row the count names nowhere ran nought rounds — that IS a count');
  assert.equal(cardRoundsLine({ read: { state: 'unavailable', why: 'x' }, ledger: consults, ledgerFailure: '', atMs: T0 }, 'codex'), 'Rounds: count unavailable',
    'an unavailable count is never drawn as rounds beside a consult count');
});
