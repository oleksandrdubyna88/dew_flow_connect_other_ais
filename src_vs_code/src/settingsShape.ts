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
import { RESULT_CODE, bucketOf, composed, isActive, rolesFrom, type RoleRow } from './roles';
import {
  CALLER_KINDS,
  ConsultSettings,
  DEFAULT_CONSULT,
  ResolvedConsultant,
  consultSettingsFrom,
  CONSULT_SETTINGS,
  sameChoice,
} from './consultSettings';
import { CADENCE_SETTINGS, CadenceSettings, DEFAULT_CADENCE, cadenceEnv, cadenceSettingsFrom } from './cadenceSettings';
import { CommandModels, commandModelsEnv, commandModelsFrom } from './commandModels';
import { commandsFrom, type CommandRow } from './commands';

export type OnExhausted = 'continue' | 'escalate' | 'human' | 'good_enough';

/** How often split work is gated (issue #131) — `COAI_GATE_PER` on the wire. */
export type GatePer = 'epic' | 'task';

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

  /**
   * Which consultant each kind of CALLER gets when it is stuck, and the caps around a consultation.
   *
   * <p>Its own module (`consultSettings.ts`) rather than fields here, because the panel section and
   * the env block must read it through ONE function — the chat's four settings carry the same note
   * and the same reason. What differs is that these DO cross to the server: it decides which vendor
   * answers and it enforces the caps.</p>
   */
  readonly consult: ConsultSettings;

  /**
   * When the consultant is asked without anybody being stuck — once per group of epics, and on the
   * riskiest pieces of a big plan (research/PLAN_consult_on_a_cadence.md).
   *
   * <p>Its own module (`cadenceSettings.ts`) for the reason `consult` has one: the panel block and the
   * env block read it through ONE function.</p>
   */
  readonly cadence: CadenceSettings;

  /**
   * Which local model a ranking pass over the collected corpus would use.
   *
   * <p>A SETTING rather than a field on the panel, for the reason the panel's own header gives:
   * settings are written to VS Code configuration, not to a place of our own, so a person who
   * prefers the Settings UI gets the same value and it survives a reload. It was a private field
   * for one commit, which meant the picker wrote to configuration and the panel read from a field
   * nothing assigned — so choosing a model did nothing at all. (Code round, four reviewers.)</p>
   */
  readonly bugzModel: string;

  /** Where collected pairs would be sent. Asked for in a dialog; stored here like any other. */
  readonly bugzServer: string;

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
  /** The local reviewer stands down after a quiet cloud (issue #485). */
  readonly stopLocalWhenQuiet: boolean;
  /**
   * Do the split, and the stories where being wrong is expensive, with the caller's STRONGEST model.
   *
   * <p>The key's name is historical: it named Fable to every caller until issue #117 made the two
   * models a per-caller choice ({@link commandModels}). Renaming a stored key is a migration of every
   * settings file for a word nobody sees.</p>
   */
  readonly splitWithFable: boolean;
  /** Per caller kind, the models the model order names — what a person typed, trimmed (issue #117). */
  readonly commandModels: CommandModels;
  /**
   * How often split work comes back through the gate: once per EPIC (the default) or once for the
   * whole TASK — never per story, which cost a branch and two rounds a story (issue #131).
   */
  readonly gatePer: GatePer;
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

  /**
   * The gate commands this person added, as the rows `COAI_COMMANDS` carries (issue #467) — the wire
   * format, like `roles`. Empty is the normal state and emits no key at all.
   */
  readonly commands: readonly CommandRow[];
}

// How a changed control is routed lives in its own module; re-exported so every importer keeps its path.
export { settingMessageFrom, settingWrite, type ControlKind, type SettingMessage, type SettingWrite } from './settingRoute';

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

/** The defaults, matching the master plan's configuration table — pinned by tests. */
export const DEFAULTS: CoaiSettings = {
  // The operator's own settings, after a day of running the gate on this repository's real work
  // (2026-09-07). One plan round rather than three: the second and third rounds re-raise what the
  // first found rather than finding more, which the round records show. Architecture keeps two
  // because its first round is the conventions pass and its second is the broad question — two
  // different questions, not the same one twice. The thresholds went UP because they had been set
  // where a real change could not pass: a plan round that regularly produces six findings and a
  // code role that produces five are not failures, and a gate that says they are gets ignored,
  // which is the one failure mode a gate cannot survive.
  // The two document roles take the same shipped numbers as a code role, because the server's
  // fallback does: `ShippedFor` asks whether the role's STAGE is the plan stage, and a document
  // role's stage is `result`. A different number here would be a panel showing one budget while
  // the server ran another, which is the whole thing this object exists to prevent.
  // The feature role takes `PanelConfig.FeatureDefault`, the server's own instance of the code numbers
  // (S2.1); `panelServerDefaultsAgreement` reads that line of the C# rather than trusting this comment.
  rounds: { PlanCritique: 1, Conventions: 1, Architecture: 1, SecurityReliability: 1, UxDxPerformance: 1,
    DocumentReview: 1, DocumentSummary: 1, FeatureReview: 1 },
  thresholds: { PlanCritique: 6, Conventions: 5, Architecture: 5, SecurityReliability: 5, UxDxPerformance: 5,
    DocumentReview: 5, DocumentSummary: 5, FeatureReview: 5 },
  // Every code, document and feature role on. These keys are also what the reader iterates, so this
  // object is the list of roles that HAVE a switch — the plan role is absent from it deliberately.
  // The feature role's switch IS the feature gate's switch: there is no second setting (§4.2).
  roleEnabled: { Conventions: true, Architecture: true, SecurityReliability: true, UxDxPerformance: true,
    DocumentReview: true, DocumentSummary: true, FeatureReview: true },
  bugzModel: '',
  bugzServer: '',
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
  stopLocalWhenQuiet: false,
  splitWithFable: false,
  commandModels: commandModelsFrom(undefined),
  gatePer: 'epic',
  codeWorkspace: 'none',
  roles: [],
  commands: [],
  consult: DEFAULT_CONSULT,
  cadence: DEFAULT_CADENCE,
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
  'dealPlanLenses', 'dealCodeLenses', 'autonomous', 'splitPlan', 'splitWithFable', 'commandModels', 'gatePer', 'codeWorkspace', 'stopLocalWhenQuiet',
  // A person's own review roles belong to the WORK, which is what a side is — beside `rounds`,
  // `thresholds` and `roleEnabled`, which ask the same question about the roles this product ships.
  // The prompt BODIES do NOT: they live in one data directory, because a body is the text of a
  // question rather than a configuration, and two sides asking one question is right.
  'roles',
  // A person's own gate commands, beside their roles and for the same reason; their TEXTS, like the
  // prompt bodies, live in the one data directory.
  'commands',
  // The Bugz pair, per side for the reason every row above is: a side is the WORK. Two sides of one
  // machine can face different companies, and the local engine that may read their findings — and
  // the server those pairs would be sent to — are not the same question on both.
  'bugzModel', 'bugzServer',
  // Which consultant answers is a property of the WORK, not of the person reading the panel — two
  // sides of one machine serving two companies want their own, like every other row above. Spread
  // rather than listed, so adding a sixth consult setting cannot leave it silently shared.
  ...CONSULT_SETTINGS,
  // When the consultant is asked is a property of the work too, and spread for the same reason.
  ...CADENCE_SETTINGS,
];

/**
 * The settings a side keeps its own copy of WHATEVER the per-side switch says (issue #115).
 *
 * <p>Every entry of {@link OVERLAID_SETTINGS} is a fact about the WORK — which vendors review, which
 * roles run, which company's server they belong to — so sharing them across the sides of one machine
 * is a choice a person can reasonably make, and the switch is how they make it. These two are facts
 * about a side's FILESYSTEM. `Z:\coai` and `/mnt/z/coai` are one NAS and not one string, so a
 * Windows window and a WSL window pointed at the same mount need different values: a shared one is
 * wrong on at least one of them by construction rather than by preference.</p>
 *
 * <p>They are declared in the manifest all the same, so the shared value is what a side that has
 * never chosen falls back to, and so a person can read and edit them without opening a panel.</p>
 *
 * <p>Deliberately NOT part of {@link CoaiSettings}: `envBlock` turns that into the settings file the
 * server reads out of its data directory, and these two are the only settings that cannot live
 * there — the file is inside the directory they select.</p>
 */
export const ALWAYS_PER_SIDE: readonly string[] = ['dataDirectory', 'dataSide'];

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
    bugzModel: asString(read('bugzModel')),
    bugzServer: asString(read('bugzServer')),

    escalationMinutes: asPositive(read('escalationMinutes'), DEFAULTS.escalationMinutes),
    promptsPerRound: asPromptRounds(read('promptsPerRound')),
    dealPlanLenses: read('dealPlanLenses') === true,
    dealCodeLenses: read('dealCodeLenses') === true,
    autonomous: read('autonomous') === true,
    splitPlan: read('splitPlan') === true,
    stopLocalWhenQuiet: read('stopLocalWhenQuiet') === true,
    splitWithFable: read('splitWithFable') === true,
    commandModels: commandModelsFrom(read('commandModels')),
    gatePer: read('gatePer') === 'task' ? 'task' : 'epic',
    codeWorkspace: read('codeWorkspace') === 'worktree' ? 'worktree' : 'none',
    roles: rolesFrom(read('roles')),
    commands: commandsFrom(read('commands')),
    consult: consultSettingsFrom(read),
    cadence: cadenceSettingsFrom(read),
  };
}

/**
 * The `env` block for `mcpServers` — only what differs from the server's own defaults, so a
 * pristine configuration produces NO env at all and the block stays readable.
 */
export function envBlock(
  settings: CoaiSettings,
  vendors: readonly Vendor[] = DEFAULT_VENDORS,
  /**
   * The `coai-mcp` this side has, when known — it decides whether an `api` row may cross at all
   * (`vendorsEnv`). Empty means unknown, which is not old. Threaded from the writer rather than read
   * here, so this stays pure.
   */
  installedServerVersion = '',
): Record<string, string> {
  const env: Record<string, string> = {};
  if (!sameVendors(vendors, DEFAULT_VENDORS)) {
    env['COAI_VENDORS'] = vendorsEnv(vendors, installedServerVersion);
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
  // The same bargain for a person's own gate commands (issue #467): the rows ARE the wire format.
  if (settings.commands.length > 0) {
    env['COAI_COMMANDS'] = JSON.stringify(settings.commands);
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
  if (settings.stopLocalWhenQuiet) {
    env['COAI_STOP_LOCAL_WHEN_QUIET'] = 'true';
  }
  if (settings.splitWithFable) {
    env['COAI_SPLIT_WITH_FABLE'] = 'true';
  }
  // Only the caller kinds whose pair differs from the shipped one — a pristine panel adds nothing.
  Object.assign(env, commandModelsEnv(settings.commandModels));
  if (settings.gatePer !== DEFAULTS.gatePer) {
    env['COAI_GATE_PER'] = settings.gatePer;
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
  // The consultant's four, each only when it differs — so a pristine panel sends NOTHING and the
  // server's own fallback is what runs. That is what makes the two default sets one contract rather
  // than two numbers that happen to agree today; the gate's defaults diverged for a day once, and a
  // new install read one number off the screen while another one ran.
  //
  // Decided on the STORED map, never the resolved one. `consultSettingsFrom` resolves a legacy entry
  // on read, so a pristine map comes back as four definitions carrying the reviewer rows' values —
  // compared with the shipped pairs those are "different", and every untouched install would write
  // this key while the server, with no key, resolves the same absence to the same consultant.
  //
  // What CROSSES is the resolved entry — since story B4 of PLAN_the_consultant_has_its_own_vendors
  // measured a definition against the last released server (mcp-v0.22.0, 2026-09-15). That server's
  // DTO is `(Vendor, Model)`: `System.Text.Json` skips the three members it does not declare, and it
  // consulted through the reviewer row with the same id — that row's runtime, endpoint and CLI path,
  // with the entry's model — exactly as it did for the legacy pair, and refused a definition whose id
  // had no row as "not configured". The definition's own three fields are dropped without a word, so
  // `consultantSkewNote` says so in the section while such a server is installed, and
  // `research/module_server.md` holds the replies verbatim.
  //
  // PER CALLER: only an entry that differs from its shipped pair travels. Every caller used to
  // travel as a legacy pair whenever one differed, which cost nothing — the server resolved a shipped
  // pair to what it would have chosen with no key. A resolved DEFINITION is not that: it would
  // freeze this panel's reading of a caller nobody configured, so an untouched caller stays off the
  // wire and the server resolves its own absence, which `ConsultantRouting.For` does for a kind the
  // map lacks. A map with nothing to say emits no key, as before.
  const crossing = callersOnTheWire(settings.consult);
  if (crossing.length > 0) {
    // One JSON key rather than four scalars, for the reason `COAI_VENDORS` already carries: a
    // compound value needs a structured encoding, and four key spellings is four chances for the two
    // halves to disagree about one of them.
    env['COAI_CONSULTANTS'] = JSON.stringify(Object.fromEntries(
      crossing.map((id) => [id, wireEntry(settings.consult.byCaller[id]!)])));
  }
  if (settings.consult.turns !== DEFAULT_CONSULT.turns) {
    env['COAI_CONSULT_TURNS'] = String(settings.consult.turns);
  }
  if (settings.consult.callsPerSession !== DEFAULT_CONSULT.callsPerSession) {
    env['COAI_CONSULT_CALLS_PER_SESSION'] = String(settings.consult.callsPerSession);
  }
  if (settings.consult.idleMinutes !== DEFAULT_CONSULT.idleMinutes) {
    env['COAI_CONSULT_IDLE_MINUTES'] = String(settings.consult.idleMinutes);
  }
  // Written only when OFF, like every role switch: absent means on, and the server encodes that
  // structurally rather than by agreement.
  if (!settings.consult.enabled) {
    env['COAI_CONSULT_ENABLED'] = 'false';
  }
  // Only the cadence values that differ from the server's own — a pristine panel adds nothing.
  Object.assign(env, cadenceEnv(settings.cadence));
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
  // The Bugz pair. The panel passes the model on the command line when it starts a collect, but a
  // collect started from a TERMINAL has no panel to pass it — and a setting the server can never
  // see is a setting that silently does nothing, which the test beside this block exists to catch.
  // The collector refuses a non-local model wherever the value arrives from.
  if (settings.bugzModel) {
    env['COAI_BUGZ_MODEL'] = settings.bugzModel;
  }
  if (settings.bugzServer) {
    env['COAI_BUGZ_SERVER'] = settings.bugzServer;
  }
  return env;
}

/**
 * The callers whose STORED entry differs from the shipped pair — the ones `COAI_CONSULTANTS` carries.
 *
 * <p>Per caller through {@link sameChoice}, the same comparison `sameCallers` makes for the whole map,
 * so "differs" means one thing on both sides of the key. A stored DEFINITION always differs — its
 * runtime is set and a shipped pair's is not — so a caller a person has edited always travels.</p>
 */
function callersOnTheWire(consult: ConsultSettings): readonly string[] {
  return CALLER_KINDS
    .filter(({ id }) => !sameChoice(consult.stored[id], DEFAULT_CONSULT.stored[id]))
    .map(({ id }) => id);
}

/**
 * One caller's entry as the server's `ConsultantDto` reads it — a definition whole, an unavailable
 * entry raw.
 *
 * <p>In the DTO's DECLARED order, legacy pair first: `panelServerDefaultsAgreement` reads that order
 * off the C# and holds this level with it, and a reader older than the three new fields meets the two
 * it knows where it always met them. All five are written for a definition, empty strings included —
 * this is the wire, not the file a person edits (`consultantWrite.ts` drops empties there), and `vendorsEnv`
 * writes `COAI_VENDORS` the same way. An UNAVAILABLE entry travels as `{vendor, model}` with NO
 * runtime, so the server's rule (c) refuses it by name: a runtime invented here would be a definition
 * the server builds a provider for, sending a working tree to a vendor nobody chose.</p>
 */
function wireEntry(one: ResolvedConsultant): Record<string, string> {
  return one.kind === 'unavailable'
    ? { vendor: one.vendor, model: one.model }
    : { vendor: one.vendor, model: one.model, runtime: one.runtime, baseUrl: one.baseUrl, executablePath: one.executablePath };
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
  // the server reads both. By BUCKET, not "not the plan stage" (§9.8 of the feature-review plan): that
  // filter took every programming role outside the plan stage for code — the feature role included.
  return composed(settings.roles)
    .filter((role) => bucketOf(role) === RESULT_CODE && isActive(role) && roleIsOn(settings, role.id))
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
