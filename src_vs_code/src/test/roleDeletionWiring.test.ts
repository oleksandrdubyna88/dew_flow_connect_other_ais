import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The half of the deletion that only a host can do, and so can only be READ.
 *
 * <p>`extension.ts` and `rolesPanel.ts` import `vscode`, and no test in this repository can load
 * one. Everything decidable is a value and is RUN — `roleDeletion.test.ts` drives all five steps
 * against an in-memory store and an injected clock. What is left is the wiring: that the coordinator
 * is actually told when the mirror settles, and that activation actually sweeps. A build with both
 * of those missing passes every behavioural test in this change and deletes nothing, ever.</p>
 *
 * <p>Each assertion pins BOTH halves where there are two, because a structural test matching only
 * the presence of a call survives its own defect.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');

const source = (file: string): string => fs.readFileSync(path.join(SRC, file), 'utf8');

test('the schedule is handed the deletions, and it is the SETTLED seat it is handed', () => {
  const host = source('extension.ts');

  assert.match(
    host,
    /new MirrorSchedule\(\s*\(\) => settingsSync\.sync\(\),[\s\S]{0,400}?reportMirroredAfterAll,\s*tellTheDeletions,\s*\)/u,
    'the schedule was built without anything to tell, so a deletion waits for an outcome that never '
    + 'arrives and the person must reload or force every single one',
  );
  // And it is the fifth seat, not the fourth: handed as `report`, every exhausted ladder would
  // finish deletions the mirror never carried.
  assert.match(
    source('mirrorSchedule.ts'),
    /private readonly settled: ReportSettled = \(\) => undefined,\s*\) \{\}/u,
    'the settled reporter is not the last constructor argument any more, so the driver is filling a '
    + 'different seat than it thinks',
  );
});

test('activation SWEEPS what a dead host left behind, and does not finish it there', () => {
  const host = source('extension.ts');

  assert.match(host, /void theDeletions\.sweep\(\)/u,
    'nothing resumes a deletion interrupted by a crash, so its prompts are orphaned for ever');
  // The other half, and it is the one a careless fix gets wrong: nothing has been CARRIED at
  // activation, so a sweep that finished deletions there would delete text the server still needs.
  assert.doesNotMatch(host, /\.settled\(true/u,
    'something tells the deletions a write landed without the mirror having said so');
});

test('the outcome is translated where the host is, and the schedule stays ignorant of roles', () => {
  // A schedule that knew what a role deletion is would be a schedule with an opinion about roles.
  // It reports the outcome; `extension.ts` decides what that means for a deletion.
  // Asserted on the IMPORTS, not on the prose. The first version of this matched the word "role"
  // anywhere in the file and went red on the doc comment saying why the dependency must not exist -
  // which is the trap `structural assertions` names: a test reading source reads the comments too,
  // so it fires hardest in the files documented best.
  const schedule = source('mirrorSchedule.ts');
  const imports = [...schedule.matchAll(/^import .*from '([^']+)';$/gmu)].map((one) => one[1]);

  assert.deepEqual(imports, ['./serverSettingsSync'],
    'the mirror schedule imports something besides the sync, which is a dependency in the wrong '
    + 'direction the moment it is a role or a page');
  const host = source('extension.ts');

  assert.match(host, /const carried = outcome === 'written' \|\| outcome === 'unchanged';/u,
    'the two outcomes that mean the server HAS it are no longer the two that count as carried');
  assert.match(host, /'stood-down': STOOD_DOWN,/u,
    'the stand-down reason is written out here instead of shared, so the page that decides on it '
    + 'compares against a different string');
});

test('the roles page reaches the same deletions the host sweeps, from the same module', () => {
  // One coordinator per window, or the page shows the tombstones of a store nothing writes. And it
  // does NOT live in the panel: `extension.ts` reaches for it at activation and on every mirror
  // outcome, and making background work import the webview module inverts the layering and pins the
  // page into the activation path of a window nobody opened the Roles tab in. (antigravity, the code
  // round.)
  assert.match(source('roleDeletionsHost.ts'),
    /export function roleDeletions\(context: vscode\.ExtensionContext\): RoleDeletions \{\s*deletions \?\?=/u,
    'the coordinator is rebuilt per call, so its store, clock and reporter differ between the page '
    + 'and the host that drives it');
  assert.match(source('rolesPanel.ts'), /stranded: await roleDeletions\(side\(\)\)\.stranded\(\)/u,
    'the page is drawn without the stranded deletions, so a tombstone that cannot clear is invisible');
  // The panel itself is a legitimate import — `extension.ts` registers the command that opens it.
  // What must not come from there is the COORDINATOR, which is what pinned the page into the
  // activation path.
  const fromPanel = /import \{([^}]*)\} from '\.\/rolesPanel';/u.exec(source('extension.ts'));

  assert.ok(fromPanel !== null, 'the panel import is gone entirely, so this guard checks nothing');
  assert.ok(!(fromPanel[1] ?? '').includes('roleDeletions'),
    'the coordinator is imported from the webview panel again, which is the layering this moved');
});
