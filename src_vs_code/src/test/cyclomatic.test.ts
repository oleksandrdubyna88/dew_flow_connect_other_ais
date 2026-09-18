import assert from 'node:assert/strict';
import { test } from 'node:test';

import { cyclomatic, withoutLiterals } from '../cyclomatic';

/**
 * The complexity count, over SKELETONS — and, first, over the things in a skeleton that only look
 * like decisions.
 *
 * <p>The corpus keeps comments and string literals verbatim (`Normalise` renames identifiers and
 * nothing else), so an `if` inside either is something a skeleton really contains. That is the trap
 * this module exists to step around, so it is the first thing asserted; the keyword arithmetic comes
 * after.</p>
 */

/** The number, or a failure naming why there is none. */
function count(code: string, language = 'CSharp'): number {
  const found = cyclomatic(code, language);
  assert.ok(found.known, `no count for ${language}: ${found.known ? '' : found.why}`);

  return found.value;
}

// --------------------------------------------------------------------------------------------
// Words that are not decisions.
// --------------------------------------------------------------------------------------------

test('a keyword inside a string literal or a comment is not a decision', () => {
  assert.equal(count('void method_1() { var var_1 = "if while for case"; }'), 1);
  assert.equal(count('void method_1() { // if this races, catch it\n }'), 1);
  assert.equal(count('void method_1() { /* for (;;) while (true) */ }'), 1);
  assert.equal(count("void method_1() { var var_1 = 'if'; }"), 1);
});

test('C# verbatim and raw strings are literals too, doubled quote and all', () => {
  assert.equal(count('void method_1() { var var_1 = @"if ""for"" while"; }'), 1);
  assert.equal(count('void method_1() { var var_1 = $@"if {var_2} while"; }'), 1);
  assert.equal(count('void method_1() { var var_1 = @$"if while"; }'), 1);
  assert.equal(count('void method_1() { var var_1 = """\n  if (a) while (b)\n  """; }'), 1);
});

test('a template literal is a literal, across lines, and an escaped quote does not end a string', () => {
  assert.equal(count('function method_1() { const var_1 = `if\n while ${var_2} for`; }', 'TypeScript'), 1);
  assert.equal(count('function method_1() { const var_1 = "say \\"if\\" while"; }', 'JavaScript'), 1);
});

/**
 * A `//` INSIDE a string must not start a comment that swallows the rest of the line.
 *
 * <p>`"http://x"` is the shape, and it is common. The scanner walks left to right and meets the
 * quote before the marker, so the string is read to its end and the `if` after it is still
 * counted. The order of the checks at one position cannot break this — only a scanner that finds
 * the NEXT marker globally can, which is the obvious first draft: verified by mutation, blanking
 * `//` comments with a whole-text regex before the walk makes this report 1.</p>
 */
test('a comment marker inside a string does not hide the code after it', () => {
  assert.equal(count('void method_1() { var var_1 = "http://x"; if (var_2) { } }'), 2);
  assert.equal(count("void method_1() { var var_1 = 'a//b'; if (var_2) { } }", 'TypeScript'), 2);
});

test('an apostrophe inside a comment does not open a literal that swallows the code after it', () => {
  assert.equal(count("void method_1() {\n  // don't race\n  if (var_1) { }\n}"), 2);
});

/**
 * The scanner blanks exactly the literals and keeps the code around them — LENGTH FOR LENGTH.
 *
 * <p>It used to collapse each literal to one space. The interpolation fix changed that: a literal
 * is now walked character by character so the expressions inside `${…}` survive, and what is blanked
 * is blanked in place. Keeping the length is the better property and it came for free — every offset
 * in the blanked text still matches the original, so anything later built on this can point at a
 * position rather than guess one.</p>
 */
test('the scanner blanks exactly the literals and keeps the code around them', () => {
  assert.equal(withoutLiterals('a("if"); // b\nc'), 'a(    );     \nc');
  assert.equal(withoutLiterals('x = @"a""b"; y'), 'x =        ; y');
  assert.equal(withoutLiterals('/* a */ b /* unterminated'), '        b                ');

  // The property behind those three, asserted as itself rather than left implicit in the spacing.
  for (const code of ['a("if"); // b\nc', 'x = @"a""b"; y', 'f(`${x ? 1 : 0}`)', '/* a */ b']) {
    assert.equal(withoutLiterals(code).length, code.length, code);
  }
  // And the newline is not blanked, or a `//` comment would swallow the line after it.
  assert.ok(withoutLiterals('// a\nif (x) {}').includes('\n'));
});

// --------------------------------------------------------------------------------------------
// The arithmetic: one, plus one per decision.
// --------------------------------------------------------------------------------------------

test('a method with no branches is 1', () => {
  assert.equal(count('void method_1() { var_1 = var_2; return var_1; }'), 1);
  assert.equal(count('function method_1() { return var_1; }', 'JavaScript'), 1);
});

test('each if is a decision, else is not, and else-if is another if', () => {
  assert.equal(count('void method_1() { if (var_1) { } }'), 2);
  assert.equal(count('void method_1() { if (var_1) { } else { } }'), 2);
  assert.equal(count('void method_1() { if (var_1) { } else if (var_2) { } else { } }'), 3);
});

test('each loop is a decision, and a do-while is counted once at its while', () => {
  assert.equal(count('void method_1() { for (;;) { } }'), 2);
  assert.equal(count('void method_1() { foreach (var var_1 in var_2) { } }'), 2);
  assert.equal(count('void method_1() { while (var_1) { } }'), 2);
  assert.equal(count('void method_1() { do { } while (var_1); }'), 2);
  assert.equal(count('function method_1() { for (const var_1 of var_2) { } }', 'TypeScript'), 2);
});

test('each case label is a decision, and default is not', () => {
  const code = 'void method_1() { switch (var_1) { case 1: break; case 2: break; case 3: break; default: break; } }';

  assert.equal(count(code), 4);
});

test('each catch is a decision, a when guard is another, and finally is not', () => {
  assert.equal(count('void method_1() { try { } catch { } finally { } }'), 2);
  assert.equal(count('void method_1() { try { } catch (type_1) { } catch (type_2) { } }'), 3);
  assert.equal(count('void method_1() { try { } catch (type_1 var_1) when (var_1.Ok) { } }'), 3);
  assert.equal(count('function method_1() { try { } catch (var_1) { } }', 'JavaScript'), 2);
});

test('the boolean operators and the null-coalescers are decisions', () => {
  assert.equal(count('void method_1() { if (var_1 && var_2 || var_3) { } }'), 4);
  assert.equal(count('void method_1() { var var_1 = var_2 ?? var_3; }'), 2);
  assert.equal(count('void method_1() { var_1 ??= var_2; }'), 2);
  assert.equal(count('function method_1() { return var_1 ?? var_2; }', 'TypeScript'), 2);
});

/**
 * The conditional is the `?` with whitespace on both sides — and NOTHING ELSE that is spelled
 * with a question mark is a decision.
 *
 * <p>A skeleton of typed code is full of the others: nullable types, null-conditional access, an
 * optional parameter. Counting them would make every C# method with a `string?` parameter read as
 * branching, which is the confident-and-wrong number this page is not allowed to show.</p>
 */
test('a ternary is a decision, and every other question mark is not', () => {
  assert.equal(count('void method_1() { var var_1 = var_2 ? 1 : 2; }'), 2);
  assert.equal(count('void method_1() {\n  var var_1 = var_2\n    ? 1\n    : 2;\n}'), 2);
  assert.equal(count('void method_1(string? var_1, int? var_2) { }'), 1);
  assert.equal(count('void method_1() { var var_1 = var_2?.Length; var var_3 = var_4?[0]; }'), 1);
  assert.equal(count('function method_1(var_1?: number): void { }', 'TypeScript'), 1);
  assert.equal(count('function method_1() { return var_1?.var_2; }', 'JavaScript'), 1);
});

/**
 * Over a skeleton the normaliser actually produces — the C# fixture from `AstNormalizerTests`,
 * as `Normalise` renders it — so the keyword arithmetic is checked on the real shape and not only
 * on one-liners built to exercise it.
 */
test('a normalised method counts the same as the method it came from', () => {
  const skeleton = [
    'public int method_1(string var_1, int var_2)',
    '{',
    '    if (!var_3.ContainsKey(var_1))',
    '    {',
    '        var_3.Add(var_1, var_2);',
    '    }',
    '',
    '    return var_3[var_1];',
    '}',
  ].join('\n');
  // The method that skeleton came from — `AstNormalizerTests`' fixture, un-anonymised. The
  // un-anonymised view (story 2.3) shows this text beside a complexity computed from the skeleton,
  // and the claim that the two agree is worth one line rather than an assumption: the anonymiser
  // renames identifiers and leaves control flow alone, so the count must not move.
  const original = [
    'public int GetOrAdd(string key, int value)',
    '{',
    '    if (!_items.ContainsKey(key))',
    '    {',
    '        _items.Add(key, value);',
    '    }',
    '',
    '    return _items[key];',
    '}',
  ].join('\n');

  assert.equal(count(skeleton), 2);
  assert.equal(count(original), count(skeleton), 'anonymisation must not change the number a person reads');
});

// --------------------------------------------------------------------------------------------
// A count that could not be taken is not zero.
// --------------------------------------------------------------------------------------------

test('a language this page does not read is not counted, and the refusal names it', () => {
  const found = cyclomatic('if (a) { }', 'Fortran');

  assert.equal(found.known, false);
  assert.match(found.known ? '' : found.why, /Fortran/u, 'the refusal must say which language');
  assert.match(found.known ? '' : found.why, /not computed/u);
});

test('a language that is not a string is refused rather than thrown on', () => {
  for (const bad of [null, undefined, 42, {}, []]) {
    const found = cyclomatic('if (a) { }', bad as unknown as string);

    assert.equal(found.known, false, String(bad));
  }
  assert.match((cyclomatic('x', '') as { why: string }).why, /no language/u);
});

/**
 * A decision hidden inside an interpolation is still a decision.
 *
 * <p>Found by the plan round (codex) on the template-literal case, and it turned out to be wider
 * than reported: the C# interpolated form loses it too. Blanking a literal WHOLE removes the
 * expression inside `${…}` or `{…}` along with the text around it, so
 * `` `${x ? 1 : 0}` `` counted 1 where it should count 2. Measured before the fix at 1 for both
 * languages and 2 for the same ternary written outside a string.</p>
 *
 * <p>It understates rather than overstates, which is the worse direction here: a method the page
 * calls simple is one a person does not look twice at.</p>
 */
test('a ternary inside an interpolation counts, in both languages that have one', () => {
  const inside = cyclomatic('function f(x) { return `${x ? 1 : 0}`; }', 'TypeScript');
  const outside = cyclomatic('function f(x) { return x ? 1 : 0; }', 'TypeScript');

  assert.deepEqual(inside, outside, 'a template literal hid the decision inside it');

  const csInside = cyclomatic('void m(int x) { var s = $"{(x > 1 ? "a" : "b")}"; }', 'CSharp');
  assert.equal(csInside.known && csInside.value, 2, 'a C# interpolation hid the decision inside it');
});

test('a question mark in the literal TEXT of an interpolated string still does not count', () => {
  // The other half of the same fix: scanning the expressions must not start counting the prose.
  const asked = cyclomatic('function f(x) { return `are you sure? ${x}`; }', 'TypeScript');

  assert.equal(asked.known && asked.value, 1, 'punctuation in a string was read as a branch');
});

/**
 * A comment is never code, however many braces it contains.
 *
 * <p><b>This is a regress I introduced and the code round caught, in four findings across two
 * providers.</b> Fixing the interpolation case by keeping whatever sat inside braces applied that
 * rule to EVERYTHING — comments and ordinary strings included — so `// { if }` counted a branch and
 * `"{ if for while }"` counted three. Measured before this fix: 2, 4 and 3 where 1 belonged.</p>
 *
 * <p>It was the worse trade, too. I had swapped an understatement in a rare case (a ternary inside
 * an interpolation) for an OVERSTATEMENT in common ones — braces in a comment or a string literal,
 * both of which survive `Normalise` verbatim and are therefore in the corpus. And a complexity that
 * moves when only a comment changes is worse than one that is slightly low: it invents a change
 * where the control flow has none.</p>
 */
test('braces in a comment or an ordinary string are not control flow', () => {
  assert.equal(count('void method_1() { // { if }\n }'), 1, 'a line comment');
  assert.equal(count('void m() { /* { if while } */ }'), 1, 'a block comment');
  assert.equal(count('string method_1() { return "{ if for while }"; }'), 1, 'a plain string');
  assert.equal(count('function f() { return \'{ if }\'; }', 'JavaScript'), 1, 'a single-quoted string');

  // And the one that motivated the whole thing still works, on both languages that have it.
  assert.equal(count('function f(x) { return `${x ? 1 : 0}`; }', 'TypeScript'), 2, 'a template hole');
  assert.equal(count('void m(int x) { var s = $"{(x > 1 ? "a" : "b")}"; }'), 2, 'a C# hole');
});

/**
 * A complexity that moves when only a comment moves is the defect, stated as a property.
 *
 * <p>Four of the code round's findings were really this one sentence: the number must describe the
 * control flow and nothing else. Asserting it directly is stronger than asserting any particular
 * spelling, because it fails for a comment form nobody thought to enumerate.</p>
 */
test('changing only the prose does not change the number', () => {
  const bare = 'void method_1(int var_1) { if (var_1 > 0) { return; } }';
  const commented = 'void method_1(int var_1) { // { while for } and a note\n  if (var_1 > 0) { return; } }';
  const stringy = 'void method_1(int var_1) { var s = "{ while for }"; if (var_1 > 0) { return; } }';

  assert.equal(count(commented), count(bare), 'a comment changed the complexity');
  assert.equal(count(stringy), count(bare), 'a string literal changed the complexity');
});

test('a doubled brace in a template literal does not swallow the rest of it', () => {
  // `${{ … }}` is an object literal in a hole; the C# escape rule must not apply to it. Before the
  // fix this counted 1 — the doubled brace was read as an escape and the hole never opened.
  const object = cyclomatic('function f(x) { return `${{ a: x ? 1 : 0 }}`; }', 'TypeScript');
  assert.equal(object.known && object.value, 2, 'an object literal in a hole hid its decision');

  // And a hole that ENDS with two braces must still close, or every word after it counts.
  const arrow = cyclomatic('function f() { return `${(() => { return 1; })()} if for while`; }', 'TypeScript');
  assert.equal(arrow.known && arrow.value, 1, 'the hole never closed, so the prose after it counted');
});
