/**
 * What a model costs per million tokens, looked up rather than typed.
 *
 * <p>The panel takes two rates per vendor and computes the money from them. Typing them is the part
 * nobody does, so the numbers stayed dashes — and both public sources for them turn out to carry
 * every model this build offers. Checked live on 2026-09-01, not recalled.</p>
 *
 * <p><b>A looked-up rate is a LIST price, never a bill.</b> Reviews here run through a vendor's CLI
 * on a subscription; the API price is what those tokens would have cost through the API, which is a
 * useful order of magnitude and not an invoice. So it never overwrites a rate somebody typed, and
 * the money it produces keeps the tilde that already means "worked out, not charged".</p>
 *
 * <p><b>Two sources, in order.</b> OpenRouter answers one endpoint with canonical vendor-prefixed
 * ids; LiteLLM's price file covers what OpenRouter does not (`gpt-5.6-sol` and `gpt-5.6-terra` are
 * listed there and not there). Both are read-only public JSON, no key, no account.</p>
 */

/** Dollars per million tokens. Zero means "no answer", never "free". */
export interface ModelPrice {
  readonly inPerMillion: number;
  readonly outPerMillion: number;
  /** The CACHED input rate — absent when the list does not say, and then the input rate applies. */
  readonly cachedPerMillion?: number;
  readonly tier?: PriceTier;
  /** Which list this came from, so the panel can say so rather than implying a bill. */
  readonly source: 'openrouter' | 'litellm';
}

/**
 * A long-context tier: from `fromTokens` prompt tokens in ONE request, every rate is this one.
 *
 * <p>xAI doubles every grok rate from 200K prompt tokens, and LiteLLM lists it as
 * `*_cost_per_token_above_200k_tokens` (read 2026-09-26). Only LiteLLM carries tiers; OpenRouter
 * publishes one rate per model.</p>
 */
export interface PriceTier {
  readonly fromTokens: number;
  readonly inPerMillion: number;
  readonly outPerMillion: number;
  readonly cachedPerMillion: number;
}

/**
 * The provider whose OWN price list a row's endpoint answers to, or none (PLAN_feature_review.md S3.7b).
 *
 * <p>OpenRouter's number for a model is OpenRouter's resale price, not the vendor's: grok-4.7 is 1.60 /
 * 4.80 there and 2.00 / 0.50 / 6.00 at xAI (both read 2026-09-26). A row that talks to the vendor
 * directly is billed at the vendor's rate, which LiteLLM keeps under `<provider>/<model>`.</p>
 */
export type PriceRoute = '' | 'xai' | 'dashscope' | 'zai';

/**
 * Which LiteLLM provider an endpoint belongs to, by its host.
 *
 * <p>DashScope covers both the general `dashscope*.aliyuncs.com` hosts and the per-workspace and Token
 * Plan hosts under `maas.aliyuncs.com` — one vendor, one price list. Anything unrecognised is no route,
 * and the lookup then asks in the order it always has.</p>
 */
export function routeOf(baseUrl: string): PriceRoute {
  let host: string;
  try {
    host = new URL(baseUrl).hostname.toLowerCase();
  } catch {
    return '';
  }

  return ROUTES.find(([suffix]) => host === suffix || host.endsWith(`.${suffix}`))?.[1] ?? '';
}

/** Host suffix → provider, each a host that answers with that vendor's own bill. */
const ROUTES: readonly (readonly [string, PriceRoute])[] = [
  ['x.ai', 'xai'],
  ['aliyuncs.com', 'dashscope'],
  ['z.ai', 'zai'],
  ['bigmodel.cn', 'zai'],
];

export const OPENROUTER_MODELS = 'https://openrouter.ai/api/v1/models';
export const LITELLM_PRICES =
  'https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json';

/**
 * The panel's model id reduced to the name a price list would use.
 *
 * <p>Three differences, each real and each observed in the lists themselves:</p>
 *
 * <ul>
 *   <li>A reasoning EFFORT is not a model. `gemini-3.7-flash-high` and `-low` are one model at two
 *       settings, priced identically, and no list has the suffix.</li>
 *   <li>Anthropic writes versions with a dot; this panel's ids use a dash, because they came from a
 *       CLI's own naming. `claude-opus-4-6-thinking` is `claude-opus-4.6`.</li>
 *   <li>A vendor prefix (`openai/`, `google/`, `anthropic/`) is present in one list and absent in
 *       the other, so matching ignores it.</li>
 * </ul>
 *
 * <p>And one thing that looks like an effort and is not: `-max` names a model (`qwen3.8-max`).</p>
 */
export function priceKey(modelId: string): string {
  return modelId
    .toLowerCase()
    // `max` is NOT an effort: `qwen3.8-max` is a model, and stripping it left `qwen3.8`, which no list
    // prices (§9.12, found 2026-09-26). Every effort this panel sends is in the list; `max` never was one.
    .replace(/-(high|medium|low|thinking|xhigh|ultra)$/g, '')
    .replace(/-(\d+)-(\d+)$/, '-$1.$2')
    .replace(/^[a-z-]+\//, '');
}

/** One price list, flattened to `key -> price`. */
export type PriceTable = Readonly<Record<string, ModelPrice>>;

/**
 * OpenRouter's catalogue as a price table.
 *
 * <p>Its `pricing` is dollars per TOKEN as strings, and `:batch` / `:free` variants are dropped —
 * they are the same model at a different commercial rate, and taking one because it sorted first
 * would quietly halve every number.</p>
 */
export function openRouterTable(body: unknown): PriceTable {
  const rows = (body as { data?: unknown })?.data;
  if (!Array.isArray(rows)) {
    return {};
  }
  const table: Record<string, ModelPrice> = {};
  for (const row of rows as { id?: unknown; pricing?: { prompt?: unknown; completion?: unknown; input_cache_read?: unknown } }[]) {
    if (typeof row.id !== 'string' || row.id.includes(':')) {
      continue;
    }
    const inPerMillion = perMillion(row.pricing?.prompt);
    const outPerMillion = perMillion(row.pricing?.completion);
    if (inPerMillion === 0 && outPerMillion === 0) {
      continue; // a free or unpriced entry says nothing about what this model costs
    }
    const key = priceKey(row.id);
    table[key] ??= withCached({ inPerMillion, outPerMillion, source: 'openrouter' }, perMillion(row.pricing?.input_cache_read));
  }

  return table;
}

/**
 * LiteLLM's price file as a price table.
 *
 * <p>Its keys carry a deployment prefix — `azure/`, `azure/eu/`, `bedrock/`, `deepinfra/google/` —
 * and the same model appears under several at different regional rates. The cheapest is taken, and
 * the reason is the direction of the error: this number goes beside a tilde and under a heading
 * about what things cost, so the honest failure is to under-state a list price rather than to
 * inflate a bill nobody was sent.</p>
 */
export function liteLlmTable(body: unknown): PriceTable {
  if (body === null || typeof body !== 'object') {
    return {};
  }
  const table: Record<string, ModelPrice> = {};
  for (const [rawKey, value] of Object.entries(body as Record<string, unknown>)) {
    const price = liteLlmPrice(value);
    if (price === undefined) {
      continue;
    }
    const key = priceKey(rawKey.replace(/^.*\//, ''));
    const known = table[key];
    if (known === undefined || price.inPerMillion < known.inPerMillion) {
      table[key] = price;
    }
    // And under its PROVIDER, for a row whose endpoint is that provider (S3.7b). Only a plain
    // `provider/model` key — `azure/eu/…` and `deepinfra/google/…` are deployments, not a vendor's list.
    const [provider, model, ...deeper] = rawKey.toLowerCase().split('/');
    if (provider !== undefined && model !== undefined && deeper.length === 0) {
      table[`${provider}/${priceKey(model)}`] = price;
    }
  }

  return table;
}

/** One LiteLLM entry as a price — its three rates and its long-context tier — or nothing when it has no price. */
function liteLlmPrice(value: unknown): ModelPrice | undefined {
  // `Object(null)` is `{}`: an entry that is not an object simply has no price fields.
  const entry = Object(value) as Record<string, unknown>;
  const inPerMillion = perMillion(entry['input_cost_per_token']);
  const outPerMillion = perMillion(entry['output_cost_per_token']);
  if (inPerMillion === 0 && outPerMillion === 0) {
    return undefined;
  }
  const tier = liteLlmTier(entry);

  return {
    ...withCached({ inPerMillion, outPerMillion, source: 'litellm' }, perMillion(entry['cache_read_input_token_cost'])),
    ...(tier === undefined ? {} : { tier }),
  };
}

/**
 * The long-context tier, from `input_cost_per_token_above_<N>k_tokens` and its two siblings.
 *
 * <p>Exactly that shape: a `_priority`, `_batches` or `_flex` suffix is a different commercial rate for
 * the same tokens, and taking one would price an ordinary request at a rate nobody chose.</p>
 */
function liteLlmTier(entry: Readonly<Record<string, unknown>>): PriceTier | undefined {
  const threshold = Object.keys(entry)
    .map((field) => /^input_cost_per_token_above_(\d+)k_tokens$/u.exec(field)?.[1])
    .find((found) => found !== undefined);
  if (threshold === undefined) {
    return undefined;
  }
  const above = `_above_${threshold}k_tokens`;
  const inPerMillion = perMillion(entry[`input_cost_per_token${above}`]);
  const outPerMillion = perMillion(entry[`output_cost_per_token${above}`]);

  return inPerMillion === 0 && outPerMillion === 0
    ? undefined
    : {
      fromTokens: Number(threshold) * 1000,
      inPerMillion,
      outPerMillion,
      cachedPerMillion: perMillion(entry[`cache_read_input_token_cost${above}`]),
    };
}

/** The cached rate added only when a list states one: absent means "not said", which is not zero. */
function withCached(price: ModelPrice, cachedPerMillion: number): ModelPrice {
  return cachedPerMillion > 0 ? { ...price, cachedPerMillion } : price;
}

function perMillion(value: unknown): number {
  const n = typeof value === 'string' ? Number.parseFloat(value) : typeof value === 'number' ? value : 0;
  if (!Number.isFinite(n) || n <= 0) {
    return 0;
  }
  // Rounded, because a price per TOKEN times a million is a float artefact waiting to be printed:
  // gpt-oss-120b's 0.00000017 comes out as 0.16999999999999998. No published rate is finer than
  // six decimals of a dollar per million.
  return Math.round(n * 1_000_000 * 1e6) / 1e6;
}

/**
 * The price for one model, from whichever list knows it.
 *
 * <p>OpenRouter first because its ids are the vendors' own; LiteLLM second because it covers the
 * models OpenRouter has not listed. Nothing is invented for a model neither knows — an absent price
 * stays absent, and the row keeps its dash.</p>
 *
 * <p><b>Unless the row names its provider</b> (S3.7b): an endpoint that IS the vendor — `api.x.ai`, a
 * DashScope host, `api.z.ai` — is billed at the vendor's own rate, so LiteLLM's `<provider>/<model>`
 * answers first and OpenRouter's resale price only when the provider lists no such model.</p>
 */
export function priceFor(
  modelId: string,
  openRouter: PriceTable,
  liteLlm: PriceTable,
  route: PriceRoute = '',
): ModelPrice | undefined {
  const key = priceKey(modelId);

  // `liteLlm['/key']` for no route is a key no table holds, so it answers nothing — no branch needed.
  return liteLlm[`${route}/${key}`] ?? openRouter[key] ?? liteLlm[key];
}

/** Fetch one list, or nothing. An offline machine is not an error worth showing. */
export async function fetchTable(
  url: string,
  parse: (body: unknown) => PriceTable,
): Promise<PriceTable> {
  try {
    const response = await fetch(url, { headers: { accept: 'application/json' } });

    return response.ok ? parse(await response.json()) : {};
  } catch {
    return {};
  }
}
