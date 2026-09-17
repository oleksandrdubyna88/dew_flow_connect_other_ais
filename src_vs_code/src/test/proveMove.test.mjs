import assert from 'node:assert/strict';
import { test } from 'node:test';
import { bodyOf, movedVerbatim } from '../../scripts/prove-move.mjs';

/**
 * That the move proof can actually tell a move from a rewrite.
 *
 * <p><b>Why this test exists, precisely.</b> `prove-move.mjs` is the guard against a refactor
 * silently reverting somebody else's work: an extraction deletes a region and re-adds it in a new
 * file, and a rebase resolves delete-versus-modify in favour of the delete, so main's changes to
 * those lines disappear with no conflict. That happened here — a first attempt at splitting
 * `chatCommand.ts` would have re-inlined seven `vscode.window.show*` calls main had already moved to
 * the notifications ledger, with a green suite and a passing review.</p>
 *
 * <p>A guard that cannot fail is worse than none, so every case below is the guard being WRONG in
 * one specific way: forgiving a changed line, forgiving a dropped comment, or crying wolf over the
 * `export` keyword and the module header that a move legitimately adds.</p>
 *
 * <p>It runs BEFORE the compile, beside `prepareGate`, `claudeAdapter` and `notificationSites`,
 * because it needs no build: the script and this test are both ESM.</p>
 */

const ORIGINAL = [
  "import * as vscode from 'vscode';",
  '',
  '/** What it does, and the defect that made it do it. (codex, the code round.) */',
  'function alpha(one: string): string {',
  '  return one.trim();',
  '}',
  '',
  'function beta(): void {',
  '  void notify({ as: 1 });',
  '}',
].join('\n');

/** A module as an extraction really produces one: its own imports, its own header, then the code. */
const moved = (...body) => [
  "import * as vscode from 'vscode';",
  '',
  '/**',
  ' * What this module is, said in words that were never in the original.',
  ' */',
  '',
  ...body,
].join('\n');

test('a faithful move has no residue, and the export keyword is not a difference', () => {
  const { checked, residue } = movedVerbatim(ORIGINAL, [{
    name: 'alpha.ts',
    text: moved(
      '/** What it does, and the defect that made it do it. (codex, the code round.) */',
      'export function alpha(one: string): string {',
      '  return one.trim();',
      '}',
    ),
  }]);

  assert.equal(residue.length, 0, 'a pure move was reported as a rewrite');
  assert.equal(checked, 4, 'the import block or the module header was counted as moved code');
});

test('a changed line is residue, however small the change', () => {
  const { residue } = movedVerbatim(ORIGINAL, [{
    name: 'alpha.ts',
    text: moved(
      'export function alpha(one: string): string {',
      '  return one.trimEnd();',
      '}',
    ),
  }]);

  assert.deepEqual(residue.map((one) => one.line), ['return one.trimEnd();'],
    'a line that changed while moving was accepted as a move');
});

test('a call site reverted to what it replaced is residue — the defect this exists for', () => {
  // The real shape: main converted `vscode.window.show*` to the notifications ledger, and a branch
  // built before that carried the old line into a new file. Nothing else can see it.
  const { residue } = movedVerbatim(ORIGINAL, [{
    name: 'beta.ts',
    text: moved(
      'export function beta(): void {',
      "  void vscode.window.showWarningMessage('gone');",
      '}',
    ),
  }]);

  assert.deepEqual(residue.map((one) => one.line), ["void vscode.window.showWarningMessage('gone');"],
    'a reverted call site passed as a move, which is the whole failure this guard is for');
});

test('a dropped comment is NOT residue, so the count is reported beside it', () => {
  // Honest about its own blind spot: this compares the lines that ARE there, so a comment deleted on
  // the way out leaves nothing to find. `checked` falling is the only signal, which is why the script
  // prints it — the comments in this codebase each record a defect somebody found.
  const { checked, residue } = movedVerbatim(ORIGINAL, [{
    name: 'alpha.ts',
    text: moved(
      'export function alpha(one: string): string {',
      '  return one.trim();',
      '}',
    ),
  }]);

  assert.equal(residue.length, 0);
  assert.equal(checked, 3, 'dropping a comment changed something other than the count');
});

test('moving whole functions is a move, and the run count says how many regions were cut', () => {
  // Taking two functions out in the other order is still a move: each keeps its own lines in their
  // own order. What the caller reads is the RUN count — two regions cut, two runs.
  const { residue, runs } = movedVerbatim(ORIGINAL, [{
    name: 'both.ts',
    text: moved(
      'export function beta(): void {',
      '  void notify({ as: 1 });',
      '}',
      '',
      'export function alpha(one: string): string {',
      '  return one.trim();',
      '}',
    ),
  }]);

  assert.equal(residue.length, 0, 'moving two whole functions was reported as a rewrite');
  assert.equal(runs, 2, 'the run count does not say how many regions were cut');
});

test('reordering two statements INSIDE a function shatters the run count', () => {
  // The case three reviewers named at the plan round. Every line is still present, so residue alone
  // cannot see it — what gives it away is FRAGMENTATION: a four-line function that moved whole is
  // one run, and the same four lines reordered cannot be walked without restarting at each of them.
  //
  // This is why the caller declares how many regions it cut and the run count is checked against
  // that budget, rather than the tool guessing what "too many" means.
  const ordered = ['function gamma(): void {', '  first();', '  second();', '}'].join('\n');
  const faithful = movedVerbatim(ordered, [{
    name: 'gamma.ts',
    text: moved('export function gamma(): void {', '  first();', '  second();', '}'),
  }]);
  const swapped = movedVerbatim(ordered, [{
    name: 'gamma.ts',
    text: moved('export function gamma(): void {', '  second();', '  first();', '}'),
  }]);

  assert.equal(faithful.runs, 1, 'one function moved whole was not one contiguous run');
  assert.ok(swapped.runs > faithful.runs,
    'two statements were swapped inside a function and the walk did not fragment, so nothing can see it');
});

test('a line borrowed from another function shows as an extra run, not as a clean move', () => {
  // A four-thousand-line file repeats idioms, so "this line exists somewhere" is a weak question.
  // In order, a statement lifted out of a different function cannot follow the one before it.
  const twice = [
    'function gamma(): void {', '  first();', '}',
    'function delta(): void {', '  second();', '}',
  ].join('\n');
  const borrowed = movedVerbatim(twice, [{
    name: 'gamma.ts',
    text: moved('export function gamma(): void {', '  second();', '}'),
  }]);
  const honest = movedVerbatim(twice, [{
    name: 'gamma.ts',
    text: moved('export function gamma(): void {', '  first();', '}'),
  }]);

  assert.equal(honest.runs, 1, 'one function moved whole was not one contiguous run');
  assert.ok(borrowed.runs > honest.runs,
    'a statement borrowed from another function walked as though it belonged where it was put');
});

test('the residue names the module it is in, so a fifteen-module series can be read', () => {
  const { residue } = movedVerbatim(ORIGINAL, [
    { name: 'alpha.ts', text: moved('export function alpha(one: string): string {', '  return one.trim();', '}') },
    { name: 'beta.ts', text: moved('export function beta(): void {', '  void somethingElse();', '}') },
  ]);

  assert.deepEqual(residue, [{ name: 'beta.ts', line: 'void somethingElse();' }]);
});

test('a module with no imports is compared whole, rather than silently skipped', () => {
  const { checked } = movedVerbatim(ORIGINAL, [{ name: 'bare.ts', text: 'function alpha(one: string): string {\n}' }]);

  assert.equal(checked, 2, 'a module without an import block was read as having no body');
});

test('the header is found after the LAST import, not the first', () => {
  const text = ["import a from 'a';", "import b from 'b';", '', '/** header */', '', 'const kept = 1;'].join('\n');

  assert.equal(bodyOf(text).trim(), 'const kept = 1;',
    'an import after the first one was read as part of the body, or the header was kept');
});
