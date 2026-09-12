import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CARRY_EVERYTHING, carriedFrom, carryMark } from '../chatCarry';
import { ChatMessage } from '../chatPage';

/**
 * Where a handed-over conversation begins.
 *
 * <p>The mark arrives from two places nobody controls — a webview's message and a JSON file on disk
 * — and sits in a transcript that shrinks from the end when a re-ask drops the answer nobody wanted.
 * So every one of these is a real state of the world rather than a hypothetical.</p>
 */

const said = (text: string): ChatMessage => ({ role: 'you', text });
const answered = (text: string): ChatMessage => ({ role: 'model', text });

const CONVERSATION: readonly ChatMessage[] = [
  said('the passage, fenced, about the first subject'),
  answered('an answer about the first subject'),
  said('a follow-up'),
  answered('another answer'),
  said('and now something else entirely'),
];

test('no mark carries the whole conversation, which is what every tab did before', () => {
  assert.deepStrictEqual(carriedFrom(CONVERSATION, CARRY_EVERYTHING), CONVERSATION);
  assert.strictEqual(CARRY_EVERYTHING, 0, 'the absent mark stopped meaning "carry everything"');
});

test('a mark carries what is BELOW it and nothing above', () => {
  // The button on the answer at index 3 posts 4 — the index of the first message carried.
  assert.deepStrictEqual(carriedFrom(CONVERSATION, 4), [said('and now something else entirely')]);
  // And the captured passage goes with everything else above the line. No special case does that:
  // the passage lives in the transcript as the first turn's material, so the slice excludes it.
  assert.ok(
    !carriedFrom(CONVERSATION, 4).some((one) => one.text.includes('the passage')),
    'the captured passage was carried past a mark set below it',
  );
});

test('a mark at the end carries nothing at all', () => {
  assert.deepStrictEqual(carriedFrom(CONVERSATION, CONVERSATION.length), []);
});

test('a mark past the end carries nothing, rather than reaching backwards', () => {
  // A re-ask drops the rejected answer AND its question — two from the end — which can leave a mark
  // beyond the last message. Nothing below it is the honest answer.
  assert.deepStrictEqual(carriedFrom(CONVERSATION.slice(0, -2), 5), []);
});

test('a NEGATIVE mark never reaches slice, which would read it from the end', () => {
  // slice(-1) carries the LAST message — the exact opposite of a suffix, and the one value that
  // turns this feature into its own inverse. It arrives from a webview and from a file on disk.
  // (codex, the plan round.)
  assert.deepStrictEqual(carriedFrom(CONVERSATION, -1), CONVERSATION);
  assert.strictEqual(carryMark(-1, 5), CARRY_EVERYTHING);
  assert.strictEqual(carryMark(-100, 5), CARRY_EVERYTHING);
});

test('anything that is not a whole number is no mark at all', () => {
  for (const bad of [1.5, Number.NaN, Number.POSITIVE_INFINITY, '2', null, undefined, {}, []]) {
    assert.strictEqual(carryMark(bad, 5), CARRY_EVERYTHING, `${String(bad)} was accepted as a mark`);
  }
});

test('a mark is clamped to the conversation it is in', () => {
  assert.strictEqual(carryMark(99, 5), 5, 'a mark past the end was not brought back to it');
  assert.strictEqual(carryMark(3, 5), 3);
  assert.strictEqual(carryMark(0, 0), 0, 'an empty conversation confused the clamp');
  assert.strictEqual(carryMark(2, 0), 0);
  // A length that is somehow negative cannot produce a negative mark.
  assert.strictEqual(carryMark(2, -1), 0);
});

test('an upper bound cuts the question off the end, in ONE slice', () => {
  // A Team server is handed everything below the mark and above the question it is about to ask. It
  // used to take two slices — one to drop the question, one to apply the mark — which allocated a
  // near-copy of the whole transcript on every single turn, and a Team conversation is handed one
  // every time. (codex, the code round, on the hot path.)
  assert.deepStrictEqual(carriedFrom(CONVERSATION, 2, CONVERSATION.length - 1), [
    said('a follow-up'),
    answered('another answer'),
  ]);
  // The bound is exclusive, and it cannot reach past the conversation or below its own start.
  assert.deepStrictEqual(carriedFrom(CONVERSATION, 0, 99), CONVERSATION);
  assert.deepStrictEqual(carriedFrom(CONVERSATION, 4, 2), []);
  assert.deepStrictEqual(carriedFrom(CONVERSATION, 0, -5), []);
});

test('the slice knows nothing about what a message IS', () => {
  // It was typed to the page's own ChatMessage and imported the renderer into a decision — policy
  // coupled to HTML. What this function knows about is positions. (codex, the code round.)
  assert.deepStrictEqual(carriedFrom([1, 2, 3, 4], 2), [3, 4]);
  assert.deepStrictEqual(carriedFrom(['a', 'b'], 1), ['b']);
});
