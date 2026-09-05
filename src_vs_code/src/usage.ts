import { Vendor } from './vendors';
/**
 * What each AI has consumed, read from the server's append-only ledger.
 *
 * <p>Pure: the file read lives in the provider, the arithmetic and the shapes are here, so the
 * numbers on the chart are a test rather than something checked by squinting at a sidebar.</p>
 */

export interface UsageEntry {
  readonly utc: string;
  readonly provider: string;
  readonly model: string;
  readonly role: string;
  readonly stage: string;
  readonly seconds: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  readonly costUsd: number | null;
  readonly outcome: string;
}

export type Window = 'day' | 'week' | 'month' | 'year';

export const WINDOWS: readonly { readonly id: Window; readonly label: string; readonly days: number }[] = [
  { id: 'day', label: 'Today', days: 1 },
  { id: 'week', label: 'Week', days: 7 },
  { id: 'month', label: 'Month', days: 30 },
  { id: 'year', label: 'Year', days: 365 },
];

export interface VendorTotals {
  readonly provider: string;
  readonly runs: number;
  readonly failed: number;
  readonly tokensIn: number;
  readonly tokensOut: number;
  /** Only from vendors that price their own runs; null when nobody in this row reported money. */
  readonly costUsd: number | null;
  /**
   * What this row cost according to the RATES the person entered, when the vendor reported none.
   *
   * <p>Null when there is no rate, and null when the vendor reported a real cost — an estimate
   * beside a bill is noise, and the bill is the fact. Marked with a tilde wherever it is shown,
   * because a number derived from tokens is not the same kind of thing as a number a vendor
   * charged.</p>
   */
  readonly estimatedUsd: number | null;
  readonly seconds: number;
  readonly averageSeconds: number;
}

/** One ledger line; a torn or foreign line is skipped rather than breaking the chart. */
export function parseUsageLine(line: string): UsageEntry | undefined {
  try {
    const parsed = JSON.parse(line) as Partial<UsageEntry>;
    if (typeof parsed.utc !== 'string' || typeof parsed.provider !== 'string') {
      return undefined;
    }
    // Every metric is coerced, because ONE missing number turns a sum into NaN and the whole
    // chart reads "NaN tokens" — a line written by an older server, or a torn one, must cost its
    // own row and nothing else. Caught by a reviewer on the commit that added this file.
    return {
      utc: parsed.utc,
      provider: parsed.provider,
      model: typeof parsed.model === 'string' ? parsed.model : '',
      role: typeof parsed.role === 'string' ? parsed.role : '',
      stage: typeof parsed.stage === 'string' ? parsed.stage : '',
      seconds: number(parsed.seconds),
      tokensIn: number(parsed.tokensIn),
      tokensOut: number(parsed.tokensOut),
      costUsd: typeof parsed.costUsd === 'number' && Number.isFinite(parsed.costUsd) ? parsed.costUsd : null,
      outcome: typeof parsed.outcome === 'string' ? parsed.outcome : 'ok',
    };
  } catch {
    return undefined;
  }
}

function number(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

export function parseUsage(text: string): UsageEntry[] {
  return text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
    .map(parseUsageLine)
    .filter((e): e is UsageEntry => e !== undefined);
}

/**
 * The entries inside a window.
 *
 * <p><b>"Today" is since local midnight.</b> It was the last 24 hours — "a review at 23:50 and one
 * at 00:10 belong to the same piece of work" — and the operator overruled it on 2026-09-05: what
 * today cost is a question about the calendar day, and a rolling day answers a different one. Week,
 * month and year stay rolling: a quiet Monday morning must still show last week's work.</p>
 */
export function within(entries: readonly UsageEntry[], window: Window, now: Date): UsageEntry[] {
  const days = WINDOWS.find((w) => w.id === window)?.days ?? 1;
  const cutoff = window === 'day'
    ? new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
    : now.getTime() - days * 24 * 60 * 60 * 1000;
  return entries.filter((e) => {
    const at = Date.parse(e.utc);
    return Number.isFinite(at) && at >= cutoff;
  });
}

/** Per vendor, biggest spender first — the row order a person actually scans for. */
export function totalsByVendor(
  entries: readonly UsageEntry[],
  vendors: readonly Vendor[] = [],
  listed: PriceLookup = () => undefined,
): VendorTotals[] {
  const byProvider = new Map<string, UsageEntry[]>();
  for (const e of entries) {
    byProvider.set(e.provider, [...(byProvider.get(e.provider) ?? []), e]);
  }

  return [...byProvider.entries()]
    .map(([provider, rows]) => {
      const priced = rows.filter((r) => typeof r.costUsd === 'number');
      const seconds = sum(rows.map((r) => r.seconds));
      const tokensIn = sum(rows.map((r) => r.tokensIn));
      const tokensOut = sum(rows.map((r) => r.tokensOut));
      const reported = priced.length === 0 ? null : sum(priced.map((r) => r.costUsd as number));
      const price = priceOf(provider, vendors, listed);
      return {
        provider,
        runs: rows.length,
        failed: rows.filter((r) => r.outcome !== 'ok').length,
        tokensIn,
        tokensOut,
        costUsd: reported,
        estimatedUsd:
          reported !== null || price === undefined
            ? null
            : round4((tokensIn / 1_000_000) * price.in + (tokensOut / 1_000_000) * price.out),
        seconds,
        averageSeconds: rows.length === 0 ? 0 : seconds / rows.length,
      };
    })
    .sort((a, b) => b.tokensIn + b.tokensOut - (a.tokensIn + a.tokensOut));
}

function sum(values: readonly number[]): number {
  return values.reduce((total, v) => total + v, 0);
}

/** 12 345 → "12.3k"; a chart row has no space for six digits and nobody counts them. */
export function shortNumber(value: number): string {
  if (value >= 1_000_000) {
    return `${(value / 1_000_000).toFixed(1)}M`;
  }
  if (value >= 1_000) {
    return `${(value / 1_000).toFixed(1)}k`;
  }
  return String(Math.round(value));
}

export function shortDuration(seconds: number): string {
  if (seconds >= 3600) {
    return `${(seconds / 3600).toFixed(1)} h`;
  }
  if (seconds >= 60) {
    return `${(seconds / 60).toFixed(1)} min`;
  }
  return `${Math.round(seconds)} s`;
}

/**
 * Money, or an honest dash.
 *
 * <p>A vendor that does not price its own runs contributes tokens and no money, and that MUST NOT
 * render as `$0.00`: free and unreported are different facts, and only one of them is good news.</p>
 */
export function money(costUsd: number | null): string {
  if (costUsd === null) {
    return '—';
  }

  // Cents are not enough. A single round is fractions of a dollar, and `toFixed(2)` turns $0.0004
  // into "$0.00", which reads as free. There were TWO of this function — this one rounding to cents
  // and the one in rounds.ts using four decimals, whose comment warned about exactly the bug this
  // one had. One concept, one implementation.
  return costUsd >= 1 ? `$${costUsd.toFixed(2)}` : `$${costUsd.toFixed(4)}`;
}

/** The bar width for one row, as a percentage of the busiest row. */
export function barWidth(value: number, max: number): number {
  return max <= 0 ? 0 : Math.max(2, Math.round((value / max) * 100));
}

/**
 * This vendor's rates, or nothing when it has none.
 *
 * <p>Found by ID, because that is what the ledger records and what the panel row is named by — the
 * model can change between runs and the rate follows the vendor, not the model.</p>
 */
export function priceOf(
  provider: string,
  vendors: readonly Vendor[],
  listed: PriceLookup = () => undefined,
): { readonly in: number; readonly out: number } | undefined {
  const vendor = vendors.find((v) => v.id === provider);
  if (vendor === undefined) {
    return undefined;
  }
  // A TYPED rate is a fact about this account — a flat subscription, a negotiated rate, a free
  // tier — and a published list price is a general estimate. The specific statement wins over the
  // general one, per field: somebody who filled in only the input rate keeps it, and the output
  // rate falls back rather than the whole vendor going dark.
  const published = listed(vendor.model);
  const inRate = vendor.pricePerMillionIn > 0 ? vendor.pricePerMillionIn : (published?.inPerMillion ?? 0);
  const outRate = vendor.pricePerMillionOut > 0 ? vendor.pricePerMillionOut : (published?.outPerMillion ?? 0);

  return inRate === 0 && outRate === 0 ? undefined : { in: inRate, out: outRate };
}

/**
 * What a model costs per million tokens according to a public list, or nothing.
 *
 * <p>A function rather than a table so this file stays free of the fetching: the panel holds the
 * lists, `usage.ts` holds the arithmetic, and a test can answer for one model without a network.</p>
 */
export type PriceLookup = (modelId: string) => { inPerMillion: number; outPerMillion: number } | undefined;

/** An estimate, marked as one. The tilde is the whole point: this is not what anybody billed. */
export function estimated(usd: number): string {
  return `~${money(usd)}`;
}

/** Four decimals, so a fraction of a cent does not become zero on the way through. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
}
