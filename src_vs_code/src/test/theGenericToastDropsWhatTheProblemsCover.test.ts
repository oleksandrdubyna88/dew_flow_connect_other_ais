import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type PathFinding, agreed, findingsFromServer, kindOfState } from '../pathFindings';
import { type SettingProblem, withoutCovered } from '../providers';

/**
 * Never two toasts for one folder (todo/PLAN_paths_per_side.md E5.3, risk 6): the generic "the server could not
 * understand a setting" toast drops every sentence the server also named structurally — the missing-path notice says
 * those, with the place and its button — and keeps every other sentence; a server before the field covers nothing, so
 * the toast stays the only surface, as before. And E5.6's half: for the roots the server's problems replace the page's own.
 */

const SENTENCE = "COAI_QCONSULT_ROOTS: 'D:\\old-project' is not a directory on this machine — a disk row needs a folder that exists";
const PROBLEM: SettingProblem = { key: 'COAI_QCONSULT_ROOTS', kind: 'missing-folder', value: 'D:\\old-project', sentence: SENTENCE };

test('a sentence a structured problem covers leaves the generic toast; every other sentence stays', () => {
  const kept = withoutCovered([SENTENCE, 'COAI_RETRY_BACKOFF is not a number'], [PROBLEM]);

  assert.deepEqual(kept, ['COAI_RETRY_BACKOFF is not a number'], 'the operator\'s folder would be said twice: once by its notice, once by the generic toast');
});

test('a server before the field covers nothing — the toast keeps every sentence, as before', () => {
  assert.deepEqual(withoutCovered([SENTENCE], []), [SENTENCE]);
  assert.deepEqual(withoutCovered([SENTENCE], [{ ...PROBLEM, sentence: '' }]), [SENTENCE], 'a problem without its sentence covers no sentence');
});

const own: readonly PathFinding[] = [
  { key: 'coai.qconsultRoots', place: 'consultants/qconsult', value: 'D:\\mine', family: 'windows', state: 'missing', owner: '' },
  { key: 'coai.vendors[].executablePath', place: 'models', value: 'D:\\cli.exe', family: 'windows', state: 'missing', owner: 'codex' },
];

test('once the server has answered, its problems are the roots\' findings and the window\'s own roots findings go; the rest stays', () => {
  const problems: SettingProblem[] = [
    { key: 'COAI_QCONSULT_ROOTS', kind: 'missing-folder', value: ' D:\\theirs ', sentence: 'x' },
    { key: 'COAI_QCONSULT_ROOTS', kind: 'unreadable-folder', value: 'Z:\\share', sentence: 'y' },
    { key: 'COAI_QCONSULT_ROOTS', kind: 'a-kind-this-build-does-not-know', value: 'D:\\odd', sentence: 'z' },
    { key: 'COAI_SOMETHING_ELSE', kind: 'missing-folder', value: 'D:\\x', sentence: 'w' },
  ];

  const findings = agreed(own, problems, 'windows');

  assert.deepEqual(findings.map((one) => [one.key, one.value, one.state]), [
    ['coai.vendors[].executablePath', 'D:\\cli.exe', 'missing'],
    ['coai.qconsultRoots', 'D:\\theirs', 'missing'],
    ['coai.qconsultRoots', 'Z:\\share', 'unknown'],
  ], 'the server\'s roots replace the window\'s; an unknown kind or key marks nothing; a CLI finding of the window\'s own stays');
  assert.equal(findingsFromServer(problems, 'posix')[0]?.family, 'posix');
  assert.deepEqual(agreed(own, undefined, 'windows'), own, 'a server that has not answered, or is older, leaves the window\'s own findings');
  assert.deepEqual([kindOfState('missing'), kindOfState('unknown')], ['missing-folder', 'unreadable-folder']);
});
