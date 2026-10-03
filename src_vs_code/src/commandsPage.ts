import { COMMAND_MODELS_SINCE } from './commandModels';
import { SHIPPED_COMMANDS, fileIdOf, hasText, shippedTextOf, type CommandRow, type CommandStageName, type ShippedCommand } from './commands';
import type { RowCommand } from './commandsEdit';
import { compareVersions } from './coaiInstall';
import { FORM_FIELDS_CSS, FORM_HEAD_CSS, formCardCss, formFrameCss } from './formPageStyle';
import { textControlsHtml, textControlsScript, textOf } from './textControls';
import { escapeHtml } from './webviewHtml';
import { type BusySnapshot, IDLE } from './busySnapshot';
import { BUSY_BAR, BUSY_CSS, busyMarkScript } from './busyMark';

/**
 * The Edit commands page — issue #467, Epic B. Pure: the markup, its script and the parser of what the
 * script posts. `commandsPanel.ts` is the only part that touches VS Code.
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
  /** What the host had running when this tab was drawn: the busy mark's painted half (todo/PLAN_busy_marks_on_every_webview.md). */
  readonly busy?: BusySnapshot;
}

/** What the page can ask the host for: an edit of the rows, or a text written or restored. */
export type PageCommand =
  | RowCommand
  | { readonly kind: 'text'; readonly fileId: string; readonly value: string }
  | { readonly kind: 'restore'; readonly fileId: string }
  | { readonly kind: 'ignore' };

const IGNORE: PageCommand = { kind: 'ignore' };

const STAGE_NAMES: readonly CommandStageName[] = ['any', 'plan', 'code'];

/** A message from the page, read as one of the few things it may ask — anything else is ignored. */
export function commandEdit(message: unknown): PageCommand {
  const m = recordOf(message);
  const type = text(m['type']);

  return Object.hasOwn(READERS, type) ? (READERS[type] ?? ignored)(m) : IGNORE;
}

const ignored = (): PageCommand => IGNORE;

function recordOf(message: unknown): Record<string, unknown> {
  return typeof message === 'object' && message !== null ? message as Record<string, unknown> : {};
}

const text = (value: unknown): string => (typeof value === 'string' ? value : '');

const READERS: Readonly<Record<string, (m: Record<string, unknown>) => PageCommand>> = {
  // No token from the page: the host draws a random one (`commandsPanel.store`).
  add: () => ({ kind: 'add', token: '' }),
  remove: (m) => ({ kind: 'remove', id: text(m['id']) }),
  retitle: (m) => ({ kind: 'retitle', id: text(m['id']), value: text(m['value']) }),
  restage: (m) => stageCommand(text(m['id']), m['value']),
  switch: (m) => (typeof m['value'] === 'boolean' ? { kind: 'switch', id: text(m['id']), value: m['value'] } : IGNORE),
  text: (m) => ({ kind: 'text', fileId: text(m['fileId']), value: text(m['value']) }),
  restore: (m) => ({ kind: 'restore', fileId: text(m['fileId']) }),
};

function stageCommand(id: string, value: unknown): PageCommand {
  const stage = STAGE_NAMES.find((one) => one === value);

  return stage === undefined ? IGNORE : { kind: 'restage', id, value: stage };
}

/**
 * The note for a server too old to read any of this — the texts and the commands arrived in the same
 * release as the per-caller models (`COMMAND_MODELS_SINCE`).
 */
export function commandsSkewNote(serverVersion: string): string {
  return serverVersion.length === 0 || compareVersions(COMMAND_MODELS_SINCE, serverVersion) <= 0
    ? ''
    : `The installed coai-mcp is ${serverVersion}: it ignores these texts and commands and gives the shipped orders. `
      + `Update it to ${COMMAND_MODELS_SINCE} or later.`;
}

/**
 * The posts this tab numbers: the structural changes, each of which re-reads every command file and redraws the tab
 * (todo/PLAN_busy_marks_on_every_webview.md, E3). Not `text` or `retitle`: both are typing, settled for 300 ms.
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
${state.rows.map((row) => customBlock(row, state.texts)).join('\n')}
<button type="button" data-add>Add a command</button>
<h2>Shipped</h2>
<p class="note">The words the built-in orders are made of. Write your own to replace them; an empty box is the shipped text, shown faintly. The words in <code>bold</code> before a box are kept by the server — they are how an order is recognised.</p>
${SHIPPED_COMMANDS.map((one) => shippedBlock(one, state.texts)).join('\n')}
${script(nonce, state.busy ?? IDLE)}
</body>
</html>`;
}

function customBlock(row: CommandRow, texts: Readonly<Record<string, string>>): string {
  const fileId = fileIdOf(row.id);

  return `<section class="command" data-id="${escapeHtml(row.id)}">
<div class="row">
<input type="text" data-field="title" aria-label="Title" value="${escapeHtml(row.title)}">
<select data-field="stage" aria-label="Rounds">${STAGE_NAMES.map((stage) => stageOption(stage, row.stage)).join('')}</select>
<label><input type="checkbox" data-field="enabled"${row.enabled ? ' checked' : ''}> On</label>
<button type="button" class="remove" data-remove>Remove</button>
</div>
<textarea data-text="${escapeHtml(fileId)}" aria-label="What it tells the AI" rows="3">${escapeHtml(texts[fileId] ?? '')}</textarea>
</section>`;
}

const STAGE_LABEL: Readonly<Record<CommandStageName, string>> = { any: 'Every round', plan: 'Plan rounds', code: 'Code rounds' };

function stageOption(stage: CommandStageName, chosen: CommandStageName): string {
  return `<option value="${stage}"${stage === chosen ? ' selected' : ''}>${STAGE_LABEL[stage]}</option>`;
}

function shippedBlock(one: ShippedCommand, texts: Readonly<Record<string, string>>): string {
  // Only an override that SAYS something is one — a blank file is the shipped text, as the server reads it.
  const written = hasText(texts, one.id) ? texts[one.id] ?? '' : '';

  return `<section class="command" data-file="${escapeHtml(one.id)}">
<h3>${escapeHtml(one.title)}${written.length > 0 ? ' <span class="badge">yours</span>' : ''}</h3>
${placeholderNote(one)}${markerLine(one.marker)}
<textarea data-text="${escapeHtml(one.id)}" aria-label="${escapeHtml(one.title)}" rows="4" placeholder="${escapeHtml(shippedTextOf(one.id))}">${escapeHtml(written)}</textarea>
${restoreButton(one.id, written)}
</section>`;
}

function markerLine(marker: string): string {
  return marker.length > 0 ? `<p class="marker"><b>${escapeHtml(marker)}</b>…</p>` : '';
}

function restoreButton(fileId: string, written: string): string {
  return written.length > 0 ? `<button type="button" data-restore="${escapeHtml(fileId)}">Restore the shipped text</button>` : '';
}

function placeholderNote(one: ShippedCommand): string {
  return one.placeholders.length === 0
    ? ''
    : `<p class="note">The server fills in ${one.placeholders.map((p) => `<code>${escapeHtml(p)}</code>`).join(', ')}.</p>`;
}

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
