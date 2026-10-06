import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatModelAdd, chatModelEdit, type ChatModelStores } from '../chatModelEdits';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md: the chat presets page edits ONE store. A model that is a catalog row is
 * edited on the row — so a moved preset can never be edited in `chatModelPresets` again — and a preset the move has not
 * taken (before the move, after a refused one, in a restored layer) keeps its own path. Main is the chat model.
 */

const chatRow = { id: 'chat-a', name: 'A', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false, baseUrl: '', executablePath: '', uses: ['chat'], vaultKeyName: 'a' };
const reviewer = { ...DEFAULT_VENDORS[0]!, uses: ['chat'] } as Record<string, unknown>;
const stores = (extra: Partial<ChatModelStores> = {}): ChatModelStores => ({
  rows: [...DEFAULT_VENDORS.map((row) => ({ ...row })), chatRow, reviewer].filter((row, at, all) => all.findIndex((one) => one['id'] === row['id']) === at),
  presets: [{ id: 'old', name: 'Old', runtime: 'claude', model: 'haiku', main: false }],
  chatModel: 'chat-a',
  ...extra,
});

const edit = (id: string, field: string, value: string | boolean) => ({ kind: 'edit' as const, list: 'model' as const, id, field, value });

test('a row\'s name, model and starting text are written on the row — never into the presets', () => {
  for (const [field, key, value] of [['name', 'name', 'Architect'], ['model', 'model', 'sonnet'], ['startingPrompt', 'chatStartingPrompt', 'You review APIs.']] as const) {
    const writes = chatModelEdit(edit('chat-a', field, value), stores());
    assert.deepEqual(writes.map((one) => one.key), ['vendors'], field);
    const row = (writes[0]!.value as readonly Record<string, unknown>[]).find((one) => one['id'] === 'chat-a')!;
    assert.equal(row[key], value);
    assert.equal(row['vaultKeyName'], 'a', 'a field the edit does not name was dropped');
  }
});

test('emptying a row\'s starting text removes it from the row', () => {
  const rows = chatModelEdit(edit('chat-a', 'startingPrompt', '  '), stores({ rows: [{ ...chatRow, chatStartingPrompt: 'x' }] }))[0]!.value as readonly Record<string, unknown>[];

  assert.ok(!('chatStartingPrompt' in rows[0]!));
});

test('ticking main makes the row the chat model and clears a stale model name; unticking the chat model is refused', () => {
  assert.deepEqual(chatModelEdit(edit(reviewer['id'] as string, 'main', true), stores()), [
    { key: 'chatModel', value: reviewer['id'] }, { key: 'chatModelName', value: '' },
  ]);
  assert.deepEqual(chatModelEdit(edit('chat-a', 'main', false), stores()), [], 'the model a new conversation opens on was left as none');
});

test('removing a chat-only row removes it; removing a reviewer from the chat only unticks Chat', () => {
  const gone = chatModelEdit({ kind: 'remove', list: 'model', id: 'chat-a' }, stores())[0]!.value as readonly Record<string, unknown>[];
  assert.ok(!gone.some((row) => row['id'] === 'chat-a'));

  const unticked = chatModelEdit({ kind: 'remove', list: 'model', id: reviewer['id'] as string }, stores())[0]!.value as readonly Record<string, unknown>[];
  const still = unticked.find((row) => row['id'] === reviewer['id'])!;
  assert.deepEqual(still['uses'], [], 'a reviewer was deleted for leaving the chat');
});

test('a preset the move has not taken keeps its own path — the only time the presets are written', () => {
  const writes = chatModelEdit(edit('old', 'name', 'Older'), stores());

  assert.deepEqual(writes.map((one) => one.key), ['chatModelPresets']);
  assert.equal((writes[0]!.value as readonly Record<string, unknown>[])[0]!['name'], 'Older');
});

test('an id naming nothing, or a value already held, writes nothing', () => {
  assert.deepEqual(chatModelEdit(edit('nobody', 'name', 'x'), stores()), []);
  assert.deepEqual(chatModelEdit(edit('chat-a', 'name', 'A'), stores()), []);
});

test('a model added on the page is a catalog row ticked Chat, never a preset', () => {
  const rows = chatModelAdd(stores().rows, { runtime: 'codex', baseUrl: 'https://api.example.test/v1', executablePath: '' }, 'gpt-5', 'Fast reader', 'Be brief.');
  const added = rows.at(-1)!;

  assert.deepEqual(added, {
    id: 'chat-fast-reader', name: 'Fast reader', runtime: 'codex', model: 'gpt-5', enabled: true, plan: false, code: false, document: false,
    baseUrl: 'https://api.example.test/v1', executablePath: '', uses: ['chat'], chatStartingPrompt: 'Be brief.',
  });
});
