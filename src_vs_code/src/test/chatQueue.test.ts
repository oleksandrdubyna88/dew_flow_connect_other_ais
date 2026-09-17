import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  MOST_WAITING, MOST_WAITING_CHARS, began, drained, isWanted, join, withdraw,
} from '../chatQueue';
import type { WaitingQuestion } from '../chatPage';

/**
 * What a queue of unasked questions does — the decisions, where a test can reach them.
 *
 * <p><b>Why a module of its own.</b> `chatCommand.ts` needs a `vscode` host this suite has none of,
 * so nothing in it can be executed here; the standing idiom for that is to keep the DECISIONS in a
 * pure module and leave the wiring in the command file, which is how `answerCopy`, `chatLedger` and
 * `chatUsage` are already arranged. A queue that cannot be driven by a test is a queue whose
 * ordering, ceiling and withdrawal rules are hoped at.</p>
 *
 * <p>The one that matters most is <b>withdrawal</b>. Taking a row off the page does not un-create
 * the callback that will run it — that was made the moment Send was pressed — so the thing that
 * begins a turn has to ask, by NAME, whether its question is still wanted. (codex, the plan round.)</p>
 */

const q = (id: string, text: string): WaitingQuestion => ({ id, text });

// ---------- joining ----------

test('a question joins the back of the queue, and the order is the order it was typed', () => {
  const one = join([], 'first', 'w1');
  assert.equal(one.kind, 'queued');
  const two = one.kind === 'queued' ? join(one.waiting, 'second', 'w2') : undefined;

  assert.ok(two && two.kind === 'queued');
  assert.deepEqual(two.waiting.map((w) => w.text), ['first', 'second']);
});

test('the id it is given is the id it keeps, because that is what withdraws it', () => {
  const joined = join([], 'ask', 'w7');

  assert.ok(joined.kind === 'queued');
  assert.equal(joined.id, 'w7');
  assert.equal(joined.waiting[0]?.id, 'w7');
});

test('two identical questions are two rows, and they do not share a fate', () => {
  // The same words typed twice is an ordinary thing to do. Keying on the text would make
  // withdrawing the second cancel the first, which is a question silently unasked.
  const first = join([], 'again?', 'w1');
  const second = first.kind === 'queued' ? join(first.waiting, 'again?', 'w2') : undefined;
  assert.ok(second && second.kind === 'queued');

  const after = withdraw(second.waiting, 'w2');

  assert.deepEqual(after.waiting.map((w) => w.id), ['w1']);
});

// ---------- the ceiling ----------

test('the queue has a ceiling, and the refusal names which limit was hit', () => {
  let waiting: readonly WaitingQuestion[] = [];
  for (let n = 0; n < MOST_WAITING; n += 1) {
    const joined = join(waiting, `question ${n}`, `w${n}`);
    assert.ok(joined.kind === 'queued', `question ${n} was refused below the ceiling`);
    waiting = joined.waiting;
  }

  const over = join(waiting, 'one too many', 'w99');

  assert.equal(over.kind, 'full');
  assert.ok(over.kind === 'full' && over.why.includes(String(MOST_WAITING)), 'the refusal does not name the limit');
});

test('and a ceiling on the WORDS too, because eight pasted files is not eight questions', () => {
  const huge = 'x'.repeat(MOST_WAITING_CHARS);
  const first = join([], huge, 'w1');
  assert.ok(first.kind === 'queued', 'one question at the limit is allowed');

  const over = first.kind === 'queued' ? join(first.waiting, 'and a little more', 'w2') : undefined;

  assert.ok(over && over.kind === 'full');
  assert.match(over.why, /long|large|size|words/iu, 'the refusal does not say it is about the size');
});

test('an empty question never joins the queue at all', () => {
  assert.equal(join([], '   ', 'w1').kind, 'full');
});

// ---------- withdrawing ----------

test('withdrawing takes that one out and hands its words back', () => {
  const waiting = [q('w1', 'first'), q('w2', 'second'), q('w3', 'third')];

  const after = withdraw(waiting, 'w2');

  assert.deepEqual(after.waiting.map((w) => w.id), ['w1', 'w3'], 'it took the wrong one, or more than one');
  assert.equal(after.returned, 'second', 'the words were dropped rather than returned');
});

test('withdrawing something that is not there changes nothing and returns nothing', () => {
  // A late press against a row whose turn has already begun. It must not be an error and it must
  // not put somebody else's words in the composer.
  const waiting = [q('w1', 'first')];

  const after = withdraw(waiting, 'gone');

  assert.deepEqual(after.waiting, waiting);
  assert.equal(after.returned, '');
});

test('a withdrawn question is no longer WANTED, which is what stops it running', () => {
  const waiting = [q('w1', 'first'), q('w2', 'second')];

  assert.equal(isWanted(waiting, 'w1'), true);
  assert.equal(isWanted(withdraw(waiting, 'w1').waiting, 'w1'), false);
});

// ---------- beginning ----------

test('a question leaves the queue as its turn begins, and only that one', () => {
  const waiting = [q('w1', 'first'), q('w2', 'second')];

  assert.deepEqual(began(waiting, 'w1').map((w) => w.id), ['w2']);
});

test('beginning something already gone is harmless', () => {
  const waiting = [q('w1', 'first')];

  assert.deepEqual(began(waiting, 'w2'), waiting);
});

// ---------- nothing typed is ever silently dropped ----------

test('a whole queue comes back oldest first, so a reset loses nothing', () => {
  // Three queued and New chat pressed. All three return: a count on its own is not a recovery, and
  // the first draft of this plan said "three dropped" while restoring one, which was simply false.
  const waiting = [q('w1', 'first'), q('w2', 'second'), q('w3', 'third')];

  assert.deepEqual(drained(waiting), ['first', 'second', 'third']);
});

test('an empty queue hands nothing back', () => {
  assert.deepEqual(drained([]), []);
});
