import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ConversationRecord } from '../chatStore';
import { OpenConversation, PickerRow } from '../conversationPicker';
import {
  byLastUsed,
  forgetting,
  mayForget,
  opening,
  pickerTitle,
  scopeOf,
  scopeTooltip,
} from '../conversationChoice';

/**
 * What CHOOSING something in the picker means — decided without a host, for the same reason the rows
 * are.
 *
 * <p>The widget is a `createQuickPick` and nothing in this file can be reached through it: a sentence
 * a person reads when the conversation they picked has gone, the title that says which folders are
 * being listed, and the rule that an open conversation is not forgettable are all decisions, and a
 * decision inside an event handler is a decision no test can see.</p>
 */

const AT = Date.UTC(2026, 8, 13, 12, 0, 0);

const record = (over: Partial<ConversationRecord> = {}): ConversationRecord => ({
  version: 1,
  rev: 3,
  id: 'a1',
  title: 'Why the lock is fenced',
  passage: '',
  modelId: 'gemini-3-pro',
  messages: [],
  fromSession: false,
  carryFrom: 0,
  source: { kind: 'none' },
  workspace: 'D:\\rsd\\one',
  createdAt: AT - 3_600_000,
  updatedAt: AT - 60_000,
  ...over,
});

const open = (over: Partial<OpenConversation> = {}): OpenConversation => ({
  id: 'a1',
  title: 'Why the lock is fenced',
  modelId: 'gemini-3-pro',
  turns: 4,
  lastLine: 'because a read and a delete are two operations',
  updatedAt: AT - 60_000,
  ...over,
});

const row = (over: Partial<Extract<PickerRow, { kind: 'conversation' }>> = {}): PickerRow => ({
  kind: 'conversation',
  id: 'a1',
  label: 'Why the lock is fenced',
  description: 'gemini-3-pro · 4 turns · a minute ago',
  detail: 'because a read and a delete are two operations',
  live: false,
  ...over,
});

test('the title says which folders are being listed, and the button offers the other', () => {
  // The scope is the one thing about this list a person cannot see from the rows themselves: a
  // picker showing three conversations is the same picture whether the other forty are filtered out
  // or do not exist. So it is in the title, always, rather than only after the toggle is pressed.
  assert.notEqual(pickerTitle(false), pickerTitle(true), 'the title says the same thing in both scopes');
  assert.match(pickerTitle(false), /this folder/iu);
  assert.match(pickerTitle(true), /every folder/iu);
  // And the button says what pressing it WILL do, not what is on screen — a toggle labelled with the
  // current state is the oldest way to make somebody press it twice.
  assert.match(scopeTooltip(false), /every folder/iu);
  assert.match(scopeTooltip(true), /this folder/iu);
});

test('the scope is what the index is asked for, and this window is named in it', () => {
  assert.deepEqual(scopeOf(false, 'D:\\rsd\\one'), { kind: 'workspace', workspace: 'D:\\rsd\\one' });
  assert.deepEqual(scopeOf(true, 'D:\\rsd\\one'), { kind: 'everywhere' });
});

test('the open conversations are ordered newest first, and a copy is made rather than the caller’s array sorted', () => {
  // The registry hands them back in whatever order its map iterates, which is the order tabs were
  // opened — not the order anybody looks for them in. Newest first is what the stored rows below
  // them already use, so the whole list reads one way.
  const given: readonly OpenConversation[] = [
    open({ id: 'old', updatedAt: AT - 7_200_000 }),
    open({ id: 'new', updatedAt: AT - 1_000 }),
    open({ id: 'middle', updatedAt: AT - 60_000 }),
  ];

  assert.deepEqual(byLastUsed(given).map((one) => one.id), ['new', 'middle', 'old']);
  assert.deepEqual(given.map((one) => one.id), ['old', 'new', 'middle'], 'the caller’s own array was sorted under it');
});

test('two open conversations last used in the same instant have one order, not an arbitrary one', () => {
  // Two tabs opened by one keypress share an instant, and a list whose order changes between two
  // openings of the same picker is a list nobody can build a habit on.
  const given = [open({ id: 'b2' }), open({ id: 'a1' })];

  assert.deepEqual(byLastUsed(given).map((one) => one.id), ['a1', 'b2']);
});

test('a record that is still there is reopened, and it is the record the store just read', () => {
  // NOT the metadata the index was holding: the row was drawn from a copy that may be minutes old,
  // and what is put on screen has to be what is on disk now.
  const fresh = record({ title: 'Why the lock is fenced', rev: 9 });

  assert.deepEqual(opening('Why the lock is fenced', { kind: 'record', record: fresh }), { kind: 'reopen', record: fresh });
});

test('a conversation that has GONE says so and is taken off the list, rather than opening a blank tab', () => {
  // The gap this feature has by construction: the index is built in the background and a row is
  // chosen seconds or hours later, and another window can forget a conversation in between. A tab
  // pretending to be a conversation that is nowhere is the one outcome the reload serializer already
  // refuses, and it is refused here for the same reason.
  const gone = opening('Why the lock is fenced', { kind: 'absent' });

  assert.equal(gone.kind, 'gone');
  assert.match(gone.kind === 'gone' ? gone.message : '', /Why the lock is fenced/u,
    'the sentence does not name the conversation that vanished');
});

test('a record this build cannot read, and a disk that will not answer, are two different sentences — and neither is “gone”', () => {
  // `gone` takes the row off the list. Doing that for a permissions error or a record a newer build
  // wrote would hide a conversation that is perfectly safe, which is the three-way honesty
  // `chatStoreFile.ts` states at every one of its boundaries.
  const torn = opening('Why the lock is fenced', { kind: 'incompatible', reason: 'a conversation written by a newer version' });
  const shut = opening('Why the lock is fenced', { kind: 'unavailable', reason: 'the conversation could not be read from disk (EACCES)' });

  assert.equal(torn.kind, 'refused');
  assert.equal(shut.kind, 'refused');
  assert.match(torn.kind === 'refused' ? torn.message : '', /newer version/u, 'the reason the store gave was dropped');
  assert.match(shut.kind === 'refused' ? shut.message : '', /EACCES/u, 'the reason the store gave was dropped');
  assert.notEqual(torn.kind === 'refused' ? torn.message : '', shut.kind === 'refused' ? shut.message : '');
});

test('an answer this module has never heard of fails naming the ones it knows', () => {
  // A fifth outcome added to the store must not fall through into "reopen" or into silence. The
  // throw names the legal values, because the next person to read it is the one who added the fifth.
  assert.throws(
    () => opening('x', { kind: 'sideways' } as never),
    /record[\s\S]*absent[\s\S]*incompatible[\s\S]*unavailable/u,
  );
});

test('a conversation that was forgotten is dropped quietly; the row leaving IS the answer', () => {
  // No toast. The operator refused a dialog on a basic action by name, and a notification for every
  // trash press is the same interruption wearing a smaller hat — the row disappearing from a picker
  // that stayed open says it already.
  const done = forgetting('Why the lock is fenced', { kind: 'ok' });

  assert.deepEqual(done, { kind: 'forgotten' });
});

test('a forget that FAILED says so, because the row is still there and nothing else would explain it', () => {
  const failed = forgetting('Why the lock is fenced', { kind: 'failed', reason: 'the conversation is being changed by another window' });

  assert.equal(failed.kind, 'failed');
  assert.match(failed.kind === 'failed' ? failed.message : '', /another window/u);
  assert.match(failed.kind === 'failed' ? failed.message : '', /Why the lock is fenced/u);
});

test('an OPEN conversation is not forgettable, and the refusal says what to do about it', () => {
  // Its transcript is being written by the tab that holds it: deleting the files under a live
  // conversation would have the next push create them again, so the row would come back and the
  // person would have been told a lie. The trash button is not drawn on an open row either; this is
  // the keybinding's half of the same rule.
  const refused = mayForget(row({ live: true }));

  assert.equal(refused.kind, 'refuse');
  assert.match(refused.kind === 'refuse' ? refused.message : '', /open in a tab/iu);
});

test('a closed conversation is forgettable, by the id and title the row carries', () => {
  assert.deepEqual(mayForget(row({ id: 'b2', label: 'Another one' })), { kind: 'forget', id: 'b2', title: 'Another one' });
});

test('a separator, a notice, the offer to start one, and nothing at all are all “not a conversation”', () => {
  // Alt+Delete is pressed wherever the cursor happens to be. Every one of these carries no id — which
  // is what makes this total rather than careful — and each gets a sentence rather than silence.
  const rows: readonly (PickerRow | undefined)[] = [
    undefined,
    { kind: 'section', label: 'Recent' },
    { kind: 'notice', notice: 'empty', label: 'No conversations yet', detail: '' },
    { kind: 'new', label: 'New conversation for main.ts', detail: '' },
  ];
  for (const one of rows) {
    const answer = mayForget(one);
    assert.equal(answer.kind, 'refuse', `${one?.kind ?? 'nothing'} was treated as a conversation`);
    assert.ok((answer.kind === 'refuse' ? answer.message : '').length > 20, 'the refusal is not a sentence');
  }
});
