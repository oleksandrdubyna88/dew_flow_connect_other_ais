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
  /** Which list this came from, so the panel can say so rather than implying a bill. */
  readonly source: 'openrouter' | 'litellm';
}

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
 */
export function priceKey(modelId: string): string {
  return modelId
    .toLowerCase()
    .replace(/-(high|medium|low|thinking|xhigh|max|ultra)$/g, '')
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
  for (const row of rows as { id?: unknown; pricing?: { prompt?: unknown; completion?: unknown } }[]) {
    if (typeof row.id !== 'string' || row.id.includes(':')) {
      continue;
    }
    const inPerMillion = perMillion(row.pricing?.prompt);
    const outPerMillion = perMillion(row.pricing?.completion);
    if (inPerMillion === 0 && outPerMillion === 0) {
      continue; // a free or unpriced entry says nothing about what this model costs
    }
    const key = priceKey(row.id);
    table[key] ??= { inPerMillion, outPerMillion, source: 'openrouter' };
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
    const entry = value as { input_cost_per_token?: unknown; output_cost_per_token?: unknown };
    const inPerMillion = perMillion(entry?.input_cost_per_token);
    const outPerMillion = perMillion(entry?.output_cost_per_token);
    if (inPerMillion === 0 && outPerMillion === 0) {
      continue;
    }
    const key = priceKey(rawKey.replace(/^.*\//, ''));
    const known = table[key];
    if (known === undefined || inPerMillion < known.inPerMillion) {
      table[key] = { inPerMillion, outPerMillion, source: 'litellm' };
    }
  }

  return table;
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
 */
export function priceFor(
  modelId: string,
  openRouter: PriceTable,
  liteLlm: PriceTable,
): ModelPrice | undefined {
  const key = priceKey(modelId);

  return openRouter[key] ?? liteLlm[key];
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
