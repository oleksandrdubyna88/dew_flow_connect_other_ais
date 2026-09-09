import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatCommandOf } from '../chatMessages';

/**
 * What the page said, read without a host.
 *
 * <p>This file exists because of one finding on the plan round: every other test in epic 2 exercised
 * the pure registry and the pure page, while the only module that turned a webview message into an
 * action was the one no unit test could reach. A wrong message type would then ship with all of them
 * green — a person's send quietly ignored, or delivered somewhere else. So the reading moved into a
 * function, and this is that function under test.</p>
 */

test('a send carries its text, trimmed', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'send', text: '  why is it pinned?  ' }),
    { kind: 'send', text: 'why is it pinned?' },
  );
});

test('an empty send is ignored rather than spending a vendor turn on a stray Enter', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send', text: '   ' }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send' }), { kind: 'ignore' });
});

test('a pick carries its model id, and a nameless one is ignored', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'pick', id: 'remsoftdev-codex' }),
    { kind: 'pick', id: 'remsoftdev-codex' },
  );
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'pick', id: '' }), { kind: 'ignore' });
});

test('the zoom control posts its own shape, not a command', () => {
  // It is the shared control's message, and it has been that shape since the help page shipped.
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: -1 }), { kind: 'zoom', delta: -1 });
});

test('a zoom with nonsense in it moves nothing rather than throwing', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: 'lots' }), { kind: 'zoom', delta: 0 });
  assert.deepStrictEqual(chatCommandOf({ type: 'zoom', delta: Number.NaN }), { kind: 'zoom', delta: 0 });
});

test('the two capped actions are read', () => {
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'restart' }), { kind: 'restart' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'useLocal' }), { kind: 'useLocal' });
});

test('a page that trapped an error tells the host what it was', () => {
  assert.deepStrictEqual(
    chatCommandOf({ type: 'pageError', message: 'x is not defined' }),
    { kind: 'pageError', message: 'x is not defined' },
  );
  // Nothing to report is not a report.
  assert.deepStrictEqual(chatCommandOf({ type: 'pageError', message: '' }), { kind: 'ignore' });
});

test('a word this host does not know is ignored, not thrown on', () => {
  // A webview retained across a reload can be older than the extension talking to it - or newer.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'summarise' }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'somethingElse' }), { kind: 'ignore' });
});

test('nothing at all is ignored', () => {
  assert.deepStrictEqual(chatCommandOf(undefined), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({}), { kind: 'ignore' });
});

test('a message whose fields are the wrong types cannot become a command', () => {
  // The bridge is untyped: everything here arrived as JSON from a page.
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'send', text: 42 }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'pick', id: { id: 'x' } }), { kind: 'ignore' });
  assert.deepStrictEqual(chatCommandOf({ type: 42, command: 'send', text: 'hi' }), { kind: 'ignore' });
});

test('a stop carries the turn it means to stop', () => {
  // The number is what keeps a late stop from ending the turn AFTER the one it was pressed for: the
  // page renders the control against a turn, and the host refuses a number that is no longer running.
  assert.deepStrictEqual(
    chatCommandOf({ type: 'command', command: 'stop', turn: 3 }),
    { kind: 'stop', turn: 3 },
  );
});

test('a stop that names no turn is not a stop', () => {
  // There is no wildcard. The first version let a missing number mean "whichever is running", which
  // hands back the exact defect the number exists to prevent — and no such page can exist, because
  // `stop` and its turn number ship in the same release. (codex and gemini, the code round.)
  assert.deepStrictEqual(chatCommandOf({ type: 'command', command: 'stop' }), { kind: 'ignore' });
});

test('a stop naming something that is not a turn is refused, not applied to whatever is running', () => {
  // The boundary rule: a value from a surface the host does not control is validated before it
  // reaches anything that acts on it. Every one of these used to become "stop whatever is running".
  for (const turn of [-1, 0, 1.5, Number.NaN, Number.POSITIVE_INFINITY, Number.MAX_SAFE_INTEGER + 2, 'two', null, {}]) {
    assert.deepStrictEqual(
      chatCommandOf({ type: 'command', command: 'stop', turn }),
      { kind: 'ignore' },
      `a turn of ${String(turn)} should have been refused outright`,
    );
  }
});
