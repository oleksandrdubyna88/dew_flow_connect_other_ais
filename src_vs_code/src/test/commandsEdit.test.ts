import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { CommandRow } from '../commands';
import { commandsAfter, forgettable, nextId, textBelongs } from '../commandsEdit';

/** What an edit on the Edit commands page does to the rows — issue #467, Epic B. */

const docs: CommandRow = { id: 'custom-1', title: 'Docs', enabled: false, stage: 'any' };

/** Hands out the given tokens in order, the way the host hands out random ones. */
const tokens = (...given: string[]) => (): string => given.shift() ?? 'spent';

test('adding makes an off command in every round, with a fresh id', () => {
  const out = commandsAfter([], { kind: 'add', token: 'a1b2' }, {});

  assert.deepEqual(out, { kind: 'rows', rows: [{ id: 'custom-a1b2', title: 'New command', enabled: false, stage: 'any' }], forget: [] });
});

test('a new id skips a row AND a text file a removed command left on disk', () => {
  assert.equal(nextId([docs], {}, tokens('1', '2')), 'custom-2');
  assert.equal(nextId([], { 'command-custom-1': 'old words' }, tokens('1', '2')), 'custom-2',
    'an orphaned file would give the new command old words');
  assert.equal(nextId([], { 'command-custom-1': '' }, tokens('1', '2')), 'custom-2', 'even a blank one — its file is still there');
});

test('ids are random, not counted — two sides of one machine share the texts but not the rows', () => {
  // Each side reads only ITS rows, so both would have counted to `custom-1` and written one file. (codex,
  // the code round.) The token is the host's `randomBytes`; the rule here is only that a taken one is skipped.
  assert.match(nextId([], {}, tokens('7f3a')), /^custom-7f3a$/);
});

test('removing a command forgets its text file with it', () => {
  assert.deepEqual(commandsAfter([docs], { kind: 'remove', id: 'custom-1' }, {}), { kind: 'rows', rows: [], forget: ['command-custom-1'] });
});

test('retitling and restaging change that row only', () => {
  const other: CommandRow = { ...docs, id: 'custom-2', title: 'Lint' };

  const retitled = commandsAfter([docs, other], { kind: 'retitle', id: 'custom-1', value: 'Module docs' }, {});
  const restaged = commandsAfter([docs, other], { kind: 'restage', id: 'custom-2', value: 'code' }, {});

  assert.deepEqual(retitled.kind === 'rows' ? retitled.rows.map((one) => one.title) : [], ['Module docs', 'Lint']);
  assert.deepEqual(restaged.kind === 'rows' ? restaged.rows.map((one) => one.stage) : [], ['any', 'code']);
});

test('switching on is refused without text, allowed with it, and switching off is never refused', () => {
  const refused = commandsAfter([docs], { kind: 'switch', id: 'custom-1', value: true }, {});
  const allowed = commandsAfter([docs], { kind: 'switch', id: 'custom-1', value: true }, { 'command-custom-1': 'Update the docs.' });
  const off = commandsAfter([{ ...docs, enabled: true }], { kind: 'switch', id: 'custom-1', value: false }, {});

  assert.equal(refused.kind, 'refused');
  assert.match(refused.kind === 'refused' ? refused.why : '', /"Docs" has no text yet/);
  assert.deepEqual(allowed.kind === 'rows' ? allowed.rows[0]?.enabled : undefined, true);
  assert.deepEqual(off.kind === 'rows' ? off.rows[0]?.enabled : undefined, false);
});

test('an edit to a command that is not there changes nothing', () => {
  assert.deepEqual(commandsAfter([docs], { kind: 'remove', id: 'custom-9' }, {}), { kind: 'unchanged' });
});

test('the page may write a shipped text or a listed command’s text, and nothing else', () => {
  assert.equal(textBelongs([docs], 'command-autonomy'), true);
  assert.equal(textBelongs([docs], 'command-custom-1'), true);
  assert.equal(textBelongs([docs], 'command-custom-2'), false, 'a removed command’s file');
  assert.equal(textBelongs([docs], 'role2-general'), false, 'a role’s prompt');
});

test('a removed command may take only its OWN file with it, never a shipped text', () => {
  assert.equal(forgettable('command-custom-1'), true);
  assert.equal(forgettable('command-preamble'), false, 'the shipped Preamble’s override is the person’s, not a row’s');
});
