import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';

/**
 * One conversation per tab, and disposal that reaches exactly one session.
 *
 * <p>The two-tabs-one-label case is here again, from the other side: `sessionKey.ts` decides that
 * they are two keys, and this file proves the registry then keeps them apart. Both halves have to
 * hold — a correct key handed to a registry that merged on label would fail just as silently.</p>
 */

function fakes(label: string): { entry: ChatEntry; revealed: () => number; disposed: () => number } {
  let reveals = 0;
  let disposals = 0;

  return {
    entry: {
      label,
      panel: { reveal: () => { reveals += 1; }, dispose: () => undefined },
      session: { dispose: () => { disposals += 1; } },
    },
    revealed: () => reveals,
    disposed: () => disposals,
  };
}

test('a tab with no conversation gets one', () => {
  const panels = new ChatPanels();
  const first = fakes('привет');

  const opened = panels.open({}, () => first.entry);

  assert.strictEqual(opened.outcome, 'created');
  assert.strictEqual(panels.size, 1);
});

test('the same tab reveals what it already has, and builds nothing', () => {
  const panels = new ChatPanels();
  const key = {};
  const first = fakes('привет');
  panels.open(key, () => first.entry);

  let built = 0;
  const again = panels.open(key, () => {
    built += 1;

    return fakes('привет').entry;
  });

  assert.strictEqual(again.outcome, 'revealed');
  assert.strictEqual(built, 0, 'a second vendor process was started for a tab that had one');
  assert.strictEqual(first.revealed(), 1, 'the existing panel was not brought forward');
  assert.strictEqual(panels.size, 1);
});

test('two tabs sharing a label are two conversations', () => {
  const panels = new ChatPanels();
  const one = fakes('main');
  const two = fakes('main');

  panels.open({}, () => one.entry);
  panels.open({}, () => two.entry);

  assert.strictEqual(panels.size, 2, 'a namesake collapsed two conversations into one');
});

test('closing one panel ends its own session and nobody else’s', () => {
  const panels = new ChatPanels();
  const keyA = {};
  const keyB = {};
  const a = fakes('a');
  const b = fakes('b');
  panels.open(keyA, () => a.entry);
  panels.open(keyB, () => b.entry);

  panels.close(keyA);

  // A vendor process that outlives its tab is an authenticated child nobody can see and nobody will
  // stop; one that dies with a stranger's tab loses a conversation somebody is still reading.
  assert.strictEqual(a.disposed(), 1, 'the closed tab’s session was left running');
  assert.strictEqual(b.disposed(), 0, 'closing one tab ended another tab’s session');
  assert.strictEqual(panels.size, 1);
});

test('closing a tab that has nothing says so rather than throwing', () => {
  const panels = new ChatPanels();

  assert.strictEqual(panels.close({}), false);
});

test('deactivating ends every session', () => {
  const panels = new ChatPanels();
  const a = fakes('a');
  const b = fakes('b');
  panels.open({}, () => a.entry);
  panels.open({}, () => b.entry);

  panels.closeAll();

  assert.strictEqual(a.disposed(), 1);
  assert.strictEqual(b.disposed(), 1);
  assert.strictEqual(panels.size, 0);
});

test('a conversation can move onto a new tab object', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const only = fakes('привет');
  panels.open(from, () => only.entry);

  assert.strictEqual(panels.rekey(from, to), true);
  assert.strictEqual(panels.has(from), false);
  assert.strictEqual(panels.get(to), only.entry);
});

test('a re-key never overwrites a conversation that is already there', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const moving = fakes('a');
  const sitting = fakes('b');
  panels.open(from, () => moving.entry);
  panels.open(to, () => sitting.entry);

  assert.strictEqual(panels.rekey(from, to), false, 'the re-key ate an open conversation');
  assert.strictEqual(panels.get(to), sitting.entry);
  assert.strictEqual(panels.size, 2);
});

test('the registry can describe itself to the key resolver', () => {
  const panels = new ChatPanels();
  const key = {};
  panels.open(key, () => fakes('привет').entry);

  assert.deepStrictEqual(panels.known(), [{ key, label: 'привет' }]);
});
