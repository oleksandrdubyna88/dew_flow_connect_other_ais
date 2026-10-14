import type { GaugeRatio } from './gaugeWords';

/**
 * A subscription's share (story 6.2 of todo/PLAN_every_round_is_counted.md) — the operator's "price" for a flat plan.
 *
 * <p>A monthly plan price, typed ONCE per vendor account (one subscription serves every model on it, so it lives with the
 * account — a Team server's vendor on Setup › Team servers — never on each model card), never looked up. A finite number
 * of dollars ≥ 0; anything else is refused AT THE CARD with the reason and never saved; 0 means "no price", not free.</p>
 *
 * <p>With a weekly window's rounds per 1 % it becomes <i>≈ $0.42 of your plan per round</i> = monthly price × weekly-% per
 * round ÷ 100 ÷ weeks in the month. Shown only beside a figure that is shown — hidden whenever the figure is — labelled as
 * a share, and never summed with a list-price estimate. Pure: the host stores what {@link savePlanPrice} answers.</p>
 */

/** What a typed plan price came to: a price, no price (0), or refused with the reason. */
export type TypedPlanPrice =
  | { readonly kind: 'price'; readonly usd: number }
  | { readonly kind: 'none' }
  | { readonly kind: 'refused'; readonly reason: string };

/** A price refused at the card: which account, what was typed, and why — never saved. */
export interface PlanPriceRefusal {
  readonly key: string;
  readonly typed: string;
  readonly reason: string;
}

/** The `coai.*` setting the prices are kept in — the person's own, never a workspace's (`application` scope). */
export const PLAN_PRICES_SETTING = 'planPrices';

/** The monthly plan prices, by account key (`<team server id>/<vendor>`). */
export type PlanPrices = Readonly<Record<string, number>>;

/** A decimal number of dollars, as a person types one: digits, at most one point, an optional minus to refuse by name. */
const DECIMAL = /^-?(\d+(\.\d*)?|\.\d+)$/;

/** A typed price, read: a price, no price, or the reason it is refused. */
export function readPlanPrice(text: string): TypedPlanPrice {
  const typed = text.trim();
  const usd = numberOf(typed);
  if (!Number.isFinite(usd)) {
    return refused(`"${typed}" is not a number — type the monthly price in US dollars, such as 20 (0 removes it)`);
  }

  return usd < 0 ? refused('a price cannot be negative — type the monthly price in US dollars, or 0 to remove it') : priceOf(usd);
}

function numberOf(typed: string): number {
  return DECIMAL.test(typed) ? Number(typed) : Number.NaN;
}

function priceOf(usd: number): TypedPlanPrice {
  return usd === 0 ? { kind: 'none' } : { kind: 'price', usd };
}

function refused(reason: string): TypedPlanPrice {
  return { kind: 'refused', reason };
}

/** The key an account's price is kept under: the Team server and the vendor it serves. */
export function planPriceKey(serverId: string, vendor: string): string {
  return `${serverId}/${vendor}`;
}

/** The stored prices, read back — finite and positive only; anything else in the setting is not a price. */
export function planPricesFrom(value: unknown): PlanPrices {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return {};
  }

  return Object.fromEntries(Object.entries(value).filter((entry): entry is [string, number] => isPrice(entry[1])));
}

function isPrice(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

/** What a Save press comes to: the prices to store, or the refusal the card shows — nothing stored. */
export type PlanPriceSave =
  | { readonly kind: 'saved'; readonly prices: PlanPrices }
  | { readonly kind: 'refused'; readonly refusal: PlanPriceRefusal };

/**
 * One Save press, as the page posts it: `<key>|<what was typed>`. A new record either way — the prices held are never
 * changed in place; 0 takes the account's price away.
 */
export function savePlanPrice(prices: PlanPrices, id: string): PlanPriceSave {
  const { key, typed } = pressed(id);
  const read = key.length === 0 ? refused('the press named no account') : readPlanPrice(typed);

  return read.kind === 'refused' ? { kind: 'refused', refusal: { key, typed, reason: read.reason } } : { kind: 'saved', prices: withPrice(prices, key, read) };
}

/** The account and the text of a press — the key before the first `|`, whatever was typed after it. */
function pressed(id: string): { readonly key: string; readonly typed: string } {
  const at = id.indexOf('|');

  return at > 0 ? { key: id.slice(0, at), typed: id.slice(at + 1) } : { key: '', typed: id };
}

function withPrice(prices: PlanPrices, key: string, read: Exclude<TypedPlanPrice, { kind: 'refused' }>): PlanPrices {
  const others = Object.fromEntries(Object.entries(prices).filter(([one]) => one !== key));

  return read.kind === 'price' ? { ...others, [key]: read.usd } : others;
}

/** The weekly window — the one a monthly plan's share is taken over. */
const WEEK_MINS = 10080;

/** `≈ $0.11 of your plan per round — a share of the $200/month plan, not a bill`, or '' when any part is missing. */
export function shareLine(priceUsd: number | undefined, ratio: GaugeRatio | undefined, nowMs: number): string {
  const perPercent = shownWeekly(ratio);
  if (!isPrice(priceUsd) || perPercent === undefined) {
    return '';
  }
  const share = (priceUsd * (1 / perPercent)) / 100 / weeksIn(nowMs);
  if (!Number.isFinite(share)) {
    return '';
  }

  return `≈ ${money(share)} of your plan per round — a share of the $${dollars(priceUsd)}/month plan, not a bill`;
}

/** The weekly window's rounds per 1 % when it is SHOWN — undefined when hidden, unavailable or another window. */
function shownWeekly(ratio: GaugeRatio | undefined): number | undefined {
  const value = ratio?.windowMins === WEEK_MINS ? ratio.roundsPerPercent : null;

  return isPrice(value) ? value : undefined;
}

/** How many weeks the month of `nowMs` (UTC) holds. */
function weeksIn(nowMs: number): number {
  const at = new Date(nowMs);

  return new Date(Date.UTC(at.getUTCFullYear(), at.getUTCMonth() + 1, 0)).getUTCDate() / 7;
}

function money(usd: number): string {
  return usd < 0.01 ? '< $0.01' : `$${usd.toFixed(2)}`;
}

function dollars(usd: number): string {
  return Number.isInteger(usd) ? String(usd) : usd.toFixed(2);
}
