/* ConnectOtherAIs settings mockup: the Consultants tab — Consultant and Question consultant.
   Owns the `asking` slice; the callers map (`consultants`) is shared with Models. */
(function () {
  'use strict';

  const { CALLERS, VENDORS } = window.COAI;
  const app = () => window.COAI_APP;
  const esc = (s) => app().esc(s);
  const help = (key) => app().help(HELP[key]);
  const a = () => app().state.asking;
  const setup = () => app().state.setup || {};
  const checked = (on) => (on ? ' checked' : '');
  const MAX_ON = 6;

  /* The first coai-mcp that reads each part of this tab — src_vs_code/src/consultSettings.ts:438 and
     qconsultSettings.ts:92. The cadence has no version gate of its own in the product. */
  const CONSULTANT_DEFINITION_SINCE = '0.23.0';
  const QCONSULT_SINCE = '0.41.0';

  /* shared/runtime-capabilities.json, measured 2026-10-01: what each runtime can be held to. */
  const STANDING = {
    claude: { none: 'confined', disk: 'confined', web: 'confined' },
    codex: { none: 'unconfined', disk: 'unconfined', web: 'unconfined' },
    antigravity: { none: 'default-deny', disk: 'default-deny', web: 'unsupported' },
    local: { none: 'confined', disk: 'unsupported', web: 'unsupported' },
    api: { none: 'confined', disk: 'unsupported', web: 'unmeasured' },
  };
  /* The same file's `note` for every refused cell — the reason a stored pair is named with. */
  const REFUSED_BECAUSE = {
    'antigravity:web': 'it can search, but cannot fetch a page headless without --dangerously-skip-permissions, which nothing here passes',
    'local:disk': 'a local engine has no file tool to hand a folder to',
    'local:web': 'a local engine has no web tool',
    'api:disk': 'a hosted completion reads no disk',
    'api:web': 'not measured yet — it is vendor-specific',
  };
  const RUNTIME_OF = { claude: 'claude', codex: 'codex', deepseek: 'codex', openrouter: 'codex', antigravity: 'antigravity',
    local: 'local', dashscope: 'api', xai: 'api', compat: 'api' };
  /* consultSettings.ts CONSULTING_RUNTIMES: what the server consults through today — codex WITHOUT an endpoint. */
  const CONSULTING_RUNTIMES = ['codex', 'claude', 'antigravity', 'local'];
  const CONFINEMENT = {
    claude: ['ok', 'Confined', 'its CLI holds it to the folders it is given'],
    codex: ['err', 'Not confined', 'it has a shell and can read any file the account can'],
    antigravity: ['warn', 'Denies by default', 'held outside its folders only by a headless default'],
    local: ['ok', 'Prompt only', 'it sees the diff it is sent and opens nothing'],
    api: ['ok', 'Prompt only', 'it sees the diff it is sent and opens nothing'],
  };

  /* Where each side's CLI was found, as its MCP server reported it. `null`: not found on that side (demo). */
  const DEMO_CLI = {
    windows: {
      claude: { path: 'C:\\Users\\strug\\.local\\bin\\claude.exe', version: '2.1.258' },
      codex: { path: 'C:\\Users\\strug\\AppData\\Roaming\\npm\\codex.cmd', version: 'codex-cli 0.130.0' },
      antigravity: null,
    },
    linux: {
      claude: { path: '/home/strug/.local/bin/claude', version: '2.1.197' },
      codex: { path: '/home/strug/.npm-global/bin/codex', version: 'codex-cli 0.130.0' },
      antigravity: { path: '/home/strug/.local/bin/agy', version: 'agy 1.2.15' },
    },
  };
  /* The other side's own store, as its server last wrote it: read here, never written. */
  const OTHER_SIDE_CHECKS = {
    claude: { state: 'passed', when: '2 h ago', secs: 21 },
    codex: { state: 'never' },
    gemini: { state: 'passed', when: '2 h ago', secs: 33 },
    other: { state: 'never' },
  };

  /* shared/consultant-limitations.json, the Linux and WSL antigravity rows (and the Windows one's fact). */
  const AGY = {
    settingsPath: '~/.gemini/antigravity-cli/settings.json',
    snippet: '{\n  "permissions": {\n    "allow": ["command(git grep)"]\n  }\n}',
    warning: 'This rule is not read-only: a prefix rule lets git grep -O<cmd> run a program and git log --output= write a file. Paste it only if you accept that.',
    windows: 'On Windows agy honours only exact command lines in permissions.allow, so there is no such rule to paste there: on Windows the confinement is held by agy\'s headless default only.',
  };

  /* help.ts, shortened to a sentence or two each. */
  const HELP = {
    consultEnabled: 'Lets an AI ask ANOTHER vendor\'s model for a second opinion: it reads this checkout read-only and answers advice the AI must verify. Off switches off stuck, cadence and risk consultations alike.',
    consultCaller: 'Which model answers when THIS kind of AI consults. A different vendor from the caller is the point: a model cannot see its own blind spot.',
    turns: 'How many question-and-answer turns one consultation may have — five unless you change it. The last turn closes it; a fresh problem opens a new one.',
    calls: 'How many calls a STUCK session may make to the consultant in 24 hours — ten unless you change it. Cadence and risk consultations are not counted.',
    idle: 'A consultation nobody has come back to for this long is closed and its vendor conversation dropped. A later question is a new consultation with the whole budget.',
    consultPrompt: 'The instructions the consultant receives ahead of the problem. Empty is the prompt this build ships with; Restore default takes your override away.',
    cadence: 'When the consultant is asked although nobody is stuck: every group of epics owes one consultation before its first code round. Remind puts the order in every review reply; Require refuses the group\'s first code round until it is taken.',
    cadenceEvery: 'How many epics share ONE consultation — three unless you change it. The groups are counted from the plan\'s own first epic.',
    cadenceRiskThreshold: 'From how many epics the AI is also asked which pieces carry the most risk — five unless you change it. Below it, the groups alone are consulted on.',
    cadenceRiskMax: 'The most risky pieces one plan may name — three unless you change it. Each one named gets a consultation of its own.',
    qconsultEnabled: 'Lets an AI put a question to the question consultant — ask_consultants — before it asks you. Every row that is on answers at once, and the answers go back separately, as advice.',
    qconsultMode: 'What the gate in front of ask_human does with a question not put to the consultants first. Require refuses it while a plan is being built; Remind lets it through with a note; Off says nothing.',
    qconsultRows: 'Each row is one model with exactly ONE base prompt, switched on or off — at most six on, and every row that is on runs in parallel. A pair whose runtime cannot do what the prompt needs is disabled with the measured reason.',
    qconsultRowPrompt: 'The one base prompt this row answers with. A prompt this row\'s runtime cannot serve is listed but disabled, with the reason from the measured capability table.',
    qconsultRowCanRead: 'This runtime can serve the prompt but cannot be held inside the folders it is given. The mark stays on the row and beside every answer it gives.',
    qconsultPrompts: 'The instructions a row is given ahead of the question. Three ship; edit their words (Restore default puts them back) or add your own, each with the capability it needs.',
    qconsultRoots: 'The folders a disk row may read, read-only. Never a drive root, your profile folder itself, a system folder or the data folder — each is refused here and by the server, by name.',
    rowMinutes: 'How long one row may take on one question — five minutes unless you change it. A row past it is timed out and the others still answer.',
    perSession: 'How many questions one assistant session may put to the consultants — ten unless you change it. Past it the next question goes to you directly.',
    freeBatches: 'How many batches of questions after a plan reached proceed go to you directly before the consultants are required — two unless you change it.',
  };

  const SHIPPED_PROMPTS = [
    { id: 'question-disk', title: 'Projects on this disk', capability: 'disk', shipped: true,
      text: 'You are a senior engineer who knows this machine\'s other projects. Study the code of the other projects in the folders you were given for something similar to what is asked…' },
    { id: 'question-web', title: 'The internet', capability: 'web', shipped: true,
      text: 'You are a senior engineer with the web in front of you. Search for how this is solved today…' },
    { id: 'question-opinion', title: 'The best developer\'s opinion', capability: 'none', shipped: true,
      text: 'You are the best developer this team knows. Answer from the question and its context alone…' },
  ];

  function defaults() {
    return {
      consult: {
        enabled: true, turns: 5, calls: 10, idle: 15, prompt: '',
        cadence: { mode: 'remind', every: 3, riskThreshold: 5, riskMax: 3 },
        health: {
          claude: { state: 'passed', when: '12 min ago', secs: 14 },
          codex: { state: 'never' },
          gemini: { state: 'failed', when: 'yesterday 18:40', what: 'Quota reached', cure: 'Wait for the quota to reset, or let another instance answer Gemini.' },
          other: { state: 'never' },
        },
      },
      qconsult: {
        enabled: true, mode: 'require',
        rows: [
          { id: 'row-1', instance: 'claude-2', prompt: 'question-disk', enabled: true },
          { id: 'row-2', instance: 'codex-2', prompt: 'question-web', enabled: true },
          { id: 'row-3', instance: 'qwen-2', prompt: 'question-opinion', enabled: true },
        ],
        prompts: structuredClone(SHIPPED_PROMPTS), roots: ['D:\\rsd', 'C:\\Users\\strug'],
        rowMinutes: 5, perSession: 10, freeBatches: 2,
      },
    };
  }

  /* ---------- small controls ---------- */

  function seg(name, options, value, data) {
    return `<div class="seg" role="radiogroup" aria-label="${esc(name)}">${options.map(([v, l]) =>
      `<label><input type="radio" name="${name}" value="${v}" ${data}${checked(value === v)}>${esc(l)}</label>`).join('')}</div>`;
  }

  function number(label, value, min, max, data, helpKey) {
    return `<div class="field-line"><label>${esc(label)}${helpKey ? help(helpKey) : ''}</label>
      <input type="number" class="num" min="${min}" max="${max}" value="${esc(value)}" ${data} aria-label="${esc(label)}"></div>`;
  }

  /* A checkbox with its words; the "?" sits beside the label, so a click on it never flips the box. */
  function tick(data, on, words, helpKey) {
    return `<div class="row-line"><label class="inline"><input type="checkbox" ${data}${checked(on)}> ${esc(words)}</label>${help(helpKey)}</div>`;
  }

  /* What a stored pick points at: a live instance, one no longer ticked for the feature, or one removed. */
  function pickOf(id, feature) {
    if (!id) return { instance: undefined, stranded: '' };
    const instance = app().byId(id);
    if (!instance) return { instance: undefined, stranded: 'removed' };
    return { instance, stranded: instance.features.includes(feature) ? '' : 'unticked' };
  }

  /* The disabled option a stranded pick is shown as. */
  function strandedLabel(id, pick, featureWord) {
    return pick.stranded === 'removed' ? `${id} — removed` : `${pick.instance.name} — no longer ticked ${featureWord}`;
  }

  /* The same fact as a sentence. */
  function strandedSentence(id, pick, featureWord) {
    return pick.stranded === 'removed' ? `${id} was removed` : `${pick.instance.name} is no longer ticked "${featureWord}" on Models`;
  }

  /* ---------- Consultant ---------- */

  function consultantPage() {
    const c = a().consult;
    return `<p class="lead">When an AI is stuck it calls <code>consult</code>: another model reads this checkout READ-ONLY, with the
        uncommitted change, and answers advice the AI must verify. Each caller picks one of the models ticked "Consultant" on Models.</p>
      ${app().skew(CONSULTANT_DEFINITION_SINCE, 'A consultant defined by its own model, runtime and CLI path')}
      ${app().modelsUsedHtml('consultant', 'Consultants')}
      <div class="plan-grid">
        <section class="panel ask-wide"><h3>Who answers whom</h3>${CALLERS.map(callerRow).join('')}</section>
        ${consultingPanel(c)}
        ${cadencePanel(c.cadence)}
        ${promptPanel(c)}
      </div>`;
  }

  function consultingPanel(c) {
    return `<section class="panel"><h3>Consulting</h3>
      ${tick('data-ac="enabled"', c.enabled, 'Let an AI consult another model', 'consultEnabled')}
      <p class="from">The AI calls it itself when stuck; the gate also orders one per group of epics when the cadence is on.</p>
      ${number('Turns per consultation', c.turns, 1, 20, 'data-ac-num="turns"', 'turns')}
      ${number('Calls per session', c.calls, 1, 100, 'data-ac-num="calls"', 'calls')}
      ${number('Close an idle consultation after, minutes', c.idle, 1, 240, 'data-ac-num="idle"', 'idle')}
      <p class="from">A consultation leaves a thread in the vendor's own store holding the uncommitted change — that is what makes
        a follow-up possible, and it is not ours to delete.</p></section>`;
  }

  function callerRow(caller) {
    const stored = app().state.consultants[caller.id] || '';
    const pick = pickOf(stored, 'consultant');
    const chosen = pick.stranded ? undefined : pick.instance;
    const body = pick.stranded ? strandedCallerNote(caller, stored, pick)
      : `${callerNotes(caller, chosen)}${chosen ? serverWorkNote(chosen) + healthHtml(caller, chosen) : ''}`;
    return `<div class="caller-row"><label for="caller-${caller.id}">${esc(caller.label)}${help('consultCaller')}</label>
      <select id="caller-${caller.id}" data-caller="${caller.id}">${callerOptions(stored, pick)}</select>
      <div class="ask-col">${body}</div></div>`;
  }

  function callerOptions(stored, pick) {
    const pool = app().state.instances.filter((i) => i.features.includes('consultant'));
    const stranded = pick.stranded
      ? `<option value="${esc(stored)}" selected disabled>${esc(strandedLabel(stored, pick, 'Consultant'))}</option>` : '';
    const live = pool.map((i) => `<option value="${esc(i.id)}"${!pick.stranded && pick.instance?.id === i.id ? ' selected' : ''}>`
      + `${esc(i.name)} · ${esc(VENDORS[i.vendor].maker)}${i.enabled ? '' : ' (off)'}</option>`);
    return [`<option value=""${stored ? '' : ' selected'}>— nobody —</option>`, stranded, ...live].join('');
  }

  function strandedCallerNote(caller, stored, pick) {
    return `<div class="overlap callout warn"><span class="state warn">No consultant</span> ${esc(strandedSentence(stored, pick, 'Consultant'))}.
      Consultations from ${esc(caller.label.replace(/ asks$/, ''))} are refused until another model is picked.</div>`;
  }

  function callerNotes(caller, chosen) {
    if (!chosen) return '<div class="overlap callout warn">Nobody answers this caller — its consultations are refused.</div>';
    const notes = [];
    if (caller.maker && VENDORS[chosen.vendor].maker === caller.maker) {
      notes.push(`Same vendor as the caller (${esc(caller.maker)}). Allowed — a different vendor usually catches what this one misses.`);
    }
    if (!chosen.enabled) notes.push('This model is switched off on Models.');
    if (!app().accessOf(chosen).files) notes.push('Prompt only: it answers from the diff it is sent and cannot open files.');
    return notes.length ? `<div class="overlap callout warn">${notes.join('<br>')}</div>` : '';
  }

  /* An instance the server cannot consult through today: an API key, or the Codex CLI with an endpoint. */
  function serverWorkNote(chosen) {
    const runtime = RUNTIME_OF[chosen.vendor];
    if (runtime === 'api') return app().skew(app().PLANNED, 'A consultant on an API key');
    const withEndpoint = runtime === 'codex' && Boolean(VENDORS[chosen.vendor].endpoint);
    if (withEndpoint || !CONSULTING_RUNTIMES.includes(runtime)) return app().skew(app().PLANNED, 'A consultant through the Codex CLI with an endpoint');
    return '';
  }

  /* ---------- a caller's health: this side, the other side read-only, agy's allow rule ---------- */

  function healthHtml(caller, chosen) {
    return `${thisSideHtml(caller, chosen)}${otherSideHtml(caller, chosen)}${agyHtml(caller, chosen)}`;
  }

  const sideKind = (label) => (/^(WSL|Linux)/i.test(label || '') ? 'linux' : 'windows');
  const fact = (term, html) => (html ? `<dt>${esc(term)}</dt><dd>${html}</dd>` : '');

  /* The CLI as that side found it, or why there is none — `missing` when a CLI runtime has none installed. */
  function cliFact(chosen, sideLabel) {
    const runtime = RUNTIME_OF[chosen.vendor];
    if (runtime === 'api') return { html: esc(`No CLI — an HTTP call with the vault key ${chosen.key || VENDORS[chosen.vendor].key || 'it names'}.`) };
    if (runtime === 'local') return { html: esc(`No CLI — the engine at ${chosen.endpoint || VENDORS.local.endpoint}.`) };
    const found = DEMO_CLI[sideKind(sideLabel)][runtime];
    if (!found) return { missing: true, html: '<span class="state err">CLI not found</span> — install it under Setup › Vendor keys.' };
    return { html: `CLI: <code>${esc(chosen.cliPath || found.path)}</code> · ${esc(found.version)}` };
  }

  function confinementFact(chosen) {
    const [tone, word, why] = CONFINEMENT[RUNTIME_OF[chosen.vendor]] || ['warn', 'Not measured', ''];
    return `<span class="state ${tone}">${esc(word)}</span> — ${esc(why)}`;
  }

  function thisSideHtml(caller, chosen) {
    const side = setup().side || 'this side';
    const h = a().consult.health[caller.id] || { state: 'never' };
    const cli = cliFact(chosen, side);
    return `<section class="ask-health" aria-label="On ${esc(side)}"><h4>On ${esc(side)}</h4><dl>
      ${fact('CLI', cli.html)}${fact('Confinement', confinementFact(chosen))}
      ${fact('Check', `${checkHtml(h, chosen, caller.id)}${checkButton(caller, h, cli.missing)}`)}</dl></section>`;
  }

  function checkButton(caller, h, missing) {
    const busy = h.state === 'checking';
    const why = missing ? 'Install the CLI first — there is nothing to check' : 'One real, paid turn of this consultant in a scratch folder — never your code';
    return `<button type="button" class="btn small" data-ac-check="${caller.id}"${busy || missing ? ' disabled' : ''}
      title="${esc(why)}">${busy ? 'Checking…' : 'Check'}</button>`;
  }

  function checkHtml(h, chosen, callerId) {
    if (h.state !== 'failed') return `<div>${esc(checkLine(h, chosen))}</div>`;
    const transcript = `${setup().data?.directory || '<data folder>'}\\consult\\checks\\${callerId}-last.jsonl`;
    return `<div><span class="state err">Failed</span> ${esc(checkLine(h, chosen))}</div>
      ${h.cure ? `<div>${esc(h.cure)}</div>` : ''}<div class="from">Its transcript: <code>${esc(h.transcript || transcript)}</code></div>`;
  }

  function checkLine(h, chosen) {
    if (h.state === 'checking') return 'Checking… the MCP server is starting the check.';
    if (h.state === 'passed') return `Checked ${h.when}: ${chosen.name} answered in ${h.secs} s. It read the marker in CHECK.md — the file was read, not guessed.`;
    if (h.state === 'failed') return `Checked ${h.when}: ${chosen.name} — ${h.what}.`;
    return 'Never checked on this side.';
  }

  function otherSideCheck(h, chosen, side) {
    if (h.state !== 'passed') return `<span class="state idle">Not checked</span> Never checked on ${esc(side)}.`;
    return `<span class="state ok">Passed</span> Checked ${esc(h.when)} on ${esc(side)}: ${esc(chosen.name)} answered in ${esc(h.secs)} s.`;
  }

  /* Another installation's side: its own store as its server last wrote it — no button, and where one would be. */
  function otherSideHtml(caller, chosen) {
    const side = (setup().otherSides || [])[0];
    if (!side) return '';
    const h = OTHER_SIDE_CHECKS[caller.id] || { state: 'never' };
    const where = side.startsWith('WSL') ? 'a Remote-WSL window' : 'a window on that installation';
    return `<section class="ask-health read-only" aria-label="On ${esc(side)}, read-only">
      <h4>On ${esc(side)} — read-only, as of 2 h ago</h4><dl>
      ${fact('CLI', cliFact(chosen, side).html)}${fact('Check', otherSideCheck(h, chosen, side))}</dl>
      <p class="from">To run a Check there, open a window on that side — ${esc(where)} on ${esc(side)}.</p></section>`;
  }

  function agyHtml(caller, chosen) {
    if (RUNTIME_OF[chosen.vendor] !== 'antigravity') return '';
    return `<div class="ask-snippet">
      <p class="from">An allow rule for agy's settings file, <code>${esc(AGY.settingsPath)}</code> — coai never writes it (Linux and WSL):</p>
      <pre class="paste">${esc(AGY.snippet)}</pre>
      <p class="from"><span class="state warn">Not read-only</span> ${esc(AGY.warning)}</p>
      <p class="row-line"><button type="button" class="btn small" data-ac-copy="${caller.id}">Copy</button></p>
      <p class="from">${esc(AGY.windows)}</p></div>`;
  }

  /* ---------- Consultant: cadence and prompt ---------- */

  function cadencePanel(cd) {
    return `<section class="panel"><h3>Consultation cadence</h3>
      <div class="row-line"><span>When it is ordered${help('cadence')}</span>
        ${seg('cadence', [['off', 'Off'], ['remind', 'Remind'], ['require', 'Require']], cd.mode, 'data-ac-cadence="mode"')}</div>
      <p class="from">After a plan is split, every group of epics owes one consultation — is the group right, where is it weak,
        what did it forget. Remind puts the order in every review reply; Require also refuses the group's first code round until it is taken.</p>
      ${number('One consultation per this many epics', cd.every, 1, 14, 'data-ac-cadence="every"', 'cadenceEvery')}
      ${number('Ask for the riskiest pieces from this many epics', cd.riskThreshold, 1, 14, 'data-ac-cadence="riskThreshold"', 'cadenceRiskThreshold')}
      ${number('Riskiest pieces per plan, at most', cd.riskMax, 1, 14, 'data-ac-cadence="riskMax"', 'cadenceRiskMax')}</section>`;
  }

  function promptPanel(c) {
    return `<section class="panel"><h3><label for="ac-prompt">What the consultant is asked to do</label>${help('consultPrompt')}</h3>
      <textarea id="ac-prompt" rows="7" data-ac="prompt" placeholder="The prompt this build ships with.">${esc(c.prompt)}</textarea>
      <p class="row-line"><button type="button" class="btn small ghost" data-ac-action="restore-consult"${c.prompt ? '' : ' disabled'}>Restore default</button>
        <span class="from">${c.prompt ? 'Your own text is used on the next consultation.' : 'Empty is the prompt this build ships with.'}</span></p>
      <p class="from">Emptying the box is the same as Restore default — there is no such thing as a prompt that says nothing.
        The model's own system prompt (Models) is sent before this one.</p></section>`;
  }

  /* ---------- Question consultant ---------- */

  function qconsultPage() {
    const q = a().qconsult;
    return `<p class="lead">Before an AI asks YOU a question it calls <code>ask_consultants</code>: every row that is on answers it at
        once, each with its one prompt, and the answers come back separately — advice, never orders.</p>
      ${app().skew(QCONSULT_SINCE, 'The question consultant (ask_consultants)')}
      ${app().modelsUsedHtml('qconsult', 'Question consultants')}
      <div class="plan-grid">
        ${whenPanel(q)}
        ${rowsPanel(q)}
        ${promptsPanel(q)}
        ${rootsPanel(q)}
      </div>`;
  }

  function whenPanel(q) {
    return `<section class="panel"><h3>When it is asked</h3>
      ${tick('data-aq="enabled"', q.enabled, 'Let an AI ask the question consultant first', 'qconsultEnabled')}
      <div class="row-line"><span>Before it asks you${help('qconsultMode')}</span>${seg('qmode', [['off', 'Off'], ['remind', 'Remind'], ['require', 'Require']], q.mode, 'data-aq="mode"')}</div>
      <p class="from">Require: once a plan is being built, a question not put to the consultants first is refused. A production
        risk with a reason always reaches you at once, with the answers folded under it.</p>
      ${number('Minutes one row may run', q.rowMinutes, 1, 60, 'data-aq-num="rowMinutes"', 'rowMinutes')}
      ${number('Questions per session', q.perSession, 1, 100, 'data-aq-num="perSession"', 'perSession')}
      ${number('Question batches that reach you first', q.freeBatches, 1, 20, 'data-aq-num="freeBatches"', 'freeBatches')}</section>`;
  }

  /* The capability table's decision for a pair: admitted (perhaps flagged), or refused with the measured reason. */
  function admit(instance, prompt) {
    const runtime = RUNTIME_OF[instance?.vendor];
    if (!runtime) return { ok: false, why: 'a Team server cannot answer questions' };
    const standing = STANDING[runtime][prompt.capability];
    if (standing === 'confined') return { ok: true };
    if (standing === 'unconfined' || standing === 'default-deny') return { ok: true, flag: standing };
    return { ok: false, why: REFUSED_BECAUSE[`${runtime}:${prompt.capability}`] || `${runtime} cannot do "${prompt.capability}" (${standing})` };
  }

  function rowsPanel(q) {
    const on = q.rows.filter((x) => x.enabled).length;
    return `<section class="panel"><h3>Who answers${help('qconsultRows')}</h3>${q.rows.map((row) => rowHtml(row, on)).join('')}
      <p class="row-line"><button type="button" class="btn small" data-aq-action="add-row">＋ Add a row</button>
        <span class="from">${on} of at most ${MAX_ON} on. A row is one model and exactly one prompt; a new row starts switched off.</span></p></section>`;
  }

  /* Why a row cannot run as stored — its stranded model first, then the pair — or empty when it can. */
  function rowRefusal(row, pick, prompt) {
    if (pick.stranded) return `pick a model and a prompt: ${strandedSentence(row.instance, pick, 'Question consultant')}`;
    if (!pick.instance || !prompt) return 'pick a model and a prompt';
    const verdict = admit(pick.instance, prompt);
    return verdict.ok ? '' : `${pick.instance.name} cannot run '${prompt.title}' — ${verdict.why}`;
  }

  function rowHtml(row, on) {
    const pick = pickOf(row.instance, 'qconsult');
    const instance = pick.stranded ? undefined : pick.instance;
    const prompt = a().qconsult.prompts.find((p) => p.id === row.prompt);
    const refusal = rowRefusal(row, pick, prompt);
    const blocked = refusal || (!row.enabled && on >= MAX_ON ? `${MAX_ON} rows are on — switch another off first` : '');
    const flag = !refusal && instance && prompt ? admit(instance, prompt).flag : '';
    return `<div class="block" data-row="${esc(row.id)}"><div class="row-line">
        <label class="inline"><input type="checkbox" data-aq-row="enabled"${checked(row.enabled && !refusal)}${blocked ? ` disabled title="${esc(blocked)}"` : ''}> runs</label>
        <select data-aq-row="instance" aria-label="Model">${rowModelOptions(row, pick)}</select>
        <select data-aq-row="prompt" aria-label="Prompt">${rowPromptOptions(row, instance)}</select>${help('qconsultRowPrompt')}
        <button type="button" class="icon-btn" data-aq-action="remove-row" title="Remove this row">✕</button></div>
      ${blocked ? `<p class="from"><span class="state warn">Blocked</span> ${esc(blocked)}</p>` : ''}
      ${flag ? canReadHtml(flag) : ''}</div>`;
  }

  function rowModelOptions(row, pick) {
    const pool = app().state.instances.filter((i) => i.features.includes('qconsult'));
    const stranded = `<option value="${esc(row.instance)}" selected disabled>${esc(strandedLabel(row.instance, pick, 'Question consultant'))}</option>`;
    const head = pick.stranded ? stranded : (pick.instance ? '' : '<option value="">— a model —</option>');
    return head + pool.map((i) => `<option value="${esc(i.id)}"${!pick.stranded && i.id === row.instance ? ' selected' : ''}>${esc(i.name)}</option>`).join('');
  }

  function rowPromptOptions(row, instance) {
    return a().qconsult.prompts.map((p) => {
      const v = instance ? admit(instance, p) : { ok: true };
      return `<option value="${esc(p.id)}"${p.id === row.prompt ? ' selected' : ''}${v.ok ? '' : ` disabled title="${esc(v.why)}"`}>`
        + `${esc(p.title)} — ${esc(p.capability)}${v.ok ? '' : ' — cannot run here'}</option>`;
    }).join('');
  }

  function canReadHtml(flag) {
    const why = flag === 'unconfined'
      ? 'it has no setting that limits what it reads, so this cannot be switched off'
      : 'only its own default refuses a read outside the folders below';
    return `<p class="from"><span class="state warn">Can read this machine</span>${help('qconsultRowCanRead')} — ${why}.</p>`;
  }

  function promptsPanel(q) {
    const cards = q.prompts.map((p) => {
      const kind = !p.shipped ? 'mine' : p.edited ? 'edited' : 'shipped';
      const tag = { shipped: 'default', edited: 'edited', mine: 'yours' }[kind];
      return `<div class="pcard ${kind}" data-prompt="${esc(p.id)}"><h4>${esc(p.title)} <span class="kind">${tag}</span> <span class="cap ${esc(p.capability)}">${esc(p.capability)}</span></h4>
        <textarea rows="3" data-aq-prompt="text" aria-label="${esc(p.title)}">${esc(p.text)}</textarea>
        <div class="foot">${kind === 'edited' ? '<button type="button" class="btn small ghost" data-aq-action="restore-prompt">Restore default</button>' : ''}
          ${kind === 'mine' ? '<button type="button" class="btn small ghost danger" data-aq-action="remove-prompt">Remove</button>' : ''}
          ${kind === 'shipped' ? '<span class="from">The words this build ships with.</span>' : ''}</div></div>`;
    }).join('');
    return `<section class="panel"><h3>Base prompts${help('qconsultPrompts')}</h3><div class="choices">${cards}</div>
      <p class="row-line"><button type="button" class="btn small" data-aq-action="add-prompt">＋ Add a prompt…</button>
        <span class="from">none — the question alone · disk — reads the folders below · web — searches the internet.</span></p></section>`;
  }

  /* ---------- the roots: the server's QuestionRoots.WhyNot, as qconsultWrite.ts rootRefusal draws it ---------- */

  const PROFILE = 'C:\\Users\\strug';
  const SYSTEM_DIRS = ['C:\\Windows', 'C:\\Program Files', 'C:\\Program Files (x86)', 'C:\\ProgramData'];
  const CREDENTIAL_DIRS = ['.ssh', '.aws', '.gnupg', '.config\\gcloud', '.claude', '.codex', '.azure'];

  /* Windows compares paths without case: one spelling, no trailing separator. */
  const norm = (path) => String(path).trim().replace(/\//g, '\\').replace(/\\+$/, '').toLowerCase();
  const within = (path, dir) => path === norm(dir) || path.startsWith(`${norm(dir)}\\`);
  const above = (path, dir) => norm(dir).startsWith(`${path}\\`);

  function rootRefusal(root, dataDir) {
    const p = norm(root);
    const checks = [
      [!/^([a-z]:\\|\\\\|\/)/i.test(String(root).trim()), `'${root}' is not an absolute path — a disk root is spelled from a drive or from /`],
      [/^[a-z]:$/.test(p) || p === '', `'${root}' is a drive root — a disk row would read the whole disk; name the project folders instead`],
      [p === norm(PROFILE), `'${root}' is your profile folder itself — every file of yours; name the project folders under it instead`],
      [above(p, PROFILE), `'${root}' contains your profile folder — every file of yours; name the project folders instead`],
      [Boolean(dataDir) && (within(p, dataDir) || above(p, dataDir)), `'${root}' is or holds the data folder — the records, the ledger and the sessions are not another project to read`],
      [SYSTEM_DIRS.some((dir) => within(p, dir) || above(p, dir)), `'${root}' is or holds a system folder — not a project, and not a disk row's to read`],
      [CREDENTIAL_DIRS.some((dir) => `${p}\\`.includes(`\\${dir}\\`)), `'${root}' is a credential folder or inside one — keys and vendor sign-ins are not a project to read`],
    ];
    return checks.find(([refused]) => refused)?.[1] || '';
  }

  function rootHtml(root, n) {
    const refusal = rootRefusal(root, setup().data?.directory);
    return `<div class="ask-root"><div class="row-line"><code>${esc(root)}</code>
      <button type="button" class="icon-btn" data-aq-action="remove-root" data-n="${n}" title="Remove">✕</button></div>
      ${refusal ? `<p class="ask-refusal"><span class="state err">Refused</span> ${esc(refusal)}</p>` : ''}</div>`;
  }

  function rootsPanel(q) {
    const list = q.roots.length ? q.roots.map(rootHtml).join('')
      : '<p class="from">No folder yet — a disk row reads nothing until one is here.</p>';
    return `<section class="panel"><h3>Folders a disk row may read${help('qconsultRoots')}</h3>${list}
      <p class="row-line"><button type="button" class="btn small" data-aq-action="add-root">＋ Add a folder…</button></p>
      <p class="from">Read-only, and never a drive root, your profile folder itself, a system folder or the data folder — the server refuses those too.</p></section>`;
  }

  /* ---------- events ---------- */

  const patchConsult = (change) => app().patch('asking', { consult: { ...a().consult, ...change } });
  const patchQ = (change) => app().patch('asking', { qconsult: { ...a().qconsult, ...change } });
  const patchRow = (id, change) => patchQ({ rows: a().qconsult.rows.map((x) => (x.id === id ? { ...x, ...change } : x)) });
  const clamp = (t) => Math.min(Number(t.max), Math.max(Number(t.min), Number(t.value) || Number(t.min)));
  const valueOf = (t) => (t.type === 'checkbox' ? t.checked : t.value);

  function onCallerChange(t) {
    app().setState({ ...app().state, consultants: { ...app().state.consultants, [t.dataset.caller]: t.value } });
    app().render();
  }

  function onCadenceChange(t) {
    const v = t.type === 'radio' ? t.value : clamp(t);
    patchConsult({ cadence: { ...a().consult.cadence, [t.dataset.acCadence]: v } });
  }

  function onRowChange(t) {
    const id = t.closest('[data-row]').dataset.row;
    const v = valueOf(t);
    patchRow(id, t.dataset.aqRow === 'enabled' ? { enabled: v } : { [t.dataset.aqRow]: v, enabled: false });
  }

  /* The first data-* key a control carries decides who handles its change. */
  const CHANGES = [
    ['caller', onCallerChange],
    ['ac', (t) => patchConsult({ [t.dataset.ac]: valueOf(t) })],
    ['acNum', (t) => patchConsult({ [t.dataset.acNum]: clamp(t) })],
    ['acCadence', onCadenceChange],
    ['aq', (t) => patchQ({ [t.dataset.aq]: valueOf(t) })],
    ['aqNum', (t) => patchQ({ [t.dataset.aqNum]: clamp(t) })],
    ['aqRow', onRowChange],
    ['aqPrompt', (t) => promptText(t)],
  ];

  function onChange(e) {
    const hit = CHANGES.find(([key]) => e.target.dataset[key]);
    if (!hit) return false;
    hit[1](e.target);
    return true;
  }

  /* A shipped prompt's text, once changed, is an override — orange, with Restore default. */
  function promptText(t) {
    const id = t.closest('[data-prompt]').dataset.prompt;
    const shipped = SHIPPED_PROMPTS.find((p) => p.id === id);
    patchQ({ prompts: a().qconsult.prompts.map((p) => (p.id === id ? { ...p, text: t.value, edited: Boolean(shipped && t.value !== shipped.text) } : p)) });
  }

  function onInput(e) {
    if (e.target.dataset.ac !== 'prompt') return false;
    app().setState({ ...app().state, asking: { ...a(), consult: { ...a().consult, prompt: e.target.value } } });
    return true;
  }

  /* A Check is one real, paid turn — so it asks first, and only a yes starts it. */
  function confirmCheck(callerId) {
    const chosen = app().byId(app().state.consultants[callerId] || '');
    if (!chosen) return;
    app().confirm({
      title: `Check ${chosen.name}?`,
      body: `<p>One real, paid turn of <b>${esc(chosen.name)}</b> in a scratch folder — never your code. It costs what one short answer costs on that model.</p>`,
      action: 'Run the check', danger: false, onConfirm: () => runCheck(callerId),
    });
  }

  function runCheck(callerId) {
    const health = (state) => patchConsult({ health: { ...a().consult.health, [callerId]: state } });
    health({ state: 'checking' });
    setTimeout(() => health({ state: 'passed', when: 'just now', secs: 9 }), 1200);
  }

  async function copySnippet() {
    try {
      await navigator.clipboard.writeText(AGY.snippet);
      app().toast('The allow rule is on the clipboard — paste it into agy\'s settings file yourself.');
    } catch {
      app().toast('The clipboard refused — select the rule above and copy it by hand.');
    }
  }

  function addRow() {
    const n = a().qconsult.rows.length + 1;
    const first = app().state.instances.find((i) => i.features.includes('qconsult'));
    patchQ({ rows: [...a().qconsult.rows, { id: `row-${Date.now()}`, instance: first?.id || '', prompt: 'question-opinion', enabled: false }] });
    app().toast(`Row ${n} added, switched off.`);
  }

  function addPrompt() {
    const title = (window.prompt('Its title — the id is made from it:', '') || '').trim();
    if (!title) return;
    const capability = (window.prompt('What it needs of its runtime: none, disk or web', 'none') || 'none').trim();
    const id = title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
    patchQ({ prompts: [...a().qconsult.prompts, { id, title, capability: ['disk', 'web'].includes(capability) ? capability : 'none', shipped: false, text: '' }] });
  }

  /* A refused folder is never stored — the same sentence the server would answer with. */
  function addRoot() {
    const path = (window.prompt('A folder a disk row may read, read-only:', '') || '').trim();
    if (!path) return;
    const refusal = rootRefusal(path, setup().data?.directory);
    if (refusal) { app().toast(`Not added: ${refusal}.`); return; }
    patchQ({ roots: [...a().qconsult.roots, path] });
  }

  const promptIdOf = (t) => t.closest('[data-prompt]').dataset.prompt;

  function restorePrompt(t) {
    const shipped = SHIPPED_PROMPTS.find((p) => p.id === promptIdOf(t));
    if (shipped) patchQ({ prompts: a().qconsult.prompts.map((p) => (p.id === shipped.id ? { ...shipped } : p)) });
  }

  const ACTIONS = {
    'restore-consult': () => patchConsult({ prompt: '' }),
    'add-row': addRow,
    'remove-row': (t) => patchQ({ rows: a().qconsult.rows.filter((x) => x.id !== t.closest('[data-row]').dataset.row) }),
    'restore-prompt': restorePrompt,
    'remove-prompt': (t) => patchQ({ prompts: a().qconsult.prompts.filter((p) => p.id !== promptIdOf(t)) }),
    'add-prompt': addPrompt,
    'add-root': addRoot,
    'remove-root': (t) => patchQ({ roots: a().qconsult.roots.filter((_, n) => n !== Number(t.dataset.n)) }),
  };

  function onClick(e) {
    const c = e.target.closest('[data-ac-check]');
    if (c) { confirmCheck(c.dataset.acCheck); return true; }
    if (e.target.closest('[data-ac-copy]')) { copySnippet(); return true; }
    const t = e.target.closest('[data-ac-action], [data-aq-action]');
    if (!t) return false;
    ACTIONS[t.dataset.acAction || t.dataset.aqAction](t);
    return true;
  }

  const PAGES = { consultant: consultantPage, qconsult: qconsultPage };

  window.COAI_PAGES = {
    ...window.COAI_PAGES,
    consultants: { slice: 'asking', defaults, html: (sub) => (PAGES[sub] || consultantPage)(), onChange, onInput, onClick },
  };
})();
