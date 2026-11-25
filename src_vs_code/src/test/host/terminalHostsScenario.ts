import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { RemoteRun } from '../../remoteSsh';
import { until, activated } from './hostHelpers';

/**
 * The per-host consent and the picked id of the BOUND road, in a real editor (S3-H1 of the E2 stories document, the chat reads the
 * remote Claude session).
 *
 * <p>What a value cannot answer: that `bindRemoteSessions` really binds the road of this window to the profile's own `globalState`
 * (a `Memento` that is not a map), that a press on a terminal named like a CredsForDevs one — really open in this window — stops at a
 * remembered "no" with its own sentence and spawns nothing, goes on to the ssh seam after a "yes", keeps the id of a title pick, and
 * loses that id when the terminal is really closed (`onDidCloseTerminal`). The unit tests (`remoteRead.test.ts`) fake the store.
 * The run seam here replaces the bound road's ssh call through the extension's TEST-ONLY door (present only in
 * `ExtensionMode.Test`): it answers with frames and counts; no ssh is started. The modal and the pick list are not driven (a test
 * cannot answer them): the consent is set through the store, as the road writes it, and the pick is a title pick of ONE session,
 * which asks nobody. It runs only in CI's `extension · a real editor` job: a real VS Code does not start on a machine without `libnss3`.</p>
 */

const HOST = 'h';
const SESSION = 's';
const ID = '0b2e6a52-1f6c-4d0e-8a43-1c9d1d5e7f10';
const REFUSAL = 'the run seam of S3-H1 answers nothing';

const frame = (...lines: readonly string[]): RemoteRun =>
  ({ kind: 'answered', code: 0, stdout: ['COAI-BEGIN 1', ...lines, 'COAI-END', ''].join('\n') });

/** The fallback's answers: the pane's folder, then ONE session whose title is the tmux session's name, then a refusal for its tail. */
function titleRoad(operands: readonly string[]): RemoteRun {
  if (operands.length === 1) {
    return frame('COAI-PANE 0.0 %1 1000', 'COAI-CWD', ':/home/git/kb', 'COAI-FOLDERS', ':-home-git-kb', 'COAI-FOLDERS-END 0 0');
  }

  return operands[1] === 'titles'
    ? frame('COAI-FOLDER -home-git-kb ok', `COAI-SESSION ${ID} 100 9 rows`, `:{"type":"ai-title","aiTitle":"${SESSION}"}`, 'COAI-ROWS 0 0', 'COAI-SCAN 1 all')
    : { kind: 'refused', refusal: REFUSAL };
}

export async function theConsentOfAHostIsKeptInTheRealGlobalState(): Promise<void> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await activated(extension)).remoteSessions;
  assert.ok(door !== undefined, 'the extension did not hand out its test door in test mode, so nothing below is about the bound road');

  const calls: (readonly string[])[] = [];
  let answer: (operands: readonly string[]) => RemoteRun = () => ({ kind: 'refused', refusal: REFUSAL });
  door.replaceRun(async (_host, _session, operands) => {
    calls.push(operands);

    return answer(operands);
  });
  const before = await door.stored();
  const terminal = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  try {
    terminal.show();
    await door.commit({ kind: 'person', host: HOST, answer: 'no' });
    assert.equal((await door.standings()).find((row) => row.host === HOST)?.answer, 'no', 'a "no" committed through globalState was not read back from it');

    const off = await door.read(terminal, 'question');
    assert.equal(calls.length, 0, 'ssh was spawned for a host nobody agreed to');
    assert.equal(off.kind, 'refused', 'a press on a host answered "no" did not refuse');
    assert.ok(off.kind === 'refused' && off.refusal.includes('is turned off'), `the refusal is not the "turned off" sentence: ${JSON.stringify(off)}`);

    await door.commit({ kind: 'person', host: HOST, answer: 'yes' });
    const fresh = await door.standings();
    assert.ok(fresh.some((row) => row.host === HOST && row.answer === 'yes'), `the "yes" is not in the list read back from globalState: ${JSON.stringify(fresh)}`);

    const on = await door.read(terminal, 'question');
    assert.equal(calls.length, 1, `a press on a host answered "yes" did not reach the run seam exactly once (${calls.length} calls)`);
    assert.deepEqual(calls[0], [SESSION], 'the first call of a press is not the registry path');
    assert.deepEqual(on, { kind: 'refused', refusal: REFUSAL }, 'the run seam\'s refusal did not come back as the answer');

    // A title pick of one session: three calls, the last refused — the id stays with the terminal, and goes when the terminal is closed.
    answer = titleRoad;
    calls.length = 0;
    await door.read(terminal, 'question');
    assert.equal(calls.length, 3, `the fallback made ${calls.length} calls, not three`);
    assert.equal(door.pickedOf(terminal)?.id, ID, 'the id of the title pick was not kept for the terminal after a refused tail');

    terminal.dispose();
    await until(() => door.pickedOf(terminal) === undefined, 'the picked id of a closed terminal is still kept');
  } finally {
    door.replaceRun(undefined);
    await door.restore(before);
    terminal.dispose();
  }
}

const GLOBAL = vscode.ConfigurationTarget.Global;

async function doorOf(): Promise<NonNullable<CoaiExports['remoteSessions']>> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await activated(extension)).remoteSessions;
  assert.ok(door !== undefined, 'the extension did not hand out its test door in test mode');

  return door;
}

/**
 * A1-H1 of the accounts plan (E1.S1): the store in a real editor. The automatic "yes", a person's "no" with its copy in the list and
 * its entry in the refusals file on the real disk, the Stop, and a profile that is put back as it was found — the list and the file's
 * text. It cannot run on a machine without `libnss3`; CI runs it.
 */
export async function theHostsStoreWritesTheListAndTheRefusalsFile(): Promise<void> {
  const door = await doorOf();
  const before = await door.stored();
  try {
    const first = await door.admit('h1');
    assert.deepEqual(first, { kind: 'read', how: 'auto', allowedNow: true }, `a host with no answer was not allowed by itself: ${JSON.stringify(first)}`);
    assert.equal(((await door.stored()).list as { host: string; how?: string }[]).find((row) => row.host === 'h1')?.how, 'auto', 'the automatic row is not in globalState');
    await door.commit({ kind: 'person', host: 'h1', answer: 'no' });
    assert.equal((await door.admit('h1')).kind, 'refused', 'a "no" did not refuse the host');
    const stored = await door.stored();
    assert.equal((stored.list as { host: string; how?: string }[]).find((row) => row.host === 'h1')?.how, undefined, 'the person\'s row carries `how`');
    assert.ok(stored.file !== undefined && (JSON.parse(stored.file) as { refused: { host: string }[] }).refused.some((one) => one.host === 'h1'), 'the refusals file on the real disk does not hold the host');
    await door.stopRound();
    assert.equal(await door.roundStop(), 'stopped');
    await door.startRound();
    assert.equal(await door.roundStop(), 'running');
  } finally {
    await door.restore(before);
    assert.deepEqual(await door.stored(), before, 'the profile was not left as it was found');
  }
}

/**
 * A1-H2: the setting through the real configuration. If the editor refuses to write the user-level key, this FAILS and says so: the
 * claim that only the user layer decides must not rest on a skipped check.
 */
export async function theSettingReachesTheDoorThroughTheRealConfiguration(): Promise<void> {
  const door = await doorOf();
  const before = await door.stored();
  const config = (): vscode.WorkspaceConfiguration => vscode.workspace.getConfiguration('coai');
  try {
    try {
      await config().update('terminalAccountsAllowNewHosts', false, GLOBAL).then(undefined, (reason: unknown) => {
        throw new Error(`the editor refused to write the user-level key, so A1-H2 cannot judge the setting: ${String(reason)}`);
      });
      console.info(`A1-H2: inspect() answers ${JSON.stringify(config().inspect('terminalAccountsAllowNewHosts'))}`);
      assert.equal((await door.admit('h2')).kind, 'not-asked', 'the setting off did not stop an automatic allow');
      assert.equal(((await door.stored()).list as { host: string }[] | undefined)?.some((row) => row.host === 'h2') ?? false, false, 'a row was stored with the setting off');
    } finally {
      await config().update('terminalAccountsAllowNewHosts', undefined, GLOBAL);
    }
    assert.equal((await door.admit('h2')).kind, 'read', 'the setting back to its default did not allow the host');
  } finally {
    await door.restore(before);
  }
}
