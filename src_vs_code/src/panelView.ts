import { CoaiSettings, LANGUAGES, TRANSLATORS } from './settingsShape';
import { Escalation } from './escalations';
import { HELP, HelpKey } from './help';
import { ModelChoice, modelsFor, modelsProvenance } from './models';
import { SessionFile } from './rounds';
import { Vendor } from './vendors';

/**
 * The panel's HTML, as a pure function of what it shows.
 *
 * <p>Pure and `vscode`-free so the markup is a test rather than something checked by opening a
 * sidebar. Three rules the tests hold: every colour comes from a theme variable, every value that
 * came from a file or a person is escaped — a question is DATA, never markup — and nothing can
 * make the view scroll sideways.</p>
 *
 * <p><b>Labels sit above their controls, never beside them.</b> A sidebar is narrow and its width
 * is the person's choice; a label-and-field row collides the moment they drag it in, and then the
 * whole view scrolls sideways. Stacked fields cannot do that.</p>
 *
 * <p><b>One section per job, and most of them closed.</b> Almost everything here is configured
 * once and never touched again; a wall of it hides the two things that do change — who reviews,
 * and what came back.</p>
 */

export interface PanelState {
  readonly settings: CoaiSettings;
  readonly vendors: readonly Vendor[];
  readonly codexModels: readonly ModelChoice[];
  readonly serverInstalled: boolean;
  readonly serverVersion: string;
  readonly questions: readonly Escalation[];
  readonly sessions: readonly SessionFile[];
  /** Which collapsible sections are open. Empty falls back to {@link OPEN_BY_DEFAULT}. */
  readonly openSections: readonly string[];
}

/**
 * Nothing is open before anyone touches anything.
 *
 * <p>The panel opens as a list of headings that fits in one glance, and you expand what you came
 * for. Two sections open was still a wall on a narrow sidebar — and this is a panel you configure
 * once and afterwards only visit when something is waiting on you.</p>
 */
export const OPEN_BY_DEFAULT: readonly string[] = [];

export function panelHtml(state: PanelState, nonce: string): string {
  const open = state.openSections.length === 0 ? OPEN_BY_DEFAULT : state.openSections;
  const body = [
    questionsSection(state.questions),
    section('reviewers', 'Reviewers', open, reviewersBody(state)),
    section('language', 'Language', open, languageBody(state)),
    section('gate', 'The gate', open, gateBody(state.settings)),
    section('limits', 'Limits', open, limitsBody(state.settings)),
    section('keys', 'Vendor keys', open, keysBody(state)),
    section('server', 'Server', open, serverBody(state)),
    section('rounds', 'Recent rounds', open, roundsBody(state.sessions)),
  ].join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>${CSS}</style>
</head>
<body>
${body}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  for (const el of document.querySelectorAll('[data-setting]')) {
    el.addEventListener('change', () => {
      const value = el.type === 'checkbox' ? el.checked : el.type === 'number' ? Number(el.value) : el.value;
      if (value === '__other__') {
        // Not a model — a request to type one; the input box comes from the provider side.
        vscode.postMessage({ type: 'command', command: 'customModel', id: el.dataset.vendor ?? '__translator__' });
        return;
      }
      vscode.postMessage({ type: 'setting', key: el.dataset.setting, value, vendor: el.dataset.vendor });
    });
  }
  for (const el of document.querySelectorAll('.section')) {
    el.addEventListener('toggle', () =>
      vscode.postMessage({ type: 'section', id: el.dataset.section, open: el.open }));
  }
  for (const el of document.querySelectorAll('[data-command]')) {
    el.addEventListener('click', () =>
      vscode.postMessage({ type: 'command', command: el.dataset.command, id: el.dataset.id }));
  }
</script>
</body>
</html>`;
}

/**
 * One collapsible section.
 *
 * <p><code>&lt;details&gt;</code> rather than a scripted accordion: keyboard-operable and
 * screen-reader-correct for free, and its arrow cannot get out of step with its own state. The
 * open set is carried in {@link PanelState} because the panel repaints on every change — a
 * section that snapped shut while somebody was typing in it would be worse than no collapsing.</p>
 */
function section(id: string, title: string, open: readonly string[], body: string): string {
  return `<details class="section" data-section="${id}"${open.includes(id) ? ' open' : ''}>
  <summary>${escapeHtml(title)}</summary>
${body}
</details>`;
}

function reviewersBody(state: PanelState): string {
  return `${state.vendors.map((v) => vendorCard(v, state.codexModels)).join('\n')}
<button class="add" data-command="addVendor" title="${escapeHtml(HELP.addVendor)}">＋&nbsp; Add a reviewer</button>`;
}

function vendorCard(vendor: Vendor, codexModels: readonly ModelChoice[]): string {
  const id = escapeHtml(vendor.id);
  const models = modelsFor(vendor.runtime, codexModels, vendor.model);
  const endpoint =
    vendor.baseUrl.length === 0
      ? ''
      : `
  <div class="field">
    <input type="url" data-setting="baseUrl" data-vendor="${id}" title="${escapeHtml(HELP.vendorBaseUrl)}"
           value="${escapeHtml(vendor.baseUrl)}">
    <div class="hint">Its OpenAI-compatible endpoint. The key for it lives in the vault entry under <code>${id}</code>.</div>
  </div>`;

  return `<div class="vendor">
  <div class="head">
    <input type="checkbox" id="v-${id}" data-setting="enabled" data-vendor="${id}"${vendor.enabled ? ' checked' : ''}
           title="${escapeHtml(HELP.vendorEnabled)}">
    <label class="name" for="v-${id}">${id}</label>
    <button class="link" data-command="removeVendor" data-id="${id}">remove</button>
  </div>
  <div class="field">
    <select data-setting="model" data-vendor="${id}" title="${escapeHtml(HELP.vendorModel)}">
      ${modelOptions(models, vendor.model)}
    </select>
    <div class="hint">${escapeHtml(vendor.runtime)} · ${escapeHtml(modelsProvenance(vendor.runtime, codexModels))}</div>
  </div>${endpoint}
</div>`;
}

function languageBody(state: PanelState): string {
  const s = state.settings;
  const languages = LANGUAGES.map(
    (l) => `<option value="${l.code}"${s.language === l.code ? ' selected' : ''}>${escapeHtml(l.label)}</option>`,
  ).join('\n    ');
  const translators = TRANSLATORS.map(
    (t) => `<option value="${t.id}"${s.translator.provider === t.id ? ' selected' : ''}>${escapeHtml(t.label)}</option>`,
  ).join('\n    ');
  const runtime =
    s.translator.provider === 'codex' ? 'codex' : s.translator.provider === 'claude' ? 'claude' : 'gemini';
  const models = modelOptions(modelsFor(runtime, state.codexModels, s.translator.model), s.translator.model);

  return `<div class="field">
  ${labelled('language', 'Ask and answer in', 'language')}
  <select id="language" data-setting="language">
    ${languages}
  </select>
</div>
<div class="field">
  ${labelled('translator', 'Translated by', 'translator')}
  <select id="translator" data-setting="translator.provider">
    ${translators}
  </select>
</div>
<div class="field">
  ${labelled('translatorModel', 'Translator model', 'translatorModel')}
  <select id="translatorModel" data-setting="translator.model">
    ${models}
  </select>
  <div class="hint">A question already in this language is left alone. If the translator cannot run you get the original with the reason — never an error in its place.</div>
</div>`;
}

function gateBody(s: CoaiSettings): string {
  return `<div class="field inline">
  ${labelled('maxRounds', 'Rounds per stage', 'maxRounds')}
  <input type="number" id="maxRounds" min="1" data-setting="maxRounds" value="${s.maxRounds}">
</div>
<div class="field inline">
  ${labelled('gateThreshold', 'Passes at or under', 'gateThreshold')}
  <input type="number" id="gateThreshold" min="0" data-setting="gateThreshold" value="${s.gateThreshold}">
</div>
<div class="field">
  ${labelled('onExhausted', 'When the rounds run out', 'onExhausted')}
  <select id="onExhausted" data-setting="onExhausted">
    <option value="human"${s.onExhausted === 'human' ? ' selected' : ''}>Ask a human</option>
    <option value="continue"${s.onExhausted === 'continue' ? ' selected' : ''}>Continue, and say so</option>
    <option value="escalate"${s.onExhausted === 'escalate' ? ' selected' : ''}>Climb the ladder</option>
  </select>
</div>`;
}

function limitsBody(s: CoaiSettings): string {
  return `<div class="field inline">
  ${labelled('maxConcurrency', 'Reviewers at once', 'maxConcurrency')}
  <input type="number" id="maxConcurrency" min="1" data-setting="maxConcurrency" value="${s.maxConcurrency}">
</div>
<div class="field inline">
  ${labelled('maxPerProvider', 'Per vendor', 'maxPerProvider')}
  <input type="number" id="maxPerProvider" min="1" data-setting="maxPerProvider" value="${s.maxPerProvider}">
</div>
<div class="field inline">
  ${labelled('reviewerTimeoutMinutes', 'Reviewer timeout, minutes', 'reviewerTimeout')}
  <input type="number" id="reviewerTimeoutMinutes" min="1" data-setting="reviewerTimeoutMinutes" value="${s.reviewerTimeoutMinutes}">
</div>
<div class="field inline">
  ${labelled('escalationMinutes', 'Wait for you, minutes', 'escalationMinutes')}
  <input type="number" id="escalationMinutes" min="1" data-setting="escalationMinutes" value="${s.escalationMinutes}">
</div>`;
}

/**
 * The keys, and — first — whether they are needed at all.
 *
 * <p>Somebody reading "CredsForDevs config key" with codex and gemini configured has no way to
 * know the answer is "not yet". A field that cannot say whether it applies to you is a field that
 * gets filled in wrongly, so this one says it.</p>
 */
function keysBody(state: PanelState): string {
  const needy = state.vendors.filter((v) => v.enabled && v.baseUrl.length > 0);
  const field = `<div class="field">
  ${labelled('credsKey', 'CredsForDevs config key', 'credsKey')}
  <input type="text" id="credsKey" data-setting="credsKey" value="${escapeHtml(state.settings.credsKey)}"
         placeholder="${needy.length === 0 ? 'not needed yet' : 'the key from Enable Code Access…'}">
</div>`;

  if (needy.length === 0) {
    return `<div class="hint"><b>Nothing to fill in yet.</b> Every reviewer you have signs in through its own CLI, so none of them needs an API key. This becomes necessary when you add a vendor that has no CLI of its own — DeepSeek, OpenRouter, any endpoint you give a URL to.</div>
${field}`;
  }

  const names = needy.map((v) => escapeHtml(v.id)).join(', ');
  const one = needy.length === 1;
  return `<div class="hint">${names} ${one ? 'reaches an endpoint of its own, so it needs' : 'reach endpoints of their own, so they need'} an API key. Put the keys in ONE CredsForDevs entry of kind <code>config</code> — a JSON object keyed by vendor name — turn on <i>Enable Code Access…</i> for it, and paste the key it mints here.</div>
${field}`;
}

function serverBody(state: PanelState): string {
  return `<div class="status">${
    state.serverInstalled
      ? `coai-mcp ${escapeHtml(state.serverVersion)} is installed.`
      : 'coai-mcp is not installed yet — use the ⋯ menu above.'
  }</div>
<div class="hint">Changes here are saved for the server straight away; it reads them when your MCP client next starts it. The config block in the ⋯ menu is pasted once, when you first set it up.</div>`;
}

function questionsSection(questions: readonly Escalation[]): string {
  if (questions.length === 0) {
    return '';
  }
  const cards = questions
    .map((q) => {
      const findings = q.openFindings
        .map(
          (f) =>
            `<div class="finding">• ${escapeHtml(f.severity)} ${escapeHtml(f.category)} — ${escapeHtml(f.title)}</div>`,
        )
        .join('\n      ');
      return `<div class="question">
      <div>${escapeHtml(q.question)}</div>
      ${findings}
      <div class="meta">${escapeHtml(q.branch)}${q.translationNote ? ` · shown untranslated: ${escapeHtml(q.translationNote)}` : ''}</div>
      <button data-command="answer" data-id="${escapeHtml(q.id)}">Answer…</button>
    </div>`;
    })
    .join('\n');
  // Never collapsible: a blocked round is the one thing that must not be tidied away.
  return `<h2>A review is waiting on you</h2>\n${cards}`;
}

function roundsBody(sessions: readonly SessionFile[]): string {
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

/**
 * A model list as SELECT options: the CLI's default first, every known model, the saved value
 * kept even when unknown, and "another model…" as the way out of the list.
 *
 * <p>A select rather than a datalist, learned the hard way: a datalist FILTERS its options by the
 * field's current value, so the moment a model was chosen every other option vanished and the
 * picker read as broken.</p>
 */
function modelOptions(models: readonly ModelChoice[], current: string): string {
  const known = models
    .map((m) => `<option value="${escapeHtml(m.id)}"${m.id === current ? ' selected' : ''}>${escapeHtml(m.label)}</option>`)
    .join('\n      ');
  return `<option value=""${current === '' ? ' selected' : ''}>the CLI's default</option>
      ${known}
      <option value="__other__">another model…</option>`;
}

/**
 * The little "?" that explains a setting on hover.
 *
 * <p>A native `title` rather than a scripted popup: it works with the keyboard, it cannot escape
 * the webview, and it needs no state of its own.</p>
 */
export function help(key: HelpKey): string {
  return `<span class="help" title="${escapeHtml(HELP[key])}" role="img" aria-label="What this means">?</span>`;
}

/** A label with its explanation beside it. */
function labelled(forId: string, text: string, key: HelpKey): string {
  return `<label for="${forId}">${help(key)}${escapeHtml(text)}</label>`;
}

/** Every value here came from a file or a person; none of it may become markup. */
export function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

const CSS = `
  /* Everything measures its own border and padding: without this a 100% field plus padding is
     wider than its parent, and the whole view gains a horizontal scrollbar. */
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent;
    /* Air down both sides: nothing should touch the edge of the view, and the right-hand gap is
       a shade wider so a field never sits under the scrollbar's track. */
    margin: 0; padding: 4px 14px 20px 12px; overflow-x: hidden;
  }
  h2 {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    margin: 10px 0 6px; font-weight: 600;
  }
  .section { border-top: 1px solid var(--vscode-panel-border); padding: 0 0 8px; }
  .section > summary {
    font-size: 11px; text-transform: uppercase; letter-spacing: .06em; opacity: .75;
    font-weight: 600; padding: 10px 0 6px; cursor: pointer; list-style: none;
    display: flex; align-items: center; gap: 6px; user-select: none;
  }
  .section > summary:hover { opacity: 1; }
  .section > summary::-webkit-details-marker { display: none; }
  /* A real chevron, drawn from two borders rather than borrowed from punctuation: it matches the
     Explorer's weight, scales with the text, and points the right way in both states with no
     script. (A '›' glyph rendered a third of this size — a disclosure arrow nobody can hit.) */
  .section > summary::before {
    content: ''; flex: 0 0 auto; width: 6px; height: 6px; margin: 0 4px 0 2px;
    border-right: 1.5px solid currentColor; border-bottom: 1.5px solid currentColor;
    transform: rotate(-45deg); transition: transform .12s ease;
  }
  .section[open] > summary::before { transform: rotate(45deg); margin-top: -3px; }
  .field { margin: 8px 0; }
  .field > label { display: block; margin-bottom: 3px; }
  .inline { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
  .inline > label { flex: 1 1 auto; min-width: 0; margin-bottom: 0; }
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
  button.add { margin-top: 10px; padding: 7px 10px; font-weight: 600; }
  button.link {
    background: none; color: var(--vscode-textLink-foreground); padding: 0; width: auto;
    margin: 0; text-decoration: underline; font-size: 11px;
  }
  button.link:hover { background: none; color: var(--vscode-textLink-activeForeground); }
  .help {
    display: inline-block; width: 14px; height: 14px; line-height: 14px; margin-right: 6px;
    text-align: center; border-radius: 50%; font-size: 10px; font-weight: 700; cursor: help;
    background: var(--vscode-badge-background); color: var(--vscode-badge-foreground);
    opacity: .8; flex: 0 0 auto;
  }
  .help:hover { opacity: 1; }
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
  .status { margin: 2px 0 0; }
`;
