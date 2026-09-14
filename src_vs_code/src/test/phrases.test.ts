import assert from 'node:assert/strict';
import { test } from 'node:test';
import { freshPhraseRow, phraseById, phrasesFrom } from '../phrases';

/**
 * The phrases a person keeps, read from a setting they can edit by hand.
 *
 * <p>This is a BOUNDARY, and the same one `chatPresets.test.ts` guards for its own two lists: an
 * array in `settings.json`, composed by the person rather than picked from a catalog this product
 * shipped. A malformed row is dropped rather than trusted, and the list is never lost because one
 * row of it was wrong.</p>
 *
 * <p><b>The rule this file exists for, and the one that differs from the prompt presets:</b> a
 * phrase is a piece of writing somebody wants back verbatim, and its name is only what fits on a
 * button. So a row with a text and no name is KEPT and given a name from its own first line.
 * Dropping it would discard what they typed, which is the one thing a reader of a hand-edited file
 * must never do quietly. (Gate finding 1, accepted 2026-09-14.)</p>
 */

test('a phrase needs a text, and a row without one is dropped without taking the others', () => {
  const phrases = phrasesFrom([
    { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy' },
    { id: 'b', name: 'no text', text: '   ' },
    { id: 'c', name: 'also no text' },
    { id: 'd', name: 'Check', text: 'check that it works' },
    'not an object',
    null,
    42,
    ['nested'],
  ]);

  assert.deepStrictEqual(phrases.map((phrase) => phrase.name), ['Ship it', 'Check'],
    'a malformed row took the good ones with it');
});

test('a phrase with a text and no name is kept, and named from its own first line', () => {
  const phrases = phrasesFrom([
    { id: 'a', text: 'deploy it\nand read the logs' },
    { id: 'b', name: '   ', text: 'accept the pull request' },
  ]);

  assert.deepStrictEqual(phrases.map((phrase) => phrase.name), ['deploy it', 'accept the pull request'],
    'a phrase somebody typed into settings.json by hand was thrown away for having no name');
  assert.deepStrictEqual(phrases.map((phrase) => phrase.text), ['deploy it\nand read the logs', 'accept the pull request'],
    'naming the row changed the words it holds');
});

test('a derived name is cut to the width of a button, and says that it was cut', () => {
  const long = 'x'.repeat(200);
  const [phrase] = phrasesFrom([{ id: 'a', text: long }]);

  assert.equal(phrase?.name.length, 60, 'the derived name is not the width a button has');
  assert.ok(phrase?.name.endsWith('…'), 'a cut name does not say that it was cut');
  assert.equal(phrase?.text, long, 'the TEXT was truncated — a phrase has meaning to keep');
});

test('a name somebody wrote is cut to the same width, and the text is never touched', () => {
  const [phrase] = phrasesFrom([{ id: 'a', name: 'n'.repeat(120), text: 'p'.repeat(5000) }]);

  assert.equal(phrase?.name.length, 60, 'a hand-written name is not capped');
  assert.equal(phrase?.text.length, 5000, 'the text was truncated');
});

test('every phrase gets an id, and two rows never share one', () => {
  const phrases = phrasesFrom([
    { text: 'no id at all' },
    { id: '', text: 'an empty id' },
    { id: 'same', text: 'first' },
    { id: 'same', text: 'second' },
  ]);
  const ids = phrases.map((phrase) => phrase.id);

  assert.equal(new Set(ids).size, ids.length, 'two phrases share an id, so a click is ambiguous');
  assert.deepStrictEqual(ids, ['phrase-1', 'phrase-2', 'same', 'phrase-4'],
    'a repaired id is not the positional one the list can be looked up by');
});

test('a setting that is not a list at all yields no phrases rather than a throw', () => {
  for (const junk of [undefined, null, 'a string', 7, { not: 'an array' }]) {
    assert.deepStrictEqual(phrasesFrom(junk), [], `${JSON.stringify(junk) ?? 'undefined'} was not survived`);
  }
});

test('a row carrying prototype keys is read as data, and pollutes nothing', () => {
  const phrases = phrasesFrom([JSON.parse('{"id":"a","text":"fine","__proto__":{"polluted":true}}')]);

  assert.equal(phrases.length, 1, 'an ordinary row was refused for carrying an odd key');
  assert.equal(({} as Record<string, unknown>)['polluted'], undefined, 'Object.prototype was polluted');
});

test('a new row is one the reader keeps — the defect Add-a-model shipped with', () => {
  const fresh = freshPhraseRow([]);

  assert.deepStrictEqual(phrasesFrom([fresh]).length, 1,
    'Add a phrase writes a row its own reader drops, so the press does nothing and litters settings.json');
});

test('a fresh row never collides with one that is already there', () => {
  const first = freshPhraseRow([]);
  const second = freshPhraseRow([first]);

  assert.notEqual(first.id, second.id, 'two added phrases share an id');
});

test('a button names the phrase it chose, and an id that is gone chooses nothing', () => {
  const phrases = phrasesFrom([{ id: 'a', name: 'One', text: 'first' }, { id: 'b', name: 'Two', text: 'second' }]);

  assert.equal(phraseById(phrases, 'b')?.text, 'second', 'the wrong phrase was looked up');
  assert.equal(phraseById(phrases, 'gone'), undefined, 'an id naming no row returned something');
  assert.equal(phraseById(phrases, ''), undefined, 'an empty id returned something');
});
