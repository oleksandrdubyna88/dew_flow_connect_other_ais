import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  ChatTabMemory,
  KEEP_FOR_MS,
  KEEP_TABS,
  SavedTab,
  TAB_STORE_KEY,
  TAB_VERSION,
  forgotten,
  pruned,
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

test('the newest record for a conversation replaces the one before it', () => {
  const first = tab({ messages: [message('you', 'first')] });
  const second = tab({ savedAt: 2_000, messages: [message('you', 'second')] });

  const held = remembered([first, tab({ id: 'other', savedAt: 500 })], second, 2_000);

  assert.deepEqual(held.map((held) => held.id), ['a1', 'other'], 'newest first, and no duplicate id');
  assert.deepEqual(savedTab(held, 'a1'), second);
});

test('a week-old conversation is not carried forever, and neither are thirty of them', () => {
  const now = 10 * KEEP_FOR_MS;
  const old = tab({ id: 'old', savedAt: now - KEEP_FOR_MS - 1 });
  const fresh = tab({ id: 'fresh', savedAt: now - 1 });

  assert.deepEqual(pruned([old, fresh], now).map((held) => held.id), ['fresh']);

  const many = Array.from({ length: KEEP_TABS + 5 }, (_, at) => tab({ id: `t${at}`, savedAt: now - at }));

  assert.equal(pruned(many, now).length, KEEP_TABS, 'the store grows without bound');
  assert.equal(pruned(many, now)[0].id, 't0', 'the oldest were kept and the newest dropped');
});

test('a conversation the person closed is forgotten; one they only reloaded is not', () => {
  assert.deepEqual(forgotten([tab(), tab({ id: 'b2' })], 'a1').map((held) => held.id), ['b2']);
  assert.deepEqual(forgotten([tab()], 'nobody').map((held) => held.id), ['a1'],
    'forgetting an id nobody holds threw the store away');
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

test('the store is pruned on demand, and a tab is forgotten by id', async () => {
  const fake = fakeStore();
  let now = 5_000;
  const memory = new ChatTabMemory(fake.store, () => now);

  memory.remember({ id: 'a1', title: 'main', passage: 'p', modelId: 'gemini', messages: [] });
  await memory.settled();
  assert.notEqual(memory.saved('a1'), undefined);

  memory.forget('a1');
  await memory.settled();
  assert.equal(memory.saved('a1'), undefined, 'a closed tab was kept');

  memory.remember({ id: 'b2', title: 'other', passage: 'p', modelId: 'codex', messages: [] });
  await memory.settled();
  now += KEEP_FOR_MS + 1;
  memory.prune();
  await memory.settled();

  assert.deepEqual(memory.held(), [], 'a week-old conversation survived the sweep');
});

// ---------------------------------------------------------------------------------------------
// The wiring, which imports `vscode` and so can only be read.
// ---------------------------------------------------------------------------------------------

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the extension tells VS Code how to bring a chat tab back', () => {
  // The symptom, as a test: there was no serializer anywhere, so a reload had nothing to restore
  // through and every tab came back empty or not at all.
  const wiring = source('extension.ts');

  assert.match(wiring, /registerWebviewPanelSerializer\(\s*'coaiChat'/,
    'nothing registers a serializer for the chat tab, so a reload still empties every one of them');
  assert.match(wiring, /chatTabMemory|ChatTabMemory/, 'the serializer has no store to restore from');
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

test('a panel whose conversation is not in the store is not left as an empty tab', () => {
  const wiring = source('extension.ts');
  const handler = wiring.slice(wiring.indexOf('deserializeWebviewPanel'));

  assert.match(handler.slice(0, 700), /panel\.dispose\(\)/,
    'a reload with no record left a chat tab that looks like a conversation and holds none');
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
  assert.match(restore.slice(0, 4_500), /carry: carriedFrom\(saved\.messages,/,
    'a restored conversation hands the next model nothing, so it answers a follow-up it never heard');
});

test('a push that changed nothing writes nothing', () => {
  // `show` runs on every state push — a turn starting, a queue position moving, a failure clearing —
  // and each of those used to rewrite up to twenty whole transcripts into one key. The comparison is
  // by reference, which is exact because `thread.messages` is replaced rather than mutated.
  const command = source('chatCommand.ts');

  assert.match(command, /if \(thread\.savedMessages === thread\.messages && thread\.savedModelId === thread\.modelId\) \{\s*\n\s*return;/,
    'every state push writes the whole store again, transcripts and all');
  assert.match(command, /thread\.savedMessages = thread\.messages;/, 'nothing records what was written');
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
