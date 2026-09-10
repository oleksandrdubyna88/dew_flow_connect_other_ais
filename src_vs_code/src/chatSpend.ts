/**
 * What a conversation has cost, as the tab shows it.
 *
 * <p>A chat turn carries the whole conversation, so question five is billed for one through four as
 * well — `chatCommand.ts` says so out loud where the carry is built. That is the number a person
 * would use to decide between asking again and starting a fresh conversation, and it is precisely
 * the moment at which the number was invisible: the ledger writes every turn down, and this says the
 * running total while the decision is being made.</p>
 *
 * <p><b>An estimate is marked as one, and the tilde is not decoration.</b> The three vendors do not
 * report comparable numbers — `claude` counts cache reads, `antigravity` omits cache entirely,
 * `codex` reports a cumulative maximum — which is measured and recorded in
 * `todo/PLAN_usage_that_compares.md`. For two of the three, a cost worked out from tokens is not
 * what anybody was charged. `usage.ts` already writes `~$0.42` for exactly this, and one convention
 * for one thing is worth more than a prettier line here.</p>
 */

/** Four decimals, the precision this product counts money in — cents would read a real cost as free. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
}

export interface TurnSpend {
  /** What the vendor said this turn cost, or null when it said nothing this product can use. */
  readonly costUsd: number | null;
  /** Worked out from tokens rather than reported as a bill. */
  readonly estimated: boolean;
}

export interface Spend {
  readonly usd: number;
  /** Any part of it worked out from tokens, which makes the whole total an estimate. */
  readonly estimated: boolean;
  /** Turns whose cost nobody reported — counted, because a total that hides them reads as complete. */
  readonly unpriced: number;
}

/**
 * The running total.
 *
 * <p>A turn nobody priced is COUNTED rather than skipped. `codex` reports a cumulative maximum that
 * the ledger refuses to difference, so its turns legitimately arrive with no cost at all — and a
 * total that quietly left them out would be the most confident number on the page and the least
 * true. A nonsense value is treated the same way, because a negative bill is not a credit and NaN is
 * not a price.</p>
 */
export function spendSoFar(turns: readonly TurnSpend[]): Spend {
  let usd = 0;
  let estimated = false;
  let unpriced = 0;

  for (const turn of turns) {
    const cost = turn.costUsd;
    if (typeof cost !== 'number' || !Number.isFinite(cost) || cost < 0) {
      unpriced += 1;
      continue;
    }
    usd += cost;
    estimated = estimated || turn.estimated;
  }

  return { usd: round4(usd), estimated, unpriced };
}

/**
 * The line the tab shows, or nothing at all when there is nothing to say.
 *
 * <p>One estimated turn makes the whole total an estimate: a sum of one measured price and one guess
 * is a guess, and presenting it as a bill would be the most confident number on the page.</p>
 */
export function spendLabel(spend: Spend): string {
  if (spend.usd === 0 && spend.unpriced === 0) {
    return '';
  }
  const money = spend.usd === 0 ? '' : `${spend.estimated ? '~' : ''}$${spend.usd.toFixed(4)}`;
  if (spend.unpriced === 0) {
    return money;
  }
  const unpriced = `${spend.unpriced} turn${spend.unpriced === 1 ? '' : 's'} nobody priced`;

  return money.length === 0 ? unpriced : `${money} + ${unpriced}`;
}
