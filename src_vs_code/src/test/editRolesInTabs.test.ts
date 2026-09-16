import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUILTIN_ROLES } from '../builtinRoles.generated';
import { DEFAULTS } from '../settingsShape';
import { SNIPPET_VERSION } from '../claudeSnippet';
import { panelHtml, type PanelState } from '../panelView';
import { CUSTOM_ROLES_SINCE, DEFAULT_ROLE_TAB, ROLE_TABS, nextTab, roleEdit, rolesHtml, type RolesPageState } from '../rolesPage';
import { PLAN_STAGE, RESULT_STAGE, type RoleRow } from '../roles';
import { Node, runRolesPage } from './rolesPageHarness';

/**
 * The Edit-roles page, divided — and coloured the way the sidebar already colours these roles.
 *
 * <p>Issue #293, in three parts: the page is one column of everything, a prompt you add lands among
 * the shipped ones looking identical to them, and every left edge here is the same blue while the
 * sidebar has given each role its own tone since the settings panel was written.</p>
 *
 * <p><b>The tab is held by the HOST, and that is the part that is not decoration.</b> `apply` in
 * `rolesPanel.ts` returns whether the page must be redrawn, and a redraw replaces
 * `panel.webview.html` wholesale — a new document with no memory of anything. Every shape-changing
 * action redraws: add a prompt, add a role, a switch, a stage, a remove, a restore. A page-local tab
 * would mean pressing "Add a prompt" on an architecture role and being thrown back to the plan tab,
 * with the prompt just added on a tab you can no longer see — the complaint in the issue, made
 * worse. So the transition is a pure function, {@link nextTab}, and it is RUN here rather than read:
 * `.agents/PROJECT.md` refuses a new behavioural assertion over page source text, and the code round
 * of the plan named this test in particular.</p>
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

const state = (over: Partial<RolesPageState> = {}): RolesPageState => ({
  rows: [mine],
  texts: {},
  serverVersion: CUSTOM_ROLES_SINCE,
  perSide: false,
  uiScale: 0,
  ...over,
});

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

// ---------- the tabs ----------

test('the three sections are three tabs, and exactly one of them is open', () => {
  const html = rolesHtml(state(), 'n0nce');

  const tabs = [...html.matchAll(/data-tab="([a-z]+)"/gu)].map((m) => m[1]);
  assert.deepEqual(tabs, [...ROLE_TABS], 'the page does not offer the three sections as tabs');

  const on = [...html.matchAll(/class="tab on" data-tab="([a-z]+)"/gu)].map((m) => m[1]);
  assert.deepEqual(on, [DEFAULT_ROLE_TAB], 'a page with no choice yet opens on none or on several');
});

test('the tab the host is holding is the open one, and the other two sections are hidden', () => {
  const html = rolesHtml(state({ tab: 'documents' }), 'n0nce');

  assert.match(html, /class="tab on" data-tab="documents"/u, 'the held tab is not the open one');
  // The sections themselves, not the buttons: a strip that highlights one tab over three visible
  // sections is the page this issue is about, with a decoration added to it.
  const shown = [...html.matchAll(/data-section="([a-z]+)"([^>]*)>/gu)]
    .filter((m) => !m[2].includes('hidden'))
    .map((m) => m[1]);
  assert.deepEqual(shown, ['documents'], 'the page is showing the wrong number of sections');
});

test('a tab the page does not know draws the default rather than nothing', () => {
  // The host can be older than the page, or newer. Whatever arrives, exactly one tab is open —
  // there is no state here to migrate, and no arrangement in which a person sees a blank page.
  for (const held of ['', 'spots', 'PLAN', '../plan']) {
    const html = rolesHtml(state({ tab: held }), 'n0nce');
    const on = [...html.matchAll(/class="tab on" data-tab="([a-z]+)"/gu)].map((m) => m[1]);

    assert.deepEqual(on, [DEFAULT_ROLE_TAB], `a tab held as ${JSON.stringify(held)} opened ${on.length} tabs`);
  }
});

test('pressing a tab switches the page AND tells the host, in that order of importance', () => {
  // RUN, not read. The page must switch without waiting for a round trip — and it must also post,
  // or the next repaint (which every add and every switch causes) throws the person back.
  // The nodes are built from what the page ACTUALLY rendered, not from ROLE_TABS: if the markup
  // and the script ever disagreed about the attribute — data-panel drawn, data-section queried —
  // a fixture written from the constant would hide it, and the switch would do nothing on a real
  // page while this test stayed green. (codex, the code round.)
  const html = rolesHtml(state(), 'n0nce');
  const drawn = (attribute: string): Node[] =>
    [...html.matchAll(new RegExp(`data-${attribute}="([a-z]+)"`, 'gu'))]
      .map((m) => new Node({ [attribute]: m[1] }, attribute === 'tab' ? 'BUTTON' : 'SECTION'));

  const buttons = drawn('tab');
  const sections = drawn('section');
  assert.deepEqual(buttons.map((b) => b.dataset['tab']), [...ROLE_TABS], 'the page drew a different set of tabs');
  assert.deepEqual(sections.map((x) => x.dataset['section']), [...ROLE_TABS], 'the sections are not the tabs');

  const page = runRolesPage(state(), { '[data-tab]': buttons, '[data-section]': sections });

  page.fire('click', buttons[1]);

  assert.deepEqual(
    page.posted.filter((m) => m['type'] === 'tab'),
    [{ type: 'tab', id: 'code' }],
    'pressing a tab told the host nothing, so the next repaint will undo it',
  );
  assert.deepEqual(buttons.map((b) => b.className), ['tab', 'tab on', 'tab'],
    'the strip does not show which tab is open');
  assert.deepEqual(buttons.map((b) => b.attributes['aria-selected']), ['false', 'true', 'false'],
    'a screen reader is still being told the tab that was open before the press');
  assert.deepEqual(sections.map((s) => s.hidden), [true, false, true],
    'the page did not switch what is on screen');
});

test('a message naming a tab this page has no section for is ignored, not stored', () => {
  // The page is not trusted — a retained webview can be older or newer than the extension. The two
  // halves guard separately: this one refuses to store it, and rolesHtml above draws the default
  // whatever it is handed.
  assert.deepEqual(roleEdit({ type: 'tab', id: 'code' }), { kind: 'tab', id: 'code' });
  assert.deepEqual(roleEdit({ type: 'tab', id: 'spots' }), { kind: 'ignore' });
  assert.deepEqual(roleEdit({ type: 'tab', id: '' }), { kind: 'ignore' });
  assert.deepEqual(roleEdit({ type: 'tab' }), { kind: 'ignore' });
});

// ---------- what moves the tab, and what must not ----------

test('choosing a tab moves to it and nothing else does, except adding a role', () => {
  // Adding a ROLE moves you to the code tab, because a new role always joins the code bucket of the
  // result stage — created from the plan tab it would land somewhere you cannot see.
  assert.equal(nextTab('plan', { kind: 'tab', id: 'documents' }), 'documents');
  assert.equal(nextTab('plan', { kind: 'add' }), 'code');
});

test('adding a PROMPT leaves you where you are', () => {
  // The distinction the plan round asked to be made explicit: only a role can be created somewhere
  // you are not looking. A prompt is created inside a role you are already looking at, and moving
  // the page under someone who pressed "Add a prompt" is the defect this issue reports.
  assert.equal(nextTab('plan', { kind: 'addPrompt', id: 'PlanCritique' }), 'plan');
  assert.equal(nextTab('documents', { kind: 'addPrompt', id: 'DocumentReview' }), 'documents');
});

test('every other command leaves the tab exactly where it was', () => {
  const here = 'documents';

  assert.equal(nextTab(here, { kind: 'edit', id: 'Requirements', field: 'active', value: false }), here);
  assert.equal(nextTab(here, { kind: 'editPrompt', id: 'Requirements', promptId: 'p', field: 'text', value: 'x' }), here);
  assert.equal(nextTab(here, { kind: 'removePrompt', id: 'Requirements', promptId: 'p' }), here);
  assert.equal(nextTab(here, { kind: 'restorePrompt', id: 'Architecture', promptId: 'architecture' }), here);
  assert.equal(nextTab(here, { kind: 'remove', id: 'Requirements' }), here);
  assert.equal(nextTab(here, { kind: 'zoom', delta: 1 }), here);
  assert.equal(nextTab(here, { kind: 'ignore' }), here);
});

test('a tab the host is holding that this page cannot draw settles to the default', () => {
  // The host outlives the page and can be holding a word from a version that had a fourth section.
  assert.equal(nextTab('spots', { kind: 'ignore' }), DEFAULT_ROLE_TAB);
  assert.equal(nextTab('', { kind: 'addPrompt', id: 'Requirements' }), DEFAULT_ROLE_TAB);
});

// ---------- the green frame ----------

test('a prompt you added is framed and a shipped one is not', () => {
  const html = rolesHtml(state(), 'n0nce');

  // Both halves, so this cannot pass by framing every prompt on the page.
  assert.match(html, /<div class="prompt mine" data-prompt="requirements-general"/u,
    'the prompt this person wrote is not marked as theirs');
  assert.match(html, /<div class="prompt" data-prompt="architecture"/u,
    'a prompt this product ships was marked as the person\'s own');
});

test('a shipped prompt you have rewritten is still a shipped prompt', () => {
  // The green says who OWNS the block — whose label, purpose and text are all theirs to edit — not
  // whether it has been typed in. An overridden shipped prompt keeps its Restore, and keeps its edge.
  const html = rolesHtml(state({ texts: { architecture: 'My own words for it.' } }), 'n0nce');

  assert.match(html, /<div class="prompt" data-prompt="architecture"/u,
    'overriding a shipped prompt turned it into one of the person\'s own');
  assert.match(html, /data-restore="architecture"/u, 'the overridden prompt lost the way back');
});

test('a prompt of your own inside a SHIPPED role is yours too', () => {
  // The case the issue's second picture actually shows: "Add a prompt" pressed on a built-in role.
  const added: RoleRow = { id: 'Architecture', prompts: [{ id: 'arch-mine', label: 'Mine' }] };
  const html = rolesHtml(state({ rows: [added] }), 'n0nce');

  assert.match(html, /<div class="prompt mine" data-prompt="arch-mine"/u,
    'a prompt added to a shipped role was drawn as though the product shipped it');
});

test('the stylesheet frames the whole block, on every side, in green', () => {
  // A class with no rule behind it is invisible, and this suite has no CSS engine to notice. The
  // class assertions above all pass with `.prompt.mine` missing, misspelled, or setting only the
  // left edge the block already had.
  const html = rolesHtml(state(), 'n0nce');
  const rule = /\.prompt\.mine\s*\{([^}]*)\}/u.exec(html);

  assert.ok(rule, 'nothing in the stylesheet colours a prompt of your own');
  assert.match(rule[1], /(^|[^-])border:\s*[^;]*green/u, 'the frame is not a border on all four sides, in green');
  assert.match(rule[1], /var\(--vscode-charts-green,\s*#[0-9a-f]{6}\)/u,
    'the green has no fallback, so a theme without a charts palette draws no frame at all');
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
  const inPanel = panelHtml(panel(), 'n0nce');
  const inPage = rolesHtml(state(), 'n0nce');

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
  const inPage = rolesHtml(state(), 'n0nce');
  const tones = BUILTIN_ROLES.filter((r) => r.programmingTask !== false).map((r) => toneInPage(inPage, r.id));

  assert.equal(new Set(tones).size, tones.length, `the five shipped roles share colours: ${tones.join(', ')}`);
});

test('a role of your own takes its colour from its stage, in both views', () => {
  const planSide: RoleRow = { id: 'Assumptions', name: 'Assumptions', stage: PLAN_STAGE, active: true, prompts: [] };
  const inPage = rolesHtml(state({ rows: [mine, planSide] }), 'n0nce');
  const inPanel = panelHtml(panel({ settings: { ...DEFAULTS, roles: [mine, planSide] } }), 'n0nce');

  assert.equal(toneInPage(inPage, 'Assumptions'), toneInPanel(inPanel, 'Assumptions'));
  assert.equal(toneInPage(inPage, 'Requirements'), toneInPanel(inPanel, 'Requirements'));
  assert.notEqual(toneInPage(inPage, 'Assumptions'), toneInPage(inPage, 'Requirements'));
});

test('the page defines the tones it uses, so the edges are not a class with no rule', () => {
  const html = rolesHtml(state(), 'n0nce');

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
  const html = rolesHtml(state(), 'n0nce');
  const css = html.slice(html.indexOf('<style'), html.indexOf('</style>'));

  const base = css.indexOf('.role { border-left');
  assert.ok(base >= 0, 'the base rule was renamed, and this test can no longer see what it guards');
  for (const tone of ['plan', 'arch', 'sec', 'uxdx', 'conv', 'code']) {
    const at = css.indexOf(`.role-${tone} {`);
    assert.ok(at > base, `.role-${tone} is declared before .role, so the base border-left overrides it`);
  }
});
