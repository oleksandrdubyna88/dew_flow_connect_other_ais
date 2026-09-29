import { tabCss } from './tabStrip';
import { TEXT_CONTROLS_CSS, textControlsHtml, textControlsScript, textControlsStyle } from './textControls';
import { scalePx } from './zoomControl';
import { tabKeysScript } from './tabKeys';
import { jsonForScript } from './webviewHtml';

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
  /* The MCP server tab, twice the size: there is room for it here, and it is the tab people read rather
     than set. zoom, not font-size: 2em — every note in it is sized in rem, which a parent's font-size does
     not reach, so 2em would have doubled only the unsized text and left the small print as it was. */
  .settings .pane.sec-server { zoom: 2; }
${TEXT_CONTROLS_CSS}
`;

/**
 * The Settings tab's size and tone, for the page's stylesheet — the ROOT as well as the body, because the
 * shared sheet sizes its small print in `rem`, which is measured from the root alone
 * (`todo/PLAN_every_page_reads_alike.md`, D5). In the stylesheet rather than the markup, so neither value
 * is in the paint key and a press never reloads the tab.
 */
export function settingsTextCss(size: number, tone: number): string {
  return `
  html { font-size: ${scalePx(size)}px; }
  body { ${textControlsStyle(size, tone)} }
`;
}

/** The header above the tab strip: the page's name and its two text controls. */
export function settingsHead(size: number, tone: number): string {
  return `<header class="settingsHead"><h1>Settings</h1>${textControlsHtml(size, tone)}</header>`;
}

/**
 * Opens the tab the host holds, switches on a press or an arrow key, and says which one to the host.
 *
 * <p>The held tab arrives as a literal, beside the caret's, rather than drawn into the markup (D6): the
 * markup is the paint key, and a tab drawn into it would reload the page after every press. A `showTab`
 * from the host selects without posting back — it is the host telling the page what it already holds,
 * after a repaint raced a press. An id the page has no tab for opens the first tab, so a stale or
 * mistyped id can never leave a page with nothing showing.</p>
 */
export function settingsScript(heldTab: string): string {
  return `
  const shownTab = ${jsonForScript(heldTab)};
  function showSettingsTab(which, tell) {
    const tabs = Array.prototype.slice.call(document.querySelectorAll('[data-tab]'));
    const known = tabs.some((tab) => tab.dataset.tab === which);
    const chosen = known ? which : (tabs.length > 0 ? tabs[0].dataset.tab : '');
    for (const tab of tabs) {
      const on = tab.dataset.tab === chosen;
      tab.className = on ? 'tab on' : 'tab';
      tab.setAttribute('aria-selected', on ? 'true' : 'false');
      tab.setAttribute('tabindex', on ? '0' : '-1');
    }
    for (const pane of document.querySelectorAll('[data-pane]')) {
      pane.hidden = pane.dataset.pane !== chosen;
    }
    if (tell) { vscode.postMessage({ type: 'tab', id: chosen }); }
  }
  document.addEventListener('click', (event) => {
    const pressed = event.target;
    if (!pressed || typeof pressed.closest !== 'function') { return; }
    const tab = pressed.closest('[data-tab]');
    if (tab) { showSettingsTab(tab.dataset.tab, true); }
  });
  window.addEventListener('message', (event) => {
    if (event.data?.type === 'showTab') { showSettingsTab(String(event.data.id ?? ''), false); }
  });
  showSettingsTab(shownTab, false);
${tabKeysScript()}
${textControlsScript()}
  // The root too: the small print is in rem, and rem follows the root, not the body.
  window.addEventListener('message', (event) => {
    if (event.data?.type === 'uiScale') { document.documentElement.style.fontSize = event.data.px + 'px'; }
  });`;
}

/**
 * Which tab to hold after a request — the page pressed one, or the command was run with an argument.
 *
 * <p>A known id is taken; ANYTHING else leaves the held tab as it was. The command is reachable from the
 * view's title bar, the palette and `executeCommand`, so its argument can be nothing, a URI, an object or
 * a stale id from an older build — none of which may move the page, or blank it.</p>
 */
export function nextSettingsTab(held: string, requested: unknown, ids: readonly string[]): string {
  return typeof requested === 'string' && ids.includes(requested) ? requested : held;
}

/** What the tab shows before its first paint: the state is still being gathered, and it says so. */
export const SETTINGS_LOADING = `<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
<style>body { font-family: var(--vscode-font-family); color: var(--vscode-descriptionForeground); padding: 16px 20px; }</style>
</head><body><p>Reading the settings…</p></body></html>`;
