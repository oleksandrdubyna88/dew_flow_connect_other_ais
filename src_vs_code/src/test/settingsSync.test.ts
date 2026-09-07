import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';
import { ExistingFile, ServerSettingsSync } from '../serverSettingsSync';
import { CoaiSettings, DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, Vendor } from '../vendors';

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

/**
 * A configuration the compiler checks, rather than one cast past it.
 *
 * <p>These fixtures were written with `as never` on both halves, which turns off exactly the check
 * that would catch a settings shape drifting out from under them. Two reviewers named it on the same
 * round, and the rule they cited says what to do instead: a typed factory with real defaults and
 * typed overrides. It was copied from the fixture next to it, which is the specific thing
 * reuse-first forbids — do not imitate a pattern you can see is wrong because everything here does
 * it that way.</p>
 */
function configuration(over: Partial<CoaiSettings> = {}): { settings: CoaiSettings; vendors: readonly Vendor[] } {
  return { settings: { ...DEFAULTS, ...over }, vendors: DEFAULT_VENDORS };
}

function sync(state: { settings?: Partial<CoaiSettings>; vendors?: readonly Vendor[] } = {}) {
  const written: string[] = [];
  const value = { settings: { ...DEFAULTS, ...state.settings }, vendors: state.vendors ?? DEFAULT_VENDORS };
  const it = new ServerSettingsSync(
    () => ({ settings: value.settings, vendors: value.vendors }),
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

/**
 * The file every window shares records which build wrote it, and an older build stands down.
 *
 * <p><b>The report, 2026-09-07.</b> A Team-server reviewer was added in one VS Code window and
 * vanished from every round. VS Code's own `coai.vendors` held it correctly — `runtime: "remote"`,
 * `remoteVendor: "claude"` — and `<dataDir>/settings.json` held it as `runtime: "codex"`, written
 * 0.4 seconds later. Two extension hosts, one file. One of the windows had been open since before
 * the update and was running 0.31.0, whose `RUNTIMES` has no `remote`; `vendorsFrom` rewrites an
 * unknown runtime to `codex` so the row still launches something, and `ServerSettingsSync` then
 * wrote that coerced value out. A rendering fallback became a persisted fact.</p>
 *
 * <p>The guard cannot be retroactive — 0.31.0 has already shipped and has no guard in it — so it
 * stops the NEXT pair, and today's machine is unstuck by reloading the stale window. That limit is
 * stated rather than implied.</p>
 */
/** A file with these contents, or — for the empty string — no file at all. */
function asFile(existing: string): ExistingFile {
  return existing.length === 0 ? { kind: 'absent' } : { kind: 'contents', text: existing };
}

function stamped(existing: string, version = '0.31.3') {
  const written: string[] = [];
  const refusals: string[] = [];
  const it = new ServerSettingsSync(
    () => configuration({ onExhausted: 'good_enough' }),
    async (json: string) => {
      written.push(json);
    },
    version,
    async () => asFile(existing),
    (theirs: string) => refusals.push(theirs),
  );
  return { it, written, refusals };
}

test('the file says which build wrote it', async () => {
  const { it, written } = stamped('');

  await it.sync();

  assert.equal(
    (JSON.parse(written[0]!) as Record<string, string>)['COAI_WRITTEN_BY'],
    '0.31.3',
    'without a stamp no build can tell whether it is about to overwrite a newer one',
  );
});

test('an older build does not overwrite what a newer one wrote', async () => {
  const { it, written, refusals } = stamped('{"COAI_WRITTEN_BY":"0.31.9"}', '0.31.3');

  await it.sync();

  assert.equal(written.length, 0, 'this is the write that reverted a reviewer nobody could see');
  assert.deepEqual(refusals, ['0.31.9'], 'a silent stand-down is the same defect from the other side');
});

test('a refusal is not remembered as a write, so the next change still tries', async () => {
  const { it, written } = stamped('{"COAI_WRITTEN_BY":"0.31.9"}', '0.31.3');

  await it.sync();
  await it.sync();

  assert.equal(written.length, 0);
  // And once the newer window is gone, the same content must still be written.
  const again = stamped('', '0.31.3');
  await again.it.sync();
  assert.equal(again.written.length, 1, 'the refusal must not have poisoned the remembered content');
});

test('an absent, older or unreadable stamp is overwritten, which is every file written before this', async () => {
  for (const existing of ['', '{}', '{"COAI_WRITTEN_BY":"0.31.0"}', 'not json at all', '{"COAI_WRITTEN_BY":""}']) {
    const { it, written } = stamped(existing, '0.31.3');
    await it.sync();
    assert.equal(written.length, 1, `a file holding ${JSON.stringify(existing)} must not block the write`);
  }
});

test('the same version does not block itself — two windows on one build are one build', async () => {
  const { it, written } = stamped('{"COAI_WRITTEN_BY":"0.31.3"}', '0.31.3');

  await it.sync();

  assert.equal(written.length, 1);
});

test('ten is after nine, and a build suffix is not a version', async () => {
  // `0.31.10` sorts before `0.31.9` as text and after it as a version, which is the one comparison
  // a hand-rolled string check always gets wrong. Reused from `updateAvailable`, which the CLI
  // update buttons have compared this way since they existed.
  assert.equal((await syncedAgainst('0.31.10', '0.31.9')).length, 1, '0.31.9 must not block 0.31.10');
  assert.equal((await syncedAgainst('0.31.9', '0.31.10')).length, 0, '0.31.10 must block 0.31.9');
  // Build metadata is not part of the comparison, and a pre-release compares as its release: this
  // guard is about a SHIPPED build overwriting a newer SHIPPED build, and two locally built
  // 0.31.3s are the same version to it. Stated because "semver" alone does not say which.
  assert.equal((await syncedAgainst('0.31.3', '0.31.3+build.7')).length, 1);
  assert.equal((await syncedAgainst('0.31.3', '0.31.3-alpha.1')).length, 1);
});

test('a SECOND, different newer build is reported too — the suppression is per version, not per session', async () => {
  // A bare "already said something" flag makes the first stand-down the only one a window ever
  // mentions: a person who updates the other window to 0.31.11 and hits it again is told nothing.
  // Accepted finding, this story's plan round.
  const written: string[] = [];
  const refusals: string[] = [];
  let existing = '{"COAI_WRITTEN_BY":"0.31.9"}';
  const it = new ServerSettingsSync(
    () => configuration({ onExhausted: 'good_enough' }),
    async (json: string) => {
      written.push(json);
    },
    '0.31.3',
    async () => asFile(existing),
    (theirs: string) => refusals.push(theirs),
  );

  await it.sync();
  await it.sync();
  existing = '{"COAI_WRITTEN_BY":"0.31.11"}';
  await it.sync();

  assert.equal(written.length, 0);
  assert.deepEqual(refusals, ['0.31.9', '0.31.11'], 'the same stamp once, a different stamp again');
});

test('a stand-down that later succeeds can be reported again if it happens again', async () => {
  // The suppression is about not repeating one sentence, not about saying it once per lifetime.
  // Once a write goes through the situation is over, and the next stand-down is news.
  //
  // The settings have to MOVE between the two, and that is not a detail of the fixture: with
  // unchanged content there is nothing to write, so `sync` returns before it looks at the file and
  // there is nothing to stand down from. The first draft of this test missed that and expected a
  // second refusal from an unchanged configuration — the code was right and the test was not.
  const written: string[] = [];
  const refusals: string[] = [];
  let existing = '{"COAI_WRITTEN_BY":"0.31.9"}';
  let settings: CoaiSettings = { ...DEFAULTS, onExhausted: 'good_enough' };
  const it = new ServerSettingsSync(
    () => ({ settings, vendors: DEFAULT_VENDORS }),
    async (json: string) => {
      written.push(json);
    },
    '0.31.3',
    async () => asFile(existing),
    (theirs: string) => refusals.push(theirs),
  );

  await it.sync();
  existing = '';
  await it.sync();

  settings = { ...DEFAULTS, onExhausted: 'escalate' };
  existing = '{"COAI_WRITTEN_BY":"0.31.9"}';
  await it.sync();

  assert.equal(written.length, 1, 'the newer window went away and the pending content landed');
  assert.deepEqual(refusals, ['0.31.9', '0.31.9'], 'the second wall is a second sentence');
});

async function syncedAgainst(mine: string, theirs: string): Promise<string[]> {
  const { it, written } = stamped(`{"COAI_WRITTEN_BY":"${theirs}"}`, mine);
  await it.sync();
  return written;
}

/** The sync with a reader that answers however the test wants, including badly. */
function reading(answer: () => Promise<ExistingFile>, version = '0.31.3') {
  const written: string[] = [];
  const refusals: string[] = [];
  const it = new ServerSettingsSync(
    () => configuration({ onExhausted: 'good_enough' }),
    async (json: string) => {
      written.push(json);
    },
    version,
    answer,
    (theirs: string) => refusals.push(theirs),
  );
  return { it, written, refusals };
}

test('a file that cannot be READ is not a file that is not there', async () => {
  // The defect three reviewers found independently: an earlier draft answered '' for both, so a
  // locked file — or a volume that blinked — read as "nothing there", which is permission to
  // overwrite. That is the exact revert this story exists to prevent, through a different door.
  const { it, written, refusals } = reading(async () => ({ kind: 'unreadable' }));

  await it.sync();

  assert.equal(written.length, 0, 'a guard that cannot answer must stand the write down');
  assert.deepEqual(refusals, [], 'and say nothing about a version it never read');
});

test('a reader that throws synchronously stands the write down too', async () => {
  // `.catch()` on the promise does not cover this, and the guard is awaited outside the try that
  // protects the write — so the exception would abort `sync` before it ever reached the write.
  const { it, written } = reading(() => {
    throw new Error('the provider is gone');
  });

  await it.sync();

  assert.equal(written.length, 0);
});

test('after a stand-down, going back to what was last written still writes it', async () => {
  // The "nothing changed" short-circuit compares against the last SUCCESSFUL write, and after a
  // stand-down the file holds something else entirely. Change a setting, stand down, change it
  // back, and the content matches — so the write was skipped and the value never landed.
  const written: string[] = [];
  let existing: ExistingFile = { kind: 'absent' };
  let settings: CoaiSettings = { ...DEFAULTS, onExhausted: 'good_enough' };
  const it = new ServerSettingsSync(
    () => ({ settings, vendors: DEFAULT_VENDORS }),
    async (json: string) => {
      written.push(json);
    },
    '0.31.3',
    async () => existing,
  );

  await it.sync();
  assert.equal(written.length, 1);

  settings = { ...DEFAULTS, onExhausted: 'escalate' };
  existing = { kind: 'contents', text: '{"COAI_WRITTEN_BY":"0.31.9"}' };
  await it.sync();
  assert.equal(written.length, 1, 'stood down, as it should');

  settings = { ...DEFAULTS, onExhausted: 'good_enough' };
  existing = { kind: 'absent' };
  await it.sync();

  assert.equal(written.length, 2, 'the same content as before the stand-down still has to be written');
});

test('two spellings of one version are one sentence', async () => {
  // If the comparison treats `0.31.9-alpha.1` and `0.31.9` as the same version, so must the
  // suppression, or a person is told the same thing twice.
  const written: string[] = [];
  const refusals: string[] = [];
  let existing = '{"COAI_WRITTEN_BY":"0.31.9-alpha.1"}';
  let settings: CoaiSettings = { ...DEFAULTS, onExhausted: 'good_enough' };
  const it = new ServerSettingsSync(
    () => ({ settings, vendors: DEFAULT_VENDORS }),
    async (json: string) => {
      written.push(json);
    },
    '0.31.3',
    async () => asFile(existing),
    (theirs: string) => refusals.push(theirs),
  );

  await it.sync();
  settings = { ...DEFAULTS, onExhausted: 'escalate' };
  existing = '{"COAI_WRITTEN_BY":"0.31.9"}';
  await it.sync();

  assert.deepEqual(refusals, ['0.31.9-alpha.1'], 'the second is the same version, differently spelled');
});

test('a stamp from a corrupted file cannot produce a dialog nobody can read', async () => {
  // It has to still READ as newer, or nothing is reported and the test proves nothing about the
  // message. The noise goes after the number, where a corrupted file would leave it.
  const noisy = `9.9.9${String.fromCharCode(7)}${'x'.repeat(200)}`;
  const { it, refusals } = reading(async () => ({
    kind: 'contents',
    text: JSON.stringify({ COAI_WRITTEN_BY: noisy }),
  }));

  await it.sync();

  assert.equal(refusals.length, 1);
  assert.ok(!refusals[0]!.includes(String.fromCharCode(7)), 'control characters are stripped');
  assert.ok(refusals[0]!.length <= 64, 'and the length is capped');
});
