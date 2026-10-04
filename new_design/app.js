/* ConnectOtherAIs settings mockup: state, rendering and interaction. Plain script, no build. */
(function () {
  'use strict';

  const { FEATURE_GROUPS, FEATURES, ACCESS, VENDORS, CALLERS, VAULT_KEYS, NEW_FOR_DAYS,
    DEMO, DEMO_CONSULTANTS, DEMO_TEAM_SERVERS, DEMO_SETUP, availability } = window.COAI;

  const STORE_KEY = 'coai-settings-mockup-v1';
  const OTHER_MODEL = '__other__';
  const IN_MODEL_NAME = '@model';
  const EFFORT_ORDER = ['default', 'none', 'minimal', 'low', 'medium', 'high', 'xhigh', 'max', IN_MODEL_NAME];
  const EFFORT_LABEL = { default: 'Default', [IN_MODEL_NAME]: 'In the model name' };

  const TABS = [
    { id: 'models', label: 'Models' },
    { id: 'reviews', label: 'Reviews', subs: [
      { id: 'stages', label: 'Stages' }, { id: 'roles', label: 'Roles & prompts' }, { id: 'prompts', label: 'Prompts per round' },
      { id: 'gate', label: 'The gate' }, { id: 'commands', label: 'Commands' }, { id: 'limits', label: 'Limits' }] },
    { id: 'consultants', label: 'Consultants', subs: [
      { id: 'consultant', label: 'Consultant' }, { id: 'qconsult', label: 'Question consultant' }] },
    { id: 'security', label: 'Security lane' },
    { id: 'chat', label: 'Chat' },
    { id: 'setup', label: 'Setup', subs: [
      { id: 'keys', label: 'Vendor keys' }, { id: 'team', label: 'Team servers' },
      { id: 'mcp', label: 'MCP server' }, { id: 'side', label: 'This side' }] },
    { id: 'notes', label: 'Design notes', notes: true },
  ];

  /* The first part of a new instance's id, so history keeps its key: claude, claude-2, … */
  const ID_BASE = { dashscope: 'qwen', xai: 'grok', compat: 'api' };

  const ADD_GROUPS = [
    { title: 'A CLI on this machine', note: 'Can read the checkout. The CLI must be installed and signed in.',
      vendors: ['claude', 'codex', 'antigravity', 'deepseek', 'openrouter'] },
    { title: 'An API key', note: 'Prompt only. The key is read from your CredsForDevs vault by its name.',
      vendors: ['dashscope', 'xai', 'compat'] },
    { title: 'This machine\'s GPU', note: 'Prompt only. Ollama or vLLM on this machine.', vendors: ['local'] },
    { title: 'A Team server', note: 'Prompt only. The company\'s accounts, behind its sign-in.', vendors: ['remote'] },
  ];

  /* ---------- state ---------- */

  /* Every page module registers itself in window.COAI_PAGES[tab] and may own one slice of the
     state, with its own defaults. Models, the callers map and the Team servers stay here because
     several pages read them. Declared before the state is loaded, which reads it. */
  const PAGES = window.COAI_PAGES || {};

  const ui = {
    tab: 'models', sub: {}, q: '', feature: '', effort: '', vendor: '', access: '', showDisabled: true,
    open: new Set(), zoom: 0, tone: 0, theme: 'auto', daysSinceUpdate: 0,
  };
  let state = load();

  function pageSlices() {
    return Object.fromEntries(Object.values(PAGES).filter((p) => p.slice).map((p) => [p.slice, p.defaults()]));
  }

  function fresh() {
    return {
      instances: DEMO.map(normalise),
      consultants: { ...DEMO_CONSULTANTS },
      teamServers: structuredClone(DEMO_TEAM_SERVERS),
      setup: structuredClone(DEMO_SETUP),
      ...pageSlices(),
    };
  }

  function normalise(raw) {
    const v = VENDORS[raw.vendor];
    return {
      enabled: true, effort: '', thinking: false, cliPath: '', timeout: 10, priceIn: '', priceOut: '',
      endpoint: v.endpoint || '', key: v.key || '', dialect: v.dialect || '',
      systemPrompt: { mode: 'default', text: '' }, usage: { runs: 0, failed: 0, cost: '—' }, health: 'warn',
      features: [],
      ...structuredClone(raw),
    };
  }

  function load() {
    try {
      const saved = JSON.parse(localStorage.getItem(STORE_KEY) || 'null');
      if (saved && Array.isArray(saved.instances)) {
        Object.assign(ui, saved.ui || {}, { open: new Set() });
        const base = fresh();
        // A saved slice is laid over today's defaults, so a field a newer mockup added is never undefined.
        const kept = Object.fromEntries(Object.keys(base).filter((k) => k !== 'instances' && saved[k])
          .map((k) => [k, Array.isArray(base[k]) ? saved[k] : { ...base[k], ...saved[k] }]));
        return { ...base, ...kept, instances: saved.instances.map(normalise) };
      }
    } catch { /* a private window or blocked storage: the page still works, it just forgets */ }
    return fresh();
  }

  function save() {
    try {
      const { open, ...kept } = ui;
      localStorage.setItem(STORE_KEY, JSON.stringify({ ...state, ui: kept }));
    } catch { /* see load() */ }
  }

  /* ---------- small helpers ---------- */

  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', '\'': '&#39;' }[c]));
  const $ = (sel, root = document) => root.querySelector(sel);
  const byId = (id) => state.instances.find((i) => i.id === id);
  const vendorOf = (i) => VENDORS[i.vendor];
  const accessOf = (i) => ACCESS[vendorOf(i).access];
  const featureLabel = (id) => FEATURES.find((f) => f.id === id)?.label ?? id;
  const serverOf = (i) => state.teamServers.find((s) => s.id === i.server);

  function modelsOf(i) {
    if (i.vendor !== 'remote') return vendorOf(i).models;
    return serverOf(i)?.vendors.find((x) => x.id === i.serverVendor)?.models || [];
  }

  function effortLevels(i) {
    const v = vendorOf(i);
    if (v.effortByModel) return v.effortByModel[i.model] || [];
    return v.effort || null;
  }

  /* The filter's key for an instance's effort: a level, "default", or "in the model name". */
  function effortKey(i) {
    if (effortLevels(i) === null) return IN_MODEL_NAME;
    return i.effort || 'default';
  }

  /* ---------- the installed coai-mcp, and what it ignores ---------- */

  /* The release that ships this design. A control this design adds is ignored by any coai-mcp before it. */
  const PLANNED = '0.44.0';
  const MCP_CHOICES = [[PLANNED, `${PLANNED} — the release with this design`], ['0.43.0', '0.43.0 — today'], ['0.36.0', '0.36.0 — an old one'], ['', 'not installed']];
  const mcpVersion = () => ui.mcp ?? PLANNED;

  function older(version, than) {
    const a = version.split('.').map(Number);
    const b = than.split('.').map(Number);
    for (let i = 0; i < 3; i += 1) if ((a[i] || 0) !== (b[i] || 0)) return (a[i] || 0) < (b[i] || 0);
    return false;
  }

  /* A note where a control does nothing on this side because coai-mcp is too old (or absent) to read it.
     Empty when the installed server knows it. Every *_SINCE gate on the page goes through this one road. */
  function skew(since, what) {
    const v = mcpVersion();
    if (!v) return `<p class="skew">coai-mcp is not installed on this side, so ${esc(what)} does nothing yet — install it under Setup › MCP server.</p>`;
    if (!older(v, since)) return '';
    const planned = since === PLANNED ? ' (the release that ships this design)' : '';
    return `<p class="skew">${esc(what)} needs coai-mcp ${since}${planned}; this side runs ${esc(v)}, which ignores it — update under Setup › MCP server.</p>`;
  }

  /* The "?" every control carries: its help text on hover and focus, and read out by a screen reader. */
  function help(text) {
    return ` <span class="help" tabindex="0" role="img" aria-label="${esc(text)}" title="${esc(text)}">?</span>`;
  }

  /* "new" is shown for the first NEW_FOR_DAYS days after the person first ran the version that
     brought the control; the top bar lets the mockup pretend any number of days have passed. */
  function newTag(what) {
    const left = NEW_FOR_DAYS - ui.daysSinceUpdate;
    if (left <= 0) return '';
    const when = left === 1 ? 'tomorrow' : `in ${left} days`;
    return ` <span class="tag-new" title="${esc(what)} New in this version — this tag goes away ${when}.">new</span>`;
  }

  function nextId(base) {
    const taken = new Set(state.instances.map((i) => i.id));
    if (!taken.has(base)) return base;
    let n = 2;
    while (taken.has(`${base}-${n}`)) n += 1;
    return `${base}-${n}`;
  }

  function baseIdOf(i) {
    if (i.vendor === 'remote') return `${i.server}-${i.serverVendor}`;
    return ID_BASE[i.vendor] || i.vendor;
  }

  function callersUsing(id) {
    return CALLERS.filter((c) => state.consultants[c.id] === id);
  }

  function update(id, change) {
    state = { ...state, instances: state.instances.map((i) => (i.id === id ? { ...i, ...change } : i)) };
    save();
  }

  let toastTimer = 0;
  function toast(text) {
    const el = $('#toast');
    el.textContent = text;
    el.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { el.hidden = true; }, 3200);
  }

  /* ---------- tabs ---------- */

  function renderTabs() {
    const host = $('#tabs');
    host.innerHTML = TABS.map((t) => {
      const on = t.id === ui.tab;
      const count = t.id === 'models' ? `<span class="count">${state.instances.length}</span>` : '';
      return `<button type="button" role="tab" class="tab${t.notes ? ' notes' : ''}" id="tab-${t.id}"
        data-tab="${t.id}" aria-selected="${on}" tabindex="${on ? 0 : -1}">${esc(t.label)}${count}</button>`;
    }).join('');
    const tab = TABS.find((t) => t.id === ui.tab);
    const subHost = $('#subtabs-host');
    if (!tab.subs) { subHost.innerHTML = ''; return; }
    const current = currentSub(tab);
    subHost.innerHTML = `<div class="subtabs" role="tablist" aria-label="${esc(tab.label)}">${tab.subs.map((s) =>
      `<button type="button" role="tab" class="subtab" data-sub="${s.id}" aria-selected="${s.id === current}"
        tabindex="${s.id === current ? 0 : -1}">${esc(s.label)}</button>`).join('')}</div>`;
  }

  function currentSub(tab) {
    return ui.sub[tab.id] || tab.subs?.[0].id;
  }

  function render() {
    renderTabs();
    const pane = $('#pane');
    const tab = TABS.find((t) => t.id === ui.tab);
    pane.setAttribute('aria-labelledby', `tab-${ui.tab}`);
    if (ui.tab === 'models') pane.innerHTML = modelsHtml();
    else if (ui.tab === 'notes') pane.replaceChildren($('#notes-template').content.cloneNode(true));
    else pane.innerHTML = PAGES[ui.tab].html(currentSub(tab));
    if (location.hash.slice(1) !== ui.tab) location.hash = ui.tab;
  }

  /* ---------- Models ---------- */

  function visible(i) {
    if (!ui.showDisabled && !i.enabled) return false;
    if (ui.feature && !i.features.includes(ui.feature)) return false;
    if (ui.effort && effortKey(i) !== ui.effort) return false;
    if (ui.vendor && i.vendor !== ui.vendor) return false;
    if (ui.access && accessOf(i).kind !== ui.access) return false;
    if (!ui.q) return true;
    const v = vendorOf(i);
    const hay = `${i.name} ${i.model} ${i.id} ${v.label} ${v.maker}`.toLowerCase();
    return hay.includes(ui.q.toLowerCase());
  }

  const filtering = () => Boolean(ui.feature || ui.effort || ui.vendor || ui.access || ui.q);

  function modelsHtml() {
    return `
      <p class="lead">Every model ConnectOtherAIs can use, and what each one is used for. Add the same model as
        many times as you need — with another effort, system prompt or key it is another instance. Every other
        tab picks from this list.</p>
      ${toolbarHtml()}
      ${filtersHtml()}
      ${cardsHtml()}`;
  }

  function cardsHtml() {
    const shown = state.instances.filter(visible);
    if (shown.length) return `<div class="cards">${shown.map(cardHtml).join('')}</div>`;
    return `<div class="empty-state">No model matches these filters.
      <button type="button" class="btn small ghost" data-action="clear-filters">Clear the filters</button></div>`;
  }

  function toolbarHtml() {
    const kinds = [['', 'Any access'], ['cli', 'CLI'], ['api', 'API key'], ['local', 'Local engine'], ['remote', 'Team server']];
    return `<div class="toolbar">
      <button type="button" class="btn primary" data-action="add">＋ Add a model</button>
      <input type="search" id="q" placeholder="Filter by name, model or vendor" value="${esc(ui.q)}" aria-label="Filter models">
      <label>Access <select id="access-filter">${kinds.map(([v, l]) =>
        `<option value="${v}"${v === ui.access ? ' selected' : ''}>${l}</option>`).join('')}</select></label>
      <label><input type="checkbox" id="show-disabled"${ui.showDisabled ? ' checked' : ''}> Show switched-off models</label>
      ${filtering() ? '<button type="button" class="btn small ghost" data-action="clear-filters">Clear the filters</button>' : ''}
      <span class="spacer"></span>
      <span class="from">${state.instances.filter((i) => i.enabled).length} on · ${state.instances.length} in all</span>
    </div>`;
  }

  /* Three rows of chips, each its own question: what it is used for, at which effort, from which vendor.
     They combine — a chip narrows what the other rows already show. */
  function filtersHtml() {
    return `<div class="filters">
      ${filterRowHtml('Used for', 'feature', featureChips())}
      ${filterRowHtml('Effort', 'effort', effortChips())}
      ${filterRowHtml('Vendor', 'vendor', vendorChips())}
    </div>`;
  }

  function filterRowHtml(label, kind, chips) {
    const all = `<button type="button" class="chip" data-filter-kind="${kind}" data-value="" aria-pressed="${!ui[kind]}">All</button>`;
    return `<div class="filter-row" role="group" aria-label="${esc(label)}">
      <span class="filter-label">${esc(label)}</span><div class="chips">${all}${chips.join('')}</div></div>`;
  }

  function chipHtml(kind, value, label, n, extra = {}) {
    const on = ui[kind] === value;
    const dot = extra.colour ? `<span class="dot" style="background: var(--vc-${extra.colour})"></span>` : '';
    return `<button type="button" class="chip${n ? '' : ' empty'}" data-filter-kind="${kind}" data-value="${esc(value)}"
      aria-pressed="${on}" title="${esc(extra.title || '')}">${dot}${esc(label)} <span class="n">${n || 'none'}</span></button>`;
  }

  function featureChips() {
    return FEATURES.map((f) => {
      const n = state.instances.filter((i) => i.enabled && i.features.includes(f.id)).length;
      const title = n ? `Show the ${n} model(s) used for ${f.label}` : `Nothing is ticked for ${f.label} — it is off`;
      return chipHtml('feature', f.id, f.label, n, { title });
    });
  }

  function effortChips() {
    const counts = new Map();
    state.instances.forEach((i) => counts.set(effortKey(i), (counts.get(effortKey(i)) || 0) + 1));
    return EFFORT_ORDER.filter((k) => counts.has(k)).map((k) => chipHtml('effort', k, EFFORT_LABEL[k] || k, counts.get(k), {
      title: k === 'default' ? 'Left to the vendor\'s own default' : k === IN_MODEL_NAME ? 'Antigravity: the effort is part of the model name' : `Effort ${k}`,
    }));
  }

  function vendorChips() {
    const counts = new Map();
    state.instances.forEach((i) => counts.set(i.vendor, (counts.get(i.vendor) || 0) + 1));
    return Object.keys(VENDORS).filter((k) => counts.has(k)).map((k) => {
      const v = VENDORS[k];
      return chipHtml('vendor', k, `${v.maker} · ${v.label}`, counts.get(k), { colour: v.colour });
    });
  }

  function cardHtml(i) {
    const v = vendorOf(i);
    return `<article class="card${i.enabled ? '' : ' disabled'}" id="card-${esc(i.id)}" data-id="${esc(i.id)}"
        style="--vc: var(--vc-${v.colour})" aria-label="${esc(i.name)}">
      <div class="card-top">
        ${cardHeadHtml(i, v)}
        ${badgesHtml(i, v)}
        ${v.accessNote ? `<p class="access-note">${esc(v.accessNote)}</p>` : ''}
        ${M().ignoredNote(i)}
      </div>
      ${featuresHtml(i)}
      ${modelBlockHtml(i, v)}
      <div class="card-foot">
        ${connectionHtml(i, v)}
        ${limitsHtml(i)}
        ${usedByHtml(i)}
      </div>
    </article>`;
  }

  const M = () => window.COAI_MODELS;
  const stageLabel = (f) => FEATURES.find((x) => x.id === f).label;
  const lastWhy = (stages) => `The only model switched on for ${stages.map(stageLabel).join(' and ')} — switch another one on first, or that review would have nobody.`;

  function cardHeadHtml(i, v) {
    const last = M().lastFor(i, state);
    const lock = last.length ? ` disabled title="${esc(lastWhy(last))}"` : ' title="On / off"';
    return `<div class="card-head">
      <input type="checkbox" class="switch" data-field="enabled"${i.enabled ? ' checked' : ''}
        aria-label="${i.enabled ? 'Switch off' : 'Switch on'} ${esc(i.name)}"${lock}>
      <div>
        <input class="name" data-field="name" value="${esc(i.name)}" aria-label="Display name" spellcheck="false">
        <div class="sub"><span class="maker">${esc(v.maker)}</span><span>${esc(v.label)}</span>
          <span>id <code>${esc(i.id)}</code></span></div>
      </div>
      <div class="actions">
        <button type="button" class="icon-btn" data-action="test" title="Ask it one short question to see it answers — one real, paid turn">✓ Check</button>
        <button type="button" class="icon-btn" data-action="duplicate" title="Add another instance of this model">⧉ Duplicate</button>
        <button type="button" class="icon-btn" data-action="remove" title="Remove this instance">✕</button>
      </div>
    </div>`;
  }

  function badgesHtml(i, v) {
    const a = ACCESS[v.access];
    const unconfined = v.access === 'cli-unconfined' ? ' unconfined' : '';
    const health = { ok: 'checked: it answered', warn: 'not checked yet', err: 'checked: no answer', busy: 'checking…' }[i.health];
    const usage = i.usage.runs ? `${i.usage.runs} runs · ${i.usage.failed} failed · ${esc(i.usage.cost)}` : 'not used yet';
    return `<div class="badges">
      <span class="badge ${a.kind}${unconfined}" title="${esc(a.long)}"><b>${a.label}</b> ${esc(a.detail)}</span>
      ${M().verdictBadge(i)}
      ${M().cliBadge(i)}
      <span class="badge health ${i.health === 'busy' ? 'warn' : i.health}" title="The last ✓ Check — a test call, not coai-mcp's verdict">${health}</span>
      <span class="badge usage">${usage}</span>
    </div>`;
  }

  function featuresHtml(i) {
    const notes = [];
    const last = state.instances.includes(i) ? M().lastFor(i, state) : [];
    const rows = FEATURE_GROUPS.map((g) => {
      const boxes = FEATURES.filter((f) => f.group === g.id).map((f) => featureBox(i, f, notes, last)).join('');
      return `<div class="feature-row"><span class="group">${esc(g.label)}</span><div class="boxes">${boxes}</div></div>`;
    }).join('');
    const lastNote = last.length ? `<p class="from"><span class="state warn">${esc(lastWhy(last))}</span></p>` : '';
    return `<div class="block"><h4 class="block-title">Use for${help('What this model is used for. Every other tab picks from the models ticked here.')}</h4>
      <div class="features">${rows}</div>${featureNotesHtml(notes)}${lastNote}</div>`;
  }

  function featureBox(i, f, notes, last) {
    const a = availability(i, f.id);
    if (a.state !== 'ok') notes.push({ state: a.state, reason: a.reason, label: f.label });
    const checked = i.features.includes(f.id) && a.state !== 'off';
    const locked = last.includes(f.id);
    const title = locked ? lastWhy([f.id]) : a.reason;
    return `<label class="feat ${a.state === 'ok' ? '' : a.state}"${title ? ` title="${esc(title)}"` : ''}>
      <input type="checkbox" data-field="feature" data-feature="${f.id}"${checked ? ' checked' : ''}
        ${a.state === 'off' || locked ? 'disabled' : ''}> ${esc(f.label)}</label>`;
  }

  /* Features sharing one reason become one line, so a card says each thing once. */
  function featureNotesHtml(notes) {
    if (!notes.length) return '';
    const merged = new Map();
    notes.forEach((n) => {
      const key = `${n.state}|${n.reason}`;
      merged.set(key, [...(merged.get(key) || []), n.label]);
    });
    const items = [...merged].map(([key, labels]) => {
      const [state, reason] = key.split('|');
      const lead = state === 'off' ? 'Not offered' : 'Limited';
      return `<li class="${state}">${lead}: ${esc(labels.join(', '))} — ${esc(reason)}</li>`;
    }).join('');
    return `<ul class="notes-list">${items}</ul>`;
  }

  function modelBlockHtml(i, v) {
    return `<div class="block"><h4 class="block-title">How it answers</h4><div class="settings-grid">
      ${modelFieldHtml(i, v)}
      ${effortFieldHtml(i, v)}
      ${M().thinkingField(i)}
      ${systemPromptHtml(i)}
      <div class="wide">${M().apiRuns(i)}</div>
    </div></div>`;
  }

  function modelFieldHtml(i, v) {
    const label = `<span>Model${help('The model this instance asks. "Another model…" takes any id the vendor accepts.')}</span>`;
    const list = i.fetchedModels || (v.freeModel ? null : modelsOf(i));
    if (!list) {
      return `<label class="field">${label}
        <input type="text" data-field="model" value="${esc(i.model)}" placeholder="the endpoint's model id">${M().provenance(i)}</label>`;
    }
    const known = list.includes(i.model);
    const opts = [...(known || !i.model ? [] : [i.model]), ...list].map((m) =>
      `<option value="${esc(m)}"${m === i.model ? ' selected' : ''}>${esc(m)}${known || m !== i.model ? '' : ' (typed)'}</option>`).join('');
    const from = i.fetchedModels ? '<span class="hint">from the endpoint, just now</span>' : M().provenance(i);
    return `<label class="field">${label}
      <select data-field="model">${opts}<option value="${OTHER_MODEL}">Another model…</option></select>${from}</label>`;
  }

  function effortFieldHtml(i, v) {
    const levels = effortLevels(i);
    if (levels === null) {
      // A disabled select rather than a sentence: the same height as its neighbours, so rows line up.
      return `<label class="field" title="${esc(v.effortNote)}"><span>Effort</span>
        <select disabled><option>Part of the model name</option></select></label>`;
    }
    const calibrated = M().calibrated(i);
    const none = calibrated ? `Default — ${calibrated} (calibrated)` : 'Default — the vendor decides';
    const opts = ['', ...levels].map((l) =>
      `<option value="${l}"${l === i.effort ? ' selected' : ''}>${l ? esc(l) : esc(none)}</option>`).join('');
    const tag = v.effortNew ? newTag('Effort for this vendor.') : '';
    const hint = v.effortHint ? `<span class="hint">${esc(v.effortHint)}</span>` : '';
    const why = help('How hard the model thinks. Higher is not always better: measured on agy, Flash Low beat High on plans.');
    return `<label class="field"><span>Effort${tag}${why}</span><select data-field="effort">${opts}</select>${hint}</label>`;
  }

  function systemPromptHtml(i) {
    const custom = i.systemPrompt.mode === 'custom';
    const tag = newTag('A system prompt per model.');
    const select = `<select data-field="systemPromptMode" aria-label="System prompt">
        <option value="default"${custom ? '' : ' selected'}>Default (empty)</option>
        <option value="custom"${custom ? ' selected' : ''}>My own for this instance</option></select>`;
    if (!custom) {
      return `<label class="field wide"><span>System prompt${tag}</span>
        <span class="inline">${select}<span class="hint">empty — the feature's own prompt is sent unchanged</span></span></label>`;
    }
    return `<div class="field wide"><span>System prompt${tag}</span>
      <span class="inline">${select}<button type="button" class="btn small ghost" data-action="prompt-default">Back to default</button></span>
      <textarea rows="5" data-field="systemPromptText" placeholder="Written before every request this instance sends. Leave empty to send none."
        aria-label="System prompt for ${esc(i.name)}">${esc(i.systemPrompt.text)}</textarea>
      <div class="count-line"><span>Sent before the feature's own prompt.</span><span data-count>${i.systemPrompt.text.length} characters</span></div></div>`;
  }

  function connectionHtml(i, v) {
    const parts = v.connection.map((c) => CONNECTION[c](i, v));
    const summary = v.connection.map((c) => SUMMARY[c](i, v)).filter(Boolean).join(' · ');
    return detailsHtml(i, 'conn', 'Connection', summary, parts.join('') + M().localSafety(i));
  }

  const CONNECTION = {
    cli: (i) => `<label class="field wide"><span>Where its CLI is${help('Leave empty to look it up on PATH. Kept for this machine only — never exported.')}</span>
      <input type="text" data-field="cliPath" value="${esc(i.cliPath)}" placeholder="leave empty to look it up on PATH"></label>${M().cliActions(i)}`,
    endpoint: (i) => `<label class="field wide"><span>Endpoint</span>
      <input type="text" data-field="endpoint" value="${esc(i.endpoint)}" placeholder="https://…/v1"></label>`,
    key: (i) => `<label class="field"><span>Key in the vault</span><select data-field="key">
      ${['', ...VAULT_KEYS].map((k) => `<option value="${esc(k)}"${k === i.key ? ' selected' : ''}>${k ? esc(k) : '— choose a key —'}</option>`).join('')}
      </select><span class="hint">Two instances may share one key, or use two accounts.</span></label>`,
    dialect: (i) => `<label class="field"><span>Request dialect</span><select data-field="dialect">
      ${['openai', 'dashscope', 'xai'].map((d) => `<option${d === i.dialect ? ' selected' : ''}>${d}</option>`).join('')}
      </select><span class="hint">How one request is spelled for this endpoint family. "local" is for local engines only.</span></label>`,
    server: (i) => (state.instances.includes(i) ? serverFixed(i) : serverPick(i)),
  };

  /* Chosen once, when the model is added: the instance's id is made from the server and its vendor. */
  function serverPick(i) {
    return `<label class="field"><span>Team server</span><select data-field="server">
      ${state.teamServers.map((s) => `<option value="${esc(s.id)}"${s.id === i.server ? ' selected' : ''}>${esc(s.name)}</option>`).join('')}
      </select></label><label class="field"><span>Vendor on that server</span><select data-field="serverVendor">
      ${(serverOf(i)?.vendors || []).map((x) => `<option${x.id === i.serverVendor ? ' selected' : ''}>${esc(x.id)}</option>`).join('')}
      </select></label>`;
  }

  function serverFixed(i) {
    return `<div class="field wide"><span>Team server</span><span>${esc(serverOf(i)?.name || 'a removed server')} · vendor <code>${esc(i.serverVendor)}</code></span>
      <span class="hint">Fixed — the id is made from them. For another server or vendor, add another model; you can have several of each.</span></div>`;
  }

  const SUMMARY = {
    cli: (i) => (i.cliPath ? `CLI ${i.cliPath}` : 'CLI on PATH'),
    endpoint: (i) => i.endpoint || 'no endpoint yet',
    key: (i) => (i.key ? `key ${i.key}` : 'no key chosen'),
    dialect: (i) => `${i.dialect} dialect`,
    server: (i) => `${serverOf(i)?.name || 'a removed server'} · ${i.serverVendor}`,
  };

  /* Prices are not asked of a Team-server model: the company pays, and the server reports its own spend. */
  function limitsHtml(i) {
    const remote = i.vendor === 'remote';
    const api = accessOf(i).kind === 'api';
    const price = remote ? 'paid by the Team server' : i.priceIn || i.priceOut ? `$${i.priceIn || '?'} in / $${i.priceOut || '?'} out per 1M` : 'list price';
    const priceField = (field, label, list) => `<label class="field"><span>${label}</span>
      <input type="number" min="0" step="0.01" data-field="${field}" value="${esc(i[field] || '')}" placeholder="${list}"></label>`;
    const prices = remote ? '' : `${priceField('priceIn', '$ per 1M in', '0.75 (LiteLLM list price)')}
      ${priceField('priceOut', '$ per 1M out', '3.75 (LiteLLM list price)')}
      ${api ? priceField('priceCached', '$ per 1M cached in', '0.15 (LiteLLM list price)') : ''}`;
    const body = `${prices}
      <label class="field"><span>Give up on one answer after, minutes${help('After this, the reviewer process is killed and the round goes on without it. Empty uses Limits › Reviewer timeout.')}</span>
        <input type="number" min="1" max="60" data-field="timeout" value="${esc(i.timeout)}"></label>`;
    return detailsHtml(i, 'limits', remote ? 'Time limit' : 'Price and time limit', `${price} · ${i.timeout} min`, body);
  }

  function detailsHtml(i, key, title, summary, body) {
    const open = ui.open.has(`${i.id}:${key}`) ? ' open' : '';
    return `<details class="more" data-open-key="${esc(i.id)}:${key}"${open}>
      <summary>${esc(title)} <span class="what">${esc(summary)}</span></summary>
      <div class="settings-grid">${body}</div></details>`;
  }

  function usedByHtml(i) {
    const callers = callersUsing(i.id);
    if (!callers.length) return '';
    const names = callers.map((c) => `<b>${esc(c.label.replace(' asks', ''))}</b>`).join(', ');
    return `<div class="used-by">Consultant for ${names} — set on <a href="#consultants" data-goto="consultants">Consultants</a>.</div>`;
  }

  /* ---------- shared: which models a page uses ---------- */

  /* A feature page never picks a model itself: it shows the instances ticked for it and links to Models. */
  function modelsUsedHtml(feature, label) {
    const list = state.instances.filter((i) => i.features.includes(feature));
    const chips = list.map((i) => {
      const v = vendorOf(i);
      const effort = i.effort ? ` · ${esc(i.effort)}` : '';
      return `<span class="used-chip${i.enabled ? '' : ' off'}" style="--vc: var(--vc-${v.colour})"
        title="${esc(v.maker)} · ${esc(i.model)}${effort}"><span class="nm">${esc(i.name)}${i.enabled ? '' : ' (off)'}</span>
        <span class="md">${esc(i.model)}${effort}</span></span>`;
    }).join('');
    return `<div class="models-used"><span class="filter-label">${esc(label || featureLabel(feature))}</span>
      <div class="used-grid">${chips || '<span class="state warn">no model — this is off</span>'}</div>
      <a href="#models" data-goto="models" data-filter="${feature}">change on Models</a></div>`;
  }

  /* ---------- add dialog ---------- */

  let draft = null;

  function openAdd() {
    draft = null;
    paintAdd();
    $('#add-dialog').showModal();
  }

  /* Opened from a Team server's panel on Setup: straight to the form, that server chosen. */
  function openAddFor(serverId) {
    startDraft('remote', serverId);
    $('#add-dialog').showModal();
  }

  function paintAdd() {
    $('#add-dialog').innerHTML = draft ? addFormHtml() : addPickHtml();
  }

  function addPickHtml() {
    const groups = ADD_GROUPS.map((g) => `<section class="vendor-group"><h3>${esc(g.title)}</h3><p>${esc(g.note)}</p>
      <div class="vendor-tiles">${g.vendors.map(tileHtml).join('')}</div></section>`).join('');
    return `<div class="dlg-head"><h2 id="add-title">Add a model — where does it run?</h2>
        <button type="button" class="icon-btn" data-close aria-label="Close">✕</button></div>
      <div class="dlg-body"><div class="vendor-groups">${groups}</div></div>
      <div class="dlg-foot"><button type="button" class="btn" data-close>Cancel</button></div>`;
  }

  function tileHtml(id) {
    const v = VENDORS[id];
    const a = ACCESS[v.access];
    const none = id === 'remote' && !state.teamServers.some((s) => s.signedIn);
    const sub = none ? 'No Team server signed in — add one on Setup › Team servers' : `${v.maker} · ${a.label}, ${a.detail}`;
    return `<button type="button" class="tile" data-pick="${id}" style="--vc: var(--vc-${v.colour})"${none ? ' disabled' : ''}>
      <span class="t">${esc(v.label)}</span><span class="m">${esc(sub)}</span></button>`;
  }

  function startDraft(vendorId, serverId) {
    const v = VENDORS[vendorId];
    const server = state.teamServers.find((s) => s.id === serverId) || state.teamServers.find((s) => s.signedIn);
    const base = { vendor: vendorId, name: '', server: server?.id, serverVendor: server?.vendors[0]?.id };
    const probe = normalise({ ...base, model: '', id: '' });
    const model = modelsOf(probe)[0] || '';
    const withModel = { ...probe, model };
    const features = ['plan', 'code'].filter((f) => availability(withModel, f).state === 'ok');
    const name = vendorId === 'local' ? model : prettyModel(model) || v.label;
    draft = { ...withModel, features, name };
    paintAdd();
  }

  function prettyModel(m) {
    return m.replace(/-/g, ' ').replace(/\b(gpt|glm)\b/gi, (s) => s.toUpperCase()).replace(/^\w/, (s) => s.toUpperCase());
  }

  function addFormHtml() {
    const v = VENDORS[draft.vendor];
    return `<div class="dlg-head"><h2 id="add-title">Add ${esc(v.label)}</h2>
        <button type="button" class="icon-btn" data-close aria-label="Close">✕</button></div>
      <div class="dlg-body" data-draft>
        ${v.accessNote ? `<p class="callout warn">${esc(v.accessNote)}</p>` : ''}
        <div class="settings-grid">
          <label class="field wide"><span>Display name</span><input type="text" class="draft-name" data-field="name" value="${esc(draft.name)}"></label>
          ${v.connection.includes('server') ? CONNECTION.server(draft, v) : ''}
          ${modelFieldHtml(draft, v)}
          ${effortFieldHtml(draft, v)}
          ${v.connection.filter((c) => c !== 'cli' && c !== 'server').map((c) => CONNECTION[c](draft, v)).join('')}
        </div>
        ${featuresHtml(draft)}
        <p class="from">It gets the id <code>${esc(nextId(baseIdOf(draft)))}</code>. Everything else — system prompt,
          CLI path, price — is on its card once it is added.</p>
      </div>
      <div class="dlg-foot"><button type="button" class="btn ghost" data-back>← Another vendor</button><span class="spacer"></span>
        <button type="button" class="btn" data-close>Cancel</button>
        <button type="button" class="btn primary" data-commit>Add the model</button></div>`;
  }

  function commitDraft() {
    const instance = { ...draft, id: nextId(baseIdOf(draft)), name: draft.name.trim() || draft.model, health: 'warn' };
    state = { ...state, instances: [...state.instances, instance] };
    save();
    $('#add-dialog').close();
    Object.assign(ui, { tab: 'models', feature: '', effort: '', vendor: '', access: '', q: '' });
    render();
    flash(instance.id);
  }

  /* ---------- confirm dialog ---------- */

  /* One confirmation every page may use: a title, body HTML, the button's word, and what it does.
     `danger` paints the confirming button as destructive; Escape and "Keep it" do nothing. */
  let pendingConfirm = null;

  function confirmDialog({ title, body, action, onConfirm, danger = true }) {
    pendingConfirm = onConfirm;
    const dlg = $('#confirm-dialog');
    dlg.innerHTML = `<div class="dlg-head"><h2 id="confirm-title">${esc(title)}</h2></div>
      <div class="dlg-body">${body}</div>
      <div class="dlg-foot"><button type="button" class="btn" data-close autofocus>Keep it</button>
        <button type="button" class="btn ${danger ? 'primary danger-fill' : 'primary'}" data-confirm-generic>${esc(action)}</button></div>`;
    dlg.showModal();
  }

  function confirmRemove(id) {
    const i = byId(id);
    const uses = i.features.map(featureLabel);
    const refs = M().references(i, state);
    const last = M().lastFor(i, state);
    const dlg = $('#confirm-dialog');
    dlg.innerHTML = `<div class="dlg-head"><h2 id="confirm-title">Remove ${esc(i.name)}?</h2></div>
      <div class="dlg-body">
        <p>${uses.length ? `It is ticked for <b>${esc(uses.join(', '))}</b>.` : 'It is ticked for nothing.'}</p>
        ${refs.length ? `<p class="callout warn">It is named as ${esc(refs.join('; '))}. Each of those is left with nobody until you pick another.</p>` : ''}
        ${last.length ? `<p class="callout warn">${esc(lastWhy(last))}</p>` : ''}
        <p class="from">Its spending history stays in the log under <code>${esc(i.id)}</code>, and the id is not reused.</p>
      </div>
      <div class="dlg-foot"><button type="button" class="btn" data-close autofocus>Keep it</button>
        <button type="button" class="btn primary" data-confirm-remove="${esc(id)}"${last.length ? ' disabled' : ''}>Remove</button></div>`;
    dlg.showModal();
  }

  function removeInstances(ids) {
    const gone = new Set(ids);
    const consultants = Object.fromEntries(Object.entries(state.consultants).map(([k, v]) => [k, gone.has(v) ? '' : v]));
    state = { ...state, instances: state.instances.filter((i) => !gone.has(i.id)), consultants };
    save();
  }

  /* ---------- card actions ---------- */

  function duplicate(id) {
    const src = byId(id);
    const copy = { ...structuredClone(src), id: nextId(baseIdOf(src)), name: `${src.name} (copy)`,
      usage: { runs: 0, failed: 0, cost: '—' }, health: 'warn' };
    const at = state.instances.indexOf(src) + 1;
    state = { ...state, instances: [...state.instances.slice(0, at), copy, ...state.instances.slice(at)] };
    save();
    render();
    flash(copy.id, true);
  }

  function test(id) {
    update(id, { health: 'busy' });
    repaintCard(id);
    setTimeout(() => { update(id, { health: 'ok' }); repaintCard(id); }, 900);
  }

  function flash(id, focusName) {
    const card = document.getElementById(`card-${id}`);
    if (!card) return;
    card.scrollIntoView({ block: 'center', behavior: 'smooth' });
    card.classList.add('flash');
    if (focusName) card.querySelector('.name')?.select();
  }

  function repaintCard(id) {
    const old = document.getElementById(`card-${id}`);
    if (!old) return;
    const focusKey = document.activeElement?.closest('.card') === old ? focusSignature(document.activeElement) : '';
    const wrap = document.createElement('div');
    wrap.innerHTML = cardHtml(byId(id));
    old.replaceWith(wrap.firstElementChild);
    if (focusKey) document.getElementById(`card-${id}`).querySelector(focusKey)?.focus();
    const filters = $('.filters');
    if (filters) filters.outerHTML = filtersHtml();
  }

  function focusSignature(el) {
    if (el.dataset.feature) return `[data-feature="${el.dataset.feature}"]`;
    if (el.dataset.field) return `[data-field="${el.dataset.field}"]`;
    if (el.dataset.action) return `[data-action="${el.dataset.action}"]`;
    return '';
  }

  /* ---------- field edits ---------- */

  function applyField(target, i) {
    const f = target.dataset.field;
    if (f === 'feature') return toggleFeature(i, target.dataset.feature, target.checked);
    if (f === 'enabled' || f === 'thinking') return { [f]: target.checked };
    if (f === 'model') return modelChange(i, target);
    if (f === 'systemPromptMode') return { systemPrompt: { mode: target.value, text: i.systemPrompt.text } };
    if (f === 'server') return serverChange(i, target.value);
    if (f === 'serverVendor') return modelReset({ ...i, serverVendor: target.value });
    return { [f]: target.value };
  }

  function serverChange(i, serverId) {
    const server = state.teamServers.find((s) => s.id === serverId);
    return modelReset({ ...i, server: serverId, serverVendor: server?.vendors[0]?.id });
  }

  /* After the server or its vendor changes, keep the model only if that vendor still offers it. */
  function modelReset(next) {
    const list = modelsOf(next);
    return { server: next.server, serverVendor: next.serverVendor, model: list.includes(next.model) ? next.model : list[0] || '' };
  }

  /* Bugz has ONE ranking model: ticking it here unticks it everywhere else, and says so. */
  function toggleFeature(i, feature, on) {
    const features = on ? [...new Set([...i.features, feature])] : i.features.filter((x) => x !== feature);
    if (feature === 'bugz' && on) {
      const others = state.instances.filter((x) => x.id !== i.id && x.features.includes('bugz'));
      others.forEach((x) => update(x.id, { features: x.features.filter((f) => f !== 'bugz') }));
      if (others.length) setTimeout(() => toast(`One model ranks Bugz — moved from ${others.map((x) => x.name).join(', ')}.`), 0);
    }
    return { features };
  }

  function modelChange(i, target) {
    let model = target.value;
    if (model === OTHER_MODEL) {
      model = (window.prompt('The model id, exactly as the vendor spells it:', '') || '').trim() || i.model;
    }
    const probe = { ...i, model };
    const levels = effortLevels(probe);
    const effort = levels && levels.includes(i.effort) ? i.effort : '';
    return { model, effort };
  }

  /* ---------- events ---------- */

  function onPaneChange(e) {
    if (PAGES[ui.tab]?.onChange?.(e)) return;
    const t = e.target;
    if (t.id === 'access-filter') { ui.access = t.value; save(); return render(); }
    if (t.id === 'show-disabled') { ui.showDisabled = t.checked; save(); return render(); }
    const card = t.closest('.card');
    if (!card || !t.dataset.field) return;
    const i = byId(card.dataset.id);
    update(i.id, applyField(t, i));
    repaintCard(i.id);
  }

  function onPaneInput(e) {
    if (PAGES[ui.tab]?.onInput?.(e)) return;
    const t = e.target;
    if (t.id === 'q') { ui.q = t.value; save(); renderCardsOnly(); return; }
    if (t.dataset.field !== 'systemPromptText') return;
    const id = t.closest('.card').dataset.id;
    update(id, { systemPrompt: { mode: 'custom', text: t.value } });
    t.closest('.field').querySelector('[data-count]').textContent = `${t.value.length} characters`;
  }

  function renderCardsOnly() {
    const host = $('.cards') || $('.empty-state');
    host.outerHTML = cardsHtml();
  }

  function onPaneClick(e) {
    if (PAGES[ui.tab]?.onClick?.(e)) return;
    const t = e.target.closest('button, a');
    if (!t) return;
    if (t.dataset.goto) { e.preventDefault(); return go(t.dataset.goto, t.dataset.filter); }
    if (t.dataset.filterKind) {
      const kind = t.dataset.filterKind;
      ui[kind] = ui[kind] === t.dataset.value ? '' : t.dataset.value;
      save();
      return render();
    }
    const action = t.dataset.action;
    if (action === 'add') return openAdd();
    if (action === 'clear-filters') {
      Object.assign(ui, { feature: '', effort: '', vendor: '', access: '', q: '' });
      save();
      return render();
    }
    const id = t.closest('.card')?.dataset.id;
    if (action === 'duplicate') return duplicate(id);
    if (action === 'remove') return confirmRemove(id);
    if (action === 'test') return test(id);
    if (action === 'prompt-default') { update(id, { systemPrompt: { mode: 'default', text: '' } }); return repaintCard(id); }
    if (CARD_ACTIONS[action]) return CARD_ACTIONS[action](byId(id));
  }

  /* What a card's world-facing buttons do in the extension; the mockup says so and, where it can, shows the result. */
  const CARD_ACTIONS = {
    'cli-open': (i) => toast(`Opens a terminal with ${M().CLIS[cliKey(i)]?.exe} typed — sign in there if it asks.`),
    'cli-install': (i) => toast(`Opens a terminal with the install command for ${M().CLIS[cliKey(i)]?.name} typed — you press Enter.`),
    'cli-update': (i) => toast(`Opens a terminal with the update command for ${M().CLIS[cliKey(i)]?.name} typed — you press Enter.`),
    'list-refresh': () => toast('Asking again… the list and the line under it change when it answers.'),
    'wsl-fix': () => toast('Writes networkingMode=mirrored to %USERPROFILE%\\.wslconfig, then asks you to run wsl --shutdown.'),
    'ask-endpoint': (i) => {
      update(i.id, { fetchedModels: ['gpt-oss-120b', 'qwen3-coder-480b', 'deepseek-v4', 'llama-4-maverick'] });
      repaintCard(i.id);
      toast('The endpoint answered with 4 models this key can call.');
    },
  };

  const cliKey = (i) => ({ claude: 'claude', codex: 'codex', deepseek: 'codex', openrouter: 'codex', antigravity: 'antigravity' }[i.vendor]);

  function go(tab, filter) {
    ui.tab = tab;
    if (filter !== undefined) Object.assign(ui, { feature: filter, effort: '', vendor: '', access: '', q: '' });
    save();
    render();
  }

  function onAddClick(e) {
    const t = e.target.closest('button');
    if (!t) return;
    if (t.hasAttribute('data-close')) { $('#add-dialog').close(); return; }
    if (t.dataset.pick) return startDraft(t.dataset.pick);
    if (t.hasAttribute('data-back')) { draft = null; return paintAdd(); }
    if (t.hasAttribute('data-commit')) return commitDraft();
  }

  function onAddChange(e) {
    const t = e.target;
    if (!draft || !t.dataset.field) return;
    draft = { ...draft, ...applyField(t, draft) };
    if (t.dataset.field !== 'name') paintAdd();
  }

  function onConfirmClick(e) {
    const t = e.target.closest('button');
    if (!t) return;
    if (t.dataset.confirmRemove) { removeInstances([t.dataset.confirmRemove]); render(); }
    if (t.hasAttribute('data-confirm-generic') && pendingConfirm) { const run = pendingConfirm; pendingConfirm = null; $('#confirm-dialog').close(); run(); return; }
    if (t.hasAttribute('data-close')) pendingConfirm = null;
    $('#confirm-dialog').close();
  }

  function onTabsClick(e) {
    const t = e.target.closest('[data-tab]');
    if (t) go(t.dataset.tab);
  }

  function onSubtabsClick(e) {
    const t = e.target.closest('[data-sub]');
    if (!t) return;
    ui.sub = { ...ui.sub, [ui.tab]: t.dataset.sub };
    save();
    render();
  }

  /* Arrow keys move along a tab strip, as the ARIA tabs pattern expects. */
  function onTabKeys(e) {
    const strip = e.target.closest('[role="tablist"]');
    if (!strip || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) return;
    const tabs = [...strip.querySelectorAll('[role="tab"]')];
    const at = tabs.indexOf(e.target);
    const next = { ArrowLeft: at - 1, ArrowRight: at + 1, Home: 0, End: tabs.length - 1 }[e.key];
    const target = tabs[(next + tabs.length) % tabs.length];
    e.preventDefault();
    target.click();
    const id = target.id || '';
    requestAnimationFrame(() => (document.getElementById(id) || document.querySelector(`[data-sub="${target.dataset.sub}"]`))?.focus());
  }

  function applyLook() {
    document.documentElement.style.setProperty('--base', `${(13 * 1.1 ** ui.zoom).toFixed(2)}px`);
    $('#zoom-value').textContent = ui.zoom > 0 ? `+${ui.zoom}` : String(ui.zoom);
    // Brighter mixes the text toward the theme's extreme, dimmer toward the background: 9 % a step.
    const root = document.documentElement.style;
    root.setProperty('--tone-to', ui.tone >= 0 ? 'var(--hi)' : 'var(--bg)');
    root.setProperty('--tone-pct', `${Math.abs(ui.tone) * 9}%`);
    $('#tone-value').textContent = ui.tone > 0 ? `+${ui.tone}` : String(ui.tone);
    if (ui.theme === 'auto') delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = ui.theme;
    $('#theme').value = ui.theme;
    $('#days').value = String(ui.daysSinceUpdate);
    $('#mcp').innerHTML = MCP_CHOICES.map(([v, l]) => `<option value="${v}"${v === mcpVersion() ? ' selected' : ''}>${l}</option>`).join('');
  }

  function wire() {
    const pane = $('#pane');
    pane.addEventListener('change', onPaneChange);
    pane.addEventListener('input', onPaneInput);
    pane.addEventListener('click', onPaneClick);
    pane.addEventListener('toggle', (e) => {
      const key = e.target.dataset?.openKey;
      if (key) e.target.open ? ui.open.add(key) : ui.open.delete(key);
    }, true);
    $('#tabs').addEventListener('click', onTabsClick);
    $('#subtabs-host').addEventListener('click', onSubtabsClick);
    document.addEventListener('keydown', onTabKeys);
    $('#add-dialog').addEventListener('click', onAddClick);
    $('#add-dialog').addEventListener('change', onAddChange);
    $('#add-dialog').addEventListener('input', (e) => { if (draft && e.target.dataset.field === 'name') draft = { ...draft, name: e.target.value }; });
    $('#confirm-dialog').addEventListener('click', onConfirmClick);
    document.querySelectorAll('[data-zoom]').forEach((b) => b.addEventListener('click', () => {
      ui.zoom = Math.max(-3, Math.min(5, ui.zoom + Number(b.dataset.zoom)));
      save();
      applyLook();
    }));
    document.querySelectorAll('[data-tone]').forEach((b) => b.addEventListener('click', () => {
      ui.tone = Math.max(-5, Math.min(5, ui.tone + Number(b.dataset.tone)));
      save();
      applyLook();
    }));
    $('#theme').addEventListener('change', (e) => { ui.theme = e.target.value; save(); applyLook(); });
    $('#days').addEventListener('change', (e) => { ui.daysSinceUpdate = Number(e.target.value); save(); render(); });
    $('#mcp').addEventListener('change', (e) => { ui.mcp = e.target.value; save(); render(); });
    $('#reset').addEventListener('click', () => { state = fresh(); save(); render(); });
    window.addEventListener('hashchange', () => {
      const tab = location.hash.slice(1);
      if (tab !== ui.tab && TABS.some((t) => t.id === tab)) go(tab);
    });
  }

  /* What the page modules may use. Each renders and edits only its own slice of the state. */
  window.COAI_APP = {
    get state() { return state; },
    setState(next) { state = next; save(); },
    /* Replace one slice and repaint: the common case for a page's own controls. */
    patch(slice, change) { state = { ...state, [slice]: { ...state[slice], ...change } }; save(); render(); },
    render, esc, toast, openAddFor, removeInstances, vendorOf, accessOf, newTag, byId, featureLabel,
    modelsUsedHtml, go, skew, help, mcpVersion, older, PLANNED, confirm: confirmDialog,
  };

  const hashTab = location.hash.slice(1);
  if (TABS.some((t) => t.id === hashTab)) ui.tab = hashTab;
  wire();
  applyLook();
  render();
})();
