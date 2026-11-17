import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { mock, test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';
import { revealBoundWith } from '../chatReveal';
import { terminalOfWindow } from '../terminalSource';

/**
 * Where a conversation is revealed and where it is moved — the road both go-to's reopen and the picker's accept take (E3.S2, S2-T8).
 *
 * <p>Over a real `ChatPanels` with panels whose `reveal` is a mock function, so the calls are read from the mock's own list and
 * nothing here is counted by mutating a variable. A conversation under a terminal of the window — live or exited — is never moved onto
 * its namesake; one under a key that is not a terminal (restored by a reload, reopened by the picker) is.</p>
 */

/** A registry entry whose panel's `reveal` is the given mock. */
const entry = (reveal: () => void): ChatEntry => ({
  id: {},
  panel: { reveal, dispose: () => undefined, post: () => undefined, isActive: () => false },
  session: { dispose: () => undefined },
});

/** A registry holding one conversation under `key`, and the mock its panel reveals through. */
function registered(key: object): { readonly panels: ChatPanels; readonly reveal: ReturnType<typeof mock.fn> } {
  const panels = new ChatPanels();
  const reveal = mock.fn();
  panels.open(key, 'h / s', () => entry(reveal));

  return { panels, reveal };
}

test('S2-T8: a conversation is never moved from one live terminal onto its namesake', () => {
  const t1 = {};
  const t2 = {};
  const { panels, reveal } = registered(t1);

  revealBoundWith(panels, t1, t2, (key) => key === t1 || key === t2);

  assert.ok(panels.get(t1) !== undefined, "a terminal's conversation was moved onto its namesake");
  assert.equal(panels.get(t2), undefined, 'the namesake was given a conversation it does not own');
  assert.equal(reveal.mock.callCount(), 1, 'the conversation was not revealed where it is');
});

test('S2-T8: a conversation under an EXITED terminal of the window is revealed, not moved', () => {
  const exited = {};
  const live = {};
  const window = [{ key: exited, name: 'Creds: h / s', exited: true }, { key: live, name: 'Creds: h / s', exited: false }];
  const { panels, reveal } = registered(exited);

  revealBoundWith(panels, exited, live, (key) => terminalOfWindow(window, key));

  assert.ok(panels.get(exited) !== undefined, "an exited terminal's conversation was moved onto its live namesake");
  assert.equal(panels.get(live), undefined, 'the live namesake was given a conversation it does not own');
  assert.equal(reveal.mock.callCount(), 1);
});

test('S2-T8: a conversation under a key that is not a terminal is moved onto the terminal, and onto nothing it is only revealed', () => {
  const orphan = {};
  const terminal = {};
  const moved = registered(orphan);

  revealBoundWith(moved.panels, orphan, terminal, (key) => key === terminal);

  assert.equal(moved.panels.get(orphan), undefined, 'a restored conversation was not moved off its key of its own');
  assert.ok(moved.panels.get(terminal) !== undefined, 'a restored conversation did not take its terminal');
  assert.equal(moved.reveal.mock.callCount(), 1);

  const alone = registered(orphan);
  revealBoundWith(alone.panels, orphan, undefined, () => false);
  assert.ok(alone.panels.get(orphan) !== undefined, 'with nowhere to go the conversation moved');
  assert.equal(alone.reveal.mock.callCount(), 1);

  const same = registered(terminal);
  revealBoundWith(same.panels, terminal, terminal, () => true);
  assert.ok(same.panels.get(terminal) !== undefined);
  assert.equal(same.reveal.mock.callCount(), 1);
});
