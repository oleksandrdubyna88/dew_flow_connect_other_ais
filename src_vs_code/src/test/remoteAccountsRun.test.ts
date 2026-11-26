import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import type { AccountsCommand } from '../remoteAccountsMessages';
import { runAccountsCommand } from '../remoteAccountsRun';
import type { RunSeams } from '../remoteAccountsRun';

/**
 * E2S3-T10: `allow` and `read-again` change nothing by themselves. The two questions are fakes that answer Escape: after them no grant is written
 * and no refusal is lifted. `refuse`, `stop` and `withdraw` act at once. Each command reaches exactly one seam.
 */

interface Rig { readonly seams: RunSeams; readonly calls: string[]; readonly written: string[] }

/** `escape` is what both questions answer: nothing chosen. A grant or a lifted refusal can only be written by a question that is answered. */
function rig(): Rig {
  const calls: string[] = [];
  const written: string[] = [];
  const mark = (name: string) => async (...args: unknown[]): Promise<undefined> => { calls.push([name, ...args.map(String)].join(' ')); return undefined; };
  const question = (name: string) => async (...args: unknown[]): Promise<undefined> => { calls.push([name, ...args.map(String)].join(' ')); return undefined; };
  const seams: RunSeams = {
    refresh: mark('refresh'),
    stop: async () => { calls.push('stop'); written.push('stop'); },
    start: mark('start'),
    askPermission: question('askPermission'),
    withdraw: async () => { calls.push('withdraw'); written.push('withdraw'); },
    saveWaiting: mark('saveWaiting'),
    refuse: async (host) => { calls.push(`refuse ${host}`); written.push(`refuse ${host}`); },
    askChange: question('askChange'),
    copy: mark('copy'),
  };

  return { seams, calls, written };
}

test('E2S3-T10: allow and read-again raise their question and write nothing when it is answered with Escape', async () => {
  const { seams, calls, written } = rig();
  await runAccountsCommand({ kind: 'allow' }, seams);
  await runAccountsCommand({ kind: 'read-again', host: 'h1' }, seams);
  assert.deepEqual(calls, ['askPermission', 'askChange h1']);
  assert.deepEqual(written, [], 'a grant was written or a refusal lifted without an answer');
});

test('E2S3-T10: refuse, stop and withdraw act at once, each through its own seam only', async () => {
  const { seams, calls, written } = rig();
  await runAccountsCommand({ kind: 'refuse', host: 'h1' }, seams);
  await runAccountsCommand({ kind: 'stop' }, seams);
  await runAccountsCommand({ kind: 'withdraw' }, seams);
  assert.deepEqual(calls, ['refuse h1', 'stop', 'withdraw']);
  assert.deepEqual(written, ['refuse h1', 'stop', 'withdraw']);
});

test('E2S3-T10: every other command reaches exactly its seam, and ready reaches none', async () => {
  const table: readonly (readonly [AccountsCommand, string])[] = [
    [{ kind: 'refresh' }, 'refresh'],
    [{ kind: 'start' }, 'start'],
    [{ kind: 'save-now' }, 'saveWaiting'],
    [{ kind: 'copy-install', decision: { kind: 'copy', text: 't', done: 'd', failed: 'f' } }, 'copy [object Object]'],
  ];
  for (const [command, expected] of table) {
    const { seams, calls } = rig();
    await runAccountsCommand(command, seams);
    assert.deepEqual(calls, [expected], command.kind);
  }
  const { seams, calls } = rig();
  await runAccountsCommand({ kind: 'ready' }, seams);
  assert.deepEqual(calls, []);
});
