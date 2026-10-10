import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { DEFAULT_CONSULT, type ConsultSettings, resolveConsultant } from '../consultSettings';
import { type PanelState } from '../panelView';
import type { QuestionRowSetting } from '../qconsultSettings';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState, runPanel } from './panelPageHarness';
import { type PageNode, pageTree } from './pageTree';

/**
 * Operator, 2026-10-10: Consultants › Consultant said "Used by: chat-preset-mtwtqr0p-3." and the question consultant's
 * picker listed `chat-preset-mtwtqr0p-3` — the raw id of a migrated chat preset — while its Models card is called
 * "GPT-6-Astra". Nobody recognised the row, and the tab read as not seeing the added model at all. A catalog row is
 * named the way its Models card names it — its display name, with the id beside it when the two differ — and a row with
 * no name keeps its id alone. The option's VALUE stays the id: that is what a pick stores.
 */

const ASTRA_ID = 'chat-preset-mtwtqr0p-3';
const astra: Vendor = { ...DEFAULT_VENDORS[0]!, id: ASTRA_ID, name: 'GPT-6-Astra', plan: false, code: false, uses: ['consultant', 'qconsult'] };
const plain: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-claude', runtime: 'claude', plan: false, code: false, uses: ['consultant', 'qconsult'] };
/** A named row that is ticked for nothing here — what a stranded pick of a row still on Models is. */
const parked: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'chat-preset-old', name: 'Old Luna', plan: false, code: false, uses: ['chat'] };
const ROWS: readonly Vendor[] = [...DEFAULT_VENDORS, astra, plain, parked];

const question = (id: string, vendor: string): QuestionRowSetting => ({
  id, vendor, runtime: '', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'question-opinion', enabled: false,
});

function consultWith(stored: ConsultSettings['stored']): ConsultSettings {
  const byCaller = Object.fromEntries(Object.entries(stored).map(([caller, one]) => [caller, resolveConsultant(one, ROWS)]));

  return { ...DEFAULT_CONSULT, stored: { ...DEFAULT_CONSULT.stored, ...stored }, byCaller: { ...DEFAULT_CONSULT.byCaller, ...byCaller } };
}

/** The pane of a place, from the page as it is RUN. */
function paneOf(place: string, settings: PanelState['settings']): PageNode {
  const state: PanelState = { ...panelState('consultant', { settings }), catalogRows: ROWS };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', place) });

  return pageTree(page.html).one((node) => node.dataset['pane'] === place, `${place} pane`);
}

/** What a picker offers, as a person reads it — value and label. */
function offered(pane: PageNode, selectId: string): readonly (readonly [string, string])[] {
  return pane.one((node) => node.id === selectId, `picker ${selectId}`).find((node) => node.tagName === 'OPTION')
    .map((option) => [option.attrs['value'] ?? '', option.text()] as const);
}

function usedBy(pane: PageNode): string {
  return pane.one((node) => node.dataset['usedBy'] !== undefined, 'used-by strip').one((node) => node.tagName === 'SPAN', 'strip sentence').text();
}

test('the Consultant tab names a row by its display name — in the strip, the picker and a stranded pick\'s sentence', () => {
  const pane = paneOf('consultants/consultant', { ...DEFAULTS, consult: consultWith({ codex: { vendor: 'chat-preset-old', runtime: '', model: '', baseUrl: '', executablePath: '' } }) });
  const claude = offered(pane, 'consult-row-claude');
  const codexPick = pane.one((node) => node.className === 'field consult-pick' && node.find((one) => one.id === 'consult-row-codex').length > 0, 'codex pick');

  assert.equal(usedBy(pane), `Used by: GPT-6-Astra (${ASTRA_ID}), consult-claude.`);
  assert.ok(claude.some(([value, label]) => value === ASTRA_ID && label === `GPT-6-Astra (${ASTRA_ID})`), `offered: ${JSON.stringify(claude)}`);
  assert.ok(claude.some(([value, label]) => value === 'consult-claude' && label === 'consult-claude'), 'a row with no name keeps its id alone');
  assert.ok(offered(pane, 'consult-row-codex').some(([value, label]) => value === 'chat-preset-old' && label === 'Old Luna (chat-preset-old) (stranded)'),
    'a stranded pick of a row still on Models is not named');
  assert.match(codexPick.text(), /Old Luna \(chat-preset-old\) is not ticked for the consultant on Models/);
});

test('the Question consultant tab names a row by its display name — in the strip and in each row\'s picker', () => {
  const qconsult = { ...DEFAULTS.qconsult, rows: [question('q-1', ASTRA_ID)] };
  const pane = paneOf('consultants/qconsult', { ...DEFAULTS, qconsult });
  const picks = offered(pane, 'qconsultRowPick-q-1');

  assert.equal(usedBy(pane), `Used by: GPT-6-Astra (${ASTRA_ID}), consult-claude.`);
  assert.deepEqual(picks, [[ASTRA_ID, `GPT-6-Astra (${ASTRA_ID})`], ['consult-claude', 'consult-claude']]);
});
