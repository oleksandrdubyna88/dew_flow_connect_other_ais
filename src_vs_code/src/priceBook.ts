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
