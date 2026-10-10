import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { test } from 'node:test';

import {
  type HeldNotice,
  currentFindings,
  dataRootSettled,
  expectDataRoot,
  forgetFindingsBoard,
  heldNotices,
  holdNotice,
  keyOfSubject,
  missingDataRoot,
  onFindings,
  publishFindings,
  refuseUnderMissingRoot,
  refusedDataRoot,
  stillHeld,
  withHeld,
} from '../findingsBoard';
import type { NotificationRecord } from '../notifications';
import { SETTING_PATH_MISSING, type PathFinding, findingSubject } from '../pathFindings';

/**
 * The storage-write policy's state (todo/PLAN_paths_per_side.md E5.0): what the host last published about this window's
 * paths, the guard every writer of the data directory asks, and the notices held in memory while the named data root is
 * missing — one per setting key, cleared on a change or a recovery, so the list is bounded by the registry.
 */

const ROOT = resolve('/srv/coai');

const dataRoot: PathFinding = { key: 'coai.dataDirectory', place: 'setup/mcp', value: ROOT, family: 'posix', state: 'missing', owner: '' };

function notice(key: string, value: string): NotificationRecord {
  return { utc: '2026-10-10T12:00:00.000Z', class: 'failure', source: 'pathWatch', code: SETTING_PATH_MISSING, subject: findingSubject({ key, value }), title: `${value} is missing` };
}

test('three successive missing values for the data directory leave ONE held notice — the newest — and a recovery clears it', () => {
  forgetFindingsBoard();
  publishFindings([dataRoot]);
  holdNotice(notice('coai.dataDirectory', 'Z:\\coai'));
  holdNotice(notice('coai.dataDirectory', 'Z:\\coai2'));
  holdNotice(notice('coai.dataDirectory', 'Z:\\coai3'));

  assert.deepEqual(heldNotices().map((one) => one.subject), ['coai.dataDirectory|Z:\\coai3'], 'a retyped path is one held notice per setting key, never one per retype');

  // The folder is found again: nothing is missing, so nothing stays held.
  publishFindings([]);
  assert.deepEqual(heldNotices(), [], 'a recovery left the held notice behind');
});

test('a held notice is released the moment its path CHANGES, and one for another key is kept beside it', () => {
  const roots: PathFinding = { key: 'coai.qconsultRoots', place: 'consultants/qconsult', value: 'D:\\old', family: 'windows', state: 'missing', owner: '' };
  const list: readonly HeldNotice[] = [
    { key: 'coai.dataDirectory', record: notice('coai.dataDirectory', 'Z:\\coai') },
    { key: 'coai.qconsultRoots', record: notice('coai.qconsultRoots', 'D:\\old') },
  ];

  const changed = stillHeld(list, [{ ...dataRoot, value: 'Z:\\elsewhere' }, roots]);
  assert.deepEqual(changed.map((one) => one.record.subject), ['coai.qconsultRoots|D:\\old'], 'a notice about a path no longer stored stayed held');

  const replaced = withHeld(list, notice('coai.dataDirectory', 'Z:\\new'));
  assert.deepEqual(replaced.map((one) => one.record.subject), ['coai.qconsultRoots|D:\\old', 'coai.dataDirectory|Z:\\new'], 'the same key is held once, by its newest notice');
  assert.equal(keyOfSubject('coai.dataDirectory|Z:\\coai'), 'coai.dataDirectory');
  assert.equal(keyOfSubject(undefined), '');
});

test('a write at or under the missing named root is refused with the finding\'s sentence; one elsewhere is not; the default location never is', () => {
  forgetFindingsBoard();
  publishFindings([dataRoot]);

  assert.equal(refusedDataRoot(ROOT), dataRoot);
  assert.equal(refusedDataRoot(resolve(ROOT, 'windows')), dataRoot, 'the side folder inside the missing root is under it');
  assert.equal(refusedDataRoot(resolve('/srv/coai-other')), undefined, 'a sibling whose name merely starts the same is not under the root');
  assert.equal(refusedDataRoot(resolve('/tmp/x')), undefined);
  assert.throws(() => refuseUnderMissingRoot(resolve(ROOT, 'notifications.jsonl')), /was not found on this machine .*Nothing is written under it until it exists/u);
  assert.doesNotThrow(() => refuseUnderMissingRoot(resolve('/tmp/x')));

  publishFindings([{ ...dataRoot, state: 'unknown' }]);
  assert.equal(missingDataRoot(), undefined, 'a root the disk could not answer for is not missing — writes go on as today');
  assert.doesNotThrow(() => refuseUnderMissingRoot(ROOT));

  publishFindings([]);
  assert.equal(refusedDataRoot(ROOT), undefined);
});

test('a publish tells its listeners and is what the page reads; the gate waits for the data root to be asked about, bounded', async () => {
  forgetFindingsBoard();
  let told = 0;
  onFindings(() => { told += 1; });
  publishFindings([dataRoot]);

  assert.equal(told, 1);
  assert.deepEqual(currentFindings(), [dataRoot]);

  let settled = false;
  const first = new Promise<void>((resolve) => { setTimeout(() => { settled = true; resolve(); }, 20); });
  expectDataRoot(first);
  await dataRootSettled();
  assert.equal(settled, true, 'the gate decided before the data root had been asked about');

  expectDataRoot(Promise.reject(new Error('the probe threw')));
  await assert.doesNotReject(dataRootSettled(), 'a probe that threw must not hold every toast of the window');
  forgetFindingsBoard();
});
