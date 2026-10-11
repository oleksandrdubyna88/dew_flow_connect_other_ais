import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, test } from 'node:test';

import { writeFileAtomically } from '../atomicFile';
import { NOTHING_CHOSEN, useStorageSettings } from '../dataDir';
import { absentDataRoot, forgetFindingsBoard } from '../findingsBoard';
import { appendLine } from '../jsonlLedger';
import type { FilePresence } from '../pathFamily';
import { PathProbes, type ProbeOptions } from '../pathProbes';
import { PathWatch } from '../pathWatch';

/**
 * A settings change must not switch the storage guard off (research/PLAN_paths_per_side.md E5.0/E5.7, the risk consultation
 * of story 5.7, item D).
 *
 * <p><b>What the consultation suspected and this measured.</b> Every change to a `coai` setting calls `PathWatch.forget`:
 * the disk's answers are dropped and asked again. Until the new answer lands the data root reads `pending`, which is no
 * finding — and the guard every writer asks read only the published FINDING. The first-answer wait
 * (`expectDataRoot`) runs once, at activation, so after that a writer awaited a promise settled long ago and saw no
 * missing root: a ledger append or the chat store's heartbeat made the folder the notice says is missing. A probe that
 * timed out answered `unknown`, which is no finding either, and permitted the same write.</p>
 *
 * <p>The guard now keeps its own state: a named root the disk said is ABSENT stays refused until a probe CONFIRMS it is
 * there — a pending or unknown answer never authorises a write under it.</p>
 */

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });

interface Rig {
  readonly root: string;
  readonly watch: PathWatch;
  /** What the disk answers about the root; `'hang'` never answers. */
  answer: FilePresence | 'hang';
}

let parent = '';

function rig(options: Partial<ProbeOptions> = {}): Rig {
  forgetFindingsBoard();
  parent = mkdtempSync(join(tmpdir(), 'coai-refresh-'));
  const root = join(parent, 'named-but-missing');
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);
  const state: Rig = { root, watch: undefined as unknown as PathWatch, answer: 'absent' };
  const probes = new PathProbes(
    (path) => (path === root && state.answer === 'hang' ? new Promise<FilePresence>(() => undefined) : Promise.resolve(path === root ? state.answer as FilePresence : 'absent')),
    options,
  );
  const watch = new PathWatch({
    reads: { roots: () => [], vendors: () => [], watched: () => [], ownDataDir: () => root, dataRoot: () => ({ root, source: 'this side' }) },
    probes,
    side: { family: process.platform === 'win32' ? 'windows' : 'posix', systemDrive: 'C:', fileAt: () => 'absent' },
    platform: process.platform,
    say: () => undefined,
    resolved: () => undefined,
  });

  return Object.assign(state, { watch });
}

afterEach(() => {
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  forgetFindingsBoard();
  rmSync(parent, { recursive: true, force: true });
});

/** The two writers the consultation named: a ledger append and the chat store's heartbeat (the atomic write). */
async function writeUnder(root: string): Promise<void> {
  await appendLine(join(root, 'notifications.jsonl'), '{}\n', 'a test line', { chain: 'notifications' });
  await writeFileAtomically(join(root, 'window-1.json'), '{}').catch(() => undefined);
}

test('after a settings change, with the replacement probe still out, a ledger append and the heartbeat leave the missing root absent', async () => {
  const it = rig();
  it.watch.start();
  await tick();
  await tick();
  assert.ok(absentDataRoot() !== undefined, 'the rig never established the missing root');

  it.answer = 'hang';
  it.watch.forget();
  await writeUnder(it.root);

  assert.equal(existsSync(it.root), false,
    'a settings change dropped the disk\'s answer, the guard read "no finding", and a writer created the missing named root');
});

test('a replacement probe that TIMES OUT does not authorise the write either — and the mirror still stands aside', async () => {
  const it = rig({ later: (work) => { setImmediate(work); }, deadlineMs: 0 });
  it.watch.start();
  await tick();
  await tick();
  assert.ok(absentDataRoot() !== undefined, 'the rig never established the missing root');

  it.answer = 'hang';
  it.watch.forget();
  const missing = await it.watch.dataRootMissing();
  await writeUnder(it.root);

  assert.equal(missing, true, 'a timed-out probe made the mirror believe the named root was there');
  assert.equal(existsSync(it.root), false, 'a timed-out probe let a writer create the missing named root');
});

test('only a probe that CONFIRMS the root lifts the refusal — and then the writers write', async () => {
  const it = rig();
  it.watch.start();
  await tick();
  await tick();

  it.answer = 'unknown';
  it.watch.forget();
  await tick();
  await tick();
  assert.ok(absentDataRoot() !== undefined, 'an unknown answer lifted the refusal');

  it.answer = 'here';
  it.watch.forget();
  await tick();
  await tick();
  assert.equal(absentDataRoot(), undefined, 'a confirmed root stayed refused');
  assert.equal(await it.watch.dataRootMissing(), false);
});
