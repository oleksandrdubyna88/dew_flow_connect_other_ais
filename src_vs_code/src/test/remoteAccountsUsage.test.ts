import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { AgyUsage, ClaudeUsage, CodexUsage, readAccounts } from '../remoteAccountsReply';
import { AGY_QUOTA_LINES, GOOD, LINES, NOW, agyLines, claudeLines, claudeOnlyOf, codexLines, frame, ok, readOf } from './accountsFrames';

/**
 * The value rules of the accounts parser (E1.S2, spec § A2.11.3): the Claude usage lines with their reset text, and the Codex
 * windows (the quota of agy too). Every case is a frame handed to the REAL `readAccounts`, so a rule that is not wired in fails here.
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

const windowsOfCodex = (...data: readonly string[]): readonly { minutes: number; usedPercent: number | null; resetsAtMs: number | null }[] => {
  const usage = codexUsage(...data);
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
    assert.deepEqual(codexUsage(one), { state: 'failed', why: 'none' }, one);
    assert.deepEqual(windowsOfCodex(one, ':window 5 300 9').map((window) => window.minutes), [300], `${one}: left out beside a good one`);
  }
});

test('A2-T19: two windows of one length — the first is kept', () => {
  const kept = windowsOfCodex(':window 5 300 1', ':window 90 300 2');
  assert.equal(kept.length, 1);
  assert.equal(kept[0]?.usedPercent, 5);
});

test('A2-T19: the slots primary and secondary are not in the grammar', () => {
  const base = ['COAI-JQ found', 'COAI-VENDOR claude absent', 'COAI-VENDOR codex found', ':sign-in-file present', 'COAI-USAGE codex read', 'COAI-VENDOR agy absent'];
  const withKey = [...base.slice(0, 5), ':primary 1 2 3', ...base.slice(5)];
  const withMarker = [...base.slice(0, 5), 'COAI-USAGE secondary read', ...base.slice(5)];
  const reply = (lines: readonly string[]): string => ['COAI-ACCOUNTS 1', ...lines, 'COAI-ACCOUNTS-END', ''].join('\n');
  assert.deepEqual(readAccounts(ok(reply(withKey)), 'no-claude-usage', NOW), { kind: 'unreadable', why: 'key' });
  assert.deepEqual(readAccounts(ok(reply(withMarker)), 'no-claude-usage', NOW), { kind: 'unreadable', why: 'marker' });
});

test('A2-T19 companion: the reply of § A2.5 is read — one window, and the account and the plan of what Codex said just now', () => {
  const codex = readOf(readAccounts(ok(GOOD), 'no-claude-usage', NOW)).codex;
  assert.ok(codex.state === 'found' && codex.usage.state === 'read');
  const usage = codex.state === 'found' && codex.usage.state === 'read' ? codex.usage : undefined;
  assert.equal(usage?.windows.length, 1);
  assert.ok(codex.state === 'found' && codex.email === 'cox@example.test' && codex.plan === 'prolite');
  assert.deepEqual(Object.keys(usage ?? {}).sort(), ['state', 'windows'], 'a Codex reading has no time of a file: it is from now');
});

// ───────────────── the Antigravity quota (revision 3) ─────────────────

function agyUsage(...data: readonly string[]): AgyUsage {
  const agy = readOf(readAccounts(ok(agyLines(...data)), 'no-claude-usage', NOW)).agy;
  assert.equal(agy.state, 'found', `the agy side is ${JSON.stringify(agy)}`);

  return agy.state === 'found' ? agy.usage : { state: 'failed', why: 'error' };
}

test('R3-T1: the measured quota — two groups of two windows, a dash is a window that has not started', () => {
  assert.deepEqual(agyUsage(...AGY_QUOTA_LINES), {
    state: 'read',
    windows: [
      { group: 'gemini', window: 'weekly', leftPercent: 60, resetsAtMs: 1795506120000 },
      { group: 'gemini', window: '5h', leftPercent: 92, resetsAtMs: 1795100400000 },
      { group: 'third-party', window: 'weekly', leftPercent: 100, resetsAtMs: null },
      { group: 'third-party', window: '5h', leftPercent: 100, resetsAtMs: null },
    ],
  });
});

test('R3-T1: a window that is not in the closed lists is no window; no window at all is the failure format', () => {
  for (const line of [':quota other weekly 50 1', ':quota gemini daily 50 1', ':quota Gemini 5h 50 1', ':quota third_party 5h 50 1']) {
    assert.deepEqual(agyUsage(line), { state: 'failed', why: 'format' }, line);
    assert.deepEqual((agyUsage(line, ':quota gemini 5h 50 9') as { windows: readonly unknown[] }).windows.length, 1, `${line}: left out beside a good one`);
  }
  assert.deepEqual(agyUsage(), { state: 'failed', why: 'format' });
});

test('R3-T1: 101 is not known, a zero is a zero, a decimal is as sent, two lines for one window keep the first', () => {
  const windows = (...data: readonly string[]): readonly { leftPercent: number | null; resetsAtMs: number | null }[] => (agyUsage(...data) as { windows: readonly { leftPercent: number | null; resetsAtMs: number | null }[] }).windows;
  assert.equal(windows(':quota gemini 5h 101 5')[0]?.leftPercent, null);
  assert.equal(windows(':quota gemini 5h 0 5')[0]?.leftPercent, 0);
  assert.equal(windows(':quota gemini 5h 33.5 5')[0]?.leftPercent, 33.5);
  assert.equal(windows(':quota gemini 5h 10 5', ':quota gemini 5h 90 6').length, 1);
  assert.equal(windows(':quota gemini 5h 10 5', ':quota gemini 5h 90 6')[0]?.leftPercent, 10);
  assert.equal(windows(':quota gemini 5h 10 -')[0]?.resetsAtMs, null);
});

test('R3-T1: the usage words of agy that are not figures; no-jq is only legal beside COAI-JQ missing', () => {
  const reply = (jq: string, word: string): string => frame(jq, 'COAI-VENDOR claude absent', 'COAI-VENDOR codex absent', 'COAI-VENDOR agy found', ':sign-in-file present', `COAI-USAGE agy ${word}`);
  const agyOf = (jq: string, word: string): unknown => {
    const answer = readAccounts(ok(reply(jq, word)), 'no-claude-usage', NOW);

    return answer.kind === 'read' ? answer.agy : answer;
  };
  const side = (usage: AgyUsage): unknown => ({ state: 'found', version: null, signInFile: true, email: '', usage });
  assert.deepEqual(agyOf('COAI-JQ found', 'skipped'), side({ state: 'skipped' }));
  assert.deepEqual(agyOf('COAI-JQ found', 'failed timeout'), side({ state: 'failed', why: 'timeout' }));
  assert.deepEqual(agyOf('COAI-JQ found', 'failed error'), side({ state: 'failed', why: 'error' }));
  assert.deepEqual(agyOf('COAI-JQ found', 'failed model-call'), side({ state: 'failed', why: 'model-call' }));
  assert.deepEqual(agyOf('COAI-JQ missing apt-get', 'failed no-jq'), side({ state: 'failed', why: 'no-jq' }));
  assert.deepEqual(agyOf('COAI-JQ found', 'failed no-jq'), { kind: 'unreadable', why: 'shape' }, 'no-jq beside COAI-JQ found');
  assert.deepEqual(agyOf('COAI-JQ missing apt-get', 'skipped'), { kind: 'unreadable', why: 'shape' }, 'a quota that was asked for beside COAI-JQ missing');
  assert.deepEqual(agyOf('COAI-JQ found', 'failed format'), { kind: 'unreadable', why: 'marker' }, 'format is the parser\'s word, the script never sends it');
  assert.deepEqual(agyOf('COAI-JQ found', 'none'), { kind: 'unreadable', why: 'marker' }, 'the old word is gone');
});
