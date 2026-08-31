import { CoaiSettings, LANGUAGES, TRANSLATORS } from './settingsShape';
import { Escalation } from './escalations';
import { ModelChoice, modelsFor, modelsProvenance } from './models';
import { SessionFile } from './rounds';
import { Vendor } from './vendors';

/**
 * The panel's HTML, as a pure function of what it shows.
 *
 * <p>Pure and `vscode`-free so the markup is a test rather than something checked by opening a
 * sidebar. Two rules the tests hold: every colour comes from a theme variable, and every value
 * that came from a file or a person is escaped — a question is DATA, never markup.</p>
 *
 * <p><b>Labels sit above their controls, never beside them.</b> A sidebar is narrow and its width
 * is the person's choice; a label-and-field row collides the moment they drag it in, and then the
 * whole view scrolls sideways. Stacked fields cannot do that, and nothing here is wide enough to
 * need it.</p>
 */

export interface PanelState {
  readonly settings: CoaiSettings;
  readonly vendors: readonly Vendor[];
  readonly codexModels: readonly ModelChoice[];
  readonly serverInstalled: boolean;
  readonly serverVersion: string;
  readonly questions: readonly Escalation[];
  readonly sessions: readonly SessionFile[];
}

export function panelHtml(state: PanelState, nonce: string): string {
  const s = state.settings;
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  /* Everything measures its own border and padding: without this a 100% field plus padding is
     wider than its parent, and the whole view gains a horizontal scrollbar. */
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent;
    margin: 0; padding: 4px 0 20px; overflow-x: hidden;
  }
  h2 {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    margin: 20px 0 6px; font-weight: 600;
  }
  h2:first-of-type { margin-top: 6px; }
  .field { margin: 8px 0; }
  .field > label { display: block; margin-bottom: 3px; }
  .inline { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .inline > label { flex: 1 1 auto; min-width: 0; }
  .inline > input[type="number"] { flex: 0 0 64px; width: 64px; }
  .hint { opacity: .65; font-size: 11px; margin: 3px 0 0; line-height: 1.45; }
  input[type="text"], input[type="url"], input[type="number"], select {
    background: var(--vscode-input-background); color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px;
    padding: 3px 6px; font-family: inherit; font-size: inherit;
    width: 100%; max-width: 100%; min-width: 0;
  }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 10px; cursor: pointer;
    font-family: inherit; font-size: inherit; width: 100%; margin: 6px 0 0;
  }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button.secondary {
    background: var(--vscode-button-secondaryBackground);
    color: var(--vscode-button-secondaryForeground);
  }
  button.link {
    background: none; color: var(--vscode-textLink-foreground); padding: 0; width: auto;
    margin: 0; text-decoration: underline; font-size: 11px;
  }
  button.link:hover { background: none; color: var(--vscode-textLink-activeForeground); }
  .vendor {
    border: 1px solid var(--vscode-panel-border);
    border-radius: 3px; padding: 8px; margin: 8px 0;
  }
  .vendor .head { display: flex; align-items: center; gap: 6px; }
  .vendor .name { font-weight: 600; flex: 1 1 auto; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
  .vendor input[type="checkbox"] { flex: 0 0 auto; margin: 0; }
  .question {
    border-left: 3px solid var(--vscode-inputValidation-warningBorder);
    background: var(--vscode-inputValidation-warningBackground);
    padding: 8px 10px; margin: 8px 0; border-radius: 0 3px 3px 0;
  }
  .question .meta { opacity: .7; font-size: 11px; margin-top: 4px; }
  .finding { font-size: 11px; opacity: .85; margin: 3px 0 0 8px; }
  .verdict {
    font-family: var(--vscode-editor-font-family); font-size: 11px; margin: 2px 0;
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  }
  .empty { opacity: .6; font-style: italic; margin: 6px 0; }
  .status { font-size: 11px; opacity: .8; margin: 2px 0 0; }
</style>
</head>
<body>

${questionsSection(state.questions)}

<h2>Reviewers</h2>
${state.vendors.map((v) => vendorCard(v, state.codexModels)).join('\n')}
<button class="secondary" data-command="addVendor">Add a reviewer…</button>

<h2>Language</h2>
<div class="field">
  <label for="language">Ask and answer in</label>
  <select id="language" data-setting="language">
    ${LANGUAGES.map(
      (l) => `<option value="${l.code}"${s.language === l.code ? ' selected' : ''}>${escapeHtml(l.label)}</option>`,
    ).join('\n    ')}
  </select>
</div>
<div class="field">
  <label for="translator">Translated by</label>
  <select id="translator" data-setting="translator.provider">
    ${TRANSLATORS.map(
      (t) => `<option value="${t.id}"${s.translator.provider === t.id ? ' selected' : ''}>${escapeHtml(t.label)}</option>`,
    ).join('\n    ')}
  </select>
</div>
<div class="field">
  <label for="translatorModel">Translator model</label>
  <input type="text" id="translatorModel" data-setting="translator.model" list="translator-models"
         value="${escapeHtml(s.translator.model)}" placeholder="the CLI's default">
  <datalist id="translator-models">
    ${modelsFor(s.translator.provider === 'codex' ? 'codex' : 'gemini', state.codexModels, s.translator.model)
      .map((m) => `<option value="${escapeHtml(m.id)}">${escapeHtml(m.label)}</option>`)
      .join('\n    ')}
  </datalist>
  <div class="hint">A question already in this language is left alone. If the translator cannot run you get the original with the reason — never an error in its place.</div>
</div>

<h2>The gate</h2>
<div class="field inline">
  <label for="maxRounds">Rounds per stage</label>
  <input type="number" id="maxRounds" min="1" data-setting="maxRounds" value="${s.maxRounds}">
</div>
<div class="field inline">
  <label for="gateThreshold">Passes at or under</label>
  <input type="number" id="gateThreshold" min="0" data-setting="gateThreshold" value="${s.gateThreshold}">
</div>
<div class="hint">Blocking and major findings only, after the same defect from two vendors is merged into one.</div>
<div class="field">
  <label for="onExhausted">When the rounds run out</label>
  <select id="onExhausted" data-setting="onExhausted">
    <option value="human"${s.onExhausted === 'human' ? ' selected' : ''}>Ask a human</option>
    <option value="continue"${s.onExhausted === 'continue' ? ' selected' : ''}>Continue, and say so</option>
    <option value="escalate"${s.onExhausted === 'escalate' ? ' selected' : ''}>Climb the ladder</option>
  </select>
</div>

<h2>Limits</h2>
<div class="field inline">
  <label for="maxConcurrency">Reviewers at once</label>
  <input type="number" id="maxConcurrency" min="1" data-setting="maxConcurrency" value="${s.maxConcurrency}">
</div>
<div class="field inline">
  <label for="maxPerProvider">Per vendor</label>
  <input type="number" id="maxPerProvider" min="1" data-setting="maxPerProvider" value="${s.maxPerProvider}">
</div>
<div class="field inline">
  <label for="reviewerTimeoutMinutes">Reviewer timeout, minutes</label>
  <input type="number" id="reviewerTimeoutMinutes" min="1" data-setting="reviewerTimeoutMinutes" value="${s.reviewerTimeoutMinutes}">
</div>
<div class="field inline">
  <label for="escalationMinutes">Wait for you, minutes</label>
  <input type="number" id="escalationMinutes" min="1" data-setting="escalationMinutes" value="${s.escalationMinutes}">
</div>

<h2>Vendor keys</h2>
<div class="field">
  <label for="credsKey">CredsForDevs config key</label>
  <input type="text" id="credsKey" data-setting="credsKey" value="${escapeHtml(s.credsKey)}"
         placeholder="empty = keyless vendors only">
  <div class="hint">A pass to one vault entry holding the API keys — revocable, and useless while VS Code is closed. Codex and Gemini need none if their CLIs are signed in.</div>
</div>

<h2>Server</h2>
<div class="status">${
    state.serverInstalled
      ? `coai-mcp ${escapeHtml(state.serverVersion)} is installed.`
      : 'coai-mcp is not installed yet — use the ⋯ menu above.'
  }</div>
<div class="hint">Settings travel to the server inside its config block: after changing anything here, copy the block again from the ⋯ menu and restart your MCP client.</div>

<h2>Recent rounds</h2>
${roundsSection(state.sessions)}

<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener('change', () => {
      const value = el.type === 'checkbox' ? el.checked : el.type === 'number' ? Number(el.value) : el.value;
      vscode.postMessage({ type: 'setting', key: el.dataset.setting, value, vendor: el.dataset.vendor });
    });
  }
  for (const el of document.querySelectorAll('[data-command]')) {
    el.addEventListener('click', () =>
      vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
  }
</script>
</body>
</html>`;
}

function vendorCard(vendor: Vendor, codexModels: readonly ModelChoice[]): string {
  const listId = `models-${escapeHtml(vendor.id)}`;
  const models = modelsFor(vendor.runtime, codexModels, vendor.model);
  return `<div class="vendor">
  <div class="head">
    <input type="checkbox" id="v-${escapeHtml(vendor.id)}" data-setting="enabled" data-vendor="${escapeHtml(vendor.id)}"${vendor.enabled ? ' checked' : ''}>
    <label class="name" for="v-${escapeHtml(vendor.id)}">${escapeHtml(vendor.id)}</label>
    <button class="link" data-command="removeVendor" data-id="${escapeHtml(vendor.id)}">remove</button>
  </div>
  <div class="field">
    <input type="text" list="${listId}" data-setting="model" data-vendor="${escapeHtml(vendor.id)}"
           value="${escapeHtml(vendor.model)}" placeholder="model — empty for the CLI's default">
    <datalist id="${listId}">
      ${models.map((m) => `<option value="${escapeHtml(m.id)}">${escapeHtml(m.label)}</option>`).join('\n      ')}
    </datalist>
    <div class="hint">${escapeHtml(vendor.runtime)} · ${escapeHtml(modelsProvenance(vendor.runtime, codexModels))}</div>
  </div>${
    vendor.baseUrl.length > 0
      ? `\n  <div class="field">
    <input type="url" data-setting="baseUrl" data-vendor="${escapeHtml(vendor.id)}" value="${escapeHtml(vendor.baseUrl)}">
    <div class="hint">Its OpenAI-compatible endpoint. The key for it lives in the vault entry under <code>${escapeHtml(vendor.id)}</code>.</div>
  </div>`
      : ''
  }
</div>`;
}

function questionsSection(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const cards = questions
    .map((q) => {
      const findings = q.openFindings
        .map((f) => `<div class="finding">• ${escapeHtml(f.severity)} ${escapeHtml(f.category)} — ${escapeHtml(f.title)}</div>`)
        .join('\n      ');
      return `<div class="question">
      <div>${escapeHtml(q.question)}</div>
      ${findings}
      <div class="meta">${escapeHtml(q.branch)}${q.translationNote ? ` · shown untranslated: ${escapeHtml(q.translationNote)}` : ''}</div>
      <button data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
    </div>`;
    })
    .join('\n');
  return `<h2>A review is waiting on you</h2>\n${cards}`;
}

function roundsSection(sessions: readonly SessionFile[]): string {
  const rounds = sessions
    .flatMap((s) => s.rounds.map((r) => ({ branch: s.state.branch, ...r })))
    .sort((a, b) => b.completedUtc.localeCompare(a.completedUtc))
    .slice(0, 6);
  if (rounds.length === 0) {
    return '<div class="empty">No rounds yet.</div>';
  }
  return rounds
    .map(
      (r) =>
        `<div class="verdict">${escapeHtml(r.branch)} · ${escapeHtml(r.stage)} ${r.number} · <b>${escapeHtml(r.verdict)}</b> · ${r.gatingCount} gating</div>`,
    )
    .join('\n');
}

/** Every value here came from a file or a person; none of it may become markup. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}
