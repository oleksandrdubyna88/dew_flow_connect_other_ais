import assert from 'node:assert/strict';
import { test } from 'node:test';
import { barWidth, money, parseUsage, shortDuration, shortNumber, totalsByVendor, UsageEntry, within } from '../usage';

const NOW = new Date('2026-09-01T12:00:00Z');

function entry(over: Partial<UsageEntry> = {}): UsageEntry {
  return {
    utc: '2026-09-01T11:00:00Z',
    provider: 'codex',
    model: 'gpt-5.6-terra',
    role: 'Architecture',
    stage: 'CodeReview',
    seconds: 40,
    tokensIn: 1000,
    tokensOut: 100,
    costUsd: null,
    outcome: 'ok',
    ...over,
  };
}

test('a torn or foreign line is skipped, never fatal to the chart', () => {
  const parsed = parseUsage(
    ['{"utc":"2026-09-01T11:00:00Z","provider":"codex","tokensIn":5,"tokensOut":1,"seconds":2,"outcome":"ok"}', '{"half', '', 'not json at all'].join(
      '\n',
    ),
  );
  assert.equal(parsed.length, 1);
  assert.equal(parsed[0]!.provider, 'codex');
});

test('a missing cost reads as unknown, not as zero', () => {
  const parsed = parseUsage('{"utc":"2026-09-01T11:00:00Z","provider":"codex"}');
  assert.equal(parsed[0]!.costUsd, null);
  assert.equal(money(parsed[0]!.costUsd), '—');
});

test('today is since midnight; the longer windows count back from now', () => {
  // Overruled on 2026-09-05: "what did today cost" is about the calendar day. A review at 13:00
  // yesterday is yesterday, however few hours ago that was. Week and month stay rolling.
  const entries = [
    entry({ utc: '2026-09-01T11:00:00Z' }), // an hour ago, today
    entry({ utc: '2026-08-31T13:00:00Z' }), // 23 hours ago — yesterday
    entry({ utc: '2026-08-20T12:00:00Z' }), // twelve days ago
  ];

  assert.equal(within(entries, 'day', NOW).length, 1, 'yesterday is not today');
  assert.equal(within(entries, 'week', NOW).length, 2);
  assert.equal(within(entries, 'month', NOW).length, 3);
});

test('totals are per vendor, and a failed run still counts as spending', () => {
  const rows = totalsByVendor([
    entry({ provider: 'codex', tokensIn: 1000, tokensOut: 100, seconds: 40 }),
    entry({ provider: 'codex', tokensIn: 500, tokensOut: 50, seconds: 20, outcome: 'timeout' }),
    entry({ provider: 'antigravity', tokensIn: 9000, tokensOut: 900, seconds: 150, costUsd: 0.5 }),
  ]);

  const codex = rows.find((r) => r.provider === 'codex')!;
  assert.equal(codex.runs, 2);
  assert.equal(codex.failed, 1, 'a reviewer that did not answer still burned tokens');
  assert.equal(codex.tokensIn, 1500);
  assert.equal(codex.seconds, 60);
  assert.equal(codex.averageSeconds, 30);
  assert.equal(codex.costUsd, null, 'codex prices nothing, so its row shows no money');

  assert.equal(rows[0]!.provider, 'antigravity', 'the busiest vendor leads the list');
  assert.equal(rows.find((r) => r.provider === 'antigravity')!.costUsd, 0.5);
});

test('a vendor that priced only some runs sums only those', () => {
  const rows = totalsByVendor([
    entry({ provider: 'claude', costUsd: 0.25 }),
    entry({ provider: 'claude', costUsd: null }),
  ]);
  assert.equal(rows[0]!.costUsd, 0.25);
});

test('numbers and durations shorten the way a sidebar needs', () => {
  assert.equal(shortNumber(950), '950');
  assert.equal(shortNumber(12_345), '12.3k');
  assert.equal(shortNumber(2_400_000), '2.4M');
  assert.equal(shortDuration(45), '45 s');
  assert.equal(shortDuration(150), '2.5 min');
  assert.equal(shortDuration(7200), '2.0 h');
});

test('a bar is never invisible and never wider than the busiest row', () => {
  assert.equal(barWidth(100, 100), 100);
  assert.equal(barWidth(1, 10_000), 2, 'a tiny row must still be findable');
  assert.equal(barWidth(5, 0), 0);
});
