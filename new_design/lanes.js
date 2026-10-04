/* ConnectOtherAIs settings mockup: the Security lane and Chat tabs. Owns the `lanes` slice.
   The Security lane follows todo/PLAN_the_security_tab_reads_at_a_glance.md (branch feat/security-tab-e1):
   the general prompt first, coloured cards with a text badge, conditions collapsed, real buttons. The flows
   (Restore asks first, Remove names its pairs, a new prompt from a pair re-points that pair) are
   securityFlows.ts; the warnings are securityLaneView.ts and securityPromptCard.ts on that branch. */
(function () {
  'use strict';

  const app = () => window.COAI_APP;
  const esc = (s) => app().esc(s);
  const help = (text) => app().help(text);
  const L = () => app().state.lanes;
  const checked = (on) => (on ? ' checked' : '');
  /* shared/security-lane.json limits: SecurityCatalog.MostRuns and MostPrompts. */
  const MAX_PAIRS = 16;
  const MAX_PROMPTS = 32;
  /* securityLane.ts:26 (feat/security-tab-e1) — the first coai-mcp that runs the lane. */
  const SECURITY_SINCE = '0.41.0';
  /* securityPromptFiles.ts:19 — the server's SecurityContext.MaxPromptBytes. */
  const PROMPT_MAX_BYTES = 65536;

  /* shared/security-lane.json */
  const SIGNALS = [
    ['sql', 'SQL and raw queries'], ['auth-token', 'Tokens and sessions'], ['oauth', 'OAuth and OpenID Connect'],
    ['authz', 'Authorization and ownership'], ['xss', 'HTML and script injection'], ['ssrf', 'Outbound requests'],
    ['path', 'Paths, files and archives'], ['upload', 'File uploads and multipart data'], ['command', 'Processes and shell commands'],
    ['deserialize', 'Deserialization'], ['secrets', 'Credentials and disclosure'], ['crypto', 'Cryptography and randomness'],
    ['concurrency', 'Races, payments and transaction isolation'], ['webhooks', 'Webhook signatures and replay'],
    ['prompt-injection', 'LLM prompts and tool calling'], ['entry-point', 'Entry points and middleware'],
  ];
  const TRIGGERS = SIGNALS.filter(([id]) => id !== 'entry-point');
  const label = (id) => SIGNALS.find(([s]) => s === id)?.[1] || id;

  const PRESETS = [
    { id: 'redteam-general', always: true, triggers: [], focus: ['entry-point'] },
    { id: 'redteam-authz', triggers: ['authz'], focus: ['authz', 'entry-point', 'sql'] },
    { id: 'redteam-sql', triggers: ['sql'], focus: ['sql', 'entry-point'] },
    { id: 'redteam-concurrency', triggers: ['concurrency'], focus: ['concurrency', 'sql', 'entry-point'] },
    { id: 'redteam-auth-tokens', triggers: ['auth-token', 'oauth'], focus: ['auth-token', 'oauth', 'entry-point'] },
    { id: 'redteam-ssrf', triggers: ['ssrf'], focus: ['ssrf', 'entry-point'] },
    { id: 'redteam-webhooks', triggers: ['webhooks'], focus: ['webhooks', 'crypto', 'entry-point'] },
    { id: 'redteam-files', triggers: ['path', 'upload'], focus: ['path', 'upload', 'entry-point'] },
    { id: 'redteam-command', triggers: ['command'], focus: ['command', 'entry-point'] },
    { id: 'redteam-deserialize', triggers: ['deserialize'], focus: ['deserialize', 'entry-point'] },
    { id: 'redteam-secrets', triggers: ['secrets'], focus: ['secrets', 'auth-token', 'crypto'] },
    { id: 'redteam-prompt-injection', triggers: ['prompt-injection'], focus: ['prompt-injection', 'entry-point'] },
    { id: 'redteam-xss', triggers: ['xss'], focus: ['xss', 'entry-point'] },
  ];

  const LANGUAGES = [['en', 'English'], ['es', 'Español'], ['de', 'Deutsch'], ['ru', 'Русский'], ['uk', 'Українська']];
  const AUTOSEND = [
    ['keyboard', 'The keybinding sends; the menu waits', 'The keybinding copies the selection itself, so it asks at once. The right-click menu takes what you last copied and fills the box for you to send.'],
    ['always', 'Always send at once', 'Both paths send straight away.'],
    ['never', 'Never — always let me press Enter', 'Both paths fill the box and wait.'],
  ];
  const SHORTCUTS = [
    ['Chat with other AI', 'Ctrl+Alt+A', 'in the editor or the Claude panel'],
    ['CoAI: default', 'right-click', 'sends the selection with the main prompt'],
    ['CoAI: choose', 'right-click', 'pick the prompt and the model first'],
    ['CoAI: go to conversation', 'Ctrl+Alt+G', 'from the editor or the Claude panel'],
    ['CoAI: back to where this chat came from', 'Ctrl+Alt+G', 'inside a chat'],
    ['CoAI: switch conversations…', 'Ctrl+Shift+Alt+G', ''],
  ];

  /* The "?" texts. Chat: help.ts (chatPrompt, chatLanguage, chatAutoSend, chatModel) and chatPresetsPage.ts's leads.
     The Security lane has no HELP entries in the product yet; these say what securityLane*.ts does. */
  const HELP = {
    enabled: 'Whether the pairs below run beside the ordinary gate. Off runs none of them and keeps every pair and prompt as it is. The lane never replaces the ordinary gate: keep an ordinary code or feature reviewer on as well.',
    threshold: 'The lane passes when this many blocking and major security findings are left, or fewer. Zero demands a clean security review; minor findings never count. From 0 to 100.',
    maxRounds: 'How many rounds the security lane may take on one change before it stops and reports what is left — two unless you change it, at most ten.',
    triggers: 'The signals that start this check: it runs when any changed file carries one of them. A shipped preset with nothing ticked never runs; your own prompt with nothing ticked and no words of its own runs on every change.',
    readFirst: 'The files carrying these signals are given to the reviewer first. This only orders what the reviewer reads — it never decides whether the check runs.',
    ownWords: 'Words of this check\'s own: a word, a phrase or a piece of code that starts it even when none of its ticked signals is found. Case does not matter; write /pattern/ for a regular expression.',
    text: 'The words the reviewer is given. For a shipped prompt, writing here replaces the shipped text; empty keeps it. For your own prompt this IS the prompt, and until it holds usable text its pairs cannot run. Saved as prompts/<id>.md.',
    pairing: 'Tick a model to pair it with this prompt: that model reviews the change with this prompt whenever its conditions match. The models offered are the ones ticked "Security lane" on Models.',
    prompts: `Thirteen prompts ship; you may add your own, up to ${MAX_PROMPTS} in all. Green is shipped as it ships, orange is a shipped prompt you changed, purple is yours.`,
    pairs: `A pair is one model and one prompt; it runs when the prompt's conditions match the change. One pair per model and prompt, at most ${MAX_PAIRS} in a lane.`,
    source: 'Slice sends the code around what the change touches — what a model with a small context needs. Diff sends the diff itself. A local engine starts on slice, every other model on diff.',
    tokens: 'The most the pair\'s source may fill, from 1,024 to 200,000 tokens — 24,000 for slice and 200,000 for diff unless you change it.',
    stages: 'Which rounds this pair reviews: the code round, the feature round, or both.',
    words: 'The words that find a signal, shared by every check. Case does not matter, and a changed file\'s path counts as well as its text.',
    chatPrompt: 'What the selected passage is sent with. One word — Explain — unless you change it, and the box is several lines high because a word is not always enough: "explain this to somebody who knows C# but not Rust" is a different question from "explain this". The passage itself always arrives below your instruction, fenced and marked as material, so a paragraph that reads like an order is treated as text rather than obeyed.',
    chatLanguage: 'The language the OTHER AI answers in. Deliberately not the language of these help pages: that one is English on most machines, and borrowing it would deliver English explanations — which is the one thing this feature exists to avoid. English by default, because the models are best at it; set it to yours if a dense English answer is what sent you here.',
    chatAutoSend: 'Who presses send. The keybinding copies the selection itself, one keystroke earlier, so it can be trusted to be the passage you meant — it asks straight away. The right-click menu cannot copy for you (closing the menu takes the selection out of the panel), so it takes whatever you last copied and fills the box for you to send, because it has no way to know how old that is. Always sends on both paths; Never fills the box on both.',
    chatModel: 'Which model answers a chat. Only models on a runtime the chat can speak to appear here; any other model is listed underneath with the reason, rather than quietly missing — a picker with a gap in it cannot tell you whether it is a bug or a policy.',
    chatPresets: 'The prompts and the models you keep as buttons above the composer. Everything here is saved as you type. The one marked main is used when a capture sends by itself.',
    chatStarting: 'A model preset is a vendor, one of its models, and the instruction the composer opens with. The one marked main is what a captured passage opens on.',
  };

  function defaults() {
    return {
      security: {
        enabled: true, threshold: 0, maxRounds: 2,
        overrides: {},
        /* Prompt TEXT per id: for a shipped prompt it replaces the shipped words; for yours it IS the prompt. */
        texts: { 'redteam-sql': 'Act as an attacker who can reach every query this change builds. For each one, show the input that breaks it…' },
        custom: [{ id: 'redteam-billing', triggers: ['concurrency'], focus: ['concurrency', 'sql'] }],
        pairs: [
          { instance: 'codex', prompt: 'redteam-authz', context: 'diff', tokens: 200000, code: true, feature: true },
          { instance: 'qwen', prompt: 'redteam-sql', context: 'diff', tokens: 200000, code: true, feature: false },
          { instance: 'qwen', prompt: 'redteam-billing', context: 'diff', tokens: 200000, code: true, feature: true },
        ],
      },
      chat: {
        main: 'claude', promptChoice: '', language: 'en', autoSend: 'keyboard', starting: {},
        presets: [
          { id: 'preset-1', name: 'Explain', text: 'Explain', main: true },
          { id: 'preset-2', name: 'Find the bug', text: 'What is wrong with this code, and how would you prove it?', main: false },
        ],
      },
    };
  }

  /* ---------- Security lane: what a card is ---------- */

  const prompts = () => [...PRESETS.map((p) => ({ ...p, shipped: true, ...(L().security.overrides[p.id] || {}) })),
    ...L().security.custom.map((p) => ({ ...p, shipped: false }))];
  const textOf = (id) => (L().security.texts || {})[id] || '';
  const fileOf = (id) => `prompts/${id}.md`;
  const securityPool = () => app().state.instances.filter((i) => i.features.includes('security'));

  /* securityPromptFiles.ts: no file, an empty one, the bare operator placeholder, too large, or text the server sends. */
  function textState(text) {
    if (!text) return 'none';
    const trimmed = text.trim();
    if (!trimmed) return 'blank';
    if (new TextEncoder().encode(text).length > PROMPT_MAX_BYTES) return 'oversized';
    const placeholder = trimmed.startsWith('<!-- OPERATOR') && trimmed.endsWith('-->') && !trimmed.slice(0, -3).includes('-->');
    return placeholder ? 'placeholder' : 'written';
  }
  /* An override that holds anything is the person's text, usable by the server or not: deleting it asks first. */
  const HOLDS_CONTENT = new Set(['written', 'placeholder', 'oversized']);
  const TEXT_PROBLEMS = { placeholder: 'holds only the operator placeholder', oversized: 'is over 64 KiB' };

  /* Edited means usable text of its own, or conditions that are not the shipped ones (securityLaneState.ts promptState). */
  function kindOf(p) {
    if (!p.shipped) return 'mine';
    const base = PRESETS.find((x) => x.id === p.id);
    const same = (x, y) => [...x].sort().join() === [...y].sort().join();
    const changed = textState(textOf(p.id)) === 'written' || (p.patterns || []).length || !same(p.triggers, base.triggers) || !same(p.focus, base.focus);
    return changed ? 'edited' : 'shipped';
  }

  /* Restore shows wherever it would do something: text in the file, or changed conditions (general's have their own Clear). */
  const restorable = (p, kind) => p.shipped && (HOLDS_CONTENT.has(textState(textOf(p.id))) || (kind === 'edited' && !p.always));

  /* The server too old to run the lane at all: its switch and the pairing boxes do nothing, so they are drawn disabled. */
  function laneTooOld() {
    const v = app().mcpVersion();
    return Boolean(v) && app().older(v, SECURITY_SINCE);
  }

  /* ---------- Security lane: the page ---------- */

  function securityPage() {
    const s = L().security;
    return `<div class="lanes-page">
      ${app().skew(SECURITY_SINCE, 'The Security lane')}
      <p class="lead">Security reviews beside the ordinary gate: a model and a red-team prompt, run when the change touches
        what the prompt is about. The prompt text stays in your prompt files.</p>
      ${app().modelsUsedHtml('security', 'Security models')}
      ${ordinaryNote()}
      <div class="plan-grid">${lanePanel(s)}${matchingPanel()}</div>
      ${routingPanel()}
      ${promptsSection()}
      ${pairsPanel(s)}</div>`;
  }

  function ordinaryNote() {
    const ordinary = app().state.instances.some((i) => i.enabled && (i.features.includes('code') || i.features.includes('feature')));
    return ordinary ? '' : '<p class="callout warn">Enable an ordinary code or feature reviewer too: the security lane cannot replace the ordinary gate.</p>';
  }

  function lanePanel(s) {
    const old = laneTooOld() ? ' disabled' : '';
    return `<section class="panel"><h3>The lane</h3>
      <div class="row-line"><label class="inline"><input type="checkbox" data-sl="enabled"${checked(s.enabled)}${old}> Run the security lane</label>${help(HELP.enabled)}</div>
      <div class="field-line"><label for="sl-threshold">Allowed major / blocking findings${help(HELP.threshold)}</label>
        <input id="sl-threshold" type="number" class="num" min="0" max="100" value="${esc(s.threshold)}" data-sl-num="threshold"></div>
      <div class="field-line"><label for="sl-rounds">Maximum rounds${help(HELP.maxRounds)}</label>
        <input id="sl-rounds" type="number" class="num" min="1" max="10" value="${esc(s.maxRounds)}" data-sl-num="maxRounds"></div>
      <p class="from panel-foot">Pairs: ${s.pairs.length} of ${MAX_PAIRS} · Prompts: ${prompts().length} of ${MAX_PROMPTS} (${PRESETS.length} shipped)</p></section>`;
  }

  /* The prompt library: the cards, the cap, and the prompts a too-full lane loses at the server. */
  function promptsSection() {
    const all = prompts();
    const full = all.length >= MAX_PROMPTS;
    const dropped = all.slice(MAX_PROMPTS).map((p) => p.id);
    const lost = dropped.length ? `<p class="callout warn">The server reads at most ${MAX_PROMPTS} prompts and drops ${esc(dropped.join(', '))}.</p>` : '';
    return `<h3 class="section-title">Prompts${help(HELP.prompts)}</h3>
      ${app().skew(app().PLANNED, 'A check\'s own words (the "Or contains" row on each card)')}
      ${lost}
      <div class="prompt-cards">${all.map(promptCard).join('')}</div>
      <p class="row-line"><button type="button" class="btn small" data-sl-action="new-prompt"${full ? ' disabled' : ''}>＋ New custom prompt</button>
        <span class="from">Prompts: ${all.length} of ${MAX_PROMPTS} (${PRESETS.length} shipped)${full ? ' — the library is full' : ''}.
          Green — shipped · orange — a shipped one you changed · purple — yours.</span></p>`;
  }

  function promptCard(p) {
    const kind = kindOf(p);
    const tag = { shipped: 'default', edited: 'edited', mine: 'custom' }[kind];
    return `<div class="pcard ${kind}" data-sp="${esc(p.id)}">
      <h4>${esc(p.id)} <span class="kind">${tag}</span></h4>
      ${p.always ? alwaysLine(p) : ''}
      ${textLine(p)}
      <div class="pair-boxes"><span class="filter-label">Paired with${help(HELP.pairing)}</span>${pairTicks(p)}</div>
      ${textHtml(p)}
      ${p.always ? '' : triggersBlock(p)}
      ${p.always ? '' : conditionsHtml(p)}
      ${cardFoot(p, kind)}</div>`;
  }

  function pairTicks(p) {
    const old = laneTooOld() ? ' disabled' : '';
    return securityPool().map((i) => {
      const on = L().security.pairs.some((x) => x.instance === i.id && x.prompt === p.id);
      return `<label class="inline"><input type="checkbox" data-sl-tick="${esc(i.id)}"${checked(on)}${old}> ${esc(i.name)}</label>`;
    }).join('') || '<span class="from">No model is ticked "Security lane" on Models.</span>';
  }

  /* General's whole condition story: none, or legacy triggers stored before it shipped (securityPromptCard.ts alwaysBody). */
  function alwaysLine(p) {
    if (!p.triggers.length) return '<p class="from">Runs on every code change while a model is ticked below; it has no conditions.</p>';
    return `<p class="from"><span class="state warn">${esc(p.id)} has stored conditions (${esc(p.triggers.join(', '))}) from before it shipped.</span>
        This version ignores them, but an older MCP server still runs it only when they match. Clear them so it runs on every code change.</p>
      <p class="row-line"><button type="button" class="btn small ghost" data-sl-action="clear-conditions">Clear stored conditions</button></p>`;
  }

  /* Why the server would not send this prompt's text (securityPromptCard.ts textProblem / missingText). */
  function textLine(p) {
    const state = textState(textOf(p.id));
    const why = TEXT_PROBLEMS[state];
    if (why) return `<p class="from"><span class="state err">${esc(fileOf(p.id))} ${why}, so the lane reports its pairs as unable to run.</span></p>`;
    if (!p.shipped && state !== 'written') return '<p class="from"><span class="state err">No prompt text yet</span> — a pair with this prompt is skipped until you write it below.</p>';
    return '';
  }

  function cardFoot(p, kind) {
    const holds = HOLDS_CONTENT.has(textState(textOf(p.id)));
    const what = holds ? `puts back the shipped text and conditions — asks first, because ${esc(fileOf(p.id))} is deleted` : 'puts back the shipped conditions';
    const restore = restorable(p, kind)
      ? `<button type="button" class="btn small ghost" data-sl-action="restore">Restore default</button><span class="from">${what}</span>` : '';
    const remove = kind === 'mine'
      ? `<button type="button" class="btn small ghost danger" data-sl-action="remove-prompt">Remove</button><span class="from">its file stays on disk</span>` : '';
    return restore || remove ? `<div class="foot">${restore}${remove}</div>` : '';
  }

  const openConditions = new Set();
  const openTexts = new Set();

  /* The prompt's words, edited in place. Collapsed by default, like the conditions, with a one-line summary;
     its open state survives a repaint. In the extension the text is the override file prompts/<id>.md. */
  function textHtml(p) {
    const text = textOf(p.id);
    const placeholder = p.shipped ? 'The text this product ships. Write here to replace it — leave empty to keep the shipped words.'
      : 'The question this prompt asks the reviewer.';
    return `<details class="more" data-text-open="${esc(p.id)}"${openTexts.has(p.id) ? ' open' : ''}>
      <summary>Prompt text${help(HELP.text)} <span class="what">${esc(textSummary(p, text))}</span></summary>
      <textarea rows="6" data-sl-text placeholder="${esc(placeholder)}" aria-label="Prompt text for ${esc(p.id)}">${esc(text)}</textarea>
      <div class="count-line"><span>Saved as ${esc(fileOf(p.id))}</span><span data-count>${text.length} characters</span></div></details>`;
  }

  function textSummary(p, text) {
    const state = textState(text);
    if (state === 'placeholder') return 'only the operator placeholder — not sent';
    if (state === 'oversized') return 'over 64 KiB — not sent';
    if (text) return `${p.shipped ? 'replaced' : 'yours'}: ${text.slice(0, 70)}${text.length > 70 ? '…' : ''}`;
    return p.shipped ? 'the text this product ships' : 'not written yet';
  }

  /* ---------- triggers: when a check runs ---------- */

  const T = () => window.COAI_TRIGGERS;
  const wordOverrides = () => L().security.words || {};

  /* The trigger setting itself, under the prompt text: one box per signal in two columns, then the
     card's OWN words — a word, a phrase, an expression or a piece of code that starts this check alone. */
  function triggersBlock(p) {
    const boxes = TRIGGERS.map(([id, l]) => `<label class="inline"><input type="checkbox" data-sl-trig="${id}"${checked(p.triggers.includes(id))}>
      ${esc(l)} <code class="from">${id}</code></label>`).join('');
    const own = (p.patterns || []).map((w) => `<span class="tchip mine">${esc(w)}
      <button type="button" class="icon-btn" data-sl-unpattern="${esc(w)}" aria-label="Remove ${esc(w)}">✕</button></span>`).join('');
    const ignored = own ? app().skew(app().PLANNED, `${p.id}'s own words`) : '';
    return `<div class="block trig-block"><h4 class="block-title">Runs when the change touches${help(HELP.triggers)}</h4>
      <div class="legend-cols">${boxes}</div>
      <div class="trig-row"><span class="filter-label">Or contains${app().newTag('Words of a check\'s own.')}${help(HELP.ownWords)}</span>
        <div class="chips">${own}<input type="text" class="word-add wide" data-sl-addpattern
          placeholder="＋ a word, a phrase or a piece of code · /regex/ for a pattern" aria-label="Add a word of this check's own"></div></div>
      ${ignored}
      ${triggerStatus(p)}</div>`;
  }

  /* What these settings mean, in one line — said outside any fold. */
  function triggerStatus(p) {
    const any = p.triggers.length || (p.patterns || []).length;
    if (any) return '';
    return p.shipped
      ? '<p class="from"><span class="state err">Nothing ticked — this preset never runs.</span> Tick a signal or add a word.</p>'
      : '<p class="from">Nothing ticked — it runs on every change.</p>';
  }

  /* Focus only orders what the reviewer reads first; it never decides whether the check runs. */
  function conditionsHtml(p) {
    const box = (id) => `<label class="inline"><input type="checkbox" data-sl-cond="focus" value="${id}"${checked(p.focus.includes(id))}> ${esc(label(id))}</label>`;
    return `<details class="more" data-cond="${esc(p.id)}"${openConditions.has(p.id) ? ' open' : ''}>
      <summary>Read first${help(HELP.readFirst)} <span class="what">${esc(p.focus.map(label).join(', ') || 'nothing in particular')}</span></summary>
      <p class="from">The files carrying these signals are given to the reviewer first. This never decides whether the check runs.</p>
      <div class="legend-cols">${SIGNALS.map(([id]) => box(id)).join('')}</div></details>`;
  }

  /* The rules the server matches a change by, and a box to try them on real text. */
  function matchingPanel() {
    return `<section class="panel"><h3>How a change is matched</h3>
      <ul class="tight">
        <li>Every changed file's path and text is searched for each signal's <b>words</b> — case does not matter. <code>sql</code> is also found by statement shapes.</li>
        <li>A check runs when <b>any</b> changed file carries one of its signals. Removed lines count too: deleting a check is a change worth reading.</li>
        <li>Your own prompt with no signal runs on every change; a shipped one with none never runs.</li>
        <li>Not searched: <code>.md</code> files, binaries and credential files. Over 256 KB a file is only partly checked; at most 512 files.</li>
      </ul>
      <label class="field" style="margin-top: 8px"><span>Try it — paste a diff or some code</span>
        <textarea rows="4" data-sl-try placeholder="var rows = db.Query&lt;Order&gt;(&quot;SELECT * FROM orders WHERE id = &quot; + id);">${esc(tryText)}</textarea></label>
      <div data-try-result>${tryResult(tryText)}</div></section>`;
  }

  let tryText = '';

  function tryResult(text) {
    if (!text.trim()) return '<p class="from">Paste something to see which checks it would start.</p>';
    const found = T().detect(text, wordOverrides());
    const runs = T().wouldRun(found, prompts().filter((p) => L().security.pairs.some((x) => x.prompt === p.id)), text);
    const signals = found.length ? found.map((f) => `<span class="tchip on">${esc(label(f.signal))} <span class="from">← ${esc(f.by)}</span></span>`).join('')
      : '<span class="from">no signal</span>';
    const checks = runs.length ? runs.map((p) => `<span class="tchip run">${esc(p.id)}</span>`).join('') : '<span class="from">no paired check would run</span>';
    return `<div class="trig-row"><span class="filter-label">Signals</span><div class="chips">${signals}</div></div>
      <div class="trig-row"><span class="filter-label">Would run</span><div class="chips">${checks}</div></div>`;
  }

  /* Signal → the words that find it → the checks it starts and the ones that read it first. An "always" prompt's
     leftover triggers start nothing in this version, so it is never listed as started by a signal. */
  function routingRow([id, l], all) {
    const starts = all.filter((p) => !p.always && p.triggers.includes(id));
    const reads = all.filter((p) => p.focus.includes(id) && !p.triggers.includes(id));
    const startCell = id === 'entry-point' ? '<span class="from">focus only — never starts a check</span>'
      : starts.length ? starts.map((p) => `<span class="tchip run">${esc(p.id)}</span>`).join('') : '<span class="state warn">nothing runs on it</span>';
    return `<tr data-signal="${id}"><td><code>${id}</code><br><span class="from">${esc(l)}</span></td>
      <td>${wordsCell(id)}</td><td><div class="chips">${startCell}</div></td>
      <td><div class="chips">${reads.map((p) => `<span class="tchip">${esc(p.id)}</span>`).join('') || '<span class="from">—</span>'}</div></td></tr>`;
  }

  function routingPanel() {
    const all = prompts();
    const always = all.filter((p) => p.always || (!p.shipped && !p.triggers.length && !(p.patterns || []).length))
      .map((p) => `<span class="tchip run">${esc(p.id)}</span>`).join('');
    const ownRows = all.filter((p) => (p.patterns || []).length).map((p) => `<tr><td><b>${esc(p.id)}</b><br><span class="from">its own words</span></td>
      <td><div class="chips">${p.patterns.map((w) => `<span class="tchip mine">${esc(w)}</span>`).join('')}</div></td>
      <td><div class="chips"><span class="tchip run">${esc(p.id)}</span></div></td><td></td></tr>`).join('');
    return `<section class="panel" style="margin-top: 14px">
      <details class="more" data-routing${routingOpen ? ' open' : ''}><summary>Which check runs on which change
        <span class="what">an overview of every signal, and the words each one is found by</span></summary>
      <p class="from">Tick a check's signals on its card below. The words that find a signal are shared by every check and edited
        here${app().newTag('Editing the words a signal is found by.')} — today they are compiled into the server.</p>
      ${app().skew(app().PLANNED, 'Editing the words a signal is found by')}
      <table class="roles routing"><thead><tr><th scope="col">Signal</th><th scope="col">Found by these words${help(HELP.words)}</th>
        <th scope="col">Starts</th><th scope="col">Read first by</th></tr></thead><tbody>
        <tr><td><b>Every change</b></td><td class="from">no words — always</td><td><div class="chips">${always || '<span class="from">—</span>'}</div></td><td></td></tr>
        ${SIGNALS.map((s) => routingRow(s, all)).join('')}${ownRows}</tbody></table></details></section>`;
  }

  let routingOpen = false;

  function wordsCell(id) {
    const o = wordOverrides()[id] || { added: [], removed: [] };
    const words = T().wordsOf(id, wordOverrides()).map((w) => `<span class="tchip${o.added.includes(w) ? ' mine' : ''}">${esc(w)}
      <button type="button" class="icon-btn" data-sl-unword="${esc(w)}" aria-label="Remove ${esc(w)}">✕</button></span>`).join('');
    const shapes = id === 'sql' ? `<div class="from">+ statement shapes: ${esc(T().SQL_SHAPES_TEXT)}</div>` : '';
    const edited = o.added.length || o.removed.length;
    return `<div class="chips">${words}<input type="text" class="word-add" data-sl-addword placeholder="＋ word" aria-label="Add a word to ${id}">
      ${edited ? '<button type="button" class="btn small ghost" data-sl-action="restore-words">Restore</button>' : ''}</div>${shapes}`;
  }

  /* ---------- pairs ---------- */

  /* The first model ticked for the lane and prompt not yet paired, or nothing. */
  function freePair(pool) {
    return pool.flatMap((i) => prompts().map((p) => [i.id, p.id]))
      .find(([i, p]) => !L().security.pairs.some((x) => x.instance === i && x.prompt === p));
  }

  /* Why + Add a pair can add nothing — said as text beside it, never only as a disabled look (securityLaneView.ts). */
  function whyNoPair(s, pool) {
    if (s.pairs.length >= MAX_PAIRS) return 'the most a lane holds';
    if (!pool.length) return 'no model is ticked Security lane on Models';
    return freePair(pool) ? '' : 'every model ticked for the lane is already paired with every prompt';
  }

  function pairsPanel(s) {
    const pool = securityPool();
    const rows = s.pairs.map((x, n) => pairRow(x, n, pool)).join('') || '<tr><td colspan="7"><i>No pair yet.</i></td></tr>';
    const why = whyNoPair(s, pool);
    return `<section class="panel" style="margin-top: 14px"><h3>Model / prompt pairs${help(HELP.pairs)}</h3>
      <table class="roles pairs"><thead><tr><th scope="col">Model</th><th scope="col">Prompt</th><th scope="col">Source${help(HELP.source)}</th>
        <th scope="col">Context tokens${help(HELP.tokens)}</th><th scope="col">Code${help(HELP.stages)}</th><th scope="col">Feature</th>
        <th scope="col"><span class="sr-only">Remove</span></th></tr></thead>
      <tbody>${rows}</tbody></table>
      <p class="row-line"><button type="button" class="btn small" data-sl-action="add-pair"${why ? ' disabled' : ''}>＋ Add a pair</button>
        ${why ? `<span class="why-not">— ${esc(why)}</span>` : ''}
        <span class="from">A pair is unique per model and prompt. Slice sends the code around what the change touches; diff sends the diff.</span></p></section>`;
  }

  /* The pair's Prompt select keeps a prompt that is gone as a disabled, selected option, and ends in "＋ New custom prompt…". */
  function promptOptions(x) {
    const all = prompts();
    const gone = all.some((p) => p.id === x.prompt) ? '' : `<option value="${esc(x.prompt)}" selected disabled>${esc(x.prompt)} — missing</option>`;
    return `${gone}${all.map((p) => `<option value="${esc(p.id)}"${p.id === x.prompt ? ' selected' : ''}>${esc(p.id)}</option>`).join('')}
      <option value="__new__">＋ New custom prompt…</option>`;
  }

  function pairRow(x, n, pool) {
    const inst = app().byId(x.instance);
    const models = pool.map((i) => `<option value="${esc(i.id)}"${i.id === x.instance ? ' selected' : ''}>${esc(i.name)}</option>`).join('');
    const missing = pool.some((i) => i.id === x.instance) ? ''
      : `<option value="${esc(x.instance)}" selected disabled>${esc(inst?.name || x.instance)} — not ticked for the lane</option>`;
    const ctx = ['slice', 'diff'].map((c) => `<option${c === x.context ? ' selected' : ''}>${c}</option>`).join('');
    const only = inst && !inst.features.includes('code') && !inst.features.includes('feature')
      ? '<span class="tag-sec" title="This model is not ticked for ordinary code or feature review on Models.">security only</span>' : '';
    return `<tr data-pair="${n}"><td><div class="pair-model"><select data-sl-pair="instance" aria-label="Model">${missing}${models}</select>${only}</div></td>
      <td><select data-sl-pair="prompt" aria-label="Prompt">${promptOptions(x)}</select></td>
      <td><select data-sl-pair="context" aria-label="Source">${ctx}</select></td>
      <td><input type="number" class="num" min="1024" max="200000" step="1024" value="${esc(x.tokens)}" data-sl-pair="tokens" aria-label="Context tokens"></td>
      <td><input type="checkbox" data-sl-pair="code"${checked(x.code)} aria-label="Code"></td>
      <td><input type="checkbox" data-sl-pair="feature"${checked(x.feature)} aria-label="Feature"></td>
      <td><button type="button" class="icon-btn" data-sl-action="remove-pair" title="Remove this pair">✕</button></td></tr>${pairNotes(x, inst)}`;
  }

  /* Why a pair does not run, under it (securityLaneView.ts runWarnings). Not a [data-pair] row: it holds no control. */
  function pairNotes(x, inst) {
    const notes = [
      !inst && `${x.instance} was removed from Models — this pair does not run`,
      inst && !inst.enabled && `${inst.name} is switched off on Models — this pair does not run`,
      inst && inst.enabled && !inst.features.includes('security') && `${inst.name} is not ticked Security lane on Models — this pair does not run`,
      !prompts().some((p) => p.id === x.prompt) && `Prompt ${x.prompt} is missing — pick a registered prompt`,
    ].filter(Boolean);
    if (!notes.length) return '';
    return `<tr class="pair-note"><td colspan="7">${notes.map((m) => `<span class="state err">${esc(m)}</span>`).join('')}</td></tr>`;
  }

  /* ---------- Chat ---------- */

  const canChat = (i) => window.COAI.availability(i, 'chat').state !== 'off';
  const chatPool = () => app().state.instances.filter((i) => i.features.includes('chat') && canChat(i));

  function chatPage() {
    const c = L().chat;
    return `<div class="lanes-page">
      <p class="lead">Ask another AI about a passage without leaving the editor. Which models can answer is ticked
        "Chat" on Models; here is which one a chat opens on, what it is asked, and how it is sent.</p>
      ${app().modelsUsedHtml('chat', 'Chat models')}
      <div class="plan-grid">${chatModelsPanel(c)}${sendingPanel(c)}${presetsPanel(c)}${shortcutsPanel()}</div></div>`;
  }

  function chatModelsPanel(c) {
    const pool = chatPool();
    const rows = pool.map((i) => chatModelRow(i, c)).join('');
    const empty = rows ? '' : '<p class="from">No model that can answer is ticked "Chat" on Models — chat is off.</p>';
    const perSide = app().state.setup?.perSide
      ? '<p class="from panel-foot">Saved for this side of the machine — chat models used to be shared by every side.</p>' : '';
    return `<section class="panel"><h3>Which model a chat opens on${help(HELP.chatModel)}</h3>
      ${strandedMain(c, pool)}${rows}${empty}${chatRefusals()}${perSide}</section>`;
  }

  function chatModelRow(i, c) {
    return `<div class="block"><label class="inline"><input type="radio" name="chat-main" value="${esc(i.id)}" data-ch="main"${checked(c.main === i.id)}>
        <b>${esc(i.name)}</b> <span class="from">${i.enabled ? '' : '(off)'} opens here when a passage is sent</span></label>
      <label class="field"><span>Opens with${help(HELP.chatStarting)}</span>
        <textarea rows="2" data-ch-start="${esc(i.id)}" placeholder="What the composer opens with when this model is chosen (optional)">${esc(c.starting[i.id] || '')}</textarea></label></div>`;
  }

  /* A saved choice that no longer resolves stays on screen, selected and disabled: what is configured is what is shown,
     and the chat refuses it by name rather than sending the passage to somebody else's model (chatModels.ts chatChoice). */
  function strandedMain(c, pool) {
    if (!c.main || pool.some((i) => i.id === c.main)) return '';
    const i = app().byId(c.main);
    const name = i?.name || c.main;
    const ticked = Boolean(i) && i.features.includes('chat');
    const why = !i ? 'was removed from Models' : ticked ? 'cannot answer a chat' : 'is no longer ticked Chat on Models';
    return `<div class="block stranded"><label class="inline"><input type="radio" name="chat-main" value="${esc(c.main)}" checked disabled>
        <b>${esc(name)}</b> <span class="from">— ${ticked ? 'cannot answer a chat' : 'no longer ticked Chat'}</span></label>
      <p class="callout warn">A chat is set to open on ${esc(name)}, which ${why}. Until you pick another model here a chat is refused
        by that name — it is never quietly sent to a different one.</p></div>`;
  }

  /* Every switched-on model the chat cannot speak to, with the reason — named, never quietly missing (chatModels.ts). */
  function chatRefusals() {
    const refused = app().state.instances.filter((i) => i.enabled && !canChat(i));
    if (!refused.length) return '';
    const ordered = [...refused.filter((i) => i.features.includes('chat')), ...refused.filter((i) => !i.features.includes('chat'))];
    const how = (i) => ({ api: 'an API key', local: 'a local engine' }[app().accessOf(i).kind] || app().accessOf(i).label);
    const line = (i) => `<li>Cannot answer a chat: <b>${esc(i.name)}</b> — ${i.features.includes('chat') ? 'ticked Chat, but ' : ''}it is reached by
      ${esc(how(i))}, and the chat speaks only to ${esc(window.COAI.availability(i, 'chat').reason.replace(/ only$/, ''))}.</li>`;
    return `<ul class="tight chat-refusals">${ordered.map(line).join('')}</ul>`;
  }

  function sendingPanel(c) {
    const main = c.presets.find((p) => p.main);
    const stranded = c.promptChoice && !c.presets.some((p) => p.id === c.promptChoice)
      ? `<option value="${esc(c.promptChoice)}" selected disabled>${esc(c.promptChoice)} — deleted — the main one is being sent</option>` : '';
    const choice = [`<option value="">The main one — ${esc(main?.name || 'none')}</option>`, ...c.presets.map((p) =>
      `<option value="${esc(p.id)}"${p.id === c.promptChoice ? ' selected' : ''}>${esc(p.name)}</option>`), stranded].join('');
    const langs = LANGUAGES.map(([v, l]) => `<option value="${v}"${v === c.language ? ' selected' : ''}>${esc(l)}</option>`).join('');
    const auto = AUTOSEND.map(([v, l, d]) => `<label class="choice"><input type="radio" name="autosend" value="${v}" data-ch="autoSend"${checked(c.autoSend === v)}>
      <span>${esc(l)}</span><span class="d">${esc(d)}</span></label>`).join('');
    return `<section class="panel"><h3>Sending</h3>
      <div class="settings-grid"><label class="field"><span>What to ask about the selection${help(HELP.chatPrompt)}</span><select data-ch="promptChoice">${choice}</select></label>
        <label class="field"><span>Answer in${help(HELP.chatLanguage)}</span><select data-ch="language">${langs}</select></label></div>
      <div class="field" style="margin-top: 10px"><span>Who presses send${help(HELP.chatAutoSend)}</span><div class="choices">${auto}</div></div></section>`;
  }

  function presetsPanel(c) {
    const rows = c.presets.map((p) => `<div class="pcard shipped" data-preset="${esc(p.id)}" style="--pc: var(--border-strong)">
      <div class="row-line"><input type="text" data-ch-preset="name" value="${esc(p.name)}" maxlength="60" placeholder="A name for this prompt" aria-label="Prompt name" style="flex: 1">
        <label class="inline"><input type="radio" name="preset-main" data-ch-preset="main"${checked(p.main)}> main</label>
        <button type="button" class="icon-btn" data-ch-action="remove-preset" aria-label="Remove ${esc(p.name)}"${p.main ? ' disabled title="Make another one main first"' : ''}>✕</button></div>
      <textarea rows="2" data-ch-preset="text" placeholder="What the captured passage travels with" aria-label="Prompt text">${esc(p.text)}</textarea></div>`).join('');
    return `<section class="panel"><h3>Prompt presets${help(HELP.chatPresets)}</h3><p class="from">The one marked main is sent when a capture sends by itself.</p>
      <div class="choices">${rows}</div>
      <p class="row-line panel-foot"><button type="button" class="btn small" data-ch-action="add-preset">＋ Add a prompt</button></p></section>`;
  }

  function shortcutsPanel() {
    const rows = SHORTCUTS.map(([what, key, where]) => `<tr><td>${esc(what)}</td><td><kbd>${esc(key)}</kbd></td><td class="from">${esc(where)}</td></tr>`).join('');
    return `<section class="panel"><h3>Shortcuts</h3><table class="roles"><tbody>${rows}</tbody></table>
      <p class="from panel-foot">Change a key in VS Code's Keyboard Shortcuts — search "CoAI".</p></section>`;
  }

  /* ---------- events ---------- */

  const patchSec = (change) => app().patch('lanes', { security: { ...L().security, ...change } });
  const patchChat = (change) => app().patch('lanes', { chat: { ...L().chat, ...change } });
  const cardId = (t) => t.closest('[data-sp]').dataset.sp;

  function setPromptLists(id, change) {
    const custom = L().security.custom.find((p) => p.id === id);
    if (custom) return patchSec({ custom: L().security.custom.map((p) => (p.id === id ? { ...p, ...change } : p)) });
    const current = prompts().find((p) => p.id === id);
    return patchSec({ overrides: { ...L().security.overrides, [id]: { ...(L().security.overrides[id] || {}),
      triggers: current.triggers, focus: current.focus, patterns: current.patterns || [], ...change } } });
  }

  function onSecurityChange(t) {
    if (t.dataset.sl) return patchSec({ [t.dataset.sl]: t.checked });
    if (t.dataset.slNum) return patchSec({ [t.dataset.slNum]: Math.min(Number(t.max), Math.max(Number(t.min), Number(t.value) || 0)) });
    if (t.dataset.slTick) return togglePair(t.dataset.slTick, cardId(t), t.checked);
    if (t.dataset.slCond) {
      const id = cardId(t);
      const k = t.dataset.slCond;
      const list = prompts().find((p) => p.id === id)[k];
      openConditions.add(id);
      return setPromptLists(id, { [k]: t.checked ? [...list, t.value] : list.filter((x) => x !== t.value) });
    }
    return onPairChange(t);
  }

  function onPairChange(t) {
    const n = Number(t.closest('[data-pair]').dataset.pair);
    const k = t.dataset.slPair;
    const pair = L().security.pairs[n];
    if (k === 'prompt' && t.value === '__new__') { app().render(); return newPrompt({ n, instance: pair.instance, prompt: pair.prompt }); }
    const v = t.type === 'checkbox' ? t.checked : k === 'tokens' ? Number(t.value) : t.value;
    return patchSec({ pairs: L().security.pairs.map((x, i) => (i === n ? { ...x, [k]: v } : x)) });
  }

  function togglePair(instance, prompt, on) {
    const pairs = L().security.pairs;
    if (!on) return patchSec({ pairs: pairs.filter((x) => !(x.instance === instance && x.prompt === prompt)) });
    if (pairs.length >= MAX_PAIRS) { app().toast(`${MAX_PAIRS} pairs is the most a lane may hold.`); return app().render(); }
    const local = app().byId(instance)?.vendor === 'local';
    return patchSec({ pairs: [...pairs, { instance, prompt, context: local ? 'slice' : 'diff', tokens: local ? 24000 : 200000, code: true, feature: true }] });
  }

  function onChange(e) {
    const t = e.target;
    const d = t.dataset;
    if (d.slTrig) {
      const id = cardId(t);
      const list = prompts().find((p) => p.id === id).triggers;
      setPromptLists(id, { triggers: t.checked ? [...list, d.slTrig] : list.filter((x) => x !== d.slTrig) });
      return true;
    }
    if (t.hasAttribute('data-sl-addpattern')) { addPattern(cardId(t), t.value); return true; }
    if (t.hasAttribute('data-sl-addword')) { addWord(t.closest('[data-signal]').dataset.signal, t.value); return true; }
    if (t.hasAttribute('data-sl-text')) {
      const id = cardId(t);
      openTexts.add(id);
      patchSec({ texts: { ...(L().security.texts || {}), [id]: t.value.trim() ? t.value : '' } });
      return true;
    }
    if (d.sl || d.slNum || d.slTick || d.slCond || d.slPair) { onSecurityChange(t); return true; }
    return onChatChange(t);
  }

  function onChatChange(t) {
    if (t.dataset.ch) { patchChat({ [t.dataset.ch]: t.value }); return true; }
    if (t.dataset.chStart) { patchChat({ starting: { ...L().chat.starting, [t.dataset.chStart]: t.value } }); return true; }
    if (t.dataset.chPreset) { presetChange(t); return true; }
    return false;
  }

  function presetChange(t) {
    const id = t.closest('[data-preset]').dataset.preset;
    const k = t.dataset.chPreset;
    const presets = L().chat.presets.map((p) => (k === 'main' ? { ...p, main: p.id === id } : p.id === id ? { ...p, [k]: t.value } : p));
    patchChat({ presets });
  }

  /* ---------- the flows: new prompt, restore, remove (securityFlows.ts) ---------- */

  /* Why a name cannot become a new custom prompt, or empty when it can (securityLaneState.ts newPromptProblem). */
  function promptNameProblem(name) {
    const checks = [
      [/^redteam-/.test(name), 'a prompt name starts with redteam-'],
      [/^redteam-[a-z0-9-]+$/.test(name), 'after redteam-, use lower-case letters, digits and hyphens only'],
      [name.length <= 80, 'a prompt name is at most 80 characters'],
      [!prompts().some((p) => p.id === name), `${name} is already a prompt`],
      [prompts().length < MAX_PROMPTS, `the lane holds at most ${MAX_PROMPTS} prompts, ${PRESETS.length} of them shipped`],
    ];
    return checks.find(([ok]) => !ok)?.[1] || '';
  }

  function askPromptName() {
    const name = (window.prompt('Its name — redteam- and lower-case letters, digits and hyphens:', 'redteam-') || '').trim();
    if (!name) return '';
    const problem = promptNameProblem(name);
    if (problem) app().toast(`Not added: ${problem}.`);
    return problem ? '' : name;
  }

  /* + New custom prompt: a name, then the prompt, then — when a pair's select asked for it — THAT pair re-pointed to it.
     The pair is named by identity (row, model, prompt); one that moved meanwhile is left alone and the person told so. */
  function newPrompt(identity) {
    const name = askPromptName();
    if (!name) return;
    const custom = [...L().security.custom, { id: name, triggers: [], focus: ['entry-point'] }];
    const pairs = L().security.pairs;
    const held = identity && pairs[identity.n]?.instance === identity.instance && pairs[identity.n]?.prompt === identity.prompt;
    openTexts.add(name);
    if (held) {
      patchSec({ custom, pairs: pairs.map((x, i) => (i === identity.n ? { ...x, prompt: name } : x)) });
      app().toast(`${name} added, and pair ${identity.n + 1} now uses it — write its question in Prompt text on its card.`);
    } else {
      patchSec({ custom });
      app().toast(identity ? `Prompt ${name} created; the pair changed while you typed — pair it from its card.`
        : `${name} added — write its question in Prompt text on its card.`);
    }
    document.querySelector(`[data-sp="${CSS.escape(name)}"] textarea[data-sl-text]`)?.focus();
  }

  function restoreNow(id) {
    const { [id]: goneConditions, ...overrides } = L().security.overrides;
    const { [id]: goneText, ...texts } = L().security.texts || {};
    patchSec({ overrides, texts });
  }

  /* Restore default asks first whenever the file holds anything — it is deleted; a conditions-only restore acts at once. */
  function restore(t) {
    const id = cardId(t);
    if (!HOLDS_CONTENT.has(textState(textOf(id)))) return restoreNow(id);
    return app().confirm({
      title: `Restore ${id}?`,
      body: `<p>Restore <b>${esc(id)}</b> to its shipped text and conditions? <code>${esc(fileOf(id))}</code> — your text in it — is deleted.</p>`,
      action: 'Restore default', danger: true, onConfirm: () => restoreNow(id),
    });
  }

  /* Remove custom prompt: say which pairs go with it, and that its text stays on disk. */
  function removePrompt(t) {
    const id = cardId(t);
    const going = L().security.pairs.filter((x) => x.prompt === id).map((x) => app().byId(x.instance)?.name || x.instance);
    const pairs = going.length ? ` and its ${going.length === 1 ? 'pair' : `${going.length} pairs`} with <b>${esc(going.join(', '))}</b>` : '';
    app().confirm({
      title: `Remove ${id}?`,
      body: `<p>Remove <b>${esc(id)}</b>${pairs}?</p><p class="from">Its text stays on disk in <code>${esc(fileOf(id))}</code>.</p>`,
      action: 'Remove custom prompt', danger: true,
      onConfirm: () => {
        patchSec({ custom: L().security.custom.filter((p) => p.id !== id), pairs: L().security.pairs.filter((x) => x.prompt !== id) });
        app().toast(`Removed. ${fileOf(id)} stays on disk.`);
      },
    });
  }

  /* General's legacy triggers: dropped, so an older server too runs it on every code change. Its text is untouched. */
  function clearConditions(t) {
    const id = cardId(t);
    const { [id]: gone, ...overrides } = L().security.overrides;
    patchSec({ overrides });
    app().toast(`${id}'s stored conditions are cleared — it runs on every code change.`);
  }

  const ACTIONS = {
    'new-prompt': () => newPrompt(null),
    'restore-words': (t) => {
      const { [t.closest('[data-signal]').dataset.signal]: gone, ...rest } = wordOverrides();
      patchSec({ words: rest });
    },
    restore,
    'remove-prompt': removePrompt,
    'clear-conditions': clearConditions,
    'add-pair': () => {
      const free = freePair(securityPool());
      if (free) togglePair(free[0], free[1], true); else app().toast('Every model and prompt is already paired.');
    },
    'remove-pair': (t) => patchSec({ pairs: L().security.pairs.filter((_, i) => i !== Number(t.closest('[data-pair]').dataset.pair)) }),
    'add-preset': () => patchChat({ presets: [...L().chat.presets, { id: `preset-${Date.now()}`, name: 'New prompt', text: 'Explain', main: false }] }),
    'remove-preset': (t) => patchChat({ presets: L().chat.presets.filter((p) => p.id !== t.closest('[data-preset]').dataset.preset) }),
  };

  /* A word the person added is theirs; removing a shipped word is recorded so Restore can put it back. */
  function patchWords(signal, change) {
    const o = wordOverrides()[signal] || { added: [], removed: [] };
    patchSec({ words: { ...wordOverrides(), [signal]: { ...o, ...change(o) } } });
  }

  function addWord(signal, raw) {
    const word = raw.trim().toLowerCase();
    if (!word || T().wordsOf(signal, wordOverrides()).includes(word)) return;
    patchWords(signal, (o) => (o.removed.includes(word) ? { removed: o.removed.filter((w) => w !== word) } : { added: [...o.added, word] }));
  }

  function removeWord(signal, word) {
    patchWords(signal, (o) => (o.added.includes(word) ? { added: o.added.filter((w) => w !== word) } : { removed: [...o.removed, word] }));
  }

  /* A check's own words: kept as typed (a phrase or code keeps its spaces); /…/ marks a regular expression. */
  function addPattern(id, raw) {
    const word = raw.trim();
    const list = prompts().find((p) => p.id === id).patterns || [];
    if (!word || list.includes(word)) return;
    if (!T().validPattern(word)) { app().toast(`${word} is not a valid regular expression.`); return; }
    setPromptLists(id, { patterns: [...list, word] });
  }

  function onClick(e) {
    const unpattern = e.target.closest('[data-sl-unpattern]');
    if (unpattern) {
      const id = cardId(unpattern);
      setPromptLists(id, { patterns: (prompts().find((p) => p.id === id).patterns || []).filter((w) => w !== unpattern.dataset.slUnpattern) });
      return true;
    }
    const unword = e.target.closest('[data-sl-unword]');
    if (unword) { removeWord(unword.closest('[data-signal]').dataset.signal, unword.dataset.slUnword); return true; }
    const t = e.target.closest('[data-sl-action], [data-ch-action]');
    if (!t) return false;
    ACTIONS[t.dataset.slAction || t.dataset.chAction](t);
    return true;
  }

  function onToggle(e) {
    const cond = e.target.dataset?.cond;
    if (cond) e.target.open ? openConditions.add(cond) : openConditions.delete(cond);
    const text = e.target.dataset?.textOpen;
    if (text) e.target.open ? openTexts.add(text) : openTexts.delete(text);
    if (e.target.hasAttribute?.('data-routing')) routingOpen = e.target.open;
  }

  /* Typing keeps focus: the words are kept as they are typed and the card repaints on change (blur). */
  function onInput(e) {
    const t = e.target;
    if (t.hasAttribute('data-sl-try')) {
      tryText = t.value;
      document.querySelector('[data-try-result]').innerHTML = tryResult(tryText);
      return true;
    }
    if (!t.hasAttribute('data-sl-text')) return false;
    const id = cardId(t);
    app().setState({ ...app().state, lanes: { ...L(), security: { ...L().security, texts: { ...(L().security.texts || {}), [id]: t.value } } } });
    t.closest('details').querySelector('[data-count]').textContent = `${t.value.length} characters`;
    return true;
  }

  document.getElementById('pane').addEventListener('toggle', onToggle, true);

  window.COAI_PAGES = {
    ...window.COAI_PAGES,
    security: { slice: 'lanes', defaults, html: securityPage, onChange, onClick, onInput },
    chat: { html: chatPage, onChange, onClick },
  };
})();
