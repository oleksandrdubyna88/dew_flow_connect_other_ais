/* ConnectOtherAIs settings mockup: the Reviews tab — Stages, Roles & prompts, Prompts per round, The gate,
   Commands, Limits. Owns the `reviews` slice. No model is picked here: each stage shows the instances ticked for it.
   Roles & prompts replaces the separate "Review roles" page, and ONE switch per role replaces the two that
   used to exist (the panel's tick and the roles page's Active). Commands is the extension's "Gate commands"
   page (commandsPage.ts, commands.ts). */
(function () {
  'use strict';

  const app = () => window.COAI_APP;
  const esc = (s) => app().esc(s);
  const help = (text) => app().help(text);
  const r = () => app().state.reviews;
  const checked = (on) => (on ? ' checked' : '');
  const hasText = (s) => String(s ?? '').trim().length > 0;
  const MAX_ACTIVE = 5;

  /* The installed coai-mcp that first reads each control — the extension's own *_SINCE constants. */
  const SINCE = {
    conventions: '0.18.10', // src_vs_code/src/prompts.ts:120 CONVENTIONS_ROLE_SINCE
    roleSwitch: '0.18.13', // src_vs_code/src/prompts.ts:131 ROLE_SWITCH_SINCE
    customRoles: '0.19.0', // src_vs_code/src/rolesPage.ts:449 CUSTOM_ROLES_SINCE
    stopLocal: '0.34.0', // src_vs_code/src/gateScope.ts:41 STOP_LOCAL_SINCE
    gatePer: '0.33.0', // src_vs_code/src/gateScope.ts:18 GATE_PER_SINCE = COMMAND_MODELS_SINCE
    commandModels: '0.33.0', // src_vs_code/src/commandModels.ts:185 COMMAND_MODELS_SINCE
  };

  /* The "?" texts: the extension's HELP (help.ts), shortened to a sentence or two. */
  const H = {
    roleEnabled: 'Whether this role reviews at all. Off, it takes no part in a round; its rounds, threshold and prompt picks are kept and come back unchanged when you switch it on again.',
    planAlways: 'Plan review is the plan stage\'s own role and is always on — the plan gate has no round without it.',
    featureSwitch: 'This switch is the feature gate\'s switch. Off, review_feature records that it did not run and does not block.',
    maxRounds: 'How many times THIS ROLE may be asked before the policy on The gate takes over. When its count is spent it stops being asked, and the stage keeps going for the roles that have not.',
    gateThreshold: 'The gate opens for this role when this many of its findings are left, or fewer. Only blocking and major findings count; zero demands a clean review.',
    dealPlan: 'Off: every vendor answers the same question, and two vendors agreeing is a fact the gate can use. On: every lens is asked once, at half the launches — and that agreement is gone.',
    dealCode: 'Off: every code role is asked of every model. On: the roles are dealt out across the models, each asked of one.',
    codeWorkspace: 'Fast sends the diff, the plan and this project\'s written rules in an empty directory; Full also hands over the checkout. Fast is the default because it was measured to find more, at a half to a third of the tokens.',
    programmingTask: 'A programming task reads code. A result-stage role that is not one is a Document role, run by review_document.',
    stage: 'Which review runs this role. Document and Code fix its kind: a Document role reads a document, a Code role reads the change.',
    addRole: 'A role of your own: a question this product does not ship. It starts switched off — write its first prompt, then switch it on.',
    roundPicks: 'Which prompt each round of this role asks. A round you have not picked asks the role\'s first prompt.',
    onExhausted: 'What happens when the rounds run out and findings still gate: ask you, continue and say so, take what is true and move on, or climb the ladder.',
    stopLocal: 'When every cloud reviewer of a round has answered with at most one remark between them, the local reviewer finishes the launch it is on and starts no more. A round reads this switch when it starts.',
    autonomous: 'The gate tells the assistant not to interrupt you: a question that does not block it is asked at the end, all together; blocking ones are gathered and asked at once.',
    splitPlan: 'After a plan passes, the gate tells the assistant how big it is and how to split it into epics and stories — a heuristic it may disagree with in writing.',
    gatePer: 'How often split work comes back through this gate: once per epic, each on its own branch, or once for the whole task. Never once per story.',
    splitWithFable: 'The split is done by the calling assistant\'s strongest model, and the risky stories go back to it; the ordinary ones run on its implementation model.',
    callerModels: 'The models the split order names, per kind of calling assistant — each names its own vendor\'s models. The first choice in each list is the default.',
    commands: 'The words the gate\'s orders are made of, and commands of your own handed over with them.',
    maxConcurrency: 'How many reviewer processes may run at the same time, across every vendor. Higher finishes a round sooner and loads the machine harder.',
    maxPerProvider: 'Of those, how many may belong to ONE vendor. Rate limits are per vendor, so one throttled vendor cannot hold every slot.',
    reviewerTimeout: 'How long one reviewer may take before its process is killed and the round records a timeout for it.',
    roundTimeout: 'How long a whole round may take before the reviewers still running are cancelled. 0 works it out: the reviewers through the cap above, each wave one reviewer timeout.',
    escalationMinutes: 'How long the gate\'s question waits for your answer before the AI is told to ask you in the chat instead. Nothing is decided by your silence.',
    commandsYours: 'Commands of your own, handed over after the built-in orders in the rounds you choose.',
    commandRounds: 'Which rounds this command is handed over in: every round, plan rounds only, or code rounds only.',
    commandOn: 'Handed over only while it is on — and it can be switched on only once it has text.',
    commandText: 'What this command tells the AI that called the gate, word for word.',
    commandsShipped: 'Write your own words to replace a shipped text. An empty box is the shipped text, shown faintly; Restore deletes yours.',
  };

  const STAGES = [
    { id: 'plan', label: 'Plan stage', short: 'Plan', feature: 'plan',
      hint: 'What review_plan runs, before any code is written. The reviewer gets the plan in an empty folder — nothing to explore.' },
    { id: 'code', label: 'Code stage', short: 'Code', feature: 'code',
      hint: 'What review_code runs on a committed branch: the scope, the diff and this project\'s rules.' },
    { id: 'document', label: 'Document stage', short: 'Document', feature: 'document',
      hint: 'What review_document runs: reviewers that read a DOCUMENT rather than a diff — a specification, a policy, a proposal. Its own session, keyed by the document.' },
    { id: 'feature', label: 'Feature stage', short: 'Feature', feature: 'feature',
      hint: 'What review_feature runs once every epic has landed: the whole plan\'s code, OUTLINED. Its switch is the feature gate\'s switch — off, the gate records that it did not run and does not block.' },
  ];

  const P = (id, label, purpose = '') => ({ id, label, purpose });
  const ROLES = [
    { id: 'PlanCritique', name: 'Plan review', stage: 'plan', rounds: 1, threshold: 6, prompts: [
      P('plan-critique', 'Universal', 'Everything a plan can get wrong'), P('plan-assumptions', 'Assumptions & verification'), P('plan-human-path', 'The human path'),
      P('plan-data-loss', 'Data loss & recovery'), P('plan-operability', 'Operability'), P('plan-scope-creep', 'Scope & budget')] },
    { id: 'Conventions', name: 'Conventions', stage: 'code', rounds: 1, threshold: 5, prompts: [P('conventions', 'Conventions', 'The diff against the rules this project wrote down')] },
    { id: 'Architecture', name: 'Architecture', stage: 'code', rounds: 1, threshold: 5, prompts: [
      P('architecture', 'Universal'), P('arch-boundaries', 'Boundaries & duplication'), P('arch-evolution', 'Cost of the next change'),
      P('arch-coupling', 'Coupling & knowledge'), P('arch-naming', 'Names & the shape they imply'), P('arch-testability', 'Testability of the seams')] },
    { id: 'SecurityReliability', name: 'Security & reliability', stage: 'code', rounds: 1, threshold: 5, prompts: [
      P('security-reliability', 'Universal'), P('sec-memory-leaks', 'What it holds and leaves'), P('sec-attack', 'Attack surface'),
      P('sec-blast-radius', 'Blast radius'), P('sec-concurrency', 'Two at once'), P('sec-supply-chain', 'What this change trusts')] },
    { id: 'UxDxPerformance', name: 'Performance & UX-DX', stage: 'code', rounds: 1, threshold: 5, prompts: [
      P('uxdx-performance', 'Universal'), P('perf-scale', 'Cost at scale'), P('dx-ergonomics', 'Ergonomics & waiting'),
      P('perf-first-run', 'The first run and the empty case'), P('perf-wasted-work', 'Work done twice'), P('ux-undo', 'What cannot be taken back')] },
    { id: 'DocumentReview', name: 'The document', stage: 'document', rounds: 1, threshold: 5, programmingTask: false, prompts: [
      P('document-review', 'Universal'), P('document-decisions', 'Decisions & owners'), P('document-reader', 'The reader who has to act')] },
    { id: 'DocumentSummary', name: 'Summary', stage: 'document', rounds: 1, threshold: 5, programmingTask: false, prompts: [P('document-summary', 'Universal', 'Writes the account of the document')] },
    { id: 'FeatureReview', name: 'The feature', stage: 'feature', rounds: 2, threshold: 5, prompts: [P('feature-review', 'Universal')] },
  ].map((x) => ({ programmingTask: true, ...x, shipped: true }));

  /* What every deployed Team server runs when it has not said which roles it runs (serverRoles.ts BEFORE_THE_CATALOG). */
  const BEFORE_THE_CATALOG = ['plancritique', 'conventions', 'architecture', 'securityreliability', 'uxdxperformance'];

  const EXHAUSTED = [
    { id: 'human', label: 'Ask a human', d: 'The review stops and waits for you.' },
    { id: 'continue', label: 'Continue, and say so', d: 'It proceeds and says out loud what is still open, touching none of it.' },
    { id: 'good_enough', label: 'Good enough — take what\'s true and move on', d: 'The AI applies the findings that are true and useful, rejects the rest with reasons, and moves on.' },
    { id: 'escalate', label: 'Climb the ladder', d: 'Raise the reviewers\' effort, then their model, then the arbiter\'s — and try again.' },
  ];

  const CALLER_MODELS = [
    { id: 'claude', label: 'Claude Code', strongest: 'Fable', implementation: 'Opus', models: ['Fable', 'Opus', 'Sonnet', 'Haiku'] },
    { id: 'codex', label: 'Codex', strongest: '', implementation: '', models: ['gpt-6-sol', 'gpt-6-terra', 'gpt-6-luna', 'gpt-5.5'] },
    { id: 'gemini', label: 'Gemini', strongest: '', implementation: '', models: ['gemini-3.8-pro', 'gemini-3.8-flash'] },
    { id: 'other', label: 'Another client', strongest: '', implementation: '', models: [] },
  ];

  /* ---------- the gate's shipped commands (commands.ts:46-72, texts from commandTexts.generated.ts) ---------- */

  const COMMAND_PREFIX = 'command-';
  const GATE_MARKER = 'THE GATE runs once ';
  const ALREADY_SPLIT_MARKER = 'This plan is a PIECE of a split that is already under way';
  const CONSULT_FILL = ['{load}', '{every}', '{threshold}', '{max}', '{most}', '{plan}', '{enforced}'];
  const C = (id, title, marker = '', placeholders = []) => ({ id, title, marker, placeholders });

  const SHIPPED_COMMANDS = [
    C('preamble', 'The sentence before the orders'),
    C('autonomy', 'Work autonomously', 'Work AUTONOMOUSLY. ', ['{scope}']),
    C('question-consult', 'Ask the consultants first', 'ASK THE CONSULTANTS FIRST. ', ['{freeBatches}', '{enforced}']),
    C('split-none', 'Split: a plan small enough to build as it stands'),
    C('split-small', 'Split: stories only'),
    C('split-medium', 'Split: 2-3 epics'),
    C('split-large', 'Split: 3-4 epics'),
    C('split-huge', 'Split: 4-5 epics'),
    C('split-massive', 'Split: 6-14 epics'),
    C('split-measured', 'Split: what the plan was measured at', '', ['{numbers}', '{verdict}']),
    C('cadence-epic', 'Gate: once per epic', GATE_MARKER),
    C('cadence-task', 'Gate: once for the whole task', GATE_MARKER),
    C('cadence-single', 'Gate: once, for a plan with no epics', GATE_MARKER),
    C('another-code-round', 'Gate: the door to a second code round'),
    C('already-split-epic', 'A piece of a split, gated per epic', ALREADY_SPLIT_MARKER),
    C('already-split-task', 'A piece of a split, gated once for the task', ALREADY_SPLIT_MARKER),
    C('model', 'Split with the strongest model', 'Do the SPLIT itself with ', ['{strongest}', '{implementation}']),
    C('consult-forecast', 'Consult on a cadence: what the plan will owe', 'CONSULT ON A CADENCE. ', ['{owed}', ...CONSULT_FILL]),
    C('consult-group', 'Consult on a cadence: a group of epics is due', 'CONSULT BEFORE YOU BUILD this group of epics. ', ['{range}', '{titles}', '{call}', ...CONSULT_FILL]),
    C('consult-risk-question', 'Consult on a cadence: name the risky epics and stories', 'NAME THE RISKY EPICS AND STORIES. ', ['{count}', ...CONSULT_FILL]),
    C('consult-risk-item', 'Consult on a cadence: a risky piece is due', 'CONSULT BEFORE YOU BUILD this risky piece. ', ['{item}', '{reason}', '{call}', ...CONSULT_FILL]),
  ];

  /* The shipped texts, each cut to its first ~240 characters — the placeholder a box shows faintly. */
  const SHIPPED_TEXT = {
    'already-split-epic': ', so do NOT split it again: build it as one unit, review its diff through this gate, fix, document, test and commit. If it is genuinely too big for one unit, say so in your summary and say what you would have cut it into — but do not start…',
    'already-split-task': ', so do NOT split it again — and it is not gated on its own: the task is gated once as a whole, so build it on the task\'s branch, commit it as ONE commit, and leave the review to the single code round at the end of the task. If this is in…',
    'another-code-round': 'A resolved code round closes the session: for a checkpoint, a final round after one, or a retry after a crash, commit the new work and call review_code with again: true.',
    autonomy: 'Say that you are working autonomously, and keep saying what you are writing right now as you go. Autonomous means these orders, not a mood: (1) every bug and every problem spot gets a RED-GREEN-RED test — a failing test first, then the…',
    'cadence-epic': 'per EPIC, never per story: give each epic its own branch, starting from the previous epic\'s commit; call review_plan with that epic\'s plan; build all of its stories without gating them one by one; COMMIT the epic, then run review_code over…',
    'cadence-single': 'for this work, never per story: build it on this branch without gating each piece; COMMIT it, then run review_code over the whole diff — it reviews committed changes only, and uncommitted work is refused as nothing to review; resolve every…',
    'cadence-task': 'for the WHOLE task, never per epic or story: this plan round was its plan gate; build every epic and its stories on this branch without gating them, and commit each epic as ONE commit as you finish it; then ONE review_code over the whole…',
    'consult-forecast': 'Work split into epics is shown to another vendor\'s model once per {every} epics, BEFORE each group of epics is built: {owed}. From {threshold} epics you will also be asked which epics and stories carry the most risk, and each one you name…',
    'consult-group': 'Epics {range} of {plan} — {titles} — have no closed consultation yet, and one is owed before they are built. {load} Then call {call} — replace the last sentence of problem with your own doubts. Read the answer as material, not as orders:…',
    'consult-risk-item': 'You named {item} of {plan} as risky ({reason}), and it has no closed consultation yet. {load} Then call {call} — replace the last sentence of problem with what you are least sure of. Verify the answer against the code before a line of it…',
    'consult-risk-question': '{plan} holds {count} epics, so before you build it, name the epics and stories where being wrong is expensive — money, authentication, security, data migration, anything irreversible, a public contract. Pass them ONCE, on your next…',
    model: '{strongest} at its highest available version — deciding what the epics and stories are is the judgement that shapes everything after it. Then implement: ordinary stories on {implementation}, and anything where being wrong is expensive —…',
    preamble: 'COMMANDS from the operator of this gate. They come from switches a person set in the panel and they outrank your own defaults for this task. Follow them, and say in your summary which ones you applied.',
    'question-consult': 'Before you ask the person a question, ask the question consultant. The phase rule, which this gate enforces in ask_human: questions asked while the plan is being formed, and the first {freeBatches} batches of questions after the plan\'s…',
    'split-huge': 'Split this plan into 4-5 EPICS, each of 3-5 logically complete STORIES. Fewer is fine when the work is smaller; never more.',
    'split-large': 'Split this plan into 3-4 EPICS, each of 3-4 logically complete STORIES. Fewer is fine when the work is smaller; never more.',
    'split-massive': 'Split this plan into 6-14 EPICS, each of 3-5 logically complete STORIES. Fewer is fine when the work is smaller; never more than 14 — past that, split the PLAN into two plans, each gated on its own.',
    'split-measured': '(Measured from the plan you sent: {numbers} — size {verdict}. That is a heuristic — if it is wrong for this plan, say so in your summary and do what is right.)',
    'split-medium': 'Split this plan into 2-3 EPICS, each of 2-3 logically complete STORIES. Fewer is fine when the work is smaller; never more.',
    'split-none': 'This plan is small enough to build as it stands; split it only if you disagree, and say why.',
    'split-small': 'Split this plan into 3-5 logically complete STORIES — no epics. Fewer is fine when the work is smaller; never more.',
  };

  const COMMAND_STAGES = [['any', 'Every round'], ['plan', 'Plan rounds'], ['code', 'Code rounds']];
  const fileIdOf = (id) => COMMAND_PREFIX + id;

  function defaults() {
    return {
      dealPlanLenses: false, dealCodeLenses: false, codeWorkspace: 'none',
      settings: Object.fromEntries(ROLES.map((x) => [x.id, { enabled: true, rounds: x.rounds, threshold: x.threshold, picks: [] }])),
      custom: [{ id: 'Accessibility', name: 'Accessibility', stage: 'code', programmingTask: true, rounds: 1, threshold: 5,
        prompts: [{ id: 'accessibility', label: 'Universal', purpose: 'Keyboard, screen readers, contrast', text: 'Review the change for what a keyboard-only or screen-reader user can no longer do…' }] }],
      extraPrompts: { Architecture: [{ id: 'arch-migration', label: 'Migrations', purpose: 'Will this survive the data that already exists?', text: '' }] },
      texts: { 'sec-attack': 'Act as an attacker who has read this diff…' },
      reservedIds: [],
      editStage: 'code',
      onExhausted: 'human', stopLocalWhenQuiet: false,
      autonomous: false, splitPlan: false, gatePer: 'epic', splitWithFable: false,
      commandModels: {},
      commands: [{ id: 'run-tests', title: 'Run the tests before you answer', stage: 'code', enabled: true }],
      commandTexts: { 'command-run-tests': 'Run this project\'s test suite before you answer a code round, and say which tests ran and what they reported.' },
      limits: { maxConcurrency: 3, maxPerProvider: 2, reviewerTimeoutMinutes: 10, roundTimeoutMinutes: 0, escalationMinutes: 15 },
    };
  }

  /* ---------- the role catalog: shipped + yours ---------- */

  function allRoles() {
    const shipped = ROLES.map((x) => ({ ...x, prompts: [...x.prompts, ...(r().extraPrompts[x.id] || []).map((p) => ({ ...p, mine: true }))] }));
    const mine = r().custom.map((x) => ({ ...x, shipped: false, prompts: x.prompts.map((p) => ({ ...p, mine: true })) }));
    return [...shipped, ...mine];
  }

  const roleById = (id) => allRoles().find((x) => x.id === id);
  const rolesIn = (stage) => allRoles().filter((x) => x.stage === stage);
  const settingOf = (role) => ({ enabled: true, rounds: role.rounds || 1, threshold: role.threshold ?? 5, picks: [], ...(r().settings[role.id] || {}) });
  const activeIn = (stage) => rolesIn(stage).filter((x) => settingOf(x).enabled);
  const sideWords = () => (app().state.setup?.perSide ? ', for this side of the machine' : '');
  const skewIf = (on, since, what) => (on ? app().skew(since, what) : '');

  /* Why a role has no question to ask (roles.ts whyNotAskable). A shipped role always has one: its shipped text counts. */
  function whyNotAskable(role) {
    if (role.shipped) return '';
    const first = role.prompts[0];
    if (!first) return `“${role.name}” has no prompt, so there is no question to ask it. Add a prompt and write its question.`;
    if (hasText(first.text)) return '';
    return `“${role.name}” has no question to ask: its first prompt “${first.label || first.id}” has no text. Write the question in the box under it.`;
  }

  /* Why a role that is on may not be switched off. The Feature role is exempt: its switch IS the feature gate's. */
  function offBlock(role) {
    if (role.id === 'PlanCritique') return 'Always on: Plan review is the plan stage\'s own role, and the plan gate has no round without it.';
    if (role.stage === 'feature') return '';
    if (activeIn(role.stage).length === 1) return 'The only role still on in this stage — switch another one on first, or the stage would have no reviewer at all.';
    return '';
  }

  /* Why a role that is off may not be switched on (issue #338: not without a question to ask). */
  function onBlock(role) {
    const why = whyNotAskable(role);
    if (why) return why;
    if (activeIn(role.stage).length >= MAX_ACTIVE) return `${MAX_ACTIVE} roles are already on in this stage. Switch one off to make room.`;
    return '';
  }

  /* Why a role's switch may not move, or '' when it may. */
  const switchBlock = (role) => (settingOf(role).enabled ? offBlock(role) : onBlock(role));

  /* ---------- Team servers: which roles a server will not run (panelView.ts serverNotes) ---------- */

  function serverRunsRole(server, roleId) {
    const names = Array.isArray(server.roles) ? server.roles.map((n) => n.toLowerCase()) : BEFORE_THE_CATALOG;
    return names.includes(roleId.toLowerCase());
  }

  function serverNotesFor(role) {
    const { instances, teamServers } = app().state;
    return instances.filter((i) => i.enabled && i.vendor === 'remote' && i.features.includes(role.stage)).flatMap((i) => {
      const server = teamServers.find((s) => s.id === i.server);
      if (!server || serverRunsRole(server, role.id)) return [];
      return [`${i.name} runs on ${server.name}, which does not run ${role.name} — that model skips this role.`];
    });
  }

  const notesHtml = (lines) => lines.map((l) => `<p class="callout warn rv-note"><b>Skipped:</b> ${esc(l)}</p>`).join('');

  /* ---------- small controls ---------- */

  function check(key, label, hint = '', helpText = '') {
    return `<div class="rv-check"><label class="inline"><input type="checkbox" data-rv="${key}"${checked(r()[key])}> ${esc(label)}</label>${helpText ? help(helpText) : ''}</div>
      ${hint ? `<p class="from">${esc(hint)}</p>` : ''}`;
  }

  function seg(key, options, value, label) {
    return `<div class="seg" role="radiogroup" aria-label="${esc(label)}">${options.map(([v, l]) =>
      `<label><input type="radio" name="${key}" value="${esc(v)}" data-rv="${key}"${checked(value === v)}>${esc(l)}</label>`).join('')}</div>`;
  }

  function number(key, label, value, min, helpText, hint = '') {
    return `<div class="field-line"><span class="rv-check"><label for="rv-${key}">${esc(label)}</label>${help(helpText)}</span>
      <input type="number" class="num" id="rv-${key}" data-rv-limit="${key}" min="${min}" value="${esc(value)}">
      ${hint ? `<span class="hint">${esc(hint)}</span>` : ''}</div>`;
  }

  function roleSwitch(role) {
    const block = switchBlock(role);
    return `<input type="checkbox" data-rv-role="${esc(role.id)}" data-k="enabled"${checked(settingOf(role).enabled)}${block ? ` disabled title="${esc(block)}"` : ''}
      aria-label="${esc(role.name)} reviews">`;
  }

  const switchHelp = (role) => (role.id === 'PlanCritique' ? H.planAlways : role.stage === 'feature' ? H.featureSwitch : H.roleEnabled);

  /* "Stays on" / "Stays off" with the reason — the visible half of a disabled switch's title. */
  function blockLine(role) {
    const block = switchBlock(role);
    if (!block) return '';
    return `<p class="from rv-why"><span class="state warn">${settingOf(role).enabled ? 'Stays on' : 'Stays off'}</span> ${esc(block)}</p>`;
  }

  /* An ACTIVE role that will not be asked (rolesPage.ts unaskableHint). */
  function unaskableHint(role) {
    const why = settingOf(role).enabled ? whyNotAskable(role) : '';
    if (!why) return '';
    return `<p class="callout warn rv-note"><b>Skipped:</b> It is switched on, but a round that asks its first prompt will skip it —
      every round, unless another prompt is chosen for it. ${esc(why)}</p>`;
  }

  /* One road for the role-switch skews: the server's own switch, and what this design adds. */
  function switchSkews() {
    const anyOff = allRoles().some((x) => !settingOf(x).enabled);
    return skewIf(anyOff, SINCE.roleSwitch, 'Switching a role off') + app().skew(app().PLANNED, 'One switch per role');
  }

  /* ---------- Stages ---------- */

  function stagesPage() {
    return `<p class="lead">What each review stage runs: its roles, how many rounds each may take, and how many findings
        it may leave and still pass. Which models review is ticked on Models; what each role asks is on Roles &amp; prompts.</p>
      ${switchSkews()}
      <div class="plan-grid">${STAGES.map(stagePanel).join('')}</div>`;
  }

  function stagePanel(stage) {
    return `<section class="panel"><h3>${esc(stage.label)}</h3>
      <p class="from">${esc(stage.hint)}</p>
      ${app().modelsUsedHtml(stage.feature, 'Models')}
      ${stageSwitches(stage.id)}
      ${rolesTable(stage.id)}
      ${notesHtml(rolesIn(stage.id).flatMap(serverNotesFor))}
      ${featureOffNote(stage.id)}
      ${stage.id === 'code' ? conventionsNote() : ''}</section>`;
  }

  function rolesTable(stageId) {
    return `<table class="roles"><thead><tr><th scope="col">Role${help(H.roleEnabled)}</th><th scope="col">Rounds${help(H.maxRounds)}</th>
        <th scope="col">Passes at or under${help(H.gateThreshold)}</th><th scope="col">Prompts</th></tr></thead>
      <tbody>${rolesIn(stageId).map(roleRow).join('')}</tbody></table>`;
  }

  function featureOffNote(stageId) {
    if (stageId !== 'feature' || activeIn('feature').length) return '';
    return '<p class="callout warn"><b>The feature gate is off.</b> review_feature records that it did not run, and does not block.</p>';
  }

  function stageSwitches(stage) {
    if (stage === 'plan') {
      return check('dealPlanLenses', 'Deal the lenses across vendors',
        'Off: every vendor answers the same question, and two agreeing is a fact the gate can use. On: every lens is asked once, at half the launches — and that agreement is gone.', H.dealPlan);
    }
    if (stage !== 'code') return '';
    return `${codeHeading()}
      ${check('dealCodeLenses', 'Deal the roles across vendors', 'Off: every code role is asked of every model. On: the roles are dealt out, each asked of one.', H.dealCode)}
      <div class="row-line"><span class="rv-check"><span>What a reviewer gets</span>${help(H.codeWorkspace)}</span>${seg('codeWorkspace', [['none', 'Fast — diffs only'], ['worktree', 'Full — with the code']], r().codeWorkspace, 'What a reviewer gets')}</div>
      <p class="from">Fast sends the diff, the plan and the project's rules. Measured on one commit: every hosted model found MORE
        that way, at a half to a third of the tokens. Full also hands CLI models the checkout — prompt-only models still get the diff.</p>`;
  }

  function codeHeading() {
    const models = app().state.instances.filter((i) => i.enabled && i.features.includes('code')).length;
    const roles = activeIn('code');
    const rounds = Math.max(1, ...roles.map((x) => settingOf(x).rounds));
    const per = r().dealCodeLenses ? roles.length : models * roles.length;
    return `<p class="callout">${models} model(s) × ${roles.length} role(s) = <b>${per} reviewer run(s) per round</b>, up to ${rounds} round(s).</p>`;
  }

  function conventionsNote() {
    return `<details class="more"><summary>Conventions <span class="what">judges the diff against the rules you wrote down</span></summary>
      <p class="from">A code role of its own, with one prompt: it reads CLAUDE.md, AGENTS.md, GEMINI.md and .claude/rules and nothing
        else, and it is skipped in a repository that wrote none.</p></details>
      ${app().skew(SINCE.conventions, 'The Conventions role')}`;
  }

  function roleRow(role) {
    const s = settingOf(role);
    const off = s.enabled ? '' : ' disabled';
    const block = switchBlock(role);
    return `<tr class="${s.enabled ? '' : 'off'}"><td><label class="inline"${block ? ` title="${esc(block)}"` : ''}>${roleSwitch(role)} ${esc(role.name)}</label>
        ${role.shipped ? '' : ' <span class="kind-mini">yours</span>'}${blockLine(role)}</td>
      <td><input type="number" class="num" min="1" max="6" value="${s.rounds}" data-rv-role="${esc(role.id)}" data-k="rounds"${off} aria-label="${esc(role.name)} rounds"></td>
      <td><input type="number" class="num" min="0" value="${s.threshold}" data-rv-role="${esc(role.id)}" data-k="threshold"${off} aria-label="${esc(role.name)} passes at or under"></td>
      <td><a href="#reviews" data-rv-goto="roles" data-stage="${esc(role.stage)}">${role.prompts.length} — edit</a></td></tr>`;
  }

  /* ---------- Roles & prompts (was the separate "Review roles" page) ---------- */

  function rolesPage() {
    const stage = STAGES.find((s) => s.id === r().editStage) || STAGES[1];
    const tabs = STAGES.map((s) => [s.id, `${s.short} · ${rolesIn(s.id).length}`]);
    return `<p class="lead">The question each reviewer asks. Everything here is saved as you type${sideWords()}. A role is a subject;
        its prompts are the questions it can ask, one per round. Shipped roles keep their name and stage; their switch and their prompt text are yours.</p>
      ${switchSkews()}${skewIf(r().custom.length > 0, SINCE.customRoles, 'Running the roles you added')}
      <div class="row-line">${seg('editStage', tabs, stage.id, 'Which stage\'s roles to edit')}
        <span class="from">${activeIn(stage.id).length} of at most ${MAX_ACTIVE} on in this stage.</span>
        <span class="spacer"></span>
        <span class="rv-check"><button type="button" class="btn small" data-rv-action="add-role">＋ Add a role</button>${help(H.addRole)}</span></div>
      <p class="from">${esc(stage.hint)}</p>
      <div class="prompt-cards">${rolesIn(stage.id).map(roleCard).join('')}</div>
      <p class="from" style="margin-top: 10px">Green — shipped · orange — shipped text you replaced · purple — yours.
        A shipped prompt shows no text here: its words are compiled into the server, and what you type replaces them.</p>`;
  }

  function roleCard(role) {
    const s = settingOf(role);
    return `<div class="pcard ${role.shipped ? 'shipped' : 'mine'}" data-role="${esc(role.id)}">
      ${roleHead(role)}
      ${roleControls(role)}
      ${roleNotes(role)}
      <div class="prompt-list">${role.prompts.map((p, n) => promptItem(role, p, n)).join('')}</div>
      <div class="foot"><button type="button" class="btn small" data-rv-action="add-prompt">＋ Add a prompt</button>
        ${role.shipped ? '' : '<button type="button" class="btn small ghost danger" data-rv-action="remove-role">Remove this role</button>'}
        <span class="from">${s.enabled ? `on · asked ${s.rounds} round(s)` : 'off'}</span></div></div>`;
  }

  function roleHead(role) {
    const name = role.shipped ? esc(role.name)
      : `<input type="text" class="name" data-rv-custom="${esc(role.id)}" data-k="name" value="${esc(role.name)}" aria-label="Role name">`;
    return `<h4>${name} <span class="kind">${role.shipped ? 'shipped' : 'yours'}</span> <code class="from">${esc(role.id)}</code></h4>`;
  }

  function roleControls(role) {
    const block = switchBlock(role);
    const lock = kindLock(role);
    return `<div class="row-line">
      <span class="rv-check"><label class="inline"${block ? ` title="${esc(block)}"` : ''}>${roleSwitch(role)} On</label>${help(switchHelp(role))}</span>
      <span class="rv-check">${stageSelect(role)}${help(H.stage)}</span>
      <span class="rv-check"><label class="inline"${lock ? ` title="${esc(lock)}"` : ''}><input type="checkbox" data-rv-custom="${esc(role.id)}" data-k="programmingTask"${checked(role.programmingTask)}${lock ? ` disabled title="${esc(lock)}"` : ''}> A programming task</label>${help(H.programmingTask)}</span></div>`;
  }

  function stageSelect(role) {
    const opts = STAGES.map((x) => `<option value="${x.id}"${x.id === role.stage ? ' selected' : ''}>${esc(x.short)} review</option>`).join('');
    return `<select data-rv-custom="${esc(role.id)}" data-k="stage"${role.shipped ? ' disabled title="Shipped: its stage is fixed."' : ''} aria-label="Stage">${opts}</select>`;
  }

  /* Why "A programming task" cannot move: the stage decides it. A result-stage role that is not one IS a Document role. */
  function kindLock(role) {
    if (role.shipped) return 'Shipped: its kind is fixed.';
    if (role.stage === 'document') return 'A Document role is not a programming task: it reads a document, not code. Move it to Code to make it one.';
    if (role.stage === 'code') return 'A Code role is a programming task: it reads the change. Move it to Document for a role that reads a document.';
    return '';
  }

  /* What a role's KIND means where the answer is surprising (rolesPage.ts kindHint). */
  function kindHint(role) {
    if (role.programmingTask) return '';
    if (role.stage === 'plan') return '<p class="from">A plan-stage role that is not a programming task is kept and takes part in no round yet — there is no plan gate for non-programming work. Move it to Document and review_document will run it.</p>';
    if (role.stage === 'feature') return '<p class="from">A feature-stage role that is not a programming task is kept and takes part in no round — a feature review reads code, outlined. Mark it a programming task and review_feature will run it.</p>';
    return '';
  }

  function roleNotes(role) {
    const featureOff = role.stage === 'feature' && !settingOf(role).enabled
      ? '<p class="from"><span class="state warn">Off</span> The feature gate does not run: review_feature records that it did not run, and does not block.</p>' : '';
    return [blockLine(role), unaskableHint(role), featureOff, kindHint(role), notesHtml(serverNotesFor(role)),
      role.shipped ? '<p class="from">Shipped: its name, stage and kind are fixed — they key your settings and every round already recorded.</p>' : ''].join('');
  }

  function promptItem(role, p, n) {
    const shippedPrompt = !p.mine;
    const text = shippedPrompt ? (r().texts[p.id] || '') : (p.text || '');
    const kind = !shippedPrompt ? 'mine' : text ? 'edited' : 'shipped';
    const ro = shippedPrompt ? ' readonly' : '';
    const missing = !shippedPrompt && !hasText(text) ? '<p class="from"><span class="state err">No text yet</span> — a round that picks it is skipped.</p>' : '';
    return `<div class="prompt-item ${kind}" data-prompt="${esc(p.id)}">
      <div class="row-line"><span class="dot-kind" title="${kind}"></span>
        <input type="text" data-rv-prompt="label" value="${esc(p.label)}" placeholder="What the picker shows"${ro} style="flex: 1" aria-label="Label">
        <input type="text" data-rv-prompt="purpose" value="${esc(p.purpose || '')}" placeholder="The picker's tooltip"${ro} style="flex: 2" aria-label="Purpose">
        ${shippedPrompt ? `<button type="button" class="btn small ghost" data-rv-action="restore-prompt"${text ? '' : ' disabled'}>Restore</button>`
          : '<button type="button" class="icon-btn" data-rv-action="remove-prompt" title="Remove this prompt">✕</button>'}</div>
      <textarea rows="2" data-rv-prompt="text" placeholder="${shippedPrompt ? 'The text this product ships. Write here to replace it.' : 'The question this prompt asks.'}"
        aria-label="${esc(p.label)} text">${esc(text)}</textarea>${missing}${n === 0 ? '<span class="from">The first prompt is what a round you have not picked asks.</span>' : ''}</div>`;
  }

  /* ---------- Prompts per round ---------- */

  function promptsPage() {
    return `<p class="lead">Which question each round of each role asks. A round you have not picked asks the role's
        first prompt. The questions themselves are on Roles &amp; prompts.</p>
      <div class="plan-grid">${STAGES.map(promptsPanel).join('')}</div>`;
  }

  function promptsPanel(stage) {
    return `<section class="panel"><h3>${esc(stage.label)}</h3>${rolesIn(stage.id).map(rolePicks).join('')}</section>`;
  }

  function rolePicks(role) {
    const s = settingOf(role);
    const title = `<h4 class="block-title">${esc(role.name)}${help(H.roundPicks)}</h4>`;
    if (!s.enabled) return `<div class="block">${title}<p class="from">Off — switch it on in Stages.</p></div>`;
    const rows = Array.from({ length: Math.min(s.rounds, 6) }, (_, n) => {
      const pick = s.picks[n] || role.prompts[0].id;
      const opts = role.prompts.map((p) => `<option value="${esc(p.id)}"${p.id === pick ? ' selected' : ''} title="${esc(p.purpose || '')}">${esc(p.label)}${p.mine ? ' (yours)' : ''}</option>`).join('');
      return `<label><span>Round ${n + 1}</span><select data-rv-pick="${esc(role.id)}" data-n="${n}"${role.prompts.length === 1 ? ' disabled' : ''}>${opts}</select></label>`;
    }).join('');
    return `<div class="block">${title}<div class="round-picks">${rows}</div></div>`;
  }

  /* ---------- The gate ---------- */

  function gatePage() {
    return `<p class="lead">What the gate does when a stage runs out of rounds, and the orders it hands back to the AI that
        called it.</p>
      <div class="plan-grid">${exhaustedPanel()}${ordersPanel()}${callerModelsPanel()}${commandsPanel()}</div>`;
  }

  function exhaustedPanel() {
    const choices = EXHAUSTED.map((x) => `<label class="choice"><input type="radio" name="onExhausted" value="${x.id}" data-rv="onExhausted"${checked(r().onExhausted === x.id)}>
      <span>${esc(x.label)}</span><span class="d">${esc(x.d)}</span></label>`).join('');
    return `<section class="panel"><h3>When the rounds run out${help(H.onExhausted)}</h3><div class="choices" role="radiogroup" aria-label="When the rounds run out">${choices}</div>
      <div class="block">${check('stopLocalWhenQuiet', 'Stop the local reviewer when the cloud reviewers found almost nothing',
        'Takes effect from the next round — a round already running keeps the setting it started with.', H.stopLocal)}
        ${skewIf(r().stopLocalWhenQuiet, SINCE.stopLocal, 'Stopping the local reviewer when the cloud found little')}</div></section>`;
  }

  function ordersPanel() {
    return `<section class="panel"><h3>Orders to the calling AI</h3>
      <p class="from">These do not change what the gate DECIDES. They are orders handed back to the AI that called it. All are off
        unless you turn them on.</p>
      <div class="choices">
        ${check('autonomous', 'Work autonomously — batch the questions, do not interrupt me', '', H.autonomous)}
        ${check('splitPlan', 'Split the plan into epics and stories', '', H.splitPlan)}
        <div class="row-line"><span class="rv-check"><span>Split work is gated</span>${help(H.gatePer)}</span>${seg('gatePer', [['epic', 'One gate per epic'], ['task', 'One gate for the whole task']], r().gatePer, 'How often split work is gated')}</div>
        ${skewIf(r().splitPlan, SINCE.gatePer, 'Gating split work once per epic or once per task')}
        ${check('splitWithFable', 'Split with the strongest model, and give it the risky stories', '', H.splitWithFable)}
      </div></section>`;
  }

  function callerModelsPanel() {
    const rows = CALLER_MODELS.map((c) => {
      const saved = r().commandModels[c.id] || {};
      return `<tr><td>${esc(c.label)}</td><td>${callerSelect(c, 'strongest', saved)}</td><td>${callerSelect(c, 'implementation', saved)}</td></tr>`;
    }).join('');
    const named = Object.values(r().commandModels).some((m) => m.strongest || m.implementation);
    return `<section class="panel"><h3>Models the order names${help(H.callerModels)}</h3>
      <p class="from">The order is carried out by the AI that called, so each names ITS OWN models. These are not instances —
        nothing here runs on the Models tab.</p>
      ${skewIf(named, SINCE.commandModels, 'The models the order names per caller')}
      <table class="roles"><thead><tr><th scope="col">Caller</th><th scope="col">Strongest</th><th scope="col">Implementation</th></tr></thead>
      <tbody>${rows}</tbody></table></section>`;
  }

  function callerSelect(c, which, saved) {
    const shipped = c[which] || (which === 'strongest' ? 'its strongest' : 'its usual');
    const value = saved[which] || '';
    const list = [...new Set([...(value ? [value] : []), ...c.models])];
    const opts = [`<option value="">default — ${esc(shipped)}</option>`, ...list.map((m) =>
      `<option${m === value ? ' selected' : ''}>${esc(m)}</option>`), '<option value="__other__">another model…</option>'].join('');
    return `<select data-rv-caller="${c.id}" data-k="${which}" aria-label="${esc(c.label)} ${which}">${opts}</select>`;
  }

  function commandsPanel() {
    const mine = r().commands.length;
    return `<section class="panel"><h3>Commands${help(H.commands)}</h3>
      <p class="from">The words those orders are made of, and commands of your own to hand over with them.</p>
      <p class="from">${SHIPPED_COMMANDS.length} shipped texts · ${mine} of your own.</p>
      <button type="button" class="btn small" data-rv-action="edit-commands">Edit commands…</button></section>`;
  }

  /* ---------- Commands (the extension's "Gate commands" page) ---------- */

  function commandsPage() {
    return `<p class="lead">The orders the gate hands the AI that called it. Everything here is saved as you type${sideWords()}.</p>
      ${app().skew(SINCE.commandModels, 'Reading your command texts and commands')}
      ${yoursSection()}
      ${shippedSection()}`;
  }

  function yoursSection() {
    const rows = r().commands;
    const cards = rows.length ? `<div class="prompt-cards">${rows.map(commandCard).join('')}</div>`
      : '<p class="empty-state">No commands of your own yet.</p>';
    return `<h3 class="section-title">Yours${help(H.commandsYours)}</h3>
      <p class="from">Commands you add are given after the built-in orders, in the rounds you choose. One is switched on only once it has text.</p>
      ${cards}
      <div class="row-line"><button type="button" class="btn small" data-rv-action="add-command">＋ Add a command</button></div>`;
  }

  /* Why a command may not be switched on (commands.ts whyNotGivable). */
  const whyNotGivable = (row, text) => (hasText(text) ? '' : `"${row.title}" has no text yet — write what it tells the AI first, then switch it on.`);

  function commandCard(row) {
    const text = r().commandTexts[fileIdOf(row.id)] || '';
    const block = row.enabled ? '' : whyNotGivable(row, text);
    const stageLabel = (COMMAND_STAGES.find(([v]) => v === row.stage) || COMMAND_STAGES[0])[1];
    return `<div class="pcard mine" data-cmd="${esc(row.id)}">
      <h4><input type="text" class="name" data-rv-cmd="${esc(row.id)}" data-k="title" value="${esc(row.title)}" aria-label="Title"> <span class="kind">yours</span></h4>
      ${commandControls(row, block)}
      ${block ? `<p class="from rv-why"><span class="state warn">Stays off</span> ${esc(block)}</p>` : ''}
      ${row.enabled && !hasText(text) ? '<p class="callout warn rv-note"><b>Skipped:</b> It is on, but has no text — the server skips it until it has.</p>' : ''}
      <div class="rv-check"><span>What it tells the AI</span>${help(H.commandText)}</div>
      <textarea rows="3" data-rv-cmd-text="${esc(fileIdOf(row.id))}" aria-label="What it tells the AI">${esc(text)}</textarea>
      <div class="foot"><button type="button" class="btn small ghost danger" data-rv-action="remove-command">Remove</button>
        <span class="from">${row.enabled ? 'on' : 'off'} · ${esc(stageLabel)}</span></div></div>`;
  }

  function commandControls(row, block) {
    const opts = COMMAND_STAGES.map(([v, l]) => `<option value="${v}"${v === row.stage ? ' selected' : ''}>${esc(l)}</option>`).join('');
    return `<div class="row-line">
      <span class="rv-check"><label class="inline">Rounds <select data-rv-cmd="${esc(row.id)}" data-k="stage" aria-label="Rounds">${opts}</select></label>${help(H.commandRounds)}</span>
      <span class="rv-check"><label class="inline"${block ? ` title="${esc(block)}"` : ''}><input type="checkbox" data-rv-cmd="${esc(row.id)}" data-k="enabled"${checked(row.enabled)}${block ? ` disabled title="${esc(block)}"` : ''}> On</label>${help(H.commandOn)}</span></div>`;
  }

  function shippedSection() {
    return `<h3 class="section-title">Shipped${help(H.commandsShipped)}</h3>
      <p class="from">The words the built-in orders are made of. Write your own to replace them; an empty box is the shipped text,
        shown faintly. The words in <b>bold</b> before a box are kept by the server — they are how an order is recognised.</p>
      <div class="prompt-cards">${SHIPPED_COMMANDS.map(shippedCommandCard).join('')}</div>`;
  }

  function shippedCommandCard(one) {
    const fileId = fileIdOf(one.id);
    const raw = r().commandTexts[fileId] || '';
    const written = hasText(raw) ? raw : '';
    return `<div class="pcard ${written ? 'edited' : 'shipped'}" data-cmd-file="${esc(fileId)}">
      <h4>${esc(one.title)} <span class="kind">${written ? 'yours' : 'shipped'}</span></h4>
      ${placeholderNote(one)}${markerLine(one.marker)}
      <textarea rows="4" data-rv-cmd-text="${esc(fileId)}" aria-label="${esc(one.title)}" placeholder="${esc(SHIPPED_TEXT[one.id] || '')}">${esc(written)}</textarea>
      <div class="foot">${written ? '<button type="button" class="btn small ghost" data-rv-action="restore-command">Restore the shipped text</button>'
        : '<span class="from">The shipped text — write in the box to replace it.</span>'}</div></div>`;
  }

  function markerLine(marker) {
    if (!marker) return '';
    return `<p class="from rv-marker"><b>${esc(marker.trim())}</b>… <span>kept by the server — it is how an order is recognised</span></p>`;
  }

  function placeholderNote(one) {
    if (!one.placeholders.length) return '';
    return `<p class="from">The server fills in ${one.placeholders.map((p) => `<code>${esc(p)}</code>`).join(', ')}.</p>`;
  }

  /* ---------- Limits ---------- */

  function limitsPage() {
    const l = r().limits;
    return `<p class="lead">How much runs at once, and how long anything may take.</p>
      <div class="plan-grid">
        <section class="panel"><h3>Reviewers at once</h3>
          ${number('maxConcurrency', 'Reviewers at once', l.maxConcurrency, 1, H.maxConcurrency, 'Reviewer processes running at the same time, across every model.')}
          ${number('maxPerProvider', 'Per vendor', l.maxPerProvider, 1, H.maxPerProvider, 'Of those, how many may belong to one vendor — rate limits are per vendor.')}
          ${number('reviewerTimeoutMinutes', 'Reviewer timeout, minutes', l.reviewerTimeoutMinutes, 1, H.reviewerTimeout, 'The default for one reviewer before its process is killed. A model may set its own on its card.')}
        </section>
        <section class="panel"><h3>Rounds and waiting</h3>
          ${number('roundTimeoutMinutes', 'Round limit, minutes', l.roundTimeoutMinutes, 0, H.roundTimeout, roundNote(l))}
          ${number('escalationMinutes', 'Wait for you, minutes', l.escalationMinutes, 1, H.escalationMinutes, 'How long the gate\'s question waits for your answer before the AI is told to ask you in the chat.')}
        </section>
      </div>`;
  }

  function roundNote(l) {
    if (app().state.lanes?.security?.enabled && !l.roundTimeoutMinutes) return 'Worked out by the server from ordinary reviewers, security pairs and serial engine queues.';
    if (l.roundTimeoutMinutes && l.roundTimeoutMinutes < l.reviewerTimeoutMinutes) return `Shorter than one reviewer's ${l.reviewerTimeoutMinutes} min — reviewers will be cut off.`;
    if (l.roundTimeoutMinutes) return 'Set by hand.';
    const models = app().state.instances.filter((i) => i.enabled && i.features.includes('code')).length;
    const roles = activeIn('code').length;
    const waves = Math.max(1, Math.ceil((r().dealCodeLenses ? roles : models * roles) / l.maxConcurrency));
    return `0 = worked out: at most ${waves} wave(s) × ${l.reviewerTimeoutMinutes} min = ${waves * l.reviewerTimeoutMinutes} min.`;
  }

  /* ---------- events: roles ---------- */

  function patchSetting(id, change) {
    app().patch('reviews', { settings: { ...r().settings, [id]: { ...settingOf(roleById(id)), ...change } } });
  }

  /* A switch that may not move is put back and says why — a refusal redraws, as the extension's does. */
  function switchRole(id, on) {
    const role = roleById(id);
    const block = settingOf(role).enabled === on ? '' : switchBlock(role);
    if (block) { app().toast(block); app().render(); return; }
    patchSetting(id, { enabled: on });
  }

  const isCustom = (id) => r().custom.some((x) => x.id === id);

  function patchCustomRole(id, change) {
    app().patch('reviews', { custom: r().custom.map((x) => (x.id === id ? { ...x, ...change } : x)) });
  }

  /* Why a role may not move to another stage: it would leave its own stage with nobody on. */
  function moveBlock(role) {
    return settingOf(role).enabled && role.stage !== 'feature' && activeIn(role.stage).length === 1
      ? 'The only role still on in its stage — switch another one on there first.' : '';
  }

  /* Document forces "A programming task" off, Code forces it on; a role moved into a full stage arrives switched off. */
  function restage(id, stage) {
    const role = roleById(id);
    const block = moveBlock(role);
    if (block) { app().toast(block); app().render(); return; }
    const programmingTask = stage === 'document' ? false : stage === 'code' ? true : role.programmingTask;
    const full = settingOf(role).enabled && activeIn(stage).length >= MAX_ACTIVE;
    const settings = full ? { ...r().settings, [id]: { ...settingOf(role), enabled: false } } : r().settings;
    app().patch('reviews', { custom: r().custom.map((x) => (x.id === id ? { ...x, stage, programmingTask } : x)), settings, editStage: stage });
    if (full) app().toast(`${role.name} moved, switched off — ${MAX_ACTIVE} roles are already on there.`);
  }

  function customChange(t) {
    if (t.dataset.k === 'stage') return restage(t.dataset.rvCustom, t.value);
    patchCustomRole(t.dataset.rvCustom, { [t.dataset.k]: t.type === 'checkbox' ? t.checked : t.value });
  }

  /* A prompt's text lives in `texts` for a shipped prompt and on the prompt itself for yours. */
  function patchPrompt(roleId, promptId, change) {
    const prompt = roleById(roleId).prompts.find((p) => p.id === promptId);
    if (!prompt.mine) {
      if ('text' in change) app().patch('reviews', { texts: { ...r().texts, [promptId]: change.text } });
      return;
    }
    const edit = (list) => list.map((p) => (p.id === promptId ? { ...p, ...change } : p));
    if (isCustom(roleId)) return patchCustomRole(roleId, { prompts: edit(r().custom.find((x) => x.id === roleId).prompts) });
    app().patch('reviews', { extraPrompts: { ...r().extraPrompts, [roleId]: edit(r().extraPrompts[roleId] || []) } });
  }

  function roleFieldChange(t) {
    const k = t.dataset.k;
    if (k === 'enabled') return switchRole(t.dataset.rvRole, t.checked);
    const v = Math.max(Number(t.min || 0), Number(t.value) || 0);
    patchSetting(t.dataset.rvRole, { [k]: k === 'rounds' ? Math.min(6, v) : v });
  }

  function pickChange(t) {
    const role = roleById(t.dataset.rvPick);
    const picks = [...settingOf(role).picks];
    picks[Number(t.dataset.n)] = t.value;
    patchSetting(role.id, { picks });
  }

  /* ---------- events: commands ---------- */

  function patchCommand(id, change) {
    app().patch('reviews', { commands: r().commands.map((x) => (x.id === id ? { ...x, ...change } : x)) });
  }

  function commandChange(t) {
    const row = r().commands.find((x) => x.id === t.dataset.rvCmd);
    const k = t.dataset.k;
    if (k === 'title') return patchCommand(row.id, { title: t.value.trim() || row.id });
    if (k === 'stage') return patchCommand(row.id, { stage: t.value });
    const why = t.checked ? whyNotGivable(row, r().commandTexts[fileIdOf(row.id)]) : '';
    if (why) { app().toast(why); app().render(); return; }
    patchCommand(row.id, { enabled: t.checked });
  }

  const commandTextChange = (t) => app().patch('reviews', { commandTexts: { ...r().commandTexts, [t.dataset.rvCmdText]: t.value } });

  function callerChange(t) {
    let value = t.value;
    if (value === '__other__') value = (window.prompt('The model name the order should use:', '') || '').trim();
    const saved = r().commandModels[t.dataset.rvCaller] || {};
    app().patch('reviews', { commandModels: { ...r().commandModels, [t.dataset.rvCaller]: { ...saved, [t.dataset.k]: value } } });
  }

  function limitChange(t) {
    app().patch('reviews', { limits: { ...r().limits, [t.dataset.rvLimit]: Math.max(Number(t.min), Number(t.value) || 0) } });
  }

  function promptChange(t) {
    patchPrompt(t.closest('[data-role]').dataset.role, t.closest('[data-prompt]').dataset.prompt, { [t.dataset.rvPrompt]: t.value });
  }

  /* The first data-* attribute a changed control carries decides who handles it. */
  const CHANGES = [
    ['rv', (t) => app().patch('reviews', { [t.dataset.rv]: t.type === 'checkbox' ? t.checked : t.value })],
    ['rvRole', roleFieldChange], ['rvPick', pickChange], ['rvCustom', customChange], ['rvPrompt', promptChange],
    ['rvCaller', callerChange], ['rvLimit', limitChange], ['rvCmd', commandChange], ['rvCmdText', commandTextChange],
  ];

  function onChange(e) {
    const hit = CHANGES.find(([key]) => e.target.dataset[key] !== undefined);
    if (!hit) return false;
    hit[1](e.target);
    return true;
  }

  /* ---------- actions ---------- */

  const slug = (s) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  const roleIdOf = (t) => t.closest('[data-role]').dataset.role;

  /* Why a new role may not take this id: in use, or reserved by a role removed earlier. */
  function idTaken(id) {
    if (allRoles().some((x) => x.id === id)) return `A role ${id} exists already.`;
    if (r().reservedIds.includes(id)) return `The id ${id} belonged to a role you removed. It stays reserved, so rounds recorded under it keep their name — choose another name.`;
    return '';
  }

  /* A role you add starts OFF: it has no question to ask yet (issue #338). */
  function addRole() {
    const name = (window.prompt('What is this role called?', '') || '').trim();
    if (!name) return;
    const id = name.replace(/[^A-Za-z0-9]+/g, '') || `Role${r().custom.length + 1}`;
    const taken = idTaken(id);
    if (taken) { app().toast(taken); return; }
    const stage = r().editStage;
    const role = { id, name, stage, programmingTask: stage !== 'document', rounds: 1, threshold: 5, prompts: [{ id: slug(name), label: 'Universal', purpose: '', text: '' }] };
    app().patch('reviews', { custom: [...r().custom, role], settings: { ...r().settings, [id]: { enabled: false, rounds: 1, threshold: 5, picks: [] } } });
    app().toast(`${name} added, switched off — write its first prompt's question, then switch it on.`);
  }

  function removeRole(t) {
    const role = roleById(roleIdOf(t));
    const block = moveBlock(role);
    if (block) { app().toast(block.replace('first', 'before removing this one')); return; }
    app().confirm({
      title: `Remove the role “${role.name}”?`,
      body: `<p>Its prompts and everything you wrote in them are deleted. Its per-round picks and its settings — its switch,
          rounds and threshold — go with it.</p>
        <p>Its id <code>${esc(role.id)}</code> stays reserved: rounds already recorded under it keep their name and their
          findings, and a new role cannot take the same id.</p>`,
      action: 'Remove', danger: true, onConfirm: () => dropRole(role.id),
    });
  }

  function dropRole(id) {
    const { [id]: gone, ...settings } = r().settings;
    app().patch('reviews', { custom: r().custom.filter((x) => x.id !== id), settings, reservedIds: [...new Set([...r().reservedIds, id])] });
  }

  function addPrompt(t) {
    const roleId = roleIdOf(t);
    const label = (window.prompt('What should the picker call this prompt?', '') || '').trim();
    if (!label) return;
    const prompt = { id: `${slug(roleId)}-${slug(label)}`, label, purpose: '', text: '' };
    if (isCustom(roleId)) return patchCustomRole(roleId, { prompts: [...r().custom.find((x) => x.id === roleId).prompts, prompt] });
    app().patch('reviews', { extraPrompts: { ...r().extraPrompts, [roleId]: [...(r().extraPrompts[roleId] || []), prompt] } });
  }

  function removePrompt(t) {
    const roleId = roleIdOf(t);
    const promptId = t.closest('[data-prompt]').dataset.prompt;
    if (isCustom(roleId)) {
      const prompts = r().custom.find((x) => x.id === roleId).prompts.filter((p) => p.id !== promptId);
      if (!prompts.length) { app().toast('A role keeps at least one prompt — remove the role instead.'); return; }
      return patchCustomRole(roleId, { prompts });
    }
    app().patch('reviews', { extraPrompts: { ...r().extraPrompts, [roleId]: (r().extraPrompts[roleId] || []).filter((p) => p.id !== promptId) } });
  }

  function restorePrompt(t) {
    const { [t.closest('[data-prompt]').dataset.prompt]: gone, ...rest } = r().texts;
    app().patch('reviews', { texts: rest });
  }

  function uniqueCommandId(base) {
    const taken = new Set([...r().commands.map((x) => x.id), ...SHIPPED_COMMANDS.map((x) => x.id)]);
    let id = base || 'command';
    for (let n = 2; taken.has(id); n += 1) id = `${base || 'command'}-${n}`;
    return id;
  }

  /* A command you add starts off: it is switched on only once it has text. */
  function addCommand() {
    const title = (window.prompt('What is this command called?', '') || '').trim();
    if (!title) return;
    const row = { id: uniqueCommandId(slug(title)), title, stage: 'any', enabled: false };
    app().patch('reviews', { commands: [...r().commands, row] });
    app().toast(`${title} added, switched off — write what it tells the AI, then switch it on.`);
  }

  function removeCommand(t) {
    const row = r().commands.find((x) => x.id === t.closest('[data-cmd]').dataset.cmd);
    app().confirm({
      title: `Remove the command “${row.title}”?`,
      body: '<p>Its text is deleted with it, and it is no longer handed over in any round.</p>',
      action: 'Remove', danger: true,
      onConfirm: () => {
        const { [fileIdOf(row.id)]: gone, ...commandTexts } = r().commandTexts;
        app().patch('reviews', { commands: r().commands.filter((x) => x.id !== row.id), commandTexts });
      },
    });
  }

  /* Restore deletes what you wrote, so it asks first. */
  function restoreCommand(t) {
    const fileId = t.closest('[data-cmd-file]').dataset.cmdFile;
    const one = SHIPPED_COMMANDS.find((x) => fileIdOf(x.id) === fileId);
    app().confirm({
      title: `Restore the shipped text of “${one.title}”?`,
      body: '<p>What you wrote in this box is deleted, and the gate gives the shipped words again.</p>',
      action: 'Restore', danger: true,
      onConfirm: () => {
        const { [fileId]: gone, ...commandTexts } = r().commandTexts;
        app().patch('reviews', { commandTexts });
      },
    });
  }

  function editCommands() {
    const tab = document.querySelector('[data-sub="commands"]');
    if (tab) tab.click();
    else app().toast('The Commands page is not in this build of the mockup yet.');
  }

  const ACTIONS = {
    'add-role': addRole, 'remove-role': removeRole, 'add-prompt': addPrompt, 'remove-prompt': removePrompt,
    'restore-prompt': restorePrompt, 'edit-commands': editCommands,
    'add-command': addCommand, 'remove-command': removeCommand, 'restore-command': restoreCommand,
  };

  function onClick(e) {
    const g = e.target.closest('[data-rv-goto]');
    if (g) {
      e.preventDefault();
      app().patch('reviews', { editStage: g.dataset.stage });
      document.querySelector(`[data-sub="${g.dataset.rvGoto}"]`)?.click();
      return true;
    }
    const t = e.target.closest('[data-rv-action]');
    if (!t) return false;
    ACTIONS[t.dataset.rvAction]?.(t);
    return true;
  }

  /* Typing keeps focus: a shipped prompt's text and every command text are saved without a repaint;
     the card's colour, badge and switch catch up on the change that follows. */
  function onInput(e) {
    const t = e.target;
    if (t.dataset.rvCmdText !== undefined) {
      app().setState({ ...app().state, reviews: { ...r(), commandTexts: { ...r().commandTexts, [t.dataset.rvCmdText]: t.value } } });
      return true;
    }
    if (t.dataset.rvPrompt !== 'text') return false;
    const promptId = t.closest('[data-prompt]').dataset.prompt;
    const mine = roleById(roleIdOf(t)).prompts.find((p) => p.id === promptId).mine;
    if (!mine) app().setState({ ...app().state, reviews: { ...r(), texts: { ...r().texts, [promptId]: t.value } } });
    return !mine;
  }

  const PAGES = { stages: stagesPage, roles: rolesPage, prompts: promptsPage, gate: gatePage, commands: commandsPage, limits: limitsPage };

  window.COAI_PAGES = {
    ...window.COAI_PAGES,
    reviews: { slice: 'reviews', defaults, html: (sub) => (PAGES[sub] || stagesPage)(), onChange, onClick, onInput },
  };
})();
