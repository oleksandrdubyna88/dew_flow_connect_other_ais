import type { CatalogUse } from './catalogFields';
import { OLD_TAB_PLACES } from './catalogPlaces';
import { escapeHtml } from './escapeHtml';
import { USE_LABELS } from './modelCardFields';
import { BLANK_REGIONS } from './panelSurface';
import { consultantPicksHtml } from './consultantPicks';
import { consultantSection, PANEL_SECTIONS, promptsBody, questionConsultantSection, securityLaneSection, type PanelState } from './panelView';
import { rowsFor } from './catalogPicks';
import { securityTryHtml } from './securityTry';
import type { Vendor } from './vendors';

/**
 * What each place of the new Settings page draws besides Models (todo/PLAN_one_model_catalog.md E4.1): the old page's
 * own section for that place, by its own builder — never a copy — so a control behaves the same on both pages until
 * E5 retires the old one. Pure: the page calls it, tests read it.
 */

/**
 * The places whose drawing is not their old section as it is: the prompts section split in two (E4.1), and the
 * consultant's callers picking from the catalog (E4.2).
 */
const SPLIT: Readonly<Record<string, (state: PanelState) => string>> = {
  'reviews/stages': (state) => promptsBody(state, 'stages'),
  'reviews/prompts': (state) => promptsBody(state, 'prompts'),
  'consultants/consultant': (state) => consultantSection(state, consultantPicksHtml(state.settings.consult, state.catalogRows ?? state.vendors)),
  'consultants/qconsult': (state) => questionConsultantSection(state, state.catalogRows ?? state.vendors),
  security: (state) => {
    const rows = state.catalogRows ?? state.vendors;

    return `${securityLaneSection(state, rowsFor('security', rows), rows)}\n${securityTryHtml(state.securityTry)}`;
  },
};

/** The feature each place serves, for its "used by" strip. */
const PLACE_USES: Readonly<Record<string, CatalogUse>> = {
  'consultants/consultant': 'consultant',
  'consultants/qconsult': 'qconsult',
  security: 'security',
  chat: 'chat',
};

/** What a feature does while no row is ticked for it — only where a decision says (D2). */
const UNTICKED: Readonly<Partial<Record<CatalogUse, string>>> = {
  consultant: ' Until one is, each caller asks the pair it ships with.',
};

/**
 * The state a section is drawn with. The keys are counted across EVERY row (E4.5): a row that exists for a feature
 * alone still needs its key, and the panel's `vendors` holds the current page's reviewers only.
 */
function stateFor(sectionId: string, state: PanelState): PanelState {
  return sectionId === 'keys' ? { ...state, vendors: state.catalogRows ?? state.vendors } : state;
}

/** The old section drawn at this place — '' where none is (Models, and the pages E4.3 and E4.4 fold in). */
function sectionAt(place: string, state: PanelState): string {
  const split = SPLIT[place];
  if (split !== undefined) {
    return split(state);
  }
  const spec = PANEL_SECTIONS.find((one) => one.surface === 'settings' && OLD_TAB_PLACES[one.id] === place);

  return spec === undefined ? '' : spec.body(stateFor(spec.id, state), BLANK_REGIONS);
}

/** The rows ticked for a use, by name — every one a person's string, so every one escaped. */
function tickedFor(use: CatalogUse, rows: readonly Vendor[]): string {
  const ticked = rows.filter((row) => (row.uses ?? []).includes(use));
  if (ticked.length === 0) {
    return `No model is ticked for the ${USE_LABELS[use]} yet.${UNTICKED[use] ?? ''}`;
  }

  return `Used by: ${ticked.map((row) => `<b>${escapeHtml(row.id)}</b>${row.enabled ? '' : ' (switched off)'}`).join(', ')}.`;
}

/** The strip above a feature tab: the rows ticked for it, and the way to change them on Models. */
function usedByStrip(use: CatalogUse, state: PanelState): string {
  return `<div class="used-by" data-used-by="${use}"><span>${tickedFor(use, state.catalogRows ?? state.vendors)}</span>`
    + ` <button type="button" class="link" data-models-uses="${use}">Change on Models</button></div>`;
}

/**
 * What a place draws, or '' when the place is not drawn here yet.
 *
 * @param place a place of `CATALOG_TABS` (`tab` or `tab/sub`)
 */
export function placeBody(place: string, state: PanelState): string {
  const section = sectionAt(place, state);
  const use = PLACE_USES[place];
  if (section.length === 0 || use === undefined) {
    return section;
  }

  return `${usedByStrip(use, state)}\n${section}`;
}
