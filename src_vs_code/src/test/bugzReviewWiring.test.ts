import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

/**
 * The seam the page tests cannot reach: does the PANEL carry what the page told it?
 *
 * <p>A code reviewer put it exactly right — the page tests inject `expanded` straight into
 * `reviewPageHtml`, so if `received()` stopped recording an `expand` message, or if `draw()` stopped
 * passing the panel's set, every one of them would stay green while the opened row collapsed after
 * the next decision. That is the whole feature, and nothing was watching the wire.</p>
 *
 * <p><b>Why this is read and not run.</b> `bugzReviewPanel.ts` imports `vscode`, so no test in this
 * repository can import it — the same constraint `noticesDoNotBlock.test.ts` documents for the same
 * file. What is left is reading the source, and `testing.md` is clear about the price: a structural
 * assertion must pin the WHOLE condition, because one matching a fragment survives its own break.
 * So each assertion below names a specific call with its specific argument, and the file is stripped
 * of comments first — this suite has been caught once by an assertion that matched the prose
 * EXPLAINING a fix rather than the fix.</p>
 *
 * <p><b>What it does not prove.</b> Not that a row reopens after a decision — only that the three
 * links in that chain are still written. The behaviour itself needs an extension host, which this
 * suite does not have, and `research/module_tests.md` records the gap rather than leaving a reader
 * to assume it is covered.</p>
 */

const SOURCE = join(__dirname, '..', '..', 'src');

/** The file with its comments removed, so an assertion cannot match the prose about the code. */
function code(name: string): string {
  return readFileSync(join(SOURCE, name), 'utf8')
    .replace(/\/\*[\s\S]*?\*\//gu, ' ')
    .replace(/(^|[^:])\/\/.*$/gmu, '$1');
}

test('the panel records what the page opened, and both kinds of press reach the same place', () => {
  const text = code('bugzReviewPanel.ts');

  // The union is what makes the page and the panel agree about the shape; a bag of optionals
  // compiles whatever is missing.
  assert.match(text, /case 'expand':\s*case 'expandAll':\s*this\.remember\(m\.ids, m\.open\);/u,
    'one press and every press must both end in remember(), or Expand all records nothing');
  assert.match(text, /case 'decide':\s*this\.queue\(m\.ids, m\.keep\);/u);
});

test('the panel hands its open rows to the page on every draw', () => {
  const text = code('bugzReviewPanel.ts');

  // The whole condition: the call, the argument, and that the argument is the PANEL's set rather
  // than something computed from one read alone.
  //
  // It used to read `keptOpen(answer.pairs)`, inside `draw`. Story 2.2 moved the painting out of
  // `draw` so that pressing a filter tab repaints without asking the server, and the argument
  // became the panel's own `held` — which is the same guarantee spelled more plainly, since `held`
  // IS this side's state. Pruning against everything read rather than against what is SHOWN is the
  // point: a row hidden by a filter has not been closed.
  assert.match(text, /expanded: this\.keptOpen\(this\.held\)/u,
    'the page must be handed the panel\'s open rows, or a decision collapses them all');
  assert.match(text, /private keptOpen\([^)]*\): ReadonlySet<number>/u);
  // And it must not throw the set away while doing it — the pruning that was there deleted ids
  // from the panel's own state, which cannot be undone when the corpus comes back.
  assert.doesNotMatch(text, /this\.expanded\.delete\(/u,
    'keptOpen intersects per render; deleting from the panel\'s set is irreversible');
});

/** One method's body, so an assertion about it cannot be satisfied by a different method. */
function bodyOf(text: string, signature: string): string {
  const at = text.indexOf(signature);
  assert.notEqual(at, -1, `${signature} is not in the file`);
  const after = text.slice(at + signature.length);
  const ends = after.search(/\n {2}(private|public|get|async) /u);

  return ends < 0 ? after : after.slice(0, ends);
}

test('pressing a filter tab does not send anybody to the server', () => {
  // `remember`'s reasoning, one step along: a round trip per click would make narrowing a list cost
  // a process, and a filter changes nothing the database knows about. Asserted on the METHOD's own
  // body rather than the file, because `draw` two methods below does read — a file-wide assertion
  // would be satisfied by the wrong one and would go red for the right code.
  const text = code('bugzReviewPanel.ts');
  const narrow = bodyOf(text, 'private narrow(strip: string, key: string): void {');
  const paint = bodyOf(text, 'private paint(): void {');

  assert.doesNotMatch(narrow, /hooks\.read|await/u, 'a filter press must repaint, not re-read');
  assert.match(narrow, /this\.paint\(\)/u);
  assert.doesNotMatch(paint, /hooks\.read|await/u, 'and the paint it calls must not either');
  assert.match(bodyOf(text, 'private async draw(): Promise<void> {'), /await this\.hooks\.read\(\)/u,
    'the read still happens — in draw, which is the method that is allowed to');
});

test('a strip name the page did not send is ignored rather than guessed at', () => {
  const narrow = bodyOf(code('bugzReviewPanel.ts'), 'private narrow(strip: string, key: string): void {');

  assert.match(narrow, /strip !== 'project' && strip !== 'language'/u,
    'narrowing by an axis nobody asked for is worse than doing nothing');
});

test('a closed window forgets which rows were open', () => {
  const text = code('bugzReviewPanel.ts');
  const disposed = /onDidDispose\(\(\) => \{([\s\S]*?)\}\);/u.exec(text);

  assert.ok(disposed !== null, 'the panel no longer registers a dispose handler at all');
  assert.match(disposed[1] ?? '', /this\.expanded = new Set<number>\(\);/u,
    'this object outlives the webview, so a page reopened later must start collapsed');
  // The companion: the two pushed-setting hooks are unhooked in the same place. A listener that
  // outlives its webview posts into a disposed one on the next press from any other page.
  assert.match(disposed[1] ?? '', /zoomHook\.dispose\(\);/u);
  assert.match(disposed[1] ?? '', /toneHook\.dispose\(\);/u);
});

test('the two view controls are wired to the host that clamps and writes', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /m\.type === 'zoom' \? applyZoomDelta\(m\.delta\) : applyToneDelta\(m\.delta\)/u,
    'the page reports the direction; only the host decides the next value');
  // Through the SHARED helper, not a private copy — the duplication this story was told to remove.
  assert.match(text, /import \{ settingWritten \} from '\.\/settingWrite'/u);
});
