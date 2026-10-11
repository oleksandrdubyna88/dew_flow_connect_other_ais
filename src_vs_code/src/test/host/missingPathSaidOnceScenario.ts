import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import * as vscode from 'vscode';

import { activated, settle, until } from './missingDataRootScenario';

/**
 * A path that is STILL missing is said once per window run, however many settings changes follow (todo/PLAN_paths_per_side.md
 * E5.3, in a real editor).
 *
 * <p>Written after what the first host run of E5 showed: sixteen ledger rows for one CLI path. Every change to a `coai`
 * setting makes the watch drop the disk's answers and ask again, and the gap between the drop and the answer read as
 * "no longer missing" — the notice was resolved, and the answer landing said it again, a toast per keystroke in the
 * settings. The rule is a unit value (`pathWatch.test.ts`); this is the same storm where it was seen, with the real
 * configuration events, the real probe and the real ledger.</p>
 */

const CHANGES = 3;

/** The rows of the ledger that SAY the notice for this path — an answered row (a dismissed toast) is not a saying. */
function saidRows(home: string, file: string): readonly string[] {
  const ledger = path.join(home, 'notifications.jsonl');
  const lines = fs.existsSync(ledger) ? fs.readFileSync(ledger, 'utf8').split('\n') : [];
  // The subject as the ledger spells it: JSON-escaped, so a Windows path's backslashes are doubled.
  const subject = JSON.stringify(`coai.vendors[].executablePath|${file}`).slice(1, -1);

  return lines.filter((line) => line.includes('"setting-path-missing"') && line.includes(subject) && !line.includes('"answer"'));
}

export async function aStillMissingPathIsSaidOnceAcrossSettingsChanges(): Promise<void> {
  const home = process.env['COAI_DATA_DIR'] ?? '';
  assert.ok(home.length > 0, 'the launcher named no data directory for this launch');
  await activated();

  // A CLI path of THIS side that is not there — spelled for this OS, under the temp folder, never created.
  const file = path.join(os.tmpdir(), `coai-host-missing-${process.pid}`, process.platform === 'win32' ? 'claude.exe' : 'claude');
  assert.equal(fs.existsSync(file), false, `${file} exists, so this scenario proves nothing about a missing path`);
  const row = { id: 'host-missing-cli', vendor: 'claude', runtime: 'claude', model: 'sonnet', baseUrl: '', executablePath: file, key: '', enabled: true };
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  try {
    await config().update('vendors', [row], vscode.ConfigurationTarget.Global);
    await until(() => saidRows(home, file).length >= 1, `the missing CLI path ${file} was never said as setting-path-missing`);

    // Unrelated settings changes, each of which drops the disk's answers and asks again.
    for (let change = 0; change < CHANGES; change += 1) {
      await config().update('dealPlanLenses', change % 2 === 0, vscode.ConfigurationTarget.Global);
      await settle(1_500);
    }
    await settle(3_000);

    const said = saidRows(home, file);
    assert.equal(said.length, 1,
      `a path still missing was said ${said.length} times after ${CHANGES} settings changes — the toast storm: a settings change must re-ask the disk without resolving a path that is still missing`);
  } finally {
    await config().update('vendors', undefined, vscode.ConfigurationTarget.Global);
    await config().update('dealPlanLenses', undefined, vscode.ConfigurationTarget.Global);
  }
}
