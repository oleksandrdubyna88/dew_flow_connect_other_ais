import { CoaiSettings, LANGUAGES, PROVIDERS, TRANSLATORS } from './settingsShape';
import { Escalation } from './escalations';
import { SessionFile } from './rounds';

/**
 * The panel's HTML, as a pure function of what it shows.
 *
 * <p>Pure and `vscode`-free so the markup is a test rather than something checked by opening a
 * sidebar. Every colour comes from a VS Code theme variable — a panel that ignores the theme is
 * the first thing a person notices and the last thing they forgive.</p>
 */

export interface PanelState {
  readonly settings: CoaiSettings;
  readonly serverInstalled: boolean;
  readonly serverVersion: string;
  readonly questions: readonly Escalation[];
  readonly sessions: readonly SessionFile[];
}

/** `nonce` is required by the content security policy; the caller mints a fresh one per render. */
export function panelHtml(state: PanelState, nonce: string): string {
  const s = state.settings;
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent; padding: 0 0 16px 0; margin: 0;
  }
  h2 {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    margin: 18px 0 8px; font-weight: 600;
  }
  h2:first-of-type { margin-top: 10px; }
  .row { display: flex; align-items: center; gap: 8px; margin: 6px 0; }
  .row label { flex: 1; min-width: 0; }
  .hint { opacity: .65; font-size: 11px; margin: 2px 0 8px; line-height: 1.45; }
  input[type="text"], input[type="number"], select {
    background: var(--vscode-input-background); color: var(--vscode-input-foreground);
    border: 1px solid var(--vscode-input-border, transparent); border-radius: 2px;
    padding: 3px 6px; font-family: inherit; font-size: inherit; min-width: 0;
  }
  input[type="text"] { width: 100%; }
  input[type="number"] { width: 68px; }
  select { width: 100%; }
  button {
    background: var(--vscode-button-background); color: var(--vscode-button-foreground);
    border: none; border-radius: 2px; padding: 5px 10px; cursor: pointer;
    font-family: inherit; font-size: inherit; width: 100%; margin: 4px 0;
  }
  button:hover { background: var(--vscode-button-hoverBackground); }
  button.secondary {
    background: var(--vscode-button-secondaryBackground);
    color: var(--vscode-button-secondaryForeground);
  }
  .provider {
    border: 1px solid var(--vscode-panel-border);
    border-radius: 3px; padding: 8px; margin: 6px 0;
  }
  .provider .name { font-weight: 600; text-transform: capitalize; }
  .question {
    border-left: 3px solid var(--vscode-inputValidation-warningBorder);
    background: var(--vscode-inputValidation-warningBackground);
    padding: 8px 10px; margin: 8px 0; border-radius: 0 3px 3px 0;
  }
  .question .meta { opacity: .7; font-size: 11px; margin-top: 4px; }
  .finding { font-size: 11px; opacity: .85; margin: 3px 0 0 8px; }
  .verdict { font-family: var(--vscode-editor-font-family); font-size: 11px; }
  .empty { opacity: .6; font-style: italic; margin: 6px 0; }
  .status { font-size: 11px; opacity: .8; margin: 4px 0 10px; }
</style>
</head>
<body>

${questionsSection(state.questions)}

<h2>Server</h2>
<div class="status">${
    state.serverInstalled
      ? `coai-mcp ${escapeHtml(state.serverVersion)} is installed.`
      : 'coai-mcp is not installed yet.'
  }</div>
<button data-command="install">${state.serverInstalled ? 'Update the MCP server…' : 'Install the MCP server…'}</button>
<button class="secondary" data-command="copyConfig">Copy the MCP config block</button>
<button class="secondary" data-command="copySnippet">Copy the CLAUDE.md snippet</button>
<div class="hint">Settings travel to the server inside that config block — copy it again after changing anything here, and restart your MCP client.</div>

<h2>Reviewers</h2>
${PROVIDERS.map((p) => providerCard(p, s)).join('\n')}

<h2>Language</h2>
<div class="row">
  <label for="language">Ask and answer in</label>
  <select id="language" data-setting="language">
    ${LANGUAGES.map(
      (l) => `<option value="${l.code}"${s.language === l.code ? ' selected' : ''}>${escapeHtml(l.label)}</option>`,
    ).join('\n    ')}
  </select>
</div>
<div class="row">
  <label for="translator">Translated by</label>
  <select id="translator" data-setting="translator.provider">
    ${TRANSLATORS.map(
      (t) => `<option value="${t.id}"${s.translator.provider === t.id ? ' selected' : ''}>${escapeHtml(t.label)}</option>`,
    ).join('\n    ')}
  </select>
</div>
<div class="row">
  <label for="translatorModel">Model</label>
  <input type="text" id="translatorModel" data-setting="translator.model"
         value="${escapeHtml(s.translator.model)}" placeholder="the CLI's default">
</div>
<div class="hint">A question already written in this language is left alone. If the translator cannot run, you get the original with the reason — never an error in its place.</div>

<h2>The gate</h2>
<div class="row">
  <label for="maxRounds">Rounds per stage</label>
  <input type="number" id="maxRounds" min="1" data-setting="maxRounds" value="${s.maxRounds}">
</div>
<div class="row">
  <label for="gateThreshold">Passes at or under</label>
  <input type="number" id="gateThreshold" min="0" data-setting="gateThreshold" value="${s.gateThreshold}">
</div>
<div class="hint">Blocking and major findings only, after the same defect from two vendors is merged into one.</div>
<div class="row">
  <label for="onExhausted">When rounds run out</label>
  <select id="onExhausted" data-setting="onExhausted">
    <option value="human"${s.onExhausted === 'human' ? ' selected' : ''}>Ask a human</option>
    <option value="continue"${s.onExhausted === 'continue' ? ' selected' : ''}>Continue, and say so</option>
    <option value="escalate"${s.onExhausted === 'escalate' ? ' selected' : ''}>Climb the ladder</option>
  </select>
</div>

<h2>Limits</h2>
<div class="row">
  <label for="maxConcurrency">Reviewers at once</label>
  <input type="number" id="maxConcurrency" min="1" data-setting="maxConcurrency" value="${s.maxConcurrency}">
</div>
<div class="row">
  <label for="maxPerProvider">Per vendor</label>
  <input type="number" id="maxPerProvider" min="1" data-setting="maxPerProvider" value="${s.maxPerProvider}">
</div>
<div class="row">
  <label for="reviewerTimeoutMinutes">Reviewer timeout (min)</label>
  <input type="number" id="reviewerTimeoutMinutes" min="1" data-setting="reviewerTimeoutMinutes" value="${s.reviewerTimeoutMinutes}">
</div>
<div class="row">
  <label for="escalationMinutes">Wait for you (min)</label>
  <input type="number" id="escalationMinutes" min="1" data-setting="escalationMinutes" value="${s.escalationMinutes}">
</div>

<h2>Vendor keys</h2>
<div class="row">
  <label for="credsKey">CredsForDevs config key</label>
</div>
<input type="text" id="credsKey" data-setting="credsKey" value="${escapeHtml(s.credsKey)}" placeholder="empty = keyless vendors only">
<div class="hint">A pass to one vault entry holding the API keys — revocable, and useless while VS Code is closed. Codex and Gemini need none if their CLIs are signed in.</div>

<h2>Recent rounds</h2>
${roundsSection(state.sessions)}

<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener('change', () => {
      const value = el.type === 'checkbox' ? el.checked : el.type === 'number' ? Number(el.value) : el.value;
      vscode.postMessage({ type: 'setting', key: el.dataset.setting, value });
    });
  }
  for (const el of document.querySelectorAll('[data-command]')) {
    el.addEventListener('click', () => vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
  }
</script>
</body>
</html>`;
}

function providerCard(provider: string, s: CoaiSettings): string {
  const enabled = s.providers.includes(provider as never);
  const model = s.models[provider as keyof typeof s.models] ?? '';
  return `<div class="provider">
  <div class="row">
    <input type="checkbox" id="p-${provider}" data-setting="provider.${provider}"${enabled ? ' checked' : ''}>
    <label class="name" for="p-${provider}">${provider}</label>
  </div>
  <input type="text" data-setting="model.${provider}" value="${escapeHtml(model)}" placeholder="model — empty for the CLI's default">
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
