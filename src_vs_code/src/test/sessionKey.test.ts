import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CLAUDE_PANEL_VIEW_TYPE, KnownPanel, SessionMatch, TabSnapshot, sourceSession } from '../sessionKey';

/**
 * The test the gate asked for by name, twice, from two different vendors.
 *
 * <p>`twoTabsOneLabel` is the whole reason this module exists: the first version of the plan keyed a
 * conversation by the tab's LABEL and called the collision an accepted risk. Two reviewers refused
 * that independently, and the failure they described is the worst kind — a follow-up that is
 * answered, correctly, about somebody else's question.</p>
 */

let made = 0;
function tab(label: string, viewType: string = CLAUDE_PANEL_VIEW_TYPE): TabSnapshot {
  // A fresh object every call: identity is the point, so two tabs must never accidentally be one.
  made += 1;
  return { key: { tab: made }, label, viewType };
}

function panelFor(t: TabSnapshot): KnownPanel {
  return { key: t.key, label: t.label };
}

test('a Claude Code tab is recognised by its view type', () => {
  const claude = tab('привет');

  const match = sourceSession(claude, [claude], []);

  assert.deepStrictEqual(match?.kind, 'new');
  assert.strictEqual(match?.key, claude.key);
});

test('an active tab that is not a Claude Code panel yields nothing', () => {
  const editor = tab('extension.ts', 'default');

  assert.strictEqual(sourceSession(editor, [editor], []), undefined);
});

test('a group with no active tab yields nothing', () => {
  assert.strictEqual(sourceSession(undefined, [], []), undefined);
});

test('the same tab reveals the panel it already has', () => {
  const claude = tab('привет');
  const known = [panelFor(claude)];

  const match = sourceSession(claude, [claude], known);

  assert.strictEqual(match?.kind, 'existing');
  assert.strictEqual(match?.key, claude.key);
});

test('two tabs sharing one label are two sessions, not one', () => {
  const first = tab('main');
  const second = tab('main');
  const known = [panelFor(first)];

  const match = sourceSession(second, [first, second], known);

  assert.strictEqual(match?.kind, 'new', 'the second tab was collapsed into the first tab’s panel');
  assert.strictEqual(match?.key, second.key);
  assert.notStrictEqual(match?.key, first.key);
});

test('a panel whose tab is gone is re-attached by label, and only then', () => {
  const before = tab('привет');
  const known = [panelFor(before)];
  // Same label, new object, and the old object is nowhere in the snapshot — a re-created tab.
  const after: TabSnapshot = { key: { tab: 'after' }, label: 'привет', viewType: CLAUDE_PANEL_VIEW_TYPE };

  const match = sourceSession(after, [after], known);

  assert.strictEqual(match?.kind, 'rekey');
  assert.strictEqual((match as Extract<SessionMatch, { kind: 'rekey' }>).from, before.key);
  assert.strictEqual(match?.key, after.key);
});

test('a label match does NOT re-key while the original tab is still open', () => {
  const original = tab('main');
  const known = [panelFor(original)];
  const other: TabSnapshot = { key: { tab: 'other' }, label: 'main', viewType: CLAUDE_PANEL_VIEW_TYPE };

  const match = sourceSession(other, [original, other], known);

  assert.strictEqual(match?.kind, 'new', 'an open tab’s panel was stolen by a namesake');
});

test('a renamed tab keeps its panel through identity, without needing the label', () => {
  const claude = tab('old name');
  const known = [panelFor(claude)];
  const renamed: TabSnapshot = { ...claude, label: 'new name' };

  const match = sourceSession(renamed, [renamed], known);

  assert.strictEqual(match?.kind, 'existing', 'a rename lost the conversation');
  assert.strictEqual(match?.label, 'new name', 'the panel title did not follow the rename');
});
