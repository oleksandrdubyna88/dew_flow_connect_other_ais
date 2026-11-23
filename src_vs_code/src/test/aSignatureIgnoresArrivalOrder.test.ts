import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sortedJoin } from '../codeUnitOrder';
import { sourceOf } from './sourceReading';

/**
 * What a change-detection text is compared with must not depend on the order its parts arrived in: a signature is its parts in code-unit order, including two
 * parts that collate as one (`é` as one code point and `e` + a combining accent), which a `localeCompare` sort leaves in arrival order. (The directory watchers
 * whose signatures this file also held went with the held gates, v2 E4.)
 */

const composed = 'caf\u00e9';
const decomposed = 'cafe\u0301';

test('a set’s signature is its parts in code-unit order, whatever order they arrive in', () => {
  assert.equal(sortedJoin([decomposed, composed], '|'), sortedJoin([composed, decomposed], '|'));
  assert.equal(sortedJoin(['b', 'a', 'B'], '|'), 'B|a|b', 'code units put every capital before every lower case');
});

/** The places whose text is compared with its own previous value — none may collate. (The directory watchers that were the rest of this list went with the held gates, v2 E4.) */
const COMPARED_WITH_ITSELF = ['chatStoreHeartbeat.ts'];

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
