/**
 * Which two models the gate's split order names, per KIND of calling assistant (issue #117).
 *
 * <p>The order is carried out by the CALLER, never by the gate — so a Codex session told "Fable and
 * Opus" was handed two models it does not have. A person now picks, per caller kind, the STRONGEST
 * model (the split itself and the risky stories) and the one for ordinary IMPLEMENTATION. Claude
 * Code ships with Fable and Opus, the pair every earlier release named; the other kinds ship with
 * nothing, and the order then says "the strongest model your client offers" rather than naming
 * another vendor's.</p>
 *
 * <p>Pure and `vscode`-free, ONE reader for the panel and the env block alike — the rule
 * `consultSettings.ts` states: what the panel shows and what the server runs cannot disagree, because
 * neither has a reader of its own.</p>
 */

import { compareVersions } from './coaiInstall';
import { CALLER_KINDS } from './consultSettings';
import { Runtime } from './models';
import { record, text } from './savedRows';

/** The two slots. An empty string is "not named here", resolved per field against the shipped pair. */
export interface ModelPair {
  readonly strongest: string;
  readonly implementation: string;
}

/** The two field names, as the stored record and the wire spell them. */
export type ModelSlot = keyof ModelPair;

const MODEL_SLOTS: readonly ModelSlot[] = ['strongest', 'implementation'];

/** Whether a name is one of the two slots — typed by a predicate, never a cast. */
export function isModelSlot(name: string): name is ModelSlot {
  return (MODEL_SLOTS as readonly string[]).includes(name);
}

/** How a slot is named where a person reads it. */
export const SLOT_LABELS: Readonly<Record<ModelSlot, string>> = {
  strongest: 'Strongest',
  implementation: 'Implementation',
};

/** A pair that names nothing — the shipped pair of every kind but Claude Code. */
export const NO_MODELS: ModelPair = { strongest: '', implementation: '' };

/**
 * The shipped pairs — one contract with `CommandModels.Shipped` in the server, held level by
 * `shared/command-models.json`, which each half's suite asserts its own copy against.
 */
export const SHIPPED_COMMAND_MODELS: Readonly<Record<string, ModelPair>> = {
  claude: { strongest: 'Fable', implementation: 'Opus' },
  codex: NO_MODELS,
  gemini: NO_MODELS,
  other: NO_MODELS,
};

/**
 * Which runtime's model list a caller kind is offered — its OWN vendor's. `other` is a client of no
 * known vendor, so it is offered no list, only what was saved.
 */
export const COMMAND_MODEL_RUNTIMES: Readonly<Record<string, Runtime>> = {
  claude: 'claude',
  codex: 'codex',
  gemini: 'gemini',
};

/** The setting as read: what a person typed, per kind, trimmed. Absent fields are empty strings. */
export type CommandModels = Readonly<Record<string, ModelPair>>;

/**
 * `coai.commandModels`, read kind by kind and field by field.
 *
 * <p>Key by key for the reason `asRoleFlags` gives: a record holding only the kind somebody changed
 * must still answer for every other kind. Junk of any type reads as empty — a hand-edited
 * `settings.json` that throws would leave the panel on its previous paint with nothing saying why.</p>
 */
export function commandModelsFrom(raw: unknown): CommandModels {
  const stored = record(raw) ?? {};

  return Object.fromEntries(CALLER_KINDS.map(({ id }): [string, ModelPair] => {
    const row = record(stored[id]) ?? {};

    return [id, { strongest: text(row['strongest']), implementation: text(row['implementation']) }];
  }));
}

/**
 * The pair a caller of this kind is actually told: a typed name, else the shipped one — per FIELD.
 *
 * <p>Per field, so clearing one box does not blank the other slot. The server applies the same rule
 * (`CommandModels.For`) because `COAI_COMMAND_MODELS` is also a key a person can write by hand; what
 * this panel sends is already resolved, so the two never have to agree about a row it produced.</p>
 */
export function resolvedPair(models: CommandModels, kind: string): ModelPair {
  const shipped = SHIPPED_COMMAND_MODELS[kind] ?? NO_MODELS;
  const typed = models[kind] ?? NO_MODELS;

  return {
    strongest: typedOr(typed.strongest, shipped.strongest),
    implementation: typedOr(typed.implementation, shipped.implementation),
  };
}

function typedOr(typed: string, shipped: string): string {
  return typed.length > 0 ? typed : shipped;
}

/**
 * The kinds whose resolved pair is not the shipped one — the only ones that cross to the server.
 *
 * <p>Compared WITHOUT case: the Claude picker offers the CLI's own alias `fable`, and choosing it is
 * choosing the default — sending it would change nothing but put a warning about an older server
 * beside a panel that asked for nothing new. (#117's code review.)</p>
 */
export function kindsOnTheWire(models: CommandModels): readonly string[] {
  return CALLER_KINDS
    .map(({ id }) => id)
    .filter((id) => !samePair(resolvedPair(models, id), SHIPPED_COMMAND_MODELS[id] ?? NO_MODELS));
}

/**
 * `COAI_COMMAND_MODELS`, or nothing when every kind is as shipped.
 *
 * <p>One JSON key rather than eight scalars, for the reason `COAI_CONSULTANTS` already carries: a
 * compound value needs a structured encoding, and eight key spellings are eight chances for the two
 * halves to disagree about one of them.</p>
 */
export function commandModelsEnv(models: CommandModels): Record<string, string> {
  const crossing = kindsOnTheWire(models);

  return crossing.length === 0
    ? {}
    : { COAI_COMMAND_MODELS: JSON.stringify(Object.fromEntries(crossing.map((id) => [id, resolvedPair(models, id)]))) };
}

/**
 * The stored record after one picker was set.
 *
 * <p>A blank value REMOVES that field, so the kind falls back to its shipped name for it, and a kind
 * left with no fields drops out of the record — that is how a person gets the default back. Other
 * kinds and fields are carried forward as they are stored: a newer panel on the other side of a synced
 * settings file may have written them.</p>
 *
 * <p>The kind arrives in a webview message and only this build's four are written — the refusal
 * `consultantRecordUpdate` makes, and for its reason: `__proto__` indexed into a record reads
 * `Object.prototype` and would write a key nobody asked for.</p>
 */
export function commandModelsAfter(current: unknown, kind: string, slot: ModelSlot, value: string): Record<string, unknown> {
  const stored = record(current) ?? {};
  if (!CALLER_KINDS.some(({ id }) => id === kind)) {
    return { ...stored };
  }
  const row = rowAfter(stored[kind], slot, value);
  const others = Object.fromEntries(Object.entries(stored).filter(([key]) => key !== kind));

  return Object.keys(row).length > 0 ? { ...others, [kind]: row } : others;
}

/** One kind's stored row with one slot set — or removed, when the value is blank. */
function rowAfter(current: unknown, slot: ModelSlot, value: string): Record<string, unknown> {
  const others = Object.fromEntries(Object.entries(record(current) ?? {}).filter(([key]) => key !== slot));

  return value.trim().length > 0 ? { ...others, [slot]: value.trim() } : others;
}

/**
 * The picker a `customCommandModel` request came from — `<caller kind>:<slot>` — or nothing when the
 * id is not one: a page and a host that disagree about the shape write nothing rather than a guess.
 */
export function commandModelTarget(id: string): { readonly kind: string; readonly label: string; readonly slot: ModelSlot } | undefined {
  const colon = id.indexOf(':');
  const slot = id.slice(colon + 1);
  const kind = CALLER_KINDS.find(({ id: known }) => known === id.slice(0, colon));

  return kind !== undefined && isModelSlot(slot) ? { kind: kind.id, label: kind.label, slot } : undefined;
}

/**
 * The first `coai-mcp` that reads `COAI_COMMAND_MODELS`.
 *
 * <p>The last release is mcp 0.32.0 and this is the next feature on the server; features take a minor
 * bump there. Set too LOW this stays silent on a server that names Fable to everybody — the unsafe
 * direction; whoever cuts the release keeps it level with the tag.</p>
 */
export const COMMAND_MODELS_SINCE = '0.33.0';

/**
 * The sentence the gate section shows while the installed server would ignore these pickers — or
 * nothing.
 *
 * <p>Only when the server is KNOWN, strictly older, and some kind actually differs from the shipped
 * pair: an older server names Fable and Opus to every caller, which is exactly what a pristine panel
 * means for Claude Code, so there is nothing to warn about until somebody changed a picker.</p>
 */
export function commandModelsSkewNote(installedServerVersion: string, models: CommandModels): string {
  if (installedServerVersion.length === 0 || kindsOnTheWire(models).length === 0
    || compareVersions(COMMAND_MODELS_SINCE, installedServerVersion) <= 0) {
    return '';
  }

  return `The coai-mcp you have installed (${installedServerVersion}) does not read these models: it names Fable `
    + `and Opus to every caller, whatever the pickers say. Update it to ${COMMAND_MODELS_SINCE} or later — the MCP `
    + 'server section below.';
}

function samePair(one: ModelPair, other: ModelPair): boolean {
  return MODEL_SLOTS.every((slot) => one[slot].toLowerCase() === other[slot].toLowerCase());
}
