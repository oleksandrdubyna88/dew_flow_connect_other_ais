import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * E4.6a of todo/PLAN_one_model_catalog.md: no dual store. Once a preset has moved into the catalog it is edited on its
 * row, and `coai.chatModelPresets` is written only for what the move has not taken — a preset before the move, after a
 * refused one, in a restored layer (`chatModelEdits.onPreset`) — and to clear the dead rows no surface can show. The
 * move itself never writes the presets (they stay as they are until E5, for an older build), so its restore has none
 * to put back. The writes that matter import `vscode`, so they are pinned by reading the source.
 */

const KEY = /['"`]chatModelPresets['"`]/u;

test('only the known modules name the presets setting — a new one must say how it keeps to one store', () => {
  const naming = sourceFiles().filter((file) => KEY.test(sourceOf(file)));

  assert.deepEqual(naming, [
    // Reads: the migration's trigger and layer read, the chat's list, the chat settings, the model keys' one reader,
    // and the spend ledger's vendor lookup for an old line.
    'catalogMigrationHost.ts', 'chatConfig.ts',
    // The edit of a preset the move has not taken.
    'chatModelEdits.ts',
    // The presets' editing core, which both pages that edit them call (E4.6b) — its two writes are pinned below.
    'chatPresetsHost.ts', 'chatSettings.ts', 'modelKeys.ts', 'panelProvider.ts',
  ], 'a module began to name chat model presets — if it writes them, a moved preset can be edited in two places');
});

test('the presets’ editing core writes the presets in exactly two places, and the model edit picks the store', () => {
  const page = sourceOf('chatPresetsHost.ts');

  assert.equal(page.split('write(MODELS_KEY').length - 1, 2, 'the presets’ editing core gained a write to the presets');
  // The model edit's write goes there only when `chatModelEdit` named the presets — never a moved preset's row.
  assert.match(page, /if \(one\.key === 'chatModelPresets'\) \{\s*await write\(MODELS_KEY, one\.value\);/u);
  // And the other is the prune of rows nobody can show, which a preset with a provider never is.
  assert.match(page, /const kept = rows\.filter\(\(row\) => !deadModelRow\(row\)\);\s*if \(kept\.length !== rows\.length\) \{\s*await write\(MODELS_KEY, kept\);/u);
});
