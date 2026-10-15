// node --test scripts/lib/vibe.test.mjs — the manifest script's listing and classifier (D4, C8 of
// todo/PLAN_vibe_live_round_fixes.md). Runs nothing of Vibe's; makes folders only under this user's own temp folder, and
// removes them. The rules below are the script's own constants, so a rule the C# side holds and the script forgot fails here.
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { classOf, deepestChild, files } from './vibe.mjs';

const base = mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-vh-vibelib-'));
after(() => rmSync(base, { recursive: true, force: true }));

const RULES = {
  pinnedPrefix: 'logs/session/plugins/',
  varying: ['trusted_folders.toml', 'vibehistory', 'logs/vibe.log', 'logs/session/plugins/tmp/**', 'logs/session/unified/*/**'],
  varyingFolders: ['logs/session/plugins/data/**'],
};

const SESSION = '4f94d59a-afc4-fe1d-2354-ff6c5e3f799d';
const CHUNK = `logs/session/unified/${SESSION}/chunks/${'a'.repeat(64)}.json`;
const LISTING = [
  'vibehistory',
  CHUNK,
  `logs/session/unified/${SESSION}/CURRENT`,
  `logs/session/plugins/blobs/60/${'6'.repeat(64)}`,
  'logs/session/plugins/data/193149ad-d8e5-069e-8f34-aece24b2623d/vibe/',
  'logs/session/plugins/tmp/',
];

test('files() lists an empty folder as a/b/ only when asked for folders, and never lists one otherwise', () => {
  const dir = join(base, 'listing');
  mkdirSync(join(dir, 'a', 'b'), { recursive: true });
  mkdirSync(join(dir, 'a', 'full'), { recursive: true });
  writeFileSync(join(dir, 'a', 'full', 'f.txt'), 'x');

  assert.deepEqual(files(dir), ['a/full/f.txt']);
  assert.deepEqual(files(dir, { folders: true }), ['a/', 'a/b/', 'a/full/', 'a/full/f.txt']);
});

test('the classifier judges varying and varyingFolders before the pinned prefix, as the C# rules do', () => {
  const at = (path) => classOf(path, RULES, LISTING);

  assert.equal(at('logs/session/plugins/data/x/vibe/'), 'admitted-folder');
  assert.equal(at('logs/session/plugins/newthing/'), 'unknown', 'a folder under the pinned prefix that leads to no pinned file');
  assert.equal(at('logs/session/plugins/blobs/60/'), 'pinned-lead');
  assert.equal(at(`logs/session/plugins/blobs/60/${'6'.repeat(64)}`), 'pinned');
  assert.equal(at('logs/session/plugins/tmp/a'), 'varies', 'the staging folder lies under the prefix and is admitted by name');
  assert.equal(at('logs/session/plugins/data/x/vibe/state.json'), 'unknown', 'a FILE in a plugin\'s per-session data stops the script');
  assert.equal(at('logs/session/unified/'), 'varies-lead');
  assert.equal(at('hooks.toml'), 'unknown');
});

test('deepestChild is the longest persisted file path plus Vibe\'s 22-character atomic-write wrapper', () => {
  assert.equal(CHUNK.length, 134);
  assert.deepEqual(deepestChild(LISTING), { deepestChild: 156, deepestPersisted: CHUNK.replace(SESSION, '<id>').replace('a'.repeat(64), '<64 hex>') });
});
