/**
 * Types the page and the host both need, owned by neither.
 *
 * <p><b>Why this module exists at all: one import of one interface was a whole import cycle.</b>
 * `chatModels.ts:1` took `ChatModelChoice` from `chatPage.ts`, and `chatPage.ts:6` takes
 * `ChatProvider` back — so `chatModels ↔ chatPage` sat in the frozen list that
 * `src/test/importCycles.test.mjs` keeps. That one import of a four-field interface was the ENTIRE
 * return edge: nothing else in `chatModels` reaches the page. A cycle bundles perfectly well and
 * fails at runtime with `Cannot access 'X' before initialization`, which is why that ratchet exists
 * and why it may only fall.</p>
 *
 * <p>The gate over the split accepted this as <i>"couples config to the page — true and
 * pre-existing"</i> and filed it as tidiness. It is not tidiness; it is a ratchet entry, and moving
 * the interface is what deletes it.</p>
 *
 * <p><b>What is NOT here, and the measurement behind that.</b> The plan also called for `ChatMessage`
 * to move, on the grounds that `chatThread` would otherwise still import the page for one type. That
 * is true, and it is also a bigger change than the plan supposed: `ChatMessage` has FIVE importers
 * (`chatFresh`, `chatMessageShape`, `chatStore`, `chatTabs`, `chatThread`), not the one its table
 * listed — and moving it breaks no cycle, because none of those five is imported back by the page.
 * It buys tidiness, not a ratchet drop, so it is left as its own change rather than folded into one
 * that has a test to prove it.</p>
 */

/** A model the picker may offer. `remote` models say what they cannot do. */
export interface ChatModelChoice {
  readonly id: string;
  readonly label: string;
  readonly caption: string;
}
