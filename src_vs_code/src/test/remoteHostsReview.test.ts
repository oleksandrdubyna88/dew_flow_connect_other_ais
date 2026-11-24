import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { EMPTY_FILE, textOf } from '../remoteRefusals';
import { Backing, FakeDisk, KEY, row, windowOver } from './hostsFakes';

/**
 * The Opus review of E1.S1 (findings 1 to 5): the keeper does what § S1.3, § S1.4 and § S1.5 already say. Pure: a fake `globalState` and
 * a fake folder, as in `remoteHosts.test.ts`.
 */

const NOW = '2026-11-24T10:00:00.000Z';
const TORN = 'unsure-torn.json';
const fileOf = (...hosts: string[]): string => textOf({ ...EMPTY_FILE, gen: 3, by: 'someone', refused: hosts.map((host) => ({ host, utc: NOW })) });
const listOf = (backing: Backing): { host: string; answer: string }[] => (backing.values.get(KEY) as { host: string; answer: string }[] | undefined) ?? [];

test('R1: "Save now" over a torn marker commits the stop FIRST, and a stop the disk refuses is held and leaves the marker', async () => {
  const disk = new FakeDisk();
  const a = windowOver(new Backing(), disk);
  disk.files.set(TORN, '{');
  disk.failReplace = true;
  const result = await a.keeper.saveWaiting();
  assert.equal(result.kind, 'unwritten', 'a press whose stop the disk refused was answered as done');
  assert.ok(disk.files.has(TORN), 'the torn note was removed before the stop was written');
  assert.equal(a.keeper.savedTorn(), false, 'the press said a torn note was removed, and the stop is not saved');
  assert.equal(await a.keeper.roundStop(), 'stopped', 'the stop the disk refused was not held');
  assert.ok(a.unkept.some((one) => one.host === undefined), 'the stop that was not saved was not told');
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten');
  assert.equal((await a.keeper.saveWaiting()).kind, 'unwritten');
  assert.equal(a.unkept.filter((one) => one.host === undefined).length, 2, 'a second press that failed said nothing');
  assert.ok(disk.files.has(TORN), 'the torn note was removed by a press whose stop was not saved');
  disk.failReplace = false;
  assert.ok(['written', 'kept'].includes((await a.keeper.saveWaiting()).kind), 'the second press did not save the stop');
  assert.equal(a.keeper.savedTorn(), true);
  assert.equal(disk.markers().length, 0);
  assert.ok(disk.file()?.stop !== undefined, 'the stop is not in the file after the second press');
});

test('R2: a mend over a list AND a file that can be read is the plain change: a "no" keeps the file\'s refusal and goes copy first', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.files.set('refused.json', fileOf('h'));
  backing.values.set(KEY, []);
  const a = windowOver(backing, disk);
  assert.equal((await a.keeper.commit({ kind: 'mend', host: 'h', answer: 'no' })).kind, 'written');
  assert.deepEqual(disk.refusedHosts(), ['h'], 'a mend "no" over a readable store lifted the refusal of the file');
  assert.deepEqual(listOf(backing).map((one) => `${one.host}:${one.answer}`), ['h:no']);
  // a disk that refuses: the copy stands, the host is held and the answer is `unwritten`
  const refusing = new FakeDisk();
  refusing.files.set('refused.json', fileOf('x'));
  refusing.failReplace = true;
  const backing2 = new Backing();
  backing2.values.set(KEY, []);
  const b = windowOver(backing2, refusing);
  assert.deepEqual(await b.keeper.commit({ kind: 'mend', host: 'g', answer: 'no' }), { kind: 'unwritten', why: 'disk' });
  assert.deepEqual(listOf(backing2).map((one) => `${one.host}:${one.answer}`), ['g:no'], 'the copy of the "no" was not written first');
  assert.equal(await b.keeper.held(), 1);
  assert.equal(await b.keeper.consent('g'), 'no');
  // a Read lifts, as the plain change does
  assert.equal((await a.keeper.commit({ kind: 'mend', host: 'h', answer: 'yes' })).kind, 'written');
  assert.deepEqual(disk.refusedHosts(), []);
});

test('R3: a mend over a damaged list and a file that can be read applies the file half: Read lifts, "no" refuses', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.files.set('refused.json', fileOf('h'));
  backing.values.set(KEY, 'junk');
  const a = windowOver(backing, disk);
  assert.equal((await a.keeper.commit({ kind: 'mend', host: 'h', answer: 'yes' })).kind, 'written');
  assert.deepEqual(disk.refusedHosts(), [], 'a Read over a damaged list left the refusal in the file');
  assert.deepEqual(listOf(backing).map((one) => `${one.host}:${one.answer}`), ['h:yes']);

  const second = new Backing();
  const disk2 = new FakeDisk();
  second.values.set(KEY, [row('z', 'yes'), 'junk']);
  const b = windowOver(second, disk2);
  assert.equal((await b.keeper.commit({ kind: 'mend', host: 'q', answer: 'no' })).kind, 'written');
  assert.deepEqual(disk2.refusedHosts(), ['q'], 'a "no" over a damaged list was not written to the file');

  // a file that refuses the host: the Read stops there and the list stays as it was
  const third = new Backing();
  const disk3 = new FakeDisk();
  disk3.files.set('refused.json', fileOf('h'));
  const damaged = [row('h', 'no'), 'junk'];
  third.values.set(KEY, damaged);
  disk3.failReplace = true;
  const c = windowOver(third, disk3);
  assert.equal((await c.keeper.commit({ kind: 'mend', host: 'h', answer: 'yes' })).kind, 'unwritten');
  assert.equal(third.values.get(KEY), damaged, 'the list was written over although the file did not take the Read');
  disk3.failReplace = false;
  assert.equal((await c.keeper.commit({ kind: 'mend', host: 'h', answer: 'forget' })).kind, 'written');
  assert.deepEqual(disk3.refusedHosts(), []);
  assert.deepEqual(listOf(third), []);
});

test('R4: a change that comes back `full` is not landed: the hold and the marker stay', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  const a = windowOver(backing, disk);
  assert.deepEqual(await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' }), { kind: 'unwritten', why: 'busy' });
  assert.equal(disk.markers().length, 1);
  // another window fills the file with 100 other refusals, and the lock is let go
  disk.files.set('refused.json', fileOf(...Array.from({ length: 100 }, (_, n) => `r${n}`)));
  disk.lock = undefined;
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten', 'a refusal the file could not hold was taken for landed');
  assert.equal(disk.markers().length, 1, 'the marker of a refusal that is not in the file was removed');
  assert.equal(await a.keeper.held(), 1, 'the hold of a refusal that is not in the file was dropped');
  assert.equal(await a.keeper.consent('h'), 'no');
  // a window that did not know the file is full is told `full`, and the copy stands alone
  const other = new Backing();
  const stale = windowOver(other, disk);
  assert.deepEqual(await stale.keeper.commit({ kind: 'person', host: 'zz', answer: 'no' }), { kind: 'full' });
  assert.deepEqual(listOf(other).map((one) => `${one.host}:${one.answer}`), ['zz:no']);
});

test('R5: a marker that could not be made is tried again with the write', async () => {
  const backing = new Backing();
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  disk.failMark = true;
  const a = windowOver(backing, disk);
  assert.deepEqual(await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' }), { kind: 'unwritten', why: 'busy' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: false }]);
  assert.equal(disk.markers().length, 0);
  disk.failMark = false;
  assert.equal((await a.keeper.admit('g')).kind, 'unwritten');
  assert.equal(disk.markers().length, 1, 'the keeper never tried the marker again');
  assert.equal((await windowOver(new Backing(), disk).keeper.admit('g')).kind, 'unwritten', 'the second window of the side reads on although a marker could be made');
  disk.lock = undefined;
  assert.equal((await a.keeper.admit('g')).kind, 'read');
  assert.equal(disk.markers().length, 0);
  assert.deepEqual(disk.refusedHosts(), ['h']);
});

test('R7: "Save now" that fails tells the person again, with the new reason', async () => {
  const disk = new FakeDisk();
  disk.lock = { token: 'hung' };
  const a = windowOver(new Backing(), disk);
  await a.keeper.commit({ kind: 'person', host: 'h', answer: 'no' });
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: true }]);
  disk.failReplace = true;
  assert.equal((await a.keeper.saveWaiting()).kind, 'unwritten');
  assert.deepEqual(a.unkept, [{ host: 'h', why: 'busy', marked: true }, { host: 'h', why: 'disk', marked: true }], 'a press that failed said nothing');
  // an ordinary retry does not tell again
  await a.keeper.admit('g');
  assert.equal(a.unkept.length, 2, 'a retry by itself raised the notice again');
});
