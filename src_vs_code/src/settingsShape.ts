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

import { DEFAULT_VENDORS, VENDOR_PRESETS, Vendor, normaliseId, vendorsEnv } from './vendors';
import { PLAN_STAGE, composed, isActive, rolesFrom, stageOf, type RoleRow } from './roles';
import {
  CALLER_KINDS,
  ConsultantChoice,
  ConsultantDefinition,
  ConsultSettings,
  DEFAULT_CONSULT,
  ResolvedConsultant,
  consultableVendors,
  consultantChoiceFrom,
  consultSettingsFrom,
  CONSULT_SETTINGS,
  isChoiceField,
  resolveConsultant,
  sameChoice,
} from './consultSettings';

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

  /**
   * Which consultant each kind of CALLER gets when it is stuck, and the caps around a consultation.
   *
   * <p>Its own module (`consultSettings.ts`) rather than fields here, because the panel section and
   * the env block must read it through ONE function — the chat's four settings carry the same note
   * and the same reason. What differs is that these DO cross to the server: it decides which vendor
   * answers and it enforces the caps.</p>
   */
  readonly consult: ConsultSettings;

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
/**
 * <p>A FOURTH kind arrived with the consultant, and for the same reason the third did: its controls
 * are keyed by CALLER — which agent is stuck — and travelling in the vendor slot would have the
 * provider hunt for a vendor called `claude` when the row means "what Claude Code asks", and
 * sometimes find one.</p>
 */
export type SettingWrite =
  | { readonly kind: 'plain'; readonly key: string; readonly value: unknown }
  | { readonly kind: 'vendor'; readonly key: string; readonly value: unknown; readonly vendor: string }
  | { readonly kind: 'role'; readonly key: string; readonly value: unknown; readonly role: string }
  | { readonly kind: 'caller'; readonly key: string; readonly value: unknown; readonly caller: string };

/** What the webview said it changed. A message with no key changes nothing. */
export interface SettingMessage {
  readonly key: string | undefined;
  readonly value: unknown;
  readonly vendor?: string | undefined;
  readonly role?: string | undefined;
  readonly caller?: string | undefined;
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
  if (message.caller !== undefined && message.caller.length > 0) {
    return { kind: 'caller', key, value, caller: message.caller };
  }
  if (message.role !== undefined && message.role.length > 0) {
    return { kind: 'role', key, value, role: message.role };
  }
  if (message.vendor !== undefined && message.vendor.length > 0) {
    return { kind: 'vendor', key, value, vendor: message.vendor };
  }

  return { kind: 'plain', key, value };
}

/** The fields a control in the section edits in place. The vendor takes its own path: it re-resolves. */
type ConsultantField = 'model' | 'baseUrl' | 'executablePath';

/** Which field each control's setting key changes — one map, so an unknown key writes nothing. */
const CONSULTANT_FIELDS: Readonly<Record<string, ConsultantField>> = {
  consultModel: 'model',
  consultBaseUrl: 'baseUrl',
  consultExecutablePath: 'executablePath',
};

/**
 * One caller's consultant, merged into whatever the stored map already holds — as a DEFINITION.
 *
 * <p>Merged rather than replaced, exactly as a role record is: writing what Claude Code asks must
 * not drop the other three callers, and the stored object is what every other row reads on the next
 * repaint. It merges into the RAW `coai.consultants` object, so a caller kind this build has no name
 * for survives the write — a newer panel may know more of them, which is why the server's own
 * `Merge` keeps them too.</p>
 *
 * <p><b>What changed on 2026-09-14 (story A2 of `PLAN_the_consultant_has_its_own_vendors`): the
 * write stops storing a REFERENCE.</b> It used to put back `{vendor, model}`, where the vendor was a
 * reviewer row's id and everything else — the runtime, the endpoint, the CLI path, and the model when
 * none was named — was borrowed from that row on every read. Story A1 made the READ resolve that,
 * which fixed a consultant dying with a reviewer it never chose; but the file itself still held the
 * reference, so the consultant went on following the row. Here the first edit in the section writes
 * what will actually run: {@link resolveConsultant} against the rows this side can see, stored whole.
 * After it, the two settings are genuinely independent — editing the reviewer row moves the reviewer
 * and leaves the consultant where the person put it.</p>
 *
 * <p>A vendor change still CLEARS the model before resolving — a model named for one vendor is not a
 * model the next one offers — and what lands is then the model the new vendor will really use: its
 * row's, where a row lends one, and otherwise none. An id that is blank or only spaces is REFUSED:
 * it keys no vault entry and names no runtime, so the map comes back untouched rather than holding a
 * row nothing can run. An entry the rule cannot place stays a bare reference and gains no invented
 * runtime — only its model is editable, because guessing the rest would send a working tree to a
 * vendor nobody chose.</p>
 */
export function consultantRecordUpdate(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  key: string,
  value: unknown,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  // The caller arrives in a webview message, and the only kinds this build emits are its own four.
  // An id it does not know is refused rather than indexed with: `__proto__` through `current[caller]`
  // reads Object.prototype and would write a key nobody asked for. (gemini, A2's code round.)
  if (!CALLER_KINDS.some((one) => one.id === caller)) {
    return { ...current };
  }

  return key === 'consultVendor'
    ? vendorChosen(current, caller, String(value).trim(), vendors)
    : fieldEdited(current, caller, CONSULTANT_FIELDS[key], String(value).trim(), vendors);
}

/**
 * An endpoint of the consultant's own: the name and the URL a person typed, as THAT caller's definition.
 *
 * <p>Story C6, and the last entry the picker was missing. What the catalogue's blank preset IS is the
 * `codex` runtime at an endpoint, so that is what is stored — with the minted id as the vendor, which
 * is what keys the vault entry and the usage ledger. Nothing is appended to the reviewer rows: the
 * ruling is three independent sets of settings, and a consultant that added itself to somebody's
 * reviewers would be the coupling this plan removed, re-entering by the only door left open. Two
 * callers may hold the same id at the same URL, and that is one vault key used twice — the point of
 * having a name. (gemini, C6's plan round, on where the definition lands.)</p>
 *
 * <p>The id is normalised the way *Add a reviewer* normalises it, because the two flows must mint the
 * SAME id from the same words or one credential ends up under two keys. A name that normalises to
 * nothing writes nothing: there is no vault entry to key and nothing to show, so refusing is the only
 * honest outcome. So does a caller kind this build does not have — the same guard, and the same
 * reason, as `consultantRecordUpdate`'s.</p>
 */
export function consultantEndpointWrite(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  name: string,
  baseUrl: string,
): Record<string, unknown> {
  const id = normaliseId(name);
  // The runtime comes from the catalogue entry the person picked, never from here. Deciding it at
  // the write meant the picker could show that preset's label while the stored definition named
  // something else — one edit to `VENDOR_PRESETS` away from an endpoint launched through the wrong
  // CLI. (codex, C6's code round.)
  const { custom } = consultableVendors();
  if (id.length === 0 || custom === undefined || !CALLER_KINDS.some((one) => one.id === caller)) {
    return { ...current };
  }

  return merged(
    current,
    caller,
    { kind: 'definition', vendor: id, runtime: custom.runtime, model: '', baseUrl: baseUrl.trim(), executablePath: '' },
    undefined,
  );
}

/**
 * What two boxes MEAN once they are closed — and what a dismissal of either means, which is nothing.
 *
 * <p>Its own function because it is the only part of the flow that can be tested: the boxes
 * themselves are `vscode.window.showInputBox`, and a host is not something this suite has. A
 * dismissed box is `undefined` and a name that normalises to nothing keys no vault entry, so both
 * answer "no endpoint" and the caller writes nothing at all. The second box is the one worth naming:
 * a person who typed a name and then changed their mind has given no more consent than one who
 * closed the first, and an endpoint minted from a name alone would have no URL to reach.
 * (local, C6's plan round.)</p>
 */
export function endpointAnswer(
  name: string | undefined,
  baseUrl: string | undefined,
): { readonly id: string; readonly baseUrl: string } | undefined {
  const id = normaliseId(name ?? '');
  const where = (baseUrl ?? '').trim();

  return name === undefined || baseUrl === undefined || id.length === 0 || where.length === 0
    ? undefined
    : { id, baseUrl: where };
}

/**
 * Whether a name is already spoken for at a DIFFERENT endpoint — said while it is being typed.
 *
 * <p>One id is one vault key and one credential, so two endpoints under one name would send a key to
 * whichever of them answered. The check spans the reviewer ROWS and the other callers' consultants,
 * because both key the vault the same way; the same URL under the same name is not a conflict at all,
 * it is the same service named once. Returns the sentence to show, or empty for "go ahead" — a
 * validator's shape, so `showInputBox` can refuse while the box is open rather than after it closes.
 * (gemini, C6's plan round.)</p>
 */
export function endpointConflict(
  name: string,
  baseUrl: string,
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
  caller: string,
): string {
  const id = normaliseId(name);
  const wanted = baseUrl.trim();
  const clash = holders(vendors, consultants, caller)
    .find((one) => one.id.toLowerCase() === id.toLowerCase() && one.baseUrl !== wanted);

  return clash === undefined
    ? ''
    : `'${id}' is already ${clash.what}${clash.baseUrl.length > 0 ? ` at ${clash.baseUrl}` : ''}`
      + ' — one name is one key in the vault, so pick another name'
      + (clash.baseUrl.length > 0 ? ' or use that endpoint' : '');
}

/**
 * Why a typed endpoint is not one — or `undefined` when it is fine to go on.
 *
 * <p>`startsWith('http')` was the whole check, and it accepts `http-not-a-url`: the box closes, the
 * setting is stored, and the person learns it is wrong the next time they are stuck and a
 * consultation fails. Parsing is the only way to know, and `URL` is the parser both halves of this
 * product already trust. (codex, C6's code round.)</p>
 *
 * <p><b>A credential in the URL is refused rather than stored.</b> `https://user:token@host/v1` and
 * `?api_key=…` both work against many gateways, and both end up in `settings.json` — which for a
 * WORKSPACE setting is a file people commit. This product keeps keys in one CredsForDevs entry
 * precisely so they are never in argv, a log line or a settings file, and an endpoint box is not the
 * place to make an exception. The vault entry under this name is where the key goes. (codex, C6's
 * code round; I took the refusal and not the redaction — the URL a conflict names is one already in
 * this person's own settings, shown back to that same person.)</p>
 */
export function badEndpoint(typed: string): string | undefined {
  const text = typed.trim();
  let parsed: URL;
  try {
    parsed = new URL(text);
  } catch {
    return 'A base URL is needed — something like https://api.example.com/v1';
  }

  if (!['http:', 'https:'].includes(parsed.protocol) || parsed.hostname.length === 0) {
    return 'The endpoint has to be an http or https address';
  }

  return parsed.username.length > 0 || parsed.password.length > 0 || SECRETISH.some((key) => parsed.searchParams.has(key))
    ? 'Leave the key out of the URL — it goes in the vault entry under this name, which is what keeps it out of settings.json'
    : undefined;
}

/** Query names that carry a secret often enough that one in a stored URL is a mistake, not a choice. */
const SECRETISH: readonly string[] = ['api_key', 'apikey', 'key', 'token', 'access_token', 'password'];

/**
 * Everything that already means something by a name — and the caller doing the editing is not one.
 *
 * <p>Three holders, because all three key the vault the same way. The reviewer ROWS. The CATALOGUE,
 * which is the one no reviewer row need exist for: with no `deepseek` row configured a person could
 * name their own endpoint `deepseek`, and the next caller to pick DeepSeek out of the list would
 * share a vault key with a different service (codex, C6's code round). And the OTHER callers'
 * consultants — but not this caller's own, which is the record about to be replaced: counting it
 * made a person's own endpoint unchangeable for good, and told them a name they had chosen belonged
 * to somebody else (gemini, C6's code round, from two roles).</p>
 *
 * <p>A holder with no endpoint of its own still holds the NAME — `claude` is a catalogue preset and
 * a vault key — so it clashes, and the sentence then stops short of telling anyone to use an
 * endpoint there is none of.</p>
 */
function holders(
  vendors: readonly Vendor[],
  consultants: Readonly<Record<string, unknown>>,
  caller: string,
): readonly { readonly id: string; readonly baseUrl: string; readonly what: string }[] {
  return [
    ...vendors.map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: 'a reviewer' })),
    ...VENDOR_PRESETS.filter((one) => one.id.length > 0)
      .map((one) => ({ id: one.id, baseUrl: one.baseUrl, what: `what this build calls ${one.label}` })),
    ...Object.entries(consultants)
      .filter(([whose]) => whose !== caller)
      .map(([, one]) => consultantChoiceFrom(one))
      .map((one) => ({ id: one.vendor, baseUrl: one.baseUrl, what: "another caller's consultant" })),
  ];
}

/**
 * The row a write starts FROM — and an absent one means the caller's shipped pair, not a blank.
 *
 * <p>The reader's own rule, mirrored: `callers` reads a row with no vendor as `DEFAULT_CONSULT.stored`
 * for that kind, because a caller nobody has configured is one running the shipped default. Starting
 * from a blank instead resolved to UNAVAILABLE and stored `{vendor: '', model: …}` — which the very
 * next read threw away, taking the person's edit with it. A side that holds a row for one caller and
 * nothing for another is ordinary: the panel writes the caller somebody edited, so a workspace
 * overlay holds exactly the rows touched there. (gemini, A2's code round, twice.)</p>
 */
function startingChoice(current: Readonly<Record<string, unknown>>, caller: string): ConsultantChoice {
  const stored = consultantChoiceFrom(current[caller]);

  return stored.vendor.length > 0 ? stored : DEFAULT_CONSULT.stored[caller]!;
}

/**
 * A vendor arriving from the picker — a CHANGE clears the model, the same choice again changes nothing.
 *
 * <p>Clearing is for a change: a model named for one vendor is not a model the next one offers. Sent
 * the vendor already chosen — a re-selection, or a message delivered twice — the old code built a
 * fresh bare reference and re-resolved it, handing the reviewer row's model and endpoint back over
 * whatever the person had set. The consultant is not a reference to that row any more, so nothing may
 * be re-lent to it. (codex, A2's code round.)</p>
 *
 * <p><b>That guard is for a DEFINITION, and narrowing it to one is this story's code round.</b> A
 * stored entry can be a bare REFERENCE whose id the catalogue also offers: `deepseek` names no
 * runtime and needs no reviewer row, so without one it resolves to UNAVAILABLE while the picker
 * offers it two lines below as a working choice. Keeping the reference because the id matched meant
 * a person clicked the vendor their own row was already showing them and nothing happened — same
 * reference in, same unplaceable row back. There is nothing of theirs to protect in a reference: it
 * holds no model they chose, no endpoint and no CLI path. So a re-selection MATERIALISES it, which
 * is what picking a catalogue entry has meant since C5. (codex, this story's code round.)</p>
 */
function vendorChosen(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  id: string,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  const starting = startingChoice(current, caller);
  if (id.length === 0) {
    return { ...current };
  }

  return id === starting.vendor && starting.runtime !== ''
    ? merged(current, caller, resolveConsultant(starting, vendors), current[caller])
    : merged(current, caller, chosen(id, vendors), undefined);
}

/**
 * What a newly picked vendor MEANS — the catalogue first, since story C5.
 *
 * <p>The picker offers the catalogue now, so what a person chose is a CATALOGUE ENTRY, and the entry
 * says what it is: DeepSeek is the codex runtime at `api.deepseek.com`, OpenRouter is the codex
 * runtime at theirs. Resolving the bare id instead — which is all this did before — sent both of them
 * to the UNAVAILABLE state, because no reviewer row of that name exists and neither id is a runtime:
 * the section offered an entry that could not be stored.</p>
 *
 * <p>It also settles a question the ruling had already answered. For an id that IS a runtime name,
 * this stores the plain runtime rather than the model of a reviewer row with the same name. That is
 * the point of the whole plan — three independent sets of settings — and it is the last place the
 * consultant was still reaching into somebody's reviewer. A person who wants that model chooses it
 * in the box beside the vendor, where they can see it.</p>
 */
function chosen(id: string, vendors: readonly Vendor[]): ResolvedConsultant {
  const preset = consultableVendors().offered.find((one) => one.id === id);

  return preset === undefined
    ? resolveConsultant(bareReference(id), vendors)
    : {
      kind: 'definition',
      vendor: preset.id,
      runtime: preset.runtime,
      model: preset.model,
      baseUrl: preset.baseUrl,
      executablePath: preset.executablePath,
    };
}

function fieldEdited(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  field: ConsultantField | undefined,
  value: string,
  vendors: readonly Vendor[],
): Record<string, unknown> {
  return field === undefined
    ? { ...current }
    : merged(
      current,
      caller,
      edited(resolveConsultant(startingChoice(current, caller), vendors), field, value),
      current[caller],
    );
}

/** A vendor id with nothing else claimed — what a person picking from the list has actually said. */
function bareReference(vendor: string): ConsultantChoice {
  return { vendor, runtime: '', model: '', baseUrl: '', executablePath: '' };
}

/**
 * The caller's row, replaced by what the rule answered — with anything this build cannot name kept.
 *
 * <p>`keep` is the row as stored, and it is passed for an edit to one FIELD and withheld for a vendor
 * CHANGE. The outer map already survives a caller kind this build has no name for; a field inside a
 * row deserves the same, because a newer panel may hold one and an edit beside it must not delete it.
 * A vendor change is a fresh start, so anything the old vendor had goes with it — otherwise DeepSeek's
 * endpoint would still be sitting under a Claude consultant. (codex, A2's code round.)</p>
 */
function merged(
  current: Readonly<Record<string, unknown>>,
  caller: string,
  one: ResolvedConsultant,
  keep: unknown,
): Record<string, unknown> {
  return { ...current, [caller]: { ...unnamedFields(keep), ...storedShape(one) } };
}

function unnamedFields(row: unknown): Record<string, unknown> {
  const stored = typeof row === 'object' && row !== null && !Array.isArray(row) ? row as Record<string, unknown> : {};

  return Object.fromEntries(Object.entries(stored).filter(([key]) => !isChoiceField(key)));
}

/**
 * What a resolved consultant looks like in `settings.json`.
 *
 * <p>An endpoint and a CLI path are written only when they hold something: absent and empty mean the
 * same thing to every reader of this setting — the CLI's own endpoint, and whatever is on PATH — and
 * a file a person edits by hand is worth keeping readable. The runtime is always written, because it
 * is the one field that tells a definition from the legacy reference this used to store.</p>
 */
function storedShape(one: ResolvedConsultant): Record<string, unknown> {
  return one.kind === 'unavailable'
    ? { vendor: one.vendor, model: one.model }
    : {
      vendor: one.vendor,
      runtime: one.runtime,
      model: one.model,
      ...(one.baseUrl.length > 0 ? { baseUrl: one.baseUrl } : {}),
      ...(one.executablePath.length > 0 ? { executablePath: one.executablePath } : {}),
    };
}

/** One field replaced. An unplaceable entry admits only its model — nothing else has a meaning yet. */
function edited(one: ResolvedConsultant, field: ConsultantField, value: string): ResolvedConsultant {
  if (one.kind === 'definition') {
    return editedDefinition(one, field, value);
  }

  return field === 'model' ? { ...one, model: value } : one;
}

function editedDefinition(one: ConsultantDefinition, field: ConsultantField, value: string): ResolvedConsultant {
  return {
    kind: 'definition',
    vendor: one.vendor,
    runtime: one.runtime,
    model: field === 'model' ? value : one.model,
    baseUrl: field === 'baseUrl' ? value : one.baseUrl,
    executablePath: field === 'executablePath' ? value : one.executablePath,
  };
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
  // The two document roles take the same shipped numbers as a code role, because the server's
  // fallback does: `ShippedFor` asks whether the role's STAGE is the plan stage, and a document
  // role's stage is `result`. A different number here would be a panel showing one budget while
  // the server ran another, which is the whole thing this object exists to prevent.
  rounds: { PlanCritique: 1, Conventions: 1, Architecture: 1, SecurityReliability: 1, UxDxPerformance: 1,
    DocumentReview: 1, DocumentSummary: 1 },
  thresholds: { PlanCritique: 6, Conventions: 5, Architecture: 5, SecurityReliability: 5, UxDxPerformance: 5,
    DocumentReview: 5, DocumentSummary: 5 },
  // Every code and document role on. These keys are also what the reader iterates, so this object
  // is the list of roles that HAVE a switch — the plan role is absent from it deliberately.
  roleEnabled: { Conventions: true, Architecture: true, SecurityReliability: true, UxDxPerformance: true,
    DocumentReview: true, DocumentSummary: true },
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
  consult: DEFAULT_CONSULT,
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
  // Which consultant answers is a property of the WORK, not of the person reading the panel — two
  // sides of one machine serving two companies want their own, like every other row above. Spread
  // rather than listed, so adding a sixth consult setting cannot leave it silently shared.
  ...CONSULT_SETTINGS,
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

    escalationMinutes: asPositive(read('escalationMinutes'), DEFAULTS.escalationMinutes),
    promptsPerRound: asPromptRounds(read('promptsPerRound')),
    dealPlanLenses: read('dealPlanLenses') === true,
    dealCodeLenses: read('dealCodeLenses') === true,
    autonomous: read('autonomous') === true,
    splitPlan: read('splitPlan') === true,
    splitWithFable: read('splitWithFable') === true,
    codeWorkspace: read('codeWorkspace') === 'worktree' ? 'worktree' : 'none',
    roles: rolesFrom(read('roles')),
    consult: consultSettingsFrom(read),
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
 * this is the wire, not the file a person edits (`storedShape` drops empties there), and `vendorsEnv`
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
