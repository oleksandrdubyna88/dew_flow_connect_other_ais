import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CONVENTIONS_ID, selectedFor, universalFor } from '../prompts';

/**
 * The picker shows the prompt the SERVER will run, for every round the person can see.
 *
 * <p>Found by the pre-delivery campaign rather than by reading: the panel passed its
 * <b>deal</b> switch into {@link selectedFor}'s <i>rotating</i> slot, so ticking "Deal the lenses
 * across vendors" made round 2 of Architecture display <code>arch-boundaries</code> — while the
 * server, whose rotation had no switch the panel could reach, ran <code>architecture</code>. A
 * picker that names a prompt nobody runs is worse than an empty one: it is evidence for a choice
 * that was never made. Rotation is gone from both halves; this test is what keeps them level.</p>
 *
 * <p>Its twin on the C# side is <code>ConventionsPassTests</code>. Two suites for one rule, because
 * the rule is that two programs agree, and neither can check that alone.</p>
 */

/** What `PromptCatalog.ForRound` returns for an unset round. */
function whatTheServerRuns(role: string, round: number, hasRules: boolean): string {
  return hasRules && round === 1 && role !== 'PlanCritique' ? CONVENTIONS_ID : universalFor(role).id;
}

const ROLES = ['PlanCritique', 'Architecture', 'SecurityReliability', 'UxDxPerformance'];

test('an unset round shows what the server runs, with rules and without', () => {
  for (const role of ROLES) {
    for (const round of [1, 2, 3, 4]) {
      for (const hasRules of [true, false]) {
        assert.equal(
          selectedFor(role, round, {}, hasRules),
          whatTheServerRuns(role, round, hasRules),
          `${role} round ${round} with hasRules=${hasRules}: the panel and the server disagree`,
        );
      }
    }
  }
});

test('a later round never shows a lens nobody selected', () => {
  // The whole class of defect in one assertion: no unset round may resolve to a narrow lens,
  // because nothing on the server side would run one without an explicit pick.
  for (const role of ROLES) {
    for (const round of [2, 3, 4]) {
      assert.equal(selectedFor(role, round, {}), universalFor(role).id);
    }
  }
});

test('an explicit choice is still what is shown', () => {
  assert.equal(
    selectedFor('Architecture', 2, { Architecture: ['architecture', 'arch-evolution'] }),
    'arch-evolution',
  );
});
