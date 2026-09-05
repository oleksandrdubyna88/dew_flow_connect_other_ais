import assert from 'node:assert/strict';
import { test } from 'node:test';
import { LogRow, roundsLogHtml, rowMatches, rowsFrom } from '../roundsLog';
import { RoundRecord, SessionFile } from '../rounds';

/**
 * What the operator asked of the page after seeing it: it must never be silently empty, every
 * column sorts, there is a date filter, and the spending section lives here as a second tab.
 *
 * <p>The first screenshot of the page showed the toolbar and the header row over nothing — no
 * rows, no count, no "No rounds yet". In node and in headless Chromium the same HTML rendered all
 * ninety-eight rows, so whatever failed, failed inside VS Code's webview and said nothing. A page
 * that fails must say so on the page: the error, where it happened, and what to do.</p>
 */

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
    subject: 'SCOPE — the thing',
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '' }],
    ...over,
  } as RoundRecord;
}

function session(rounds: readonly RoundRecord[]): SessionFile {
  return {
    state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
    rounds: [...rounds],
  } as unknown as SessionFile;
}

const NOW = Date.parse('2026-09-05T08:00:00.000Z');

function script(html: string): string {
  return html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
}

// ---------- a page that fails says so ----------

test('an error in the page script is written onto the page, not swallowed', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW), [], 'n');
  const js = script(html);

  assert.match(js, /window\.onerror = function/, 'an uncaught error is trapped');
  assert.match(js, /getElementById\('failed'\)/, 'and shown in a region the page reserves for it');
  assert.ok(html.includes('id="failed"'), 'the region exists in the markup');
  assert.match(js, /try \{[\s\S]*render\(\);[\s\S]*\} catch/, 'and the first render is guarded the same way');
});

// ---------- every column sorts ----------

test('every column in the header is a sort key', () => {
  const html = roundsLogHtml(rowsFrom([session([round()])], NOW), [], 'n');
  const head = html.slice(html.indexOf('<thead>'), html.indexOf('</thead>'));
  const ths = head.match(/<th\b[^>]*>/g) ?? [];

  assert.ok(ths.length >= 15, `fifteen columns, got ${ths.length}`);
  for (const th of ths) {
    assert.match(th, /data-sort="[a-zA-Z]+"/, `${th} must sort`);
  }
});

// ---------- the date filter ----------

function rowOn(day: string): LogRow {
  return rowsFrom([session([round({ startedUtc: `${day}T10:00:00.000Z`, completedUtc: `${day}T10:05:00.000Z` })])], NOW)[0]!;
}

test('a from/to date narrows by the day the round started, inclusive at both ends', () => {
  const row = rowOn('2026-09-05');

  assert.equal(rowMatches(row, { from: '2026-09-05', to: '2026-09-05' }, ''), true, 'the same day, both ends');
  assert.equal(rowMatches(row, { from: '2026-09-06' }, ''), false, 'starts tomorrow: out');
  assert.equal(rowMatches(row, { to: '2026-09-04' }, ''), false, 'ends yesterday: out');
  assert.equal(rowMatches(row, { from: '2026-09-01', to: '2026-09-30' }, ''), true);
  assert.equal(rowMatches(row, { from: '', to: '' }, ''), true, 'blank bounds are no bounds');
});

test('a round with no date at all is kept by an open range and dropped by a bounded one', () => {
  const undated = { ...rowOn('2026-09-05'), startedUtc: '', completedUtc: '' };

  assert.equal(rowMatches(undated, {}, ''), true);
  assert.equal(rowMatches(undated, { from: '2026-09-01' }, ''), false, 'it cannot be shown to be inside');
});

test('the toolbar offers the date range and a Today shortcut', () => {
  const html = roundsLogHtml([], [], 'n');

  assert.ok(html.includes('id="from"') && html.includes('type="datetime-local"'), 'from, with a time');
  assert.ok(html.includes('id="to"'), 'to');
  assert.ok(html.includes('id="today"'), 'today');
});

// ---------- the spending tab ----------

test('the page has two tabs, rounds and usage, and rounds is the one shown first', () => {
  const html = roundsLogHtml([], [], 'n', '<div class="usage-rows">SPENDING</div>');

  assert.ok(html.includes('data-tab="rounds"') && html.includes('data-tab="usage"'), 'both tabs');
  assert.ok(html.includes('id="tab-usage"'), 'the usage tab has a body');
  assert.ok(html.includes('SPENDING'), 'the spending region the provider rendered is placed in it');
  assert.match(script(html), /message\.type === 'usage'/, 'and it is re-posted live like the rows');
});

function pageScript(html: string): string {
  return html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
}

test('the page opens on today, and All dates clears the range', () => {
  const html = roundsLogHtml([], [], 'n');

  assert.match(pageScript(html), /function setToday/, 'the range is set on load');
  assert.ok(html.includes('id="alldates"'), 'with a way back to everything');
  assert.match(pageScript(html), /setToday\(\);/, 'and today is what the first render applies');
});

test('a bare day as the upper bound means the END of that day', () => {
  // "To 2026-09-05" against a round at 14:30 that day would otherwise exclude the whole of today.
  const row = rowsFrom([session([round({ startedUtc: '2026-09-05T14:30:00.000Z', completedUtc: '2026-09-05T14:35:00.000Z' })])], NOW)[0]!;

  assert.equal(rowMatches(row, { to: '2026-09-05' }, ''), true);
  assert.equal(rowMatches(row, { to: '2026-09-04' }, ''), false);
});

test('an instant bound compares against the instant the round started', () => {
  const row = rowsFrom([session([round({ startedUtc: '2026-09-05T14:30:00.000Z', completedUtc: '2026-09-05T14:35:00.000Z' })])], NOW)[0]!;

  assert.equal(rowMatches(row, { from: '2026-09-05T14:00:00.000Z' }, ''), true);
  assert.equal(rowMatches(row, { from: '2026-09-05T15:00:00.000Z' }, ''), false, 'it started before that');
  assert.equal(rowMatches(row, { to: '2026-09-05T14:31:00.000Z' }, ''), true);
});
