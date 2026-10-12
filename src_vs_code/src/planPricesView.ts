import { escapeHtml } from './escapeHtml';
import { planPriceKey, type PlanPriceRefusal, type PlanPrices } from './planPrice';
import type { TeamServerState } from './teamServerView';

/**
 * Where a monthly plan price is typed (story 6.2 of todo/PLAN_every_round_is_counted.md): Setup › Team servers, once per
 * vendor account a server this side is signed in to serves — one subscription serves every model on it, so the price lives
 * with the account, never on a model card. One box and one Save price button each: every action is a panel button.
 *
 * <p>The page posts the account and what was typed (`catalogPageScript.ts`); the HOST reads it (`planPrice.ts`) and either
 * stores it or draws the refusal here, at the card, with what was typed left in the box — a refused price is never
 * saved, and the box never silently snaps back to the stored one. Pure.</p>
 */

/** One account's row: its key, its words, and the box's id — built from a position, so no server text reaches an id. */
interface Row {
  readonly key: string;
  readonly label: string;
  readonly box: string;
}

const HEAD = '<h3>Subscription prices</h3><p class="hint">What each vendor\'s plan on a Team server costs you per month, in '
  + 'US dollars — typed once per account, never looked up. It is used only to show <i>≈ $0.11 of your plan per round</i> '
  + 'beside that vendor\'s own gauge on the Team server tab, when every consumer of the account can be counted: a share of '
  + 'a flat plan, never a bill. 0 removes a price.</p>';

/** The block: a row per vendor account of each signed-in server, or one sentence when no server is signed in. */
export function planPricesHtml(states: readonly TeamServerState[], prices: PlanPrices, refusal: PlanPriceRefusal | undefined): string {
  const rows = rowsOf(states);
  const body = rows.length === 0
    ? '<p class="hint">Sign in to a Team server to type what its subscriptions cost.</p>'
    : rows.map((row) => rowHtml(row, prices, refusal)).join('');

  return `<div class="plan-prices">${HEAD}${body}</div>`;
}

function rowsOf(states: readonly TeamServerState[]): readonly Row[] {
  return states
    .filter((state) => state.email.length > 0)
    .flatMap((state) => (state.catalog?.vendors ?? []).map((vendor) => ({ key: planPriceKey(state.server.id, vendor.id), label: `${vendor.id} on ${state.server.name}` })))
    .map((row, at) => ({ ...row, box: `plan-price-${at}` }));
}

function rowHtml(row: Row, prices: PlanPrices, refusal: PlanPriceRefusal | undefined): string {
  const refused = refusal?.key === row.key ? refusal : undefined;

  return `<div class="field plan-price"><label for="${row.box}">${escapeHtml(row.label)} — per month, US $</label>`
    + boxHtml(row, shownValue(row, prices, refused), refused !== undefined)
    + `<button type="button" data-plan-price="${escapeHtml(row.key)}" data-plan-price-box="${row.box}">Save price</button>${whyHtml(row, refused)}</div>`;
}

/** What the box holds: the text that was refused, else the stored price, else nothing. */
function shownValue(row: Row, prices: PlanPrices, refused: PlanPriceRefusal | undefined): string {
  return refused?.typed ?? storedText(prices[row.key]);
}

function storedText(price: number | undefined): string {
  return price === undefined ? '' : String(price);
}

/** The box — what was typed and refused, or the stored price — pointing at its refusal when there is one. */
function boxHtml(row: Row, value: string, refused: boolean): string {
  const described = refused ? ` aria-describedby="${row.box}-why"` : '';

  return `<input type="text" inputmode="decimal" id="${row.box}" value="${escapeHtml(value)}"${described}>`;
}

function whyHtml(row: Row, refused: PlanPriceRefusal | undefined): string {
  return refused === undefined ? '' : `<p class="refused" role="alert" id="${row.box}-why">${escapeHtml(refused.reason)}</p>`;
}
