import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, test } from 'node:test';

import { NOTHING_CHOSEN, useStorageSettings } from '../dataDir';
import { absentDataRoot, forgetFindingsBoard, heldNotices, holdNotice, onDataRootBack } from '../findingsBoard';
import { appendLine } from '../jsonlLedger';
import type { FilePresence } from '../pathFamily';
import { type PathFinding, SETTING_PATH_MISSING, findingSubject } from '../pathFindings';
import { PathProbes, statProbe } from '../pathProbes';
import { PathWatch, RECHECK_MS } from '../pathWatch';
import { stat } from 'node:fs/promises';

/**
 * Recovery WITHOUT a settings edit (todo/PLAN_paths_per_side.md E5.7, the risk consultation's item F): a person whose
 * named data folder is gone makes it again — or the drive is mounted — and that is all. The next probe confirms the root,
 * the notice clears, the guard lets the writers through and the settings mirror writes. The host scenario that proved
 * recovery before also changed a setting, which is the one thing that re-asked the disk; this one changes nothing.
 */

const tick = (): Promise<void> => new Promise((resolve) => { setImmediate(resolve); });
const settle = async (): Promise<void> => { for (let at = 0; at < 6; at += 1) { await tick(); } };

/**
 * Waits until `done()` holds, or fails naming `what` after `ms`. The rig asks the REAL disk, and a `stat` goes through
 * libuv's thread pool: a fixed number of ticks raced it on a slower runner (CI on Linux, PR #752).
 */
async function until(done: () => boolean, what: string, ms = 5000): Promise<void> {
  const deadline = Date.now() + ms;
  while (!done()) {
    if (Date.now() > deadline) {
      assert.fail(`timed out after ${ms} ms waiting for ${what}`);
    }
    await new Promise((resolve) => { setTimeout(resolve, 10); });
  }
}

/** A quiet time for a check that something does NOT change: the disk's answers land well inside it. */
const quiet = (): Promise<void> => new Promise((resolve) => { setTimeout(resolve, 300); });

let parent = '';

afterEach(() => {
  useStorageSettings(NOTHING_CHOSEN, NOTHING_CHOSEN);
  forgetFindingsBoard();
  rmSync(parent, { recursive: true, force: true });
});

interface Rig {
  readonly root: string;
  readonly watch: PathWatch;
  readonly timers: { work: () => void; ms: number }[];
  readonly said: PathFinding[];
  readonly resolved: PathFinding[];
}

/** The REAL disk under a temp folder, a named root that is not there, and a timer the test fires by hand. */
function rig(): Rig {
  forgetFindingsBoard();
  parent = mkdtempSync(join(tmpdir(), 'coai-recover-'));
  const root = join(parent, 'named');
  useStorageSettings({ directory: root, side: '' }, NOTHING_CHOSEN);
  const timers: { work: () => void; ms: number }[] = [];
  const said: PathFinding[] = [];
  const resolved: PathFinding[] = [];
  const watch = new PathWatch({
    reads: { roots: () => [], vendors: () => [], watched: () => [], ownDataDir: () => root, dataRoot: () => ({ root, source: 'this side' }) },
    probes: new PathProbes(statProbe((path) => stat(path))),
    side: { family: process.platform === 'win32' ? 'windows' : 'posix', systemDrive: 'C:', fileAt: (): FilePresence => 'absent' },
    platform: process.platform,
    say: (finding) => { said.push(finding); },
    resolved: (finding) => { resolved.push(finding); },
    every: (work, ms) => { timers.push({ work, ms }); },
  });

  return { root, watch, timers, said, resolved };
}

test('the folder is made again and nothing else happens: the next recheck confirms it, the notice clears, the writers write, the mirror is told', async () => {
  const it = rig();
  let back = 0;
  onDataRootBack(() => { back += 1; });
  it.watch.start();
  await until(() => it.said.length === 1, 'the missing root to be said');
  assert.equal(it.said.length, 1, 'the rig never said the missing root');
  holdNotice({ utc: '2026-10-10T12:00:00.000Z', class: 'failure', source: 'pathWatch', code: SETTING_PATH_MISSING, subject: findingSubject(it.said[0]!), title: 'missing' });
  assert.deepEqual(it.timers.map((one) => one.ms), [RECHECK_MS], 'the watch arms no recheck, so only a settings edit would ever look again');

  mkdirSync(it.root);
  it.timers[0]!.work();
  await until(() => absentDataRoot() === undefined && back === 1, 'the recheck to confirm the root');

  assert.equal(absentDataRoot(), undefined, 'the folder exists and the guard still refuses it — no settings edit, no recovery');
  assert.deepEqual(it.resolved.map(findingSubject), [findingSubject(it.said[0]!)], 'the notice was not cleared');
  assert.deepEqual(heldNotices(), [], 'the held notice outlived the folder coming back');
  assert.equal(back, 1, 'the settings mirror, which stood aside, was never told the root is back');
  await appendLine(join(it.root, 'notifications.jsonl'), '{}\n', 'a test line', { chain: 'notifications' });
  assert.ok(existsSync(join(it.root, 'notifications.jsonl')), 'the writers did not resume');
});

test('a recheck while the folder is still gone changes nothing: still refused, said once, nobody told it is back', async () => {
  const it = rig();
  let back = 0;
  onDataRootBack(() => { back += 1; });
  it.watch.start();
  await until(() => it.said.length === 1, 'the missing root to be said');

  it.timers[0]!.work();
  await quiet();
  it.timers[0]!.work();
  await quiet();

  assert.ok(absentDataRoot() !== undefined);
  assert.deepEqual([it.said.length, it.resolved.length, back], [1, 0, 0]);
  assert.equal(existsSync(it.root), false);
});

test('a quiet window rechecks nothing: with every path there, the timer asks the disk about no path', async () => {
  const it = rig();
  mkdirSync(it.root);
  let asked = 0;
  const probes = new PathProbes(async () => { asked += 1; return 'here'; });
  const watch = new PathWatch({
    reads: { roots: () => [], vendors: () => [], watched: () => [], ownDataDir: () => it.root, dataRoot: () => ({ root: it.root, source: 'this side' }) },
    probes,
    side: { family: process.platform === 'win32' ? 'windows' : 'posix', systemDrive: 'C:', fileAt: (): FilePresence => 'absent' },
    platform: process.platform,
    say: () => undefined,
    resolved: () => undefined,
  });
  watch.start();
  await settle();
  const before = asked;

  watch.recheck();
  await settle();

  assert.equal(asked, before, 'a recheck asked the disk about a path it had found there');
});
