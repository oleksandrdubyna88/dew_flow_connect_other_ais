import { panelHtml, type PanelState } from '../panelView';
import { SURFACE_IDS, type SurfaceId } from '../panelSurface';

/**
 * Every page the panel draws, for the tests that make a claim about "every control on the panel".
 *
 * <p>Such a test used to render ONE page, and that is a test that goes vacuous rather than red the day
 * a section moves to another page: every assertion still holds, over fewer controls. So the pages are
 * derived from `SURFACE_IDS`, and {@link pageOf} switches over the surface exhaustively — a surface
 * added without a page here is a compile error, not a page nobody scans.</p>
 */
export function pageOf(surface: SurfaceId, state: PanelState): string {
  switch (surface) {
    case 'sidebar':
      return panelHtml(state, 'n0nce');
  }
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
  const pattern = new RegExp(`<${tag}\\b|</${tag}>`, 'g');
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
