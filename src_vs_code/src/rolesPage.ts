import { FEATURE_CODE, FEATURE_DOCUMENT, PLAN_CODE, PLAN_DOCUMENT, RESULT_CODE, RESULT_DOCUMENT, bucketOf, composed, type RoleRow } from './roles';
import { ROLE_TABS, type RolesCommand } from './rolesMessages';
import { roleBlock, roleBlockOptions, stageIsFull, strandedHtml, tooOldFor, unknownServerNote } from './rolesBlocks';
import { formBodyCss } from './formPageStyle';
import { TEXT_CONTROLS_CSS, textControlsHtml, textControlsScript, textOf } from './textControls';
import type { Tombstone } from './roleDeletion';
import { ROLE_TONE_CSS } from './roleTone';
import { tabCss, tabStrip } from './tabStrip';
import { type BusySnapshot, IDLE } from './busySnapshot';
import { BUSY_BAR, BUSY_CSS, busyMarkScript } from './busyMark';

/**
 * The tab where a person writes a review role.
 *
 * <p>A page module and a thin panel host — the arrangement this extension has three times already
 * (the chat presets, the rounds log, the help page), and the one the operator named when they asked
 * for this: *"отдельный таб как на chat other ais - edit preset"*.</p>
 *
 * <p><b>Everything on this page is text the person in front of it wrote</b>, which makes escaping a
 * different question from the rest of the panel. Elsewhere a label comes from a catalog this product
 * shipped; here it is their own name for their own role, and somebody will paste a `&lt;script&gt;`
 * into it to see what happens. The answer must be that they see a `&lt;script&gt;`.</p>
 *
 * <p><b>What this page may not offer.</b> Every refusal it draws (`rolesBlocks.ts`) has a twin in `RoleComposition` on the
 * server, which is the boundary that actually holds — a page is not one. They are here so the page
 * never offers an action the server would refuse: a control that saves and then does nothing is
 * worse than one that is disabled with the reason beside it.</p>
 */

/** Where a page with no choice yet opens: the first section. */
export const DEFAULT_ROLE_TAB = 'plan';

/**
 * Which tab is open after a command — the whole rule, in one pure function.
 *
 * <p>It lives here rather than in `rolesPanel` because that module needs a VS Code host no test in
 * this suite can build, and `.agents/PROJECT.md` refuses a new behavioural assertion over source
 * text. The host keeps the variable; this decides what goes in it.</p>
 *
 * <p><b>Adding a ROLE moves you; adding a PROMPT does not.</b> A new role always joins the code
 * bucket of the result stage, so one created from the plan tab would land somewhere the person
 * cannot see — which is the complaint this page is being changed for. A prompt is created inside a
 * role they are already looking at, and moving the page under them would be the same defect with
 * the roles and the prompts swapped.</p>
 */
export function nextTab(current: string, command: RolesCommand): string {
  if (command.kind === 'tab') {
    return tabShown(command.id);
  }

  return command.kind === 'add' ? 'code' : tabShown(current);
}

/**
 * The tab to DRAW, whatever was asked for.
 *
 * <p>The host outlives the page and can be older or newer than it, so the value arriving here is
 * not trusted to be one this page has a section for. Exactly one tab is open in every case: there
 * is no arrangement in which a person is shown three sections at once, or none.</p>
 */
export function tabShown(tab: string): string {
  return ROLE_TABS.includes(tab) ? tab : DEFAULT_ROLE_TAB;
}

export interface RolesPageState {
  /**
   * Deletions that asked to happen and have not, with the reason the last attempt gave.
   *
   * <p>Shown because a tombstone that cannot clear would otherwise bar somebody from ever recreating
   * a role of that name, with nothing anywhere saying why — the 2026-09-16 incident's own shape,
   * moved one step along. Only the STRANDED ones reach here: a write that failed a second ago is a
   * write in flight, and showing a person the inside of one is worse than saying nothing.</p>
   */
  readonly stranded?: readonly Tombstone[];

  /** The rows as stored — a person's own roles and their edits to the shipped ones. */
  readonly rows: readonly RoleRow[];

  /** What the host had running when this tab was drawn: the busy mark's painted half (research/PLAN_busy_marks_on_every_webview.md). */
  readonly busy?: BusySnapshot;

  /**
   * The TEXT of each prompt, by prompt id.
   *
   * <p>Read from `&lt;dataDir&gt;/prompts/` by the host, because a body is a file rather than a
   * setting: twenty-five prompts of prose in `settings.json` would be copied into every
   * `mcpServers` block a person pastes, and shown to them in the settings editor as a wall of JSON
   * with their own writing inside it.</p>
   */
  readonly texts: Readonly<Record<string, string>>;

  /** The installed server, so the page can say when it is too old to read any of this. */
  readonly serverVersion: string;

  /** Whether these roles belong to this side alone — the panel's own switch, shown, not set here. */
  readonly perSide: boolean;

  readonly uiScale: number;

  /** How far the text is from the theme's own colour; absent is the theme's own, as on the help page. */
  readonly textTone?: number;

  /**
   * Which of the three tabs is open, held by the HOST rather than by the page.
   *
   * <p>Optional, and anything unrecognised is the default — the page is handed this value by a host
   * that may be older than it, and it must draw exactly one tab open whatever arrives. There is
   * nothing stored to migrate: it is a module variable in `rolesPanel`, alive for as long as the
   * window is.</p>
   */
  readonly tab?: string;
}

/** What each tab is called — the headings they replace, so nothing was renamed out from under anyone. */
const TAB_NAMES: Readonly<Record<string, string>> = {
  plan: 'Plan review',
  code: 'Code review',
  documents: 'Document review',
  feature: 'Feature review',
};

/**
 * The three tabs, through the shared strip.
 *
 * <p>This page's private copy was the best of the three in the extension — the only one with
 * `role="tablist"`, `aria-selected` AND `aria-controls` — so {@link tabStrip} was extracted from it
 * and this is its first caller. A reviewer pointed out on the plan round that extracting it and
 * leaving this copy in place would merely make a FOURTH copy, which is right.</p>
 *
 * <p>The markup is unchanged, and that is checked rather than claimed: this page's eighteen tab
 * tests pin `class="tab on" data-tab="documents"` as one string, and the whole page was rendered
 * for all five tab values before and after the conversion and compared byte for byte.</p>
 */
function rolesTabs(openTab: string): string {
  const tabs = ROLE_TABS.map((id) => ({ key: id, label: TAB_NAMES[id] }));

  return tabStrip(tabs, openTab, { tab: 'tab-', panel: 'section-', label: 'Which roles to edit' });
}

/**
 * The posts this tab numbers: the structural changes, each of which re-reads every prompt file and redraws the tab
 * (research/PLAN_busy_marks_on_every_webview.md, E3). `edit` and `editPrompt` are numbered only from a checkbox or a
 * select — switching a role on reads every prompt — and never from typing, which settles on its own for 300 ms.
 */
export const ROLES_TRACKED: readonly string[] = ['edit', 'editPrompt', 'add', 'addPrompt', 'removePrompt', 'restorePrompt', 'remove', 'finishDeletion'];

export function rolesHtml(state: RolesPageState, nonce: string): string {
  const all = composed(state.rows);
  // The catalog's switch alone, and the counts taken once for every block (E5.1b's code round, finding 1).
  const options = roleBlockOptions(all, 'data-prompt', {});
  const openTab = tabShown(state.tab ?? DEFAULT_ROLE_TAB);
  // By BUCKET, not by "plan and everything else". That filter was right while there were two
  // kinds of round, and drew a document role under a heading that was wrong about it the moment
  // there were three.
  const plan = all.filter((r) => bucketOf(r) === PLAN_CODE);
  const code = all.filter((r) => bucketOf(r) === RESULT_CODE);
  const documents = all.filter((r) => bucketOf(r) === RESULT_DOCUMENT);
  // The fourth bucket has no round yet, so it gets no section of its own: its rows are drawn
  // with the plan roles, where their stage says they belong, and their own hint says they take
  // part in nothing. A section for something nothing runs would be a promise the product has
  // not made.
  const waiting = all.filter((r) => bucketOf(r) === PLAN_DOCUMENT);
  // The fourth stage (S2.1 of the feature-review plan): its shipped role is drawn under a tab of its
  // own, and a feature-stage row that is not a programming task is drawn beside it the way
  // `plan:document` is drawn with the plan roles — stored, counted, run by nothing.
  const features = all.filter((r) => bucketOf(r) === FEATURE_CODE || bucketOf(r) === FEATURE_DOCUMENT);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Review roles</title>
${styles(textOf(state).size, textOf(state).tone)}
</head>
<body>
${BUSY_BAR}
<header><h1>Review roles</h1>${textControlsHtml(textOf(state).size, textOf(state).tone)}</header>
<p class="lead">The question each reviewer asks. Everything here is saved as you type${state.perSide ? ', for this side of the machine' : ''}.</p>
${strandedHtml(state.stranded ?? [])}
${tooOldFor(state.serverVersion, state.rows)}${unknownServerNote(state.serverVersion, state.rows)}

${rolesTabs(openTab)}

<section id="section-plan" role="tabpanel" aria-labelledby="tab-plan" data-section="plan"${openTab === 'plan' ? '' : ' hidden'}>
<p class="note">Roles that read the plan, before any code exists.</p>
${[...plan, ...waiting].map((r) => roleBlock(r, state.texts, options)).join('\n')}
</section>

<section id="section-code" role="tabpanel" aria-labelledby="tab-code" data-section="code"${openTab === 'code' ? '' : ' hidden'}>
<p class="note">Roles that read the change itself.</p>
${code.map((r) => roleBlock(r, state.texts, options)).join('\n')}
<!-- The control lives in the section its effect lands in. A new role always joins the code
     bucket of the result stage, so offered from the plan tab it was a button that quietly
     created something on another tab and moved the person there. (gemini, the code round.) -->
<button type="button" class="add role" data-add="role">Add a role</button>
${stageIsFull(all) ? '<p class="hint">Five roles are already active in the code stage, so a new one cannot be switched on until one of them is switched off.</p>' : ''}
</section>

<section id="section-documents" role="tabpanel" aria-labelledby="tab-documents" data-section="documents"${openTab === 'documents' ? '' : ' hidden'}>
<p class="note">Roles that read a document rather than a diff — what <code>review_document</code> runs. A role here never sees a checkout or a change.</p>
${documents.map((r) => roleBlock(r, state.texts, options)).join('\n')}
</section>

<section id="section-feature" role="tabpanel" aria-labelledby="tab-feature" data-section="feature"${openTab === 'feature' ? '' : ' hidden'}>
<p class="note">Roles that read a whole feature once every epic has landed — what <code>review_feature</code> runs: the plan, the epics, the implementer&#39;s lessons and an outline of every changed file, never the code itself unless the reviewer asks for it by name.</p>
${features.map((r) => roleBlock(r, state.texts, options)).join('\n')}
</section>

${script(nonce, state.busy ?? IDLE)}
</body>
</html>`;
}

function styles(uiScale: number, textTone: number): string {
  // The size and the tone INSIDE the body rule: written loose at the top of the sheet, as they once were,
  // a browser read them and the next rule as one invalid selector, and dropped both.
  return `<style>
${TEXT_CONTROLS_CSS}
/* The Chat presets column (formPageStyle.ts), the operator's ask of 2026-09-29. */
${formBodyCss(uiScale, textTone)}
header { display: flex; align-items: baseline; gap: 12px; }
h1 { font-size: 1.4em; }
h2 { font-size: 1.1em; margin: 20px 0 2px; }
/* The rounds log's strip, the same class names and the same metrics: two pages of this product
   with tabs that look different would be two products. */
${tabCss('10px 0')}
.lead, .note { opacity: 0.8; margin: 2px 0 10px; }
.note { font-size: 0.9em; }
/* The edge colour arrives from the .role-* rules above, which are the palette the sidebar spends
   on these same seven roles. It was one fixed blue here for as long as this page has existed. */
.role { border-left: 3px solid var(--tone-code); background: var(--vscode-textBlockQuote-background);
  padding: 6px 10px; margin: 8px 0; }
.role.off { opacity: 0.55; }
/* AFTER the rule above, and that is the whole of it: .role and .role-arch have equal
   specificity and the base rule sets the entire border-left shorthand, so a palette declared
   first is a palette that never wins. Two reviewers of the code round found this; the test that
   now guards it reads the ORDER, because there is no CSS engine in the suite to ask. */
${ROLE_TONE_CSS}
.role > summary { cursor: pointer; display: flex; align-items: baseline; gap: 8px; }
.role .title { font-weight: 600; }
.role .id, .badge { font-size: 0.82em; opacity: 0.65; }
.badge { border: 1px solid var(--vscode-panel-border); border-radius: 3px; padding: 0 4px; }
.fields { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 8px 0; }
.fields label { display: flex; gap: 6px; align-items: center; }
.fields .flag { gap: 4px; }
.hint { flex-basis: 100%; font-size: 0.85em; opacity: 0.75; margin: 0; }
.hint.warn { opacity: 1; color: var(--vscode-editorWarning-foreground); }
.prompt { border-left: 2px solid var(--vscode-panel-border); padding: 4px 8px; margin: 6px 0; }
/* A prompt of your own: framed on every side, and it STAYS framed. It is not a highlight of the
   last thing added — it is what the block is. Yours are the ones whose label, purpose and text are
   all editable; a shipped one has two readonly fields and a Restore. Until this, an added prompt
   landed among the shipped ones looking identical to them, which is issue #293's second picture. */
.prompt.mine { border: 1px solid var(--vscode-charts-green, #b5cea8); border-radius: 3px; }
.prompt .head { display: flex; gap: 6px; margin-bottom: 4px; }
.prompt input { flex: 1; }
.prompt .purpose { flex: 2; }
input, select, textarea { font-family: inherit; font-size: inherit; color: var(--vscode-input-foreground);
  background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, transparent); padding: 2px 4px; }
textarea { width: 100%; box-sizing: border-box; resize: vertical; }
button { font-family: inherit; font-size: inherit; color: var(--vscode-button-foreground);
  background: var(--vscode-button-background); border: none; padding: 3px 10px; cursor: pointer; }
button:disabled { opacity: 0.5; cursor: default; }
button.remove { background: var(--vscode-button-secondaryBackground); color: var(--vscode-button-secondaryForeground); }
button.role { margin-top: 10px; }
.stale { border-left: 3px solid var(--vscode-charts-yellow, #cca700); background: var(--vscode-textBlockQuote-background);
  padding: 6px 8px; font-size: 0.9em; margin: 8px 0; }
${BUSY_CSS}
</style>`;
}

function script(nonce: string, busy: BusySnapshot): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${busyMarkScript(busy, ROLES_TRACKED)}
  ${textControlsScript()}
  // Delegated on the document: every block is replaced whenever the rows change, and a listener
  // bound to one field would die with the block it was bound to.
  const roleOf = (el) => el.closest('[data-id]');
  const promptOf = (el) => el.closest('[data-prompt]');
  const sendField = (field) => {
    const role = roleOf(field);
    if (!role || !role.dataset || typeof field.dataset.field !== 'string') { return; }
    const prompt = promptOf(field);
    const value = field.type === 'checkbox' ? field.checked : field.value;
    // A pick is a change that makes the host work (switching a role on reads every prompt); typing settles on its own and
    // is never marked.
    const post = (message) => (field.type === 'checkbox' || field.tagName === 'SELECT' ? send(message, field) : vscode.postMessage(message));
    if (prompt && prompt.dataset) {
      post({ type: 'editPrompt', id: role.dataset.id, promptId: prompt.dataset.prompt,
                           field: field.dataset.field, value: value });
      return;
    }
    post({ type: 'edit', id: role.dataset.id, field: field.dataset.field, value: value });
  };
  // A checkbox and a select are sent by 'change' below. The browser fires 'input' for them TOO, and
  // sending from both made one press two commands - two reads of every prompt and two refusals for one
  // click once switching a role on could be refused. (Our own code review of issue #338.)
  document.addEventListener('input', function (event) {
    const typed = event.target;
    if (typed && typed.dataset && typed.type !== 'checkbox' && typed.tagName !== 'SELECT') { sendField(typed); }
  });
  document.addEventListener('change', function (event) {
    const field = event.target;
    if (field && field.dataset && (field.type === 'checkbox' || field.tagName === 'SELECT')) { sendField(field); }
  });
  document.addEventListener('click', function (event) {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    // The tab, FIRST and with its own return: this is the one control on the page that acts here
    // rather than only asking the host. It switches now so the page does not wait on a round trip,
    // and it posts so the next repaint — which every add and every switch causes — keeps it.
    const tab = pressed.closest('[data-tab]');
    if (tab && tab.dataset) {
      const which = tab.dataset.tab;
      const strip = document.querySelectorAll('[data-tab]');
      for (let i = 0; i < strip.length; i += 1) {
        const isOpen = strip[i].dataset.tab === which;
        strip[i].className = isOpen ? 'tab on' : 'tab';
        // In step with the class, or a screen reader goes on announcing the tab that was open
        // before the press — which is worse than never having said which one it was.
        if (strip[i].setAttribute) { strip[i].setAttribute('aria-selected', isOpen ? 'true' : 'false'); }
      }
      const sections = document.querySelectorAll('[data-section]');
      for (let i = 0; i < sections.length; i += 1) {
        sections[i].hidden = sections[i].dataset.section !== which;
      }
      vscode.postMessage({ type: 'tab', id: which });
      return;
    }
    if (pressed.closest('[data-reload]')) { vscode.postMessage({ type: 'reloadWindow' }); return; }
    const finish = pressed.closest('[data-finish]');
    if (finish) { send({ type: 'finishDeletion', id: finish.dataset.finish }, finish); return; }
    if (pressed.closest('[data-add="role"]')) { send({ type: 'add' }, pressed); return; }
    const addPrompt = pressed.closest('[data-add-prompt]');
    if (addPrompt) { send({ type: 'addPrompt', id: addPrompt.dataset.addPrompt }, addPrompt); return; }
    const removePrompt = pressed.closest('[data-remove-prompt]');
    if (removePrompt) {
      const role = roleOf(removePrompt);
      send({ type: 'removePrompt', id: role ? role.dataset.id : '',
                           promptId: removePrompt.dataset.removePrompt }, removePrompt);
      return;
    }
    const restore = pressed.closest('[data-restore]');
    if (restore) {
      const role = roleOf(restore);
      send({ type: 'restorePrompt', id: role ? role.dataset.id : '',
                           promptId: restore.dataset.restore }, restore);
      return;
    }
    const remove = pressed.closest('[data-remove]');
    if (remove && remove.dataset) { send({ type: 'remove', id: remove.dataset.remove }, remove); }
  });
  // LAST: this tab is ready to hear what is running (busyMark.ts).
  vscode.postMessage({ type: 'ready' });
}());
</script>`;
}
