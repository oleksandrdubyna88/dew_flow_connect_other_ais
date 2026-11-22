import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { deriveSample, LIMITS, parseMeasureArgs, stats, summarize, tableRow, TABLE_HEADER } from '../../scripts/coldstart-lib.mjs';

/**
 * The pure parts of the cold-start measurement (v2 extension.md § 2, epic E2 story S2): how M1/M2/M3 are read off the marks, which samples do not
 * count, the pass/fail rule over three samples, and the shape of the JSON and the table row. A host launch cannot run on every machine, so every
 * rule the launcher applies is asserted here, on records shaped like the ones the scenario writes.
 */

const T0 = 1_000_000;

/** A raw record like `measureScenario.ts` writes: M1 = `m1` ms, M2 = `m2` ms, M3 = `m3` ms (null: none). */
function raw({ sample = 1, m1 = 12, m2 = 800, m3 = 700, startedActive = false, remoteName = null, extra = [] } = {}) {
  const marks = [
    { name: 'eval', at: T0 - 5_000 },
    { name: 'activateReturn', at: T0 - 5_000 + m1 },
    { name: 'viewResolve', at: T0 + 30 },
    ...(m3 === null ? [] : [{ name: 'firstData', at: T0 + m3 }]),
    ...(m2 === null ? [] : [{ name: 'ready', at: T0 + m2 }]),
    ...extra,
  ];

  return { schema: 1, sample, t0: T0, startedActive, marks, host: { remoteName, platform: 'linux', vscodeVersion: '1.99.0', appName: 'Code' } };
}

test('M1 is activateReturn minus eval, M2 is the first ready at or after the clock minus the clock, M3 is firstData minus the clock', () => {
  const derived = deriveSample(raw({ m1: 12.34, m2: 800.06, m3: 700 }));

  assert.deepEqual([derived.m1, derived.m2, derived.m3], [12.3, 800.1, 700]);
  assert.deepEqual(derived.problems, []);
  assert.equal(derived.location, 'local');
});

test('a ready ack from BEFORE the clock is not an ack: the first ready after it is used, and with none the sample fails naming a stale ack', () => {
  const fresh = deriveSample(raw({ m2: 900, extra: [{ name: 'ready', at: T0 - 100 }] }));
  assert.equal(fresh.m2, 900);
  assert.deepEqual(fresh.problems, []);

  const stale = deriveSample(raw({ m2: null, extra: [{ name: 'ready', at: T0 - 100 }] }));
  assert.equal(stale.m2, null);
  assert.match(stale.problems.join('\n'), /older than the driver clock/);
});

test('a missing ack fails the sample, and is never read as zero', () => {
  const derived = deriveSample(raw({ m2: null }));

  assert.equal(derived.m2, null);
  assert.match(derived.problems.join('\n'), /no ready acknowledgement/);
  const result = summarize([raw({ sample: 1 }), raw({ sample: 2, m2: null }), raw({ sample: 3 })]);
  assert.equal(result.pass, false);
  assert.match(result.problems.join('\n'), /sample 2: no ready acknowledgement/);
  assert.deepEqual(result.m2, { min: 800, median: 800, max: 800 }, 'the statistics are of the samples that have one, and the verdict is still a failure');
});

test('a record a driver wrote after a failure says so, with its own sentence', () => {
  const failed = deriveSample({ schema: 1, sample: 2, error: 'coai.__measure never answered' });

  assert.equal(failed.m2, null);
  assert.equal(failed.problems[0], 'the driver failed: coai.__measure never answered');
  assert.equal(summarize([raw(), failed, raw()]).pass, false);
});

test('first data is recorded only: a sample without it still passes', () => {
  const result = summarize([raw({ m3: null }), raw({ m3: null }), raw({ m3: null })]);

  assert.equal(result.pass, true, JSON.stringify(result.problems));
  assert.equal(result.m3, null);
});

test('a sample whose extension was already active is not a cold start; so is one that does not say', () => {
  assert.match(deriveSample(raw({ startedActive: true })).problems.join('\n'), /already active/);
  const unsaid = raw();
  delete unsaid.startedActive;
  assert.match(deriveSample(unsaid).problems.join('\n'), /does not say/);
});

test('the rule: the MAXIMUM M2 must be at most 2000 ms and M1 at most 50 ms; the edge passes, a millisecond over fails', () => {
  assert.deepEqual(LIMITS, { m2MaxMs: 2000, m1MaxMs: 50 });
  const edge = summarize([raw({ m2: 2000, m1: 50 }), raw({ m2: 100 }), raw({ m2: 100 })]);
  assert.equal(edge.pass, true, JSON.stringify(edge.problems));
  assert.equal(edge.m2.max, 2000);

  const slowest = summarize([raw({ m2: 100 }), raw({ m2: 2000.1 }), raw({ m2: 100 })]);
  assert.equal(slowest.pass, false, 'one slow sample fails the measurement: the maximum counts, not the median');
  assert.equal(slowest.m2.median, 100);
  assert.match(slowest.problems.join('\n'), /M2 max 2000\.1 ms is over the 2000 ms limit/);

  const heavy = summarize([raw({ m1: 3000 }), raw(), raw()]);
  assert.equal(heavy.pass, false, 'an activation that sleeps 3 s fails on M1 even when M2 is fine');
  assert.match(heavy.problems.join('\n'), /M1 max 3000 ms is over the 50 ms limit/);
});

test('a measurement needs every sample it asked for: two records of three is a failure', () => {
  const result = summarize([raw({ sample: 1 }), raw({ sample: 3 })]);

  assert.equal(result.pass, false);
  assert.match(result.problems.join('\n'), /2 sample\(s\) came back, 3 were needed/);
});

test('samples from two places do not make one measurement, and the location is named: wsl for a remote host', () => {
  const mixed = summarize([raw(), raw({ remoteName: 'wsl' }), raw()]);
  assert.match(mixed.problems.join('\n'), /different places: local, wsl/);
  assert.equal(mixed.location, null);

  const remote = summarize([raw({ remoteName: 'wsl' }), raw({ remoteName: 'wsl' }), raw({ remoteName: 'wsl' })]);
  assert.equal(remote.location, 'wsl');
  assert.equal(remote.pass, true);
});

test('record-only (the v1 baseline) applies no limit, accepts an active extension and a missing M1, and still needs the ack', () => {
  const v1 = { ...raw({ m2: 9000, startedActive: true }), marks: [{ name: 'ready', at: T0 + 9000 }] };
  const result = summarize([v1, v1, v1], { recordOnly: true });

  assert.equal(result.pass, true, JSON.stringify(result.problems));
  assert.equal(result.limits, null);
  assert.equal(result.m1, null);
  assert.equal(result.m2.max, 9000);
  const none = summarize([{ ...v1, marks: [] }, v1, v1], { recordOnly: true });
  assert.equal(none.pass, false);
});

test('statistics: min, median (mean of the middle two when even) and max of the numbers that exist', () => {
  assert.deepEqual(stats([3, 1, 2]), { min: 1, median: 2, max: 3 });
  assert.deepEqual(stats([4, 1, null, 2, 3]), { min: 1, median: 2.5, max: 4 });
  assert.equal(stats([null]), null);
  assert.equal(stats([]), null);
});

test('the JSON shape of a result: schema, kind, limits, place, samples without their problems, the three statistics, the verdict', () => {
  const result = summarize([raw({ sample: 1 }), raw({ sample: 2 }), raw({ sample: 3 })]);

  assert.deepEqual(Object.keys(result), ['schema', 'kind', 'recordOnly', 'limits', 'location', 'platform', 'vscodeVersion', 'samples', 'm1', 'm2', 'm3', 'pass', 'problems']);
  assert.equal(result.kind, 'coai-cold-start');
  assert.equal(result.schema, 1);
  assert.deepEqual(result.samples[0], { sample: 1, startedActive: false, location: 'local', platform: 'linux', vscodeVersion: '1.99.0', m1: 12, m2: 800, m3: 700 });
  assert.deepEqual(JSON.parse(JSON.stringify(result)), result, 'the result survives JSON, so what a file holds is what the verdict said');
});

test('the table row has one cell per column of the header, names the verdict, and cannot be broken by a pipe in the label', () => {
  const result = summarize([raw(), raw(), raw()]);
  const row = tableRow(result, { date: '2026-11-22', commit: 'abcd1234', label: 'ci | ubuntu' });
  const cells = (line) => line.split('|').slice(1, -1).map((one) => one.trim());

  assert.equal(cells(row).length, cells(TABLE_HEADER).length);
  assert.deepEqual(cells(row), ['2026-11-22', 'abcd1234', 'ci / ubuntu', 'local', 'linux', '12', '800 / 800 / 800', '700 / 700 / 700', 'pass']);
  assert.equal(cells(tableRow(summarize([raw({ m2: 5000 }), raw(), raw()]), { date: 'd', commit: 'c', label: '' })).at(-1), 'FAIL');
  assert.equal(cells(tableRow(summarize([raw(), raw(), raw()], { recordOnly: true }), { date: 'd', commit: 'c', label: '' })).at(-1), 'recorded');
});

test('flags: defaults, every flag, and an unknown or valueless flag is refused', () => {
  assert.deepEqual(parseMeasureArgs([]), { samples: 3, json: undefined, row: false, label: undefined, commit: undefined, recordOnly: false, inputs: [] });
  assert.deepEqual(parseMeasureArgs(['--samples', '5', '--json', 'a.json', '--row', '--label', 'ci', '--commit', 'abc', '--record-only', 'x.json']),
    { samples: 5, json: 'a.json', row: true, label: 'ci', commit: 'abc', recordOnly: true, inputs: ['x.json'] });
  assert.throws(() => parseMeasureArgs(['--sample', '3']), /unknown flag --sample/);
  assert.throws(() => parseMeasureArgs(['--json']), /--json needs a value/);
  assert.throws(() => parseMeasureArgs(['--json', '--row']), /--json needs a value/);
  assert.throws(() => parseMeasureArgs(['--samples', '0']), /whole number/);
});

test('research/RESULTS_v2_start.md ends with the table whose header is the one a row is written for, so an appended row lands in it', async () => {
  const { readFileSync } = await import('node:fs');
  const { join } = await import('node:path');
  const { fileURLToPath } = await import('node:url');
  const file = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..', '..', 'research', 'RESULTS_v2_start.md');
  const lines = readFileSync(file, 'utf8').trimEnd().split('\n');
  const headerAt = lines.lastIndexOf(TABLE_HEADER);

  assert.ok(headerAt >= 0, 'the results file has no table with the header coldstart-lib.mjs writes rows for');
  assert.match(lines[headerAt + 1], /^\|(---\|){9}$/, 'the separator follows the header');
  assert.ok(lines.slice(headerAt + 2).every((line) => line.startsWith('| ')), 'only rows follow the table: it is the last thing in the file');
});
