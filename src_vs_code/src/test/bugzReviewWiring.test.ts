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

// --------------------------------------------------------------------------------------------
// The un-anonymised view (story 2.3): the seams between the page and the panel that no page test
// can see — the toggle recorded, the fetch answered by POSTING with its generation, the cache
// handed back on paint, and all of it forgotten with the window.
// --------------------------------------------------------------------------------------------

test('the panel records the view the page switched to, and answers a fetch through one method', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /case 'realText':\s*this\.realText = m\.on;/u,
    'the toggle must reach the panel, or the next decision redraws the page anonymised');
  assert.match(text, /case 'fetchReal':\s*void this\.answerReal\(m\.id, m\.generation\);/u,
    'a row’s request must reach the one method that answers it, generation and all');
});

test('a fetch is answered by posting, with the generation echoed, and never by painting', () => {
  const answering = bodyOf(code('bugzReviewPanel.ts'), 'private async answerReal(id: number, generation: string): Promise<void> {');

  assert.match(answering, /type: 'real', id, generation, shown: view\.shown, keep: read\.ok, html: view\.html,/u,
    'the page decides whether an answer is still wanted, and it can only do that with the generation it asked with');
  // `keep` is the half the page cannot know: this side caches a read that REACHED the server and
  // not a process that failed, and without the word the page held the failure note anyway and
  // never asked again. (Code round, codex.)
  assert.match(answering, /keep: read\.ok/u, 'a failed process must stay retryable, and only this side knows it failed');
  assert.doesNotMatch(answering, /webview\.html\s*=/u, 'a completed fetch populates the cache and posts; it never redraws');
  assert.match(answering, /realView\(pair, read\)/u, 'the same renderer the paint uses, so the two cannot disagree');
});

test('the cache is consulted before the server, and a failed process is not kept', () => {
  const text = code('bugzReviewPanel.ts');
  const reading = bodyOf(text, 'private realOf(pair: ReviewPair): Promise<RealRead> {');
  const settling = bodyOf(text, 'private settle(pair: ReviewPair, read: RealRead): RealRead {');

  assert.ok(reading.indexOf('this.real.get(pair.findingId)') < reading.indexOf('this.hooks.readReal(pair.findingId)'),
    'four rows open and a flip must cost four processes once, not four per flip');
  assert.match(reading, /held\.headSha === pair\.headSha && held\.fixSha === pair\.fixSha/u,
    'a cache keyed on the id alone shows last week’s method under this week’s fix commit');
  // Keyed by the row AND its two commits. The id alone shares a process between two pairs that
  // are the same row at different commits: the answer is then stored under the shas it began with
  // and refused by both readers, so the fetch is spent and the new pair goes unanswered.
  // (Code round, codex, twice.)
  assert.match(reading, /const key = inFlightKey\(pair\);/u, 'two requests for one row in flight must share one process');
  assert.match(code('bugzReviewPanel.ts'),
    /function inFlightKey\(pair: ReviewPair\): string \{\s*return `\$\{pair\.findingId\}@\$\{pair\.headSha\}:\$\{pair\.fixSha\}`;/u,
    'the in-flight key must carry both commits, or it is the id alone wearing a longer name');
  assert.match(settling, /if \(read\.ok\) \{[\s\S]*?this\.real = new Map\(/u, 'only a read that reached the server is remembered');
});

test('the panel hands the view and the cache to every paint', () => {
  const paint = bodyOf(code('bugzReviewPanel.ts'), 'private paint(): void {');

  assert.match(paint, /realText: this\.realText,/u, 'a redraw after a decision must draw the view the person chose');
  assert.match(paint, /real: this\.realFor\(found\.shown\),/u, 'and hand back what is cached, or every decision costs a git read per open row');
  assert.match(paint, /draw: this\.draws,/u, 'and say which paint this is, so a stale answer can be told from a wanted one');
});

test('a closed window forgets the view and everything it fetched', () => {
  const disposed = /onDidDispose\(\(\) => \{([\s\S]*?)\}\);/u.exec(code('bugzReviewPanel.ts'));
  assert.ok(disposed !== null);

  assert.match(disposed[1] ?? '', /this\.realText = false;/u, 'a page reopened later shows what leaves the machine');
  assert.match(disposed[1] ?? '', /this\.real = new Map<number, HeldReal>\(\);/u, 'the cache’s lifetime is the window’s');
});

test('the provider wires the real-method hook to the real reader', () => {
  const text = code('panelProvider.ts');

  assert.match(text, /readReal: \(findingId\) => readRealMethod\(server\.fsPath, findingId\),/u,
    'a page test can prove the page asks; only this proves anybody answers');
});

test('the two view controls are wired to the host that clamps and writes', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /m\.type === 'zoom' \? applyZoomDelta\(m\.delta\) : applyToneDelta\(m\.delta\)/u,
    'the page reports the direction; only the host decides the next value');
  // Through the SHARED helper, not a private copy — the duplication this story was told to remove.
  assert.match(text, /import \{ settingWritten \} from '\.\/settingWrite'/u);
});
