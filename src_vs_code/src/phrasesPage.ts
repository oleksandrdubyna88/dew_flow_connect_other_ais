import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';
import { escapeHtml } from './webviewHtml';

/**
 * The tab where a person keeps the phrases they stopped wanting to retype.
 *
 * <p>A page module and a thin panel host, which is the arrangement this extension already has three
 * times — the rounds log, the help page and the presets tab. Not a fourth one.</p>
 *
 * <p><b>Everything on this page is text the person in front of it wrote</b>, so the escaping is a
 * different question from the rest of the panel: elsewhere a label comes from a catalog this product
 * shipped, and the worst case is somebody else's mistake. Here it is their own phrase, which they can
 * perfectly well paste a `&lt;script&gt;` into to see what happens — and the answer must be that they
 * see a `&lt;script&gt;`.</p>
 *
 * <p><b>The page edits the ROWS as they are stored, not the phrases as they are read.</b> The reader
 * gives a row with no name a name from its first line, which is right on a button and wrong in an
 * editor: a person who clears the name box would watch a derived one appear in it and then save that
 * derived name as though they had typed it. So the box holds what the file holds, empty included, and
 * the derived name is a thing only the sidebar does. (Gate finding, gemini, on the story's plan.)</p>
 */

/** The rows the phrase editor opens at. A phrase is often several lines and sometimes many. */
const PHRASE_ROWS = 10;

/** A row exactly as `settings.json` holds it — not a `Phrase`, which has been given a name. */
export interface PhraseRowView {
  readonly id: string;
  readonly name: string;
  readonly text: string;
}

export interface PhrasesPageState {
  readonly rows: readonly PhraseRowView[];
  readonly uiScale: number;
}

/** Every message this page can send, decided without a host so a test can reach the decision. */
export type PhraseCommand =
  | { readonly kind: 'edit'; readonly id: string; readonly field: string; readonly value: string }
  | { readonly kind: 'add' }
  | { readonly kind: 'remove'; readonly id: string }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'ignore' };

const IGNORE: PhraseCommand = { kind: 'ignore' };

/** The fields a phrase HAS. A name this does not know is not a field — `__proto__` included. */
const FIELDS: readonly string[] = ['name', 'text'];

function idOf(value: unknown): string {
  return typeof value === 'string' && value.length > 0 ? value : '';
}

/**
 * What a message from the page means.
 *
 * <p>The same split `chatMessages.ts` makes for the chat page, for the reason recorded there: the
 * module that maps a webview message to an action is otherwise the one no unit test can reach, and a
 * wrong mapping would ship with every test green.</p>
 */
export function phraseEdit(message: unknown): PhraseCommand {
  if (typeof message !== 'object' || message === null) {
    return IGNORE;
  }
  const said = message as Record<string, unknown>;
  if (said['type'] === 'zoom') {
    const delta = said['delta'];

    return typeof delta === 'number' && Number.isFinite(delta)
      ? { kind: 'zoom', delta: Math.max(-1, Math.min(1, Math.trunc(delta))) }
      : IGNORE;
  }
  if (said['type'] === 'add') {
    return { kind: 'add' };
  }
  if (said['type'] === 'remove') {
    const id = idOf(said['id']);

    return id.length === 0 ? IGNORE : { kind: 'remove', id };
  }

  return said['type'] === 'edit' ? edited(said) : IGNORE;
}

function edited(said: Record<string, unknown>): PhraseCommand {
  const id = idOf(said['id']);
  const field = said['field'];
  const value = said['value'];
  // A field this list does not have is not an edit. The check is against a list of names rather than
  // an `in` test on the object, so no key of Object.prototype can ever be one of them.
  const known = typeof field === 'string' && FIELDS.includes(field);

  return id.length === 0 || !known || typeof value !== 'string'
    ? IGNORE
    : { kind: 'edit', id, field: field as string, value };
}

/**
 * Whether the page must be REDRAWN after this command was applied.
 *
 * <p>Typing must NOT: the caret is in a box somebody is writing in, and moving it to the end of what
 * they wrote is the defect the sidebar's own prompt box had. Adding and removing MUST, because the
 * list's shape is what changed.</p>
 */
export function phraseRepaints(command: PhraseCommand): boolean {
  return command.kind === 'add' || command.kind === 'remove';
}

function phraseRow(row: PhraseRowView): string {
  return `<div class="phrase" data-id="${escapeHtml(row.id)}">
  <div class="head">
    <input type="text" data-field="name" value="${escapeHtml(row.name)}" placeholder="A name for the button">
    <button type="button" class="remove" data-remove data-id="${escapeHtml(row.id)}">Remove</button>
  </div>
  <textarea data-field="text" rows="${PHRASE_ROWS}" placeholder="What lands on the clipboard">${escapeHtml(row.text)}</textarea>
</div>`;
}

function styles(uiScale: number): string {
  return `  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 16px 24px; max-width: 900px; margin: 0 auto; ${zoomStyle(uiScale)} }
  header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 8px; }
  h1 { font-size: 1.2em; margin: 0; }
  .lead { opacity: .8; margin: 0 0 12px; }
  .phrase { border: 1px solid var(--vscode-panel-border); border-left-width: 3px; border-left-color: var(--vscode-textLink-foreground); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .head { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; flex-wrap: wrap; }
  .head input[type="text"] { flex: 1 1 12rem; min-width: 0; }
  input, textarea { font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 3px; padding: 4px 6px; }
  /* The box a person READS a phrase in. It grows with its content where the engine can do it; the
     rows attribute is the floor for every engine that cannot. */
  textarea { width: 100%; box-sizing: border-box; field-sizing: content; max-height: 60vh; }
  button { font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 3px; padding: 4px 12px; cursor: pointer; }
  button.remove { color: var(--vscode-foreground); background: none; border: 1px solid var(--vscode-panel-border); }
  /* A save that did not land. Not a repaint: a repaint would replace what the person typed with what
     the file still says, which is the very thing that was not saved. */
  .failed { color: var(--vscode-inputValidation-errorForeground, var(--vscode-errorForeground)); border: 1px solid var(--vscode-inputValidation-errorBorder, var(--vscode-errorForeground)); border-radius: 3px; padding: 6px 10px; margin: 0 0 12px; }
${ZOOM_CSS}`;
}

function script(nonce: string): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${zoomScript()}
  // Delegated on the document: every row is replaced whenever the list changes, and a listener bound
  // to a field would die with the row it was bound to.
  document.addEventListener('input', function (event) {
    const field = event.target;
    if (!field || !field.dataset || typeof field.dataset.field !== 'string') { return; }
    const row = field.closest('[data-id]');
    if (!row || !row.dataset) { return; }
    vscode.postMessage({ type: 'edit', id: row.dataset.id, field: field.dataset.field, value: field.value });
  });
  document.addEventListener('click', function (event) {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    if (pressed.closest('[data-add]')) { vscode.postMessage({ type: 'add' }); return; }
    const remove = pressed.closest('[data-remove]');
    if (remove && remove.dataset) { vscode.postMessage({ type: 'remove', id: remove.dataset.id }); }
  });
  // A failed save arrives as a MESSAGE, never as a redraw: what could not be stored is still in the
  // box, and redrawing would replace it with what the file still says.
  window.addEventListener('message', function (event) {
    const said = event.data;
    if (!said || said.type !== 'saveFailed') { return; }
    const banner = document.getElementById('save-failed');
    if (!banner) { return; }
    banner.textContent = said.text;
    banner.hidden = false;
  });
}());
</script>`;
}

/** The page. Its own function so the document below stays readable, as every page here does it. */
export function phrasesHtml(state: PhrasesPageState, nonce: string): string {
  const rows = state.rows.map(phraseRow).join('');
  const empty = state.rows.length > 0
    ? ''
    : '<p class="lead">No phrases yet. <b>Add a phrase</b> and it appears in the panel, under <b>Phrases</b>.</p>';

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Phrases</title>
<style>
${styles(state.uiScale)}
</style>
</head>
<body>
<header><h1>Phrases</h1>${zoomControlHtml(state.uiScale)}</header>
<p class="lead">The sentences you keep. Press one in the panel and it goes on the clipboard, ready to paste. Everything here is saved as you type.</p>
<p class="failed" id="save-failed" hidden></p>
${empty}${rows}
<button type="button" data-add>Add a phrase</button>
${script(nonce)}
</body>
</html>`;
}
