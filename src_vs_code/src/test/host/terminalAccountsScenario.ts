import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import type { CoaiExports } from '../../extension';
import type { AccountsMode } from '../../remoteAccountsScript';
import type { RemoteRun } from '../../remoteSsh';
import { until } from './hostHelpers';

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

async function doorsOf(): Promise<{ readonly accounts: NonNullable<CoaiExports['terminalAccounts']>; readonly sessions: NonNullable<CoaiExports['remoteSessions']> }> {
  const extension = vscode.extensions.getExtension<CoaiExports>('remsoftdev.connect-other-ais');
  assert.ok(extension !== undefined, 'the extension under test is not installed in this host');
  const exported = await extension.activate();
  assert.ok(exported.terminalAccounts !== undefined, 'the extension did not hand out its accounts test door in test mode, so nothing below is about the bound round');
  assert.ok(exported.remoteSessions !== undefined, 'the extension did not hand out its road test door in test mode');

  return { accounts: exported.terminalAccounts, sessions: exported.remoteSessions };
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
    accounts.replaceCall(undefined);
    await sessions.restore(before);
  }
}
