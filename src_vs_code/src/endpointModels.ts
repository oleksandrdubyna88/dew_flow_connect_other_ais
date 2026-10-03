import type { ModelChoice } from './modelChoice';

/**
 * What a reviewer on SOMEBODY ELSE'S endpoint can be pointed at: the models that endpoint lists, never
 * the Codex CLI's cache.
 *
 * <p><b>One decision, and it used to live at one of its two sites.</b> The `api` runtime had it — a
 * hosted endpoint's list is "neither discovered here nor curated … never the Codex cache". A `codex` row
 * with a base URL is the same kind of row (the Codex CLI pointed at an OpenAI-compatible endpoint through
 * `CustomCodexRuntime`) and fell through to the cache, so an OpenRouter reviewer was offered ten OpenAI
 * slugs that endpoint refuses, captioned "the Codex CLI has cached for this machine". Reported from a
 * screenshot on 2026-10-01 (research/PLAN_custom_endpoint_model_list.md). {@link asksAnEndpoint} is now the one
 * predicate and this file the one place that answers for both.</p>
 *
 * <p><b>Asked on demand, never per repaint.</b> The endpoint is asked through `coai-mcp --probe-api` —
 * the vault read the product's own way, then `GET /models` — only when the person presses ≡; the answer
 * is kept with what it was asked WITH and used only while the row still matches it.</p>
 */

/** What an endpoint's own `GET /models` answered through `coai-mcp --probe-api`, and what it was asked WITH. */
export interface EndpointListing {
  readonly baseUrl: string;
  readonly keyName: string;
  /** The ids it listed — empty when it refused, and then `reason` says why. */
  readonly ids: readonly string[];
  readonly reason: string;
  /** When it was asked, as an ISO instant in UTC. */
  readonly askedUtc: string;
}

/** The endpoint a row talks to, the vault key it would be asked with, and any answer kept for it. */
export interface RowEndpoint {
  readonly baseUrl: string;
  readonly keyName: string;
  readonly listed?: EndpointListing | undefined;
  /** An ask for this row is in flight. */
  readonly asking?: boolean | undefined;
}

/** A caller that holds no endpoint — the gate-command picker. */
export const NO_ENDPOINT: RowEndpoint = { baseUrl: '', keyName: '' };

/**
 * Whether a row's models belong to an endpoint rather than to a CLI: every `api` row, and a `codex` row
 * that was given a base URL. Whitespace is no base URL — `vendorsFrom` trims, but a caller building a row
 * by hand may not have.
 */
export function asksAnEndpoint(runtime: string, baseUrl: string): boolean {
  return runtime === 'api' || (runtime === 'codex' && baseUrl.trim().length > 0);
}

/**
 * What an ask is KEPT as: the probe's ids or its reason, with what it was asked WITH and when, in UTC.
 *
 * <p>A refusal is kept exactly like an answer (plan round, codex). Dropping it would put the card back
 * on "not asked", and a person whose key is missing would press ≡ forever without being told why.</p>
 */
export function listingOf(
  asked: { readonly baseUrl: string; readonly keyName: string },
  probed: { readonly ids: readonly string[]; readonly reason: string },
  at: Date,
): EndpointListing {
  return { baseUrl: asked.baseUrl, keyName: asked.keyName, ids: [...probed.ids], reason: probed.reason, askedUtc: at.toISOString() };
}

/**
 * The answer that still describes this row — asked with its base URL AND its key name — or none.
 *
 * <p>Both, because either can change under a kept answer: another endpoint was simply not asked, and
 * another key may be allowed other models. A stale answer offers nothing rather than another key's list.
 * (Plan round, codex.)</p>
 */
export function listingFor(endpoint: RowEndpoint): EndpointListing | undefined {
  const listed = endpoint.listed;

  return listed?.baseUrl === endpoint.baseUrl && listed.keyName === endpoint.keyName ? listed : undefined;
}

/**
 * The dropdown: what the endpoint listed when it has been asked, else the saved model alone.
 *
 * <p>A saved model the endpoint did NOT list is kept and marked — dropping it would switch the reviewer to
 * another model in silence, and showing it plainly would let a round be sent for a model the endpoint is
 * about to refuse. The local engine's list follows the same rule.</p>
 */
export function endpointModels(current: string, endpoint: RowEndpoint): ModelChoice[] {
  const listed = listedChoices(endpoint);

  return listed.length === 0 ? savedOnly(current) : withTheSavedOne(current, listed, endpoint.baseUrl);
}

/** The ids the still-valid answer listed, as dropdown entries — none when nothing valid was kept. */
function listedChoices(endpoint: RowEndpoint): ModelChoice[] {
  return (listingFor(endpoint)?.ids ?? []).map((id) => ({ id, label: id }));
}

/** The saved model alone, which is the whole list until the endpoint has been asked. */
function savedOnly(current: string): ModelChoice[] {
  return current.length > 0 ? [{ id: current, label: current }] : [];
}

/** The endpoint's list, with a saved model it did NOT list kept at the top and marked as such. */
function withTheSavedOne(current: string, listed: ModelChoice[], baseUrl: string): ModelChoice[] {
  return current.length === 0 || listed.some((model) => model.id === current)
    ? listed
    : [{ id: current, label: `${current} — not in what ${hostOf(baseUrl)} listed` }, ...listed];
}

/** Where the list came from, in each of its four states — the caption under the dropdown. */
export function endpointNote(endpoint: RowEndpoint): string {
  const host = hostOf(endpoint.baseUrl);
  if (endpoint.asking === true) {
    return `asking ${host} which models the key under '${endpoint.keyName}' can call…`;
  }
  const listed = listingFor(endpoint);
  if (listed === undefined) {
    return `${host} is not asked until you press ≡ — or choose another model… and type the id it uses.`;
  }

  return listed.ids.length > 0
    ? `${listed.ids.length} models ${host} listed for the key under '${listed.keyName}', asked ${clockOf(listed.askedUtc)} UTC.`
    : `${host} did not list its models: ${listed.reason} — press ≡ to ask again, or type the id.`;
}

/** The endpoint by its host, which is what a person recognises; the whole value when it is not a URL. */
function hostOf(baseUrl: string): string {
  try {
    return new URL(baseUrl).host || 'the endpoint';
  } catch {
    return baseUrl.trim().length > 0 ? baseUrl.trim() : 'the endpoint';
  }
}

/** `HH:MM` of an ISO instant, in UTC — the instant is stored in UTC and said as such. */
function clockOf(iso: string): string {
  const at = new Date(iso);

  return Number.isNaN(at.getTime()) ? '?' : at.toISOString().slice(11, 16);
}

/**
 * The probe's answer — or, when the ask THREW, a refusal carrying the error's own words.
 *
 * <p>`modelsForKey` answers a reason rather than throwing, but what wraps it can still fail: the spawn before
 * it, the progress notification around it. Without this the error flew out of the command, nothing was kept,
 * and the card went back to "not asked" after a wait, saying nothing (code round, codex and gemini). It is
 * the command's edge, so it catches everything, and the error is turned into a value rather than lost.</p>
 */
export async function askedOrRefused(
  run: () => PromiseLike<{ readonly ids: readonly string[]; readonly reason: string }>,
): Promise<{ readonly ids: readonly string[]; readonly reason: string }> {
  try {
    return await run();
  } catch (error) {
    return { ids: [], reason: error instanceof Error ? error.message : String(error) };
  }
}
