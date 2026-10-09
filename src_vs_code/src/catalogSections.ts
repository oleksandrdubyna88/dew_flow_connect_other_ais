import { cardColumns } from './cardColumns';
import type { CatalogUse } from './catalogFields';
import { escapeHtml } from './escapeHtml';
import { USE_LABELS } from './modelCardFields';
import { consultantPicksHtml } from './consultantPicks';
import {
  cliButtons, consultantSection, gateBody, keysBody, limitsSection, promptsBody, questionConsultantSection, securityLaneSection,
  serverBody, sideBody, teamServersSection, type PanelState,
} from './panelView';
import { rowsFor } from './catalogPicks';
import { securityTryHtml } from './securityTry';
import { rolesEmbedded } from './rolesEmbed';
import { commandsEmbedded } from './commandsEmbed';
import { chatTabHtml } from './chatTabEmbed';
import { cliTableHtml, mcpClientsHtml, movedFromHtml } from './setupTab';
import type { Vendor } from './vendors';

/**
 * What each place of the Settings page draws besides Models (todo/PLAN_one_model_catalog.md E4.1): the section the old
 * page drew for that place, by the same builder — never a copy — which is why a control kept behaving the same through
 * the switch-over (E5). Pure: the page calls it, tests read it.
 *
 * <p>Every builder is NAMED here, in {@link SPLIT} or {@link MOVED_SECTIONS}. Until E5's prerequisite (b) the moved
 * sections were found by searching the old page's section list (`PANEL_SECTIONS`) through `OLD_TAB_PLACES`, so deleting
 * an old section in E5.1 would have emptied its place on this page without a word from the compiler.</p>
 */

/**
 * The places whose drawing is not their old section as it is: the prompts section split in two (E4.1), the features that
 * pick from the catalog (E4.2), the Review roles tab folded in (E4.3), and Chat (E4.6b).
 */
const SPLIT: Readonly<Record<string, (state: PanelState) => string>> = {
  'reviews/commands': (state) => (state.commands === undefined ? '<p class="hint">Reading the commands…</p>' : commandsEmbedded(state.commands)),
  'reviews/roles': (state) => (state.roles === undefined ? '<p class="hint">Reading the roles…</p>' : rolesEmbedded(state.roles, state.settings.roleEnabled)),
  'reviews/stages': (state) => promptsBody(state, 'stages'),
  // Chat, drawn from the rows ticked Chat with the prompt presets inline (E4.6b).
  chat: chatTabHtml,
  'reviews/prompts': (state) => promptsBody(state, 'prompts'),
  // Each pick with its caller's health block under it, by the one rule (E5.1b).
  'consultants/consultant': (state) => consultantSection(state, consultantPicksHtml(state.settings.consult, state.catalogRows ?? state.vendors, state.consultantHealth)),
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
 * The state the keys are drawn with: counted across EVERY row (E4.5), because a row that exists for a feature alone still
 * needs its key, and the panel's `vendors` holds the reviewers only.
 */
function everyRow(state: PanelState): PanelState {
  return { ...state, vendors: state.catalogRows ?? state.vendors };
}

/**
 * The sections drawn here as they are, by place, each by the builder the old page drew it with. The old page's
 * Reviewers tab became Models, which the page builds itself; every other old tab is in {@link SPLIT}.
 */
const MOVED_SECTIONS: Readonly<Record<string, (state: PanelState) => string>> = {
  // Its "Edit commands…" a jump to Reviews › Commands here, never the commands page (E5.1 step 1). Its fields in the
  // page's columns, two on a wide editor; Limits, five numbers one to a row, keeps its one column.
  'reviews/gate': (state) => cardColumns(gateBody(state)),
  'reviews/limits': limitsSection,
  'setup/keys': (state) => keysBody(everyRow(state)),
  'setup/team': teamServersSection,
  'setup/side': sideBody,
  'setup/mcp': serverBody,
};

/**
 * What a Setup place adds after its moved section (E4.5): the CLIs the models run on under the keys; the data folder's
 * last move and the MCP clients' registration under the MCP server.
 */
const AFTER: Readonly<Record<string, (state: PanelState) => string>> = {
  'setup/keys': (state) => cliTableHtml(state.catalogRows ?? state.vendors, state.cliStatus, cliButtons),
  'setup/mcp': (state) => `${movedFromHtml(state.lastDataMove)}\n${mcpClientsHtml(state.mcpClients ?? [])}`,
};

/** The section drawn at this place — '' where none is (Models, and the pages E4.3 and E4.4 fold in). */
function sectionAt(place: string, state: PanelState): string {
  const split = SPLIT[place];

  return split === undefined ? `${movedSectionAt(place, state)}${afterAt(place, state)}` : split(state);
}

/** The moved section for this place, by its own builder — '' where it has none. */
function movedSectionAt(place: string, state: PanelState): string {
  const moved = MOVED_SECTIONS[place];

  return moved === undefined ? '' : moved(state);
}

function afterAt(place: string, state: PanelState): string {
  const after = AFTER[place];

  return after === undefined ? '' : `\n${after(state)}`;
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
