import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChatTabMemory,
  SavedTab,
  TAB_STORE_KEY,
  TAB_VERSION,
  reloadedNote,
  remembered,
  savedTab,
  stored,
  tabsFrom,
} from '../chatTabs';

/**
 * A window reload no longer empties every chat tab.
 *
 * <p>2026-09-09: there was no `registerWebviewPanelSerializer` anywhere in this extension, so a
 * reload — or an extension update restarting the host — took every open conversation with it.
 * `retainContextWhenHidden` keeps a panel alive while it is HIDDEN, which is a different thing.</p>
 *
 * <p><b>Identity is the whole design, and the plan round is why.</b> The first draft keyed the store
 * by the tab's TITLE, because without touching `chatPage.ts` nothing calls `setState` and a title is
 * the only thing VS Code preserves. Six reviewers took that apart independently: two namesake tabs
 * would overwrite each other on the way IN, so the collision could never be detected on the way out;
 * a `deserializeWebviewPanel` call cannot know whether another panel of the same name is still
 * coming; and `workspaceState` is shared by two windows on one folder. The conversation now carries
 * its own id, minted where the tab is made and echoed back by the page.</p>
 *
 * <p><b>Story A4 of the go-to-conversation plan moved the source of truth to the store on disk</b>
 * (`chatStoreFile.ts`). The memento this suite was written for is now what earlier builds wrote: the
 * migration (`chatStoreImport.test.ts`) carries it across once, the serializer reads the STORE and
 * falls back to the memento only while its key still holds anything, and the memento goes on being
 * written only until that migration has succeeded in the window. The pure rules over the memento's
 * shape are still tested here because they are still run — by the migration on the way in, and by the
 * dual write until it ends. What was removed with the code: the activation-time prune and the
 * `forget` path.</p>
 */

const message = (role: 'you' | 'model', text: string) => ({ role, text });
const tab = (over: Partial<SavedTab> = {}): SavedTab => ({
  id: 'a1',
  savedAt: 1_000,
  title: 'main',
  passage: 'the selected text',
  modelId: 'gemini',
  messages: [message('you', 'поясни'), message('model', 'вот что это значит')],
  ...over,
});

// ---------------------------------------------------------------------------------------------
// What is stored, and what a store that has been damaged does.
// ---------------------------------------------------------------------------------------------

test('a conversation round-trips through the store unchanged', () => {
  const read = tabsFrom(stored([tab()]));

  assert.deepEqual(read, [tab()]);
  assert.deepEqual(savedTab(read, 'a1'), tab());
  assert.equal(savedTab(read, 'nobody'), undefined);
});

test('the memento judges a message by the same rule the store does', () => {
  // There were TWO copies of that rule, and when the store's was tightened to check the optional
  // fields the memento's was not. The gap was not cosmetic: an entry carrying a malformed `model`
  // passed here, was carried into the store by the migration, and came back from the store as
  // unreadable — so a conversation that should have been set aside in the quarantine, where a person
  // can go and get it, became one that reads as broken instead. One rule, one module, and this is
  // what fails if a third copy ever appears. (CodeRabbit, pull request #223.)
  const malformed = { ...tab(), messages: [{ role: 'model', text: 'hello', model: { id: 'x', label: null } }] };

  assert.deepEqual(tabsFrom(stored([malformed as never])), [],
    'a message the page would throw on while rendering was accepted by the old store');
  // And what is merely ABSENT is still legal: a record written before those fields existed is a real
  // conversation, and discarding it to tighten a check would be the opposite of the point.
  assert.equal(tabsFrom(stored([tab()])).length, 1, 'a record without the optional fields was dropped');
});

test('a store written by another version is not guessed at', () => {
  assert.deepEqual(tabsFrom({ version: TAB_VERSION + 1, tabs: [tab()] }), [],
    'a record from a shape this build does not know was rendered anyway');
  assert.deepEqual(tabsFrom({ tabs: [tab()] }), [], 'a record with no version was rendered anyway');
});

test('a record that cannot be read is dropped, and its neighbours survive', () => {
  // Every field is checked because every field is rendered, and this value can be edited by hand,
  // written by another build, or half-written by a host that was killed mid-update.
  const damaged: unknown[] = [
    null,
    'a conversation',
    { ...tab(), id: '' },
    { ...tab(), savedAt: 'yesterday' },
    { ...tab(), messages: 'поясни' },
    { ...tab(), messages: [{ role: 'somebody else', text: 'hi' }] },
    { ...tab(), messages: [{ role: 'you' }] },
  ];

  for (const bad of damaged) {
    assert.deepEqual(
      tabsFrom(stored([bad as SavedTab, tab({ id: 'b2' })])), [tab({ id: 'b2' })],
      `a damaged record was read back as a conversation: ${JSON.stringify(bad)}`,
    );
  }
});

test('the newest record for a conversation replaces the one before it, and NOTHING is cut', () => {
  const first = tab({ messages: [message('you', 'first')] });
  const second = tab({ savedAt: 2_000, messages: [message('you', 'second')] });

  const held = remembered([first, tab({ id: 'other', savedAt: 500 })], second);

  assert.deepEqual(held.map((held) => held.id), ['a1', 'other'], 'newest first, and no duplicate id');
  assert.deepEqual(savedTab(held, 'a1'), second);

  // The week-and-twenty cut this made on every write went with story A4 (codex, its code round): while
  // the memento is still written — until the migration has confirmed every record is on disk — a legacy
  // record whose store write failed could be dropped from the memento by an ordinary write BEFORE the
  // next activation's migration read it. Thirty old records and one new one are thirty-one.
  const thirty = Array.from({ length: 30 }, (_, at) => tab({ id: `t${at}`, savedAt: 1 }));
  const week = 7 * 24 * 60 * 60 * 1000;

  assert.equal(remembered(thirty, tab({ id: 'new', savedAt: 10 * week })).length, 31,
    'a write to the memento shrank it — a record the migration has not read yet may be gone');
});

// ---------------------------------------------------------------------------------------------
// The store as the host uses it: one key, many writers.
// ---------------------------------------------------------------------------------------------

function fakeStore(): { store: { get(k: string): unknown; update(k: string, v: unknown): Promise<void> }; writes(): number } {
  let held: unknown;
  let writes = 0;

  return {
    store: {
      get: (key: string) => (key === TAB_STORE_KEY ? held : undefined),
      update: async (key: string, value: unknown) => {
        writes += 1;
        // A tick, so a queue that did not exist would let two writers read the same old value.
        await Promise.resolve();
        if (key === TAB_STORE_KEY) {
          held = value;
        }
      },
    },
    writes: () => writes,
  };
}

test('two tabs saving at the same moment do not drop each other', async () => {
  const fake = fakeStore();
  const memory = new ChatTabMemory(fake.store, () => 5_000);

  memory.remember({ id: 'a1', title: 'main', passage: 'p', modelId: 'gemini', messages: [] });
  memory.remember({ id: 'b2', title: 'other', passage: 'p', modelId: 'codex', messages: [] });
  await memory.settled();

  assert.deepEqual(memory.held().map((held) => held.id).sort(), ['a1', 'b2'],
    'a write read a value another write had not finished replacing');
});

test('a storage failure does not stop every write after it', async () => {
  const fake = fakeStore();
  let failNext = true;
  const flaky = {
    get: (key: string) => fake.store.get(key),
    update: async (key: string, value: unknown) => {
      if (failNext) {
        failNext = false;
        throw new Error('settings.json is not writable');
      }
      await fake.store.update(key, value);
    },
  };
  const memory = new ChatTabMemory(flaky, () => 5_000);

  memory.remember({ id: 'a1', title: 'main', passage: 'p', modelId: 'gemini', messages: [] });
  memory.remember({ id: 'b2', title: 'other', passage: 'p', modelId: 'codex', messages: [] });
  await memory.settled();

  assert.deepEqual(memory.held().map((held) => held.id), ['b2'],
    'one rejected write poisoned the queue and nothing was ever saved again');
});

test('a remembered tab is read back by id, and nothing on the memory prunes or forgets any more', async () => {
  // `prune()` went with story A4: nothing may shrink the memento before the migration has read it,
  // and the store's own retention is story B1's sweep. `forget()` never had a production caller.
  const fake = fakeStore();
  const memory = new ChatTabMemory(fake.store, () => 5_000);

  memory.remember({ id: 'a1', title: 'main', passage: 'p', modelId: 'gemini', messages: [] });
  await memory.settled();

  assert.notEqual(memory.saved('a1'), undefined);
  assert.equal(memory.saved('nobody'), undefined);
  assert.equal('prune' in memory, false, 'an activation-time prune is back, and would shrink the memento before it is migrated');
  assert.equal('forget' in memory, false);
});

// ---------------------------------------------------------------------------------------------
// The wiring, which imports `vscode` and so can only be read.
// ---------------------------------------------------------------------------------------------

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the extension tells VS Code how to bring a chat tab back, and it reads the STORE to do it', () => {
  // The symptom, as a test: there was no serializer anywhere, so a reload had nothing to restore
  // through and every tab came back empty or not at all.
  const wiring = source('extension.ts');
  const handler = wiring.slice(wiring.indexOf('deserializeWebviewPanel'));

  assert.match(wiring, /registerWebviewPanelSerializer\(\s*'coaiChat'/,
    'nothing registers a serializer for the chat tab, so a reload still empties every one of them');
  assert.match(handler.slice(0, 600), /restoreAfterReload\(restoreDeps, panel, state, migration\)/,
    'the serializer restores through something other than the store path, or hands it a different store than the writers use');
  assert.match(source('chatRestorePanel.ts'), /deps\.store\.read\(id\)/,
    'the serializer does not read the store, so a conversation the memento no longer holds is not found');
  assert.doesNotMatch(handler.slice(0, 1_200), /chatTabMemory\.saved\(/,
    'the serializer still looks the conversation up in the memento first, a version too late');
});

test('the serializer waits for the migration before it answers — a tab not yet carried across must not read as absent', () => {
  // THE SECOND FATAL FINDING of A4's plan round. VS Code deserializes panels DURING activation, and a
  // conversation the migration has not written yet reads as `absent` in the store, which the first
  // brief said to dispose: an open tab destroyed on the first reload after the upgrade, the single most
  // visible moment this feature has. The wait has a CEILING and the tab is drawn before it (the code
  // round's four findings on a blank tab) — `chatRestore.test.ts` holds that order.
  const wiring = source('extension.ts');
  const panel = source('chatRestorePanel.ts');
  const body = panel.slice(panel.indexOf('export async function restoreAfterReload'), panel.indexOf('export async function restoreChatTab'));

  assert.match(body, /await withinCeiling\(migration, MIGRATION_WAIT_MS\);/, 'the serializer answers before the migration has had its say');
  assert.ok(body.indexOf('withinCeiling(migration') < body.indexOf('restoreChatTab('),
    'the migration is waited for after the store has already been read');
  assert.ok(wiring.indexOf('importLegacyTabs(') < wiring.indexOf('registerWebviewPanelSerializer'),
    'the migration is started after the serializer could already have been called');
});

test('the memento is written until the migration has SEALED it — and sealing drains the queue before the key goes', () => {
  // The first brief cut over unconditionally, which meant: store unavailable → memento preserved →
  // the cut-over still happens → the person's next words are written NOWHERE. (A4's plan round.) Then
  // the code round found the race in the fix: `ChatTabMemory` QUEUES, so a write issued a moment before
  // the clear lands after it and fills the key again. The seal is the one retirement point: unbind, then
  // drain; and the way back when the clear did not happen.
  const wiring = source('extension.ts');
  const host = source('chatHost.ts');

  assert.match(wiring, /seal: async \(\) => \{\s*\n\s*retireMemento\(\);\s*\n\s*await chatTabMemory\.settled\(\);/,
    'the seal does not unbind the writer and then drain its queue, in that order');
  assert.match(wiring, /unseal: \(\) => \{\s*\n\s*rememberChatsIn\(chatTabMemory\);/,
    'a clear that did not happen leaves this window writing one store while the other is in charge');
  assert.equal(wiring.split('retireMemento()').length - 1, 1, 'the memento is retired from somewhere other than the seal');
  // The binders moved to `chatHost.ts` when the command file was split: three handles a window
  // binds once, read by five of the modules coming out of it, so leaving them behind made every
  // extraction a cycle. What they DO is unchanged, and that is what this still asserts.
  assert.match(host, /export function retireMemento\(\): void \{\s*\n\s*memory = undefined;/,
    'retiring the memento does something other than unbind it, so a memory?. site keeps writing');
  // TWO writers, counted rather than matched. The page push and the fork both write the memento,
  // and they are in two modules now — a match over both files together would pass on either one
  // alone, which is a weaker assertion than the one that was here before the split.
  assert.equal(
    (source('chatShow.ts') + source('chatPersist.ts')).split('memory?.remember(').length - 1,
    2,
    'the memento is no longer written by both the page push and the fork — a store that cannot be'
    + ' reached leaves words nowhere');
  assert.doesNotMatch(wiring, /chatTabMemory\.prune\(\)/,
    'the activation-time prune is back, and shrinks the memento before the migration has read it');
  assert.doesNotMatch(source('chatTabs.ts'), /KEEP_TABS|KEEP_FOR_MS|\.slice\(0, /,
    'a cut on the memento is back — a record the migration has not read yet can be dropped by an ordinary write');
});

test('a restored tab is built the way an opened one is', () => {
  // The requirement `theTabWearsAnIcon.test.ts` holds from the other side: a panel restored past
  // `createChatPanel` would come back without its icon, without its message wiring and without its
  // disposal. This asserts the same rule from this plan's side, so removing either test still leaves
  // one of them saying it.
  assert.match(source('chatCommand.ts'), /createChatPanel\(/,
    'the restore path builds a panel some other way');
});

test('a restored tab says what it is, and names the model the next question goes to', () => {
  const note = reloadedNote('gemini');

  assert.match(note, /closed by a window reload/,
    'a tab full of text with nothing saying the process is gone reads as a live conversation');
  assert.match(note, /carries everything above across/,
    'nothing warns that the next question re-sends the whole transcript, which is what it costs');
  assert.ok(note.includes('gemini'), 'the sentence does not name the model that will answer');
});

test('a panel whose conversation is NOWHERE is not left as an empty tab — and that is the only thing that disposes one', () => {
  // The store answers four ways, and only `absent` — with the memento holding nothing either — means
  // "no such conversation". `incompatible` (a newer build's record) and `unavailable` (a disk that
  // would not answer) keep the tab and say so; disposing over either throws a person's conversation
  // away over a permissions error or a downgrade. `chatRestore.test.ts` holds the decision to that
  // as values; this holds the wiring to it as source.
  const panel = source('chatRestorePanel.ts');

  assert.match(panel, /decision\.kind === 'dispose'\)\s*\{[\s\S]{0,400}panel\.dispose\(\)/,
    'a reload with no record left a chat tab that looks like a conversation and holds none');
  // Two disposals, both "nowhere": a state whose id nothing can be filed under (validated at the
  // boundary — `persistedId` — with the legal shape named on the console), and a conversation the
  // store and the memento both say is absent.
  assert.equal(panel.split('panel.dispose()').length - 1, 2,
    'the panel is disposed somewhere other than the two arms where the conversation is nowhere');
  assert.match(panel, /if \(id\.length === 0\) \{[\s\S]{0,600}panel\.dispose\(\)/,
    'a state with no usable id is not refused at the boundary');
  assert.match(panel, /showNotice\(/, 'the two answers that keep the tab draw nothing on it');
  assert.match(source('chatRestore.ts'), /case 'incompatible':\s*\n\s*return \{ kind: 'notice'/,
    'a record this build cannot read is not given a defined tab');
  assert.match(source('chatRestore.ts'), /case 'unavailable':\s*\n\s*return \{ kind: 'notice', sentence: unavailableNotice\(seen\.reason\), retry: true \}/,
    'a disk that would not answer is not given a tab with a retry');
  // And the serializer itself disposes nothing: every decision about a tab is in one module.
  const wiring = source('extension.ts');
  const handler = wiring.slice(wiring.indexOf('deserializeWebviewPanel'));
  assert.doesNotMatch(handler.slice(0, 1_400), /panel\.dispose\(\)/,
    'the serializer disposes a panel on its own account, outside the one module that decides');
});

test('no process is started until the first question after a restore', () => {
  // The stop condition of the plan, as a test. A window with five restored tabs must spawn nothing:
  // the process behind each of them died with the window, and most restored tabs are never spoken to
  // again. `reopened` is what opens one, and it runs inside a turn.
  const command = source('chatCommand.ts');
  const restore = command.slice(command.indexOf('export function restoreConversation'));

  assert.doesNotMatch(restore.slice(0, 2_000), /started\(/,
    'restoring a tab starts a vendor process for a conversation nobody has asked anything');
  assert.match(command, /const refused = await reopened\(thread\);/,
    'a turn no longer opens the session a restored conversation has not got');
  assert.match(command, /thread\.reopen = false;/, 'a reopened conversation would reopen on every turn');
});

test('the first question after a restore carries the whole transcript', () => {
  const command = source('chatCommand.ts');
  const restore = command.slice(command.indexOf('export function restoreConversation'));

  // The WINDOW, not the file: this asserts the carry is set where a restored thread is built, and it
  // is widened when that function grows rather than dropped. It grew when a restored tab started
  // carrying its prompt, its role and the words this side last wrote.
  // It grew again when a restored tab started remembering which door it came through.
  // And again in story A4, when a restored thread started sharing one transcript array with its
  // "already saved" mark so that a reload writes nothing.
  // And again in story C3, when a restored conversation could be bound to the tab it belongs to.
  assert.match(restore.slice(0, 6_400), /carry: carriedFrom\(saved\.messages,/,
    'a restored conversation hands the next model nothing, so it answers a follow-up it never heard');
});

test('a push that changed nothing writes nothing', () => {
  // `show` runs on every state push — a turn starting, a queue position moving, a failure clearing —
  // and each of those used to rewrite up to twenty whole transcripts into one key. The comparison is
  // by reference, which is exact because `thread.messages` is replaced rather than mutated.
  // The page push moved to `chatShow.ts` when the command file was split — it had to come out
  // before the archive, the turn, the launch and the hooks, all of which call it.
  const command = source('chatShow.ts');

  assert.match(
    command,
    /if \(thread\.savedMessages === thread\.messages\s*\n\s*&& thread\.savedModelId === thread\.modelId\s*\n\s*&& thread\.savedCarryFrom === thread\.carryFrom\) \{\s*\n\s*return;/,
    'every state push writes the whole store again, transcripts and all',
  );
  assert.match(command, /thread\.savedMessages = thread\.messages;/, 'nothing records what was written');
  // THE MARK COUNTS AS A CHANGE. Pressing Carry nothing above moves neither the transcript nor the
  // model, so a guard comparing only those two skipped the write — and the rule the person had just
  // drawn would not have survived a reload, without a word about it.
  assert.match(command, /thread\.savedCarryFrom = thread\.carryFrom;/,
    'a press that changes only the mark is not written down');
});

test('a question refused by a dead conversation comes back to the composer', () => {
  const command = source('chatCommand.ts');
  const guard = command.slice(command.indexOf('const refused = await reopened(thread);'));

  assert.match(guard.slice(0, 600), /pushChatDraft\(entry, text\)/,
    'the refusal threw away what the person typed, so they must write it again to try the fix');
});

test('a record written before the door was remembered still reads, as a session tab', () => {
  // The field is OPTIONAL rather than a TAB_VERSION bump — a bump discards every stored conversation
  // to carry one boolean. Every tab that could have been written before it existed was a session one.
  const old = {
    id: 'c1',
    savedAt: 10,
    title: 'Подключение к БД',
    passage: '',
    modelId: 'antigravity',
    messages: [],
  };

  const read = tabsFrom({ version: TAB_VERSION, tabs: [old] });

  assert.strictEqual(read.length, 1, 'a record from before the field was dropped');
  assert.strictEqual(read[0]?.fromSession, undefined, 'a field nobody wrote was invented on the way in');
});

test('a door that is not a boolean is a record this build cannot trust', () => {
  const bad = {
    id: 'c1',
    savedAt: 10,
    title: 'x',
    passage: '',
    modelId: 'antigravity',
    messages: [],
    fromSession: 'yes',
  };

  assert.deepStrictEqual(tabsFrom({ version: TAB_VERSION, tabs: [bad] }), []);
});

test('the door a chat came through survives the reload', () => {
  const fromFile = {
    id: 'c1',
    savedAt: 10,
    title: 'chatPage.ts',
    passage: '',
    modelId: 'antigravity',
    messages: [],
    fromSession: false,
  };

  assert.strictEqual(tabsFrom({ version: TAB_VERSION, tabs: [fromFile] })[0]?.fromSession, false);
});
