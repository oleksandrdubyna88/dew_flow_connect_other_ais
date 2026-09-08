import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CONVENTIONS_ID, CONVENTIONS_ROLE_SINCE, selectedFor, universalFor } from '../prompts';
import { panelHtml, PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS } from '../vendors';
import { SNIPPET_VERSION } from '../claudeSnippet';

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

/** Enough panel to render the Prompts section; every field the version-skew test does not read. */
const baseState = (): PanelState => ({
  settings: DEFAULTS,
  vendors: DEFAULT_VENDORS,
  codexModels: [], agyModels: [],
  localEngines: {},
  server: { kind: 'absent', version: '', remembered: false, updateOffered: false },
  side: '',
  perSide: false,
  questions: [],
  sessions: [],
  openSections: ['prompts'],
  usage: [],
  usageWindow: 'week',
  cliStatus: {},
  modelPrices: {},
  snippetStatus: { kind: 'current', current: SNIPPET_VERSION },
  latestServerVersion: '',
});

/**
 * What `PromptCatalog.ForRound` returns for an unset round.
 *
 * <p>Transcribed from the C# by hand, on purpose: the point of this file is that two programs agree,
 * and a shared implementation would make the agreement true by construction rather than checked.
 * When the server's branch changes, this line changes with it — and that is the moment somebody has
 * to look at both.</p>
 *
 * <p>2026-09-08: there is no branch left to transcribe. The conventions pass took round 1 — of every
 * code role, then of Architecture alone — because it had no budget of its own; as a ROLE it has one,
 * so both programs are back to "what was chosen, else this role's universal prompt". A rule that
 * fits on one line is a rule two programs can hold.</p>
 */
function whatTheServerRuns(role: string): string {
  return universalFor(role).id;
}

const ROLES = ['PlanCritique', 'Conventions', 'Architecture', 'SecurityReliability', 'UxDxPerformance'];

test('an unset round shows what the server runs, for every role and every round', () => {
  for (const role of ROLES) {
    for (const round of [1, 2, 3, 4]) {
      assert.equal(
        selectedFor(role, round, {}),
        whatTheServerRuns(role),
        `${role} round ${round}: the panel and the server disagree`,
      );
    }
  }
});

/**
 * The one case the transcription above cannot cover: the two programs are versioned separately.
 *
 * <p>The two agree in the SOURCE, and are installed apart — an extension updates itself, a server
 * is a binary somebody presses a button to replace. Below {@link CONVENTIONS_ROLE_SINCE} the server
 * does not know `Conventions` is a role at all, so a code round asks for a role its enum cannot
 * parse. Raised by three reviewers as version skew when the warning was about a wrong prompt; the
 * skew got worse, and the warning with it.</p>
 */
test('a panel ahead of its server says so, instead of showing a round the server will not run', () => {
  const withServer = (server: PanelState['server']): string =>
    panelHtml({ ...baseState(), server }, 'n0nce');

  const behind = withServer({ kind: 'known', version: '0.18.7', remembered: true, updateOffered: true });
  assert.ok(behind.includes('does not'), 'an older server is named');
  assert.ok(behind.includes('0.18.7'), 'and so is the version that is there');

  // 0.18.9 by name, because it is the one everybody has: it is yesterday's release and it has
  // four roles. This line is what would catch the constant sliding back onto it.
  const released = withServer({ kind: 'known', version: '0.18.9', remembered: true, updateOffered: true });
  assert.ok(released.includes('does not know'), '0.18.9 predates the role and must say so');

  const current = withServer({ kind: 'known', version: CONVENTIONS_ROLE_SINCE, remembered: true, updateOffered: false });
  assert.ok(!current.includes('does not know'), 'the server that agrees says nothing');

  const newer = withServer({ kind: 'known', version: '0.19.0', remembered: true, updateOffered: false });
  assert.ok(!newer.includes('does not know'), 'nor does a later one');

  const absent = withServer({ kind: 'absent', version: '', remembered: false, updateOffered: false });
  assert.ok(!absent.includes('does not know'), 'a server nobody has installed is not behind');
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

/**
 * The saved configuration of somebody who chose Conventions under Architecture yesterday.
 *
 * <p>Two reviewers on the plan round asked what becomes of that stored selection now that the
 * prompt has left the three code roles: silently ignored, or a round that fails on an id nobody
 * offers? Neither — it falls back to the role's own universal question, by the same rule that
 * covers a renamed prompt. Asserted by NAME because the generic sentence is already tested and
 * this is the specific selection this change orphaned. Its twin is `ConventionsPassTests`.</p>
 */
test('a saved Conventions choice under a role that no longer offers it falls back', () => {
  for (const role of ['Architecture', 'SecurityReliability', 'UxDxPerformance']) {
    assert.equal(selectedFor(role, 1, { [role]: [CONVENTIONS_ID] }), universalFor(role).id);
  }
});
