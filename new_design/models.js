/* ConnectOtherAIs settings mockup: the parts of a Models card that report on the world rather than edit a
   setting — the CLI's version and its actions, coai-mcp's own verdict, where a model list came from, what
   the server actually runs an API model at, local-engine safety, and what this coai-mcp ignores.
   Pure helpers over the instance and the state; app.js draws them into the card. Plain script. */
(function () {
  'use strict';

  const { VENDORS, ACCESS } = window.COAI;

  /* The CLI each runtime runs on, with its installed and newest version (demo values). */
  const CLIS = {
    claude: { name: 'Claude Code', exe: 'claude', installed: '2.1.258', latest: '2.1.258' },
    codex: { name: 'Codex CLI', exe: 'codex', installed: '0.156.1', latest: '0.157.0' },
    antigravity: { name: 'Antigravity', exe: 'agy', installed: '', latest: '' },
  };
  const CLI_OF = { claude: 'claude', codex: 'codex', deepseek: 'codex', openrouter: 'codex', antigravity: 'antigravity' };

  /* What coai-mcp itself answers about each instance (`coai-mcp --providers`): absent = it would run it. */
  const VERDICTS = {
    'grok-openrouter': 'xAI answered 401 for the key GROK_OPENROUTER on the last probe',
  };

  /* Where each list of models came from — what makes a list worth trusting. */
  const SOURCES = {
    codex: ['from ~/.codex/models_cache.json, as the Codex CLI last saw it', ''],
    claude: ['from the Claude probe, 2 days ago', 'ask again'],
    antigravity: ['from `agy models`', ''],
    deepseek: ['from the endpoint, 1 hour ago', 'ask the endpoint'],
    dashscope: ['the models coai-mcp\'s Alibaba module knows', ''],
    xai: ['the models coai-mcp\'s xAI module knows', ''],
    local: ['from the engine, just now', 'look again'],
    remote: ['the server\'s allowlist, fetched when you signed in', ''],
  };

  /* The calibrated default an API module runs a model at when nothing is chosen (coai-mcp reports these). */
  const CALIBRATED = { 'glm-5.3': 'low', 'qwen3.8-max': 'low', 'qwen3.7-plus': 'low', 'deepseek-v4': 'medium', 'grok-4.3': 'low', 'grok-4.3-fast': 'low' };

  const esc = (s) => window.COAI_APP.esc(s);
  const cliOf = (i) => CLIS[CLI_OF[i.vendor]];

  function cliBadge(i) {
    const c = cliOf(i);
    if (!c) return '';
    if (!c.installed) return `<span class="badge cli-missing" title="coai-mcp found no ${esc(c.exe)} on PATH or at the path below">${esc(c.name)} not found</span>`;
    const update = c.latest && c.latest !== c.installed ? ` · <b class="upd">${esc(c.latest)} available</b>` : '';
    return `<span class="badge" title="The CLI this model runs on">${esc(c.exe)} ${esc(c.installed)}${update}</span>`;
  }

  /* The CLI's own actions, as today's reviewer card has them: open (which is also how you sign in), install, update. */
  function cliActions(i) {
    const c = cliOf(i);
    if (!c) return '';
    const update = c.installed && c.latest && c.latest !== c.installed
      ? `<button type="button" class="btn small upd-btn" data-action="cli-update">⟳ Update to ${esc(c.latest)}</button>` : '';
    const install = c.installed ? '' : '<button type="button" class="btn small" data-action="cli-install">⤓ Install</button>';
    return `<div class="field wide"><span>The CLI</span><div class="inline">
      <button type="button" class="btn small" data-action="cli-open" title="Opens ${esc(c.exe)} in a terminal — where you sign in">▶ Open in a terminal</button>
      ${install}${update}<span class="hint">Signing in happens in the CLI itself; coai cannot see whether you are signed in.</span></div></div>`;
  }

  /* coai-mcp's verdict, which is not the same thing as a Check: it is whether a round would include this model at all. */
  function verdictBadge(i) {
    const app = window.COAI_APP;
    if (!app.mcpVersion()) return '<span class="badge verdict unknown" title="Install coai-mcp under Setup › MCP server">coai-mcp: not installed — nobody can say</span>';
    const why = VERDICTS[i.id];
    return why ? `<span class="badge verdict no" title="${esc(why)}">coai-mcp cannot run it — ${esc(why)}</span>`
      : '<span class="badge verdict yes" title="As coai-mcp reads your settings file here">coai-mcp will run it</span>';
  }

  function provenance(i) {
    const v = VENDORS[i.vendor];
    if (v.freeModel) {
      return `<span class="hint">Typed by hand. <button type="button" class="linkish" data-action="ask-endpoint">≡ Ask the endpoint which models this key can call</button></span>`;
    }
    const [from, again] = SOURCES[i.vendor] || ['', ''];
    if (!from) return '';
    const button = again ? ` · <button type="button" class="linkish" data-action="list-refresh">⟳ ${esc(again)}</button>` : '';
    return `<span class="hint">${esc(from)}${button}</span>`;
  }

  const calibrated = (i) => (ACCESS[VENDORS[i.vendor].access].kind === 'api' ? CALIBRATED[i.model] || '' : '');

  /* What the server actually runs an API model at — the setting, or the module's calibrated default. */
  function apiRuns(i) {
    if (ACCESS[VENDORS[i.vendor].access].kind !== 'api') return '';
    const effort = i.effort || `${calibrated(i) || 'the vendor default'} (calibrated)`;
    const thinking = VENDORS[i.vendor].thinkingModels?.includes(i.model) ? (i.thinking ? 'on' : 'off') : 'not switchable';
    return `<p class="from runs-at">coai-mcp runs it at effort ${esc(effort)} · thinking ${thinking} · ${esc(i.timeout)} min per answer.</p>`;
  }

  /* Thinking, for every API model: a switch where the module says it can be switched, a sentence where not. */
  function thinkingField(i) {
    const v = VENDORS[i.vendor];
    if (ACCESS[v.access].kind !== 'api') return '';
    if (!v.thinkingModels?.includes(i.model)) {
      return `<div class="field"><span>Thinking</span><span class="hint">${esc(i.model)} always thinks — it cannot be switched off.</span></div>`;
    }
    return `<label class="field"><span>Thinking</span><span class="inline"><input type="checkbox" data-field="thinking"${i.thinking ? ' checked' : ''}> Think before answering</span></label>`;
  }

  const LOOPBACK = /^https?:\/\/(127\.0\.0\.1|localhost|\[::1\])(:\d+)?(\/|$)/i;

  /* A local engine that is not on this machine sends the code away — said where the endpoint is. */
  function localSafety(i) {
    if (i.vendor !== 'local') return '';
    const away = i.endpoint && !LOOPBACK.test(i.endpoint)
      ? `<p class="callout warn">${esc(i.endpoint)} is not this machine: the diff and the rules are sent there. A local engine is meant to keep code here.</p>` : '';
    return `${away}<div class="field wide"><span>The engine</span><div class="inline">
      <button type="button" class="btn small" data-action="wsl-fix" title="Writes networkingMode=mirrored to %USERPROFILE%\\.wslconfig">⇄ Fix WSL networking</button>
      <span class="hint">When the engine answers on Windows but not from WSL. Needs <code>wsl --shutdown</code> afterwards.</span></div></div>`;
  }

  /* What this coai-mcp ignores on this card — one note per card, never one per field. */
  function ignoredNote(i) {
    const app = window.COAI_APP;
    const v = VENDORS[i.vendor];
    const kind = ACCESS[v.access].kind;
    const items = [
      kind === 'api' && ['0.37.0', 'a model reached by an API key'],
      v.effortNew && i.effort && [app.PLANNED, 'its effort'],
      i.systemPrompt.mode === 'custom' && i.systemPrompt.text && [app.PLANNED, 'its system prompt'],
      kind !== 'api' && Number(i.timeout) !== 10 && [app.PLANNED, 'its own time limit'],
      i.features.includes('feature') && ['0.39.0', 'feature review'],
    ].filter(Boolean);
    const version = app.mcpVersion();
    const ignored = items.filter(([since]) => !version || app.older(version, since));
    if (!ignored.length) return '';
    const what = ignored.map(([since, label]) => `${label} (needs ${since})`).join(', ');
    return `<p class="skew">${version ? `coai-mcp ${esc(version)} on this side ignores` : 'coai-mcp is not installed — nothing runs'} on this model: ${esc(what)}. Update under Setup › MCP server.</p>`;
  }

  /* Every place an instance is named — what removing it, or switching it off, takes away. */
  function references(i, state) {
    const out = [];
    const caller = Object.entries(state.consultants).filter(([, id]) => id === i.id).map(([c]) => c);
    if (caller.length) out.push(`the consultant for ${caller.join(', ')}`);
    const rows = state.asking?.qconsult.rows.filter((r) => r.instance === i.id).length || 0;
    if (rows) out.push(`${rows} question-consultant row(s)`);
    const pairs = state.lanes?.security.pairs.filter((p) => p.instance === i.id).length || 0;
    if (pairs) out.push(`${pairs} security pair(s)`);
    if (state.lanes?.chat.main === i.id) out.push('the model a chat opens on');
    if (i.features.includes('bugz')) out.push('Bugz ranking');
    return out;
  }

  /* The last switched-on model for plan or code review cannot leave it — the stage would have no reviewer. */
  function lastFor(i, state) {
    return ['plan', 'code'].filter((f) => i.enabled && i.features.includes(f)
      && state.instances.filter((x) => x.enabled && x.features.includes(f)).length === 1);
  }

  window.COAI_MODELS = { cliBadge, cliActions, verdictBadge, provenance, calibrated, apiRuns, thinkingField, localSafety, ignoredNote, references, lastFor, CLIS };
})();
