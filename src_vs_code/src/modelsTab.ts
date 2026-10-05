import { CATALOG_USES } from './catalogFields';
import { lastStagesOf } from './catalogWriteRules';
import { escapeHtml } from './escapeHtml';
import { accessOf, modelCard, usedFor } from './modelCard';
import { USE_LABELS } from './modelCardFields';
import { cardContextFor, type PanelState } from './panelView';
import type { Vendor } from './vendors';

/**
 * The Models tab of the new Settings page (todo/PLAN_one_model_catalog.md E3.2): every model this side can use, added
 * once — the toolbar, the three filter rows, and one card per catalog row.
 *
 * <p>The filters live in the page (`catalogPageScript.ts` reads the cards' `data-*` and keeps the choice in the webview's
 * own state): narrowing what is shown changes nothing stored, so it never costs a round trip or a repaint.</p>
 */

/** What refers to a row — the sentences the remove dialog lists and the card's "Used as" line. */
export function referencesOf(state: PanelState, id: string): readonly string[] {
  return REFERENCES.map((find) => find(state, id)).filter((said) => said.length > 0);
}

/** "3 security-lane pairs", "1 question-consultant row" — or '' for none. */
function counted(count: number, what: string): string {
  return count === 0 ? '' : `${count} ${what}${count === 1 ? '' : 's'}`;
}

/** Each place a catalog row is referred to from, as one finder: what it says about the row, or '' when it does not. */
const REFERENCES: readonly ((state: PanelState, id: string) => string)[] = [
  (state, id) => {
    const callers = Object.entries(state.settings.consult.byCaller).filter(([, chosen]) => chosen.vendor === id).map(([caller]) => caller);

    return callers.length === 0 ? '' : `the consultant for ${callers.join(', ')}`;
  },
  (state, id) => counted(state.settings.qconsult.rows.filter((row) => row.vendor === id).length, 'question-consultant row'),
  (state, id) => counted(state.settings.securityLane.runs.filter((run) => run.vendor === id).length, 'security-lane pair'),
  (state, id) => (state.chat?.model === id ? 'the model a chat opens on' : ''),
  (state, id) => (state.settings.bugzModel.split('/')[0] === id ? 'the Bugz ranking model' : ''),
];

/** One filter row: "All", then a chip per value with its count — dashed when nothing has it. */
function chipRow(title: string, key: string, values: readonly (readonly [string, string, number])[]): string {
  const chips = values.map(([value, label, count]) => `<button type="button" class="chip${count === 0 ? ' empty' : ''}" data-chip="${key}"`
    + ` data-value="${escapeHtml(value)}" aria-pressed="false">${escapeHtml(label)} <span class="n">${count === 0 ? 'none' : count}</span></button>`);

  return `<div class="chip-row"><span class="group">${escapeHtml(title)}</span>`
    + `<button type="button" class="chip" data-chip="${key}" data-value="" aria-pressed="true">All</button>${chips.join('')}</div>`;
}

const STAGE_FILTERS: readonly (readonly [string, string])[] = [['plan', 'plan review'], ['code', 'code review'], ['document', 'documents']];

function countWhere(rows: readonly Vendor[], has: (row: Vendor) => boolean): number {
  return rows.filter((row) => row.enabled && has(row)).length;
}

function filters(rows: readonly Vendor[]): string {
  const uses = [...STAGE_FILTERS, ...CATALOG_USES.map((use) => [use, USE_LABELS[use]] as const)]
    .map(([value, label]) => [value, label, countWhere(rows, (row) => usedFor(row).includes(value))] as const);
  const efforts = [...new Set(rows.map((row) => row.effort ?? '').filter((level) => level.length > 0))]
    .map((level) => [level, level, countWhere(rows, (row) => row.effort === level)] as const);
  const runtimes = [...new Set(rows.map((row) => row.runtime))].map((runtime) => [runtime, runtime, countWhere(rows, (row) => row.runtime === runtime)] as const);

  return `<div class="filters">${chipRow('Used for', 'uses', uses)}`
    + `${chipRow('Effort', 'effort', [['default', 'default', countWhere(rows, (row) => (row.effort ?? '') === '')], ...efforts])}`
    + `${chipRow('Runs on', 'runtime', runtimes)}</div>`;
}

const ACCESS_CHOICES: readonly (readonly [string, string])[] = [['', 'Any'], ['cli', 'a CLI here'], ['api', 'an API key'], ['local', 'this machine’s GPU'], ['remote', 'a Team server']];

function toolbar(rows: readonly Vendor[]): string {
  const on = rows.filter((row) => row.enabled).length;

  return `<div class="toolbar"><button type="button" class="primary" data-command="addVendor">＋ Add a model</button>`
    + `<input type="search" id="model-search" placeholder="Find a model" aria-label="Find a model">`
    + `<label>Runs on <select id="model-access">${ACCESS_CHOICES.map(([value, label]) => `<option value="${value}">${escapeHtml(label)}</option>`).join('')}</select></label>`
    + `<label><input type="checkbox" id="model-show-off"> Show switched-off models</label>`
    + `<span class="spacer"></span><span class="hint" id="model-count">${on} on · ${rows.length} in all</span></div>`;
}

/** The tab. */
export function modelsTabHtml(state: PanelState): string {
  const rows = state.vendors;
  const contextOf = cardContextFor(state);
  const binary = { installed: state.server.kind !== 'absent', features: state.serverFeatures };
  const cards = rows.map((row) => modelCard(row, {
    context: contextOf(row), references: referencesOf(state, row.id), lastFor: lastStagesOf(rows, row), binary,
  }));

  return `<p class="lead">Every model this side can use, added once. Tick what each one is used for; one model can be added `
    + `more than once with different settings.</p>${toolbar(rows)}${filters(rows)}`
    + `<div class="cards">${cards.join('\n')}</div>`
    + `<div class="empty-state" data-models-empty hidden>No model matches these filters. <button type="button" id="model-clear">Clear the filters</button></div>`;
}

/** Where a row runs — re-exported for the page's own counters. */
export { accessOf };
