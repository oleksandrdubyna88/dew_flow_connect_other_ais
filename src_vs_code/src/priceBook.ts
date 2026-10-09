/**
 * The two public price lists, fetched at most once a day and shared by everything in this window
 * that prices tokens — the panel's cards and spending tab, and the settings file an `api` row's
 * price crosses to the server in (PLAN_feature_review.md S3.7).
 *
 * <p><b>Extracted from `PanelProvider`, not written beside it.</b> The panel held the two tables in
 * private fields and refreshed them from its own render. The settings file is written from
 * activation, where no panel may exist yet — so the tables had either to move to where both could
 * reach them or be fetched a second time by a second copy of the refresh; the first is this file.</p>
 *
 * <p>`vscode`-free: the fetch is the platform's, and a test hands in its own.</p>
 */

import { fetchTable, LITELLM_PRICES, liteLlmTable, ModelPrice, OPENROUTER_MODELS, openRouterTable, priceFor, PriceTable, routeOf } from './modelPrices';
import { Vendor } from './vendors';

/** One fetch of one list — `fetchTable`, or a test's stand-in. */
export type FetchList = (url: string, parse: (body: unknown) => PriceTable) => Promise<PriceTable>;

const A_DAY = 24 * 60 * 60 * 1000;

export class PriceBook {
  private openRouterTable: PriceTable = {};
  private liteLlmTable: PriceTable = {};
  private checkedAt = 0;
  private inFlight: Promise<boolean> | undefined = undefined;

  constructor(
    private readonly fetchList: FetchList = fetchTable,
    private readonly now: () => number = Date.now,
  ) {}

  /** OpenRouter's table as last fetched — empty before the first fetch or offline. */
  get openRouter(): PriceTable {
    return this.openRouterTable;
  }

  /** LiteLLM's table as last fetched — empty before the first fetch or offline. */
  get liteLlm(): PriceTable {
    return this.liteLlmTable;
  }

  /**
   * Fetch both lists when the last fetch is a day old, and say whether anything was fetched.
   *
   * <p>One fetch at a time: a render and the activation hook asking together share the one request
   * rather than each spending a download on the same answer.</p>
   */
  async refresh(): Promise<boolean> {
    // The fetch in flight FIRST: its start already stamped the clock, so a caller arriving mid-fetch
    // would otherwise be told "fresh" and read the empty tables the fetch has not filled yet.
    if (this.inFlight !== undefined) {
      return this.inFlight;
    }
    if (this.now() - this.checkedAt <= A_DAY) {
      return false;
    }
    this.inFlight = this.fetchBoth().finally(() => {
      this.inFlight = undefined;
    });

    return this.inFlight;
  }

  /** What a model costs by the lists, asking the row's own provider first when its endpoint names one. */
  priceOf(model: string, baseUrl = ''): ModelPrice | undefined {
    return model.length === 0 ? undefined : priceFor(model, this.openRouterTable, this.liteLlmTable, routeOf(baseUrl));
  }

  private async fetchBoth(): Promise<boolean> {
    this.checkedAt = this.now();
    [this.openRouterTable, this.liteLlmTable] = await Promise.all([
      this.fetchList(OPENROUTER_MODELS, openRouterTable),
      this.fetchList(LITELLM_PRICES, liteLlmTable),
    ]);

    return true;
  }
}

/** The window's one book: the panel and the settings writer read the same tables. */
export const PRICE_BOOK = new PriceBook();

/** What a model costs on a route — {@link PriceBook.priceOf}, or a test's table. */
export type PriceOf = (model: string, baseUrl?: string) => ModelPrice | undefined;

/**
 * The route a row is billed by: an `api` row by the endpoint it names (S3.7b — xAI's own rate for grok on api.x.ai, not
 * OpenRouter's resale price), every other runtime by the published lists alone. ONE decision for the cards and for the
 * spending and consultation tabs, so a row's card and its runs are never priced on two routes (the plan round).
 */
export function billedRoute(row: Pick<Vendor, 'runtime' | 'baseUrl'>): string {
  return row.runtime === 'api' ? row.baseUrl : '';
}

/**
 * The catalog price each Models card shows, keyed by ROW id (todo/PLAN_models_card_prices_every_row.md): every row's
 * own model on its own route. Keyed by model and built from the reviewers only, a consultant-only row showed a dash and
 * an `api` row on a reviewer's model showed the reviewer's rate — and pricing every row by model let a hidden api row
 * put its endpoint's rate on the reviewer's card (PR #681). Keyed by row, no row's price can land on another's card.
 * A row the lists do not know, or with no model, has no entry.
 */
export function cardPrices(rows: readonly Vendor[], priceOf: PriceOf): Readonly<Record<string, ModelPrice>> {
  return Object.fromEntries(rows.flatMap((row) => {
    const price = priceOf(row.model, billedRoute(row));

    return price === undefined ? [] : [[row.id, price] as const];
  }));
}
