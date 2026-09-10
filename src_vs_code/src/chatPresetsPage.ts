import { ChatProvider } from './chatModels';
import { ModelPreset, PromptPreset } from './chatPresets';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';
import { escapeHtml } from './webviewHtml';

/**
 * The tab where a person keeps their prompts and their models.
 *
 * <p>A page module and a thin panel host, which is the arrangement this extension already has twice
 * — the rounds log and the help page. Not a third one.</p>
 *
 * <p><b>Everything on this page is text the person in front of it wrote</b>, and that makes the
 * escaping a different question from the rest of the panel: elsewhere a label comes from a catalog
 * this product shipped or a server it talked to, and the worst case is somebody else's mistake. Here
 * it is their own name for their own prompt, which they can perfectly well paste a `<script>` into
 * to see what happens — and the answer must be that they see a `<script>`.</p>
 *
 * <p><b>The prompt box is large, and that was asked for in as many words.</b> A prompt is often a
 * paragraph and sometimes several, and a person editing one in a three-line box is reading it three
 * lines at a time. It also sizes itself to its content where the engine can.</p>
 */

/** The rows the prompt editor opens at. Twelve is about a screenful of a real instruction. */
const PROMPT_ROWS = 14;

export interface PresetsPageState {
  readonly prompts: readonly PromptPreset[];
  readonly models: readonly ModelPreset[];
  /** What a model preset may point at — the same rows the chat's own picker offers. */
  readonly providers: readonly ChatProvider[];
  readonly uiScale: number;
}

/** Every message this page can send, decided without a host so a test can reach the decision. */
export type PresetCommand =
  | { readonly kind: 'edit'; readonly list: 'prompt' | 'model'; readonly id: string; readonly field: string; readonly value: string | boolean }
  | { readonly kind: 'add'; readonly list: 'prompt' | 'model' }
  | { readonly kind: 'remove'; readonly list: 'prompt' | 'model'; readonly id: string }
  | { readonly kind: 'zoom'; readonly delta: number }
  | { readonly kind: 'ignore' };

const IGNORE: PresetCommand = { kind: 'ignore' };

/** The fields each list HAS. A name this does not know is not a field — `__proto__` included. */
const FIELDS: Record<'prompt' | 'model', readonly string[]> = {
  prompt: ['name', 'text', 'main'],
  model: ['name', 'provider', 'model', 'startingPrompt'],
};

function listOf(value: unknown): 'prompt' | 'model' | undefined {
  return value === 'prompt' || value === 'model' ? value : undefined;
}

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
export function presetEdit(message: unknown): PresetCommand {
  if (typeof message !== 'object' || message === null) {
    return IGNORE;
  }
  const said = message as Record<string, unknown>;
  const list = listOf(said['list']);
  if (said['type'] === 'zoom') {
    const delta = said['delta'];

    return typeof delta === 'number' && Number.isFinite(delta)
      ? { kind: 'zoom', delta: Math.max(-1, Math.min(1, Math.trunc(delta))) }
      : IGNORE;
  }
  if (list === undefined) {
    return IGNORE;
  }
  if (said['type'] === 'add') {
    return { kind: 'add', list };
  }
  if (said['type'] === 'remove') {
    const id = idOf(said['id']);

    return id.length === 0 ? IGNORE : { kind: 'remove', list, id };
  }
  if (said['type'] !== 'edit') {
    return IGNORE;
  }
  const id = idOf(said['id']);
  const field = said['field'];
  // A field this list does not have is not an edit. The check is against a list of names rather
  // than a `in` test on the object, so no key of Object.prototype can ever be one of them.
  if (id.length === 0 || typeof field !== 'string' || !FIELDS[list].includes(field)) {
    return IGNORE;
  }
  const value = said['value'];
  if (field === 'main') {
    return typeof value === 'boolean' ? { kind: 'edit', list, id, field, value } : IGNORE;
  }

  return typeof value === 'string' ? { kind: 'edit', list, id, field, value } : IGNORE;
}

function option(id: string, label: string, chosen: string): string {
  return `<option value="${escapeHtml(id)}"${id === chosen ? ' selected' : ''}>${escapeHtml(label)}</option>`;
}

function promptRow(preset: PromptPreset): string {
  return `<div class="preset" data-id="${escapeHtml(preset.id)}">
  <div class="head">
    <input type="text" data-list="prompt" data-field="name" value="${escapeHtml(preset.name)}" placeholder="A name for this prompt">
    <label class="main"><input type="checkbox" data-list="prompt" data-field="main"${preset.main ? ' checked' : ''}> main</label>
    <button type="button" class="remove" data-remove="prompt" data-id="${escapeHtml(preset.id)}">Remove</button>
  </div>
  <textarea data-list="prompt" data-field="text" rows="${PROMPT_ROWS}" placeholder="What the captured passage travels with">${escapeHtml(preset.text)}</textarea>
</div>`;
}

function modelRow(preset: ModelPreset, providers: readonly ChatProvider[]): string {
  const chosen = providers.find((provider) => provider.id === preset.provider);
  const rows = providers.map((provider) => option(provider.id, provider.label, preset.provider)).join('');
  // Its own row's models and nobody else's — the same rule the chat's picker keeps, and for the same
  // reason: a pair nothing can run is a pair a person can save here and be refused for later.
  const models = (chosen?.models ?? []).map((model) => option(model.id, model.label, preset.model)).join('');

  return `<div class="preset" data-id="${escapeHtml(preset.id)}">
  <div class="head">
    <input type="text" data-list="model" data-field="name" value="${escapeHtml(preset.name)}" placeholder="A name for this model">
    <select data-list="model" data-field="provider">${rows}</select>
    <select data-list="model" data-field="model">${models}</select>
    <button type="button" class="remove" data-remove="model" data-id="${escapeHtml(preset.id)}">Remove</button>
  </div>
  <textarea data-list="model" data-field="startingPrompt" rows="3" placeholder="What the composer opens with when this model is chosen (optional)">${escapeHtml(preset.startingPrompt ?? '')}</textarea>
</div>`;
}

function styles(uiScale: number): string {
  return `  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 16px 24px; max-width: 900px; margin: 0 auto; ${zoomStyle(uiScale)} }
  header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 8px; }
  h1 { font-size: 1.2em; margin: 0; }
  h2 { font-size: 1em; margin: 24px 0 4px; }
  .lead { opacity: .8; margin: 0 0 12px; }
  .preset { border: 1px solid var(--vscode-panel-border); border-left-width: 3px; border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .preset.prompt-row { border-left-color: var(--vscode-textLink-foreground); }
  .head { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; flex-wrap: wrap; }
  .head input[type="text"] { flex: 1 1 12rem; min-width: 0; }
  input, select, textarea { font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 3px; padding: 4px 6px; }
  /* The box a person READS a prompt in. It also grows with its content where the engine can do it;
     the rows attribute is the floor for every engine that cannot. */
  textarea { width: 100%; box-sizing: border-box; field-sizing: content; max-height: 60vh; }
  .main { display: inline-flex; align-items: center; gap: 4px; opacity: .85; }
  button { font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 3px; padding: 4px 12px; cursor: pointer; }
  button.remove { color: var(--vscode-foreground); background: none; border: 1px solid var(--vscode-panel-border); }
${ZOOM_CSS}`;
}

function script(nonce: string): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${zoomScript()}
  // Delegated on the document: every row is replaced whenever the lists change, and a listener bound
  // to a field would die with the row it was bound to.
  document.addEventListener('input', function (event) {
    const field = event.target;
    if (!field || !field.dataset || typeof field.dataset.field !== 'string') { return; }
    const row = field.closest('[data-id]');
    if (!row || !row.dataset) { return; }
    vscode.postMessage({
      type: 'edit',
      list: field.dataset.list,
      id: row.dataset.id,
      field: field.dataset.field,
      value: field.type === 'checkbox' ? field.checked : field.value,
    });
  });
  document.addEventListener('change', function (event) {
    const field = event.target;
    if (field && field.dataset && field.type === 'checkbox') {
      const row = field.closest('[data-id]');
      if (row && row.dataset) {
        vscode.postMessage({
          type: 'edit', list: field.dataset.list, id: row.dataset.id,
          field: field.dataset.field, value: field.checked,
        });
      }
    }
  });
  document.addEventListener('click', function (event) {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    const add = pressed.closest('[data-add]');
    if (add && add.dataset) { vscode.postMessage({ type: 'add', list: add.dataset.add }); return; }
    const remove = pressed.closest('[data-remove]');
    if (remove && remove.dataset) {
      vscode.postMessage({ type: 'remove', list: remove.dataset.remove, id: remove.dataset.id });
    }
  });
}());
</script>`;
}

/** The page. Its own function so the document below stays readable, as every page here does it. */
export function chatPresetsHtml(state: PresetsPageState, nonce: string): string {
  const prompts = state.prompts.map((preset) => promptRow(preset).replace('class="preset"', 'class="preset prompt-row"')).join('');
  const models = state.models.map((preset) => modelRow(preset, state.providers)).join('');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Chat presets</title>
<style>
${styles(state.uiScale)}
</style>
</head>
<body>
<header><h1>Chat presets</h1>${zoomControlHtml(state.uiScale)}</header>
<p class="lead">The prompts and the models you keep as buttons above the composer. Everything here is saved as you type.</p>
<h2>Prompts</h2>
<p class="lead">The one marked <b>main</b> is used when a capture sends by itself.</p>
${prompts}
<button type="button" data-add="prompt">Add a prompt</button>
<h2>Models</h2>
<p class="lead">A model preset names the reviewer row that answers, and optionally which of its models.</p>
${models}
<button type="button" data-add="model">Add a model</button>
${script(nonce)}
</body>
</html>`;
}
