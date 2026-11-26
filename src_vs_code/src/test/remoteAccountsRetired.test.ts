import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { accountsStateOf, stateMerge } from '../remoteAccountsRound';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * E2S4-T9: what the cut and E1's notices left is gone. No source file names a retired symbol or code; the census holds the one new notice and neither
 * of the two old ones; a state row that still holds the two old times is read, not damaged, and is not written with them again.
 */

const filesWith = (pattern: RegExp): readonly string[] => sourceFiles().filter((file) => pattern.test(sourceOf(file)));

test('E2S4-T9: the cut\'s constants, the old view of the door and the two notices are in no source file', () => {
  const retired = filesWith(/\bACCOUNTS_READING\b|\bREADING_OFF_SAID\b|\bAccountsReading\b|\ballowedNow\b|\badmissionOf\b|\bRoundResult\b[^;]*\ballowedNow\b|accounts-hosts-allowed|accounts-read-every-ten-minutes|\ballowedNotice\b|\beveryTenNotice\b|\btellAfterRound\b|\bnoticesDue\b/u);
  assert.deepEqual(retired, [], `a retired name is still in ${retired.join(', ')}`);
  assert.deepEqual(filesWith(/\bAdmission\b/u).filter((file) => file.startsWith('remote')), [], 'the type Admission of the round\'s old view is still named');
  assert.ok(sourceFiles().includes('remoteAccountsRound.ts'), 'companion: the scan reads the round\'s source');
});

test('E2S4-T9: the key of the retired setting is not named by the accounts host any more (E2S1-T17\'s allowance of the bridge line is gone)', () => {
  assert.ok(!/terminalAccountsAllowNewHosts/u.test(sourceOf('remoteAccountsHost.ts')));
  assert.ok(!/ALLOW_NEW_HOSTS_KEY/u.test(sourceOf('remoteAccountsHost.ts')));
});

test('E2S4-T9: the census holds the one new notice and not the two old ones', () => {
  const census = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'notification-sites.json'), 'utf8')) as { codes: readonly string[] };

  assert.ok(census.codes.includes('accounts-can-be-shown'), 'the new notice is not in the census');
  assert.ok(!census.codes.includes('accounts-hosts-allowed') && !census.codes.includes('accounts-read-every-ten-minutes'), 'an old notice is still in the census');
  assert.equal(filesWith(/source: 'terminalAccounts',\s*code: 'accounts-can-be-shown'/u).join(','), 'remoteAccountsHost.ts', 'the new notice has not exactly one call site');
});

test('E2S4-T9: a state row that holds toldUtc and lastAutoNoticeUtc is read, not damaged, and is not written with them again', () => {
  const T = '2026-11-24T10:00:00.000Z';
  const row = { toldUtc: T, lastAutoNoticeUtc: T, stoppedUtc: T };

  assert.deepEqual(accountsStateOf(row), { kind: 'state', state: row }, 'a row with the two old times is damaged');
  assert.equal(accountsStateOf({ toldUtc: 5 }).kind, 'damaged', 'the companion: a time that is not text is still damage');
  const merged = stateMerge(row, { canBeShown: T });
  assert.deepEqual(merged.next?.value, { ...row, canBeShownUtc: T }, 'an old time was dropped from the row');
  assert.deepEqual(stateMerge({ ...row, canBeShownUtc: T }, { canBeShown: '2027-01-01T00:00:00.000Z' }).next, undefined, 'the one notice\'s time was replaced');
  for (const change of [{ told: T }, { autoNoticed: T }]) {
    assert.equal(stateMerge({}, change as never).next, undefined, `the retired change ${JSON.stringify(change)} still writes`);
  }
});
