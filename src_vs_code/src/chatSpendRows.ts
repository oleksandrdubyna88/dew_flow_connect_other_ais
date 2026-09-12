import { ChatDoorRecord, asking } from './chatDoors';
import { ChatTurnRecord } from './chatUsage';
import { Window, within } from './usage';

/**
 * What the CHAT has cost, as rows — the other half of the spending page.
 *
 * <p>*What each AI has used* reads the server's ledger, which holds reviewers and nothing else, so a
 * day spent asking a second model has always cost nothing on the one screen that adds money up. This
 * is the arithmetic behind the section that fixes it, kept pure for the usual reason: a number on a
 * panel is checked by squinting, and a number out of a function is checked by a test.</p>
 *
 * <h2>Per vendor AND model, where the reviewer cards are per vendor</h2>
 *
 * <p>A chat switches model mid-conversation — that is what the picker is for — and a rate belongs to
 * a model. A row keyed on the vendor alone could not show a price at all without averaging two rates
 * into a number nobody is charged.</p>
 *
 * <h2>What a door's count is a count OF</h2>
 *
 * <p>A door record carries the provider and model that were in force when the command was used,
 * which is a different claim from the model that answered: the door may never be answered at all,
 * and a conversation can switch afterwards. So a row's `asked` and `opened` are "how often this pair
 * was reached for", and the section TOTAL is the honest sum of every invocation — which is why the
 * total is computed from the records rather than by adding the rows up. (Two reviewers raised the
 * attribution on the plan round, independently.)</p>
 */

/** Four decimals, the precision this product counts money in — cents would read a real cost as free. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
}

/** What a rate lookup answers: the published or typed price for one model of one vendor. */
export type ChatPriceOf = (model: string, provider: string) =>
  { readonly inPerMillion: number; readonly outPerMillion: number } | undefined;

export interface ChatSpendRow {
  readonly provider: string;
  readonly model: string;
  readonly tokensIn: number;
  readonly tokensOut: number;
  /** The rate in force, or null when nothing prices this model — shown as a dash, never as zero. */
  readonly inPerMillion: number | null;
  readonly outPerMillion: number | null;
  /** What the vendor BILLED in this window; null when it billed nothing this product can use. */
  readonly costUsd: number | null;
  /** Worked out from the rates, and only when nothing was billed — the convention `usage.ts` uses. */
  readonly estimatedUsd: number | null;
  /** Turns with neither a bill nor a rate. Counted, because a total that hides them reads complete. */
  readonly unpriced: number;
  /** The money over the WHOLE ledger, outside the window: asked for in those words. */
  readonly allTimeUsd: number | null;
  /** Any part of the all-time figure worked out from a rate rather than billed. */
  readonly allTimeEstimated: boolean;
  /** Turns that finished in this window, whatever their outcome. */
  readonly turns: number;
  /** `take the question` and `add the question`, in this window. */
  readonly asked: number;
  /** Every door, in this window. */
  readonly opened: number;
}

export interface ChatSpendTotals {
  readonly tokens: number;
  readonly costUsd: number | null;
  readonly estimatedUsd: number | null;
  readonly unpriced: number;
  readonly asked: number;
  readonly opened: number;
}

/** The key a row is grouped on. A NUL between them, so two halves can never spell one key. */
function keyOf(provider: string, model: string): string {
  return `${provider}\u0000${model}`;
}

function sum(values: readonly number[]): number {
  return values.reduce((total, one) => total + one, 0);
}

/** The money for one set of turns: what was billed, or what the rate says when nothing was. */
function moneyOf(
  turns: readonly ChatTurnRecord[],
  price: ReturnType<ChatPriceOf>,
): { readonly usd: number | null; readonly estimated: boolean } {
  const billed = turns.filter((turn) => typeof turn.costUsd === 'number');
  if (billed.length > 0) {
    return { usd: round4(sum(billed.map((turn) => turn.costUsd as number))), estimated: false };
  }
  if (price === undefined || turns.length === 0) {
    return { usd: null, estimated: false };
  }

  return {
    usd: round4(
      (sum(turns.map((turn) => turn.tokensIn)) / 1_000_000) * price.inPerMillion
      + (sum(turns.map((turn) => turn.tokensOut)) / 1_000_000) * price.outPerMillion,
    ),
    estimated: true,
  };
}

/**
 * One row per vendor and model, busiest first.
 *
 * <p>A door that resolved NOTHING — a window with no vendor configured — keeps its empty strings and
 * lands in one row of its own rather than being dropped or attached to somebody else's. It is a real
 * state and the page names it; inventing a vendor for it would put invocations under a row that
 * never ran.</p>
 */
export function chatSpendRows(
  turns: readonly ChatTurnRecord[],
  doors: readonly ChatDoorRecord[],
  window: Window,
  now: Date,
  priceOf: ChatPriceOf = () => undefined,
): ChatSpendRow[] {
  const inWindow = within(turns, window, now);
  const doorsInWindow = within(doors, window, now);
  const byKey = new Map<string, { provider: string; model: string }>();
  // Only what is IN the window can start a row - a pair with nothing here is dropped by the filter
  // below whatever its history, so collecting keys from the whole ledger builds rows to throw away.
  for (const record of [...inWindow, ...doorsInWindow]) {
    byKey.set(keyOf(record.provider, record.model), { provider: record.provider, model: record.model });
  }

  return [...byKey.values()]
    .map(({ provider, model }) => {
      const mine = inWindow.filter((turn) => turn.provider === provider && turn.model === model);
      const ever = turns.filter((turn) => turn.provider === provider && turn.model === model);
      const myDoors = doorsInWindow.filter((one) => one.provider === provider && one.model === model);
      const price = priceOf(model, provider);
      const money = moneyOf(mine, price);
      const allTime = moneyOf(ever, price);

      return {
        provider,
        model,
        tokensIn: sum(mine.map((turn) => turn.tokensIn)),
        tokensOut: sum(mine.map((turn) => turn.tokensOut)),
        inPerMillion: price?.inPerMillion ?? null,
        outPerMillion: price?.outPerMillion ?? null,
        costUsd: money.estimated ? null : money.usd,
        estimatedUsd: money.estimated ? money.usd : null,
        // A turn nobody billed AND nobody can price. Not the same as an estimate, and not free.
        unpriced: price === undefined ? mine.filter((turn) => turn.costUsd === null).length : 0,
        allTimeUsd: allTime.usd,
        allTimeEstimated: allTime.estimated,
        turns: mine.length,
        asked: myDoors.filter((one) => asking(one.door)).length,
        opened: myDoors.length,
      };
    })
    // A row is drawn for what happened IN THE WINDOW, which is the same rule the reviewer cards
    // above it follow - a window with nothing in it says so rather than listing dashes. The all-time
    // column is CONTEXT for a pair that is on the page, not a reason to put one there: the first
    // version also kept any pair with all-time money, which made a row appear or not depending on
    // whether its model happened to have a rate. A pair that was only reached for and never answered
    // stays, because that is something to see.
    .filter((row) => row.turns > 0 || row.opened > 0)
    .sort((a, b) => b.tokensIn + b.tokensOut - (a.tokensIn + a.tokensOut));
}

/**
 * The section's own total.
 *
 * <p>Counted from the RECORDS, not by adding the rows up. A door whose pair never answered would
 * otherwise be dropped by the row filter and go missing from the count of how often the chat was
 * opened — the two numbers must agree with the ledger, not with each other.</p>
 */
export function chatSpendTotals(
  turns: readonly ChatTurnRecord[],
  doors: readonly ChatDoorRecord[],
  window: Window,
  now: Date,
  priceOf: ChatPriceOf = () => undefined,
): ChatSpendTotals {
  const inWindow = within(turns, window, now);
  const doorsInWindow = within(doors, window, now);
  const rows = chatSpendRows(turns, doors, window, now, priceOf);

  return {
    tokens: sum(inWindow.map((turn) => turn.tokensIn + turn.tokensOut)),
    // Kept apart all the way to the end: a total that mixes what a vendor billed with what we worked
    // out from a rate somebody typed is a number nobody can check.
    costUsd: rows.every((row) => row.costUsd === null)
      ? null
      : round4(sum(rows.map((row) => row.costUsd ?? 0))),
    estimatedUsd: rows.every((row) => row.estimatedUsd === null)
      ? null
      : round4(sum(rows.map((row) => row.estimatedUsd ?? 0))),
    unpriced: sum(rows.map((row) => row.unpriced)),
    asked: doorsInWindow.filter((one) => asking(one.door)).length,
    opened: doorsInWindow.length,
  };
}
