import { panelHtml, type PanelState, settingsHtml, settingsKey, staticKey } from '../panelView';
import { SURFACE_IDS, type SurfaceId } from '../panelSurface';

/**
 * Every page the panel draws, for the tests that make a claim about "every control on the panel".
 *
 * <p>Such a test used to render ONE page, and that is a test that goes vacuous rather than red the day
 * a section moves to another page: every assertion still holds, over fewer controls. So the pages are
 * derived from `SURFACE_IDS`, and {@link pageOf} switches over the surface exhaustively — a surface
 * added without a page here is a compile error, not a page nobody scans.</p>
 */
export function pageOf(surface: SurfaceId, state: PanelState, nonce = 'n0nce', nowMs?: number): string {
  switch (surface) {
    case 'sidebar':
      return panelHtml(state, nonce, nowMs);
    case 'settings':
      return settingsHtml(state, nonce, '');
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
      return settingsKey(state);
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
 * One section's markup, from whichever page draws it — and a FAILURE when no page does.
 *
 * <p>A test that slices a section out of the page with `indexOf` gets `-1` for a section that moved,
 * and `slice(-1, n)` is an empty string every negative assertion passes against. That is how a guard
 * stops guarding without going red (plan F7 is the same trap in a source scan). This throws instead.</p>
 */
export function sectionHtml(state: PanelState, id: string): string {
  for (const { html } of everyPanelPage(state)) {
    const found = elementWith(html, `data-section="${id}"`);
    if (found !== undefined) {
      return found;
    }
  }
  throw new Error(`no page draws a section "${id}" — a test reading it would be checking nothing`);
}

/** The element whose start tag carries `attribute`, through its matching close tag, nesting counted. */
function elementWith(html: string, attribute: string): string | undefined {
  const at = html.indexOf(attribute);
  if (at < 0) {
    return undefined;
  }
  const start = html.lastIndexOf('<', at);
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
