/* ConnectOtherAIs settings mockup: catalog data and the availability rules.
   A static mockup. Nothing here talks to coai-mcp; every value is demo data shaped like the real
   settings so the operator can judge the layout. Plain script (no modules): the page must open from
   file:// with a double click, and browsers refuse module imports there. */
(function () {
  'use strict';

  /* The features an instance can be ticked for, in the order the card draws them. */
  const FEATURE_GROUPS = [
    { id: 'gates', label: 'Review gates' },
    { id: 'ask', label: 'Asking another model' },
    { id: 'other', label: 'Other' },
  ];

  const FEATURES = [
    { id: 'plan', group: 'gates', label: 'Plan review' },
    { id: 'code', group: 'gates', label: 'Code review' },
    { id: 'document', group: 'gates', label: 'Document review' },
    { id: 'feature', group: 'gates', label: 'Feature review' },
    { id: 'security', group: 'gates', label: 'Security lane' },
    { id: 'consultant', group: 'ask', label: 'Consultant' },
    { id: 'qconsult', group: 'ask', label: 'Question consultant' },
    { id: 'chat', group: 'other', label: 'Chat' },
    { id: 'bugz', group: 'other', label: 'Bugz ranking' },
  ];

  /* How an instance is reached. The standing comes from shared/runtime-capabilities.json, measured
     2026-10-01: it is a property of the RUNTIME, not of where the key is kept — DeepSeek through the
     Codex CLI holds a key and still has a shell on this machine. */
  const ACCESS = {
    'cli-confined': {
      kind: 'cli', label: 'CLI', detail: 'reads this machine · confined',
      long: 'Runs as a CLI on this machine. It can read the checkout, and the CLI holds it to what it is granted.',
      files: true,
    },
    'cli-unconfined': {
      kind: 'cli', label: 'CLI', detail: 'reads this machine · cannot be confined',
      long: 'Runs as a CLI on this machine. It has a shell, so it can read any file the account can (measured 2026-10-01).',
      files: true,
    },
    'cli-default-deny': {
      kind: 'cli', label: 'CLI', detail: 'reads this machine · held by defaults',
      long: 'Runs as a CLI on this machine. Outside the folders it is given it is held only by a headless default.',
      files: true,
    },
    api: {
      kind: 'api', label: 'API key', detail: 'prompt only',
      long: 'An HTTP call with a key. It sees only what is in the prompt and cannot open a file.',
      files: false,
    },
    local: {
      kind: 'local', label: 'Local engine', detail: 'prompt only',
      long: 'A model on this machine\'s GPU (Ollama or vLLM) over HTTP. It sees only what is in the prompt.',
      files: false,
    },
    remote: {
      kind: 'remote', label: 'Team server', detail: 'prompt only',
      long: 'Runs on the company\'s Team server, confined in an empty folder. It sees only what is in the prompt.',
      files: false,
    },
  };

  /* Effort vocabularies. `effortNew` marks a runtime where coai-mcp does not pass an effort today —
     the control is new in this design and needs adapter work behind it. */
  const CODEX_EFFORT = ['minimal', 'low', 'medium', 'high', 'xhigh'];

  const VENDORS = {
    claude: {
      label: 'Claude Code', maker: 'Anthropic', colour: 'purple', access: 'cli-confined',
      models: ['haiku', 'sonnet', 'opus', 'fable'],
      effort: ['low', 'medium', 'high', 'max'], effortNew: true,
      connection: ['cli'],
    },
    codex: {
      label: 'Codex CLI', maker: 'OpenAI', colour: 'blue', access: 'cli-unconfined',
      models: ['gpt-6-luna', 'gpt-6-sol', 'gpt-6-terra', 'gpt-5.5'],
      effort: CODEX_EFFORT, effortNew: true,
      connection: ['cli'],
    },
    antigravity: {
      label: 'Antigravity', maker: 'Google', colour: 'cyan', access: 'cli-default-deny',
      models: ['gemini-3.8-flash-low', 'gemini-3.8-flash-medium', 'gemini-3.8-flash-high',
        'gemini-3.8-pro-high', 'gpt-oss-120b-medium'],
      effort: null,
      effortNote: 'Antigravity names the effort inside the model — pick …-low, …-medium or …-high.',
      connection: ['cli'],
    },
    dashscope: {
      label: 'Model Studio · Token Plan', maker: 'Alibaba', colour: 'amber', access: 'api',
      models: ['glm-5.3', 'qwen3.8-max', 'qwen3.7-plus', 'deepseek-v4'],
      effortByModel: {
        'glm-5.3': ['low', 'high', 'max'],
        'qwen3.8-max': ['none', 'low', 'medium', 'xhigh'],
        'qwen3.7-plus': ['none', 'low', 'medium', 'xhigh'],
        'deepseek-v4': ['low', 'medium', 'high'],
      },
      thinkingModels: ['qwen3.8-max', 'qwen3.7-plus'],
      connection: ['endpoint', 'key', 'dialect'],
      endpoint: 'https://dashscope-intl.aliyuncs.com/compatible-mode/v1', key: 'DASHSCOPE_TOKEN_PLAN',
      dialect: 'dashscope',
    },
    xai: {
      label: 'Grok API', maker: 'xAI', colour: 'red', access: 'api',
      models: ['grok-4.3', 'grok-4.3-fast'],
      effort: ['low', 'medium', 'high', 'xhigh'],
      connection: ['endpoint', 'key', 'dialect'],
      endpoint: 'https://api.x.ai/v1', key: 'XAI_KEY', dialect: 'xai',
    },
    deepseek: {
      label: 'DeepSeek through the Codex CLI', maker: 'DeepSeek', colour: 'turquoise', access: 'cli-unconfined',
      models: ['deepseek-v4', 'deepseek-v4-reasoner'],
      effort: ['low', 'medium', 'high'], effortNew: true,
      connection: ['cli', 'endpoint', 'key'],
      endpoint: 'https://api.deepseek.com/v1', key: 'DEEPSEEK_KEY',
      accessNote: 'A key-based model driven by the Codex CLI: it still has a shell on this machine.',
    },
    openrouter: {
      label: 'OpenRouter through the Codex CLI', maker: 'OpenRouter', colour: 'pink', access: 'cli-unconfined',
      models: [], freeModel: true,
      effort: CODEX_EFFORT, effortNew: true,
      connection: ['cli', 'endpoint', 'key'],
      endpoint: 'https://openrouter.ai/api/v1', key: 'OPENROUTER_KEY',
      accessNote: 'A key-based model driven by the Codex CLI: it still has a shell on this machine.',
    },
    compat: {
      label: 'Any OpenAI-compatible endpoint', maker: 'Custom', colour: 'lime', access: 'api',
      models: [], freeModel: true,
      effort: ['none', 'low', 'medium', 'high'],
      connection: ['endpoint', 'key', 'dialect'],
      endpoint: '', key: '', dialect: 'openai',
    },
    local: {
      label: 'Ollama / vLLM', maker: 'Local engine', colour: 'orange', access: 'local',
      models: ['qwen3-coder:30b', 'gpt-oss:20b', 'devstral:24b'],
      effort: ['none', 'low', 'medium', 'high'],
      effortHint: 'Measured: with thinking on, a local model gave no answer after 1,056 s — keep "none" for reviews.',
      connection: ['endpoint'],
      endpoint: 'http://127.0.0.1:11434',
    },
    remote: {
      label: 'Team server', maker: 'Company', colour: 'slate', access: 'remote',
      models: ['gpt-6-luna', 'gpt-6-sol', 'sonnet', 'opus'],
      effort: CODEX_EFFORT, effortNew: true,
      connection: ['server'],
    },
  };

  /* Which features a runtime can serve. `limited` is allowed but explained; `off` is disabled with
     the reason written next to it. Nothing here blocks a choice the person may legitimately make —
     a different-vendor consultant is advice, not a rule (operator, 2026-10-04). */
  function availability(instance, featureId) {
    const vendor = VENDORS[instance.vendor];
    const access = ACCESS[vendor.access];
    const rule = RULES[featureId];
    return rule ? rule(vendor, access) : { state: 'ok' };
  }

  /* `reason` is short on purpose: the card merges every feature that shares one into a single line. */
  const PROMPT_ONLY = 'prompt only — it answers from what it is sent and cannot open files';
  const RULES = {
    document: (v, a) => a.kind === 'remote'
      ? { state: 'limited', reason: 'the Team server\'s operator must agree to documents' }
      : { state: 'ok' },
    feature: (v, a) => a.kind === 'remote'
      ? { state: 'off', reason: 'never sent to a Team server' }
      : { state: 'ok' },
    consultant: (v, a) => {
      if (a.kind === 'remote') return { state: 'off', reason: 'never sent to a Team server' };
      return a.files ? { state: 'ok' } : { state: 'limited', reason: PROMPT_ONLY };
    },
    qconsult: (v, a) => {
      if (a.kind === 'remote') return { state: 'off', reason: 'never sent to a Team server' };
      return a.files ? { state: 'ok' } : { state: 'limited', reason: PROMPT_ONLY };
    },
    chat: (v, a) => (a.kind === 'cli' || a.kind === 'remote')
      ? { state: 'ok' }
      : { state: 'off', reason: 'a CLI or a Team server only' },
    bugz: (v, a) => a.kind === 'local'
      ? { state: 'ok' }
      : { state: 'off', reason: 'a local engine only — it reads code that is not anonymised' },
  };

  /* The four callers a consultant answers. The consultant tab picks one instance per caller. */
  const CALLERS = [
    { id: 'claude', label: 'Claude Code asks', maker: 'Anthropic' },
    { id: 'codex', label: 'Codex asks', maker: 'OpenAI' },
    { id: 'gemini', label: 'Gemini asks', maker: 'Google' },
    { id: 'other', label: 'Another client asks', maker: '' },
  ];

  /* The key NAMES the vault entry holds. The values never reach the page. */
  const VAULT_KEYS = ['DASHSCOPE_TOKEN_PLAN', 'XAI_KEY', 'DEEPSEEK_KEY', 'OPENROUTER_KEY', 'GROK_OPENROUTER'];

  /* A control marked "new" keeps its tag for this many days after the person first ran the version
     that brought it. */
  const NEW_FOR_DAYS = 7;

  const DEMO_TEAM_SERVERS = [{
    id: 'remsoft', name: 'Company', url: 'https://coai.remsoft.dev', account: 'oleksandr.dubyna@fasttask.net',
    signedIn: true, version: '0.9.0', published: '0.9.0', contract: 1, admin: true,
    roles: ['PlanCritique', 'Conventions', 'Architecture', 'SecurityReliability', 'UxDxPerformance'],
    vendors: [
      { id: 'codex', runtime: 'codex', models: ['gpt-6-luna', 'gpt-6-sol'], health: 'ok', slots: 3, busy: 1 },
      { id: 'claude', runtime: 'claude', models: ['sonnet', 'opus'], health: 'ok', slots: 2, busy: 0 },
    ],
  }];

  /* The clients coai-mcp is registered in, and where each keeps its entry. */
  const CLIENTS = [
    { id: 'claude', label: 'Claude Code', file: '~/.claude.json', registered: true, snippet: 'current' },
    { id: 'codex', label: 'Codex', file: '~/.codex/config.toml', registered: true, snippet: 'stale' },
    { id: 'gemini', label: 'Gemini CLI', file: '~/.gemini/settings.json', registered: false, snippet: 'none' },
  ];

  const DEMO_SETUP = {
    credsKey: 'coai-vendor-keys', vaultAnswered: true,
    perSide: true, side: 'Windows', otherSides: ['WSL: Ubuntu-24.04'],
    mcp: { installed: '0.42.1', published: '0.42.1', path: '%LOCALAPPDATA%\\coai\\bin\\coai-mcp.exe', checked: '4 min ago' },
    data: { directory: 'D:\\coai-data', source: 'this side', sideName: 'windows', alsoWatched: ['\\\\wsl$\\Ubuntu-24.04\\home\\strug\\.coai'] },
  };

  /* The operator's current setup, as the screenshots of 2026-10-04 show it. */
  const DEMO = [
    { id: 'codex', name: 'GPT-6 Luna', vendor: 'codex', model: 'gpt-6-luna', effort: 'medium',
      features: ['plan', 'code', 'security'], usage: { runs: 261, failed: 24, cost: '~$2.53' }, health: 'ok' },
    { id: 'codex-2', name: 'GPT-6 Luna · high', vendor: 'codex', model: 'gpt-6-luna', effort: 'high',
      features: ['consultant', 'qconsult'], usage: { runs: 4, failed: 1, cost: '—' }, health: 'ok' },
    { id: 'antigravity', name: 'Gemini 3.8 Flash', vendor: 'antigravity', model: 'gemini-3.8-flash-medium',
      features: ['plan', 'code', 'chat'], usage: { runs: 18, failed: 10, cost: '—' }, health: 'ok' },
    { id: 'claude', name: 'Claude Sonnet', vendor: 'claude', model: 'sonnet', effort: '',
      features: ['plan', 'code', 'document', 'chat'], usage: { runs: 15, failed: 2, cost: '$12.00' }, health: 'ok' },
    { id: 'claude-2', name: 'Claude Opus · consultant', vendor: 'claude', model: 'opus', effort: 'high',
      features: ['consultant', 'qconsult'], usage: { runs: 0, failed: 0, cost: '—' }, health: 'ok' },
    { id: 'local', name: 'Qwen3 Coder 30B', vendor: 'local', model: 'qwen3-coder:30b', effort: 'none',
      features: ['plan', 'bugz'], usage: { runs: 81, failed: 4, cost: '—' }, health: 'ok' },
    { id: 'qwen', name: 'GLM 5.3 · low', vendor: 'dashscope', model: 'glm-5.3', effort: 'low',
      features: ['code', 'feature', 'security'], usage: { runs: 18, failed: 1, cost: '$0.75' }, health: 'ok' },
    { id: 'qwen-2', name: 'GLM 5.3 · high', vendor: 'dashscope', model: 'glm-5.3', effort: 'high',
      features: ['consultant', 'qconsult'], usage: { runs: 0, failed: 0, cost: '—' }, health: 'ok' },
    { id: 'grok-openrouter', name: 'Grok 4.3', vendor: 'xai', model: 'grok-4.3', effort: 'medium',
      features: ['feature'], key: 'GROK_OPENROUTER', usage: { runs: 1, failed: 0, cost: '—' }, health: 'warn' },
    { id: 'remsoft-codex', name: 'Company Codex', vendor: 'remote', server: 'remsoft', serverVendor: 'codex',
      model: 'gpt-6-luna', effort: '', features: ['plan', 'code'], usage: { runs: 0, failed: 0, cost: '—' },
      health: 'ok', enabled: false },
  ];

  const DEMO_CONSULTANTS = { claude: 'codex-2', codex: 'claude-2', gemini: 'qwen-2', other: 'claude-2' };

  window.COAI = {
    FEATURE_GROUPS, FEATURES, ACCESS, VENDORS, CALLERS, VAULT_KEYS, NEW_FOR_DAYS, CLIENTS,
    DEMO, DEMO_CONSULTANTS, DEMO_TEAM_SERVERS, DEMO_SETUP, availability,
  };
})();
