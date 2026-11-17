import '../../scripts/inRunFolder.cjs';
import assert from 'node:assert/strict';
import { before, test } from 'node:test';

import {
  BundledLog, bundledLogModule, clickInRow, firstRenderedKey, runningDomPage, runningPage, SESSION, STARTED,
} from './roundsLogPageHarness';

/**
 * The note on a rounds-log row that RAN (story E2.S3, `todo/PLAN_auto_never_asks_a_person_E2.md` § 9.1).
 *
 * <p>Policy `auto` records what it decided for a round — why nobody was asked, or that it released a hold an older
 * build had raised — in the row's note. A skipped round already says its note in its own branch; every other opened
 * round shows it under the "asked by" lines and above the reviewers, once. The page is RUN, not read as text.</p>
 */

const [ROUND] = SESSION.rounds;

/** The shared session with its one round replaced by `round`, and the fixture's own day. */
function sessionWith(round: Record<string, unknown>): typeof SESSION {
  return { ...SESSION, rounds: [{ ...ROUND, startedUtc: STARTED.toISOString(), ...round }] } as typeof SESSION;
}

/** The page run on one session, the row opened — what the person reads after a click. */
function opened(round: Record<string, unknown>): string {
  const page = runningPage([sessionWith(round)]);
  const key = firstRenderedKey(page.seen['rows']?.innerHTML ?? '');
  page.click(clickInRow('tr[data-key]', 'data-key', key));
  const rows = page.seen['rows']?.innerHTML ?? '';
  assert.match(rows, /class="detail"/, 'the row did not open');

  return rows;
}

const UNREVIEWED = 'policy auto: no reviewer answered — nothing was reviewed, and the stage proceeds without a review. codex: exit 1';

test('AnUnreviewedRound_ShowsWhyWhenOpened', () => {
  const rows = opened({ stage: 'PlanReview', verdict: 'not_reviewed', note: UNREVIEWED });

  assert.match(rows, /class="reviewer roundnote"/, 'the note of a round that ran is not drawn');
  assert.match(rows, /no reviewer answered/, 'the reason policy auto recorded is not on the page');
});

test('AReleasedHold_SaysItWasReleased', () => {
  const rows = opened({ verdict: 'call_human', note: 'released by policy auto: this hold was raised by a build before auto' });

  assert.match(rows, /class="reviewer roundnote"/);
  assert.match(rows, /released by policy auto: this hold was raised by a build before auto/);
});

test('ANoteOnARoundWithNoReviewerDetail_IsStillShown', () => {
  const rows = opened({ verdict: 'not_reviewed', note: UNREVIEWED, reviewerStates: [] });

  assert.match(rows, /written by an older server/, 'the fixture is not the branch with no reviewer detail');
  assert.match(rows, /class="reviewer roundnote"/, 'the note is lost on the branch that has no reviewers');
});

test('ARoundWithNoNote_DrawsNoNoteElement', () => {
  const rows = opened({});

  assert.doesNotMatch(rows, /roundnote/, 'an empty note drew an element anyway');
});

test('ASkip_StillSaysItsReasonOnce', () => {
  const rows = opened({
    stage: 'FeatureReview', verdict: 'skipped', status: 'skipped', reviewers: 'no reviewer ran', reviewerStates: [],
    note: 'no vendor is ticked to review features', repeats: 3,
  });
  const reason = 'no vendor is ticked to review features ×3';

  assert.equal(rows.split(reason).length - 1, 1, 'the skip says its reason more than once');
  assert.match(rows, /class="reviewer skipnote"/);
  assert.doesNotMatch(rows, /roundnote/, 'a skip has its own note and must not get a second one');
});

test('ANoteIsEscaped', () => {
  const rows = opened({ note: '<img src=x onerror=alert(1)>' });

  assert.match(rows, /&lt;img/, 'the note is not escaped');
  assert.doesNotMatch(rows, /<img src=x/, 'the note reached the page as markup');
});

let bundle: BundledLog;
before(() => { bundle = bundledLogModule(); });

test('NotReviewedFiltersLikeAnyVerdict', () => {
  const proceed = { ...ROUND, number: 1, verdict: 'proceed', startedUtc: STARTED.toISOString() };
  const notReviewed = { ...ROUND, number: 2, verdict: 'not_reviewed', note: UNREVIEWED, startedUtc: STARTED.toISOString() };
  const page = runningDomPage(bundle, [{ ...SESSION, rounds: [proceed, notReviewed] }]);
  const rowsBody = page.element('rows');
  const verdicts = (): string[] => rowsBody.querySelectorAll('td.col-verdict').map((cell) => cell.textContent);

  assert.deepEqual([...verdicts()].sort(), ['not_reviewed', 'proceed'], 'the fixture does not carry both verdicts');
  const select = page.document.querySelector('[data-filter="verdict"]');
  assert.ok(select !== null, 'the page has no verdict filter');
  assert.ok(select.querySelectorAll('option').some((option) => option.value === 'not_reviewed'), 'the filter does not offer not_reviewed');

  select.value = 'not_reviewed';
  page.document.dispatch('change', select);

  assert.deepEqual(verdicts(), ['not_reviewed'], 'the filter did not leave exactly the not_reviewed row');
});
