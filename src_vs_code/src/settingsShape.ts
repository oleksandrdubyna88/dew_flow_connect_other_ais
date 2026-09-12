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
import { PLAN_STAGE, composed, isActive, rolesFrom, stageOf, type RoleRow } from './roles';

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
  /**
   * Whether each CODE role takes part at all, keyed by role id.
   *
   * <p>Asked for on 2026-09-08: a checkbox on each of the four code-review boxes, and unticking one
   * means the role does not take part in the round — not a reviewer that runs and is then ignored.
   * Until this existed a role could only be kept out by lying to a control that means something
   * else, which also lost the number the person wants back when they turn it on again.</p>
   *
   * <p>Code roles only. `PlanCritique` is absent from this record and from the panel, because a
   * switch whose only setting turns the whole plan stage off is a different feature.</p>
   */
  readonly roleEnabled: Readonly<Record<string, boolean>>;

  readonly onExhausted: OnExhausted;
  readonly maxConcurrency: number;
  readonly maxPerProvider: number;
  readonly reviewerTimeoutMinutes: number;
  /**
   * How long a whole ROUND may take, or 0 to derive it from the round's own shape.
   *
   * <p>Zero is the default and it is not laziness: a round runs `vendors × roles` reviewers through
   * the machine's cap, so it takes as many WAVES as that division needs and each wave can
   * legitimately last a whole reviewer timeout. Three vendors, four code roles and a cap of three
   * is four waves — forty minutes of entirely healthy work. A fixed number shipped in its place
   * would cancel real rounds on any machine with more vendors than whoever chose it.</p>
   */
  readonly roundTimeoutMinutes: number;
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
  /**
   * Work without interrupting the person until there is no other way.
   *
   * <p>These three are ORDERS the gate hands back to whichever AI called it, not changes to what the
   * gate decides. They are the operator's decisions — how the work is broken up, when a person is
   * interrupted, which model does the expensive half — and the panel is where the operator sits.</p>
   */
  readonly autonomous: boolean;
  /** Break an accepted plan into epics and stories, and close each story properly. */
  readonly splitPlan: boolean;
  /** Do the split, and the stories where being wrong is expensive, with Fable. */
  readonly splitWithFable: boolean;
  /**
   * What a code reviewer is launched in: `none` (Fast — the diff alone) or `worktree` (Full).
   *
   * <p>Fast is the default because it was measured, not preferred: without a checkout every hosted
   * model found MORE useful defects at a fraction of the tokens, and three real defects surfaced
   * that no run with one reached. See `RESULTS_findings_that_are_worth_something.md`.</p>
   */
  readonly codeWorkspace: string;

  /**
   * The review roles this person configured, as the rows `COAI_ROLES` carries.
   *
   * <p>Stored as the WIRE FORMAT rather than a panel-shaped model, so `envBlock` is a
   * `JSON.stringify` and nothing translates between the two halves — the arrangement
   * `PLAN_review_roles_become_data.md` spent eight stories arriving at. Empty is the normal state
   * and emits no key at all: an installation that adds no role of its own runs the shipped five, in
   * the shipped order, with the shipped budgets.</p>
   */
  readonly roles: readonly RoleRow[];
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
  // The operator's own settings, after a day of running the gate on this repository's real work
  // (2026-09-07). One plan round rather than three: the second and third rounds re-raise what the
  // first found rather than finding more, which the round records show. Architecture keeps two
  // because its first round is the conventions pass and its second is the broad question — two
  // different questions, not the same one twice. The thresholds went UP because they had been set
  // where a real change could not pass: a plan round that regularly produces six findings and a
  // code role that produces five are not failures, and a gate that says they are gets ignored,
  // which is the one failure mode a gate cannot survive.
  rounds: { PlanCritique: 1, Conventions: 1, Architecture: 1, SecurityReliability: 1, UxDxPerformance: 1 },
  thresholds: { PlanCritique: 6, Conventions: 5, Architecture: 5, SecurityReliability: 5, UxDxPerformance: 5 },
  // Every code role on. The four keys are also what the reader iterates, so this object is the list
  // of roles that HAVE a switch — the plan role is absent from it deliberately.
  roleEnabled: { Conventions: true, Architecture: true, SecurityReliability: true, UxDxPerformance: true },
  onExhausted: 'human',
  maxConcurrency: 3,
  maxPerProvider: 2,
  reviewerTimeoutMinutes: 10,
  roundTimeoutMinutes: 0,
  credsKey: '',
  escalationMinutes: 30,
  promptsPerRound: {},
  dealPlanLenses: false,
  dealCodeLenses: false,
  autonomous: false,
  splitPlan: false,
  splitWithFable: false,
  codeWorkspace: 'none',
  roles: [],
};

/** A raw configuration reader: `get(section)` returns whatever the host stored, if anything. */
export type ConfigReader = (section: string) => unknown;

/** One side's own values, by setting name. Absent means "use the shared one". */
export type SettingsOverlay = Readonly<Record<string, unknown>>;

/**
 * Every `coai.*` setting a side can hold on its own.
 *
 * <p>Named rather than derived, because seeding an overlay has to copy a KNOWN set: a setting this
 * list forgets is one that silently stays shared, and a person who set a different proxy on one
 * side would not find out until a review ran against the wrong company's server.</p>
 */
export const OVERLAID_SETTINGS: readonly string[] = [
  'vendors', 'rounds', 'thresholds', 'roleEnabled', 'onExhausted', 'maxConcurrency', 'maxPerProvider',
  'reviewerTimeoutMinutes', 'roundTimeoutMinutes', 'credsKey', 'escalationMinutes', 'promptsPerRound',
  'dealPlanLenses', 'dealCodeLenses', 'autonomous', 'splitPlan', 'splitWithFable', 'codeWorkspace',
  // A person's own review roles belong to the WORK, which is what a side is — beside `rounds`,
  // `thresholds` and `roleEnabled`, which ask the same question about the roles this product ships.
  // The prompt BODIES do NOT: they live in one data directory, because a body is the text of a
  // question rather than a configuration, and two sides asking one question is right.
  'roles',
];

/**
 * A reader that answers from this side's overlay first, and from the shared settings otherwise.
 *
 * <p>The fallback is what keeps a forked side working after an update: a setting added by a new
 * version is not in an overlay written by the old one, and reading `undefined` for it would turn a
 * new feature off on exactly the machines that had customised anything.</p>
 *
 * <p>`uiScale` and `helpLanguage` are deliberately NOT overlaid — they are about the person reading
 * the panel, not about the company the work belongs to, and a person who sets a text size wants it
 * in every window.</p>
 */
export function overlaidReader(shared: ConfigReader, overlay: SettingsOverlay): ConfigReader {
  return (section) =>
    Object.prototype.hasOwnProperty.call(overlay, section) ? overlay[section] : shared(section);
}

/**
 * The overlay a side starts with when the switch is turned on: exactly what it reads today.
 *
 * <p>So enabling the switch changes NOTHING until something is edited. The alternative — an empty
 * overlay that falls through to the shared values — looks identical until the first shared edit on
 * another side silently changes this one, which is the surprise this feature exists to remove.</p>
 */
export function seedOverlay(shared: ConfigReader): SettingsOverlay {
  const seeded: Record<string, unknown> = {};
  for (const section of OVERLAID_SETTINGS) {
    const value = shared(section);
    if (value !== undefined) {
      seeded[section] = value;
    }
  }

  return seeded;
}

/** VS Code config → a validated `CoaiSettings`; anything malformed falls back to the default. */
export function settingsFrom(read: ConfigReader): CoaiSettings {
  return {
    rounds: asRoleNumbers(read('rounds'), DEFAULTS.rounds, asPositive),
    thresholds: asRoleNumbers(read('thresholds'), DEFAULTS.thresholds, asCount),
    roleEnabled: asRoleFlags(read('roleEnabled'), DEFAULTS.roleEnabled),
    onExhausted: asOnExhausted(read('onExhausted')),
    maxConcurrency: asPositive(read('maxConcurrency'), DEFAULTS.maxConcurrency),
    maxPerProvider: asPositive(read('maxPerProvider'), DEFAULTS.maxPerProvider),
    reviewerTimeoutMinutes: asPositive(read('reviewerTimeoutMinutes'), DEFAULTS.reviewerTimeoutMinutes),
    // `asCount`, not `asPositive`: zero is the meaningful value here — it means "derive it".
    roundTimeoutMinutes: asCount(read('roundTimeoutMinutes'), DEFAULTS.roundTimeoutMinutes),
    credsKey: asString(read('credsKey')),

    escalationMinutes: asPositive(read('escalationMinutes'), DEFAULTS.escalationMinutes),
    promptsPerRound: asPromptRounds(read('promptsPerRound')),
    dealPlanLenses: read('dealPlanLenses') === true,
    dealCodeLenses: read('dealCodeLenses') === true,
    autonomous: read('autonomous') === true,
    splitPlan: read('splitPlan') === true,
    splitWithFable: read('splitWithFable') === true,
    codeWorkspace: read('codeWorkspace') === 'worktree' ? 'worktree' : 'none',
    roles: rolesFrom(read('roles')),
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
  // The rows go out exactly as they are stored: this setting IS the wire format, so there is no
  // shape to translate and no second schema to keep level. Empty emits nothing, which is what makes
  // a server older than 0.19.0 a non-event for everybody who has added no role of their own.
  if (settings.roles.length > 0) {
    env['COAI_ROLES'] = JSON.stringify(settings.roles);
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
  if (settings.codeWorkspace !== DEFAULTS.codeWorkspace) {
    env['COAI_CODE_WORKSPACE'] = settings.codeWorkspace;
  }
  // The three that make the gate give orders. Written only when ON, like every other switch here:
  // a file that carries only what differs from the defaults is one a person can read.
  if (settings.autonomous) {
    env['COAI_AUTONOMOUS'] = 'true';
  }
  if (settings.splitPlan) {
    env['COAI_SPLIT_PLAN'] = 'true';
  }
  if (settings.splitWithFable) {
    env['COAI_SPLIT_WITH_FABLE'] = 'true';
  }

  for (const [role, threshold] of Object.entries(settings.thresholds)) {
    if (threshold !== DEFAULTS.thresholds[role]) {
      env[`COAI_THRESHOLD_${role.toUpperCase()}`] = String(threshold);
    }
  }
  // Only the roles that are OFF, because every role is on by default and this file carries only what
  // differs. The key is spelled from the role ID exactly as the two loops above spell theirs, which
  // is what keeps it the same string the server reads — `envKeysMatchTheServer.test.ts` asserts it.
  for (const [role, on] of Object.entries(settings.roleEnabled)) {
    if (!on) {
      env[`COAI_ENABLED_${role.toUpperCase()}`] = 'false';
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
  if (settings.roundTimeoutMinutes !== DEFAULTS.roundTimeoutMinutes) {
    env['COAI_ROUND_TIMEOUT_MINUTES'] = String(settings.roundTimeoutMinutes);
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
/**
 * Whether this role takes part in a code round.
 *
 * <p>The plan role has no entry and never will, so it answers true — a caller asking about it is
 * asking a question the feature does not have, and the safe answer is the one that changes nothing.
 * The same is true of a role id nobody recognises.</p>
 */
export function roleIsOn(settings: CoaiSettings, role: string): boolean {
  return settings.roleEnabled[role] !== false;
}

/**
 * The code roles that will actually run — what the panel counts when it predicts a round.
 *
 * <p>Derived from the settings rather than from {@link ROLES}, because the fan-out sentence and the
 * round-limit note are promises about what is about to happen. A panel that says "up to eight
 * reviewers" while two roles are switched off is not describing this round.</p>
 */
export function enabledCodeRoles(settings: CoaiSettings): readonly string[] {
  // The COMPOSED catalog, not the shipped four. Until a person could write `coai.roles` those were
  // the same list; now a role they added is a reviewer this round will launch, and a count taken
  // from the shipped names alone both under-promises the fan-out and miscounts "the last role
  // standing" — telling somebody they cannot untick Architecture while a role of their own is still
  // running. Off by EITHER switch: `roleEnabled` is the sidebar's tick, `active` the catalog's, and
  // the server reads both.
  return composed(settings.roles)
    .filter((role) => stageOf(role) !== PLAN_STAGE
      && (role.programmingTask ?? true)
      && isActive(role)
      && roleIsOn(settings, role.id))
    .map((role) => role.id);
}

/**
 * A role-keyed record of switches, read one KEY at a time and ON unless a key says otherwise.
 *
 * <p>Per key rather than per record, which is what makes a partial stored object safe: a
 * configuration written before this setting existed has no keys at all, and one written the moment
 * somebody unticked Architecture has exactly one. Reading the record as a whole would turn both into
 * "everything off" — three reviewers silently not reviewing, with nothing on screen saying so.</p>
 *
 * <p>Only an explicit `false` disables. A string, a number, a null left by a hand-edited
 * settings.json — all of them leave the role working, which is the same asymmetry the server's env
 * parser applies: a role wrongly on costs one extra pass, a role wrongly off is a review nobody
 * performed.</p>
 */
function asRoleFlags(
  value: unknown,
  defaults: Readonly<Record<string, boolean>>,
): Readonly<Record<string, boolean>> {
  const stored = typeof value === 'object' && value !== null ? (value as Record<string, unknown>) : {};
  const out: Record<string, boolean> = {};
  for (const role of Object.keys(defaults)) {
    out[role] = stored[role] !== false;
  }

  return out;
}

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
