import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { type PanelState } from '../panelView';
import { questionPrompts, rowEdited } from '../qconsultWrite';
import type { QuestionRowSetting } from '../qconsultSettings';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { lastWrite, panelState, runPanel } from './panelPageHarness';
import { pageTree } from './pageTree';

/**
 * E4.2 of research/PLAN_one_model_catalog.md, the question consultant's half: on the new page a question row PICKS a
 * catalog row ticked "question consultant" — its model is edited on Models — and keeps its own id, prompt and switch.
 */

const asking: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'ask-deep', runtime: 'codex', plan: false, code: false, uses: ['qconsult'] };
const chatOnly: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'chat-fast', plan: false, code: false, uses: ['chat'] };
const ROWS: readonly Vendor[] = [...DEFAULT_VENDORS, asking, chatOnly];

function question(id: string, overrides: Partial<QuestionRowSetting> = {}): QuestionRowSetting {
  return { id, vendor: 'claude', runtime: 'claude', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'question-opinion', enabled: true, ...overrides };
}

const context = { prompts: questionPrompts([]), vendors: ROWS };

test('a question row that picks a catalog row becomes a reference to it, keeps its id and prompt, and goes off', () => {
  const after = rowEdited([question('q-1', { model: 'opus', baseUrl: 'x' })], 'q-1', 'qconsultRowPick', 'ask-deep', context);

  assert.deepEqual(after, [{ id: 'q-1', vendor: 'ask-deep', runtime: '', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'question-opinion', enabled: false }]);
});

test('picking the row it already refers to changes nothing — it stays on', () => {
  const held = question('q-1', { vendor: 'ask-deep', runtime: '' });

  assert.deepEqual(rowEdited([held], 'q-1', 'qconsultRowPick', 'ask-deep', context), [held]);
});

test('a pick of a row not ticked for the question consultant is refused', () => {
  assert.equal(rowEdited([question('q-1')], 'q-1', 'qconsultRowPick', 'chat-fast', context), undefined);
  assert.equal(rowEdited([question('q-1')], 'q-1', 'qconsultRowPick', 'nobody', context), undefined);
});

function stateWith(rows: readonly QuestionRowSetting[]): PanelState {
  return { ...panelState('questionconsultant', { settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, rows } } }), catalogRows: ROWS };
}

test('the new page draws a pick per question row, not the definition fields; the current page keeps them', () => {
  const state = stateWith([question('q-1', { vendor: 'ask-deep', runtime: '' }), question('q-2', { vendor: 'chat-fast', runtime: '' })]);
  const html = catalogHtml(state, 'test-nonce', 'consultants/qconsult');
  const controls = runPanel(state, { html }).controls.filter((one) => one.dataset['setting']?.startsWith('qconsultRow') === true);
  const picks = controls.filter((one) => one.dataset['setting'] === 'qconsultRowPick');

  assert.deepEqual(picks.map((one) => one.dataset['caller']), ['q-1', 'q-2']);
  assert.deepEqual(controls.filter((one) => /^qconsultRow(Vendor|Model|BaseUrl|ExecutablePath|Key)$/.test(one.dataset['setting'] ?? '')), []);
  const stranded = picks[1]!;
  assert.equal(stranded.value, 'chat-fast', 'a pick of a row not ticked is gone from its list, so the next change would lose it');
  assert.ok(stranded.options.some((one) => one.value === 'chat-fast' && one.text.includes('(stranded)')));
  assert.match(pageTree(html).one((node) => node.dataset.pane === 'consultants/qconsult', 'the pane').text(),
    /chat-fast is not ticked for the question consultant on Models/);
});

test('changing a row\'s pick on the new page writes it', () => {
  const state = stateWith([question('q-1')]);
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'consultants/qconsult') });
  const pick = page.controls.find((one) => one.dataset['setting'] === 'qconsultRowPick' && one.dataset['caller'] === 'q-1');
  assert.ok(pick !== undefined);
  pick.value = 'ask-deep';
  pick.fire('change');

  const { key, value, caller } = lastWrite(page);
  assert.deepEqual({ key, value, caller }, { key: 'qconsultRowPick', value: 'ask-deep', caller: 'q-1' });
});
