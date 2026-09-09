import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  compareRows,
  LogRow,
  roundsLogHtml,
  rowMatches,
  rowsFrom,
} from '../roundsLog';
import { Escalation } from '../escalations';
import { RoundRecord, SessionFile } from '../rounds';
import { DbFinding, DbLog, DbRound, EMPTY_TOTALS } from '../roundsDb';

/**
 * The rounds log: every round of every session, as rows a table can sort, filter and search.
 *
 * <p>Asked for on 2026-09-05 over a screenshot of `rounds.md`: fifty-three lines of markdown
 * tables, one per session, each row an unwrapped line running off the right edge, no way to sort
 * by time across sessions, no way to find "every round on branch X". A log is a table.</p>
 *
 * <p>The predicates are plain functions that reference nothing outside themselves, because the
 * PAGE runs them: their source is embedded into the webview script verbatim, so the function under
 * test here is the function the table sorts with. One implementation, not a TypeScript one and a
 * hand-copied JavaScript one that drift.</p>
 */

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 2,
    reviewers: '3 of 3 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-05T07:41:00.000Z',
    completedUtc: '2026-09-05T07:43:10.000Z',
    subject: 'SCOPE — what it was supposed to achieve',
    tokensIn: 547000,
    tokensOut: 13000,
    costUsd: null,
    reviewerStates: [
      { provider: 'codex', role: 'Architecture', status: 'done', findings: 0, note: '', seconds: 46 },
      { provider: 'gemini', role: 'Architecture', status: 'done', findings: 0, note: '', seconds: 7 },
      { provider: 'local', role: 'Architecture', status: 'done', findings: 2, note: '', seconds: 28 },
    ],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[], repoPath = 'D:/rsd/dew_flow_connect_other_ais', branch = 'feat/shared-gate-rule'): SessionFile {
  return {
    state: { sessionId: 'ba0c73a1', repoPath, branch, stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

const NOW = Date.parse('2026-09-05T08:00:00.000Z');

// ---------- rows ----------

test('every round of every session becomes one row, newest first', () => {
  const older = round({ stage: 'PlanReview', startedUtc: '2026-09-05T07:40:00.000Z', completedUtc: '2026-09-05T07:40:35.000Z', subject: 'PLAN — older' });
  const rows = rowsFrom([
    session([older, round()]),
    session([round({ subject: 'OTHER REPO', startedUtc: '2026-09-04T20:00:00.000Z', completedUtc: '2026-09-04T20:05:00.000Z' })], 'C:/tmp/coai-round-card', '7133c2f'),
  ], NOW);

  assert.equal(rows.length, 3);
  assert.equal(rows[0]!.subject, 'SCOPE — what it was supposed to achieve');
  assert.equal(rows[1]!.subject, 'PLAN — older');
  assert.equal(rows[2]!.subject, 'OTHER REPO');
});

test('a row carries what the table shows, derived once', () => {
  const [row] = rowsFrom([session([round()])], NOW) as [LogRow];

  assert.equal(row.repoName, 'dew_flow_connect_other_ais', 'the folder name, for a column; the path stays for the filter');
  assert.equal(row.branch, 'feat/shared-gate-rule');
  assert.equal(row.stage, 'code review');
  assert.equal(row.number, 1);
  assert.equal(row.status, 'done');
  assert.equal(row.verdict, 'proceed');
  assert.equal(row.gating, 2);
  assert.equal(row.findings, 2, 'the sum over its reviewers');
  assert.equal(row.seconds, 130, 'completed minus started');
  assert.equal(row.tokensIn, 547000);
  assert.equal(row.tokensOut, 13000);
  assert.equal(row.costUsd, null, 'unknown stays unknown, never zero');
  assert.deepEqual(row.vendors, ['codex', 'gemini', 'local']);
  assert.equal(row.reviewers.length, 3, 'one line per reviewer, the same lines the sidebar shows');
});

test('a running round has a duration so far, and an interrupted one has none', () => {
  const running = round({ status: 'running', verdict: '', completedUtc: '', startedUtc: '2026-09-05T07:58:00.000Z' });
  const dead = round({ status: 'interrupted', verdict: 'interrupted', completedUtc: '' });
  const [a, b] = rowsFrom([session([running, dead])], NOW) as [LogRow, LogRow];

  assert.equal(a.status, 'running');
  assert.equal(a.seconds, 120, 'now minus started');
  assert.equal(b.status, 'interrupted');
  assert.equal(b.seconds, null, 'a round that died has no length — printing one would be a measurement nobody made');
});

test('a round from an older server has no subject, no tokens and no reviewers, and says so with blanks', () => {
  const old = { stage: 'PlanReview', number: 1, verdict: 'revise', gatingCount: 3, reviewers: 'all 2', completedUtc: '2026-08-30T00:00:00Z' } as RoundRecord;
  const [row] = rowsFrom([session([old])], NOW) as [LogRow];

  assert.equal(row.subject, '');
  assert.equal(row.tokensIn, null);
  assert.equal(row.findings, null);
  assert.deepEqual(row.reviewers, []);
  assert.equal(row.startedUtc, '', 'absent is not the epoch');
});

test('a start date from an older server does not become a billion seconds', () => {
  // .NET's default date is year ONE. Subtracting it from a real completion time produced
  // "1065396701m 44s" in the panel and in the file alike; the markdown renderer had a cap and
  // this page must not lose it with the renderer.
  const yearOne = round({ startedUtc: '0001-01-01T00:00:00', completedUtc: '2026-09-01T09:00:00Z' });
  const [row] = rowsFrom([session([yearOne])], NOW) as [LogRow];

  assert.equal(row.seconds, null, 'an implausible duration is no duration');
  assert.equal(row.startedUtc, '0001-01-01T00:00:00', 'the raw value is kept; only the arithmetic refuses it');
});

// ---------- sorting: the function the PAGE runs ----------

function rowsWith(...subjects: readonly string[]): LogRow[] {
  return subjects.map((subject, i) => ({
    ...rowsFrom([session([round({ subject, number: i + 1, startedUtc: `2026-09-05T0${i}:00:00.000Z`, completedUtc: `2026-09-05T0${i}:0${i}:00.000Z`, tokensIn: (i + 1) * 1000 })])], NOW)[0]!,
  }));
}

test('sorting by a column works both ways, and the page can carry the function verbatim', () => {
  const rows = rowsWith('b', 'a', 'c');

  assert.deepEqual([...rows].sort((x, y) => compareRows(x, y, 'subject', 'asc')).map((r) => r.subject), ['a', 'b', 'c']);
  assert.deepEqual([...rows].sort((x, y) => compareRows(x, y, 'subject', 'desc')).map((r) => r.subject), ['c', 'b', 'a']);
  assert.deepEqual([...rows].sort((x, y) => compareRows(x, y, 'tokensIn', 'desc')).map((r) => r.tokensIn), [3000, 2000, 1000]);
  assert.deepEqual([...rows].sort((x, y) => compareRows(x, y, 'startedUtc', 'asc')).map((r) => r.number), [1, 2, 3]);
});

test('a missing number sorts after every real one, whichever way the column goes', () => {
  const [known] = rowsWith('x') as [LogRow];
  const unknown: LogRow = { ...known, tokensIn: null, subject: 'old' };

  assert.equal(compareRows(unknown, known, 'tokensIn', 'asc') > 0, true);
  assert.equal(compareRows(unknown, known, 'tokensIn', 'desc') > 0, true, 'blanks go last even descending — a blank is not a small number');
});

// ---------- filtering and search: also the page's ----------

test('filters narrow by repository, branch, stage, status, verdict and vendor', () => {
  const [row] = rowsFrom([session([round()])], NOW) as [LogRow];

  assert.equal(rowMatches(row, { branch: 'feat/shared-gate-rule' }, ''), true);
  assert.equal(rowMatches(row, { branch: 'main' }, ''), false);
  assert.equal(rowMatches(row, { stage: 'code review', status: 'done', verdict: 'proceed' }, ''), true);
  assert.equal(rowMatches(row, { vendor: 'local' }, ''), true);
  assert.equal(rowMatches(row, { vendor: 'claude' }, ''), false);
  assert.equal(rowMatches(row, { repoPath: 'D:/rsd/dew_flow_connect_other_ais' }, ''), true);
});

test('search reads the subject, the branch, the repository and the reviewer lines, case-insensitively', () => {
  const [row] = rowsFrom([session([round()])], NOW) as [LogRow];

  assert.equal(rowMatches(row, {}, 'SUPPOSED'), true, 'the subject');
  assert.equal(rowMatches(row, {}, 'shared-gate'), true, 'the branch');
  assert.equal(rowMatches(row, {}, 'connect_other'), true, 'the repository');
  assert.equal(rowMatches(row, {}, 'gemini/Architecture'), true, 'a reviewer line');
  assert.equal(rowMatches(row, {}, 'nothing like this'), false);
  assert.equal(rowMatches(row, {}, '   '), true, 'blank search is no search');
});

// ---------- the page ----------

function question(): Escalation {
  return {
    id: 'q1',
    sessionId: 'ba0c73a1',
    repoPath: 'D:/rsd/dew_flow_connect_other_ais',
    branch: 'feat/shared-gate-rule',
    question: 'Ship with the <b>unresolved</b> finding?',
    openFindings: [{ severity: 'major', category: 'security', file: 'src/a.cs', line: 12, title: 'Token in log' }],
    askedUtc: '2026-09-05T07:50:00.000Z',
  };
}

test('the page carries the rows as JSON, the predicates verbatim, and no backtick in its script', () => {
  const rows = rowsFrom([session([round()])], NOW);
  const html = roundsLogHtml(rows, [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));

  assert.ok(html.includes('<table'), 'a table');
  assert.ok(script.includes('var compareRows = ' + compareRows.toString()), 'the sort the tests ran is the sort the page runs — bound by assignment, because a minifier renames the declaration');
  assert.ok(script.includes('var rowMatches = ' + rowMatches.toString()), 'and the filter');
  assert.ok(!script.includes('`'), 'the script lives inside a template literal');
  assert.ok(script.includes('"subject":"SCOPE'), 'the rows are there to render from');
  assert.ok(html.includes('nonce="n0nce"'));
});

test('nothing from a session or a question reaches the page unescaped', () => {
  const rows = rowsFrom([session([round({ subject: '<img src=x onerror=alert(1)>' })])], NOW);
  const html = roundsLogHtml(rows, [question()], 'n');

  assert.ok(!html.includes('<img src=x'), 'the subject is data, in the JSON and in the markup');
  assert.ok(html.includes('&lt;b&gt;unresolved&lt;/b&gt;'), 'the question is text');
  assert.ok(html.includes('Token in log'));
  assert.ok(roundsLogHtml([], [{ ...question(), openFindings: [] }], 'n').includes('No findings attached'), 'and a question without findings says so');
  assert.ok(html.includes('data-command="answer"') && html.includes('data-id="q1"'), 'the answer button reaches the command');
});

test('with no question waiting there is no questions section', () => {
  const html = roundsLogHtml([], [], 'n');

  assert.ok(!html.includes('waiting on you'));
  assert.ok(html.includes('No rounds yet'), 'and an empty log says so');
});

test('the table offers every column the log has, each sortable, and a filter per facet', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW), [], 'n');

  for (const column of ['when', 'repository', 'branch', 'stage', 'round', 'what', 'status', 'verdict', 'gating', 'findings', 'took', 'tokens in', 'tokens out', 'cost', 'reviewers']) {
    assert.ok(html.toLowerCase().includes(`data-sort="${column.replace(' ', '')}"`) || html.toLowerCase().includes(`>${column}<`), `column ${column}`);
  }
  for (const facet of ['repoPath', 'branch', 'stage', 'status', 'verdict', 'vendor']) {
    assert.ok(html.includes(`data-filter="${facet}"`), `filter ${facet}`);
  }
  assert.ok(html.includes('id="search"'));
});

// ---------- whether anybody has decided yet ----------

function dbFinding(over: Partial<DbFinding> = {}): DbFinding {
  return {
    ordinal: 1, severity: 'Major', category: 'Reliability', file: 'src/Panel.cs', line: 40,
    title: 'the session file is opened without FileShare', why: '…', fix: '…',
    role: 'Architecture', isGating: true, providers: 'codex', resolution: '', reason: '',
    reRaised: false,
    ...over,
  };
}

function dbLog(over: Partial<DbRound> = {}): DbLog {
  const findings = over.findings ?? [dbFinding()];

  return {
    rounds: [{
      repoPath: 'D:/rsd/dew_flow_connect_other_ais', branch: 'feat/shared-gate-rule',
      stage: 'CodeReview', number: 1, startedUtc: '2026-09-05T07:41:00.000Z',
      sessionId: 'ba0c73a1', accepted: -1, rejected: -1, findings,
      cursor: '2026-09-05T07:41:00.000Z|1',
      // The count FOLLOWS the findings unless a test names it on purpose. Once a paged server
      // stopped sending findings with the list, this is what a row's status reads, so a fixture
      // where the two disagree would be testing a state the server cannot produce.
      foundCount: findings.length,
      ...over,
    }],
    blindSpots: [],
    defended: [],
    totals: EMPTY_TOTALS,
    read: true,
    paged: true,
  };
}

test('a finished round whose findings nobody has decided says so, instead of done', () => {
  // Asked for over a screenshot: the row read Status `done`, Verdict `good_enough`, and thirteen
  // findings under it were still `open`. Both fields were true and together they read as finished.
  // `done` is about the reviewers having answered; whether the gate was CLOSED is a different fact,
  // and the server already records it — accepted stays -1 until a resolve lands.
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [], dbLog()) as [LogRow];

  assert.equal(row.status, 'awaiting');
  assert.deepEqual(row.decided, { accepted: -1, rejected: -1 });
});

test('once the decisions are recorded the row shows the split', () => {
  const [row] = rowsFrom(
    [session([round()])], NOW, () => undefined, [],
    dbLog({ accepted: 9, rejected: 4, findings: [dbFinding({ resolution: 'accept' })] })) as [LogRow];

  assert.equal(row.status, 'done');
  assert.deepEqual(row.decided, { accepted: 9, rejected: 4 });
});

test('a paged server sends the COUNT and not the findings, and the status still reads it', () => {
  // The list stopped carrying findings — 3.78 MB of a 3.83 MB payload, for rounds nobody had
  // opened. Without a count beside them, a round with an open gate and a round that raised nothing
  // become the same row, and every clean round would read as awaiting a resolve nobody owes.
  const [row] = rowsFrom(
    [session([round()])], NOW, () => undefined, [], dbLog({ findings: [], foundCount: 4 })) as [LogRow];

  assert.equal(row.status, 'awaiting');
  assert.equal(row.foundCount, 4);
  assert.equal(row.found.length, 0);
  assert.equal(row.foundState, 'unasked', 'nobody has opened it, so nothing has been asked for');
  assert.equal(row.origin, 'db');
});

test('a row the database has never heard of says so, rather than asking for what was never written', () => {
  // The distinction the five states rest on. An empty answer cannot say whether a round was clean
  // or was never recorded — and this row can, because the merge knows which half it came from.
  // The log is an ANSWER here, from a database holding a different round: `absent` is a claim about
  // what was read, so it may only be made about a read that happened.
  const answered = { ...dbLog({ number: 99 }), totals: { ...EMPTY_TOTALS, rounds: 1 } };
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [], answered) as [LogRow];

  assert.equal(row.origin, 'session');
  assert.equal(row.foundState, 'absent');
});

test('a page painted before the database is read calls no round absent', () => {
  // The page is painted FIRST, from the session files alone — reading the database spawns a process
  // and nobody should wait on one to see their log. So the first paint has EMPTY_LOG, and an unread
  // log used to be indistinguishable from a read one that found nothing: `0 >= 0`. Every row opened
  // saying the database had no record of it, and the tick that knew better was then held off by the
  // row already drawn. (Code round, CodeRabbit — Critical.)
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, []) as [LogRow];

  assert.equal(row.foundState, 'unasked', 'nothing has been read, so nothing can be claimed');
  assert.notEqual(row.foundState, 'absent');
});

test('a server too old to page has already sent everything, so a clean round is not asked about again', () => {
  // The pairing that would have turned an honestly clean round into a failed read: the old shape
  // carries every finding, so an empty list from it is the whole truth — and asking `--findings` of
  // a binary that does not know the flag gets exit 64. (Code round, codex.)
  const old = { ...dbLog({ findings: [] }), paged: false };
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [], old) as [LogRow];

  assert.equal(row.foundState, 'loaded');
  assert.equal(row.found.length, 0);
});

test('a round that produced no findings has nothing to decide and stays done', () => {
  // Otherwise every clean round would sit in "awaiting decisions" for ever: there is no resolve to
  // make, so the server's -1 never moves.
  const [row] = rowsFrom(
    [session([round()])], NOW, () => undefined, [], dbLog({ findings: [] })) as [LogRow];

  assert.equal(row.status, 'done');
});

test('a running round is running, whatever the database has not yet been told', () => {
  const [row] = rowsFrom(
    [session([round({ status: 'running', completedUtc: '' })])], NOW, () => undefined, [], dbLog()) as [LogRow];

  assert.equal(row.status, 'running');
});

test('a round the database has never heard of is done, not awaiting', () => {
  // An older server wrote no database at all, and a row that accused it of an unclosed gate would
  // be inventing a fact about a round nobody can resolve any more.
  const [row] = rowsFrom([session([round()])], NOW) as [LogRow];

  assert.equal(row.status, 'done');
  assert.equal(row.decided, null);
});
