import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ATTEMPTS, Clock, MirrorSchedule, Pending, WAITS_MS } from '../mirrorSchedule';
import { SyncOutcome } from '../serverSettingsSync';

/**
 * The settings mirror trying again, and saying when it stops.
 *
 * <p>Every case here is RUN. `MirrorSchedule` takes the sync, the timer and the reporter as
 * parameters for exactly that reason — a schedule tested by sleeping is slow when it passes and
 * flaky when it does not, and one tested by reading its source is not tested.</p>
 */

/** A clock whose timers fire only when a test says so, in the order they were armed. */
function heldClock(): Clock & { tick(): void; armed(): number; readonly waits: number[] } {
  const timers = new Map<number, () => void>();
  const waits: number[] = [];
  let next = 1;

  return {
    waits,
    later: (work, ms) => {
      waits.push(ms);
      timers.set(next, work);

      return next++;
    },
    stop: (pending: Pending) => {
      timers.delete(pending as number);
    },
    armed: () => timers.size,
    tick: () => {
      const [id, work] = [...timers][0] ?? [];
      assert.ok(work !== undefined, 'nothing is armed, so there is nothing to fire');
      timers.delete(id as number);
      work();
    },
  };
}

/** A sync that answers the given outcomes in order, and its last answer for ever after. */
function answering(...outcomes: readonly SyncOutcome[]): (() => Promise<SyncOutcome>) & { calls(): number } {
  let call = 0;
  const sync = async (): Promise<SyncOutcome> => {
    const answer = outcomes[Math.min(call, outcomes.length - 1)] as SyncOutcome;
    call += 1;

    return answer;
  };

  return Object.assign(sync, { calls: () => call });
}

/** Let every pending promise settle — the schedule awaits its sync before arming the next timer. */
const settle = async (): Promise<void> => { await Promise.resolve(); await Promise.resolve(); };

test('a write that lands needs no second attempt and says nothing', async () => {
  const clock = heldClock();
  const said: string[] = [];
  const sync = answering('written');

  new MirrorSchedule(sync, clock, (outcome) => said.push(outcome)).start();
  await settle();

  assert.equal(sync.calls(), 1, 'one attempt is enough');
  assert.equal(clock.armed(), 0, 'and nothing is waiting to fire');
  assert.deepEqual(said, []);
});

test('a lock that is busy every time reports ONCE, after the last attempt and not before', async () => {
  // Two halves, and a test with only one of them passes a broken build: asserting "it reports"
  // passes a build that reports on the first blip, and asserting "it tried three times" passes one
  // that never reports at all.
  const clock = heldClock();
  const said: string[] = [];
  const sync = answering('busy');
  new MirrorSchedule(sync, clock, (outcome) => said.push(outcome)).start();
  await settle();

  assert.equal(sync.calls(), 1);
  assert.deepEqual(said, [], 'nothing is said while there is still an attempt left');

  clock.tick();
  await settle();

  assert.equal(sync.calls(), 2);
  assert.deepEqual(said, [], 'nor after the second');

  clock.tick();
  await settle();

  assert.equal(sync.calls(), ATTEMPTS, 'three attempts, the first one counted');
  assert.deepEqual(said, ['busy'], 'and the report comes when there is nothing left to try');
  assert.equal(clock.armed(), 0, 'with no timer left ticking against a fault it cannot fix');
  assert.deepEqual(clock.waits, [...WAITS_MS], 'the waits are the ones the plan names, in order');
});

test('a write that recovers on a later attempt reports nothing at all', async () => {
  const clock = heldClock();
  const said: string[] = [];
  const sync = answering('busy', 'written');
  new MirrorSchedule(sync, clock, (outcome) => said.push(outcome)).start();
  await settle();
  clock.tick();
  await settle();

  assert.equal(sync.calls(), 2);
  assert.deepEqual(said, [], 'the lock was held for a moment, which is not worth telling anybody');
  assert.equal(clock.armed(), 0);
});

test('a settings change SUPERSEDES the pending schedule rather than racing it', async () => {
  // Two schedules would race each other's attempt counters, and the one that lost would report a
  // condition the other had already recovered from. (Two reviewers, the plan round.)
  const clock = heldClock();
  const said: string[] = [];
  const sync = answering('busy');
  const schedule = new MirrorSchedule(sync, clock, (outcome) => said.push(outcome));

  schedule.start();
  await settle();
  assert.equal(clock.armed(), 1, 'one attempt is waiting');

  schedule.start();
  await settle();

  assert.equal(clock.armed(), 1, 'still ONE — the pending timer was dropped, not joined');
  assert.equal(sync.calls(), 2, 'and the new schedule began with an attempt of its own');

  // And it really started again from the beginning: two more ticks, not one.
  clock.tick();
  await settle();
  assert.deepEqual(said, [], 'the second attempt of the NEW schedule, so nothing is said yet');
  clock.tick();
  await settle();
  assert.deepEqual(said, ['busy']);
});

test('busy and failed are two conditions, and reporting one does not silence the other', async () => {
  // One counter for both would let recovering from either hide the other — the eviction defect S4
  // already met, in a new place.
  const clock = heldClock();
  const said: string[] = [];
  const schedule = new MirrorSchedule(answering('busy'), clock, (outcome) => said.push(outcome));

  schedule.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();

  assert.deepEqual(said, ['busy']);

  const failing = new MirrorSchedule(answering('failed'), clock, (outcome) => said.push(outcome));
  failing.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();

  assert.deepEqual(said, ['busy', 'failed'], 'the second condition is its own');
});

test('the same condition twice over is said once, until a write clears it', async () => {
  const clock = heldClock();
  const said: string[] = [];
  const answers: SyncOutcome[] = ['failed'];
  const sync = async (): Promise<SyncOutcome> => answers[0] as SyncOutcome;
  const schedule = new MirrorSchedule(sync, clock, (outcome) => said.push(outcome));

  const wholeSchedule = async (): Promise<void> => {
    schedule.start();
    await settle();
    clock.tick();
    await settle();
    clock.tick();
    await settle();
  };

  await wholeSchedule();
  await wholeSchedule();

  assert.deepEqual(said, ['failed'], 'the same disk failing twice is one condition, not two');

  // It writes. The condition is over, and the next failure is news rather than a repeat.
  answers[0] = 'written';
  schedule.start();
  await settle();

  answers[0] = 'failed';
  await wholeSchedule();

  assert.deepEqual(said, ['failed', 'failed'], 'a condition that came back is said again');
});

test('cancel drops the pending attempt, which is what a closing window does', async () => {
  const clock = heldClock();
  const said: string[] = [];
  const sync = answering('busy');
  const schedule = new MirrorSchedule(sync, clock, (outcome) => said.push(outcome));

  schedule.start();
  await settle();
  schedule.cancel();

  assert.equal(clock.armed(), 0, 'nothing is left ticking');
  assert.equal(sync.calls(), 1);
  assert.deepEqual(said, [], 'and a schedule that was dropped reports nothing');
});
