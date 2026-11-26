import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { AccountsDriver, CAN_BE_SHOWN, answerCanBeShown } from '../remoteAccountsDriver';
import type { DriverWorld } from '../remoteAccountsDriver';
import { FIRST_DELAY_MS, ROUND_GAP_MS, STATE_KEY, USAGE_POLICY, armFor, nextAfterRound, stateMerge } from '../remoteAccountsRound';
import type { AccountsRead } from '../remoteAccountsReply';
import type { HostStanding } from '../remoteHosts';
import type { DoorAnswer } from '../remotePermission';
import type { LiveTerminal } from '../terminalSource';
import { OFF, callsOf, claudeFound, roundOf, terminal, world } from './roundFakes';

/**
 * E2.S4's tests of the round under the permission (E2S4-T1 … T8): the window's driver, with a fake clock, fake timers, a fake door and a
 * fake store. The driver has NO seam that asks a person: the world is a proxy that throws on any member the driver is not given (T5).
 */

interface Rig {
  readonly world: DriverWorld;
  readonly driver: AccountsDriver;
  readonly calls: string[];
  readonly mays: string[];
  readonly timers: { ms: number; run: () => void; cleared: boolean }[];
  readonly landed: (string | undefined)[];
  readonly notices: number[];
  readonly control: {
    permitted: boolean; stopped: boolean; terminals: LiveTerminal[]; standings: HostStanding[]; row: unknown; now: number; hold: (() => Promise<void>) | undefined;
    refused: Set<string>; failing: boolean;
  };
}

const U0 = '2026-11-26T10:00:00.000Z';
const standing = (host: string, answer: 'yes' | 'no', how: 'person' | 'auto' = 'person'): HostStanding => ({ host, answer, how, askedUtc: U0 });

function rig(init: { hosts?: readonly string[]; permitted?: boolean; test?: boolean } = {}): Rig {
  const calls: string[] = [];
  const mays: string[] = [];
  const timers: { ms: number; run: () => void; cleared: boolean }[] = [];
  const landed: (string | undefined)[] = [];
  const notices: number[] = [];
  const control: Rig['control'] = {
    permitted: init.permitted ?? false, stopped: false, terminals: (init.hosts ?? []).map((host) => terminal(host)), standings: [], row: undefined, now: 1_000_000,
    hold: undefined, refused: new Set(), failing: false,
  };
  const given: DriverWorld = {
    test: init.test ?? false,
    live: () => control.terminals,
    permitted: () => Promise.resolve(control.permitted),
    may: (host): Promise<DoorAnswer> => {
      mays.push(host);

      return Promise.resolve(control.refused.has(host) ? 'no' : !control.permitted ? 'no-permission' : control.stopped ? 'stopped' : 'yes');
    },
    standings: () => Promise.resolve(control.standings),
    call: async (host) => {
      calls.push(host);
      await control.hold?.();

      return { kind: 'answered', code: 0, stdout: JSON.stringify({ host }) };
    },
    parse: (run): AccountsRead => claudeFound(`${(JSON.parse(run.stdout) as { host: string }).host}@example.test`),
    now: () => control.now,
    setTimer: (ms, run) => { const one = { ms, run, cleared: false }; timers.push(one); return one; },
    clearTimer: (handle) => { (handle as { cleared: boolean }).cleared = true; },
    landed: (host) => { landed.push(host); },
    log: () => undefined,
    wsl: () => undefined,
    readState: () => control.row,
    door: {
      stopRound: () => Promise.resolve({ kind: 'written', row: undefined, evicted: [] }),
      startRound: () => Promise.resolve({ kind: 'written', row: undefined, evicted: [] }),
      roundStop: () => Promise.resolve(control.stopped ? 'stopped' : 'running'),
      commitKey: <C, R>(key: string, change: C, merge: (stored: unknown, change: C) => { next: { value: unknown } | undefined; result: R }): Promise<R> => {
        assert.equal(key, STATE_KEY);
        const { next, result } = merge(control.row, change);
        if (next !== undefined) {
          control.row = next.value;
        }

        return Promise.resolve(result);
      },
    },
    canBeShown: () => { notices.push(1); },
  };
  // T5: the driver is handed no question. Any member it reaches for that is not in the world throws.
  const world = new Proxy(given, { get: (target, name) => { if (name in target) { return Reflect.get(target, name) as unknown; } throw new Error(`the driver reached for ${String(name)}`); } });
  const driver = new AccountsDriver(world);

  return { world, driver, calls, mays, timers, landed, notices, control };
}

const live = (r: Rig): { ms: number; run: () => void }[] => r.timers.filter((one) => !one.cleared);

test('E2S4-T1: no permission, no call, no door question and no timer — by 100 ticks and by every press; the acceptance of entry 2.3', async () => {
  const hosts = ['a', 'b', 'c', 'wsl', ...Array.from({ length: 16 }, (_x, n) => `h${n}`)];
  const r = rig({ hosts, permitted: false });
  r.control.standings = [standing('a', 'yes'), standing('b', 'yes', 'auto'), standing('c', 'no')];
  r.control.refused.add('c');

  await r.driver.changed();
  await r.driver.rearm();
  for (let n = 0; n < 100; n += 1) {
    await r.driver.runNow('timer');
  }
  await r.driver.refresh();
  await r.driver.openStarts();
  await r.driver.start();
  await r.driver.runNow('refresh');
  await r.driver.runNow('open');

  assert.deepEqual(r.calls, [], 'a host was called with no permission');
  assert.deepEqual(r.mays, [], 'the door was asked with no permission');
  assert.deepEqual(live(r), [], 'a timer was armed with no permission');
  assert.equal(await r.driver.armedNow(), false);
});

test('E2S4-T2: after Allow a round starts at once; one first call for each host that is not refused, none to the refused one and none to wsl; a later terminal is read next round', async () => {
  const r = rig({ hosts: ['a', 'b', 'c', 'wsl'], permitted: false });
  r.control.refused.add('c');
  r.control.standings = [standing('c', 'no')];
  await r.driver.changed();
  assert.deepEqual(r.calls, []);

  r.control.permitted = true;
  await r.driver.changed();
  await waitIdle(r);

  assert.deepEqual([...r.calls].sort(), ['a', 'b'], 'the first round did not call exactly the hosts that are not refused, wsl left out');
  assert.equal(live(r).length, 1, 'the next round is not set');
  assert.equal(live(r)[0]?.ms, ROUND_GAP_MS, 'the next round is not 10 minutes from the end');

  r.control.terminals = [...r.control.terminals, terminal('d')];
  await r.driver.rearm();
  r.calls.length = 0;
  await r.driver.runNow('timer');
  assert.deepEqual([...r.calls].sort(), ['a', 'b', 'd'], 'a terminal of a new host was not read in the next round with no question');
});

test('E2S4-T3: a withdrawal during a round abandons it, publishes nothing, empties the memory and clears the timer; the next tick calls nothing', async () => {
  const r = rig({ hosts: ['a', 'b'], permitted: true });
  await r.driver.changed();
  let release: () => void = () => undefined;
  r.control.hold = () => new Promise<void>((resolve) => { release = resolve; });
  const running = r.driver.runNow('refresh');
  await until(() => r.calls.length === 1);
  const landedBefore = r.landed.length;

  r.control.permitted = false;
  await r.driver.changed();
  assert.equal(r.driver.memory().size, 0, 'the memory was not emptied at once');
  r.control.hold = undefined;
  release();
  const result = await running;

  assert.equal(result?.abandoned, true, 'the round that was running was not abandoned');
  assert.equal(r.calls.length, 1, 'the round went on to the next host after the withdrawal');
  assert.equal(r.driver.memory().size, 0, 'a result of the abandoned round was published');
  assert.deepEqual(r.landed.slice(landedBefore).filter((one) => one !== undefined), [], 'the abandoned round posted a block');
  assert.deepEqual(live(r), [], 'the timer was not cleared');
  r.calls.length = 0;
  await r.driver.runNow('timer');
  assert.deepEqual(r.calls, [], 'a tick after the withdrawal called a host');
});

test('E2S4-T4: a stopped round stays stopped through an Allow; Start after it runs a round', async () => {
  const r = rig({ hosts: ['a'], permitted: false });
  r.control.stopped = true;
  await r.driver.changed();
  r.control.permitted = true;
  await r.driver.changed();
  await waitIdle(r);
  assert.deepEqual(r.calls, [], 'an Allow started a round that was stopped');
  assert.deepEqual(live(r), [], 'an Allow armed a timer on a stopped round');

  r.control.stopped = false;
  assert.equal(await r.driver.start(), true);
  await waitIdle(r);
  assert.deepEqual(r.calls, ['a'], 'Start after a Stop did not run a round');
});

test('E2S4-T5: the timer never asks — the world has no question, and a tick with no permission raises nothing', async () => {
  const r = rig({ hosts: ['a'], permitted: false });
  r.control.row = {};
  await r.driver.changed();
  for (let n = 0; n < 5; n += 1) {
    await r.driver.runNow('timer');
    await r.driver.rearm();
  }
  r.control.permitted = true;
  await r.driver.changed();
  await waitIdle(r);

  // reaching for `ask`, `askPermission`, `askPermissionFirst` or `allow` would have thrown from the proxy
  assert.deepEqual(r.calls, ['a']);
});

test('E2S4-T6: activation with the permission and an eligible terminal arms the first round 30 s later; without the permission no timer', async () => {
  const withIt = rig({ hosts: ['a'], permitted: true });
  await withIt.driver.changed();
  assert.deepEqual(live(withIt).map((one) => one.ms), [FIRST_DELAY_MS]);
  assert.deepEqual(withIt.calls, [], 'a round ran at activation instead of 30 s later');

  const without = rig({ hosts: ['a'], permitted: false });
  await without.driver.changed();
  assert.deepEqual(live(without), []);

  const nobody = rig({ hosts: [], permitted: true });
  await nobody.driver.changed();
  assert.deepEqual(live(nobody), [], 'a timer for no terminal');

  const onlyWsl = rig({ hosts: ['wsl'], permitted: true });
  await onlyWsl.driver.changed();
  assert.deepEqual(live(onlyWsl), [], 'a timer for a terminal that is never called');
});

test('E2S4-T7: each place posts its own block while the round runs; the summary counts the places', async () => {
  const r = rig({ hosts: ['a', 'b', 'wsl'], permitted: true });
  await r.driver.changed();
  await r.driver.runNow('refresh');

  assert.deepEqual(r.landed.filter((one): one is string => one !== undefined), ['a', 'b'], 'a place did not post its own block');
  assert.deepEqual(r.driver.summary(), { endedMs: r.control.now, asked: 2, read: 2 });
});

test('E2S4-T8: the one notice — once for each installation, when a Creds terminal is first seen without the permission; not with it; its button opens the tab and decides nothing', async () => {
  const r = rig({ hosts: ['a'], permitted: false });
  await r.driver.changed();
  await r.driver.rearm();
  await r.driver.changed();
  assert.equal(r.notices.length, 1, 'the notice was not shown exactly once');
  assert.equal(typeof (r.control.row as { canBeShownUtc?: string }).canBeShownUtc, 'string', 'its time is not in the row');

  const again = rig({ hosts: ['a'], permitted: false });
  again.control.row = r.control.row;
  await again.driver.changed();
  assert.equal(again.notices.length, 0, 'the notice came again for the same installation');

  const permitted = rig({ hosts: ['a'], permitted: true });
  await permitted.driver.changed();
  assert.equal(permitted.notices.length, 0, 'the notice was shown with the permission');

  const none = rig({ hosts: [], permitted: false });
  await none.driver.changed();
  assert.equal(none.notices.length, 0, 'the notice was shown with no Creds terminal');

  const damaged = rig({ hosts: ['a'], permitted: false });
  damaged.control.row = 'not a row';
  await damaged.driver.changed();
  assert.equal(damaged.notices.length, 0, 'the notice was shown over a row that cannot be read');

  const opened: string[] = [];
  answerCanBeShown(CAN_BE_SHOWN.action, () => { opened.push('tab'); });
  answerCanBeShown(undefined, () => { opened.push('dismissed'); });
  assert.deepEqual(opened, ['tab'], 'a button other than Open opened the tab');
  assert.equal(CAN_BE_SHOWN.code, 'accounts-can-be-shown');
  assert.equal(CAN_BE_SHOWN.title, 'CoAI can show the AI accounts and usage of the hosts of your Creds terminals.');
  assert.equal(CAN_BE_SHOWN.action, 'Open');
  void stateMerge;
});

async function waitIdle(r: Rig): Promise<void> {
  for (let n = 0; n < 50; n += 1) {
    await new Promise<void>((resolve) => { setImmediate(resolve); });
  }
  assert.equal(r.driver.isRunning(), false);
}

async function until(done: () => boolean): Promise<void> {
  for (let n = 0; n < 200 && !done(); n += 1) {
    await new Promise<void>((resolve) => { setImmediate(resolve); });
  }
  assert.ok(done(), 'the awaited state never came');
}

// ───────────────── the round over the door, as the cut's tests carried it ─────────────────

test('E2S4-T1 (carried CUT-T1): a round whose door answers no-permission calls no runner, writes nothing and is abandoned; the scope host wsl is never called or asked', async () => {
  for (const trigger of ['timer', 'refresh', 'open'] as const) {
    const w = world({ hosts: ['hA', 'hB'], door: () => 'no-permission' });
    const result = await roundOf(w, new Map(), 0, trigger);

    assert.deepEqual(callsOf(w), [], `a ${trigger} round called a runner with no permission`);
    assert.equal(result.abandoned, true, `a ${trigger} round with no permission was not abandoned`);
    assert.deepEqual([...result.memory], [], `a ${trigger} round remembered a host`);
    assert.equal(w.admits.length, 1, 'the round asked the door again after the answer that ends it');
  }
  const scope = world({ hosts: ['wsl', 'hA'], door: () => 'yes' });
  await roundOf(scope, new Map(), 0, 'timer', OFF);
  assert.deepEqual(callsOf(scope), ['hA N'], 'a round called wsl, or missed hA');
  assert.deepEqual(scope.admits, ['hA', 'hA'], 'the door was asked about wsl (hA is asked before its call and before its result)');
});

test('E2S4-T1 (carried CUT-T2): no timer is armed without the permission, whatever the terminal or the round did', () => {
  const none = { eligible: true, permitted: false, armed: false, stopped: false, running: false };
  assert.equal(armFor(none), 'clear');
  assert.equal(armFor({ ...none, armed: true }), 'clear', 'a timer stayed after the permission went');
  assert.deepEqual(nextAfterRound({ stopped: false, eligible: true, permitted: false, endedMs: 1_000 }), { kind: 'clear' }, 'the end of a round set the next one with no permission');
  assert.equal(armFor({ ...none, permitted: true }), 'arm-first', 'the companion: with the permission the same terminal arms the first round');
});

test('E2S4-T3 (the round): a door that answers no-permission in the middle of a round ends it; the result already in flight is not published', async () => {
  const w = world({ hosts: ['a', 'b'], door: (_host, asked) => (asked === 0 ? 'yes' : 'no-permission') });
  const result = await roundOf(w);

  assert.deepEqual(callsOf(w), ['a N'], 'the round went on after the permission was gone');
  assert.equal(result.abandoned, true);
  assert.equal(result.memory.get('a')?.reading, undefined, 'a result checked after a withdrawal was published');
});

test('E2S4-T7 (the round): each place lands with its own host, and the result counts the places', async () => {
  const w = world({ hosts: ['a', 'b', 'c'], door: (host) => (host === 'c' ? 'no' : 'yes') });
  const result = await roundOf(w, new Map(), 0, 'timer', OFF);

  assert.equal(result.asked, 2, 'asked is not the places called');
  assert.equal(result.read, 2, 'read is not the places that answered');
  assert.equal(w.landed.count, 3, 'each place, the refused one too, lands once');
});

test('E2S4-T10: the shipped usage policy is still off', () => {
  assert.deepEqual(USAGE_POLICY, { kind: 'off' });
});
