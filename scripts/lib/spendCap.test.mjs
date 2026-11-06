import assert from 'node:assert/strict';
import { test } from 'node:test';
import { capPassed, capRefusal, inputTokensOf } from './spendCap.mjs';

test('the run that passed the cap would have stopped before its second launch', () => {
  // 2026-10-21: launch 1 took 94 883 input tokens, launch 2 took 357 039. Checked before launch 2 with a 400 000 cap, a
  // launch as large as the largest so far fits (189 766), so it ran; checked before launch 3, it does not.
  assert.equal(capRefusal(94_883, [94_883], 400_000), '');
  assert.match(capRefusal(451_922, [94_883, 357_039], 400_000), /input-token cap: 451922 spent \+ 357039/);
});

test('a launch that would take the spend past the cap is not started, even before the cap is reached', () => {
  assert.match(capRefusal(250_000, [200_000], 400_000), /250000 spent \+ 200000 .* > 400000/);
});

test('nothing launched yet always fits', () => {
  assert.equal(capRefusal(0, [], 400_000), '');
});

test('a value that is not a finite non-negative number refuses the launch — NaN can no longer slip past the comparison', () => {
  const refused = (spent, inputs, cap) => capRefusal(spent, inputs, cap);
  for (const bad of [Number.NaN, -1, Number.POSITIVE_INFINITY, '5', null, undefined]) {
    assert.match(refused(bad, [1], 400_000), /not a finite non-negative number/, `spent ${String(bad)}`);
    assert.match(refused(0, [bad], 400_000), /not a finite non-negative number/, `launch input ${String(bad)}`);
    assert.match(refused(0, [1], bad), /not a finite non-negative number/, `cap ${String(bad)}`);
  }
  assert.match(capRefusal(0, 'nope', 400_000), /not a finite non-negative number/);
  assert.match(capRefusal(0, undefined, 400_000), /not a finite non-negative number/);
});

test('the post-launch hard stop: spent past the cap is passed, exactly at the cap is not, and a bad value is treated as passed', () => {
  assert.equal(capPassed(400_001, 400_000), true);
  assert.equal(capPassed(400_000, 400_000), false);
  assert.equal(capPassed(0, 400_000), false);
  for (const bad of [Number.NaN, -5, Number.POSITIVE_INFINITY, '1', null, undefined]) {
    assert.equal(capPassed(bad, 400_000), true, `spent ${String(bad)}`);
    assert.equal(capPassed(1, bad), true, `cap ${String(bad)}`);
  }
});

test('a usage block with a finite non-negative input_tokens gives that number, zero included', () => {
  assert.deepEqual(inputTokensOf({ input_tokens: 94_883, output_tokens: 4_399 }), { tokens: 94_883, refusal: '' });
  assert.deepEqual(inputTokensOf({ input_tokens: 0 }), { tokens: 0, refusal: '' });
});

test('a launch with no usable input_tokens is a refusal, never a zero — and the sentence never echoes the value', () => {
  const cases = [null, undefined, {}, [], 'usage', 7, { input_tokens: undefined }, { input_tokens: null }, { input_tokens: '94883' },
    { input_tokens: Number.NaN }, { input_tokens: -1 }, { input_tokens: Number.POSITIVE_INFINITY }, { input_tokens: { n: 1 } }];
  for (const usage of cases) {
    const got = inputTokensOf(usage);
    assert.notEqual(got.refusal, '', `usage ${JSON.stringify(usage)}`);
    assert.equal(got.tokens, 0);
  }
  assert.doesNotMatch(inputTokensOf({ input_tokens: 'sk-SECRET' }).refusal, /SECRET/);
});
