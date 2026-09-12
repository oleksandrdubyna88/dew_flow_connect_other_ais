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
 * <p>Story A3 is a DUAL WRITE: the memento is still the source of truth and the store is filled
 * beside it, because story A4 can only make the store authoritative against one that has been
 * filling for a version. So these assert both halves are written, not that one replaced the other.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('a conversation is written to the store, and to the memento it has not replaced yet', () => {
  const command = source('chatCommand.ts');

  assert.match(command, /memory\?\.remember\(/u, 'the memento is no longer written, a version too early');
  assert.match(command, /void keepOnDisk\(entry, thread\)/u, 'nothing writes a conversation to the store');
  assert.ok(
    command.indexOf('memory?.remember(') < command.indexOf('void keepOnDisk('),
    'the store is written before the memento, so a crash between them loses the authoritative copy',
  );
});

test('the store write is detached, and a detached edge ends in a catch that says something', () => {
  // `reliability.md`: a fire-and-forget whose fault nobody observes is a defect that reports nothing
  // while the process looks healthy. Nobody waits for a disk to see their own words, so the write is
  // detached — which makes the catch mandatory rather than optional.
  const command = source('chatCommand.ts');
  const detached = command.slice(command.indexOf('void keepOnDisk('));

  assert.match(detached.slice(0, 400), /\.catch\(/u, 'a detached store write has no owner for its failure');
  assert.match(detached.slice(0, 400), /console\.error/u, 'a failed store write says nothing anywhere');
});

test('the extension gives the chat a store to write to, under the coai data directory', () => {
  const wiring = source('extension.ts');

  assert.match(wiring, /keepChatsIn\(new ChatStoreFile\(conversationsDir\(coaiDataDir\(\)\)\)\)/u,
    'the chat has no store, so every conversation is written to the memento alone');
  assert.ok(
    wiring.indexOf('rememberChatsIn(') < wiring.indexOf('keepChatsIn('),
    'the two stores are bound in an order that reads as if the disk one were in charge',
  );
});

test('a conversation another window took over is kept under a NEW id, never overwritten', () => {
  // The whole point of the swap. The transcript is on the thread, so a fork loses nothing: what it
  // costs is one id, and what it buys is that neither window's turns are destroyed by the other's.
  const command = source('chatCommand.ts');
  const fork = command.slice(command.indexOf('async function forkOnDisk'));

  assert.match(fork.slice(0, 900), /thread\.saveId = randomUUID\(\)/u, 'a fork keeps the id it was refused under');
  assert.match(fork.slice(0, 900), /thread\.rev = 0/u, 'a forked conversation would swap against a revision that is not its own');
  assert.match(fork.slice(0, 900), /pushChatNote\(entry, thread\.saveId/u, 'the tab is not told it has become a copy');
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

  assert.match(handler.slice(0, 700), /vscode\.setState\(\{ id: data\.id \}\)/u,
    'a forked tab would come back after a reload as the conversation it no longer owns');
  assert.ok(
    page.indexOf("if (data.type === 'note')") < page.indexOf("if (data.type !== 'state')"),
    'the note is read after the state guard has already returned',
  );
});
