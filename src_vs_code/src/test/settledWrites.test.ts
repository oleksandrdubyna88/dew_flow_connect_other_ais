import assert from 'node:assert/strict';
import { test } from 'node:test';
import { settledWrites } from '../settledWrites';

/**
 * The two rules that keep a tab from losing what somebody typed into it.
 *
 * <p>They shipped inside `rolesPanel.ts`, where nothing could reach them: driving them needed a
 * webview, and this suite has no extension host. Moving them into `settledWrites.ts` for the phrases
 * tab is what made them testable, and these are the tests that were owed.</p>
 *
 * <p>The timers are injected, so a test does not wait 300 ms to find out what a timer would have
 * done — it fires it.</p>
 */

interface Recorded {
  readonly applied: string[];
  readonly rendered: () => number;
  readonly reported: () => unknown[];
  readonly fire: () => void;
  readonly pending: () => number;
}

/** A writer with hand-driven timers, recording what it did. */
function harness(options: { readonly apply?: (command: string) => Promise<boolean> } = {}) {
  const applied: string[] = [];
  const reported: unknown[] = [];
  let rendered = 0;
  const timers = new Map<number, () => void>();
  let next = 1;

  const writes = settledWrites<string>({
    apply: async (command) => {
      applied.push(command);

      return options.apply === undefined ? true : options.apply(command);
    },
    render: () => { rendered += 1; },
    report: (error) => { reported.push(error); },
    // A command beginning `type:` is somebody typing in a field: `type:<field>=<value>`. The key is
    // the FIELD alone — keying it by the whole command would make every keystroke its own field, and
    // the test would then prove nothing while appearing to.
    fieldOf: (command) => (command.startsWith('type:') ? command.slice('type:'.length).split('=')[0] : undefined),
    setTimer: (run) => {
      const id = next++;
      timers.set(id, run);

      return id;
    },
    clearTimer: (timer) => { timers.delete(timer as number); },
  });

  const recorded: Recorded = {
    applied,
    rendered: () => rendered,
    reported: () => reported,
    fire: () => {
      const due = [...timers.entries()];
      timers.clear();
      for (const [, run] of due) {
        run();
      }
    },
    pending: () => timers.size,
  };

  return { writes, recorded };
}

test('a field somebody is typing in is stored once, when they stop — not once per keystroke', async () => {
  const { writes, recorded } = harness();

  for (const value of ['d', 'de', 'dep', 'depl', 'deplo', 'deploy']) {
    writes.queue(`type:text=${value}`);
  }

  assert.deepStrictEqual(recorded.applied, [], 'a keystroke was stored while the person was still typing');
  recorded.fire();
  await writes.flush();
  assert.deepStrictEqual(recorded.applied, ['type:text=deploy'],
    'six keystrokes were six writes, and each one is a configuration change every window reacts to');
});

test('two fields both survive — the settling is keyed by field, not by the last thing typed', async () => {
  const { writes, recorded } = harness();

  writes.queue('type:name=Ship');
  writes.queue('type:text=make a pr');
  recorded.fire();
  await writes.flush();

  assert.deepStrictEqual(recorded.applied.sort(), ['type:name=Ship', 'type:text=make a pr'],
    'typing in one field and then another stored only one of them');
});

test('a structural command stores what is still settling first, in the order it was typed', async () => {
  const { writes, recorded } = harness();

  writes.queue('type:name=Ship');
  writes.queue('type:text=deploy');
  writes.queue('remove');
  await writes.flush();

  assert.deepStrictEqual(recorded.applied, ['type:name=Ship', 'type:text=deploy', 'remove'],
    'a redraw replaced a half-typed field with what the file still said');
  assert.equal(recorded.pending(), 0, 'a timer was left to fire after the command it belonged before');
});

test('writes land one at a time, so an earlier keystroke cannot overwrite a later one', async () => {
  const order: string[] = [];
  // Made BEFORE anything is queued: capturing it inside `apply` would leave it undefined at the
  // moment the test releases it, and the test would then wait for itself forever.
  let release = (): void => { /* replaced below */ };
  const held = new Promise<void>((resolve) => { release = resolve; });
  const { writes } = harness({
    apply: async (command) => {
      order.push(`start ${command}`);
      if (command === 'first') {
        await held;
      }
      order.push(`end ${command}`);

      return false;
    },
  });

  writes.queue('first');
  writes.queue('second');
  await new Promise<void>((resolve) => { setImmediate(resolve); });
  assert.deepStrictEqual(order, ['start first'], 'the second write began while the first was still in flight');
  release();
  await writes.flush();

  assert.deepStrictEqual(order, ['start first', 'end first', 'start second', 'end second'],
    'two writes overlapped, so whichever resolved last wins and a field reverts under the person');
});

test('closing the tab stores what was still settling, rather than dropping it', async () => {
  const { writes, recorded } = harness();

  writes.queue('type:text=half a sentence');
  await writes.flush();

  assert.deepStrictEqual(recorded.applied, ['type:text=half a sentence'],
    'text somebody typed was lost because they closed the tab before it settled');
});

test('a write that fails is reported, and the next one still runs', async () => {
  const { writes, recorded } = harness({
    apply: async (command) => {
      if (command === 'bad') {
        throw new Error('the settings file is read-only');
      }

      return true;
    },
  });

  writes.queue('bad');
  writes.queue('good');
  await writes.flush();

  assert.equal(recorded.reported().length, 1, 'a failed save was swallowed');
  assert.deepStrictEqual(recorded.applied, ['bad', 'good'], 'one failure stopped every write after it');
});

test('the page is redrawn only when the command asked for it', async () => {
  const { writes, recorded } = harness({ apply: async (command) => command === 'add' });

  writes.queue('type:text=typing');
  recorded.fire();
  await writes.flush();
  assert.equal(recorded.rendered(), 0, 'typing redrew the page, which moves the caret out of the box');

  writes.queue('add');
  await writes.flush();
  assert.equal(recorded.rendered(), 1, 'adding a row did not redraw the list it changed');
});
