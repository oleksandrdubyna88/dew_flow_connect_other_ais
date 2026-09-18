import assert from 'node:assert/strict';
import { test } from 'node:test';

import { corpusLanguage } from '../corpusLanguage';
import { collectorLanguages } from './sourceLanguages';

/**
 * The one table that says which of three languages a row is in — pinned against the producer, and
 * against the two ways a lookup table quietly answers wrongly.
 */

test('every spelling the column might hold lands on one canonical id', () => {
  for (const spelling of ['CSharp', 'csharp', 'cs', 'C#', '  CSharp  ']) {
    assert.equal(corpusLanguage(spelling), 'csharp', spelling);
  }
  for (const spelling of ['TypeScript', 'ts', 'TS']) {
    assert.equal(corpusLanguage(spelling), 'typescript', spelling);
  }
  for (const spelling of ['JavaScript', 'js', 'Js']) {
    assert.equal(corpusLanguage(spelling), 'javascript', spelling);
  }
});

test('a language this page does not read is nothing, never a guess', () => {
  for (const other of ['Unsupported', '', 'python', 'Fortran', 'mjs', 'c']) {
    assert.equal(corpusLanguage(other), undefined, other);
  }
});

test('a value that is not a string is nothing rather than a throw', () => {
  for (const bad of [null, undefined, 42, {}, [], true]) {
    assert.equal(corpusLanguage(bad), undefined, String(bad));
  }
});

/**
 * The reason the table is a `Map`.
 *
 * <p>An object literal answers `['constructor']` with `Object.prototype.constructor` — truthy, a
 * function, and typed as a string by the index signature — and the highlighter would then hand
 * Shiki a function as a grammar id. Verified by mutation: with the table an object, this goes red
 * on the first name.</p>
 */
test('a name inherited from Object.prototype is not a language', () => {
  for (const inherited of ['constructor', '__proto__', 'toString', 'hasOwnProperty', 'valueOf']) {
    assert.equal(corpusLanguage(inherited), undefined, inherited);
  }
});

/**
 * The table is checked against the PRODUCER, not against itself.
 *
 * <p>A table that only agrees with its own unit tests drifts silently the day the collector learns
 * a fourth language: the page then renders it plain and counts nothing for it, while every test
 * here stays green. `SourceLanguage` is where that decision is actually made, so this reads it, and
 * the loop DERIVES the expectation — a fourth member arrives with nothing where the loop demands a
 * language, and the assertion names it.</p>
 */
test('every language the collector can record is one this table knows, and only those', () => {
  for (const member of collectorLanguages()) {
    assert.equal(corpusLanguage(member) !== undefined, member !== 'Unsupported', member);
  }
});
