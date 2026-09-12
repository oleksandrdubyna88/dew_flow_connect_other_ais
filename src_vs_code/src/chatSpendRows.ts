import { ChatDoorRecord, asking } from './chatDoors';
import { ChatTurnRecord } from './chatUsage';
import { Window, windowStart } from './usage';

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
 * <h2>ONE PASS, and the reason it had to be</h2>
 *
 * <p>The first version filtered the whole ledger once per row and then did the whole thing again for
 * the totals. Four reviewers of the code round arrived at the same arithmetic independently: a year
 * of history against fifty vendor-and-model pairs is fifty full sweeps of a hundred thousand records,
 * on the extension host's own thread, on every repaint and every window button. It is one pass over
 * each ledger into buckets now, and the totals come out of that same pass.</p>
 *
 * <h2>What a door's count is a count OF</h2>
 *
 * <p>A door record carries the provider and model that were in force when the command was used,
 * which is a different claim from the model that answered: the door may never be answered at all,
 * and a conversation can switch afterwards. So a row's `asked` and `opened` are "how often this pair
 * was reached for", and the section TOTAL is counted before the rows are filtered, so it agrees with
 * the ledger whatever the row rule does with a pair.</p>
 */

/** Four decimals, the precision this product counts money in — cents would read a real cost as free. */
function round4(usd: number): number {
  return Math.round(usd * 10_000) / 10_000;
}

/**
 * What a rate lookup answers: the price for one model, per million tokens.
 *
 * <p>By MODEL alone, because that is what the price map this is handed actually holds — a
 * `Record<string, ModelPrice>` keyed on the model id. The first version took a provider as well and
 * the only caller dropped it, which is a type promising something the data cannot keep. (gemini, the
 * code round.)</p>
 */
/**
 * WHOSE row a recorded id belongs to — the vendor, not the preset.
 *
 * <p>What the ledger holds is the id of the model preset in force, because a preset is what a chat
 * is switched between. A preset id is a generated string nobody recognises (`preset-mtwxbymp-4`),
 * and the section above this one is per VENDOR — so a page showing both named the same three vendors
 * two different ways. A preset knows its runtime; this resolves one to the other at READ time, which
 * fixes every line already on disk rather than only the next ones. A preset since deleted keeps its
 * recorded id, because the honest answer is then "this is what was written down".</p>
 */
export type ChatVendorOf = (provider: string) => string;

export type ChatPriceOf = (model: string) =>
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
  /**
   * What the turns nobody billed would cost at the rate in force, in this window.
   *
   * <p>Kept BESIDE the bill rather than instead of it. The first version returned one or the other,
   * so a model that billed one turn and said nothing about the next recorded the second as free —
   * two reviewers found the same hole from different directions. A row can legitimately be part bill
   * and part estimate, and the renderer already writes that as `$0.50 + ~$11`.</p>
   */
  readonly estimatedUsd: number | null;
  /** Turns with neither a bill nor a rate. Counted, because a total that hides them reads complete. */
  readonly unpriced: number;
  /** What was BILLED over the whole ledger, outside the window: asked for in those words. */
  readonly allTimeUsd: number | null;
  /** And the same for what was never billed — two numbers, for the reason `estimatedUsd` is two. */
  readonly allTimeEstimatedUsd: number | null;
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

/** Everything the section needs, out of one pass over each ledger. */
export interface ChatSpend {
  readonly rows: readonly ChatSpendRow[];
  readonly totals: ChatSpendTotals;
}

/**
 * The key a row is grouped on.
 *
 * <p>A NUL between the halves, not a space or a dash: a model id can contain either, and two pairs
 * that spell one key would silently add one vendor's tokens to another's.</p>
 */
function keyOf(provider: string, model: string): string {
  return `${provider}\u0000${model}`;
}

/** What one vendor-and-model pair has done, filled in as each ledger is read once. */
interface Bucket {
  provider: string;
  model: string;
  tokensIn: number;
  tokensOut: number;
  billed: number;
  billedCount: number;
  unbilledIn: number;
  unbilledOut: number;
  unbilledCount: number;
  everBilled: number;
  everBilledCount: number;
  everUnbilledIn: number;
  everUnbilledOut: number;
  everUnbilledCount: number;
  turns: number;
  asked: number;
  opened: number;
}

function emptyBucket(provider: string, model: string): Bucket {
  return {
    provider,
    model,
    tokensIn: 0,
    tokensOut: 0,
    billed: 0,
    billedCount: 0,
    unbilledIn: 0,
    unbilledOut: 0,
    unbilledCount: 0,
    everBilled: 0,
    everBilledCount: 0,
    everUnbilledIn: 0,
    everUnbilledOut: 0,
    everUnbilledCount: 0,
    turns: 0,
    asked: 0,
    opened: 0,
  };
}

/**
 * What a vendor really charged for this turn, or nothing at all.
 *
 * <p>A negative number is not a credit and NaN is not a price — both are "nobody said", which is the
 * same rule `spendSoFar` applies to the running total in a chat tab.</p>
 */
function billedFor(turn: ChatTurnRecord): number | undefined {
  const cost = turn.costUsd;

  return typeof cost === 'number' && Number.isFinite(cost) && cost >= 0 ? cost : undefined;
}

/** What a set of unbilled tokens would cost at a rate, or null when there is no rate to ask. */
function estimate(
  tokensIn: number,
  tokensOut: number,
  count: number,
  price: ReturnType<ChatPriceOf>,
): number | null {
  if (price === undefined || count === 0) {
    return null;
  }

  return round4((tokensIn / 1_000_000) * price.inPerMillion + (tokensOut / 1_000_000) * price.outPerMillion);
}

/**
 * The rows and the section's own total, from one pass over the turns and one over the doors.
 *
 * <p>A row is drawn for what happened IN THE WINDOW, which is the rule the reviewer cards above it
 * follow — a window with nothing in it says so rather than listing dashes. The all-time column is
 * CONTEXT for a pair that is on the page, not a reason to put one there.</p>
 *
 * <p>A door that resolved NOTHING — a window with no vendor configured — keeps its empty strings and
 * lands in one row of its own rather than being dropped or attached to somebody else's. It is a real
 * state and the page names it; inventing a vendor for it would put invocations under a row that
 * never ran.</p>
 *
 * <p>A record whose instant cannot be read belongs to no window and is counted in none — the same
 * thing `within` does for the server's ledger, rather than letting a `NaN` decide a comparison.</p>
 */
export function chatSpend(
  turns: readonly ChatTurnRecord[],
  doors: readonly ChatDoorRecord[],
  window: Window,
  now: Date,
  priceOf: ChatPriceOf = () => undefined,
  vendorOf: ChatVendorOf = (provider) => provider,
): ChatSpend {
  const from = windowStart(window, now);
  const buckets = new Map<string, Bucket>();
  // WHAT WAS WRITTEN DOWN wins over what the preset list says today. A preset is a row somebody
  // edits, so resolving an old line through the list as it is now would move a year of history to
  // another vendor in a ledger that is supposed to be append-only. The resolver is the answer only
  // for lines written before the field existed. (CodeRabbit, PR #209.)
  const bucket = (recorded: string, model: string, written?: string): Bucket => {
    const provider = written !== undefined && written.length > 0 ? written : vendorOf(recorded);
    const key = keyOf(provider, model);
    const found = buckets.get(key) ?? emptyBucket(provider, model);
    buckets.set(key, found);

    return found;
  };
  const inWindow = (utc: string): boolean => {
    const at = Date.parse(utc);

    return Number.isFinite(at) && at >= from;
  };

  for (const turn of turns) {
    const one = bucket(turn.provider, turn.model, turn.vendor);
    const billed = billedFor(turn);
    // ALL TIME first, and whatever the window says: that column is the one that ignores the buttons.
    if (billed === undefined) {
      one.everUnbilledIn += turn.tokensIn;
      one.everUnbilledOut += turn.tokensOut;
      one.everUnbilledCount += 1;
    } else {
      one.everBilled += billed;
      one.everBilledCount += 1;
    }
    if (!inWindow(turn.utc)) {
      continue;
    }
    one.turns += 1;
    one.tokensIn += turn.tokensIn;
    one.tokensOut += turn.tokensOut;
    if (billed === undefined) {
      one.unbilledIn += turn.tokensIn;
      one.unbilledOut += turn.tokensOut;
      one.unbilledCount += 1;
    } else {
      one.billed += billed;
      one.billedCount += 1;
    }
  }

  for (const door of doors) {
    if (!inWindow(door.utc)) {
      continue;
    }
    const one = bucket(door.provider, door.model, door.vendor);
    one.opened += 1;
    one.asked += asking(door.door) ? 1 : 0;
  }

  const all: ChatSpendRow[] = [...buckets.values()].map((one) => {
    const price = priceOf(one.model);

    return {
      provider: one.provider,
      model: one.model,
      tokensIn: one.tokensIn,
      tokensOut: one.tokensOut,
      inPerMillion: price?.inPerMillion ?? null,
      outPerMillion: price?.outPerMillion ?? null,
      costUsd: one.billedCount === 0 ? null : round4(one.billed),
      estimatedUsd: estimate(one.unbilledIn, one.unbilledOut, one.unbilledCount, price),
      // A turn nobody billed AND nobody can price. Not the same as an estimate, and not free.
      unpriced: price === undefined ? one.unbilledCount : 0,
      allTimeUsd: one.everBilledCount === 0 ? null : round4(one.everBilled),
      allTimeEstimatedUsd: estimate(one.everUnbilledIn, one.everUnbilledOut, one.everUnbilledCount, price),
      turns: one.turns,
      asked: one.asked,
      opened: one.opened,
    };
  });
  // Summed over EVERY bucket, before the row filter, so the section total goes on agreeing with the
  // ledger whatever the row rule does with a pair.
  const sum = (read: (row: ChatSpendRow) => number | null): number | null =>
    all.every((row) => read(row) === null)
      ? null
      : round4(all.reduce((total, row) => total + (read(row) ?? 0), 0));

  return {
    rows: all
      .filter((row) => row.turns > 0 || row.opened > 0)
      .sort((a, b) => b.tokensIn + b.tokensOut - (a.tokensIn + a.tokensOut)),
    totals: {
      tokens: all.reduce((total, row) => total + row.tokensIn + row.tokensOut, 0),
      // Kept apart all the way to the end: a total that mixes what a vendor billed with what we
      // worked out from a rate somebody typed is a number nobody can check.
      costUsd: sum((row) => row.costUsd),
      estimatedUsd: sum((row) => row.estimatedUsd),
      unpriced: all.reduce((total, row) => total + row.unpriced, 0),
      asked: all.reduce((total, row) => total + row.asked, 0),
      opened: all.reduce((total, row) => total + row.opened, 0),
    },
  };
}
