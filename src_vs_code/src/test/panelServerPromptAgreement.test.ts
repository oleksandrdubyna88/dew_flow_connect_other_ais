import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import path from 'node:path';
import { CONVENTIONS_ID, CONVENTIONS_ROLE_SINCE, PROMPTS, ROLES, selectedFor, universalFor } from '../prompts';
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
 * What `PromptCatalog.ForRound` returns for an unset round — READ from the C#, not restated here.
 *
 * <p><b>It used to be `universalFor(role).id`, and that was worth nothing.</b> `selectedFor` falls
 * back to `universalFor` too, so the assertion below compared a function with itself: the two
 * "programs" could have moved together forever and this file would have stayed green. Caught on
 * this change's own pull request, and it is the same defect this suite exists to prevent — one
 * implementation standing in for two.</p>
 *
 * <p>So the expected ids come out of `PromptCatalog.cs`: the rows whose last argument is `true` are
 * the universal ones, and each names its role through a constant declared in the same file. If the
 * server renames a universal prompt or moves it to another role, this goes red — which is the whole
 * job, and a hand transcription could only do it if somebody remembered to retype the line.</p>
 */
const promptCatalog = fs.readFileSync(
  path.join(__dirname, '..', '..', '..', 'src_mcp', 'core', 'Rounds', 'PromptCatalog.cs'), 'utf8');

/** `public const string PlanRole = "PlanCritique";` -> PlanRole: PlanCritique */
function csharpConstants(): ReadonlyMap<string, string> {
  const out = new Map<string, string>();
  for (const m of promptCatalog.matchAll(/const string (\w+)\s*=\s*"([^"]+)"/g)) {
    out.set(m[1]!, m[2]!);
  }
  return out;
}

/** The universal prompt id the SERVER ships for each role name. */
function serverUniversals(): ReadonlyMap<string, string> {
  const constants = csharpConstants();
  const resolve = (token: string): string =>
    token.startsWith('"') ? token.slice(1, -1) : (constants.get(token) ?? token);

  const out = new Map<string, string>();
  for (const m of promptCatalog.matchAll(/new\((("[^"]+")|\w+),\s*(\w+),[^\n]*,\s*true\)/g)) {
    out.set(resolve(m[3]!), resolve(m[1]!));
  }
  return out;
}

test('an unset round shows what the server runs, for every role and every round', () => {
  const universals = serverUniversals();

  // The roles come from the panel's own catalog rather than a list retyped here: a role the panel
  // gained and this file did not would otherwise go unchecked, which is the second half of the
  // same finding.
  assert.ok(ROLES.length > 0);
  for (const { id: role } of ROLES) {
    const server = universals.get(role);
    assert.ok(server, `${role} has no universal prompt in PromptCatalog.cs — the panel knows a role the server does not`);

    for (const round of [1, 2, 3, 4]) {
      assert.equal(
        selectedFor(role, round, {}),
        server,
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
 * does not know `Conventions` is a role at all, so it does not run it: the box is in the panel and
 * the reviewer never appears. Raised by three reviewers as version skew when the warning was about
 * a wrong prompt — and all three, and this comment before them, called it a failed round. It is not:
 * the server reads its gate from its own list of roles and never parses a name the panel sends.</p>
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

  // It counts ROLES, and the distinction is not pedantry: reviewers are vendors x roles, so with
  // two vendors an old server runs six reviewer calls, not three. The warning said "three
  // reviewers, not four" until two reviewers on the code round pointed at the panel's own fan-out
  // sentence, which multiplies by the vendor count three lines further down the same page.
  assert.ok(behind.includes('three code roles, not four'), 'the unit is roles');
  assert.ok(!behind.includes('three reviewers'), 'a reviewer count would contradict the fan-out line');
});

test('a later round never shows a lens nobody selected', () => {
  // The whole class of defect in one assertion: no unset round may resolve to a narrow lens,
  // because nothing on the server side would run one without an explicit pick.
  //
  // Asserted as a PROPERTY of what came back — is this prompt the role's universal one — rather
  // than by recomputing the answer with `universalFor`, which is the shape that made the test
  // above vacuous for a day.
  for (const { id: role } of ROLES) {
    for (const round of [2, 3, 4]) {
      const shown = selectedFor(role, round, {});
      assert.ok(
        PROMPTS.some((p) => p.id === shown && p.role === role && p.universal),
        `${role} round ${round} shows ${shown}, which is not this role's universal prompt`,
      );
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
