import assert from 'node:assert/strict';
import { test } from 'node:test';
import { JOB_NAME, PROMOTE_AT, asText, streakOf } from '../../scripts/host-job-streak.mjs';

/**
 * The counter behind the extension-host job's promotion rule.
 *
 * <p><b>What it is for.</b> `ci.yml` says the job becomes a required check once it has been green on
 * twenty consecutive runs of `main` — <i>"a number, so the promotion is a measurement rather than a
 * mood"</i>. Nobody counted, and the promoted tail plan had to record the promotion as pending with
 * no figure, because none had been measured. This is the machine doing the counting.</p>
 *
 * <p>The API half is not tested here and is not pretended to be: fetching is the part that cannot be
 * asserted without a network or a fake of GitHub, and the part worth asserting — what a list of
 * conclusions MEANS — is pure and is all of this file. The script is written so the two are
 * separable, and imported rather than spawned, because a script exercised by spawning reports zero
 * coverage and would fail the new-code gate.</p>
 */

/** A run whose host job concluded the given way. */
const ran = (conclusion) => ({ conclusion });

test('a streak is counted from the newest run backwards', () => {
  const streak = streakOf([ran('success'), ran('success'), ran('success'), ran('failure')]);

  assert.equal(streak, 3, 'the streak did not stop at the first run that was not green');
});

test('a failure at the newest run means a streak of nothing, however good the history', () => {
  // The case the counter exists to get right. Twenty green runs behind one red one is not a
  // promotion: making it required would break the very next merge.
  const history = [ran('failure'), ...Array.from({ length: 30 }, () => ran('success'))];

  assert.equal(streakOf(history), 0, 'a red newest run was counted as part of a streak');
});

test('a run still in flight ENDS the streak rather than being skipped over', () => {
  // Skipping it would let this say twenty while the newest run is failing at that moment. Ending on
  // it is the conservative reading, and the streak resumes by itself once the run concludes.
  const streak = streakOf([ran(null), ran('success'), ran('success')]);

  assert.equal(streak, 0, 'an unfinished run was treated as green');
});

test('a cancelled run is not a green one', () => {
  assert.equal(streakOf([ran('success'), ran('cancelled'), ran('success')]), 1,
    'a cancelled run was counted towards the streak');
});

test('no runs at all is a streak of zero, not a promotion', () => {
  // An empty list must never read as "nothing has failed, so promote it" - the same shape as a glob
  // that matches nothing passing a test suite.
  assert.equal(streakOf([]), 0, 'an empty history was read as a streak');
  assert.match(asText(streakOf([])), /not yet/u, 'an empty history said the job was ready to promote');
});

test('the threshold is what decides the verdict, and the verdict names the job', () => {
  const short = asText(PROMOTE_AT - 1);
  const met = asText(PROMOTE_AT);

  assert.match(short, /not yet — 1 more green run/u, 'one run short did not say how many were left');
  assert.match(met, /READY TO PROMOTE/u, 'the threshold was reached and the text did not say so');
  assert.ok(met.includes(JOB_NAME),
    'the promotion line does not name the check an operator has to go and tick');
});

test('past the threshold it stays ready rather than starting over', () => {
  assert.match(asText(PROMOTE_AT + 11), /READY TO PROMOTE/u, 'a longer streak stopped being ready');
});
