import assert from 'node:assert/strict';
import { test } from 'node:test';

import { existingHere } from '../pathFamily';
import { groupedPaths, groupOf, NOTHING_ASKED } from '../pathGroups';

/**
 * A shared list of folders in three groups (todo/PLAN_paths_per_side.md E3.3; the cadence consultation for epics 4-6,
 * finding c): this side's, the other side's, and both sides / unclear — never two, and nothing hidden. The disk is asked
 * through the real `existingHere`, so what the groups are built from is what the host asks.
 */

/** A host disk holding exactly `folders`, recording every path it is asked about. */
function disk(folders: readonly string[], unreadable: readonly string[] = []): { asked: string[]; isDirectory: (path: string) => Promise<boolean> } {
  const asked: string[] = [];

  return {
    asked,
    isDirectory: async (path) => {
      asked.push(path);
      if (unreadable.includes(path)) {
        throw Object.assign(new Error('EACCES'), { code: 'EACCES' });
      }

      return folders.includes(path);
    },
  };
}

const WINDOWS_LIST = ['D:\\old-project', '/work', '/home/jinx/git', '//nas/share/x', 'work', '\\\\server\\share\\y', '/locked'];

test('on Windows: its own spellings and an existing root-relative folder are this side\'s, a WSL path the other side\'s, the rest unclear', async () => {
  const here = disk(['C:\\work'], ['C:\\locked']);
  const grouped = groupedPaths(WINDOWS_LIST, true, await existingHere(WINDOWS_LIST, true, 'C:', here.isDirectory));

  assert.deepEqual(grouped, {
    thisSide: ['D:\\old-project', '/work', '\\\\server\\share\\y'],
    otherSide: ['/home/jinx/git'],
    unclear: ['//nas/share/x', 'work', '/locked'],
  });
});

test('in WSL: the same kind of list groups the other way — a Windows path is the other side\'s, the share and the bare name still unclear', async () => {
  const list = ['/home/jinx/git', 'C:\\work', '//nas/share/x', 'work', '\\\\server\\share\\y'];
  const grouped = groupedPaths(list, false, await existingHere(list, false, 'C:', disk([]).isDirectory));

  assert.deepEqual(grouped, { thisSide: ['/home/jinx/git'], otherSide: ['C:\\work', '\\\\server\\share\\y'], unclear: ['//nas/share/x', 'work'] });
});

test('a share and a relative name are unclear on BOTH platforms — never this window\'s on each side', () => {
  for (const entry of ['//nas/share/x', 'work', 'C:drive-relative']) {
    assert.deepEqual([groupOf(entry, true, NOTHING_ASKED), groupOf(entry, false, NOTHING_ASKED)], ['unclear', 'unclear'], entry);
  }
});

test('an entry the disk could not answer for is unclear, never the other side\'s on a guess', async () => {
  const here = disk([], ['C:\\home\\jinx\\git']);

  assert.equal(groupOf('/home/jinx/git', true, await existingHere(['/home/jinx/git'], true, 'C:', here.isDirectory)), 'unclear');
});

test('the grouping asks only THIS host\'s disk, at this machine\'s own spelling — never the other side\'s disk', async () => {
  const here = disk(['C:\\work']);
  await existingHere(WINDOWS_LIST, true, 'C:', here.isDirectory);

  assert.deepEqual(here.asked, ['C:\\work', 'C:\\home\\jinx\\git', 'C:\\locked'], 'a root-relative entry on the system drive, nothing else');
  assert.equal(here.asked.some((path) => path.startsWith('\\\\wsl') || path.startsWith('/mnt/')), false);
});

test('nothing is dropped and nothing is moved between lists — every entry once, in the list\'s order', () => {
  const grouped = groupedPaths(WINDOWS_LIST, true, NOTHING_ASKED);

  assert.deepEqual([...grouped.thisSide, ...grouped.otherSide, ...grouped.unclear].sort(), [...WINDOWS_LIST].sort());
});
