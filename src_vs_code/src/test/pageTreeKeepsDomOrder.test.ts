import assert from 'node:assert/strict';
import { test } from 'node:test';
import { pageTree } from './pageTree';

/**
 * The page tree reads text in DOM order (CodeRabbit on #688): text kept apart from the child elements made
 * `<p>a<b>b</b>c</p>` read `acb`, so a test of a rendered label could pass or fail on the wrong order.
 */

test('an element\'s text is its text and its children\'s, in the order the page wrote them', () => {
  const tree = pageTree('<p id="x">a<b>b</b>c<i>d<u>e</u></i>f</p>');

  assert.equal(tree.one((node) => node.id === 'x', 'the paragraph').text(), 'abcdef');
});

test('a textarea still takes its own text as its value', () => {
  const tree = pageTree('<textarea id="t">hello &amp; bye</textarea>');

  assert.equal(tree.one((node) => node.id === 't', 'the textarea').value, 'hello & bye');
});
