import { cardColumns } from './cardColumns';
import { SHIPPED_COMMANDS, type CommandRow } from './commands';
import { commandsSkewNote, customBlock, shippedBlock, type CommandAttrs } from './commandsBlocks';
import { escapeHtml } from './escapeHtml';

/**
 * Reviews › Commands on the Settings page (research/PLAN_one_model_catalog.md E4.4): the Gate commands tab's own blocks
 * (`commandsBlocks.customBlock`, `shippedBlock` — never a copy), drawn in the panel's document and edited through the one
 * editing core (`commandsHost.ts`) by `commands` messages.
 *
 * <p>The page draws the roles as well, whose wiring reads `data-field`, `data-remove` and `data-restore`; so a command
 * block here carries names of its own ({@link EMBEDDED}), and each wiring reads only its own.</p>
 */

/** What the host read for the commands: the rows, the texts on disk, the server, and whether this side keeps its own. */
export interface CommandsEmbedState {
  readonly rows: readonly CommandRow[];
  readonly texts: Readonly<Record<string, string>>;
  readonly serverVersion: string;
  readonly perSide: boolean;
}

const EMBEDDED: CommandAttrs = {
  row: 'data-cmd-id', field: 'data-cmd-field', text: 'data-cmd-text', remove: 'data-cmd-remove',
  restore: 'data-cmd-restore', file: 'data-cmd-file', add: 'data-cmd-add',
};

/** The commands, as the Settings page draws them — the tab's text, in its order, each list's blocks in the page's columns. */
export function commandsEmbedded(state: CommandsEmbedState): string {
  const note = commandsSkewNote(state.serverVersion);

  return `<div class="commands-embed">
<p class="lead">The orders the gate hands the AI that called it. Everything here is saved as you type${state.perSide ? ', for this side of the machine' : ''}.</p>
${note.length > 0 ? `<div class="stale" role="status">${escapeHtml(note)}</div>` : ''}
<h3>Yours</h3>
<p class="note">Commands you add are given after the built-in orders, in the rounds you choose. One is switched on only once it has text.</p>
${cardColumns(state.rows.map((row) => customBlock(row, state.texts, EMBEDDED)).join('\n'))}
<button type="button" ${EMBEDDED.add}>Add a command</button>
<h3>Shipped</h3>
<p class="note">The words the built-in orders are made of. Write your own to replace them; an empty box is the shipped text, shown faintly. The words in <code>bold</code> before a box are kept by the server — they are how an order is recognised.</p>
${cardColumns(SHIPPED_COMMANDS.map((one) => shippedBlock(one, state.texts, EMBEDDED)).join('\n'))}
</div>`;
}

/**
 * The commands' wiring in the panel's document — block-scoped, on the panel's own `vscode` and `send`. A pick and a
 * press are numbered (the busy mark); typing posts plainly and settles in the host. Focus is reported, so a repaint
 * never lands under the caret while somebody types a command.
 */
export function commandsEmbeddedScript(): string {
  return `
  {${commandsFieldsScript()}${commandsPressesScript()}
  }`;
}

/** The fields: a title, a text, a switch or a stage, each posted as the tab's own edit, and focus reported. */
function commandsFieldsScript(): string {
  return `
    const cmdRowOf = (el) => { const row = el.closest('[data-cmd-id]'); return row ? row.dataset.cmdId : ''; };
    const cmdPost = (edit, el, numbered) => (numbered ? send({ type: 'commands', edit: edit }, el) : vscode.postMessage({ type: 'commands', edit: edit }));
    const cmdTyped = (t) => (t.dataset.cmdText !== undefined ? { type: 'text', fileId: t.dataset.cmdText, value: t.value }
      : t.dataset.cmdField === 'title' ? { type: 'retitle', id: cmdRowOf(t), value: t.value } : null);
    const cmdPicked = (t) => (t.dataset.cmdField === 'enabled' ? { type: 'switch', id: cmdRowOf(t), value: t.checked }
      : t.dataset.cmdField === 'stage' ? { type: 'restage', id: cmdRowOf(t), value: t.value } : null);
    const cmdOwn = (t) => !!(t && t.dataset && typeof t.closest === 'function' && (t.dataset.cmdText !== undefined || t.dataset.cmdField !== undefined));
    document.addEventListener('input', (event) => {
      const t = event.target;
      if (!cmdOwn(t) || t.type === 'checkbox' || t.tagName === 'SELECT') { return; }
      const edit = cmdTyped(t);
      if (edit) { cmdPost(edit, t, false); }
    });
    document.addEventListener('change', (event) => {
      const t = event.target;
      const edit = cmdOwn(t) ? cmdPicked(t) : null;
      if (edit) { cmdPost(edit, t, true); }
    });
    const cmdKey = (t) => 'commands|' + (t.dataset.cmdText !== undefined ? t.dataset.cmdText : cmdRowOf(t)) + '|' + (t.dataset.cmdText !== undefined ? 'text' : t.dataset.cmdField);
    const cmdFocus = (t, editing) => vscode.postMessage({ type: 'focus', id: cmdKey(t), editing: editing,
      start: typeof t.selectionStart === 'number' ? t.selectionStart : 0, end: typeof t.selectionEnd === 'number' ? t.selectionEnd : 0 });
    document.addEventListener('focusin', (event) => { if (cmdOwn(event.target)) { cmdFocus(event.target, true); } });
    document.addEventListener('focusout', (event) => {
      const next = event.relatedTarget;
      // Moving from one field to the next is not a moment to rebuild the page.
      if (cmdOwn(event.target) && !cmdOwn(next)) { cmdFocus(event.target, false); }
    });`;
}

/** The buttons: each posts its edit, numbered, so it carries the busy mark. */
function commandsPressesScript(): string {
  return `
    document.addEventListener('click', (event) => {
      const t = event.target;
      if (!t || typeof t.closest !== 'function') { return; }
      if (t.closest('[data-cmd-add]')) { cmdPost({ type: 'add' }, t, true); return; }
      const remove = t.closest('[data-cmd-remove]');
      if (remove) { cmdPost({ type: 'remove', id: cmdRowOf(remove) }, remove, true); return; }
      const restore = t.closest('[data-cmd-restore]');
      if (restore) { cmdPost({ type: 'restore', fileId: restore.dataset.cmdRestore }, restore, true); }
    });`;
}
