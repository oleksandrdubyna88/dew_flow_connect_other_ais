import { ModelPrice } from './modelPrices';
import { ModelUsage, VendorUsage } from './teamServerApi';
import { Estimate, totalCost, UNKNOWN } from './usage';

// The figure, its sum and its text moved to `usage.ts` with story 2.5 of todo/PLAN_every_round_is_counted.md, where the
// spending tab prices each ledger line by the same three states; re-exported so this tab's callers are unchanged.
export { costText, totalCost, UNKNOWN } from './usage';
export type { Estimate } from './usage';

/**
 * What the team's spending would cost at public list prices (todo/PLAN_team_usage_by_person.md, story 3.2, D4).
 *
 * <p><b>Each model at ITS list price.</b> The Team server reports, under each vendor, a row per model that ran; the price
 * book (`PRICE_BOOK.priceOf`, OpenRouter then LiteLLM) answers per model. Never a vendor's CURRENT model — that is not
 * what ran — and never an admin's own typed row rate (`priceOfLine` prefers those): a rate this machine's person typed
 * for their own subscription says nothing about a colleague's runs on the company's.</p>
 *
 * <p><b>Never a long-context tier.</b> `ModelPrice.tier` starts at a number of prompt tokens in ONE request; a week's
 * summed input crosses it on its first afternoon, and pricing the sum by it would double every grok figure.</p>
 *
 * <p><b>A vendor-reported cost wins</b> over an estimate, and keeps its own floor mark. A model no list prices is a
 * dash — never `$0` — and a total that includes one is a FLOOR, shown `≥`. The server's lines carry no cached-token
 * count yet, so every input token is priced at the full input rate: an estimate over cache-heavy input overstates, and
 * the tab's note says so.</p>
 *
 * <p>Pure: the price lookup is handed in.</p>
 */

/** A model's list price, or nothing when no list names it. */
export type ListPrice = (model: string) => ModelPrice | undefined;

const PER_MILLION = 1_000_000;

/** One model's spending: the vendor's own figure when it reported one, else its list price, else unknown. */
export function modelCost(row: ModelUsage, price: ListPrice): Estimate {
  if (typeof row.costUsd === 'number') {
    return { usd: row.costUsd, known: true, floor: row.costIsFloor === true, estimated: false };
  }
  const listed = row.model.length === 0 ? undefined : price(row.model);

  return listed === undefined ? UNKNOWN : {
    usd: (row.tokensIn * listed.inPerMillion + row.tokensOut * listed.outPerMillion) / PER_MILLION,
    known: true,
    floor: false,
    estimated: true,
  };
}

/** One vendor's spending, from its models — or unknown when the server is too old to say which models ran. */
export function vendorCost(row: VendorUsage, price: ListPrice): Estimate {
  return row.models === undefined ? reported(row) : totalCost(row.models.map((one) => modelCost(one, price)));
}

/**
 * What an older server's vendor row says it was BILLED — 0.10.0 already sends `costUsd` and `costIsFloor` per vendor,
 * and a reported cost wins over any estimate (D4); it needs no model to be a bill. Nothing reported is unknown.
 */
function reported(row: VendorUsage): Estimate {
  return typeof row.costUsd === 'number' ? { usd: row.costUsd, known: true, floor: row.costIsFloor === true, estimated: false } : UNKNOWN;
}

/** The one question the tab asks of a price book: what does this model cost by the public lists. */
export interface ListedPrices {
  readonly priceOf: (model: string) => ModelPrice | undefined;
}

/**
 * The tab's price lookup over the window's price book: the model's LIST price, and only that.
 *
 * <p>Named and kept here, free of \`vscode\`, so that "never the admin's own typed row rates" (D4) is a test rather than a
 * comment on a lambda in the panel: \`priceOfLine\` — what the spending tab uses — prefers a vendor row's typed rate,
 * which is a fact about THIS machine's person, not about a colleague's runs on the company's subscription.</p>
 */
export function listPriceFrom(book: ListedPrices): ListPrice {
  return (model) => book.priceOf(model);
}
