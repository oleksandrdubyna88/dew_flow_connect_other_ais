import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogBody, catalogHtml } from '../catalogPage';
import { consultantPickView, consultantPickWrites } from '../consultantPicks';
import { DEFAULT_CONSULT, type ConsultantChoice, type ConsultSettings, resolveConsultant } from '../consultSettings';
import { foldedWrite } from '../catalogEdit';
import { type PanelState } from '../panelView';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { lastWrite, panelState, runPanel } from './panelPageHarness';
import { pageTree, type PageNode } from './pageTree';

/**
 * E4.2 of research/PLAN_one_model_catalog.md, the consultant half: on the new page a caller's consultant is PICKED from the
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

// ---------------------------------------------------------------------------------------------
// What the page SAYS under each pick — run on the Consultant tab as drawn (research/PLAN_one_model_catalog.md E5.3)

/** The settings with these callers' stored entries, resolved against the rows as the reader resolves them. */
function consultWith(stored: Readonly<Record<string, ConsultantChoice>>, rows: readonly Vendor[]): ConsultSettings {
  const byCaller = Object.fromEntries(Object.entries(stored).map(([caller, one]) => [caller, resolveConsultant(one, rows)]));

  return { ...DEFAULT_CONSULT, stored: { ...DEFAULT_CONSULT.stored, ...stored }, byCaller: { ...DEFAULT_CONSULT.byCaller, ...byCaller } };
}

/** A place's pane, from the page as it is RUN (its script executed by the panel harness). */
function paneRun(state: PanelState, place: string): PageNode {
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', place) });

  return pageTree(page.html).one((node) => node.dataset['pane'] === place, `${place} pane`);
}

/** One caller's pick on the Consultant tab, as the page draws it. */
function pickOn(consult: ConsultSettings, rows: readonly Vendor[], caller: string): PageNode {
  const state: PanelState = { ...panelState('consultant', { settings: { ...DEFAULTS, consult } }), catalogRows: rows };
  const pane = paneRun(state, 'consultants/consultant');

  return pane.one((node) => node.className === 'field consult-pick' && node.find((one) => one.id === `consult-row-${caller}`).length > 0,
    `${caller}'s pick`);
}

test('a Gemini CLI caller pointed at an Antigravity row is told it is its own vendor — a caller kind is not a runtime', () => {
  // The row that runs Gemini models is on the `antigravity` runtime, so a name-to-name comparison withholds the word
  // from the one caller most likely to be pointed back at itself — and a row from before the `gemini` runtime retired
  // still exists in people's settings.
  const agy = row('agy-pro', ['consultant'], 'antigravity');
  const retired = row('old-gemini', ['consultant'], 'gemini');
  const rows = [...ROWS, agy, retired];
  const consult = consultWith({ gemini: reference('agy-pro'), claude: reference('agy-pro'), other: reference('deep-high') }, rows);

  assert.match(pickOn(consult, rows, 'gemini').text(), /same vendor as the caller/, 'the Gemini CLI asking Antigravity is not told it is asking itself');
  assert.doesNotMatch(pickOn(consult, rows, 'claude').text(), /same vendor as the caller/, 'Claude Code asking Antigravity is told it is asking itself');
  assert.doesNotMatch(pickOn(consult, rows, 'other').text(), /same vendor as the caller/, 'another client is not any vendor, so nothing is itself');
  assert.match(pickOn(consultWith({ gemini: reference('old-gemini') }, rows), rows, 'gemini').text(), /same vendor as the caller/,
    'a row on the retired gemini runtime is the Gemini CLI too');
});

test('a caller whose consultant cannot be placed says why under its pick, and its id is shown as text', () => {
  // The rule's own sentence (`resolveConsultant`), never one invented by the page: it is what a person acts on. The id
  // is a name somebody typed, so it is read as text — never drawn as markup.
  const odd = '<b>retired</b>';
  const pick = pickOn(consultWith({ other: reference(odd) }, ROWS), ROWS, 'other');

  assert.match(pick.text(), /no reviewer is named '<b>retired<\/b>' and it is not a runtime this build can consult with/,
    'the reason the consultant cannot run is not said under its pick');
  assert.deepEqual(pick.find((node) => node.tagName === 'B'), [], 'the stored id was drawn as markup');
});

// Operator, 2026-10-10: GPT-6-Astra was ticked consultant on Models and Claude Code still asked the shipped pair. That is
// the design — a tick makes a row AVAILABLE, the caller's pick decides, absence is the shipped pair (D2) — but nothing on
// the page said so. The pick now says it, naming the ticked rows, while the caller is on the shipped pair.

/** The quiet hints under one caller's pick, as text. */
function hintsUnder(pick: PageNode): readonly string[] {
  return pick.find((node) => node.className.split(' ').includes('hint')).map((node) => node.text());
}

test('a caller on the shipped pair, with other rows ticked consultant, is told they are not asked until picked here', () => {
  const astra: Vendor = { ...row('chat-preset-mtwtqr0p-3', ['consultant']), name: 'GPT-6-Astra' };
  const one = [...DEFAULT_VENDORS, astra, chatty];
  const two = [...one, deep];

  assert.ok(hintsUnder(pickOn(DEFAULT_CONSULT, one, 'claude')).includes(
    'GPT-6-Astra is ticked consultant on Models, but Claude Code still asks the shipped pair — pick it here to use it.'),
  `under Claude Code's pick: ${JSON.stringify(hintsUnder(pickOn(DEFAULT_CONSULT, one, 'claude')))}`);
  assert.ok(hintsUnder(pickOn(DEFAULT_CONSULT, two, 'other')).includes(
    'GPT-6-Astra and deep-high are ticked consultant on Models, but Another client still asks the shipped pair — pick one here to use it.'));
});

/** Whether a pick carries the not-asking line. */
const saysUnpicked = (pick: PageNode): boolean => hintsUnder(pick).some((one) => one.includes('still asks the shipped pair'));

test('no such word when ONLY the shipped pair\'s own row is ticked consultant — it is the pair the caller already asks', () => {
  // The shipped pair of Claude Code is codex; of Codex, claude. A codex row ticked consultant is Claude Code's own pair.
  const codexTicked = [{ ...DEFAULT_VENDORS[0]!, uses: ['consultant'] as Vendor['uses'] }, DEFAULT_VENDORS[1]!];

  assert.equal(saysUnpicked(pickOn(DEFAULT_CONSULT, codexTicked, 'claude')), false,
    'Claude Code is told its own shipped pair is a ticked model it is not asking');
  assert.equal(saysUnpicked(pickOn(DEFAULT_CONSULT, codexTicked, 'codex')), true,
    'Codex, whose pair is claude, is not told about the ticked codex row — the fixture does not reach the line');
});

test('no such word once the caller has picked a row', () => {
  const picked = consultWith({ claude: reference('deep-high') }, ROWS);

  assert.equal(saysUnpicked(pickOn(picked, ROWS, 'claude')), false, 'a caller that picked a row is still told it asks the shipped pair');
  assert.equal(saysUnpicked(pickOn(picked, ROWS, 'codex')), true,
    'a neighbour still on its pair is not told — the fixture does not reach the line');
});

test('the Question consultant tab never draws the line — it has no shipped pair', () => {
  // The same catalog that makes the Consultant tab say it, every row ticked for the question consultant too.
  const both = ROWS.map((one) => (one.uses ?? []).includes('consultant') ? { ...one, uses: [...one.uses!, 'qconsult'] as Vendor['uses'] } : one);
  const state: PanelState = { ...panelState('consultant'), catalogRows: both };

  assert.equal(saysUnpicked(pickOn(DEFAULT_CONSULT, both, 'claude')), true, 'the fixture does not make the Consultant tab say it');
  assert.ok(!paneRun(state, 'consultants/qconsult').text().includes('still asks the shipped pair'),
    'the Question consultant tab says a caller still asks the shipped pair');
});
