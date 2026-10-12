import assert from 'node:assert/strict';
import { test } from 'node:test';

import { serverGauges, teamGaugeBlock, type GaugeBlockView } from '../teamGauge';
import type { CatalogVendor, GaugeReading } from '../teamServerApi';
import { pageTree } from './pageTree';

/**
 * The Team server tab's gauge block (story 6.2 of todo/PLAN_every_round_is_counted.md): per vendor whose account the
 * server read, the vendor's own windows from the CATALOG, and beside them each window's rounds per 1 % from the USAGE
 * answer — the figure, or why there is none — and, when a price is typed and the figure is shown, the plan's share.
 * Both answers are a stranger's: every field is read by hand, and absent is never zero.
 */

const NOW = Date.parse('2026-10-12T12:00:00Z');

const READING: GaugeReading = {
  observedUtc: '2026-10-12T11:48:00+00:00', planType: 'prolite', rateLimitReachedType: '', ordinaryUsageAllowed: true,
  resetCreditsAvailable: null, stale: false,
  windows: [{ limitId: 'codex', kind: 'primary', usedPercent: 37, windowDurationMins: 10080, resetsAtUtc: '2026-10-15T07:00:00Z' }],
};

function vendor(id: string, gauge?: GaugeReading): CatalogVendor {
  return { id, runtime: id, models: [], slots: { total: 1, ready: 1, coolingDown: 0, needsSignIn: 0, ...(gauge === undefined ? {} : { gauge }) } };
}

const HIDDEN = {
  vendor: 'codex', limitId: 'codex', windowMins: 10080, roundsPerPercent: null, intervals: 0,
  unavailable: 'consumer-not-counted', missingConsumer: 'direct-codex-use',
};

function view(over: Partial<GaugeBlockView> = {}): GaugeBlockView {
  return {
    serverId: 'ts', vendors: [vendor('codex', READING), vendor('antigravity')], usage: { gauges: [HIDDEN] }, prices: {},
    now: NOW, palette: () => '#123', timeZone: 'UTC', ...over,
  };
}

test('a newer server\'s figures are read field by field; an older server\'s absence is undefined, never an empty list', () => {
  assert.deepEqual(serverGauges({ gauges: [HIDDEN] }), [HIDDEN]);
  assert.equal(serverGauges({ vendors: [] }), undefined, 'a server older than the field said nothing about figures');
  assert.deepEqual(serverGauges({ gauges: [] }), []);
  assert.deepEqual(serverGauges({ gauges: [{ ...HIDDEN, roundsPerPercent: 'lots' }, { vendor: 1 }, null] }), [],
    'an entry that is not the contract is dropped, never guessed into a number');
  assert.equal(serverGauges({ gauges: [{ ...HIDDEN, roundsPerPercent: 3 }] })?.[0]?.roundsPerPercent, 3);
});

test('the block draws each read window and why its figure is hidden — and nothing for a vendor with no gauge', () => {
  const tree = pageTree(teamGaugeBlock(view()));
  const text = tree.text();

  assert.match(text, /codex \(weekly\): 37 % used · resets Thu 07:00 · seen 12 min ago/);
  assert.match(text, /rounds per 1 % of codex \(weekly\): hidden — direct codex use outside coai spends the same limit and is not counted/);
  assert.doesNotMatch(text, /antigravity/, 'a vendor whose account reports no gauge has no line — absent, not 0 %');
});

test('the share is drawn beside a SHOWN weekly figure when a price is typed — and hidden with the figure', () => {
  const shown = { ...HIDDEN, vendor: 'codex', roundsPerPercent: 4, intervals: 5, unavailable: '', missingConsumer: '' };

  const priced = pageTree(teamGaugeBlock(view({ usage: { gauges: [shown] }, prices: { 'ts/codex': 200 } }))).text();
  assert.match(priced, /≈ \$0\.11 of your plan per round — a share of the \$200\/month plan, not a bill/);

  const hidden = pageTree(teamGaugeBlock(view({ prices: { 'ts/codex': 200 } }))).text();
  assert.doesNotMatch(hidden, /of your plan per round/, 'a share drawn beside a hidden figure');
});

test('an older server is said as such where the figures would be — not as a figure of none', () => {
  const text = pageTree(teamGaugeBlock(view({ usage: { vendors: [] } }))).text();

  assert.match(text, /codex \(weekly\): 37 % used/, 'the gauge itself comes from the catalog and is still drawn');
  assert.match(text, /rounds per 1 %: this Team server does not derive it yet/);
});

test('no vendor read at all: one quiet sentence, no window', () => {
  const text = pageTree(teamGaugeBlock(view({ vendors: [vendor('codex'), vendor('antigravity')] }))).text();

  assert.match(text, /No vendor's own gauge has been read on this server yet/);
  assert.doesNotMatch(text, /%/);
});

test('every word from the wire is escaped', () => {
  const html = teamGaugeBlock(view({ vendors: [vendor('<b>x</b>', { ...READING, windows: [{ ...READING.windows[0]!, limitId: '<i>y</i>' }] })] }));

  assert.doesNotMatch(html, /<b>x<\/b>|<i>y<\/i>/);
});
