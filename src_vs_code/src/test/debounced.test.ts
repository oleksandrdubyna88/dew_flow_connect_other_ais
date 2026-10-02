import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type Timer, WATCH_DEBOUNCE_MS, debounced, needsPoll } from '../debounced';

/**
 * The watchers' debounce (todo/PLAN_question_consultant.md, S4 acceptance 4 / A5) with a STOPPED clock: time
 * moves only when the test moves it, so "one refresh at 175 ms" is a statement about the code, not the machine.
 */

/** A clock that runs only on `advance`, firing what falls due in order — stricter than a real one: nothing fires by itself. */
function stoppedClock(): Timer & { now: () => number; advance: (ms: number) => void } {
  let now = 0;
  let next = 1;
  const due = new Map<number, { at: number; fire: () => void }>();

  return {
    now: () => now,
    set: (fire, ms) => {
      const id = next++;
      due.set(id, { at: now + ms, fire });
      return id;
    },
    clear: (handle) => { due.delete(handle as number); },
    advance: (ms) => {
      const until = now + ms;
      for (;;) {
        const first = [...due.entries()].filter(([, one]) => one.at <= until).sort(([, a], [, b]) => a.at - b.at)[0];
        if (first === undefined) {
          break;
        }
        due.delete(first[0]);
        now = first[1].at;
        first[1].fire();
      }
      now = until;
    },
  };
}

test('five events in 100 ms are ONE refresh, at 175 ms', () => {
  const clock = stoppedClock();
  const fired: number[] = [];
  const event = debounced(() => fired.push(clock.now()), WATCH_DEBOUNCE_MS, clock);

  for (let i = 0; i < 5; i += 1) {
    event();
    clock.advance(25);
  }
  assert.deepEqual(fired, [], 'nothing fires inside the window');
  clock.advance(1000);

  assert.deepEqual(fired, [175]);
});

test('an event after the window fired opens the next one — a change is never lost to the debounce', () => {
  const clock = stoppedClock();
  let fired = 0;
  const event = debounced(() => { fired += 1; }, WATCH_DEBOUNCE_MS, clock);

  event();
  clock.advance(200);
  event();
  clock.advance(200);

  assert.equal(fired, 2);
});

test('a cancelled debounce fires nothing — what a disposed watcher needs', () => {
  const clock = stoppedClock();
  let fired = 0;
  const event = debounced(() => { fired += 1; }, WATCH_DEBOUNCE_MS, clock);

  event();
  event.cancel();
  clock.advance(1000);

  assert.equal(fired, 0);
});

test('the window is the operator’s 150–200 ms', () => {
  assert.ok(WATCH_DEBOUNCE_MS >= 150 && WATCH_DEBOUNCE_MS <= 200);
});

test('the poll is kept for a UNC or \\\\wsl.localhost data folder, and for nothing local', () => {
  assert.equal(needsPoll(['\\\\wsl.localhost\\Ubuntu\\home\\me\\.local\\share\\coai-mcp']), true);
  assert.equal(needsPoll(['\\\\wsl$\\Ubuntu\\home\\me']), true);
  assert.equal(needsPoll(['\\\\nas\\share\\coai']), true);
  assert.equal(needsPoll(['//nas/share/coai']), true);
  assert.equal(needsPoll(['C:\\Users\\me\\AppData\\Local\\coai-mcp']), false);
  assert.equal(needsPoll(['/home/me/.local/share/coai-mcp']), false);
  assert.equal(needsPoll(['C:\\Users\\me\\AppData\\Local\\coai-mcp', '\\\\wsl.localhost\\Ubuntu\\x']), true, 'one remote directory among several');
  assert.equal(needsPoll([]), false);
});
