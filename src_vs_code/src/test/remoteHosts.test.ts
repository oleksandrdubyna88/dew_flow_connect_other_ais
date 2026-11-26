import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { HostChange, HostStanding, choicesOf, commit, consentOf, dayOf, hostMerge, listedHosts, readableOf, standingsOf } from '../remoteHosts';
import type { KeeperSeams } from '../remoteKeeper';
import { EMPTY_FILE, RefusalsRead, textOf } from '../remoteRefusals';
import { refusalsStore } from '../remoteRefusalsStore';
import { Backing, FakeDisk, KEY, admit, consent, instant, row, rowOver, view, windowOver } from './hostsFakes';

/**
 * The store of the hosts (E1.S1): the rows, the merge, the cap, the damage rule, the keeper over both. Pure: a fake `globalState` that
 * several windows share, and a fake folder for the side's refusals file.
 */

const NOW = '2026-11-24T10:00:00.000Z';
const noWithdrawals = { files: [], crowded: false };
const noFile: RefusalsRead = { kind: 'file', file: EMPTY_FILE, waiting: [], withdrawn: noWithdrawals };
const fileWith = (...hosts: string[]): RefusalsRead => ({ kind: 'file', file: { ...EMPTY_FILE, gen: 1, by: 'x', refused: hosts.map((host) => ({ host, utc: NOW })) }, waiting: [], withdrawn: noWithdrawals });
const damagedFile: RefusalsRead = { kind: 'damaged' };

const merged = (stored: unknown, change: HostChange, now = NOW) => hostMerge(now)(stored, change);

test('A1-T1: a row says how it came, and a marker nobody knows is not read', () => {
  const rows = [row('a', 'yes', NOW, 'auto'), row('b', 'yes'), row('c', 'no', 'x', undefined), { ...row('d', 'yes'), extra: 1 }];
  const standings = standingsOf(rows);
  assert.deepEqual(standings.map((one) => [one.host, one.how]), [['a', 'auto'], ['b', 'person'], ['c', 'person'], ['d', 'person']]);
  const next = merged(rows, { kind: 'person', host: 'z', answer: 'no' });
  const written = (next.next?.value ?? []) as unknown[];
  assert.ok(rows.filter((one) => one['how'] !== 'auto').every((one) => written.includes(one)), 'a row of another host was rewritten, so its extra keys were lost');
  assert.ok(!written.some((one) => (one as Record<string, unknown>)['how'] === 'auto'), 'a write of the list kept an automatic row (J7)');
  for (const bad of [[row('h', 'yes', NOW, 'auto' as never), { ...row('h', 'yes'), how: 'later' }], [{ ...row('h', 'no'), how: 'auto' }], [{ ...row('h', 'yes'), how: 'later' }]]) {
    assert.equal(consentOf(noFile, bad, 'h'), 'damaged', `a row with a marker this build does not know was read as a person's "yes": ${JSON.stringify(bad)}`);
  }
  assert.equal(consentOf(noFile, undefined, 'h'), 'open');
  assert.equal(consentOf(noFile, [], 'h'), 'open');
});

test('A1-T2 (E2S1-T11, the list half): the refusal half of the door over the stores, and a per-host "yes" is never an answer', () => {
  const stores: readonly (readonly [string, unknown, 'no' | 'open' | 'damaged'])[] = [
    ['empty', [], 'open'],
    ['never written', undefined, 'open'],
    ['a person yes (0.87.0)', [row('h', 'yes')], 'open'],
    ['an automatic yes (2.0.901)', [row('h', 'yes', NOW, 'auto')], 'open'],
    ['a no', [row('h', 'no')], 'no'],
    ['two rows, one a no', [row('h', 'yes'), row('h', 'no')], 'no'],
    ['junk', 'junk', 'damaged'],
    ['a bad entry beside a yes', [row('h', 'yes'), 'junk'], 'damaged'],
    ['a bad entry beside a no', [row('h', 'no'), 'junk'], 'no'],
  ];
  for (const [name, value, want] of stores) {
    assert.equal(consentOf(noFile, value, 'h'), want, name);
    assert.equal(consentOf(fileWith('h'), value, 'h'), 'no', `${name}: a refusal in the file is a no`);
    assert.equal(consentOf(damagedFile, value, 'h'), name.includes('no') && !name.includes('yes') ? 'no' : 'damaged', `${name}, a damaged file`);
  }
  // E2S1-T11: a list that holds a "yes" row or a how:'auto' row is not damaged
  assert.equal(readableOf([row('h', 'yes'), row('g', 'yes', NOW, 'auto')]).damaged, false);
});

test('A1-T4 (E2S1-T18, T23): who may change which row — every cell of the merge; no write adds a "yes"', () => {
  const none: unknown = [];
  const auto = [row('h', 'yes', NOW, 'auto')];
  const yes = [row('h', 'yes')];
  const no = [row('h', 'no')];
  const rowOf = (outcome: ReturnType<typeof merged>): Record<string, unknown> | undefined => (outcome.next?.value as Record<string, unknown>[] | undefined)?.[0];
  const person: HostChange = { kind: 'person', host: 'h', answer: 'no' };

  for (const stored of [none, auto, yes, no]) {
    assert.equal(rowOf(merged(stored, person))?.['answer'], 'no');
    assert.equal(rowOf(merged(stored, person))?.['how'], undefined, 'a person\'s no carries how');
  }
  for (const stored of [auto, yes, no]) {
    assert.deepEqual(merged(stored, { kind: 'forget', host: 'h' }).next, { value: [] });
  }
  assert.equal(merged(none, { kind: 'forget', host: 'h' }).next, undefined);
  assert.equal(merged(none, { kind: 'forget', host: 'h' }).result.kind, 'kept');
  // no change can carry answer 'yes' or kind 'auto' (a type test): these lines do not compile when the types widen
  // @ts-expect-error — a person's "yes" is not a change any more
  const noYes: HostChange = { kind: 'person', host: 'h', answer: 'yes' };
  // @ts-expect-error — an automatic change is not a change any more
  const noAuto: HostChange = { kind: 'auto', host: 'h' };
  // @ts-expect-error — a mend cannot write a "yes"
  const noMendYes: HostChange = { kind: 'mend', host: 'h', answer: 'yes' };
  assert.ok(noYes !== noAuto && noMendYes !== undefined);
  // over generated lists and changes, no write adds a row whose answer is 'yes'
  let seed = 11;
  const pick = (n: number): number => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
  const changes: readonly HostChange[] = [
    { kind: 'person', host: 'a', answer: 'no' }, { kind: 'forget', host: 'b' }, { kind: 'mend', host: 'c', answer: 'no' }, { kind: 'mend', host: 'd', answer: 'forget' },
  ];
  for (let n = 0; n < 200; n += 1) {
    const rows = Array.from({ length: pick(6) }, () => row(`h${pick(5)}`, pick(2) === 0 ? 'yes' : 'no', NOW, pick(3) === 0 ? 'auto' : undefined)).filter((one) => !(one['answer'] === 'no' && one['how'] === 'auto'));
    const out = merged(rows, changes[pick(changes.length)] as HostChange);
    const before = new Set(rows.filter((one) => one['answer'] === 'yes').map((one) => one));
    for (const one of (out.next?.value ?? []) as Record<string, unknown>[]) {
      assert.ok(one['answer'] === 'no' || before.has(one), 'a write added a "yes" row');
      assert.notEqual(one['how'], 'auto', 'a write left an automatic row (J7)');
    }
  }
});

test('E2S1-T23: the cap — a person\'s "no" pushes out the oldest "yes" row, never a "no"; 100 refusals are full', () => {
  const rows: Record<string, unknown>[] = Array.from({ length: 99 }, (_, n) => row(`h${n}`, 'yes', `2026-10-${String(10 + (n % 20)).padStart(2, '0')}T00:00:00.000Z`));
  rows.push(row('oldest', 'no', '2026-01-01T00:00:00.000Z'));
  const out = merged(rows, { kind: 'person', host: 'new', answer: 'no' });
  const next = out.next?.value as { host: string }[];
  assert.equal(next.length, 100);
  assert.ok(next.some((one) => one.host === 'oldest'), 'the list dropped a “Not this host” to make room');
  assert.ok(!next.some((one) => one.host === 'h0'), 'the oldest yes row (the host name breaks the tie) did not go');
  assert.deepEqual(out.result.kind === 'written' && out.result.evicted, ['h0']);

  const autos = [...Array.from({ length: 99 }, (_, n) => row(`a${n}`, 'yes', `2026-10-${String(10 + (n % 20)).padStart(2, '0')}T00:00:00.000Z`, 'auto')), row('keep', 'no')];
  const afterAutos = merged(autos, { kind: 'person', host: 'fresh', answer: 'no' });
  assert.equal(afterAutos.result.kind, 'written');
  assert.deepEqual((afterAutos.next?.value as { host: string }[]).map((one) => one.host).sort(), ['fresh', 'keep'], 'the automatic rows stayed, or a refusal went');
  const refusals = Array.from({ length: 100 }, (_, n) => row(`r${n}`, 'no'));
  assert.equal(merged(refusals, { kind: 'person', host: 'x', answer: 'no' }).result.kind, 'full');
  assert.equal(merged(refusals, { kind: 'person', host: 'r5', answer: 'no' }).result.kind, 'written', 'a changed answer of a known host was refused');
  const people = Array.from({ length: 100 }, (_, n) => row(`p${n}`, 'yes'));
  assert.equal(merged(people, { kind: 'person', host: 'p5', answer: 'no' }).result.kind, 'written', 'a changed answer of a known host dropped something');
});

test('A1-T8: a list that cannot be read allows nobody, and a person\'s answer mends it', async () => {
  for (const junk of ['junk', null, 7, {}, [null], ['h'], [{ host: 'h' }], [row('z', 'yes'), 'junk'], [row('x', 'no'), { host: 'y', answer: 'maybe', askedUtc: NOW }], [{ ...row('z', 'yes'), how: 'later' }]]) {
    const backing = new Backing();
    backing.values.set(KEY, junk);
    const w = windowOver(backing, new FakeDisk());
    assert.equal((await admit(w, 'newhost')).kind, 'damaged', `a damaged list let a new host be read: ${JSON.stringify(junk)}`);
    assert.equal((await w.keeper.commit({ kind: 'forget', host: 'q' })).kind, 'damaged');
    assert.equal(backing.values.get(KEY), junk, 'a list that could not be read was written over');
  }
  const backing = new Backing();
  backing.values.set(KEY, [row('z', 'yes'), 'junk', row('x', 'no')]);
  const w = windowOver(backing, new FakeDisk());
  await w.keeper.allow();
  assert.equal((await admit(w, 'x')).kind, 'refused');
  assert.equal((await admit(w, 'z')).kind, 'damaged');
  const done = await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
  assert.equal(done.kind, 'written', 'the answer that repaired the list was not kept');
  const stored = backing.values.get(KEY) as { host: string; answer: string }[];
  assert.deepEqual(stored.map((one) => `${one.host}:${one.answer}`).sort(), ['q:no', 'x:no', 'z:yes']);
  assert.equal(readableOf(stored).damaged, false);
  assert.equal((await admit(w, 'z')).kind, 'read', 'the mended list does not work');
  // Over 100 readable refusals and a bad entry a mend for a new host is `full`.
  const full = [...Array.from({ length: 100 }, (_, n) => row(`r${n}`, 'no')), 'junk'];
  assert.equal(merged(full, { kind: 'mend', host: 'new', answer: 'no' }).result.kind, 'full');
  assert.equal(merged(full, { kind: 'mend', host: 'r1', answer: 'forget' }).result.kind, 'written');
  // The list shown over damage, and (E2S1-T15) the live hosts of a list that can be read too
  assert.deepEqual(listedHosts([row('z', 'yes'), 'junk'], ['z', 'q', 'q']).map((one) => [one.host, one.standing === undefined]), [['z', false], ['q', true]]);
  assert.deepEqual(listedHosts([row('z', 'yes')], ['q']).map((one) => one.host), ['z', 'q'], 'a live host with no answer was not listed, so it could not be refused before it is read');
  const reader = windowOver(backing, new FakeDisk());
  await reader.keeper.allow();
  assert.equal((await admit(reader, 'z')).kind, 'read', 'a readable list does not read a known host');
});

test('A1-T3: a "no" against a read host, in both orders, with a stale window', async () => {
  for (const mode of ['one-key', 'whole-state'] as const) {
    const backing = new Backing();
    const disk = new FakeDisk();
    const a = windowOver(backing, disk, { mode });
    const b = windowOver(backing, disk, { mode });
    await b.keeper.allow();
    assert.deepEqual(await admit(b, 'h'), { kind: 'read', how: 'person', allowedNow: false });
    a.view.sync();
    await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
    assert.deepEqual(disk.refusedHosts(), ['h']);
    const updates = b.view.updates.length;
    assert.equal((await admit(b, 'h')).kind, 'refused', 'a "no" given in another window did not stop the second call');
    assert.equal(b.view.updates.length, updates, 'a stale window wrote while the host was refused');
    // (c) C is stale: it holds a list from before A's write.
    const c = windowOver(new Backing(), disk, { mode });
    assert.equal((await admit(c, 'h')).kind, 'refused', 'the store lost “Not this host” for h and the host was read');
    const other = await admit(c, 'g');
    assert.equal(other.kind, 'read');
    assert.equal((await admit(c, 'h')).kind, 'refused');
    assert.equal((await admit(a, 'h')).kind, 'refused');
    // (e) two refusals, each window stale to the other
    const d = windowOver(backing, disk, { mode });
    const e = windowOver(backing, disk, { mode });
    await d.keeper.commit({ kind: 'person', host: 'x', answer: 'no' });
    await e.keeper.commit({ kind: 'person', host: 'y', answer: 'no' });
    assert.ok(disk.refusedHosts().includes('x') && disk.refusedHosts().includes('y'), 'one of two refusals was lost');
    for (const w of [d, e]) {
      assert.equal((await admit(w, 'x')).kind, 'refused');
      assert.equal((await admit(w, 'y')).kind, 'refused');
    }
  }
});

test('A1-T5: no list is handed to a write, and every write is in the step of its read', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const w = windowOver(backing, disk);
  await admit(w, 'a');
  await w.keeper.commit({ kind: 'person', host: 'b', answer: 'no' });
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
  await a.keeper.allow();
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  // (a) the copy is lost behind A's back: still refused, and nobody writes the row again
  backing.values.set(KEY, []);
  a.view.sync();
  b.view.sync();
  const before = a.view.updates.length;
  assert.equal((await admit(a, 'h')).kind, 'refused');
  assert.equal((await admit(b, 'h')).kind, 'refused', 'a “Not this host” whose copy was lost was read');
  assert.equal(a.view.updates.length, before, 'a no row was written again');
  // (b) Read this host again in B, A catches up first
  const gen = disk.file()?.gen ?? 0;
  assert.equal(['written', 'kept'].includes((await b.keeper.commit({ kind: 'forget', host: 'h' })).kind), true);
  assert.deepEqual(disk.refusedHosts(), []);
  assert.ok((disk.file()?.gen ?? 0) > gen);
  a.view.sync();
  assert.deepEqual(await admit(a, 'h'), { kind: 'read', how: 'person', allowedNow: false });
  // (c) Forget in B, the "no" does NOT come back
  await b.keeper.commit({ kind: 'person', host: 'k', answer: 'no' });
  a.view.sync();
  b.view.sync();
  await b.keeper.commit({ kind: 'forget', host: 'k' });
  a.view.sync();
  assert.equal((await admit(a, 'k')).kind, 'read');
  // (f) a lift that the file does not take: unwritten, the list untouched, refused
  await a.keeper.commit({ kind: 'person', host: 'm', answer: 'no' });
  disk.failReplace = true;
  const lift = await a.keeper.commit({ kind: 'forget', host: 'm' });
  assert.equal(lift.kind, 'unwritten');
  assert.equal((backing.values.get(KEY) as { host: string; answer: string }[]).find((one) => one.host === 'm')?.answer, 'no', 'the "no" row of the list was touched');
  disk.failReplace = false;
  assert.equal((await admit(a, 'm')).kind, 'refused');
  // (g) a damaged list: refused in the file, every other host damaged
  backing.values.set(KEY, 'junk');
  a.view.sync();
  assert.equal((await admit(a, 'm')).kind, 'refused');
  assert.equal((await admit(a, 'other')).kind, 'damaged');
});

test('A1-T9 and A1-T10: the store and the file are read again before every call', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.allow();
  assert.equal((await admit(a, 'h')).kind, 'read');
  b.view.sync();
  await b.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal((await admit(a, 'h')).kind, 'refused', 'a “no” given between two calls did not stop the second');
  // a stored "yes" row of an earlier version is no answer and no refusal: the host is read under the permission like any other
  backing.values.set(KEY, [...(backing.values.get(KEY) as unknown[]), row('n3', 'yes'), row('n4', 'yes', NOW, 'auto')]);
  a.view.sync();
  assert.equal((await admit(a, 'n3')).kind, 'read');
  assert.equal((await admit(a, 'n4')).kind, 'read');
  assert.equal((backing.values.get(KEY) as { host: string }[]).some((one) => one.host === 'n9'), false);
});

test('A1-T10: the row is stored before the answer is given', async () => {
  const backing = new Backing();
  const v = view(backing);
  let release: () => void = () => undefined;
  const gate = new Promise<void>((resolve) => { release = resolve; });
  const slow = { ...v, update: async (key: string, value: unknown) => { await gate; await v.update(key, value); } };
  const seen: string[] = [];
  const done = commit(slow, KEY, { kind: 'person', host: 'h', answer: 'no' } as HostChange, hostMerge(NOW)).then(() => seen.push('resolved'));
  await Promise.resolve();
  assert.deepEqual(seen, [], 'the host was answered before its row was stored');
  release();
  await done;
  assert.equal(backing.values.has(KEY), true);
});

test('A1-T13: Forget, and Not this host on a stored "yes" row of an earlier version', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  backing.values.set(KEY, [row('h', 'yes', NOW, 'auto'), row('g', 'yes')]);
  const w = windowOver(backing, disk);
  await w.keeper.allow();
  assert.deepEqual(await admit(w, 'h'), { kind: 'read', how: 'person', allowedNow: false }, 'a stored row changed what is read');
  assert.equal((await w.keeper.commit({ kind: 'forget', host: 'h' })).kind, 'written');
  assert.deepEqual((backing.values.get(KEY) as { host: string }[]).map((one) => one.host), ['g'], 'Forget removed more than its row, or the automatic row stayed');
  await w.keeper.commit({ kind: 'person', host: 'g', answer: 'no' });
  const rows = (backing.values.get(KEY) as Record<string, unknown>[]).filter((one) => one['host'] === 'g');
  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.['how'], undefined);
  assert.equal((await admit(w, 'g')).kind, 'refused');
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
});

test('A1-T19: the refusing window is gone, and a stale window writes', async () => {
  for (const viaKey of [false, true]) {
    const backing = new Backing();
    const disk = new FakeDisk();
    const a = windowOver(backing, disk, { mode: 'whole-state' });
    const b = windowOver(backing, disk, { mode: 'whole-state' });
    await a.keeper.allow();
    await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
    // A is disposed; B is stale and writes the whole object
    if (viaKey) {
      await b.keeper.commitKey('coai.terminalAccountsState', { autoNoticed: 'T2' }, () => ({ next: { value: { autoNoticed: 'T2' } }, result: 1 }));
    } else {
      assert.equal((await admit(b, 'g')).kind, 'read');
      await b.keeper.commit({ kind: 'person', host: 'zz', answer: 'no' });
    }
    assert.equal((backing.values.get(KEY) as unknown[] | undefined)?.some((one) => (one as { host: string }).host === 'h') ?? false, false, 'the stale write did not lose the copy, so this test proves nothing');
    const calls: string[] = [];
    for (const reloaded of [windowOver(backing, disk, { mode: 'whole-state' }), windowOver(backing, disk, { mode: 'whole-state' })]) {
      const admission = await admit(reloaded, 'h');
      if (admission.kind !== 'refused') {
        calls.push('h was read');
      }
    }
    assert.deepEqual(calls, [], 'a “Not this host” was erased for good by a stale window after the window that gave it was closed: h was read');
    assert.ok(disk.refusedHosts().includes('h'));
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
  const a = windowOver(backing, disk);
  await a.keeper.allow();
  disk.lock = { token: 'hung' };
  const given = a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(a.view.updates.length, 1, 'a “Not this host” had no copy while its write waited');
  assert.deepEqual(await given, { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: true }]);
  assert.equal(disk.markers().length, 1);
  assert.equal(await a.keeper.held(), 1);
  assert.equal((await admit(a, 'h')).kind, 'refused', 'a refused host answers no before it answers waiting (X10)');
  assert.equal((await admit(a, 'g')).kind, 'unwritten', 'a window that could not save a refusal went on reading other hosts');
  assert.equal(await consent(a, 'h'), 'no');
  assert.equal(await consent(a, 'g'), 'damaged');
  assert.equal(await a.keeper.may('session', 'g'), 'waiting', 'T8: a waiting side has its own answer, and it is not damage');
  disk.now += 3_600_000;
  assert.equal((await admit(a, 'g')).kind, 'unwritten', 'a lock was taken over by its age');
  disk.ownerAlive = false;
  assert.equal((await admit(a, 'g')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  assert.equal(disk.markers().length, 0);
  assert.equal(await a.keeper.held(), 0);
  assert.equal((await admit(a, 'h')).kind, 'refused');
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
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  await a.keeper.allow();
  disk.lock = { token: 'hung' };
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  // A is disposed. B is stale and writes a notice time: the copy is gone.
  await b.keeper.commitKey('coai.terminalAccountsState', {}, () => ({ next: { value: { n: 1 } }, result: 1 }));
  for (const reloaded of [b, windowOver(backing, disk, { mode: 'whole-state' })]) {
    assert.equal((await admit(reloaded, 'h')).kind, 'refused', 'a window that could not save a “Not this host” was closed, and another window of the same side read the host');
    assert.equal((await admit(reloaded, 'g')).kind, 'unwritten');
    assert.equal(await consent(reloaded, 'h'), 'no');
    assert.ok((await reloaded.keeper.standings()).some((one) => one.host === 'h' && one.answer === 'no'));
  }
  assert.ok(b.unkept.some((one) => one.host === 'h' && one.why === 'elsewhere'));
  disk.ownerAlive = false;
  const c = windowOver(backing, disk, { mode: 'whole-state' });
  assert.equal((await admit(c, 'g')).kind, 'read');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  assert.equal(disk.markers().length, 0);
  // A torn marker is removed by nothing but a press
  disk.files.set('unsure-torn.json', '{');
  assert.equal((await admit(c, 'g')).kind, 'unwritten');
  assert.equal((await admit(c, 'g')).kind, 'unwritten');
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
  await admit(a, 'z');
  assert.deepEqual(disk.refusedHosts(), ['n1', 'n2']);
  const migrated = (disk.file() as { migratedUtc?: string }).migratedUtc;
  assert.ok(migrated !== undefined);
  const replaces = disk.replaces;
  await admit(a, 'z2');
  assert.equal(disk.replaces, replaces, 'a read with nothing to copy wrote the file');
  // an older window's "no" after migratedUtc
  const old = windowOver(backing, disk);
  old.view.update(KEY, [...(backing.values.get(KEY) as unknown[]), row('k', 'no')]);
  a.view.sync();
  await admit(a, 'any');
  assert.ok(disk.refusedHosts().includes('k'), 'a “Not this host” given in a window of an older build after the migration was never in the file');
  assert.equal((disk.file() as { migratedUtc?: string }).migratedUtc, migrated);
  backing.values.set(KEY, []);
  for (const w of [a, windowOver(backing, disk)]) {
    assert.equal((await admit(w, 'k')).kind, 'refused');
  }
  // two sides over one backing
  const other = new FakeDisk();
  const backing2 = new Backing();
  const s1 = windowOver(backing2, disk);
  const s2 = windowOver(backing2, other);
  await s1.keeper.commit({ kind: 'person', host: 'q', answer: 'no' });
  s2.view.sync();
  assert.equal((await admit(s2, 'q')).kind, 'refused');
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
  await admit(a, 'x');
  const text = disk.files.get('refused.json') as string;
  await a.keeper.commit({ kind: 'person', host: 'g', answer: 'no' });
  // a stale writer renames an OLDER text over the newer file
  disk.files.set('refused.json', text);
  const decision = await admit(a, 'h');
  assert.equal(decision.kind, 'refused');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h'], 'the union was not written back');
  // the file removed
  disk.files.delete('refused.json');
  assert.equal((await admit(a, 'g')).kind, 'refused');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h']);
  // a lift that was written over comes back as a refusal (residual 4)
  await a.keeper.commit({ kind: 'forget', host: 'h' });
  assert.deepEqual(disk.refusedHosts(), ['g']);
});

test('A1-T23: a file that cannot be read contacts nobody, and its mend leaves the round stopped and no permission', async () => {
  for (const bad of ['not json', '[]', '{}', '{"v":2,"refused":[]}', '{"v":1}', '{"v":1,"gen":1,"by":"x","refused":["h"]}', '{"v":1,"gen":1,"by":"x","refused":[],"stop":5}']) {
    const backing = new Backing();
    backing.values.set(KEY, [row('p', 'yes'), row('n', 'no')]);
    const disk = new FakeDisk();
    disk.files.set('refused.json', bad);
    const w = windowOver(backing, disk);
    assert.equal((await admit(w, 'new')).kind, 'damaged', `a file that could not be read let a host be read: ${bad}`);
    assert.equal((await admit(w, 'p')).kind, 'damaged');
    assert.equal((await admit(w, 'n')).kind, 'refused');
    assert.equal(await w.keeper.roundStop(), 'damaged');
    assert.equal((await w.keeper.commit({ kind: 'forget', host: 'p' })).kind, 'damaged');
    assert.equal(disk.files.get('refused.json'), bad, 'the bytes on the disk changed');
    const mended = await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
    assert.equal(mended.kind, 'written');
    assert.deepEqual(disk.refusedHosts(), ['n', 'q']);
    assert.ok(disk.file()?.stop !== undefined, 'a mend started a round that may have been stopped');
    assert.equal(await w.keeper.roundStop(), 'stopped');
    assert.equal((await admit(w, 'new')).kind, 'not-asked', 'a mended file kept a permission, or gave one');
    assert.equal((disk.file() as { grant?: unknown }).grant, undefined, 'a mend wrote a grant');
  }
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  await w.keeper.allow();
  disk.failReads = 1;
  assert.equal((await admit(w, 'a')).kind, 'read', 'a read that fails once made the file damaged');
  const none = new FakeDisk();
  none.failReads = 3;
  assert.equal(await windowOver(new Backing(), none).keeper.roundStop(), 'damaged');
  const unlisted = new FakeDisk();
  unlisted.failMarks = true;
  assert.equal((await admit(windowOver(new Backing(), unlisted), 'a')).kind, 'damaged');
  const stopOver = new FakeDisk();
  stopOver.files.set('refused.json', 'junk');
  const s = windowOver(new Backing(), stopOver);
  assert.equal((await s.keeper.stopRound()).kind, 'unwritten');
  assert.equal((await admit(s, 'a')).kind, 'damaged', 'damaged is answered before waiting (§ P8)');
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
  const disk = new FakeDisk();
  const state = view(new Backing());
  const base = {
    state, key: KEY, refusals: refusalsStore(disk, () => 0, instant), now: () => NOW, row: rowOver(state, disk),
    changed: () => undefined, unkept: () => undefined, unkeptWithdrawal: () => undefined,
  };
  // @ts-expect-error — a seam that asks a person is not a KeeperSeams
  const withAsk: KeeperSeams = { ...base, ask: async () => 'yes' };
  // @ts-expect-error — a seam for a notice of damage is not a KeeperSeams (a damaged list or file has no notice of its own)
  const withDamaged: KeeperSeams = { ...base, damaged: () => undefined };
  assert.ok(withAsk !== withDamaged);
  assert.ok(!Object.keys(base).some((name) => name.toLowerCase().includes('ask') || name.toLowerCase().includes('raise')), 'a member of the seams holds "ask"');
  const w = windowOver(new Backing(), new FakeDisk());
  for (let n = 0; n < 100; n += 1) {
    const admission = await admit(w, `new${n}`);
    assert.equal(admission.kind, 'not-asked', `the door answered ${admission.kind} with no permission`);
  }
  assert.deepEqual(w.unkept, [], 'the door of the round raised something');
});

test('A1-T3 (b) and (d): no with a fresh view, and a stale commitKey erases the copy', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk, { mode: 'whole-state' });
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  b.view.sync();
  assert.equal((await admit(b, 'h')).kind, 'refused', '(b) no, with a fresh view');
  // (d) B is stale again (a window opened before A's write) and writes the state row: the whole object goes out
  const stale = windowOver(new Backing(), disk, { mode: 'whole-state' });
  await stale.keeper.commitKey(SIDE_STATE, { n: 1 }, () => ({ next: { value: { n: 1 } }, result: 1 }));
  backing.values.clear();
  backing.values.set(KEY, []);
  for (const w of [a, b, stale]) {
    w.view.sync();
    assert.equal((await admit(w, 'h')).kind, 'refused', '(d) h was read after a stale write of another key');
  }
});

test('A1-T6 (b2), (c2), (d), (e): the sub-cases of a lift inside the lag', async () => {
  // (b2) Read this host again in B while A is stale: A copies the old "no" back into the file (residual 4)
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  const b = windowOver(backing, disk);
  await a.keeper.allow();
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  b.view.sync();
  await b.keeper.commit({ kind: 'forget', host: 'h' });
  assert.deepEqual(disk.refusedHosts(), []);
  assert.equal((await admit(a, 'h')).kind, 'refused', '(b2) the stale view did not copy the old no in');
  assert.deepEqual(disk.refusedHosts(), ['h']);
  a.view.sync();
  assert.equal((await admit(a, 'h')).kind, 'refused');
  assert.equal((await admit(b, 'h')).kind, 'refused');
  await a.keeper.commit({ kind: 'forget', host: 'h' });
  assert.equal((await admit(b, 'h')).kind, 'read', 'one more Read this host again after the sync did not lift it');
  assert.deepEqual(disk.refusedHosts(), []);
  // (c2) Forget in B, A reads inside the lag
  const backing2 = new Backing();
  const disk2 = new FakeDisk();
  const c = windowOver(backing2, disk2);
  const d = windowOver(backing2, disk2);
  await c.keeper.commit({ kind: 'person', host: 'k', answer: 'no' });
  d.view.sync();
  await d.keeper.commit({ kind: 'forget', host: 'k' });
  assert.equal((await admit(c, 'k')).kind, 'refused', '(c2) the refusal did not come back inside the lag');
  // (d) Forget in A with an admit of A queued behind it
  const backing3 = new Backing();
  const disk3 = new FakeDisk();
  const e = windowOver(backing3, disk3);
  await e.keeper.allow();
  await e.keeper.commit({ kind: 'person', host: 'm', answer: 'no' });
  const forget = e.keeper.commit({ kind: 'forget', host: 'm' });
  const next = admit(e, 'm');
  await forget;
  assert.deepEqual(await next, { kind: 'read', how: 'person', allowedNow: false }, '(d) the lift was met half-way by the queued admit');
  assert.deepEqual(disk3.refusedHosts(), []);
  // (e) an old no is put back by a stale whole-state write
  backing3.values.set(KEY, [row('m', 'no')]);
  e.view.sync();
  assert.equal((await admit(e, 'm')).kind, 'refused', '(e) the old no put back was read');
  assert.deepEqual(disk3.refusedHosts(), ['m']);
  await e.keeper.commit({ kind: 'forget', host: 'm' });
  assert.equal((await admit(e, 'm')).kind, 'read');
});





test('A1-T24 (the rest): no marker either; a closed window; a Read this host again for a held host', async () => {
  // No marker either: only this window knows (residual 5d)
  const disk = new FakeDisk();
  const a = windowOver(new Backing(), disk);
  await a.keeper.allow();
  disk.lock = { token: 'hung' };
  disk.failMark = true;
  assert.deepEqual(await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' }), { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: false }]);
  assert.equal((await admit(a, 'g')).kind, 'unwritten');
  assert.equal(disk.markers().length, 0);
  assert.equal((await admit(windowOver(new Backing(), disk), 'g')).kind, 'read', 'residual 5d: another window of the side reads on');
  // The window is closed while it holds one, with its marker made: after a reload with the lock free the first admit lands it
  const disk2 = new FakeDisk();
  const backing2 = new Backing();
  const first = windowOver(backing2, disk2);
  await first.keeper.allow();
  disk2.lock = { token: 'hung' };
  await first.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(disk2.markers().length, 1);
  disk2.ownerAlive = false;
  const reloaded = windowOver(backing2, disk2);
  assert.equal((await admit(reloaded, 'g')).kind, 'read');
  assert.deepEqual(disk2.refusedHosts(), ['h'], 'the file has no h after a reload');
  // A Read this host again for a held host leaves the memory and removes its marker
  const disk3 = new FakeDisk();
  const c = windowOver(new Backing(), disk3);
  await c.keeper.allow();
  disk3.lock = { token: 'hung' };
  await c.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(await c.keeper.held(), 1);
  assert.deepEqual(await c.keeper.commit({ kind: 'forget', host: 'h' }), { kind: 'unwritten', why: 'busy' }, 'a lift under a held lock is not written');
  disk3.lock = undefined;
  await c.keeper.commit({ kind: 'forget', host: 'h' });
  assert.equal(await c.keeper.held(), 0);
  assert.equal(disk3.markers().length, 0, 'the marker of a lifted refusal stayed');
  assert.equal((await admit(c, 'g')).kind, 'read');
  assert.deepEqual(disk3.refusedHosts(), [], 'a lifted refusal was written later');
});

test('A1-T25 (the rest): a refused copy-in, a deleted file, a damaged list, the two sides', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  const a = windowOver(backing, disk);
  await a.keeper.allow();
  backing.values.set(KEY, [row('n1', 'no')]);
  a.view.sync();
  disk.failReplace = true;
  assert.equal((await admit(a, 'z')).kind, 'unwritten', 'a copy-in the disk refuses was not held');
  assert.ok(a.unkept.some((one) => one.host === 'n1' && one.why === 'disk'));
  assert.equal(disk.markers().length, 1);
  assert.equal((await consent(a, 'n1')), 'no', 'the row did not count as a copy');
  disk.failReplace = false;
  assert.equal((await admit(a, 'z')).kind, 'read', 'the next admit did not try again');
  assert.deepEqual(disk.refusedHosts(), ['n1']);
  assert.deepEqual(backing.values.get(KEY), [row('n1', 'no')], 'the copy-in changed the shared list, or the door wrote an automatic row');
  // a file deleted after the first copy-in: it held the grant too, so the permission is gone (residual 7) and the refusal is written forward again
  disk.files.delete('refused.json');
  assert.equal((await admit(a, 'z')).kind, 'not-asked');
  assert.deepEqual(disk.refusedHosts(), ['n1'], 'the deleted file was not written again');
  // a damaged list: no copy-in; after the mend the copy-in runs
  const b2 = new Backing();
  const d2 = new FakeDisk();
  b2.values.set(KEY, [row('n2', 'no'), 'junk']);
  const w = windowOver(b2, d2);
  assert.equal((await admit(w, 'z')).kind, 'damaged');
  assert.equal(d2.file(), undefined, 'a damaged list was copied in');
  await w.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' });
  await admit(w, 'z');
  assert.ok(d2.refusedHosts().includes('n2'), 'the copy-in did not run after the mend');
  // two sides: the copy lost BEFORE the other side has read (residual 2), then Forget and Read per side
  const shared = new Backing();
  const side1 = new FakeDisk();
  const side2 = new FakeDisk();
  const s1 = windowOver(shared, side1, { mode: 'whole-state' });
  const s2 = windowOver(shared, side2, { mode: 'whole-state' });
  await s2.keeper.allow();
  await s1.keeper.commit({ kind: 'person', host: 'q', answer: 'no' });
  shared.values.set(KEY, []);
  s2.view.sync();
  assert.equal((await admit(s1, 'q')).kind, 'refused');
  assert.equal((await admit(s2, 'q')).kind, 'read', 'residual 2 was not asserted');
  const back = new Backing();
  const d1 = new FakeDisk();
  const dd2 = new FakeDisk();
  const x1 = windowOver(back, d1);
  const x2 = windowOver(back, dd2);
  await x1.keeper.allow();
  await x2.keeper.allow();
  await x1.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  x2.view.sync();
  assert.equal((await admit(x2, 'h')).kind, 'refused');
  await x2.keeper.commit({ kind: 'forget', host: 'h' });
  assert.equal((await admit(x2, 'h')).kind, 'read', 'Read this host again on side 2 did not allow it there');
  assert.equal((await admit(x1, 'h')).kind, 'refused', 'Read this host again on side 2 reached side 1');
  await x1.keeper.commit({ kind: 'forget', host: 'h' });
  assert.equal((await admit(x1, 'h')).kind, 'read');
});

test('A1-T26 (the rest): the cap of markers, a marker already in the file, a disk that refuses the file', async () => {
  const disk = new FakeDisk();
  const w = windowOver(new Backing(), disk);
  await w.keeper.allow();
  disk.lock = { token: 'hung' };
  for (let n = 0; n < 32; n += 1) {
    disk.files.set(`unsure-${n}.json`, JSON.stringify({ v: 1, kind: 'refuse', host: `m${n}`, utc: NOW }));
  }
  assert.equal((await admit(w, 'g')).kind, 'unwritten', 'the side is stopped by the 32 markers already there');
  await w.keeper.commit({ kind: 'person', host: 'x', answer: 'no' });
  assert.deepEqual(w.unkept.find((one) => one.host === 'x'), { host: 'x', why: 'busy', marked: false }, 'the 33rd marker was made');
  // a marker whose change is already in the file stops nothing and is removed
  const d2 = new FakeDisk();
  const store = refusalsStore(d2, () => d2.now, instant);
  await store.commit({ kind: 'refuse', host: 'h', utc: NOW });
  d2.files.set('unsure-a.json', JSON.stringify({ v: 1, kind: 'refuse', host: 'h', utc: NOW }));
  const w2 = windowOver(new Backing(), d2);
  await w2.keeper.allow();
  assert.equal((await admit(w2, 'g')).kind, 'read');
  assert.equal(d2.markers().length, 0, 'a marker whose change is in the file was not removed');
  // the disk refuses the file and takes the marker
  const d3 = new FakeDisk();
  const bk = new Backing();
  await windowOver(bk, d3).keeper.allow();
  d3.failReplace = true;
  const w3 = windowOver(bk, d3);
  await w3.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.equal(d3.markers().length, 1);
  assert.equal((await admit(windowOver(bk, d3), 'g')).kind, 'unwritten');
  d3.failReplace = false;
  assert.equal((await admit(windowOver(bk, d3), 'g')).kind, 'read');
  assert.deepEqual(d3.refusedHosts(), ['h']);
});

test('A1-T27 (the keeper half): a writer that woke up after a take-over loses nobody\'s refusal', async () => {
  const disk = new FakeDisk();
  const backing = new Backing();
  const b = windowOver(backing, disk, { mode: 'whole-state' });
  const c = windowOver(backing, disk, { mode: 'whole-state' });
  await b.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  await admit(c, 'probe');
  // the copy in the list is gone too (a stale whole-state write), so only the memory of the newest file can bring the refusal back
  backing.values.set(KEY, []);
  b.view.sync();
  c.view.sync();
  const written = disk.file() as { gen: number };
  // a stale writer renames a file of the same gen and another writer over it: it holds g and not h
  disk.files.set('refused.json', textOf({ v: 1, gen: written.gen, by: 'woke-up', refused: [{ host: 'g', utc: NOW }] }));
  assert.equal((await admit(b, 'h')).kind, 'refused', 'the remembered refusal did not count between the rename and the union');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h'], 'the union was not written');
  // B is closed before its next read; a third window that had read the newer file writes the union
  assert.notEqual((await admit(c, 'probe')).kind, 'refused');
  await b.keeper.commit({ kind: 'person', host: 'y', answer: 'no' });
  backing.values.set(KEY, []);
  b.view.sync();
  c.view.sync();
  assert.notEqual((await admit(c, 'probe')).kind, 'refused');
  disk.files.set('refused.json', textOf({ v: 1, gen: written.gen + 1, by: 'woke-up-too', refused: [{ host: 'g', utc: NOW }] }));
  assert.notEqual((await admit(c, 'probe')).kind, 'refused');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h', 'y'], 'a window that saw the newer file did not write the union of all three refusals');
  // no window that saw it: the copy is gone too, the host is not refused (residual 5b, asserted so that it is seen)
  const d = new FakeDisk();
  const bk = new Backing();
  await windowOver(bk, d).keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  d.files.set('refused.json', textOf({ v: 1, gen: 9, by: 'woke-up', refused: [] }));
  bk.values.set(KEY, []);
  assert.notEqual((await admit(windowOver(bk, d), 'h')).kind, 'refused', 'residual 5b: nothing remembered and no copy');
});

test('A1-T15 (T15): the choices of the hosts list are the listed hosts and the hosts of the standings, the keeper\'s standing first', async () => {
  const refused: HostStanding = { host: 'h', answer: 'no', how: 'person', askedUtc: NOW };
  // an empty list, and a file that refuses `h`: the standings hold `h`, the list does not
  assert.deepEqual(choicesOf(listedHosts([], []), [refused]).map((one) => [one.host, one.standing?.answer]), [['h', 'no']]);
  // both hold it: the keeper's standing wins
  const asked = choicesOf(listedHosts([row('h', 'yes')], []), [refused]);
  assert.deepEqual(asked.map((one) => [one.host, one.standing?.answer]), [['h', 'no']]);
  // the host of a live terminal with no answer stays — over a list that can be read too — and the order is by code unit
  const mixed = choicesOf(listedHosts([row('z', 'yes'), 'junk'], ['q']), [refused]);
  assert.deepEqual(mixed.map((one) => one.host), ['h', 'q', 'z']);
  assert.equal(mixed.find((one) => one.host === 'q')?.standing, undefined);
  const readable = choicesOf(listedHosts([row('z', 'yes')], ['q', 'a']), [refused]);
  assert.deepEqual(readable.map((one) => one.host), ['a', 'h', 'q', 'z'], 'refused ∪ live ∪ stored, in code-unit order');
  // through the keeper: a refusal only in the file is among the standings
  const disk = new FakeDisk();
  const a = windowOver(new Backing(), disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  const b = windowOver(new Backing(), disk);
  const standings = await b.keeper.standings();
  assert.deepEqual(choicesOf(listedHosts(b.keeper.stored(), []), standings).map((one) => one.host), ['h'], 'a refusal that is only in the side file could not be chosen to be changed');
});

