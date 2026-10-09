import assert from 'node:assert/strict';
import { test } from 'node:test';
import { chatSettingsFrom } from '../chatSettings';

/**
 * E4.6a of research/PLAN_one_model_catalog.md, the panel's half. After the move `coai.chatModel` names a ROW, so the
 * settings' chat picker must list the rows ticked Chat — the same list the chat itself opens from — and not the presets
 * the move took. Listing the presets showed the chat's own model as one that "cannot answer a chat", and offered the
 * old preset ids beside it.
 */

const reading = (values: Record<string, unknown>) => (key: string) => values[key];

const PRESETS = [
  { id: 'deep', name: 'Deep', runtime: 'claude', model: 'opus', main: true },
  { id: 'later', name: 'Not moved yet', runtime: 'claude', model: 'haiku', main: false },
];
const ROW = {
  id: 'chat-deep', name: 'Deep', runtime: 'claude', model: 'opus', enabled: true, plan: false, code: false, document: false,
  baseUrl: '', executablePath: '', uses: ['chat'],
};
const RECORD = [{ presetId: 'deep', runtime: 'claude', model: 'opus', name: 'Deep', rowId: 'chat-deep' }];

test('the panel lists the row a preset moved to, and a preset only while the move has not taken it', () => {
  const chat = chatSettingsFrom(reading({ chatModelPresets: PRESETS, vendors: [ROW], chatPresetsMoved: RECORD, chatModel: 'chat-deep' }));

  assert.deepEqual(chat.models.map((one) => one.id), ['chat-deep', 'later']);
});

test('before any move the panel lists the presets, as it always did', () => {
  const chat = chatSettingsFrom(reading({ chatModelPresets: PRESETS }));

  assert.deepEqual(chat.models.map((one) => one.id), ['deep', 'later']);
});
