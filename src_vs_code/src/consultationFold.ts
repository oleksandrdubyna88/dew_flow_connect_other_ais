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

/**
 * Every line terminator a vendor's text may carry — `\r` alone and the Unicode separators too, or a
 * field broken by one of those would show whole. (codex, the code round.)
 */
const LINE_BREAK = /\r\n|[\r\n\u2028\u2029]/;

/**
 * A field folds when it is longer than the cap or has a line break: a break hides what follows it. A
 * break with nothing after it hides nothing, so the end is trimmed first. (Our own code reviewer.)
 */
function folds(text: string): boolean {
  const shown = text.trimEnd();

  return shown.length > FOLD_AFTER || LINE_BREAK.test(shown);
}

/**
 * The first line with words in it, never longer than the cap, and `…` — something is always hidden
 * behind a fold. A field that opens on a blank line used to show `…` alone. (Our own code reviewer.)
 */
function preview(text: string): string {
  const first = text.split(LINE_BREAK).find((line) => line.trim().length > 0) ?? '';

  return `${first.slice(0, FOLD_AFTER)}…`;
}
