import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { OwnerSeam, ownerGone } from '../chatStoreLock';
import { Grant, STAMP_MAX, grantOf, withdrawalName, withdrawalText } from '../remotePermission';
import {
  EMPTY_FILE, REFUSALS_BYTES_MAX, RefusalChange, RefusalsFile, refusalsOf, textOf, waitingOf, withChange,
} from '../remoteRefusals';
import { refusalsStore } from '../remoteRefusalsStore';
import { refusalsDiskOver } from '../remoteRefusalsDisk';
import { FakeDisk, instant } from './hostsFakes';
import { ownedTemp } from './ownedTemp';

/**
 * The refusals file (E1.S1, entry 1.2): its pure rules, its one writer over a fake disk and over a real folder with the real lock and
 * the real atomic write, and the owner rule of the lock.
 */

const T = '2026-11-24T10:00:00.000Z';
const fileOf = (hosts: readonly string[], extra: Partial<RefusalsFile> = {}): RefusalsFile => ({ v: 1, gen: 3, by: 'w', refused: hosts.map((host) => ({ host, utc: T })), ...extra });

test('A1-T21: nothing automatic shortens the file, by construction', () => {
  let seed = 7;
  const next = (n: number): number => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
  const hostName = (): string => `h${next(8)}`;
  const kinds: readonly (() => RefusalChange)[] = [
    () => ({ kind: 'refuse', host: hostName(), utc: T }),
    () => ({ kind: 'lift', host: hostName() }),
    () => ({ kind: 'stop', utc: T }),
    () => ({ kind: 'start' }),
    () => ({ kind: 'migrate', rows: [{ host: hostName(), utc: T }, { host: hostName(), utc: T }], utc: T }),
    () => ({ kind: 'regain', gen: next(9), rows: [{ host: hostName(), utc: T }], ...(next(2) === 0 ? { stop: { utc: T } } : {}) }),
    () => ({ kind: 'mend', gen: next(9), rows: [{ host: hostName(), utc: T }], utc: T }),
  ];
  for (let n = 0; n < 200; n += 1) {
    const file = fileOf(Array.from({ length: next(6) }, hostName), next(2) === 0 ? { stop: { utc: T } } : {});
    for (const make of kinds) {
      const change = make();
      const out = withChange(file, change, 'x');
      if (out.kind !== 'written') {
        continue;
      }
      const before = new Set(file.refused.map((one) => one.host));
      const after = new Set(out.file.refused.map((one) => one.host));
      const lost = [...before].filter((host) => !after.has(host));
      assert.deepEqual(lost, change.kind === 'lift' && before.has(change.host) ? [change.host] : [], `the migration or the merge removed a refusal (${change.kind})`);
      assert.equal(out.file.stop === undefined, change.kind === 'start' ? true : file.stop === undefined && change.kind !== 'stop' && !(change.kind === 'regain' && change.stop !== undefined), `the Stop changed under ${change.kind}`);
      assert.ok(out.file.gen > file.gen, 'gen did not rise');
      if (change.kind === 'regain') {
        assert.ok(out.file.gen > change.gen);
      }
    }
  }
  const full = fileOf(Array.from({ length: 100 }, (_, n) => `r${n}`));
  assert.equal(withChange(full, { kind: 'refuse', host: 'new', utc: T }, 'x').kind, 'full', 'the file dropped a “Not this host” to make room');
  assert.equal(withChange(fileOf(['a'], { by: 'w', pad: 'x'.repeat(REFUSALS_BYTES_MAX) } as never), { kind: 'refuse', host: 'b', utc: T }, 'x').kind, 'full');
  assert.equal(withChange(fileOf(['a']), { kind: 'mend', gen: 1, rows: [], utc: T }, 'x').kind, 'kept');
});

test('A1-T23 (the read half): what the file reads as', () => {
  const ok = textOf({ ...fileOf(['a', 'b']), migratedUtc: T, stop: { utc: T } });
  assert.deepEqual(refusalsOf(ok)?.refused.map((one) => one.host), ['a', 'b']);
  for (const bad of ['not json', '[]', '{}', '{"v":2,"gen":1,"by":"x","refused":[]}', '{"v":1}', '{"v":1,"gen":0,"by":"x","refused":[]}', '{"v":1,"gen":1,"by":"","refused":[]}', '{"v":1,"gen":9007199254740992,"by":"x","refused":[]}',
    '{"v":1,"gen":1,"by":"x","refused":[{"host":"","utc":"t"}]}', '{"v":1,"gen":1,"by":"x","refused":[],"stop":5}', JSON.stringify({ ...fileOf([]), refused: Array.from({ length: 101 }, (_, n) => ({ host: `h${n}`, utc: T })) }), ' '.repeat(REFUSALS_BYTES_MAX + 1)]) {
    assert.equal(refusalsOf(bad), undefined, `a file that cannot be read was read: ${bad.slice(0, 40)}`);
  }
  assert.deepEqual(refusalsOf(JSON.stringify({ ...fileOf([]), later: 1 })) as unknown as Record<string, unknown>, { ...fileOf([]), later: 1 }, 'a key this build does not read was dropped');
  assert.equal(waitingOf('m', '{').kind, 'torn');
  assert.equal(waitingOf('m', undefined).kind, 'torn');
  assert.equal(waitingOf('m', '{"v":1,"kind":"stop","utc":"t"}').kind, 'stop');
  assert.equal(waitingOf('m', '{"v":2,"kind":"stop","utc":"t"}').kind, 'torn');
  assert.equal(waitingOf('m', `{"v":1,"kind":"refuse","host":"h","utc":"t","pad":"${'x'.repeat(2000)}"}`).kind, 'torn');
});

test('A1-T22: the writer — one at a time, and a change is not kept unless it is seen', async () => {
  const disk = new FakeDisk();
  const store = refusalsStore(disk, () => disk.now, instant);
  // two writers at once
  const both = await Promise.all([store.commit({ kind: 'refuse', host: 'h', utc: T }), store.commit({ kind: 'refuse', host: 'g', utc: T })]);
  assert.ok(both.every((one) => one.kind === 'written'), JSON.stringify(both));
  assert.deepEqual(disk.refusedHosts(), ['g', 'h'], 'two windows refused two hosts at once and one refusal was lost');
  // gen and by
  const first = disk.file();
  await store.commit({ kind: 'refuse', host: 'k', utc: T });
  assert.equal((disk.file()?.gen ?? 0), (first?.gen ?? 0) + 1);
  assert.notEqual(disk.file()?.by, first?.by);
  // a lost change that stays lost: the fake drops every write of one writer
  const dropper = new FakeDisk();
  dropper.replace = async () => { dropper.replaces += 1; };
  const lost = await refusalsStore(dropper, () => dropper.now, instant).commit({ kind: 'refuse', host: 'x', utc: T });
  assert.deepEqual(lost, { kind: 'unwritten', why: 'lost' });
  // a held claim whose owner lives: busy, 0 replaces, and not taken by age
  const held = new FakeDisk();
  held.lock = { token: 'hung' };
  held.now += 3_600_000;
  assert.deepEqual(await refusalsStore(held, () => held.now, instant).commit({ kind: 'refuse', host: 'x', utc: T }), { kind: 'unwritten', why: 'busy' });
  assert.equal(held.replaces, 0);
  held.ownerAlive = false;
  assert.equal((await refusalsStore(held, () => held.now, instant).commit({ kind: 'refuse', host: 'x', utc: T })).kind, 'written');
  held.lock = { token: 'alive' };
  held.ownerAlive = true;
  assert.equal((await refusalsStore(held, () => held.now, instant).commit({ kind: 'refuse', host: 'y', utc: T }, 'person')).kind, 'written');
  // a disk that refuses
  const full = new FakeDisk();
  full.failReplace = true;
  assert.deepEqual(await refusalsStore(full, () => full.now, instant).commit({ kind: 'refuse', host: 'x', utc: T }), { kind: 'unwritten', why: 'disk' });
  assert.equal(full.lock, undefined, 'the claim was not released');
  // the removals are by identity: another writer refuses h again between the rename and the read-back
  const byId = new FakeDisk();
  const writer = refusalsStore(byId, () => byId.now, instant);
  await writer.commit({ kind: 'refuse', host: 'h', utc: T });
  const raced = refusalsStore(byId, () => byId.now, instant);
  const origRead = byId.read;
  let reads = 0;
  byId.read = async () => {
    reads += 1;
    if (reads === 2) {
      const now = byId.file() as RefusalsFile;
      byId.files.set('refused.json', textOf({ ...now, gen: now.gen + 1, by: 'other', refused: [{ host: 'h', utc: '2026-11-25T00:00:00.000Z' }] }));
    }

    return origRead();
  };
  assert.equal((await raced.commit({ kind: 'lift', host: 'h' })).kind, 'written');
  assert.deepEqual(byId.refusedHosts(), ['h'], 'a person\'s new refusal was lifted by a second round');
});

test('A1-T22: a writer that was frozen does not replace after a take-over (the pause after the ownership check)', async () => {
  const disk = new FakeDisk();
  const a = refusalsStore(disk, () => disk.now, instant);
  const b = refusalsStore(disk, () => disk.now, instant);
  await a.commit({ kind: 'refuse', host: 'x', utc: T });
  let release: () => void = () => undefined;
  let entered: () => void = () => undefined;
  const reached = new Promise<void>((resolve) => { entered = resolve; });
  const gate = new Promise<void>((resolve) => { release = resolve; });
  disk.beforeReplace = async () => { disk.beforeReplace = undefined; entered(); await gate; };
  const frozen = a.commit({ kind: 'refuse', host: 'g', utc: T });
  await reached;
  // a person's press takes the lock over and writes h
  assert.equal((await b.commit({ kind: 'refuse', host: 'h', utc: T }, 'person')).kind, 'written');
  release();
  // Its rename put its stale file over the newer one; its step 7 saw that the lock was not its own and it began again: its second round is kept.
  assert.equal((await frozen).kind, 'kept');
  assert.equal(disk.replaces, 3, 'the frozen writer did not write exactly once');
  assert.deepEqual(disk.refusedHosts(), ['g', 'x'], 'the fixture did not lose h, so the keeper half below proves nothing');
  // A window that had read the newer file writes the union back: A1-T27 of remoteHosts.test.ts (the keeper's regain).
});

test('A1-T27 (the rule): no lock is taken over by its age, and only a gone owner counts', () => {
  const lock = (note: { host?: string; boot?: string; pid?: number } | undefined, at = 0) => ({ at, signature: 's', ...(note === undefined ? {} : { note: { pid: note.pid ?? 5, at: 'x', token: 't', doing: 'd', ...(note.host === undefined ? {} : { host: note.host }), ...(note.boot === undefined ? {} : { boot: note.boot }) } }) });
  const seam = (alive: (pid: number) => void, boot: string | undefined = 'b1'): OwnerSeam => ({ host: () => 'm', boot: () => boot, alive });
  const gone = (code: string) => (): void => { throw Object.assign(new Error(code), { code }); };
  const rule = (s: OwnerSeam, l: ReturnType<typeof lock>) => ownerGone(s)(l, 1_000_000_000);
  assert.notEqual(rule(seam(() => undefined), lock({ host: 'm', boot: 'b0' })), undefined, 'another boot is gone');
  assert.notEqual(rule(seam(gone('ESRCH')), lock({ host: 'm', boot: 'b1' })), undefined, 'ESRCH is gone');
  for (const [what, s, l] of [
    ['EPERM', seam(gone('EPERM')), lock({ host: 'm', boot: 'b1' })],
    ['another error', seam(gone('EIO')), lock({ host: 'm' })],
    ['a process that answers', seam(() => undefined), lock({ host: 'm', boot: 'b1' })],
    ['another host', seam(gone('ESRCH')), lock({ host: 'other', boot: 'b0' })],
    ['a note that cannot be read', seam(gone('ESRCH')), lock(undefined)],
    ['no boot id on either side', seam(() => undefined, undefined), lock({ host: 'm' })],
    ['a lock one hour old with a live owner', seam(() => undefined), lock({ host: 'm', boot: 'b1' }, 0)],
  ] as const) {
    assert.equal(rule(s, l), undefined, `a lock was taken from an owner that was not shown to be gone: ${what}`);
  }
});

test('A1-T22 and A1-T26 over the real lock, the real atomic write and the real marker create', async () => {
  const dir = ownedTemp('coai-refusals');
  const machine: OwnerSeam = { host: () => 'm', boot: () => undefined, alive: () => undefined };
  const disk = refusalsDiskOver(dir, { file: 'refused.json', markerPrefix: 'unsure-', withdrawnPrefix: 'withdrawn-' }, machine);
  const store = refusalsStore(disk, Date.now, instant);
  const results = await Promise.all(['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'].map((host) => store.commit({ kind: 'refuse', host, utc: T })));
  assert.ok(results.every((one) => one.kind === 'written'), JSON.stringify(results));
  const file = refusalsOf(readFileSync(join(dir, 'refused.json'), 'utf8'));
  assert.equal(file?.refused.length, 8, 'eight racers, and a refusal was lost');
  assert.ok(!existsSync(join(dir, 'refused.lock')), 'the lock was left behind');
  assert.deepEqual(readdirSync(dir).filter((name) => name.endsWith('.tmp')), [], 'a beside-file was left');
  // markers: an exclusive create beside a held lock, listed, read, removed
  writeFileSync(join(dir, 'refused.lock'), JSON.stringify({ pid: process.pid, at: new Date().toISOString(), token: 'someone', doing: 'x', host: 'm' }));
  assert.deepEqual(await store.commit({ kind: 'refuse', host: 'z', utc: T }), { kind: 'unwritten', why: 'busy' });
  assert.equal(await store.mark({ kind: 'refuse', host: 'z', utc: T }), true, 'a held lock stopped the marker');
  const read = await store.read();
  assert.ok(read.kind === 'file' && read.waiting.length === 1 && read.waiting[0]?.kind === 'refuse');
  const name = read.kind === 'file' ? read.waiting[0]?.name ?? '' : '';
  await store.unmark(name);
  assert.ok(!existsSync(join(dir, name)));
  for (let n = 0; n < 32; n += 1) {
    assert.equal(await disk.mark('{}') !== undefined, true);
  }
  assert.equal(await disk.mark('{}'), undefined, 'the 33rd marker was made');
  assert.equal(EMPTY_FILE.gen, 0);
});

test('A1-T22 (step 5): a writer that slept before its last look at the claim does not replace', async () => {
  const disk = new FakeDisk();
  const a = refusalsStore(disk, () => disk.now, instant);
  const b = refusalsStore(disk, () => disk.now, instant);
  await a.commit({ kind: 'refuse', host: 'x', utc: T });
  let entered: () => void = () => undefined;
  let release: () => void = () => undefined;
  const reached = new Promise<void>((resolve) => { entered = resolve; });
  const gate = new Promise<void>((resolve) => { release = resolve; });
  disk.beforeSweep = async () => { disk.beforeSweep = undefined; entered(); await gate; };
  const frozen = a.commit({ kind: 'refuse', host: 'g', utc: T });
  await reached;
  disk.now += 40_000;
  assert.equal((await b.commit({ kind: 'refuse', host: 'h', utc: T }, 'person')).kind, 'written');
  release();
  assert.equal((await frozen).kind, 'written');
  assert.deepEqual(disk.refusedHosts(), ['g', 'h', 'x'], 'a writer that slept for 40 s wrote over a refusal given meanwhile');
});


// ───────────────────────── E2.S1: the grant in the file, Allow, Withdraw, the copy-in and free ─────────────────────────

const U1 = '2026-11-26T10:00:00.000Z';
const U0 = '2026-11-26T09:00:00.000Z';
const U2 = '2026-11-26T11:00:00.000Z';
const wname = (stamp: number): string => withdrawalName('withdrawn-', stamp);
const storeOver = (disk: FakeDisk) => refusalsStore(disk, () => disk.now, instant);
const grantFile = (stamp: number | undefined, extra: Record<string, unknown> = {}): string =>
  textOf({ v: 1, gen: 4, by: 'seed', refused: [], ...(stamp === undefined ? {} : { grant: { utc: U0, stamp } }), ...extra } as RefusalsFile);
/** A fake side with its file and withdrawal files already there. `bad` are names with the prefix whose text cannot be read. */
function sideWith(grantStamp: number | undefined, files: readonly number[] = [], bad: readonly number[] = []): FakeDisk {
  const disk = new FakeDisk();
  if (grantStamp !== undefined) {
    disk.files.set('refused.json', grantFile(grantStamp));
  }
  for (const stamp of files) {
    disk.files.set(wname(stamp), withdrawalText(stamp, U0));
  }
  for (const stamp of bad) {
    disk.files.set(wname(stamp), '{');
  }

  return disk;
}
const stampOnDisk = (disk: FakeDisk): number | undefined => grantOf(JSON.parse(disk.files.get('refused.json') ?? '{}') as { grant?: unknown })?.stamp;
const noCopy = (): undefined => undefined;

test('E2S1-T1 (the writes): a write of any other change keeps the stored grant value as it is — well made or not — a mend drops it, a regain carries the file\'s', () => {
  const stored: readonly unknown[] = [{ utc: U0, stamp: 3 }, { utc: U0, stamp: 3, extra: 1 }, 'junk', { utc: U0, stamp: 0 }, [], { utc: 5, stamp: 1 }, { stamp: 2 }];
  let seed = 3;
  const next = (n: number): number => { seed = (seed * 1103515245 + 12345) % 2147483648; return seed % n; };
  const hostName = (): string => `h${next(6)}`;
  const kinds: readonly (() => RefusalChange)[] = [
    () => ({ kind: 'refuse', host: hostName(), utc: T }),
    () => ({ kind: 'lift', host: hostName() }),
    () => ({ kind: 'stop', utc: T }),
    () => ({ kind: 'start' }),
    () => ({ kind: 'migrate', rows: [{ host: hostName(), utc: T }], utc: T }),
    () => ({ kind: 'regain', gen: next(9), rows: [{ host: hostName(), utc: T }], ...(next(2) === 0 ? { stop: { utc: T } } : {}) }),
  ];
  for (const value of stored) {
    for (let n = 0; n < 60; n += 1) {
      const file = fileOf(Array.from({ length: next(4) }, hostName), { grant: value } as Partial<RefusalsFile>);
      for (const make of kinds) {
        const out = withChange(file, make(), 'x');
        if (out.kind === 'written') {
          assert.deepEqual(out.file.grant, value, `a write dropped or changed the stored grant ${JSON.stringify(value)}`);
        }
      }
    }
  }
  // text round trip: a write by this writer keeps a grant it does not know the shape of
  const text = JSON.stringify({ v: 1, gen: 1, by: 'x', refused: [], grant: { utc: U0, stamp: 3, future: true } });
  const read = refusalsOf(text);
  assert.ok(read !== undefined);
  const out = withChange(read, { kind: 'refuse', host: 'h', utc: T }, 'y');
  assert.equal(out.kind === 'written' && JSON.stringify(out.file.grant), JSON.stringify({ utc: U0, stamp: 3, future: true }), 'a write rebuilt the object from its known keys and dropped the grant');
  // a malformed grant is no grant AND the file is not damaged: its refusals and its Stop still read
  const bad = refusalsOf(JSON.stringify({ v: 1, gen: 1, by: 'x', refused: [{ host: 'h', utc: T }], stop: { utc: T }, grant: { utc: U0, stamp: 'three' } }));
  assert.ok(bad !== undefined, 'a malformed grant made the file damaged');
  assert.deepEqual(bad.refused, [{ host: 'h', utc: T }]);
  assert.ok(bad.stop !== undefined);
  assert.equal(grantOf(bad), undefined);
  // a mend builds from nothing: no grant
  const mended = withChange('damaged', { kind: 'mend', gen: 4, rows: [], utc: T }, 'x');
  assert.ok(mended.kind === 'written' && mended.file.grant === undefined, 'a mend kept a grant');
  // a regain carries the grant of the file it was merged into, never a remembered one
  const regained = withChange(fileOf(['a'], { grant: { utc: U0, stamp: 2 } } as Partial<RefusalsFile>), { kind: 'regain', gen: 9, rows: [{ host: 'z', utc: T }] }, 'x');
  assert.ok(regained.kind === 'written' && JSON.stringify(regained.file.grant) === JSON.stringify({ utc: U0, stamp: 2 }));
  // a grant cannot be sent through commit (a type test)
  const disk = new FakeDisk();
  // @ts-expect-error — the store's `commit` takes a plain change: only `allow` writes a grant
  void storeOver(disk).commit({ kind: 'grant', utc: U1, stamp: 5, floor: 1 });
});

test('E2S1-T3: Allow — the stamp one above its floor, one floor file left, read back; the merger refuses a low stamp; a grant in force is kept', async () => {
  // no withdrawal: stamp 1, no file
  const none = sideWith(undefined);
  const first = await storeOver(none).allow({ utc: U1, copy: noCopy });
  assert.ok(first.kind === 'given' && first.grant.stamp === 1 && first.floor === 0);
  assert.deepEqual(none.withdrawnNames(), []);
  // grant 1 and withdrawn-2: stamp 3, exactly one file left (the floor, withdrawn-2), read back
  const two = sideWith(1, [2]);
  const second = await storeOver(two).allow({ utc: U1, copy: noCopy });
  assert.ok(second.kind === 'given' && second.grant.stamp === 3 && second.floor === 2);
  assert.deepEqual(two.withdrawnNames(), [wname(2)]);
  assert.equal(stampOnDisk(two), 3);
  // files 2, 5, 7: stamp 8, file 7 left
  const many = sideWith(undefined, [2, 5, 7]);
  const third = await storeOver(many).allow({ utc: U1, copy: noCopy });
  assert.ok(third.kind === 'given' && third.grant.stamp === 8);
  assert.deepEqual(many.withdrawnNames(), [wname(7)]);
  // the floor from the copy alone: copy 9, no file: withdrawn-9 is made, stamp 10
  const copied = sideWith(undefined);
  const fourth = await storeOver(copied).allow({ utc: U1, copy: () => 9 });
  assert.ok(fourth.kind === 'given' && fourth.grant.stamp === 10 && fourth.floor === 9);
  assert.deepEqual(copied.withdrawnNames(), [wname(9)]);
  // the pure merger refuses a stamp that is not above its floor
  assert.equal(withChange(fileOf([]), { kind: 'grant', utc: U1, stamp: 5, floor: 5 }, 'x').kind, 'low');
  assert.equal(withChange(fileOf([]), { kind: 'grant', utc: U1, stamp: 4, floor: 5 }, 'x').kind, 'low');
  assert.equal(withChange(fileOf([]), { kind: 'grant', utc: U1, stamp: 6, floor: 5 }, 'x').kind, 'written');
  assert.equal(withChange(fileOf([]), { kind: 'grant', utc: U1, stamp: 0, floor: -1 }, 'x').kind, 'low', 'a stamp that is not a stamp was written');
  assert.equal(withChange(fileOf([]), { kind: 'grant', utc: U1, stamp: STAMP_MAX + 1, floor: 1 }, 'x').kind, 'low');
  // a file whose grant is in force: kept, zero calls of replace, withdrawn and unwithdraw
  const held = sideWith(3, [1, 2]);
  const kept = await storeOver(held).allow({ utc: U1, copy: () => 2 });
  assert.equal(kept.kind, 'kept');
  assert.deepEqual(held.calls.filter((one) => one === 'replace' || one.startsWith('withdrawn:') || one.startsWith('unwithdraw:')), []);
  // two Allows at once: the second meets the first one's floor file as a new name, finds the grant in force and answers kept, not withdrawn
  const twice = sideWith(1, [2]);
  const [a, b] = await Promise.all([storeOver(twice).allow({ utc: U1, copy: noCopy }), storeOver(twice).allow({ utc: U1, copy: noCopy })]);
  assert.deepEqual([a.kind, b.kind].sort(), ['given', 'kept']);
});

test('E2S1-T4: Withdraw — one above what it sees, an exclusive create, no claim, pruning from the lowest, a read-back that looks at the lock first', async () => {
  const at = async (disk: FakeDisk, above = 0) => storeOver(disk).withdraw({ utc: U1, above });
  // one above the highest of the grant, a file, the name of an unreadable file, and `above`
  for (const [grantStamp, files, bad, above, want] of [[3, [], [], 0, 4], [undefined, [5], [], 0, 6], [undefined, [], [8], 0, 9], [undefined, [], [], 11, 12]] as const) {
    const disk = sideWith(grantStamp, files, bad);
    const done = await at(disk, above);
    assert.ok(done.kind === 'withdrawn' && done.stamp === want, `the stamp of the withdrawal: ${JSON.stringify(done)} wanted ${want}`);
    assert.ok(disk.files.has(wname(want)));
    assert.ok(!disk.calls.includes('claim') && !disk.calls.includes('claim:person'), 'a withdrawal took a claim');
  }
  // `there` is no error: another window made the same name between the listing and the create
  const there = sideWith(undefined);
  there.afterListing = (n) => { if (n === 1) { there.files.set(wname(1), withdrawalText(1, U0)); } };
  const raced = await at(there);
  assert.equal(raced.kind, 'withdrawn');
  // no path of any other operation removes or replaces a withdrawal file
  const others = sideWith(undefined, [1, 2, 3]);
  const store = storeOver(others);
  await store.commit({ kind: 'refuse', host: 'h', utc: T });
  await store.commit({ kind: 'lift', host: 'h' });
  await store.commit({ kind: 'stop', utc: T });
  await store.commit({ kind: 'start' });
  await store.commit({ kind: 'migrate', rows: [{ host: 'm', utc: T }], utc: T });
  await store.commit({ kind: 'regain', gen: 1, rows: [{ host: 'r', utc: T }] });
  await store.free();
  assert.deepEqual(others.called('unwithdraw:'), [], 'an operation of the file removed a withdrawal');
  assert.deepEqual(others.withdrawnNames(), [wname(1), wname(2), wname(3)]);
  // past 8 names the LOWEST readable stamp goes first, never the highest and never an unreadable one
  const full = sideWith(undefined, [3, 4, 5, 6, 7, 8, 9, 10]);
  const pruned = await at(full);
  assert.ok(pruned.kind === 'withdrawn' && pruned.stamp === 11);
  assert.ok(!full.files.has(wname(3)) && full.files.has(wname(10)) && full.files.has(wname(11)), 'the lowest did not go, or the highest did');
  assert.ok(full.withdrawnNames().length <= 8, 'the folder holds more than 8 withdrawal files');
  const blocked = sideWith(undefined, [], [1, 2, 3, 4, 5, 6, 7, 8]);
  const none = await at(blocked);
  assert.equal(none.kind, 'withdrawn');
  assert.equal(blocked.withdrawnNames().length, 8, 'a file was made or removed although 8 that cannot be read fill the folder');
  assert.deepEqual(blocked.called('unwithdraw:'), [], 'a file that cannot be read was pruned');
  // the name is built from the handed-in prefix
  const other = new FakeDisk();
  other.withdrawnPrefix = 'gone-';
  await at(other);
  assert.deepEqual(other.withdrawnNames(), ['gone-1.json']);
  assert.deepEqual([...other.files.keys()].filter((name) => name.startsWith('withdrawn-')), []);
});

test('E2S1-T4 (the lock): the read-back looks at the lock BEFORE the listing; held then free with a grant written between goes again; held to the end is busy; gone stands', async () => {
  const order = sideWith(undefined);
  await storeOver(order).withdraw({ utc: U1, above: 0 });
  const calls = order.calls.filter((one) => one === 'lockNow' || one === 'withdrawals');
  assert.deepEqual(calls.slice(-2), ['lockNow', 'withdrawals'], 'the read-back listed before it looked at the lock');
  // held, then free, and a grant was written in between: the withdrawal goes again from 4 and ends above the grant
  const between = sideWith(undefined);
  between.lockSays = 'held';
  between.beforeLockNow = (n) => { if (n === 2) { between.lockSays = 'free'; between.files.set('refused.json', grantFile(9)); between.files.set(wname(8), withdrawalText(8, U0)); } };
  const again = await storeOver(between).withdraw({ utc: U1, above: 0 });
  assert.ok(again.kind === 'withdrawn' && again.stamp === 10, `the withdrawal did not end above the grant: ${JSON.stringify(again)}`);
  // held to the end: busy, the file is in the folder, no claim was made
  const held = sideWith(undefined);
  held.lockSays = 'held';
  const busy = await storeOver(held).withdraw({ utc: U1, above: 0 });
  assert.deepEqual(busy, { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(held.withdrawnNames(), [wname(1)]);
  assert.ok(!held.calls.includes('claim') && !held.calls.includes('claim:person'));
  // gone: the read-back stands
  const gone = sideWith(undefined);
  gone.lockSays = 'gone';
  assert.equal((await storeOver(gone).withdraw({ utc: U1, above: 0 })).kind, 'withdrawn');
  // the disk refuses the file: unwritten, disk
  const refuses = sideWith(undefined);
  refuses.failWithdrawn = true;
  assert.deepEqual(await storeOver(refuses).withdraw({ utc: U1, above: 0 }), { kind: 'unwritten', why: 'disk' });
});

test('E2S1-T19 (the store): an Allow removes what cannot be read after the floor file and before the grant, and never a file that came after it began', async () => {
  // the order: the floor file is made BEFORE the first removal, the grant is written AFTER the last
  const disk = sideWith(1, [], [3]);
  const done = await storeOver(disk).allow({ utc: U1, copy: noCopy });
  assert.ok(done.kind === 'given', JSON.stringify(done));
  const at = (prefix: string): number => disk.calls.findIndex((one) => one.startsWith(prefix));
  assert.ok(at('withdrawn:') >= 0 && at('withdrawn:') < at('unwithdraw:'), 'the floor file was not made before the first removal');
  assert.ok(at('unwithdraw:') < disk.calls.indexOf('replace'), 'the grant was written before the last removal');
  assert.ok(!disk.files.has(wname(3)), 'the unreadable file stayed');
  assert.equal(stampOnDisk(disk), 2);
  // a file empty at the first read and whole at the second is a stamp and is not removed
  const blind = sideWith(1, [3]);
  blind.blind.set(wname(3), 2);
  const kept = await storeOver(blind).allow({ utc: U1, copy: noCopy });
  assert.ok(kept.kind === 'given' && kept.grant.stamp === 4, JSON.stringify(kept));
  assert.ok(!blind.called('unwithdraw:').includes(`unwithdraw:${wname(3)}`), 'a file that reads now was removed');
  assert.ok(blind.files.has(wname(3)));
  // a file that appears after the Allow began: withdrawn, never removed, nothing written
  const late = sideWith(1, [], [3]);
  late.afterListing = (n) => { if (n === 1) { late.files.set(wname(9), withdrawalText(9, U0)); } };
  const lateAnswer = await storeOver(late).allow({ utc: U1, copy: noCopy });
  assert.equal(lateAnswer.kind, 'withdrawn');
  assert.ok(!late.called('unwithdraw:').includes(`unwithdraw:${wname(9)}`));
  assert.equal(late.called('replace').length, 0);
  // a file that appears between the write and the read-back
  const after = sideWith(undefined);
  after.beforeReplace = async () => undefined;
  const replacing = after.replace;
  after.replace = async (text) => { await replacing(text); after.files.set(wname(1), withdrawalText(1, U0)); };
  const afterAnswer = await storeOver(after).allow({ utc: U1, copy: noCopy });
  assert.equal(afterAnswer.kind, 'withdrawn');
  // an unreadable file that cannot be removed: unwritten disk and no grant written
  const stuck = sideWith(1, [], [3]);
  stuck.failUnwithdraw = true;
  assert.deepEqual(await storeOver(stuck).allow({ utc: U1, copy: noCopy }), { kind: 'unwritten', why: 'disk' });
  assert.equal(stuck.called('replace').length, 0);
  // crowded: 20 names are not in force, and an Allow removes the ones that were not read
  const crowded = sideWith(undefined, Array.from({ length: 20 }, (_, n) => n + 1));
  const crowdedAnswer = await storeOver(crowded).allow({ utc: U1, copy: noCopy });
  assert.equal(crowdedAnswer.kind, 'given', JSON.stringify(crowdedAnswer));
  assert.ok(crowded.withdrawnNames().length <= 16);
  // the floor's name taken by a file that cannot be read: the floor is one higher
  const taken = sideWith(undefined, [], [4]);
  const takenAnswer = await storeOver(taken).allow({ utc: U1, copy: () => 4 });
  assert.ok(takenAnswer.kind === 'given' && takenAnswer.floor === 5 && takenAnswer.grant.stamp === 6, JSON.stringify(takenAnswer));
  // F5: a readable withdrawal whose utc is later than the Allow's, already there before step 0, is a later word
  const later = sideWith(undefined);
  later.files.set(wname(2), withdrawalText(2, U2));
  const laterAnswer = await storeOver(later).allow({ utc: U1, copy: noCopy });
  assert.equal(laterAnswer.kind, 'withdrawn');
  assert.equal(later.called('replace').length, 0);
  const earlier = sideWith(undefined);
  earlier.files.set(wname(2), withdrawalText(2, U0));
  assert.equal((await storeOver(earlier).allow({ utc: U1, copy: noCopy })).kind, 'given');
});

test('E2S1-T22 (the store): free makes ONE claim with take: person and writes nothing; copyIn is an exclusive create', async () => {
  const disk = sideWith(undefined);
  await storeOver(disk).free();
  assert.deepEqual(disk.calls.filter((one) => one.startsWith('claim')), ['claim:person']);
  assert.equal(disk.called('replace').length, 0);
  assert.equal(await storeOver(disk).copyIn(5, U1), 'written');
  assert.equal(await storeOver(disk).copyIn(5, U1), 'kept');
  disk.failWithdrawn = true;
  assert.equal(await storeOver(disk).copyIn(6, U1), 'unwritten');
  void ({} as Grant);
});

test('E2S1-T4 (the real disk): a withdrawal is an exclusive create in a real folder, the sweep and the writers never remove it, and a hostile name reads as unreadable', async () => {
  const dir = ownedTemp('coai-withdrawn');
  const machine: OwnerSeam = { host: () => 'm', boot: () => undefined, alive: () => undefined };
  const disk = refusalsDiskOver(dir, { file: 'refused.json', markerPrefix: 'unsure-', withdrawnPrefix: 'withdrawn-' }, machine);
  const store = refusalsStore(disk, Date.now, instant);
  // the exclusive create: made once, `there` after, listed with its text
  assert.equal(await disk.withdrawn(wname(3), withdrawalText(3, U0)), 'made');
  assert.equal(await disk.withdrawn(wname(3), withdrawalText(3, U0)), 'there');
  assert.equal(readFileSync(join(dir, wname(3)), 'utf8'), withdrawalText(3, U0));
  // names with the prefix that fit nothing else: a directory, a tmp name, a padded name, and a name over the top
  writeFileSync(join(dir, 'withdrawn-07.json'), withdrawalText(7, U0));
  writeFileSync(join(dir, 'withdrawn-4.json.tmp'), withdrawalText(4, U0));
  writeFileSync(join(dir, 'withdrawn-5.json'), '{');
  const listed = await disk.withdrawals();
  assert.ok(listed !== 'failed');
  assert.deepEqual(listed.seen.map((one) => one.name), [wname(5), wname(3), 'withdrawn-07.json', 'withdrawn-4.json.tmp'], 'the grammar names come first, the highest stamp first, then the rest in code-unit order');
  assert.equal(listed.seen.find((one) => one.name === wname(3))?.text, withdrawalText(3, U0));
  // every writer of the file, the sweep and a person's free leave every withdrawal file where it is
  await Promise.all(['a', 'b', 'c'].map((host) => store.commit({ kind: 'refuse', host, utc: T })));
  await disk.sweep(0);
  await store.free();
  assert.deepEqual(readdirSync(dir).filter((name) => name.startsWith('withdrawn-')).sort(), ['withdrawn-07.json', 'withdrawn-4.json.tmp', wname(3), wname(5)].sort(), 'a writer or the sweep removed a withdrawal file');
  // the lock as it is: free, held by a process that answers, and gone
  assert.equal(await disk.lockNow(), 'free');
  writeFileSync(join(dir, 'refused.lock'), JSON.stringify({ pid: process.pid, at: new Date().toISOString(), token: 'someone', doing: 'x', host: 'm' }));
  assert.equal(await disk.lockNow(), 'held');
  writeFileSync(join(dir, 'refused.lock'), '{');
  assert.equal(await disk.lockNow(), 'held', 'a note that cannot be read is held');
  const gone: OwnerSeam = { host: () => 'm', boot: () => undefined, alive: () => { throw Object.assign(new Error('no such process'), { code: 'ESRCH' }); } };
  writeFileSync(join(dir, 'refused.lock'), JSON.stringify({ pid: 999999, at: new Date().toISOString(), token: 'dead', doing: 'x', host: 'm' }));
  assert.equal(await refusalsDiskOver(dir, { file: 'refused.json', markerPrefix: 'unsure-', withdrawnPrefix: 'withdrawn-' }, gone).lockNow(), 'gone');
  assert.ok(existsSync(join(dir, 'refused.lock')), 'a look at the lock took it over or removed it');
  // an Allow over the real disk removes the unreadable names and leaves its floor file, and the grant is read back
  rmSync(join(dir, 'refused.lock'));
  rmSync(join(dir, 'withdrawn-4.json.tmp'));
  rmSync(join(dir, 'withdrawn-07.json'));
  const given = await store.allow({ utc: U1, copy: noCopy });
  assert.ok(given.kind === 'given' && given.floor === 3 && given.grant.stamp === 4, JSON.stringify(given));
  assert.deepEqual(readdirSync(dir).filter((name) => name.startsWith('withdrawn-')), [wname(3)]);
  assert.equal(grantOf(refusalsOf(readFileSync(join(dir, 'refused.json'), 'utf8')) ?? {})?.stamp, 4);
});
