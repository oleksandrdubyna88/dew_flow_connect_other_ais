import { TEXT_CONTROLS_CSS, textControlsStyle } from './textControls';

/**
 * The look of this extension's form pages — a centred column of cards with themed fields — written ONCE.
 *
 * <p>It was Chat presets' own stylesheet, and Phrases carried a near copy of it. The operator asked on
 * 2026-09-29 for Review roles to sit in the same column and for Gate commands to look the same, and a third
 * and fourth copy is the drift `reuse-first.md` names: Gate commands was already the page that had drifted,
 * with the browser's white fields and no column at all (`todo/PLAN_every_page_reads_alike.md`, S3). Each
 * piece is named so a page takes what it needs and keeps its own rules after them.</p>
 */

/** The column: 900px, centred, on the editor's background, in the page's text size and tone. */
export function formBodyCss(size: number, tone: number): string {
  return `  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); padding: 16px 24px; max-width: 900px; margin: 0 auto; ${textControlsStyle(size, tone)} }`;
}

/** The column with its header row — the page's name beside its text controls — and its headings. */
export function formFrameCss(size: number, tone: number): string {
  return `${formBodyCss(size, tone)}
  header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 8px; }
  h1 { font-size: 1.2em; margin: 0; }
  h2 { font-size: 1em; margin: 24px 0 4px; }
  .lead { opacity: .8; margin: 0 0 12px; }`;
}

/** A card: one thing a person edits, framed, with a coloured left edge. */
export function formCardCss(selector: string): string {
  return `  ${selector} { border: 1px solid var(--vscode-panel-border); border-left-width: 3px; border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }`;
}

/** The row at the top of a card: its name, and its switches beside it. */
export const FORM_HEAD_CSS = `  .head { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; flex-wrap: wrap; }
  .head input[type="text"] { flex: 1 1 12rem; min-width: 0; }`;

/** The fields and the buttons, in the theme's colours rather than the browser's white boxes. */
export const FORM_FIELDS_CSS = `  input, select, textarea { font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 3px; padding: 4px 6px; }
  /* The box a person READS a text in. It grows with its content where the engine can do it; the rows
     attribute is the floor for every engine that cannot. */
  textarea { width: 100%; box-sizing: border-box; field-sizing: content; max-height: 60vh; }
  button { font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 3px; padding: 4px 12px; cursor: pointer; }
  button.remove { color: var(--vscode-foreground); background: none; border: 1px solid var(--vscode-panel-border); }
${TEXT_CONTROLS_CSS}`;
