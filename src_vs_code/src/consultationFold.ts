import { escapeHtml } from './webviewHtml';

/**
 * A consultation's long fields, folded — the Consultations tab (operator, 2026-09-25: "список
 * консультантов по умолчанию должен быть свернут. а то сильно много листать").
 *
 * <p>One consultation's problem filled the whole screen on its own, so the table was a scroll. A field
 * is shown as the page already shows a round's orders: a native `<details>`, closed by default, opened
 * by a click with no script at all — its first line as the summary, the whole text inside. The page's
 * script keeps an opened one open when a live push replaces the table (`roundsLog.ts`, the
 * `consultations` message).</p>
 */

/** Longer than this, or holding a line break, and a consultation field is folded. */
export const FOLD_AFTER = 160;

/**
 * A consultation's problem or advice, as its table cell shows it — escaped on every road.
 *
 * @param text the field, whole
 * @param key what names this fold across a re-render — `<consultation id>:problem` or `:advice`
 */
export function foldedCell(text: string, key: string): string {
  if (!folds(text)) {
    return escapeHtml(text);
  }

  // When the fold is open the preview is hidden by the stylesheet and "Collapse" shows instead, so the
  // first line is not read — or copied — twice. (gemini, the plan round.)
  return `<details class="fold" data-fold="${escapeHtml(key)}"><summary>`
    + `<span class="preview">${escapeHtml(preview(text))}</span><span class="less">Collapse</span></summary>`
    + `<div class="whole">${escapeHtml(text)}</div></details>`;
}

/** A field folds when it is longer than the cap or has a line break: a break hides what follows it. */
function folds(text: string): boolean {
  return text.length > FOLD_AFTER || text.includes('\n');
}

/** The first line, never longer than the cap, and `…` — something is always hidden behind a fold. */
function preview(text: string): string {
  return `${(text.split('\n', 1)[0] ?? '').slice(0, FOLD_AFTER)}…`;
}
