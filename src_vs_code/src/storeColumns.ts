import { escapeHtml } from './escapeHtml';
import {
  RoundCountsAnswer, RoundPart, RoundStore, rowIn, STAGES, totalRounds, WINDOW_NAMES, WindowName, windowNamed,
} from './roundCounts';
import { asOfWords, storeLabel } from './storeWords';

/**
 * What the Rounds block draws for a vendor row over TWO stores or more (E6.S4 of todo/PLAN_the_round_counter_tail_E6.md): the
 * tooltip on a count cell that lists each store's part, and one sub-row per store.
 *
 * <p><b>Stores, never accounts.</b> A sub-row is labelled *store · <name>* — coai cannot tell whether two stores sign in to one
 * vendor account, so the row says where the rounds were held and never whose they are. Activity is summed in the vendor row;
 * a store sub-row carries only the rounds it held (and the reviewers per round those rounds launched), no money.</p>
 *
 * <p>A part is a store's rounds for one provider in one window, as the merged answer cut them at one `now`. A store with no part
 * for a row held none — a count of nought, which for a store quiet for a week is nought *as of* its last write, never "not
 * included".</p>
 */

/** The stores a merged answer counted, when there is more than one to show — none for a single-store answer. */
export function manyStores(answer: RoundCountsAnswer): readonly RoundStore[] {
  const stores = answer.stores ?? [];

  return stores.length > 1 ? stores : [];
}

function partOf(answer: RoundCountsAnswer, window: WindowName, provider: string, store: RoundStore): RoundPart | undefined {
  return rowIn(answer, window, provider)?.parts?.find((part) => part.store === store.id);
}

function partRounds(part: RoundPart | undefined): number {
  return part === undefined ? 0 : totalRounds(part);
}

/**
 * The tooltip of a count cell: each store's share, *Windows (this machine): 2 · WSL (Ubuntu): 1*. A store whose last write is
 * older than the window's start says *as of …* — its share could not know of a round after it, so the 0 is a statement about
 * its write and not about the window.
 */
export function countTitle(answer: RoundCountsAnswer, window: WindowName, provider: string, nowMs: number): string {
  const began = Date.parse(windowNamed(answer, window)?.fromUtc ?? '');

  return manyStores(answer)
    .map((store) => `${storeLabel(store)}: ${partRounds(partOf(answer, window, provider, store))}${beforeWindow(store, began, nowMs)}`)
    .join(' · ');
}

function beforeWindow(store: RoundStore, windowStartMs: number, nowMs: number): string {
  return !store.live && Date.parse(store.asOf) < windowStartMs ? ` (${asOfWords(store.asOf, nowMs)})` : '';
}

/** A count cell: the figure, and — over two stores or more — its parts as the tooltip. */
export function titledCount(answer: RoundCountsAnswer, window: WindowName, provider: string, figure: string, nowMs: number): string {
  const title = countTitle(answer, window, provider, nowMs);

  return title.length === 0 ? figure : `<span title="${escapeHtml(title)}">${figure}</span>`;
}

/**
 * One sub-row's cells per store, in the order of the table's columns: the three windows, the 7 d stages, the reviewers per round,
 * and blanks under every column that is summed in the vendor row above. Empty over a single store.
 */
export function storeCells(answer: RoundCountsAnswer, provider: string): readonly (readonly string[])[] {
  return manyStores(answer).map((store) => [
    `<span class="indent">store · ${escapeHtml(storeLabel(store))}</span>`,
    ...WINDOW_NAMES.map((window) => String(partRounds(partOf(answer, window, provider, store)))),
    stagesOf(partOf(answer, '7d', provider, store)), '', reviewersOf(partOf(answer, '7d', provider, store)), '', '', '', '', '',
  ]);
}

function stagesOf(part: RoundPart | undefined): string {
  return STAGES.map((stage) => `${stage} ${part === undefined ? 0 : part.rounds[stage]}`).join(' · ');
}

function reviewersOf(part: RoundPart | undefined): string {
  return part === undefined || partRounds(part) === 0 ? '—' : (part.reviewers / partRounds(part)).toFixed(1);
}
