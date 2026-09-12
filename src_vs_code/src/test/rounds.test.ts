import assert from 'node:assert/strict';
import { test } from 'node:test';
import { costPhrase, elapsed, isRunning, parseSession, reviewerLines, reviewerRows, RoundRecord } from '../rounds';

/**
 * What `rounds.ts` still guarantees now that the markdown log is gone.
 *
 * <p>This file used to be mostly about `renderRounds` / `renderSession` — the `rounds.md` tables
 * the extension wrote and rewrote every five seconds. That view was replaced by a page with a
 * sortable table on 2026-09-05 (`roundsLog.test.ts`), and the renderer went with it; the dating
 * and duration rules its tests pinned moved to the page's own rows. What is left here is the
 * parser and the small honest helpers the sidebar and the page both use.</p>
 */

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'CodeReview',
  number: 1,
  verdict: 'revise',
  gatingCount: 2,
  reviewers: 'all 6 reviewers answered',
  completedUtc: '2026-08-31T12:10:00Z',
  ...over,
});

test('a torn or foreign file is skipped, never a crash', () => {
  assert.equal(parseSession('{not json'), undefined);
  assert.equal(parseSession('{"something": "else"}'), undefined);
  assert.notEqual(
    parseSession(JSON.stringify({ state: { sessionId: 'a', repoPath: 'D:/r', branch: 'main', stage: 'CodeReview', awaitingResolve: false }, rounds: [round()] })),
    undefined,
  );
});

test('isRunning, elapsed and reviewerLines are honest about missing data', () => {
  assert.equal(isRunning(round()), false);
  assert.equal(elapsed(round(), Date.now()), '', 'a round with no start time claims no duration');
  assert.deepEqual(reviewerLines(round()), []);
  assert.equal(costPhrase(round()), 'no usage reported');
});

test('a start date from an older server does not render as a billion minutes', () => {
  // .NET's default date is year ONE, and the subtraction produced "1065396701m 44s" in the panel.
  assert.equal(elapsed(round({ startedUtc: '0001-01-01T00:00:00' }), Date.parse('2026-09-01T09:00:00Z')), '');
});

/**
 * The log names the model, and says nothing where it does not know one.
 *
 * <p>A round that names its vendor and its role but not its model cannot answer the question people
 * actually ask about a slow or a weak reviewer — which is how the claude row came to be investigated
 * by reading a spending ledger instead of the log
 * (`research/RESULTS_reviewer_input_sizes.md`).</p>
 *
 * <p>The absent case matters as much: every round written before this field exists has none, and
 * must read exactly as it did rather than growing an empty separator.</p>
 */
test('a reviewer row names the model it was launched with', () => {
  const withModel = round({
    reviewerStates: [
      { provider: 'remsoftdev-claude', role: 'Architecture', status: 'done', findings: 3, note: '', model: 'claude-haiku-4-5' },
    ],
  });

  assert.deepEqual(reviewerLines(withModel), [
    'remsoftdev-claude/Architecture · claude-haiku-4-5 — done (3 findings)',
  ]);
});

// ---------- the row has a second seam, for a sidebar that is narrow (#132) ----------

/**
 * A reviewer row is split twice: at the vendor's name, so the panel can colour that word and the
 * markdown export cannot — and at the em dash, so the panel can put what a reviewer IS on one line
 * and what it is DOING on the next. The model name doubled the length of the sentence when it was
 * added, and a 30-character model id is one unbreakable token: the line wrapped wherever that token
 * happened to end.
 *
 * <p>Both halves are RETURNED by the builder that already holds `role`, the model and the detail
 * list as separate values. Nothing parses the rendered sentence looking for a dash — which is what
 * the em-dash-in-a-model-name case below exists to keep true.</p>
 */
test('a reviewer row is split into what it is and what it said', () => {
  const [row] = reviewerRows(round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 3, note: '', seconds: 30, model: 'Qwen3.5-35B-A3B-Q5_vk128:latest' },
    ],
  }));

  assert.equal(row!.provider, 'local');
  assert.equal(row!.rest, '/Architecture · Qwen3.5-35B-A3B-Q5_vk128:latest', 'what it IS — no status, no dash');
  assert.equal(row!.said, 'done (3 findings, 30 s)', 'what it is DOING — and it owns no leading dash');
});

test('a model with an em dash in its name is not mistaken for a separator', () => {
  // The regression case for a split this code deliberately does NOT do. Raised on the plan round:
  // splitting the rendered sentence at ' — ' would put `custom` on the second line and lose half the
  // model from the first. Building both halves from the source fields cannot.
  const [row] = reviewerRows(round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', model: 'Qwen — custom' },
    ],
  }));

  assert.equal(row!.rest, '/Architecture · Qwen — custom', 'the whole id stays on the identity half');
  assert.equal(row!.said, 'done (0 findings)', 'and the status half is only the status');
  assert.deepEqual(reviewerLines(round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', model: 'Qwen — custom' },
    ],
  })), ['local/Architecture · Qwen — custom — done (0 findings)'], 'and the one-line form is unchanged');
});

test('a session whose status is missing, blank or not a string does not blank the panel', () => {
  // An interface is not runtime validation, and this file already learned that once with `model`.
  // A session that omits `status` reached `.length` and threw while the Active rounds view was
  // being built — one malformed reviewer taking the whole list with it. Raised on the code round by
  // two vendors. Whitespace is not a status either, or the panel renders an indented empty line.
  const states = [
    { provider: 'local', role: 'A', status: undefined as unknown as string, findings: 0, note: '' },
    { provider: 'local', role: 'B', status: 42 as unknown as string, findings: 0, note: '' },
    { provider: 'local', role: 'C', status: '   ', findings: 0, note: '' },
  ];

  assert.deepEqual(reviewerRows(round({ reviewerStates: states })).map((r) => r.said), ['', '', '']);
  assert.deepEqual(reviewerLines(round({ reviewerStates: states })), ['local/A', 'local/B', 'local/C']);
});

test('a reviewer with no status still reports the detail the file does record', () => {
  // Raised on the code round: the first fix suppressed the whole detail when the status was blank,
  // which hides a duration the session file states as a fact. Status and detail are independent.
  //
  // The finding COUNT is a separate matter and deliberately unchanged: it has been gated on
  // `status === 'done'` since long before this change, because a count from a reviewer that has not
  // finished is not a result. So a blank status keeps its duration and reports no findings.
  const detailed = round({
    reviewerStates: [{ provider: 'local', role: 'Architecture', status: '', findings: 3, note: '', seconds: 30 }],
  });

  assert.equal(reviewerRows(detailed)[0]!.said, '(30 s)');
  assert.deepEqual(reviewerLines(detailed), ['local/Architecture — (30 s)']);
});

test('a reviewer with no status says nothing, rather than a dangling dash', () => {
  // A session file is JSON somebody else wrote. A blank status used to render `…/Architecture — `
  // with a trailing dash, and under a two-line row it would add an indented empty line. Raised on
  // the plan round by two vendors; this is the one case where the one-line form CHANGES, and it
  // changes from a dangling dash to no dash.
  const blank = round({
    reviewerStates: [{ provider: 'local', role: 'Architecture', status: '', findings: 0, note: '', model: '' }],
  });

  assert.equal(reviewerRows(blank)[0]!.said, '');
  assert.deepEqual(reviewerLines(blank), ['local/Architecture']);
});

test('a reviewer launched without a model gets no separator, not an empty one', () => {
  // The case the "older round" test below does NOT cover, and a reviewer on the plan round was
  // right to separate them: an old file has no field at all, while a CURRENT round can carry an
  // empty string — a local engine with no model configured. Both must read the same.
  const noModel = round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', model: '' },
      { provider: 'local', role: 'SecurityReliability', status: 'done', findings: 0, note: '', model: '   ' },
    ],
  });

  assert.deepEqual(reviewerLines(noModel), [
    'local/Architecture — done (0 findings)',
    'local/SecurityReliability — done (0 findings)',
  ]);
});

test('a session carrying a model that is not a string renders as if it had none', () => {
  // An interface is not runtime validation. A hand-edited or foreign session file reaches the
  // renderer as-is, and `.trim()` on a number would throw while the log was being built — blanking
  // a whole page to render one row. Raised on the code round; the cast is the only way to express
  // "this file is not what the type says", which is the situation being tested.
  const nonsense = round({
    reviewerStates: [
      { provider: 'local', role: 'Architecture', status: 'done', findings: 0, note: '', model: 42 as unknown as string },
    ],
  });

  assert.deepEqual(reviewerLines(nonsense), ['local/Architecture — done (0 findings)']);
});

test('a round from before the field reads exactly as it did', () => {
  const older = round({
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '' }],
  });

  assert.deepEqual(reviewerLines(older), ['codex/Architecture — done (1 finding)']);
});
