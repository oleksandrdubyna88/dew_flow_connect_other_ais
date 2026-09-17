import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DbLog, EMPTY_LOG, EMPTY_TOTALS, findingsByRound, parseFindings, parseLog, roundKeyOf } from '../roundsDb';
import { blindSpotsHtml, LogRow, roundsLogHtml, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';

/**
 * The log page reads the rounds database — the findings themselves, not only how many.
 *
 * <p>Until the server grew `coai.db` there was nothing to read: a session file records that codex
 * produced four findings and not what they were. The page therefore showed a number with nothing
 * behind it, and the operator's question — "мы тут пишем сами находки в бд?" — had the answer no.</p>
 *
 * <p>The server reads the database and answers `--log` with JSON, so nothing here needs SQLite. That
 * makes the extension's half two pure things: believing that JSON only as far as its shape, and
 * matching a round in it to a row built from the session files.</p>
 */

function session(rounds: readonly RoundRecord[], repoPath = 'D:/repo', branch = 'main'): SessionFile {
  return {
    state: { sessionId: 's1', repoPath, branch, stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

function round(over: Partial<RoundRecord> = {}): RoundRecord {
  return {
    stage: 'CodeReview',
    number: 1,
    verdict: 'proceed',
    gatingCount: 1,
    reviewers: 'all 3 reviewers answered',
    status: 'done',
    startedUtc: '2026-09-05T07:41:00.000Z',
    completedUtc: '2026-09-05T07:43:10.000Z',
    subject: 'SCOPE — something',
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '' }],
    ...over,
  } as RoundRecord;
}

const NOW = Date.parse('2026-09-05T08:00:00.000Z');

const LOG: DbLog = {
  consultations: [],
  rounds: [{
    repoPath: 'D:\\repo', branch: 'main', stage: 'CodeReview', number: 1,
    startedUtc: '2026-09-05T07:41:00.000Z', sessionId: 's1', accepted: 1, rejected: 1,
    findings: [
      {
        ordinal: 0, severity: 'Major', category: 'Reliability', file: 'src/Panel.cs', line: 40,
        title: 'session file opened without FileShare', why: 'a reader forbids writing', fix: 'share it',
        role: 'SecurityReliability', isGating: true, providers: 'codex',
        resolution: 'accept', reason: '', reRaised: false,
      },
      {
        ordinal: 1, severity: 'Minor', category: 'Ux', file: '', line: 0,
        title: 'a name could be clearer', why: 'it reads oddly', fix: 'rename it',
        role: 'UxDxPerformance', isGating: false, providers: 'gemini',
        resolution: 'reject', reason: 'the name is the domain word', reRaised: true,
      },
    ],
    cursor: '2026-09-05T07:41:00.000Z|1',
    foundCount: 2,
    resolvedUtc: '',
  }],
  blindSpots: [
    { kind: 'category', name: 'Reliability', accepted: 7, total: 9 },
    { kind: 'category', name: 'Ux', accepted: 0, total: 11 },
    { kind: 'role', name: 'SecurityReliability', accepted: 5, total: 8 },
    { kind: 'providers', name: 'codex', accepted: 7, total: 12 },
  ],
  defended: [{
    ordinal: 1, severity: 'Minor', category: 'Ux', file: '', line: 0,
    title: 'a name could be clearer', why: 'it reads oddly', fix: 'rename it',
    role: 'UxDxPerformance', isGating: false, providers: 'gemini',
    resolution: 'reject', reason: 'the name is the domain word', reRaised: true,
  }],
  totals: EMPTY_TOTALS,
  read: true,
  paged: true,
};

// ---------- believing another program's JSON, only as far as its shape ----------

test('a round comes back with the findings it produced', () => {
  const log = parseLog(JSON.stringify(LOG));

  assert.equal(log.rounds.length, 1);
  assert.equal(log.rounds[0]?.findings.length, 2);
  assert.equal(log.rounds[0]?.findings[0]?.title, 'session file opened without FileShare');
  assert.equal(log.rounds[0]?.findings[0]?.resolution, 'accept');
  assert.equal(log.rounds[0]?.findings[1]?.reason, 'the name is the domain word');
});

test('nonsense, or a shape from a future server, is an empty log rather than a broken page', () => {
  // This reads a file written by another program. A page that throws on one unexpected field is a
  // page that goes blank for a reason nobody can see from the outside.
  assert.deepEqual(parseLog('not json at all'), EMPTY_LOG);
  assert.deepEqual(parseLog(''), EMPTY_LOG);

  // A server that ANSWERED, with nothing in it, is not the same as no answer: the page is painted
  // before the database is read, and a row may only be called "never recorded" against a read that
  // happened. So this one parses to an empty list that says it was read.
  const answered = parseLog('{"rounds":null}');
  assert.equal(answered.rounds.length, 0);
  assert.equal(answered.read, true);
  assert.equal(EMPTY_LOG.read, false, 'and the value that means "not asked" says so');
  assert.equal(parseLog('{"rounds":[{}]}').rounds[0]?.accepted, -1, 'nobody closed a gate we know nothing about');
  assert.equal(parseLog('{"rounds":[{}]}').rounds[0]?.findings.length, 0);
});

// ---------- matching a database round to a page row ----------

test('a round is matched however Windows spelled its path', () => {
  assert.equal(
    roundKeyOf('s1', 'D:\\repo\\', 'MAIN', 'CodeReview', 1),
    roundKeyOf('s1', 'd:/repo', 'main', 'codereview', 1),
    'separators, a trailing slash and case are not three different repositories');
  // And the session is in the key because round numbers restart: one repository and branch reviewed
  // twice has two "CodeReview round 1" records, and keyed without it the second overwrote the first.
  assert.notEqual(
    roundKeyOf('s1', 'D:/repo', 'main', 'CodeReview', 1),
    roundKeyOf('s2', 'D:/repo', 'main', 'CodeReview', 1),
    'two sessions each have a round 1');
});

test('the row a round belongs to carries its findings', () => {
  const [row] = rowsFrom([session([round()])], NOW, () => undefined, [], LOG) as [LogRow];

  assert.equal(row.found.length, 2);
  assert.equal(row.found[0]?.title, 'session file opened without FileShare');
});

test('a row with nothing in the database keeps rendering, with no findings', () => {
  // Every round recorded before the database existed, and every one on a machine whose server is
  // older than it. The page shows what it always showed.
  const [row] = rowsFrom([session([round({ number: 9 })])], NOW, () => undefined, [], LOG) as [LogRow];

  assert.deepEqual(row.found, []);
  assert.equal(row.findings, 1, 'the COUNT is still what the session file says');
});

test('findings are keyed one round at a time', () => {
  assert.equal(findingsByRound(LOG).size, 1);
  assert.equal(findingsByRound(EMPTY_LOG).size, 0);
});

// ---------- what the page does with them ----------

test('the page can render a finding under its round', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW, () => undefined, [], LOG), [], 'n');
  const script = html.slice(html.indexOf('<script'));

  assert.match(script, /function foundHtml/, 'the detail region builds them');
  assert.match(script, /session file opened without FileShare/, 'and the rows carry what they say');
  assert.match(script, /raised again/, 'including that one was raised over a standing rejection');
});

// ---------- the two questions the data exists for ----------

test('what the caller ACCEPTS is shown by category, by role and by vendor', () => {
  const html = blindSpotsHtml(LOG);

  assert.match(html, /By category/);
  assert.match(html, /By reviewer role/);
  assert.match(html, /By vendor/);
  assert.match(html, /Reliability/);
  // Accepted OVER total: a category that produces eleven findings and gets none taken says something
  // quite different from one that produces nine and gets seven.
  assert.match(html, /<td class="num">7<\/td><td class="num">9<\/td><td class="num">78<\/td>/);
});

test('what it argued with and got again is its own list, with the reason that still stood', () => {
  const html = blindSpotsHtml(LOG);

  assert.match(html, /Rejected, and raised again anyway/);
  assert.match(html, /the name is the domain word/);
});

test('with nothing decided yet the tab says what will fill it', () => {
  const html = blindSpotsHtml(EMPTY_LOG);

  assert.match(html, /Nothing decided yet/);
  assert.doesNotMatch(html, /By category/);
});

test('the page has the third tab, and it is not the one it opens on', () => {
  const html = roundsLogHtml([], [], 'n', '', blindSpotsHtml(LOG));

  assert.match(html, /data-tab="spots"/);
  assert.match(html, /id="tab-spots"/);
  // The ATTRIBUTES are not the assertion — that it renders hidden is. Pinning their exact order
  // made this red when S6 added the `data-section` marker the tab handler now derives from,
  // which is a change to how sections are found and not to whether this one opens closed.
  assert.match(html, /<section id="tab-spots"[^>]*\shidden>/, 'the rounds tab is what opens');
  assert.match(html, /<section id="tab-spots"[^>]*\sdata-section="spots"/, 'and it carries the marker');
  assert.match(html.slice(html.indexOf('<script')), /message\.type === 'spots'/, 'and it is pushed live like the rest');
});

// ---------- a finding must arrive with an identity, or it is not a finding ----------

test('an entry with no ordinal is an unreadable answer, not a finding with ordinal 0', () => {
  // `finding()` fills every field it is not given, so `{}` used to become a real-looking finding
  // numbered 0 with no title and no resolution — which the CSV then published as an OPEN finding
  // nobody raised. The operator's ruling, 2026-09-14: the system must not synthesise an entity out
  // of an empty object; no data means a validation error, never a fabricated row.
  assert.equal(parseFindings(JSON.stringify({ findings: [{}] })), undefined);
  assert.equal(parseFindings(JSON.stringify({ findings: [{ title: 'no ordinal' }] })), undefined);
  assert.equal(parseFindings(JSON.stringify({ findings: [{ ordinal: 'first' }] })), undefined,
    'an ordinal that is not a number is not an ordinal');
  assert.equal(parseFindings(JSON.stringify({ findings: [{ ordinal: 1.5 }] })), undefined,
    'nor is a fraction');
  assert.equal(parseFindings(JSON.stringify({ findings: [{ ordinal: -1 }] })), undefined,
    'nor a negative one');
  assert.equal(parseFindings(JSON.stringify({ findings: [null] })), undefined,
    'and a null is not an entry at all');
});

test('a finding that DOES carry its ordinal still gets its defaults filled', () => {
  // The other half of the rule: validation is about identity, not about demanding every field of a
  // record the server may have widened since this build shipped.
  const parsed = parseFindings(JSON.stringify({ findings: [{ ordinal: 0, title: 'a real one' }] }));

  assert.equal(parsed?.length, 1);
  assert.equal(parsed?.[0]?.title, 'a real one');
  assert.equal(parsed?.[0]?.severity, '', 'absent fields are still filled, as they always were');
  assert.equal(parsed?.[0]?.isGating, false);
});

test('one malformed entry fails the WHOLE list, because a short list reads as a cleaner round', () => {
  assert.equal(
    parseFindings(JSON.stringify({ findings: [{ ordinal: 0, title: 'real' }, {}] })),
    undefined,
    'dropping the bad entry would publish a round with fewer findings than it had');
});
