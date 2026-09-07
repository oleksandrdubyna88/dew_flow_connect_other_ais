import assert from 'node:assert/strict';
import { test } from 'node:test';
import { CONVENTIONS_ID, CONVENTIONS_NARROWED_IN, selectedFor, universalFor } from '../prompts';
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
 * <p>2026-09-07: the conventions pass narrowed from every code role to <b>Architecture</b> alone.</p>
 */
function whatTheServerRuns(role: string, round: number, hasRules: boolean): string {
  return hasRules && round === 1 && role === 'Architecture' ? CONVENTIONS_ID : universalFor(role).id;
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

/**
 * The one case the transcription above cannot cover: the two programs are versioned separately.
 *
 * <p>`selectedFor` and `PromptCatalog.ForRound` agree in the SOURCE, and are installed apart — an
 * extension updates itself, a server is a binary somebody presses a button to replace. Below
 * {@link CONVENTIONS_NARROWED_IN} the server still makes round 1 of every code role the conventions
 * pass, so this panel would show `Universal` for a round that runs `conventions`. Raised by three
 * reviewers independently on the round that shipped the narrowing, and it is the only one of their
 * version-skew findings that a test can hold.</p>
 */
test('a panel ahead of its server says so, instead of showing a round the server will not run', () => {
  const withServer = (server: PanelState['server']): string =>
    panelHtml({ ...baseState(), server }, 'n0nce');

  const behind = withServer({ kind: 'known', version: '0.18.7', remembered: true, updateOffered: true });
  assert.ok(behind.includes('still runs'), 'an older server is named');
  assert.ok(behind.includes('0.18.7'), 'and so is the version that is there');

  const current = withServer({ kind: 'known', version: CONVENTIONS_NARROWED_IN, remembered: true, updateOffered: false });
  assert.ok(!current.includes('still runs'), 'the server that agrees says nothing');

  const newer = withServer({ kind: 'known', version: '0.19.0', remembered: true, updateOffered: false });
  assert.ok(!newer.includes('still runs'), 'nor does a later one');

  const absent = withServer({ kind: 'absent', version: '', remembered: false, updateOffered: false });
  assert.ok(!absent.includes('still runs'), 'a server nobody has installed is not behind');
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
