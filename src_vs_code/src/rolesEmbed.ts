import { FEATURE_CODE, FEATURE_DOCUMENT, PLAN_CODE, PLAN_DOCUMENT, RESULT_CODE, RESULT_DOCUMENT, bucketOf, composed, type RoleRow } from './roles';
import type { Tombstone } from './roleDeletion';
import { roleBlock, roleBlockOptions, stageIsFull, strandedHtml, tooOldFor, unknownServerNote, type RoleBlockOptions } from './rolesBlocks';

/**
 * Roles & prompts on the Settings page (research/PLAN_one_model_catalog.md E4.3): the Review roles tab's own role blocks
 * (`rolesBlocks.roleBlock`, never a copy), drawn in the panel's document and edited through the one editing core
 * (`rolesHost.ts`) by `roles` messages.
 *
 * <p>Three things differ from the tab, each because the document is shared: a prompt is marked `data-role-prompt` (the
 * panel's script reads `data-prompt` as a round pick); the four stages are headed groups, not a second tab strip inside
 * a place the page's strip already selects; and a role shows ONE switch, the catalog's and the panel's read as one.</p>
 */

/** What the host read for the roles: the rows, the prompt bodies, the server, and the deletions that cannot clear. */
export interface RolesEmbedState {
  readonly rows: readonly RoleRow[];
  readonly texts: Readonly<Record<string, string>>;
  readonly serverVersion: string;
  readonly perSide: boolean;
  readonly stranded: readonly Tombstone[];
}

/** One stage's roles, under its heading. */
function stageGroup(title: string, note: string, roles: readonly string[], extra = ''): string {
  return `<section class="role-stage">
<h3>${title}</h3>
<p class="note">${note}</p>
${roles.join('\n')}${extra}
</section>`;
}

/** The roles of the given buckets, each as its block. */
function blocksOf(all: readonly RoleRow[], buckets: readonly string[], texts: Readonly<Record<string, string>>, options: RoleBlockOptions): readonly string[] {
  return all.filter((role) => buckets.includes(bucketOf(role))).map((role) => roleBlock(role, texts, options));
}

/** The four stages, in the tab's order, with the tab's own notes. */
function stages(all: readonly RoleRow[], texts: Readonly<Record<string, string>>, options: RoleBlockOptions): readonly string[] {
  const full = stageIsFull(all) ? '<p class="hint">Five roles are already active in the code stage, so a new one cannot be switched on until one of them is switched off.</p>' : '';

  return [
    stageGroup('Plan stage', 'Roles that read the plan, before any code exists.', blocksOf(all, [PLAN_CODE, PLAN_DOCUMENT], texts, options)),
    stageGroup('Code stage', 'Roles that read the change itself.', blocksOf(all, [RESULT_CODE], texts, options),
      `\n<button type="button" class="add role" data-add="role">Add a role</button>\n${full}`),
    stageGroup('Document stage', 'Roles that read a document rather than a diff — what <code>review_document</code> runs.', blocksOf(all, [RESULT_DOCUMENT], texts, options)),
    stageGroup('Feature stage', 'Roles that read a whole feature once every epic has landed — what <code>review_feature</code> runs.',
      blocksOf(all, [FEATURE_CODE, FEATURE_DOCUMENT], texts, options)),
  ];
}

/**
 * The roles, as the Settings page draws them.
 *
 * @param roleEnabled the panel's own switch per role — read with the catalog's as ONE switch
 */
export function rolesEmbedded(state: RolesEmbedState, roleEnabled: Readonly<Record<string, boolean>>): string {
  const all = composed(state.rows);
  // The panel's switches, so each block's tick AND its last-role refusal read the one switch (E5.1b) — and the page's
  // counts, taken here once rather than by every block (E5.1b's code round, finding 1).
  const options = roleBlockOptions(all, 'data-role-prompt', roleEnabled);

  // `roles-embed` scopes the tab's own layout (catalogCss.ts): the panel's sheet already uses `.role` for Stages' boxes.
  return [
    '<div class="roles-embed">',
    `<p class="lead">The question each reviewer asks, and one switch for each. Everything here is saved as you type${state.perSide ? ', for this side of the machine' : ''}.</p>`,
    strandedHtml(state.stranded),
    `${tooOldFor(state.serverVersion, state.rows)}${unknownServerNote(state.serverVersion, state.rows)}`,
    ...stages(all, state.texts, options),
    '</div>',
  ].join('\n');
}

/**
 * The roles' wiring in the panel's document — block-scoped, on the panel's own `vscode` and `send` (a document may call
 * `acquireVsCodeApi` once). A pick and a press are numbered through `send`, so they carry the busy mark; typing posts
 * plainly and settles in the host. Focus is reported like the panel's own controls', so a repaint never lands under the
 * caret while somebody is typing a prompt.
 */
export function rolesEmbeddedScript(): string {
  return `
  {${rolesFieldsScript()}${rolesPressesScript()}
  }`;
}

/** The fields: a typed or picked value posted as the tab's own edit, and focus reported. */
function rolesFieldsScript(): string {
  return `
    const rolesRoleOf = (el) => el.closest('[data-id]');
    const rolesPromptOf = (el) => el.closest('[data-role-prompt]');
    const rolesPicked = (el) => el.type === 'checkbox' || el.tagName === 'SELECT';
    const rolesOwn = (el) => !!(el && el.dataset && typeof el.dataset.field === 'string' && typeof el.closest === 'function' && rolesRoleOf(el));
    const rolesPost = (edit, el, numbered) => (numbered ? send({ type: 'roles', edit: edit }, el) : vscode.postMessage({ type: 'roles', edit: edit }));
    const rolesKey = (el) => {
      const prompt = rolesPromptOf(el);
      return 'roles|' + rolesRoleOf(el).dataset.id + '|' + (prompt ? prompt.dataset.rolePrompt : '') + '|' + el.dataset.field;
    };
    const rolesField = (el) => {
      const id = rolesRoleOf(el).dataset.id;
      const prompt = rolesPromptOf(el);
      const value = el.type === 'checkbox' ? el.checked : el.value;
      const edit = prompt
        ? { type: 'editPrompt', id: id, promptId: prompt.dataset.rolePrompt, field: el.dataset.field, value: value }
        : { type: 'edit', id: id, field: el.dataset.field, value: value };
      rolesPost(edit, el, rolesPicked(el));
    };
    // A checkbox and a select fire 'input' too: sent from 'change' alone, or one press is two commands.
    document.addEventListener('input', (event) => { const typed = event.target; if (rolesOwn(typed) && !rolesPicked(typed)) { rolesField(typed); } });
    document.addEventListener('change', (event) => { const field = event.target; if (rolesOwn(field) && rolesPicked(field)) { rolesField(field); } });
    const rolesFocus = (el, editing) => vscode.postMessage({ type: 'focus', id: rolesKey(el), editing: editing,
      start: typeof el.selectionStart === 'number' ? el.selectionStart : 0, end: typeof el.selectionEnd === 'number' ? el.selectionEnd : 0 });
    document.addEventListener('focusin', (event) => { if (rolesOwn(event.target)) { rolesFocus(event.target, true); } });
    document.addEventListener('focusout', (event) => {
      const next = event.relatedTarget;
      // Moving from one field to the next is not a moment to rebuild the page.
      if (rolesOwn(event.target) && !(next && next.dataset && (next.dataset.field !== undefined || next.dataset.setting !== undefined))) {
        rolesFocus(event.target, false);
      }
    });`;
}

/** The buttons: each posts its edit, numbered, so it carries the busy mark. */
function rolesPressesScript(): string {
  return `
    const rolesPresses = [
      ['[data-add="role"]', () => ({ type: 'add' })],
      ['[data-add-prompt]', (el) => ({ type: 'addPrompt', id: el.dataset.addPrompt })],
      ['[data-remove-prompt]', (el) => ({ type: 'removePrompt', id: rolesRoleOf(el) ? rolesRoleOf(el).dataset.id : '', promptId: el.dataset.removePrompt })],
      ['[data-restore]', (el) => ({ type: 'restorePrompt', id: rolesRoleOf(el) ? rolesRoleOf(el).dataset.id : '', promptId: el.dataset.restore })],
      ['[data-remove]', (el) => ({ type: 'remove', id: el.dataset.remove })],
      ['[data-finish]', (el) => ({ type: 'finishDeletion', id: el.dataset.finish })],
      ['[data-reload]', () => ({ type: 'reloadWindow' })],
    ];
    document.addEventListener('click', (event) => {
      const pressed = event.target;
      if (!pressed || typeof pressed.closest !== 'function') { return; }
      for (const [selector, editOf] of rolesPresses) {
        const button = pressed.closest(selector);
        // A button drawn disabled posts nothing — a browser sends it no click, and the page says so itself rather than
        // leaning on that (the last role ON's Remove, E5.1b's code round).
        if (button) { if (!button.disabled) { rolesPost(editOf(button), button, true); } return; }
      }
    });`;
}
