import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { consultantPickView, consultantPickWrites } from '../consultantPicks';
import { DEFAULT_CONSULT, type ConsultantChoice } from '../consultSettings';
import { foldedWrite } from '../catalogEdit';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * E4.2 of todo/PLAN_one_model_catalog.md, the consultant half: on the new page a caller's consultant is PICKED from the
 * rows ticked Consultant — the model itself is edited on its Models card. Absent is the shipped pair (D2); a pick of a
 * row no longer ticked, or gone, is shown and named, never cleared (D3); the caller's own vendor is offered, never
 * refused.
 */

const reference = (vendor: string): ConsultantChoice => ({ vendor, runtime: '', model: '', baseUrl: '', executablePath: '' });
const row = (id: string, uses: Vendor['uses'], runtime: Vendor['runtime'] = 'codex'): Vendor =>
  ({ ...DEFAULT_VENDORS[0]!, id, runtime, plan: false, code: false, uses });

const deep = row('deep-high', ['consultant']);
const fable = row('fable', ['consultant'], 'claude');
const chatty = row('chat-fast', ['chat']);
const ROWS: readonly Vendor[] = [...DEFAULT_VENDORS, deep, fable, chatty];

test('a caller picks from the rows ticked Consultant, and from the shipped pair', () => {
  const view = consultantPickView('claude', DEFAULT_CONSULT.stored['claude']!, ROWS);

  assert.deepEqual(view.options.map((one) => one.value), ['', 'deep-high', 'fable']);
  assert.match(view.options[0]!.label, /shipped pair.*codex/i);
  assert.equal(view.selected, '', 'nothing stored is the shipped pair');
});

test('a pick of a row no longer ticked is shown, selected and named — never cleared (D3)', () => {
  const view = consultantPickView('codex', reference('chat-fast'), ROWS);

  assert.equal(view.selected, 'chat-fast');
  assert.ok(view.options.some((one) => one.value === 'chat-fast'), 'the stranded pick is not on the list, so the next change would lose it');
  assert.match(view.note, /chat-fast is not ticked for the consultant/);
});

test('a pick of a row that is gone says so', () => {
  const view = consultantPickView('codex', reference('deleted-one'), ROWS);

  assert.equal(view.selected, 'deleted-one');
  assert.match(view.note, /deleted-one is no longer on Models/);
});

test('the caller\'s own vendor is offered, with a word about it, never refused', () => {
  const view = consultantPickView('claude', reference('fable'), ROWS);

  assert.equal(view.selected, 'fable');
  assert.match(view.note, /same vendor as the caller/);
  assert.ok(view.options.every((one) => !one.disabled));
});

test('a pick is stored as a reference, and the shipped pair as nothing', () => {
  const stored = { codex: { vendor: 'claude' }, gemini: { vendor: 'deep-high' } };

  assert.deepEqual(consultantPickWrites(stored, 'claude', 'deep-high', ROWS).writes,
    [{ key: 'consultants', value: { codex: { vendor: 'claude' }, gemini: { vendor: 'deep-high' }, claude: { vendor: 'deep-high' } } }]);
  assert.deepEqual(consultantPickWrites(stored, 'gemini', '', ROWS).writes, [{ key: 'consultants', value: { codex: { vendor: 'claude' } } }]);
});

test('a pick never touches the rows — re-pointing a caller through the old page\'s fold would drop its row', () => {
  const owned = row('consult-claude', ['consultant']);
  const stored = { claude: { vendor: 'consult-claude' } };
  const rows = [...ROWS, owned];
  const pick = consultantPickWrites(stored, 'claude', 'deep-high', rows);

  assert.deepEqual(pick.writes.map((one) => one.key), ['consultants'], 'a pick writes the map alone');
  // Why the pick is written UNFOLDED: the old page's fold reads a caller re-pointed away from a row only it used as
  // that row's removal (E1.4). On the new page that row is a model the person sees on Models.
  const folded = foldedWrite('consultants', pick.writes[0]!.value, (key) => ({ vendors: rows, consultants: stored } as Record<string, unknown>)[key]);
  assert.ok(folded.vendors !== undefined && folded.vendors.every((one) => (one as { id?: string }).id !== 'consult-claude'),
    'the old page\'s fold no longer drops the row — then the pick may go through it like every other write');
});

test('a pick the page could not have offered is refused, and nothing is written', () => {
  assert.match(consultantPickWrites({}, 'claude', 'chat-fast', ROWS).refusal, /not ticked for the consultant/);
  assert.deepEqual(consultantPickWrites({}, 'claude', 'chat-fast', ROWS).writes, []);
  assert.deepEqual(consultantPickWrites({}, '__proto__', 'deep-high', ROWS).writes, []);
});

test('the new page draws one picker per caller, and changing it writes the pick', () => {
  const state = { ...panelState('reviewers'), catalogRows: ROWS };
  const body = catalogBody(state);
  assert.equal([...body.matchAll(/data-setting="consultantRow"/g)].length, 4);

  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'consultants/consultant') });
  const picker = page.controls.find((one) => one.dataset['setting'] === 'consultantRow' && one.dataset['caller'] === 'gemini');
  assert.ok(picker !== undefined);
  picker.value = 'deep-high';
  picker.fire('change');

  const { key, value, caller } = lastWrite(page);
  assert.deepEqual({ key, value, caller }, { key: 'consultantRow', value: 'deep-high', caller: 'gemini' });
});
