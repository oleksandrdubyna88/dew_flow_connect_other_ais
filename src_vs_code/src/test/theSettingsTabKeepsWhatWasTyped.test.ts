import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sourceOf } from './sourceReading';

/**
 * What the three editing tabs did on their own, and the Settings tab must do now that their editors are its places
 * (research/PLAN_one_model_catalog.md E5.1 step 4; E5.1c's code round, findings 2 and 3).
 *
 * <p>The tabs' hosts ran in a VS Code panel no unit test can build, so the WIRING is held here, over the provider's
 * source — the editing cores' own queues and the prune are tested where they live (`settledWrites.test.ts`,
 * `chatPresets.test.ts`).</p>
 */

/** The body of `attachSettings`, the one place the Settings tab is wired. */
function attachSettings(): string {
  const source = sourceOf('panelProvider.ts');
  const start = source.indexOf('attachSettings(panel: vscode.WebviewPanel): void {');
  assert.ok(start >= 0, 'the provider no longer wires the Settings tab in attachSettings');

  return source.slice(start, source.indexOf('\n  }\n', start));
}

test('closing the Settings tab writes what was still settling in every editor on it', () => {
  // A title, a prompt or a preset typed and the tab closed within the settle window: the roles, commands and presets
  // tabs each flushed their queue on dispose, so a closed tab never dropped the last keystrokes.
  const disposed = /onDidDispose\(\(\) => \{([\s\S]*?)\}\);/u.exec(attachSettings())?.[1] ?? '';

  for (const flush of ['flushRoleEdits()', 'flushCommandEdits()', 'flushChatPresetEdits()']) {
    assert.ok(disposed.includes(flush), `closing the Settings tab does not call ${flush}, so what was settling is lost`);
  }
});

test('opening the Settings tab clears the dead model presets an older build wrote, as the presets tab did', () => {
  assert.ok(attachSettings().includes('pruneDeadModelRows()'), 'nothing clears the litter rows once the presets tab is gone');
});
