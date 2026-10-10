import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import { hostExecutableSide } from '../hostSide';
import { promises as fsp } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { ROOT_ANSWER_LIFETIME_MS, RootExistence, directoryAt, executableHere, existingHere, familyOf, fileAt, otherSideHere, pathForThisSide, qualified, spelledForTheOtherOs, systemDriveOf, useFileProbe, type ExecutableSide, type FilePresence } from '../pathFamily';

/**
 * The TS half of `shared/path-family-vectors.json` — which roots are the OTHER operating system's. The server skips
 * exactly those (`QuestionRoots.OtherSide`, answered by `QuestionConsultSettingsTests.cs`); the Settings page says
 * "the other side's" beside exactly those. Two self-consistent loaders cannot notice they disagree, so both answer one
 * file. The JSON is validated into shape rather than cast.
 */

/** One vector row, checked: every field read through its type, a missing or mistyped one failing with where it is. */
interface Row {
  readonly where: string;
  text(name: string): string;
  flag(name: string): boolean;
  /** A flag a row may leave out — false when absent, and a boolean when present. */
  optionalFlag(name: string): boolean;
}

/** A parsed JSON value as a vector row — a non-null, non-array object, or a failure naming the row (never a TypeError). */
function rowOf(value: unknown, where: string): Row {
  assert.ok(typeof value === 'object' && value !== null && !Array.isArray(value), `${where} is a vector row — a JSON object, not ${JSON.stringify(value)}`);
  const fields = new Map(Object.entries(value));
  const field = (name: string, kind: 'string' | 'boolean'): unknown => {
    const one = fields.get(name);
    assert.equal(typeof one, kind, `${where}.${name} is a ${kind}, not ${JSON.stringify(one)}`);

    return one;
  };

  return {
    where,
    text: (name) => String(field(name, 'string')),
    flag: (name) => field(name, 'boolean') === true,
    optionalFlag: (name) => fields.has(name) && field(name, 'boolean') === true,
  };
}

/** A section of `shared/path-family-vectors.json`, every row checked. */
function rowsOf(section: string): readonly Row[] {
  const file = path.join(__dirname, '..', '..', '..', 'shared', 'path-family-vectors.json');
  const parsed: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
  assert.ok(typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed), 'shared/path-family-vectors.json is not a JSON object');
  const rows = new Map(Object.entries(parsed)).get(section);
  assert.ok(Array.isArray(rows), `shared/path-family-vectors.json carries no \`${section}\` array`);

  return rows.map((row: unknown, at: number) => rowOf(row, `${section}[${at}]`));
}

test('a vector row that is not an object fails naming the row, not with a TypeError from the reader', () => {
  assert.throws(() => rowOf(null, 'existence[0]'), /existence\[0\] is a vector row — a JSON object, not null/);
  assert.throws(() => rowOf([], 'existence[1]'), /existence\[1\] is a vector row/);
  assert.throws(() => rowOf({ path: 3 }, 'existence[2]').text('path'), /existence\[2\]\.path is a string, not 3/);
  assert.equal(rowOf({ windows: true }, 'x').optionalFlag('unknownHere'), false, 'an optional flag left out is false');
});

test('which roots are the other side\'s answers the shared vectors, on both platforms, as the server does', () => {
  const all = rowsOf('vectors');
  assert.ok(all.length > 5, 'the file is read, not an empty array');
  assert.ok(all.some((v) => v.flag('onWindows')) && all.some((v) => v.flag('elsewhere')), 'both directions are in the file');

  for (const vector of all) {
    assert.equal(spelledForTheOtherOs(vector.text('path'), true), vector.flag('onWindows'), `on Windows: ${vector.text('path')} — ${vector.text('why')}`);
    assert.equal(spelledForTheOtherOs(vector.text('path'), false), vector.flag('elsewhere'), `elsewhere: ${vector.text('path')} — ${vector.text('why')}`);
  }
});

test('the decision asks whether the root EXISTS here, and answers the shared existence vectors as the server does', () => {
  // On Windows `/work` is the folder `work` (on the system drive): spelled like WSL, and this side's when it is there — or
  // when the disk could not tell (`unknownHere`, the third code round): only a CONFIRMED absence is the other side's.
  const all = rowsOf('existence');
  assert.ok(all.length > 2, 'the file is read, not an empty array');
  assert.ok(all.some((v) => v.optionalFlag('unknownHere')), 'the unknown state is pinned in the file both halves read');

  for (const one of all) {
    assert.equal(
      otherSideHere(one.text('path'), one.flag('windows'), one.flag('existsHere'), one.optionalFlag('unknownHere')),
      one.flag('otherSide'),
      `${one.where}: ${one.text('path')} (windows: ${one.flag('windows')}, exists: ${one.flag('existsHere')}, unknown: ${one.optionalFlag('unknownHere')}) — ${one.text('why')}`,
    );
  }
});

test('the window asks the disk only about roots spelled for the other OS, and keeps the ones that are folders here', async () => {
  const asked: string[] = [];
  const isDirectory = async (one: string): Promise<boolean> => { asked.push(one); return one === 'Q:\\work'; };

  // Q: is no drive any window stands on: the disk is asked on the SYSTEM drive, never the current one (the code round).
  const found = await existingHere(['/work', '/home/jinx/git', 'D:\\rsd'], true, 'Q:', isDirectory);

  assert.deepEqual(found, { existing: ['/work'], unknown: [] }, 'the root-relative folder that exists is this side\'s, named as it is stored; the WSL one is not');
  assert.deepEqual(asked, ['Q:\\work', 'Q:\\home\\jinx\\git'], 'asked on the system drive, and a root of this OS\'s own spelling not at all');
});

test('a root the disk could not answer for is UNKNOWN — neither this side\'s nor called the other side\'s', async () => {
  // The code round: EACCES or EBUSY is not "absent here". Only ENOENT / ENOTDIR say there is no folder.
  const refused = Object.assign(new Error('EACCES: permission denied'), { code: 'EACCES' });
  const found = await existingHere(['/work', '/home/jinx/git'], true, 'C:', async (one) => {
    if (one === 'C:\\work') {
      throw refused;
    }

    return false;
  });

  assert.deepEqual(found, { existing: [], unknown: ['/work'] }, 'the root the disk would not answer for is kept apart, the absent one is not');
  assert.equal(otherSideHere('/work', true, false, true), false, 'an unknown root is never classified as the other side\'s');
  assert.equal(otherSideHere('/home/jinx/git', true, false, false), true, 'a root known to be absent still is');
});

test('a stat answers present, absent — ENOENT or ENOTDIR only — or throws for anything it could not tell', async () => {
  const failing = (code: string) => async (): Promise<never> => { throw Object.assign(new Error(code), { code }); };
  const being = (directory: boolean) => async () => ({ isDirectory: () => directory });

  assert.equal(await directoryAt(being(true))('x'), true);
  assert.equal(await directoryAt(being(false))('x'), false, 'a file is no folder');
  assert.equal(await directoryAt(failing('ENOENT'))('x'), false);
  assert.equal(await directoryAt(failing('ENOTDIR'))('x'), false);
  for (const code of ['EACCES', 'EBUSY', 'EPERM', 'EIO']) {
    await assert.rejects(directoryAt(failing(code))('x'), { code }, `${code} is not "absent here"`);
  }
});

test('an unspellable root is ABSENT on the real stat, as the server\'s probe answers it — never unknown', async () => {
  // The fourth code round: a NUL made Node throw ERR_INVALID_ARG_VALUE, which this probe called unknown while the
  // server's called it absent. A name that cannot be spelled can never be this machine's folder.
  const unspellable = rowsOf('existence').filter((row) => row.optionalFlag('unspellable'));
  assert.ok(unspellable.length > 0, 'the file pins an unspellable root for both halves');
  const real = directoryAt((one) => fsp.stat(one));
  for (const row of unspellable) {
    assert.equal(await real(qualified(row.text('path'), process.platform === 'win32', 'C:')), false, `${row.where}: ${row.text('why')}`);
  }
  assert.equal(await real(path.join(os.tmpdir(), 'coai-no-such<folder')), false, 'a name Windows cannot spell is no folder either');
});

test('an answer holding an unknown root is not kept: the next render asks the disk again', async () => {
  let calls = 0;
  const answers = new RootExistence(async () => {
    calls += 1;
    if (calls === 1) {
      throw Object.assign(new Error('EBUSY'), { code: 'EBUSY' });
    }

    return true;
  });

  assert.deepEqual(await answers.of(['/work'], true, 'C:'), { existing: [], unknown: ['/work'] });
  assert.deepEqual(await answers.of(['/work'], true, 'C:'), { existing: ['/work'], unknown: [] }, 'asked again, and now known');
  assert.equal(calls, 2);
});

test('a root is qualified the way the server qualifies it, answering the shared resolution vectors', () => {
  const all = rowsOf('resolution');
  assert.ok(all.length > 4, 'the file is read, not an empty array');

  for (const one of all) {
    assert.equal(
      qualified(one.text('path'), one.flag('windows'), one.text('systemDrive')),
      one.text('qualified'),
      `${one.where}: ${one.text('path')} (windows: ${one.flag('windows')}, drive: ${one.text('systemDrive')}) — ${one.text('why')}`,
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

  assert.deepEqual((await answers.of(['/work', '/home/a', '/home/b'], true, 'C:')).existing, ['/work']);
  assert.deepEqual((await answers.of(['/work', '/home/a', '/home/b'], true, 'C:')).existing, ['/work'], 'the cached answer');
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

test('a definitive answer lives ROOT_ANSWER_LIFETIME_MS: within it one stat, after it a fresh one — a folder made or removed later is seen', async () => {
  // The fourth code round: kept until the root list changed, a folder created after the first paint stayed "the other
  // side's" on the page while the server, which reads the disk on every start, already used it.
  let now = 1_000;
  let asked = 0;
  let exists = false;
  const answers = new RootExistence(async () => { asked += 1; return exists; }, () => now);

  assert.deepEqual((await answers.of(['/work'], true, 'C:')).existing, []);
  now += ROOT_ANSWER_LIFETIME_MS - 1;
  exists = true;
  assert.deepEqual((await answers.of(['/work'], true, 'C:')).existing, [], 'within the lifetime the kept answer stands');
  assert.equal(asked, 1, 'one stat within the lifetime');

  now += 1;
  assert.deepEqual((await answers.of(['/work'], true, 'C:')).existing, ['/work'], 'after it, the disk is asked again and the new folder is seen');
  assert.equal(asked, 2);
  assert.equal(ROOT_ANSWER_LIFETIME_MS, 60_000, 'the lifetime is the named constant the docs state');
});

test('a CLI path of the other OS is skipped — the PATH lookup — and named; answers the shared executable vectors as the server does', () => {
  // todo/PLAN_paths_per_side.md E1.2: a Windows `codex.cmd` read in a WSL window was probed as a file there, and the card
  // said "cannot review". It is the Windows side's CLI: this side runs the runtime's own name from PATH instead.
  const all = rowsOf('executable');
  assert.ok(all.length > 5, 'the file is read, not an empty array');
  assert.ok(all.some((v) => v.flag('otherSide')) && all.some((v) => !v.flag('otherSide')), 'both answers are in the file');

  assert.ok(all.some((v) => v.flag('existsHere') && !v.flag('otherSide')), 'an existing root-relative file is pinned in the file');
  assert.ok(all.some((v) => v.optionalFlag('unknownHere')), 'the unknown state is pinned in the file');

  for (const vector of all) {
    const family = vector.flag('windows') ? 'windows' : 'posix';
    const asked: string[] = [];
    const presence: FilePresence = vector.flag('existsHere') ? 'here' : vector.optionalFlag('unknownHere') ? 'unknown' : 'absent';
    const side: ExecutableSide = { family, systemDrive: vector.text('systemDrive'), fileAt: (path) => { asked.push(path); return presence; } };
    const answer = pathForThisSide({ executablePath: vector.text('path') }, side);
    const said = `${family}: ${JSON.stringify(vector.text('path'))} — ${vector.text('why')}`;
    assert.equal(answer.path, vector.text('here'), said);
    assert.equal(answer.otherSide, vector.flag('otherSide') ? vector.text('path').trim() : '', said);
    assert.equal(executableHere(vector.text('path'), side), vector.text('here'), said);
    assert.ok(asked.every((path) => path === qualified(vector.text('path'), family === 'windows', vector.text('systemDrive'))), `${said}: asked the disk about ${JSON.stringify(asked)}`);
  }
});

test('the consultant\'s case: an existing root-relative CLI on Windows runs qualified with the system drive, not a PATH install', () => {
  // E1 cadence consultation: `/Program Files/nodejs/node.exe --version` launches from C:\ — a lexical skip silently ran a
  // different installation. The disk is injected, so this is the same test on any machine.
  const side = (presence: FilePresence): ExecutableSide => ({ family: 'windows', systemDrive: 'Q:', fileAt: (path) => (path === 'Q:\\Program Files\\nodejs\\node.exe' ? presence : 'absent') });

  assert.deepEqual(pathForThisSide({ executablePath: '/Program Files/nodejs/node.exe' }, side('here')), { path: 'Q:\\Program Files\\nodejs\\node.exe', otherSide: '' });
  assert.deepEqual(pathForThisSide({ executablePath: '/Program Files/nodejs/node.exe' }, side('unknown')), { path: 'Q:\\Program Files\\nodejs\\node.exe', otherSide: '' }, 'skipped on a guess');
  assert.deepEqual(pathForThisSide({ executablePath: '/Program Files/nodejs/node.exe' }, side('absent')), { path: '', otherSide: '/Program Files/nodejs/node.exe' });
});

test('the host side asks the INSTALLED probe — and before activation installs one, every path is unknown and kept', () => {
  const windows = hostExecutableSide('win32');
  try {
    useFileProbe(() => 'unknown');
    assert.equal(executableHere('/usr/local/bin/codex', windows), `${windows.systemDrive}\\usr\\local\\bin\\codex`, 'skipped on a guess before the probe was installed');
    useFileProbe(() => 'absent');
    assert.equal(executableHere('/usr/local/bin/codex', windows), '', 'the installed probe is the one asked');
  } finally {
    useFileProbe(fileAt(fs.statSync));
  }
});

test('a file probe says here for a file, absent only when the disk said so, unknown otherwise — on the real stat too', () => {
  const failing = (code: string) => (): never => { throw Object.assign(new Error(code), { code }); };
  assert.equal(fileAt(() => ({ isFile: () => true }))('x'), 'here');
  assert.equal(fileAt(() => ({ isFile: () => false }))('x'), 'absent', 'a directory where the CLI should be is no CLI');
  for (const code of ['ENOENT', 'ENOTDIR', 'ERR_INVALID_ARG_VALUE']) {
    assert.equal(fileAt(failing(code))('x'), 'absent', code);
  }
  for (const code of ['EACCES', 'EBUSY', 'EIO', 'EPERM']) {
    assert.equal(fileAt(failing(code))('x'), 'unknown', code);
  }
  const real = fileAt(fs.statSync);
  assert.equal(real(__filename), 'here');
  assert.equal(real(__dirname), 'absent');
  assert.equal(real(path.join(__dirname, 'no-such-cli.exe')), 'absent');
});

test('the family of a host is windows for win32 alone — WSL, Linux and macOS spell their paths alike', () => {
  assert.equal(familyOf('win32'), 'windows');
  assert.equal(familyOf('linux'), 'posix');
  assert.equal(familyOf('darwin'), 'posix');
});
