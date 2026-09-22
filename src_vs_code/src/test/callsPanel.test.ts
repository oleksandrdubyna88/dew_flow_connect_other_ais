import assert from 'node:assert/strict';
import { test } from 'node:test';

import { ReviewPair } from '../reviewPair';
import { CallEnd, Calls, methodOf } from '../callHierarchy';
import { AskedAbout } from '../callHierarchyAsk';
import { CallsItem, CallsPanel } from '../callsPanel';

/**
 * What one review page remembers about who calls its methods — RUN, not read.
 *
 * <p>These were source-text assertions in the first draft of story 3.3, and a code-round reviewer was
 * right about them: `callsPanel.ts` reaches the world through two functions it is handed and imports
 * no `vscode`, so every one of its rules can be exercised instead of matched. A regex over
 * `async ask(...)` goes green on a body that has been rewritten to do the opposite; these do not.</p>
 */

const pair = (findingId: number, symbolName = `method${findingId}`, file = 'src/Totals.cs'): ReviewPair => ({
  findingId,
  symbolName,
  language: 'CSharp',
  skeletonBefore: '', skeletonAfter: '',
  keep: -1, severity: 'Major', category: 'Reliability', title: 'a race',
  repoPath: 'D:/repo', headSha: 'aaaa111', fixSha: 'bbbb222',
  file, line: 5, why: '', fix: '', comment: '', sentUtc: '', commentLost: '',
});

const ROWS = [pair(7), pair(8)];

const END = (name: string): CallEnd => ({ name, file: 'src/A.cs', line: 3, character: 4, detail: '' });

const answered = (about: AskedAbout, ends: readonly CallEnd[] = [END('uses')]): Calls => ({
  findingId: about.findingId,
  about: methodOf(about.file, about.line, about.symbolName),
  attempt: about.attempt,
  prepared: 'ok',
  incoming: { asked: true, failed: false, ends },
  outgoing: { asked: true, failed: false, ends: [] },
});

/** A panel whose asking is held open, so the states between a press and an answer are observable. */
function panelThat(ask: (about: AskedAbout) => Promise<Calls>, open?: (end: CallEnd) => Promise<void>) {
  const said: CallsItem[][] = [];
  const opened: CallEnd[] = [];
  const panel = new CallsPanel(
    { ask, open: open ?? (async (end) => { opened.push(end); }) },
    (items) => { said.push([...items]); });

  return { panel, said, opened };
}

test('the row says it is asking BEFORE the language support is asked, and the answer replaces it', async () => {
  // Measured at 0.8-2.0 s cold: a row that looks idle for two seconds is a row a person presses again.
  let answer: (calls: Calls) => void = () => { /* set below */ };
  let askedWhile = '';
  const { panel, said } = panelThat((about) => new Promise<Calls>((resolve) => {
    askedWhile = said.at(-1)?.[0]?.html ?? '';
    answer = () => resolve(answered(about));
  }));

  const press = panel.ask(pair(7), ROWS);
  await Promise.resolve();

  assert.match(askedWhile, /asking the language support/u,
    'the asking state must be on screen by the time the provider is reached, not after it answers');
  answer(answered({ findingId: 7, attempt: 'a1', file: '', line: 0, symbolName: '' }));
  await press;
  assert.match(said.at(-1)?.[0]?.html ?? '', /1 method calls this/u, 'and every ending replaces it');
});

test('a press that throws leaves the row pressable again rather than asking forever', async () => {
  const { panel, said } = panelThat(async () => { throw new Error('the adapter fell over'); });

  await panel.ask(pair(7), ROWS);

  const last = said.at(-1)?.[0]?.html ?? '';
  assert.doesNotMatch(last, /asking the language support/u, 'a row stuck on "asking" can never be retried');
  assert.match(last, /Who calls this\?/u);
});

test('a superseded attempt does not overwrite the retry that replaced it', async () => {
  const held = new Map<string, () => void>();
  const { panel, said } = panelThat((about) => new Promise<Calls>((resolve) => {
    held.set(about.attempt, () => resolve(answered(about, about.attempt === 'a1' ? [END('stale')] : [END('fresh')])));
  }));

  const first = panel.ask(pair(7), ROWS);
  const second = panel.ask(pair(7), ROWS);

  held.get('a2')?.();
  await second;
  held.get('a1')?.();
  await first;

  const last = said.at(-1)?.[0]?.html ?? '';
  assert.match(last, /fresh/u, 'the awaited attempt is the one that may be shown');
  assert.doesNotMatch(last, /stale/u,
    'a timed-out request resolving after a second press must not overwrite the second answer');
});

test('collapsing a row drops BOTH what it is waiting for and the answer it already has', async () => {
  const { panel, said } = panelThat(async (about) => answered(about));

  await panel.ask(pair(7), ROWS);
  assert.match(said.at(-1)?.[0]?.html ?? '', /1 method calls this/u);

  panel.closed([7], ROWS);

  // Reopening after a branch switch would otherwise show the OLD checkout's count under the sentence
  // "in the current checkout" — the one thing this story promises never to say.
  assert.match(panel.blockFor(pair(7)), /Who calls this\?/u);
  assert.doesNotMatch(panel.blockFor(pair(7)), /1 method calls this/u);
});

test('collapsing REPAINTS the row, because the markup it was holding is still in the page', async () => {
  // The block lives in the detail row, which a collapse hides rather than removes. Forgetting the
  // answer on this side while leaving the old count in the DOM means re-expanding after a branch
  // switch shows that count under the sentence "in the current checkout" — which is the one thing
  // this story promises never to say, arrived at from the other direction. (Round 2, two reviewers.)
  const { panel, said } = panelThat(async (about) => answered(about));

  await panel.ask(pair(7), ROWS);
  const before = said.length;
  panel.closed([7], ROWS);

  assert.equal(said.length, before + 1, 'the page must be told, or it keeps painting the old answer');
  assert.match(said.at(-1)?.[0]?.html ?? '', /Who calls this\?/u);
  assert.doesNotMatch(said.at(-1)?.[0]?.html ?? '', /1 method calls this/u);
});

test('a collapse the page has already forgotten tells nothing about rows it cannot draw', () => {
  const { panel, said } = panelThat(async (about) => answered(about));

  panel.closed([99], ROWS);

  assert.deepEqual(said, [[]], 'a row that is not on the page has no block to repaint');
});

test('an answer that lands after its row was collapsed is refused, not shown', async () => {
  let answer: (calls: Calls) => void = () => { /* set below */ };
  const { panel } = panelThat((about) => new Promise<Calls>((resolve) => {
    answer = () => resolve(answered(about));
  }));

  const press = panel.ask(pair(7), ROWS);
  panel.closed([7], ROWS);
  answer(answered({ findingId: 7, attempt: 'a1', file: '', line: 0, symbolName: '' }));
  await press;

  assert.match(panel.blockFor(pair(7)), /Who calls this\?/u,
    'the provider cannot be stopped, so the answer is what gets refused');
});

test('a closed window forgets every answer, because the checkout may have moved on', async () => {
  const { panel } = panelThat(async (about) => answered(about));

  await panel.ask(pair(7), ROWS);
  panel.forget();

  assert.doesNotMatch(panel.blockFor(pair(7)), /1 method calls this/u);
});

test('an end is opened by INDEX out of what this side holds, and nothing else can be named', async () => {
  const { panel, opened } = panelThat(async (about) => answered(about, [END('first'), END('second')]));

  await panel.ask(pair(7), ROWS);

  await panel.open(7, 'in', 1);
  assert.deepEqual(opened.map((one) => one.name), ['second'], 'the index names one of OUR ends');

  await panel.open(7, 'in', 99);
  await panel.open(7, 'out', 0);
  await panel.open(8, 'in', 0);
  assert.equal(opened.length, 1,
    'a row with no answer, a direction with no ends and an index past the end each open nothing');
});

test('a row still waiting shows only that it is asking, and asking twice does not stack', async () => {
  const { panel, said } = panelThat(() => new Promise<Calls>(() => { /* never */ }));

  void panel.ask(pair(7), ROWS);
  void panel.ask(pair(7), ROWS);
  await Promise.resolve();

  assert.match(panel.blockFor(pair(7)), /asking the language support/u);
  assert.ok(said.every((one) => one.length === 1), 'every telling is about the one row that changed');
});

// --------------------------------------------------------------------------------------------
// A findingId is not an identity (round 2, coderabbit).
// --------------------------------------------------------------------------------------------

test('a row whose id now names a DIFFERENT method shows no answer, rather than the old one', async () => {
  // A collect can reuse a findingId for another finding. Keyed on the id alone, the panel would
  // render the previous method's callers under the new row's name — the same "confidently wrong"
  // number this whole story exists to prevent, arrived at through a refresh instead of a provider.
  const { panel } = panelThat(async (about) => answered(about));

  await panel.ask(pair(7), ROWS);
  assert.match(panel.blockFor(pair(7)), /1 method calls this/u);

  assert.doesNotMatch(panel.blockFor(pair(7, 'somethingElse')), /1 method calls this/u,
    'a different symbol under the same id is a different question');
  assert.doesNotMatch(panel.blockFor(pair(7, 'method7', 'src/Other.cs')), /1 method calls this/u,
    'and so is the same name in a different file');
  assert.match(panel.blockFor(pair(7)), /1 method calls this/u,
    'while the row it was actually about still shows its answer');
});

test('an answer for a row that has since become a different method is refused', async () => {
  let answer: (calls: Calls) => void = () => { /* set below */ };
  const { panel } = panelThat((about) => new Promise<Calls>((resolve) => {
    answer = () => resolve(answered(about));
  }));

  const asked = pair(7);
  const press = panel.ask(asked, ROWS);
  // The refresh happens while the provider is still thinking.
  const now = pair(7, 'somethingElse');
  answer(answered({ findingId: 7, attempt: 'a1', file: '', line: 0, symbolName: '' }));
  await press;

  assert.doesNotMatch(panel.blockFor(now), /1 method calls this/u,
    'the in-flight answer was about the method that row USED to be');
  assert.match(panel.blockFor(now), /Who calls this\?/u, 'and the new row can be asked about itself');
});
