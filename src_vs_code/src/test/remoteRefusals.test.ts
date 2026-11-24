import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';
import { OwnerSeam, ownerGone } from '../chatStoreLock';
import {
  EMPTY_FILE, REFUSALS_BYTES_MAX, RefusalChange, RefusalsFile, refusalsOf, refusalsStore, textOf, waitingOf, withChange,
} from '../remoteRefusals';
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
  for (const bad of ['not json', '[]', '{}', '{"v":2,"gen":1,"by":"x","refused":[]}', '{"v":1}', '{"v":1,"gen":0,"by":"x","refused":[]}', '{"v":1,"gen":1,"by":"","refused":[]}',
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
  const disk = refusalsDiskOver(dir, { file: 'refused.json', markerPrefix: 'unsure-' }, machine);
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
