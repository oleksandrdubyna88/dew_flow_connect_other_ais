import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  Against, Allowed, Grant, Permission, STAMP_MAX, WITHDRAWN_BYTES_MAX, Withdrawal, floorOf, grantOf, inForce, nameStamp, permissionAsker, stampOf, withdrawalName, withdrawalOf, withdrawalStamp, withdrawalText,
} from '../remotePermission';

/** The pure half of E2.S1: the grant record (T1), the stamp rule (T2), the withdrawal file's parse (T19) and the question's decision (T14). */

const PREFIX = 'withdrawn-';
const UTC = '2026-11-26T10:00:00.000Z';
const grant = (stamp: number): Grant => ({ utc: UTC, stamp });
const file = (stamp: number): Withdrawal => ({ name: withdrawalName(PREFIX, stamp), kind: 'stamp', stamp, utc: UTC });
const unreadable: Withdrawal = { name: `${PREFIX}9.json`, kind: 'unreadable' };
const none: Against = { files: [], crowded: false, copy: undefined, held: false };

test('E2S1-T1 (parse): the grant record, row by row', () => {
  assert.deepEqual(grantOf({ grant: { utc: UTC, stamp: 3 } }), { utc: UTC, stamp: 3 });
  assert.deepEqual(grantOf({ grant: { utc: UTC, stamp: 3, other: 'kept inside' } }), { utc: UTC, stamp: 3 });
  assert.equal(grantOf({}), undefined, 'a file without a grant has no permission');
  const bad: unknown[] = [
    null, 'x', 7, [], [{ utc: UTC, stamp: 1 }], {}, { utc: UTC }, { stamp: 1 }, { utc: 5, stamp: 1 }, { utc: UTC, stamp: 0 }, { utc: UTC, stamp: -1 }, { utc: UTC, stamp: 1.5 },
    { utc: UTC, stamp: '1' }, { utc: UTC, stamp: STAMP_MAX + 1 }, { utc: UTC, stamp: Number.NaN }, { utc: UTC, stamp: Number.POSITIVE_INFINITY },
  ];
  for (const one of bad) {
    assert.equal(grantOf({ grant: one }), undefined, `a malformed grant was read as one: ${JSON.stringify(one)}`);
  }
  assert.equal(stampOf(STAMP_MAX), STAMP_MAX);
  assert.equal(stampOf(1), 1);
  assert.equal(stampOf(2 ** 60), undefined);
});

test('E2S1-T2: the stamp rule, every row of the table', () => {
  const rows: readonly (readonly [string, Grant | undefined, Partial<Against>, boolean])[] = [
    ['no grant', undefined, {}, false],
    ['no grant, a file', undefined, { files: [file(1)] }, false],
    ['grant 1, nothing', grant(1), {}, true],
    ['grant 1, a file 1: equal is not above', grant(1), { files: [file(1)] }, false],
    ['grant 2, a file 1', grant(2), { files: [file(1)] }, true],
    ['grant 3, files 1 and 2, copy 2', grant(3), { files: [file(1), file(2)], copy: 2 }, true],
    ['grant 3, no file, copy 3: the withdrawal is only in the copy', grant(3), { copy: 3 }, false],
    ['grant 3, a file that cannot be read', grant(3), { files: [unreadable] }, false],
    ['grant 3, file 2 and crowded', grant(3), { files: [file(2)], crowded: true }, false],
    ['grant 3, file 2, copy unreadable', grant(3), { files: [file(2)], copy: 'unreadable' }, false],
    ['grant 3, file 2, held', grant(3), { files: [file(2)], held: true }, false],
    ['a malformed grant', grantOf({ grant: { utc: UTC, stamp: 'x' } }), {}, false],
  ];
  for (const [name, one, against, want] of rows) {
    assert.equal(inForce(one, { ...none, ...against }), want, name);
  }
});

test('E2S1-T2 (floor and stamps): a grant is one above its floor, a withdrawal one above what it sees', () => {
  assert.equal(floorOf([], undefined, undefined), 0);
  assert.equal(floorOf([file(2), file(7), file(5)], undefined, undefined), 7);
  assert.equal(floorOf([file(2)], 9, undefined), 9, 'the floor comes from the copy alone');
  assert.equal(floorOf([file(2)], 'unreadable', grant(4)), 4, 'a copy that is not a stamp gives nothing; the file\'s own grant does');
  assert.equal(floorOf([unreadable], undefined, undefined), 0, 'an unreadable file gives nothing: the Allow removes it');
  assert.equal(withdrawalStamp([3, 1, 2]), 4);
  assert.equal(withdrawalStamp([]), 1);
  assert.equal(withdrawalStamp([STAMP_MAX]), STAMP_MAX, 'a withdrawal at the top takes STAMP_MAX');
});

test('E2S1-T19 (parse): each row of the withdrawal file table reads as written', () => {
  const ok = withdrawalOf(PREFIX, `${PREFIX}4.json`, withdrawalText(4, UTC));
  assert.deepEqual(ok, { name: `${PREFIX}4.json`, kind: 'stamp', stamp: 4, utc: UTC });
  assert.ok(Buffer.byteLength(withdrawalText(STAMP_MAX, UTC)) < 100, 'a withdrawal is written under 100 bytes');
  const text = withdrawalText(4, UTC);
  const unreadables: readonly (readonly [string, string, string | undefined])[] = [
    ['empty', `${PREFIX}4.json`, ''],
    ['not json', `${PREFIX}4.json`, '{'],
    ['not an object', `${PREFIX}4.json`, '[4]'],
    ['another v', `${PREFIX}4.json`, '{"v":2,"stamp":4,"utc":"x"}'],
    ['a stamp that is not the name\'s', `${PREFIX}4.json`, withdrawalText(5, UTC)],
    ['no utc', `${PREFIX}4.json`, '{"v":1,"stamp":4}'],
    ['larger than the cap', `${PREFIX}4.json`, `${text.slice(0, -1)},"pad":"${'x'.repeat(WITHDRAWN_BYTES_MAX)}"}`],
    ['a read that failed', `${PREFIX}4.json`, undefined],
    ['no stamp in the name', `${PREFIX}.json`, text],
    ['a leading zero', `${PREFIX}07.json`, withdrawalText(7, UTC)],
    ['a letter', `${PREFIX}x.json`, text],
    ['above the top', withdrawalName(PREFIX, STAMP_MAX + 1), withdrawalText(STAMP_MAX + 1, UTC)],
    ['a tmp name', `${PREFIX}4.json.tmp`, text],
    ['a sign', `${PREFIX}-4.json`, text],
  ];
  for (const [name, fileName, body] of unreadables) {
    assert.equal(withdrawalOf(PREFIX, fileName, body).kind, 'unreadable', name);
  }
  assert.equal(withdrawalOf('other-', 'other-4.json', withdrawalText(4, UTC)).kind, 'stamp', 'the grammar is built from the handed-in prefix');
  assert.equal(withdrawalOf('other-', `${PREFIX}4.json`, withdrawalText(4, UTC)).kind, 'unreadable');
  assert.equal(nameStamp(PREFIX, `${PREFIX}12.json`), 12);
  assert.equal(nameStamp(PREFIX, `${PREFIX}012.json`), undefined);
});

interface Fake {
  readonly seams: Parameters<typeof permissionAsker>[0];
  readonly log: string[];
  state: { permission: Permission; asked: boolean; raiseAnswer: boolean; allowed: Allowed };
}

function fake(): Fake {
  const log: string[] = [];
  const state: Fake['state'] = { permission: 'none', asked: false, raiseAnswer: true, allowed: { kind: 'given', grant: grant(1), floor: 0, restarted: false } };
  const seams: Fake['seams'] = {
    raise: async () => { log.push('raise'); await Promise.resolve(); return state.raiseAnswer; },
    permission: async () => state.permission,
    allow: async () => { log.push('allow'); return state.allowed; },
    askedBefore: () => state.asked,
    noteAsked: async () => { log.push('note'); state.asked = true; },
    notKept: (result) => `not kept: ${result.kind}`,
  };

  return { seams, log, state };
}

test('E2S1-T14 (pure half): the icon asks once, a lost row asks again, Not now writes nothing, two calls make one modal', async () => {
  const one = fake();
  const asker = permissionAsker(one.seams);
  assert.equal(await asker.askFirst(), 'given');
  assert.deepEqual(one.log, ['note', 'raise', 'allow'], 'noteAsked was not recorded BEFORE the modal');
  one.state.permission = 'none';
  assert.equal(await asker.askFirst(), 'asked-before', 'the second press of the icon asked again');
  one.state.asked = false;
  one.log.length = 0;
  assert.equal(await asker.askFirst(), 'given');
  assert.deepEqual(one.log, ['note', 'raise', 'allow'], 'a row that was lost did not ask once more');

  const nothing = fake();
  nothing.state.raiseAnswer = false;
  assert.equal(await permissionAsker(nothing.seams).ask(), 'not-now');
  assert.deepEqual(nothing.log, ['raise'], 'Not now wrote something');

  const inForceNow = fake();
  inForceNow.state.permission = { given: UTC };
  assert.equal(await permissionAsker(inForceNow.seams).ask(), 'given');
  assert.equal(await permissionAsker(inForceNow.seams).askFirst(), 'given');
  assert.deepEqual(inForceNow.log, [], 'a modal was raised while the permission was in force');

  const open = fake();
  const both = permissionAsker(open.seams);
  const [a, b] = await Promise.all([both.ask(), both.ask()]);
  assert.equal(a, 'given');
  assert.equal(b, 'given');
  assert.equal(open.log.filter((line) => line === 'raise').length, 1, 'two calls while one modal was open raised two');
});

test('E2S1-T14 (pure half): an Allow the store did not keep is told with its sentence', async () => {
  const bad = fake();
  bad.state.allowed = { kind: 'unwritten', why: 'busy' };
  assert.deepEqual(await permissionAsker(bad.seams).ask(), { kind: 'not-kept', said: 'not kept: unwritten' });
  bad.state.allowed = { kind: 'kept', grant: grant(2) };
  assert.equal(await permissionAsker(bad.seams).ask(), 'given', 'a kept grant is given');
  bad.state.allowed = { kind: 'withdrawn' };
  assert.deepEqual(await permissionAsker(bad.seams).ask(), { kind: 'not-kept', said: 'not kept: withdrawn' });
});
