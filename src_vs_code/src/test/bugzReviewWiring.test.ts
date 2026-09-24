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
  assert.match(
    text,
    /case 'expand':\s*case 'expandAll':\s*if \(!m\.open\) \{\s*this\.calls\.closed\(m\.ids, this\.held\);\s*\}\s*this\.remember\(m\.ids, m\.open\);/u,
    'one press and every press must both end in remember(), or Expand all records nothing');
  // A decision carries the page's ids and keep AND each pair's words — the draft when one is held —
  // so a keep press cannot write an empty comment over a stored one (story 4.2).
  // Resolved when the write RUNS, not when the press arrives: a thunk, evaluated after the previous
  // link's redraw has refreshed `this.held`. Evaluated eagerly, a comment queued behind a pending
  // Keep carried the OLD keep and quietly undid the decision. (CodeRabbit, the pull request.)
  assert.match(text, /case 'decide':\s*this\.queue\(\(\) => decisionsFor\(m\.ids, m\.keep, this\.held, this\.drafts\)\);/u);
  // And a comment is held as a draft BEFORE its write is queued, so a redraw while the write is in
  // the air still paints the words — pinned whole, the draft and the write in that order.
  assert.match(
    text,
    /case 'comment':\s*this\.drafts = new Map\(\[\.\.\.this\.drafts, \[m\.id, m\.text\]\]\);\s*this\.queue\(\(\) => commentWrite\(m\.id, m\.text, this\.held\)\);/u,
    'a comment must become a draft and then a write, or a redraw loses what was typed');
  // And a draft is let go ONLY when the write landed: a refused comment (65) or a binary too old
  // for comments (64) must leave the words in their box, with the reason on screen. Pinned whole —
  // the condition and the one statement inside it.
  assert.match(
    text,
    /if \(written\.ok\) \{\s*this\.drafts = settledBy\(this\.drafts, decisions, written\.decided\);\s*\}\s*await reportWrite\(written, decisions\.length\);/u,
    'a draft let go on a failed or PARTIAL write is a comment lost in the one case the person was told about');
  // A keystroke is held as a draft and written by nobody — and a closing panel writes every held
  // draft the store does not have, as one batch, BEFORE it lets them go (code round of 4.2, codex).
  assert.match(
    text,
    /case 'draft':\s*this\.drafts = new Map\(\[\.\.\.this\.drafts, \[m\.id, m\.text\]\]\);\s*return;/u,
    'a draft is held and never written on its own');
  assert.match(
    text,
    /for \(const \[id, text\] of this\.drafts\) \{\s*this\.queue\(\(\) => unwritten\(new Map\(\[\[id, text\]\]\), this\.held\)\);\s*\}\s*this\.drafts = new Map<number, string>\(\);/u,
    'a panel closed inside a pause must write what it holds before it forgets it — ONE write per draft, '
    + 'because a batch with one refused comment is refused whole and would take the others with it');
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

// --------------------------------------------------------------------------------------------
// Reaching the code (story 3.1): the seams between the page and the panel that no page test can
// see — the two presses routed, the answer posted and fanned out per repository, the current file
// opened only from inside the workspace, and all of it forgotten with the window.
// --------------------------------------------------------------------------------------------

test('the panel routes both openers to the methods that answer them', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /case 'openAt':\s*void this\.opened\(m\.id, \(pair\) => this\.revisions\.openAt\(pair, this\.held\)\);/u,
    'a press on Open at <sha> must reach the method that asks the server, or the button does nothing');
  assert.match(text, /case 'openCurrent':\s*void this\.opened\(m\.id, \(pair\) => this\.revisions\.openCurrent\(pair, this\.held\)\);/u,
    'a press on Open CURRENT must reach the method that guards and opens, or the button does nothing');
});

test('a revision answer is remembered, posted to the page and fanned out per repository — never painted', () => {
  const text = code('revisionPanel.ts');
  const opening = bodyOf(text, 'async openAt(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  assert.match(opening, /this\.memory = remember\(this\.memory, pair, read\);/u,
    'what the server said must be remembered for the panel\'s lifetime, or every press is a process');
  assert.match(opening, /this\.tell\(affectedBy\(this\.memory, pair, rows\), rows\);/u,
    'a checkout that is gone must reach every row of that repository, not only the one pressed');
  assert.doesNotMatch(opening, /webview\.html\s*=/u, 'an answer fills containers by posting; it never redraws');
  // The message type comes from `livePatch.ts` on BOTH sides now - the panel posts it and the page
  // script dispatches on it - so a rename changes them together instead of silently dropping the
  // patch. Pinned as the whole condition: the constant AND the payload. (Code round 2, codex.)
  assert.match(code('bugzReviewPanel.ts'), /type: REVISIONS\.message, items/u,
    'the page is told which rows changed and what they now say, from the one place holding a webview');
  assert.match(code('bugzReviewPanel.ts'), /type: CALLS\.message, items/u,
    'and the calls channel posts from the same owner, or a rename breaks one side only');
  assert.doesNotMatch(code('revisionPanel.ts'), /webview/u,
    'and the half that decides holds none, so there is still one door to VS Code');
});

test('what was held is opened again without a process, and the server is asked only otherwise', () => {
  const text = code('revisionPanel.ts');
  const opening = bodyOf(text, 'async openAt(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  // The whole condition: held first, the server only when nothing is held — one expression, so a
  // refactor cannot ask the server first and consult the memory afterwards.
  assert.match(opening, /const read = heldRevision\(this\.memory, pair\) \?\? await this\.fileAtOf\(pair\);/u,
    'a row already answered must open again from what was held, not from a second process');
  assert.match(bodyOf(text, '  private fileAtOf(pair: ReviewPair): Promise<FileAtRead> {'),
    /readFileAt\(\{ findingId: pair\.findingId, headSha: pair\.headSha, file: pair\.file \}\)/u,
    'and the fetch really reaches the hook the provider wires, carrying the three coordinates the answer is checked against');
});

test('the current file is opened from a repository inside this workspace, and only from inside that repository', () => {
  const opening = bodyOf(code('revisionPanel.ts'), 'async openCurrent(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  // The WHOLE condition, in one expression: the guard's verdict is what gates the open. A pin on the
  // call alone would survive `currentFileIn(...).then(() => ({ ok: true }))` — the guard running and
  // its answer ignored — which is the shape a source assertion is most likely to miss.
  assert.match(opening,
    /const target = await currentFileIn\(this\.hooks\.folders\(\), pair\.repoPath, pair\.file\);\s*let why = target\.ok \? '' : target\.why;\s*if \(target\.ok\) \{\s*try \{\s*await this\.hooks\.showCurrent\(target\.path, pair\.line\);/u,
    'the workspace folders, the recorded checkout and the recorded path must all go through the one guard, and only its yes opens anything');
  assert.doesNotMatch(opening, /hooks\.readFileAt/u, 'the current file costs no server process');
});

test('a closed window forgets what it learned about revisions', () => {
  const disposed = /onDidDispose\(\(\) => \{([\s\S]*?)\}\);/u.exec(code('bugzReviewPanel.ts'));
  assert.ok(disposed !== null);

  assert.match(disposed[1] ?? '', /this\.revisions\.forget\(\);/u,
    'a page reopened later must probe again — a checkout can have come back');
});

test('the panel hands what it remembers about revisions to every paint', () => {
  const paint = bodyOf(code('bugzReviewPanel.ts'), 'private paint(): void {');

  assert.match(paint, /revisions: this\.revisions\.stateFor\(found\.shown\),/u,
    'a redraw after a decision must say again which rows cannot be opened, or every reason is lost');
});

test('the provider wires the file-at hook to the real reader and both opens to the real editor', () => {
  const text = code('panelProvider.ts');

  assert.match(text, /readFileAt: \(asked\) => readFileAt\(server\.fsPath, asked\),/u,
    'a page test can prove the page asks; only this proves anybody answers');
  assert.match(text, /showRevision: \(document\) => this\.revisionDocuments\(\)\.show\(document\),/u,
    'the text must reach a read-only document of this product\'s own scheme');
  assert.match(text, /showCurrent: \(file, line\) => showCurrentFile\(file, line\),/u);
  assert.match(text, /folders: \(\) => workspaceFolderPaths\(\),/u,
    'the guard must be handed the real workspace folders');
});

test('the two view controls are wired to the host that clamps and writes', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /m\.type === 'zoom' \? applyZoomDelta\(m\.delta\) : applyToneDelta\(m\.delta\)/u,
    'the page reports the direction; only the host decides the next value');
  // Through the SHARED helper, not a private copy — the duplication this story was told to remove.
  assert.match(text, /import \{ settingWritten \} from '\.\/settingWrite'/u);
});


test('a press says it is working before it waits for anything', () => {
  // Code round, codex. The first press can wait through a server launch and several git calls while
  // the row still reads "not checked yet" beside an enabled button, so a person cannot tell whether
  // it registered. CLAUDE.md section 8 is the rule and it is about exactly this shape.
  const opening = bodyOf(code('revisionPanel.ts'), 'async openAt(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  const saysFirst = opening.indexOf('this.working');
  const waits = opening.indexOf('await');

  assert.ok(saysFirst >= 0, 'the row must be put into an in-flight state, not left on its old one');
  assert.ok(saysFirst < waits, 'and it must be said BEFORE the first await, or nobody sees it while it matters');
});

test('every ending replaces the in-flight state, including one nobody wanted', () => {
  // Code round, codex, twice: `void this.openAt(...)` with an un-caught `await` lets a rejection
  // from the editor escape as an unhandled promise rejection, and the row keeps its old note with
  // no explanation and no way to retry.
  const opening = bodyOf(code('revisionPanel.ts'), 'async openAt(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  assert.match(opening, /try \{/u, 'the editor call is the one that can reject, and it is not this side\'s to trust');
  assert.match(opening, /finally \{[\s\S]*?this\.tell\(/u,
    'the row state is posted on every path out, or a failure leaves the page saying it is still working');
});

// --------------------------------------------------------------------------------------------
// The review tree (story 3.2a): a press reaches the checkout, and the checkout opens a window of
// its own. Both halves are seams no page test and no unit test can see.
// --------------------------------------------------------------------------------------------

test('a checkout press reaches the one method that answers it', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /case 'openTree':\s*void this\.opened\(m\.id, \(pair\) => this\.revisions\.openTree\(pair, this\.held\)\);/u,
    'the third action must reach the panel, or the button is wired to nothing');
});

/**
 * The finding two plan reviewers raised independently, pinned as the WHOLE option object.
 *
 * <p>Without `forceNewWindow`, `vscode.openFolder` REPLACES the current window — so a person
 * pressing a button on a row would watch the review page they were reading disappear. A match on
 * the call alone would survive the option being dropped, which is the only way this can break.</p>
 */
test('a checkout is opened in a NEW window, never in the one holding the review page', () => {
  const text = code('revisionOpen.ts');

  assert.match(text, /executeCommand\(\s*'vscode\.openFolder',\s*vscode\.Uri\.file\(path\),\s*\{ forceNewWindow: true \}\)/u,
    'the option is the whole defect: pin it, not the call');
  assert.equal([...text.matchAll(/openFolder/gu)].length, 1,
    'one place opens a folder, so there is one place for this to be wrong');
});

test('a checkout says it is working before it awaits anything, and every ending replaces that', () => {
  const opening = bodyOf(code('revisionPanel.ts'), 'async openTree(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {');

  // CLAUDE.md §8. A checkout runs for a minute, which is exactly long enough for a person to
  // conclude nothing happened — so the state is said before the first await, not after the answer.
  assert.ok(opening.indexOf('this.tell([pair.findingId], rows);') < opening.indexOf('await '),
    'the row must say it is working before the server is asked, or the minute looks like nothing');
  assert.match(opening, /this\.checking = new Set\(\[\.\.\.this\.checking, pair\.findingId\]\);/u);
  assert.match(opening, /\} finally \{[\s\S]*?this\.checking = new Set\(\[\.\.\.this\.checking\]\.filter/u,
    'every ending must clear it, including the one where the editor refuses');
  assert.match(opening, /if \(this\.checking\.has\(pair\.findingId\)\) \{/u,
    'a second press while one is out must not start a second checkout');
});

test('a closed window forgets the checkout presses and what they said', () => {
  const forget = bodyOf(code('revisionPanel.ts'), 'forget(): void {');

  assert.match(forget, /this\.checking = new Set<number>\(\);/u);
  assert.match(forget, /this\.treeNotes = new Map<number, string>\(\);/u,
    'a refusal from last week is not a fact about this corpus');
});

test('the provider wires the checkout hooks to the reader and to the editor', () => {
  const text = code('panelProvider.ts');

  assert.match(text, /readTreeAt: \(asked\) => readTreeAt\(server\.fsPath, asked\),/u,
    'a page test can prove the page asks; only this proves anybody answers');
  assert.match(text, /openFolder: \(path\) => openTreeFolder\(path\),/u);
});

// --------------------------------------------------------------------------------------------
// Who calls this (story 3.3): the seams a page test and a value test cannot reach.
// --------------------------------------------------------------------------------------------

test('a calls press reaches the one method that answers it, and an opened end goes by INDEX', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /case 'calls':\s*void this\.opened\(m\.id, \(pair\) => this\.calls\.ask\(pair, this\.held\)\);/u,
    'the control must reach the panel, or it is wired to nothing');
  // The page holds no file names for this: it posts which row, which direction and which entry, and
  // the panel — which has the answer — opens it. A page that carried paths could be asked to open one.
  assert.match(text, /void this\.calls\.open\(named\.id, named\.which, named\.at\);/u);
});

test('the panel hands every row its calls block on every paint, and a closed window forgets them', () => {
  const text = code('bugzReviewPanel.ts');

  assert.match(text, /calls: new Map\(found\.shown\.map\(\(pair\) => \[pair\.findingId, this\.calls\.blockFor\(pair\)\] as const\)\)/u,
    'a redraw after a decision must not lose what a person asked for');
  const disposed = /onDidDispose\(\(\) => \{([\s\S]*?)\}\);/u.exec(text);
  assert.ok(disposed !== null);
  assert.match(disposed[1] ?? '', /this\.calls\.forget\(\);/u,
    'the answers were about a checkout that may have moved on');
});

test('only a THROW means the file is gone; a line past the end of one that opens is a move', () => {
  // The one decision in this story that no test can execute: `callHierarchyVsCode.ts` imports
  // `vscode`, which is why it is the single module in sonar.coverage.exclusions. So it is pinned as
  // a whole condition instead — the ternary AND both of its answers, because a fragment match would
  // survive its own break. Empty string, never undefined: `preparedAt` reads undefined as an absent
  // FILE and would send a person looking for one that is sitting in front of them. (Round 2.)
  const adapter = code('callHierarchyVsCode.ts');

  assert.match(
    adapter,
    /return line < opened\.lineCount \? opened\.lineAt\(line\)\.text : '';\s*\} catch \{\s*return undefined;/u,
    'the out-of-range answer and the absent-file answer must not be the same value');
});

test('the provider wires the calls hooks to the real editor, and opens only inside the workspace', () => {
  const text = code('panelProvider.ts');

  assert.match(text, /askCalls: \(about\) => askCalls\(callHierarchyEditor\(\), about\),/u,
    'a value test can prove the decisions; only this proves anybody asks the editor');
  // A provider answers with whatever URIs it knows — a dependency in node_modules, another root, a
  // generated file in a temp directory. Story 3.1 built the guard; it is reused, not rewritten.
  assert.match(text, /openCall: \(end\) => openInsideWorkspace\(end\.file, end\.line \+ 1\),/u);
  const opening = bodyOf(text, 'async function openInsideWorkspace(file: string, line: number): Promise<void> {');
  assert.match(opening, /currentFileIn\(\[folder\], folder, relative\(folder, file\)\)/u,
    'and the check is story 3.1 own, which tests the path as written AND as it really leads');
  assert.match(opening, /if \(inside\.ok\) \{/u,
    'a path the guard refuses opens nothing at all');
});

// --------------------------------------------------------------------------------------------
// CoAI: choose on a bug (issue #487): read, not run, for the reason at the top of this file — the
// panel, the provider, the extension and the chat command all import `vscode`.
// --------------------------------------------------------------------------------------------

test('a press on a row\'s CoAI: choose hands the hook that row\'s pair and the words being typed about it', () => {
  assert.match(code('bugzReviewPanel.ts'),
    /case 'choose':\s*void this\.opened\(m\.id, \(pair\) => this\.hooks\.choose\(bugChat\(pair, this\.drafts\)\)\);\s*return;/u,
    'the press must reach the hook with the DRAFTS, or the chat is handed a comment the person already changed');
});

test('the sidebar hands its hook to the chat, and the extension gives it the window\'s own registry', () => {
  assert.match(code('panelProvider.ts'), /choose: \(chat\) => this\.chooseInChat\?\.\(chat\) \?\? Promise\.resolve\(\),/u,
    'the review page\'s hook must reach the callback the extension handed in');
  const extension = code('extension.ts');
  const registry = extension.indexOf('const chatPanels = new ChatPanels();');
  const sidebar = extension.indexOf('new PanelProvider(');
  assert.ok(registry >= 0 && registry < sidebar, 'the registry must exist before the sidebar is handed it');
  // No door is recorded: the door inventory is one recorder per MANIFEST command, and this button is
  // not one — recording it as the right-click `choose` would count a press nobody made there.
  assert.match(extension, /\}, consultations, \(chat\) => chooseFromBug\(chatPanels, context\.extensionUri, chat\)\.catch\(\(reason: unknown\) => \{/u,
    'a press goes into THIS window\'s conversations');
  // The panel starts the hook with `void`, so a throw nobody catches is a press that does nothing and
  // says nothing — every other action on this page catches and says so. (Our own code reviewer.)
  const failure = bodyOf(extension, '(chat) => chooseFromBug(chatPanels, context.extensionUri, chat).catch((reason: unknown) => {');
  assert.match(failure, /console\.error\('CoAI: choose on a bug failed', reason\);/u, 'the detail goes to the console');
  assert.match(failure, /notify\(\{\s*as: 'warning',\s*class: 'failure',\s*source: 'bugz',\s*code: 'bug-not-chosen',[\s\S]*?detail: asText\(reason\),/u,
    'and a short sentence to the person, with the detail written down');
});

test('a second press on the same bug only brings its conversation back, and a first one is never sent', () => {
  const body = bodyOf(code('chatCommand.ts'), 'export async function chooseFromBug(');
  const reveal = body.search(/const open = bugChats\.openFor\(chat\.key, panels\);\s*if \(open !== undefined\) \{\s*open\.panel\.reveal\(\);\s*return;\s*\}/u);
  const deliver = body.search(/const key = bugChats\.keyFor\(chat\.key\);\s*await deliverPassage\(panels, extensionUri, ready, \{ kind: 'new', key, label: chat\.label \}, \{ text: chat\.text \}, false, false, fileUriOf\(chat\)\);\s*bugChats\.remember\(chat\.key, panels\);/u);

  assert.ok(reveal >= 0, 'an open conversation — found by its id, wherever it was re-keyed — is revealed and its composer left alone');
  assert.ok(deliver > reveal,
    'only a bug with no conversation gets a passage, keyed by the bug, never sent (send = false), and remembered by its id');
});
