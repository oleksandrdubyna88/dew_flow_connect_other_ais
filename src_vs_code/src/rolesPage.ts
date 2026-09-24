import { MAX_ACTIVE_PER_BUCKET, PLAN_CODE, PLAN_DOCUMENT, PLAN_STAGE, RESULT_CODE, RESULT_DOCUMENT, RESULT_STAGE, activeCount, bucketOf, builtInFor, composed, isActive, isBuiltIn, isProgramming, stageOf, whyNotAskable, type RoleRow } from './roles';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';
import { STOOD_DOWN, type Tombstone } from './roleDeletion';
import { escapeHtml } from './webviewHtml';
import { ROLE_TONE_CSS, roleTone } from './roleTone';
import { tabCss, tabStrip } from './tabStrip';

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
 * <p><b>What this page may not offer.</b> Every refusal below has a twin in `RoleComposition` on the
 * server, which is the boundary that actually holds — a page is not one. They are here so the page
 * never offers an action the server would refuse: a control that saves and then does nothing is
 * worse than one that is disabled with the reason beside it.</p>
 */

/**
 * How tall a prompt box starts.
 *
 * <p>A prompt is often a paragraph and sometimes several, and a box three lines high is reading one
 * three lines at a time. It is a starting HEIGHT and nothing else — the box scrolls, it is
 * `resize: vertical`, and what it holds is its value however much of it is on screen.</p>
 */
const PROMPT_ROWS = 10;

/** The three tabs this page is divided into, in the order they are drawn. */
export const ROLE_TABS: readonly string[] = ['plan', 'code', 'documents'];

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

/** Every message this page can send, decided without a host so a test can reach the decision. */
export type RolesCommand =
  | { readonly kind: 'add' }
  | { readonly kind: 'remove'; readonly id: string }
  | { readonly kind: 'edit'; readonly id: string; readonly field: RoleField; readonly value: string | boolean }
  | { readonly kind: 'addPrompt'; readonly id: string }
  | { readonly kind: 'removePrompt'; readonly id: string; readonly promptId: string }
  | { readonly kind: 'editPrompt'; readonly id: string; readonly promptId: string; readonly field: PromptField; readonly value: string }
  | { readonly kind: 'restorePrompt'; readonly id: string; readonly promptId: string }
  | { readonly kind: 'zoom'; readonly delta: number }
  /** Which of the three sections is open — decided on the page, remembered by the host. */
  | { readonly kind: 'tab'; readonly id: string }
  /** A deletion the mirror could not carry, finished locally with the cost accepted. */
  | { readonly kind: 'finishDeletion'; readonly id: string }
  /** The cure for a stand-down, offered beside the deletion it is stuck behind. */
  | { readonly kind: 'reloadWindow' }
  | { readonly kind: 'ignore' };

const IGNORE: RolesCommand = { kind: 'ignore' };

/** The fields a ROLE has. A name this does not know is not a field — `__proto__` included. */
const ROLE_FIELDS = ['name', 'stage', 'programmingTask', 'active'] as const;
type RoleField = (typeof ROLE_FIELDS)[number];

/** The fields a PROMPT has. `text` is the body; it goes to a file rather than into the setting. */
const PROMPT_FIELDS = ['label', 'purpose', 'text'] as const;
type PromptField = (typeof PROMPT_FIELDS)[number];

/** The two fields that are booleans on the wire; everything else arrives as a string. */
const FLAGS: readonly string[] = ['programmingTask', 'active'];

function idOf(value: unknown): string {
  return typeof value === 'string' && value.length > 0 ? value : '';
}

/**
 * One message from the page, as a command — or `ignore`.
 *
 * <p>Pure, and separate from the host, so every shape a webview can post is reachable from a test.
 * A field is checked against a LIST of names rather than with `in`, so no key of `Object.prototype`
 * can be one.</p>
 */
export function roleEdit(message: unknown): RolesCommand {
  if (typeof message !== 'object' || message === null) {
    return IGNORE;
  }

  const said = message as Record<string, unknown>;
  const type = said['type'];
  if (type === 'zoom') {
    const delta = said['delta'];

    return typeof delta === 'number' && Number.isFinite(delta)
      ? { kind: 'zoom', delta: Math.max(-1, Math.min(1, Math.trunc(delta))) }
      : IGNORE;
  }
  if (type === 'add') {
    return { kind: 'add' };
  }
  if (type === 'reloadWindow') {
    return { kind: 'reloadWindow' };
  }
  if (type === 'finishDeletion') {
    // Through `idOf`, like every other id off this page: the value reaches a file path, and a page
    // that can be older or newer than the extension it talks to is not a source this side trusts.
    const id = idOf(said['id']);

    return id === '' ? IGNORE : { kind: 'finishDeletion', id };
  }
  if (type === 'tab') {
    // Against the LIST, so a word this page has no section for is never stored. The drawing side
    // normalises too — two halves, neither trusting the other, because a retained webview can be
    // older or newer than the extension it is talking to.
    const wanted = idOf(said['id']);

    return ROLE_TABS.includes(wanted) ? { kind: 'tab', id: wanted } : IGNORE;
  }

  const id = idOf(said['id']);
  if (id.length === 0) {
    return IGNORE;
  }
  if (type === 'remove') {
    return { kind: 'remove', id };
  }
  if (type === 'addPrompt') {
    return { kind: 'addPrompt', id };
  }
  if (type === 'removePrompt' || type === 'restorePrompt' || type === 'editPrompt') {
    return promptCommand(type, id, said);
  }
  if (type !== 'edit') {
    return IGNORE;
  }

  const field = said['field'];
  if (typeof field !== 'string' || !(ROLE_FIELDS as readonly string[]).includes(field)) {
    return IGNORE;
  }

  const value = said['value'];
  const wanted = FLAGS.includes(field) ? 'boolean' : 'string';

  return typeof value === wanted
    ? { kind: 'edit', id, field: field as RoleField, value: value as string | boolean }
    : IGNORE;
}

function promptCommand(type: string, id: string, said: Record<string, unknown>): RolesCommand {
  const promptId = idOf(said['promptId']);
  if (promptId.length === 0) {
    return IGNORE;
  }
  if (type === 'removePrompt') {
    return { kind: 'removePrompt', id, promptId };
  }
  if (type === 'restorePrompt') {
    return { kind: 'restorePrompt', id, promptId };
  }

  const field = said['field'];
  const value = said['value'];
  if (typeof field !== 'string' || !(PROMPT_FIELDS as readonly string[]).includes(field) || typeof value !== 'string') {
    return IGNORE;
  }

  return { kind: 'editPrompt', id, promptId, field: field as PromptField, value };
}

/** Whether a prompt is one this product ships — its text is embedded, so it cannot be deleted. */
export function isShippedPrompt(roleId: string, promptId: string): boolean {
  return builtInFor(roleId)?.prompts.some((p) => p.id === promptId) === true;
}

/**
 * Whether this role's switch may be turned ON.
 *
 * <p>Five active per BUCKET, and the sixth is refused HERE rather than dropped by the server. A page
 * that let somebody tick a sixth and then showed them five would be a page that lies about what it
 * saved — the server does cap, and it names what it capped, so nothing is at risk except the truth.</p>
 */
export function canActivate(rows: readonly RoleRow[], role: RoleRow): boolean {
  return isActive(role) || activeCount(rows, bucketOf(role)) < MAX_ACTIVE_PER_BUCKET;
}

/**
 * Whether this role's switch may be turned OFF.
 *
 * <p>The last one standing in a bucket cannot be: a bucket with no role in it produces a round with
 * no reviewer, which the session counts as unresolved and never lets a person retry. The server
 * refuses such a round with a sentence — `review_plan` and `review_code` both do — and refusing it
 * HERE, where the pointer is, beats refusing it at round time, after somebody has waited for a
 * review that was never going to happen.</p>
 *
 * <p>It is the rule the sidebar's code-role ticks have had since they shipped, applied to the plan
 * stage as well, which is what the operator asked for when they asked for plan-stage switches.</p>
 */
export function canDeactivate(rows: readonly RoleRow[], role: RoleRow): boolean {
  return !isActive(role) || activeCount(rows, bucketOf(role)) > 1;
}

/**
 * What a role's KIND means for whether it runs — said only where the answer is surprising.
 *
 * <p>Until plan 4 every non-programming role ran in nothing and every one of them carried the same
 * sentence. Now one of the two buckets HAS a round, so repeating it there would be a page saying a
 * role does nothing while the round it takes part in is running — and dropping it from the other
 * would leave the one case that is still true unsaid. "Not built yet" and "does not exist" are
 * different states, and this is where a person meets the difference.</p>
 */
function kindHint(role: RoleRow): string {
  if (isProgramming(role) || stageOf(role) !== PLAN_STAGE) {
    return '';
  }

  return '<p class="hint">A plan-stage role that is not a programming task is kept and takes part in '
    + 'no round yet — there is no plan gate for non-programming work. Move it to the result stage and '
    + '<code>review_document</code> will run it.</p>';
}

function promptBlock(role: RoleRow, prompt: { id: string; label?: string; purpose?: string }, texts: Readonly<Record<string, string>>): string {
  const shipped = isShippedPrompt(role.id, prompt.id);
  const text = texts[prompt.id] ?? '';

  return `  <div class="prompt${shipped ? '' : ' mine'}" data-prompt="${escapeHtml(prompt.id)}">
    <div class="head">
      <input type="text" data-field="label" value="${escapeHtml(prompt.label ?? prompt.id)}" placeholder="What the picker shows"${shipped ? ' readonly' : ''}>
      <input type="text" class="purpose" data-field="purpose" value="${escapeHtml(prompt.purpose ?? '')}" placeholder="The picker's tooltip"${shipped ? ' readonly' : ''}>
      ${shipped
        ? `<button type="button" class="restore" data-restore="${escapeHtml(prompt.id)}"${text.length === 0 ? ' disabled' : ''}>Restore</button>`
        : `<button type="button" class="remove" data-remove-prompt="${escapeHtml(prompt.id)}">Remove</button>`}
    </div>
    <textarea data-field="text" rows="${PROMPT_ROWS}" placeholder="${shipped ? 'The text this product ships. Write here to replace it.' : 'The question this prompt asks.'}">${escapeHtml(text)}</textarea>
  </div>`;
}

/**
 * The mark on an ACTIVE role that will not be asked — issue #338, for the routes the refusal to switch
 * one on cannot see: a text erased after the switch, a settings file edited by hand, a prompt file
 * deleted. Until this the only signal was one line in a round reply, naming a generated id and a path.
 * A role that is off needs no mark: it is not asked anyway, and switching it on says why it cannot be.
 */
function unaskableHint(role: RoleRow, texts: Readonly<Record<string, string>>): string {
  const why = isActive(role) ? whyNotAskable(role, texts) : '';

  return why.length === 0
    ? ''
    // "A round that asks its first prompt", not "never": a per-round choice of a later prompt WITH text
    // is still asked. (CodeRabbit, PR #495.)
    : `<p class="hint warn">It is switched on, but a round that asks its first prompt will skip it — `
      + `every round, unless another prompt is chosen for it. ${escapeHtml(why)}</p>`;
}

function roleBlock(rows: readonly RoleRow[], role: RoleRow, texts: Readonly<Record<string, string>>): string {
  const shipped = isBuiltIn(role.id);
  const on = isActive(role);
  const may = isActive(role) ? canDeactivate(rows, role) : canActivate(rows, role);
  const last = isActive(role) && !canDeactivate(rows, role);
  const stage = stageOf(role);
  const prompts = (role.prompts ?? []).map((p) => promptBlock(role, p, texts)).join('\n');

  return `<details class="role role-${roleTone(role.id, stage)}${on ? '' : ' off'}" data-id="${escapeHtml(role.id)}"${on ? ' open' : ''}>
  <summary>
    <span class="title">${escapeHtml(role.name ?? role.id)}</span>
    <span class="id">${escapeHtml(role.id)}</span>
    ${shipped ? '<span class="badge">shipped</span>' : ''}
  </summary>
  <div class="fields">
    <label>Name
      <input type="text" data-field="name" value="${escapeHtml(role.name ?? role.id)}" placeholder="What this role is called"${shipped ? ' readonly' : ''}>
    </label>
    <label>Stage
      <select data-field="stage"${shipped ? ' disabled' : ''}>
        <option value="${PLAN_STAGE}"${stage === PLAN_STAGE ? ' selected' : ''}>Plan review</option>
        <option value="${RESULT_STAGE}"${stage === RESULT_STAGE ? ' selected' : ''}>Code review</option>
      </select>
    </label>
    <label class="flag"><input type="checkbox" data-field="programmingTask"${role.programmingTask ?? true ? ' checked' : ''}${shipped ? ' disabled' : ''}> A programming task</label>
    <label class="flag"><input type="checkbox" data-field="active"${on ? ' checked' : ''}${may ? '' : ' disabled'}> Active</label>
    ${may ? '' : last
      ? '<p class="hint">The only role still active in this stage — switch another one on before turning this one off, or the stage would have no reviewer in it at all.</p>'
      : `<p class="hint">Five roles are already active in this stage. Switch one off to make room.</p>`}
    ${kindHint(role)}
    ${unaskableHint(role, texts)}
    ${shipped ? '<p class="hint">A role this product ships. Its id, its name, its stage and its kind are fixed — they key your settings, your open sessions and every round already recorded, and the review server reads none of them from your configuration. Its switch and its prompt text are yours.</p>' : ''}
  </div>
${prompts}
  <button type="button" class="add" data-add-prompt="${escapeHtml(role.id)}">Add a prompt</button>
  ${shipped ? '' : `<button type="button" class="remove role" data-remove="${escapeHtml(role.id)}">Remove this role</button>`}
</details>`;
}

/**
 * The banner for a server too old to read any of this.
 *
 * <p>The third of its kind, and the same shape as the two in the panel: shown only when the server
 * is KNOWN and strictly older, naming the version that is there. Below 0.19.0 the key is never
 * read — the roles are on this page and in no round, and nothing anywhere fails. That is precisely
 * why it is said out loud.</p>
 */
export function tooOldFor(serverVersion: string, rows: readonly RoleRow[]): string {
  if (serverVersion.length === 0 || rows.length === 0 || !older(serverVersion, CUSTOM_ROLES_SINCE)) {
    return '';
  }

  return `<div class="stale">The coai-mcp you have installed (${escapeHtml(serverVersion)}) does not read `
    + `roles at all, so nothing on this page will run. Update it to ${escapeHtml(CUSTOM_ROLES_SINCE)} `
    + `or later — the <b>MCP server</b> section of the panel.</div>`;
}

/**
 * The note for a server this window has not identified yet.
 *
 * <p>`coai.editRoles` is on the command palette, so this page can be the FIRST thing opened in a
 * window — before the panel has rendered, which is what detects the installed server. An unknown
 * server draws no banner, and no banner reads exactly like "checked, and fine". Saying the check has
 * not run is the honest shape, and it disappears the moment the version arrives, because the host
 * repaints when it learns one.</p>
 *
 * <p>Only when there is something that could fail to run: a machine with no roles of its own has
 * nothing at stake in the answer.</p>
 */
export function unknownServerNote(serverVersion: string, rows: readonly RoleRow[]): string {
  if (serverVersion.length > 0 || rows.length === 0) {
    return '';
  }

  return `<div class="stale">Whether the coai-mcp you have installed can read these roles has `
    + `<b>not been checked yet</b> — it needs ${escapeHtml(CUSTOM_ROLES_SINCE)} or later. Open the `
    + `ConnectOtherAIs panel and this line will answer itself.</div>`;
}

/** The first `coai-mcp` that reads `COAI_ROLES`. Below it, this page writes into a void. */
export const CUSTOM_ROLES_SINCE = '0.19.0';

/** Whether `version` is strictly older than `since`, comparing numbers rather than text. */
function older(version: string, since: string): boolean {
  const mine = version.split('.').map((n) => Number.parseInt(n, 10));
  const theirs = since.split('.').map((n) => Number.parseInt(n, 10));
  for (let i = 0; i < Math.max(mine.length, theirs.length); i += 1) {
    const a = mine[i] ?? 0;
    const b = theirs[i] ?? 0;
    if (Number.isNaN(a) || Number.isNaN(b)) {
      return false;
    }
    if (a !== b) {
      return a < b;
    }
  }

  return false;
}

/**
 * Whether a role added now could not be switched on even once its question is written.
 *
 * <p>Every new role arrives switched off since issue #338 — it has no question yet — so what is worth
 * saying beside the button is the other reason it would stay off: the stage is full. A new role always
 * joins the code bucket of the result stage, so that is the only count to take.</p>
 */
function stageIsFull(all: readonly RoleRow[]): boolean {
  return activeCount(all, RESULT_CODE) >= MAX_ACTIVE_PER_BUCKET;
}

/** What each tab is called — the headings they replace, so nothing was renamed out from under anyone. */
const TAB_NAMES: Readonly<Record<string, string>> = {
  plan: 'Plan review',
  code: 'Code review',
  documents: 'Document review',
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
 * The deletions that are stuck, and the two things that can be done about them.
 *
 * <p><b>Reload Window comes first when the mirror STOOD DOWN</b>, because that is the thing which
 * actually ends one: a newer build owns the settings file, and reloading is how this window becomes
 * that build. Offering the destructive action first for a condition with a cure would be offering
 * the wrong one.</p>
 *
 * <p><b>And the other action costs something, which it says here rather than afterwards.</b> While
 * the server still carries the row, deleting the prompt text IS the incident: rounds run the role,
 * find no text, and complain each time. The softer wording this had first — <i>the server may still
 * carry the row</i> — describes the same fact without naming what it does to the person.
 * (antigravity, the plan round.)</p>
 */
function strandedHtml(stranded: readonly Tombstone[]): string {
  if (stranded.length === 0) {
    return '';
  }
  const rows = stranded.map((one) => {
    const reload = one.reason === STOOD_DOWN
      ? '<button type="button" class="act" data-reload="1">Reload Window</button>'
      : '';

    return `<li><b>${escapeHtml(one.name)}</b> was removed here, and the server has not been told yet.`
      + ` <span class="why">${escapeHtml(one.reason)}</span>`
      + ` Its prompts are kept until the server has it.`
      + `${reload}`
      + `<button type="button" class="act" data-finish="${escapeHtml(one.roleId)}">Finish the deletion anyway</button>`
      + `<span class="cost">The server may go on running this role without its text until its settings`
      + ` catch up, and rounds against it will complain each time.</span></li>`;
  }).join('');

  return `<section class="stranded" aria-label="Deletions the server has not been told about"><ul>${rows}</ul></section>`;
}

export function rolesHtml(state: RolesPageState, nonce: string): string {
  const all = composed(state.rows);
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

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Review roles</title>
${styles(state.uiScale)}
</head>
<body>
<header><h1>Review roles</h1>${zoomControlHtml(state.uiScale)}</header>
<p class="lead">The question each reviewer asks. Everything here is saved as you type${state.perSide ? ', for this side of the machine' : ''}.</p>
${strandedHtml(state.stranded ?? [])}
${tooOldFor(state.serverVersion, state.rows)}${unknownServerNote(state.serverVersion, state.rows)}

${rolesTabs(openTab)}

<section id="section-plan" role="tabpanel" aria-labelledby="tab-plan" data-section="plan"${openTab === 'plan' ? '' : ' hidden'}>
<p class="note">Roles that read the plan, before any code exists.</p>
${[...plan, ...waiting].map((r) => roleBlock(all, r, state.texts)).join('\n')}
</section>

<section id="section-code" role="tabpanel" aria-labelledby="tab-code" data-section="code"${openTab === 'code' ? '' : ' hidden'}>
<p class="note">Roles that read the change itself.</p>
${code.map((r) => roleBlock(all, r, state.texts)).join('\n')}
<!-- The control lives in the section its effect lands in. A new role always joins the code
     bucket of the result stage, so offered from the plan tab it was a button that quietly
     created something on another tab and moved the person there. (gemini, the code round.) -->
<button type="button" class="add role" data-add="role">Add a role</button>
${stageIsFull(all) ? '<p class="hint">Five roles are already active in the code stage, so a new one cannot be switched on until one of them is switched off.</p>' : ''}
</section>

<section id="section-documents" role="tabpanel" aria-labelledby="tab-documents" data-section="documents"${openTab === 'documents' ? '' : ' hidden'}>
<p class="note">Roles that read a document rather than a diff — what <code>review_document</code> runs. A role here never sees a checkout or a change.</p>
${documents.map((r) => roleBlock(all, r, state.texts)).join('\n')}
</section>

${script(nonce)}
</body>
</html>`;
}

function styles(uiScale: number): string {
  return `<style>
${zoomStyle(uiScale)}
${ZOOM_CSS}
body { font-family: var(--vscode-font-family); color: var(--vscode-foreground);
  background: var(--vscode-editor-background); padding: 0 16px 24px; }
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
</style>`;
}

function script(nonce: string): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${zoomScript()}
  // Delegated on the document: every block is replaced whenever the rows change, and a listener
  // bound to one field would die with the block it was bound to.
  const roleOf = (el) => el.closest('[data-id]');
  const promptOf = (el) => el.closest('[data-prompt]');
  const send = (field) => {
    const role = roleOf(field);
    if (!role || !role.dataset || typeof field.dataset.field !== 'string') { return; }
    const prompt = promptOf(field);
    const value = field.type === 'checkbox' ? field.checked : field.value;
    if (prompt && prompt.dataset) {
      vscode.postMessage({ type: 'editPrompt', id: role.dataset.id, promptId: prompt.dataset.prompt,
                           field: field.dataset.field, value: value });
      return;
    }
    vscode.postMessage({ type: 'edit', id: role.dataset.id, field: field.dataset.field, value: value });
  };
  // A checkbox and a select are sent by 'change' below. The browser fires 'input' for them TOO, and
  // sending from both made one press two commands - two reads of every prompt and two refusals for one
  // click once switching a role on could be refused. (Our own code review of issue #338.)
  document.addEventListener('input', function (event) {
    const typed = event.target;
    if (typed && typed.dataset && typed.type !== 'checkbox' && typed.tagName !== 'SELECT') { send(typed); }
  });
  document.addEventListener('change', function (event) {
    const field = event.target;
    if (field && field.dataset && (field.type === 'checkbox' || field.tagName === 'SELECT')) { send(field); }
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
    if (finish) { vscode.postMessage({ type: 'finishDeletion', id: finish.dataset.finish }); return; }
    if (pressed.closest('[data-add="role"]')) { vscode.postMessage({ type: 'add' }); return; }
    const addPrompt = pressed.closest('[data-add-prompt]');
    if (addPrompt) { vscode.postMessage({ type: 'addPrompt', id: addPrompt.dataset.addPrompt }); return; }
    const removePrompt = pressed.closest('[data-remove-prompt]');
    if (removePrompt) {
      const role = roleOf(removePrompt);
      vscode.postMessage({ type: 'removePrompt', id: role ? role.dataset.id : '',
                           promptId: removePrompt.dataset.removePrompt });
      return;
    }
    const restore = pressed.closest('[data-restore]');
    if (restore) {
      const role = roleOf(restore);
      vscode.postMessage({ type: 'restorePrompt', id: role ? role.dataset.id : '',
                           promptId: restore.dataset.restore });
      return;
    }
    const remove = pressed.closest('[data-remove]');
    if (remove && remove.dataset) { vscode.postMessage({ type: 'remove', id: remove.dataset.remove }); }
  });
}());
</script>`;
}
