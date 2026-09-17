import assert from 'node:assert/strict';
import { test } from 'node:test';
import { oneAtATime } from '../oneAtATime';

/**
 * A door that is already open does not open twice.
 *
 * <p><b>The defect this exists for.</b> The keyboard path to *Chat with other AI* probed the host —
 * `windowsReach()`, about a second in a remote window — BEFORE it showed any progress. A person who
 * presses a shortcut and watches nothing happen presses it again, which is how one question becomes
 * two. Showing the progress earlier makes the wait legible and does **not** fix it: both presses
 * arrive while the first is awaiting, both pass the entry point, and both start a capture.</p>
 *
 * <p>So the latch is what fixes it, and the progress is what makes it comprehensible. The two are
 * separate changes to the same line and only one of them was in the story as first written.</p>
 *
 * <p><b>Why it is a module rather than a flag in `chatCapture`.</b> There are already three
 * hand-rolled latches here — `chatGotoCommand`, `chatStoreCache` and `bugzReviewPanel` — and a fourth
 * written in place would be a fourth thing to get the `finally` wrong in. This one is a value, so the
 * rules below are asserted rather than described. The three that predate it are not converted by this
 * change; that is named in the plan rather than done in passing.</p>
 */

/** A promise and the handle to settle it from outside, which is how a test holds a door open. */
function held<T>(): { readonly promise: Promise<T>; resolve: (value: T) => void; reject: (reason: Error) => void } {
  let resolve = (_: T): void => undefined;
  let reject = (_: Error): void => undefined;
  const promise = new Promise<T>((yes, no) => {
    resolve = yes;
    reject = no;
  });

  return { promise, resolve, reject };
}

test('a second press while the first is still running is refused, not queued', async () => {
  const door = oneAtATime('busy');
  const first = held<string>();
  let runs = 0;
  const work = (): Promise<string> => {
    runs += 1;

    return first.promise;
  };

  const one = door(work);
  const two = await door(work);

  assert.equal(two, 'busy', 'a second press started a second capture while the first was still running');
  assert.equal(runs, 1, 'the work ran twice for one door');
  first.resolve('done');
  assert.equal(await one, 'done', 'the press that DID get through lost its answer');
});

test('the door opens again once the first has finished', async () => {
  // The companion the refusal needs. A latch that is never released refuses the case above for ever,
  // which is worse than the defect: the shortcut would work exactly once per window.
  const door = oneAtATime('busy');

  assert.equal(await door(() => Promise.resolve('first')), 'first');
  assert.equal(await door(() => Promise.resolve('second')), 'second',
    'the door never opened again, so the shortcut works once and then never');
});

test('a throw releases the door, or one failure wedges the command for the life of the window', async () => {
  // The `finally`, asserted rather than assumed. This is the one that is got wrong in a hand-rolled
  // latch, and the symptom — a shortcut that silently stops working after something went wrong once —
  // is the kind nobody reports because nobody connects the two events.
  const door = oneAtATime('busy');

  await assert.rejects(door(() => Promise.reject(new Error('the host would not answer'))), /would not answer/u);

  assert.equal(await door(() => Promise.resolve('after')), 'after',
    'a failed press wedged the door, so the command is dead until the window is reloaded');
});

test('the latch is taken SYNCHRONOUSLY, so two presses in one tick cannot both pass', async () => {
  // The case the fix exists for, at its narrowest. Two keypresses can arrive in the same tick; if the
  // latch were taken after an await — even an already-resolved one — both would see it open.
  const door = oneAtATime('busy');
  const first = held<string>();
  let runs = 0;

  const both = [door(() => { runs += 1; return first.promise; }), door(() => { runs += 1; return first.promise; })];
  first.resolve('done');
  const answers = await Promise.all(both);

  assert.equal(runs, 1, 'two presses in one tick both started work');
  assert.deepEqual(answers, ['done', 'busy'], 'the second press was not refused');
});
