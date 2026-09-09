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
 * <p>The help page is in although it has a search box of its own: the find bar is chrome ABOVE the
 * webview and displaces nothing, and a discovery that carried an exception for one file would be the
 * hand-written list it exists to replace. `src/test` is skipped, as the disposal scan skips it — a
 * panel in a test file is not a page anybody opens, and a scan that read this file would find the
 * hostile fixtures below and call them owners.</p>
 *
 * <p><b>What the code round demanded, and why the checking is this careful.</b> Three reviewers
 * refused a whole-file text match independently: a string literal, a comment, or a nested object can
 * all carry the words `enableFindWidget: true` while the call VS Code actually receives has no such
 * option — the guard would stay green over a dead Ctrl+F. So comments, string literals and regular
 * expressions are blanked before anything is matched, the call's arguments are cut out by balanced
 * parentheses (the blanking is what makes that safe: a `)` inside a title can no longer close the
 * count), and the option is required as a TOP-LEVEL property of the options object. The second test
 * in this file drives every one of those evasions through the same code and watches it complain.</p>
 */

const SOURCE_ROOT = path.join(__dirname, '..', '..', 'src');

/** Every extension `tsc` compiles here. A panel arriving as `.tsx` must not be a panel nobody scans. */
const SOURCES = ['.ts', '.tsx', '.mts', '.cts'];

function walk(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      return entry.name === 'test' ? [] : walk(full);
    }

    return SOURCES.some((suffix) => entry.name.endsWith(suffix)) ? [path.relative(SOURCE_ROOT, full)] : [];
  });
}

const read = (file: string): string => fs.readFileSync(path.join(SOURCE_ROOT, file), 'utf8');

/** Where a `/` opens a regular expression rather than dividing. The standard previous-token rule. */
const BEFORE_REGEX = /[(,=:[!&|?{};+\-*%~^]/;

/**
 * The source with every comment, string and regular expression blanked to spaces, delimiters kept.
 *
 * <p>Same length as the input on purpose: offsets stay meaningful, and a bracket inside a string can
 * no longer be counted by anything downstream. Not a parser — a `/` after an identifier is read as
 * division, so `return /x'/` would be misread. That case fails loudly rather than passing quietly,
 * which is the direction a guard is allowed to be wrong in.</p>
 */
function blanked(source: string): string {
  const out = [...source];
  const blank = (from: number, to: number): void => {
    for (let k = from; k < to && k < out.length; k += 1) {
      if (out[k] !== '\n') {
        out[k] = ' ';
      }
    }
  };
  const closes = (at: number, quote: string): number => {
    for (let j = at + 1; j < source.length; j += 1) {
      if (source[j] === '\\') {
        j += 1;
      } else if (source[j] === quote) {
        return j;
      }
    }

    return source.length;
  };
  const endOfRegex = (at: number): number => {
    let inClass = false;
    for (let j = at + 1; j < source.length; j += 1) {
      const c = source[j];
      if (c === '\\') {
        j += 1;
      } else if (c === '[') {
        inClass = true;
      } else if (c === ']') {
        inClass = false;
      } else if (c === '\n' || (c === '/' && !inClass)) {
        return j;
      }
    }

    return source.length;
  };

  let previous = '';
  let i = 0;
  while (i < source.length) {
    const here = source[i];
    const after = source[i + 1] ?? '';
    if (here === '/' && after === '/') {
      const stop = source.indexOf('\n', i);
      const end = stop < 0 ? source.length : stop;
      blank(i, end);
      i = end;
    } else if (here === '/' && after === '*') {
      const stop = source.indexOf('*/', i + 2);
      const end = stop < 0 ? source.length : stop + 2;
      blank(i, end);
      i = end;
    } else if (here === '\'' || here === '"' || here === '`') {
      const end = closes(i, here);
      blank(i + 1, end);
      i = end + 1;
    } else if (here === '/' && (previous === '' || BEFORE_REGEX.test(previous))) {
      const end = endOfRegex(i);
      blank(i + 1, end);
      i = end + 1;
    } else {
      if (here.trim().length > 0) {
        previous = here;
      }
      i += 1;
    }
  }

  return out.join('');
}

/** The text between the outermost brackets of every `createWebviewPanel(` call, blanked source in. */
function callArguments(source: string): string[] {
  const calls: string[] = [];
  // Whitespace and a comment may both sit between the name and its bracket; the comment is already
  // spaces by the time this runs, so one whitespace class covers both.
  const opening = /createWebviewPanel\s*\(/g;
  for (let hit = opening.exec(source); hit !== null; hit = opening.exec(source)) {
    const from = hit.index + hit[0].length - 1;
    let depth = 0;
    for (let i = from; i < source.length; i += 1) {
      if (source[i] === '(') {
        depth += 1;
      } else if (source[i] === ')') {
        depth -= 1;
        if (depth === 0) {
          calls.push(source.slice(from + 1, i));
          break;
        }
      }
    }
  }

  return calls;
}

/** Split at commas that are not inside a bracket of any kind. Arguments, or an object's properties. */
function parts(text: string): string[] {
  const found: string[] = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < text.length; i += 1) {
    const c = text[i];
    if (c === '(' || c === '[' || c === '{') {
      depth += 1;
    } else if (c === ')' || c === ']' || c === '}') {
      depth -= 1;
    } else if (c === ',' && depth === 0) {
      found.push(text.slice(start, i));
      start = i + 1;
    }
  }
  found.push(text.slice(start));

  return found.map((part) => part.trim()).filter((part) => part.length > 0);
}

/**
 * Everything wrong with the find bar in one file's panel calls. Empty means every page is searchable.
 */
export function findBarComplaints(source: string): string[] {
  const clean = blanked(source);
  const complaints: string[] = [];

  for (const call of callArguments(clean)) {
    const args = parts(call);
    const options = args[args.length - 1] ?? '';
    if (!options.startsWith('{') || !options.endsWith('}')) {
      complaints.push('the options are not an object literal this scan can read');
      continue;
    }

    const properties = parts(options.slice(1, -1));
    const spread = properties.some((property) => property.startsWith('...'));
    if (spread) {
      complaints.push('a spread in a panel\'s options can override enableFindWidget out of sight');
    }

    const enabling = properties.filter((property) => /^enableFindWidget\s*:\s*true$/.test(property));
    const mentions = (call.match(/enableFindWidget/g) ?? []).length;
    if (enabling.length !== 1) {
      complaints.push('enableFindWidget: true is not a top-level option of this call — Ctrl+F does nothing without it');
    } else if (mentions !== 1) {
      complaints.push('enableFindWidget is named more than once in this call; only the top-level option counts');
    }
  }

  return complaints;
}

test('every webview panel this extension opens gives it a find bar', () => {
  const owners = walk(SOURCE_ROOT).filter((file) => /createWebviewPanel\s*\(/.test(blanked(read(file))));

  // The known instances, so a scan that stopped matching anything cannot pass by matching nothing.
  const listed = owners.join(', ');
  assert.ok(owners.includes('chatPanel.ts'), `the chat tab is one of the owners this finds; found ${listed}`);
  assert.ok(owners.includes('roundsLogPanel.ts'), `the rounds log is one of the owners this finds; found ${listed}`);
  assert.ok(owners.includes('helpPanel.ts'), `the help page is one of the owners this finds; found ${listed}`);

  for (const owner of owners) {
    const source = read(owner);
    assert.ok(callArguments(blanked(source)).length > 0, `${owner} names createWebviewPanel but no call could be read out of it`);
    assert.deepEqual(findBarComplaints(source), [], `${owner}: this page cannot be searched`);
  }
});

test('the find-bar scan cannot be satisfied by a comment, a string or a nested object', () => {
  // Every evasion the code round named, driven through the same code the scan above runs. A guard
  // this file cannot describe in a fixture is a guard nobody can trust.
  const call = (args: string): string => `vscode.window.createWebviewPanel(${args});`;
  const good = '\'coaiChat\', title, vscode.ViewColumn.Active, { enableScripts: true, enableFindWidget: true, localResourceRoots: [] }';

  assert.deepEqual(findBarComplaints(call(good)), [], 'the honest call is refused');
  assert.deepEqual(
    findBarComplaints(call('\'a)b\', title, column, { enableScripts: true, enableFindWidget: true }')), [],
    'a bracket inside a string closed the argument list',
  );
  assert.deepEqual(
    findBarComplaints('vscode.window.createWebviewPanel\n(\'a\', b, c, { enableFindWidget: true })'), [],
    'a call whose bracket is on the next line was never discovered',
  );

  for (const [why, args] of [
    ['the option is simply absent', '\'a\', title, column, { enableScripts: true }'],
    ['the option is present and false', '\'a\', title, column, { enableScripts: true, enableFindWidget: false }'],
    ['the words are in a string argument', '\'enableFindWidget: true\', title, column, { enableScripts: true }'],
    ['the words are in a comment', '\'a\', title, column, { /* enableFindWidget: true */ enableScripts: true }'],
    ['the option is nested one level down', '\'a\', title, column, { webviewOptions: { enableFindWidget: true } }'],
    ['a spread could override it', '\'a\', title, column, { ...base, enableFindWidget: true }'],
    ['the value is a variable, not true', '\'a\', title, column, { enableFindWidget: wanted }'],
  ] as const) {
    assert.notDeepEqual(findBarComplaints(call(args)), [], `${why}: the scan said nothing`);
  }

  // Two calls in one file: the second one's omission must not hide behind the first one's option.
  assert.notDeepEqual(
    findBarComplaints(`${call(good)}\n${call('\'b\', title, column, { enableScripts: true }')}`),
    [],
    'a file that grows a second panel is only checked once',
  );
});
