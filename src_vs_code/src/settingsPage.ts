import { tabCss } from './tabStrip';
import { TEXT_CONTROLS_CSS, textControlsStyle } from './textControls';
import { scalePx } from './zoomControl';
import { SECURITY_LANE_CSS } from './securityLaneStyle';

/**
 * What the Settings tab adds to the panel's shared document: its strip's look and its tab switching.
 *
 * <p>Everything else on the page — every control, every write, the caret put back after a repaint — is
 * the panel's own script, shared rather than copied (`pageDocument` in `panelView.ts`), because the tabs
 * hold the very sections the sidebar used to (`research/PLAN_settings_page.md`).</p>
 */

/**
 * The strip, wrapping, and the panes at a width a line can be read at.
 *
 * <p>The panel's stylesheet was written for a narrow sidebar: every `button` is full width with a top
 * margin, which would turn a strip of ten tabs into ten stacked bars. So the tabs take their own width
 * back here, and `[hidden]` is made to win — a pane given a `display` of its own by any rule would
 * otherwise stay visible under the attribute that is meant to hide it.</p>
 */
export const SETTINGS_CSS = `
${tabCss('0 0 12px')}
${SECURITY_LANE_CSS}
  body { padding: 12px 20px 24px; }
  .settings .tabs { flex-wrap: wrap; }
  .settings .tabs .tab { width: auto; margin: 0; flex: 0 0 auto; }
  .settings .pane { max-width: 760px; padding: 4px 2px 16px; }
  .settings [hidden] { display: none !important; }
  .settingsHead { display: flex; align-items: baseline; gap: 12px; margin: 0 0 8px; }
  .settingsHead h1 { font-size: 1.2em; margin: 0; }
  /* The shared sheet draws every button full width with a top margin — right for the sidebar's actions,
     wrong for four small buttons in a row. */
  .zoomCtl button, .toneCtl button { width: auto; margin: 0; }
  /* The MCP server tab, one and a half times the size: it is the tab people read rather than set. Twice
     the size read far larger than the tabs beside it, so the operator asked for the middle (2026-09-29).
     zoom, not font-size: 1.5em — every note in it is sized in rem, which a parent's font-size does not
     reach, so an em size would have grown only the unsized text and left the small print as it was. */
  .settings .pane.sec-server { zoom: 1.5; }
${TEXT_CONTROLS_CSS}
`;

/**
 * The Settings tab's size and tone, for the page's stylesheet — the ROOT as well as the body, because the
 * shared sheet sizes its small print in `rem`, which is measured from the root alone
 * (`research/PLAN_every_page_reads_alike.md`, D5). In the stylesheet rather than the markup, so neither value
 * is in the paint key and a press never reloads the tab.
 */
export function settingsTextCss(size: number, tone: number): string {
  return `
  html { font-size: ${scalePx(size)}px; }
  body { ${textControlsStyle(size, tone)} }
`;
}

/** What the tab shows before its first paint: the state is still being gathered, and it says so. */
export const SETTINGS_LOADING = `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
<style>body { font-family: var(--vscode-font-family); color: var(--vscode-descriptionForeground); padding: 16px 20px; }</style>
</head><body><p>Reading the settings…</p></body></html>`;
