import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { collectWithPick } from '../../bugzCollect';
import { bugzInputsOf } from '../../bugzPick';
import { settingsFrom } from '../../settingsShape';
import { vendorsFrom } from '../../vendors';

/**
 * The Bugz collect's refusal in a REAL editor (research/PLAN_one_model_catalog.md E5.1; its code round, finding 6).
 *
 * <p><b>What it drives for real.</b> The shipped extension, activated, and `coai.bugzModel` written to the real user
 * settings — a stranded pick, then none — read back through VS Code's configuration layers by the readers the provider
 * uses (`settingsFrom`, `vendorsFrom`), handed to `bugzInputsOf`, the one reader the sidebar draws from, and decided by
 * `collectWithPick`: it refuses each by its sentence and starts nothing.</p>
 *
 * <p><b>What it does NOT drive.</b> The provider's own `collectBugs`: it needs the bundled extension's `ExtensionContext`,
 * which only the bundle holds, so its ports — the `no-ranking-model` notice and the spawn through `serverRun` — are
 * pinned by `bugzCollect.test.ts` reading its source, and here the two effects are recorded instead. Nor the press in the
 * sidebar (a host cannot reach a webview's DOM), nor a collect that does start (it spawns coai-mcp against the window's
 * database).</p>
 */
export async function aBugzCollectIsRefusedInARealEditor(): Promise<void> {
  const extension = vscode.extensions.getExtension('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  await extension.activate();
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  const before = config().inspect('bugzModel')?.globalValue;
  try {
    for (const [saved, says] of [['gone/qwen3.5', /gone\/qwen3\.5[\s\S]*Models/u], ['', /No model is ticked for Bugz/u]] as const) {
      await config().update('bugzModel', saved, vscode.ConfigurationTarget.Global);
      const refused: string[] = [];
      const started: (readonly string[])[] = [];
      const read = (key: string): unknown => config().get(key);

      await collectWithPick({
        inputs: () => Promise.resolve(bugzInputsOf({
          vendors: vendorsFrom(read('vendors')), catalogRows: vendorsFrom(read('vendors')), settings: settingsFrom(read), localEngines: {},
        })),
        refuse: (sentence) => { refused.push(sentence); return Promise.resolve(); },
        start: (args) => { started.push(args); return Promise.resolve(); },
      });

      assert.deepEqual(started, [], `a collect started with the pick "${saved}" read from the real settings`);
      assert.match(refused[0] ?? '', says, `the pick "${saved}" was not refused by its sentence`);
    }
  } finally {
    await config().update('bugzModel', before, vscode.ConfigurationTarget.Global);
  }
}
