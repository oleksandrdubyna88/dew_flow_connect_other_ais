import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import {
  FIRST_DELAY_MS, HOURLY_MS, ROUND_GAP_MS, REFRESH_FLOOR_MS, USAGE_POLICY, STUB_SENTENCE,
  accountsHosts, armFor, blockedAfter, callFor, claudeAccountKey, due, eligibleOf, logGate, memoryAfter, modeFor, needsSecondCall,
  nextAfterRound, openStarts, refreshAnswer, roundOrder, runRound, settleRound, standingsBump, timerFor, unreadHost, withRefusals,
} from '../remoteAccountsRound';
import type { ArmDecision, HostMemory, Memory, RoundResult, RoundSeams } from '../remoteAccountsRound';
import type { HostStanding } from '../remoteHosts';
import type { DoorAnswer } from '../remotePermission';
import type { RemoteRun } from '../remoteSsh';
import {
  EVERY_ROUND, HOURLY, OFF, ON_PRESS, READ_OK, USAGE_READ, accounts, callsOf, claudeFound, foundOf, memoryOf, remembered, roundOf, terminal, usageCalls, world,
} from './roundFakes';

/**
 * The round, over fake seams and a fake clock (E1.S3, A3-T8 … A3-T22, A3-T25, A3-T27 and A3-T29 … A3-T32). The state row, the Stop
 * and the nothing-stored scenarios are in `remoteAccountsState.test.ts`. Each name carries its scenario's id.
 */

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

// ───────────────── A3-T8: the log gate ─────────────────

test('A3-T8: a host that stays down is logged twice, not 144 times', () => {
  const written: string[] = [];
  const gate = logGate((line) => { written.push(line); });
  const seam = gate.seamFor('h');

  for (let at = 0; at < 144; at += 1) {
    seam('ConnectOtherAIs: remote read host=h session=accounts end=timeout stderr=');
  }
  assert.equal(written.length, 1, 'a host down for a day wrote 144 lines');
  seam('ConnectOtherAIs: remote read host=h session=accounts end=exit 255 stderr=Permission denied');
  assert.equal(written.length, 2, 'a different line was not written');
  assert.equal(gate.seamFor('g') === undefined, false);
  gate.seamFor('g')('ConnectOtherAIs: remote read host=g session=accounts end=timeout stderr=');
  assert.equal(written.length, 3, 'one host\'s last line stopped another host\'s first one');

  gate.answered('h');
  assert.deepEqual(written.slice(3), ['ConnectOtherAIs: accounts read host=h answered again']);
  gate.answered('h');
  assert.equal(written.length, 4, 'the "answered again" line was written twice');
  seam('ConnectOtherAIs: remote read host=h session=accounts end=exit 255 stderr=Permission denied');
  assert.equal(written.length, 5, 'a new failure after the host answered again was not written');
});

// ───────────────── A3-T9, A3-T10: which hosts, in which order ─────────────────

test('A3-T9: the distinct hosts of the live Creds session terminals, sorted', () => {
  const live = [
    terminal('hB', 's1'), terminal('hA', 's2'), terminal('hA', 's3'),
    { key: {}, name: 'bash', exited: false }, { key: {}, name: 'Creds run: x', exited: false }, { key: {}, name: 'Creds: -h / s', exited: false },
    terminal('hC', 's', { exited: true }),
  ];

  assert.deepEqual(accountsHosts(live), ['hA', 'hB'], 'an exited terminal\'s host was read');
  assert.deepEqual(accountsHosts([]), []);
});

test('A3-T10: the order moves one host each round', () => {
  const hosts = ['a', 'b', 'c'];

  assert.deepEqual(roundOrder(hosts, 0), ['a', 'b', 'c']);
  assert.deepEqual(roundOrder(hosts, 1), ['b', 'c', 'a'], 'the last host is always read last');
  assert.deepEqual(roundOrder(hosts, 2), ['c', 'a', 'b']);
  assert.deepEqual(roundOrder(hosts, 3), ['a', 'b', 'c']);
  assert.deepEqual(roundOrder([], 7), []);
});

// ───────────────── A3-T11 … A3-T13: one at a time, a failure, the door ─────────────────

test('A3-T11: one call at a time, in order', async () => {
  const w = world({ hosts: ['a', 'b', 'c'], hold: () => tick() });

  await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.equal(w.control.maxActive, 1, 'two reads ran at once');
  assert.deepEqual(w.events, ['start a', 'end a', 'start b', 'end b', 'start c', 'end c']);
});

test('A3-T12: a failed host does not stop the round, and keeps its older reading', async () => {
  const older = remembered('a', 'old@example.test');
  const w = world({ hosts: ['a', 'b', 'c'], reply: (host) => (host === 'a' ? 'down' : claudeFound('x@example.test')) });

  const result = await roundOf(w, memoryOf(['a', older]), 0, 'timer', OFF);

  assert.deepEqual(w.calls.map((call) => call.host), ['a', 'b', 'c'], 'the round stopped at the first failed host');
  assert.deepEqual(result.memory.get('a')?.failure, { sentence: '“a” did not answer over ssh within 30 s.', atMs: w.control.now });
  assert.equal(result.memory.get('a')?.reading, older.reading, 'the older reading was dropped by a failed read');
  assert.notEqual(result.memory.get('b')?.reading, undefined);
});

test('A3-T13: the door is asked before EVERY call, and its answer holds', async () => {
  const kinds: ReadonlyArray<readonly [Exclude<DoorAnswer, 'yes' | 'no-permission' | 'stopped'>, string]> = [['no', 'refused'], ['damaged', 'damaged'], ['waiting', 'unwritten']];
  for (const [answer, kind] of kinds) {
    const w = world({ hosts: ['a'], door: () => answer });
    const result = await roundOf(w, memoryOf(['a', remembered('a', 'x@example.test')]));
    assert.equal(w.calls.length, 0, `a host answered ${answer} was called`);
    assert.equal(result.memory.get('a')?.admission, kind, `${answer} was not kept in the host's memory`);
    assert.equal(result.memory.get('a')?.reading === undefined, answer === 'no', `${answer}: the reading of a host is dropped by a refusal and by nothing else`);
  }
  const all = world({ hosts: ['a', 'b', 'c'], door: () => 'waiting' });
  await roundOf(all);
  assert.equal(all.calls.length, 0, 'a window that could not save a refusal called a host');

  // `a` is read; after its call the door says `refused` for `b`: a "no" given during the round.
  const during = world({ hosts: ['a', 'b'], door: (host, _asked, w) => (host === 'b' && w.calls.length >= 1 ? 'no' : READ_OK) });
  const result = await roundOf(during, memoryOf(['b', remembered('b', 'x@example.test')]), 0, 'timer', OFF);
  assert.deepEqual(during.calls.map((call) => call.host), ['a'], 'a "no" given during a round did not stop the next call');
  assert.equal(result.memory.get('b')?.reading, undefined, 'a refusal kept the reading');

  // A later `read` clears the kept kind.
  const again = world({ hosts: ['a'] });
  const cleared = await roundOf(again, memoryOf(['a', { admission: 'not-asked' }]), 0, 'timer', OFF);
  assert.equal(cleared.memory.get('a')?.admission, undefined);
});

// ───────────────── A3-T14 … A3-T18: the account first, usage once for each account ─────────────────

test('A3-T14: the first call of every host reads no usage; the account decides the second', async () => {
  assert.equal(modeFor('first'), 'no-claude-usage', 'one account\'s usage was read twice in a round');
  assert.equal(modeFor('usage'), 'claude-usage');
  // @ts-expect-error — modeFor takes the kind of call only: no memory and no host chooses a mode
  modeFor('first', new Map(), 'a');

  const same = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'x@example.test' }) });
  await roundOf(same, memoryOf(['a', remembered('a', 'x@example.test')], ['b', remembered('b', 'x@example.test')]));
  assert.deepEqual(callsOf(same), ['a N', 'a U', 'b N'], 'one account\'s usage was read twice in a round');

  const timeout = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'x@example.test' }, { state: 'failed', why: 'timeout' }) });
  await roundOf(timeout);
  assert.deepEqual(callsOf(timeout), ['a N', 'a U', 'b N', 'b U'], 'a failed read on a was not read by the next host of the account');

  const three = world({ hosts: ['a', 'b', 'c'], reply: accounts({ a: 'x@example.test', b: 'x@example.test', c: 'x@example.test' }) });
  await roundOf(three);
  assert.deepEqual(callsOf(three), ['a N', 'a U', 'b N', 'c N']);
  assert.equal(usageCalls(three), 1, 'one account\'s usage was read twice in a round');

  // Two hosts whose account is NOT known, with an equal `org`: two keys, so each gets its own usage read.
  const unknown = world({ hosts: ['a', 'b'], reply: (_host, mode) => claudeFound('', { org: 'Acme', usage: mode === 'claude-usage' ? USAGE_READ : undefined }) });
  await roundOf(unknown);
  assert.deepEqual(callsOf(unknown), ['a N', 'a U', 'b N', 'b U'], 'two unknown accounts were merged, and the second host\'s usage was never read');
});

test('A3-T14: the account key — a known account by email and organization, an unknown one by its host', () => {
  const key = (email: string, org: string | null, host = 'a'): string => claudeAccountKey(foundOf(claudeFound(email, { org })), host);

  assert.equal(key('x@example.test', 'Acme', 'a'), key('x@example.test', 'Acme', 'b'), 'one account on two hosts has two keys');
  assert.notEqual(key('x@example.test', null), key('x@example.test', 'Acme'), 'an account with no organization and one with an organization share a key');
  assert.notEqual(key('', 'Acme', 'a'), key('', 'Acme', 'b'), 'two unknown accounts share a key');
  assert.notEqual(key('', null, 'a'), key('x@example.test', null, 'a'));
});

test('A3-T15: an account that changed is read in the same round, once', async () => {
  // `a` and `b` of X; `b` last seen with X, and now answers a new account Y, whose second call answers Z.
  const w = world({
    hosts: ['a', 'b', 'c'],
    reply: (host, mode, nth) => {
      const email = host === 'b' ? (nth === 0 ? 'y@example.test' : 'z@example.test') : host === 'c' ? 'z@example.test' : 'x@example.test';

      return claudeFound(email, { usage: mode === 'claude-usage' ? USAGE_READ : undefined });
    },
  });

  const result = await roundOf(w, memoryOf(['a', remembered('a', 'x@example.test')], ['b', remembered('b', 'x@example.test')]));

  assert.ok(w.calls.filter((call) => call.host === 'b').length <= 2, 'a host was called three times in a round');
  // E3.S2: the second call of `b` named another account (Z) than its first reply (Y), so nothing is counted for either, and `c`, which holds Z, asks for its own usage
  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N', 'b U', 'c N', 'c U'], 'a new account\'s usage was a round late, or the usage of another account was counted');
  const kept = result.memory.get('b')?.reading;
  assert.ok(kept !== undefined, 'b has no reading');
  assert.equal(foundOf(kept.read).email, 'z@example.test', 'the second call\'s reading was not kept');
});

test('A3-T15: no second call for a blocked host, or for an account already read in the round', async () => {
  const blocked = world({ hosts: ['b'], reply: accounts({ b: 'y@example.test' }) });
  await roundOf(blocked, memoryOf(['b', { ...remembered('b', 'x@example.test'), usageBlockedAt: '2.1.5' }]));
  assert.deepEqual(callsOf(blocked), ['b N'], 'a blocked host was called a second time');

  const read = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'x@example.test' }) });
  await roundOf(read, memoryOf(['b', remembered('b', 'y@example.test')]));
  assert.deepEqual(callsOf(read), ['a N', 'a U', 'b N'], 'b was called a second time for an account already read');
});

test('A3-T16: a usage read that went wrong is not repeated blindly', async () => {
  const wrong = (why: 'model-call' | 'format' | 'timeout', version: (round: number) => string) => {
    let round = 0;
    const w = world({ hosts: ['b'], reply: (_host, mode) => claudeFound('x@example.test', { version: version(round), usage: mode === 'claude-usage' ? { state: 'failed', why } : undefined }) });

    return { w, next: async (memory: Memory): Promise<ReturnType<typeof roundOf>> => { round += 1; return roundOf(w, memory, round); }, first: (): ReturnType<typeof roundOf> => roundOf(w, new Map(), 0) };
  };
  for (const why of ['model-call', 'format'] as const) {
    const run = wrong(why, (round) => (round < 4 ? '2.1.5' : '2.2.0'));
    let memory = (await run.first()).memory;
    assert.deepEqual(callsOf(run.w), ['b N', 'b U'], `${why}: the first usage read did not happen`);
    for (let round = 1; round < 4; round += 1) {
      memory = new Map((await run.next(memory)).memory);
    }
    assert.equal(usageCalls(run.w), 1, `${why}: a model call was made six times an hour`);
    await run.next(memory);
    assert.equal(usageCalls(run.w), 2, `${why}: a host that reports another version stayed blocked`);
  }
  const timeout = wrong('timeout', () => '2.1.5');
  await timeout.next((await timeout.first()).memory);
  assert.equal(usageCalls(timeout.w), 2, 'a timeout blocked the host');
  const reload = world({ hosts: ['b'], reply: accounts({ b: 'x@example.test' }) });
  await roundOf(reload, new Map());
  assert.equal(usageCalls(reload), 1, 'a reload (a new Memory) did not unblock the host');
  assert.equal(blockedAfter(claudeFound('x@example.test', { version: null, usage: { state: 'failed', why: 'model-call' } })) !== undefined, true, 'a reading with no version cannot be blocked');
  assert.equal(blockedAfter(claudeFound('x@example.test', { usage: { state: 'failed', why: 'timeout' } })), undefined);
});

test('A3-T17: the usage policy is one constant, and E3.S5 ships it every-round', async () => {
  assert.deepEqual(USAGE_POLICY, { kind: 'every-round' }, 'the shipped policy is not every-round');

  const hosts = ['a', 'b'];
  const both = accounts({ a: 'x@example.test', b: 'x@example.test' });
  // every-round: once in each of six rounds, always by the second call of the first host of the round.
  const every = world({ hosts, reply: both });
  let memory = new Map<string, HostMemory>();
  for (let round = 0; round < 6; round += 1) {
    const before = every.calls.length;
    memory = new Map((await roundOf(every, memory, round, 'timer', EVERY_ROUND)).memory);
    every.control.now += ROUND_GAP_MS;
    const mine = every.calls.slice(before);
    const usage = mine.map((call, at) => ({ call, at })).filter((one) => one.call.mode === 'claude-usage');
    assert.equal(usage.length, 1, `round ${round}: not one usage read`);
    assert.equal(usage[0]?.call.host, roundOrder(hosts, round)[0], `round ${round}: the usage was not read by the first host of the round`);
    assert.equal(usage[0]?.at, 1, `round ${round}: the usage read was not the second call of the first host`);
  }
  // hourly: at most once in six rounds ten minutes apart.
  const hourly = world({ hosts, reply: both });
  memory = new Map();
  for (let round = 0; round < 6; round += 1) {
    memory = new Map((await roundOf(hourly, memory, round, 'timer', HOURLY)).memory);
    hourly.control.now += ROUND_GAP_MS;
  }
  assert.equal(usageCalls(hourly), 1, 'hourly read the usage more than once in an hour');
  hourly.control.now += HOURLY_MS;
  await roundOf(hourly, memory, 6, 'timer', HOURLY);
  assert.equal(usageCalls(hourly), 2, 'hourly never read the usage again after an hour');
  // on-press: only on a refresh.
  for (const [trigger, reads] of [['timer', 0], ['open', 0], ['refresh', 1]] as const) {
    const press = world({ hosts, reply: both });
    await roundOf(press, new Map(), 0, trigger, ON_PRESS);
    assert.equal(usageCalls(press), reads, `on-press: ${trigger}`);
  }
  // off: never, for any trigger, and not for an account that changed.
  for (const trigger of ['timer', 'open', 'refresh'] as const) {
    const off = world({ hosts, reply: both });
    await roundOf(off, new Map(), 0, trigger, OFF);
    assert.equal(usageCalls(off), 0, `a usage read was made while the usage read is off (${trigger})`);
  }
  const changed = claudeFound('y@example.test');
  assert.equal(needsSecondCall(changed, new Set(), memoryOf(['b', remembered('b', 'x@example.test')]), 'b', OFF, 0, 'refresh'), false, 'a usage read was made while the usage read is off');
  assert.equal(needsSecondCall(changed, new Set(), new Map(), 'b', EVERY_ROUND, 0, 'timer'), true, 'the companion: the same reading is a second call with the policy on');
  // The shipped constant: six rounds over two hosts of one account make 18 calls, and each round's one usage call is the second call of its first host.
  const shipped = world({ hosts, reply: both });
  memory = new Map();
  for (let round = 0; round < 6; round += 1) {
    memory = new Map((await runRound(shipped.seams, memory, round, 'timer', USAGE_POLICY)).memory);
  }
  assert.equal(shipped.calls.length, 18);
  assert.equal(usageCalls(shipped), 6, 'the shipped policy does not read the usage once in each round');
});

test('A3-T17 companion: due — each policy, on its own', () => {
  assert.equal(due(EVERY_ROUND, 5, 6, 'timer'), true);
  assert.equal(due(HOURLY, undefined, 0, 'timer'), true);
  assert.equal(due(HOURLY, 0, HOURLY_MS - 1, 'timer'), false);
  assert.equal(due(HOURLY, 0, HOURLY_MS, 'timer'), true);
  assert.equal(due(ON_PRESS, undefined, 0, 'timer'), false);
  assert.equal(due(ON_PRESS, undefined, 0, 'refresh'), true);
  for (const trigger of ['timer', 'open', 'refresh'] as const) {
    assert.equal(due(OFF, undefined, 0, trigger), false);
  }
});

test('A3-T18: the calls of a round are bounded', async () => {
  const names = ['a', 'b', 'c', 'd', 'e'];
  const oneAccount = Object.fromEntries(names.map((host) => [host, 'x@example.test']));
  const manyAccounts = Object.fromEntries(names.map((host) => [host, `${host}@example.test`]));
  const counts = (w: ReturnType<typeof world>): Record<string, number> => Object.fromEntries(names.map((host) => [host, w.calls.filter((call) => call.host === host).length]));
  const bounded = (w: ReturnType<typeof world>): void => {
    assert.ok(Object.values(counts(w)).every((count) => count <= 2), 'a host was called three times in a round');
  };

  for (const memory of [new Map<string, HostMemory>(), memoryOf(...names.map((host) => [host, remembered(host, 'x@example.test')] as const))]) {
    const one = world({ hosts: names, reply: accounts(oneAccount) });
    await roundOf(one, memory);
    bounded(one);
    assert.equal(one.calls.length, 6, 'five hosts of one account did not make exactly six calls');
    assert.deepEqual(counts(one), { a: 2, b: 1, c: 1, d: 1, e: 1 });
    assert.equal(usageCalls(one), 1);
  }
  const many = world({ hosts: names, reply: accounts(manyAccounts) });
  await roundOf(many);
  bounded(many);
  assert.equal(many.calls.length, 10);
  assert.deepEqual(counts(many), { a: 2, b: 2, c: 2, d: 2, e: 2 });
  assert.equal(usageCalls(many), 5);
  const off = world({ hosts: names, reply: accounts(oneAccount) });
  await roundOf(off, new Map(), 0, 'timer', OFF);
  bounded(off);
  assert.equal(off.calls.length, 5);
});

// ───────────────── A3-T19: a closed host ─────────────────

test('A3-T19: a closed host is dropped, and a result in flight too', async () => {
  const w = world({ hosts: ['a', 'b'] });
  w.control.terminals = w.control.terminals.filter((one) => !one.name.includes(' b '));

  const result = await roundOf(w, memoryOf(['b', remembered('b', 'x@example.test')]), 0, 'timer', OFF);

  assert.deepEqual(callsOf(w), ['a N']);
  assert.equal(result.memory.has('b'), false, 'a closed terminal\'s host kept its reading');

  const flight = world({
    hosts: ['a', 'b'],
    hold: (host, _mode, self) => {
      if (host === 'b') {
        self.control.terminals = self.control.terminals.filter((one) => !one.name.includes(' b '));
      }

      return undefined;
    },
  });
  const late = await roundOf(flight, memoryOf(['b', remembered('b', 'x@example.test')]), 0, 'timer', OFF);
  assert.deepEqual(callsOf(flight), ['a N', 'b N']);
  assert.equal(late.memory.has('b'), false, 'a closed terminal\'s host kept its older entry, and memoryAfter would bring it back');
  assert.equal(flight.landed.count, 1, 'the list was redrawn for a result that was dropped');
  assert.notEqual(late.memory.get('a')?.reading, undefined, 'the companion: an open host keeps its reading');
});

// ───────────────── A3-T20 … A3-T22: the timer ─────────────────

test('A3-T20: the first round and the gap', () => {
  const decide = (over: Partial<Parameters<typeof armFor>[0]> = {}): ArmDecision => armFor({ eligible: true, permitted: true, armed: false, stopped: false, running: false, ...over });

  assert.equal(decide(), 'arm-first', 'an eligible host at activation did not arm a first round');
  assert.deepEqual(timerFor('arm-first', false, 0), { kind: 'start', ms: FIRST_DELAY_MS });
  assert.equal(FIRST_DELAY_MS, 30_000);
  assert.equal(decide({ eligible: false }), 'clear', 'a reload with no eligible host armed a round');
  assert.equal(decide({ armed: true }), 'keep', 'an open while armed re-armed the timer');
  assert.equal(decide({ running: true }), 'keep', 'a terminal opened during a round armed a second one');
  assert.equal(decide({ stopped: true }), 'clear', 'a stopped round armed itself');

  const started = 1_000;
  const ended = started + 150_000;
  const next = nextAfterRound({ stopped: false, eligible: true, permitted: true, endedMs: ended });
  assert.ok(next.kind === 'at' && next.atMs >= ended + ROUND_GAP_MS, 'the next round began before 10 minutes after the last ended');
  assert.deepEqual(next, { kind: 'at', atMs: ended + ROUND_GAP_MS });
  assert.equal(ROUND_GAP_MS, 600_000);
  assert.deepEqual(nextAfterRound({ stopped: true, eligible: true, permitted: true, endedMs: ended }), { kind: 'clear' });
  assert.deepEqual(nextAfterRound({ stopped: false, eligible: false, permitted: true, endedMs: ended }), { kind: 'clear' });
});

test('A3-T21: the timer goes with the last terminal', () => {
  const standing = (host: string, answer: 'yes' | 'no'): HostStanding => ({ host, answer, how: 'person', askedUtc: '2026-11-01T00:00:00.000Z' });
  const eligible = (live: readonly string[], standings: readonly HostStanding[]): boolean => eligibleOf(live, standings).length > 0;

  assert.equal(eligible([], []), false);
  assert.equal(armFor({ eligible: eligible([], []), permitted: true, armed: true, stopped: false, running: false }), 'clear', 'a timer ran with no Creds terminal');
  assert.equal(eligible(['a'], [standing('a', 'no')]), false, 'only refused hosts left, and the host was eligible');
  assert.equal(armFor({ eligible: eligible(['a'], [standing('a', 'no')]), permitted: true, armed: true, stopped: false, running: false }), 'clear');
  assert.deepEqual(eligibleOf(['a', 'b'], [standing('a', 'no'), standing('b', 'yes')]), ['b']);
  assert.deepEqual(eligibleOf(['a'], []), ['a'], 'a host with no row is eligible: the round allows it by itself');
  assert.equal(armFor({ eligible: eligible(['a'], []), permitted: true, armed: false, stopped: false, running: false }), 'arm-first', 'a terminal that opens did not arm the timer again');
});

test('A3-T22: Refresh now and opening the list', async () => {
  const now = 10_000_000;

  assert.equal(refreshAnswer({ running: false, lastEndMs: now - 61_000, now }), 'start');
  assert.equal(refreshAnswer({ running: false, lastEndMs: now - 59_000, now }), 'floor', 'Refresh started a round 30 s after the last');
  assert.equal(refreshAnswer({ running: true, lastEndMs: now - 61_000, now }), 'busy');
  assert.equal(refreshAnswer({ running: false, lastEndMs: undefined, now }), 'start');
  assert.equal(REFRESH_FLOOR_MS, 60_000);
  assert.deepEqual(nextAfterRound({ stopped: false, eligible: true, permitted: true, endedMs: now }), { kind: 'at', atMs: now + ROUND_GAP_MS }, 'after a refresh round the next timer round is 600 s after ITS end');

  const reading = remembered('a', 'x@example.test');
  const failed: HostMemory = { failure: { sentence: 'did not answer', atMs: 0 } };
  const unreadable: HostMemory = { unreadable: { why: 'exit', atMs: 0 } };
  assert.equal(unreadHost(['a'], new Map(), []), true, 'a host with neither a reading nor a failure');
  assert.equal(unreadHost(['a'], memoryOf(['a', reading]), []), false);
  assert.equal(unreadHost(['a'], memoryOf(['a', failed]), []), false);
  assert.equal(unreadHost(['a'], memoryOf(['a', unreadable]), []), true, 'the text says neither a reading nor a failure: an unreadable reply is neither');
  assert.equal(unreadHost(['a', 'b'], memoryOf(['a', reading]), []), true);
  assert.equal(unreadHost(['b'], new Map(), [{ host: 'b', answer: 'no', how: 'person', askedUtc: '2026-11-01T00:00:00.000Z' }]), false, 'a refused host counts as unread');
  assert.equal(openStarts({ running: false, lastEndMs: now - 61_000, now, unreadHost: true }), true);
  assert.equal(openStarts({ running: false, lastEndMs: now - 61_000, now, unreadHost: false }), false);
  assert.equal(openStarts({ running: false, lastEndMs: now - 59_000, now, unreadHost: true }), false, 'opening the list started a round inside the floor');
  assert.equal(openStarts({ running: true, lastEndMs: undefined, now, unreadHost: true }), false);
});

// ───────────────── A3-T25, A3-T27 (pure half): no ask, and the two test-mode locks ─────────────────

test('A3-T25: the round never asks a person', async () => {
  const w = world({ hosts: [] });
  const withAsk: RoundSeams = {
    ...w.seams,
    // @ts-expect-error — a round never asks a person: RoundSeams has no member that asks
    ask: () => { throw new Error('the round asked a person'); },
  };
  type AskKeys = Extract<keyof RoundSeams, `${string}ask${string}` | `${string}Ask${string}`>;
  const noAsk: [AskKeys] extends [never] ? true : false = true;
  assert.equal(noAsk, true);
  assert.equal(typeof withAsk.may, 'function');

  const hundred = Array.from({ length: 100 }, (_unused, at) => `host${at}`);
  const many = world({ hosts: hundred, door: () => 'waiting' });
  const result = await roundOf(many);
  assert.equal(many.calls.length, 0);
  assert.equal(result.memory.size, 100);
});

test('A3-T26: the parser reads the mode that was sent, the extra call\'s too', async () => {
  const w = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'y@example.test' }) });

  await roundOf(w);

  assert.deepEqual(w.parsed.map((one) => one.mode), w.calls.map((call) => call.mode), 'the reply was read against another mode');
  assert.equal(w.parsed.length, 4);
});

test('A3-T27: the two test-mode locks, pure', async () => {
  const decisions: readonly ArmDecision[] = ['arm-first', 'clear', 'keep'];
  for (const decision of decisions) {
    assert.deepEqual(timerFor(decision, true, 0), { kind: 'none' }, 'a round armed itself in a test run');
  }
  assert.notDeepEqual(timerFor('arm-first', false, 0), { kind: 'none' });
  assert.deepEqual(timerFor('clear', false, 0), { kind: 'clear' });
  assert.deepEqual(timerFor('keep', false, 0), { kind: 'none' });

  // The timer after a round goes through the same lock: the next round is the gap from the END of the last, counted from now.
  const next = nextAfterRound({ stopped: false, eligible: true, permitted: true, endedMs: 1_000_000 });
  assert.deepEqual(timerFor(next, true, 1_000_000), { kind: 'none' }, 'a round armed itself in a test run');
  assert.deepEqual(timerFor(next, false, 1_000_000), { kind: 'start', ms: ROUND_GAP_MS });
  assert.deepEqual(timerFor(next, false, 1_100_000), { kind: 'start', ms: ROUND_GAP_MS - 100_000 }, 'the gap was counted from the wrong moment');
  assert.deepEqual(timerFor(next, false, 9_000_000), { kind: 'start', ms: 0 }, 'a gap that is over waited a negative time');
  assert.deepEqual(timerFor({ kind: 'clear' }, false, 0), { kind: 'clear' });
  assert.deepEqual(timerFor({ kind: 'clear' }, true, 0), { kind: 'none' });

  const real: string[] = [];
  const realCall = (host: string): Promise<RemoteRun> => { real.push(host); return Promise.resolve({ kind: 'answered', code: 0, stdout: '' }); };
  const fakeCall = (): Promise<RemoteRun> => Promise.resolve({ kind: 'answered', code: 1, stdout: 'fake' });

  const stub = await callFor(true, realCall, undefined)('h', 'no-claude-usage');
  assert.deepEqual(stub, { kind: 'refused', refusal: STUB_SENTENCE }, 'an accounts call reached ssh in a test run');
  assert.equal(STUB_SENTENCE, 'The accounts call is not replaced in this test run.');
  assert.deepEqual(real, [], 'an accounts call reached ssh in a test run');
  assert.deepEqual(await callFor(true, realCall, fakeCall)('h', 'no-claude-usage'), { kind: 'answered', code: 1, stdout: 'fake' });
  await callFor(false, realCall, undefined)('h', 'no-claude-usage');
  assert.deepEqual(real, ['h'], 'outside test mode the real call is used');
});

// ───────────────── A3-T29, A3-T30: a Stop or a refusal while a call is held open ─────────────────

/** A call for `a` that the test holds open, and the way to let it go. */
function held(): { readonly hold: () => Promise<void>; readonly release: () => void; readonly opened: Promise<void> } {
  let release: () => void = () => undefined;
  let opened: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const started = new Promise<void>((resolve) => { opened = resolve; });

  return { hold: () => { opened(); return gate; }, release, opened: started };
}

test('A3-T29: a Stop while a call is held open publishes nothing, and the memory stays cleared', async () => {
  const variants: ReadonlyArray<readonly [string, (control: { generation: number; stopped: boolean }) => void, string]> = [
    ['the generation only (a Stop of this window)', (control) => { control.generation += 1; }, 'a reading came back after Stop'],
    ['stopped() only (a Stop by the state row of another window)', (control) => { control.stopped = true; }, 'a stopped round published a reading'],
    ['both', (control) => { control.generation += 1; control.stopped = true; }, 'a reading came back after Stop'],
  ];
  for (const [name, stop, said] of variants) {
    const gate = held();
    const w = world({ hosts: ['a', 'b'], hold: (host) => (host === 'a' ? gate.hold() : undefined) });
    const running = roundOf(w, new Map(), 0, 'timer', OFF);
    await gate.opened;
    stop(w.control);
    gate.release();
    const result = await running;

    assert.equal(result.memory.get('a')?.reading, undefined, `${name}: ${said}`);
    assert.equal(w.parsed.length, 0, `${name}: the reply of a stopped round was parsed for a reading to keep`);
    assert.equal(w.landed.count, 0, `${name}: the list was redrawn for a result that was dropped`);
    assert.deepEqual(w.calls.map((call) => call.host), ['a'], `${name}: b was called after the Stop`);
    assert.equal(result.abandoned, true, `${name}: the round was not abandoned`);
    assert.deepEqual(w.dropped, ['a'], `${name}: one drop was logged ${w.dropped.length} times`);
    assert.deepEqual([...memoryAfter(new Map(), result)], [], `a stopped round's memory came back (${name})`);
  }
  const before = world({ hosts: ['a', 'b'] });
  before.control.stopped = true;
  const result = await roundOf(before);
  assert.deepEqual([before.calls.length, before.admits.length], [0, 0], 'a Stop before the first call: a call or an admission was made');
  assert.equal(result.abandoned, true);
  const fine = world({ hosts: ['a'] });
  const finished = await roundOf(fine, new Map(), 0, 'timer', OFF);
  assert.equal(finished.abandoned, false);
  assert.equal(memoryAfter(new Map(), finished).size, 1, 'the companion: a finished round\'s memory is stored');
});

test('A3-T30: a refusal while a call is held open publishes nothing', async () => {
  const older = remembered('a', 'old@example.test');
  const heldRound = (door: (asked: number) => DoorAnswer, during: (control: { generation: number }) => void): { readonly w: ReturnType<typeof world>; readonly done: ReturnType<typeof roundOf> } => {
    const gate = held();
    const w = world({ hosts: ['a'], hold: () => gate.hold(), door: (_host, asked) => door(asked) });
    const done = (async () => {
      const running = roundOf(w, memoryOf(['a', older]), 0, 'timer', OFF);
      await gate.opened;
      during(w.control);
      gate.release();

      return running;
    })();

    return { w, done };
  };

  // (1) the generation is bumped and the door now says `refused`: dropped; the round ends.
  // The host's change handler runs while the call is held, as stop() does in A3-T29: the newly refused host keeps an entry that says so.
  let current: Memory = memoryOf(['a', older]);
  const one = heldRound((asked) => (asked === 0 ? READ_OK : 'no'), (control) => { control.generation += 1; current = withRefusals(current, new Set(['a'])); });
  const first = await one.done;
  assert.deepEqual(memoryAfter(current, first).get('a'), { ...older, reading: undefined, admission: 'refused' }, 'a refused host\'s older reading was brought back or published');
  assert.deepEqual(one.w.dropped, ['a']);
  assert.equal(one.w.calls.length, 1, 'an extra call was made');

  // (2) a Forget: the generation is bumped; the door is NOT asked again.
  const two = heldRound((asked) => (asked === 0 ? READ_OK : 'no-permission'), (control) => { control.generation += 1; });
  const second = await two.done;
  assert.equal(two.w.admits.length, 1, 'a forgotten host was admitted again by the check');
  assert.equal(second.memory.get('a')?.admission, undefined);
  assert.deepEqual(two.w.dropped, ['a']);

  // (3) the generation unchanged, the door says `refused` at the check: dropped, the older reading goes.
  const three = heldRound((asked) => (asked === 0 ? READ_OK : 'no'), () => undefined);
  const third = await three.done;
  assert.deepEqual(three.w.dropped, ['a'], 'a refused host\'s reading was published');
  assert.equal(third.memory.get('a')?.admission, 'refused', 'the kind the check answered was not kept');
  assert.equal(third.memory.get('a')?.reading, undefined, 'a refused host kept its reading');
  assert.equal(third.memory.get('a')?.failure, undefined);
  assert.equal(three.w.calls.length, 1);
  assert.deepEqual(three.w.dropped, ['a'], 'a refused host\'s reading was published');
  assert.equal(third.abandoned, false);

  // (4) the door says `read` and nothing was bumped: published.
  const four = heldRound(() => READ_OK, () => undefined);
  const fourth = await four.done;
  assert.notEqual(fourth.memory.get('a')?.reading, older.reading);
  assert.deepEqual(four.w.dropped, []);
  assert.equal(four.w.landed.count, 1);
});

// ───────────────── A3-T31, A3-T32: one account's usage once, whatever was remembered ─────────────────

test('A3-T31: one account\'s usage is read once, in the first round and in every later one', async () => {
  const same = accounts({ a: 'x@example.test', b: 'x@example.test' });
  const fresh = world({ hosts: ['a', 'b'], reply: same });
  const first = await roundOf(fresh);
  assert.deepEqual(callsOf(fresh), ['a N', 'a U', 'b N'], 'one account\'s usage was read twice in a round');

  const later = world({ hosts: ['a', 'b'], reply: same });
  await roundOf(later, first.memory, 1);
  assert.deepEqual(callsOf(later), ['b N', 'b U', 'a N'], 'a remembered host did not start without usage');

  const apart = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'y@example.test' }) });
  await roundOf(apart);
  assert.deepEqual(callsOf(apart), ['a N', 'a U', 'b N', 'b U']);

  const blocked = world({ hosts: ['a', 'b'], reply: same });
  await roundOf(blocked, memoryOf(['a', { ...remembered('a', 'x@example.test'), usageBlockedAt: '2.1.5' }]));
  assert.deepEqual(callsOf(blocked), ['a N', 'b N', 'b U'], 'a blocked host got a second call, or b did not');

  const off = world({ hosts: ['a', 'b'], reply: same });
  await roundOf(off, new Map(), 0, 'timer', OFF);
  assert.equal(off.calls.length, 2);
  assert.equal(usageCalls(off), 0);

  const timeout = world({ hosts: ['a', 'b'], reply: accounts({ a: 'x@example.test', b: 'x@example.test' }, { state: 'failed', why: 'timeout' }) });
  await roundOf(timeout);
  assert.deepEqual(callsOf(timeout), ['a N', 'a U', 'b N', 'b U'], 'b was not called a second time after the usage of X failed on a');
});

test('A3-T32: a changed account whose new account was already read gets no usage call', async () => {
  const same = accounts({ a: 'x@example.test', b: 'x@example.test' });
  const ahead = memoryOf(['a', remembered('a', 'x@example.test')], ['b', remembered('b', 'y@example.test')]);

  const w = world({ hosts: ['a', 'b'], reply: same });
  const result = await roundOf(w, ahead);
  assert.deepEqual(callsOf(w), ['a N', 'a U', 'b N'], 'an account already read in this round was read again on a host whose account had changed');
  assert.equal(result.memory.get('b')?.claudeAccount, claudeAccountKey(foundOf(claudeFound('x@example.test')), 'b'), 'b\'s account is not what its first reply said');

  const mirror = world({ hosts: ['a', 'b'], reply: (host, mode) => claudeFound(host === 'b' ? 'z@example.test' : 'x@example.test', { usage: mode === 'claude-usage' ? USAGE_READ : undefined }) });
  await roundOf(mirror, ahead);
  assert.deepEqual(callsOf(mirror), ['a N', 'a U', 'b N', 'b U'], 'a new account was not read');

  const reversed = world({ hosts: ['a', 'b'], reply: same });
  await roundOf(reversed, ahead, 1);
  assert.deepEqual(callsOf(reversed), ['b N', 'b U', 'a N'], 'the reversed order read X twice');
});

// ───────────────── A3-T29, A3-T30 (the host's half): what the change signal of the list makes of the generation ─────────────────

test('A3-T30: a refusal or a Forget shown by the change signal bumps the generation; a new row does not', () => {
  const row = (host: string, answer: 'yes' | 'no'): HostStanding => ({ host, answer, how: 'person', askedUtc: '2026-11-01T00:00:00.000Z' });
  const known = new Set(['a', 'b']);
  const refused = new Set(['b']);

  assert.equal(standingsBump(known, refused, [row('a', 'yes'), row('b', 'no')]), false, 'a signal that changed nothing bumped the generation');
  assert.equal(standingsBump(known, refused, [row('a', 'yes'), row('b', 'no'), row('c', 'yes')]), false, 'a new row (the automatic "yes" of this very round) bumped the generation');
  assert.equal(standingsBump(known, refused, [row('a', 'no'), row('b', 'no')]), true, 'a refusal in flight did not bump the generation');
  assert.equal(standingsBump(known, refused, [row('a', 'yes')]), true, 'a Forget did not bump the generation');
  assert.equal(standingsBump(new Set(), new Set(), [row('a', 'no')]), true, 'a refusal shown to a window that knew nothing did not bump the generation');
  assert.equal(standingsBump(new Set(), new Set(), []), false);
});

// ───────────────── the review fixes of E1.S3 ─────────────────

test('A3-T29 (CR): a Stop saved in another window (stopped() only, generation unchanged) while the check\'s door is awaited publishes nothing', async () => {
  const w = world({ hosts: ['a'], door: (_host, asked, self) => { if (asked === 1) { self.control.stopped = true; } return READ_OK; } });

  const result = await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.equal(result.abandoned, true, 'a Stop saved while the door was awaited was not seen after the door answered read');
  assert.equal(result.memory.get('a')?.reading, undefined, 'a reading came back after Stop');
  assert.equal(w.parsed.length, 0, 'the reply of a stopped round was parsed for a reading to keep');
  assert.equal(w.landed.count, 0, 'the list was redrawn for a result that was dropped');
  assert.deepEqual(w.dropped, ['a']);
  assert.deepEqual([...memoryAfter(new Map(), result)], [], 'a stopped round\'s memory came back');
});

test('A3-T29 (F1): a Stop given while the check\'s door is awaited, on the LAST host, publishes nothing', async () => {
  const w = world({ hosts: ['a'], door: (_host, asked, self) => { if (asked === 1) { self.control.generation += 1; } return READ_OK; } });

  const result = await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.equal(result.abandoned, true, 'a Stop given while the door was awaited was not seen');
  assert.equal(result.memory.get('a')?.reading, undefined, 'a reading came back after Stop');
  assert.equal(w.landed.count, 0, 'the list was redrawn for a result that was dropped');
  assert.deepEqual(w.dropped, ['a']);
  assert.deepEqual([...memoryAfter(new Map(), result)], [], 'a stopped round\'s memory came back');
});

test('A3-T29 (F1): a generation that moved by the end of the round abandons it, so its memory is never stored', async () => {
  // The last host\'s result passed every check; the Stop came after, while the round was finishing.
  const w = world({ hosts: ['a'] });
  const seams = { ...w.seams, landed: () => { w.control.generation += 1; } };

  const result = await runRound(seams, new Map(), 0, 'timer', OFF);

  assert.equal(result.abandoned, true, 'a Stop given as the round ended did not abandon it');
  assert.deepEqual([...memoryAfter(new Map(), result)], [], 'a stopped round\'s memory came back');
});

test('A3-T29 (F2): no ssh call starts after a Stop', async () => {
  const before = world({ hosts: ['a', 'b'], door: (_host, asked, self) => { if (asked === 0) { self.control.generation += 1; } return READ_OK; } });
  const result = await roundOf(before, new Map(), 0, 'timer', OFF);
  assert.equal(before.calls.length, 0, 'an ssh call started after a Stop was given while the door was awaited');
  assert.equal(result.abandoned, true);

  // A terminal that closed during its call, and a Stop: the round ends, it does not go on to the next host.
  const closed = world({
    hosts: ['a', 'b'],
    hold: (host, _mode, self) => {
      if (host === 'a') {
        self.control.terminals = self.control.terminals.filter((one) => !one.name.includes(' a '));
        self.control.generation += 1;
      }

      return undefined;
    },
  });
  const ended = await roundOf(closed, new Map(), 0, 'timer', OFF);
  assert.deepEqual(callsOf(closed), ['a N'], 'a host was called after a Stop that came while a closed host\'s call ran');
  assert.equal(ended.abandoned, true);
});

test('A3-T22 (F3): a round that fails is settled: logged once, and the timer is still set', async () => {
  const failing = world({ hosts: ['a'], door: () => { throw new Error('a seam failed'); } });
  const log: string[] = [];
  const scheduled: (RoundResult | undefined)[] = [];
  const fallbacks: string[] = [];

  const result = await settleRound<RoundResult>(
    () => roundOf(failing),
    (done) => { scheduled.push(done); return Promise.resolve(); },
    () => { fallbacks.push('fallback'); },
    () => { log.push('failed'); },
  );

  assert.equal(result, undefined);
  assert.deepEqual(log, ['failed'], 'a failed round was not logged exactly once');
  assert.deepEqual(scheduled, [undefined], 'rounds stopped silently: no timer was asked for after a failed round');
  assert.equal(fallbacks.length, 0);
  // The timer that is then set is the planner\'s, from now, and outside test mode it starts.
  assert.deepEqual(timerFor(nextAfterRound({ stopped: false, eligible: true, permitted: true, endedMs: 5_000 }), false, 5_000), { kind: 'start', ms: ROUND_GAP_MS });

  // A scheduler that fails too: the fallback sets the timer.
  const both = await settleRound(() => Promise.reject(new Error('x')), () => Promise.reject(new Error('y')), () => { fallbacks.push('fallback'); }, () => { log.push('failed'); });
  assert.equal(both, undefined);
  assert.deepEqual(fallbacks, ['fallback'], 'rounds stopped silently: the fallback timer was not set');
  assert.equal(log.length, 3);

  const seen: unknown[] = [];
  const fine = await settleRound(() => Promise.resolve(7), (done) => { seen.push(done); return Promise.resolve(); }, () => undefined, () => undefined);
  assert.equal(fine, 7, 'the companion: a round that finished is returned');
  assert.deepEqual(seen, [7]);
});

// ───────────────── the review of PR #1047: T3, T38, T39 ─────────────────

test('A3-T29 (T3): a Stop saved in another window while the door is awaited starts no call, though the generation did not move', async () => {
  const w = world({ hosts: ['a', 'b'], door: (_host, asked, self) => { if (asked === 0 && self.admits.length === 1) { self.control.stopped = true; } return READ_OK; } });

  const result = await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.deepEqual(w.calls, [], 'an ssh call started after the Stop was read again');
  assert.equal(result.abandoned, true);
});

test('A3-T19 (T39): a host whose last terminal closed while the door was awaited is not called', async () => {
  const w = world({
    hosts: ['a', 'b'],
    door: (host, asked, self) => {
      if (host === 'a' && asked === 0) {
        self.control.terminals = self.control.terminals.filter((one) => !one.name.includes(' a '));
      }

      return READ_OK;
    },
  });

  await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.deepEqual(callsOf(w), ['b N'], 'a host with no terminal was called');
});

test('A3-T29 (T38): a generation that moved while the Stop was read is seen before the door is asked', async () => {
  const before = world({ hosts: ['a'], stopped: (self) => { self.control.generation += 1; return Promise.resolve(false); } });
  const first = await roundOf(before, new Map(), 0, 'timer', OFF);
  assert.deepEqual(before.admits, [], 'the door was asked after a Forget that came while the Stop was read (before the call)');
  assert.equal(first.abandoned, true);
});

test('A3-T29 (T38b): the same in the check after the call: the door would write an automatic row for a host a person just took away', async () => {
  const after = world({ hosts: ['a'], stopped: (self) => { if (self.calls.length >= 1) { self.control.generation += 1; } return Promise.resolve(false); } });
  const second = await roundOf(after, new Map(), 0, 'timer', OFF);
  assert.deepEqual(after.admits, ['a'], 'the door was asked for the result after a Forget that came while the Stop was read (the check)');
  assert.equal(second.memory.get('a')?.reading, undefined);
  assert.deepEqual(after.dropped, ['a']);
});

test('A3-T22 (T34): the list is drawn from the news of each host as it lands, not from the last round\'s memory', async () => {
  let atB: Memory | undefined;
  const w = world({
    hosts: ['a', 'b'],
    hold: (host, _mode, self) => {
      if (host === 'b') {
        atB = self.landed.memories.at(-1);
      }

      return undefined;
    },
  });

  await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.notEqual(atB?.get('a')?.reading, undefined, 'host a\'s reading was not handed to the list while host b was still being read');
  assert.equal(atB?.has('b'), false, 'a host that was not read yet had a reading');
  assert.ok(w.landed.memories.every((one) => one !== undefined), 'a landing came with no memory');
  assert.notEqual(w.landed.memories.at(-1)?.get('b')?.reading, undefined);

  // a host the door did not allow lands too, with its kind
  const door = world({ hosts: ['a'], door: () => 'no' });
  await roundOf(door, new Map(), 0, 'timer', OFF);
  assert.equal(door.landed.memories.at(-1)?.get('a')?.admission, 'refused');
});
