import assert from 'node:assert/strict';
import { test } from 'node:test';

import { MirrorSchedule } from '../mirrorSchedule';
import { ServerSettingsSync, type SyncOutcome } from '../serverSettingsSync';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';

/**
 * The settings mirror does not write into a NAMED data root that is missing (todo/PLAN_paths_per_side.md E5.7, D3).
 * Writing the server's settings file created the folder — into a bare `/mnt/z` mount point when the drive was not
 * mounted — and hid the one condition the page and the notice now report. The mirror asks the watch first and answers
 * `no-data-root`: a terminal outcome the schedule neither retries nor reports, because the missing-path notice already
 * says it and two toasts for one folder is what it must not be.
 */

function sync(dataRootMissing: () => Promise<boolean>): { readonly it: ServerSettingsSync; readonly written: string[]; readonly entered: number[] } {
  const written: string[] = [];
  const entered: number[] = [];
  const it = new ServerSettingsSync(
    () => ({ settings: DEFAULTS, vendors: DEFAULT_VENDORS }),
    async (json) => { written.push(json); },
    '9.9.9',
    async () => ({ kind: 'absent' }),
    () => undefined,
    async (work) => { entered.push(1); await work(); return true; },
    () => '',
    () => () => undefined,
    () => [],
    dataRootMissing,
  );

  return { it, written, entered };
}

test('with the named data root missing the mirror writes nothing, enters no critical section, and answers no-data-root', async () => {
  const { it, written, entered } = sync(async () => true);

  const outcome = await it.sync();

  assert.equal(outcome, 'no-data-root', 'activation with a missing named root creates it today: the write that hides the condition');
  assert.deepEqual(written, [], 'the settings file was written into a folder that is not there');
  assert.deepEqual(entered, [], 'the lock would make the directory too');
});

test('with the root there — or unknown, or the default location — the mirror writes as it always did', async () => {
  const { it, written } = sync(async () => false);

  assert.equal(await it.sync(), 'written');
  assert.equal(written.length, 1);
});

test('the schedule treats no-data-root as terminal: no retry, no report, told once — and a write that lands later is still news', () => {
  const outcomes: SyncOutcome[] = ['no-data-root'];
  const settled: SyncOutcome[] = [];
  const reported: string[] = [];
  const armed: { readonly ms: number }[] = [];
  const schedule = new MirrorSchedule(
    async () => outcomes[0]!,
    { later: (_work, ms) => { armed.push({ ms }); return 0; }, stop: () => undefined },
    (outcome) => { reported.push(outcome); },
    () => { reported.push('recovered'); },
    (outcome) => { settled.push(outcome); },
  );

  schedule.start();

  return new Promise<void>((done) => {
    setImmediate(() => {
      assert.deepEqual(settled, ['no-data-root'], 'the deletions are told what the mirror did');
      assert.deepEqual(reported, [], 'the mirror reported the missing root as a failed write — a second toast for the folder the notice already names');
      assert.deepEqual(armed, [], 'a retry was armed for a folder that will not appear by waiting');
      done();
    });
  });
});
