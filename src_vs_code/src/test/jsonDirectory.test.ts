import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type DirEntry, type JsonDirectoryShape, JsonDirectorySnapshot, type SnapshotFs, jsonRecordFiles } from '../jsonDirectory';

/**
 * The snapshot behind the three record watchers (S4b items 10 and 12), over a FAKE file system: what a refresh keeps
 * and drops, the last good snapshot, and the generation guard — a slower, older read never overwrites a newer one.
 */

interface Rec {
  readonly id: string;
  readonly status: string;
  readonly root: string;
}

const SHAPE: JsonDirectoryShape<string, Rec> = {
  subdir: 'records',
  records: jsonRecordFiles,
  parse: (text, root) => {
    try {
      const raw = JSON.parse(text) as Partial<Rec>;
      return typeof raw.id === 'string' ? { id: raw.id, status: raw.status ?? '', root } : undefined;
    } catch {
      return undefined;
    }
  },
  keep: (one) => one.status !== 'gone',
  fileOf: (one) => `${one.id}.json`,
  signature: (items) => items.map((one) => `${one.root}/${one.id}:${one.status}`).sort((a, b) => a.localeCompare(b)).join('|'),
};

class Absent extends Error {}

/** A file system of plain maps — root → name → text — that a test can make slow, absent or failing per call. */
function fakeFs(): SnapshotFs<string> & {
  files: Map<string, Map<string, string>>;
  listing: (root: string) => Promise<void>;
  failList: Set<string>;
  failRead: Set<string>;
} {
  const files = new Map<string, Map<string, string>>();
  const fs = {
    files,
    failList: new Set<string>(),
    failRead: new Set<string>(),
    listing: async (_root: string): Promise<void> => {},
    list: async (root: string): Promise<readonly DirEntry[]> => {
      const snapshot = files.get(root);
      await fs.listing(root);
      if (fs.failList.has(root)) {
        throw new Error('EBUSY: a rename landed on an open handle');
      }
      if (snapshot === undefined) {
        throw new Absent('ENOENT');
      }
      return [...snapshot.keys()].map((name) => ({ name, isFile: true }));
    },
    read: async (root: string, _subdir: string, name: string): Promise<string> => {
      if (fs.failRead.has(name)) {
        throw new Error('EBUSY');
      }
      const text = files.get(root)?.get(name);
      if (text === undefined) {
        throw new Absent('ENOENT');
      }
      return text;
    },
    isAbsent: (error: unknown) => error instanceof Absent,
  };

  return fs;
}

function put(fs: ReturnType<typeof fakeFs>, root: string, id: string, status: string): void {
  const dir = fs.files.get(root) ?? new Map<string, string>();
  dir.set(`${id}.json`, JSON.stringify({ id, status }));
  fs.files.set(root, dir);
}

function snapshotOver(fs: SnapshotFs<string>): JsonDirectorySnapshot<string, Rec> {
  return new JsonDirectorySnapshot(SHAPE, fs, (root) => root);
}

/** A promise and the hand that resolves it. */
function gate(): { readonly opened: Promise<void>; readonly open: () => void } {
  let open = (): void => {};
  const opened = new Promise<void>((resolve) => { open = resolve; });

  return { opened, open };
}

test('a refresh superseded by a LATER-started one does not overwrite the snapshot — a slow old read never wins (item 10)', async () => {
  const fs = fakeFs();
  put(fs, 'D:/data', 'a', 'consulting');
  const snapshot = snapshotOver(fs);
  const slow = gate();
  let calls = 0;
  // The FIRST listing is held: it read the directory as it was, and resolves only after the second has landed.
  fs.listing = async () => {
    calls += 1;
    if (calls === 1) {
      await slow.opened;
    }
  };

  const first = snapshot.refresh(['D:/data']);
  await Promise.resolve();
  put(fs, 'D:/data', 'a', 'answered');
  const second = await snapshot.refresh(['D:/data']);
  assert.equal(second, true, 'the newer read changed what a person sees');
  // The first listing's TEXT is read after the change too, but its generation is older: what it would have read
  // as "consulting" is simulated by putting the old state back before releasing it.
  put(fs, 'D:/data', 'a', 'consulting');
  slow.open();
  const late = await first;

  assert.equal(late, false, 'a superseded refresh tells nobody');
  assert.deepEqual(snapshot.items.map((one) => one.status), ['answered'], 'the older read overwrote the newer snapshot');
});

test('an absent directory is no records; one that cannot be read keeps its last good snapshot', async () => {
  const fs = fakeFs();
  const snapshot = snapshotOver(fs);

  assert.equal(await snapshot.refresh(['D:/data']), false, 'absent and empty look the same to a person');
  put(fs, 'D:/data', 'a', 'consulting');
  assert.equal(await snapshot.refresh(['D:/data']), true);
  fs.failList.add('D:/data');
  assert.equal(await snapshot.refresh(['D:/data']), false, 'a busy directory changes nothing');
  assert.deepEqual(snapshot.items.map((one) => one.id), ['a'], 'the region is not blanked for a rename on an open handle');
});

test("a shape that says unreadable: 'none' shows nothing for a directory or a file it cannot read — the escalation cards' rule", async () => {
  const fs = fakeFs();
  put(fs, 'C:/own', 'mine', 'open');
  put(fs, '//nas/data', 'theirs', 'open');
  put(fs, '//nas/data', 'other', 'open');
  const snapshot = new JsonDirectorySnapshot<string, Rec>({ ...SHAPE, unreadable: 'none' }, fs, (root) => root);
  await snapshot.refresh(['C:/own', '//nas/data']);
  assert.deepEqual(snapshot.items.map((one) => one.id).sort(), ['mine', 'other', 'theirs']);

  fs.failList.add('//nas/data');
  await snapshot.refresh(['C:/own', '//nas/data']);
  assert.deepEqual(snapshot.items.map((one) => one.id), ['mine'], "the unplugged share shows no cards, and this window's own still do");

  fs.failList.clear();
  fs.failRead.add('theirs.json');
  await snapshot.refresh(['C:/own', '//nas/data']);
  assert.deepEqual(snapshot.items.map((one) => one.id).sort(), ['mine', 'other'], 'a file it cannot read is not shown either');
});

test('one file that cannot be read this pass keeps its last copy; one that does not parse is not a record', async () => {
  const fs = fakeFs();
  put(fs, 'D:/data', 'a', 'consulting');
  put(fs, 'D:/data', 'b', 'consulting');
  const snapshot = snapshotOver(fs);
  await snapshot.refresh(['D:/data']);

  fs.failRead.add('a.json');
  fs.files.get('D:/data')!.set('b.json', '{ torn');
  await snapshot.refresh(['D:/data']);

  assert.deepEqual(snapshot.items.map((one) => one.id), ['a']);
});

test('a write in flight and a subdirectory are not records; a kept-out record is dropped', async () => {
  assert.deepEqual(jsonRecordFiles([
    { name: 'a.json', isFile: true },
    { name: 'b.tmp.json', isFile: true },
    { name: 'c.json.tmp', isFile: true },
    { name: 'answers', isFile: false },
  ]), ['a.json']);

  const fs = fakeFs();
  put(fs, 'D:/data', 'a', 'gone');
  const snapshot = snapshotOver(fs);
  await snapshot.refresh(['D:/data']);
  assert.deepEqual(snapshot.items, []);
});

test('several roots are read apart: one failing keeps ITS last snapshot, and a root no longer watched is dropped', async () => {
  const fs = fakeFs();
  put(fs, 'C:/own', 'mine', 'open');
  put(fs, '//wsl/data', 'theirs', 'open');
  const snapshot = snapshotOver(fs);
  await snapshot.refresh(['C:/own', '//wsl/data']);

  fs.failList.add('//wsl/data');
  put(fs, 'C:/own', 'second', 'open');
  await snapshot.refresh(['C:/own', '//wsl/data']);
  assert.deepEqual(snapshot.items.map((one) => one.id).sort(), ['mine', 'second', 'theirs']);

  await snapshot.refresh(['C:/own']);
  assert.deepEqual(snapshot.items.map((one) => one.id).sort(), ['mine', 'second'], 'the setting no longer names the share');
});

test('a record is kept as it is SHOWN at the refresh — the shape decides, and the signature sees it', async () => {
  const fs = fakeFs();
  put(fs, 'D:/data', 'a', 'consulting');
  let now = 1_000;
  const shown = new JsonDirectorySnapshot<string, Rec>(
    { ...SHAPE, shown: (one, at) => (one.status === 'consulting' && at > 5_000 ? { ...one, status: 'interrupted' } : one) },
    fs, (root) => root, () => now);

  await shown.refresh(['D:/data']);
  assert.equal(shown.items[0]!.status, 'consulting');
  now = 10_000;
  assert.equal(await shown.refresh(['D:/data']), true, 'the same file, shown differently later, is a change a person sees');
  assert.equal(shown.items[0]!.status, 'interrupted');
});
