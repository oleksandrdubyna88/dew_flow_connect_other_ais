import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';

/**
 * Reading this extension's own source, for the wiring tests that have no other way in.
 *
 * <p>There is no extension-host harness here — `research/module_tests.md` records that as the
 * largest gap — so the half of this product that imports `vscode` is pinned by reading it. These are
 * the two things every such test needs, in one place.</p>
 *
 * <p><b>Why it exists as a module.</b> `bodyOf` had been copied into three test files by the time
 * the chat command split was done, and `reuse-first.md` is explicit: *"A second implementation of a
 * capability is a defect from the moment it compiles, because the two will drift and nothing will
 * notice."* A fourth copy was the point at which that stopped being theoretical. (codex, the code
 * round.)</p>
 */

/** One of the extension's source files, as text. */
export const sourceOf = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** Every `*.ts` under `src`, joined — for a count that must not be evadable by adding a module. */
export const everySource = (): string => fs.readdirSync(path.join(__dirname, '..', '..', 'src'))
  .filter((one) => one.endsWith('.ts'))
  .map((one) => sourceOf(one))
  .join('\n');

/**
 * One function's text, from its opening line to the next top-level declaration.
 *
 * <p>A fixed character width was what these assertions used, and a comment added inside a function
 * pushed the line being asserted past the end of the slice — a test that went red for the length of
 * a paragraph rather than for anything about the code. The end is found rather than guessed.</p>
 */
export const bodyOf = (text: string, opening: string): string => {
  const at = text.indexOf(opening);
  assert.ok(at >= 0, `there is no ${opening} to read`);
  const after = text.indexOf('\n}', at);

  return after < 0 ? text.slice(at) : text.slice(at, after + 2);
};
