import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  NOWHERE,
  backTo,
  fileFailed,
  focusGroupCommand,
  sessionFailed,
  tabNotActivated,
  tabReachable,
} from '../chatReturn';
import { sourceOfFile, sourceOfSession } from '../chatStore';

/**
 * Issue #314: the road back from a coai chat to where it came from. *Go to* takes a Claude Code tab to
 * its conversation; this takes the conversation back — to the very tab it was opened from while that tab
 * is open, and otherwise to what the conversation recorded.
 */

test('the tab the chat was opened from wins, even when the source was pinned to a session', () => {
  // Exact and self-sufficient: it is the very tab, it exists before the session walk pinned anything,
  // and going to it needs no other extension.
  assert.deepEqual(backTo({ liveTab: true, source: sourceOfSession('9f1c-uuid') }), { kind: 'tab' });
  assert.deepEqual(backTo({ liveTab: true, source: { kind: 'none' } }), { kind: 'tab' });
});

test('with no live tab, a Claude source goes back by its session', () => {
  assert.deepEqual(backTo({ liveTab: false, source: sourceOfSession('9f1c-uuid') }), { kind: 'session', sessionId: '9f1c-uuid' });
});

test('with no live tab, a file source goes back to its file', () => {
  assert.deepEqual(
    backTo({ liveTab: false, source: sourceOfFile('file:///d%3A/work/notes.md') }),
    { kind: 'file', uri: 'file:///d%3A/work/notes.md' },
  );
});

test('a chat that knows neither goes nowhere, and never guesses by title', () => {
  assert.deepEqual(backTo({ liveTab: false, source: { kind: 'none' } }), { kind: 'nowhere' });
});

test('the editor’s fixed focus commands reach eight groups, and no further', () => {
  assert.equal(focusGroupCommand(1), 'workbench.action.focusFirstEditorGroup');
  assert.equal(focusGroupCommand(8), 'workbench.action.focusEighthEditorGroup');
  for (const column of [0, 9, -1, 1.5, Number.NaN]) {
    assert.equal(focusGroupCommand(column), '', String(column));
  }
});

test('a tab is only attempted where the recipe can reach it', () => {
  assert.equal(tabReachable(1, 0), true);
  assert.equal(tabReachable(8, 12), true);
  // A group past the eighth has no fixed focus command, and -1 is a tab that moved or closed since the
  // snapshot: both fall through to the recorded source rather than focusing something else. (gemini,
  // the plan round.)
  assert.equal(tabReachable(9, 0), false);
  assert.equal(tabReachable(2, -1), false);
});

test('every refusal names what it is about', () => {
  assert.match(NOWHERE, /does not know where it came from/);
  assert.match(sessionFailed('9f1c-uuid', 'command not found'), /9f1c-uuid.*command not found/);
  assert.match(fileFailed('file:///d%3A/gone.md', 'ENOENT'), /gone\.md.*ENOENT/);
  assert.match(tabNotActivated('Claude Code'), /Claude Code/);
});
