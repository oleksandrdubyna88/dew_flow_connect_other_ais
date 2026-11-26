import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { byCodeUnit } from '../../codeUnitOrder';
import type { CoaiExports } from '../../extension';
import type { RemoteRun } from '../../remoteSsh';
import type { WslPort } from '../../remoteWslRound';
import type { WslRun } from '../../remoteWslRun';
import { GOOD } from '../accountsFrames';
import { until, activated } from './hostHelpers';

/**
 * E2S4-H1 of the accounts plan (E2.S4): the round under the permission, in a real editor, through the real keeper. With no permission four terminals
 * — two of one host, one of another and one of `wsl` — and a timer tick and two presses call NOTHING, remember nothing, arm no timer, and the command of
 * the list calls nothing. After an *Allow* through the keeper a round starts at once, with the call replaced: one call for each of the two hosts and none
 * for `wsl`. The profile is put back as it was found.
 *
 * <p>The scenario never waits for a timer: in test mode there is none, and `armed()` says what the planner answered. It cannot run on a machine without
 * `libnss3`; CI's real-editor job runs it and nothing here fakes its result.</p>
 */

const HOST_A = 'hA';
const HOST_B = 'hB';

interface Doors { readonly accounts: NonNullable<CoaiExports['terminalAccounts']>; readonly sessions: NonNullable<CoaiExports['remoteSessions']> }

async function doorsOf(): Promise<Doors> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const exported = await activated(extension);
  assert.ok(exported.terminalAccounts !== undefined, 'the extension did not hand out its accounts test door in test mode, so nothing below is about the bound round');
  assert.ok(exported.remoteSessions !== undefined, 'the extension did not hand out its road test door in test mode');

  return { accounts: exported.terminalAccounts, sessions: exported.remoteSessions };
}

/**
 * The call and the profile put back. The road's `restore` sends the change signal a commit sends, so the window's known hosts and its generation follow
 * the restored store. Without it the next scenario's round writes its own automatic "yes", the window sees a known host gone, reads it as a Forget and
 * bumps the generation: the round's result is dropped, its memory stays empty, and a list has no group to draw.
 */
async function putBack(accounts: Doors['accounts'], sessions: Doors['sessions'], before: Awaited<ReturnType<Doors['sessions']['stored']>>): Promise<void> {
  accounts.replaceCall(undefined);
  accounts.replaceWsl(undefined);
  await sessions.restore(before);
  await seesTheStore(accounts);
}

/**
 * The window's round reads the permission when something asks it to, and it starts a round at once only on an EDGE: not-given, then given. A raw
 * `restore` sends the change signal, but the window reads the store only after its own awaits; a scenario that gives the permission at once after a restore
 * can find the grant before the window saw the withdrawal, and then there is no edge and no round. A round asked for here with no permission answers nothing and
 * makes the window read the store as it is.
 */
async function seesTheStore(accounts: Doors['accounts']): Promise<void> {
  await accounts.runRoundNow('refresh');
}

export async function theAccountsRoundRunsOnlyUnderThePermission(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  const calls: string[] = [];
  accounts.replaceCall(async (host, mode): Promise<RemoteRun> => {
    calls.push(`${host} ${mode}`);

    return { kind: 'answered', code: 0, stdout: '' };
  });
  const names = [`Creds: ${HOST_A} / s1`, `Creds: ${HOST_A} / s2`, `Creds: ${HOST_B} / s1`, 'Creds: wsl / s1'];
  const terminals = names.map((name) => vscode.window.createTerminal({ name }));
  try {
    await until(() => names.every((name) => vscode.window.terminals.some((one) => one.name === name)), 'the four terminals did not open');
    if (typeof (await sessions.permission()) === 'object') {
      await sessions.withdraw();
    }

    for (const trigger of ['timer', 'refresh', 'open'] as const) {
      assert.equal(await accounts.runRoundNow(trigger), undefined, `a ${trigger} round ran with no permission`);
    }
    assert.ok((await vscode.commands.getCommands(true)).includes('coai.terminalAccounts'), 'coai.terminalAccounts is not a registered command');
    assert.deepEqual(calls, [], 'a host was called with no permission');
    assert.deepEqual([...accounts.memoryHosts()], [], 'the round remembered a host with no permission');
    assert.equal(await accounts.armed(), false, 'with an eligible terminal open the timer would be armed with no permission');

    assert.equal((await sessions.allow()).kind, 'given', 'the permission was not given through the keeper');
    await until(() => calls.length >= 2, 'the Allow did not start a round');
    await until(() => accounts.memoryHosts().length === 2, 'the round did not remember its two hosts');

    assert.deepEqual([...calls].sort(byCodeUnit), [`${HOST_A} no-claude-usage`, `${HOST_B} no-claude-usage`], 'the first round did not call each host once, and none else (wsl is never called)');
  } finally {
    for (const one of terminals) {
      one.dispose();
    }
    await putBack(accounts, sessions, before);
  }
}

/**
 * E2S3-H1 of the accounts plan (E2.S3): the icon opens ONE tab and a second press shows the same one; with the call replaced through the test door
 * and the permission given through the keeper, two terminals of two hosts are one call each, and the model the tab holds has both blocks. The permission
 * is given BEFORE the press so the icon asks no question here (a modal has nobody to answer it); the first-press question is `askPermissionFirst`'s own test.
 * The profile is put back as it was found. CI's real-editor job runs it.
 */
export async function theIconOpensOneTabAndItsModelHoldsBothHosts(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  const calls: string[] = [];
  accounts.replaceCall(async (host, mode): Promise<RemoteRun> => {
    calls.push(`${host} ${mode}`);

    return { kind: 'answered', code: 0, stdout: '' };
  });
  const names = [`Creds: ${HOST_A} / s1`, `Creds: ${HOST_B} / s1`];
  const terminals = names.map((name) => vscode.window.createTerminal({ name }));
  try {
    await seesTheStore(accounts);
    await until(() => names.every((name) => vscode.window.terminals.some((one) => one.name === name)), 'the two terminals did not open');
    assert.equal(accounts.tabOpen(), false, 'a tab was open before the icon was pressed');
    assert.equal((await sessions.allow()).kind, 'given', 'the permission was not given through the keeper');
    await until(() => calls.length >= 2, 'the Allow did not start a round');
    await until(() => accounts.memoryHosts().length === 2, 'the round did not remember its two hosts');

    await vscode.commands.executeCommand('coai.terminalAccounts');
    await until(() => accounts.tabOpen(), 'the icon did not open the tab');
    await vscode.commands.executeCommand('coai.terminalAccounts');
    await until(() => (accounts.lastModel()?.blocks.length ?? 0) === 2, 'the tab does not hold both hosts');

    assert.deepEqual(accounts.lastModel()?.blocks.map((block) => block.host), [HOST_A, HOST_B], 'the blocks of the model are not the two hosts, in code-unit order');
    assert.deepEqual([...calls].sort(byCodeUnit), [`${HOST_A} no-claude-usage`, `${HOST_B} no-claude-usage`], 'each host was not called exactly once');
    assert.equal(vscode.window.tabGroups.all.flatMap((group) => group.tabs).filter((tab) => tab.label === 'AI accounts and usage').length, 1, 'a second press opened a second tab');
  } finally {
    for (const one of terminals) {
      one.dispose();
    }
    await putBack(accounts, sessions, before);
  }
}

/** What a fake `wsl.exe` prints for a list: UTF-16LE, as the launcher decodes it. */
const listText = (...names: readonly string[]): WslRun => ({ kind: 'answered', code: 0, stdout: Buffer.from(`${names.join('\r\n')}\r\n`, 'utf16le').toString('utf8') });

/** A PC of two distros, one of them stopped: the running one has root and two users; every account call answers a good reply. */
const FAKE_WSL: WslPort = {
  list: (running) => Promise.resolve(running ? listText('Ubuntu') : listText('Ubuntu', 'Debian')),
  users: () => Promise.resolve({ kind: 'answered', code: 0, stdout: 'COAI-USERS 1\nCOAI-USER root 0\nCOAI-USER alice 1000\nCOAI-USER bob 1001\nCOAI-USERS-END\n' }),
  accounts: () => Promise.resolve({ kind: 'answered', code: 0, stdout: GOOD }),
};

/**
 * E3S3-H1 of the accounts plan (E3.S3): one `Creds: wsl / s1` terminal, the WSL port replaced through the test door (so no `wsl.exe` starts), two distros of
 * which one is stopped, root and two users: the tab's model holds the `wsl` block with the line of the stopped distro and the three places, each by its key.
 * The permission is given through the keeper first. The profile is put back as it was found. CI's real-editor job runs it.
 */
export async function theWslPlacesAreInTheTabsModel(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  accounts.replaceCall(async (): Promise<RemoteRun> => ({ kind: 'answered', code: 0, stdout: '' }));
  accounts.replaceWsl(FAKE_WSL);
  const name = 'Creds: wsl / s1';
  const terminal = vscode.window.createTerminal({ name });
  try {
    await seesTheStore(accounts);
    await until(() => vscode.window.terminals.some((one) => one.name === name), 'the terminal did not open');
    assert.equal((await sessions.allow()).kind, 'given', 'the permission was not given through the keeper');
    await until(() => accounts.memoryHosts().filter((key) => key.startsWith('wsl:')).length === 3, 'the round did not remember the three places');
    await vscode.commands.executeCommand('coai.terminalAccounts');
    await until(() => (accounts.lastModel()?.blocks.length ?? 0) === 4, 'the tab does not hold the wsl block and the three places');

    const model = accounts.lastModel();
    assert.deepEqual(model?.blocks.map((block) => block.host), ['wsl', 'wsl:Ubuntu/alice', 'wsl:Ubuntu/bob', 'wsl:Ubuntu/root']);
    assert.deepEqual(model?.blocks.map((block) => block.shown), ['wsl', 'Ubuntu · alice', 'Ubuntu · bob', 'Ubuntu · root']);
    assert.deepEqual(model?.blocks[0]?.wsl?.distros.map((one) => `${one.name}: ${one.said}`), ['Ubuntu: 3 users', 'Debian: not running, so not read']);
  } finally {
    terminal.dispose();
    await putBack(accounts, sessions, before);
  }
}
