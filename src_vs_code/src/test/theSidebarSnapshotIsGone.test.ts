import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * The sidebar snapshot feature is deleted (v2 E4, story s6-dead-v1-out; owner decision 2026-11-23): the real native menu replaced it, and nothing may keep writing
 * its file. There is no program to run for an absence, so this reads the shipped tree: the two modules are not there, and no shipped source names the file, the
 * writer or the call that fed it. A scan that finds nothing proves nothing, so it first checks that it does read files (the entry's own source must be among them).
 */

const ROOT = process.cwd();

/** Every shipped source file under `src` (tests excluded) and `shell`, as `relative path -> text`. */
function shipped(): ReadonlyMap<string, string> {
  const found = new Map<string, string>();
  const walk = (dir: string): void => {
    for (const entry of readdirSync(join(ROOT, dir))) {
      const relative = `${dir}/${entry}`;
      if (statSync(join(ROOT, relative)).isDirectory()) {
        if (entry !== 'test') {
          walk(relative);
        }
      } else if (entry.endsWith('.ts')) {
        found.set(relative, readFileSync(join(ROOT, relative), 'utf8'));
      }
    }
  };
  walk('src');
  walk('shell');

  return found;
}

test('the two snapshot modules are gone', () => {
  for (const file of ['src/sidebarSnapshot.ts', 'src/sidebarSnapshotStore.ts', 'src/test/theSidebarSnapshotFile.test.ts']) {
    assert.equal(existsSync(join(ROOT, file)), false, `${file} must not exist`);
  }
});

test('no shipped source names the snapshot file, its writer, its reader or the call that fed it', () => {
  const sources = shipped();
  assert.ok(sources.has('shell/entry.ts') && sources.has('src/panelProvider.ts'), 'the scan reads the shipped tree');
  const names = ['sidebar-snapshot', 'SidebarSnapshotWriter', 'keepSnapshot', 'snapshotPage', 'skeletonPage', 'NONCE_PLACEHOLDER'];
  const hits = [...sources].flatMap(([file, text]) => names.filter((name) => text.includes(name)).map((name) => `${file}: ${name}`));

  assert.deepEqual(hits, [], 'something still names the snapshot feature');
});
