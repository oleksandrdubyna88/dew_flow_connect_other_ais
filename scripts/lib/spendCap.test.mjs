import assert from 'node:assert/strict';
import { test } from 'node:test';
import { capRefusal } from './spendCap.mjs';

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
