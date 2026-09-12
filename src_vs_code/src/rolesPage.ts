import { MAX_ACTIVE_PER_STAGE, PLAN_STAGE, RESULT_STAGE, activeCount, builtInFor, composed, isActive, isBuiltIn, stageOf, type RoleRow } from './roles';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';
import { escapeHtml } from './webviewHtml';

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

export interface RolesPageState {
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
 * <p>Five active per stage, and the sixth is refused HERE rather than dropped by the server. A page
 * that let somebody tick a sixth and then showed them five would be a page that lies about what it
 * saved — the server does cap, and it names what it capped, so nothing is at risk except the truth.</p>
 */
export function canActivate(rows: readonly RoleRow[], role: RoleRow): boolean {
  return isActive(role) || activeCount(rows, stageOf(role)) < MAX_ACTIVE_PER_STAGE;
}

/**
 * Whether this role's switch may be turned OFF.
 *
 * <p>The last one standing in a stage cannot be: a stage with no role in it produces a round with
 * no reviewer, which the session counts as unresolved and never lets a person retry. The server
 * refuses such a round with a sentence — `review_plan` and `review_code` both do — and refusing it
 * HERE, where the pointer is, beats refusing it at round time, after somebody has waited for a
 * review that was never going to happen.</p>
 *
 * <p>It is the rule the sidebar's code-role ticks have had since they shipped, applied to the plan
 * stage as well, which is what the operator asked for when they asked for plan-stage switches.</p>
 */
export function canDeactivate(rows: readonly RoleRow[], role: RoleRow): boolean {
  return !isActive(role) || activeCount(rows, stageOf(role)) > 1;
}

function promptBlock(role: RoleRow, prompt: { id: string; label?: string; purpose?: string }, texts: Readonly<Record<string, string>>): string {
  const shipped = isShippedPrompt(role.id, prompt.id);
  const text = texts[prompt.id] ?? '';

  return `  <div class="prompt" data-prompt="${escapeHtml(prompt.id)}">
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

function roleBlock(rows: readonly RoleRow[], role: RoleRow, texts: Readonly<Record<string, string>>): string {
  const shipped = isBuiltIn(role.id);
  const on = isActive(role);
  const may = isActive(role) ? canDeactivate(rows, role) : canActivate(rows, role);
  const last = isActive(role) && !canDeactivate(rows, role);
  const stage = stageOf(role);
  const prompts = (role.prompts ?? []).map((p) => promptBlock(role, p, texts)).join('\n');

  return `<details class="role${on ? '' : ' off'}" data-id="${escapeHtml(role.id)}"${on ? ' open' : ''}>
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
    ${(role.programmingTask ?? true) ? '' : '<p class="hint">A role that is not a programming task is kept and takes part in no round yet — the stage that reviews a document rather than a diff is still being built.</p>'}
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
 * Whether a role added now would arrive switched off.
 *
 * <p>It does arrive switched off, which is honest — but the person found that out AFTER clicking,
 * from a hint on a role they had just created. Said beside the button, it is the same sentence one
 * step earlier. A new role always joins the code stage, so that is the only count to take.</p>
 */
function stageIsFull(all: readonly RoleRow[]): boolean {
  return activeCount(all, RESULT_STAGE) >= MAX_ACTIVE_PER_STAGE;
}

export function rolesHtml(state: RolesPageState, nonce: string): string {
  const all = composed(state.rows);
  const plan = all.filter((r) => stageOf(r) === PLAN_STAGE);
  const code = all.filter((r) => stageOf(r) !== PLAN_STAGE);

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
${tooOldFor(state.serverVersion, state.rows)}${unknownServerNote(state.serverVersion, state.rows)}

<h2>Plan review</h2>
<p class="note">Roles that read the plan, before any code exists.</p>
${plan.map((r) => roleBlock(all, r, state.texts)).join('\n')}

<h2>Code review</h2>
<p class="note">Roles that read the change itself.</p>
${code.map((r) => roleBlock(all, r, state.texts)).join('\n')}

<button type="button" class="add role" data-add="role">Add a role</button>
${stageIsFull(all) ? '<p class="hint">Five roles are already active in the code stage, so a new one will arrive switched off. Switch one of them off to make room for it.</p>' : ''}
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
.lead, .note { opacity: 0.8; margin: 2px 0 10px; }
.note { font-size: 0.9em; }
.role { border-left: 3px solid var(--vscode-charts-blue, #3794ff); background: var(--vscode-textBlockQuote-background);
  padding: 6px 10px; margin: 8px 0; }
.role.off { opacity: 0.55; }
.role > summary { cursor: pointer; display: flex; align-items: baseline; gap: 8px; }
.role .title { font-weight: 600; }
.role .id, .badge { font-size: 0.82em; opacity: 0.65; }
.badge { border: 1px solid var(--vscode-panel-border); border-radius: 3px; padding: 0 4px; }
.fields { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; margin: 8px 0; }
.fields label { display: flex; gap: 6px; align-items: center; }
.fields .flag { gap: 4px; }
.hint { flex-basis: 100%; font-size: 0.85em; opacity: 0.75; margin: 0; }
.prompt { border-left: 2px solid var(--vscode-panel-border); padding: 4px 8px; margin: 6px 0; }
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
  document.addEventListener('input', function (event) {
    if (event.target && event.target.dataset) { send(event.target); }
  });
  document.addEventListener('change', function (event) {
    const field = event.target;
    if (field && field.dataset && (field.type === 'checkbox' || field.tagName === 'SELECT')) { send(field); }
  });
  document.addEventListener('click', function (event) {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
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
