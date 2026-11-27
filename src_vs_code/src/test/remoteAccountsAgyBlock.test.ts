import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { AGY_BLOCKS_MAX, agyBlockAfter, agyBlockStore, agyBlocksInMemory, agyBlocksOf } from '../remoteAccountsAgyBlocks';
import type { AgyBlocks } from '../remoteAccountsAgyBlocks';
import type { AccountsRead, AgyUsage } from '../remoteAccountsReply';
import { firstOperandOf } from '../remoteAccountsRound';
import { ACCOUNTS_SKIP_AGY } from '../remoteAccountsScript';
import type { WslPlace } from '../remotePlaces';
import { accountsArgv } from '../remoteSsh';
import { wslAccountsArgv } from '../remoteWsl';
import { claudeFound, roundOf, world } from './roundFakes';

/**
 * Antigravity's quota command (revision 3 of the accounts script, and the review of PR #1066): an unknown slash command is sent to the model, and a real
 * model turn usually outlives the command's four seconds. So ANY outcome that is not a read — it asked the model, it printed no known bucket, it timed out,
 * it ended with an error — blocks the place at that agy version, and the place's first call then carries `no-agy-quota`. The block is kept apart from the round's
 * memory (a Stop, a closed terminal and a refusal clear that), in the store the window keeps in `globalState`, with no email in it.
 */

type Reading = Extract<AccountsRead, { kind: 'read' }>;

const withAgy = (usage: AgyUsage, version: string | null = '1.5.18'): Reading => ({ ...claudeFound('x@example.test'), agy: { state: 'found', version, signInFile: true, email: '', usage } });
const MODEL_CALL: AgyUsage = { state: 'failed', why: 'model-call' };
const FIGURES: AgyUsage = { state: 'read', windows: [{ group: 'gemini', window: '5h', leftPercent: 90, resetsAtMs: null }] };

/** A host whose agy behaves as the script does: with the operand it prints `skipped`, without it the quota is what the test says; its version is `version(round)`. */
function host(quota: AgyUsage, version: (round: number) => string | null = () => '1.5.18', options: { readonly agy?: AgyBlocks; readonly absentAt?: number } = {}) {
  let round = 0;
  const w = world({
    hosts: ['b'],
    ...(options.agy === undefined ? {} : { agy: options.agy }),
    reply: (_host, mode) => {
      if (mode === 'claude-usage') {
        return claudeFound('x@example.test');
      }
      if (options.absentAt === round) {
        return claudeFound('x@example.test');
      }
      const skip = w.calls.filter((call) => call.mode === 'no-claude-usage').at(-1)?.digest === ACCOUNTS_SKIP_AGY;

      return withAgy(skip ? { state: 'skipped' } : quota, version(round));
    },
  });

  return { w, next: () => { round += 1; return roundOf(w, new Map(), round); }, first: () => roundOf(w, new Map(), 0) };
}

const firstOperands = (w: ReturnType<typeof world>): readonly (string | undefined)[] => w.calls.filter((call) => call.mode === 'no-claude-usage').map((call) => call.digest);

test('R3-T7: a quota that asked the model — the next first call carries no-agy-quota while the agy version is the same, with the block kept apart from the memory', async () => {
  const run = host(MODEL_CALL);
  await run.first();
  assert.deepEqual(run.w.agy.get('b'), { version: '1.5.18', why: 'model-call' });
  for (let round = 1; round <= 3; round += 1) {
    await run.next();
    assert.deepEqual(run.w.agy.get('b'), { version: '1.5.18', why: 'model-call' }, `round ${round}: the block went while the version is the same`);
  }
  assert.deepEqual(firstOperands(run.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY]);
});

test('R3-T7: ANY quota outcome that is not a read blocks — model-call, format, timeout, error — with its reason; no-jq and figures do not, and figures end a block', async () => {
  for (const why of ['model-call', 'format', 'timeout', 'error'] as const) {
    const run = host({ state: 'failed', why });
    await run.first();
    await run.next();
    assert.deepEqual(firstOperands(run.w), [undefined, ACCOUNTS_SKIP_AGY], why);
    assert.deepEqual(run.w.agy.get('b'), { version: '1.5.18', why }, why);
    assert.equal(run.w.agy.get('b')?.why, why, 'the reason is kept for the row\'s sentence');
  }
  for (const usage of [{ state: 'failed', why: 'no-jq' }, FIGURES] as const) {
    const run = host(usage);
    await run.first();
    await run.next();
    assert.deepEqual(firstOperands(run.w), [undefined, undefined], JSON.stringify(usage));
    assert.equal(run.w.agy.get('b'), undefined);
  }
});

test('R3-T7: a new agy version asks again — the skipped reply names it, the block goes, and the next first call has no operand; a person\'s press and Refresh now end a block too', async () => {
  const run = host(MODEL_CALL, (round) => (round < 2 ? '1.5.18' : '1.6.0'));
  await run.first();
  await run.next();
  assert.deepEqual(run.w.agy.get('b'), { version: '1.5.18', why: 'model-call' });
  await run.next();
  assert.equal(run.w.agy.get('b'), undefined, 'a host that reports another version stayed blocked');
  await run.next();
  assert.deepEqual(firstOperands(run.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY, undefined]);
  assert.deepEqual(run.w.agy.get('b'), { version: '1.6.0', why: 'model-call' }, 'the new version said model-call again and the host is blocked at it');
  const store = agyBlocksInMemory();
  store.set('a', { version: '1.0', why: 'timeout' });
  store.set('b', { version: '1.0', why: 'model-call' });
  store.set('c', { version: '1.0', why: 'error' });
  store.set('d', { version: '1.0', why: 'format' });
  store.clearTransient();
  assert.deepEqual(['a', 'b', 'c', 'd'].map((place) => store.get(place)?.why), [undefined, 'model-call', undefined, 'format'], 'Refresh now asks again where a timeout or an error blocked, never after a model turn');
  store.clear('b');
  assert.equal(store.get('b'), undefined, 'a press on the host clears its block');
  assert.equal(store.get('d')?.why, 'format', 'and only its own');
});

test('R3-T7: the block outlives what clears the memory — a reply with no version, agy missing in one reply, a Stop, a closed terminal, a refusal, a call that did not answer', async () => {
  const noVersion = host(MODEL_CALL, (round) => (round === 0 ? '1.5.18' : null));
  await noVersion.first();
  await noVersion.next();
  await noVersion.next();
  assert.deepEqual(firstOperands(noVersion.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY], 'a reply that names no version');
  assert.deepEqual(noVersion.w.agy.get('b'), { version: '1.5.18', why: 'model-call' });
  const absent = host(MODEL_CALL, () => '1.5.18', { absentAt: 1 });
  await absent.first();
  await absent.next();
  await absent.next();
  assert.deepEqual(firstOperands(absent.w), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY], 'agy missing for one reply');
  // every round above started with an EMPTY memory (a Stop, a closed terminal): the block still stood. A refusal and a call that did not answer:
  const flags = { refuse: false, down: false };
  const w = world({ hosts: ['b'], door: () => (flags.refuse ? 'no' : 'yes'), reply: (_host, mode) => (flags.down ? 'down' : mode === 'claude-usage' ? claudeFound('x@example.test') : withAgy(MODEL_CALL)) });
  await roundOf(w, new Map(), 0);
  flags.refuse = true;
  await roundOf(w, new Map(), 1);
  flags.refuse = false;
  flags.down = true;
  await roundOf(w, new Map(), 2);
  flags.down = false;
  await roundOf(w, new Map(), 3);
  assert.deepEqual(w.agy.get('b'), { version: '1.5.18', why: 'model-call' });
  assert.deepEqual(w.calls.filter((call) => call.mode === 'no-claude-usage').map((call) => call.digest), [undefined, ACCOUNTS_SKIP_AGY, ACCOUNTS_SKIP_AGY], 'a refusal or a failed call made the round ask again');
});

test('R3-T7: the store — a reload (a new store over the stored value) keeps the block and asks no more, the stored value holds no email, and what is not a block is dropped', async () => {
  let stored: unknown;
  const store = agyBlockStore(() => stored, (value) => { stored = JSON.parse(JSON.stringify(value)); });
  const run = host(MODEL_CALL, () => '1.5.18', { agy: store });
  await run.first();
  assert.deepEqual(stored, { b: { version: '1.5.18', why: 'model-call' } });
  assert.ok(!JSON.stringify(stored).includes('@'), 'an email is in the stored value');
  const reloaded = host(MODEL_CALL, () => '1.5.18', { agy: agyBlockStore(() => stored, () => undefined) });
  await reloaded.first();
  assert.deepEqual(firstOperands(reloaded.w), [ACCOUNTS_SKIP_AGY], 'a reload asked the model again');
  const hostile = {
    ok: { version: '1.5.18', why: 'timeout' },
    badVersion: { version: '1.5; rm -rf /', why: 'timeout' },
    longVersion: { version: '1'.repeat(33), why: 'timeout' },
    badWhy: { version: '1.5', why: 'because' },
    extra: { version: '1.5', why: 'error', email: 'someone@example.test' },
    notAnObject: 'x',
    nothing: null,
    '': { version: '1', why: 'error' },
    [`p${'x'.repeat(250)}`]: { version: '1', why: 'error' },
  };
  assert.deepEqual([...agyBlocksOf(hostile)], [['ok', { version: '1.5.18', why: 'timeout' }], ['extra', { version: '1.5', why: 'error' }]], 'only whole entries; nothing but the version and the reason is carried');
  for (const raw of [undefined, null, 'x', 5, [], [1]]) {
    assert.equal(agyBlocksOf(raw).size, 0, JSON.stringify(raw));
  }
  const many = Object.fromEntries(Array.from({ length: 200 }, (_, n) => [`h${n}`, { version: '1', why: 'error' }]));
  assert.equal(agyBlocksOf(many).size, AGY_BLOCKS_MAX);
  const small = agyBlocksInMemory(many);
  small.set('another', { version: '1', why: 'error' });
  assert.equal(small.get('another'), undefined, 'a place past the cap is not remembered');
  small.set('h3', { version: '2', why: 'error' });
  assert.equal(small.get('h3')?.version, '2', 'a place that has a block can change it');
});

test('R3-T7: agyBlockAfter — what one reply does to a block, as a pure rule', () => {
  const agy = (usage: { state: string; why?: string }, version: string | null = '1.5') => ({ version, usage });
  const had = { version: '1.5', why: 'timeout' } as const;
  assert.equal(agyBlockAfter(undefined, undefined), 'keep');
  assert.deepEqual(agyBlockAfter(undefined, agy({ state: 'failed', why: 'error' })), { version: '1.5', why: 'error' });
  assert.deepEqual(agyBlockAfter(undefined, agy({ state: 'failed', why: 'model-call' }, null)), { version: '', why: 'model-call' });
  assert.equal(agyBlockAfter(had, agy({ state: 'read' })), undefined);
  assert.equal(agyBlockAfter(had, agy({ state: 'skipped' })), 'keep');
  assert.equal(agyBlockAfter(had, agy({ state: 'skipped' }, null)), 'keep');
  assert.equal(agyBlockAfter(had, agy({ state: 'skipped' }, '1.6')), undefined);
  assert.equal(agyBlockAfter(undefined, agy({ state: 'skipped' }, '1.6')), 'keep');
  assert.equal(agyBlockAfter(had, agy({ state: 'failed', why: 'no-jq' })), 'keep');
});

test('R3-T7: the operand is the first call\'s only: the usage call carries the digest, never no-agy-quota; and firstOperandOf follows the block alone', async () => {
  const run = host(MODEL_CALL);
  await run.first();
  await run.next();
  const usage = run.w.calls.filter((call) => call.mode === 'claude-usage');
  assert.ok(usage.length >= 1 && usage.every((call) => /^[0-9a-f]{64}$/u.test(call.digest ?? '')), JSON.stringify(usage));
  assert.equal(firstOperandOf({ version: '1.5.18', why: 'timeout' }), ACCOUNTS_SKIP_AGY);
  assert.equal(firstOperandOf(undefined), undefined);
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
