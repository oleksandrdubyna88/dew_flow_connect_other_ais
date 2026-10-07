import { SHIPPED_COMMANDS, type CommandRow } from './commands';
import { commandsSkewNote, customBlock, shippedBlock, type CommandAttrs } from './commandsBlocks';
import { FORM_FIELDS_CSS, FORM_HEAD_CSS, formCardCss, formFrameCss } from './formPageStyle';
import { textControlsHtml, textControlsScript, textOf } from './textControls';
import { escapeHtml } from './webviewHtml';
import { type BusySnapshot, IDLE } from './busySnapshot';
import { BUSY_BAR, BUSY_CSS, busyMarkScript } from './busyMark';

/**
 * The Edit commands page — issue #467, Epic B. Pure: the markup and its script. The blocks it draws are
 * `commandsBlocks.ts` and the parser of what the script posts is `commandsMessages.ts`, both shared with
 * the new Settings page. `commandsPanel.ts` is the only part that touches VS Code.
 */

/** Everything the page is drawn from. `texts` are the command files on disk, by file id. */
export interface CommandsPageState {
  readonly rows: readonly CommandRow[];
  readonly texts: Readonly<Record<string, string>>;
  /** The installed server's version, or empty when it is not known. */
  readonly serverVersion: string;
  readonly perSide: boolean;
  /** The text size and tone the page is drawn in; absent is the theme's own, as on the help page. */
  readonly uiScale?: number;
  readonly textTone?: number;
  /** What the host had running when this tab was drawn: the busy mark's painted half (research/PLAN_busy_marks_on_every_webview.md). */
  readonly busy?: BusySnapshot;
}

/**
 * The posts this tab numbers: the structural changes, each of which re-reads every command file and redraws the tab
 * (research/PLAN_busy_marks_on_every_webview.md, E3). Not `text` or `retitle`: both are typing, settled for 300 ms.
 */
export const COMMANDS_TRACKED: readonly string[] = ['switch', 'restage', 'add', 'remove', 'restore'];

export function commandsHtml(state: CommandsPageState, nonce: string): string {
  const note = commandsSkewNote(state.serverVersion);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Gate commands</title>
${styles(textOf(state).size, textOf(state).tone)}
</head>
<body>
${BUSY_BAR}
<header><h1>Gate commands</h1>${textControlsHtml(textOf(state).size, textOf(state).tone)}</header>
<p class="lead">The orders the gate hands the AI that called it. Everything here is saved as you type${state.perSide ? ', for this side of the machine' : ''}.</p>
${note.length > 0 ? `<div class="stale" role="status">${escapeHtml(note)}</div>` : ''}
<h2>Yours</h2>
<p class="note">Commands you add are given after the built-in orders, in the rounds you choose. One is switched on only once it has text.</p>
${state.rows.map((row) => customBlock(row, state.texts, OWN_ATTRS)).join('\n')}
<button type="button" data-add>Add a command</button>
<h2>Shipped</h2>
<p class="note">The words the built-in orders are made of. Write your own to replace them; an empty box is the shipped text, shown faintly. The words in <code>bold</code> before a box are kept by the server — they are how an order is recognised.</p>
${SHIPPED_COMMANDS.map((one) => shippedBlock(one, state.texts, OWN_ATTRS)).join('\n')}
${script(nonce, state.busy ?? IDLE)}
</body>
</html>`;
}

/** This tab's own attribute names — the ones its script reads. */
const OWN_ATTRS: CommandAttrs = {
  row: 'data-id', field: 'data-field', text: 'data-text', remove: 'data-remove', restore: 'data-restore', file: 'data-file', add: 'data-add',
};

function styles(size: number, tone: number): string {
  // The Chat presets look (formPageStyle.ts), the operator's ask of 2026-09-29: this page had drifted
  // furthest, with the browser's white fields and no column. Its own rules come AFTER the fields.
  return `<style>
${formFrameCss(size, tone)}
${formCardCss('.command')}
  .command { border-left-color: var(--vscode-textLink-foreground); }
  .command h3 { font-size: 1em; margin: 0 0 6px; }
${FORM_HEAD_CSS}
${FORM_FIELDS_CSS}
.lead, .note { color: var(--vscode-descriptionForeground); }
.stale { border-left: 3px solid var(--vscode-editorWarning-foreground); padding: 4px 8px; margin: 8px 0; }
.row { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; margin-bottom: 8px; }
.row input[type="text"] { flex: 1 1 12rem; min-width: 0; }
/* After the fields, whose font: inherit would reset it: a command is read as the AI will read it. */
textarea { font-family: var(--vscode-editor-font-family); }
.command > button { margin-top: 6px; }
.marker { margin: 4px 0; }
.badge { font-size: 0.8em; color: var(--vscode-textLink-foreground); }
${BUSY_CSS}
</style>`;
}

/**
 * Delegated on the document, as the roles page's: a block is replaced whenever the rows change. A
 * checkbox and a select post on `change` ONLY — one press, one message (issue #338 found the roles page
 * sending two).
 */
function script(nonce: string, busy: BusySnapshot): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${textControlsScript()}
  ${busyMarkScript(busy, COMMANDS_TRACKED)}
  // Through the busy mark: a structural change is numbered, typing passes through it unnumbered.
  const post = (message, control) => { send(message, control || null); };
  const idOf = (el) => { const row = el.closest('[data-id]'); return row ? row.dataset.id : ''; };
  document.addEventListener('input', (event) => {
    const t = event.target;
    if (t.type === 'checkbox' || t.tagName === 'SELECT') { return; }
    if (t.dataset.text !== undefined) { post({ type: 'text', fileId: t.dataset.text, value: t.value }); return; }
    if (t.dataset.field === 'title') { post({ type: 'retitle', id: idOf(t), value: t.value }); }
  });
  document.addEventListener('change', (event) => {
    const t = event.target;
    if (t.dataset.field === 'enabled') { post({ type: 'switch', id: idOf(t), value: t.checked }, t); }
    if (t.dataset.field === 'stage') { post({ type: 'restage', id: idOf(t), value: t.value }, t); }
  });
  document.addEventListener('click', (event) => {
    const t = event.target;
    if (t.closest('[data-add]')) { post({ type: 'add' }, t); return; }
    const remove = t.closest('[data-remove]');
    if (remove) { post({ type: 'remove', id: idOf(remove) }, remove); return; }
    const restore = t.closest('[data-restore]');
    if (restore) { post({ type: 'restore', fileId: restore.dataset.restore }, restore); }
  });
  // LAST: this tab is ready to hear what is running (busyMark.ts).
  vscode.postMessage({ type: 'ready' });
})();
</script>`;
}
