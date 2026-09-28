import { escapeHtml } from './escapeHtml';
import { tabStrip } from './tabStrip';

/**
 * Which webview a section is drawn on, and how a page of sections becomes markup and a paint key.
 *
 * <p>Pure, and generic over the state it renders, so it imports nothing from `panelView.ts`: the section
 * builders live there and are private, and a registry module that needed them would be an import cycle.
 * `panelView.ts` declares its sections as data of this shape; this module owns what is done with them.</p>
 *
 * <p><b>The paint key is the markup itself</b> (`research/PLAN_settings_page.md`, F1). It used to be a list of
 * state fields kept beside the markup by hand, and ten fields the page draws were missing from it — so a
 * probe that answered after the first paint changed the state, left the key where it was, and never
 * reached the screen. A key made of what is DRAWN cannot miss a field, because it is not a list. What it
 * leaves out it leaves out on purpose: the live regions, which are patched rather than repainted, and the
 * open/closed state of a section, which only the page itself ever changes.</p>
 */

/** The regions a live message patches in place. Everything else on a page is a control. */
export const LIVE_REGION_IDS = ['questions', 'rounds', 'consultations', 'notifications'] as const;
export type LiveRegionId = (typeof LIVE_REGION_IDS)[number];
export type Regions = Readonly<Record<LiveRegionId, string>>;

/** Every region empty — what a paint key is built with, so a live change can never move the key. */
export const BLANK_REGIONS: Regions = { questions: '', rounds: '', consultations: '', notifications: '' };

/**
 * The webviews a section can be drawn on — the list every page-wide test iterates.
 *
 * <p>A test that checks "every control on the panel" by rendering ONE page goes vacuous rather than
 * red the day a section moves to another page, so those tests walk this list instead of naming a page.</p>
 */
export const SURFACE_IDS = ['sidebar', 'settings'] as const;
export type SurfaceId = (typeof SURFACE_IDS)[number];

/** One section of a page: what it is called, where it is drawn, and its body. */
export interface SectionSpec<S> {
  /** Letters only: it is an id fragment (`pane-<id>`, `tab-<id>`) and the `data-section` value the tests scan with `[a-zA-Z]+`. */
  readonly id: string;
  readonly title: string;
  readonly surface: SurfaceId;
  /** The body, given the live regions to embed — blank when a key is being built. */
  readonly body: (state: S, live: Regions) => string;
}

/** A live region's wrapper, the element the page's script patches by id. */
export function liveRegion(id: LiveRegionId, live: Regions): string {
  return `<div id="live-${id}">${live[id]}</div>`;
}

/** The sections a surface draws, in declaration order. */
export function sectionsOn<S>(specs: readonly SectionSpec<S>[], surface: SurfaceId): readonly SectionSpec<S>[] {
  return specs.filter((spec) => spec.surface === surface);
}

function disclosure(id: string, title: string, open: boolean, body: string): string {
  return `<details class="section sec-${id}" data-section="${id}"${open ? ' open' : ''}>
  <summary>${escapeHtml(title)}</summary>
${body}
</details>`;
}

/**
 * The sidebar's body: the questions region first, never collapsible, then one disclosure per section.
 *
 * @param open the sections the person has open — drawn, and deliberately NOT part of the key
 */
export function sidebarBody<S>(
  specs: readonly SectionSpec<S>[],
  state: S,
  open: readonly string[],
  live: Regions,
): string {
  return [
    liveRegion('questions', live),
    ...sectionsOn(specs, 'sidebar').map((spec) => disclosure(spec.id, spec.title, open.includes(spec.id), spec.body(state, live))),
  ].join('\n');
}

/**
 * What a surface paints on: its body with every live region blank and every section closed.
 *
 * <p>Closed, because only the PAGE opens or closes a section: a toggle arrives here already drawn, so a
 * key that moved with it reloaded the whole webview a few seconds later to show exactly what was on
 * screen — dropping the scroll position and any open dropdown with it (F10). A repaint made for any
 * other reason still draws the sections open that the person opened.</p>
 */
export function sidebarKey<S>(specs: readonly SectionSpec<S>[], state: S): string {
  return sidebarBody(specs, state, [], BLANK_REGIONS);
}

/** The names the Settings page wires its strip and panes with: `tab-<id>` controls `pane-<id>`. */
const SETTINGS_STRIP = { tab: 'tab-', panel: 'pane-', label: 'Settings', strip: 'settings', roving: true } as const;

/**
 * The Settings page's body: a strip with one tab per section, and one pane per tab.
 *
 * <p><b>Drawn tab-neutral</b> — no tab chosen, every pane hidden — and the page's own script opens the
 * tab the host holds, which reaches it as a script literal beside the caret (`research/PLAN_settings_page.md`,
 * D6). A pane drawn as selected would put the held tab into the markup, the markup is the paint key, and
 * every tab press would then reload the whole page a few seconds later: the defect the key's own rule
 * exists to stop.</p>
 *
 * <p>Each pane keeps `data-section`, so everything that reads a section by that attribute finds it on
 * this page as on the sidebar, and lacks the `section` class and an `open` attribute, so neither the
 * accordion's toggle binding nor the open-sections scan ever mistakes a pane for a disclosure.
 * `data-pane` is what the page's script selects panes by.</p>
 *
 * @param specs the sections to draw, in order — the caller chooses them, so a test can pass its own
 */
export function settingsBody<S>(specs: readonly SectionSpec<S>[], state: S): string {
  const tabs = specs.map((spec) => ({ key: spec.id, label: spec.title }));
  const panes = specs.map((spec) => `<section id="${SETTINGS_STRIP.panel}${spec.id}" class="pane sec-${spec.id}" role="tabpanel" `
    + `aria-labelledby="${SETTINGS_STRIP.tab}${spec.id}" tabindex="0" data-section="${spec.id}" data-pane="${spec.id}" hidden>
${spec.body(state, BLANK_REGIONS)}
</section>`);

  return [`<main class="settings">`, tabStrip(tabs, '', SETTINGS_STRIP), ...panes, '</main>'].join('\n');
}
