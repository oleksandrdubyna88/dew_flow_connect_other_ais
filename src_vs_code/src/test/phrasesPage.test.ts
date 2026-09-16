import assert from 'node:assert/strict';
import { test } from 'node:test';
import { phraseEdit, phraseRepaints, phrasesHtml, type PhraseRowView } from '../phrasesPage';

/**
 * The phrases tab, decided without a host.
 *
 * <p>Everything on this page is text the person in front of it wrote, which makes the escaping a
 * different question from the rest of the panel: it is their own phrase, and they can perfectly well
 * paste a `<script>` into it to see what happens.</p>
 */

const ROWS: readonly PhraseRowView[] = [
  { id: 'a', name: 'Ship it', text: 'make a pr, accept it, deploy' },
  { id: 'b', name: '', text: 'check that it works' },
];

function html(rows: readonly PhraseRowView[] = ROWS): string {
  return phrasesHtml({ rows, uiScale: 1 }, 'NONCE');
}

test('every phrase is a row with its name and its words', () => {
  const page = html();

  assert.match(page, /data-id="a"/, 'a saved phrase is not on the page');
  assert.match(page, /data-id="b"/, 'the second phrase is not on the page');
  assert.match(page, /value="Ship it"/, 'the name box does not hold the name');
  assert.match(page, /make a pr, accept it, deploy/, 'the phrase itself is not in its box');
});

test('the box a phrase is written in is a large one', () => {
  const page = html();

  assert.match(page, /<textarea[^>]*rows="(\d+)"/, 'the phrase is edited in something other than a textarea');
  const rows = Number(/<textarea[^>]*rows="(\d+)"/.exec(page)?.[1] ?? '0');
  assert.ok(rows >= 8, `the editor opens at ${rows} rows, which is not a box you can read a phrase in`);
  assert.match(page, /field-sizing: content/, 'the box does not grow with what is in it');
});

test('the page offers a way to add a phrase and a way to remove each one', () => {
  const page = html();

  assert.match(page, /data-add/, 'there is no way to add a phrase');
  assert.match(page, /data-remove data-id="a"/, 'there is no way to remove a phrase');
});

test('a phrase that contains markup is shown as text, not run as markup', () => {
  const page = html([{ id: 'x', name: 'Odd" onload="alert(1)', text: '<script>alert(2)</script>' }]);

  assert.ok(!page.includes('<script>alert(2)</script>'), 'a phrase was written into the page as markup');
  assert.ok(!page.includes('onload="alert(1)'), 'a name broke out of its attribute');
  assert.match(page, /&lt;script&gt;/, 'the phrase is not shown to the person who wrote it');
});

test('the name box holds what the file holds, empty included', () => {
  // The reader gives a nameless row a name from its first line. That is right on a button and wrong
  // here: a person who cleared the box would watch a derived name appear in it and then save it as
  // though they had typed it.
  const page = html();
  const second = page.slice(page.indexOf('data-id="b"'));

  assert.match(second, /data-field="name" value=""/, 'the editor invented a name the person never typed');
});

test('an empty list says what to do rather than showing nothing', () => {
  const page = html([]);

  assert.match(page, /No phrases yet/, 'an empty list is an empty page, which reads as broken');
  assert.match(page, /data-add/, 'an empty list offers no way to start');
});

test('the page can show a save that did not land, without being redrawn', () => {
  const page = html();

  assert.match(page, /id="save-failed"/, 'there is nowhere to say that a save failed');
  assert.match(page, /hidden/, 'the failure line is visible before anything has failed');
  assert.match(page, /said\.type === 'saveFailed'/, 'the page does not listen for a failed save');
  assert.match(page, /banner\.hidden = false/, 'the failure line is never actually shown');
});

test('a message names a field this list has, and nothing else is an edit', () => {
  assert.deepStrictEqual(phraseEdit({ type: 'edit', id: 'a', field: 'name', value: 'Ship' }),
    { kind: 'edit', id: 'a', field: 'name', value: 'Ship' });
  assert.deepStrictEqual(phraseEdit({ type: 'edit', id: 'a', field: 'text', value: 'go' }),
    { kind: 'edit', id: 'a', field: 'text', value: 'go' });

  for (const field of ['__proto__', 'constructor', 'prototype', 'id', 'main', 'nope']) {
    assert.deepStrictEqual(phraseEdit({ type: 'edit', id: 'a', field, value: 'x' }), { kind: 'ignore' },
      `${field} was accepted as a field of a phrase`);
  }
  assert.deepStrictEqual(phraseEdit({ type: 'edit', id: '', field: 'name', value: 'x' }), { kind: 'ignore' },
    'an edit naming no row was accepted');
  assert.deepStrictEqual(phraseEdit({ type: 'edit', id: 'a', field: 'name', value: 7 }), { kind: 'ignore' },
    'a value that is not text was accepted');
});

test('add, remove and zoom are understood, and anything else is ignored', () => {
  assert.deepStrictEqual(phraseEdit({ type: 'add' }), { kind: 'add' });
  assert.deepStrictEqual(phraseEdit({ type: 'remove', id: 'a' }), { kind: 'remove', id: 'a' });
  assert.deepStrictEqual(phraseEdit({ type: 'remove' }), { kind: 'ignore' });
  assert.deepStrictEqual(phraseEdit({ type: 'zoom', delta: 5 }), { kind: 'zoom', delta: 1 });
  assert.deepStrictEqual(phraseEdit({ type: 'zoom', delta: 'up' }), { kind: 'ignore' });

  for (const junk of [undefined, null, 'a string', 7, { type: 'nonsense' }]) {
    assert.deepStrictEqual(phraseEdit(junk), { kind: 'ignore' }, `${JSON.stringify(junk) ?? 'undefined'} was understood`);
  }
});

test('typing does not redraw the page, and changing its shape does', () => {
  assert.equal(phraseRepaints({ kind: 'edit', id: 'a', field: 'text', value: 'x' }), false,
    'a keystroke redraws the page, which moves the caret out of the box being typed in');
  assert.equal(phraseRepaints({ kind: 'add' }), true, 'a new row does not appear until something else redraws');
  assert.equal(phraseRepaints({ kind: 'remove', id: 'a' }), true, 'a removed row stays on screen');
  assert.equal(phraseRepaints({ kind: 'ignore' }), false, 'an ignored message redraws the page');
});

test('a save that later succeeds takes the failure line away again', () => {
  // Raised twice in the code round: the banner had no path back. Once the settings file became
  // writable the page went on saying nothing was being saved, which is worse than never saying it.
  const page = html();

  assert.match(page, /said\.type === 'saveOk'/, 'nothing clears the failure line when a save works again');
  assert.match(page, /banner\.hidden = true/, 'the failure line is never hidden again once shown');
});

// ---------- a phrase you can tell apart (issue #295) ----------
//
// Six phrases were six identical rectangles: `.phrase` gave every one the same left edge, so the
// only way to find one was to read it. And the edit form had no labels at all — two placeholders,
// which vanish the moment a box has content, which is the normal state of a saved phrase.

test('every phrase on the page wears its own colour', () => {
  const many: readonly PhraseRowView[] = Array.from({ length: 8 }, (_, at) => ({
    id: `phrase-${at + 1}`, name: `Phrase ${at + 1}`, text: 'words',
  }));

  const page = phrasesHtml({ rows: many, uiScale: 1 }, 'NONCE');

  // Every row carries a colour, and no two carry the same one. Asserting only "each has a style"
  // passes for a page that painted all eight the same fallback, which is the defect reported.
  const worn = many.map((row) => {
    const found = new RegExp(`data-id="${row.id}" style="border-left-color:([^"]+)"`).exec(page);
    assert.ok(found, `${row.id} has no colour of its own`);

    return found[1]!;
  });

  assert.equal(new Set(worn).size, worn.length, `two phrases share a colour: ${worn.join(', ')}`);
});

test('the frame is still a frame, whatever colour its edge is', () => {
  // The hue is inline; the BORDER is in the stylesheet. Without the rule the inline colour paints
  // nothing and the rows go back to being flat boxes — which is what was reported.
  // Whitespace-normalised: the assertions below are about DECLARATIONS, and a reformat of the
  // stylesheet's indentation is not a change to any of them. (local, the code round.)
  const css = html().split('<style>')[1]!.split('</style>')[0]!.replace(/\s+/gu, ' ');
  const rule = css.split('.phrase {')[1]?.split('}')[0] ?? '';

  assert.ok(rule.length > 0, 'the .phrase rule is gone, so the rows are unframed whatever colour they carry');
  assert.match(rule, /border: 1px solid/, 'the entries have no frame');
  assert.match(rule, /border-left-width: 3px/, 'the left edge has no width, so the colour paints nothing');
});

test('both boxes say what they are, and say it to a screen reader too', () => {
  const page = html();

  for (const [field, words] of [['name', 'Name'], ['text', 'What it copies']]) {
    // The label's `for` must name the control's `id` — that pairing is what makes it a label rather
    // than a caption sitting nearby, and it is the half a person using a screen reader depends on.
    const label = new RegExp(`<label for="(phrase-${field}-a)">([^<]+)</label>`).exec(page);
    assert.ok(label, `the ${field} box has no label`);
    assert.equal(label[2], words, `the ${field} box is labelled "${label[2]}"`);
    assert.ok(page.includes(`id="${label[1]}"`), `${label[1]} labels a control that does not exist`);
  }
});

test('nothing about a phrase changed to buy the colour and the labels', () => {
  const page = html();

  for (const hook of ['data-field="name"', 'data-field="text"', 'data-remove data-id="a"', 'data-id="a"']) {
    assert.ok(page.includes(hook), `the row lost ${hook}`);
  }
  assert.match(page, /value="Ship it"/, 'the name box no longer holds the name');
  assert.match(page, /make a pr, accept it, deploy/, 'the phrase itself is no longer in its box');
});
