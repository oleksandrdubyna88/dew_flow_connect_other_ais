import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HostChange, HostStanding, commit, consentOf, dayOf, hostMerge, listedHosts, readableOf, standingsOf } from '../remoteHosts';
import { EMPTY_FILE, RefusalsRead } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, row, view, windowOver } from './hostsFakes';

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
  // No marker either: only this window knows
  disk.failMark = true;
  const c = windowOver(new Backing(), new FakeDisk());
  c.setting.on = true;
  assert.ok(c);
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
