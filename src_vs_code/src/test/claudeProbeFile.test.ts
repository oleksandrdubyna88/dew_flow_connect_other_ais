import assert from 'node:assert/strict';
import { test } from 'node:test';

import { LONGEST_FILE, parseProbe, writeProbe } from '../claudeProbeFile';
import { stillGood } from '../claudeModels';
import type { ProbeResult } from '../claudeModels';

const PROBE: ProbeResult = {
  cliVersion: '2.1.0',
  checkedUtc: '2026-09-16T10:00:00.000Z',
  models: [
    { asked: 'fable', answered: 'claude-fable-5-1', verified: true },
    { asked: 'haiku', answered: 'claude-opus-5[1m]', verified: false },
  ],
};

test('what was written is what comes back', () => {
  assert.deepEqual(parseProbe(writeProbe(PROBE)), PROBE);
});

test('a file this cannot understand is no answer, rather than a wrong one', () => {
  for (const [what, text] of [
    ['truncated by a killed window', '{"cliVersion":"2.1.0","checked'],
    ['empty', ''],
    ['json that is not a record', '[1,2,3]'],
    ['a record with no version', JSON.stringify({ checkedUtc: '2026-09-16T10:00:00.000Z', models: [] })],
    ['a record with no stamp', JSON.stringify({ cliVersion: '2.1.0', models: [] })],
  ] as const) {
    assert.equal(parseProbe(text), undefined, `${what} must read as nothing`);
  }
});

test('an entry that is half written never reaches a dropdown as confirmed', () => {
  // This file is on disk, an older build may have written it, and a person may have opened it. A
  // `verified` that is a string, a missing `answered`, an entry that is not an object at all — none
  // of them may end up naming a model the CLI was never asked about.
  const read = parseProbe(JSON.stringify({
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [
      // A truthy verdict over an answer from ANOTHER family: the stored boolean is ignored and the
      // answer beside it decides, so this is refused rather than believed.
      { asked: 'fable', answered: 'claude-opus-5', verified: 'yes' },
      { asked: 'sonnet' },
      { answered: 'claude-opus-5' },
      null,
      'opus',
    ],
  }));

  assert.ok(read);
  assert.deepEqual(read.models, [
    { asked: 'fable', answered: 'claude-opus-5', verified: false },
    { asked: 'sonnet', answered: '', verified: false },
  ], 'a truthy-looking value was taken for a confirmation, or an entry with no candidate survived');
});

test('an answer from another binary, or from last month, is not used', () => {
  const at = Date.parse('2026-09-16T10:00:00.000Z');

  assert.equal(stillGood(parseProbe(writeProbe(PROBE)), '2.1.0', at + 60_000), true);
  assert.equal(stillGood(parseProbe(writeProbe(PROBE)), '2.2.0', at + 60_000), false);
  assert.equal(stillGood(parseProbe(writeProbe(PROBE)), '2.1.0', at + 8 * 24 * 3600_000), false);
});

test('a stored verdict is never trusted — it is re-derived from the answer beside it', () => {
  // A hand-edited or older-build entry claiming a family it did not get. The two fields are one
  // decision written twice, and only one of them is evidence.
  const lying = parseProbe(JSON.stringify({
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [
      { asked: 'fable', answered: 'claude-opus-5', verified: true },
      { asked: 'opus', answered: 'claude-opus-5', verified: false },
    ],
  }));

  assert.equal(lying?.models[0]?.verified, false, 'the answer names another family, so it is not confirmed');
  assert.equal(lying?.models[1]?.verified, true, 'and one that DID answer as itself is, whatever the file said');
});

test('a file that has grown beyond anything this writes is refused', () => {
  const huge = JSON.stringify({
    cliVersion: 'x'.repeat(5_000),
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [],
  });

  assert.equal(parseProbe(huge), undefined, 'a version string this long was not written by this product');
});

test('a file far larger than anything this writes is refused before it is parsed', () => {
  // `MOST_ENTRIES` applies AFTER `JSON.parse`, so a cache grown by something else is fully parsed
  // into extension-host memory first. The data directory is one a person chooses. (CodeRabbit.)
  const huge = `{"cliVersion":"2.1.0","checkedUtc":"2026-09-16T10:00:00.000Z","models":[${
    Array.from({ length: 40_000 }, (_, i) => `{"asked":"m${i}","answered":"x","verified":true}`).join(',')
  }]}`;

  assert.ok(huge.length > LONGEST_FILE, 'the fixture is not actually oversized');
  assert.equal(parseProbe(huge), undefined, 'a cache this size was parsed rather than refused');
});

test('an entry whose strings run away is dropped, and the rest survive it', () => {
  const read = parseProbe(JSON.stringify({
    cliVersion: '2.1.0',
    checkedUtc: '2026-09-16T10:00:00.000Z',
    models: [
      { asked: 'x'.repeat(5_000), answered: 'claude-sonnet-5', verified: true },
      { asked: 'sonnet', answered: 'y'.repeat(5_000), verified: true },
      { asked: 'opus', answered: 'claude-opus-5', verified: true },
    ],
  }));

  assert.deepEqual(read?.models.map((m) => m.asked), ['opus'],
    'a model id longer than any this product writes reached a dropdown');
});
