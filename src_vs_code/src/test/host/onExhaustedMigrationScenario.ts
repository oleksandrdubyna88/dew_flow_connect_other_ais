import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { recordOf, USER_WITNESS, USER_WITNESS_WORD } from '../../onExhaustedMigration';
import { until, activated } from './hostHelpers';

/**
 * The one-time reset of *When the rounds run out* to Auto completes in a REAL editor
 * (research/PLAN_auto_never_asks_a_person_E2_S2_spec.md T31, D16).
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated by the editor, and VS Code's own settings registry and
 * settings file: the witness setting is known to the registry although the manifest declares it with no default (D13's signal,
 * in a real registry); the run completes — the record this extension keeps in its own `globalState`, read through what
 * activation returns, names the extension's version as `completed` and says `told` — and a real `settings.json` accepted
 * the witness: its `globalValue` is the one fixed word, while `coai.onExhausted` holds no value of the person's own.</p>
 *
 * <p><b>What it does NOT drive.</b> An update in place before activation, two windows, two machines, and a value saved before
 * activation (the editor under test starts with an empty profile): `onExhaustedMigration*.test.ts` drive those over fakes
 * that read VS Code's source (R10), and no test here can observe them for real. CI runs this job with `continue-on-error`; the
 * pull request quotes its line, and without it the host wiring is reported as unverified.</p>
 */
export async function theOnExhaustedMigrationCompletesInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension<{ readonly onExhaustedMigration?: () => unknown }>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const exports = await activated(extension);
  const record = exports?.onExhaustedMigration;
  assert.ok(typeof record === 'function', 'activation returned no onExhaustedMigration, so this host cannot read the record');

  const config = vscode.workspace.getConfiguration('coai');
  assert.notEqual(config.inspect(USER_WITNESS)?.defaultValue, undefined, 'the real registry does not know the witness setting');
  const version = (extension.packageJSON as { readonly version?: string }).version;
  await until(
    () => recordOf(record()).completed === version && recordOf(record()).told === true,
    `the migration did not complete and tell: the record reads ${JSON.stringify(record())}`,
    30_000,
  );

  const after = vscode.workspace.getConfiguration('coai');
  assert.equal(after.inspect('onExhausted')?.globalValue, undefined, 'the user layer still holds a value of When the rounds run out');
  assert.equal(after.inspect(USER_WITNESS)?.globalValue, USER_WITNESS_WORD, 'a real settings file did not take the witness');
}
