import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ConversationMeta, isSafeId } from '../chatStore';
import {
  NEW_ROW_ID,
  OPEN_SECTION,
  RECENT_SECTION,
  UNREADABLE_ROW_ID,
  pickerRows,
  rowAge,
} from '../conversationPicker';

/**
 * What the picker OFFERS, decided without a host.
 *
 * <p>Pure for the reason every decision in this feature is: the rows are the whole of what a person
 * chooses between, and a rule about them buried in a `createQuickPick` call is a rule no test can
 * reach. The `vscode` half is the widget and nothing else.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);
const WORKSPACE = 'D:\\rsd\\one';

const meta = (over: Partial<ConversationMeta> = {}): ConversationMeta => ({
  version: 1,
  id: 'a1',
  rev: 3,
  title: 'Why the lock is fenced',
  modelId: 'gemini-3-pro',
  turns: 4,
  lastLine: 'because a read and a delete are two operations',
  source: { kind: 'none' },
  workspace: WORKSPACE,
  updatedAt: AT - 60_000,
  ...over,
});

test('open conversations come first, and are never repeated among the closed ones', () => {
  // The ten-tabs case the whole feature exists for: what is already open is what a person is most
  // likely to be looking for, and offering it twice would make the list read as two conversations.
  const rows = pickerRows({
    open: [{ id: 'a1', title: 'Why the lock is fenced', modelId: 'gemini-3-pro', turns: 4 }],
    stored: [meta(), meta({ id: 'b2', title: 'The other one' })],
    workspace: WORKSPACE,
    everywhere: false,
    now: AT,
  });

  assert.deepEqual(rows.map((row) => row.kind), ['section', 'conversation', 'section', 'conversation']);
  assert.equal(rows[0]?.kind === 'section' ? rows[0].label : '', OPEN_SECTION);
  assert.equal(rows[1]?.kind === 'conversation' ? rows[1].id : '', 'a1');
  assert.equal(rows[2]?.kind === 'section' ? rows[2].label : '', RECENT_SECTION);
  assert.equal(rows[3]?.kind === 'conversation' ? rows[3].id : '', 'b2',
    'a conversation that is open was offered a second time among the closed ones');
});

test('the closed ones are newest first, by when they were last used', () => {
  // By `updatedAt`, never by when they began: the question a person is asking the list is "where was
  // I", and the answer to that moved when they last said something.
  const rows = pickerRows({
    open: [],
    stored: [
      meta({ id: 'old', updatedAt: AT - 900_000 }),
      meta({ id: 'new', updatedAt: AT - 1_000 }),
      meta({ id: 'mid', updatedAt: AT - 60_000 }),
    ],
    workspace: WORKSPACE,
    everywhere: false,
    now: AT,
  });

  assert.deepEqual(
    rows.filter((row) => row.kind === 'conversation').map((row) => (row.kind === 'conversation' ? row.id : '')),
    ['new', 'mid', 'old'],
  );
});

test('a row says what a person needs to tell two conversations apart', () => {
  const rows = pickerRows({
    open: [],
    stored: [meta({ updatedAt: AT - 7_200_000 })],
    workspace: WORKSPACE,
    everywhere: false,
    now: AT,
  });
  const row = rows.find((one) => one.kind === 'conversation');

  assert.equal(row?.kind === 'conversation' ? row.label : '', 'Why the lock is fenced');
  // The model, how much was said, and how long ago — the three things that separate two
  // conversations with the same title, which is the case the whole registry exists to handle.
  assert.match(row?.kind === 'conversation' ? row.description : '', /gemini-3-pro/u);
  assert.match(row?.kind === 'conversation' ? row.description : '', /4 turns/u);
  assert.match(row?.kind === 'conversation' ? row.description : '', /2 hours ago/u);
  assert.equal(row?.kind === 'conversation' ? row.detail : '', 'because a read and a delete are two operations',
    'the last thing said is not on the row, so two conversations with one title are indistinguishable');
});

test('only this workspace, unless the person asked for all of them', () => {
  const stored = [meta({ id: 'here' }), meta({ id: 'there', workspace: 'D:\\rsd\\two' })];
  const mine = pickerRows({ open: [], stored, workspace: WORKSPACE, everywhere: false, now: AT });
  const all = pickerRows({ open: [], stored, workspace: WORKSPACE, everywhere: true, now: AT });

  assert.deepEqual(mine.filter((r) => r.kind === 'conversation').map((r) => (r.kind === 'conversation' ? r.id : '')), ['here']);
  assert.equal(all.filter((r) => r.kind === 'conversation').length, 2);
  // And when they are shown, the row says WHERE, or two conversations from two projects look alike.
  const there = all.find((r) => r.kind === 'conversation' && r.id === 'there');
  assert.match(there?.kind === 'conversation' ? there.description : '', /two/u,
    'a conversation from another workspace does not say which');
});

test('a conversation with no folder behind it is still offered, and says so', () => {
  // A window with no folder open is an ordinary way to work, and the store files those under the
  // empty string. Dropping them would hide a whole way of using the product.
  const rows = pickerRows({
    open: [],
    stored: [meta({ id: 'loose', workspace: '' })],
    workspace: '',
    everywhere: false,
    now: AT,
  });

  assert.equal(rows.filter((r) => r.kind === 'conversation').length, 1);
});

test('the NEW row is offered first when it is asked for, and never otherwise', () => {
  // `go to` opens the picker with this preselected when the active tab has no conversation. Nothing
  // is created until it is chosen — the operator's own rule: a hotkey that silently makes an empty
  // chat is the trap the two commands exist to avoid.
  const withNew = pickerRows({
    open: [], stored: [meta()], workspace: WORKSPACE, everywhere: false, now: AT, offerNew: 'README.md',
  });

  assert.equal(withNew[0]?.kind, 'new');
  assert.equal(withNew[0]?.kind === 'new' ? withNew[0].id : '', NEW_ROW_ID);
  assert.match(withNew[0]?.kind === 'new' ? withNew[0].label : '', /README\.md/u,
    'the row does not name the tab the conversation would belong to');

  const without = pickerRows({ open: [], stored: [meta()], workspace: WORKSPACE, everywhere: false, now: AT });

  assert.notEqual(without[0]?.kind, 'new');
});

test('a store that would not answer says so, instead of looking empty', () => {
  // The distinction A2 built the typed listing for. An empty list and a list that could not be read
  // are opposite facts, and only one of them means "you have no conversations".
  const rows = pickerRows({
    open: [], stored: 'unavailable', workspace: WORKSPACE, everywhere: false, now: AT,
  });

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.kind === 'unreadable' ? rows[0].id : '', UNREADABLE_ROW_ID);
  assert.match(rows[0]?.kind === 'unreadable' ? rows[0].label : '', /could not be read/u);
});

test('an unreadable store still offers the NEW row, because that needs no store', () => {
  const rows = pickerRows({
    open: [], stored: 'unavailable', workspace: WORKSPACE, everywhere: false, now: AT, offerNew: 'main.ts',
  });

  assert.equal(rows[0]?.kind, 'new', 'a disk that would not answer took away the one action that does not need it');
});

test('nothing at all is one row saying so, not an empty list', () => {
  // An empty QuickPick reads as a broken command. It says what is true instead.
  const rows = pickerRows({ open: [], stored: [], workspace: WORKSPACE, everywhere: false, now: AT });

  assert.equal(rows.length, 1);
  assert.equal(rows[0]?.kind, 'empty');
});

test('the list is bounded, and says when it was cut', () => {
  // Ninety days at this operator's rate is hundreds of rows; a QuickPick that renders all of them is
  // slow to open for a list nobody scrolls to the end of. The cut is stated rather than silent.
  const many = Array.from({ length: 140 }, (_, at) => meta({ id: `c${at}`, updatedAt: AT - at * 1_000 }));
  const rows = pickerRows({ open: [], stored: many, workspace: WORKSPACE, everywhere: false, now: AT });
  const shown = rows.filter((row) => row.kind === 'conversation');

  assert.equal(shown.length, 100, 'the whole store was rendered into the picker');
  assert.ok(rows.some((row) => row.kind === 'more'), 'the list was cut and said nothing about it');
});

test('how long ago reads the way a person would say it', () => {
  assert.equal(rowAge(AT, AT - 30_000), 'just now');
  assert.equal(rowAge(AT, AT - 90_000), 'a minute ago');
  assert.equal(rowAge(AT, AT - 20 * 60_000), '20 minutes ago');
  assert.equal(rowAge(AT, AT - 3 * 3_600_000), '3 hours ago');
  assert.equal(rowAge(AT, AT - 26 * 3_600_000), 'yesterday');
  assert.equal(rowAge(AT, AT - 5 * 86_400_000), '5 days ago');
  // And a clock that stepped backwards is not a conversation from the future.
  assert.equal(rowAge(AT, AT + 60_000), 'just now');
});

test('the two action rows carry ids no conversation can ever have', () => {
  // They sit in the same list as conversations and are told apart by their id, so an id a record
  // could legitimately hold would be a crafted conversation impersonating the action beside it.
  // `isSafeId` is the one rule that says what a record's id may be, so it is the one asked here.
  assert.equal(isSafeId(NEW_ROW_ID), false, 'the new-conversation row could be impersonated by a record');
  assert.equal(isSafeId(UNREADABLE_ROW_ID), false, 'the unreadable-store row could be impersonated by a record');
  // And they are not empty or whitespace, which a widget would render as a blank line.
  assert.match(NEW_ROW_ID, /\S/u);
  assert.match(UNREADABLE_ROW_ID, /\S/u);
});
