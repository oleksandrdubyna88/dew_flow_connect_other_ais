import assert from 'node:assert/strict';
import { test } from 'node:test';
import { afterToggle, Card, nextOpenRounds, NOTHING_OPEN } from '../openRounds';
import { roundKey } from '../panelView';
import { RoundRecord } from '../rounds';

/**
 * Running is open, finished is closed, and what the person opened is never touched.
 *
 * <p>The operator's words, and the previous rule was the opposite of the middle one: a round that
 * finished stayed open "because that is the moment its reviewers are worth reading". True of one
 * round and wrong of a list — every round anybody had watched stayed expanded, and the panel became
 * a wall of open cards.</p>
 *
 * <p>None of this had a test before, because the policy lived inside the provider next to `vscode`.
 * That is why it shipped wrong and stayed wrong: it was asserted only by its own comment.</p>
 */

function card(over: Partial<RoundRecord> = {}, branch = 'main'): Card {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 0,
    reviewers: '',
    status: 'done',
    startedUtc: '2026-09-04T09:00:00.000Z',
    completedUtc: '2026-09-04T09:05:00.000Z',
    branch,
    ...over,
  } as Card;
}

const RUNNING = card({ status: 'running', completedUtc: '' });
const FINISHED = card();
const KEY = roundKey(RUNNING);

test('a round that starts running opens itself', () => {
  const state = nextOpenRounds(NOTHING_OPEN, [RUNNING]);

  assert.deepEqual([...state.open], [KEY]);
});

test('and closes itself again when it finishes', () => {
  const running = nextOpenRounds(NOTHING_OPEN, [RUNNING]);
  const done = nextOpenRounds(running, [FINISHED]);

  assert.deepEqual([...done.open], [], 'the panel opened it, so the panel closes it');
});

test('a card the person opened survives the round finishing', () => {
  // The half that matters most: their click takes the card away from the panel for good.
  const theirs = afterToggle(NOTHING_OPEN, KEY, true);
  const done = nextOpenRounds(theirs, [FINISHED]);

  assert.deepEqual([...done.open], [KEY], 'what a person opened is never closed for them');
});

test('a running card the person closed is not re-opened by the next tick', () => {
  const opened = nextOpenRounds(NOTHING_OPEN, [RUNNING]);
  const closed = afterToggle(opened, KEY, false);

  const tick = nextOpenRounds(closed, [RUNNING]);

  assert.deepEqual([...tick.open], [], 'five seconds later it must still be shut');
});

test('re-opening after the panel closed it makes it theirs again', () => {
  const done = nextOpenRounds(nextOpenRounds(NOTHING_OPEN, [RUNNING]), [FINISHED]);
  const reopened = afterToggle(done, KEY, true);

  const tick = nextOpenRounds(reopened, [FINISHED]);

  assert.deepEqual([...tick.open], [KEY]);
});

test('a round that was never running is never opened by the panel', () => {
  const state = nextOpenRounds(NOTHING_OPEN, [FINISHED]);

  assert.deepEqual([...state.open], []);
});

test('one running round does not open the others', () => {
  const other = card({ number: 2, status: 'done' });
  const state = nextOpenRounds(NOTHING_OPEN, [RUNNING, other]);

  assert.deepEqual([...state.open], [KEY]);
});

test('all three sets are pruned to rounds that still exist', () => {
  // None is large in a day's work, but each would otherwise grow for the life of the extension
  // host and be scanned on every five-second tick long after that round left the list. (codex.)
  const opened = afterToggle(nextOpenRounds(NOTHING_OPEN, [RUNNING]), KEY, true);

  const gone = nextOpenRounds(opened, [card({ number: 9 })]);

  assert.deepEqual([...gone.open], []);
  assert.deepEqual([...gone.autoOpened], []);
  assert.deepEqual([...gone.panelOpened], []);
});

test('a person closing a card does not forget that it was auto-opened', () => {
  // `autoOpened` answers "has this ever been opened for them"; forgetting it here would let the
  // next tick open a round they have just closed.
  const closed = afterToggle(nextOpenRounds(NOTHING_OPEN, [RUNNING]), KEY, false);

  assert.deepEqual([...closed.autoOpened], [KEY]);
  assert.deepEqual([...closed.panelOpened], []);
});
