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

import { Vendor } from './vendors';

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

/** Which vendor row consults for one caller, and on which model. Empty model = the row's own. */
export interface ConsultantChoice {
  readonly vendor: string;
  readonly model: string;
}

export interface ConsultSettings {
  /** Caller kind → the consultant it gets. Every kind in {@link CALLER_KINDS} is present. */
  readonly byCaller: Readonly<Record<string, ConsultantChoice>>;
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
 */
export const DEFAULT_CONSULT: ConsultSettings = {
  byCaller: {
    claude: { vendor: 'codex', model: '' },
    codex: { vendor: 'claude', model: '' },
    gemini: { vendor: 'codex', model: '' },
    other: { vendor: 'codex', model: '' },
  },
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
 */
export function consultSettingsFrom(read: Read): ConsultSettings {
  return {
    byCaller: callers(asRecord(read('consultants'))),
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

/** Whether this is exactly what the server would do with no configuration at all. */
export function isDefaultConsult(settings: ConsultSettings): boolean {
  return settings.turns === DEFAULT_CONSULT.turns
    && settings.callsPerSession === DEFAULT_CONSULT.callsPerSession
    && settings.idleMinutes === DEFAULT_CONSULT.idleMinutes
    && settings.enabled === DEFAULT_CONSULT.enabled
    && sameCallers(settings.byCaller, DEFAULT_CONSULT.byCaller);
}

/** Whether the caller map alone differs from the shipped one. */
export function sameCallers(
  one: Readonly<Record<string, ConsultantChoice>>,
  other: Readonly<Record<string, ConsultantChoice>>,
): boolean {
  return CALLER_KINDS.every(({ id }) =>
    (one[id]?.vendor ?? '') === (other[id]?.vendor ?? '')
    && (one[id]?.model ?? '') === (other[id]?.model ?? ''));
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
 */
export const CONSULTING_RUNTIMES: readonly string[] = ['codex', 'claude', 'antigravity', 'local'];

function callers(stored: Record<string, unknown>): Record<string, ConsultantChoice> {
  const map: Record<string, ConsultantChoice> = {};
  for (const { id } of CALLER_KINDS) {
    const row = asRecord(stored[id]);
    const vendor = typeof row['vendor'] === 'string' ? row['vendor'].trim() : '';
    map[id] = vendor.length > 0
      ? { vendor, model: typeof row['model'] === 'string' ? row['model'].trim() : '' }
      : DEFAULT_CONSULT.byCaller[id];
  }

  return map;
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
