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

import { DEFAULT_VENDORS, Vendor, vendorsEnv } from './vendors';

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
  { id: 'claude', label: 'Claude, a small model (CLI)' },
  { id: 'codex', label: 'Codex, a mini model (CLI)' },
  { id: 'none', label: 'Nobody — always show the original' },
];

export interface TranslatorChoice {
  readonly provider: string;
  readonly model: string;
}

export interface CoaiSettings {
  /**
   * The plan stage's budget. Separate from the code stage's because a plan is a document and a
   * diff is not: two findings still open is a lot of doubt about a page of text, while three open
   * on a diff of a dozen files is an ordinary Tuesday. One number for both made the plan gate
   * strict and the code gate a permanent `call_human`.
   */
  readonly maxRoundsPlan: number;
  readonly gateThresholdPlan: number;
  readonly maxRoundsCode: number;
  readonly gateThresholdCode: number;
  readonly onExhausted: OnExhausted;
  readonly maxConcurrency: number;
  readonly maxPerProvider: number;
  readonly reviewerTimeoutMinutes: number;
  readonly credsKey: string;
  readonly language: LanguageCode;
  readonly translator: TranslatorChoice;
  readonly escalationMinutes: number;
  /** Per role, the prompt id each round uses — index 0 is round 1. Empty = the universal one. */
  readonly promptsPerRound: Readonly<Record<string, readonly string[]>>;
  /** Spend the rounds on different lenses instead of asking the same broad question again. */
  readonly rotatePrompts: boolean;
}

/** The defaults, matching the master plan's configuration table — pinned by tests. */
export const DEFAULTS: CoaiSettings = {
  maxRoundsPlan: 3,
  gateThresholdPlan: 2,
  maxRoundsCode: 3,
  gateThresholdCode: 3,
  onExhausted: 'human',
  maxConcurrency: 3,
  maxPerProvider: 2,
  reviewerTimeoutMinutes: 10,
  credsKey: '',
  language: 'en',
  translator: { provider: 'gemini', model: 'gemini-flash-latest' },
  escalationMinutes: 30,
  promptsPerRound: {},
  rotatePrompts: false,
};

/** A raw configuration reader: `get(section)` returns whatever the host stored, if anything. */
export type ConfigReader = (section: string) => unknown;

/** VS Code config → a validated `CoaiSettings`; anything malformed falls back to the default. */
export function settingsFrom(read: ConfigReader): CoaiSettings {
  return {
    maxRoundsPlan: asPositive(read('maxRoundsPlan'), DEFAULTS.maxRoundsPlan),
    gateThresholdPlan: asCount(read('gateThresholdPlan'), DEFAULTS.gateThresholdPlan),
    maxRoundsCode: asPositive(read('maxRoundsCode'), DEFAULTS.maxRoundsCode),
    gateThresholdCode: asCount(read('gateThresholdCode'), DEFAULTS.gateThresholdCode),
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
    promptsPerRound: asPromptRounds(read('promptsPerRound')),
    rotatePrompts: read('rotatePrompts') === true,
  };
}

/**
 * The `env` block for `mcpServers` — only what differs from the server's own defaults, so a
 * pristine configuration produces NO env at all and the block stays readable.
 */
export function envBlock(settings: CoaiSettings, vendors: readonly Vendor[] = DEFAULT_VENDORS): Record<string, string> {
  const env: Record<string, string> = {};
  if (!sameVendors(vendors, DEFAULT_VENDORS)) {
    env['COAI_VENDORS'] = vendorsEnv(vendors);
  }
  if (settings.rotatePrompts) {
    env['COAI_ROTATE_PROMPTS'] = 'true';
  }
  if (Object.keys(settings.promptsPerRound).length > 0) {
    env['COAI_PROMPTS_PER_ROUND'] = JSON.stringify(settings.promptsPerRound);
  }
  if (settings.maxRoundsPlan !== DEFAULTS.maxRoundsPlan) {
    env['COAI_MAX_ROUNDS_PLAN'] = String(settings.maxRoundsPlan);
  }
  if (settings.maxRoundsCode !== DEFAULTS.maxRoundsCode) {
    env['COAI_MAX_ROUNDS_CODE'] = String(settings.maxRoundsCode);
  }
  if (settings.gateThresholdPlan !== DEFAULTS.gateThresholdPlan) {
    env['COAI_THRESHOLD_PLAN'] = String(settings.gateThresholdPlan);
  }
  if (settings.gateThresholdCode !== DEFAULTS.gateThresholdCode) {
    env['COAI_THRESHOLD_CODE'] = String(settings.gateThresholdCode);
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

/** Whether the reviewers are still exactly the shipped pair, unchanged. */
function sameVendors(a: readonly Vendor[], b: readonly Vendor[]): boolean {
  return (
    a.length === b.length &&
    a.every((v, i) => {
      const other = b[i];
      return (
        other !== undefined &&
        v.id === other.id &&
        v.runtime === other.runtime &&
        v.model === other.model &&
        v.enabled === other.enabled &&
        v.baseUrl === other.baseUrl
      );
    })
  );
}

/**
 * The stored per-round prompt map, kept only where it is actually a map of string arrays.
 *
 * <p>A stale id is NOT filtered here: the server falls back to the universal prompt for anything
 * it does not recognise, and silently dropping a name the person chose would make a typo look
 * like it had been accepted.</p>
 */
function asPromptRounds(value: unknown): Record<string, string[]> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return {};
  }
  const out: Record<string, string[]> = {};
  for (const [role, rounds] of Object.entries(value as Record<string, unknown>)) {
    if (Array.isArray(rounds)) {
      out[role] = rounds.filter((r): r is string => typeof r === 'string');
    }
  }
  return out;
}
