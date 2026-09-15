/**
 * The consultant a caller gets, and the two caps around it.
 *
 * <p>Pure and `vscode`-free, so every fallback below is a unit test rather than a claim. ONE reader,
 * used by the panel section and by the env block alike: the chat feature's `chatSettingsFrom` is the
 * precedent, and the reason is written into it — what the panel SHOWS and what the server RUNS cannot
 * disagree, because neither has a reader of its own.</p>
 *
 * <p><b>These settings DO cross to the server</b>, unlike the chat's four. The server is what decides
 * which vendor a caller gets and enforces the caps; the panel only edits them.</p>
 */

import { compareVersions } from './coaiInstall';
import { Runtime, RUNTIMES } from './models';
import { Vendor, vendorsFrom } from './vendors';

/**
 * The callers a consultant can be configured for — the kinds `CallerIdentity.KindFrom` answers.
 *
 * <p>`other` is not a leftover: an MCP client that exports none of the three session variables is a
 * real and ordinary case (a script, a different editor), and it needs a consultant like any other.</p>
 */
export const CALLER_KINDS: readonly { id: string; label: string }[] = [
  { id: 'claude', label: 'Claude Code' },
  { id: 'codex', label: 'Codex' },
  { id: 'gemini', label: 'Gemini' },
  { id: 'other', label: 'Another client' },
];

/**
 * What a caller's consultant IS — not the name of somebody's reviewer.
 *
 * <p>Until 2026-09-14 this was `{ vendor, model }`, and `vendor` was a REVIEWER ROW's id: the
 * consultant borrowed that row's runtime, endpoint and CLI path, and an empty model meant the row's
 * own. So a reviewer removed took the consultant with it, a reviewer switched off refused a
 * consultation nobody had switched off, and the shipped `codex → claude` was dead on every machine
 * without a `claude` reviewer row — the opening symptom of `PLAN_the_consultant_has_its_own_vendors`.
 * The chat made the same move for the same reason: `ModelPreset` carries the whole of what it needs
 * to run, and its type comment records the operator asking five times.</p>
 *
 * <p>`vendor` stays, because the vault entry and the usage ledger are keyed by it — a credential is
 * not a setting. URL, model and CLI path are, and those three are now the consultant's own.</p>
 *
 * <p><b>`runtime: ''` means a LEGACY reference</b>: an entry written before these fields existed, or
 * one of the shipped pairs. {@link resolveConsultant} is the one place that says what such an entry
 * means.</p>
 */
export interface ConsultantChoice {
  /** The id — names the vault entry, the usage ledger, and every refusal. */
  readonly vendor: string;
  /** Which CLI answers. `''` = a legacy reference, resolved by {@link resolveConsultant}. */
  readonly runtime: Runtime | '';
  /** Empty = the runtime's own default. */
  readonly model: string;
  /** For a vendor riding the codex CLI. Empty = the CLI's own endpoint. */
  readonly baseUrl: string;
  /** Where the CLI is. Empty = look it up on PATH. */
  readonly executablePath: string;
}

/** A choice whose runtime is known — every field of {@link ConsultantChoice}, with `''` ruled out. */
export interface ConsultantDefinition extends ConsultantChoice {
  readonly runtime: Runtime;
}

export interface ConsultSettings {
  /**
   * Caller kind → what its consultant RESOLVES to. Every kind in {@link CALLER_KINDS} is present.
   *
   * <p>What the section draws and what a consultation will run on — the RESULT of
   * {@link resolveConsultant}, never a look-alike choice. A definition carries its runtime; an
   * UNAVAILABLE entry (a legacy reference that matched no reviewer row and names no consulting
   * runtime) carries the stored vendor and model, raw, and the `why` a person can act on. The first
   * cut of this map handed back the STORED choice for an unavailable entry, so a resolved definition
   * and an unresolved legacy entry had one type and the reason was thrown away — `resolveAll` says
   * what that cost. {@link DEFAULT_CONSULT}'s `byCaller` is the shipped pairs resolved against no
   * rows; its docblock says what that means.</p>
   */
  readonly byCaller: Readonly<Record<string, ResolvedConsultant>>;
  /**
   * Caller kind → the entry as STORED — trimmed, unresolved, the shipped pair where absent.
   *
   * <p>Two maps rather than one, because of the default comparison. "Is this what the server would
   * do with no configuration at all?" is a question about what is STORED: a pristine map resolved
   * against customised reviewer rows is four definitions that differ from the shipped pairs in every
   * field, and is still exactly what the server runs with no key — both halves resolve absence
   * identically. Comparing the resolved side would have every untouched install writing
   * `COAI_CONSULTANTS`, which `panelServerDefaultsAgreement.test.ts` forbids. The wire reads this
   * side too, until story B4 has measured a definition against an OLD server half: resolution can
   * change a MODEL (rule (a) materialises the row's), and a byte that changes on the wire before it
   * is measured is the skew that plan exists to catch.</p>
   */
  readonly stored: Readonly<Record<string, ConsultantChoice>>;
  /** Turns one consultation may take before it closes. */
  readonly turns: number;
  /** Consult calls one caller session may make in its window. */
  readonly callsPerSession: number;
  /** How long an open consultation may sit unasked before its vendor handle is dropped. */
  readonly idleMinutes: number;
  /** Whether the tool answers at all. Off is a named refusal, not a silent no-op. */
  readonly enabled: boolean;
}

/**
 * The runtimes that can hold a consultation — the extension's copy of `ConsultantResolution.Consulting`.
 *
 * <p>A mirror, and a test asserts it against the C#. The alternative was asking the server, which
 * cannot answer before it is installed — and this list decides what a picker OFFERS, which has to be
 * drawable the first time the panel opens.</p>
 *
 * <p>Typed as runtimes rather than strings since {@link resolveConsultant} rule (b) resolves an id
 * INTO one of them: a `find` over this list is then a `Runtime` with no cast standing in for a type.</p>
 *
 * <p>Declared ABOVE {@link DEFAULT_CONSULT}, and that is load-bearing: that constant resolves the
 * shipped pairs through rule (b) while the module is loading, and a `const` read before its own line
 * is a `ReferenceError`, not `undefined`.</p>
 */
export const CONSULTING_RUNTIMES: readonly Runtime[] = ['codex', 'claude', 'antigravity', 'local'];

/**
 * A different vendor for every caller, by default.
 *
 * <p>A stuck agent has a blind spot by definition and the one model that cannot see it is the one
 * that produced it — so the shipped map never sends a caller to itself. The same vendor is allowed as
 * an explicit CHOICE, because Fable answering a Sonnet session is a stronger model rather than the
 * same one; the panel says so beside the row, since the server can see the caller's vendor and never
 * its model.</p>
 *
 * <p><b>These four pairs are one contract with `ConsultantRouting.Shipped` in the server.</b> The
 * panel writes a key only when it DIFFERS from this map, so a pristine install sends nothing and the
 * server's own fallback is what runs. A test reads the C# to hold the two level — the gate defaults
 * diverged for a day once, and a new install read one number off the screen while another ran.</p>
 *
 * <p><b>Legacy-shaped, deliberately: byte-for-byte the pairs the server ships.</b> `runtime: ''` on
 * each is what makes them resolve on read — the shipped `codex → claude` reaches a Claude CLI with no
 * `claude` reviewer row and no write — and it is what keeps the sibling plan's VALUES
 * (`PLAN_consultant_defaults_from_phase_0.md`) untouched by the plan that changed the shape.</p>
 */
const SHIPPED_PAIRS: Readonly<Record<string, ConsultantChoice>> = {
  claude: { vendor: 'codex', runtime: '', model: '', baseUrl: '', executablePath: '' },
  codex: { vendor: 'claude', runtime: '', model: '', baseUrl: '', executablePath: '' },
  gemini: { vendor: 'codex', runtime: '', model: '', baseUrl: '', executablePath: '' },
  other: { vendor: 'codex', runtime: '', model: '', baseUrl: '', executablePath: '' },
};

/**
 * The shipped configuration as a value: the four legacy pairs, and what they resolve to with no rows.
 *
 * <p>`byCaller` is not the pairs again. It holds {@link ResolvedConsultant}s, so it has to say what
 * the shipped pairs MEAN — and with no reviewer row in hand every one of them means the same thing by
 * rule (b): a definition on the runtime its id names, `codex` on the codex CLI and `claude` on the
 * Claude CLI, borrowing no model, endpoint or path. That is truthful twice over. It is what
 * `resolveAll` answers for these pairs against an empty list; and — pinned by a test — it is what a
 * fresh install READS, because the shipped `codex` reviewer row carries no model, endpoint or path
 * that a bare runtime would not. A tuned row changes a real read (rule (a) materialises its model),
 * and this value never learns of it: it is the default, not the panel.</p>
 */
export const DEFAULT_CONSULT: ConsultSettings = {
  byCaller: resolveAll(SHIPPED_PAIRS, []),
  stored: SHIPPED_PAIRS,
  turns: 5,
  callsPerSession: 10,
  idleMinutes: 15,
  enabled: true,
};

/** A raw configuration reader, as `settingsShape` defines it. */
type Read = (section: string) => unknown;

/**
 * The five settings this feature owns, parsed from whatever `settings.json` actually holds.
 *
 * <p><b>FIVE stored keys, one value.</b> `coai.consultants` holds the caller map and each cap is a
 * setting of its own — the shape the chat feature already uses, and the shape of the wire: five
 * settings, five `COAI_CONSULT*` env keys, one for one. One nested object would have read better in
 * this file and worse everywhere else: VS Code describes and completes a declared key, the panel's
 * ordinary write path stores one, and the per-side overlay copies one. It cannot do any of the three
 * for a field buried inside an object.</p>
 *
 * <p>Key by key rather than object by object, for the reason `asRoleFlags` already carries: a record
 * written before a key existed, or holding only the caller somebody changed, must still answer for
 * every other caller. Reading the whole map or nothing would turn three consultants off because a
 * fourth was edited.</p>
 *
 * <p><b>A legacy entry is resolved HERE, on every read, and nothing is written back.</b> A migration
 * that runs when the section is edited never runs for the person who never edits it — and the
 * opening symptom of `PLAN_the_consultant_has_its_own_vendors` was a panel nobody had touched.
 * Resolving on read fixes it with no write at all, and identically on both halves once the server
 * applies the same rule (story B3); the first edit in the section is what writes a definition back
 * (story A2). The rows it resolves against are the ones THIS reader sees through `vendorsFrom` — the
 * same parse the Reviewers section draws — so a row a person can see is a row a legacy entry can
 * borrow from.</p>
 */
export function consultSettingsFrom(read: Read): ConsultSettings {
  const stored = callers(asRecord(read('consultants')));

  return {
    byCaller: resolveAll(stored, vendorsFrom(read('vendors'))),
    stored,
    turns: positive(read('consultTurns'), DEFAULT_CONSULT.turns),
    callsPerSession: positive(read('consultCallsPerSession'), DEFAULT_CONSULT.callsPerSession),
    idleMinutes: positive(read('consultIdleMinutes'), DEFAULT_CONSULT.idleMinutes),
    // Only a stored `false` switches it off, and absence means ON — the asymmetry the role switches
    // already use, for the same reason: a consultant wrongly available costs nothing because nobody
    // calls it, while one wrongly unavailable is a refusal in the one moment it was wanted.
    enabled: read('consultEnabled') !== false,
  };
}

/** The stored keys this feature owns, so no list of them is written out twice. */
export const CONSULT_SETTINGS: readonly string[] = [
  'consultants', 'consultTurns', 'consultCallsPerSession', 'consultIdleMinutes', 'consultEnabled',
];

/**
 * Whether this is exactly what the server would do with no configuration at all.
 *
 * <p>Read off the STORED map, never the resolved one — {@link ConsultSettings.stored} says why.</p>
 */
export function isDefaultConsult(settings: ConsultSettings): boolean {
  return settings.turns === DEFAULT_CONSULT.turns
    && settings.callsPerSession === DEFAULT_CONSULT.callsPerSession
    && settings.idleMinutes === DEFAULT_CONSULT.idleMinutes
    && settings.enabled === DEFAULT_CONSULT.enabled
    && sameCallers(settings.stored, DEFAULT_CONSULT.stored);
}

/**
 * Every field a choice has, as a value the compiler holds level with the type in BOTH directions.
 *
 * <p>This used to be a hand-written `readonly (keyof ConsultantChoice)[]`, which catches a field
 * REMOVED from the type — the name stops compiling — and never one ADDED: a sixth consultant-owned
 * field, parsed by `entryFrom` and written by the panel but missing from the list, would have
 * {@link sameCallers} call two choices that differ only in it equal, and `envBlock` would keep the
 * changed setting off the wire with nothing saying so. A `Record<keyof ConsultantChoice, true>` must
 * name every key or it does not compile, so the list below is the type's keys or nothing. (codex,
 * A1's code round.)</p>
 */
const CHOICE_SHAPE: Readonly<Record<keyof ConsultantChoice, true>> = {
  vendor: true, runtime: true, model: true, baseUrl: true, executablePath: true,
};

/**
 * The keys of {@link CHOICE_SHAPE} as names. Typed by a predicate rather than a cast, because
 * `Object.keys` is `string[]` by design — an object may hold more keys than its type admits — and a
 * literal we wrote ourselves is the one case where that cannot happen.
 */
export const CHOICE_FIELDS: readonly (keyof ConsultantChoice)[] = Object.keys(CHOICE_SHAPE).filter(isChoiceField);

/**
 * Whether a name is one of the fields a choice has — the ONE answer to that question.
 *
 * <p>Exported because the write path asks it too, of the keys a stored row holds, to tell the fields
 * it manages from a field a newer panel wrote that it must not delete. A second spelling there would
 * be a second list to keep level with this type, which is the whole defect {@link CHOICE_SHAPE}
 * exists to close.</p>
 */
export function isChoiceField(name: string): name is keyof ConsultantChoice {
  return Object.hasOwn(CHOICE_SHAPE, name);
}

/**
 * Whether two caller maps hold the same choice for every caller kind — all five fields.
 *
 * <p>Five, not two: a definition that differs from another only in its endpoint or its CLI path is a
 * different consultant, and a definition is not the legacy pair it resolves from. Keys this build
 * has no caller kind for are ignored, as before.</p>
 */
export function sameCallers(
  one: Readonly<Record<string, ConsultantChoice>>,
  other: Readonly<Record<string, ConsultantChoice>>,
): boolean {
  return CALLER_KINDS.every(({ id }) => sameChoice(one[id], other[id]));
}

/**
 * One caller's choice against another, over every field in {@link CHOICE_FIELDS}.
 *
 * <p>Every field, because a definition that differs only in its endpoint or its CLI path is a
 * different consultant. An ABSENT entry compares as all-empty rather than as "different": in
 * `coai.consultants` a caller kind with no key and a caller kind whose vendor is blank both mean
 * "no choice made" — `callers` reads either as the shipped pair — so a map that lacks a kind must
 * equal one that holds that kind's empty choice, not differ from everything.</p>
 *
 * <p>Exported since story B4, because `envBlock` asks it per CALLER: only an entry that differs from
 * its shipped pair travels, and the whole-map question {@link sameCallers} answers is not the
 * per-caller one. One comparison for both, so the two cannot disagree about what "differs" means.</p>
 */
export function sameChoice(one: ConsultantChoice | undefined, other: ConsultantChoice | undefined): boolean {
  return CHOICE_FIELDS.every((field) => (one?.[field] ?? '') === (other?.[field] ?? ''));
}

/** What a stored entry means: a consultant this build can describe in full, or one it cannot place. */
export type ResolvedConsultant =
  | ({ readonly kind: 'definition' } & ConsultantDefinition)
  | { readonly kind: 'unavailable'; readonly vendor: string; readonly model: string; readonly why: string };

/**
 * The one resolution rule — what a stored entry MEANS, stated once for the reader, the section, the
 * wire, and (mirrored in C#, story B3) the server.
 *
 * <p>A definition (`runtime` present) is itself. A legacy reference resolves in this order:</p>
 * <ol>
 *   <li><b>(a)</b> a reviewer row with that id, <b>enabled or disabled</b> → that row's runtime, its
 *   model unless the entry names one, its endpoint, its CLI path. That is the meaning the entry
 *   always had, materialised: `ConsultationService` borrowed exactly these four from the row.
 *   Disabled counts, because a reviewer switched off is a fact about REVIEWS and it took the
 *   consultant down with it — the opening symptom. The id is matched case-insensitively because that
 *   is how the server has always looked the row up (`StringComparison.OrdinalIgnoreCase`) while
 *   `vendorsFrom` lower-cases ids: a hand-written `Codex` consulted fine and was drawn as "not
 *   configured".</li>
 *   <li><b>(b)</b> else an id that is itself a consulting runtime (`codex`, `claude`, `antigravity`,
 *   `local`) → that runtime, the entry's model or empty, no endpoint, no CLI path. This is the rule
 *   the server's `RuntimeResolution.NameOf` already applies, and it is what makes the shipped
 *   `codex → claude` reach a Claude CLI on a machine with no `claude` reviewer row.</li>
 *   <li><b>(c)</b> else UNAVAILABLE: the raw entry preserved — vendor and model as stored, never
 *   rewritten, never defaulted — with a reason a person can act on. The server refuses it by name;
 *   the section says what to do.</li>
 * </ol>
 *
 * <p><b>Materialising is not permitting.</b> Rule (a) hands back whatever runtime the row is on,
 * `remote` included: whether that runtime may hold a consultation is {@link CONSULTING_RUNTIMES}'
 * question, asked where a consultation is offered or run — and a definition that names its runtime
 * is what lets that refusal name it too.</p>
 *
 * <p><b>The id is kept in (a) and canonicalised in (b), and the asymmetry is the point.</b> In (a)
 * the id names a ROW a person created; it keys their vault entry and the usage ledger, and a
 * resolution that rewrote it would move a credential — so `Codex` stays `Codex`. In (b) the id names
 * a RUNTIME, and `codex` and `Codex` are the same one; meanwhile `vendorsFrom` lower-cases every row
 * id, so a `Claude` kept in its stored casing would key a vault entry and a ledger line that no
 * reviewer row can ever share, one letter away from every `claude` the rest of the panel can produce.
 * The matched runtime's own name is the id. Rule (c) rewrites nothing: the entry is shown back to the
 * person exactly as they stored it. (A1's code round.)</p>
 *
 * <p>Pure, and a fixed point: what it produces resolves to itself against the same rows, so the
 * reader's output can be resolved again — by a section holding no rows at all — and mean the same.</p>
 */
export function resolveConsultant(choice: ConsultantChoice, vendors: readonly Vendor[]): ResolvedConsultant {
  if (choice.runtime !== '') {
    return { kind: 'definition', ...choice, runtime: choice.runtime };
  }
  const wanted = choice.vendor.toLowerCase();
  const row = vendors.find((one) => one.id.toLowerCase() === wanted);
  if (row !== undefined) {
    return fromRow(choice, row);
  }
  const runtime = CONSULTING_RUNTIMES.find((one) => one === wanted);

  return runtime === undefined ? unavailable(choice) : fromRuntime(choice, runtime);
}

/** Rule (a): the row's runtime, endpoint and CLI path; the row's model only where the entry names none. */
function fromRow(choice: ConsultantChoice, row: Vendor): ResolvedConsultant {
  return {
    kind: 'definition',
    vendor: choice.vendor,
    runtime: row.runtime,
    model: choice.model.length > 0 ? choice.model : row.model,
    baseUrl: row.baseUrl,
    executablePath: row.executablePath,
  };
}

/** Rule (b): the runtime the id names — under that runtime's own name — and nothing borrowed. */
function fromRuntime(choice: ConsultantChoice, runtime: Runtime): ResolvedConsultant {
  return { kind: 'definition', vendor: runtime, runtime, model: choice.model, baseUrl: '', executablePath: '' };
}

/** Rule (c): the entry exactly as it is, and what to do about it. */
function unavailable(choice: ConsultantChoice): ResolvedConsultant {
  return {
    kind: 'unavailable',
    vendor: choice.vendor,
    model: choice.model,
    why: `no reviewer is named '${choice.vendor}' and it is not a runtime this build can consult with `
      + `(${CONSULTING_RUNTIMES.join(', ')}) — choose another consultant for this caller, or add a reviewer under that name`,
  };
}

/**
 * The runtimes that ARE a caller kind, which is not the same question as sharing its name.
 *
 * <p>`gemini` is the case that proves it: the caller kind is `gemini` and the vendor row that runs
 * Gemini models is on the `antigravity` runtime, so a name-to-name comparison silently withheld the
 * warning from the one caller most likely to be pointed back at itself. The retired `gemini` runtime
 * is kept beside it because a row configured before it was retired still exists in people's
 * settings. Raised twice on this story's code round, by the same reviewer in two roles.</p>
 */
const CALLER_RUNTIMES: Readonly<Record<string, readonly string[]>> = {
  claude: ['claude'],
  codex: ['codex'],
  gemini: ['antigravity', 'gemini'],
  // A client that exports none of the three session variables is not any vendor, so nothing here
  // is "itself" — and saying so of an arbitrary script would be a guess dressed as advice.
  other: [],
};

/**
 * The note beside a row whose consultant is the caller's own vendor.
 *
 * <p>Allowed, never refused: the server cannot see the caller's MODEL, only its vendor, so
 * "Fable answering a Sonnet session" and "Sonnet answering itself" look identical from here. The
 * person can tell them apart, so the panel says what to think about rather than deciding for them.</p>
 */
export function sameVendorNote(callerKind: string, runtime: string): string {
  return (CALLER_RUNTIMES[callerKind] ?? []).includes(runtime)
    ? 'the same vendor as the caller — worth it only with a stronger model, since a model cannot see its own blind spot'
    : '';
}

/**
 * The first `coai-mcp` that reads a consultant's DEFINITION — its own runtime, endpoint and CLI path.
 *
 * <p>The same shape of skew as `ROLE_SWITCH_SINCE` in `prompts.ts`, and it fails the same way —
 * BACKWARDS — which is why it has to be said out loud. Measured 2026-09-15 (story B4 of
 * `PLAN_the_consultant_has_its_own_vendors`) against the last released server, mcp-v0.22.0
 * (4fe3cb02), driven over stdio with a definition on the wire: its DTO is `(Vendor, Model)`,
 * `System.Text.Json` skips the three members it does not declare, and it consulted through the
 * REVIEWER ROW with the same id — that row's runtime, endpoint and CLI path — with the entry's model,
 * launch for launch what it did for the legacy pair; a definition whose id had no reviewer row was
 * refused "not configured". So a person who made the consultant's endpoint or CLI path its own sees it
 * in the section and gets the reviewer's, and one who defined a consultant with no reviewer row sees
 * a consultant and gets a refusal. The replies are verbatim in `research/module_server.md`.</p>
 *
 * <p>`0.23.0`: the last release is 0.22.0, a server's version is stamped from its `mcp-v*` tag at
 * release (`release.yml`), and the server half of this plan (story B3) is the first server change since
 * that tag — a feature, which every feature-bearing server release of the last week took as a minor
 * bump. Set too LOW this stays silent on a server that drops the definition, the unsafe direction; set
 * too high it nags a current one. Whoever cuts the release keeps it level with the tag.</p>
 */
export const CONSULTANT_DEFINITION_SINCE = '0.23.0';

/**
 * The sentence the Consultant section shows while the installed server would consult through the
 * reviewer row instead of the definition the section draws — or nothing.
 *
 * <p>Nothing unless the server is KNOWN and strictly older — the rule its three siblings apply
 * (`conventionsSkew`, `roleSwitchSkew`, `customRolesSkew` in `panelView.ts`). Pure, with the version
 * as an argument, so every branch is a unit test rather than a paint; the caller is `panelView.ts`,
 * which puts it in the section it is about.</p>
 *
 * <p><b>The gate is what an older server would ANSWER DIFFERENTLY — not what is stored, and not
 * merely what crosses.</b> Two corrections, both from B4's plan round, and the second came out of
 * fixing the first. Asking whether the STORED entry is a definition is wrong because `envBlock` emits
 * the RESOLVED one: a legacy reference the reader turned into a definition crosses AS one, and an
 * upgraded install nobody has edited since is exactly that case — the people an older server
 * mishandles would have been the people the note never appeared for. But asking merely whether a
 * definition crosses over-warns, and the measurement says why: an older server resolves the id
 * through the reviewer rows, so where the definition CAME FROM a row that still holds those values it
 * reaches the same runtime, endpoint and CLI path, and gets nothing wrong. What it cannot reproduce
 * is a definition no row backs — it refuses that outright as "not configured" — or one whose fields
 * have since diverged from the row of the same name. Those are the cases, and they are the ones the
 * sentence below describes. (gemini, B4's plan round, on both counts.)</p>
 */
export function consultantSkewNote(
  installedServerVersion: string,
  consult: ConsultSettings,
  vendors: readonly Vendor[],
): string {
  const affected = CALLER_KINDS
    .filter(({ id }) => anOlderServerWouldDiffer(consult, id, vendors))
    .map(({ label }) => label);
  if (installedServerVersion.length === 0 || affected.length === 0 || !olderThanMarker(installedServerVersion)) {
    return '';
  }

  return `The coai-mcp you have installed (${installedServerVersion}) does not read a consultant's own runtime, `
    + `endpoint or CLI path. The consultant for ${listed(affected)} will run through the reviewer row with the same `
    + 'vendor id instead — that row’s runtime, endpoint and CLI path, with the model chosen here — and one whose '
    + `id names no reviewer row is refused as not configured, whatever this section shows. Update it to `
    + `${CONSULTANT_DEFINITION_SINCE} or later — the MCP server section below.`;
}

/**
 * Whether an older server's answer for this caller differs from what the section means.
 *
 * <p>Three conditions, in the order they cost nothing to ask. EMITTED at all — a pristine caller
 * sends no key, so there is nothing for an older server to read wrongly. Crossing as a DEFINITION —
 * an unavailable entry travels raw and is refused by name on every version alike. And not
 * REPRODUCIBLE from the reviewer rows, which is the one the measurement taught: an older server has
 * no rule for an id that is itself a runtime, so it resolves through the rows or refuses, and where
 * the definition came from a row that still holds those values it reaches exactly the same place.</p>
 */
function anOlderServerWouldDiffer(consult: ConsultSettings, caller: string, vendors: readonly Vendor[]): boolean {
  const crossing = consult.byCaller[caller];

  return !sameChoice(consult.stored[caller], DEFAULT_CONSULT.stored[caller])
    && crossing?.kind === 'definition'
    && !reproducedByARow(crossing, vendors);
}

/** Whether a reviewer row of that id would hand an older server the same three fields. */
function reproducedByARow(one: ConsultantDefinition, vendors: readonly Vendor[]): boolean {
  const row = vendors.find((each) => each.id.toLowerCase() === one.vendor.toLowerCase());

  return row !== undefined
    && row.runtime === one.runtime
    && row.baseUrl === one.baseUrl
    && row.executablePath === one.executablePath;
}

function olderThanMarker(version: string): boolean {
  return compareVersions(CONSULTANT_DEFINITION_SINCE, version) > 0;
}

/** `A`, `A and B`, `A, B and C` — the affected callers as a person would list them. */
function listed(labels: readonly string[]): string {
  return labels.length <= 1
    ? labels.join('')
    : `${labels.slice(0, -1).join(', ')} and ${labels[labels.length - 1]}`;
}

/** The rows this feature may consult with, and the reason a row cannot. */
export function consultableVendors(vendors: readonly Vendor[]): {
  readonly offered: readonly Vendor[];
  readonly refused: readonly { readonly vendor: Vendor; readonly why: string }[];
} {
  const offered: Vendor[] = [];
  const refused: { vendor: Vendor; why: string }[] = [];
  for (const vendor of vendors) {
    if (!vendor.enabled) {
      refused.push({ vendor, why: 'switched off' });
    } else if (CONSULTING_RUNTIMES.includes(vendor.runtime)) {
      offered.push(vendor);
    } else {
      // NAMED, never filtered away: a row that silently vanishes from a picker is a person hunting
      // for a vendor they can see configured. `chatModelsFrom` made the same call.
      refused.push({ vendor, why: `runs on '${vendor.runtime}', which cannot hold a consultation` });
    }
  }

  return { offered, refused };
}

/**
 * The stored map resolved against the reviewer rows — the RESULT for every caller kind, whole.
 *
 * <p>This is where an unavailable entry lives on. A definition is carried as itself; an entry the
 * rule cannot place is carried as the `unavailable` result — vendor and model raw, and the `why` —
 * rather than as the stored choice it came from. The first cut did the latter, and it cost the one
 * sentence a person can act on: the section (story C5) renders that sentence, and with the reason
 * discarded it would have had to re-run {@link resolveConsultant} against `stored` plus the rows to
 * recover it — two roads to one decision, which is the defect this module's one-reader rule exists
 * to prevent. So the map is typed by what the rule ANSWERED, and `kind` says which it was. (codex
 * and gemini, independently, on A1's code round.)</p>
 *
 * <p>Also the value of {@link DEFAULT_CONSULT}'s `byCaller`, against no rows — which is why
 * {@link CONSULTING_RUNTIMES} is declared above that constant in this file.</p>
 */
function resolveAll(
  stored: Readonly<Record<string, ConsultantChoice>>,
  vendors: readonly Vendor[],
): Record<string, ResolvedConsultant> {
  return Object.fromEntries(
    CALLER_KINDS.map(({ id }): [string, ResolvedConsultant] => [id, resolveConsultant(stored[id], vendors)]),
  );
}

/**
 * ONE stored row, read exactly as {@link consultSettingsFrom} reads the four.
 *
 * <p>Exported for the WRITE path, which has to start from the row as stored before it resolves and
 * puts a definition back (`consultantRecordUpdate`). It is the same reader rather than a second one
 * on purpose: two spellings of "what does this row say" is how the panel comes to store a shape its
 * own reader will not accept.</p>
 */
export function consultantChoiceFrom(row: unknown): ConsultantChoice {
  return entryFrom(asRecord(row));
}

/** The stored map, one entry per caller kind: what is written, trimmed — or the shipped pair. */
function callers(stored: Record<string, unknown>): Record<string, ConsultantChoice> {
  return Object.fromEntries(CALLER_KINDS.map(({ id }): [string, ConsultantChoice] => {
    const entry = consultantChoiceFrom(stored[id]);

    return [id, entry.vendor.length > 0 ? entry : DEFAULT_CONSULT.stored[id]];
  }));
}

/**
 * One stored entry, every field trimmed and junk of any type read as empty.
 *
 * <p>A hand-edited `settings.json` can hold a number, a null or an array where a string belongs, and
 * a reader that throws leaves the panel on its previous paint with nothing saying why — the guard
 * `vendorsFrom` applies to every field of a row, applied to every field here. A runtime this build
 * does not know reads as `''`, a legacy reference: the id still resolves by rule (a) or (b), which is
 * more than a runtime nobody can launch would do.</p>
 */
function entryFrom(row: Record<string, unknown>): ConsultantChoice {
  return {
    vendor: text(row['vendor']),
    runtime: runtimeOf(row['runtime']),
    model: text(row['model']),
    baseUrl: text(row['baseUrl']),
    executablePath: text(row['executablePath']),
  };
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : '';
}

/**
 * A stored runtime, recognised WITHOUT case and answered in the list's own spelling.
 *
 * <p>Both halves have to recognise a runtime the same way or they disagree about the same file. This
 * read through a list of lower-case names exactly, so `"Codex"` was no runtime at all — the entry
 * fell back to a legacy reference, resolved by its id, and the panel drew a consultant the SERVER
 * then refused by name, because the server reads any non-empty runtime as a definition. Matching
 * without case removes that; answering with the list's spelling means one name reaches the wire.
 * (gemini and the local reviewer, independently, on B3's plan round.)</p>
 *
 * <p>A name this build has never heard of still reads as `''` — a legacy reference, resolved by id —
 * and that residual difference from the server is deliberate: this is how an extension meets a
 * runtime a NEWER one wrote, while the half that would LAUNCH it fails closed instead.</p>
 */
function runtimeOf(value: unknown): Runtime | '' {
  const name = text(value).toLowerCase();

  return RUNTIMES.find((one) => one === name) ?? '';
}

function asRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : {};
}

/**
 * A whole positive number the SERVER can also hold, or the shipped default.
 *
 * <p>The upper bound is not decoration: `settings.json` is a file a person edits by hand, and
 * `2147483648` satisfies `Number.isInteger`, travels across the seam, and is then refused by the C#
 * `int.TryParse` — which falls back. The panel would display one cap while another was enforced,
 * which is the whole failure mode this module's defaults test exists to prevent, reached by a
 * different door. Raised on this story's code round.</p>
 */
const SERVER_MAX = 2_147_483_647;

function positive(value: unknown, fallback: number): number {
  return typeof value === 'number' && Number.isInteger(value) && value > 0 && value <= SERVER_MAX
    ? value
    : fallback;
}
