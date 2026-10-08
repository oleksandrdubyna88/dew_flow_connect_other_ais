import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { EndpointListing } from '../endpointModels';
import { type PanelState } from '../panelView';
import { SEARCH_FROM_OPTIONS } from '../selectSearch';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { type Control, PageEvent, type Page, panelState, runPanel, withoutSeq } from './panelPageHarness';

/**
 * A long model list's search box, RUN — the panel's own script over the card the panel really renders
 * (research/PLAN_model_search_and_busy_marks.md, Epic 2).
 *
 * <p>Reported on 2026-10-02: ≡ on an OpenRouter card filled its dropdown with two hundred `vendor/model` ids in the
 * endpoint's own order, and the only way to find one was to scroll. Every assertion here answers "what would I see if
 * the behaviour were deleted": no box, a box on a short list, a non-match still in the list, a sentinel moved, Enter
 * saving the wrong thing or something when nothing matched, a query gone after a repaint.</p>
 */

const OPENROUTER = 'https://openrouter.ai/api/v1';

/** OpenRouter's shape, in an order that is not alphabetical — the order the endpoint answers in. */
const LISTED = [
  'apodex/apodex-1.1-mini:free', 'openai/gpt-6.1-sol', 'anthropic/claude-sonnet-5.5', 'anthropic/claude-sonnet-5.5:batch',
  'z-ai/glm-5.3-prime', 'qwen/qwen3.8-max-prime', 'anthropic/claude-opus-5.5', 'anthropic/claude-opus-5.5:batch',
  'openai/gpt-6-luna', 'openai/gpt-6-sol', 'x-ai/grok-4.7', 'xiaomi/mimo-v2.6-pro', 'cohere/command-a-plus',
  'fireworks/ember-1', 'upstage/solar-mini4', 'opus-community/opus-7b', 'aion-labs/aion-3.5', 'deepseek/deepseek-flash',
];

function openrouter(overrides: Partial<Vendor> = {}): Vendor {
  const codex = DEFAULT_VENDORS.find((one) => one.runtime === 'codex');
  assert.ok(codex !== undefined, 'the shipped vendors carry a codex row');

  return { ...codex, id: 'openrouter', baseUrl: OPENROUTER, model: '', enabled: true, ...overrides };
}

function listing(ids: readonly string[], overrides: Partial<EndpointListing> = {}): EndpointListing {
  return { baseUrl: OPENROUTER, keyName: 'openrouter', ids: [...ids], reason: '', askedUtc: '2026-10-02T08:34:00.000Z', ...overrides };
}

/** The reviewers section with one OpenRouter card whose endpoint listed `ids`. */
function card(ids: readonly string[] = LISTED, vendor: Vendor = openrouter(), overrides: Partial<PanelState> = {}, saved?: unknown): Page {
  // The answer kept for THIS row — asked with its own base URL and key name, or `listingFor` rightly ignores it.
  const answer = listing(ids, { baseUrl: vendor.baseUrl, keyName: vendor.vaultKeyName ?? vendor.id });
  const state = panelState('reviewers', { vendors: [vendor], endpointListings: { [vendor.id]: answer }, ...overrides });

  return runPanel(state, saved === undefined ? {} : { saved });
}

function modelSelect(page: Page, vendor = 'openrouter'): Control {
  const select = page.controls.find((one) => one.dataset['setting'] === 'model' && one.dataset['vendor'] === vendor);
  assert.ok(select !== undefined, `the page has no model dropdown for ${vendor}`);

  return select;
}

/** The search box the page put before this select — exactly one, or the test fails saying how many. */
function boxFor(page: Page, select: Control): Control {
  const boxes = page.inserted().filter((one) => one.before === select).map((one) => one.node);
  assert.equal(boxes.length, 1, `one search box before the select, found ${boxes.length}`);

  return boxes[0]!;
}

function values(select: Control): readonly string[] {
  return select.options.map((option) => option.value);
}

function type(box: Control, query: string): void {
  box.value = query;
  box.fire('input');
}

function press(box: Control, key: string): PageEvent {
  return box.fire('keydown', new PageEvent('keydown', key));
}

test('a list of fifteen or more options gets a search box before it; one of fourteen does not', () => {
  // The two sentinels count: the empty choice and "another model…" are options of the select like any other.
  const fifteen = card(LISTED.slice(0, SEARCH_FROM_OPTIONS - 2));
  const fourteen = card(LISTED.slice(0, SEARCH_FROM_OPTIONS - 3));

  assert.equal(modelSelect(fifteen).options.length, SEARCH_FROM_OPTIONS);
  const box = boxFor(fifteen, modelSelect(fifteen));
  assert.equal(box.type, 'search');
  assert.match(box.getAttribute('aria-label') ?? '', /Search the 13 choices/u, 'it says what it searches, sentinels not counted');
  assert.equal(modelSelect(fourteen).options.length, SEARCH_FROM_OPTIONS - 1);
  assert.equal(fourteen.inserted().length, 0, 'a short list is scanned by eye');
});

test('typing ranks the list and takes the non-matches out of it; the sentinels stay where they were', () => {
  const page = card();
  const select = modelSelect(page);
  type(boxFor(page, select), 'opus');

  assert.deepEqual(values(select), [
    '', 'opus-community/opus-7b', 'anthropic/claude-opus-5.5', 'anthropic/claude-opus-5.5:batch', '__other__',
  ], 'the empty choice first, a prefix match before a segment match, "another model…" last');
});

test('the chosen model stays in the list while it does not match, and stays chosen', () => {
  const page = card(LISTED, openrouter({ model: 'openai/gpt-6-luna' }));
  const select = modelSelect(page);
  assert.equal(select.value, 'openai/gpt-6-luna', 'the fixture is drawn on its saved model');
  type(boxFor(page, select), 'opus');

  assert.deepEqual(values(select).slice(0, 2), ['', 'openai/gpt-6-luna'], 'kept, right after the leading sentinel');
  assert.equal(select.value, 'openai/gpt-6-luna', 'moving options never changes what is chosen');
});

test('Enter commits the first MATCH through the select’s own change, exactly as a mouse pick does', () => {
  const searched = card(LISTED, openrouter({ model: 'openai/gpt-6-luna' }));
  const select = modelSelect(searched);
  const box = boxFor(searched, select);
  type(box, 'claude opus');
  const before = searched.posted.length;
  const event = press(box, 'Enter');

  assert.ok(event.defaultPrevented, 'the key does not also submit anything');
  const posted = searched.posted.slice(before);
  // The mouse pick on a fresh page of the same card, for the message a person picking it would send.
  const picked = card(LISTED, openrouter({ model: 'openai/gpt-6-luna' }));
  const pickedSelect = modelSelect(picked);
  pickedSelect.value = 'anthropic/claude-opus-5.5';
  const pickedFrom = picked.posted.length;
  pickedSelect.fire('change');

  // Two documents, so their numbering is their own: the write is compared without it (`withoutSeq`, which checks it).
  const asSent = (list: readonly Record<string, unknown>[]): readonly Record<string, unknown>[] =>
    list.map((one) => (typeof one['seq'] === 'number' ? withoutSeq(one) : one));
  assert.deepEqual(asSent(posted), asSent(picked.posted.slice(pickedFrom)),
    'the same setting write AND the same focus release — the select’s change handler ran, not a shortcut around it');
  assert.equal(posted[0]?.['value'], 'anthropic/claude-opus-5.5', 'never the preserved, non-matching chosen model');
});

test('Enter with nothing matching does nothing: no write, no throw', () => {
  const page = card();
  const box = boxFor(page, modelSelect(page));
  type(box, 'no-such-model');
  const before = page.posted.length;
  press(box, 'Enter');

  assert.deepEqual(page.posted.slice(before), []);
  assert.deepEqual(values(modelSelect(page)), ['', '__other__'], 'and the list holds only what is always there');
});

test('Enter that confirms an input method’s candidate is not a pick', () => {
  // CodeRabbit on #637: with CJK input, the Enter that ends a composition chose the first match and wrote it.
  const page = card();
  const box = boxFor(page, modelSelect(page));
  type(box, 'opus');
  const before = page.posted.length;
  box.fire('keydown', new PageEvent('keydown', 'Enter', null, true));

  assert.deepEqual(page.posted.slice(before).filter((one) => one['type'] === 'setting'), [], 'nothing was chosen yet');
  press(box, 'Enter');
  assert.equal(page.posted.filter((one) => one['type'] === 'setting').length, 1, 'the Enter after the composition still picks');
});

test('Enter on a blank box does not pick the first model', () => {
  const page = card();
  const box = boxFor(page, modelSelect(page));
  const before = page.posted.length;
  press(box, 'Enter');

  assert.deepEqual(page.posted.slice(before), []);
});

test('a keystroke that leaves the result as it was moves no option at all', () => {
  // E2 code round (local, UX/performance): rebuilding the list on every keystroke churns the DOM for nothing when the
  // answer did not change — typing "opus-c" then "opus-co" leaves exactly one match either way.
  const page = card();
  const select = modelSelect(page);
  const box = boxFor(page, select);
  type(box, 'opus-c');
  const moves: string[] = [];
  const append = select.appendChild.bind(select);
  const remove = select.removeChild.bind(select);
  select.appendChild = (option) => { moves.push(`append ${option.value}`); return append(option); };
  select.removeChild = (option) => { moves.push(`remove ${option.value}`); return remove(option); };

  type(box, 'opus-co');
  assert.deepEqual(moves, [], 'the same answer, so nothing is moved');
  type(box, 'opus');
  assert.ok(moves.length > 0, 'and a different answer still rebuilds the list');
  assert.deepEqual(values(select).slice(0, 2), ['', 'opus-community/opus-7b']);
});

test('Escape empties the box and puts the list back in its own order; ArrowDown moves to the select', () => {
  const page = card();
  const select = modelSelect(page);
  const original = values(select);
  const box = boxFor(page, select);
  type(box, 'gpt');
  assert.notDeepEqual(values(select), original, 'the query took effect');

  press(box, 'Escape');
  assert.equal(box.value, '');
  assert.deepEqual(values(select), original, 'every option back, in the order the page drew them');

  press(box, 'ArrowDown');
  assert.ok(select.focused);
});

test('typing in the box holds the repaint under its own identity, and tabbing to its select is no release', () => {
  const page = card();
  const select = modelSelect(page);
  const box = boxFor(page, select);
  type(box, 'luna');

  const holds = page.posted.filter((one) => one['type'] === 'focus');
  assert.ok(holds.length > 0, 'typing reports a focus hold');
  assert.equal(holds.at(-1)?.['editing'], true);
  assert.match(String(holds.at(-1)?.['id']), /^search\|model\|openrouter\|/u, 'under the box’s identity, not the select’s');

  const before = page.posted.length;
  box.fire('focusout', new PageEvent('focusout', '', select));
  assert.deepEqual(page.posted.slice(before), [], 'moving from the box to a control does not release the hold');
  box.fire('focusout', new PageEvent('focusout', '', null));
  assert.equal(page.posted.at(-1)?.['editing'], false, 'leaving for nowhere does');
});

test('a query survives the document being replaced — the next page of the same webview applies it again', () => {
  const first = card();
  type(boxFor(first, modelSelect(first)), 'opus');

  const second = card(LISTED, openrouter(), {}, first.saved());
  const select = modelSelect(second);
  const box = boxFor(second, select);

  assert.equal(box.value, 'opus');
  assert.deepEqual(values(select), [
    '', 'opus-community/opus-7b', 'anthropic/claude-opus-5.5', 'anthropic/claude-opus-5.5:batch', '__other__',
  ]);
});

test('a held search box gets its caret back after a repaint', () => {
  const first = card();
  const box = boxFor(first, modelSelect(first));
  type(box, 'opus');
  const id = String(first.posted.filter((one) => one['type'] === 'focus').at(-1)?.['id']);

  const second = runPanel(
    panelState('reviewers', { vendors: [openrouter()], endpointListings: { openrouter: listing(LISTED) } }, { id, start: 2, end: 2 }),
    { saved: first.saved() },
  );
  const again = boxFor(second, modelSelect(second));

  assert.ok(again.focused, 'the caret is back in the box');
  assert.deepEqual(again.selection, [2, 2]);
});

/** The identity a box keys its query under, read from what the page itself reported — never typed here. */
function keyOf(page: Page): string {
  const hold = page.posted.filter((one) => one['type'] === 'focus' && String(one['id']).startsWith('search|')).at(-1);
  assert.ok(hold !== undefined, 'the box reported no focus hold to read its identity from');

  return String(hold['id']).slice('search|'.length);
}

test('a stored query whose list no longer has a box is dropped', () => {
  const first = card();
  type(boxFor(first, modelSelect(first)), 'opus');
  const key = keyOf(first);
  const page = card(LISTED.slice(0, 3), openrouter(), {}, { search: { [key]: 'opus', other: 'x' } });

  assert.equal(page.inserted().length, 0);
  assert.deepEqual((page.saved() as { search: Record<string, string> }).search, {});
});

test('Escape’s empty box is what the next document starts with too', () => {
  const first = card();
  const box = boxFor(first, modelSelect(first));
  type(box, 'opus');
  press(box, 'Escape');
  assert.deepEqual((first.saved() as { search: Record<string, string> }).search, {}, 'the cleared query is not kept');

  const second = card(LISTED, openrouter(), {}, first.saved());
  assert.equal(boxFor(second, modelSelect(second)).value, '');
  assert.equal(modelSelect(second).options.length, LISTED.length + 2, 'and the whole list is back');
});

test('moving from a select to its own box is no release either', () => {
  const page = card();
  const select = modelSelect(page);
  const box = boxFor(page, select);
  const before = page.posted.length;
  select.fire('focusout', new PageEvent('focusout', '', box));

  assert.deepEqual(page.posted.slice(before).filter((one) => one['type'] === 'focus' && one['editing'] === false), []);
});

test('after Enter the next document puts the caret back in the box, so typing can go on', () => {
  // Own review of E2: Enter commits through the select's change, which releases the hold as a mouse pick does, so the
  // repaint the write causes carried no focus and a keyboard user had to find the box again after every pick.
  const first = card();
  const box = boxFor(first, modelSelect(first));
  type(box, 'opus');
  press(box, 'Enter');

  const second = card(LISTED, openrouter(), {}, first.saved());
  const again = boxFor(second, modelSelect(second));
  assert.ok(again.focused, 'the caret is back in the box');
  assert.deepEqual(again.selection, [4, 4], 'at the end of what is in it');
  assert.equal((second.saved() as { searchFocus?: unknown }).searchFocus, undefined, 'and the note is spent');
});

test('an old note to return to a box is ignored, so a later repaint never steals the caret', () => {
  const first = card();
  type(boxFor(first, modelSelect(first)), 'opus');
  const key = keyOf(first);
  const page = card(LISTED, openrouter(), {}, { search: { [key]: 'opus' }, searchFocus: { key, at: 0 } });

  assert.equal(boxFor(page, modelSelect(page)).focused, false);
  assert.equal((page.saved() as { searchFocus?: unknown }).searchFocus, undefined);
});
