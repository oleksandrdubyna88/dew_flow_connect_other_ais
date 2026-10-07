import { FEATURES } from './binaryFeatures';
import { apiRuntimeOnServer } from './apiRuntime';
import { apiSettingsOnTheWire } from './apiSettings';
import { featureOnServer, reviewsFeatures } from './featureGate';
import { ModelPrice } from './modelPrices';
import { saidText } from './saidText';
import { Vendor } from './vendors';

/**
 * The reviewer rows as they CROSS to coai-mcp — `COAI_VENDORS` — split from `vendors.ts`, which keeps the
 * row and its parsing (PLAN_one_model_catalog.md E1.1: the row grows, and the file was at the 800-line
 * limit). A whitelist of fields, never the stored row: a field the row gains crosses only when a line
 * here says so.
 */

/** An `api` row's price as it crosses to the server — dollars per million, plus an optional tier. */
export interface WirePrice {
  readonly in: number;
  readonly cached: number;
  readonly out: number;
  readonly tierFrom?: number;
  readonly tierIn?: number;
  readonly tierCached?: number;
  readonly tierOut?: number;
}

/**
 * What an `api` row charges, as the server is to price it (PLAN_feature_review.md S3.7) — or nothing.
 *
 * <p><b>A typed rate wins, field by field</b>, over the looked-up one — the rule the card and the
 * spending tab already follow. The long-context tier comes only from the list, and only while nothing
 * was typed: a person who typed a rate is on their own terms, and doubling it from somebody else's
 * threshold would be arithmetic on a price they never agreed to.</p>
 *
 * <p>No rate at all is `undefined`, never zeroes: the server then says "no price set", which is true,
 * where a zero would say the run was free.</p>
 */
export function wirePrice(v: Vendor, looked: ModelPrice | undefined): WirePrice | undefined {
  const listed: Listed = looked ?? UNLISTED;
  const typed = typedRates(v);
  const rates = {
    in: typedOr(typed.in, listed.inPerMillion),
    cached: typedOr(typed.cached, listed.cachedPerMillion),
    out: typedOr(typed.out, listed.outPerMillion),
  };
  if (rates.in === 0 && rates.out === 0) {
    return undefined;
  }

  return { ...rates, ...tierOnTheWire(typed, listed) };
}

/** The rates a list can give — the part of a `ModelPrice` that prices anything. */
type Listed = Pick<ModelPrice, 'inPerMillion' | 'outPerMillion' | 'cachedPerMillion' | 'tier'>;

/** No list answered: every rate unknown. */
const UNLISTED: Listed = { inPerMillion: 0, outPerMillion: 0 };

/** The three rates the person typed on the row; 0 is "not typed". */
function typedRates(v: Vendor): { readonly in: number; readonly cached: number; readonly out: number } {
  return { in: v.pricePerMillionIn, cached: v.pricePerMillionCached ?? 0, out: v.pricePerMillionOut };
}

/** A typed rate when there is one, else the looked-up one, else 0 — "not known", never free. */
function typedOr(typed: number, looked: number | undefined): number {
  return typed > 0 ? typed : (looked ?? 0);
}

/** The list's long-context tier as the wire spells it — only while no rate was typed; otherwise nothing. */
function tierOnTheWire(typed: ReturnType<typeof typedRates>, listed: Listed): Partial<WirePrice> {
  const tier = anyTyped(typed) ? undefined : listed.tier;

  return tier === undefined
    ? {}
    : { tierFrom: tier.fromTokens, tierIn: tier.inPerMillion, tierCached: tier.cachedPerMillion, tierOut: tier.outPerMillion };
}

function anyTyped(typed: ReturnType<typeof typedRates>): boolean {
  return typed.in > 0 || typed.cached > 0 || typed.out > 0;
}

/** What a row's model costs by the list, when the panel has looked — the writer's seam to the lookup. */
export type RowPriceLookup = (v: Vendor) => ModelPrice | undefined;

/**
 * The environment the server reads: the vendor list as JSON, because a comma-separated string
 * cannot carry a runtime and a base URL, and inventing a second encoding for them would be a
 * format nobody could read in a config file.
 */
export function vendorsEnv(vendors: readonly Vendor[], installedServerVersion = '', priceOf: RowPriceLookup = () => undefined, features: readonly string[] = []): string {
  return JSON.stringify(
    vendors
      .filter((v) => v.enabled)
      // An `api` row is SUPPRESSED from the file for a server older than `API_RUNTIME_SINCE`, not
      // merely disabled in the card: the old server reads the settings file, not the panel, and its
      // `RuntimeOf` turns a runtime it does not know into codex WITH the base URL — a Grok review
      // through the Codex CLI against xAI's endpoint, under the row's own name (§4.13). Every other
      // row still crosses, so a person's codex and antigravity keep reviewing while the api row waits
      // for the update. Unknown is not old — see `apiRuntimeOnServer`.
      .filter((v) => v.runtime !== 'api' || apiRuntimeOnServer(installedServerVersion))
      .map((v) => rowOnTheWire(v, installedServerVersion, priceOf, features)),
  );
}

/**
 * ONE row as `COAI_VENDORS` writes it — the one field list, also what a consultant entry and a question row carry as
 * `row` (todo/PLAN_one_model_catalog.md, C2) and what `--check-model` is handed. A field the wire gains is gained by all.
 */
export function rowOnTheWire(v: Vendor, installedServerVersion: string, priceOf: RowPriceLookup, features: readonly string[]): Record<string, unknown> {
  return {
    id: v.id,
    runtime: v.runtime,
    model: v.model,
    baseUrl: v.baseUrl,
    executablePath: v.executablePath,
    ...saidOnTheWire(v),
    ...featureOnTheWire(v, installedServerVersion),
    ...apiOnTheWire(v, priceOf),
    // The three per-model settings (S3.8): an api row's own, and never to a server known to predate them.
    ...apiSettingsOnTheWire(v, installedServerVersion),
    ...promptOnTheWire(v, features),
    ...timeoutOnTheWire(v, features),
    ...cliEffortOnTheWire(v, features),
    ...streamOnTheWire(v, features),
    ...fastOnTheWire(v, features),
  };
}

/**
 * A row's fast mode, to a binary that lists `fastMode` (research/PLAN_fast_mode.md) — and to no other, which would skip the
 * member while the card said it was set. Off crosses as nothing: the binary's own default is Off.
 */
function fastOnTheWire(v: Vendor, features: readonly string[]): { fast?: 'on' | 'cli' } {
  return v.fast !== undefined && features.includes(FEATURES.fastMode) ? { fast: v.fast } : {};
}

/**
 * An api row's stream switch, to a binary that lists `apiStream` (research/PLAN_api_streaming.md) — and to no other: an older
 * binary skips the member, so the card would say "streamed" over a call that is not. Only when on.
 */
function streamOnTheWire(v: Vendor, features: readonly string[]): { stream?: true } {
  return v.stream === true && features.includes(FEATURES.apiStream) ? { stream: true } : {};
}

/**
 * A CLI row's effort, to a binary that lists `cliEffort` — what it means is the runtime's, decided by coai-mcp. An api
 * row's effort keeps its own road (`apiSettingsOnTheWire`), so it is never written twice.
 */
function cliEffortOnTheWire(v: Vendor, features: readonly string[]): { effort?: string } {
  return v.runtime !== 'api' && features.includes(FEATURES.cliEffort) && v.effort !== undefined ? { effort: v.effort } : {};
}

/** A CLI row's own timeout, to a binary that lists `timeoutMinutes` — an api row has none (it keeps `reviewMinutes`). */
function timeoutOnTheWire(v: Vendor, features: readonly string[]): { timeoutMinutes?: number } {
  return features.includes(FEATURES.timeoutMinutes) && v.timeoutMinutes !== undefined ? { timeoutMinutes: v.timeoutMinutes } : {};
}

/**
 * A row's system prompt, to a binary that lists `systemPrompt` (`--features`; PLAN_one_model_catalog.md E2.2) — and to
 * no other: an older binary skips a member it does not know, so the prompt would silently do nothing there. Held
 * back, the card can say it is not sent.
 */
function promptOnTheWire(v: Vendor, features: readonly string[]): { systemPrompt?: string } {
  return features.includes(FEATURES.systemPrompt) && v.systemPrompt !== undefined ? { systemPrompt: v.systemPrompt } : {};
}

/**
 * What only an `api` row carries: the vault key's name when it said one (S3.6), and its price (S3.7).
 *
 * <p>Nothing for any other runtime. A CLI on a subscription is not billed per token, so a price on its
 * row would turn the panel's list-price estimate into a figure the ledger records as spent. A server
 * older than these fields ignores both — the JSON reader skips members it does not know — so the row
 * still runs there, reading the key under its own id and unpriced, which is what it did before.</p>
 */
function apiOnTheWire(v: Vendor, priceOf: RowPriceLookup): { key?: string; price?: WirePrice } {
  if (v.runtime !== 'api') {
    return {};
  }
  const key = saidText(v.vaultKeyName, 'lower');
  const price = wirePrice(v, priceOf(v));

  return {
    ...(key === undefined ? {} : { key }),
    ...(price === undefined ? {} : { price }),
  };
}

/**
 * The feature tick as the wire wants it: `feature: true`, or nothing.
 *
 * <p>Only a TRUE crosses — absent is the server's no, so a `false` would be noise in a block a person
 * reads. Never for a Team server row (D10), and never to a server known to be older than
 * `FEATURE_SINCE`: that one has no feature stage, and a tick in its file would claim something it
 * cannot do while the card says the box is off. The ROW still crosses either way — the vendor keeps
 * reviewing plans and code. Unknown is not old (`featureOnServer`).</p>
 */
function featureOnTheWire(v: Vendor, installedServerVersion: string): { feature?: true } {
  return reviewsFeatures(v) && featureOnServer(installedServerVersion) ? { feature: true } : {};
}

/**
 * The fields a row carries on the wire only when it SAID or NARROWED them.
 *
 * <p>`remoteVendor` is the SERVER's own name for this vendor, which is not the row's id and must not
 * be confused with it: a row is `<server>-<vendor>` so two Team servers offering `codex` do not
 * collide, while `--vendor` has to carry what the server actually knows. This was missing for the
 * whole life of the `remote` runtime — the server received the row id, refused it as a vendor it
 * "does not offer", and the reviewer was dropped from every round while the panel went on reporting
 * it as configured. `teamServerId` deliberately stays behind: the server has no field for it and no
 * question it answers. It exists so the PANEL can follow a row to its server entry after somebody
 * fixes a typo in a hostname.</p>
 *
 * <p>Guarded by `typeof`, not by `!== undefined`, and trimmed before it is weighed. This is JSON a
 * person edits: a hand-written `"remoteVendor": null` passes an undefined check and then has
 * `.length` read off it, which throws inside the settings sync — and a sync that throws leaves the
 * server on the file's previous contents with nothing saying the write never happened. A name made
 * only of spaces is absent for the same reason it is absent from `vendorsFrom`. Trimming is not the
 * normalisation this field forbids: that one is about CASE, and neither side lower-cases, because a
 * server whose catalog says `DeepSeek` matches `DeepSeek`. Both raised on this change's code round.</p>
 *
 * <p>`plan` and `code` are written only when NARROWED, like every other value in the env block: a
 * vendor that reviews both stages says nothing, and the server reads an absent flag as both. So the
 * block a person opens still carries only what differs from the defaults. `document` is written
 * whenever it was SAID, in either direction — the one field here whose absence is a value rather
 * than a default: `coai-mcp` reads an absent `document` as "the plan tick for a local vendor, no for
 * a Team server", which is the whole consent rule, and emitting `true` only when narrowed would have
 * thrown away the half that says yes. `dialect` only when SAID, like `remoteVendor`: absent lets
 * `coai-mcp` pick the generic dialect, and a row that never named one stays byte-identical to what
 * it always was.</p>
 */
function saidOnTheWire(v: Vendor): Record<string, unknown> {
  const remoteVendor = saidText(v.remoteVendor);
  const dialect = saidText(v.dialect);

  return {
    ...(remoteVendor === undefined ? {} : { remoteVendor }),
    ...narrowedStages(v),
    ...(dialect === undefined ? {} : { dialect }),
  };
}

/** The three stage switches as the wire wants them: two only when narrowed, the third whenever it was said. */
function narrowedStages(v: Vendor): { plan?: false; code?: false; document?: boolean } {
  return {
    ...(v.plan ? {} : { plan: false }),
    ...(v.code ? {} : { code: false }),
    ...(v.document === undefined ? {} : { document: v.document }),
  };
}
