import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type LineMark, pairDiff } from '../lineDiff';

/**
 * What differs between two skeletons — and, first, what only LOOKS like it differs.
 *
 * <p>The measurement that shaped this module is the one test that matters most here, so it comes
 * first: anonymisation numbers placeholders in order of declaration, so one added line renumbers
 * everything below it and a plain diff reports the whole method as rewritten.</p>
 */

const marks = (text: string): string => text;

/** The two sides as one readable string, so a failure shows the shape rather than two arrays. */
function shape(before: readonly LineMark[], after: readonly LineMark[]): string {
  return `${before.join(' ')} | ${after.join(' ')}`;
}

const changedCount = (...sides: readonly (readonly LineMark[])[]): number =>
  sides.flat().filter((mark) => mark !== 'same').length;

/**
 * ONE added line must read as one added line, not as a rewritten method.
 *
 * <p>The numbers in the docblock of `lineDiff.ts` come from exactly this pair: a plain line diff
 * marks 7 of 13 lines across the two sides, because `var_2` became `var_3` everywhere below the
 * insertion. That is the defect this module exists to avoid, so it is asserted with the number.</p>
 */
test('a fix that adds one line does not repaint the whole method', () => {
  const before = marks([
    'void method_1(int var_1)',
    '{',
    '    var var_2 = var_1 * 2;',
    '    type_1.method_2(var_2);',
    '    return var_2;',
    '}',
  ].join('\n'));
  const after = marks([
    'void method_1(int var_1)',
    '{',
    '    var var_2 = type_2.method_3();',
    '    var var_3 = var_1 * 2;',
    '    type_1.method_2(var_3);',
    '    return var_3;',
    '}',
  ].join('\n'));

  const diff = pairDiff(before, after);

  assert.equal(changedCount(diff.before, diff.after), 1,
    `the renumbering was read as real change: ${shape(diff.before, diff.after)}`);
  assert.equal(diff.after[2], 'added', 'and the line that IS new is the one marked');
});

test('an identical pair is marked nowhere', () => {
  const code = 'void method_1()\n{\n    return 1;\n}';

  const diff = pairDiff(code, code);

  assert.equal(changedCount(diff.before, diff.after), 0,
    `nothing differs, so nothing may be coloured: ${shape(diff.before, diff.after)}`);
});

test('a line only added is added on one side and nothing on the other', () => {
  const diff = pairDiff('a\nc', 'a\nb\nc');

  assert.deepEqual([...diff.before], ['same', 'same']);
  assert.deepEqual([...diff.after], ['same', 'added', 'same']);
});

test('a line only removed is removed on one side and nothing on the other', () => {
  const diff = pairDiff('a\nb\nc', 'a\nc');

  assert.deepEqual([...diff.before], ['same', 'removed', 'same']);
  assert.deepEqual([...diff.after], ['same', 'same']);
});

/**
 * A removal opposite an addition reads as one line REWRITTEN.
 *
 * <p>Git's own line diff has only `+` and `-`, but a person reading two panes side by side sees a
 * rewrite, and saying so is the whole reason for a third colour.</p>
 */
test('a line replaced is changed on both sides, not removed and added', () => {
  const diff = pairDiff('a\n    old();\nc', 'a\n    neu();\nc');

  assert.deepEqual([...diff.before], ['same', 'changed', 'same']);
  assert.deepEqual([...diff.after], ['same', 'changed', 'same']);
});

/**
 * Only the OVERLAP is paired — three gone and one arrived is one change and two removals.
 *
 * <p>Pairing the whole run would claim a rewrite that did not happen and would leave a person
 * looking for two lines of new code that are not there.</p>
 */
test('an uneven run pairs only as far as both sides go', () => {
  const diff = pairDiff('a\nx1\nx2\nx3\nz', 'a\ny1\nz');

  assert.deepEqual([...diff.before], ['same', 'changed', 'removed', 'removed', 'same']);
  assert.deepEqual([...diff.after], ['same', 'changed', 'same']);
});

/**
 * The mask is wider than the three kinds the normaliser emits today, deliberately.
 *
 * <p>A fourth kind would otherwise stop being masked the day it is added, and the wall of false
 * changes would come back with nothing pointing at why.</p>
 */
test('a placeholder kind nobody has invented yet is masked too', () => {
  const diff = pairDiff('use(field_1);', 'use(field_2);');

  assert.deepEqual([...diff.before], ['same'], 'an index shift is not a change');
});

test('a genuine change to a line survives the mask', () => {
  // The names are identical; what differs is the operator, which no mask touches.
  const diff = pairDiff('var_1 = var_2 * 2;', 'var_1 = var_2 + 2;');

  assert.deepEqual([...diff.before], ['changed']);
  assert.deepEqual([...diff.after], ['changed']);
});

test('an empty skeleton on one side marks every line of the other', () => {
  const diff = pairDiff('', 'a\nb');

  assert.equal(diff.before.length, 1, 'an empty string is still one (empty) line');
  assert.ok(diff.after.every((mark) => mark !== 'same'), shape(diff.before, diff.after));
});
