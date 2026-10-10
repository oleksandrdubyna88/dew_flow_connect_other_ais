import assert from 'node:assert/strict';
import { test } from 'node:test';

import { compound, DomDocument } from './pageDom';

/**
 * The page DOM's own contract (`generated-code-tests.md`: a fake is code under test, and may be stricter than the real
 * thing, never more permissive). Each case is a place the older stub answered wrongly in the permissive direction.
 */

const PAGE = '<!DOCTYPE html><div id="a" class="x y" hidden><details data-fold="k1" open><summary>One</summary></details>'
  + '<details data-fold="k2"><summary>Two</summary></details><select id="s"><option value="p">P</option>'
  + '<option value="q" selected>Q</option></select><input id="i" value="start"></div>';

test('an id that is not in the page is null — never an invented element', () => {
  const doc = new DomDocument(PAGE);

  assert.equal(doc.getElementById('missing'), null);
  assert.notEqual(doc.getElementById('a'), null, 'and one that IS there is found');
});

test('a selector it does not understand throws, rather than matching nothing', () => {
  const doc = new DomDocument(PAGE);

  assert.throws(() => doc.querySelectorAll('div details'), /does not understand/);
  assert.throws(() => doc.querySelectorAll('details:not([open])'), /does not understand/);
  assert.throws(() => compound(''), /empty selector/);
});

test('compound selectors find exactly what is there', () => {
  const doc = new DomDocument(PAGE);

  assert.deepEqual(doc.querySelectorAll('details[data-fold]').map((one) => one.getAttribute('data-fold')), ['k1', 'k2']);
  assert.deepEqual(doc.querySelectorAll('details[data-fold][open]').map((one) => one.getAttribute('data-fold')), ['k1']);
  assert.equal(doc.querySelectorAll('[data-fold="k2"]').length, 1);
  assert.equal(doc.querySelectorAll('.y').length, 1, 'a class is a word of the attribute, not the whole of it');
  assert.equal(doc.querySelectorAll('#a').length, 1);
});

test('hidden, open and className are the attributes, both ways', () => {
  const doc = new DomDocument(PAGE);
  const a = doc.getElementById('a')!;

  assert.equal(a.hidden, true);
  a.hidden = false;
  assert.equal(a.getAttribute('hidden'), null);
  const two = doc.querySelector('[data-fold="k2"]')!;
  two.open = true;
  assert.equal(doc.querySelectorAll('details[open]').length, 2);
});

test('a select answers the selected option, and a value no option has selects none', () => {
  const doc = new DomDocument(PAGE);
  const select = doc.getElementById('s')!;

  assert.equal(select.value, 'q');
  select.value = 'p';
  assert.equal(select.value, 'p');
  select.value = 'nope';
  assert.equal(select.value, 'p', 'with nothing selected, the first usable option is what a select shows');
});

test('innerHTML replaces the children with what the markup says, and text stays text', () => {
  const doc = new DomDocument(PAGE);
  const a = doc.getElementById('a')!;

  a.innerHTML = '<b>bold</b> &lt;i&gt;';
  assert.equal(a.children.length, 1);
  assert.equal(a.textContent, 'bold <i>');
  assert.equal(doc.getElementById('s'), null, 'what was replaced is gone from the document');
});

test('appendChild MOVES a node, which is how a page reorders a list', () => {
  const doc = new DomDocument(PAGE);
  const a = doc.getElementById('a')!;
  const first = doc.querySelector('[data-fold="k1"]')!;

  a.appendChild(first);

  assert.deepEqual(doc.querySelectorAll('details').map((one) => one.getAttribute('data-fold')), ['k2', 'k1']);
});

test('an event reaches the element, its ancestors, then the document', () => {
  const doc = new DomDocument(PAGE);
  const heard: string[] = [];
  doc.getElementById('a')!.addEventListener('click', () => heard.push('a'));
  doc.addEventListener('click', (event) => heard.push(`document:${event.target.getAttribute('data-fold') ?? ''}`));

  doc.querySelector('[data-fold="k1"]')!.click();

  assert.deepEqual(heard, ['a', 'document:k1']);
});

test('an input keeps what was typed, and focus is the document\'s', () => {
  const doc = new DomDocument(PAGE);
  const input = doc.getElementById('i')!;

  assert.equal(input.value, 'start');
  input.value = 'typed';
  input.focus();
  assert.equal(input.value, 'typed');
  assert.equal(doc.activeElement, input);
});
