import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * That *New chat* is WIRED, and that it happens IN ORDER — the half no unit test here can reach.
 *
 * <p>`chatFresh.test.ts` drives the slate as a value. What it cannot see is the order the host does
 * things in, and the order is the whole story: a reset that stopped the old turn without waiting for
 * it, or that cleared the transcript before the answer in flight had landed, would pass every value
 * test and file an answer into a conversation that had already been archived. This repository has no
 * extension-host harness — `research/module_tests.md` records that as its largest gap — so what is
 * pinned is the source, which is the house pattern here.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** Where one function begins and the next one does, so an assertion cannot match a neighbour. */
const between = (text: string, from: string, to: string): string =>
  text.slice(text.indexOf(from), text.indexOf(to));

test('the gesture that has always been there now does something', () => {
  // `chatPage.ts` has rendered *Start a new conversation* in the capped notice since the cap
  // existed, `chatMessages.ts` has parsed it and `chatPanel.ts` has dispatched it — to a hook that
  // was literally `() => undefined`. A shipped button that did nothing.
  // The page hooks moved to `chatHooks.ts` when the command file was split — still ONE object
  // built in ONE place, which is what these assert.
  const command = source('chatHooks.ts');

  assert.doesNotMatch(command, /onRestart: \(\) => undefined/u, 'the restart hook is still a stub');
  assert.match(command, /onRestart: \(id\) => \{[\s\S]{0,400}freshStart\(entry\)/u, 'the restart hook does not reach the reset');
  // The page's half is unchanged, because the two buttons are one gesture: D2's header button posts
  // the very same message, so one host implementation serves both.
  assert.match(source('chatPage.ts'), /vscode\.postMessage\(\{ type: 'command', command: 'restart' \}\)/u);
});

test('NOTHING IS SWITCHED until the old conversation is provably finished AND FILED', () => {
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const reset = between(command, 'async function freshening(', 'function keepTheOldOne(');

  // 1. The slate moves FIRST, before anything is stopped. A question queued behind the running
  // answer would otherwise begin a whole new turn after the stop: the reset would wait it out, and
  // it would append what somebody typed into a transcript that is about to be replaced.
  const bumped = reset.indexOf('thread.generation += 1;');
  const ending = reset.indexOf('ended(thread)');
  assert.ok(bumped >= 0 && ending > bumped, 'the slate is bumped after the old conversation is ended, or not at all');
  // 2. Ended AND ARCHIVED inside the one notification: archiving a thousand-turn transcript is a
  // serialisation and a disk write, and a progress that stopped before it would leave the longest
  // part of the wait looking like nothing happening.
  const progress = reset.indexOf("ProgressLocation.Window, title: 'Ending the previous conversation…'");
  assert.ok(progress >= 0 && progress < ending, 'a person who presses the button and waits is told nothing');
  assert.ok(reset.indexOf('archiveConversation(thread)') > progress,
    'the archive write happens after the progress notification has gone');
  // 3. ARCHIVING IS PART OF THE ALL-OR-NOTHING. The first draft said a sentence and carried on, which
  // left the old record open on disk while the page said it was archived and the new slate sat over
  // it. Five findings from two vendors named the same gap. (The code round.)
  assert.match(reset, /failed\.length > 0 \? \{ kind: 'refused', reason: failed \} : archiveConversation\(thread\)/u,
    'the ending and the archive are not one answer, so one of them can fail unnoticed');
  const refused = reset.indexOf("done.kind === 'refused'");
  const publish = reset.indexOf('publish(entry, thread, done.note)');
  assert.ok(refused >= 0 && refused < publish, 'the reset publishes a new slate without asking whether the old one was filed');
  assert.match(reset.slice(refused, publish), /keepTheOldOne\(entry, thread, done\.reason\)/u,
    'a refusal does not go through the same recovery an ending failure does');
});

test('the new slate is WRITTEN before its id is published', () => {
  // The page hands its id back to the serializer after a reload, so publishing an id whose record is
  // still queued leaves a crash in between restoring a tab that names a conversation nothing ever
  // wrote. A crash BEFORE the publish leaves the tab on the id it already had — an archived
  // conversation, which is still there and still opens. (codex, the code round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const publish = between(command, 'async function publish(', 'async function ended(');

  const slate = publish.indexOf('Object.assign(thread, slate)');
  const wrote = publish.indexOf('await thread.writes;');
  const told = publish.indexOf('pushChatFresh(entry, thread.saveId)');
  assert.ok(slate >= 0 && wrote > slate, 'nothing waits for the new record to reach the disk');
  assert.ok(told > wrote, 'the page is given an id whose record is still in a queue');
  // The dead stub on the SUCCESS path too: `reopen` says the next QUESTION opens a process, but
  // between here and that question a stop, a switch or a tab closing all reach the session this
  // reset has just disposed. (gemini, the code round.)
  assert.match(publish, /thread\.session = closedSession\(reloadedNote\(thread\.modelId\)\)/u,
    'a reset leaves the thread holding a session it has just disposed');
  // And the slate is typed as part of a thread on the way in, so a field of `Freshened` that is not
  // a field of `Thread` — or is one of another type — is a compile error rather than a property
  // nothing reads. It caught one the moment it was added. (local, the code round.)
  assert.match(publish, /const slate: Partial<Thread> = freshened\(randomUUID\(\), Date\.now\(\)\);/u,
    'the slate is applied untyped, so a field that is not a thread field lands silently');
});

test('the ending is stop, WAIT, drain, dispose, release — in that order', () => {
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const ending = between(command, 'async function ended(', 'async function archiveConversation(');

  const stop = ending.indexOf('thread.session.stop()');
  const turns = ending.indexOf('await thread.turns');
  const writes = ending.indexOf('await thread.writes');
  const dispose = ending.indexOf('thread.session.dispose()');
  const release = ending.indexOf('thread.home.release()');
  assert.ok(stop >= 0 && turns > stop, 'the turn is stopped without waiting for it, so its answer can arrive after the archive');
  assert.ok(writes > turns, 'the disk queue is not drained, so the archive saves against a revision a queued push is about to move on');
  assert.ok(dispose > writes, 'the session is disposed before the conversation has finished');
  assert.ok(release > dispose, 'the directory is released before the session that writes into it is gone');
  // The release is best effort and SAID: a temp directory that would not go is one the sweep
  // collects later, never a reason to refuse a reset that has otherwise succeeded.
  assert.match(ending.slice(release), /console\.warn\(/u, 'a directory that would not be released is swallowed');
  assert.doesNotMatch(ending.slice(release), /return asText/u, 'a directory that would not be released refuses the whole reset');
});

test('a reset that FAILED leaves the conversation live AND usable, and archives nothing', () => {
  // A half-performed reset that reports success is the one outcome worse than no reset. And "live"
  // is not enough on its own: if the session was disposed before the failure, a conversation left
  // pointing at it is one the next question cannot use. (codex, the plan round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const branch = between(command, 'function keepTheOldOne(', 'async function publish(');

  assert.match(branch, /closedSession\(reloadedNote\(thread\.modelId\)\)/u, 'the conversation is left holding a session that may be gone');
  assert.match(branch, /thread\.carry = carriedFrom\(thread\.messages, thread\.carryFrom\)/u,
    'the conversation is reopened without the transcript, so the next model is handed nothing');
  assert.match(branch, /thread\.reopen = true/u, 'nothing will open a process for it again');
  assert.match(branch, /couldNotEnd\(reason\)/u, 'the failure is not said, or is said without its reason');
  assert.doesNotMatch(branch, /freshened\(|pushChatFresh\(/u, 'a reset that failed still wipes the slate');
});

test('the disposal and the release run whatever went wrong before them', () => {
  // Written as one try for all four, a stop that threw returned before either cleanup — and the
  // thread was then handed a stub, with the real session unreferenced and still running, writing
  // into a directory nothing would collect. (codex, the code round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const ending = between(command, 'async function ended(', 'type Archived =');
  const caught = ending.indexOf('stopped = asText(reason);');

  assert.ok(caught >= 0, 'a stop that throws is not caught at all');
  assert.ok(ending.indexOf('thread.session.dispose()') > caught, 'a session that would not stop is never disposed');
  assert.ok(ending.indexOf('thread.home.release()') > caught, 'a session that would not stop leaves its directory behind');
  // Both said out loud rather than swallowed, and neither able to refuse the reset on its own.
  assert.equal((ending.match(/console\.warn\(/gu) ?? []).length, 2, 'a cleanup that failed says nothing');
  assert.equal((ending.match(/return stopped;/gu) ?? []).length, 1,
    'a cleanup failure can refuse the reset, where only the stop and the wait may');
});

test('a question typed WHILE the slate is being wiped goes back to the composer', () => {
  // The generation has already moved by then, so the guard inside the turn lets such a question
  // through as the NEW conversation's — and it runs against a session being disposed and is dropped
  // at the end, with the words gone. (gemini, the code round.)
  // The turn moved to `chatTurn.ts` when the command file was split — a re-ask and a retry still
  // go THROUGH `oneTurn` rather than beside it, which is what these assert.
  const command = source('chatTurn.ts');
  const queue = between(command, 'function ask(entry: ChatEntry', 'function chatLanguage(');
  // THE WHOLE GUARD, so an arm disabled in place is as red as an arm deleted.
  const latched = queue.indexOf('if (thread.resetting) {');

  assert.ok(latched >= 0, 'a question asked during a reset is queued as if nothing were happening');
  assert.ok(latched < queue.indexOf('const began = thread.generation;'), 'the latch is read after the turn has already been queued');
  assert.match(queue.slice(latched, latched + 500), /pushChatDraft\(entry, text\)/u, 'the words somebody typed are thrown away');
});

test('a turn knows which conversation it was ASKED in, and cannot write into another', () => {
  // The turn moved to `chatTurn.ts` when the command file was split — a re-ask and a retry still
  // go THROUGH `oneTurn` rather than beside it, which is what these assert.
  const command = source('chatTurn.ts');

  // Captured as the turn JOINS the chain, which is the whole point: a question queued behind an
  // answer waits, and *New chat* pressed while it waits means its author is no longer in the
  // conversation they typed it in.
  const queue = between(command, 'function ask(entry: ChatEntry', 'function chatLanguage(');
  assert.match(queue, /const began = thread\.generation;/u, 'a queued turn does not remember which conversation it was typed in');
  // BOTH of what the turn is told, because since issue #288 there are two things it must not be
  // able to lose: which conversation the question was typed in, and WHICH QUESTION it is. The
  // second is what a withdrawal acts on — the callback that will run it cannot be un-chained, so
  // the turn asks the queue by name at the moment it begins, and a turn given only the generation
  // would run a question somebody had taken back.
  assert.match(queue, /oneTurn\(entry, text, began, joined\.id, ready\)/u,
    'the turn is not told which conversation it belongs to, or which question it is');

  const turn = between(command, 'async function oneTurn(', 'function outcomeOf(');
  // Refused before ANYTHING, so a queued question is never sent and the reset is not held waiting
  // for an answer nobody is in the conversation for — and the words go back to the composer.
  const refused = turn.indexOf('!sameSlate(began, thread.generation)');
  assert.ok(refused >= 0 && refused < turn.indexOf('reopened(thread)'),
    'a turn asked in a conversation that has been reset is still sent');
  assert.match(turn.slice(refused, refused + 600), /pushChatDraft\(entry, text\)/u, 'the words somebody typed are thrown away');
  // And the WRITES at the end are guarded on the slate itself rather than on the generation, because
  // those are two different questions: between a reset beginning and the slate being wiped, the turn
  // in flight is still writing into the OLD conversation, which is where its stopped line belongs.
  // The question and the handover became ONE unit on 2026-09-17 — `turnEnded` asks and, only on the
  // owning branch, clears `running` — because asking and acting had drifted five lines apart and the
  // clearing was happening on the conversation that had REPLACED this turn's own.
  const wiped = turn.indexOf('!turnEnded(thread, mySlate)');
  assert.ok(wiped > turn.indexOf('await thread.session.send('), 'the slate is checked before the answer is even asked for');
  assert.ok(wiped < turn.indexOf('ledger(thread, {'),
    'a turn from a wiped conversation is still priced into the one that replaced it');
  assert.match(turn, /const mySlate = thread\.saveId;/u, 'the turn never records which conversation it is writing into');
});

test('EVERY field of a thread is decided about, and the compiler is what checks it', () => {
  // `Partial<Thread>` proves every field of `Freshened` is a thread field of the right type — and it
  // cannot prove the other direction. A per-conversation field added to `Thread` and forgotten would
  // simply survive the reset, carrying the old conversation into the new one with nothing to say so.
  // The partition below makes that a build failure that names the field. (codex, both code rounds.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');

  assert.match(command, /type Unclassified = Exclude<keyof Thread, keyof Freshened \| KeptByAReset>;/u,
    'nothing works out which thread fields a reset has not been told about');
  assert.match(command, /export const RESET_DECIDES_EVERY_THREAD_FIELD: Unclassified extends never \? true : Unclassified = true;/u,
    'the partition is computed and then never asserted, so an unclassified field compiles');
  // And the two that a reader would most expect to be reset are deliberately in the KEPT half.
  const kept = between(command, 'type KeptByAReset =', 'type Unclassified =');
  assert.match(kept, /'turn'/u, 'the turn number is resettable, so a late stop could name a turn of the new conversation');
  assert.match(kept, /'title'/u, 'the tab’s own name is resettable');
  assert.match(kept, /'generation' \| 'resetting'/u, 'the reset’s own bookkeeping is treated as part of the slate it installs');
});

test('a conversation with nowhere to be archived is NOT wiped', () => {
  // Reading an absent store as a successful archive would wipe a conversation with nowhere for it to
  // have gone — "archived, never deleted" broken in the one case where the deletion is total. And
  // the empty check comes first, because a conversation with nothing in it needs no store to be
  // archived correctly. (codex, the second code round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const archive = between(command, 'async function archiveConversation(', 'function whyNotClosed(');

  const empty = archive.indexOf('thread.messages.length === 0');
  const missing = archive.indexOf('store === undefined');
  assert.ok(empty >= 0 && missing > empty, 'an absent store is checked before whether there is anything to keep');
  assert.match(archive.slice(missing, missing + 400), /kind: 'refused'/u,
    'a window with nowhere to keep conversations wipes one anyway');
});

test('DRAINED IS NOT SAVED: the id is published only once the disk has the record', () => {
  // The write chain is built so it cannot reject — which is what makes awaiting it safe — so a save
  // that failed still lets the drain resolve. `rev` is the disk's own answer: zero until the store
  // has accepted this record. (codex and gemini, the second code round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const publish = between(command, 'async function publish(', 'async function ended(');
  const drained = publish.indexOf('await thread.writes;');
  const asked = publish.indexOf('thread.rev === 0');
  const told = publish.indexOf('pushChatFresh(entry, thread.saveId)');

  assert.ok(drained >= 0 && asked > drained, 'nothing asks whether the new record actually landed');
  assert.ok(asked < told, 'the id is published before it is known that the record exists');
  // And what happens then: the tab stays on the id it had — an archived conversation, whole and
  // still opening, which is a better thing to reload into than a name nothing was written under.
  assert.match(publish.slice(asked, told), /return;/u, 'a record that never landed has its id published anyway');
  assert.match(publish.slice(asked, told), /will not survive a reload/u, 'nobody is told the new conversation is not on disk');
});

test('one reset at a time, per conversation', () => {
  // The gesture is a button and the work takes as long as ending a turn takes, so two presses are
  // ordinary. Two resets at once would dispose the same session twice, archive the same record
  // twice, and publish two different fresh ids for one tab. (Two vendors, the plan round.)
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const thread = source('chatThread.ts');
  const start = between(command, 'async function freshStart(', 'async function freshening(');

  // ON THE THREAD, where the rest of this conversation's lifecycle lives — a module-level set of
  // object identities outlives the threads in it if a `finally` is ever missed. (gemini, the second
  // code round.) The `Thread` type now has its own module, so the field is read there; the latch
  // must not come back as a module-level set in EITHER file, so both files are asked.
  assert.match(thread, /\n  resetting: boolean;/u, 'nothing stops a second press');
  assert.doesNotMatch(command + thread, /new Set<object>\(\)/u, 'the latch is still a module-level set of identities');
  assert.match(start, /thread\.resetting\)/u, 'the latch is never read');
  assert.match(start, /try \{\s*\n\s*await freshening\(entry, thread\);\s*\n\s*\} finally \{\s*\n\s*thread\.resetting = false;/u,
    'a reset that throws leaves the latch set, and the button dead for the life of the window');
});

test('the slate is applied as ONE value, and the disk marks are deleted rather than emptied', () => {
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const publish = between(command, 'async function publish(', 'async function ended(');

  // One statement, so a field added to `Freshened` is applied here by construction rather than by
  // somebody remembering this statement exists.
  assert.match(publish, /Object\.assign\(thread, slate\);/u,
    'the reset assigns fields one by one, so the next one added will be forgotten');
  assert.match(publish, /for \(const mark of UNSAVED\) \{\s*\n\s*delete thread\[mark\];/u,
    'the marks that say what the disk holds are not deleted, so the new record is never written');
});

test('a conversation nobody said anything in is not archived, and the dead session is written once', () => {
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');
  const archive = between(command, 'async function archiveConversation(', 'const MODEL_SWITCH');

  assert.match(archive, /thread\.messages\.length === 0/u, 'resetting an untouched tab leaves an empty conversation in Recent');
  assert.match(archive, /closedAt: Date\.now\(\)/u, 'the old record is not stamped closed');
  // Closed, never deleted: it appears in Recent under its own id and opens.
  assert.doesNotMatch(archive, /forget\(/u, 'the old conversation is deleted rather than archived');
  // And the dead stub both the reload and the reset need is written once rather than twice.
  assert.match(command, /function closedSession\(note: string\): ChatSession \{/u);
  // THE CALLER is still in the command file: the reload path is an entry point, and it reaches for
  // the one stub rather than building a second.
  assert.match(source('chatConversationRestore.ts'), /const closed = closedSession\(reloadedNote\(saved\.modelId\)\);/u,
    'the reload path still builds its own copy of the dead session');
});

test('the page is told in its own message — and the SENTENCE travels with the state instead', () => {
  // A state message is the whole truth about every region it MENTIONS. The push does not mention the
  // quotation at the top of the tab, so only a message of its own can clear it — and it DOES mention
  // the line sentences are written in, so a sentence written straight into that line was wiped by
  // the very next push a tick later and nobody ever read it. Two regions, opposite conclusions.
  // (gemini, twice, the code round.)
  const panel = source('chatPanel.ts');
  const page = source('chatPage.ts');
  // The reset feature moved to `chatArchive.ts` when the command file was split; what it must do,
  // and the ORDER it must do it in, are unchanged.
  const command = source('chatArchive.ts');

  assert.match(panel, /entry\.panel\.post\(\{ type: 'fresh', id \}\)/u, 'the fresh message still carries a sentence of its own');
  const handler = page.slice(page.indexOf("data.type === 'fresh'"), page.indexOf("if (data.type !== 'state')"));
  assert.match(handler, /getElementById\('passage'\)[\s\S]{0,200}textContent = ''/u, 'the old conversation’s quotation survives the reset');
  assert.doesNotMatch(handler, /lastWritten\.failure|innerHTML/u,
    'the sentence is written into a region the next state push owns, which wipes it on the following frame');
  // The id, so a tab that was reset and then reloaded comes back as the new conversation — merged
  // into a NEW object rather than written into the one the host handed back.
  assert.match(handler, /vscode\.setState\(Object\.assign\(\{\}, vscode\.getState\(\) \|\| \{\}, \{ id: data\.id \}\)\)/u,
    'the reset does not reach the serializer, or mutates the state object in place');
  // And the sentence itself goes through `show`, which is what makes it survive — the archive's own
  // warning when there was one, and otherwise that the conversation was archived.
  assert.match(command, /show\(entry, false, note\.length > 0 \? note : ARCHIVED\);/u,
    'the sentence is not carried by the state push, so it cannot survive it');
});

test('a reset hands every waiting question back BEFORE the slate moves', () => {
  // The one a re-home nearly lost. `freshening` moved to `chatArchive.ts` when the command file
  // was split, and the drain that belongs at the top of it did not come with it — so New chat
  // silently discarded whatever was queued. codex caught it on the second code round; this is what
  // catches it next time. (issue #288.)
  const archive = source('chatArchive.ts');
  const reset = between(archive, 'export async function freshening(', 'function keepTheOldOne(');

  const drain = reset.indexOf('drained(thread.waiting)');
  const slate = reset.indexOf('thread.generation += 1');

  assert.ok(drain >= 0, 'a reset discards the queued questions instead of returning the words');
  assert.ok(slate >= 0, 'the reset no longer moves the slate at all');
  assert.ok(drain < slate,
    'the words are returned AFTER the slate moves, by which time the generation guard has dropped them');
  assert.match(reset.slice(drain, drain + 400), /pushChatDraft\(entry, unasked\)/u,
    'the drained questions go nowhere a person can see');
});
