import assert from 'node:assert/strict';
import { test } from 'node:test';
import { freshPhraseRow, phraseById, phraseColours, phrasesFrom } from '../phrases';

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

/**
 * What the code round of 2026-09-14 found. Three real defects, each a test before it was a fix.
 *
 * <p>Two of them were inherited rather than written: the id collision and the blank derived name are
 * `chatPresets.ts`'s rules as they have always been, which is what moving them into `savedRows.ts`
 * exposed. Fixing them there fixes both lists at once.</p>
 */

test('a phrase is copied verbatim — the whitespace a person put around it is theirs', () => {
  const [phrase] = phrasesFrom([{ id: 'a', name: 'Indented', text: '  - item\n  - other\n' }]);

  assert.equal(phrase?.text, '  - item\n  - other\n',
    'the text was trimmed, so an indented snippet pastes unindented and a trailing newline is gone');
});

test('a positional id is never one an earlier row already claimed', () => {
  // The row that has no id sits at index 1, so the positional name it would take is `phrase-2` —
  // which the row before it wrote by hand. Before the fix both rows answered to `phrase-2` and the
  // second phrase could not be copied at all: the lookup found the first one.
  const phrases = phrasesFrom([
    { id: 'phrase-2', text: 'the one somebody named' },
    { text: 'the one with no id' },
  ]);
  const ids = phrases.map((phrase) => phrase.id);

  assert.equal(new Set(ids).size, 2, `two rows share an id (${ids.join(', ')}), so one of them cannot be copied`);
});

test('a phrase that opens with a blank line is named from the first line that has words', () => {
  const [phrase] = phrasesFrom([{ id: 'a', text: '\n\n   \nSELECT * FROM claims' }]);

  assert.equal(phrase?.name, 'SELECT * FROM claims', 'the button carries a blank label, so nobody can see it');
});

test('a colour per phrase, decided over the whole list', () => {
  const ids = ['phrase-1', 'phrase-2', 'phrase-3'];

  const colour = phraseColours(ids);
  const again = phraseColours(ids);

  const worn = ids.map(colour);
  assert.equal(new Set(worn).size, ids.length, `two phrases share a colour: ${worn.join(', ')}`);
  // Both halves: "all different" alone is satisfied by an allocator that answers differently every
  // time it is built, and then a phrase changes colour on every repaint.
  for (const id of ids) {
    assert.equal(again(id), colour(id), `${id} is a different colour the second time it is asked`);
  }
});

test('a phrase keeps its colour when another is added beside it', () => {
  // The property the whole design rests on: a name's slot comes from a hash of the name itself, so
  // the list decides who wins a genuine collision rather than who gets which colour. Measured over
  // the real allocator rather than assumed. (gemini, the plan round, predicted the opposite.)
  const before = phraseColours(['phrase-1', 'phrase-2', 'phrase-3']);
  const after = phraseColours(['phrase-1', 'phrase-2', 'phrase-3', 'phrase-4']);

  for (const id of ['phrase-1', 'phrase-2', 'phrase-3']) {
    assert.equal(after(id), before(id), `${id} changed colour because a fourth phrase was added`);
  }
});

test('a dozen phrases still get a dozen colours', () => {
  const many = Array.from({ length: 12 }, (_, at) => `phrase-${at + 1}`);

  const colour = phraseColours(many);

  assert.equal(new Set(many.map(colour)).size, 12, 'the palette ran out before twelve phrases');
});

test('a colliding neighbour CAN move a phrase, and that is the price of no repeats', () => {
  // Found by the code round, and it is real: with a list of two, adding `phrase-11` moves
  // `phrase-2`. The earlier "adding a seventh moved none of six" was true of that sequence and not
  // general — two ids whose preferred slot is the same one resolve it by list order, so a list that
  // gains the earlier-sorting one takes the slot from the later.
  //
  // It is pinned here rather than fixed because the two ways out are worse, and both were measured:
  // a per-id hash is subset-stable but gives THREE phrases only TWO distinct colours and twelve
  // only six — it destroys the "different colours" the issue asked for in order to protect the
  // "same colour" it also asked for. Subset-stability and no-repeats cannot both hold in general;
  // this is graph colouring, and something has to give. What gives is a transient: both surfaces
  // read ONE saved list, so they disagree only while one of them is stale.
  const before = phraseColours(['phrase-1', 'phrase-2']);
  const after = phraseColours(['phrase-1', 'phrase-2', 'phrase-11']);

  assert.notEqual(after('phrase-2'), before('phrase-2'),
    'this case stopped colliding — if the allocator changed, the limit documented beside it is now wrong');
  assert.equal(after('phrase-1'), before('phrase-1'), 'a phrase that does not collide must not move');

  // And the property that makes it tolerable: over ONE list, every phrase still has its own colour.
  const ids = ['phrase-1', 'phrase-2', 'phrase-11'];
  assert.equal(new Set(ids.map(after)).size, ids.length, 'the list lost its no-repeat promise');
});
