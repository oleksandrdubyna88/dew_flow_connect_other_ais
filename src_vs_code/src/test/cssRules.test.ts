import assert from 'node:assert/strict';
import { test } from 'node:test';
import { stylesheet, winning, type Element } from './cssRules';

/**
 * `winning` — the value the cascade gives ONE property on an element (issue #537).
 *
 * <p>Each case is a way the first version answered wrong while the page's own test stayed green: our
 * own code reviewer probed it and found every one. A helper that a page test trusts to see an override
 * has to see all of them, or say it cannot.</p>
 */

const PRE: Element = { tag: 'pre', classes: [], attrs: {} };
const IN: readonly Element[] = [{ tag: 'div', classes: ['what'], attrs: {} }];

function sheetOf(css: string) {
  return stylesheet(`<style>${css}</style>`);
}

test('the LAST declaration in a rule is the one that counts', () => {
  const { value } = winning(sheetOf('.what pre { white-space: pre-wrap; white-space: pre; }'), PRE, IN, ['white-space']);

  assert.equal(value, 'pre', 'the first of two declarations was read, but CSS applies the last');
});

test('an !important declaration is not guessed at — it is reported as unreadable', () => {
  const { unreadable } = winning(
    sheetOf('.what pre { white-space: pre-wrap; } pre { white-space: pre !important; }'), PRE, IN, ['white-space'],
  );

  assert.deepEqual(unreadable.map((rule) => rule.selector), ['pre'],
    'a weaker selector with !important beats the rule this helper called the winner, and nobody was told');
});

test('an alias or a shorthand of the property competes too', () => {
  const wrap = winning(sheetOf('.what pre { overflow-wrap: anywhere; } .what pre { word-wrap: normal; }'),
    PRE, IN, ['overflow-wrap', 'word-wrap']);
  const scroll = winning(sheetOf('.what pre { overflow-x: auto; } .what pre { overflow: hidden; }'),
    PRE, IN, ['overflow-x', 'overflow']);

  assert.equal(wrap.value, 'normal', 'the legacy word-wrap took the wrap back and was not seen');
  assert.equal(scroll.value, 'hidden', 'the overflow shorthand took the scroll back and was not seen');
});

test('a name is matched whole, never inside a longer one', () => {
  const { value } = winning(sheetOf('.what pre { overflow-x: auto; --overflow: x; }'), PRE, IN, ['overflow']);

  assert.equal(value, undefined, 'overflow-x or a custom property was read as the overflow shorthand');
});

test('the strongest rule wins by specificity, then by coming later', () => {
  const later = winning(sheetOf('.what pre { white-space: pre-wrap; } .what pre { white-space: pre; }'), PRE, IN, ['white-space']);
  const stronger = winning(sheetOf('div.what pre { white-space: pre; } .what pre { white-space: pre-wrap; }'), PRE, IN, ['white-space']);

  assert.equal(later.value, 'pre');
  assert.equal(stronger.value, 'pre');
});
