import assert from 'node:assert/strict';
import { test } from 'node:test';

import { WriteQueue } from '../writeQueue';

/**
 * Two quick presses on a text control are two steps, not one (CodeRabbit on PR #615).
 *
 * <p>A press reads the setting, adds one step and writes it back. Started one after the other without
 * waiting, two presses both read the same value and both write the same next one — one press lost. The
 * hosts now run each press through a `WriteQueue`, whose `run` hands back the press's own outcome, so a
 * failed write is still reported where it happened and the next press still runs.</p>
 */

/** A setting held in memory, whose write takes a moment — the shape of `config.update`. */
function slowSetting(): { read: () => number; write: (value: number) => Promise<void> } {
  let value = 0;

  return {
    read: () => value,
    write: (next) => new Promise((resolve) => { setTimeout(() => { value = next; resolve(); }, 5); }),
  };
}

test('two presses in a row, each reading the setting and writing it back, both count', async () => {
  const setting = slowSetting();
  const queue = new WriteQueue();
  const press = (): Promise<void> => queue.run(async () => { await setting.write(setting.read() + 1); });

  await Promise.all([press(), press()]);

  assert.equal(setting.read(), 2, 'the second press read the value before the first had written it');
});

test('a press that fails reports its own failure, and the press after it still runs', async () => {
  const queue = new WriteQueue();
  const ran: string[] = [];

  const failed = queue.run(async () => { ran.push('first'); throw new Error('the setting could not be written'); });
  const next = queue.run(async () => { ran.push('second'); });

  await assert.rejects(failed, /could not be written/);
  await next;
  assert.deepEqual(ran, ['first', 'second']);
});
