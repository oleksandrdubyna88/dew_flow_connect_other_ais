import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { FEATURES } from '../binaryFeatures';
import { ON_EXHAUSTED_CHOICES } from '../onExhaustedPanel';
import { onExhaustedEnv } from '../onExhaustedWire';

/**
 * What the settings file says about When the rounds run out, for the binary it meets (parent § 4.6).
 *
 * <p>Auto is what a server with no policy named runs, so the panel writes nothing for it — except to a binary that is
 * KNOWN to predate Auto, which would refuse the word and so is sent the nearest policy it has. "Not asked yet" is not
 * "older": every settled answer rewrites the file, so a cold start writes nothing and is corrected a moment later.</p>
 */

/** The four columns of the table: what the binary said, and whether it has said it yet. */
const COLUMNS: readonly { readonly name: string; readonly features: readonly string[]; readonly settled: boolean }[] = [
  { name: 'lists Auto', features: [FEATURES.onExhaustedAuto], settled: true },
  { name: 'settled without it', features: [], settled: true },
  { name: 'settled with other features only', features: [FEATURES.fastMode], settled: true },
  { name: 'not settled', features: [], settled: false },
];

/** What `auto` writes in each column, in the order of `COLUMNS` — the one row the table does not repeat. */
const AUTO_WRITES: readonly Record<string, string>[] = [{}, { COAI_ON_EXHAUSTED: 'good_enough' }, { COAI_ON_EXHAUSTED: 'good_enough' }, {}];

test('EveryCellOfTheTable', () => {
  for (const choice of ON_EXHAUSTED_CHOICES) {
    COLUMNS.forEach((column, at) => {
      const expected = choice.value === 'auto' ? AUTO_WRITES[at] : { COAI_ON_EXHAUSTED: choice.value };

      assert.deepEqual(
        onExhaustedEnv(choice.value, column.features, column.settled),
        expected,
        `${choice.value} for a binary ${column.name}`,
      );
    });
  }
});

test('AnUnsettledAnswerIsNotAnOlderServer', () => {
  assert.deepEqual(onExhaustedEnv('auto', [], false), {}, 'a cold start writes nothing: the settled answer rewrites the file');
});

test('AnOlderServerIsToldGoodEnough_NeverAuto', () => {
  // A 64 (a binary from before `--features`) settles as an empty list, and is the oldest binary there is.
  assert.deepEqual(onExhaustedEnv('auto', [], true), { COAI_ON_EXHAUSTED: 'good_enough' });
  assert.notEqual(onExhaustedEnv('auto', [], true)['COAI_ON_EXHAUSTED'], 'auto', 'an older server refuses the word');
});

test('AskAHumanCrosses_NowThatItIsNotTheDefault', () => {
  for (const column of COLUMNS) {
    assert.deepEqual(onExhaustedEnv('human', column.features, column.settled), { COAI_ON_EXHAUSTED: 'human' }, column.name);
  }
});
