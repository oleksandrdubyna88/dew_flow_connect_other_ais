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

/** The five languages a person may be asked in. */
export type LanguageCode = 'en' | 'es' | 'de' | 'ru' | 'uk';

export const LANGUAGES: readonly { code: LanguageCode; label: string }[] = [
  { code: 'en', label: 'English' },
  { code: 'es', label: 'Español' },
  { code: 'de', label: 'Deutsch' },
  { code: 'ru', label: 'Русский' },
  { code: 'uk', label: 'Українська' },
];

/** Who does the translating when the AI did not write in that language. */
export const TRANSLATORS: readonly { id: string; label: string }[] = [
  { id: 'gemini', label: 'Gemini Flash (CLI)' },
  { id: 'codex', label: 'Codex, a mini model (CLI)' },
  { id: 'none', label: 'Nobody — always show the original' },
];

/** Every vendor the panel can offer, in the order it shows them. */
export const PROVIDERS: readonly Provider[] = ['codex', 'gemini', 'deepseek'];

export interface TranslatorChoice {
  readonly provider: string;
  readonly model: string;
}

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
  readonly language: LanguageCode;
  readonly translator: TranslatorChoice;
  readonly escalationMinutes: number;
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
  language: 'en',
  translator: { provider: 'gemini', model: 'gemini-flash-latest' },
  escalationMinutes: 30,
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
    language: asLanguage(read('language')),
    translator: {
      provider: asTranslator(read('translator.provider')),
      model: asString(read('translator.model')) || DEFAULTS.translator.model,
    },
    escalationMinutes: asPositive(read('escalationMinutes'), DEFAULTS.escalationMinutes),
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
  if (settings.language !== DEFAULTS.language) {
    env['COAI_LANGUAGE'] = settings.language;
  }
  if (settings.translator.provider !== DEFAULTS.translator.provider) {
    env['COAI_TRANSLATOR_PROVIDER'] = settings.translator.provider;
  }
  if (settings.translator.model !== DEFAULTS.translator.model) {
    env['COAI_TRANSLATOR_MODEL'] = settings.translator.model;
  }
  if (settings.escalationMinutes !== DEFAULTS.escalationMinutes) {
    env['COAI_ESCALATION_MINUTES'] = String(settings.escalationMinutes);
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

function asLanguage(value: unknown): LanguageCode {
  return LANGUAGES.some((l) => l.code === value) ? (value as LanguageCode) : DEFAULTS.language;
}

function asTranslator(value: unknown): string {
  return TRANSLATORS.some((t) => t.id === value) ? (value as string) : DEFAULTS.translator.provider;
}

function asOnExhausted(value: unknown): OnExhausted {
  return value === 'continue' || value === 'escalate' || value === 'human' ? value : DEFAULTS.onExhausted;
}

function sameProviders(a: readonly Provider[], b: readonly Provider[]): boolean {
  return a.length === b.length && a.every((p, i) => p === b[i]);
}
