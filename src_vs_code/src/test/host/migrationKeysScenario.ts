import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { MIGRATION_KEYS } from '../../catalogMigrationHost';
import { unknownKeysOf } from '../../catalogMigrationRun';

/**
 * The registry signal the catalog migration waits on, held to a REAL editor
 * (research/PLAN_catalog_migration_waits_for_its_settings.md; its plan round, findings 1 and 2).
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated, and VS Code's own settings registry read through
 * `WorkspaceConfiguration.inspect` exactly as the migration's user layer reads it: every key the move writes to
 * `settings.json` is known — including `migratedFrom`, `catalogMigration` and `chatPresetsMoved`, which the manifest
 * declares with no default — and a `coai.` key the manifest does not declare is not. If VS Code ever stops filling a
 * default from the type, the first assertion fails here instead of every upgrade waiting for a key it already knows.</p>
 *
 * <p><b>What it does NOT drive.</b> An update in place before activation (the editor under test installs the extension
 * once, before it starts); `migrationWait.test.ts` and `catalogMigrationRun.test.ts` drive what follows from the signal.</p>
 */
export async function theMigrationKeysAreKnownInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  await extension.activate();
  const config = vscode.workspace.getConfiguration('coai');
  const defaultOf = (key: string): unknown => config.inspect(key)?.defaultValue;

  assert.deepEqual(unknownKeysOf(MIGRATION_KEYS, defaultOf), [],
    'a key the migration writes reads as unknown in a real editor — the move would wait for it forever');
  assert.deepEqual(unknownKeysOf(['aKeyNoManifestDeclares'], defaultOf), ['aKeyNoManifestDeclares'],
    'an undeclared key reads as known — the wait would never see the registry lag it exists for');
}
