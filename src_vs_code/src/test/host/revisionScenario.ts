import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { inCatalogTurn } from '../../catalogMigrationHost';
import { chatRead } from '../../chatConfig';
import { movedRecordFrom, type MovedPreset } from '../../chatPresetMove';
import { applyRevisionChoice, chatConflictsReading, type PresetConflict, type RevisionPorts } from '../../chatPresetRevision';
import { presetEdit, type RevisionChoice } from '../../chatPresetsMessages';
import { userChatPresets } from '../../modelKeys';
import { recordsIn, type RawRecord } from '../settingsFileFixture';

/**
 * R7 (research/PLAN_one_model_catalog.md, epic 5 prerequisite (a)) in a REAL editor — its code round's finding 0.
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated: its catalog migration moves a chat preset into a
 * row of its own when `coai.chatModelPresets` changes, and moves nothing — makes no second row — when an older build's
 * edit changes it again. The settings themselves, through VS Code's real configuration layers: the presets the conflict
 * is read from are the USER layer's, so a repository's `.vscode/settings.json` value raises nothing (finding 6). Each
 * choice goes the way the page's press goes after the webview: the page's own message read by `presetEdit`, carried out
 * by `applyRevisionChoice` with the window's own reads (`userChatPresets`, `chatRead`) inside the catalog's turn, and
 * written to the real user settings — which a reload then reads back.</p>
 *
 * <p><b>What it does NOT drive.</b> The webview click: a host cannot reach a webview's DOM, so the press itself is the page
 * tests' (`aRevisionShowsOnChat.test.ts`, run against the DOM shim). Nor `chatPresetsHost.revisionPorts` itself — it needs
 * the extension's own `ExtensionContext`, which only the bundled extension holds; this scenario hands the same reads and a
 * Global `update`, which is what `saveSetting` does on a window that keeps no settings of its own. And the modules here
 * are the compiled test copy, not the bundle: their catalog turn is not the bundle's, so the scenario waits for the
 * bundle's migration to go quiet before each choice rather than relying on the turn to exclude it.</p>
 */

const PRESET_ID = 'host-r7';

const KEYS = ['chatModelPresets', 'vendors', 'chatPresetsMoved', 'chatModel', 'chatModelName'] as const;

const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');

const userValue = (key: string): unknown => config().inspect(key)?.globalValue;

/** A preset as an older build writes it — the raw shape `coai.chatModelPresets` holds. */
function preset(extra: Readonly<Record<string, string>> = {}): RawRecord {
  return { id: PRESET_ID, name: 'Host R7', runtime: 'claude', model: 'opus', executablePath: 'D:\\old\\claude.exe', baseUrl: '', ...extra };
}

/** This preset's entry in force in the user layer's record, or none. */
function entry(): MovedPreset | undefined {
  return [...movedRecordFrom(userValue('chatPresetsMoved'))].filter((one) => one.presetId === PRESET_ID).pop();
}

/** The rows the move made for this preset — exactly one, if no second row was ever written. */
function rowsOfThePreset(): readonly RawRecord[] {
  return recordsIn(userValue('vendors')).filter((row) => String(row['id'] ?? '').startsWith(`chat-${PRESET_ID}`));
}

const conflicts = (): readonly PresetConflict[] => chatConflictsReading(userChatPresets(config()), chatRead(config()))
  .filter((one) => one.presetId === PRESET_ID);

async function wait(ms: number): Promise<void> {
  await new Promise((wake) => setTimeout(wake, ms));
}

/** Until the extension's own migration has stopped writing these keys — a second of no change, within fifteen. */
async function quiet(): Promise<void> {
  const snapshot = (): string => JSON.stringify(KEYS.map(userValue));
  const deadline = Date.now() + 15_000;
  let seen = snapshot();
  let since = Date.now();
  while (Date.now() - since < 1000) {
    assert.ok(Date.now() < deadline, 'the extension\'s migration never went quiet');
    await wait(100);
    const now = snapshot();
    if (now !== seen) {
      seen = now;
      since = Date.now();
    }
  }
}

async function until(ready: () => boolean, whatDidNotHappen: string): Promise<void> {
  const deadline = Date.now() + 15_000;
  while (!ready()) {
    assert.ok(Date.now() < deadline, `${whatDidNotHappen} (waited 15s)`);
    await wait(100);
  }
}

/** The window's reads and the user layer's writes, as a choice in this window makes them. */
function windowPorts(refusals: string[]): RevisionPorts {
  return {
    presets: () => userChatPresets(config()),
    reader: () => chatRead(config()),
    save: async (key, value) => {
      await config().update(key, value, vscode.ConfigurationTarget.Global);
    },
    refused: (key, error) => {
      refusals.push(`${key}: ${error instanceof Error ? error.message : String(error)}`);
    },
    turn: inCatalogTurn,
  };
}

/** One press, as the page posts it after the webview: the message, read by the presets' reader, carried out. */
async function choose(choice: RevisionChoice): Promise<void> {
  await quiet();
  const command = presetEdit({ type: 'revision', id: PRESET_ID, choice });
  assert.equal(command.kind, 'revision', `the page's message was not read as a choice: ${JSON.stringify(command)}`);
  const refusals: string[] = [];
  if (command.kind === 'revision') {
    assert.equal(await applyRevisionChoice(command, windowPorts(refusals)), true, 'a choice did not ask for a redraw');
  }
  assert.deepEqual(refusals, [], 'the real settings refused a choice');
}

const fieldsOf = (all: readonly PresetConflict[]): readonly string[] => all.flatMap((one) => one.fields.map((field) => field.field));

export async function aRevisionInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  await extension.activate();
  const before = KEYS.map((key) => [key, userValue(key)] as const);
  try {
    // 1. This build moves the preset — the shipped extension's own migration, triggered by the change.
    await config().update('chatModelPresets', [preset()], vscode.ConfigurationTarget.Global);
    await until(() => entry()?.copied !== undefined, 'the extension never moved the preset into the catalog with a snapshot');
    const rowId = entry()?.rowId ?? '';

    // 2. An older build edits it: another model and another CLI. The migration runs again and moves nothing.
    await config().update('chatModelPresets', [preset({ model: 'sonnet', executablePath: 'D:\\new\\claude.exe' })], vscode.ConfigurationTarget.Global);
    await quiet();
    assert.deepEqual(rowsOfThePreset().map((row) => row['id']), [rowId], 'the older build\'s edit made a second row');

    // 3. A repository's own value of the presets is not what Chat compares (finding 6).
    await config().update('chatModelPresets', [preset({ name: 'A repository\'s name' })], vscode.ConfigurationTarget.Workspace);
    assert.deepEqual(fieldsOf(conflicts()), ['model', 'executablePath'], 'Chat raised something other than the older build\'s edit');

    // 4. Keep the row: the record takes the edit, the row stays, nothing is raised any more.
    await choose('keep');
    assert.equal(entry()?.copied?.model, 'sonnet', 'Keep the row did not reach the record');
    assert.equal(rowsOfThePreset()[0]?.['model'], 'opus', 'Keep the row changed the row');
    assert.deepEqual(conflicts(), [], 'the conflict survived Keep the row');

    // 5. A later, different edit — on the row the chat opens on — and Use the edited values.
    await config().update('chatModel', rowId, vscode.ConfigurationTarget.Global);
    await config().update('chatModelName', 'opus', vscode.ConfigurationTarget.Global);
    await config().update('chatModelPresets', [preset({ model: 'haiku', executablePath: 'E:\\claude.exe' })], vscode.ConfigurationTarget.Global);
    await quiet();
    assert.deepEqual(fieldsOf(conflicts()), ['model', 'executablePath'], 'a later, different edit was not raised again');
    await choose('use');
    assert.deepEqual([rowsOfThePreset()[0]?.['model'], rowsOfThePreset()[0]?.['executablePath']], ['haiku', 'E:\\claude.exe'], 'the row did not take the edited values');
    assert.equal(userValue('chatModelName'), 'haiku', 'the chat would open the old model on the edited row (finding 5)');
    assert.equal(entry()?.copied?.model, 'haiku', 'Use the edited values did not reach the record');

    // 6. The row write is a migration trigger: the extension runs again, and still makes no second row and raises nothing.
    await quiet();
    assert.deepEqual(rowsOfThePreset().map((row) => row['id']), [rowId], 'the migration after the choice made a second row');
    assert.deepEqual(conflicts(), [], 'the conflict came back after the choice');
  } finally {
    await config().update('chatModelPresets', undefined, vscode.ConfigurationTarget.Workspace);
    for (const [key, value] of before) {
      await config().update(key, value, vscode.ConfigurationTarget.Global);
    }
  }
}
