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
   * Caller kind → the consultant it gets, RESOLVED. Every kind in {@link CALLER_KINDS} is present.
   *
   * <p>What the section draws and what a consultation will run on. After {@link consultSettingsFrom}
   * an entry here carries `runtime: ''` only when it is UNAVAILABLE — a legacy reference that matched
   * no reviewer row and names no consulting runtime — and then it is the stored entry itself,
   * preserved. {@link DEFAULT_CONSULT} is the one value whose `byCaller` is unresolved: it is the
   * shipped CONFIGURATION, a contract with the C#, and there are no rows to resolve it against.</p>
   */
  readonly byCaller: Readonly<Record<string, ConsultantChoice>>;
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

export const DEFAULT_CONSULT: ConsultSettings = {
  // The same four pairs on both sides: nothing has been resolved, because nothing has been read.
  byCaller: SHIPPED_PAIRS,
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

/** Every field a choice has, so a comparison cannot forget one the type gained. */
const CHOICE_FIELDS: readonly (keyof ConsultantChoice)[] = ['vendor', 'runtime', 'model', 'baseUrl', 'executablePath'];

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

function sameChoice(one: ConsultantChoice | undefined, other: ConsultantChoice | undefined): boolean {
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
 * is what lets that refusal name it too. The vendor id is rewritten by no arm: it keys the vault
 * entry and the ledger, and a resolution that changed it would move a person's credential.</p>
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

/** Rule (b): the runtime the id names, and nothing borrowed. */
function fromRuntime(choice: ConsultantChoice, runtime: Runtime): ResolvedConsultant {
  return { kind: 'definition', vendor: choice.vendor, runtime, model: choice.model, baseUrl: '', executablePath: '' };
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
 * The runtimes that can hold a consultation — the extension's copy of `ConsultantResolution.Consulting`.
 *
 * <p>A mirror, and a test asserts it against the C#. The alternative was asking the server, which
 * cannot answer before it is installed — and this list decides what a picker OFFERS, which has to be
 * drawable the first time the panel opens.</p>
 *
 * <p>Typed as runtimes rather than strings since {@link resolveConsultant} rule (b) resolves an id
 * INTO one of them: a `find` over this list is then a `Runtime` with no cast standing in for a type.</p>
 */
export const CONSULTING_RUNTIMES: readonly Runtime[] = ['codex', 'claude', 'antigravity', 'local'];

/**
 * The stored map resolved against the reviewer rows, as a map of choices again.
 *
 * <p>A definition is written out in full; an unavailable entry is the STORED one, untouched — so
 * after a read `runtime: ''` means exactly "unavailable", and resolving it again against the same
 * rows, or none, yields the same `unavailable` with the same reason.</p>
 */
function resolveAll(
  stored: Readonly<Record<string, ConsultantChoice>>,
  vendors: readonly Vendor[],
): Record<string, ConsultantChoice> {
  return Object.fromEntries(
    CALLER_KINDS.map(({ id }): [string, ConsultantChoice] => [id, materialised(stored[id], vendors)]),
  );
}

function materialised(choice: ConsultantChoice, vendors: readonly Vendor[]): ConsultantChoice {
  const resolved = resolveConsultant(choice, vendors);

  return resolved.kind === 'definition'
    ? {
      vendor: resolved.vendor,
      runtime: resolved.runtime,
      model: resolved.model,
      baseUrl: resolved.baseUrl,
      executablePath: resolved.executablePath,
    }
    : choice;
}

/** The stored map, one entry per caller kind: what is written, trimmed — or the shipped pair. */
function callers(stored: Record<string, unknown>): Record<string, ConsultantChoice> {
  return Object.fromEntries(CALLER_KINDS.map(({ id }): [string, ConsultantChoice] => {
    const entry = entryFrom(asRecord(stored[id]));

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

function runtimeOf(value: unknown): Runtime | '' {
  const name = text(value);

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
