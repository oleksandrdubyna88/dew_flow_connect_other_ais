import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, test } from 'node:test';

import { NOTHING_CHOSEN, useStorageSettings } from '../dataDir';
import { makeDataDirectory, makeDataDirectorySync } from '../dataDirMaker';

/**
 * A named data root that is not there is an expected outcome of the one maker, so it is ANSWERED as a value — never
 * thrown (the E5 code round, convention "expected failures are values, not exceptions"). Every caller decides what the
 * refusal means for it; an exception is left to what nobody expects: a disk that will not take a folder for another
 * reason.
 */

let parent = '';

beforeEach(() => {
  parent = mkdtempSync(join(tmpdir(), 'coai-made-'));
});

afterEach(() => {
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  rmSync(parent, { recursive: true, force: true });
});

test('a missing named root is answered as a refusal value, async and sync, naming the root — and nothing is made', async () => {
  const root = join(parent, 'gone');
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);

  const answered = await makeDataDirectory(join(root, 'windows')).then((made) => made, (reason: unknown) => ({ threw: String(reason) }));
  let syncAnswer: unknown;
  try {
    syncAnswer = makeDataDirectorySync(join(root, 'windows'));
  } catch (reason) {
    syncAnswer = { threw: String(reason) };
  }

  assert.deepEqual({ ...answered, sentence: undefined }, { made: false, refused: 'missing-named-root', root, sentence: undefined }, 'the missing root was thrown, not answered');
  assert.match((answered as { sentence?: string }).sentence ?? '', /was not found on this machine .*Nothing is written under it until it exists/u);
  assert.deepEqual({ ...(syncAnswer as object), sentence: undefined }, { made: false, refused: 'missing-named-root', root, sentence: undefined }, 'the sync form threw the missing root');
  assert.equal(existsSync(root), false);
});

test('a folder made, or already there, is answered made', async () => {
  const root = join(parent, 'nas');
  mkdirSync(root);
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);

  assert.deepEqual(await makeDataDirectory(join(root, 'windows', 'deep')), { made: true });
  assert.deepEqual(await makeDataDirectory(join(root, 'windows', 'deep')), { made: true });
  assert.deepEqual(makeDataDirectorySync(join(root, 'windows')), { made: true });
  assert.deepEqual(await makeDataDirectory(join(parent, 'elsewhere', 'x')), { made: true }, 'a path outside the named root is made as before');
});

test('what nobody expects still throws: a level the disk will not take is its failure, not a missing root', async () => {
  // A NUL in a name: refused by Node on every platform (`ERR_INVALID_ARG_VALUE`). A file in a folder's place is no use
  // here — Windows answers ENOENT for a folder under a file, the very code that means "the root is gone".
  const root = join(parent, 'nas');
  mkdirSync(root);
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);

  await assert.rejects(makeDataDirectory(join(root, 'bad\0name')), (reason: unknown) => !/was not found on this machine/u.test(String(reason)));
  assert.throws(() => makeDataDirectorySync(join(root, 'bad\0name')), (reason: unknown) => !/was not found on this machine/u.test(String(reason)));
});
