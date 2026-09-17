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
  //
  // ONE ROAD IN. There are two ways a session is discovered — the walk as a tab opens, and a press
  // that finds the tab unpinned — and they used to write different amounts of it: the press wrote the
  // file alone, so what it learned died at the next reload while the walk's version lasted for ever.
  // Both go through `adoptFound` now, and this asserts BOTH halves: what the helper writes, and that
  // each caller reaches it. A second copy of these four lines anywhere else fails the count below.
  const command = source('chatCommand.ts');
  const pin = command.slice(command.indexOf('function adoptFound('), command.indexOf('function adoptFound(') + 2_400);

  assert.match(pin, /sessionIdOf\(/u, 'the pin stores something other than the session’s id');
  assert.match(pin, /mine\.source = sourceOfSession\(/u, 'the pin does not give the conversation its identity');
  assert.match(pin, /filedUnder\(one\.folder,/u, 'the pin files the conversation under this window’s first root instead of the session’s own');
  // AND IT WRITES. Without this the source lives only in memory and dies with the window.
  assert.match(pin, /keepQueued\(entry, mine\);/u, 'the source is never written to disk, so it dies with the window');
  // An id it cannot read is no id: inventing one would match a tab that is not this one.
  assert.match(pin, /if \(sessionId\.length === 0\) \{/u, 'a file name of an unexpected shape becomes a source anyway');
  // AND A SCAN THAT WAS CUT SHORT IS NOT WRITTEN DOWN. The only session of that name among the ones
  // that were READ is a fine answer on screen and no proof that no namesake sits beyond the cut;
  // persisting it would make a guess permanent. It is checked BEFORE the id is taken, so the whole
  // write is skipped rather than half of it.
  assert.match(pin, /if \(!found\.complete\) \{/u, 'a match from a budget-cut scan is persisted as though it were proven');
  assert.ok(pin.indexOf('if (!found.complete) {') < pin.indexOf('sessionIdOf('),
    'the completeness check runs after the record has already been given an identity');

  // Guarded on `fromSession`, or the record would state its origin two ways and `agreeOnOrigin`
  // would refuse to read it back — the conversation would save and then be unopenable.
  const walk = command.slice(command.indexOf('function pinSession('), command.indexOf('function pinSession(') + 1_400);
  assert.match(walk, /if \(!fromSession \|\| title\.length === 0\) \{/u, 'a session source can be written onto a tab that has no session');
  assert.match(walk, /adoptFound\(entry, mine,/u, 'the walk keeps its own copy of what a pin writes');
  // Exactly two callers and the definition. A third way to adopt a session is a writer nobody
  // checked against the store's origin pair.
  assert.equal(command.split('adoptFound(').length - 1, 3, 'a caller was added to or removed from the one road in');
  assert.match(
    bodyOf(command, 'async function resolveAndPin('),
    /adoptFound\(entry, mine,/u,
    'a press that resolves an unpinned tab keeps the file alone, so what it found dies at the next reload',
  );
});

/**
 * One function's text, from its opening line to the next top-level declaration.
 *
 * <p>A fixed width was what these assertions used, and a comment added inside a function pushed the
 * line being asserted past the end of the slice — a test that went red for the length of a paragraph
 * rather than for anything about the code. The end is found rather than guessed.</p>
 */
const bodyOf = (text: string, opening: string): string => {
  const at = text.indexOf(opening);
  assert.ok(at >= 0, `there is no ${opening} to read`);
  const after = text.indexOf('\n}', at);

  return after < 0 ? text.slice(at) : text.slice(at, after + 2);
};

test('the Asked button asks the session ID before it asks the name', () => {
  // The identity was on the record all along and nothing used it: a reload empties the remembered
  // path and the next press walked the folder by a title Claude Code rewrites underneath it. The id
  // IS the file's name, so the answer is one directory entry rather than every transcript read —
  // 5.2 s warm on the operator's own folder, measured.
  const command = source('chatCommand.ts');
  const resolve = bodyOf(command, 'async function resolveAndPin(');

  assert.ok(
    resolve.indexOf('findSessionById(') > 0 && resolve.indexOf('findSessionById(') < resolve.indexOf('findSessionIn('),
    'the name is asked before the id, so a renamed conversation is hunted by a string that is already stale',
  );
  // ONE root answers, on the same terms the name walk uses — and the conversation's own filed root
  // answers first, since that is where the session was found the day it was pinned.
  assert.match(resolve, /sameRoot\(one\.folder, mine\.workspace,/u,
    'the conversation’s own root has no say in which session its id names');
  assert.match(resolve, /pinnable\(byId\.map/u,
    'two roots answering to one id would be picked between rather than refused');

  const byId = bodyOf(command, 'async function findSessionById(');
  // And the id is looked for in EVERY root, as the name is: a session's id names no folder.
  assert.match(byId, /everyFolder\(/u, 'the id is looked for in one root only');
  // A source that is not a Claude session has no id to look for, and says so by finding nothing.
  assert.match(byId, /if \(source\.kind !== 'claude'\) \{/u,
    'a file-opened conversation is asked for a session id it cannot have');
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
  // Both halves moved to `chatCapture.ts` when the command file was split — one module for what this
  // side reads out of the editor, the tabs and the clipboard — so this follows them there. The third
  // assertion stays on the command file, where a new conversation is still given its source.
  const capture = source('chatCapture.ts');
  const command = source('chatCommand.ts');
  const snap = bodyOf(capture, 'export function snapshots(');

  assert.match(snap, /uri: input\?\.uri === undefined \? '' : input\.uri\.toString\(\)/u, 'a file tab has no identity to be found by');
  // And it is the MATCHED tab's uri, not whatever is focused. From the chat panel itself — which is
  // how *add the question* is used — the active tab is a webview with no document, while the match
  // falls back through every tab to the editor. Reading the active one there gave the conversation no
  // source at all. Still out of the SAME snapshot, so the two cannot disagree about the tabs.
  assert.match(capture, /uri: all\.find\(\(tab\) => tab\.key === matched\?\.key\)\?\.uri \?\? '',/u,
    'the uri comes from the focused tab rather than the matched one, so a chat opened from the panel has no source');
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

  // Asked AT THE MOMENT the store pass reaches each record, never from a set taken before any
  // awaiting began: a conversation that closes while the loop runs is no longer followed by its
  // thread, and a stale snapshot would have said it was — so its rename would be followed by neither.
  assert.match(follow, /if \(heldConversationIds\(panels\)\.includes\(meta\.id\) \|\| !followable\(meta\.source\)\)/u,
    'the store pass decides from a snapshot of what was open, so a conversation closing mid-loop loses its rename');
  assert.doesNotMatch(follow, /const held = new Set/u, 'the live set is still snapshotted before the awaiting starts');
  assert.match(follow, /keepQueued\(entry, thread\);/u, 'a live conversation’s new source is never written');
  assert.match(follow, /follow\(onDisk, meta\.id, meta\.source, sourceOfFile\(moved\)\)/u, 'a closed conversation is not followed at all');
  // A busy claim is an ordinary concurrent save, not a verdict: a rename happens once and is never
  // replayed, so giving up on the first one loses it for ever. (Four reviewers, the code round.)
  const one = command.slice(command.indexOf('async function follow(onDisk'));
  assert.match(one.slice(0, 1_800), /done\.why !== 'busy'/u, 'a conversation busy in another window loses its rename permanently');
  assert.match(one.slice(0, 1_800), /FOLLOW_TRIES/u, 'the retry is unbounded, or there is none');
  // Both facts in one call, or the conversation ends up filed under the root it left.
  assert.match(one.slice(0, 1_800), /onDisk\.refile\(id, was, next, filedFor\(next\)\)/u, 'the root is not recomputed when a file moves between projects');
  // And the picker reads the index, not the disk.
  assert.match(follow, /await index\.refresh\(\);/u, 'the picker goes on naming the file the conversation left until something else refreshes it');
});

test('a restored conversation that lost its session source pins again — the self-healing, wired', () => {
  // This is the one the code round caught me claiming and not doing. If the host dies between the
  // pin and its queued write, the record comes back saying it came from a session and carrying no
  // source — and nothing on the restore path ever pinned again, so it would have stayed unmatchable
  // for ever. A record that already HAS its source is left alone: pinning walks folders, and doing
  // that for every restored tab on every reload is a directory walk to rediscover what is already
  // written down. (codex, the code round.)
  const command = source('chatCommand.ts');
  const restore = command.slice(command.indexOf('export function restoreConversation('));

  assert.match(restore, /if \(saved\.fromSession && saved\.source\.kind === 'none'\) \{/u,
    'a conversation whose source write was lost never pins again, so it stays unmatchable for ever');
  assert.match(restore.slice(0, restore.indexOf('return entry;')), /pinSession\(entry, saved\.title, true\);/u,
    'the restore path does not pin');
});

test('the store pass survives one conversation that cannot be followed', () => {
  // reliability.md: a loop over independent units wraps each unit, records the failure on that unit,
  // and carries on. The loop's job is the campaign; one record is never allowed to end it.
  const command = source('chatCommand.ts');
  const follow = command.slice(command.indexOf('export function followRenames('), command.indexOf('const FOLLOW_TRIES'));

  assert.match(follow, /try \{/u, 'the store pass has no per-conversation guard');
  assert.match(follow, /a conversation threw while following a renamed file/u, 'a conversation that throws is not named');
  assert.ok(follow.indexOf('try {') < follow.indexOf('heldConversationIds(panels)'),
    'the guard starts after the work it is meant to guard');
});

test('the renames are put into comparable form once, not once per conversation', () => {
  const command = source('chatCommand.ts');
  const follow = command.slice(command.indexOf('export function followRenames('), command.indexOf('const FOLLOW_TRIES'));

  assert.match(follow, /const moves = prepareMoves\(renames, NAMES_ARE_CASE_BLIND\);/u, 'the renames are normalised inside the loop over the store');
  const prepared = follow.indexOf('prepareMoves(renames,');
  assert.notEqual(prepared, -1, 'the renames are never put into comparable form at all');
  assert.ok(prepared < follow.indexOf('for (const'), 'the preparation happens inside a loop');
});
