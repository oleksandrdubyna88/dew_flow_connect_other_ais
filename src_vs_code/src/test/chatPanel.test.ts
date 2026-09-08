import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatEntry, ChatPanels } from '../chatPanels';

/**
 * One conversation per tab, and disposal that reaches exactly one session.
 *
 * <p>The two-tabs-one-label case is here again, from the other side: `sessionKey.ts` decides that
 * they are two keys, and this file proves the registry then keeps them apart. Both halves have to
 * hold — a correct key handed to a registry that merged on label would fail just as silently.</p>
 *
 * <p>The rest of this file is what the code round of 2026-09-08 added: an identity that survives a
 * re-key, a label that can be corrected, and a deactivation that closes the tab as well as the
 * conversation behind it.</p>
 */

interface Fake {
  readonly entry: ChatEntry;
  revealed(): number;
  sessionDisposed(): number;
  panelDisposed(): number;
  posted(): readonly unknown[];
}

function fakes(): Fake {
  let reveals = 0;
  let sessionDisposals = 0;
  let panelDisposals = 0;
  const messages: unknown[] = [];

  return {
    entry: {
      id: {},
      panel: {
        reveal: () => { reveals += 1; },
        dispose: () => { panelDisposals += 1; },
        post: (message: unknown) => { messages.push(message); },
      },
      session: { dispose: () => { sessionDisposals += 1; } },
    },
    revealed: () => reveals,
    sessionDisposed: () => sessionDisposals,
    panelDisposed: () => panelDisposals,
    posted: () => messages,
  };
}

test('a tab with no conversation gets one', () => {
  const panels = new ChatPanels();

  const opened = panels.open({}, 'привет', () => fakes().entry);

  assert.strictEqual(opened.outcome, 'created');
  assert.strictEqual(panels.size, 1);
});

test('the same tab reveals what it already has, and builds nothing', () => {
  const panels = new ChatPanels();
  const key = {};
  const first = fakes();
  panels.open(key, 'привет', () => first.entry);

  let built = 0;
  const again = panels.open(key, 'привет', () => {
    built += 1;

    return fakes().entry;
  });

  assert.strictEqual(again.outcome, 'revealed');
  assert.strictEqual(built, 0, 'a second vendor process was started for a tab that had one');
  assert.strictEqual(first.revealed(), 1, 'the existing panel was not brought forward');
  assert.strictEqual(panels.size, 1);
});

test('two tabs sharing a label are two conversations', () => {
  const panels = new ChatPanels();

  panels.open({}, 'main', () => fakes().entry);
  panels.open({}, 'main', () => fakes().entry);

  assert.strictEqual(panels.size, 2, 'a namesake collapsed two conversations into one');
});

test('closing one panel ends its own session and nobody else’s', () => {
  const panels = new ChatPanels();
  const keyA = {};
  const keyB = {};
  const a = fakes();
  const b = fakes();
  panels.open(keyA, 'a', () => a.entry);
  panels.open(keyB, 'b', () => b.entry);

  panels.close(keyA);

  // A vendor process that outlives its tab is an authenticated child nobody can see and nobody will
  // stop; one that dies with a stranger's tab loses a conversation somebody is still reading.
  assert.strictEqual(a.sessionDisposed(), 1, 'the closed tab’s session was left running');
  assert.strictEqual(b.sessionDisposed(), 0, 'closing one tab ended another tab’s session');
  assert.strictEqual(panels.size, 1);
});

test('closing from the tab does NOT dispose the panel, because VS Code already has', () => {
  const panels = new ChatPanels();
  const key = {};
  const only = fakes();
  panels.open(key, 'привет', () => only.entry);

  panels.close(key);

  assert.strictEqual(only.panelDisposed(), 0, 'the panel was disposed twice over');
});

test('closing a tab that has nothing says so rather than throwing', () => {
  const panels = new ChatPanels();

  assert.strictEqual(panels.close({}), false);
});

test('deactivating closes the tab as well as the conversation behind it', () => {
  const panels = new ChatPanels();
  const a = fakes();
  const b = fakes();
  panels.open({}, 'a', () => a.entry);
  panels.open({}, 'b', () => b.entry);

  panels.closeAll();

  // Nobody has told VS Code about these panels, so a session disposed without its panel leaves a tab
  // open that answers nothing — a zombie whose composer still takes text. (gemini, the code round.)
  assert.strictEqual(a.sessionDisposed(), 1);
  assert.strictEqual(a.panelDisposed(), 1, 'a tab was left open with no conversation behind it');
  assert.strictEqual(b.sessionDisposed(), 1);
  assert.strictEqual(b.panelDisposed(), 1);
  assert.strictEqual(panels.size, 0);
});

test('the disposal a deactivation triggers finds nothing left to close', () => {
  // closeAll removes the entry BEFORE disposing the panel, so the onDidDispose it causes cannot
  // dispose the same session a second time. This asserts the order, not the intention.
  const panels = new ChatPanels();
  const key = {};
  const only = fakes();
  panels.open(key, 'привет', () => only.entry);

  panels.closeAll();

  assert.strictEqual(panels.close(key), false, 'the entry was still there when the panel closed');
  assert.strictEqual(only.sessionDisposed(), 1, 'the session was disposed twice');
});

test('a conversation can move onto a new tab object', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const only = fakes();
  panels.open(from, 'привет', () => only.entry);

  assert.strictEqual(panels.rekey(from, to), true);
  assert.strictEqual(panels.has(from), false);
  assert.strictEqual(panels.get(to), only.entry);
});

test('a re-key never overwrites a conversation that is already there', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const moving = fakes();
  const sitting = fakes();
  panels.open(from, 'a', () => moving.entry);
  panels.open(to, 'b', () => sitting.entry);

  assert.strictEqual(panels.rekey(from, to), false, 'the re-key ate an open conversation');
  assert.strictEqual(panels.get(to), sitting.entry);
  assert.strictEqual(panels.get(from), moving.entry, 'the refused move removed the source anyway');
  assert.strictEqual(panels.size, 2);
});

test('a conversation is found by its own id, whatever key its tab has moved to', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const only = fakes();
  panels.open(from, 'привет', () => only.entry);
  panels.rekey(from, to);

  // This is the whole reason the id exists: a panel's callbacks were created once, with the key the
  // tab had then. Looking up by that key after a re-key found nothing — sends dropped, and a close
  // that could not find its entry left the vendor process running. (gemini and codex.)
  assert.strictEqual(panels.entryOf(only.entry.id), only.entry);
  assert.strictEqual(panels.keyOf(only.entry.id), to);
});

test('an id nobody holds resolves to nothing rather than to somebody else', () => {
  const panels = new ChatPanels();
  panels.open({}, 'привет', () => fakes().entry);

  assert.strictEqual(panels.entryOf({}), undefined);
  assert.strictEqual(panels.keyOf({}), undefined);
});

test('a renamed tab can be relabelled, so the fallback still recognises it', () => {
  const panels = new ChatPanels();
  const key = {};
  panels.open(key, 'old name', () => fakes().entry);

  assert.strictEqual(panels.relabel(key, 'new name'), true);
  assert.deepStrictEqual(panels.known(), [{ key, label: 'new name' }]);
});

test('relabelling a tab that has no conversation says so', () => {
  const panels = new ChatPanels();

  assert.strictEqual(panels.relabel({}, 'anything'), false);
});

test('a state pushed to one conversation is not seen by the other', () => {
  const panels = new ChatPanels();
  const keyA = {};
  const keyB = {};
  const a = fakes();
  const b = fakes();
  panels.open(keyA, 'a', () => a.entry);
  panels.open(keyB, 'b', () => b.entry);

  panels.get(keyA)?.panel.post({ type: 'state', running: true });

  // An answer that lands in the wrong tab is the same silent failure as a follow-up asked of the
  // wrong conversation, arriving from the other direction.
  assert.deepStrictEqual(a.posted(), [{ type: 'state', running: true }]);
  assert.deepStrictEqual(b.posted(), []);
});

test('the registry can describe itself to the key resolver', () => {
  const panels = new ChatPanels();
  const key = {};
  panels.open(key, 'привет', () => fakes().entry);

  assert.deepStrictEqual(panels.known(), [{ key, label: 'привет' }]);
});

test('a conversation can be closed by its own id, from the hook that holds one', () => {
  const panels = new ChatPanels();
  const from = {};
  const to = {};
  const only = fakes();
  panels.open(from, 'привет', () => only.entry);
  panels.rekey(from, to);

  // Every hook is passed an id, so without this each of them would have to bridge id to key before
  // closing - an asymmetric boundary, and where a second cleanup path grows. (gemini, round 2.)
  assert.strictEqual(panels.closeById(only.entry.id), true);
  assert.strictEqual(panels.size, 0);
  assert.strictEqual(only.sessionDisposed(), 1);
});

test('closing by an id nobody holds says so rather than closing somebody else', () => {
  const panels = new ChatPanels();
  const only = fakes();
  panels.open({}, 'привет', () => only.entry);

  assert.strictEqual(panels.closeById({}), false);
  assert.strictEqual(panels.size, 1);
  assert.strictEqual(only.sessionDisposed(), 0);
});

test('the id index does not survive its conversation', () => {
  const panels = new ChatPanels();
  const key = {};
  const only = fakes();
  panels.open(key, 'привет', () => only.entry);

  panels.close(key);

  // A stale index entry would resolve an id to a key whose conversation is gone, and the next look
  // would hand back undefined from a map that still claimed to know it.
  assert.strictEqual(panels.entryOf(only.entry.id), undefined);
  assert.strictEqual(panels.keyOf(only.entry.id), undefined);
});

test('deactivation clears the id index too', () => {
  const panels = new ChatPanels();
  const only = fakes();
  panels.open({}, 'привет', () => only.entry);

  panels.closeAll();

  assert.strictEqual(panels.entryOf(only.entry.id), undefined);
});
