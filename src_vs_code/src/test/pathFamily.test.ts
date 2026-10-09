import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';

import { RootExistence, existingHere, otherSideHere, qualified, spelledForTheOtherOs, systemDriveOf } from '../pathFamily';

/**
 * The TS half of `shared/path-family-vectors.json` — which roots are the OTHER operating system's. The server skips
 * exactly those (`QuestionRoots.OtherSide`, answered by `QuestionConsultSettingsTests.cs`); the Settings page says
 * "the other side's" beside exactly those. Two self-consistent loaders cannot notice they disagree, so both answer one
 * file. The JSON is validated into shape rather than cast.
 */

interface Vector {
  readonly path: string;
  readonly onWindows: boolean;
  readonly elsewhere: boolean;
  readonly why: string;
}

function vectors(): readonly Vector[] {
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const rows: unknown = (JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>)['vectors'];
  assert.ok(Array.isArray(rows), 'shared/path-family-vectors.json carries no `vectors` array');

  return rows.map((row: unknown, at: number) => {
    const one = row as Record<string, unknown>;
    assert.equal(typeof one['path'], 'string', `vector ${at} has no path`);
    assert.equal(typeof one['onWindows'], 'boolean', `vector ${at} has no Windows verdict`);
    assert.equal(typeof one['elsewhere'], 'boolean', `vector ${at} has no verdict for Linux, WSL or macOS`);

    return { path: one['path'] as string, onWindows: one['onWindows'] as boolean, elsewhere: one['elsewhere'] as boolean, why: String(one['why'] ?? '') };
  });
}

test('which roots are the other side\'s answers the shared vectors, on both platforms, as the server does', () => {
  const all = vectors();
  assert.ok(all.length > 5, 'the file is read, not an empty array');
  assert.ok(all.some((v) => v.onWindows) && all.some((v) => v.elsewhere), 'both directions are in the file');

  for (const vector of all) {
    assert.equal(spelledForTheOtherOs(vector.path, true), vector.onWindows, `on Windows: ${vector.path} — ${vector.why}`);
    assert.equal(spelledForTheOtherOs(vector.path, false), vector.elsewhere, `elsewhere: ${vector.path} — ${vector.why}`);
  }
});

test('the decision asks whether the root EXISTS here, and answers the shared existence vectors as the server does', () => {
  // On Windows `/work` is the folder `work` at the root of the current drive: spelled like WSL, and this side's when it is there.
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const rows: unknown = (JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>)['existence'];
  assert.ok(Array.isArray(rows) && rows.length > 2, 'shared/path-family-vectors.json carries no `existence` array');

  for (const [at, row] of rows.entries()) {
    const one = row as Record<string, unknown>;
    for (const key of ['windows', 'existsHere', 'otherSide']) {
      assert.equal(typeof one[key], 'boolean', `existence vector ${at} has no ${key}`);
    }
    assert.equal(
      otherSideHere(String(one['path']), one['windows'] as boolean, one['existsHere'] as boolean),
      one['otherSide'],
      `${String(one['path'])} (windows: ${String(one['windows'])}, exists: ${String(one['existsHere'])}) — ${String(one['why'] ?? '')}`,
    );
  }
});

test('the window asks the disk only about roots spelled for the other OS, and keeps the ones that are folders here', async () => {
  const asked: string[] = [];
  const isDirectory = async (one: string): Promise<boolean> => { asked.push(one); return one === 'Q:\\work'; };

  // Q: is no drive any window stands on: the disk is asked on the SYSTEM drive, never the current one (the code round).
  const found = await existingHere(['/work', '/home/jinx/git', 'D:\\rsd'], true, 'Q:', isDirectory);

  assert.deepEqual(found, ['/work'], 'the root-relative folder that exists is this side\'s, named as it is stored; the WSL one is not');
  assert.deepEqual(asked, ['Q:\\work', 'Q:\\home\\jinx\\git'], 'asked on the system drive, and a root of this OS\'s own spelling not at all');
  assert.deepEqual(await existingHere(['/work'], true, 'C:', async () => { throw new Error('EACCES'); }), [], 'a root the disk cannot answer for is not claimed as this side\'s');
});

test('a root is qualified the way the server qualifies it, answering the shared resolution vectors', () => {
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const rows: unknown = (JSON.parse(fs.readFileSync(file, 'utf8')) as Record<string, unknown>)['resolution'];
  assert.ok(Array.isArray(rows) && rows.length > 4, 'shared/path-family-vectors.json carries no `resolution` array');

  for (const [at, row] of rows.entries()) {
    const one = row as Record<string, unknown>;
    assert.equal(typeof one['windows'], 'boolean', `resolution vector ${at} has no windows`);
    assert.equal(
      qualified(String(one['path']), one['windows'] as boolean, String(one['systemDrive'])),
      one['qualified'],
      `${String(one['path'])} (windows: ${String(one['windows'])}, drive: ${String(one['systemDrive'])}) — ${String(one['why'] ?? '')}`,
    );
  }
  assert.equal(systemDriveOf('E:'), 'E:');
  assert.equal(systemDriveOf(undefined), 'C:', 'unset falls back to C:, as the server does');
  assert.equal(systemDriveOf('  '), 'C:');
});

test('a repaint with the same roots asks the disk nothing; changed roots, another drive or a forget ask again — one stat at a time', async () => {
  let inFlight = 0;
  let most = 0;
  const asked: string[] = [];
  const isDirectory = async (one: string): Promise<boolean> => {
    asked.push(one);
    inFlight += 1;
    most = Math.max(most, inFlight);
    await new Promise((resolve) => { setTimeout(resolve, 1); });
    inFlight -= 1;

    return one === 'C:\\work';
  };
  const answers = new RootExistence(isDirectory);

  assert.deepEqual(await answers.of(['/work', '/home/a', '/home/b'], true, 'C:'), ['/work']);
  assert.deepEqual(await answers.of(['/work', '/home/a', '/home/b'], true, 'C:'), ['/work'], 'the cached answer');
  assert.equal(asked.length, 3, 'two renders with the same roots stat once');
  assert.equal(most, 1, 'the disk is asked one root at a time');

  await answers.of(['/work'], true, 'C:');
  assert.equal(asked.length, 4, 'a changed root list stats again');
  await answers.of(['/work'], true, 'D:');
  assert.equal(asked.length, 5, 'another system drive stats again');
  answers.forget();
  await answers.of(['/work'], true, 'D:');
  assert.equal(asked.length, 6, 'an explicit change of the roots setting stats again');
});
