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

test('reordering whole functions is a move, because a reader can see that in the diff', () => {
  const { residue } = movedVerbatim(ORIGINAL, [{
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

  assert.equal(residue.length, 0, 'a reordering was reported as a rewrite');
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
