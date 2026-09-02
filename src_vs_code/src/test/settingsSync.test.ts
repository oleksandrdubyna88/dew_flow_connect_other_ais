import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { ServerSettingsSync } from '../serverSettingsSync';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * Settings reach the server whether or not anybody has opened the panel.
 *
 * <p><b>The report.</b> A colleague on macOS set `coai.onExhausted` to `good_enough` in VS Code
 * settings, restarted, and the server launched by Claude Code still answered `call_human` an hour
 * later — ten third rounds in a row. The mechanism that was supposed to carry it exists and has
 * since `mcp-v0.3.1`: the extension writes `<dataDir>/settings.json` and the server reads it under
 * the environment. It simply never ran.</p>
 *
 * <p><b>Why it never ran.</b> The write lived in `PanelProvider.render()`, behind
 * `if (this.view === undefined) return;`, and `this.view` is set in `resolveWebviewView` — which
 * VS Code calls LAZILY, only when the view is first made visible. The `onDidChangeConfiguration`
 * subscription was registered inside that same method. So in a window where nobody had opened the
 * ConnectOtherAIs panel, nothing watched the settings and nothing mirrored them; the server kept
 * running on whatever `env` block had been pasted into a client config months earlier.</p>
 *
 * <p>The fix is not a bigger guard. It is that mirroring settings to the server was never the
 * PANEL's job: the server needs them whether a person is looking at a webview or not, so the sync
 * belongs to the extension and takes no view at all. These tests are what makes that structural,
 * rather than a comment somebody later moves back.</p>
 */

function sync(state: { settings?: unknown; vendors?: unknown } = {}) {
  const written: string[] = [];
  const value = { settings: DEFAULTS, vendors: DEFAULT_VENDORS, ...state };
  const it = new ServerSettingsSync(
    () => ({ settings: value.settings as never, vendors: value.vendors as never }),
    async (json: string) => {
      written.push(json);
    },
  );
  return { it, written, value };
}

test('a settings change is mirrored with no view, no webview and no panel anywhere', async () => {
  const { it, written } = sync();

  await it.sync();

  assert.equal(written.length, 1, 'the server file must be written without a panel being open');
  assert.deepEqual(JSON.parse(written[0]), {}, 'a pristine configuration writes an empty object');
});

test('a changed setting reaches the file', async () => {
  const { it, written, value } = sync();
  await it.sync();

  value.settings = { ...DEFAULTS, onExhausted: 'good_enough' };
  await it.sync();

  assert.equal(written.length, 2);
  assert.equal(
    (JSON.parse(written[1]) as Record<string, string>)['COAI_ON_EXHAUSTED'],
    'good_enough',
    'the exact setting from the report has to land in the file',
  );
});

test('an unchanged configuration is not rewritten, because the server watches this file', async () => {
  // PanelServiceHost reloads when the file's mtime or length moves. The panel repaints on every
  // live poll, so writing identical content each time would ask the server to re-read its
  // settings several times a minute for nothing.
  const { it, written } = sync();

  await it.sync();
  await it.sync();
  await it.sync();

  assert.equal(written.length, 1, 'identical settings must not touch the file again');
});

test('a write that fails does not stop the next one from trying', async () => {
  // The disk being unwritable is not worth interrupting anyone over — but it must not leave the
  // sync believing it already wrote what it did not.
  let attempts = 0;
  const it = new ServerSettingsSync(
    () => ({ settings: DEFAULTS, vendors: DEFAULT_VENDORS }),
    async () => {
      attempts += 1;
      throw new Error('read-only volume');
    },
  );

  await it.sync();
  await it.sync();

  assert.equal(attempts, 2, 'a failed write must be retried, not remembered as done');
});

test('the settings mirror does not live inside the panel view', () => {
  // The regression guard for the defect itself. Both of these were in `resolveWebviewView`, which
  // is why a window with an unopened panel never mirrored anything, and a comment saying "do not
  // put it back" is not a thing that fails a build.
  const src = (name: string) => fs.readFileSync(path.join(__dirname, '..', '..', 'src', name), 'utf8');

  const provider = src('panelProvider.ts');
  assert.ok(
    !provider.includes('onDidChangeConfiguration'),
    'the configuration listener belongs to the extension, not to a webview that may never be resolved',
  );
  assert.ok(
    !provider.includes('serverSettingsJson'),
    'the panel must not be the thing that writes settings for the server',
  );

  const extension = src('extension.ts');
  assert.ok(
    extension.includes('onDidChangeConfiguration'),
    'something that exists from activation has to watch the configuration',
  );
  assert.ok(
    extension.includes('ServerSettingsSync'),
    'the settings mirror is wired at activation, where there is no view to depend on',
  );
});
