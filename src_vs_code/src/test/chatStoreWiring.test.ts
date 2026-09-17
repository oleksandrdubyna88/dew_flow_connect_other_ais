import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That the store is actually WIRED — the half a unit test cannot reach.
 *
 * <p>Every module under this feature can be right while nothing calls them: the record shape, the
 * file protocol and the decision are all tested as values, and a conversation would still never
 * reach the disk. These read the source of the two files that import `vscode`, which is the pattern
 * `chatWiring.test.ts` and `aConversationSurvivesAReload.test.ts` already use here for the same
 * reason.</p>
 *
 * <p>Story A3 was a DUAL WRITE: the memento stayed the source of truth and the store was filled
 * beside it. Story A4 turned that round — the store is the source of truth and the serializer reads
 * it — but the memento is STILL written until the migration has confirmed every record is on disk in
 * this window, because a store that cannot be reached must not leave a person's next words written
 * nowhere. So these assert both halves are written and that the gate that ends the memento's half is
 * the migration's success and nothing else.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/**
 * One function's text, from its opening line to the next top-level declaration.
 *
 * <p>The same helper `chatSourceWiring.test.ts` carries, for the same reason: a fixed character
 * width goes red for the length of a paragraph somebody added rather than for anything about the
 * code. The end is found rather than guessed.</p>
 */
const bodyOf = (text: string, opening: string): string => {
  const at = text.indexOf(opening);
  assert.ok(at >= 0, `there is no ${opening} to read`);
  const after = text.indexOf('\n}', at);

  return after < 0 ? text.slice(at) : text.slice(at, after + 2);
};

test('a conversation is written to the store, and to the memento until the migration has retired it', () => {
  const host = source('chatHost.ts');

  // TWO writers, counted rather than matched. The page push and the fork both write the memento,
  // and they are in two modules now — a match over both files together would pass on either one
  // alone, which is a weaker assertion than the one that was here before the split.
  assert.equal(
    (source('chatShow.ts') + source('chatPersist.ts')).split('memory?.remember(').length - 1,
    2,
    'the memento is no longer written by both the page push and the fork: a store that cannot be'
    + ' reached would leave the next words nowhere');
  // The write queue moved to `chatPersist.ts` when the command file was split — it had to come out
  // BEFORE the session join, because `adoptFound` calls `keepQueued`. What it must do is unchanged.
  assert.match(source('chatPersist.ts'), /keepOnDisk\(entry, thread\)/u, 'nothing writes a conversation to the store');
  // The gate is the binding itself: retiring the memento unbinds it, and every `memory?.` site
  // becomes a no-op without knowing.
  // The binders moved to `chatHost.ts` when the command file was split: three handles a window
  // binds once, read by five of the modules coming out of it, so leaving them behind made every
  // extraction a cycle. What they DO is unchanged, and that is what this still asserts.
  assert.match(host, /export function retireMemento\(\): void \{\s*\n\s*memory = undefined;\s*\n\}/u,
    'retiring the memento is something other than unbinding it, so a write site can keep going');
});

test('the store writes of ONE conversation are chained, and the chain survives a rejection', () => {
  // Two reviewers, independently, on A3's plan round: a save carries the revision this window last
  // had accepted, so two writes issued before the first answers both carry the old one — the second
  // is refused, a refusal reads as another window, and the tab forks ITSELF and says it has become a
  // copy of a conversation nobody else was in. `show` runs on every push, and pushes are not rare.
  // The write queue moved to `chatPersist.ts` when the command file was split — it had to come out
  // BEFORE the session join, because `adoptFound` calls `keepQueued`. What it must do is unchanged.
  const chained = source('chatPersist.ts').slice(source('chatPersist.ts').indexOf('const step = ('));

  assert.match(chained.slice(0, 900), /thread\.writes = thread\.writes\.then\(step, step\)/u,
    'the store writes of one conversation race each other, so a tab can fork itself');
  assert.match(chained.slice(0, 900), /\.catch\(/u, 'a detached store write has no owner for its failure');
  assert.match(chained.slice(0, 900), /console\.error/u, 'a failed store write says nothing anywhere');
  assert.match(chained.slice(0, 900), /pushChatNote\(/u,
    'a store write that rejected told the console and left the person looking at a tab that said nothing');
});

test('the extension gives the chat a store to write to, under the coai data directory, and the same store to the migration and the serializer', () => {
  const wiring = source('extension.ts');

  assert.match(wiring, /const chatStore = new ChatStoreFile\(conversationsDir\(coaiDataDir\(\)\)\)/u,
    'the chat has no store, so every conversation is written to the memento alone');
  assert.match(wiring, /keepChatsIn\(chatStore\)/u, 'the chat writes to a store other than the one the serializer reads');
  const migration = wiring.slice(wiring.indexOf('importLegacyTabs({'), wiring.indexOf('.then((report)'));
  assert.match(migration, /memento: context\.workspaceState,/u, 'the migration reads a memento other than the one the chat writes');
  assert.match(migration, /store: chatStore,/u, 'the migration is handed a different store than the writers use');
  assert.match(migration, /workspace: conversationWorkspace\(\),/u, 'the migration files records under a different workspace than the dual write');
  assert.match(wiring, /const restoreDeps: RestoreDeps = \{\s*\n\s*panels: chatPanels,\s*\n\s*store: chatStore,\s*\n\s*memento: chatTabMemory,/u,
    'the serializer reads a store or a memento other than the one that is written');
  // The gate: the memento is retired ONLY through the seal, which the migration calls when the key is
  // empty or every record is confirmed — and the seal drains the queue before the key can go.
  assert.match(migration, /seal: async \(\) => \{\s*\n\s*retireMemento\(\);\s*\n\s*await chatTabMemory\.settled\(\);/u,
    'the memento is retired on some condition other than the migration sealing it, or without its queue drained');
  assert.match(migration, /unseal: \(\) => \{\s*\n\s*rememberChatsIn\(chatTabMemory\);/u,
    'a clear that did not happen after the seal leaves this window writing one store only');
  assert.equal(wiring.split('retireMemento()').length - 1, 1, 'the memento is retired from more than one place');
  // And a migration that THREW after sealing must not leave the writer unbound either.
  const failed = wiring.slice(wiring.indexOf('.catch((reason: unknown): ImportReport'));
  assert.match(failed.slice(0, 500), /rememberChatsIn\(chatTabMemory\);/u, 'a defect in the migration leaves the memento sealed with its key still populated');
});

test('a conversation another window took over is kept under a NEW id, never overwritten', () => {
  // The whole point of the swap. The transcript is on the thread, so a fork loses nothing: what it
  // costs is one id, and what it buys is that neither window's turns are destroyed by the other's.
  // The write queue moved to `chatPersist.ts` when the command file was split — it had to come out
  // BEFORE the session join, because `adoptFound` calls `keepQueued`. What it must do is unchanged.
  const fork = bodyOf(source('chatPersist.ts'), 'async function forkOnDisk');

  assert.match(fork, /thread\.saveId = randomUUID\(\)/u, 'a fork keeps the id it was refused under');
  assert.match(fork, /thread\.rev = 0/u, 'a forked conversation would swap against a revision that is not its own');
  assert.match(fork, /pushChatNote\(entry, thread\.saveId/u, 'the tab is not told it has become a copy');
  // And the memento, for as long as it is still bound — a fallback the serializer asks while its key
  // holds anything: without it the fork lives only on disk and in memory, so a reload during the
  // dual-write window restores the tab as the original it no longer owns and the copy holding the
  // person's words is the one nothing can find. (codex, A3's plan round.) Once `retireMemento` has
  // unbound it this is a no-op and the fork rests on the store's own swap.
  assert.match(fork, /memory\?\.remember\(/u,
    'a forked conversation is not written to the memento while it is still a fallback, so a reload loses it');
});

test('the page is told through a message of its OWN, never through the state channel', () => {
  // The reason `asked` has its own type: the page reads a state message as the whole truth about
  // every region it mentions and treats what it does not mention as gone, so a sentence sent that
  // way would clear the failure line standing beside it.
  const panel = source('chatPanel.ts');

  assert.match(panel, /export function pushChatNote/u, 'there is no way to say anything to one page');
  assert.match(panel, /type: 'note'/u, 'the note travels on the state channel it must not travel on');
  assert.match(panel, /escapeHtml\(note\)/u,
    'a sentence reaches the page unescaped, and everything assigned to innerHTML is escaped where it is built');
});

test('the page takes the new id from that message and hands it back after a reload', () => {
  // A tab that forked and was then reloaded must come back as the copy it became. The id the page
  // holds is the only thing the serializer gets, so if it still named the original this window would
  // reopen somebody else's conversation.
  const page = source('chatPage.ts');
  const handler = page.slice(page.indexOf("if (data.type === 'note')"));

  assert.match(handler.slice(0, 1_200), /vscode\.setState\(held\)/u,
    'a forked tab would come back after a reload as the conversation it no longer owns');
  // MERGED rather than replaced: `setState` writes the whole state object, so naming one field
  // would throw away everything else the page keeps in it. (gemini, A3's code round.)
  assert.match(handler.slice(0, 1_200), /vscode\.getState\(\)/u,
    'the page replaces its whole stored state to record one field');
  assert.ok(
    page.indexOf("if (data.type === 'note')") < page.indexOf("if (data.type !== 'state')"),
    'the note is read after the state guard has already returned',
  );
});
