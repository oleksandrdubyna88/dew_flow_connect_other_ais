import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, test } from 'node:test';

import { writeFileAtomically, writeFileAtomicallySync } from '../atomicFile';
import { NOTHING_CHOSEN, coaiDataDir, defaultDataDir, useStorageSettings } from '../dataDir';
import { forgetFindingsBoard } from '../findingsBoard';
import { appendLine } from '../jsonlLedger';

/**
 * No writer recreates a NAMED data root — not even one the board believes is there (todo/PLAN_paths_per_side.md E5.7,
 * D3; the risk consultation of story 5.7, items E and F).
 *
 * <p><b>The race (F).</b> The guard asks the board, then the writer makes the directory: a root removed — a drive
 * unmounted, a share gone to sleep — between the two was made again by the writer's RECURSIVE `mkdir`, into a bare mount
 * point. The board cannot close that gap, because it is a question asked before an act. So a writer never makes a named
 * root at all: it makes only the folders BELOW an existing root, one non-recursive `mkdir` at a time, and a root that is
 * not there is refused at the moment of the write.</p>
 *
 * <p><b>Provenance (E).</b> Which layer named the folder decides, never its spelling: all layers unset → the default
 * location is created lazily; an explicit value that happens to equal the default path is a NAMED root and is refused when
 * missing; a side folder inside an existing named root is created.</p>
 */

let parent = '';
const savedHome = { LOCALAPPDATA: process.env['LOCALAPPDATA'], COAI_DATA_DIR: process.env['COAI_DATA_DIR'], COAI_DATA_SIDE: process.env['COAI_DATA_SIDE'] };

beforeEach(() => {
  forgetFindingsBoard();
  parent = mkdtempSync(join(tmpdir(), 'coai-maker-'));
  // The default location lives under the test's own folder, so "the default is created" never touches this machine's.
  process.env['LOCALAPPDATA'] = join(parent, 'local-app-data');
  delete process.env['COAI_DATA_DIR'];
  delete process.env['COAI_DATA_SIDE'];
});

afterEach(() => {
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  for (const [name, value] of Object.entries(savedHome)) {
    if (value === undefined) {
      delete process.env[name];
    } else {
      process.env[name] = value;
    }
  }
  forgetFindingsBoard();
  rmSync(parent, { recursive: true, force: true });
});

/** The three writers every other one goes through: the ledger append, the atomic write and its sync form. */
async function everyRoadWritesUnder(dir: string): Promise<void> {
  await appendLine(join(dir, 'notifications.jsonl'), '{}\n', 'a test line', { chain: 'notifications' });
  await writeFileAtomically(join(dir, 'window-1.json'), '{}').catch(() => undefined);
  try {
    writeFileAtomicallySync(join(dir, 'sync.json'), '{}');
  } catch {
    // Refused — what this test wants; the assertion is on the disk.
  }
}

test('F — the root removed after the board last saw it: no writer makes it again', async () => {
  const root = join(parent, 'mounted-then-gone');
  mkdirSync(root);
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);
  // The board knows nothing against the root — it was there. Then the drive goes away.
  rmSync(root, { recursive: true });

  await everyRoadWritesUnder(root);

  assert.equal(existsSync(root), false, 'a writer\'s recursive mkdir made a named root the board had no finding against — the race between the guard and the write');
});

test('F — the same with a side: the root gone, the side folder is not made, and neither is the root', async () => {
  const root = join(parent, 'gone-with-a-side');
  useStorageSettings({ directory: root, side: 'wsl' }, NOTHING_CHOSEN);

  await everyRoadWritesUnder(coaiDataDir());

  assert.equal(existsSync(root), false, 'writing into <root>/<side> made the missing root');
});

test('E — all layers unset: the default location is created lazily, as on a first start', async () => {
  await everyRoadWritesUnder(coaiDataDir());

  assert.equal(coaiDataDir(), defaultDataDir());
  assert.ok(existsSync(join(defaultDataDir(), 'notifications.jsonl')), 'the default location was not created — a first start must never be refused');
});

test('E — an explicit value EQUAL to the default path is a named root: missing, it is refused', async () => {
  useStorageSettings({ directory: defaultDataDir(), side: '' }, NOTHING_CHOSEN);

  await everyRoadWritesUnder(coaiDataDir());

  assert.equal(existsSync(defaultDataDir()), false, 'a root a person NAMED was created because its spelling matched the default');
});

test('E — an existing named root with a missing side folder: the side folder is created', async () => {
  const root = join(parent, 'nas');
  mkdirSync(root);
  useStorageSettings({ directory: root, side: 'windows' }, NOTHING_CHOSEN);

  await everyRoadWritesUnder(coaiDataDir());

  assert.ok(existsSync(join(root, 'windows', 'notifications.jsonl')), 'the side folder inside an existing root was not created');
});
