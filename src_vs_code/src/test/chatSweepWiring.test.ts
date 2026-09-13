import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That the sweep, the index and the heartbeat are actually WIRED — the half a unit test cannot reach.
 *
 * <p>`chatStoreHousekeeping.test.ts` drives the order against a real directory; what it cannot see is
 * whether `activate` calls it, on the same directory the store writes, after the migration, with the
 * registry as the source of what is held — and whether the chat pulses the heartbeat where the set of
 * open conversations changes. These read the two files that import `vscode`, the way every wiring
 * test here does.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the extension starts housekeeping on the store’s own directory, after the migration, fed by the registry', () => {
  const wiring = source('extension.ts');
  const start = wiring.indexOf('startHousekeeping({');

  assert.notEqual(start, -1, 'nothing starts the sweep, the index or the heartbeat — the store keeps everything for ever');
  const call = wiring.slice(start, start + 400);
  // ONE root. The keeper, the heartbeat and the sweep are all bound to the store's own directory by
  // `startHousekeeping`; a separate `dir` beside `store` was two roots that a future change could
  // let drift apart — the sweep surveying one while deleting from another. (The code round.)
  assert.doesNotMatch(call, /\bdir:/u, 'housekeeping is handed a directory beside the store — two roots that can drift apart');
  assert.match(call, /store: chatStore,/u, 'the sweep is handed a different store than the writers use');
  assert.match(call, /held: \(\) => heldConversationIds\(chatPanels\),/u, 'what this window holds open is not read from the registry, so a restored tab is not protected');
  assert.match(call, /after: migration,/u, 'the sweep does not wait for the migration — a record still in the memento reads as debris');
  assert.ok(wiring.indexOf('importLegacyTabs(') < start, 'housekeeping is started before the migration exists to wait for');
  assert.match(wiring, /pulseChatsThrough\(\(\) => \{\s*\n\s*housekeeping\.heartbeat\.pulse\(\);/u, 'the chat has no way to tell the heartbeat that its tabs changed');
  assert.match(wiring, /dispose: \(\) => \{\s*\n\s*housekeeping\.dispose\(\);/u, 'the heartbeat timer is never stopped');
});

test('the chat pulses the heartbeat at every place the set of open conversations changes', () => {
  const command = source('chatCommand.ts');
  const after = (anchor: string, within: number): string => {
    const at = command.indexOf(anchor);
    assert.notEqual(at, -1, `${anchor} is gone from chatCommand.ts`);

    return command.slice(at, at + within);
  };

  // The registration takes the tab *go to* bound it to since story C3, or its own key for every
  // other caller.
  assert.match(after('  panels.open(bindTo?.key ?? {}, bindTo?.label ?? saved.title, () => entry);', 200), /pulse\?\.\(\);/u, 'a restored conversation is not announced');
  assert.match(after('        threads.delete(id);', 80), /pulse\?\.\(\);/u, 'a closed conversation is still announced as open');
  assert.match(after('thread.saveId = randomUUID();', 4_000), /pulse\?\.\(\);/u, 'a conversation re-minted under a new id is announced under the old');
  // The pin takes the ENTRY rather than its id since story C1: it writes the conversation's source
  // when the session is found, and writing needs the entry the note would go to.
  const pinned = command.indexOf('pinSession(entry, state.title, state.fromSession)');
  assert.notEqual(pinned, -1, 'the pin after a new conversation is gone from chatCommand.ts');
  assert.match(command.slice(pinned - 400, pinned), /pulse\?\.\(\);/u, 'a newly opened conversation is not announced');
  assert.match(command, /export function heldConversationIds\(panels: ChatPanels\)/u, 'nothing reads the open conversations from the registry');
  assert.match(command, /export function pulseChatsThrough\(/u, 'the heartbeat cannot be bound');
});

test('the heartbeat is never removed by its own writer, and the sweep deletes a conversation only through the store', () => {
  const heartbeat = source('chatStoreHeartbeat.ts');
  assert.doesNotMatch(heartbeat, /removeHeartbeat|\brm\(|unlink/u, 'the heartbeat writer removes its own file, so a reload leaves its tabs unprotected');

  const keeper = source('chatStoreKeeper.ts');
  assert.doesNotMatch(keeper, /\.forget\(/u, 'the sweep deletes a conversation without the re-check under the lock');
  assert.match(keeper, /store\.retireIfExpired\(/u, 'the sweep deletes conversations some other way than the one lock-checked operation');
  assert.doesNotMatch(keeper, /lockName\(/u, 'the keeper builds lock paths of its own — a second lock-breaking path');
  assert.match(keeper, /claimConversation\(this\.dir, id, 'sweep abandoned lock'/u, 'a lock is collected other than by claiming through the lock module');
});
