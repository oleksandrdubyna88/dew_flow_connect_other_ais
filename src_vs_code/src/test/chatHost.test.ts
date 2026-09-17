import assert from 'node:assert/strict';
import { test } from 'node:test';
import { keepChatsIn, memory, pulse, pulseChatsThrough, rememberChatsIn, retireMemento, store } from '../chatHost';
import { threads } from '../chatThread';
import type { ChatTabMemory } from '../chatTabs';
import type { ChatStoreFile } from '../chatStoreFile';
import type { Thread } from '../chatThread';

/**
 * The three things a window binds once, and the registry it binds them beside.
 *
 * <p><b>Why these are tested at all, which is the point of the split.</b> Every one of these lines
 * lived in `chatCommand.ts`, a file that imports `vscode` and that therefore no test in this
 * repository can load. Moving them into modules that need no editor is what makes them reachable —
 * and a claim like that is worth nothing until something actually reaches them. This is that.</p>
 *
 * <p>What is checked is the behaviour the rest of the extension leans on and never states: that an
 * importer reads whatever the setter last put there (a LIVE binding, not a copy taken at import
 * time), that retiring the memento makes every `memory?.` site a no-op without any of them knowing,
 * and that a thread is keyed by object identity so two conversations cannot collide.</p>
 */

const nothing = (): void => undefined;

test('an importer reads what the setter last put there, not a copy taken at import time', () => {
  // `export let` is a live binding, and the whole of `chatHost` rests on that: fifteen modules read
  // these names, and if a reader captured the value at import time it would hold `undefined` for
  // ever, because nothing is bound until `activate` runs.
  assert.equal(memory, undefined, 'the memento is bound before anything binds it');

  const fake = { remember: nothing, settled: async () => undefined } as unknown as ChatTabMemory;
  rememberChatsIn(fake);

  assert.equal(memory, fake, 'a module reading `memory` did not see what `rememberChatsIn` bound');
});

test('retiring the memento unbinds it, which is what makes every write site a no-op at once', () => {
  rememberChatsIn({ remember: nothing, settled: async () => undefined } as unknown as ChatTabMemory);
  retireMemento();

  // The gate for the whole dual write: `memory?.remember(...)` stops writing without any of its
  // call sites being told, which is why retiring is one line and not a flag each of them reads.
  assert.equal(memory, undefined, 'retiring left the memento bound, so the dual write continues');
});

test('the store and the heartbeat are absent until something binds them', () => {
  // Absent means absent, and every use is guarded — which is how the pure tests of this file's
  // neighbours run at all. Asserted in its own test rather than beside the binding below, because
  // comparing a live binding against `undefined` narrows it to `never` for the rest of the scope and
  // the call would then not compile.
  assert.equal(store, undefined, 'the store is bound before anything binds it');
  assert.equal(pulse, undefined, 'the heartbeat is bound before anything binds it');
});

test('the store and the heartbeat are bound the same way the memento is', () => {
  const disk = { save: async () => ({ kind: 'ok', rev: 1 }) } as unknown as ChatStoreFile;
  let beats = 0;
  keepChatsIn(disk);
  pulseChatsThrough(() => {
    beats += 1;
  });

  assert.equal(store, disk, 'a module reading `store` did not see what `keepChatsIn` bound');
  pulse?.();
  assert.equal(beats, 1, 'the heartbeat was bound but calling it announced nothing');
});

test('a conversation is keyed by object identity, so two tabs cannot collide', () => {
  // The registry is a `WeakMap` on the entry's own id object rather than on any string it carries.
  // Two conversations with one title is the ordinary case this exists for.
  const one = {};
  const two = {};
  const held = { title: 'same name' } as unknown as Thread;

  threads.set(one, held);

  assert.equal(threads.get(one), held, 'a thread did not come back under the key it was stored with');
  assert.equal(threads.get(two), undefined, 'a second conversation read the first one’s thread');
  threads.delete(one);
  assert.equal(threads.get(one), undefined, 'a closed conversation is still in the registry');
});
