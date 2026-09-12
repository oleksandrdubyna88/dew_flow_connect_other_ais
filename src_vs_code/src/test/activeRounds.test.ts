import assert from 'node:assert/strict';
import { test } from 'node:test';
import { panelHtml, PanelState, roundsBody } from '../panelView';
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
  // Two lines each since #132: the identity, then the status in its own div under it.
  assert.ok(html.includes('codex</span>/Architecture<div class="said">done'), 'the reviewer rows are there without a click');
  assert.ok(html.includes('local</span>/SecurityReliability<div class="said">running'));
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
  assert.match(row[0], /<div class="said">done \(3 findings, 30 s\)<\/div>/, 'and the status is a line of its own inside it');
  assert.ok(!/<div class="said">[\s\S]*Architecture/.test(row[0]), 'the role stays on the first line');
  assert.equal((row[0].match(/<div class="reviewer">/g) ?? []).length, 1, 'one row per reviewer, not two siblings');
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
