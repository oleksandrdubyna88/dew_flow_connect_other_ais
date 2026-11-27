import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { readAccounts } from '../remoteAccountsReply';
import type { AccountsRead, ClaudeUsage } from '../remoteAccountsReply';
import { claudeAccountKey, runRound } from '../remoteAccountsRound';
import type { AccountsCall, HostMemory } from '../remoteAccountsRound';
import { ACCOUNTS_LIMITS, RemoteSeams, runAccounts } from '../remoteSsh';
import { tableModel } from '../remoteAccountsTable';
import type { Block } from '../remoteAccountsTable';
import { NOW } from './accountsFrames';
import {
  EVERY_ROUND, USAGE_READ, accounts, callsOf, claudeFound, foundOf, memoryOf, remembered, roundOf, usageCalls, world,
} from './roundFakes';
import type { Options } from './roundFakes';

/**
 * The round's join of the usage reply (E3.S2, spec § R2.8: E3S2-T17 … T21), the digest of each place's first reply, and the policy that makes the
 * usage call part of every round (E3.S5, § R2.9). The fakes of `roundFakes.ts` answer a first call with a real frame of the account lines, so the
 * round takes its digest from it, and a usage call with the Claude part alone. The policy's own tests (E3.S5) are `remoteAccountsPolicy.test.ts`.
 */

const X = 'x@example.test';
const Y = 'y@example.test';
const Z = 'z@example.test';
/** A usage read with a figure, so a place that shares it shows a number. */
const FIGURES: ClaudeUsage = { state: 'read', windows: [{ name: '5h', usedPercent: 30, resetsAtMs: NOW + 3_600_000 }], unmatched: 0 };
const KEY = (email: string, host = 'h'): string => claudeAccountKey(foundOf(claudeFound(email)), host);
const CODEX: Extract<AccountsRead, { kind: 'read' }>['codex'] = { state: 'found', signInFile: true, plan: 'prolite', usage: { state: 'failed', why: 'none' } };
const AGY: Extract<AccountsRead, { kind: 'read' }>['agy'] = { state: 'found', version: '1.5.12', signInFile: true, usage: { state: 'no-source' } };
const withOthers = (email: string): AccountsRead => ({ ...claudeFound(email), codex: CODEX, agy: AGY });

/** Each host answers its first call with its account, and a usage call with what the test gives. */
const replyOf = (firsts: Readonly<Record<string, string>>, second: (host: string, nth: number) => AccountsRead | 'down'): NonNullable<Options['reply']> =>
  (host, mode, nth) => (mode === 'claude-usage' ? second(host, nth) : withOthers(firsts[host] as string));

const memoryHostOf = (memory: ReadonlyMap<string, HostMemory>, host: string): HostMemory => memory.get(host) as HostMemory;
const readingOf = (memory: ReadonlyMap<string, HostMemory>, host: string): Extract<AccountsRead, { kind: 'read' }> => {
  const held = memoryHostOf(memory, host).reading;
  assert.ok(held !== undefined, `${host} holds no reading`);

  return held.read;
};

test('E3S2-T17: the join, the same account — Codex and agy of the FIRST reply stay, the usage is the second\'s, the key is counted', async () => {
  const w = world({
    hosts: ['a', 'b'],
    reply: replyOf({ a: X, b: X }, () => claudeFound(X, { usage: USAGE_READ, version: '2.1.9' })),
  });

  const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N'], 'the key of a usage read was not counted, or b asked for the usage of the same account');
  const read = readingOf(result.memory, 'a');
  assert.deepEqual(read.codex, CODEX, 'the Codex part of the FIRST reply was replaced by the usage reply');
  assert.deepEqual(read.agy, AGY, 'the agy part of the FIRST reply was replaced by the usage reply');
  const claude = foundOf(read);
  assert.equal(claude.usage.state, 'read', 'the usage of the second reply is not in the joined reading');
  assert.equal(claude.version, '2.1.9', 'the Claude part is not the second reply\'s');
  assert.equal(memoryHostOf(result.memory, 'a').usageAtMs, w.control.now);
  assert.equal(memoryHostOf(result.memory, 'a').claudeAccount, KEY(X, 'a'));
  assert.equal(memoryHostOf(result.memory, 'a').usageMiss, undefined);
});

test('E3S2-T18: the join, a call that fails or cannot be read — the first reading whole, with its usageMiss, and the key is NOT counted', async () => {
  const cases: readonly (readonly [string, NonNullable<Options['reply']>, Partial<Options>, HostMemory['usageMiss']])[] = [
    ['the call failed', replyOf({ a: X, b: X }, (host) => (host === 'a' ? 'down' : claudeFound(X, { usage: USAGE_READ }))), {},
      { why: 'call-failed', sentence: '“a” did not answer over ssh within 30 s.' }],
    ['an unreadable reply', replyOf({ a: X, b: X }, (host) => (host === 'a' ? { kind: 'unreadable', why: 'tool' } : claudeFound(X, { usage: USAGE_READ }))), {},
      { why: 'unreadable', word: 'tool' }],
    ['the arm read', replyOf({ a: X, b: X }, (host) => (host === 'a' ? withOthers(X) : claudeFound(X, { usage: USAGE_READ }))), { usageArm: 'read' },
      { why: 'unreadable', word: 'shape' }],
  ];
  for (const [name, reply, over, miss] of cases) {
    const w = world({ hosts: ['a', 'b'], reply, ...over });
    const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

    assert.deepEqual(memoryHostOf(result.memory, 'a').usageMiss, miss, `${name}: the miss`);
    const read = readingOf(result.memory, 'a');
    assert.deepEqual(read, withOthers(X), `${name}: the first reading is not whole`);
    assert.equal(memoryHostOf(result.memory, 'a').failure, undefined, `${name}: a failed usage call made the place failed`);
    assert.equal(memoryHostOf(result.memory, 'a').usageAtMs, undefined, `${name}: a usage time was set`);
    // the key is not in the round's usage set, so the next place of the account asks for the usage
    assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'b U'], `${name}: the key was counted`);
  }
  // the next first reply of the place clears the miss
  const again = world({ hosts: ['a'], reply: replyOf({ a: X }, () => 'down') });
  const first = await roundOf(again, new Map(), 0, 'timer', EVERY_ROUND);
  assert.notEqual(memoryHostOf(first.memory, 'a').usageMiss, undefined);
  const quiet = world({ hosts: ['a'], reply: () => claudeFound(X) });
  const second = await roundOf(quiet, first.memory, 1, 'timer', { kind: 'off' });
  assert.equal(memoryHostOf(second.memory, 'a').usageMiss, undefined, 'the next first reply did not clear the miss');
});

test('E3S2-T18 (model): the cell says the reason — the three sentences, and the one for a reply with no account lines', async () => {
  const cell = (memory: ReadonlyMap<string, HostMemory>): Block => tableModel({
    hosts: ['a'], memory, standings: [], stopped: false, permission: { given: '2026-11-19T12:00:00Z' }, damaged: undefined, round: { kind: 'idle' }, windows: false,
  }).blocks[0] as Block;
  const spanOf = (block: Block): string | null => block.rows.find((row) => row.tool === 'claude-code')?.span ?? null;
  const said: readonly (readonly [HostMemory['usageMiss'], string])[] = [
    [{ why: 'call-failed', sentence: 'the host did not answer' }, 'Usage not read: the usage call failed — the host did not answer'],
    [{ why: 'unreadable', word: 'tool' }, 'Usage not read: the usage reply could not be read (a tool the read needs (id, timeout, tail, ls, sed, grep or sha256sum) is not in a system folder there)'],
    [{ why: 'account-changed' }, 'Usage not read: the account changed between the two reads'],
    [{ why: 'no-account-lines' }, 'Usage not read: this reply names no account lines'],
  ];
  for (const [miss, sentence] of said) {
    const memory = memoryOf(['a', { ...remembered('a', X), usageMiss: miss }]);
    assert.equal(spanOf(cell(memory)), sentence, JSON.stringify(miss));
  }
  assert.ok(!(spanOf(cell(memoryOf(['a', remembered('a', X)]))) ?? '').includes('does not ask Claude Code'), 'the sentence of a build that asks for no usage is still said');
});

test('E3S2-T19: the join, account changed — X\'s usage command ran once in the round, B shows X with A\'s figures, and no third call is made', async () => {
  const w = world({
    hosts: ['a', 'b'],
    reply: (host, mode) => (mode === 'claude-usage'
      ? (host === 'a' ? claudeFound(X, { usage: FIGURES }) : claudeFound(X, { usage: { state: 'skipped', why: 'account-changed' } }))
      : withOthers(host === 'a' ? X : Y)),
  });

  const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'b U'], 'a third call was made, or the usage of X was asked twice');
  assert.equal(usageCalls(w), 2);
  const b = foundOf(readingOf(result.memory, 'b'));
  assert.equal(b.email, X, 'B does not show the account the usage reply named');
  assert.deepEqual(b.usage, { state: 'skipped' }, 'B holds figures of its own');
  assert.deepEqual(memoryHostOf(result.memory, 'b').usageMiss, { why: 'account-changed' });
  assert.equal(memoryHostOf(result.memory, 'b').claudeAccount, KEY(X, 'b'), 'the account of B is not the second reply\'s');
  assert.equal(memoryHostOf(result.memory, 'b').usageAtMs, undefined);
  // the sharing rule shows A's figures at B
  const model = tableModel({
    hosts: ['a', 'b'], memory: result.memory, standings: [], stopped: false, permission: { given: '2026-11-19T12:00:00Z' }, damaged: undefined, round: { kind: 'idle' }, windows: false,
  });
  const rowOf = (host: string): { readonly sharedAcross: number | null; readonly span: string | null; readonly five: { readonly left: number | null } } => model.blocks.find((one) => one.host === host)?.rows[0] as { sharedAcross: number | null; span: string | null; five: { left: number | null } };
  assert.equal(rowOf('b').sharedAcross, 2, 'B does not show the figures of A');
  assert.equal(rowOf('b').five.left, 70, 'B shows other figures than those of A');
  assert.equal(rowOf('b').span, null);
});

test('E3S2-T20: the join, another key with a usage — no usage is counted, the figures are not shown, and the first key stays out of the usage set', async () => {
  const w = world({
    hosts: ['b', 'c', 'd'],
    reply: (host, mode) => (mode === 'claude-usage' ? claudeFound(Z, { usage: USAGE_READ }) : withOthers(host === 'b' ? Y : host === 'c' ? Y : Z)),
  });

  const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

  // b: first Y, usage reply names Z with figures → none counted. c (Y) and d (Z) are still due, since neither key is in the usage set.
  assert.deepEqual(callsOf(w), ['b N', 'b U', 'c N', 'c U', 'd N', 'd U'], 'the first key, or the second key, was counted for another account\'s usage');
  const b = foundOf(readingOf(result.memory, 'b'));
  assert.equal(b.email, Z);
  assert.equal(b.usage.state, 'skipped', 'figures of a call that was not for this account are shown');
  assert.deepEqual(memoryHostOf(result.memory, 'b').usageMiss, { why: 'account-changed' });
  assert.equal(memoryHostOf(result.memory, 'b').usageAtMs, undefined);
});

test('E3S2-T20 (not found): a usage reply whose Claude is not found takes that state, with no usage counted', async () => {
  const w = world({
    hosts: ['a', 'b'],
    reply: (host, mode) => (mode === 'claude-usage' && host === 'a'
      ? { kind: 'claude-only', claude: { state: 'signed-out' }, jqMissing: undefined }
      : mode === 'claude-usage' ? claudeFound(X, { usage: USAGE_READ }) : withOthers(X)),
  });

  const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'b U'], 'a sign-out was counted as a read usage');
  const read = readingOf(result.memory, 'a');
  assert.deepEqual(read.claude, { state: 'signed-out' });
  assert.deepEqual(read.codex, CODEX);
  assert.equal(memoryHostOf(result.memory, 'a').claudeAccount, undefined);
});

test('E3S2-T21: a call for an account already in the round\'s usage set is never built', async () => {
  const w = world({ hosts: ['a', 'b', 'c'], reply: accounts({ a: X, b: X, c: X }) });

  await roundOf(w, memoryOf(['b', remembered('b', X)], ['c', remembered('c', Y)]), 0, 'timer', EVERY_ROUND);

  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'c N'], 'a call for an account that was already read was built');
  assert.equal(w.calls.filter((call) => call.digest !== undefined).length, 1, 'more than one call holds a digest');
});

test('E3S2: no digest, no second call — a first reply that names no account lines', async () => {
  const w = world({ hosts: ['a', 'b'], reply: accounts({ a: X, b: X }), noLines: true });

  const result = await roundOf(w, new Map(), 0, 'timer', EVERY_ROUND);

  assert.deepEqual(callsOf(w), ['a N', 'b N'], 'a usage call was made with no digest');
  assert.deepEqual(memoryHostOf(result.memory, 'a').usageMiss, { why: 'no-account-lines' });
});

test('E3S2-T10: no email in an argv — over every argv the real call builds for places whose account text holds @, no word holds one', async () => {
  const frame = (email: string, org: string, usage: readonly string[]): string => ['COAI-ACCOUNTS 1', 'COAI-JQ found', 'COAI-VENDOR claude found', `:email ${email}`, `:org ${org}`, ...usage, 'COAI-ACCOUNTS-END', ''].join('\n');
  const argvs: (readonly string[])[] = [];
  const stdins: string[] = [];
  const accountsOf: Record<string, string> = { a: 'one@example.test', b: 'two@example.test', c: 'one@example.test' };
  const seamsFor = (): RemoteSeams => ({
    capture: (_target, args, _shell, _cap, _stop, _env, input = '') => {
      argvs.push([...args]);
      stdins.push(input);
      const host = args[args.indexOf('--') + 1] as string;
      const usage = args.includes("'claude-usage'");
      const email = accountsOf[host] as string;

      return Promise.resolve({
        code: 0,
        output: usage
          ? frame(email, 'Org', ['COAI-USAGE claude read', ':turns 0', ':line Current session: 5% used · resets Nov 19, 3pm (UTC)', ':unmatched 0'])
          : frame(email, 'Org', ['COAI-USAGE claude skipped', 'COAI-VENDOR codex absent', 'COAI-VENDOR agy absent']),
        stderrTail: '',
      });
    },
    exists: () => Promise.resolve(true),
    log: () => undefined,
    platform: 'linux',
    env: {},
  });
  const w = world({ hosts: ['a', 'b', 'c'] });
  const real: AccountsCall = (host, mode, digest) => runAccounts(host, mode, digest, ACCOUNTS_LIMITS, seamsFor());
  const seams = { ...w.seams, call: real, parse: (run: { readonly code: number; readonly stdout: string }, mode: 'claude-usage' | 'no-claude-usage'): AccountsRead => readAccounts(run, mode, NOW) };

  await runRound(seams, new Map(), 0, 'timer', EVERY_ROUND);

  const usageArgvs = argvs.filter((args) => args.includes("'claude-usage'"));
  assert.equal(usageArgvs.length, 2, 'the round did not make one usage call for each of the two accounts through the real call');
  for (const args of argvs) {
    assert.ok(args.every((word) => !word.includes('@')), `an argv holds an @: ${args.join(' ')}`);
  }
  for (const args of usageArgvs) {
    assert.match(String(args.at(-1)), /^'[0-9a-f]{64}'$/u, 'the last word of a usage call is not a digest');
  }
  for (const input of stdins) {
    assert.ok(!input.includes('example.test'), 'an account is in the script text');
  }
});
