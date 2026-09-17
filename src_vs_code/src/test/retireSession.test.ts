import assert from 'node:assert/strict';
import { test } from 'node:test';
import { retire } from '../retireSession';

/**
 * A failed disposal must not take the directory release down with it.
 *
 * <p><b>The defect this exists for.</b> Three places closed a conversation as two bare statements —
 * `thread.session.dispose(); thread.home.release();` — so a throw from the first meant the second
 * never ran: a vendor process AND its temporary directory outliving the tab that owned them, with
 * nothing said. At two of the three it is worse than a leak, because `chatLaunch` and `chatTurn`
 * install a replacement session on the very next lines.</p>
 *
 * <p><b>Measured before it was written.</b> Of the four sites the plan named, `ended()` in
 * `chatArchive.ts` was ALREADY correct — guarded at its own code round, with the reason recorded
 * beside it. So this is that version lifted out rather than a fourth opinion, and these cases are
 * the rules it had been keeping silently.</p>
 *
 * <p><b>What no test here can reach:</b> that a real vendor process actually died. That needs a real
 * child, which needs a real session, which needs an editor. What IS asserted is that neither cleanup
 * can swallow the other and that both failures are reported — which is the difference between a
 * silent double failure and a leak somebody can find.</p>
 */

/** A thread-shaped thing whose two cleanups can be made to fail on demand. */
function aThread(breaks: { dispose?: boolean; release?: boolean } = {}): {
  readonly thread: { readonly session: { dispose: () => void }; readonly home: { release: () => void } };
  readonly done: string[];
} {
  const done: string[] = [];

  return {
    done,
    thread: {
      session: {
        dispose: (): void => {
          done.push('dispose');
          if (breaks.dispose === true) {
            throw new Error('the process would not go');
          }
        },
      },
      home: {
        release: (): void => {
          done.push('release');
          if (breaks.release === true) {
            throw new Error('the directory is held open');
          }
        },
      },
    },
  };
}

test('a disposal that throws does not stop the directory being released', () => {
  const { thread, done } = aThread({ dispose: true });
  const said: string[] = [];

  retire(thread, (what) => said.push(what));

  assert.deepEqual(done, ['dispose', 'release'],
    'the release never ran, so a temporary directory outlives the tab along with the process');
  assert.equal(said.length, 1, 'the disposal failed and nothing said so');
  assert.match(said[0] ?? '', /outlive the tab/u,
    'the message does not say what the failure COSTS, only that something went wrong');
});

test('a release that throws is reported, and the disposal before it still happened', () => {
  const { thread, done } = aThread({ release: true });
  const said: string[] = [];

  retire(thread, (what) => said.push(what));

  assert.deepEqual(done, ['dispose', 'release']);
  assert.equal(said.length, 1, 'a directory that could not be released said nothing');
  assert.match(said[0] ?? '', /temp directory/u);
});

test('both failing is TWO reports, not one swallowing the other', () => {
  // The case a single try/catch around both would fail: it would report the first and never attempt
  // the second, which is the defect this exists for wearing a tidier shape.
  const { thread, done } = aThread({ dispose: true, release: true });
  const said: string[] = [];

  retire(thread, (what) => said.push(what));

  assert.deepEqual(done, ['dispose', 'release'], 'one failure stopped the other cleanup being tried');
  assert.equal(said.length, 2, 'two independent failures were reported as one');
});

test('the ordinary case is silent, and the order is disposal THEN release', () => {
  // The order is load-bearing and it is not alphabetical: a CLI still writing on its way out, into a
  // directory that has already been removed, throws where nobody is listening.
  const { thread, done } = aThread();
  const said: string[] = [];

  retire(thread, (what) => said.push(what));

  assert.deepEqual(done, ['dispose', 'release'], 'the directory was released before the process let go of it');
  assert.deepEqual(said, [], 'a clean close said something to somebody');
});
