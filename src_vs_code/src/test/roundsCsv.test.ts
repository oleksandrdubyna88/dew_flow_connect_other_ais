import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  cell, csvOf, ExportableRow, ExportRound, FINDING_COLUMNS, findingCells, readStateOf, ROUND_COLUMNS,
  roundCells,
} from '../roundsCsv';
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

/**
 * A row as the page sends it.
 *
 * <p>Built as a `LogRow` first and handed out as an `ExportableRow`: the compiler still checks the
 * real shape, so adding a field to `LogRow` is a red build here, while the value handed to the CSV
 * writer has the same untyped shape a webview message actually has.</p>
 */
function row(over: Partial<LogRow> = {}): ExportableRow {
  const typed: LogRow = {
    key: 'k1', kind: 'review', calledBy: 'claude-code 7.3.1 · claude-opus-5',
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

  return { ...typed };
}

/**
 * A round WITH its findings, as the exporter takes them.
 *
 * <p>The state travels with the list because an empty list means two different things — nothing was
 * found, and nothing could be read — and the file must not render them the same way.</p>
 */
function round(
  over: Partial<LogRow> = {},
  found: ExportRound['found'] = { state: 'loaded', findings: [] },
): ExportRound {
  return { row: row(over), found };
}

/** The file, when it was written at all — a refusal is a failure of the test that expected one. */
function written(rounds: readonly ExportRound[]): string {
  const built = csvOf(rounds);
  assert.equal('text' in built, true, `expected a file, got a refusal: ${JSON.stringify(built)}`);

  return (built as { text: string }).text;
}

/** The file's lines, without the byte-order mark and without the trailing blank. */
function lines(text: string): string[] {
  return text.replace(/^\uFEFF/, '').trimEnd().split('\r\n');
}

test('the file opens with a byte-order mark, exactly once', () => {
  // Excel reads a UTF-8 file as the system codepage without one, and the commonest content here is
  // a repository path or a subject in Cyrillic.
  const text = written([round()]);

  assert.equal(text.startsWith('\uFEFF'), true);
  assert.equal(text.indexOf('\uFEFF', 1), -1, 'and not again in the body');
});

test('a header, then one line per round', () => {
  const text = lines(written([round({ key: 'a' }), round({ key: 'b' })]));

  assert.equal(text[0], [...ROUND_COLUMNS, ...FINDING_COLUMNS].join(','));
  assert.equal(text.length, 3, 'a header and two rounds');
});

test('every column the header names is written, in that order', () => {
  // The header and the row are built from one list, and this is what makes that load-bearing: a
  // column added to one and not the other is a file whose headings stop describing its contents.
  assert.equal(roundCells(row()).length, ROUND_COLUMNS.length);
});

test('the file says which AI asked for the round, and says nothing when nobody declared', () => {
  const cells = roundCells(row());
  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('asked_by')]), 'claude-code 7.3.1 · claude-opus-5');

  // A round recorded before the field existed. Empty is the truth about it; inventing a caller,
  // or borrowing the exporting window's own, would be a claim nothing supports.
  const older = roundCells(row({ calledBy: '' }));
  assert.equal(cell(older[ROUND_COLUMNS.indexOf('asked_by')]), '');
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
  assert.deepEqual(lines(written([])), [[...ROUND_COLUMNS, ...FINDING_COLUMNS].join(',')]);
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

// ---------- a row off the bridge is believed only as far as its key ----------

test('a row whose reviewers are not an array exports an empty cell, not a thrown export', () => {
  // `ExportableRow` is what a webview message actually is. The host used to cast it to `LogRow`,
  // and `row.reviewers.join` then threw on a torn row — failing the WHOLE export over one field.
  // (Code round, gemini, three findings.)
  for (const broken of [undefined, null, 'codex/Architecture', 42, {}]) {
    const cells = roundCells({ ...row(), reviewers: broken });
    assert.equal(cell(cells[ROUND_COLUMNS.indexOf('reviewers')]), '');
  }
});

test('a row whose decided is not an object exports empty counts rather than throwing', () => {
  for (const broken of [undefined, null, 'nine', 42, []]) {
    const cells = roundCells({ ...row(), decided: broken });
    assert.equal(cell(cells[ROUND_COLUMNS.indexOf('accepted')]), '');
    assert.equal(cell(cells[ROUND_COLUMNS.indexOf('rejected')]), '');
  }
});

test('a decided object carrying something that is not a number counts as nothing', () => {
  const cells = roundCells({ ...row(), decided: { accepted: 'lots', rejected: null } });

  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('accepted')]), '');
  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('rejected')]), '');
});

test('a startedUtc that is not a string leaves both time columns empty rather than throwing', () => {
  const cells = roundCells({ ...row(), startedUtc: { when: 'yesterday' } });

  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('started_local_exporter')]), '');
  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('started_utc')]), '');
});

test('a whole round of nothing but nonsense still writes one line of the right width', () => {
  // The honest failure mode: a torn row is a row of blanks, not a broken file and not an export
  // that refuses because of one field.
  const cells = roundCells({ key: 'k1' });

  assert.equal(cells.length, ROUND_COLUMNS.length);
  // Every cell blank except `findings_read`, which is a fact about the READ rather than about the
  // round's own data and defaults to the state the caller passed.
  for (const [at, name] of ROUND_COLUMNS.entries()) {
    if (name !== 'findings_read') {
      assert.equal(cell(cells[at]), '', `${name} should be blank for a row of nonsense`);
    }
  }
  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('findings_read')]), 'loaded');
});

test('a formula hiding behind a ZERO-WIDTH space is neutralised', () => {
  // A spreadsheet trims invisible characters before deciding what is a formula, and a zero-width
  // space is not matched by the regexp class for whitespace. Raised as Blocking on the code round.
  for (const invisible of ['​', '‌', '⁠', '﻿', ' ']) {
    const written = cell(invisible + '=HYPERLINK("http://x")');
    assert.equal(
      written.startsWith("'") || written.startsWith('"\''), true,
      `a cell beginning U+${invisible.codePointAt(0)!.toString(16)} then = must be written as text, got ${JSON.stringify(written)}`);
  }
});

test('a cell beginning with a pipe or a percent is neutralised too', () => {
  for (const lead of ['|', '%']) {
    const written = cell(lead + 'danger');
    assert.equal(written.startsWith("'"), true, `${lead} must be written as text, got ${written}`);
  }
});

// ---------- the findings, and the honesty rule about a read that failed ----------

function finding(over: ExportableRow = {}): ExportableRow {
  return {
    ordinal: 0, severity: 'Blocking', category: 'Reliability', file: 'src/Prompts.cs', line: 33,
    title: 'Stdio protocol error', why: 'the rule requires an omitted argument', fix: 'change the default',
    role: 'Conventions', isGating: true, providers: 'local', resolution: 'reject',
    reason: 'refuted by a test that runs the real binary', reRaised: false,
    ...over,
  };
}

test('one line per FINDING, with the round columns repeated on each', () => {
  const text = lines(written([round({}, {
    state: 'loaded',
    findings: [finding({ ordinal: 0, title: 'first' }), finding({ ordinal: 1, title: 'second' })],
  })]));

  assert.equal(text.length, 3, 'a header and two findings');
  assert.match(text[1]!, /first/);
  assert.match(text[2]!, /second/);
  assert.match(text[1]!, /SCOPE/, 'the round columns ride on every line');
  assert.match(text[2]!, /SCOPE/);
});

test('a round that genuinely found nothing still gets one line, with empty finding cells', () => {
  // Otherwise a clean round would vanish from a file that is supposed to be the log.
  const text = lines(written([round({}, { state: 'loaded', findings: [] })]));

  assert.equal(text.length, 2);
  assert.match(text[1]!, /SCOPE/);
  assert.equal(text[1]!.endsWith(','.repeat(FINDING_COLUMNS.length)), true,
    'every finding column is blank, and the line is still the full width');
});

test('a round the database never heard of says NOT RECORDED in its own column, not in decision', () => {
  // `decision` has a vocabulary — took / declined / open — and a value outside it makes a
  // downstream count of decided findings register one that does not exist. The read state is a
  // ROUND-level fact and has a column of its own. (Plan round, codex and gemini.)
  const text = lines(written([round({}, { state: 'absent', findings: [] })]));

  assert.match(text[1]!, /,not recorded,/, 'findings_read says it');
  assert.equal(text[1]!.endsWith(','.repeat(FINDING_COLUMNS.length)), true,
    'and every finding column, decision included, is blank');
});

test('when NOTHING could be read there is no file at all', () => {
  // A file of nothing but failures has no content worth a save dialog.
  const built = csvOf([round({ key: 'k1' }, { state: 'failed', findings: [] })]);

  assert.equal('refused' in built, true, 'no file is built when every round failed');
  assert.deepEqual((built as { refused: string[] }).refused, ['k1']);
});

test('one unreadable round among readable ones is MARKED, not silently dropped and not a refusal', () => {
  // Refusing everything was the first shape, and the plan round said so from two directions: before
  // selection controls exist it leaves somebody unable to export any of a log containing one bad
  // round. The file is written, the bad round is in it saying `failed`, and the caller is told.
  const built = csvOf([
    round({ key: 'good' }, { state: 'loaded', findings: [finding()] }),
    round({ key: 'bad' }, { state: 'failed', findings: [] }),
  ]);

  assert.equal('text' in built, true, 'the readable rounds are still written');
  const written_ = built as { text: string; unread: string[] };
  assert.deepEqual(written_.unread, ['bad'], 'and the caller learns which could not be read');

  const rows = lines(written_.text);
  assert.equal(rows.length, 3, 'a header, the good round, and the bad one');
  assert.match(rows[2]!, /,failed,/, 'the bad round says so in findings_read');
  assert.equal(rows[2]!.endsWith(','.repeat(FINDING_COLUMNS.length)), true,
    'and its finding columns are blank rather than invented');
});

test('the decision column says the word the PAGE says', () => {
  // `took` / `declined` / `open`, not `accept` / `reject` / ''. One vocabulary for one fact.
  const cells = (resolution: unknown): string =>
    cell(findingCells(finding({ resolution }))[FINDING_COLUMNS.indexOf('decision')]);

  assert.equal(cells('accept'), 'took');
  assert.equal(cells('reject'), 'declined');
  assert.equal(cells(''), 'open');
  assert.equal(cells(undefined), 'open');
});

test('a rejection carries its reason, and every finding column is written', () => {
  const cells = findingCells(finding());

  assert.equal(cells.length, FINDING_COLUMNS.length);
  assert.equal(cell(cells[FINDING_COLUMNS.indexOf('reason')]), 'refuted by a test that runs the real binary');
  assert.equal(cell(cells[FINDING_COLUMNS.indexOf('is_gating')]), 'true');
  assert.equal(cell(cells[FINDING_COLUMNS.indexOf('vendors')]), 'local');
});

test('a finding off the bridge that is nonsense writes blanks rather than throwing', () => {
  const cells = findingCells({});

  assert.equal(cells.length, FINDING_COLUMNS.length);
  assert.equal(cell(cells[FINDING_COLUMNS.indexOf('title')]), '');
  assert.equal(cell(cells[FINDING_COLUMNS.indexOf('decision')]), 'open');
});

test('a finding title that is a formula is neutralised like every other cell', () => {
  const cells = findingCells(finding({ title: '=HYPERLINK("http://x")' }));

  // The value carries quotes of its own, so the cell is WRAPPED and the apostrophe sits inside it.
  // Either shape is neutralised; what must never appear is a cell whose first character is `=`.
  const title = cell(cells[FINDING_COLUMNS.indexOf('title')]);
  assert.equal(/^"?'/.test(title), true, `the apostrophe is there, wrapped or not: ${title}`);
  assert.equal(/^"?=/.test(title), false, 'and it cannot open as a formula');
});

// ---------- the state is believed only if it is one of the three ----------

test('an unknown state is FAILED, never quietly loaded', () => {
  // A version mismatch or a typo — `load` for `loaded` — must not become a round that looks clean.
  // Fail closed. (Plan round, codex.)
  for (const state of ['load', 'LOADED', '', 'ok', undefined, null]) {
    assert.equal(
      readStateOf({ state, findings: [] } as unknown as ExportRound['found']), 'failed',
      `state ${JSON.stringify(state)} must fail closed`);
  }
});

test('a LOADED state whose findings are not an array is failed too', () => {
  // A shape nobody intended, rendered as a round that found nothing, is the same lie by another
  // route. (Plan round, local.)
  for (const findings of [undefined, null, 'two', 42, {}]) {
    assert.equal(
      readStateOf({ state: 'loaded', findings } as unknown as ExportRound['found']), 'failed',
      `findings ${JSON.stringify(findings)} must fail closed`);
  }
});

test('the three real states pass through as themselves', () => {
  assert.equal(readStateOf({ state: 'loaded', findings: [] }), 'loaded');
  assert.equal(readStateOf({ state: 'absent', findings: [] }), 'absent');
  assert.equal(readStateOf({ state: 'failed', findings: [] }), 'failed');
  assert.equal(readStateOf(undefined), 'failed', 'and nothing at all is failed');
});

test('a round carries the session it belonged to, which the table never shows', () => {
  // Round numbers restart per session, so two rounds numbered 1 on one branch are otherwise
  // indistinguishable in a file somebody keeps for a year. (Plan round, local.)
  const cells = roundCells(row({ dbKey: { sessionId: 'ba0c73a1', stage: 'CodeReview', number: 1 } }));

  assert.equal(cell(cells[ROUND_COLUMNS.indexOf('session_id')]), 'ba0c73a1');
});

test('a row with no usable dbKey leaves the session blank rather than throwing', () => {
  for (const dbKey of [undefined, null, 'nope', 42, {}]) {
    const cells = roundCells({ ...row(), dbKey });
    assert.equal(cell(cells[ROUND_COLUMNS.indexOf('session_id')]), '');
  }
});
