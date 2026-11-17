import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';
import { revealBoundWith } from '../chatReveal';

/**
 * Where a conversation is revealed and where it is moved — the road both go-to's reopen and the picker's accept take (E3.S2, S2-T8).
 *
 * <p>Over a real `ChatPanels` with panels that count their reveals. A conversation under a live terminal is never moved onto its
 * namesake; one under a key that is not a terminal (restored by a reload, reopened by the picker) is.</p>
 */

/** A registry entry whose panel counts its reveals. */
function entry(reveals: { count: number }): ChatEntry {
  return {
    id: {},
    panel: { reveal: () => { reveals.count += 1; }, dispose: () => undefined, post: () => undefined, isActive: () => false },
    session: { dispose: () => undefined },
  };
}

function registered(key: object): { readonly panels: ChatPanels; readonly reveals: { count: number } } {
  const panels = new ChatPanels();
  const reveals = { count: 0 };
  panels.open(key, 'h / s', () => entry(reveals));
  reveals.count = 0;

  return { panels, reveals };
}

test('S2-T8: a conversation is never moved from one live terminal onto its namesake', () => {
  const t1 = {};
  const t2 = {};
  const { panels, reveals } = registered(t1);

  revealBoundWith(panels, t1, t2, (key) => key === t1 || key === t2);

  assert.ok(panels.get(t1) !== undefined, "a terminal's conversation was moved onto its namesake");
  assert.equal(panels.get(t2), undefined, 'the namesake was given a conversation it does not own');
  assert.equal(reveals.count, 1, 'the conversation was not revealed where it is');
});

test('S2-T8: a conversation under a key that is not a terminal is moved onto the terminal, and onto nothing it is only revealed', () => {
  const orphan = {};
  const terminal = {};
  const moved = registered(orphan);

  revealBoundWith(moved.panels, orphan, terminal, (key) => key === terminal);

  assert.equal(moved.panels.get(orphan), undefined, 'a restored conversation was not moved off its key of its own');
  assert.ok(moved.panels.get(terminal) !== undefined, 'a restored conversation did not take its terminal');
  assert.equal(moved.reveals.count, 1);

  const alone = registered(orphan);
  revealBoundWith(alone.panels, orphan, undefined, () => false);
  assert.ok(alone.panels.get(orphan) !== undefined, 'with nowhere to go the conversation moved');
  assert.equal(alone.reveals.count, 1);

  const same = registered(terminal);
  revealBoundWith(same.panels, terminal, terminal, () => true);
  assert.ok(same.panels.get(terminal) !== undefined);
  assert.equal(same.reveals.count, 1);
});
