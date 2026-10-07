import { ChatProvider } from './chatModels';
import { ModelPreset, PromptPreset } from './chatPresets';
import { FORM_FIELDS_CSS, FORM_HEAD_CSS, formCardCss, formFrameCss } from './formPageStyle';
import { textControlsHtml, textControlsScript, textOf } from './textControls';
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
  /**
   * Saved models this build cannot read, by name — rows written before a preset carried its own
   * vendor. Shown rather than passed over, the rule every other list here keeps.
   */
  readonly unreadable: readonly string[];
  readonly uiScale: number;
  /** How far the text is from the theme's own colour; absent is the theme's own, as on the help page. */
  readonly textTone?: number;
}

function option(id: string, label: string, chosen: string): string {
  return `<option value="${escapeHtml(id)}"${id === chosen ? ' selected' : ''}>${escapeHtml(label)}</option>`;
}

/**
 * The attribute names a prompt block carries. The tab's own are the defaults; the new Settings page's Chat draws the
 * same block with names of its own (todo/PLAN_one_model_catalog.md E4.6b), because that page also draws the roles,
 * whose wiring reads `data-field` and `data-remove` — the commands' arrangement (`commandsPage.CommandAttrs`).
 */
export interface PresetAttrs {
  readonly row: string;
  readonly list: string;
  readonly field: string;
  readonly remove: string;
}

const TAB_ATTRS: PresetAttrs = { row: 'data-id', list: 'data-list', field: 'data-field', remove: 'data-remove' };

/** One saved prompt: its name, the main tick, Remove, and its words in a large box. */
export function promptBlock(preset: PromptPreset, attrs: PresetAttrs = TAB_ATTRS): string {
  const id = escapeHtml(preset.id);

  return `<div class="preset" ${attrs.row}="${id}">
  <div class="head">
    <input type="text" ${attrs.list}="prompt" ${attrs.field}="name" value="${escapeHtml(preset.name)}" placeholder="A name for this prompt">
    <label class="main"><input type="checkbox" ${attrs.list}="prompt" ${attrs.field}="main"${preset.main ? ' checked' : ''}> main</label>
    <button type="button" class="remove" ${attrs.remove}="prompt" ${attrs.row}="${id}">Remove</button>
  </div>
  <textarea ${attrs.list}="prompt" ${attrs.field}="text" rows="${PROMPT_ROWS}" placeholder="What the captured passage travels with">${escapeHtml(preset.text)}</textarea>
</div>`;
}

/**
 * One saved model: its NAME, the vendor it runs on, which of that vendor's models, and its prompt.
 *
 * <p>The vendor is shown rather than chosen here — it is picked once, in the wizard, from the same
 * list *Add a reviewer* offers, and changing it would change what the CLI, the endpoint and the key
 * are. The MODEL is a select, because that is the one half a person changes without changing what
 * the preset IS.</p>
 */
function modelRow(preset: ModelPreset, providers: readonly ChatProvider[]): string {
  const chosen = providers.find((provider) => provider.id === preset.id);
  // Its own vendor's models and nobody else's — the same rule the chat's picker keeps, and for the
  // same reason: a pair nothing can run is a pair a person can save here and be refused for later.
  const models = (chosen?.models ?? []).map((model) => option(model.id, model.label, preset.model)).join('');

  return `<div class="preset" data-id="${escapeHtml(preset.id)}">
  <div class="head">
    <input type="text" data-list="model" data-field="name" value="${escapeHtml(preset.name)}" placeholder="A name for this model">
    <span class="vendor">${escapeHtml(preset.runtime)}</span>
    <select data-list="model" data-field="model">${models}</select>
    <label class="main"><input type="checkbox" data-list="model" data-field="main"${preset.main ? ' checked' : ''}> main</label>
    <button type="button" class="remove" data-remove="model" data-id="${escapeHtml(preset.id)}">Remove</button>
  </div>
  <textarea data-list="model" data-field="startingPrompt" rows="3" placeholder="What the composer opens with when this model is chosen (optional)">${escapeHtml(preset.startingPrompt ?? '')}</textarea>
</div>`;
}

function styles(uiScale: number, textTone: number): string {
  return `${formFrameCss(uiScale, textTone)}
${formCardCss('.preset')}
  .preset.prompt-row { border-left-color: var(--vscode-textLink-foreground); }
${FORM_HEAD_CSS}
${FORM_FIELDS_CSS}
  .main { display: inline-flex; align-items: center; gap: 4px; opacity: .85; }
  .refused { font-size: .9em; opacity: .8; margin: 0 0 10px; }`;
}

function script(nonce: string): string {
  return `<script nonce="${nonce}">
(function () {
  const vscode = acquireVsCodeApi();
  ${textControlsScript()}
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
  const prompts = state.prompts.map((preset) => promptBlock(preset).replace('class="preset"', 'class="preset prompt-row"')).join('');
  const models = state.models.map((preset) => modelRow(preset, state.providers)).join('');
  // NAMED, not silently passed over. These rows name a reviewer, and repairing them would mean
  // reading the reviewer list — which this feature may not do. Saying so is what is left, and it is
  // more than the silence the code round found. (codex, three findings.)
  const unreadable = state.unreadable.length === 0
    ? ''
    : `<p class="refused">Saved before a model preset carried its own vendor, so ${state.unreadable.length === 1 ? 'it cannot be run' : 'they cannot be run'}`
      + ` — add ${state.unreadable.length === 1 ? 'it' : 'them'} again: `
      + `${state.unreadable.map((name) => `<b>${escapeHtml(name)}</b>`).join(', ')}.</p>`;

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Chat presets</title>
<style>
${styles(textOf(state).size, textOf(state).tone)}
</style>
</head>
<body>
<header><h1>Chat presets</h1>${textControlsHtml(textOf(state).size, textOf(state).tone)}</header>
<p class="lead">The prompts and the models you keep as buttons above the composer. Everything here is saved as you type.</p>
<h2>Prompts</h2>
<p class="lead">The one marked <b>main</b> is used when a capture sends by itself.</p>
${prompts}
<button type="button" data-add="prompt">Add a prompt</button>
<h2>Models</h2>
<p class="lead">A model preset is a vendor, one of its models, and the instruction the composer opens with.
The one marked <b>main</b> is what a captured passage opens on.</p>
${models}${unreadable}
<button type="button" data-add="model">Add a model</button>
${script(nonce)}
</body>
</html>`;
}
