import assert from 'node:assert/strict';
import { test } from 'node:test';
import { closeTarget, closeTitle, coveredSaid, kindLine, kindOf } from '../consultKind';
import { EMPTY_LOG, parseLog } from '../roundsDb';

/** A consultation row as the log reads it off the server — defaults filled by the real parser. */
const row = (over: Record<string, unknown>) =>
  parseLog(JSON.stringify({ ...EMPTY_LOG, consultations: [{ id: 'c1', status: 'closed', ...over }] }), true).consultations;

test('the close pick is told which consultation it closes and where, from the log', () => {
  const log = row({ repoPath: 'D:/rsd/repo', kind: 'cadence', plan: 'todo/PLAN_x.md', epics: '1-3' });

  assert.deepEqual(closeTarget(log, 'c1'), {
    kind: 'pick', repo: 'D:/rsd/repo', title: 'How did this consultation end? — cadence · epics 1-3 · PLAN_x.md',
  });
});

test('a consultation gone from the log, or with no checkout on its row, is said to be gone before any pick', () => {
  assert.deepEqual(closeTarget(row({ repoPath: 'D:/rsd/repo' }), 'another-id'), { kind: 'gone' });
  assert.deepEqual(closeTarget(row({ repoPath: '' }), 'c1'), { kind: 'gone' });
});

// A consultation's kind, worded once for every surface (research/PLAN_consult_limits_kinds_and_help.md, story 2).

test('a record from before the kinds existed was a stuck consultation', () => {
  assert.equal(kindOf(''), 'stuck');
  assert.equal(kindOf('cadence'), 'cadence');
});

test('what an ordered consultation covered is said in words; a stuck one and an unknown kind cover nothing readable', () => {
  assert.equal(coveredSaid('cadence', '4-6'), 'epics 4-6');
  assert.equal(coveredSaid('cadence', '7'), 'epic 7');
  assert.equal(coveredSaid('risk', '7/7.2'), 'story 7.2');
  assert.equal(coveredSaid('risk', '7'), 'epic 7');
  assert.equal(coveredSaid('stuck', ''), '');
  assert.equal(coveredSaid('urgent', '1-3'), '', 'a kind from a newer server is not dressed up as one this build knows');
});

test('the card line puts the kind first, then what it covered, then the plan by its file', () => {
  assert.equal(kindLine('cadence', '1-3', 'todo/PLAN_x.md'), 'cadence · epics 1-3 · PLAN_x.md');
  assert.equal(kindLine('risk', '5/5.1', 'D:\\rsd\\repo\\todo\\PLAN_y.md'), 'risk · story 5.1 · PLAN_y.md');
  assert.equal(kindLine('stuck', '', ''), 'stuck');
  assert.equal(kindLine('', '', ''), 'stuck');
});

test('the pick that records an outcome names the kind of the consultation it closes', () => {
  assert.equal(closeTitle('cadence', '1-3', 'todo/PLAN_x.md'), 'How did this consultation end? — cadence · epics 1-3 · PLAN_x.md');
  assert.equal(closeTitle('stuck', '', ''), 'How did this consultation end? — stuck');
});
