import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { AccountsMode } from '../../remoteAccountsScript';
import type { RemoteRun } from '../../remoteSsh';
import { until, activated } from './hostHelpers';

/**
 * A3-H1 of the accounts plan (E1.S3): the round in a real editor. With the call replaced through the test door by a fake that answers a
 * fixed reply and logs its start and end, three terminals — two of one host, one of another — are opened and ONE round is run: the fake is
 * called once for each host, each time with `no-claude-usage`, one after the other. E1 ships `USAGE_POLICY` `off`, so the real build makes no
 * second call; with the usage on this count becomes three (the PR of E2 that turns it on changes the count in the commit that changes the
 * constant). The store holds an automatic "yes" for each host. Then every terminal is closed: nothing is armed and a round calls nothing.
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

export async function theAccountsRoundCallsEachHostOnceAndStopsWithItsTerminals(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  const events: string[] = [];
  const modes: AccountsMode[] = [];
  accounts.replaceCall(async (host, mode): Promise<RemoteRun> => {
    events.push(`start ${host}`);
    modes.push(mode);
    await new Promise((wake) => setTimeout(wake, 20));
    events.push(`end ${host}`);

    return { kind: 'answered', code: 0, stdout: '' };
  });
  const names = [`Creds: ${HOST_A} / s1`, `Creds: ${HOST_A} / s2`, `Creds: ${HOST_B} / s1`];
  const terminals = names.map((name) => vscode.window.createTerminal({ name }));
  try {
    await until(() => names.every((name) => vscode.window.terminals.some((one) => one.name === name)), 'the three terminals did not open');

    const result = await accounts.runRoundNow('timer');

    assert.ok(result !== undefined, 'the round did not run');
    assert.deepEqual(events, [`start ${HOST_A}`, `end ${HOST_A}`, `start ${HOST_B}`, `end ${HOST_B}`], 'the fake was not called once for each host, one after the other');
    assert.deepEqual(modes, ['no-claude-usage', 'no-claude-usage'], 'a call of the build that ships the usage read off had another mode');
    const standings = await sessions.standings();
    for (const host of [HOST_A, HOST_B]) {
      assert.ok(standings.some((one) => one.host === host && one.answer === 'yes' && one.how === 'auto'), `the store holds no automatic "yes" for ${host}: ${JSON.stringify(standings)}`);
    }
    assert.deepEqual([...accounts.memoryHosts()], [HOST_A, HOST_B]);
    assert.equal(await accounts.armed(), true, 'with an eligible terminal open the planner did not answer arm-first, so the "false" below proves nothing');

    for (const one of terminals) {
      one.dispose();
    }
    await until(() => !vscode.window.terminals.some((one) => names.includes(one.name)), 'the terminals did not close');
    assert.equal(await accounts.armed(), false, 'a timer would be armed with no Creds terminal');
    const calls = events.length;
    await accounts.runRoundNow('timer');
    assert.equal(events.length, calls, 'a round called a host with no terminal');
  } finally {
    for (const one of terminals) {
      one.dispose();
    }
    await putBack(accounts, sessions, before);
  }
}

/**
 * A4-H1 of the accounts plan (E1.S4): the list in a real editor. The command `coai.terminalAccounts` is among `vscode.commands.getCommands`; with the
 * fake call answering a reply whose organization holds a "$(", one round is run, the command is executed, and `lastItems()` holds the group's separator with
 * that organization written "$ (", a `── Hosts ──` separator and one row for each host. The QuickPick is hidden in the `finally`.
 *
 * <p><b>Why the "$(" is in the organization and not in the email.</b> The parser takes every value through `plainValue` (which writes "$(" as "$ (") and
 * only then judges an email, which may hold no space (S2 spec, the value table): an email with a "$(" is therefore "not known" (`''`) and its group is
 * named by its host, so it could never carry the text this scenario looks for. The organization is text, and it reaches the separator.</p>
 *
 * <p><b>What this cannot see, and what it checked (U10; the plan's K3).</b> What VS Code DRAWS for a "$(" in a label: no extension API reads a drawn
 * row — checked in the locked type definitions, where `QuickPick` and `QuickPickItem` carry no such read and nothing else is offered. What it
 * checked instead is the text handed to the list: the reply's organization as the parser kept it (RAW, already plain: the parser calls `plainValue`) and the
 * label of the separator as the list drew it. U10 stays open.</p>
 */
const EMAIL = 'someone@example.test';
const RAW = 'Example$(zap) Org';

const REPLY = [
  'COAI-ACCOUNTS 1', 'COAI-JQ found', 'COAI-VENDOR claude found', `:email ${EMAIL}`, `:org ${RAW}`, ':version 2.1.326', 'COAI-USAGE claude skipped',
  'COAI-VENDOR codex absent', 'COAI-VENDOR agy absent', 'COAI-ACCOUNTS-END', '',
].join('\n');

export async function theAccountsListShowsTheGroupsAndTheHosts(): Promise<void> {
  const { accounts, sessions } = await doorsOf();
  const before = await sessions.stored();
  assert.ok((await vscode.commands.getCommands(true)).includes('coai.terminalAccounts'), 'coai.terminalAccounts is not a registered command');
  accounts.replaceCall(() => Promise.resolve<RemoteRun>({ kind: 'answered', code: 0, stdout: REPLY }));
  const name = `Creds: ${HOST_A} / s1`;
  const terminal = vscode.window.createTerminal({ name });
  try {
    await until(() => vscode.window.terminals.some((one) => one.name === name), 'the terminal did not open');
    const round = await accounts.runRoundNow('timer');
    assert.ok(round !== undefined, 'the round did not run');
    assert.equal(round.abandoned, false, 'the round was abandoned, so no reading was stored: a Forget or a refusal moved its generation while it ran');

    await vscode.commands.executeCommand('coai.terminalAccounts');
    await until(() => accounts.lastItems().some((one) => one.kind === 'separator' && one.label === '── Hosts ──'), 'the list was never given its items');

    const items = accounts.lastItems();
    const group = items.find((one) => one.kind === 'separator' && one.label.startsWith('Claude Code'));
    assert.ok(group !== undefined, `no Claude Code separator among ${items.map((one) => one.label).join(' | ')}`);
    assert.ok(group.label.includes(EMAIL) && group.label.includes('Example$ (zap) Org') && !group.label.includes('$(zap)'), `the label handed to the list is ${group.label}, raw organization ${RAW}`);
    const hostsAt = items.findIndex((one) => one.kind === 'separator' && one.label === '── Hosts ──');
    assert.deepEqual(items.slice(hostsAt + 1).map((one) => one.label), [HOST_A], 'the Hosts part is not one row for each host');
  } finally {
    await vscode.commands.executeCommand('workbench.action.closeQuickOpen');
    terminal.dispose();
    await putBack(accounts, sessions, before);
  }
}
