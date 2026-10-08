import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { phrasesFrom } from '../../phrases';
import { rowsAfter, rowsOf } from '../../phrasesEdit';

/**
 * The default phrases in a REAL editor (todo/PLAN_default_phrases.md; its plan round, finding 1).
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated, and `coai.phrases` in the real user settings, read
 * back through VS Code's configuration layers the way the panel (`panelProvider.ts`) and the phrases tab
 * (`phrasesPanel.ts`) read it: with no value saved the reader sees the seven defaults; every default removed by the tab's
 * own `rowsAfter` and the EMPTY list written as the tab writes it (`config.update(…, Global)`) reads back empty — the
 * defaults do not return; and a person's own list reads back as theirs.</p>
 *
 * <p><b>What it does NOT drive.</b> The press on the phrases tab (a host cannot reach a webview's DOM) and `saveSetting`
 * itself (it needs the bundle's `ExtensionContext`); `settingRefusedWiring.test.ts` pins that it is the one
 * `config.update(key, value, Global)` used here.</p>
 */
export async function theDefaultPhrasesInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  await extension.activate();
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  const before = config().inspect('phrases')?.globalValue;
  try {
    await config().update('phrases', undefined, vscode.ConfigurationTarget.Global);
    assert.equal(phrasesFrom(config().get('phrases')).length, 7, 'with nothing saved, the seven defaults are not what the panel reads');

    let rows: readonly Record<string, unknown>[] = rowsOf(config().get('phrases'));
    for (const row of [...rows]) {
      const after = rowsAfter(rows, { kind: 'remove', id: String(row['id']) });
      rows = after.kind === 'rows' ? after.rows : rows;
    }
    await config().update('phrases', rows, vscode.ConfigurationTarget.Global);
    assert.deepEqual(config().inspect('phrases')?.globalValue, [], 'the emptied list was not stored as an empty list');
    assert.equal(phrasesFrom(config().get('phrases')).length, 0, 'the defaults came back after every phrase was removed');

    const own = [{ id: 'phrase-own', name: 'Mine', text: 'my own sentence' }];
    await config().update('phrases', own, vscode.ConfigurationTarget.Global);
    assert.deepEqual(phrasesFrom(config().get('phrases')).map((phrase) => phrase.text), ['my own sentence'], 'a person\'s own list was not left alone');
  } finally {
    await config().update('phrases', before, vscode.ConfigurationTarget.Global);
  }
}
