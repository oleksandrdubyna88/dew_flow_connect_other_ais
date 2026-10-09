import { catalogHtml, catalogKey } from '../catalogPage';
import { OLD_TAB_PLACES } from '../catalogPlaces';
import { panelHtml, type PanelState, staticKey } from '../panelView';
import { SURFACE_IDS, type SurfaceId } from '../panelSurface';

/**
 * Every page the panel draws, for the tests that make a claim about "every control on the panel".
 *
 * <p>Such a test used to render ONE page, and that is a test that goes vacuous rather than red the day
 * a section moves to another page: every assertion still holds, over fewer controls. So the pages are
 * derived from `SURFACE_IDS`, and {@link pageOf} switches over the surface exhaustively — a surface
 * added without a page here is a compile error, not a page nobody scans.</p>
 *
 * <p><b>The Settings surface is the Settings page</b> (todo/PLAN_one_model_catalog.md) — the only one since E5.1 step 5
 * removed the page it replaced, so every test that read a section through the old page reads it there. Its body
 * draws every place at once, each pane hidden until the script shows one, so the page opened on Models holds the
 * markup of every place.</p>
 */
export function pageOf(surface: SurfaceId, state: PanelState, nonce = 'n0nce', nowMs?: number): string {
  switch (surface) {
    case 'sidebar':
      return panelHtml(state, nonce, nowMs);
    case 'settings':
      return catalogHtml(state, nonce, 'models');
  }
}

/**
 * Every page, joined — what a test reads when it makes a claim about the panel's markup as a whole.
 *
 * <p>A strict SUPERSET of what `panelHtml` returned while every section was in the sidebar, which is why
 * the tests that used it were moved onto this when ten sections moved to the Settings tab: a positive
 * assertion still finds what it looks for, and a negative one still scans everything that is drawn, so
 * neither can go vacuous for the section that left (`research/PLAN_settings_page.md`, M3).</p>
 */
export function everyPageHtml(state: PanelState, nonce = 'n0nce', nowMs?: number): string {
  return SURFACE_IDS.map((surface) => pageOf(surface, state, nonce, nowMs)).join('\n');
}

/** One surface's paint key, switched exhaustively like {@link pageOf}. */
export function keyOf(surface: SurfaceId, state: PanelState): string {
  switch (surface) {
    case 'sidebar':
      return staticKey(state);
    case 'settings':
      return catalogKey(state);
  }
}

/**
 * Every surface's paint key at once: it moves when ANY page would draw something different, and stays
 * put only when none would — which is the claim a key test makes now that a probe's answer can be drawn
 * on the Settings tab rather than the sidebar.
 */
export function paintKeys(state: PanelState): string {
  return SURFACE_IDS.map((surface) => `${surface}:${keyOf(surface, state)}`).join(' | ');
}

export function everyPanelPage(state: PanelState): readonly { readonly surface: SurfaceId; readonly html: string }[] {
  return SURFACE_IDS.map((surface) => ({ surface, html: pageOf(surface, state) }));
}

/**
 * The attribute that marks a section on the page that draws it: a sidebar section by its id, an old Settings tab by
 * the place of the new page that holds it now (`OLD_TAB_PLACES`, E5.1 step 3).
 */
export function markOf(id: string): string {
  const place = OLD_TAB_PLACES[id];

  return place === undefined ? `data-section="${id}"` : `data-pane="${place}"`;
}

/**
 * One section's markup, from whichever page draws it — and a FAILURE when no page does.
 *
 * <p>A test that slices a section out of the page with `indexOf` gets `-1` for a section that moved,
 * and `slice(-1, n)` is an empty string every negative assertion passes against. That is how a guard
 * stops guarding without going red (plan F7 is the same trap in a source scan). This throws instead.</p>
 */
export function sectionHtml(state: PanelState, id: string): string {
  for (const { html } of everyPanelPage(state)) {
    const found = elementWith(html, markOf(id));
    if (found !== undefined) {
      return found;
    }
  }
  throw new Error(`no page draws a section "${id}" — a test reading it would be checking nothing`);
}

/**
 * One place of the new Settings page out of html a test already holds — its pane, nesting counted — and a FAILURE when
 * the html draws no such place, for the reason {@link sectionHtml} gives.
 *
 * @param place a place of `CATALOG_TABS` (`tab` or `tab/sub`)
 */
export function paneIn(html: string, place: string): string {
  const found = elementWith(html, `data-pane="${place}"`);
  if (found === undefined) {
    throw new Error(`the page draws no place "${place}" — a test reading it would be checking nothing`);
  }

  return found;
}

/**
 * Where the first START TAG carrying `attribute` opens — -1 for none. The attribute's text alone is not enough: the new
 * page's sheet and script name places too (`[data-pane="setup/mcp"]`), and an occurrence there would hand back the whole
 * `<style>` element as the section.
 */
function startTagWith(html: string, attribute: string): number {
  for (let at = html.indexOf(attribute); at >= 0; at = html.indexOf(attribute, at + 1)) {
    const start = html.lastIndexOf('<', at);
    if (/^<[a-z]+\s[^<>]*$/.test(html.slice(start, at))) {
      return start;
    }
  }

  return -1;
}

/** The element whose start tag carries `attribute`, through its matching close tag, nesting counted. */
function elementWith(html: string, attribute: string): string | undefined {
  const start = startTagWith(html, attribute);
  if (start < 0) {
    return undefined;
  }
  const tag = /^<([a-z]+)/.exec(html.slice(start))?.[1];
  if (tag === undefined) {
    return undefined;
  }
  const pattern = new RegExp(String.raw`<${tag}\b|</${tag}>`, 'g');
  pattern.lastIndex = start;
  let depth = 0;
  for (let match = pattern.exec(html); match !== null; match = pattern.exec(html)) {
    depth += match[0].startsWith('</') ? -1 : 1;
    if (depth === 0) {
      return html.slice(start, match.index + match[0].length);
    }
  }

  return undefined;
}
