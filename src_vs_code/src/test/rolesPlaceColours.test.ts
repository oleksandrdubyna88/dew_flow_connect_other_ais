import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { type PanelState } from '../panelView';
import { everyPageHtml } from './panelPages';
import { catalogHtml } from '../catalogPage';
import { roleEdit } from '../rolesMessages';
import { PLAN_STAGE, RESULT_STAGE, type RoleRow } from '../roles';
import { rolesPanelState, type RolesPlaceState } from './rolesPlaceHarness';

/**
 * The roles' editor, coloured the way the sidebar colours these roles — Reviews › Roles & prompts on the Settings page.
 *
 * <p>Issue #293, in three parts: the page was one column of everything, a prompt you add landed among the shipped ones
 * looking identical to them, and every left edge was the same blue while the sidebar had given each role its own tone.
 * The Review roles tab answered it with tabs, a green frame and the sidebar's palette; E5.1 step 4 of
 * todo/PLAN_one_model_catalog.md deleted that tab, and its place on the Settings page draws the stages as headed groups
 * (`rolesPlace.test.ts`, "the plan stage and the code stage are drawn apart") — which place is open is held by the host
 * (`catalogPlaces.test.ts`). The frame and the palette are asked of the place here, in the WHOLE document, whose one
 * stylesheet the place is drawn with.</p>
 *
 */

const mine: RoleRow = {
  id: 'Requirements',
  name: 'Requirements we wrote',
  stage: RESULT_STAGE,
  active: true,
  prompts: [
    { id: 'requirements-general', label: 'General', purpose: 'Whether it is met.' },
  ],
};

/** The whole Settings page on Roles & prompts, with one role of the person's own. */
const pageWith = (over: RolesPlaceState = {}): string => catalogHtml(rolesPanelState({ rows: [mine], ...over }), 'n0nce', 'reviews/roles');

const panel = (over: Partial<PanelState> = {}): PanelState => ({
  settings: { ...DEFAULTS, roles: [mine] },
  vendors: [],
  agyModels: [],
  codexModels: [],
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
  ...over,
});

// ---------- the tab message the Review roles tab posted ----------

test('a message naming a tab this page has no section for is ignored, not stored', () => {
  // The Review roles tab posted its tab (gone with it in E5.1 step 4); the parser both pages share still reads one, and a
  // retained webview can be older than the extension — so what it is handed is still refused when it names nothing.
  assert.deepEqual(roleEdit({ type: 'tab', id: 'code' }), { kind: 'tab', id: 'code' });
  assert.deepEqual(roleEdit({ type: 'tab', id: 'spots' }), { kind: 'ignore' });
  assert.deepEqual(roleEdit({ type: 'tab', id: '' }), { kind: 'ignore' });
  assert.deepEqual(roleEdit({ type: 'tab' }), { kind: 'ignore' });
});

// ---------- the green frame ----------

test('a prompt you added is framed and a shipped one is not', () => {
  const html = pageWith();

  // Both halves, so this cannot pass by framing every prompt on the page.
  assert.match(html, /<div class="prompt mine" data-role-prompt="requirements-general"/u,
    'the prompt this person wrote is not marked as theirs');
  assert.match(html, /<div class="prompt" data-role-prompt="architecture"/u,
    'a prompt this product ships was marked as the person\'s own');
});

test('a shipped prompt you have rewritten is still a shipped prompt', () => {
  // The green says who OWNS the block — whose label, purpose and text are all theirs to edit — not
  // whether it has been typed in. An overridden shipped prompt keeps its Restore, and keeps its edge.
  const html = pageWith({ texts: { architecture: 'My own words for it.' } });

  assert.match(html, /<div class="prompt" data-role-prompt="architecture"/u,
    'overriding a shipped prompt turned it into one of the person\'s own');
  assert.match(html, /data-restore="architecture"/u, 'the overridden prompt lost the way back');
});

test('a prompt of your own inside a SHIPPED role is yours too', () => {
  // The case the issue's second picture actually shows: "Add a prompt" pressed on a built-in role.
  const added: RoleRow = { id: 'Architecture', prompts: [{ id: 'arch-mine', label: 'Mine' }] };
  const html = pageWith({ rows: [added] });

  assert.match(html, /<div class="prompt mine" data-role-prompt="arch-mine"/u,
    'a prompt added to a shipped role was drawn as though the product shipped it');
});

test('the stylesheet frames the whole block, on every side, in green', () => {
  // A class with no rule behind it is invisible, and this suite has no CSS engine to notice. The
  // class assertions above all pass with `.prompt.mine` missing, misspelled, or setting only the
  // left edge the block already had.
  const html = pageWith();
  const rule = /\.roles-embed \.prompt\.mine\s*\{([^}]*)\}/u.exec(html);

  assert.ok(rule, 'nothing in the stylesheet colours a prompt of your own');
  assert.match(rule[1], /(^|[^-])border:\s*[^;]*green/u, 'the frame is not a border on all four sides, in green');
  // The place's fallback is the page's own `--ok` token, which ends in a colour of its own.
  assert.match(rule[1], /var\(--vscode-charts-green,\s*var\(--ok\)\)/u,
    'the green has no fallback, so a theme without a charts palette draws no frame at all');
  assert.match(html, /--ok:\s*var\([^,]+,\s*#[0-9a-f]{6}\)/u, 'the fallback token has no colour of its own');
});

// ---------- the sidebar's colours ----------

/** The tone class the PANEL gives a role. Anchored on the rounds box, which every role box has. */
function toneInPanel(html: string, roleId: string): string {
  const box = html.split('<div class="role role-').find((part) => part.includes(`id="rounds-${roleId}"`));
  assert.ok(box, `the panel drew no box for ${roleId}`);

  return box.slice(0, box.indexOf('"')).split(' ')[0];
}

/** The tone class the EDIT PAGE gives a role. */
function toneInPage(html: string, roleId: string): string {
  const found = new RegExp(`<details class="role ([a-z-]+)[^"]*" data-id="${roleId}"`, 'u').exec(html);
  assert.ok(found, `the roles page drew no block for ${roleId}`);

  return found[1].replace('role-', '');
}

test('the same role is the same colour in the panel and on the page it is edited from', () => {
  // The whole of part three, and the only assertion that can see the defect: a test of the palette
  // module alone stays green while either renderer keeps a private copy of the map — which is
  // exactly today's state, one file further on.
  const inPanel = everyPageHtml(panel(), 'n0nce');
  const inPage = pageWith();

  // From the catalog, not a list retyped here: a role added to BUILTIN_ROLES would otherwise be
  // covered by nothing, and the two views could disagree about it forever. (gemini, the code round.)
  for (const roleId of [...BUILTIN_ROLES.map((r) => r.id), mine.id]) {
    assert.equal(
      toneInPage(inPage, roleId),
      toneInPanel(inPanel, roleId),
      `${roleId} is a different colour in the two views`,
    );
  }
});

test('the roles those tones name are actually different colours from one another', () => {
  // A palette that answered the same word for everything would pass the agreement test above while
  // leaving the page exactly as it is today: every edge the same blue.
  //
  // The five roles that have a tone of their OWN, which is the five programming ones. The two
  // document roles deliberately share the result stage's fallback, so including them here would be
  // asserting against a decision rather than for one.
  const inPage = pageWith();
  const tones = BUILTIN_ROLES.filter((r) => r.programmingTask !== false).map((r) => toneInPage(inPage, r.id));

  assert.equal(new Set(tones).size, tones.length, `the five shipped roles share colours: ${tones.join(', ')}`);
});

test('a role of your own takes its colour from its stage, in both views', () => {
  const planSide: RoleRow = { id: 'Assumptions', name: 'Assumptions', stage: PLAN_STAGE, active: true, prompts: [] };
  const inPage = pageWith({ rows: [mine, planSide] });
  const inPanel = everyPageHtml(panel({ settings: { ...DEFAULTS, roles: [mine, planSide] } }), 'n0nce');

  assert.equal(toneInPage(inPage, 'Assumptions'), toneInPanel(inPanel, 'Assumptions'));
  assert.equal(toneInPage(inPage, 'Requirements'), toneInPanel(inPanel, 'Requirements'));
  assert.notEqual(toneInPage(inPage, 'Assumptions'), toneInPage(inPage, 'Requirements'));
});

test('the page defines the tones it uses, so the edges are not a class with no rule', () => {
  const html = pageWith();

  assert.match(html, /--tone-plan:/u, 'the page carries none of the palette it now names');
  assert.match(html, /\.role-arch\s*\{\s*border-left-color:\s*var\(--tone-arch\)/u,
    'the tone class colours nothing');
});

test('the tone rules come after the base rule they have to beat', () => {
  // The defect two reviewers of the code round found, and the reason the class-name tests above
  // could not: `.role` and `.role-arch` have EQUAL specificity, so the later one wins, and `.role`
  // sets the whole `border-left` shorthand. Emitted first, the palette is overwritten and every
  // edge on the page renders in one colour — which is the state issue #293 is about, with the
  // classes added. There is no CSS engine here, so the cascade is read as order.
  const html = pageWith();
  const css = html.slice(html.indexOf('<style'), html.indexOf('</style>'));

  const base = css.indexOf('.role { border:');
  assert.ok(base >= 0, 'the base rule was renamed, and this test can no longer see what it guards');
  for (const tone of ['plan', 'arch', 'sec', 'uxdx', 'conv', 'code']) {
    const at = css.indexOf(`.role-${tone} {`);
    assert.ok(at > base, `.role-${tone} is declared before .role, so the base border-left overrides it`);
  }
});
