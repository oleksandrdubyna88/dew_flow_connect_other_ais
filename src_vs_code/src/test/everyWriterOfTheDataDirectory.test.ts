import assert from 'node:assert/strict';
import { test } from 'node:test';

import { THROUGH_THE_MAKER, WRITERS, codeOf, shippedSources, sitesIn } from './dataDirWriters';

/**
 * The inventory of every writer that MAKES a directory (todo/PLAN_paths_per_side.md E5.0 (b), E5.7; the gate's
 * plan-round item B), derived by a scan rather than remembered: each one makes its folders through the one maker
 * (`dataDirMaker.ts`, which never makes a named root) — the async ones after the board's guard — or carries the reason it
 * can never touch the data directory: the extension's own storage, a temp folder of its own, or the folder a person
 * just picked. The table lives in `dataDirWriters.ts`; `everyWriterLeavesAMissingRootAbsent.test.ts` RUNS each writer.
 *
 * <p>Why a scan: the policy was verified on E1 as "the notice about a missing data directory creates it" — one writer
 * found by reading. A guard applied at the sites somebody remembered is the defect `security.md` counts four of; the
 * list below is the one the scan produces, and a new `mkdir` — or a VS Code write, which makes missing parent folders —
 * is red here until it is classified.</p>
 */

test('every writer that makes a directory is in the inventory with its sites — a new mkdir is red until it is classified', () => {
  const found = Object.fromEntries(shippedSources().map((file) => [file, sitesIn(file)] as const).filter(([, sites]) => sites > 0));
  const expected = Object.fromEntries(Object.entries(WRITERS).map(([file, one]) => [file, one.sites]));

  assert.deepEqual(found, expected, 'a directory-making call site appeared, moved or went: classify it in dataDirWriters.ts, with its guard or its reason');
});

/** Either guard: the sync one, or the one that waits for the first data-root answer (`refuseUnderMissingRootOnceSettled`). */
const GUARD = /refuseUnderMissingRoot(?:OnceSettled)?\(/u;

test('every writer guarded by the board actually asks it before it makes the directory', () => {
  for (const [file, one] of Object.entries(WRITERS).filter(([, writer]) => writer.guard === 'board')) {
    assert.ok(GUARD.test(codeOf(file)), `${file} is classified as guarded by the board and never calls refuseUnderMissingRoot — ${one.why}`);
  }
});

test('a writer that can wait WAITS for the first data-root answer — a guard read before the disk answered let the heartbeat create the root', () => {
  // E5's first run in a real editor: the chat store's heartbeat was written at activation beside the probe, through the
  // atomic write, and the board had no finding yet. Every async road asks the awaiting guard; only the sync form of the
  // atomic write — the one caller that cannot wait — keeps the sync one.
  for (const [file] of Object.entries(WRITERS).filter(([, writer]) => writer.guard === 'board')) {
    assert.ok(codeOf(file).includes('await refuseUnderMissingRootOnceSettled('), `${file} asks the board without waiting for the first data-root answer`);
  }
  assert.equal((codeOf('atomicFile.ts').match(/\brefuseUnderMissingRoot\(/gu) ?? []).length, 1, 'the sync guard belongs to the sync atomic write alone');
});

test('a writer under the data directory makes folders ONLY through the one maker — a recursive mkdir there recreates a root removed after the guard', () => {
  // The risk consultation's item F: the guard is a question asked before the act, and a root removed between the two was
  // made again by the writer's own recursive mkdir. Only `dataDirMaker.ts` may hold a raw mkdir for such a path.
  for (const [file] of Object.entries(WRITERS).filter(([, writer]) => writer.guard === 'board' || writer.guard === 'through the maker')) {
    assert.equal((codeOf(file).match(THROUGH_THE_MAKER) ?? []).length, sitesIn(file), `${file} makes a directory under the data directory by a road other than the one maker`);
  }
});

test('a writer classified as making no named root does NOT carry the guard — a guard there would say the folder can be missing when it cannot', () => {
  for (const [file] of Object.entries(WRITERS).filter(([, writer]) => writer.guard !== 'board')) {
    assert.equal(GUARD.test(codeOf(file)), false, `${file} carries the guard but is classified as never creating a named root`);
  }
});

test('the scan still finds the ledger\'s maker call, the atomic write\'s two and VS Code\'s parent-making writes — a scan that matched nothing would pass forever', () => {
  assert.equal(sitesIn('jsonlLedger.ts'), 1);
  assert.equal(sitesIn('atomicFile.ts'), 2);
  assert.ok(sitesIn('installer.ts') >= 3, 'VS Code\'s writeFile and copy are no longer seen');
  assert.ok(Object.values(WRITERS).some((one) => one.guard === 'board'), 'nothing is guarded, so the policy guards nothing');
});
