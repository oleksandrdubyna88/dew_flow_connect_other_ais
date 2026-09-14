import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cell, csvOf, ROUND_COLUMNS, roundCells } from '../roundsCsv';
import { LogRow } from '../roundsLog';

/**
 * A round, as a line of a file somebody opens in a spreadsheet.
 *
 * <p>The two things this file is really about are the ones a reader cannot check by eye: that an
 * absent measurement stays absent rather than becoming a zero, and that nothing in a cell can run
 * when the file is opened. Both are properties of every column at once, so both are asserted over
 * the column list rather than over a hand-picked few — a test naming three columns is a test that
 * says nothing about the fourth somebody adds next week.</p>
 */

function row(over: Partial<LogRow> = {}): LogRow {
  return {
    key: 'k1', kind: 'review',
    startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    repoPath: 'D:/repo', repoName: 'repo', branch: 'main', stage: 'code review', number: 1,
    subject: 'SCOPE — the thing', status: 'done', decided: { accepted: 9, rejected: 4 },
    verdict: 'proceed', gating: 1,
    findings: 13, seconds: 130, decideSeconds: 300, tokensIn: 40_000, tokensOut: 7300,
    costUsd: null, costInUsd: 0.02, costOutUsd: 0.01, costTotalUsd: 0.03,
    costIsEstimate: true, costPartial: false,
    answered: 'all 3 reviewers answered', vendors: ['codex'],
    reviewers: ['codex/Architecture — done (2 findings)', 'gemini/Conventions — done (0 findings)'],
    reviewerColours: ['#fff', '#eee'], found: [], foundCount: 0, foundState: 'unasked', origin: 'db',
    dbKey: { sessionId: 's1', stage: 'CodeReview', number: 1 },
    ...over,
  };
}

/** The file's lines, without the byte-order mark and without the trailing blank. */
function lines(text: string): string[] {
  return text.replace(/^\uFEFF/, '').trimEnd().split('\r\n');
}

test('the file opens with a byte-order mark, exactly once', () => {
  // Excel reads a UTF-8 file as the system codepage without one, and the commonest content here is
  // a repository path or a subject in Cyrillic.
  const text = csvOf([row()]);

  assert.equal(text.startsWith('\uFEFF'), true);
  assert.equal(text.indexOf('\uFEFF', 1), -1, 'and not again in the body');
});

test('a header, then one line per round', () => {
  const text = lines(csvOf([row({ key: 'a' }), row({ key: 'b' })]));

  assert.equal(text[0], ROUND_COLUMNS.join(','));
  assert.equal(text.length, 3, 'a header and two rounds');
});

test('every column the header names is written, in that order', () => {
  // The header and the row are built from one list, and this is what makes that load-bearing: a
  // column added to one and not the other is a file whose headings stop describing its contents.
  assert.equal(roundCells(row()).length, ROUND_COLUMNS.length);
});

test('an absent measurement is an empty cell, never a zero', () => {
  // The whole reason `null` exists on these fields. A round from an older server recorded no
  // tokens, and writing 0 would say it used none — a measurement nobody made.
  const absent = row({
    findings: null, seconds: null, decideSeconds: null, tokensIn: null, tokensOut: null,
    costInUsd: null, costOutUsd: null, costTotalUsd: null, decided: null,
  });
  const written = roundCells(absent);

  for (const [at, name] of ROUND_COLUMNS.entries()) {
    if (['findings_count', 'analysis_seconds', 'decide_seconds', 'tokens_in', 'tokens_out',
      'cost_in_usd', 'cost_out_usd', 'cost_total_usd', 'accepted', 'rejected'].includes(name)) {
      assert.equal(cell(written[at]), '', `${name} must be empty when nobody recorded it`);
    }
  }
});

test('a gate nobody has closed writes no counts, because -1 is a state and not a number', () => {
  const open = roundCells(row({ decided: { accepted: -1, rejected: -1 } }));

  assert.equal(cell(open[ROUND_COLUMNS.indexOf('accepted')]), '');
  assert.equal(cell(open[ROUND_COLUMNS.indexOf('rejected')]), '');
});

test('a zero IS written, because somebody measured it', () => {
  const nothing = roundCells(row({ tokensIn: 0, decideSeconds: 0, decided: { accepted: 0, rejected: 0 } }));

  assert.equal(cell(nothing[ROUND_COLUMNS.indexOf('tokens_in')]), '0');
  assert.equal(cell(nothing[ROUND_COLUMNS.indexOf('decide_seconds')]), '0');
  assert.equal(cell(nothing[ROUND_COLUMNS.indexOf('accepted')]), '0');
});

test('a comma, a quote and a newline survive a round trip through a cell', () => {
  const awkward = 'he said "yes, probably",\r\nthen left';

  assert.equal(cell(awkward), '"he said ""yes, probably"",\r\nthen left"');
});

test('a semicolon is quoted too, for a spreadsheet whose separator it is', () => {
  assert.equal(cell('a;b'), '"a;b"');
});

test('EVERY string column is neutralised against a formula, not a chosen few', () => {
  // A repository called =HYPERLINK(…) or a branch called @release executes as readily as a
  // model-written title. This walks the whole row rather than naming columns, so a column added
  // later cannot quietly escape the guard. (Code round over the plan, codex.)
  const dangerous = '=cmd|\' /c calc\'!A1';
  const hostile = row({
    repoName: dangerous, repoPath: dangerous, branch: dangerous, stage: dangerous,
    subject: dangerous, status: dangerous as LogRow['status'], verdict: dangerous, kind: dangerous as LogRow['kind'],
    answered: dangerous, startedUtc: dangerous, reviewers: [dangerous],
  });

  for (const [at, name] of ROUND_COLUMNS.entries()) {
    const written = cell(roundCells(hostile)[at]);
    assert.equal(
      /^"?=/.test(written), false,
      `${name} reached the file still able to execute: ${written}`);
  }
});

test('each of the characters a spreadsheet treats as a formula is neutralised', () => {
  for (const lead of ['=', '+', '-', '@', '\t', '\r']) {
    const written = cell(`${lead}danger`);
    assert.equal(
      written.startsWith("'") || written.startsWith('"\''), true,
      `a cell beginning ${JSON.stringify(lead)} must be quoted as text, got ${written}`);
  }
});

test('a minus sign in front of a NUMBER is not mangled, because a number is not text', () => {
  // The guard applies to strings. A negative measurement is a number and must stay one, or every
  // spreadsheet sum over the column breaks.
  assert.equal(cell(-5), '-5');
});

test('a value that is not a measurement at all writes nothing', () => {
  assert.equal(cell(Number.NaN), '');
  assert.equal(cell(Number.POSITIVE_INFINITY), '');
});

test('the two cost qualifiers are their own columns, not glyphs glued to a number', () => {
  // `~` means worked out from a price list rather than billed, and `+` that one reviewer's model
  // had no listed price so the total is a floor. A file carrying the figure without them turns a
  // hedged number into a claim.
  const written = roundCells(row({ costIsEstimate: true, costPartial: true }));

  assert.equal(cell(written[ROUND_COLUMNS.indexOf('cost_is_estimate')]), 'true');
  assert.equal(cell(written[ROUND_COLUMNS.indexOf('cost_partial')]), 'true');
});

test('the reviewers ride in one cell, semicolon-joined, uncoloured', () => {
  const written = cell(roundCells(row())[ROUND_COLUMNS.indexOf('reviewers')]);

  assert.match(written, /codex\/Architecture/);
  assert.match(written, /gemini\/Conventions/);
  assert.doesNotMatch(written, /#fff/, 'the palette belongs to the page, never to the file');
});

test('the instant is written as stored AND as the reader reads it', () => {
  const written = roundCells(row());

  assert.equal(written[ROUND_COLUMNS.indexOf('started_utc')], '2026-09-05T07:41:00.000Z',
    'the stored value, unconverted');
  assert.notEqual(cell(written[ROUND_COLUMNS.indexOf('started_local_exporter')]), '',
    'and a rendering on the exporting machine beside it');
  assert.match(
    cell(written[ROUND_COLUMNS.indexOf('started_local_exporter')]), /\(UTC[+-]\d\d:\d\d\)/,
    'which names the offset it was written with, because a file cannot hold a future reader zone');
});

test('an unparseable instant leaves the local column empty rather than writing Invalid Date', () => {
  const written = roundCells(row({ startedUtc: 'whenever' }));

  assert.equal(cell(written[ROUND_COLUMNS.indexOf('started_local_exporter')]), '');
  assert.equal(written[ROUND_COLUMNS.indexOf('started_utc')], 'whenever', 'the raw value is kept');
});

test('a round with no rows at all is a header and nothing else', () => {
  assert.deepEqual(lines(csvOf([])), [ROUND_COLUMNS.join(',')]);
});

test('a formula hiding behind LEADING WHITESPACE is still neutralised', () => {
  // Excel trims a cell before deciding whether it is a formula, so a guard reading only the first
  // character sees a space and lets it through — two vendors raised it on the plan round. A leading
  // newline is the same trick.
  for (const lead of [' ', '\t', '\n', '\r', '  \t ', ' ']) {
    const written = cell(lead + "=cmd|' /c calc'!A1");
    assert.equal(
      written.startsWith("'") || written.startsWith('"\''), true,
      `a cell beginning ${JSON.stringify(lead)} then = must be written as text, got ${JSON.stringify(written)}`);
  }
});

test('a value that is not a primitive writes nothing, not "[object Object]"', () => {
  // A row arrives over the webview bridge and is believed only as far as its key. A field that
  // turns out to be an object is neither the data nor an honest blank if it reaches the file.
  assert.equal(cell({}), '');
  assert.equal(cell([1, 2]), '');
  assert.equal(cell(() => 1), '');
});
