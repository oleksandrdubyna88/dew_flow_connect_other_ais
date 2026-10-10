/**
 * The Settings page's columns: what Models' cards sit in, and the repeated cards of Reviews (every sub-tab but Limits),
 * Security lane and Chat — two columns on a wide editor, one on a narrow one (operator, 2026-10-09).
 *
 * <p>ONE class and one rule in `catalogCss.ts`, so every place reflows at the same width as Models and a change to the
 * breakpoint is a change to all of them. A place that wraps something here also leaves the 760 px column the old
 * sections were written for, on a wide editor only (`.moved:has(...)` in the same sheet).</p>
 */

/** The class the grid is keyed on. */
export const CARD_COLUMNS = 'card-columns';

/**
 * The cards, laid out in the columns — '' when there are none, so an empty stage draws no empty grid.
 *
 * @param cards the cards, drawn and joined: each top-level element becomes one cell
 */
export function cardColumns(cards: string): string {
  return cards.trim().length === 0 ? '' : `<div class="${CARD_COLUMNS}">\n${cards}\n</div>`;
}
