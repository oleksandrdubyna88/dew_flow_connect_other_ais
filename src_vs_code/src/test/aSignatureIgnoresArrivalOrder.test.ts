import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sortedJoin } from '../codeUnitOrder';
import { type QuestionConsult, parseQuestionConsult, questionsSignature } from '../questionConsults';
import { sourceFiles, sourceOf } from './sourceReading';

/**
 * A directory watcher repaints only when what a person can see changed, and it decides that by comparing a
 * signature of the snapshot with the previous one. The files arrive in whatever order the directory lists them,
 * so a signature must be the same for the same records in any order — including two whose ids collate as one
 * (`é` as one code point and `e` + a combining accent), which a `localeCompare` sort left in arrival order.
 */

const composed = 'caf\u00e9';
const decomposed = 'cafe\u0301';

/** A record as the watcher reads it — through the real parser, so the fixture is one the code accepts. */
function record(id: string): QuestionConsult {
  const parsed = parseQuestionConsult(JSON.stringify({ id, status: 'consulting', rows: [] }));
  assert.ok(parsed !== undefined, `the parser refused the fixture '${id}'`);

  return parsed;
}

test('the question signature is the same whatever order the records arrive in, even when two ids collate as one', () => {
  assert.notEqual(composed, decomposed, 'the fixture lost its point: the two ids are one string');

  assert.equal(
    questionsSignature([record(decomposed), record(composed)]),
    questionsSignature([record(composed), record(decomposed)]),
    'one snapshot gave two signatures, so an unchanged directory repaints',
  );
});

test('a set’s signature is its parts in code-unit order, whatever order they arrive in', () => {
  assert.equal(sortedJoin([decomposed, composed], '|'), sortedJoin([composed, decomposed], '|'));
  assert.equal(sortedJoin(['b', 'a', 'B'], '|'), 'B|a|b', 'code units put every capital before every lower case');
});

// ---------------------------------------------------------------------------------------------
// The record watchers import `vscode`, so their shapes can only be read.
// ---------------------------------------------------------------------------------------------

/**
 * Every `signature:` VALUE of a directory shape in the shipped sources, with the file it is in — inline or a named
 * function alike. Read only in files that declare a `JsonDirectoryShape<`, so a `signature: string` field elsewhere is
 * not one; the interface's own `readonly signature:` is a type, not a value, and is skipped.
 */
function signatureLines(): readonly string[] {
  return sourceFiles()
    .map((one) => ({ one, text: sourceOf(one) }))
    .filter(({ text }) => text.includes('JsonDirectoryShape<'))
    .flatMap(({ one, text }) => text.split(/\r?\n/)
      // A property (`signature: …`) or a method (`signature(items) {`) — both are a value; `readonly signature:` is the type.
      .filter((line) => /^\s*signature\s*(:\s*\S|\()/.test(line))
      .map((line) => `${one}: ${line.trim()}`));
}

/** Signatures that are named functions, each held by a behavioural test of its own in this file. */
const TESTED_BY_NAME = ['signature: questionsSignature,'];

test('every directory shape builds its signature through sortedJoin, never a sort of its own', () => {
  const own = signatureLines()
    .filter((line) => !line.includes('sortedJoin('))
    .filter((line) => !TESTED_BY_NAME.some((named) => line.endsWith(named)));

  assert.deepEqual(own, [], 'a signature sorts by itself, where a collation can tie two different parts');
});

/** The four places whose text is compared with its own previous value — none may collate. */
const COMPARED_WITH_ITSELF = ['chatStoreHeartbeat.ts', 'consultationWatcher.ts', 'escalationWatcher.ts', 'questionConsults.ts'];

/** Every line of `text` that CALLS `.localeCompare(` — the defect's own shape; a comment naming it has no parenthesis. */
function collatingLines(text: string): readonly string[] {
  return text.split(/\r?\n/).filter((line) => line.includes('.localeCompare('));
}

/** {@link collatingLines} over one shipped source file. */
function collatingCalls(file: string): readonly string[] {
  return collatingLines(sourceOf(file));
}

test('the collation scan counts a call and never a comment that only names localeCompare', () => {
  // The scanner's own positive and negative control, on text it is handed — independent of any module's code.
  const sample = ['// ordered by code unit, never `localeCompare`', 'names.sort((a, b) => a.localeCompare(b));', 'names.sort(byCodeUnit);'].join('\r\n');

  assert.deepEqual(collatingLines(sample), ['names.sort((a, b) => a.localeCompare(b));']);
});

test('nothing whose text is compared with its own previous value sorts by collation', () => {
  const found = COMPARED_WITH_ITSELF.flatMap((file) => collatingCalls(file).map((line) => `${file}: ${line.trim()}`));

  assert.deepEqual(found, [], 'a change-detection text is ordered by localeCompare, which ties two different parts');
});

test('the collation scan still finds a call where collation is right', () => {
  // testing.md: a scan's companion must find a KNOWN instance in the tree, because only real code shows the pattern still
  // matches how calls are actually written. The notifications page's source list is a display order, sorted for a person.
  assert.ok(collatingCalls('notificationsPage.ts').length > 0, 'the scan no longer matches a .localeCompare( call it should see');
});

test('the signature scan still finds the watchers it guards', () => {
  // A scan that matches nothing passes forever; the three record watchers are the known instances.
  const found = signatureLines().map((line) => line.slice(0, line.indexOf(':')));

  assert.ok(found.includes('escalationWatcher.ts'), `the scan found no escalation signature: ${found.join(', ')}`);
  assert.ok(found.includes('consultationWatcher.ts'), `the scan found no consultation signature: ${found.join(', ')}`);
  assert.ok(found.includes('questionConsultWatcher.ts'), `the scan found no question signature: ${found.join(', ')}`);
});
