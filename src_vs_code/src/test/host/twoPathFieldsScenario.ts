import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

import { otherSideCliKey, vendorFieldWrite } from '../../cliPathSave';
import { familyOf, type PathFamily } from '../../pathFamily';
import { DEFAULT_VENDORS } from '../../vendors';
import { activated, until } from './missingDataRootScenario';

/**
 * Two CLI-path fields in a real editor (todo/PLAN_paths_per_side.md E4.5): with two fields switched on, Settings › Models
 * opens, the OTHER side's CLI path is saved, and the settings file this side writes for ITS server still carries THIS
 * side's value — the other side's value never reaches this side's server.
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated by the editor; the gear opening the Settings tab on
 * Models; the real configuration store holding a row with both sides' values; the real mirror writing `settings.json`
 * into this launch's data directory; and the real reader (`executableOf`, in the bundle) choosing this side's value for
 * `COAI_VENDORS`. The row is saved exactly as the page's two boxes save it — through `vendorFieldWrite`, the panel's one
 * vendor write, this side's box first and then the other side's (`executablePath.<family>`) — because a host cannot type
 * into a webview.</p>
 *
 * <p><b>What it does NOT drive, and where that is held instead.</b> A host cannot read a webview's DOM or write another
 * extension's `globalState` (the bundle's module state is out of a scenario's reach, as E5 found), so it neither seeds a
 * WSL side record nor sees the two boxes drawn: it switches two fields on with the person's own switch, *Always two*
 * (`coai.pathFieldsPerSide = always`), which is the same decision (`twoPathFields`) a seeded record makes under
 * *Automatic*. That a seeded record makes two fields, that the card draws two labelled boxes, and that typing into the
 * other box posts `executablePath.<family>` are page tests that RUN the page (`twoCliPathFieldsOnTheCard.test.ts`,
 * `pathFieldsPerSideSwitch.test.ts`); that activation stamps the registry in a real editor is `sideSeenScenario.ts`.</p>
 */

/** The row this scenario writes, and removes again. */
const ROW_ID = 'host-two-sides';

/** A CLI path spelled for the other side's OS — never a file here, never asked about. */
function otherSidePath(other: PathFamily): string {
  return other === 'posix' ? '/home/coai-host/.local/bin/codex' : 'C:\\coai-host\\bin\\codex.exe';
}

/** The CLI path the server's settings file names for the row, or undefined while the file does not carry the row. */
function wiredPath(file: string): string | undefined {
  try {
    const env = JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>;
    const rows = JSON.parse(typeof env['COAI_VENDORS'] === 'string' ? env['COAI_VENDORS'] : '[]') as { id?: unknown; executablePath?: unknown }[];
    const row = rows.find((one) => one.id === ROW_ID);

    return typeof row?.executablePath === 'string' ? row.executablePath : undefined;
  } catch {
    return undefined;
  }
}

export async function twoCliPathFieldsKeepThisSidesValueInARealEditor(): Promise<void> {
  const home = process.env['COAI_DATA_DIR'] ?? '';
  assert.ok(home.length > 0, 'the launcher did not give this host a data directory of its own');
  await activated();
  const here = familyOf(process.platform);
  const other: PathFamily = here === 'windows' ? 'posix' : 'windows';
  // This side's value is a file that exists here — the editor itself — so the path watch says nothing about it.
  const mine = process.execPath;
  const theirs = otherSidePath(other);
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  const settingsTabs = (): vscode.Tab[] => vscode.window.tabGroups.all.flatMap((group) => group.tabs)
    .filter((one) => one.input instanceof vscode.TabInputWebview && one.input.viewType.endsWith('coaiSettings'));
  try {
    await config().update('pathFieldsPerSide', 'always', vscode.ConfigurationTarget.Global);
    await vscode.commands.executeCommand('coai.openSettings', 'models');
    await until(() => settingsTabs().length === 1, 'the gear did not open the Settings tab on Models');

    // This side's box, then the other side's box — each saved as the panel's one vendor write saves it.
    const stored = { ...DEFAULT_VENDORS[0]!, id: ROW_ID, vendor: 'codex', runtime: 'codex' as const, model: 'gpt-6.1-sol', enabled: true, executablePath: '' };
    const thisSaved = { ...stored, ...vendorFieldWrite(stored, 'executablePath', mine, here) };
    const bothSaved = { ...thisSaved, ...vendorFieldWrite(thisSaved, otherSideCliKey(other), theirs, here) };
    await config().update('vendors', [bothSaved], vscode.ConfigurationTarget.Global);

    const file = path.join(home, 'settings.json');
    await until(() => wiredPath(file) !== undefined, 'the row with both sides\' CLI paths never reached the file this side\'s server reads');
    assert.equal(wiredPath(file), mine,
      `the settings file this side writes for its server names ${String(wiredPath(file))} — after the other side's path was saved it must still carry this side's own`);
    const read = config().get<readonly Record<string, unknown>[]>('vendors') ?? [];
    assert.deepEqual(read.map((row) => [row['executablePath'], (row['executablePathBySide'] as Record<string, unknown> | undefined)?.[other]]), [[mine, theirs]],
      'the settings do not hold this side\'s value as the legacy one and the other side\'s in its member');
  } finally {
    await config().update('vendors', undefined, vscode.ConfigurationTarget.Global);
    await config().update('pathFieldsPerSide', undefined, vscode.ConfigurationTarget.Global);
    await vscode.window.tabGroups.close(settingsTabs());
  }
}
