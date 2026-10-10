import { ModelPrice } from './modelPrices';
import { ModelUsage, VendorUsage } from './teamServerApi';
import { money } from './usage';

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

/** One figure, and what it is: known at all, a floor, and whether any of it is an estimate rather than a bill. */
export interface Estimate {
  readonly usd: number;
  readonly known: boolean;
  readonly floor: boolean;
  readonly estimated: boolean;
}

/** Nothing priced it: a dash, and — when added to something that is priced — a floor. */
export const UNKNOWN: Estimate = { usd: 0, known: false, floor: true, estimated: false };

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
  return row.models === undefined ? UNKNOWN : totalCost(row.models.map((one) => modelCost(one, price)));
}

/** Several figures added up: known when any part is, a floor when any part is unknown or a floor itself. */
export function totalCost(parts: readonly Estimate[]): Estimate {
  if (parts.length === 0) {
    return UNKNOWN;
  }

  return {
    usd: parts.reduce((sum, part) => sum + part.usd, 0),
    known: parts.some((part) => part.known),
    floor: parts.some((part) => part.floor),
    estimated: parts.some((part) => part.estimated),
  };
}

/** `~$3.00` for an estimate, `$0.4200` for a bill, `≥ ` before a floor, and a dash for nothing known. */
export function costText(estimate: Estimate): string {
  if (!estimate.known) {
    return '—';
  }

  return `${estimate.floor ? '≥ ' : ''}${estimate.estimated ? '~' : ''}${money(estimate.usd)}`;
}
