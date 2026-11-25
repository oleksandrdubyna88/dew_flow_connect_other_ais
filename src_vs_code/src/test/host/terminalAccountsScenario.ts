import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import { READING_OFF_SAID } from '../../remoteAccountsRound';
import type { RemoteRun } from '../../remoteSsh';
import { until, activated } from './hostHelpers';

/**
 * A3-H1 of the accounts plan (E1.S3, as the cut of the plan's stop leaves it): the round in a real editor, with the reading off. With the call
 * replaced through the test door by a fake that logs its start and end, three terminals — two of one host, one of another — are opened and a
 * round is run by a timer tick and by a press: the fake is called NEVER, the door writes no automatic "yes" row, the memory holds no host and the
 * planner's timer is not armed even with an eligible terminal open. (Before the cut the fake was called once for each host with
 * `no-claude-usage`, and the store held an automatic "yes" for each; the build that turns `ACCOUNTS_READING` on changes this back.)
 *
 * <p>The scenario never waits for a timer: in test mode there is none, and `armed()` says what the planner answered. The profile is put
 * back as it was found — the list and the refusals file — and the call is put back, which in test mode is the stub again. It cannot run on
 * a machine without `libnss3`; CI's real-editor job runs it and nothing here fakes its result.</p>
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

export async function theAccountsRoundReadsNoHostWhileTheReadingIsOff(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  const events: string[] = [];
  accounts.replaceCall(async (host, mode): Promise<RemoteRun> => {
    events.push(`${host} ${mode}`);

    return { kind: 'answered', code: 0, stdout: '' };
  });
  const names = [`Creds: ${HOST_A} / s1`, `Creds: ${HOST_A} / s2`, `Creds: ${HOST_B} / s1`];
  const terminals = names.map((name) => vscode.window.createTerminal({ name }));
  try {
    await until(() => names.every((name) => vscode.window.terminals.some((one) => one.name === name)), 'the three terminals did not open');

    for (const trigger of ['timer', 'refresh', 'open'] as const) {
      const result = await accounts.runRoundNow(trigger);

      assert.equal(result, undefined, `a ${trigger} round ran while the reading is off`);
    }

    assert.deepEqual(events, [], 'the call was made while the reading is off');
    const standings = await sessions.standings();
    assert.deepEqual(standings.filter((one) => one.how === 'auto'), [], `the round wrote an automatic "yes" row: ${JSON.stringify(standings)}`);
    assert.deepEqual([...accounts.memoryHosts()], [], 'the round remembered a host');
    assert.equal(await accounts.armed(), false, 'with an eligible terminal open the timer would be armed while the reading is off');
  } finally {
    for (const one of terminals) {
      one.dispose();
    }
    await putBack(accounts, sessions, before);
  }
}

/**
 * A4-H1 of the accounts plan (E1.S4, as the cut of the plan's stop leaves it): the list in a real editor, with the reading off. The command
 * `coai.terminalAccounts` is among `vscode.commands.getCommands`; with a terminal open and a fake call that would answer a reply, the command is
 * executed and `lastItems()` holds ONE item: the row that says the reading is off in this build. The fake is never called: opening the list starts
 * no round. The QuickPick is hidden in the `finally`. (Before the cut this scenario read a reply whose organization held a "$(" and looked for the
 * group's separator and one row for each host; the build that turns `ACCOUNTS_READING` on gets that scenario back, and its U10 note with it:
 * what VS Code DRAWS for a "$(" in a label cannot be read through any extension API.)
 */
export async function theAccountsListSaysTheReadingIsOff(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  assert.ok((await vscode.commands.getCommands(true)).includes('coai.terminalAccounts'), 'coai.terminalAccounts is not a registered command');
  const calls: string[] = [];
  accounts.replaceCall((host) => { calls.push(host); return Promise.resolve<RemoteRun>({ kind: 'answered', code: 0, stdout: '' }); });
  const name = `Creds: ${HOST_A} / s1`;
  const terminal = vscode.window.createTerminal({ name });
  try {
    await until(() => vscode.window.terminals.some((one) => one.name === name), 'the terminal did not open');

    await vscode.commands.executeCommand('coai.terminalAccounts');
    await until(() => accounts.lastItems().length > 0, 'the list was never given its items');

    assert.deepEqual(accounts.lastItems().map((one) => ({ kind: one.kind, label: one.label })), [{ kind: 'item', label: READING_OFF_SAID }], 'the list is not the one row that says the reading is off');
    assert.deepEqual(calls, [], 'opening the list called a host while the reading is off');
  } finally {
    await vscode.commands.executeCommand('workbench.action.closeQuickOpen');
    terminal.dispose();
    await putBack(accounts, sessions, before);
  }
}
