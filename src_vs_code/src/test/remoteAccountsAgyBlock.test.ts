import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import type { AccountsRead, AgyUsage } from '../remoteAccountsReply';
import { firstOperandOf } from '../remoteAccountsRound';
import type { Memory } from '../remoteAccountsRound';
import { ACCOUNTS_SKIP_AGY } from '../remoteAccountsScript';
import type { WslPlace } from '../remotePlaces';
import { accountsArgv } from '../remoteSsh';
import { wslAccountsArgv } from '../remoteWsl';
import { claudeFound, memoryOf, remembered, roundOf, world } from './roundFakes';

/**
 * Antigravity's quota command (revision 3 of the accounts script): an unknown slash command is sent to the model, so a reply that says the quota command
 * asked the model (or printed no bucket it knows) makes the round stop asking that host at that agy version — the first call then carries the operand
 * `no-agy-quota` — exactly as the Claude usage block does. A new version asks again, and so does a new memory.
 */

type Reading = Extract<AccountsRead, { kind: 'read' }>;

const withAgy = (usage: AgyUsage, version: string | null = '1.5.18'): Reading => ({ ...claudeFound('x@example.test'), agy: { state: 'found', version, signInFile: true, email: '', usage } });
const MODEL_CALL: AgyUsage = { state: 'failed', why: 'model-call' };
const FIGURES: AgyUsage = { state: 'read', windows: [{ group: 'gemini', window: '5h', leftPercent: 90, resetsAtMs: null }] };

/** A host whose agy behaves as the script does: with the operand it prints `skipped`, without it the quota is what the test says; its version is `version(round)`. */
function host(quota: AgyUsage, version: (round: number) => string = () => '1.5.18') {
  let round = 0;
  const w = world({
    hosts: ['b'],
    reply: (_host, mode) => {
      if (mode === 'claude-usage') {
        return claudeFound('x@example.test');
      }
      const skip = w.calls.filter((call) => call.mode === 'no-claude-usage').at(-1)?.digest === ACCOUNTS_SKIP_AGY;

      return withAgy(skip ? { state: 'skipped' } : quota, version(round));
    },
  });

  return { w, next: (memory: Memory) => { round += 1; return roundOf(w, memory, round); }, first: () => roundOf(w, new Map(), 0) };
}

const firstOperands = (w: ReturnType<typeof world>): readonly (string | undefined)[] => w.calls.filter((call) => call.mode === 'no-claude-usage').map((call) => call.digest);

test('R3-T7: a quota that asked the model — the next round\'s first call carries no-agy-quota while the agy version is the same, and the block stays', async () => {
  const run = host(MODEL_CALL);
  let memory = (await run.first()).memory;
  assert.equal(memory.get('b')?.agyBlockedAt, '1.5.18', 'the reply that says model-call did not block the host');
  for (let round = 1; round <= 3; round += 1) {
    memory = new Map((await run.next(memory)).memory);
    assert.equal(memory.get('b')?.agyBlockedAt, '1.5.18', `round ${round}: the block went while the version is the same`);
  }
  assert.deepEqual(firstOperands(run.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY]);
});

test('R3-T7: a new agy version asks again — the reply of the skipped call names the new version, the block goes, and the next first call has no operand', async () => {
  const run = host(MODEL_CALL, (round) => (round < 2 ? '1.5.18' : '1.6.0'));
  let memory = (await run.first()).memory;
  memory = new Map((await run.next(memory)).memory);
  assert.equal(memory.get('b')?.agyBlockedAt, '1.5.18');
  memory = new Map((await run.next(memory)).memory);
  assert.equal(memory.get('b')?.agyBlockedAt, undefined, 'a host that reports another version stayed blocked');
  memory = new Map((await run.next(memory)).memory);
  assert.deepEqual(firstOperands(run.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY, undefined], 'the first call after the new version was not a question to agy');
  assert.equal(memory.get('b')?.agyBlockedAt, '1.6.0', 'the new version said model-call again and the host was not blocked at it');
});

test('R3-T7: a reply with no bucket it knows (format) blocks too; a timeout, an error and figures do not; a new memory (a reload) asks again', async () => {
  for (const [usage, blocks] of [
    [{ state: 'failed', why: 'format' }, true], [{ state: 'failed', why: 'timeout' }, false], [{ state: 'failed', why: 'error' }, false], [{ state: 'failed', why: 'no-jq' }, false], [FIGURES, false],
  ] as const) {
    const run = host(usage);
    const memory = (await run.first()).memory;
    await run.next(memory);
    assert.deepEqual(firstOperands(run.w), blocks ? [undefined, ACCOUNTS_SKIP_AGY] : [undefined, undefined], JSON.stringify(usage));
  }
  const reload = host(MODEL_CALL);
  await reload.first();
  await reload.first();
  assert.deepEqual(firstOperands(reload.w), [undefined, undefined], 'a reload (a new Memory) did not ask again');
});

test('R3-T7: the operand is the first call\'s only: the usage call carries the digest, never no-agy-quota; and firstOperandOf needs the same version in the last reading', async () => {
  const run = host(MODEL_CALL);
  await run.next((await run.first()).memory);
  const usage = run.w.calls.filter((call) => call.mode === 'claude-usage');
  assert.ok(usage.length >= 1 && usage.every((call) => /^[0-9a-f]{64}$/u.test(call.digest ?? '')), JSON.stringify(usage));
  const reading = withAgy(MODEL_CALL, '1.5.18');
  assert.equal(firstOperandOf({ reading: { read: reading, atMs: 0 }, agyBlockedAt: '1.5.18' }), ACCOUNTS_SKIP_AGY);
  assert.equal(firstOperandOf({ reading: { read: reading, atMs: 0 }, agyBlockedAt: '1.4.0' }), undefined, 'a block of another version');
  assert.equal(firstOperandOf({ reading: { read: reading, atMs: 0 } }), undefined, 'no block');
  assert.equal(firstOperandOf({ agyBlockedAt: '1.5.18' }), undefined, 'a block with no reading: the version of the next reply is unknown');
  assert.equal(firstOperandOf(undefined), undefined);
  assert.equal(firstOperandOf({ reading: { read: withAgy(MODEL_CALL, null), atMs: 0 }, agyBlockedAt: '' }), ACCOUNTS_SKIP_AGY, 'a version that was not known is the empty text');
  assert.equal(firstOperandOf(remembered('b', 'x@example.test')), undefined);
});

test('R3-T7: a place whose reading names no agy has no block, and a block is not kept across a reading without agy', async () => {
  const none = world({ hosts: ['b'], reply: () => claudeFound('x@example.test') });
  const memory: Memory = memoryOf(['b', { ...remembered('b', 'x@example.test'), agyBlockedAt: '1.5.18' }]);
  const result = await roundOf(none, memory);
  assert.equal(result.memory.get('b')?.agyBlockedAt, undefined);
  assert.deepEqual(firstOperands(none), [undefined]);
});

test('R3-T7: the operand reaches the argv of ssh (single-quoted) and of wsl.exe, and nothing else is accepted there', () => {
  assert.deepEqual(accountsArgv('ai1-dev2', 'no-claude-usage').slice(-4), ['sh', '-s', '--', '\'no-claude-usage\'']);
  assert.deepEqual(accountsArgv('ai1-dev2', 'no-claude-usage', ACCOUNTS_SKIP_AGY).slice(-5), ['sh', '-s', '--', '\'no-claude-usage\'', '\'no-agy-quota\'']);
  const place: WslPlace = { kind: 'wsl', distro: 'Ubuntu', user: 'alice', uid: 1000 };
  assert.deepEqual([...wslAccountsArgv(place, 'no-claude-usage', ACCOUNTS_SKIP_AGY)], ['-d', 'Ubuntu', '-u', 'alice', '--cd', '/', '--exec', '/bin/sh', '-s', '--', 'no-claude-usage', 'no-agy-quota']);
  for (const operand of ['no-agy-quota ', 'NO-AGY-QUOTA', '', 'no-agy-quota;id', 'someone@example.test']) {
    assert.throws(() => accountsArgv('ai1-dev2', 'no-claude-usage', operand), /^Error: The remote read was asked in a form/u, JSON.stringify(operand));
    assert.throws(() => wslAccountsArgv(place, 'no-claude-usage', operand), /^Error: The WSL read was asked in a form/u, JSON.stringify(operand));
  }
  assert.throws(() => accountsArgv('ai1-dev2', 'claude-usage', ACCOUNTS_SKIP_AGY), /^Error/u, 'the usage mode takes a digest only');
  assert.throws(() => wslAccountsArgv(place, 'claude-usage', ACCOUNTS_SKIP_AGY), /^Error/u);
});
