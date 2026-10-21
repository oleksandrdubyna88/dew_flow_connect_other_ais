import type { RoundCountsAnswer, RoundCountsRead, RoundStore } from './roundCounts';
import { asOfWords, conflictWords, notIncludedWords, storeLabel } from './storeWords';

/**
 * The sentence under the Rounds heading: which store was counted, which were counted together, and which were not (E6.S4 of
 * todo/PLAN_the_round_counter_tail_E6.md). Pure.
 *
 * <p>Three shapes. An answer that names no stores (an older coai-mcp, the plain `--round-counts`) keeps today's sentence — this
 * side's store only, and any other installation's store known to exist as *not included*. A merged answer of two stores or more
 * is *Counted from two stores — Windows (this machine) and WSL (Ubuntu, as of 3 min ago)*, each named with its age, a quiet one
 * as 0 rounds as of its last write. A merged answer of this side alone is today's sentence. Under either, each store that could not be
 * counted is *not included* with its reason (never a 0), and the rounds two stores hold with different contents are said, not
 * reconciled.</p>
 */

/** The store the counts came from, and the other installations' stores that are known to exist and are not counted. */
export interface StoreFacts {
  /** This side as a person names it — `WSL: Ubuntu`, or empty on the machine's own side. */
  readonly side: string;
  readonly directory: string;
  readonly others: readonly string[];
  /** Other installations' directories whose probe did not answer in time (a share that is down) — said as such, never counted (E6.S4). */
  readonly notAnswering?: readonly string[];
}

/** Which store was counted, and which known stores were not — today's sentence, for an answer that names no stores. */
export function storeSentence(store: StoreFacts): string {
  const side = store.side.length > 0 ? store.side : 'this machine';
  const others = store.others.map((one) => ` ${one} holds another installation's store; its rounds are not included.`).join('');
  const dead = (store.notAnswering ?? []).map((one) => ` ${one} is not answering; its rounds are not included.`).join('');

  return `Counted from this side's store only — ${side}, ${store.directory}.${others}${dead}`;
}

/**
 * The sentence for a read: today's for an answer without stores, otherwise the stores counted and the ones that were not.
 *
 * @param nowMs the instant the counts are as of — the answer's own clock, so *as of 3 min ago* is against the clock that counted.
 */
export function roundsSentence(facts: StoreFacts, read: RoundCountsRead, nowMs: number): string {
  if (read.state !== 'counted' || !namesStores(read.answer)) {
    return storeSentence(facts);
  }
  const stores = read.answer.stores ?? [];
  const head = stores.length > 1 ? togetherSentence(stores, nowMs) : storeSentence({ ...facts, others: [], notAnswering: [] });
  const notIncluded = (read.answer.storesNotIncluded ?? []).map(notIncludedWords);
  const conflicts = stores.filter((one) => one.conflicts > 0).map((one) => conflictWords(one.conflicts, storeLabel(one)));

  return [head, ...notIncluded, ...conflicts].join(' ');
}

/** An answer that says anything about stores — those it counted or those it could not. */
function namesStores(answer: RoundCountsAnswer): boolean {
  return answer.stores !== undefined || answer.storesNotIncluded !== undefined;
}

const NUMBER_WORDS = ['no', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten'] as const;

function togetherSentence(stores: readonly RoundStore[], nowMs: number): string {
  const count = NUMBER_WORDS[stores.length] ?? String(stores.length);

  return `Counted from ${count} stores — ${listed(stores.map((one) => labelAt(one, nowMs)))}.`;
}

/** One store as the sentence names it: this side plain, another with its age, and one quiet for a week as 0 rounds as of its write. */
function labelAt(store: RoundStore, nowMs: number): string {
  if (store.live) {
    return storeLabel(store);
  }
  const as = asOfWords(store.asOf, nowMs);

  return storeLabel(store, store.quiet ? `0 rounds ${as}` : as);
}

/** `A`, `A and B`, `A, B and C`. */
function listed(names: readonly string[]): string {
  return names.length < 2 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)!}`;
}
