import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  NOWHERE,
  backTo,
  bufferGone,
  fileFailed,
  focusGroupCommand,
  recordedRoute,
  recoveredTab,
  sessionFailed,
  tabNotActivated,
  tabReachable,
  terminalRoad,
} from '../chatReturn';
import { sourceOfFile, sourceOfSession, sourceOfTerminal } from '../chatStore';
import { CLAUDE_PANEL_VIEW_TYPE, type TabSnapshot } from '../sessionKey';
import type { LiveTerminal } from '../terminalSource';

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

// ---------------------------------------------------------------------------------------------
// When the chat's own key is no longer a tab (our own code review): splitting or joining editor groups
// rebuilds the host's tab model, so the Tab object a chat was registered under can stop being one of
// the tabs while the tab itself is plainly still on screen.
// ---------------------------------------------------------------------------------------------

const claudeTab = (label: string): TabSnapshot => ({ key: {}, label, viewType: CLAUDE_PANEL_VIEW_TYPE, scheme: '', uri: '' });
const fileTab = (uri: string, label = 'notes.md'): TabSnapshot => ({ key: {}, label, viewType: '', scheme: 'file', uri });

test('a file chat whose tab object was replaced finds the tab showing its file', () => {
  const tabs = [claudeTab('Fix the build'), fileTab('file:///d%3A/work/notes.md')];

  assert.equal(recoveredTab(tabs, sourceOfFile('file:///d%3A/work/notes.md'), 'notes.md'), tabs[1]);
});

test('a Claude chat that recorded no session finds the ONE Claude tab still wearing its name', () => {
  const tabs = [claudeTab('Fix the build'), claudeTab('Write the docs'), fileTab('file:///x.md', 'Fix the build')];

  assert.equal(recoveredTab(tabs, { kind: 'none' }, 'Fix the build'), tabs[0]);
});

test('a chat that RECORDED its session is not re-matched by name: the session road is exact', () => {
  const tabs = [claudeTab('Write the docs')];

  assert.equal(recoveredTab(tabs, sourceOfSession('9f1c-uuid'), 'Write the docs'), undefined);
});

test('two Claude tabs of one name are not guessed between', () => {
  const tabs = [claudeTab('Fix the build'), claudeTab('Fix the build')];

  assert.equal(recoveredTab(tabs, { kind: 'none' }, 'Fix the build'), undefined);
});

test('nothing is recovered when nothing shows the source', () => {
  assert.equal(recoveredTab([claudeTab('Other')], sourceOfFile('file:///gone.md'), 'gone.md'), undefined);
  assert.equal(recoveredTab([fileTab('file:///a.md', 'Fix')], { kind: 'none' }, 'Fix'), undefined,
    'a DOCUMENT tab wearing the name of a Claude chat was taken for its session');
});

test('an unsaved buffer that is gone is a refusal, never a fresh empty one of the same name', () => {
  // showTextDocument on an untitled: uri that is not open CREATES a new empty buffer — no error to catch.
  assert.match(bufferGone('untitled:Untitled-1', []), /no longer open/);
  assert.equal(bufferGone('untitled:Untitled-1', ['untitled:Untitled-1']), '');
  assert.equal(bufferGone('file:///d%3A/work/notes.md', []), '', 'a file on disk is opened, not refused');
});

// ---------------------------------------------------------------------------------------------
// A chat opened from a terminal (E1.S2): back to its own terminal, then to the ONE of its name.
// ---------------------------------------------------------------------------------------------

const N = 'Creds: ai1-dev2 / small-tasks';
const live = (key: object, name: string, exited = false): LiveTerminal => ({ key, name, exited });

test('with no live tab, a terminal source goes back to its terminal by its full name', () => {
  const road = { kind: 'terminal', name: N };

  assert.deepEqual(backTo({ liveTab: false, source: sourceOfTerminal(N) }), road, 'a chat from a terminal goes nowhere');
  assert.deepEqual(recordedRoute(sourceOfTerminal(N)), road);
});

test('a Claude tab that wears the chat\u2019s name is never taken for the terminal it came from', () => {
  const all = [claudeTab('main'), fileTab('file:///d%3A/work/notes.md')];

  assert.equal(recoveredTab(all, sourceOfTerminal('main'), 'main'), undefined, 'a Claude tab was taken for the terminal');
  // The companion, same tabs: a chat that recorded NOTHING does take the one Claude tab of its name.
  assert.equal(recoveredTab(all, { kind: 'none' }, 'main'), all[0]);
});

test('the chat\u2019s own terminal is shown, also when a namesake is open', () => {
  const [a, b] = [{}, {}];

  assert.deepEqual(terminalRoad(a, [live(a, N), live(b, N)], N), { kind: 'show', key: a });
});

test('the chat\u2019s own terminal is shown by its key after it was renamed', () => {
  const a = {};

  assert.deepEqual(terminalRoad(a, [live(a, 'other')], N), { kind: 'show', key: a });
});

test('a restored chat goes to the ONE live terminal of its recorded name', () => {
  const [b, c] = [{}, {}];

  assert.deepEqual(terminalRoad({}, [live(b, N), live(c, 'x')], N), { kind: 'show', key: b });
});

test('no live terminal of that name is said in the plan\u2019s sentence', () => {
  const [a, b, c] = [{}, {}, {}];
  const sentence = 'The terminal \u201cCreds: ai1-dev2 / small-tasks\u201d is not open.';

  assert.deepEqual(terminalRoad({}, [live(c, 'x')], N), { kind: 'refuse', sentence });
  assert.deepEqual(terminalRoad(a, [live(a, N, true)], N), { kind: 'refuse', sentence }, 'an exited terminal was shown');
  assert.deepEqual(terminalRoad(a, [live(a, N, true), live(b, N)], N), { kind: 'show', key: b });
});

test('several terminals of one name are counted and never picked', () => {
  const [a, b, c] = [{}, {}, {}];

  assert.deepEqual(
    terminalRoad({}, [live(a, 'bash'), live(b, 'bash')], 'bash'),
    { kind: 'refuse', sentence: '2 terminals are called \u201cbash\u201d \u2014 open the one you mean.' },
  );
  assert.deepEqual(
    terminalRoad({}, [live(a, 'bash'), live(b, 'bash'), live(c, 'bash')], 'bash'),
    { kind: 'refuse', sentence: '3 terminals are called \u201cbash\u201d \u2014 open the one you mean.' },
  );
});
