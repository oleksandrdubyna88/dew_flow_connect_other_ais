import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { USAGE_POLICY, runRound } from '../remoteAccountsRound';
import { accountsDigest } from '../remoteAccountsScript';
import { sourceOf } from './sourceReading';
import { accounts, callsOf, world } from './roundFakes';

const X = 'x@example.test';
const Y = 'y@example.test';
const Z = 'z@example.test';

/**
 * The usage on (E3.S5, spec § R2.9): `USAGE_POLICY` is `every-round`, set from W6 and W7 (both recorded). One constant, in its own commit after the story that
 * made the usage call safe (E3.S2: the digest, the join). The join itself is `remoteAccountsJoin.test.ts`.
 */

// ───────────────── E3.S5: the policy is on, and the round makes one usage call for each account ─────────────────

test('E3S5-T1: the policy is `every-round` — ONE constant, set because W6 and W7 were recorded (W7 passed: a usage read starts nothing but Claude Code\'s own; W6: five backups, the older ones deleted)', () => {
  assert.deepEqual(USAGE_POLICY, { kind: 'every-round' });
  const source = sourceOf('remoteAccountsRound.ts');
  assert.equal(source.split('export const USAGE_POLICY').length - 1, 1, 'the constant stands more than once');
  assert.ok(/W6/u.test(source) && /W7/u.test(source), 'the constant\'s comment does not name W6 and W7');
});

test('E3S5-T2: with the constant, a round over three places of two accounts makes exactly two usage calls, each with the digest of its place\'s first reply', async () => {
  const w = world({ hosts: ['a', 'b', 'c'], reply: accounts({ a: X, b: Y, c: X }) });

  await runRound(w.seams, new Map(), 0, 'timer', USAGE_POLICY);

  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'b U', 'c N']);
  const firsts = w.parsed.filter((one) => one.mode === 'no-claude-usage');
  assert.equal(firsts.length, 3);
  const digestOfFirst = (host: string): string | undefined => accountsDigest(firsts.find((one) => (JSON.parse(one.stdout.split('\n')[0] as string) as { host: string }).host === host)?.stdout ?? '');
  for (const host of ['a', 'b']) {
    const call = w.calls.find((one) => one.host === host && one.mode === 'claude-usage');
    assert.match(call?.digest ?? '', /^[0-9a-f]{64}$/u, `${host}: no digest`);
    assert.equal(call?.digest, digestOfFirst(host), `${host}: the digest is not the one of the place's first reply`);
  }
  assert.notEqual(digestOfFirst('a'), digestOfFirst('b'), 'two accounts have one digest');
});

test('E3S5-T3: no call is `claude-usage` as a place\'s first call, and no row of the model says CoAI does not ask Claude Code for usage in this build', async () => {
  const w = world({ hosts: ['a', 'b', 'c'], reply: accounts({ a: X, b: Y, c: Z }) });
  await runRound(w.seams, new Map(), 3, 'timer', USAGE_POLICY);
  const byHost = new Map<string, string[]>();
  for (const call of w.calls) {
    byHost.set(call.host, [...(byHost.get(call.host) ?? []), call.mode]);
  }
  for (const [host, modes] of byHost) {
    assert.equal(modes[0], 'no-claude-usage', `${host}: the first call is a usage call`);
  }
  for (const file of ['remoteAccountsRound.ts', 'remoteAccountsTable.ts', 'remoteAccountsRows.ts', 'remoteAccountsPage.ts', 'remoteAccountsGroups.ts', 'remoteAccountsHost.ts']) {
    assert.ok(!/does not ask Claude Code for usage in this build/iu.test(sourceOf(file)), `${file} still says that CoAI does not ask for usage`);
  }
});
