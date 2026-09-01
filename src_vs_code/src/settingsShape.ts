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

export type OnExhausted = 'continue' | 'escalate' | 'human' | 'good_enough';

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
  /**
   * Rounds and threshold per ROLE, keyed by role id.
   *
   * <p>Per stage before this, and one number for both before that. Each step was the same
   * discovery: a budget shared by things that are not alike forces the cheapest of them to pay for
   * the most expensive. Architecture may be worth two passes with different lenses while
   * performance is worth one.</p>
   */
  readonly rounds: Readonly<Record<string, number>>;
  readonly thresholds: Readonly<Record<string, number>>;

  readonly onExhausted: OnExhausted;
  readonly maxConcurrency: number;
  readonly maxPerProvider: number;
  readonly reviewerTimeoutMinutes: number;
  readonly credsKey: string;
  readonly escalationMinutes: number;
  /** Per role, the prompt id each round uses — index 0 is round 1. Empty = the universal one. */
  readonly promptsPerRound: Readonly<Record<string, readonly string[]>>;
  /** Spend the rounds on different lenses instead of asking the same broad question again. */
  /**
   * Deal the lenses across the vendors instead of giving every vendor the same one.
   *
   * <p>Two switches because the stages are not alike: a plan has three lenses for one role, a code
   * round has three roles. Off by default, and that default is the point \u2014 with it off every vendor
   * answers the same question and two vendors agreeing on a finding is a fact the gate can use.
   * On, every lens gets asked once at half the launches, and that agreement is gone.</p>
   */
  readonly dealPlanLenses: boolean;
  readonly dealCodeLenses: boolean;
}

/** The defaults, matching the master plan's configuration table — pinned by tests. */
/**
 * Where one changed control is kept: a plain setting, one vendor's property, or one role's entry in
 * a role-keyed record.
 *
 * <p>Three kinds because there ARE three, and the panel used to have two slots for them. `rounds`
 * and `thresholds` are records keyed by role, and their inputs travelled in the vendor slot — so the
 * provider looked for a vendor called `Architecture`, found none, and wrote nothing. The number
 * reverted on the next repaint and the prompt pickers never changed count.</p>
 */
export type SettingWrite =
  | { readonly kind: 'plain'; readonly key: string; readonly value: unknown }
  | { readonly kind: 'vendor'; readonly key: string; readonly value: unknown; readonly vendor: string }
  | { readonly kind: 'role'; readonly key: string; readonly value: unknown; readonly role: string };

/** What the webview said it changed. A message with no key changes nothing. */
export interface SettingMessage {
  readonly key: string | undefined;
  readonly value: unknown;
  readonly vendor?: string | undefined;
  readonly role?: string | undefined;
}

/**
 * Route one changed control. Pure: the `vscode` call it leads to is the provider's business, and
 * this is the part that was wrong.
 */
export function settingWrite(message: SettingMessage): SettingWrite | undefined {
  const { key, value } = message;
  if (key === undefined || key.length === 0) {
    return undefined;
  }
  if (message.role !== undefined && message.role.length > 0) {
    return { kind: 'role', key, value, role: message.role };
  }
  if (message.vendor !== undefined && message.vendor.length > 0) {
    return { kind: 'vendor', key, value, vendor: message.vendor };
  }

  return { kind: 'plain', key, value };
}

/**
 * One role's entry changed inside a role-keyed record, with every other role kept.
 *
 * <p>A record, MERGED rather than replaced. Replacing it would drop the three roles the person did
 * not touch, and the symptom would be the one this shape was introduced to fix — a number that will
 * not stick — for three roles instead of one.</p>
 */
export function roleRecordUpdate(
  current: Readonly<Record<string, unknown>>,
  role: string,
  value: unknown,
): Record<string, unknown> {
  return { ...current, [role]: value };
}

export const DEFAULTS: CoaiSettings = {
  rounds: { PlanCritique: 3, Architecture: 2, SecurityReliability: 2, UxDxPerformance: 2 },
  thresholds: { PlanCritique: 2, Architecture: 3, SecurityReliability: 3, UxDxPerformance: 3 },
  onExhausted: 'human',
  maxConcurrency: 3,
  maxPerProvider: 2,
  reviewerTimeoutMinutes: 10,
  credsKey: '',
  escalationMinutes: 30,
  promptsPerRound: {},
  dealPlanLenses: false,
  dealCodeLenses: false,
};

/** A raw configuration reader: `get(section)` returns whatever the host stored, if anything. */
export type ConfigReader = (section: string) => unknown;

/** VS Code config → a validated `CoaiSettings`; anything malformed falls back to the default. */
export function settingsFrom(read: ConfigReader): CoaiSettings {
  return {
    rounds: asRoleNumbers(read('rounds'), DEFAULTS.rounds, asPositive),
    thresholds: asRoleNumbers(read('thresholds'), DEFAULTS.thresholds, asCount),
    onExhausted: asOnExhausted(read('onExhausted')),
    maxConcurrency: asPositive(read('maxConcurrency'), DEFAULTS.maxConcurrency),
    maxPerProvider: asPositive(read('maxPerProvider'), DEFAULTS.maxPerProvider),
    reviewerTimeoutMinutes: asPositive(read('reviewerTimeoutMinutes'), DEFAULTS.reviewerTimeoutMinutes),
    credsKey: asString(read('credsKey')),

    escalationMinutes: asPositive(read('escalationMinutes'), DEFAULTS.escalationMinutes),
    promptsPerRound: asPromptRounds(read('promptsPerRound')),
    dealPlanLenses: read('dealPlanLenses') === true,
    dealCodeLenses: read('dealCodeLenses') === true,
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
  if (Object.keys(settings.promptsPerRound).length > 0) {
    env['COAI_PROMPTS_PER_ROUND'] = JSON.stringify(settings.promptsPerRound);
  }
  // A key per role, and only where it differs: the panel writes what is not the default so that
  // returning a control to its default REMOVES the key rather than pinning the old value.
  for (const [role, rounds] of Object.entries(settings.rounds)) {
    if (rounds !== DEFAULTS.rounds[role]) {
      env[`COAI_ROUNDS_${role.toUpperCase()}`] = String(rounds);
    }
  }
  if (settings.dealPlanLenses !== DEFAULTS.dealPlanLenses) {
    env['COAI_DEAL_PLAN'] = 'true';
  }
  if (settings.dealCodeLenses !== DEFAULTS.dealCodeLenses) {
    env['COAI_DEAL_CODE'] = 'true';
  }

  for (const [role, threshold] of Object.entries(settings.thresholds)) {
    if (threshold !== DEFAULTS.thresholds[role]) {
      env[`COAI_THRESHOLD_${role.toUpperCase()}`] = String(threshold);
    }
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



function asOnExhausted(value: unknown): OnExhausted {
  return value === 'continue' || value === 'escalate' || value === 'human' || value === 'good_enough'
    ? value
    : DEFAULTS.onExhausted;
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

/**
 * A role -> number map from settings, with every role falling back to its default.
 *
 * <p>A stored map is whatever a person or a sync left there, so each entry is validated on its
 * own and a junk one takes the default rather than poisoning the map.</p>
 */
function asRoleNumbers(
  value: unknown,
  defaults: Readonly<Record<string, number>>,
  check: (value: unknown, fallback: number) => number,
): Readonly<Record<string, number>> {
  const stored = typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  const out: Record<string, number> = {};
  for (const [role, fallback] of Object.entries(defaults)) {
    out[role] = check(stored[role], fallback);
  }

  return out;
}
