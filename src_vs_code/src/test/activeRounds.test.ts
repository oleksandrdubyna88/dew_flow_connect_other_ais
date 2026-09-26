import assert from 'node:assert/strict';
import { test } from 'node:test';
import { liveRegions, panelHtml, PanelState, roundsBody, statusMark } from '../panelView';
import { panelState, runPanel } from './panelPageHarness';
import { RoundRecord, SessionFile } from '../rounds';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';

/**
 * The panel shows what is RUNNING, and nothing else.
 *
 * <p>Decided on 2026-09-05 after a week of the other thing. The section had become a history: a
 * 72-hour window of finished rounds, each a disclosure that opened to its reviewers, with a policy
 * for who opened what and a document-level toggle listener to report it. That machinery is where
 * the flicker lived — a list replaced by innerHTML every five seconds fires `toggle` for every
 * open card exactly as a click does, and the provider answered each one with another patch. It
 * also put a full repaint on every click, and a full repaint in every window on every tick.</p>
 *
 * <p>The operator's ruling: the sidebar answers "what is happening NOW"; everything that has
 * happened is a log, and a log wants a table with filters, sorting and search — a page, not a
 * sidebar. So a running round is shown whole, reviewers and all, because that is what somebody is
 * waiting on; a finished one is not shown at all; and there is nothing to open or close.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: '',
    gatingCount: 0,
    reviewers: '2 of 3 reviewers answered',
    status: 'running',
    startedUtc: '2026-09-05T08:00:00.000Z',
    completedUtc: '',
    subject: 'SCOPE — the thing being reviewed',
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: 30 },
      { provider: 'local', role: 'SecurityReliability', status: 'running', findings: 0, note: '' },
    ],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[], branch = 'main'): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch, stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

function state(sessions: readonly SessionFile[]): PanelState {
  return {
    settings: DEFAULTS,
    vendors: [],
    codexModels: [], agyModels: [],
    localEngines: {},
    server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
    side: '',
    perSide: false,
    latestServerVersion: '',
    questions: [],
    sessions: [...sessions],
    openSections: ['rounds'],
    usage: [],
    usageWindow: 'week',
    cliStatus: {},
    modelPrices: {},
    snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  };
}

const NOW = Date.parse('2026-09-05T08:05:00.000Z');

test('a running round is shown whole — its reviewers are the point', () => {
  const html = roundsBody([session([round()])], NOW);

  assert.ok(html.includes('SCOPE — the thing being reviewed'));
  assert.ok(html.includes('class="badge running"'));
  // Two lines each since #132: the identity, then the status in its own div under it. The status
  // line leads with its mark since #286 — the identity half is what these two are about, so they
  // assert it up to the mark and leave the mark itself to the tests below.
  assert.ok(html.includes('codex</span>/Architecture<div class="said">'), 'the reviewer rows are there without a click');
  assert.ok(html.includes('>done (1 finding, 30 s)<'), 'the finished reviewer no longer says what it found');
  assert.ok(html.includes('local</span>/SecurityReliability<div class="said">'));
  assert.ok(html.includes('>running<'), 'the running reviewer no longer says so');
});

/**
 * The reviewer row is two lines, because the model name made it one long one (#132).
 *
 * <p>What a reviewer IS — vendor, role, model — on the first; what it is DOING on the second,
 * indented. One `.reviewer` element still holds both: a reviewer is one row, and two sibling divs
 * would make it the only place in this card where a row is not a row (raised on the plan round).</p>
 */
test('a reviewer is two lines — what it is, then what it said', () => {
  const html = roundsBody([session([round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 3, note: '', seconds: 30, model: 'Qwen3.5-35B-A3B-Q5_vk128:latest' },
    ],
  })])], NOW);

  const row = /<div class="reviewer">[\s\S]*?<\/div><\/div>/.exec(html);
  assert.ok(row, `one .reviewer element holds both lines: ${html}`);
  assert.ok(row[0].includes('Qwen3.5-35B-A3B-Q5_vk128:latest'), 'the model is on the identity line');
  assert.match(
    row[0],
    /<div class="said"><span class="mark mark-done" aria-hidden="true">✓<\/span>done \(3 findings, 30 s\)<\/div>/u,
    'and the status is a line of its own inside it, led by its mark',
  );
  assert.ok(!/<div class="said">[\s\S]*Architecture/.test(row[0]), 'the role stays on the first line');
  assert.equal((row[0].match(/<div class="reviewer">/g) ?? []).length, 1, 'one row per reviewer, not two siblings');
});

// ---------- the status carries a mark you can see without reading (issue #286) ----------
//
// Six reviewers are six near-identical grey 11px lines differing in one word somewhere in the
// middle, so "what is happening" has to be READ. Each known status now leads its line with a
// coloured glyph.
//
// These call `statusMark` rather than searching the page for a class, and that is the point: the
// helper is a BRANCH, and a substring assertion over generated markup cannot tell a branch that
// returned the wrong glyph from one that returned the right one — the page contains a glyph either
// way. The mapping is asserted per status, exactly, because a set-of-four-distinct-classes
// assertion passes when running shows the queued ellipsis and queued shows the running arrow.
// (codex and gemini, the plan round, independently.)

test('each status a reviewer can be in carries its own mark, and the right one', () => {
  const expected: readonly (readonly [string, string, string])[] = [
    ['done', 'mark-done', '✓'],
    ['running', 'mark-running', '⟳'],
    ['queued', 'mark-queued', '…'],
    ['failed', 'mark-failed', '✗'],
  ];

  for (const [status, className, glyph] of expected) {
    const mark = statusMark(status);

    assert.ok(mark.includes(`class="mark ${className}"`), `${status} is not marked ${className}: ${mark}`);
    assert.ok(mark.includes(glyph), `${status} does not show ${glyph}, so its mark says something else: ${mark}`);
  }

  // And no two of them are the same mark, which the loop above cannot see on its own.
  const marks = expected.map(([status]) => statusMark(status));
  assert.equal(new Set(marks).size, marks.length, `two statuses share a mark: ${marks.join(' | ')}`);
});

test('a status nobody taught the panel gets a space, not a guess', () => {
  const mark = statusMark('thinking');

  // The column is still held — a row one glyph-width to the left of every other row is the ragged
  // edge this whole change is against.
  assert.ok(mark.includes('class="mark"'), 'an unknown status gets nothing at all, so its line starts further left');
  assert.ok(!/mark-\w/.test(mark), `an unknown status was given a meaning: ${mark}`);
  assert.ok(!/[✓⟳…✗]/u.test(mark), `a glyph was invented for a word the panel has not seen: ${mark}`);
});

test('a status that happens to name a property of every object gets no mark', () => {
  // `ReviewerState.status` is a free string the SERVER writes, and a plain object's lookup answers
  // for names it inherited: MARKS['toString'] is a FUNCTION, not undefined. Without an own-key
  // check the panel emits `mark-toString` and the function's source text into the row. Nothing
  // sends these words today — which is exactly why it would have been found in a session file
  // somebody had hand-edited rather than here. (codex, the code round.)
  for (const inherited of ['toString', 'constructor', 'valueOf', 'hasOwnProperty', '__proto__']) {
    const mark = statusMark(inherited);

    assert.ok(!/mark-\w/.test(mark), `${inherited} was treated as a known status: ${mark}`);
    assert.equal(mark, statusMark('thinking'), `${inherited} does not render as the ordinary unknown status`);
  }
});

test('a mark is never asked to render a value that is not a status', () => {
  // A hand-edited or foreign session file reaches the renderer as-is; this panel has been blanked
  // once already by a status that was not a string.
  for (const odd of ['', '   ', 'DONE', 'done ']) {
    assert.doesNotThrow(() => statusMark(odd), `statusMark threw on ${JSON.stringify(odd)}`);
  }

  assert.ok(!/mark-\w/.test(statusMark('')), 'an empty status was given a mark');
});

test('the mark is hidden from a screen reader, because the word is right there', () => {
  assert.ok(statusMark('done').includes('aria-hidden="true"'), 'the glyph is read out beside the word it duplicates');
});

test('each mark wears the colour its status means, from a theme variable', () => {
  // The exact mapping, not "some charts variable": an implementation that painted done red and
  // failed green would pass a test that only looked for the prefix, and would ship inverted status
  // indicators under a green suite. (codex and gemini, the plan round.)
  const css = panelHtml(state([]), 'n0nce', NOW).split('<style>')[1]!.split('</style>')[0]!;

  const expected: readonly (readonly [string, string])[] = [
    ['mark-done', 'green'],
    ['mark-running', 'blue'],
    ['mark-queued', 'yellow'],
    ['mark-failed', 'red'],
  ];

  for (const [className, chart] of expected) {
    assert.match(
      css,
      new RegExp(`\\.${className} \\{ color: var\\(--vscode-charts-${chart}\\)`),
      `${className} is not ${chart}, so the colour says something other than the word beside it`,
    );
  }

  // Without a width the empty mark holds no column, and without a margin the glyph touches the word.
  // MIN-width rather than width: a glyph wider than 1.1em in somebody's font would be CLIPPED by a
  // fixed one, and a clipped status mark is worse than a row that shifts. (uxdx, the code round.)
  assert.match(css, /\.reviewer \.said \.mark \{[^}]*min-width: 1\.1em/, 'an unmarked row starts further left than a marked one');
  assert.match(css, /\.reviewer \.said \.mark \{[^}]*margin-right/, 'the glyph is flush against the first letter of the word');
});

test('the sentence a reviewer says is unchanged by the mark', () => {
  // The regression guard the plan round asked for. An implementer who made `said` detail-only
  // BECAUSE the status is drawn separately would still pass the sidebar tests above, while the
  // rounds-log page — the other consumer of these rows — silently lost the word.
  const html = roundsBody([session([round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 3, note: '', seconds: 30, model: '' },
      { provider: 'codex', role: 'Conventions', status: 'queued', findings: 0, note: '2 ahead on this engine', seconds: 0, model: '' },
    ],
  })])], NOW);

  assert.ok(html.includes('>done (3 findings, 30 s)<'), 'the finished reviewer lost its sentence');
  assert.ok(html.includes('>queued (2 ahead on this engine)<'), 'the queued reviewer lost the note that says how long');
});

test('a reviewer with no status gets no second line at all', () => {
  const html = roundsBody([session([round({
    reviewerStates: [{ provider: 'local', role: 'Architecture', status: '', findings: 0, note: '', model: '' }],
  })])], NOW);

  assert.ok(html.includes('local</span>/Architecture'), 'the reviewer is still listed');
  assert.ok(!html.includes('class="said"'), 'an empty status is no line, not an indented empty one');
});

test('the vendor colour is on the vendor word and nowhere else', () => {
  // Raised on the plan round: the markup moves, and an implementation could put the inline style on
  // the row or carry it onto the second line while every other assertion still passed.
  const html = roundsBody([session([round({
    reviewerStates: [{ provider: 'local', role: 'Architecture', status: 'done', findings: 1, note: '', model: 'qwen' }],
  })])], NOW, ['local']);

  assert.match(html, /<span class="who" style="color:[^"]+">local<\/span>/, 'the vendor word carries it');
  assert.ok(!/<div class="reviewer" style=/.test(html), 'the row does not');
  assert.ok(!/<div class="said" style=/.test(html), 'and neither does the status line');
});

test('a finished round is not in the sidebar at all', () => {
  // Five minutes ago, verdict and all. It belongs to the log, which is a page with a table.
  const finished = round({ status: 'done', verdict: 'proceed', completedUtc: '2026-09-05T08:04:00.000Z' });
  const html = roundsBody([session([finished])], NOW);

  assert.ok(!html.includes('SCOPE — the thing being reviewed'), 'nothing finished is listed');
  assert.ok(html.includes('Nothing is running'), 'and the empty state says where the rest went');
  assert.ok(html.includes('Show review rounds'));
});

test('an interrupted round is not running, so it is not shown either', () => {
  const dead = round({ status: 'interrupted' });

  assert.ok(!roundsBody([session([dead])], NOW).includes('SCOPE — the thing'));
});

test('two running rounds are both shown, newest first', () => {
  const older = round({ subject: 'OLDER', startedUtc: '2026-09-05T07:00:00.000Z' });
  const newer = round({ subject: 'NEWER', startedUtc: '2026-09-05T08:00:00.000Z', number: 2 });
  const html = roundsBody([session([older]), session([newer], 'feat/x')], NOW);

  assert.ok(html.indexOf('NEWER') < html.indexOf('OLDER'));
});

test('there is nothing to open: no card is a disclosure and the page never reports a toggle', () => {
  const html = panelHtml(state([session([round()])]), 'n', NOW);

  assert.ok(!html.includes('<details class="round"'), 'a running round is a block, not a disclosure');
  assert.ok(!html.includes("type: 'round'"), 'the toggle listener that fed the loop is gone');
});

test('the card head is three lines, so a narrow sidebar never cuts the branch off', () => {
  // Reported from the panel: "code review 1 · bench/rounds-collapse-r2 · running · 0 gating" on one
  // line, cut with an ellipsis where the branch got interesting.
  const html = roundsBody([session([round()], 'bench/rounds-collapse-r2')], NOW);

  assert.ok(html.includes('<div class="line">code review 1</div>'), 'the stage and number on their own line');
  assert.ok(html.includes('<div class="line branch">bench/rounds-collapse-r2</div>'), 'the branch on its own');
  assert.ok(html.includes('<div class="line"><span class="badge running">running</span> · 0 gating</div>'));
  assert.ok(!html.includes('code review 1 · bench'), 'nothing joins them back into one line');
});

test('the section is called what it shows', () => {
  const html = panelHtml(state([]), 'n', NOW);

  assert.ok(html.includes('Active rounds'));
  assert.ok(!html.includes('Recent rounds'));
});

test('a live patch that carries the same HTML as last time does not touch the DOM', () => {
  // Replacing identical markup is not free: it recreates every element and drops scroll position,
  // and nothing changed is the common case on a five-second tick.
  const script = panelHtml(state([]), 'n', NOW).split('</style>')[1] ?? '';

  assert.match(script, /message\.rounds !== lastRounds/, 'the rounds region is compared before it is replaced');
  assert.match(script, /message\.questions !== lastQuestions/);
});

// ---------------------------------------------------------------------------------------------
// The cadence line (research/PLAN_consult_on_a_cadence.md, epic 4 story 4.2)
// ---------------------------------------------------------------------------------------------

const cadence = [{
  repoPath: 'D:/repo',
  branch: 'feat/epic-4',
  answer: {
    plan: 'todo/PLAN_x.md', mode: 'remind', epics: 14, epicsClosed: [1, 2, 3, 4],
    groups: [{ range: '1-3', consulted: true }, { range: '4-6', consulted: false }],
    risk: [], riskAnswered: true, unreadable: '',
  },
}];

test('the cadence line is in Active rounds as the page renders it, and a live push to the page replaces it', () => {
  // RUN, not matched (PR #556, CodeRabbit; `.agents/PROJECT.md`): the panel's own script receives each
  // live push and replaces the region, so what is on screen is what this watches.
  const page = runPanel(panelState('rounds', { cadence }));
  assert.ok(page.region('live-rounds').includes('PLAN_x.md · epics closed 4/14 · consultation for epics 4-6: due'),
    'the first paint does not draw the line');

  const moved = [{ ...cadence[0]!, answer: { ...cadence[0]!.answer, epicsClosed: [1, 2, 3, 4, 5] } }];
  page.deliver({ type: 'live', ...liveRegions(panelState('rounds', { cadence: moved }), NOW) });
  assert.ok(page.region('live-rounds').includes('epics closed 5/14'), 'a live push did not bring the new line');

  page.deliver({ type: 'live', ...liveRegions(panelState('rounds', { cadence: [] }), NOW) });
  assert.ok(!page.region('live-rounds').includes('PLAN_x.md'), 'the line stayed on screen after the cadence went');
});

test('with no cadence line the region is exactly the running rounds, as before', () => {
  const plain = state([session([round()])]);

  assert.equal(liveRegions(plain, NOW).rounds, roundsBody(plain.sessions, NOW, []));
});
