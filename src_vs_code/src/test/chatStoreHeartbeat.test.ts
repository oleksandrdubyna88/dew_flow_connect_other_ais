import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { test } from 'node:test';
import { ConversationHeartbeat, HeartbeatTimers } from '../chatStoreHeartbeat';
import { ChatStoreKeeper } from '../chatStoreKeeper';
import { HEARTBEAT_EVERY_MS, HOUSEKEEPING_DIR, heartbeatName, parseHeartbeat } from '../chatStoreSweep';

/**
 * This window's heartbeat writer, against a real directory and hand-driven timers.
 *
 * <p>What a heartbeat protects is `chatStoreSweep.test.ts`; this is that the file is written when it
 * must be — on the timer regardless, on a pulse only when the set changed — that it is never removed
 * by its own writer, and that a write which could not land is tried again on the next pulse.</p>
 */

const NOW = Date.UTC(2026, 8, 13, 12, 0, 0);

function home(): string {
  return mkdtempSync(join(tmpdir(), 'coai-chat-heartbeat-'));
}

/** A keeper whose beats are counted. */
class Counting extends ChatStoreKeeper {
  public beats = 0;

  public override beat(pid: number, ids: readonly string[], now: number): Promise<boolean> {
    this.beats += 1;

    return super.beat(pid, ids, now);
  }
}

/** Timers a test drives by hand: the interval's callback and period are kept, and stops are counted. */
function fakeTimers(): HeartbeatTimers & { runs: (() => void)[]; periods: number[]; stopped: unknown[] } {
  const runs: (() => void)[] = [];
  const periods: number[] = [];
  const stopped: unknown[] = [];

  return {
    runs,
    periods,
    stopped,
    every: (run, ms) => {
      runs.push(run);
      periods.push(ms);

      return { handle: runs.length };
    },
    stop: (handle) => {
      stopped.push(handle);
    },
  };
}

const written = (dir: string, pid: number): unknown => parseHeartbeat(JSON.parse(readFileSync(join(dir, HOUSEKEEPING_DIR, heartbeatName(pid)), 'utf8')));

test('a beat writes the heartbeat with what is held — once each, in one order — dated by the clock', async () => {
  const dir = home();
  try {
    const heartbeat = new ConversationHeartbeat(new Counting(dir), () => ['b2', 'a1', 'b2'], 7, fakeTimers(), () => NOW);

    assert.equal(await heartbeat.beat(), true);

    assert.deepEqual(written(dir, 7), { kind: 'heartbeat', beat: { pid: 7, at: NOW, ids: ['a1', 'b2'] } });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a pulse writes when the set of open conversations changed, and writes nothing when it did not', async () => {
  const dir = home();
  try {
    const keeper = new Counting(dir);
    let held: readonly string[] = ['a1'];
    const heartbeat = new ConversationHeartbeat(keeper, () => held, 7, fakeTimers(), () => NOW);

    heartbeat.pulse();
    heartbeat.pulse(); // coalesced with the one above
    await heartbeat.settled();
    assert.equal(keeper.beats, 1, 'a pulse did not write, or two pulses in one tick wrote twice');

    heartbeat.pulse();
    await heartbeat.settled();
    assert.equal(keeper.beats, 1, 'a pulse with nothing changed wrote the same list again');

    held = ['a1', 'b2'];
    heartbeat.pulse();
    await heartbeat.settled();
    assert.equal(keeper.beats, 2, 'a pulse after the set changed wrote nothing');
    assert.deepEqual(written(dir, 7), { kind: 'heartbeat', beat: { pid: 7, at: NOW, ids: ['a1', 'b2'] } });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the timer beats every minute whether or not anything changed, and a second start is a no-op', async () => {
  const dir = home();
  try {
    const keeper = new Counting(dir);
    const timers = fakeTimers();
    const heartbeat = new ConversationHeartbeat(keeper, () => ['a1'], 7, timers, () => NOW);

    heartbeat.start();
    heartbeat.start();
    assert.deepEqual(timers.periods, [HEARTBEAT_EVERY_MS], 'the timer was not set once with the heartbeat period');

    await heartbeat.beat();
    timers.runs[0]?.();
    await heartbeat.settled();
    assert.equal(keeper.beats, 2, 'the timer did not write although nothing had changed — the refresh IS the point');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('dispose stops the timer and LEAVES the file — a reload’s restores are protected by it', async () => {
  const dir = home();
  try {
    const timers = fakeTimers();
    const heartbeat = new ConversationHeartbeat(new Counting(dir), () => ['a1'], 7, timers, () => NOW);
    heartbeat.start();
    await heartbeat.beat();

    heartbeat.dispose();

    assert.deepEqual(timers.stopped, [{ handle: 1 }], 'the timer was not stopped');
    assert.equal(existsSync(join(dir, HOUSEKEEPING_DIR, heartbeatName(7))), true, 'the heartbeat file was removed on dispose, so a reload leaves its tabs unprotected');
    heartbeat.dispose();
    assert.equal(timers.stopped.length, 1, 'a second dispose stopped something else');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a beat that could not land leaves nothing announced, so the next pulse tries again', async () => {
  const dir = home();
  try {
    const asFile = join(dir, 'chat-conversations');
    writeFileSync(asFile, 'a file where the store should be', 'utf8');
    const keeper = new Counting(asFile);
    const heartbeat = new ConversationHeartbeat(keeper, () => ['a1'], 7, fakeTimers(), () => NOW);
    const before = console.error;
    console.error = () => undefined;
    try {
      assert.equal(await heartbeat.beat(), false, 'a heartbeat into a file reported success');
      heartbeat.pulse();
      await heartbeat.settled();
    } finally {
      console.error = before;
    }

    assert.equal(keeper.beats, 2, 'after a failed write the pulse believed the list was announced');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
