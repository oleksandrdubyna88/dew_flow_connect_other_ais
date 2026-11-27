import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { ClaudeUsage, CodexUsage, readAccounts } from '../remoteAccountsReply';
import { FILE_TIME, GOOD, LINES, NOW, RECORD_TIME, claudeLines, claudeOnlyOf, codexLines, ok, readOf } from './accountsFrames';

/**
 * The value rules of the accounts parser (E1.S2, spec § A2.11.3): the Claude usage lines with their reset text, and the Codex
 * record's windows and times. Every case is a frame handed to the REAL `readAccounts`, so a rule that is not wired in fails here.
 * Nothing reads the clock: `at` passes the moment.
 */

const DOT = '·';
const iso = (ms: number | null): string | null => (ms === null ? null : new Date(ms).toISOString().slice(0, 16) + 'Z');

function claudeUsage(texts: readonly string[], at = NOW, turns = 0, unmatched = 0): ClaudeUsage {
  const claude = claudeOnlyOf(readAccounts(ok(claudeLines(texts, turns, unmatched)), 'claude-usage', at)).claude;
  assert.equal(claude.state, 'found', `the Claude side is ${JSON.stringify(claude)}`);

  return claude.state === 'found' ? claude.usage : { state: 'skipped' };
}

interface Seen { readonly name: unknown; readonly used: number | null; readonly resets: string | null }

function windowsOf(texts: readonly string[], at = NOW): readonly Seen[] {
  const usage = claudeUsage(texts, at);
  assert.equal(usage.state, 'read', `the usage is ${JSON.stringify(usage)}`);

  return usage.state === 'read' ? usage.windows.map((one) => ({ name: one.name, used: one.usedPercent, resets: iso(one.resetsAtMs) })) : [];
}

const session = (used: string, reset: string, zone = 'UTC'): string => `Current session: ${used}% used ${DOT} resets ${reset} (${zone})`;

test('A2-T18: the three measured lines', () => {
  assert.deepEqual(windowsOf(LINES), [
    { name: '5h', used: 56, resets: '2026-11-19T15:00Z' },
    { name: 'week', used: 77, resets: '2026-11-22T03:00Z' },
    { name: { model: 'Fable' }, used: 49, resets: '2026-11-22T02:59Z' },
  ]);
});

test('A2-T18 companion: the measured lines with turns 0 are read, three windows, unmatched 0', () => {
  const usage = claudeUsage(LINES);
  assert.equal(usage.state, 'read');
  assert.equal(usage.state === 'read' && usage.windows.length, 3);
  assert.equal(usage.state === 'read' && usage.unmatched, 0);
});

test('A2-T18: the hour — 3pm, 3am, 2:59am, 12am, 12pm', () => {
  for (const [text, expected] of [['3pm', '15:00'], ['3am', '03:00'], ['2:59am', '02:59'], ['12am', '00:00'], ['12pm', '12:00'], ['12:30pm', '12:30'], ['11pm', '23:00']] as const) {
    assert.deepEqual(windowsOf([session('10', `Nov 20, ${text}`)]), [{ name: '5h', used: 10, resets: `2026-11-20T${expected}Z` }], text);
  }
});

test('A2-T18: the year is the UTC year of the clock, and the next one when the moment is past', () => {
  const dec = Date.UTC(2026, 11, 30, 12, 0);
  assert.equal(windowsOf([session('10', 'Jan 1, 3am')], dec)[0]?.resets, '2027-01-01T03:00Z', 'read on 30 December, 1 January is next year');
  const leap = Date.UTC(2028, 1, 27, 12, 0);
  assert.equal(windowsOf([session('10', 'Feb 29, 3am')], leap)[0]?.resets, '2028-02-29T03:00Z', 'a leap day in a leap year');
  const plain = Date.UTC(2026, 1, 27, 12, 0);
  assert.equal(windowsOf([session('10', 'Feb 29, 3am')], plain)[0]?.resets, null, 'no 29 February in 2026');
});

test('A2-T18: the range — from one hour ago to eight days ahead', () => {
  assert.equal(windowsOf([session('10', 'Nov 19, 11:30am')])[0]?.resets, '2026-11-19T11:30Z', '30 minutes ago');
  assert.equal(windowsOf([session('10', 'Nov 19, 10am')])[0]?.resets, null, '2 hours ago: the next year is a year ahead');
  assert.equal(windowsOf([session('10', 'Nov 27, 12pm')])[0]?.resets, '2026-11-27T12:00Z', 'eight days ahead');
  assert.equal(windowsOf([session('10', 'Nov 28, 3am')])[0]?.resets, null, 'more than eight days ahead');
});

test('A2-T18: the zone must be exactly UTC', () => {
  for (const zone of ['PST', 'Europe/Berlin', 'utc']) {
    assert.deepEqual(windowsOf([session('42', 'Nov 20, 3pm', zone)]), [{ name: '5h', used: 42, resets: null }], zone);
  }
});

test('A2-T18: the form of the reset text — and in every dash row the percent is still the line\'s', () => {
  for (const reset of ['Feb 30, 3am', 'Nov 19, 13pm', 'Nov 19, 0am', 'Nov 19, 3:60pm', '19 Nov, 3pm', 'Nov 19, 2027, 3pm']) {
    assert.deepEqual(windowsOf([session('42', reset)]), [{ name: '5h', used: 42, resets: null }], reset);
  }
});

test('A2-T18: the percent — a real zero stays a zero, and 101 is not known', () => {
  assert.equal(windowsOf([session('100', 'Nov 20, 3pm')])[0]?.used, 100);
  assert.equal(windowsOf([session('0', 'Nov 20, 3pm')])[0]?.used, 0, 'a zero that was sent is a zero');
  assert.equal(windowsOf([session('49.5', 'Nov 20, 3pm')])[0]?.used, 49.5);
  assert.deepEqual(windowsOf([session('101', 'Nov 20, 3pm')]), [{ name: '5h', used: null, resets: '2026-11-20T15:00Z' }], '101 is not known, and the window is kept');
});

test('A2-T18: a line that fits no form is no window and adds 1 to unmatched', () => {
  const good = session('10', 'Nov 20, 3pm');
  const bad = [
    `Current session: 56% used ${DOT} resets Nov 20, 3pm`,
    `Current session: 56% used`,
    session('1000', 'Nov 20, 3pm'),
    session('-1', 'Nov 20, 3pm'),
    session('1e3', 'Nov 20, 3pm'),
    session('NaN', 'Nov 20, 3pm'),
    `${session('56', 'Nov 20, 3pm')} extra`,
    'something else',
  ];
  for (const one of bad) {
    const usage = claudeUsage([good, one]);
    assert.equal(usage.state, 'read', one);
    assert.equal(usage.state === 'read' && usage.windows.length, 1, `${one}: no window`);
    assert.equal(usage.state === 'read' && usage.unmatched, 1, `${one}: unmatched`);
  }
});

test('A2-T18: the unmatched count of the script and of the parser add up', () => {
  const usage = claudeUsage([session('10', 'Nov 20, 3pm'), 'junk'], NOW, 0, 3);
  assert.equal(usage.state === 'read' && usage.unmatched, 4);
});

test('A2-T18: the windows — none for a missing session line, two models in order, a window twice keeps the first', () => {
  assert.deepEqual(windowsOf([LINES[1] as string]).map((one) => one.name), ['week']);
  const two = [`Current week (Fable): 10% used ${DOT} resets Nov 20, 3pm (UTC)`, `Current week (Opus): 20% used ${DOT} resets Nov 20, 3pm (UTC)`];
  assert.deepEqual(windowsOf(two).map((one) => one.name), [{ model: 'Fable' }, { model: 'Opus' }]);
  const twice = claudeUsage([session('10', 'Nov 20, 3pm'), session('99', 'Nov 20, 4pm')]);
  assert.equal(twice.state === 'read' && twice.windows[0]?.usedPercent, 10, 'the first one is kept');
  assert.equal(twice.state === 'read' && twice.unmatched, 1);
});

test('A2-T18: no matched line is failed format, and turns 1 is failed model-call whatever lines came', () => {
  assert.deepEqual(claudeUsage([], NOW, 0, 2), { state: 'failed', why: 'format' });
  assert.deepEqual(claudeUsage(['junk'], NOW, 0, 0), { state: 'failed', why: 'format' });
  assert.deepEqual(claudeUsage(LINES, NOW, 1), { state: 'failed', why: 'model-call' });
  assert.deepEqual(claudeUsage([], NOW, 3), { state: 'failed', why: 'model-call' });
});

// ───────────────── A2-T19 (the parser's half): the Codex record ─────────────────

function codexUsage(...data: readonly string[]): CodexUsage {
  const codex = readOf(readAccounts(ok(codexLines(...data)), 'no-claude-usage', NOW)).codex;
  assert.equal(codex.state, 'found', `the Codex side is ${JSON.stringify(codex)}`);

  return codex.state === 'found' ? codex.usage : { state: 'failed', why: 'error' };
}

const AS_OF = `:as-of ${RECORD_TIME} ${FILE_TIME}`;
const windowsOfCodex = (...data: readonly string[]): readonly { minutes: number; usedPercent: number | null; resetsAtMs: number | null }[] => {
  const usage = codexUsage(...data, AS_OF);
  assert.equal(usage.state, 'read', JSON.stringify(usage));

  return usage.state === 'read' ? usage.windows : [];
};

test('A2-T19: primary weekly and secondary null — one window by its length, with the reset in milliseconds', () => {
  assert.deepEqual(windowsOfCodex(':window 14 10080 1795518850'), [{ minutes: 10080, usedPercent: 14, resetsAtMs: 1795518850000 }]);
});

test('A2-T19: both windows are kept in the order sent, and the same two when the lines are swapped', () => {
  const week = ':window 14 10080 1795518850';
  const hours = ':window 3.5 300 1795500000';
  const ab = windowsOfCodex(week, hours);
  const ba = windowsOfCodex(hours, week);
  assert.deepEqual(ab.map((one) => one.minutes), [10080, 300]);
  assert.deepEqual(ba.map((one) => one.minutes), [300, 10080]);
  assert.deepEqual([...ab].sort((x, y) => x.minutes - y.minutes), [...ba].sort((x, y) => x.minutes - y.minutes));
  assert.equal(ab[1]?.usedPercent, 3.5);
});

test('A2-T19: another length is as sent, and a reset that has passed is as sent (the sentence is the list\'s)', () => {
  assert.equal(windowsOfCodex(':window 5 120 1795500000')[0]?.minutes, 120);
  assert.equal(windowsOfCodex(':window 5 300 1700000000')[0]?.resetsAtMs, 1700000000000);
});

test('A2-T19: a dash is not zero, and 101 is not known', () => {
  assert.deepEqual(windowsOfCodex(':window - 10080 -'), [{ minutes: 10080, usedPercent: null, resetsAtMs: null }]);
  assert.deepEqual(windowsOfCodex(':window 101 300 1'), [{ minutes: 300, usedPercent: null, resetsAtMs: 1000 }]);
  assert.equal(windowsOfCodex(':window 0 300 5')[0]?.usedPercent, 0, 'a zero that was sent is a zero');
});

test('A2-T19: a window with no usable length is no window, and with no window left the usage is failed none', () => {
  for (const one of [':window 14 - 1', ':window 14 0 1', ':window 14 600000 1']) {
    assert.deepEqual(codexUsage(one, AS_OF), { state: 'failed', why: 'none' }, one);
    assert.deepEqual(windowsOfCodex(one, ':window 5 300 9').map((window) => window.minutes), [300], `${one}: left out beside a good one`);
  }
});

test('A2-T19: two windows of one length — the first is kept', () => {
  const kept = windowsOfCodex(':window 5 300 1', ':window 90 300 2');
  assert.equal(kept.length, 1);
  assert.equal(kept[0]?.usedPercent, 5);
});

interface Seen2 { readonly asOf: { readonly ms: number; readonly from: string } | null; readonly statMissing: boolean }
function asOfOf(words: string): Seen2 {
  const usage = codexUsage(':window 14 10080 1795518850', `:as-of ${words}`);
  assert.equal(usage.state, 'read', JSON.stringify(usage));

  return usage.state === 'read' ? { asOf: usage.asOf, statMissing: usage.statMissing } : { asOf: null, statMissing: false };
}

test('A2-T19: the time — the record\'s own when it parses, else the file\'s', () => {
  assert.deepEqual(asOfOf(`${RECORD_TIME} ${FILE_TIME}`), { asOf: { ms: Date.UTC(2026, 10, 17, 19, 32, 36, 474), from: 'record' }, statMissing: false });
  assert.deepEqual(asOfOf(`2026-11-17T20:32:36+01:00 ${FILE_TIME}`).asOf, { ms: Date.UTC(2026, 10, 17, 19, 32, 36), from: 'record' }, 'with an offset it is the same moment');
  const file = { ms: FILE_TIME * 1000, from: 'file' };
  for (const words of [`- ${FILE_TIME}`, `not-a-time ${FILE_TIME}`, `2026-11-17 19:32 ${FILE_TIME}`, `2026-11-17T19:32:36 ${FILE_TIME}`, `2099-01-01T00:00:00Z ${FILE_TIME}`, `2019-12-31T23:59:59Z ${FILE_TIME}`]) {
    assert.deepEqual(asOfOf(words).asOf, file, words);
  }
  assert.deepEqual(asOfOf('- -'), { asOf: null, statMissing: false });
});

test('A2-T19: a host without stat is said, and is not a time that is not known', () => {
  assert.deepEqual(asOfOf(`${RECORD_TIME} no-stat`), { asOf: { ms: Date.UTC(2026, 10, 17, 19, 32, 36, 474), from: 'record' }, statMissing: true });
  assert.deepEqual(asOfOf('- no-stat'), { asOf: null, statMissing: true });
  assert.notDeepEqual(asOfOf('- no-stat'), asOfOf('- -'));
});

test('A2-T19: the slots primary and secondary are not in the grammar', () => {
  const base = ['COAI-JQ found', 'COAI-VENDOR claude absent', 'COAI-VENDOR codex found', ':sign-in-file present', 'COAI-USAGE codex read', AS_OF, 'COAI-VENDOR agy absent'];
  const withKey = [...base.slice(0, 5), ':primary 1 2 3', ...base.slice(5)];
  const withMarker = [...base.slice(0, 5), 'COAI-USAGE secondary read', ...base.slice(5)];
  const reply = (lines: readonly string[]): string => ['COAI-ACCOUNTS 1', ...lines, 'COAI-ACCOUNTS-END', ''].join('\n');
  assert.deepEqual(readAccounts(ok(reply(withKey)), 'no-claude-usage', NOW), { kind: 'unreadable', why: 'key' });
  assert.deepEqual(readAccounts(ok(reply(withMarker)), 'no-claude-usage', NOW), { kind: 'unreadable', why: 'marker' });
});

test('A2-T19 companion: the reply of § A2.5 is read, one window, the time from the record', () => {
  const codex = readOf(readAccounts(ok(GOOD), 'no-claude-usage', NOW)).codex;
  assert.ok(codex.state === 'found' && codex.usage.state === 'read');
  const usage = codex.state === 'found' && codex.usage.state === 'read' ? codex.usage : undefined;
  assert.equal(usage?.windows.length, 1);
  assert.equal(usage?.asOf?.from, 'record');
});

test('A2-T19: asOf is null — and the iso helper says so — when neither time can be read', () => {
  assert.equal(iso(null), null);
  assert.equal(iso(Date.UTC(2026, 10, 17, 19, 32)), '2026-11-17T19:32Z');
});
