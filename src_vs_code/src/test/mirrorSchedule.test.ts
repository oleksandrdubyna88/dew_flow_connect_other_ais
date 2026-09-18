import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ATTEMPTS, Clock, MirrorSchedule, Pending, WAITS_MS } from '../mirrorSchedule';
import { SyncOutcome } from '../serverSettingsSync';

/**
 * The settings mirror trying again, and saying when it stops.
 *
 * <p>Every case here is RUN. `MirrorSchedule` takes the sync, the timer and the reporters as
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

/**
 * A sync that does not answer until a test says so — the slow lock, which is where the races are.
 *
 * <p>`answering` cannot reach them: it resolves within the same turn, so an attempt is never in
 * flight when the next `start()` arrives, and that is exactly the window five reviewers named.</p>
 */
function heldSync(): { sync: () => Promise<SyncOutcome>; answer(outcome: SyncOutcome): void; calls(): number } {
  const waiting: ((outcome: SyncOutcome) => void)[] = [];
  let call = 0;

  return {
    sync: (): Promise<SyncOutcome> => {
      call += 1;

      return new Promise<SyncOutcome>((resolve) => {
        waiting.push(resolve);
      });
    },
    answer: (outcome: SyncOutcome): void => {
      const next = waiting.shift();

      assert.ok(next !== undefined, 'no attempt is waiting for an answer');
      next(outcome);
    },
    calls: () => call,
  };
}

/** Let every pending promise settle — the schedule awaits its sync before arming the next timer. */
const settle = async (): Promise<void> => {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
};

/** The four arguments, with the two reporters collecting what they were told. */
function scheduleFor(
  sync: () => Promise<SyncOutcome>,
  clock: Clock,
): {
  schedule: MirrorSchedule;
  readonly said: string[];
  readonly back: string[];
  readonly settled: string[];
} {
  const said: string[] = [];
  const back: string[] = [];
  const settled: string[] = [];

  return {
    said,
    back,
    settled,
    schedule: new MirrorSchedule(
      sync,
      clock,
      (outcome) => said.push(outcome),
      () => back.push('recovered'),
      (outcome) => settled.push(outcome),
    ),
  };
}

test('a write that lands needs no second attempt and says nothing', async () => {
  const clock = heldClock();
  const sync = answering('written');
  const { schedule, said, back } = scheduleFor(sync, clock);

  schedule.start();
  await settle();

  assert.equal(sync.calls(), 1, 'one attempt is enough');
  assert.equal(clock.armed(), 0, 'and nothing is waiting to fire');
  assert.deepEqual(said, []);
  assert.deepEqual(back, [], 'nothing was ever reported, so there is nothing to take back');
});

test('a lock that is busy every time reports ONCE, after the last attempt and not before', async () => {
  // Two halves, and a test with only one of them passes a broken build: asserting "it reports"
  // passes a build that reports on the first blip, and asserting "it tried three times" passes one
  // that never reports at all.
  const clock = heldClock();
  const sync = answering('busy');
  const { schedule, said } = scheduleFor(sync, clock);

  schedule.start();
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
  const sync = answering('busy', 'written');
  const { schedule, said, back } = scheduleFor(sync, clock);

  schedule.start();
  await settle();
  clock.tick();
  await settle();

  assert.equal(sync.calls(), 2);
  assert.deepEqual(said, [], 'the lock was held for a moment, which is not worth telling anybody');
  assert.deepEqual(back, [], 'and a recovery from something nobody was told about is not news either');
  assert.equal(clock.armed(), 0);
});

test('a settings change SUPERSEDES the pending schedule rather than racing it', async () => {
  const clock = heldClock();
  const sync = answering('busy');
  const { schedule, said } = scheduleFor(sync, clock);

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

test('an attempt still IN FLIGHT when a change supersedes it does nothing when it answers', async () => {
  // The window `answering` cannot reach, and the one five reviewers named from four roles: `cancel`
  // drops a pending TIMER and can never drop an attempt already inside `await sync()`. Without a
  // generation the old attempt came back afterwards, armed a timer of its own against the new
  // schedule's counter, and two ladders then shared one.
  const clock = heldClock();
  const slow = heldSync();
  const { schedule, said } = scheduleFor(slow.sync, clock);

  schedule.start();
  await settle();
  assert.equal(slow.calls(), 1, 'the first attempt is in flight');

  schedule.start();
  await settle();
  assert.equal(slow.calls(), 2, 'and the change started one of its own without waiting');

  slow.answer('busy');
  await settle();

  assert.equal(clock.armed(), 0,
    'the superseded attempt armed a timer, so two ladders are now running against one counter');
  assert.deepEqual(said, []);

  // The live attempt still behaves exactly as it should.
  slow.answer('busy');
  await settle();
  assert.equal(clock.armed(), 1, 'the attempt that IS this schedule armed its wait');
});

test('an attempt in flight when the window closes cannot wake up inside a host that has gone', async () => {
  const clock = heldClock();
  const slow = heldSync();
  const { schedule, said } = scheduleFor(slow.sync, clock);

  schedule.start();
  await settle();
  schedule.cancel();
  slow.answer('failed');
  await settle();

  assert.equal(clock.armed(), 0, 'a cancelled schedule armed a timer after the host asked it to stop');
  assert.deepEqual(said, []);
});

test('busy and failed are two conditions, and reporting one does not silence the other', async () => {
  const clock = heldClock();
  const first = scheduleFor(answering('busy'), clock);

  first.schedule.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();

  assert.deepEqual(first.said, ['busy']);

  const second = scheduleFor(answering('failed'), clock);

  second.schedule.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();

  assert.deepEqual(second.said, ['failed'], 'the second condition is its own');
});

test('the same condition twice over is said once, until a write clears it', async () => {
  const clock = heldClock();
  const answers: SyncOutcome[] = ['failed'];
  const { schedule, said, back } = scheduleFor(async () => answers[0] as SyncOutcome, clock);

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

  // It writes. The condition is over, the person who was told is told that too, and the next
  // failure is news rather than a repeat.
  answers[0] = 'written';
  schedule.start();
  await settle();

  assert.deepEqual(back, ['recovered'], 'the write that ended a reported condition said nothing');

  answers[0] = 'failed';
  await wholeSchedule();

  assert.deepEqual(said, ['failed', 'failed'], 'a condition that came back is said again');
  assert.deepEqual(back, ['recovered'], 'and the recovery was announced once, not again');
});

test('a stand-down is NOT a write: it clears nothing and tries nothing', async () => {
  // The only finding all three vendors raised. `outcome !== 'busy' && outcome !== 'failed'` treated
  // `stood-down` exactly like `written`, so a newer build owning the file cleared a failure reported
  // a moment earlier — and that failure's return was then silenced as a repeat, with no write ever
  // having landed.
  const clock = heldClock();
  const answers: SyncOutcome[] = ['failed'];
  const { schedule, said, back } = scheduleFor(async () => answers[0] as SyncOutcome, clock);

  const wholeSchedule = async (): Promise<void> => {
    schedule.start();
    await settle();
    clock.tick();
    await settle();
    clock.tick();
    await settle();
  };

  await wholeSchedule();
  assert.deepEqual(said, ['failed']);

  answers[0] = 'stood-down';
  schedule.start();
  await settle();

  assert.equal(clock.armed(), 0, 'a stand-down was retried, and no number of retries can cure one');
  assert.deepEqual(back, [], 'a stand-down was announced as a recovery, which it is the opposite of');

  answers[0] = 'failed';
  await wholeSchedule();

  assert.deepEqual(said, ['failed'],
    'the stand-down cleared the reported condition, so the same failure was reported twice with no '
    + 'write in between');
});

test('an unchanged file IS the configuration reaching the server, so it clears', async () => {
  const clock = heldClock();
  const answers: SyncOutcome[] = ['failed'];
  const { schedule, said, back } = scheduleFor(async () => answers[0] as SyncOutcome, clock);

  schedule.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();
  assert.deepEqual(said, ['failed']);

  answers[0] = 'unchanged';
  schedule.start();
  await settle();

  assert.deepEqual(back, ['recovered'],
    'nothing to write means the file already holds what this window has, which is the condition ending');
});

test('a sync that THROWS is the failure it is, not an unhandled rejection', async () => {
  // `sync()` is typed to answer an outcome, and a module whose whole subject is silence may not
  // assume that. Without the catch the rejection escaped `void this.once()`, the schedule stopped
  // with nothing armed, and nobody was told anything at all.
  const clock = heldClock();
  let calls = 0;
  const { schedule, said } = scheduleFor(async () => {
    calls += 1;

    throw new Error('EACCES, open settings.json');
  }, clock);

  schedule.start();
  await settle();
  clock.tick();
  await settle();
  clock.tick();
  await settle();

  assert.equal(calls, ATTEMPTS, 'a throw stopped the ladder instead of counting as an attempt');
  assert.deepEqual(said, ['failed'], 'the throw reached nobody');
});

test('cancel drops the pending attempt, which is what a closing window does', async () => {
  const clock = heldClock();
  const sync = answering('busy');
  const { schedule, said } = scheduleFor(sync, clock);

  schedule.start();
  await settle();
  schedule.cancel();

  assert.equal(clock.armed(), 0, 'nothing is left ticking');
  assert.equal(sync.calls(), 1);
  assert.deepEqual(said, [], 'and a schedule that was dropped reports nothing');
});

test('every TERMINAL outcome is reported once, and a retry in progress is not one', async () => {
  // What a role deletion waits for, and the reason it does not ask the mirror itself: writing a
  // setting fires the configuration listener, which starts this schedule, so a deletion calling
  // `sync()` moments later is answered `busy` while the listener's own attempt goes on to succeed.
  // The two halves both matter - told on every terminal outcome, and NOT told per attempt - because
  // a deletion told about each `busy` would write a reason it replaces two seconds later.
  const clock = heldClock();
  const answers: SyncOutcome[] = ['busy'];
  const one = scheduleFor(async () => answers[0] as SyncOutcome, clock);

  one.schedule.start();
  await settle();
  assert.deepEqual(one.settled, [], 'a retry still in progress was reported as an outcome');

  clock.tick();
  await settle();
  assert.deepEqual(one.settled, [], 'nor after the second attempt');

  clock.tick();
  await settle();
  assert.deepEqual(one.settled, ['busy'], 'the exhausted ladder told nobody it had stopped');

  answers[0] = 'written';
  one.schedule.start();
  await settle();
  assert.deepEqual(one.settled, ['busy', 'written']);

  answers[0] = 'stood-down';
  one.schedule.start();
  await settle();
  assert.deepEqual(one.settled, ['busy', 'written', 'stood-down'],
    'a stand-down is terminal too, and a deletion waiting on it would wait for ever');

  answers[0] = 'unchanged';
  one.schedule.start();
  await settle();
  assert.deepEqual(one.settled, ['busy', 'written', 'stood-down', 'unchanged'],
    'nothing to write still means the server has what the settings say');
});
