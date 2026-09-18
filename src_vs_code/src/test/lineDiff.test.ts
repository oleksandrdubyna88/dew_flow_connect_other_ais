import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

import { type LineMark, PLACEHOLDER_KINDS, pairDiff } from '../lineDiff';

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
 * A REAL identifier that merely looks like a placeholder is not masked.
 *
 * <p>The first version matched any `([A-Za-z]+)_\d+`, so that a fourth placeholder kind would keep
 * working. Two reviewers on two providers refused it, and the cost they named is this one: the
 * corpus keeps string literals and comments verbatim, so `utf8_2`, `base64_128` and `token_1` are
 * things a skeleton really contains — and masking them would report a line where one of them
 * genuinely changed as UNCHANGED. Silently, on the page where somebody decides what leaves their
 * machine.</p>
 */
test('an identifier that only looks like a placeholder still counts as a change', () => {
  for (const [was, now] of [['utf8_2', 'utf8_4'], ['base64_128', 'base64_256'], ['token_1', 'token_9']]) {
    const diff = pairDiff(`use(${was});`, `use(${now});`);

    assert.deepEqual([...diff.before], ['changed'], `${was} -> ${now} was masked away`);
  }
});

/**
 * Every kind the normaliser can emit is a kind this module masks — checked against the PRODUCER.
 *
 * <p>A narrow list is only safe while it is complete, and a list that agrees with its own unit tests
 * drifts the day `Placeholders.cs` returns a fourth kind. So this reads that file and extracts the
 * words it returns as a kind: the ones in a `return ("x", …)` and the ones on either arm of a
 * ternary. `member_access_expression` and friends are node types, not kinds, and they carry
 * underscores, which is what tells them apart.</p>
 */
test('every placeholder kind the normaliser emits is one this module masks', () => {
  const file = join(__dirname, '..', '..', '..', 'src_mcp', 'normalizer', 'Placeholders.cs');
  const text = readFileSync(file, 'utf8');

  assert.match(text, /\$"\{kind\}_\{next\}"/u,
    'the placeholder FORMAT changed — masking by "kind_number" may no longer be right at all');

  const kinds = new Set<string>();
  for (const found of text.matchAll(/[?:]\s*"([a-z]+)"/gu)) {
    kinds.add(found[1] ?? '');
  }
  for (const found of text.matchAll(/\(\s*"([a-z]+)",\s*(?:true|false)\s*\)/gu)) {
    kinds.add(found[1] ?? '');
  }

  // The companion a structural scan needs: a KNOWN instance it must still find. Without it, a
  // refactor that moved every kind out of a literal would leave the extraction matching nothing and
  // this test passing over an empty set for ever. (Two reviewers, and `testing.md` names the shape.)
  assert.ok(kinds.has('var'),
    'the extraction no longer finds even the kind this file certainly returns — it reads the wrong shape');
  assert.ok(kinds.size >= 2, 'the extraction found almost nothing — it is reading the wrong shape');
  assert.deepEqual([...kinds].sort(), [...PLACEHOLDER_KINDS].sort(),
    'the normaliser emits a placeholder kind this module does not mask, or masks one it never emits');
});

/**
 * A pair too large to diff properly says so by marking everything, not by marking nothing.
 *
 * <p>The table is quadratic and `row()` builds one per pair including collapsed ones, so four
 * reviewers asked for a ceiling. Past it the two sides are compared as wholes — which is visibly
 * different from a confident diff, and from silence.</p>
 */
test('a pair beyond the ceiling is compared as a whole rather than line by line', () => {
  const huge = Array.from({ length: 600 }, (_, at) => `line ${at};`).join('\n');
  const differs = `${huge}\nand one more;`;

  const same = pairDiff(huge, huge);
  assert.ok(same.before.every((mark) => mark === 'same'),
    'two identical skeletons must stay unmarked however large they are');

  const apart = pairDiff(huge, differs);
  assert.ok(apart.before.every((mark) => mark === 'removed'));
  assert.ok(apart.after.every((mark) => mark === 'added'));
});

/**
 * The wall of false differences must not reappear ABOVE the ceiling.
 *
 * <p>Three reviewers found this and it was real: `tooBig` compared RAW lines, so two large skeletons
 * differing only by placeholder renumbering came back with every line marked — the exact defect this
 * module exists to prevent, hiding in the path nobody looks at. Measured before the fix at 600 of
 * 600 lines.</p>
 */
test('a pair beyond the ceiling is not repainted by renumbering either', () => {
  const before = Array.from({ length: 600 }, (_, at) => `  var_${at} = ${at};`).join('\n');
  const after = Array.from({ length: 600 }, (_, at) => `  var_${at + 1} = ${at};`).join('\n');

  const diff = pairDiff(before, after);

  assert.equal(diff.before.filter((mark) => mark !== 'same').length, 0,
    'above the ceiling, renumbering was read as every line changing');
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
