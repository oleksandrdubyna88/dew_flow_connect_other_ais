import assert from 'node:assert/strict';
import { test } from 'node:test';

import { type GaugeRatio, gaugeLines, ratioLine, windowName } from '../gaugeWords';
import type { GaugeReading } from '../teamServerApi';

/**
 * The vendor's own gauge, as a person reads it (story 6.2 of todo/PLAN_every_round_is_counted.md): one line per reported
 * window — `codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago` — and the window's rounds per 1 %, or why
 * there is none. Reset first: a passed reset with no newer reading says so and never infers that the quota is back.
 */

const NOW = Date.parse('2026-10-12T12:00:00Z');

function reading(over: Partial<GaugeReading> = {}): GaugeReading {
  return {
    observedUtc: '2026-10-12T11:48:00+00:00',
    planType: 'prolite',
    rateLimitReachedType: '',
    ordinaryUsageAllowed: true,
    resetCreditsAvailable: null,
    windows: [{ limitId: 'codex', kind: 'primary', usedPercent: 37, windowDurationMins: 10080, resetsAtUtc: '2026-10-15T07:00:00.0000000Z' }],
    stale: false,
    ...over,
  };
}

const ratio = (over: Partial<GaugeRatio> = {}): GaugeRatio => ({
  vendor: 'acme', limitId: 'acme', windowMins: 10080, roundsPerPercent: null, intervals: 0, unavailable: '', missingConsumer: '', ...over,
});

test('a window reads its use, its reset and how old the reading is', () => {
  assert.deepEqual(gaugeLines(reading(), NOW, 'UTC'), ['codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago']);
});

test('a window is named by its limit and its length, never by where the vendor filed it', () => {
  assert.equal(windowName({ limitId: 'codex', windowDurationMins: 300 }), 'codex (5 h)');
  assert.equal(windowName({ limitId: 'codex', windowDurationMins: 10080 }), 'codex (weekly)');
  assert.equal(windowName({ limitId: 'codex', windowDurationMins: 1440 }), 'codex (24 h)');
  assert.equal(windowName({ limitId: 'codex', windowDurationMins: null }), 'codex (a window of unstated length)');
});

test('a passed reset with no newer reading says availability is unverified — never that the quota is back', () => {
  const lines = gaugeLines(reading({ observedUtc: '2026-10-15T06:00:00+00:00' }), Date.parse('2026-10-15T09:00:00Z'), 'UTC');

  assert.deepEqual(lines, ['codex (weekly): reset time passed; availability unverified · 37 % used when last read · seen 3 h ago']);
  assert.doesNotMatch(lines[0]!, /\b0 %|available again|resets Thu/, 'recovery inferred from the clock');
});

test('a stale reading says so beside its age', () => {
  assert.deepEqual(gaugeLines(reading({ stale: true }), NOW, 'UTC'),
    ['codex (weekly): 37 % used · resets Thu 07:00 · seen 12 min ago · stale — a later read failed']);
});

test('only the windows the vendor reported are drawn — an absent window is absent, never 0 %', () => {
  const both = reading({
    windows: [
      { limitId: 'codex', kind: 'primary', usedPercent: 12.5, windowDurationMins: 300, resetsAtUtc: '2026-10-12T14:00:00Z' },
      { limitId: 'codex', kind: 'secondary', usedPercent: 37, windowDurationMins: 10080, resetsAtUtc: '' },
    ],
  });

  assert.deepEqual(gaugeLines(both, NOW, 'UTC'), [
    'codex (5 h): 12.5 % used · resets Mon 14:00 · seen 12 min ago',
    'codex (weekly): 37 % used · reset time not stated · seen 12 min ago',
  ]);
  assert.deepEqual(gaugeLines(undefined, NOW, 'UTC'), [], 'no reading, no line — and no 0 %');
  assert.deepEqual(gaugeLines(reading({ windows: [] }), NOW, 'UTC'), []);
});

test('the vendor\'s own verdict is kept beside the windows when it refuses ordinary use', () => {
  const lines = gaugeLines(reading({ ordinaryUsageAllowed: false, rateLimitReachedType: 'rate_limit_reached' }), NOW, 'UTC');

  assert.equal(lines.at(-1), 'codex: the vendor says ordinary use is not allowed now (rate_limit_reached)');
});

test('the figure, when it is shown', () => {
  assert.equal(ratioLine(ratio({ roundsPerPercent: 4.25, intervals: 6 })), '≈ 4.3 rounds per 1 % of acme (weekly) — over the last 6 intervals');
});

test('a hidden figure names the consumer coai cannot count', () => {
  const line = ratioLine(ratio({ limitId: 'codex', unavailable: 'consumer-not-counted', missingConsumer: 'direct-codex-use' }));

  assert.equal(line, 'rounds per 1 % of codex (weekly): hidden — direct codex use outside coai spends the same limit and is not counted');
});

test('an unavailable figure says why — never 0 and never infinity', () => {
  const line = ratioLine(ratio({ unavailable: 'percent-did-not-rise' }));

  assert.equal(line, 'rounds per 1 % of acme (weekly): unavailable — the percentage did not rise between the last readings');
  assert.doesNotMatch(line, /Infinity|NaN|≈ 0\b/);
  assert.match(ratioLine(ratio({ unavailable: 'window-reset' })), /the window reset between the last readings/);
  assert.match(ratioLine(ratio({ unavailable: 'missing-reading' })), /a launch between the readings has no reading of its own/);
  assert.match(ratioLine(ratio({ unavailable: 'some-new-code' })), /unavailable — some-new-code$/, 'a code this build does not know is said as it came');
});
