import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HostChange, HostStanding, choicesOf, commit, consentOf, dayOf, hostMerge, listedHosts, readableOf, standingsOf } from '../remoteHosts';
import type { KeeperSeams } from '../remoteKeeper';
import { EMPTY_FILE, RefusalsRead, refusalsStore, textOf } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, instant, row, view, windowOver } from './hostsFakes';

/**
 * The store of the hosts (E1.S1): the rows, the merge, the cap, the damage rule, the keeper over both. Pure: a fake `globalState` that
 * several windows share, and a fake folder for the side's refusals file.
 */

const NOW = '2026-11-24T10:00:00.000Z';
const noFile: RefusalsRead = { kind: 'file', file: EMPTY_FILE, waiting: [] };
const fileWith = (...hosts: string[]): RefusalsRead => ({ kind: 'file', file: { ...EMPTY_FILE, gen: 1, by: 'x', refused: hosts.map((host) => ({ host, utc: NOW })) }, waiting: [] });
const damagedFile: RefusalsRead = { kind: 'damaged' };

const merged = (stored: unknown, change: HostChange, now = NOW) => hostMerge(now)(stored, change);

test('A1-T1: a row says how it came, and a marker nobody knows is not read', () => {
  const rows = [row('a', 'yes', NOW, 'auto'), row('b', 'yes'), row('c', 'no', 'x', undefined), { ...row('d', 'yes'), extra: 1 }];
  const standings = standingsOf(rows);
  assert.deepEqual(standings.map((one) => [one.host, one.how]), [['a', 'auto'], ['b', 'person'], ['c', 'person'], ['d', 'person']]);
  const next = merged(rows, { kind: 'auto', host: 'z' });
  const written = (next.next?.value ?? []) as unknown[];
  assert.ok(rows.every((one) => written.includes(one)), 'a row of another host was rewritten, so its extra keys were lost');
  for (const bad of [[row('h', 'yes', NOW, 'auto' as never), { ...row('h', 'yes'), how: 'later' }], [{ ...row('h', 'no'), how: 'auto' }], [{ ...row('h', 'yes'), how: 'later' }]]) {
    assert.equal(consentOf(noFile, bad, 'h', true), 'damaged', `a row with a marker this build does not know was read as a person's "yes": ${JSON.stringify(bad)}`);
  }
  assert.equal(consentOf(noFile, undefined, 'h', true), 'ask');
  assert.equal(consentOf(noFile, [], 'h', true), 'ask');
});

test('A1-T2: one function decides, over the stores, the setting and the files', () => {
  const stores: readonly (readonly [string, unknown, 'yes' | 'no' | 'ask' | 'damaged', 'yes' | 'no' | 'ask' | 'damaged'])[] = [
    ['empty', [], 'ask', 'ask'],
    ['never written', undefined, 'ask', 'ask'],
    ['a person yes', [row('h', 'yes')], 'yes', 'yes'],
    ['an automatic yes', [row('h', 'yes', NOW, 'auto')], 'yes', 'ask'],
    ['a no', [row('h', 'no')], 'no', 'no'],
    ['two rows, one a no', [row('h', 'yes'), row('h', 'no')], 'no', 'no'],
    ['junk', 'junk', 'damaged', 'damaged'],
    ['a bad entry beside a yes', [row('h', 'yes'), 'junk'], 'damaged', 'damaged'],
    ['a bad entry beside a no', [row('h', 'no'), 'junk'], 'no', 'no'],
  ];
  for (const [name, value, on, off] of stores) {
    assert.equal(consentOf(noFile, value, 'h', true), on, `${name}, setting on`);
    assert.equal(consentOf(noFile, value, 'h', false), off, `${name}, setting off`);
    assert.equal(consentOf(fileWith('h'), value, 'h', true), 'no', `${name}: a refusal in the file is a no`);
    assert.equal(consentOf(fileWith('h'), value, 'h', false), 'no');
    assert.equal(consentOf(damagedFile, value, 'h', true), name.includes('no') && !name.includes('yes') ? 'no' : 'damaged', `${name}, a damaged file`);
  }
});

test('A1-T4: who may change which row — every cell of the merge', () => {
  const none: unknown = [];
  const auto = [row('h', 'yes', NOW, 'auto')];
  const yes = [row('h', 'yes')];
  const no = [row('h', 'no')];
  const rowOf = (outcome: ReturnType<typeof merged>): Record<string, unknown> | undefined => (outcome.next?.value as Record<string, unknown>[] | undefined)?.[0];
  const person = (answer: 'yes' | 'no'): HostChange => ({ kind: 'person', host: 'h', answer });

  for (const stored of [none, auto, yes, no]) {
    assert.equal(rowOf(merged(stored, person('no')))?.['answer'], 'no');
    assert.equal(rowOf(merged(stored, person('yes')))?.['answer'], 'yes');
    assert.equal(rowOf(merged(stored, person('yes')))?.['how'], undefined, 'a person\'s yes carries how');
  }
  assert.deepEqual(rowOf(merged(none, { kind: 'auto', host: 'h' })), { host: 'h', answer: 'yes', askedUtc: NOW, how: 'auto' });
  for (const stored of [auto, yes, no]) {
    const outcome = merged(stored, { kind: 'auto', host: 'h' });
    assert.equal(outcome.next, undefined, 'an automatic change wrote over a row');
    assert.equal(outcome.result.kind, 'kept');
  }
  assert.equal(merged(no, { kind: 'auto', host: 'h' }).result.kind === 'kept' && (merged(no, { kind: 'auto', host: 'h' }).result as { row: unknown }).row, no[0], 'a no did not come back as the same object');
  for (const stored of [auto, yes, no]) {
    assert.deepEqual(merged(stored, { kind: 'forget', host: 'h' }).next, { value: [] });
  }
  assert.equal(merged(none, { kind: 'forget', host: 'h' }).next, undefined);
  assert.equal(merged(none, { kind: 'forget', host: 'h' }).result.kind, 'kept');
});

test('A1-T7: the cap drops no refusal and no person\'s row for an automatic one', () => {
  const rows: Record<string, unknown>[] = Array.from({ length: 99 }, (_, n) => row(`h${n}`, 'yes', `2026-10-${String(10 + (n % 20)).padStart(2, '0')}T00:00:00.000Z`));
  rows.push(row('oldest', 'no', '2026-01-01T00:00:00.000Z'));
  const out = merged(rows, { kind: 'person', host: 'new', answer: 'yes' });
  const next = out.next?.value as { host: string }[];
  assert.equal(next.length, 100);
  assert.ok(next.some((one) => one.host === 'oldest'), 'the list dropped a “Not this host” to make room');
  assert.ok(!next.some((one) => one.host === 'h0'), 'the oldest person\'s yes (the host name breaks the tie) did not go');
  assert.deepEqual(out.result.kind === 'written' && out.result.evicted, ['h0']);

  const autos = [...Array.from({ length: 99 }, (_, n) => row(`a${n}`, 'yes', `2026-10-${String(10 + (n % 20)).padStart(2, '0')}T00:00:00.000Z`, 'auto')), row('keep', 'no')];
  const auto = merged(autos, { kind: 'auto', host: 'fresh' });
  assert.equal(auto.result.kind, 'written');
  assert.equal((auto.next?.value as unknown[]).length, 100);

  const people = Array.from({ length: 100 }, (_, n) => row(`p${n}`, 'yes'));
  assert.equal(merged(people, { kind: 'auto', host: 'new' }).result.kind, 'full', 'an automatic row pushed out a person\'s answer');
  assert.equal(merged(people, { kind: 'auto', host: 'new' }).next, undefined);
  const refusals = Array.from({ length: 100 }, (_, n) => row(`r${n}`, 'no'));
  assert.equal(merged(refusals, { kind: 'person', host: 'x', answer: 'no' }).result.kind, 'full');
  assert.equal(merged(refusals, { kind: 'person', host: 'x', answer: 'yes' }).result.kind, 'full');
  assert.equal(merged(people, { kind: 'person', host: 'p5', answer: 'no' }).result.kind, 'written', 'a changed answer of a known host dropped something');
});

test('A1-T8: a list that cannot be read allows nobody, and a person\'s answer mends it', async () => {
  for (const junk of ['junk', null, 7, {}, [null], ['h'], [{ host: 'h' }], [row('z', 'yes'), 'junk'], [row('x', 'no'), { host: 'y', answer: 'maybe', askedUtc: NOW }], [{ ...row('z', 'yes'), how: 'later' }]]) {
    const backing = new Backing();
    backing.values.set(KEY, junk);
    const w = windowOver(backing, new FakeDisk());
    assert.equal((await w.keeper.admit('newhost')).kind, 'damaged', `a damaged list let a new host be allowed by itself: ${JSON.stringify(junk)}`);
    for (const change of [{ kind: 'person', host: 'q', answer: 'yes' }, { kind: 'forget', host: 'q' }, { kind: 'auto', host: 'q' }] as HostChange[]) {
      assert.equal((await w.keeper.commit(change)).kind, 'damaged');
    }
    assert.equal(backing.values.get(KEY), junk, 'a list that could not be read was written over');
  }
  const backing = new Backing();
  backing.values.set(KEY, [row('z', 'yes'), 'junk', row('x', 'no')]);
  const w = windowOver(backing, new FakeDisk());
  assert.equal((await w.keeper.admit('x')).kind, 'refused');
  assert.equal((await w.keeper.admit('z')).kind, 'damaged');
  const done = await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
  assert.equal(done.kind, 'written', 'the answer that repaired the list was not kept');
  const stored = backing.values.get(KEY) as { host: string; answer: string }[];
  assert.deepEqual(stored.map((one) => `${one.host}:${one.answer}`), ['x:no', 'q:no', 'z:yes'].sort((a, b) => stored.findIndex((s) => `${s.host}:${s.answer}` === a) - stored.findIndex((s) => `${s.host}:${s.answer}` === b)));
  assert.equal(readableOf(stored).damaged, false);
  assert.equal((await w.keeper.admit('z')).kind, 'read', 'the mended list does not work');
  // Over 100 readable refusals and a bad entry a mend for a new host is `full`.
  const full = [...Array.from({ length: 100 }, (_, n) => row(`r${n}`, 'no')), 'junk'];
  assert.equal(merged(full, { kind: 'mend', host: 'new', answer: 'yes' }).result.kind, 'full');
  assert.equal(merged(full, { kind: 'mend', host: 'r1', answer: 'forget' }).result.kind, 'written');
  // The list shown over damage
  assert.deepEqual(listedHosts([row('z', 'yes'), 'junk'], ['z', 'q', 'q']).map((one) => [one.host, one.standing === undefined]), [['z', false], ['q', true]]);
  assert.deepEqual(listedHosts([row('z', 'yes')], ['q']).map((one) => one.host), ['z']);
  assert.equal((await windowOver(backing, new FakeDisk()).keeper.admit('z')).kind, 'read', 'a readable list does not allow a known host');
});

test('A1-T3: a "no" against an automatic "yes", in both orders, with a stale window', async () => {
  for (const mode of ['one-key', 'whole-state'] as const) {
    const backing = new Backing();
    const disk = new FakeDisk();
    const a = windowOver(backing, disk, { mode });
    const b = windowOver(backing, disk, { mode });
    assert.deepEqual(await b.keeper.admit('h'), { kind: 'read', how: 'auto', allowedNow: true });
    a.view.sync();
    await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
    assert.deepEqual(disk.refusedHosts(), ['h']);
    const updates = b.view.updates.length;
    assert.equal((await b.keeper.admit('h')).kind, 'refused', 'a "no" given in another window did not stop the second call');
    assert.equal(b.view.updates.length, updates, 'a stale window wrote while the host was refused');
    // (c) B is stale: it holds a list from before A's write.
    const c = windowOver(new Backing(), disk, { mode });
    assert.equal((await c.keeper.admit('h')).kind, 'refused', 'the store lost “Not this host” for h and the host was read');
    const other = await c.keeper.admit('g');
    assert.equal(other.kind, 'read');
    assert.equal((await c.keeper.admit('h')).kind, 'refused');
    assert.equal((await a.keeper.admit('h')).kind, 'refused');
    // (e) two refusals, each window stale to the other
    const d = windowOver(backing, disk, { mode });
    const e = windowOver(backing, disk, { mode });
    await d.keeper.commit({ kind: 'person', host: 'x', answer: 'no' });
    await e.keeper.commit({ kind: 'person', host: 'y', answer: 'no' });
    assert.ok(disk.refusedHosts().includes('x') && disk.refusedHosts().includes('y'), 'one of two refusals was lost');
    for (const w of [d, e]) {
      assert.equal((await w.keeper.admit('x')).kind, 'refused');
      assert.equal((await w.keeper.admit('y')).kind, 'refused');
    }
  }
});

test('A1-T5: no list is handed to a write, and every write is in the step of its read', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const w = windowOver(backing, disk);
  await w.keeper.admit('a');
  await w.keeper.commit({ kind: 'person', host: 'b', answer: 'yes' });
  await w.keeper.commit({ kind: 'person', host: 'c', answer: 'no' });
  await w.keeper.commit({ kind: 'forget', host: 'b' });
  await w.keeper.commitKey('coai.terminalAccountsState', { stop: 'T' }, (stored, change) => ({ next: { value: { ...(stored as object | undefined), ...change } }, result: 1 }));
  assert.ok(w.view.updates.length >= 4);
  assert.ok(w.view.updates.every((one) => one.inStep), 'the store was written in a later step than it was read');
});

test('A1-T6: a person\'s press lifts a refusal from any window of the side, and nobody writes it again', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  // (a) the copy is lost behind A's back: still refused, and nobody writes the row again
  backing.values.set(KEY, []);
  a.view.sync();
  b.view.sync();
  const before = a.view.updates.length;
  assert.equal((await a.keeper.admit('h')).kind, 'refused');
  assert.equal((await b.keeper.admit('h')).kind, 'refused', 'a “Not this host” whose copy was lost was read');
  assert.equal(a.view.updates.length, before, 'a no row was written again');
  // (b) Read in B, A catches up first
  const gen = disk.file()?.gen ?? 0;
  assert.equal((await b.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' })).kind, 'written');
  assert.deepEqual(disk.refusedHosts(), []);
  assert.ok((disk.file()?.gen ?? 0) > gen);
  a.view.sync();
  assert.deepEqual(await a.keeper.admit('h'), { kind: 'read', how: 'person', allowedNow: false });
  // (c) Forget in B, the "no" does NOT come back; the setting on writes an automatic row
  await b.keeper.commit({ kind: 'person', host: 'k', answer: 'no' });
  a.view.sync();
  b.view.sync();
  await b.keeper.commit({ kind: 'forget', host: 'k' });
  a.view.sync();
  assert.equal((await a.keeper.admit('k')).kind, 'read');
  // (f) a lift that the file does not take: unwritten, the list untouched, refused
  await a.keeper.commit({ kind: 'person', host: 'm', answer: 'no' });
  disk.failReplace = true;
  const lift = await a.keeper.commit({ kind: 'person', host: 'm', answer: 'yes' });
  assert.equal(lift.kind, 'unwritten');
  assert.equal((backing.values.get(KEY) as { host: string; answer: string }[]).find((one) => one.host === 'm')?.answer, 'no', 'the "no" row of the list was touched');
  disk.failReplace = false;
  assert.equal((await a.keeper.admit('m')).kind, 'refused');
  // (g) a damaged list: refused in the file, every other host damaged
  backing.values.set(KEY, 'junk');
  a.view.sync();
  assert.equal((await a.keeper.admit('m')).kind, 'refused');
  assert.equal((await a.keeper.admit('other')).kind, 'damaged');
});

test('A1-T9 and A1-T10: the store, the file and the setting are read again before every call', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.equal((await a.keeper.admit('h')).kind, 'read');
  b.view.sync();
  await b.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal((await a.keeper.admit('h')).kind, 'refused', 'a “no” given between two calls did not stop the second');
  // the setting
  assert.deepEqual(await a.keeper.admit('n1'), { kind: 'read', how: 'auto', allowedNow: true });
  assert.deepEqual(await a.keeper.admit('n1'), { kind: 'read', how: 'auto', allowedNow: false });
  a.setting.on = false;
  assert.equal((await a.keeper.admit('n1')).kind, 'not-asked', 'an automatic row counted as an answer with the setting off');
  assert.equal((await a.keeper.admit('n2')).kind, 'not-asked');
  assert.equal((backing.values.get(KEY) as { host: string }[]).some((one) => one.host === 'n2'), false, 'a row was written with the setting off');
  await a.keeper.commit({ kind: 'person', host: 'n3', answer: 'yes' });
  assert.equal((await a.keeper.admit('n3')).kind, 'read', 'the setting took back a person\'s yes');
  a.setting.on = true;
  assert.deepEqual(await a.keeper.admit('n1'), { kind: 'read', how: 'auto', allowedNow: false });
});

test('A1-T10: the row is stored before the answer is given', async () => {
  const backing = new Backing();
  const v = view(backing);
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const slow = { ...v, update: async (key: string, value: unknown) => { await gate; await v.update(key, value); } };
  const seen: string[] = [];
  const done = commit(slow, KEY, { kind: 'auto', host: 'h' } as HostChange, hostMerge(NOW)).then(() => seen.push('resolved'));
  await Promise.resolve();
  assert.deepEqual(seen, [], 'the host was answered before its row was stored');
  release();
  await done;
  assert.equal(backing.values.has(KEY), true);
});

test('A1-T13: Forget, and Not this host on an automatic row', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const w = windowOver(backing, disk);
  await w.keeper.admit('h');
  await w.keeper.commit({ kind: 'forget', host: 'h' });
  assert.deepEqual(await w.keeper.admit('h'), { kind: 'read', how: 'auto', allowedNow: true });
  await w.keeper.commit({ kind: 'forget', host: 'h' });
  w.setting.on = false;
  assert.equal((await w.keeper.admit('h')).kind, 'not-asked', 'Forget left the host allowed');
  w.setting.on = true;
  await w.keeper.admit('h');
  await w.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  const rows = (backing.values.get(KEY) as Record<string, unknown>[]).filter((one) => one['host'] === 'h');
  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.['how'], undefined);
  assert.equal((await w.keeper.admit('h')).kind, 'refused');
});

test('A1-T16: the commit is generic', async () => {
  const backing = new Backing();
  const v = view(backing);
  const merge = (stored: unknown, change: { stop: string }): { next: { value: unknown } | undefined; result: unknown } => ({ next: { value: { was: stored, ...change } }, result: stored });
  backing.values.set('k', 'later');
  v.sync();
  await commit(v, 'k', { stop: 'T' }, merge);
  assert.deepEqual(backing.values.get('k'), { was: 'later', stop: 'T' }, 'the state row was written from a value read earlier');
  const none = await commit(v, 'k', { stop: 'T' }, () => ({ next: undefined, result: 'x' }));
  assert.equal(none, 'x');
  assert.equal(v.updates.length, 1);
});

test('A1-T17: a person\'s answer is stamped after the row it replaces', () => {
  const future = '2026-11-24T11:00:00.000Z';
  const out = merged([row('h', 'yes', future)], { kind: 'person', host: 'h', answer: 'no' });
  assert.equal((out.next?.value as { askedUtc: string }[])[0]?.askedUtc, '2026-11-24T11:00:00.001Z', 'an older “yes” looked newer than the “no” that replaced it');
  const bad = merged([row('h', 'yes', 'x')], { kind: 'person', host: 'h', answer: 'no' });
  assert.equal((bad.next?.value as { askedUtc: string }[])[0]?.askedUtc, NOW);
  const auto = merged([], { kind: 'auto', host: 'h' }, NOW);
  assert.equal((auto.next?.value as { askedUtc: string }[])[0]?.askedUtc, NOW);
});

test('A1-T19: the refusing window is gone, and a stale window writes', async () => {
  for (const viaKey of [false, true]) {
    const backing = new Backing();
    const disk = new FakeDisk();
    const a = windowOver(backing, disk, { mode: 'whole-state' });
    const b = windowOver(backing, disk, { mode: 'whole-state' });
    await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
    // A is disposed; B is stale and writes the whole object
    if (viaKey) {
      await b.keeper.commitKey('coai.terminalAccountsState', { autoNoticed: 'T2' }, () => ({ next: { value: { autoNoticed: 'T2' } }, result: 1 }));
    } else {
      assert.equal((await b.keeper.admit('g')).kind, 'read');
    }
    assert.equal((backing.values.get(KEY) as unknown[] | undefined)?.some((one) => (one as { host: string }).host === 'h') ?? false, false, 'the stale write did not lose the copy, so this test proves nothing');
    const calls: string[] = [];
    for (const reloaded of [windowOver(backing, disk, { mode: 'whole-state' }), windowOver(backing, disk, { mode: 'whole-state', on: false })]) {
      const admission = await reloaded.keeper.admit('h');
      if (admission.kind !== 'refused') {
        calls.push('h was read');
      }
    }
    assert.deepEqual(calls, [], 'a “Not this host” was erased for good by a stale window after the window that gave it was closed: h was allowed by itself and read');
    assert.deepEqual(disk.refusedHosts(), ['h']);
  }
});

test('A1-T20: the Stop survives a stale window\'s notice write', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  assert.equal((await a.keeper.stopRound()).kind, 'written');
  await a.keeper.commitKey('coai.terminalAccountsState', { stop: 'T' }, () => ({ next: { value: { stoppedUtc: 'T' } }, result: 1 }));
  await b.keeper.commitKey('coai.terminalAccountsState', { n: 'T2' }, () => ({ next: { value: { autoNoticed: 'T2' } }, result: 1 }));
  assert.equal(((backing.values.get('coai.terminalAccountsState') as { stoppedUtc?: string }).stoppedUtc), undefined, 'the fixture did not lose the copy');
  for (const reloaded of [windowOver(backing, disk, { mode: 'whole-state' }), b]) {
    assert.equal(await reloaded.keeper.roundStop(), 'stopped', 'a stale window\'s notice write started a stopped round');
  }
  await b.keeper.startRound();
  assert.equal(await a.keeper.roundStop(), 'running');
});

test('A1-T24: a refusal the file did not take is not reported as kept, and the side contacts nobody', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  const a = windowOver(backing, disk);
  const given = a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(a.view.updates.length, 1, 'a “Not this host” had no copy while its write waited');
  assert.deepEqual(await given, { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: true }]);
  assert.equal(disk.markers().length, 1);
  assert.equal(await a.keeper.held(), 1);
  assert.equal((await a.keeper.admit('h')).kind, 'unwritten');
  await a.keeper.commit({ kind: 'person', host: 'g', answer: 'yes' }).catch(() => undefined);
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten', 'a window that could not save a refusal went on reading other hosts');
  assert.equal(await a.keeper.consent('h'), 'no');
  assert.equal(await a.keeper.consent('g'), 'damaged');
  disk.now += 3_600_000;
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten', 'a lock was taken over by its age');
  disk.ownerAlive = false;
  assert.equal((await a.keeper.admit('g')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  assert.equal(disk.markers().length, 0);
  assert.equal(await a.keeper.held(), 0);
  assert.equal((await a.keeper.admit('h')).kind, 'refused');
  assert.equal(a.unkept.length, 1, 'the notice was raised more than once');
  // The Stop
  disk.failReplace = true;
  const stop = await a.keeper.stopRound();
  assert.deepEqual(stop, { kind: 'unwritten', why: 'disk' });
  const b = windowOver(backing, disk);
  assert.equal(await b.keeper.roundStop(), 'stopped', 'the marker did not stop a second window');
});

test('A1-T26: a refusal the file did not take stops the SIDE and outlives the window that took the press', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  // A is disposed. B is stale and writes a notice time: the copy is gone.
  await b.keeper.commitKey('coai.terminalAccountsState', {}, () => ({ next: { value: { n: 1 } }, result: 1 }));
  for (const reloaded of [b, windowOver(backing, disk, { mode: 'whole-state' })]) {
    assert.equal((await reloaded.keeper.admit('h')).kind, 'unwritten', 'a window that could not save a “Not this host” was closed, and another window of the same side read the host');
    assert.equal((await reloaded.keeper.admit('g')).kind, 'unwritten');
    assert.equal(await reloaded.keeper.consent('h'), 'no');
    assert.ok((await reloaded.keeper.standings()).some((one) => one.host === 'h' && one.answer === 'no'));
  }
  assert.ok(b.unkept.some((one) => one.host === 'h' && one.why === 'elsewhere'));
  disk.ownerAlive = false;
  const c = windowOver(backing, disk, { mode: 'whole-state' });
  assert.equal((await c.keeper.admit('g')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  assert.equal(disk.markers().length, 0);
  // A torn marker is removed by nothing but a press
  disk.files.set('unsure-torn.json', '{');
  assert.equal((await c.keeper.admit('g')).kind, 'unwritten');
  assert.equal((await c.keeper.admit('g')).kind, 'unwritten');
  assert.equal(disk.markers().length, 1, 'a note that could not be read was dropped with no press');
  assert.equal((await c.keeper.saveWaiting()).kind, 'written');
  assert.equal(c.keeper.savedTorn(), true);
  assert.equal(disk.markers().length, 0);
  assert.equal(await c.keeper.roundStop(), 'stopped');
  // saveWaiting takes a lock whose owner lives
  const d = windowOver(backing, disk);
  disk.ownerAlive = true;
  disk.lock = { token: 'alive' };
  await d.keeper.commit({ kind: 'person', host: 'p', answer: 'no' });
  assert.equal(disk.markers().length, 1);
  assert.equal((await d.keeper.saveWaiting()).kind, 'written');
  assert.deepEqual(disk.claims.filter((one) => one === 'person').length > 0, true);
  assert.ok(disk.refusedHosts().includes('p'));
  assert.equal(disk.markers().length, 0);
});

test('A1-T25: the copy-in at every read, and two sides', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  backing.values.set(KEY, [row('n1', 'no'), row('n2', 'no', 'x'), row('y1', 'yes'), row('y2', 'yes'), row('y3', 'yes')]);
  const a = windowOver(backing, disk);
  await a.keeper.admit('z');
  assert.deepEqual(disk.refusedHosts(), ['n1', 'n2']);
  const migrated = (disk.file() as { migratedUtc?: string }).migratedUtc;
  assert.ok(migrated !== undefined);
  const replaces = disk.replaces;
  await a.keeper.admit('z2');
  assert.equal(disk.replaces, replaces, 'a read with nothing to copy wrote the file');
  // an older window's "no" after migratedUtc
  const old = windowOver(backing, disk);
  old.view.update(KEY, [...(backing.values.get(KEY) as unknown[]), row('k', 'no')]);
  a.view.sync();
  await a.keeper.admit('any');
  assert.ok(disk.refusedHosts().includes('k'), 'a “Not this host” given in a window of an older build after the migration was never in the file');
  assert.equal((disk.file() as { migratedUtc?: string }).migratedUtc, migrated);
  backing.values.set(KEY, []);
  for (const w of [a, windowOver(backing, disk)]) {
    assert.equal((await w.keeper.admit('k')).kind, 'refused');
  }
  // two sides over one backing
  const other = new FakeDisk();
  const backing2 = new Backing();
  const s1 = windowOver(backing2, disk);
  const s2 = windowOver(backing2, other);
  await s1.keeper.commit({ kind: 'person', host: 'q', answer: 'no' });
  s2.view.sync();
  assert.equal((await s2.keeper.admit('q')).kind, 'refused');
  assert.ok(other.refusedHosts().includes('q'), 'the other side did not copy the refusal into its own file');
  // the Stop is not copied in
  await s1.keeper.stopRound();
  assert.equal(other.file()?.stop, undefined);
});

test('A1-T27: a file that went back is written forward (the union)', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  await a.keeper.admit('x');
  const text = disk.files.get('refused.json') as string;
  await a.keeper.commit({ kind: 'person', host: 'g', answer: 'no' });
  // a stale writer renames an OLDER text over the newer file
  disk.files.set('refused.json', text);
  const decision = await a.keeper.admit('h');
  assert.equal(decision.kind, 'refused');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h'], 'the union was not written back');
  // the file removed
  disk.files.delete('refused.json');
  assert.equal((await a.keeper.admit('g')).kind, 'refused');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h']);
  // a lift that was written over comes back as a refusal (residual 4)
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.deepEqual(disk.refusedHosts(), ['g']);
});

test('A1-T23: a file that cannot be read contacts nobody, and its mend leaves the round stopped', async () => {
  for (const bad of ['not json', '[]', '{}', '{"v":2,"refused":[]}', '{"v":1}', '{"v":1,"gen":1,"by":"x","refused":["h"]}', '{"v":1,"gen":1,"by":"x","refused":[],"stop":5}']) {
    const backing = new Backing();
    backing.values.set(KEY, [row('p', 'yes'), row('n', 'no')]);
    const disk = new FakeDisk();
    disk.files.set('refused.json', bad);
    const w = windowOver(backing, disk);
    assert.equal((await w.keeper.admit('new')).kind, 'damaged', `a file that could not be read let a host be allowed by itself: ${bad}`);
    assert.equal((await w.keeper.admit('p')).kind, 'damaged');
    assert.equal((await w.keeper.admit('n')).kind, 'refused');
    assert.equal(await w.keeper.roundStop(), 'damaged');
    assert.equal((await w.keeper.commit({ kind: 'person', host: 'p', answer: 'yes' })).kind, 'damaged');
    assert.equal(disk.files.get('refused.json'), bad, 'the bytes on the disk changed');
    const mended = await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
    assert.equal(mended.kind, 'written');
    assert.deepEqual(disk.refusedHosts(), ['n', 'q']);
    assert.ok(disk.file()?.stop !== undefined, 'a mend started a round that may have been stopped');
    assert.equal(await w.keeper.roundStop(), 'stopped');
  }
  const disk = new FakeDisk();
  disk.failReads = 1;
  const w = windowOver(new Backing(), disk);
  assert.equal((await w.keeper.admit('a')).kind, 'read', 'a read that fails once made the file damaged');
  const none = new FakeDisk();
  none.failReads = 3;
  assert.equal(await windowOver(new Backing(), none).keeper.roundStop(), 'damaged');
  const unlisted = new FakeDisk();
  unlisted.failMarks = true;
  assert.equal((await windowOver(new Backing(), unlisted).keeper.admit('a')).kind, 'damaged');
  const stopOver = new FakeDisk();
  stopOver.files.set('refused.json', 'junk');
  const s = windowOver(new Backing(), stopOver);
  assert.equal((await s.keeper.stopRound()).kind, 'unwritten');
  assert.equal((await s.keeper.admit('a')).kind, 'unwritten');
  assert.equal((await s.keeper.startRound()).kind, 'damaged');
});

test('words: dayOf and the standing', () => {
  const noon = new Date(2026, 10, 24, 12, 0, 0);
  assert.equal(dayOf(noon.toISOString()), '2026-11-24');
  assert.equal(dayOf('x'), 'an unknown day');
  const standing: HostStanding = { host: 'h', answer: 'yes', how: 'auto', askedUtc: NOW };
  assert.deepEqual(standingsOf([row('h', 'yes', NOW, 'auto')]), [standing]);
});

// ───────────────────────── the sub-cases and the checks the first pass left out ─────────────────────────

const SIDE_STATE = 'coai.terminalAccountsState';

test('A1-T15: the door of the round never asks, and names no member for damage', async () => {
  const base = {
    state: view(new Backing()), key: KEY, refusals: refusalsStore(new FakeDisk(), () => 0, instant), now: () => NOW, allowNew: () => true,
    changed: () => undefined, unkept: () => undefined,
  };
  // @ts-expect-error — a seam that asks a person is not a KeeperSeams
  const withAsk: KeeperSeams = { ...base, ask: async () => 'yes' };
  // @ts-expect-error — a seam for a notice of damage is not a KeeperSeams (a damaged list or file has no notice of its own)
  const withDamaged: KeeperSeams = { ...base, damaged: () => undefined };
  assert.ok(withAsk !== withDamaged);
  assert.ok(!Object.keys(base).some((name) => name.toLowerCase().includes('ask')), 'a member of the seams holds "ask"');
  for (const on of [true, false]) {
    const w = windowOver(new Backing(), new FakeDisk(), { on });
    for (let n = 0; n < 100; n += 1) {
      const admission = await w.keeper.admit(`new${n}`);
      assert.ok(admission.kind === 'read' || admission.kind === 'not-asked' || admission.kind === 'full', `the door answered ${admission.kind}`);
    }
    assert.deepEqual(w.unkept, [], 'the door of the round raised something');
  }
});

test('A1-T3 (b) and (d): no then auto with a fresh view, and a stale commitKey erases the copy', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  b.view.sync();
  assert.equal((await b.keeper.admit('h')).kind, 'refused', '(b) no, then auto, with a fresh view');
  // (d) B is stale again (a window opened before A's write) and writes the state row: the whole object goes out
  const stale = windowOver(new Backing(), disk, { mode: 'whole-state' });
  await stale.keeper.commitKey(SIDE_STATE, { n: 1 }, () => ({ next: { value: { n: 1 } }, result: 1 }));
  backing.values.clear();
  backing.values.set(KEY, []);
  for (const w of [a, b, stale]) {
    w.view.sync();
    assert.equal((await w.keeper.admit('h')).kind, 'refused', '(d) h was read after a stale write of another key');
  }
});

test('A1-T6 (b2), (c2), (d), (e): the sub-cases of a lift inside the lag', async () => {
  // (b2) Read in B while A is stale: A copies the old "no" back into the file (residual 4)
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  b.view.sync();
  await b.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.deepEqual(disk.refusedHosts(), []);
  assert.equal((await a.keeper.admit('h')).kind, 'refused', '(b2) the stale view did not copy the old no in');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  a.view.sync();
  assert.equal((await a.keeper.admit('h')).kind, 'refused');
  assert.equal((await b.keeper.admit('h')).kind, 'refused');
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.equal((await b.keeper.admit('h')).kind, 'read', 'one more Read after the sync did not lift it');
  assert.deepEqual(disk.refusedHosts(), []);
  // (c2) Forget in B, A reads inside the lag
  const backing2 = new Backing();
  const disk2 = new FakeDisk();
  const c = windowOver(backing2, disk2);
  const d = windowOver(backing2, disk2);
  await c.keeper.commit({ kind: 'person', host: 'k', answer: 'no' });
  d.view.sync();
  await d.keeper.commit({ kind: 'forget', host: 'k' });
  assert.equal((await c.keeper.admit('k')).kind, 'refused', '(c2) the refusal did not come back inside the lag');
  // (d) Forget in A with an admit of A queued behind it
  const backing3 = new Backing();
  const disk3 = new FakeDisk();
  const e = windowOver(backing3, disk3);
  await e.keeper.commit({ kind: 'person', host: 'm', answer: 'no' });
  const forget = e.keeper.commit({ kind: 'forget', host: 'm' });
  const next = e.keeper.admit('m');
  await forget;
  assert.deepEqual(await next, { kind: 'read', how: 'auto', allowedNow: true }, '(d) the lift was met half-way by the queued admit');
  assert.deepEqual(disk3.refusedHosts(), []);
  // (e) an old no is put back by a stale whole-state write
  backing3.values.set(KEY, [row('m', 'no')]);
  e.view.sync();
  assert.equal((await e.keeper.admit('m')).kind, 'refused', '(e) the old no put back was read');
  assert.deepEqual(disk3.refusedHosts(), ['m']);
  await e.keeper.commit({ kind: 'person', host: 'm', answer: 'yes' });
  assert.equal((await e.keeper.admit('m')).kind, 'read');
});

test('A1-T9: a refusal that lands after the first read and before the answer still stops the call', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const w = windowOver(backing, disk);
  const inner = w.view;
  const refusing = windowOver(backing, disk);
  // The automatic row's write is the moment another window refuses the host.
  const original = inner.update;
  inner.update = async (key, value) => {
    await original(key, value);
    await refusing.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  };
  assert.equal((await w.keeper.admit('h')).kind, 'refused', 'a “no” given between two calls did not stop the second');
});

test('A1-T10: the admission does not resolve before the automatic row is stored', async () => {
  const backing = new Backing();
  const w = windowOver(backing, new FakeDisk());
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const original = w.view.update;
  w.view.update = async (key, value) => { await gate; await original(key, value); };
  const seen: string[] = [];
  const pending = w.keeper.admit('h').then((admission) => seen.push(admission.kind));
  for (let n = 0; n < 20; n += 1) {
    await new Promise<void>((resolve) => { setImmediate(resolve); });
  }
  assert.deepEqual(seen, [], 'the host was answered “read” before its row was stored');
  release();
  await pending;
  assert.deepEqual(seen, ['read']);
});

test('A1-T24 (the rest): no marker either; a closed window; a Read for a held host', async () => {
  // No marker either: only this window knows (residual 5d)
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  disk.failMark = true;
  const a = windowOver(new Backing(), disk);
  assert.deepEqual(await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' }), { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: false }]);
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten');
  assert.equal(disk.markers().length, 0);
  assert.equal((await windowOver(new Backing(), disk).keeper.admit('g')).kind, 'read', 'residual 5d: another window of the side reads on');
  // The window is closed while it holds one, with its marker made: after a reload with the lock free the first admit lands it
  const disk2 = new FakeDisk();
  disk2.lock = { token: 'hung' };
  const backing2 = new Backing();
  await windowOver(backing2, disk2).keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(disk2.markers().length, 1);
  disk2.ownerAlive = false;
  const reloaded = windowOver(backing2, disk2);
  assert.equal((await reloaded.keeper.admit('g')).kind, 'read');
  assert.deepEqual(disk2.refusedHosts(), ['h'], 'the file has no h after a reload');
  // A Read for a held host leaves the memory and removes its marker
  const disk3 = new FakeDisk();
  disk3.lock = { token: 'hung' };
  const c = windowOver(new Backing(), disk3);
  await c.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(await c.keeper.held(), 1);
  assert.deepEqual(await c.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' }), { kind: 'unwritten', why: 'busy' }, 'a lift under a held lock is not written');
  disk3.lock = undefined;
  await c.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.equal(await c.keeper.held(), 0);
  assert.equal(disk3.markers().length, 0, 'the marker of a lifted refusal stayed');
  assert.equal((await c.keeper.admit('g')).kind, 'read');
  assert.deepEqual(disk3.refusedHosts(), [], 'a lifted refusal was written later');
});

test('A1-T25 (the rest): a refused copy-in, a deleted file, a damaged list, the two sides', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  backing.values.set(KEY, [row('n1', 'no')]);
  disk.failReplace = true;
  const a = windowOver(backing, disk);
  assert.equal((await a.keeper.admit('z')).kind, 'unwritten', 'a copy-in the disk refuses was not held');
  assert.ok(a.unkept.some((one) => one.host === 'n1' && one.why === 'disk'));
  assert.equal(disk.markers().length, 1);
  assert.equal((await a.keeper.consent('n1')), 'no', 'the row did not count as a copy');
  disk.failReplace = false;
  assert.equal((await a.keeper.admit('z')).kind, 'read', 'the next admit did not try again');
  assert.deepEqual(disk.refusedHosts(), ['n1']);
  assert.deepEqual(backing.values.get(KEY), [row('n1', 'no'), { host: 'z', answer: 'yes', askedUtc: new Date(disk.now).toISOString(), how: 'auto' }], 'the copy-in changed the shared list');
  // a file deleted after the first copy-in
  disk.files.delete('refused.json');
  assert.equal((await a.keeper.admit('z')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['n1'], 'the deleted file was not written again');
  // a damaged list: no copy-in; after the mend the copy-in runs
  const b2 = new Backing();
  const d2 = new FakeDisk();
  b2.values.set(KEY, [row('n2', 'no'), 'junk']);
  const w = windowOver(b2, d2);
  assert.equal((await w.keeper.admit('z')).kind, 'damaged');
  assert.equal(d2.file(), undefined, 'a damaged list was copied in');
  await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'yes' });
  await w.keeper.admit('z');
  assert.ok(d2.refusedHosts().includes('n2'), 'the copy-in did not run after the mend');
  // two sides: the copy lost BEFORE the other side has read (residual 2), then Forget and Read per side
  const shared = new Backing();
  const side1 = new FakeDisk();
  const side2 = new FakeDisk();
  const s1 = windowOver(shared, side1, { mode: 'whole-state' });
  const s2 = windowOver(shared, side2, { mode: 'whole-state' });
  await s1.keeper.commit({ kind: 'person', host: 'q', answer: 'no' });
  shared.values.set(KEY, []);
  s2.view.sync();
  assert.equal((await s1.keeper.admit('q')).kind, 'refused');
  assert.equal((await s2.keeper.admit('q')).kind, 'read', 'residual 2 was not asserted');
  const t1 = windowOver(new Backing(), new FakeDisk());
  const back = new Backing();
  const d1 = new FakeDisk();
  const dd2 = new FakeDisk();
  const x1 = windowOver(back, d1);
  const x2 = windowOver(back, dd2);
  assert.ok(t1);
  await x1.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  x2.view.sync();
  assert.equal((await x2.keeper.admit('h')).kind, 'refused');
  await x2.keeper.commit({ kind: 'forget', host: 'h' });
  assert.equal((await x2.keeper.admit('h')).kind, 'read', 'Forget on side 2 did not allow it there');
  assert.equal((await x1.keeper.admit('h')).kind, 'refused', 'Forget on side 2 reached side 1');
  await x1.keeper.commit({ kind: 'person', host: 'h', answer: 'yes' });
  assert.equal((await x1.keeper.admit('h')).kind, 'read');
});

test('A1-T26 (the rest): the cap of markers, a marker already in the file, a disk that refuses the file', async () => {
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  for (let n = 0; n < 32; n += 1) {
    disk.files.set(`unsure-${n}.json`, JSON.stringify({ v: 1, kind: 'refuse', host: `m${n}`, utc: NOW }));
  }
  const w = windowOver(new Backing(), disk);
  assert.equal((await w.keeper.admit('g')).kind, 'unwritten', 'the side is stopped by the 32 markers already there');
  await w.keeper.commit({ kind: 'person', host: 'x', answer: 'no' });
  assert.deepEqual(w.unkept.find((one) => one.host === 'x'), { host: 'x', why: 'busy', marked: false }, 'the 33rd marker was made');
  // a marker whose change is already in the file stops nothing and is removed
  const d2 = new FakeDisk();
  const store = refusalsStore(d2, () => d2.now, instant);
  await store.commit({ kind: 'refuse', host: 'h', utc: NOW });
  d2.files.set('unsure-a.json', JSON.stringify({ v: 1, kind: 'refuse', host: 'h', utc: NOW }));
  const w2 = windowOver(new Backing(), d2);
  assert.equal((await w2.keeper.admit('g')).kind, 'read');
  assert.equal(d2.markers().length, 0, 'a marker whose change is in the file was not removed');
  // the disk refuses the file and takes the marker
  const d3 = new FakeDisk();
  d3.failReplace = true;
  const bk = new Backing();
  const w3 = windowOver(bk, d3);
  await w3.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(d3.markers().length, 1);
  assert.equal((await windowOver(bk, d3).keeper.admit('g')).kind, 'unwritten');
  d3.failReplace = false;
  assert.equal((await windowOver(bk, d3).keeper.admit('g')).kind, 'read');
  assert.deepEqual(d3.refusedHosts(), ['h']);
});

test('A1-T27 (the keeper half): a writer that woke up after a take-over loses nobody\'s refusal', async () => {
  const disk = new FakeDisk();
  const backing = new Backing();
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  const c = windowOver(backing, disk, { mode: 'whole-state' });
  await b.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  await c.keeper.admit('probe');
  // the copy in the list is gone too (a stale whole-state write), so only the memory of the newest file can bring the refusal back
  backing.values.set(KEY, []);
  b.view.sync();
  c.view.sync();
  const written = disk.file() as { gen: number };
  // a stale writer renames a file of the same gen and another writer over it: it holds g and not h
  disk.files.set('refused.json', textOf({ v: 1, gen: written.gen, by: 'woke-up', refused: [{ host: 'g', utc: NOW }] }));
  assert.equal((await b.keeper.admit('h')).kind, 'refused', 'the remembered refusal did not count between the rename and the union');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h'], 'the union was not written');
  // B is closed before its next read; a third window that had read the newer file writes the union
  assert.equal((await c.keeper.admit('probe')).kind, 'read');
  await b.keeper.commit({ kind: 'person', host: 'y', answer: 'no' });
  backing.values.set(KEY, []);
  b.view.sync();
  c.view.sync();
  assert.equal((await c.keeper.admit('probe')).kind, 'read');
  disk.files.set('refused.json', textOf({ v: 1, gen: written.gen + 1, by: 'woke-up-too', refused: [{ host: 'g', utc: NOW }] }));
  assert.equal((await c.keeper.admit('probe')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h', 'y'], 'a window that saw the newer file did not write the union of all three refusals');
  // no window that saw it: the copy is gone too, the host is read (residual 5b, asserted so that it is seen)
  const d = new FakeDisk();
  const bk = new Backing();
  await windowOver(bk, d).keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  d.files.set('refused.json', textOf({ v: 1, gen: 9, by: 'woke-up', refused: [] }));
  bk.values.set(KEY, []);
  assert.equal((await windowOver(bk, d).keeper.admit('h')).kind, 'read', 'residual 5b: nothing remembered and no copy');
});

test('A1-T15 (T15): the choices of the hosts list are the listed hosts and the hosts of the standings, the keeper\'s standing first', async () => {
  const refused: HostStanding = { host: 'h', answer: 'no', how: 'person', askedUtc: NOW };
  // an empty list, and a file that refuses `h`: the standings hold `h`, the list does not
  assert.deepEqual(choicesOf(listedHosts([], []), [refused]).map((one) => [one.host, one.standing?.answer]), [['h', 'no']]);
  // both hold it: the keeper's standing wins
  const asked = choicesOf(listedHosts([row('h', 'yes')], []), [refused]);
  assert.deepEqual(asked.map((one) => [one.host, one.standing?.answer]), [['h', 'no']]);
  // the host of a damaged list's live terminal with no answer stays, and the order is by code unit
  const mixed = choicesOf(listedHosts([row('z', 'yes'), 'junk'], ['q']), [refused]);
  assert.deepEqual(mixed.map((one) => one.host), ['h', 'q', 'z']);
  assert.equal(mixed.find((one) => one.host === 'q')?.standing, undefined);
  // through the keeper: a refusal only in the file is among the standings
  const disk = new FakeDisk();
  const a = windowOver(new Backing(), disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  const b = windowOver(new Backing(), disk);
  const standings = await b.keeper.standings();
  assert.deepEqual(choicesOf(listedHosts(b.keeper.stored(), []), standings).map((one) => one.host), ['h'], 'a refusal that is only in the side file could not be chosen to be changed');
});
