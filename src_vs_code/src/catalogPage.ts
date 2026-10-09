import { IDLE } from './busySnapshot';
import { CATALOG_CSS } from './catalogCss';
import { catalogScript } from './catalogPageScript';
import { CATALOG_TABS, type CatalogTab } from './catalogPlaces';
import { placeBody } from './catalogSections';
import { CONFIRM_DIALOG } from './catalogShell';
import { modelsTabHtml } from './modelsTab';
import { pageDocument, type PanelState } from './panelView';
import { settingsTextCss } from './settingsPage';
import { tabStrip } from './tabStrip';
import { textControlsHtml, textOf } from './textControls';

/**
 * The Settings page (research/PLAN_one_model_catalog.md, E3) — painted in the ONE Settings slot, the only page there since
 * E5.1 step 5 removed the page it replaced and the preview switch between them. It is drawn into the panel's document
 * (`pageDocument`: its policy, its writes, its commands, its busy marks and focus restore) with its own body, sheet and
 * script.
 *
 * <p>The body is drawn PLACE-NEUTRAL — every pane hidden, no tab chosen — and the place the host holds reaches the page
 * only as a script literal: the body is the paint key, and a place drawn into it would reload the page after every
 * press (D6).</p>
 */

/** The top strip: `ctab-<id>` controls `cpane-<id>`. */
const TOP_STRIP = { tab: 'ctab-', panel: 'cpane-', label: 'Settings', strip: 'catalog', roving: true } as const;

/** One tab's sub-strip: a sub-tab's key is its whole place, so the page's one click handler tells the two apart. */
function subStrip(tab: CatalogTab): string {
  const names = { tab: `ctab-${tab.id}-`, panel: `cpane-${tab.id}-`, label: tab.label, strip: `catalog-${tab.id}`, roving: true };

  return tabStrip(tab.subs.map((sub) => ({ key: `${tab.id}/${sub.id}`, slug: sub.id, label: sub.label })), '', names);
}

/** A place's content, in a readable column (E4.1). */
function placeContent(place: string, state: PanelState): string {
  return `<div class="moved">\n${placeBody(place, state)}\n</div>`;
}

function subPanes(tab: CatalogTab, state: PanelState): string {
  return tab.subs.map((sub) => `<div id="cpane-${tab.id}-${sub.id}" class="subpane" role="tabpanel" aria-labelledby="ctab-${tab.id}-${sub.id}"`
    + ` tabindex="0" data-pane="${tab.id}/${sub.id}" hidden>${placeContent(`${tab.id}/${sub.id}`, state)}</div>`).join('\n');
}

/** What a tab holds: Models is built here; every other place draws its builder (`catalogSections.ts`). */
function paneBody(tab: CatalogTab, state: PanelState): string {
  if (tab.id === 'models') {
    return modelsTabHtml(state);
  }

  return tab.subs.length === 0 ? placeContent(tab.id, state) : `${subStrip(tab)}\n${subPanes(tab, state)}`;
}

function pane(tab: CatalogTab, state: PanelState): string {
  return `<section id="cpane-${tab.id}" class="pane sec-${tab.id}" role="tabpanel" aria-labelledby="ctab-${tab.id}" tabindex="0"`
    + ` data-pane="${tab.id}" hidden>\n${paneBody(tab, state)}\n</section>`;
}

/** The page's body — and its paint key: no held place, no header, nothing that moves by the host's push. */
export function catalogBody(state: PanelState): string {
  const tabs = CATALOG_TABS.map((tab) => ({ key: tab.id, label: tab.label }));

  return ['<main class="settings catalog">', tabStrip(tabs, '', TOP_STRIP), ...CATALOG_TABS.map((tab) => pane(tab, state)), '</main>'].join('\n');
}

/** What the slot paints on. */
export function catalogKey(state: PanelState): string {
  return catalogBody(state);
}

/** The header: the page's name and the text controls — outside the paint key. */
function catalogHead(state: PanelState): string {
  const { size, tone } = textOf(state);

  return `<header class="settingsHead"><h1>Settings</h1>${textControlsHtml(size, tone)}</header>`;
}

/**
 * The page's whole document.
 *
 * @param place the place the host holds (`catalogPlaces.ts`); it reaches the script, never the markup
 * @param body the body when the caller has already built it (it is the paint key) — built here otherwise
 */
export function catalogHtml(state: PanelState, nonce: string, place: string, body = catalogBody(state)): string {
  const { size, tone } = textOf(state);

  return pageDocument(`${catalogHead(state)}\n${body}\n${CONFIRM_DIALOG}`, nonce, state.focus, {
    css: CATALOG_CSS + settingsTextCss(size, tone),
    script: catalogScript(place),
  }, state.busy ?? IDLE);
}
