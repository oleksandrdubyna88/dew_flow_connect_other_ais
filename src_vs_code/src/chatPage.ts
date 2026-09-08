import { escapeHtml, jsonForScript } from './webviewHtml';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';

/**
 * The conversation tab: the passage that started it, what has been said, and a box to say more.
 *
 * <p>PURE — no `vscode`, no `node:`. `bundledPage.test.ts` bundles this module the way the shipped
 * page is bundled and refuses a `node:` import in it, which is what makes "a page module imports
 * nothing from the host" a check rather than a sentence. Everything the page needs arrives as a
 * value; everything it wants done goes back as a message.</p>
 *
 * <p><b>The page escapes nothing it is handed at render time.</b> Every string that reaches
 * `innerHTML` — the messages, the failure, the capped region, the picker — is built by a function in
 * THIS file or by `chatPanel.ts`, and escaped there, at the point it is built. That is the invariant:
 * escaping happens where the string is made, never where it is written. A reviewer looking at the
 * `innerHTML` assignments in the script below should follow the value back to its builder rather
 * than conclude the page is unsafe. (Raised on the code round; it was true, and it was implicit.)</p>
 *
 * <p><b>The passage is at the top and it is not decoration.</b> The menu path takes whatever is in
 * the clipboard and cannot know whether it is the passage just selected or something copied an hour
 * ago — the gate raised that three times. Showing the text the conversation is ABOUT is how a person
 * sees a stale clipboard instead of discovering it in the answer. It is capped in height and scrolls:
 * a fifty-line selection would otherwise push the composer off the screen on open.</p>
 *
 * <p><b>The composer is disabled while a turn runs, and that is a feature.</b> A real explanation
 * took 9.4 s when it was measured, and eight of those seconds are silent. Two turns down one NDJSON
 * pipe would interleave; the page makes that impossible rather than the session refusing it late.</p>
 */

/** One thing said, by one of the two parties. */
export interface ChatMessage {
  readonly role: 'you' | 'model';
  readonly text: string;
}

/** A model the picker may offer. `remote` models say what they cannot do. */
export interface ChatModelChoice {
  readonly id: string;
  readonly label: string;
  readonly caption: string;
}

export interface ChatPageState {
  /** The Claude Code session this tab belongs to — its label, shown as the heading. */
  readonly title: string;
  readonly passage: string;
  readonly messages: readonly ChatMessage[];
  readonly models: readonly ChatModelChoice[];
  readonly modelId: string;
  /** A turn is in flight: the composer is locked and the thinking line is shown. */
  readonly running: boolean;
  /**
   * The conversation has reached its limit and cannot take another turn.
   *
   * <p>Only a remote model reaches this: it holds no conversation, so turn four re-sends everything
   * said so far for the fourth time, and the thread's cost grows while its usefulness does not. The
   * owner capped it at three on 2026-09-08. A capped page must OFFER something — a locked box with
   * no way out is the failure the gate named — so it shows the two honest actions, start again or
   * move the thread to a local model, which does have memory.</p>
   */
  readonly capped: boolean;
  /** Empty when nothing failed. A sentence when something did. */
  readonly failure: string;
  readonly uiScale: number;
}

/** The messages region on its own, so the host can push it without re-rendering the page. */
export function chatMessagesHtml(messages: readonly ChatMessage[]): string {
  if (messages.length === 0) {
    return '<p class="empty">Nothing asked yet.</p>';
  }

  return messages
    .map(
      (message) =>
        `<div class="msg ${message.role === 'you' ? 'you' : 'model'}">`
        + `<div class="who">${message.role === 'you' ? 'You' : 'The other AI'}</div>`
        + `<div class="what">${escapeHtml(message.text)}</div></div>`,
    )
    .join('');
}

/** The picker, or nothing at all when there is only one model to pick. */
export function chatPickerHtml(models: readonly ChatModelChoice[], chosen: string): string {
  if (models.length < 2) {
    return '';
  }
  const options = models
    .map(
      (model) =>
        `<option value="${escapeHtml(model.id)}"${model.id === chosen ? ' selected' : ''}>`
        + `${escapeHtml(model.label)}</option>`,
    )
    .join('');
  const caption = models.find((model) => model.id === chosen)?.caption ?? '';

  return `<div class="picker"><select id="model" aria-label="Which model answers">${options}</select>`
    + `<span class="caption" id="caption">${escapeHtml(caption)}</span></div>`;
}

/** What a capped conversation offers instead of a composer nobody can use. */
export function chatCappedHtml(capped: boolean): string {
  if (!capped) {
    return '';
  }

  return '<div class="capped"><p>This model keeps no conversation, so every follow-up re-sends the whole '
    + 'thread. Three turns is the limit.</p>'
    + '<button type="button" id="restart">Start a new conversation</button> '
    + '<button type="button" id="useLocal">Continue with a local model</button></div>';
}

/** The page's own styles. Its own function so the document below stays readable. */
function chatStyle(uiScale: number): string {
  return `  ${zoomStyle(uiScale)}
  body { font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 16px 20px; }
  header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 12px; }
  h1 { font-size: 1.2em; margin: 0; }
  .passage { border-left: 3px solid var(--vscode-panel-border); padding: 6px 0 6px 12px; margin: 0 0 16px; white-space: pre-wrap; opacity: .85; max-height: 180px; overflow-y: auto; }
  .msg { margin: 0 0 14px; }
  .msg .who { font-size: .85em; opacity: .7; margin-bottom: 3px; }
  .msg .what { white-space: pre-wrap; }
  .msg.you .what { opacity: .85; }
  .empty { opacity: .6; }
  .thinking { opacity: .75; margin: 0 0 12px; }
  .failure { border: 1px solid var(--vscode-inputValidation-errorBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 8px 10px; margin: 0 0 12px; }
  .capped { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .capped p { margin: 0 0 8px; }
  .picker { display: flex; gap: 8px; align-items: center; margin: 0 0 8px; }
  .caption { font-size: .85em; opacity: .7; }
  textarea { width: 100%; box-sizing: border-box; min-height: 64px; font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px; padding: 8px; }
  textarea[disabled] { opacity: .6; }
  .hint { font-size: .85em; opacity: .6; margin-top: 4px; }
${ZOOM_CSS}`;
}

/** The document, without its head or its script. */
function chatBody(state: ChatPageState): string {
  const locked = state.running || state.capped;

  return `<header><h1>${escapeHtml(state.title)}</h1>${zoomControlHtml(state.uiScale)}</header>
<div class="passage" id="passage">${escapeHtml(state.passage)}</div>
<div id="failure">${state.failure.length === 0 ? '' : `<div class="failure">${escapeHtml(state.failure)}</div>`}</div>
<div id="messages">${chatMessagesHtml(state.messages)}</div>
<div id="thinking">${state.running ? '<p class="thinking">Thinking…</p>' : ''}</div>
<div id="capped">${chatCappedHtml(state.capped)}</div>
<div id="pickerBox">${chatPickerHtml(state.models, state.modelId)}</div>
<textarea id="say" rows="3" placeholder="Ask about the text above…"${locked ? ' disabled' : ''}></textarea>
<div class="hint">Enter sends · Shift+Enter for a new line</div>`;
}

/** The page's behaviour. Its own function for the same reason the styles are. */
function chatScript(state: ChatPageState): string {
  return `(function () {
  const vscode = acquireVsCodeApi();
  let captions = ${jsonForScript(Object.fromEntries(state.models.map((model) => [model.id, model.caption])))};
  // The webview swallows a thrown error silently, and a page that stops responding to Enter with no
  // sign of why is the defect this trap exists to name. Same shape as the rounds log's.
  window.onerror = function (message) {
    const box = document.getElementById('failure');
    if (box) { box.textContent = 'The page hit an error: ' + message; }
    // And TELL the host. A trap that only writes into the page leaves the extension believing the
    // conversation is fine while the tab has stopped answering Enter.
    vscode.postMessage({ type: 'pageError', message: String(message) });
  };
  ${zoomScript()}
  function send() {
    const box = document.getElementById('say');
    if (!box || box.disabled) { return; }
    const text = box.value.trim();
    if (text.length === 0) { return; }
    box.value = '';
    vscode.postMessage({ type: 'command', command: 'send', text: text });
  }
  const say = document.getElementById('say');
  if (say) {
    say.addEventListener('keydown', function (event) {
      if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); send(); }
    });
  }
  function wirePicker() {
    const model = document.getElementById('model');
    if (!model) { return; }
    model.addEventListener('change', function () {
      const caption = document.getElementById('caption');
      if (caption) { caption.textContent = captions[model.value] || ''; }
      vscode.postMessage({ type: 'command', command: 'pick', id: model.value });
    });
  }
  function wireCapped() {
    const restart = document.getElementById('restart');
    if (restart) {
      restart.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'restart' });
      });
    }
    const useLocal = document.getElementById('useLocal');
    if (useLocal) {
      useLocal.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'useLocal' });
      });
    }
  }
  wirePicker();
  wireCapped();
  window.addEventListener('message', function (event) {
    const data = event.data || {};
    if (data.type !== 'state') { return; }
    const messages = document.getElementById('messages');
    if (messages && typeof data.messagesHtml === 'string') { messages.innerHTML = data.messagesHtml; }
    const thinking = document.getElementById('thinking');
    if (thinking) { thinking.innerHTML = data.running ? '<p class="thinking">Thinking…</p>' : ''; }
    const capped = document.getElementById('capped');
    if (capped) { capped.innerHTML = data.cappedHtml || ''; wireCapped(); }
    // The picker is re-rendered rather than nudged: after "continue with a local model" the whole
    // list can be different, and a select that only had its value set would show a model it no
    // longer offers.
    const pickerBox = document.getElementById('pickerBox');
    if (pickerBox && typeof data.pickerHtml === 'string') { pickerBox.innerHTML = data.pickerHtml; wirePicker(); }
    const failure = document.getElementById('failure');
    if (failure) { failure.innerHTML = data.failureHtml || ''; }
    const passage = document.getElementById('passage');
    if (passage && typeof data.passage === 'string') { passage.textContent = data.passage; }
    const box = document.getElementById('say');
    if (box) {
      const wasLocked = box.disabled;
      box.disabled = !!data.running || !!data.capped;
      // Back to the box when the turn ends. Without this every single follow-up costs a mouse click,
      // nine seconds after the last one — which is the whole conversation, one click at a time.
      if (wasLocked && !box.disabled && typeof box.focus === 'function') { box.focus(); }
    }
  });
}());`;
}

export function chatPageHtml(state: ChatPageState, nonce: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(state.title)}</title>
<style>
${chatStyle(state.uiScale)}
</style>
</head>
<body>
${chatBody(state)}
<script nonce="${nonce}">
${chatScript(state)}
</script>
</body>
</html>`;
}
