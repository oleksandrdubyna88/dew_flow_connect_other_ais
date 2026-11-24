import { ROLE_TONE_CSS } from './roleTone';
import { disclosure } from './panelSurface';
import { jsonForScript } from './webviewHtml';

/**
 * The sidebar's MENU (v2 E4, 2026-11-23): the section headers a person comes to the sidebar for, drawn by the shell's own `sidebar` module before the old code
 * has loaded, and the one definition of them that the old panel draws from too.
 *
 * <p><b>Why it exists.</b> The owner's rule for the cold start is that it ends when the REAL menu is on screen and works — the headers, in their order and
 * colours, collapsed, clickable — not when a skeleton or an inert copy of an old page appears. So the first page the sidebar module writes is this one: no
 * data in it, every section a header that opens, and a "loading…" line inside each section until the old code takes the view over and paints the live page.</p>
 *
 * <p><b>One source for both pages.</b> The ids, the titles and the order are {@link MENU_SECTIONS}; the old panel's section list takes its titles from
 * {@link menuTitle}, and a test holds the two lists together. The rules that make a header look as it does are {@link MENU_BASE_CSS} and
 * {@link MENU_SECTION_CSS}, which the old page's stylesheet includes — not a copy of them.</p>
 *
 * <p>This file is bundled into `dist/modules/sidebar.js`, which runs BEFORE the old code: it imports only light files (`scripts/bundle-entries.json` bounds them).</p>
 */

/** The sections of the menu, in the order a person sees them. `Consultation cadence` is not here: it is removed in v2. */
export const MENU_SECTIONS = [
  { id: 'notifications', title: 'Notifications' },
  { id: 'rounds', title: 'Active gates' },
  { id: 'consultations', title: 'Active consultations' },
  { id: 'phrases', title: 'Phrases' },
  { id: 'bugz', title: 'Bugz' },
] as const;

export type MenuSectionId = (typeof MENU_SECTIONS)[number]['id'];

/** The ids of the menu's sections, as plain strings: what a message from a page is checked against. */
export const MENU_IDS: readonly string[] = MENU_SECTIONS.map((one) => one.id);

/** The title a section has on every page that draws it. */
export function menuTitle(id: MenuSectionId): string {
  return MENU_SECTIONS.find((one) => one.id === id)?.title ?? id;
}

/** The document's own rules: box model, text size, the body's padding. Written once and included by the old page's stylesheet. */
export const MENU_BASE_CSS = `
  /* Everything measures its own border and padding: without this a 100% field plus padding is
     wider than its parent, and the whole view gains a horizontal scrollbar. */
  *, *::before, *::after { box-sizing: border-box; }
  :root { color-scheme: light dark; }
  /* The ROOT carries the text size, and the small print below is measured from it in rem — 13 of them
     to the old pixel, so at the theme's default 13px every line is exactly the size it was. The Settings
     tab roots it in its size control instead, which is how the notes follow that control: rem is measured
     from the root alone, so no nested size compounds (research/PLAN_every_page_reads_alike.md, D5). */
  html { font-size: var(--vscode-font-size); }
  body {
    font-family: var(--vscode-font-family); font-size: var(--vscode-font-size);
    color: var(--vscode-foreground); background: transparent;
    /* Air down both sides: nothing should touch the edge of the view, and the right-hand gap is
       a shade wider so a field never sits under the scrollbar's track. */
    margin: 0; padding: 4px 14px 20px 12px; overflow-x: hidden;
  }`;

/** What a section header looks like, its tone per section, the chevron, and the quiet line a section says while it waits. */
export const MENU_SECTION_CSS = `
  .section { border-top: 1px solid var(--vscode-panel-border); padding: 0 0 8px; }
  .section > summary {
    /* .9 rather than .75: coloured text at .75 on a dark ground is muddy, and the colour is
       doing the separating now. */
    font-size: calc(11rem / 13); text-transform: uppercase; letter-spacing: .06em; opacity: .9;
    font-weight: 600; padding: 10px 0 6px; cursor: pointer; list-style: none;
    display: flex; align-items: center; gap: 6px; user-select: none;
  }
  .section > summary:hover { opacity: 1; }
  /* Each header that opens carries its own tone, from the same palette the role boxes use, so a
     column of identical grey words becomes something you can aim at. The chevron follows for
     free: it is drawn from currentColor.

     Colour is never the only signal — every heading is also its own word — and each tone is a
     --vscode-charts-* token with a hex fallback, so a theme that redefines the charts palette
     moves these with it. */
  /* Notifications take the reliability tone, because that is what every record in them is
     about: something refused, failed, stood down or repeated. Sharing the hue with the gate's
     own reliability role says the two are asking the same question of different subjects. */
  .sec-notifications > summary { color: var(--tone-sec); }
  .sec-rounds    > summary { color: var(--tone-plan); }
  /* The Consultant section's own hue: the settings and the consultations they start read as one thing. */
  .sec-consultations > summary { color: var(--tone-uxdx); }
  .sec-phrases   > summary { color: var(--tone-plan); }
  /* Bugz takes the security tone because that is what the corpus IS: the accepted findings it
     reads back are Reliability, Performance and Security ones, filtered to exactly those. Sharing
     the hue with the gate says the two are the same subject seen from opposite ends — one deciding
     what is a defect, the other keeping what was. */
  .sec-bugz      > summary { color: var(--tone-sec); }
  .section > summary::-webkit-details-marker { display: none; }
  /* A real chevron, drawn from two borders rather than borrowed from punctuation: it matches the
     Explorer's weight, scales with the text, and points the right way in both states with no
     script. (A '›' glyph rendered a third of this size — a disclosure arrow nobody can hit.) */
  .section > summary::before {
    content: ''; flex: 0 0 auto; width: 6px; height: 6px; margin: 0 4px 0 2px;
    border-right: 1.5px solid currentColor; border-bottom: 1.5px solid currentColor;
    transform: rotate(-45deg); transition: transform .12s ease;
  }
  .section[open] > summary::before { transform: rotate(45deg); margin-top: -3px; }
  .hint { opacity: .65; font-size: calc(11rem / 13); margin: 3px 0 0; line-height: 1.45; }`;

/**
 * The script every page that can be the menu ends with: it says `menuReady` — the moment the cold-start measurement ends — ONLY when this document shows
 * every header of {@link MENU_SECTIONS}, each with its words, none of them inside an `inert` or hidden element.
 *
 * <p>A page that is not the menu says nothing: a skeleton, a copy of an old page that is `inert` until it is replaced, the Settings page. A header
 * opens and closes by itself (it is a `details` element), so "a person can use it" is the absence of `inert` and `hidden`, which is what is read.</p>
 *
 * <p>It runs inside the page's script, after `const vscode = acquireVsCodeApi()`.</p>
 */
export function menuReadyScript(): string {
  return `
  // The cold-start measurement ends HERE (v2 E4, 2026-11-23): not when a page exists, but when the menu a person came for is on screen and works.
  (function () {
    const wanted = ${jsonForScript(MENU_IDS)};
    const usable = [];
    for (const el of document.querySelectorAll('.section')) {
      const summary = el.querySelector('summary');
      if (summary !== null && summary.textContent.trim() !== '' && el.closest('[inert]') === null && el.closest('[hidden]') === null) {
        usable.push(el.getAttribute('data-section'));
      }
    }
    if (wanted.every((id) => usable.includes(id))) {
      vscode.postMessage({ type: 'menuReady' });
    }
  })();`;
}

/** What a section says until the old code has painted the live page: only that section, only while it is open. */
export const LOADING_LINE = '<p class="hint" role="status">loading…</p>';

/**
 * The page the sidebar opens on: the menu, every section closed, each with its loading line, a fresh nonce.
 *
 * @param nonce made for THIS paint (`webviewNonce()`); never one that was used before
 */
export function menuPage(nonce: string): string {
  const sections = MENU_SECTIONS.map((one) => disclosure(one.id, one.title, false, LOADING_LINE)).join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy"
      content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>${MENU_BASE_CSS}${ROLE_TONE_CSS}${MENU_SECTION_CSS}</style>
</head>
<body>
${sections}
<script nonce="${nonce}">
  const vscode = acquireVsCodeApi();
  // The same message the old page posts when a section is opened or closed: the host keeps which sections are open.
  for (const el of document.querySelectorAll('.section')) {
    el.addEventListener('toggle', () => vscode.postMessage({ type: 'section', id: el.getAttribute('data-section'), open: el.open }));
  }
  vscode.postMessage({ type: 'ready' });
${menuReadyScript()}
</script>
</body>
</html>`;
}

/** The page shown when the old code could not be loaded: what happened, in words, and nothing the person can press. */
export function failedPage(message: string): string {
  const escaped = message.replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;').replace(/"/gu, '&quot;');

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline';">
<style>body { color: var(--vscode-foreground); font-family: var(--vscode-font-family); font-size: var(--vscode-font-size); padding: 8px 12px; }</style>
</head>
<body>
<p>ConnectOtherAIs could not start: ${escaped}</p>
<p>Reload the window to try again. If it happens again, the extension's log says why.</p>
</body>
</html>`;
}
