import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { RemoteRun } from '../../remoteSsh';
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
  await sessions.restore(before);
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
    await vscode.commands.executeCommand('coai.terminalAccounts');
    await vscode.commands.executeCommand('workbench.action.closeQuickOpen');
    assert.deepEqual(calls, [], 'a host was called with no permission');
    assert.deepEqual([...accounts.memoryHosts()], [], 'the round remembered a host with no permission');
    assert.equal(await accounts.armed(), false, 'with an eligible terminal open the timer would be armed with no permission');

    assert.equal((await sessions.allow()).kind, 'given', 'the permission was not given through the keeper');
    await until(() => calls.length >= 2, 'the Allow did not start a round');
    await until(() => accounts.memoryHosts().length === 2, 'the round did not remember its two hosts');

    assert.deepEqual([...calls].sort(), [`${HOST_A} no-claude-usage`, `${HOST_B} no-claude-usage`], 'the first round did not call each host once, and none else (wsl is never called)');
  } finally {
    for (const one of terminals) {
      one.dispose();
    }
    await putBack(accounts, sessions, before);
  }
}
