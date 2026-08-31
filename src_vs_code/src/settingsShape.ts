/**
 * The settings as a typed value, and how they travel to `coai-mcp`.
 *
 * <p>Pure and `vscode`-free: the extension half reads VS Code configuration into a plain object;
 * everything below decides what that MEANS — the defaults (one test per default, so drift from
 * the master plan's table is a red test) and the environment block the server actually reads.</p>
 *
 * <p><b>Settings reach the server as environment variables in the `mcpServers` block.</b> The MCP
 * client owns the server's process and its config file is static — so the copyable block is where
 * configuration crosses over, regenerated whenever the person copies it again.</p>
 */

export type Provider = 'codex' | 'gemini' | 'deepseek';

export type OnExhausted = 'continue' | 'human' | 'escalate';

export interface CoaiSettings {
  readonly providers: readonly Provider[];
  readonly models: Readonly<Partial<Record<Provider, string>>>;
  readonly maxRounds: number;
  readonly gateThreshold: number;
  readonly onExhausted: OnExhausted;
  readonly maxConcurrency: number;
  readonly maxPerProvider: number;
  readonly reviewerTimeoutMinutes: number;
  readonly credsKey: string;
}

/** The defaults, matching the master plan's configuration table — pinned by tests. */
export const DEFAULTS: CoaiSettings = {
  providers: ['codex', 'gemini'],
  models: {},
  maxRounds: 3,
  gateThreshold: 2,
  onExhausted: 'human',
  maxConcurrency: 3,
  maxPerProvider: 2,
  reviewerTimeoutMinutes: 10,
  credsKey: '',
};

/** A raw configuration reader: `get(section)` returns whatever the host stored, if anything. */
export type ConfigReader = (section: string) => unknown;

/** VS Code config → a validated `CoaiSettings`; anything malformed falls back to the default. */
export function settingsFrom(read: ConfigReader): CoaiSettings {
  const providers = asProviders(read('providers'));
  return {
    providers,
    models: {
      codex: asString(read('model.codex')),
      gemini: asString(read('model.gemini')),
      deepseek: asString(read('model.deepseek')),
    },
    maxRounds: asPositive(read('maxRounds'), DEFAULTS.maxRounds),
    gateThreshold: asCount(read('gateThreshold'), DEFAULTS.gateThreshold),
    onExhausted: asOnExhausted(read('onExhausted')),
    maxConcurrency: asPositive(read('maxConcurrency'), DEFAULTS.maxConcurrency),
    maxPerProvider: asPositive(read('maxPerProvider'), DEFAULTS.maxPerProvider),
    reviewerTimeoutMinutes: asPositive(read('reviewerTimeoutMinutes'), DEFAULTS.reviewerTimeoutMinutes),
    credsKey: asString(read('credsKey')),
  };
}

/**
 * The `env` block for `mcpServers` — only what differs from the server's own defaults, so a
 * pristine configuration produces NO env at all and the block stays readable.
 */
export function envBlock(settings: CoaiSettings): Record<string, string> {
  const env: Record<string, string> = {};
  if (!sameProviders(settings.providers, DEFAULTS.providers)) {
    env['COAI_PROVIDERS'] = settings.providers.join(',');
  }
  for (const provider of settings.providers) {
    const model = settings.models[provider];
    if (model) {
      env[`COAI_MODEL_${provider.toUpperCase()}`] = model;
    }
  }
  if (settings.maxRounds !== DEFAULTS.maxRounds) {
    env['COAI_MAX_ROUNDS'] = String(settings.maxRounds);
  }
  if (settings.gateThreshold !== DEFAULTS.gateThreshold) {
    env['COAI_GATE_THRESHOLD'] = String(settings.gateThreshold);
  }
  if (settings.onExhausted !== DEFAULTS.onExhausted) {
    env['COAI_ON_EXHAUSTED'] = settings.onExhausted;
  }
  if (settings.maxConcurrency !== DEFAULTS.maxConcurrency) {
    env['COAI_MAX_CONCURRENCY'] = String(settings.maxConcurrency);
  }
  if (settings.maxPerProvider !== DEFAULTS.maxPerProvider) {
    env['COAI_MAX_PER_PROVIDER'] = String(settings.maxPerProvider);
  }
  if (settings.reviewerTimeoutMinutes !== DEFAULTS.reviewerTimeoutMinutes) {
    env['COAI_REVIEWER_TIMEOUT_MINUTES'] = String(settings.reviewerTimeoutMinutes);
  }
  if (settings.credsKey) {
    env['COAI_CREDS_KEY'] = settings.credsKey;
  }
  return env;
}

function asProviders(value: unknown): readonly Provider[] {
  if (!Array.isArray(value)) {
    return DEFAULTS.providers;
  }
  const valid = value.filter((p): p is Provider => p === 'codex' || p === 'gemini' || p === 'deepseek');
  return valid.length > 0 ? valid : DEFAULTS.providers;
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

function asPositive(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 1 ? value : fallback;
}

function asCount(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isInteger(value) && value >= 0 ? value : fallback;
}

function asOnExhausted(value: unknown): OnExhausted {
  return value === 'continue' || value === 'escalate' || value === 'human' ? value : DEFAULTS.onExhausted;
}

function sameProviders(a: readonly Provider[], b: readonly Provider[]): boolean {
  return a.length === b.length && a.every((p, i) => p === b[i]);
}
