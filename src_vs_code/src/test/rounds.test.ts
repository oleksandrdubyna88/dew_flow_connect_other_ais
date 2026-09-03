import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  costPhrase,
  elapsed,
  isRunning,
  parseSession,
  renderRounds,
  renderSession,
  reviewerLines,
  ReviewerState,
  RoundRecord,
  SessionFile,
} from '../rounds';

const session = (over: Partial<SessionFile['state']> = {}, rounds: SessionFile['rounds'] = []): SessionFile => ({
  state: {
    sessionId: 'abc123',
    repoPath: 'D:/rsd/thing',
    branch: 'feature/x',
    stage: 'CodeReview',
    awaitingResolve: false,
    ...over,
  },
  rounds,
});

test('no sessions renders an honest empty state, not an error', () => {
  const view = renderRounds([]);
  assert.ok(view.includes('No sessions yet'));
  assert.ok(view.includes('`open`'), 'it says what would create one');
});

test('a session with no rounds says so rather than rendering an empty table', () => {
  assert.ok(renderSession(session()).includes('No rounds yet'));
});

test('rounds render as a table with the verdict and the honest reviewer count', () => {
  const view = renderSession(
    session({}, [
      {
        stage: 'PlanReview',
        number: 1,
        verdict: 'revise',
        gatingCount: 3,
        reviewers: '4 of 6 reviewers answered; failed: gemini/UxDxPerformance: timeout',
        completedUtc: '2026-08-31T12:00:00Z',
      },
    ]),
  );
  // The row leads with the stage as a person says it, then the round, then WHAT was reviewed —
  // the same shape and the same words the panel uses, because two renderers over one file that
  // disagree make a reader ask which one is lying.
  assert.ok(view.includes('| plan review | 1 |  | ✔ done | `revise` | 3 |'), view);
  assert.ok(view.includes('4 of 6'), 'a partial round must not read as a full panel');
  assert.ok(view.includes('feature/x'));
});

test('awaiting resolve is visible in the heading', () => {
  assert.ok(renderSession(session({ awaitingResolve: true })).includes('awaiting resolve'));
});

test('sessions are ordered by most recent activity', () => {
  const older = session({ branch: 'old' }, [
    { stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 2', completedUtc: '2026-08-01T00:00:00Z' },
  ]);
  const newer = session({ branch: 'new' }, [
    { stage: 'PlanReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 2', completedUtc: '2026-08-30T00:00:00Z' },
  ]);
  const view = renderRounds([older, newer]);
  assert.ok(view.indexOf('## new') < view.indexOf('## old'));
});

test('a torn or foreign file is skipped, never a crash', () => {
  assert.equal(parseSession('{not json'), undefined);
  assert.equal(parseSession('{"something": "else"}'), undefined);
  assert.notEqual(parseSession(JSON.stringify(session())), undefined);
});

const round = (over: Partial<RoundRecord> = {}): RoundRecord => ({
  stage: 'CodeReview',
  number: 1,
  verdict: 'revise',
  gatingCount: 2,
  reviewers: 'all 6 reviewers answered',
  completedUtc: '2026-08-31T12:10:00Z',
  ...over,
});

test('a running round is shown as running, with the reviewers still in flight', () => {
  // The operator's report: a ten-minute round showed nothing at all while it ran.
  const view = renderSession(
    session({}, [
      round({
        status: 'running',
        verdict: 'running',
        startedUtc: '2026-08-31T12:00:00Z',
        reviewerStates: [
          { provider: 'codex', role: 'Architecture', status: 'done', findings: 3, note: '' },
          { provider: 'codex', role: 'SecurityReliability', status: 'running', findings: 0, note: '' },
          { provider: 'claude', role: 'Architecture', status: 'failed', findings: 0, note: 'timeout' },
        ],
      }),
    ]),
    Date.parse('2026-08-31T12:02:30Z'),
  );

  assert.ok(view.includes('⏳ running'), 'the status column says it is alive');
  assert.ok(view.includes('2m 30s'), 'and how long it has been going');
  assert.ok(view.includes('In flight now'));
  assert.ok(view.includes('codex/Architecture — done (3 findings)'));
  assert.ok(view.includes('codex/SecurityReliability — running'));
  assert.ok(view.includes('claude/Architecture — failed (timeout)'), 'a failure says why while the round is open');
});

test('tokens and money are shown per round, and an unpriced vendor is not called free', () => {
  const view = renderSession(session({}, [round({ tokensIn: 29364, tokensOut: 304, costUsd: 0.0489 })]));
  assert.ok(view.includes('29k in / 304 out'));
  assert.ok(view.includes('$0.0489'), 'a round costs fractions of a dollar, so cents are not enough');

  const silent = renderSession(session({}, [round({ tokensIn: 5300, tokensOut: 260, costUsd: null })]));
  assert.ok(silent.includes('no cost reported'), 'a vendor that does not price its run is unknown, not free');
  assert.ok(!silent.includes('$0.00'));
});

test('a round abandoned by a dead server reads as interrupted, never as running forever', () => {
  const view = renderSession(session({}, [round({ status: 'interrupted', verdict: 'interrupted' })]));
  assert.ok(view.includes('⚠️ interrupted'));
});

test('the whole view totals what has been spent', () => {
  const view = renderRounds([
    session({}, [round({ tokensIn: 1000, tokensOut: 100, costUsd: 0.01 })]),
    session({ branch: 'other' }, [round({ tokensIn: 2000, tokensOut: 200, costUsd: 0.02 })]),
  ]);
  assert.ok(view.includes('Across every round here: 3.0k in / 300 out · $0.0300'));
});

test('a file from an older server, with no status or usage, still renders', () => {
  // Forward compatibility in the direction that actually happens: the extension updates first.
  const view = renderSession(session({}, [round()]));
  assert.ok(view.includes('✔ done'));
  assert.ok(view.includes('no usage reported'));
});

test('isRunning, elapsed and reviewerLines are honest about missing data', () => {
  assert.equal(isRunning(round()), false);
  assert.equal(elapsed(round(), Date.now()), '', 'a round with no start time claims no duration');
  assert.deepEqual(reviewerLines(round()), []);
  assert.equal(costPhrase(round()), 'no usage reported');
});

test('a round carries what it was about, and an older round without one still renders', () => {
  const withSubject = renderSession(
    session({}, [
      {
        stage: 'CodeReview',
        number: 2,
        verdict: 'revise',
        gatingCount: 7,
        reviewers: 'all 6 reviewers answered',
        completedUtc: '2026-09-01T09:00:00Z',
        subject: 'PLAN — payment instruments',
      },
    ]),
  );
  assert.ok(withSubject.includes('| code review | 2 | PLAN — payment instruments |'), withSubject);
});

test('a start date from an older server does not render as a billion minutes', () => {
  // .NET's default date is year ONE, and the subtraction produced "1065396701m 44s" in the panel
  // and in the file alike.
  const view = renderSession(
    session({}, [
      {
        stage: 'PlanReview',
        number: 1,
        verdict: 'revise',
        gatingCount: 1,
        reviewers: 'all 2',
        completedUtc: '2026-09-01T09:00:00Z',
        startedUtc: '0001-01-01T00:00:00',
      },
    ]),
  );
  assert.ok(!/\d{4,}m/.test(view), view);
});

test('the rendered table is a table — the delimiter has as many cells as the header', () => {
  // It did not, and markdown is unforgiving about it: a delimiter row with a different cell count
  // than the header means the block is not a table at all, so a preview renders the whole thing as
  // one paragraph of pipes. The `What` column was added and this row was not extended with it.
  const markdown = renderRounds([
    {
      state: { sessionId: 's', repoPath: 'D:/r', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
      rounds: [
        {
          stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 0, reviewers: 'all 2 reviewers answered',
          completedUtc: '2026-09-01T12:00:00Z', startedUtc: '2026-09-01T11:58:00Z', status: 'done',
          subject: 'S1.1 — payment is a kind', tokensIn: 531_000, tokensOut: 23_000, costUsd: null,
        },
      ],
    },
  ]);

  const lines = markdown.split('\n').filter((l) => l.startsWith('|'));
  const cells = (line: string): number => line.split('|').length;
  const [header, delimiter, ...rows] = lines;

  assert.ok(header !== undefined && delimiter !== undefined);
  assert.match(delimiter!, /^\|[-|]+\|$/, 'the second line must be the delimiter');
  assert.equal(cells(delimiter!), cells(header!), 'a mismatched delimiter means markdown sees no table');
  for (const row of rows) {
    assert.equal(cells(row), cells(header!), `a data row disagrees with the header: ${row}`);
  }
});

test('a table row never contains a newline, because one row is one line', () => {
  const markdown = renderRounds([
    {
      state: { sessionId: 's', repoPath: 'D:/r', branch: 'main', stage: 'PlanReview', awaitingResolve: false },
      rounds: [
        {
          stage: 'PlanReview', number: 1, verdict: 'call_human', gatingCount: 0, reviewers: 'failed: codex/PlanCritique: exit 1\nand a second line',
          completedUtc: '2026-09-01T12:00:00Z', startedUtc: '2026-09-01T11:58:00Z', status: 'done',
          tokensIn: 0, tokensOut: 0, costUsd: null,
        },
      ],
    },
  ]);

  for (const line of markdown.split('\n')) {
    if (line.startsWith('| plan review')) {
      assert.ok(!line.includes('\n'));
    }
  }
  // A reviewer sentence with a newline in it would end the table mid-row.
  assert.ok(!markdown.includes('exit 1\nand a second line'), 'the newline must be flattened');
});

/**
 * The round line's own money, for the vendors that report none.
 *
 * <p>`pricing.test.ts` says why the estimate exists at all: *"every row in the spending section
 * read `—` and every round read `no cost reported`. That is TRUE, and it is also useless."* The
 * rows were fixed and the ROUND was not, so a round of codex and gemini — both with rates typed
 * into the panel — still read "no cost reported" beside 220k tokens.</p>
 *
 * <p>Why the rate is applied per REVIEWER and never to the round's total: a round runs several
 * vendors at once on different rates, so there is no single rate the sum is priceable by. That is
 * the same refusal as `UsageParserTests`' "inventing a price is worse than none".</p>
 */
const rates: Record<string, { in: number; out: number }> = {
  codex: { in: 0.2, out: 1.2 },
  gemini: { in: 0.75, out: 3.75 },
};
const rate = (provider: string): { in: number; out: number } | undefined => rates[provider];

const reviewer = (over: Partial<ReviewerState> = {}): ReviewerState => ({
  provider: 'codex',
  role: 'Architecture',
  status: 'done',
  findings: 0,
  note: '',
  ...over,
});

test('a round of vendors that bill nothing is priced from the rates the person typed', () => {
  const view = costPhrase(
    round({
      tokensIn: 1_000_000,
      tokensOut: 100_000,
      costUsd: null,
      reviewerStates: [
        reviewer({ tokensIn: 1_000_000, tokensOut: 100_000, costUsd: null }),
      ],
    }),
    rate,
  );

  // 1M in at 0.20 + 0.1M out at 1.20 = 0.20 + 0.12
  assert.ok(view.includes('~$0.32'), `expected an estimate priced from the rates, got: ${view}`);
  assert.ok(!view.includes('no cost reported'), 'a round with a rate behind it is not unknown');
  assert.ok(view.includes('1000k in / 100k out'), 'and the tokens are still there');
});

test('two vendors on different rates are priced apart and then added', () => {
  const view = costPhrase(
    round({
      tokensIn: 2_000_000,
      tokensOut: 200_000,
      costUsd: null,
      reviewerStates: [
        reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000 }),
        reviewer({ provider: 'gemini', tokensIn: 1_000_000, tokensOut: 100_000 }),
      ],
    }),
    rate,
  );

  // codex 0.20 + 0.12, gemini 0.75 + 0.375 => 1.445, shown to the cent because `money` only spends
  // four decimals below a dollar, where a round really does cost fractions of a cent.
  assert.ok(view.includes('~$1.45'), `expected each vendor priced by its own rate, got: ${view}`);
});

test('a reported cost is never replaced by an estimate, and the two are never mixed into one number', () => {
  const view = costPhrase(
    round({
      tokensIn: 2_000_000,
      tokensOut: 200_000,
      costUsd: 0.5,
      reviewerStates: [
        reviewer({ provider: 'claude', tokensIn: 1_000_000, tokensOut: 100_000, costUsd: 0.5 }),
        reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000, costUsd: null }),
      ],
    }),
    rate,
  );

  assert.ok(view.includes('$0.50'), 'what was billed is stated as billed');
  assert.ok(view.includes('~$0.32'), 'what was worked out is marked as worked out');
  assert.ok(!view.includes('$0.82'), 'a bill and a guess added into one number is a number nobody can check');
});

test('a round from an older server, with no per-reviewer usage, stays honestly unpriced', () => {
  const view = costPhrase(round({ tokensIn: 5300, tokensOut: 260, costUsd: null }), rate);

  assert.ok(view.includes('no cost reported'), 'a summed total over unknown vendors is not priceable');
  assert.ok(!view.includes('~$'), 'and it must not be guessed at by applying one rate to all of it');
});

test('a vendor with no rate contributes nothing rather than zero', () => {
  const view = costPhrase(
    round({
      tokensIn: 1_000_000,
      tokensOut: 100_000,
      costUsd: null,
      reviewerStates: [reviewer({ provider: 'local', tokensIn: 1_000_000, tokensOut: 100_000 })],
    }),
    rate,
  );

  assert.ok(view.includes('no cost reported'), 'nobody priced it and nobody can');
  assert.ok(!view.includes('$0.00'), 'free and unknown must not read the same');
});

test('the grand total across every round is priced the same way, per vendor and never on the sum', () => {
  const markdown = renderRounds(
    [
      session({ sessionId: 's1' }, [
        round({
          tokensIn: 1_000_000,
          tokensOut: 100_000,
          costUsd: null,
          reviewerStates: [reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000 })],
        }),
      ]),
      session({ sessionId: 's2' }, [
        round({
          tokensIn: 1_000_000,
          tokensOut: 100_000,
          costUsd: null,
          reviewerStates: [reviewer({ provider: 'gemini', tokensIn: 1_000_000, tokensOut: 100_000 })],
        }),
      ]),
    ],
    Date.parse('2026-09-03T12:00:00Z'),
    rate,
  );

  // codex 0.32 + gemini 1.125 — added only AFTER each is priced by its own rate.
  assert.ok(markdown.includes('~$1.45'), `expected the footer to price both vendors, got: ${markdown}`);
  assert.ok(!markdown.includes('Money is only counted'), 'the footer explains the tilde now');
});

/**
 * A partial estimate must not read as a total — the review round's finding, and one that will fire
 * on this very machine: the local engine burns tokens and has no rates behind it.
 */
test('a round with an unpriced vendor says the figure is not all of it', () => {
  const view = costPhrase(
    round({
      tokensIn: 2_000_000,
      tokensOut: 200_000,
      costUsd: null,
      reviewerStates: [
        reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000 }),
        reviewer({ provider: 'local', tokensIn: 1_000_000, tokensOut: 100_000 }),
      ],
    }),
    rate,
  );

  assert.ok(view.includes('~$0.32'), 'what could be priced, is');
  assert.ok(view.includes('+ unpriced'), 'and it says out loud that the rest could not be');
});

test('a repaired reviewer whose launches disagree marks the estimate partial too', () => {
  // One launch billed, another reported tokens and no money. Merged, the two cannot be separated,
  // so the tokens the bill does not cover would otherwise be silently counted as billed.
  const view = costPhrase(
    round({
      tokensIn: 1_000_000,
      tokensOut: 100_000,
      costUsd: null,
      reviewerStates: [
        reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000, partlyBilled: true }),
      ],
    }),
    rate,
  );

  assert.ok(view.includes('+ unpriced'));
});

test('a round where everything could be priced says nothing extra', () => {
  const view = costPhrase(
    round({
      tokensIn: 1_000_000,
      tokensOut: 100_000,
      costUsd: null,
      reviewerStates: [reviewer({ provider: 'codex', tokensIn: 1_000_000, tokensOut: 100_000 })],
    }),
    rate,
  );

  assert.ok(!view.includes('unpriced'), 'the marker is for the case that needs it, not decoration');
});
