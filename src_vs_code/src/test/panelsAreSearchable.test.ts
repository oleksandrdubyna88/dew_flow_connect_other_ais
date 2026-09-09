import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';

/**
 * Every page this extension opens can be searched with the editor's own find bar.
 *
 * <p>2026-09-09: Ctrl+F did nothing in the chat tab and nothing in the rounds log. Both are pages of
 * long text — one of answers, one a table built to be searched — and neither had a find bar, because
 * `createWebviewPanel` was called without `enableFindWidget`. VS Code gives a webview panel its real
 * find bar for that one option and for nothing else.</p>
 *
 * <p>Structural, like the disposal scan in `theLogRefusesToOpen.test.ts`, and for the same reason:
 * every panel owner imports `vscode`, which does not exist outside the extension host, so no unit
 * test in this suite can call one. It follows that file's two rules as well — the owners are
 * DISCOVERED rather than listed, so a fifth panel added next year is covered the day it is written;
 * and the discovery is paired with known instances, so a scan that has stopped matching anything
 * cannot pass by matching nothing.</p>
 *
 * <p>The plan round asked for one thing the disposal scan does not do: the option must be inside the
 * call, not merely somewhere in the file. A file with two panels, or a comment naming the option,
 * would otherwise satisfy a whole-file match while the call under test stayed unsearchable. So each
 * call's own argument text is cut out by balanced parentheses and read on its own.</p>
 */

const SOURCE_ROOT = path.join(__dirname, '..', '..', 'src');

/** Every `.ts` file under `src/`, tests excluded — the same walk the disposal scan uses. */
function walk(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return entry.name === 'test' ? [] : walk(full);
    }

    return entry.name.endsWith('.ts') ? [path.relative(SOURCE_ROOT, full)] : [];
  });
}

const read = (file: string): string => fs.readFileSync(path.join(SOURCE_ROOT, file), 'utf8');

/**
 * Comments removed, so a sentence ABOUT the option cannot stand in for the option.
 *
 * <p>Crude on purpose: this runs over one call's arguments, where a `//` inside a string literal
 * would be a URL nobody passes to `createWebviewPanel`. The alternative was a TypeScript AST in a
 * suite that has never needed one.</p>
 */
const withoutComments = (text: string): string =>
  text.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\/\/[^\n]*/g, ' ');

/**
 * The argument text of every `createWebviewPanel(` call in one file, by balanced parentheses.
 *
 * <p>Returns one entry per call: a file that grows a second panel is checked twice rather than
 * passing on the strength of its first.</p>
 */
function panelCalls(source: string): string[] {
  const calls: string[] = [];
  const marker = 'createWebviewPanel(';
  for (let at = source.indexOf(marker); at >= 0; at = source.indexOf(marker, at + 1)) {
    let depth = 0;
    for (let i = at + marker.length - 1; i < source.length; i += 1) {
      if (source[i] === '(') {
        depth += 1;
      } else if (source[i] === ')') {
        depth -= 1;
        if (depth === 0) {
          calls.push(source.slice(at, i + 1));
          break;
        }
      }
    }
  }

  return calls;
}

test('every webview panel this extension opens gives it a find bar', () => {
  const owners = walk(SOURCE_ROOT).filter((file) => /createWebviewPanel\(/.test(read(file)));

  // The known instances, so a scan that stopped matching anything cannot pass by matching nothing.
  assert.ok(owners.includes('chatPanel.ts'), `the chat tab is one of the owners this finds; found ${owners.join(', ')}`);
  assert.ok(owners.includes('roundsLogPanel.ts'), `the rounds log is one of the owners this finds; found ${owners.join(', ')}`);
  assert.ok(owners.includes('helpPanel.ts'), `the help page is one of the owners this finds; found ${owners.join(', ')}`);

  for (const owner of owners) {
    const calls = panelCalls(read(owner));
    assert.ok(calls.length > 0, `${owner} names createWebviewPanel but no call could be read out of it`);

    for (const call of calls) {
      const args = withoutComments(call);
      const hits = args.match(/enableFindWidget/g) ?? [];

      assert.equal(
        hits.length, 1,
        `${owner}: exactly one enableFindWidget belongs in a panel's options — Ctrl+F does nothing without it`,
      );
      assert.match(
        args, /enableFindWidget:\s*true/,
        `${owner}: enableFindWidget must be true; present-but-false is the same dead Ctrl+F`,
      );
      // A spread could carry the option in — and could just as easily override it with false from a
      // constant this scan never reads. Nothing spreads into these options today; the day something
      // does, this test has to grow an AST rather than quietly stop guarding.
      assert.doesNotMatch(
        args, /\.\.\./,
        `${owner}: a spread in a panel's options can override enableFindWidget where this test cannot see it`,
      );
    }
  }
});
