import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That a conversation's durable identity is actually WRITTEN, and followed — the half a unit test
 * cannot reach.
 *
 * <p>`chatSource.test.ts` drives every rule without a host: which root a path belongs to, what a
 * rename moves, what a session id is. What it cannot see is whether anything CALLS them — and this
 * story's whole risk is that it does not. The `source` field has existed since epic A and has always
 * been written as `none`; a build where the new writes are missing or wired to the wrong lifecycle
 * point passes every pure test in this feature while every record on disk still says `none`. The
 * pin-time save is the sharpest case, because `show`'s dedupe guard compares messages, model and
 * mark, so a source arriving on its own would never reach disk through that path. (codex, the plan
 * round, asked for exactly this test.)</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('a record is written with the conversation’s OWN source and root, not with a placeholder', () => {
  // The one line that decides whether any of this reaches disk. It said `{ kind: 'none' }` and
  // `conversationWorkspace()` for three epics.
  const command = source('chatCommand.ts');
  const built = command.slice(command.indexOf('function recordOf('), command.indexOf('function recordOf(') + 1_200);

  assert.match(built, /source: thread\.source,/u, 'a record is still written with a placeholder source');
  assert.match(built, /workspace: thread\.workspace,/u, 'a record is still filed under this window’s first root rather than its own');
  assert.doesNotMatch(built, /kind: 'none'/u, 'the placeholder is still there');
});

test('the pin writes the session id AND the folder it was found in, and saves for itself', () => {
  // A UUID names no directory, so the root cannot be worked out later from the source — it has to be
  // captured here, from the search that found the session. Three reviewers refused the alternative.
  const command = source('chatCommand.ts');
  const pin = command.slice(command.indexOf('function pinSession('), command.indexOf('function pinSession(') + 2_400);

  assert.match(pin, /sessionIdOf\(/u, 'the pin stores something other than the session’s id');
  assert.match(pin, /mine\.source = sourceOfSession\(/u, 'the pin does not give the conversation its identity');
  assert.match(pin, /filedUnder\(one\.folder,/u, 'the pin files the conversation under this window’s first root instead of the session’s own');
  // AND IT WRITES. Without this the source lives only in memory and dies with the window.
  assert.match(pin, /keepQueued\(entry, mine\);/u, 'the source is never written to disk, so it dies with the window');
  // Guarded on `fromSession`, or the record would state its origin two ways and `agreeOnOrigin`
  // would refuse to read it back — the conversation would save and then be unopenable.
  assert.match(pin, /if \(!fromSession \|\| title\.length === 0\) \{/u, 'a session source can be written onto a tab that has no session');
  // An id it cannot read is no id: inventing one would match a tab that is not this one.
  assert.match(pin, /if \(sessionId\.length === 0\) \{/u, 'a file name of an unexpected shape becomes a source anyway');
});

test('the write queue is ONE queue, so the pin cannot race the page', () => {
  // Two writers now: the page, on every push, and the pin, minutes later. Both carry the revision
  // this window last accepted, so two issued before the first answers would have the second refused
  // — and a refusal reads as another window, which would fork the conversation.
  const command = source('chatCommand.ts');

  assert.match(command, /function keepQueued\(entry: ChatEntry, thread: Thread\): void \{/u, 'there is no one place a write is queued');
  // Three writers and the definition: the page on every push, the pin when a session is found, and a
  // rename following a file that moved. A fourth appearing without this test being read is a writer
  // nobody checked against the chain.
  assert.equal(command.split('keepQueued(').length - 1, 4, 'a writer was added to or removed from the one queue');
  const queue = command.slice(command.indexOf('function keepQueued('), command.indexOf('function keepQueued(') + 900);
  assert.match(queue, /thread\.writes = thread\.writes\.then\(step, step\);/u, 'writes are fired rather than chained');
});

test('a tab snapshot carries the document’s uri, from the SAME read as its scheme', () => {
  const command = source('chatCommand.ts');
  const snap = command.slice(command.indexOf('function snapshots('), command.indexOf('function snapshots(') + 1_400);

  assert.match(snap, /uri: input\?\.uri === undefined \? '' : input\.uri\.toString\(\)/u, 'a file tab has no identity to be found by');
  // And it reaches the conversation through the one match, not a second snapshot taken later.
  assert.match(command, /uri: active\?\.uri \?\? '',/u, 'the uri is not carried out of the match');
  assert.match(command, /source: fromSession \? \{ kind: 'none' \} : sourceOfFile\(sourceUri\),/u, 'a new conversation is not given its source');
});

test('a renamed file is followed; a SAVED untitled buffer deliberately is not', () => {
  // `onDidRenameFiles` carries an explicit old-to-new mapping. `onDidSaveTextDocument` does not — it
  // hands over the saved document and no previous uri — so a listener on it would attach a
  // conversation to the wrong file as readily as to the right one. Pinned here so that adding one is
  // a decision somebody makes on purpose rather than an afternoon's good idea.
  const wiring = source('extension.ts');

  assert.match(wiring, /vscode\.workspace\.onDidRenameFiles\(/u, 'a moved file loses its conversation');
  assert.match(wiring, /followRenames\(chatPanels, housekeeping\.index,/u, 'the rename reaches neither the open tabs nor the store');
  assert.match(wiring, /context\.subscriptions\.push\(vscode\.workspace\.onDidRenameFiles\(/u, 'the listener outlives the extension that made it');
  assert.doesNotMatch(wiring, /onDidSaveTextDocument/u,
    'a listener was added on document saves, which cannot say which untitled buffer a saved file used to be');
});

test('the follow writes a live conversation through its thread and a closed one through the store — never both', () => {
  // One record, one writer. A live conversation's thread is the authority and its chain serialises
  // the write; a closed one is only on disk. Following both halves for one id would have them race.
  const command = source('chatCommand.ts');
  const follow = command.slice(command.indexOf('export function followRenames('), command.indexOf('export function followRenames(') + 2_600);

  assert.match(follow, /held\.add\(thread\.saveId\);/u, 'nothing records which conversations this window holds open');
  assert.match(follow, /if \(held\.has\(meta\.id\) \|\| !followable\(meta\.source\)\)/u, 'the store pass rewrites records a live thread is also writing');
  assert.match(follow, /keepQueued\(entry, thread\);/u, 'a live conversation’s new source is never written');
  assert.match(follow, /onDisk\.refile\(meta\.id, meta\.source,/u, 'a closed conversation is not followed at all');
  // Both facts in one call, or the conversation ends up filed under the root it left.
  assert.match(follow, /filedFor\(next\)\)/u, 'the root is not recomputed when a file moves between projects');
});
