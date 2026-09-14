import assert from 'node:assert/strict';
import { test } from 'node:test';
import { rowsAfter, rowsOf, viewOf, type SavedPhraseRow } from '../phrasesEdit';
import { phrasesFrom } from '../phrases';

/**
 * What each command does to the stored rows.
 *
 * <p>Outside the host, for the reason `rolesEdit.ts` was written down: a rule that lives inside a
 * webview host is a rule no test can reach, and *Remove this role* silently did nothing for a whole
 * plan because of it.</p>
 *
 * <p><b>`unchanged` is not a detail either.</b> Every write is a configuration change VS Code
 * broadcasts to every listener in the window, so a write that says what the file already said is
 * noise every other part of the extension reacts to.</p>
 */

const ROWS: readonly SavedPhraseRow[] = [
  { id: 'a', name: 'Ship it', text: 'make a pr' },
  { id: 'b', name: 'Check', text: 'check it works' },
];

test('adding a phrase appends a row the reader will keep', () => {
  const outcome = rowsAfter(ROWS, { kind: 'add' });

  assert.equal(outcome.kind, 'rows');
  const rows = outcome.kind === 'rows' ? outcome.rows : [];
  assert.equal(rows.length, 3, 'Add did not add a row');
  assert.equal(phrasesFrom(rows).length, 3,
    'Add wrote a row its own reader drops, so the press does nothing and litters settings.json');
});

test('a phrase added twice does not collide with the one before it', () => {
  const once = rowsAfter(ROWS, { kind: 'add' });
  const rows = once.kind === 'rows' ? once.rows : [];
  const twice = rowsAfter(rows, { kind: 'add' });
  const both = twice.kind === 'rows' ? twice.rows : [];

  assert.equal(new Set(both.map((row) => row['id'])).size, both.length, 'two added phrases share an id');
});

test('removing a phrase drops that row and leaves the others', () => {
  const outcome = rowsAfter(ROWS, { kind: 'remove', id: 'a' });
  const rows = outcome.kind === 'rows' ? outcome.rows : [];

  assert.deepStrictEqual(rows.map((row) => row['id']), ['b'], 'Remove took the wrong row, or none');
});

test('removing a row that is not there changes nothing, rather than writing the list back', () => {
  assert.equal(rowsAfter(ROWS, { kind: 'remove', id: 'gone' }).kind, 'unchanged',
    'a stale Remove wrote the list back, which every window then reacts to for nothing');
});

test('an edit changes the row it names and no other', () => {
  const outcome = rowsAfter(ROWS, { kind: 'edit', id: 'b', field: 'text', value: 'deploy' });
  const rows = outcome.kind === 'rows' ? outcome.rows : [];

  assert.deepStrictEqual(rows.map((row) => row['text']), ['make a pr', 'deploy'], 'the wrong row was edited');
  assert.equal(rows[1]?.['name'], 'Check', 'editing the text lost the name beside it');
});

test('an edit that says what the row already says changes nothing', () => {
  assert.equal(rowsAfter(ROWS, { kind: 'edit', id: 'a', field: 'name', value: 'Ship it' }).kind, 'unchanged',
    'choosing the value a row already holds wrote the file back');
});

test('an edit naming a row that is gone changes nothing', () => {
  assert.equal(rowsAfter(ROWS, { kind: 'edit', id: 'gone', field: 'name', value: 'x' }).kind, 'unchanged',
    'a message naming a removed row produced a new array of identical content, which the host writes');
});

test('an edit may empty a field — clearing a name is something a person does', () => {
  const outcome = rowsAfter(ROWS, { kind: 'edit', id: 'a', field: 'name', value: '' });
  const rows = outcome.kind === 'rows' ? outcome.rows : [];

  assert.equal(rows[0]?.['name'], '', 'clearing the name box was refused or ignored');
});

test('a stored value that is not a list of rows yields no rows rather than a throw', () => {
  assert.deepStrictEqual(rowsOf(undefined), []);
  assert.deepStrictEqual(rowsOf('a string'), []);
  assert.deepStrictEqual(rowsOf([1, null, 'two', ['three']]), [], 'something that is not a row was kept as one');
  assert.deepStrictEqual(rowsOf([{ id: 'a' }]), [{ id: 'a' }]);
});

test('the page is shown the rows as stored, with a missing field read as empty', () => {
  // Through `rowsOf`, because that is where a row is given the id the page names it by — asking
  // `viewOf` about raw settings is asking it a question it is no longer the one to answer.
  const view = viewOf(rowsOf([{ id: 'a', text: 'only a text' }, { name: 'only a name' }]));

  assert.deepStrictEqual(view[0], { id: 'a', name: '', text: 'only a text' },
    'a row with no name was shown one it does not have');
  assert.ok((view[1]?.id ?? '').length > 0, 'a row with no id has nothing for a button to name');
});

test('the rows keep fields this build does not read', () => {
  const outcome = rowsAfter([{ id: 'a', name: 'One', text: 'x', mine: 'keep me' }], { kind: 'edit', id: 'a', field: 'name', value: 'Two' });
  const rows = outcome.kind === 'rows' ? outcome.rows : [];

  assert.equal(rows[0]?.['mine'], 'keep me',
    'a field this build does not know was deleted — the trap remoteVendor fell into for three releases');
});

/**
 * What the code round of 2026-09-14 found. Three reviewers, independently, on the same defect.
 *
 * <p>The schema lets a person write `{ "text": "deploy it" }` into `settings.json` — the reader was
 * changed in story 1 so that such a row is KEPT rather than dropped. The tab then showed it with an
 * invented id, and every edit and every Remove aimed at that id matched nothing and did nothing.
 * Visible, and inert.</p>
 */

test('a row saved without an id can still be edited and removed', () => {
  const rows = rowsOf([{ text: 'deploy it' }, { text: 'check that it works' }]);
  const first = viewOf(rows)[0]?.id ?? '';

  assert.ok(first.length > 0, 'a row with no id was given nothing for a button to name');
  assert.equal(rowsAfter(rows, { kind: 'edit', id: first, field: 'name', value: 'Ship' }).kind, 'rows',
    'editing a row that had no id in settings.json silently did nothing');
  assert.equal(rowsAfter(rows, { kind: 'remove', id: first }).kind, 'rows',
    'removing a row that had no id in settings.json silently did nothing');
});

test('the id a row is shown under is the id that gets stored', () => {
  const rows = rowsOf([{ text: 'deploy it' }]);
  const shown = viewOf(rows)[0]?.id ?? '';
  const outcome = rowsAfter(rows, { kind: 'edit', id: shown, field: 'name', value: 'Ship' });
  const stored = outcome.kind === 'rows' ? outcome.rows : [];

  assert.equal(stored[0]?.['id'], shown,
    'the row is written back under a different id than the one the page was told, so the next click misses again');
});
