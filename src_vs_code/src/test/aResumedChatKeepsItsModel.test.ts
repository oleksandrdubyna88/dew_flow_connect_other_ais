import assert from 'node:assert/strict';
import { test } from 'node:test';
import { movedTo, resumedPick } from '../chatCatalogModels';
import { catalogUsing, EMPTY_DISCOVERY } from '../chatDiscovery';
import { chatProvidersFromPresets, legacyPick } from '../chatModels';
import { chatRunSpec, type ModelPreset } from '../chatPresets';
import type { MovedPreset } from '../chatPresetMove';
import { CONVERSATION_VERSION, recordFrom } from '../chatStore';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md, the guarantee the move exists to keep: a conversation resumed after its
 * preset moved into the catalog opens on the SAME model — the row it was spoken to when that row is still there, and an
 * old preset id followed to the row it became — never the first model in the list, which is somebody else's model,
 * prompt and effort.
 */

const model = (id: string, extra: Partial<ModelPreset> = {}): ModelPreset => ({
  id, name: id, runtime: 'claude', model: 'opus', main: false, executablePath: '', baseUrl: '', ...extra,
});

/** Two rows that offer the same model name: a model name alone cannot tell them apart. */
const MODELS = [model('chat-fast', { model: 'sonnet' }), model('chat-deep', { model: 'sonnet' })];
const list = chatProvidersFromPresets(MODELS, catalogUsing(EMPTY_DISCOVERY, []));
const specs = MODELS.map(chatRunSpec);
const RECORD: readonly MovedPreset[] = [{ presetId: 'preset-7', runtime: 'claude', model: 'sonnet', name: 'Deep', rowId: 'chat-deep' }];

test('a conversation that recorded its row resumes on that row, though another row offers the same model', () => {
  assert.deepEqual(resumedPick(list, specs, RECORD, 'chat-deep', 'sonnet'), { providerId: 'chat-deep', modelId: 'sonnet', candidates: [] });
});

test('an old preset id — a record\'s, or coai.chatModel\'s — is followed to the row it became', () => {
  assert.equal(movedTo('preset-7', RECORD), 'chat-deep');
  assert.equal(movedTo('chat-fast', RECORD), 'chat-fast', 'a row id is not a preset id');
  assert.equal(resumedPick(list, specs, RECORD, '', 'preset-7').providerId, 'chat-deep');
});

test('a saved value that names a row that exists is that row, though an old preset had the same id', () => {
  // `coai.chatModel` has always held a ROW id; an old preset that happened to share it must not capture the choice.
  const shadowing: readonly MovedPreset[] = [{ presetId: 'chat-fast', runtime: 'claude', model: 'sonnet', name: 'Old', rowId: 'chat-deep' }];

  assert.equal(resumedPick(list, specs, shadowing, '', 'chat-fast').providerId, 'chat-fast');
});

test('a recorded row that is gone falls back to what the model name says, as before', () => {
  const pick = resumedPick(list, specs, RECORD, 'chat-removed', 'sonnet');

  assert.notEqual(pick.providerId, 'chat-removed', 'a conversation was resumed on a row that is not there');
  // Exactly what the model name says on its own — here two rows offer it, so the person is asked, never guessed for.
  assert.deepEqual(pick, legacyPick(list, specs, 'sonnet'));
  assert.deepEqual(pick.candidates, ['chat-fast', 'chat-deep']);
});

test('the record keeps the row it was spoken to; one written before it existed reads as it did', () => {
  const base = {
    version: CONVERSATION_VERSION, rev: 1, id: 'c1', title: 't', passage: '', modelId: 'sonnet', messages: [], fromSession: false, carryFrom: 0,
    source: { kind: 'none' }, workspace: '', createdAt: 1, updatedAt: 1,
  };
  const at = base;

  assert.equal(recordFrom({ ...at, providerId: 'chat-deep' })?.providerId, 'chat-deep');
  assert.equal(recordFrom(at)?.providerId, undefined);
  assert.ok(recordFrom({ ...at, providerId: 42 }) !== undefined, 'a malformed field threw the whole conversation away');
  assert.equal(recordFrom({ ...at, providerId: 42 })?.providerId, undefined);
});
