import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

/**
 * A NAMED data directory that is missing is always red, and nothing creates it (todo/PLAN_paths_per_side.md E5.7, D3)
 * — the two halves a real editor can answer, each in the launch that can reach it (`scripts/run-host.mjs` runs two).
 *
 * <p><b>The first-start guarantee</b>, in the ordinary launch: the launcher names an existing data directory, the
 * extension activates, mirrors its settings and raises whatever it raises — and the ledger holds no
 * `setting-path-missing`. (The default location, which is never checked, is a value: `dataDirFinding(undefined, …)`.)</p>
 *
 * <p><b>The missing named root</b>, in the SECOND launch, whose `COAI_DATA_DIR` names a folder that does not exist: after
 * activation, and after the notice has settled — its toast shown and its record handled, which is exactly the write that
 * used to create the folder — the folder is still absent, with no settings file and no ledger in it. Then the folder is
 * created and a setting changed: the mirror, which stood aside, writes the settings file within seconds — the proof that
 * it was the guard holding it, not a mirror that never ran.</p>
 *
 * <p>What a host cannot observe is the toast itself: the held notice is written to no ledger by design, and the extension
 * runs from its bundle, whose module state a scenario cannot read. The notice, its once-per-run and its button are values
 * (`pathWatch.test.ts`, `findingsBoard.test.ts`); this is the half only a host can see.</p>
 */

/** The launcher sets this for the second launch, whose data directory is a folder that is not there. */
export const MISSING_ROOT_LAUNCH = 'COAI_HOST_MISSING_DATA_ROOT';

const SETTLE_MS = 4_000;

/** Shared with the other E5 scenario (`missingPathSaidOnceScenario.ts`): a wait, the activation, and a bounded until. */
export const settle = (ms: number): Promise<void> => new Promise((resolve) => { setTimeout(resolve, ms); });

export async function activated(): Promise<void> {
  const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  await extension.activate();
}

export async function until(ready: () => boolean, whatDidNotHappen: string, ms = 15_000): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (ready()) {
      return;
    }
    await settle(100);
  }
  assert.fail(`${whatDidNotHappen} (waited ${Math.round(ms / 1000)}s)`);
}

/** The ordinary launch: an existing named root, and no missing-path notice in its ledger after activation settles. */
export async function aFirstStartRaisesNoMissingPathNotice(): Promise<void> {
  const home = process.env['COAI_DATA_DIR'] ?? '';
  assert.ok(home.length > 0 && fs.existsSync(home), 'this half needs the launcher\'s existing data directory');
  await activated();
  await settle(SETTLE_MS);

  const ledger = path.join(home, 'notifications.jsonl');
  const lines = fs.existsSync(ledger) ? fs.readFileSync(ledger, 'utf8').split('\n') : [];
  const raised = lines.filter((line) => line.includes('"setting-path-missing"'));
  assert.deepEqual(raised, [], 'a first start with an existing named data directory raised the missing-path notice');
}

/** The second launch: the named root is not there, stays not there, and the mirror resumes once it is. */
export async function aMissingNamedDataRootStaysAbsent(): Promise<void> {
  const home = process.env['COAI_DATA_DIR'] ?? '';
  assert.ok(home.length > 0, 'the launcher named no data directory for this launch');
  assert.equal(fs.existsSync(home), false, `the folder ${home} exists before activation, so this launch proves nothing about a missing root`);

  await activated();
  // The notice settles here: its toast is shown and its record HANDLED — the write that created the folder on E1.
  await settle(SETTLE_MS);

  assert.equal(fs.existsSync(home), false, `the missing named data directory ${home} was created during activation — the write that hides the condition`);
  // And a setting change, which is the ordinary trigger of the mirror, must not create it either.
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  try {
    await config().update('dealPlanLenses', true, vscode.ConfigurationTarget.Global);
    await settle(SETTLE_MS);
    assert.equal(fs.existsSync(home), false, 'a settings change wrote the server\'s settings into a folder that is not there');

    // The folder appears: the watch re-asks on the next settings change, the mirror resumes, the file lands.
    fs.mkdirSync(home, { recursive: true });
    await config().update('dealPlanLenses', false, vscode.ConfigurationTarget.Global);
    await until(() => fs.existsSync(path.join(home, 'settings.json')),
      'the folder was created and a setting changed, and the mirror never wrote the settings file — it was not the guard that held it');
  } finally {
    await config().update('dealPlanLenses', undefined, vscode.ConfigurationTarget.Global);
  }
}
