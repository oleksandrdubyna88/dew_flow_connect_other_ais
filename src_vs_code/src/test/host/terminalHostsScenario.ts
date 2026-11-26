import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { RemoteRun } from '../../remoteSsh';
import { until, activated } from './hostHelpers';

/**
 * The permission, the per-host "no" and the picked id of the BOUND road, in a real editor (S3-H1 of the E2 stories document, the chat reads the
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

    await door.commit({ kind: 'forget', host: HOST });
    assert.ok(!(await door.standings()).some((row) => row.host === HOST && row.answer === 'no'), 'Read this host again did not lift the "no" in the list read back from globalState');
    const none = await door.read(terminal, 'question');
    assert.equal(calls.length, 0, 'ssh was spawned with no permission');
    assert.ok(none.kind === 'refused', 'a press with no permission did not refuse');
    assert.equal((await door.allow()).kind, 'given', 'the permission was not given through the keeper');

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

async function doorOf(): Promise<NonNullable<CoaiExports['remoteSessions']>> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const door = (await activated(extension)).remoteSessions;
  assert.ok(door !== undefined, 'the extension did not hand out its test door in test mode');

  return door;
}

/**
 * A1-H1 of the accounts plan (E1.S1, as the cut leaves it): the store in a real editor. A host with no answer stays `not-asked` and no
 * automatic row is written; a person's "no" with its copy in the list and its entry in the refusals file on the real disk, the Stop, and a
 * profile that is put back as it was found — the list and the file's text. It cannot run on a machine without `libnss3`; CI runs it.
 */
export async function theHostsStoreWritesTheListAndTheRefusalsFile(): Promise<void> {
  const door = await doorOf();
  const before = await door.stored();
  try {
    const first = await door.may('round', 'h1');
    assert.equal(first, 'no-permission', `a host with no permission was not left unread: ${JSON.stringify(first)}`);
    assert.equal((((await door.stored()).list ?? []) as { host: string; how?: string }[]).some((row) => row.host === 'h1'), false, 'the door wrote a row for a host nobody answered');
    await door.commit({ kind: 'person', host: 'h1', answer: 'no' });
    assert.equal(await door.may('round', 'h1'), 'no', 'a "no" did not refuse the host');
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
 * E2S1-H1: the permission through the real `globalState` and the real folder. No permission: the door answers `no-permission`, and a press with a run
 * that fails the test when called reads nothing. An *Allow* through the keeper: `given`, the door says `yes`. A withdrawal: one
 * `withdrawn-<n>.json` on the real disk, the row's copy is that `n`, and both doors say `no-permission`. An *Allow* again: `given`, and exactly one
 * withdrawal file is left, one below the grant's stamp. Everything is put back in a `finally`. CI only: it cannot start here.
 */
export async function thePermissionIsGivenAndWithdrawnThroughTheRealStores(): Promise<void> {
  const door = await doorOf();
  const before = await door.stored();
  const terminal = vscode.window.createTerminal({ name: `Creds: ${HOST} / ${SESSION}` });
  door.replaceRun(async () => { assert.fail('a host was read without the permission'); });
  try {
    terminal.show();
    await door.restore({ list: undefined, row: undefined, file: undefined, withdrawn: [] });
    assert.equal(await door.may('session', HOST), 'no-permission');
    const refused = await door.read(terminal, 'question');
    assert.ok(refused.kind === 'refused' && refused.refusal.includes('no permission'), `the press with no permission did not say W15's first sentence: ${JSON.stringify(refused)}`);
    door.replaceRun(undefined);
    assert.equal((await door.allow()).kind, 'given');
    const now = await door.permission();
    assert.ok(typeof now === 'object', `the permission is not given: ${JSON.stringify(now)}`);
    assert.equal(await door.may('session', HOST), 'yes');
    assert.ok(['yes', 'stopped'].includes(await door.may('round', HOST)));
    assert.equal(await door.withdraw(), 'withdrawn');
    const files = await door.withdrawnFiles();
    assert.equal(files.length, 1, `a withdrawal did not make exactly one file: ${JSON.stringify(files)}`);
    const stamp = Number(/^withdrawn-(\d+)\.json$/u.exec(files[0] ?? '')?.[1]);
    assert.equal(((await door.stored()).row as { withdrawnStamp?: number } | undefined)?.withdrawnStamp, stamp, 'the copy in the state row is not the stamp of the file');
    assert.equal(await door.may('session', HOST), 'no-permission');
    assert.equal(await door.may('round', HOST), 'no-permission');
    assert.equal((await door.allow()).kind, 'given');
    const left = await door.withdrawnFiles();
    assert.deepEqual(left, [`withdrawn-${stamp}.json`], 'the Allow did not leave exactly its floor file');
    assert.equal(await door.may('session', HOST), 'yes');
  } finally {
    door.replaceRun(undefined);
    await door.restore(before);
    terminal.dispose();
  }
}
