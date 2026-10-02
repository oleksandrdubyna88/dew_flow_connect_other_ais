import assert from 'node:assert/strict';
import { test } from 'node:test';

import { click, controlFrom, createdElement, PageEvent, PageOption, panelState, runPanel } from './panelPageHarness';

/** A select carrying these option values, built the way the harness builds one from a page. */
function selectOf(...values: string[]): ReturnType<typeof controlFrom> {
  const select = controlFrom('select', ' data-setting="model"');
  for (const value of values) {
    select.appendChild(new PageOption(value, value));
  }

  return select;
}

test('a select refuses a value none of its options carries, as a DOM does — it selects nothing', () => {
  // Widened for the list search box (todo/PLAN_model_search_and_busy_marks.md, Epic 2). Before, any string was
  // accepted, so a test could "pick" a model the page never offered and pass.
  const select = selectOf('', 'a', 'b');
  select.value = 'b';
  assert.equal(select.value, 'b');
  select.value = 'not-offered';
  assert.equal(select.value, '');
});

test('appendChild MOVES an option — taken out of where it was, never copied — and removeChild refuses a stranger', () => {
  const select = selectOf('a', 'b', 'c');
  const [first] = select.options;
  select.appendChild(first!);
  assert.deepEqual(select.options.map((one) => one.value), ['b', 'c', 'a'], 'moved to the end, still three');

  const other = selectOf('x');
  other.appendChild(first!);
  assert.deepEqual(select.options.map((one) => one.value), ['b', 'c'], 'and gone from the list it left');
  assert.equal(first!.parentNode, other);

  select.removeChild(select.options[0]!);
  assert.throws(() => select.removeChild(new PageOption('z', 'z')), /NotFoundError/u);
});

test('removing the CHOSEN option moves the choice to the first one left, as a DOM’s selectedness does', () => {
  const select = selectOf('a', 'b', 'c');
  select.value = 'b';
  select.removeChild(select.options[1]!);
  assert.equal(select.value, 'a');

  select.removeChild(select.options[1]!);
  assert.equal(select.value, 'a', 'removing another option leaves the choice alone');
});

test('a dispatched event runs the control’s own listeners with that event, and says whether it was stopped', () => {
  const select = selectOf('a');
  const seen: string[] = [];
  select.addEventListener('change', (event) => { seen.push(event.type); });
  select.addEventListener('keydown', (event) => { event.preventDefault(); });

  assert.equal(select.dispatchEvent(new PageEvent('change')), true);
  assert.deepEqual(seen, ['change']);
  assert.equal(select.dispatchEvent(new PageEvent('keydown', 'Enter')), false);
});

test('the fake document creates only the tags it models, and refuses any other loudly', () => {
  assert.equal(createdElement('input').tagName, 'INPUT');
  assert.throws(() => createdElement('div'), /does not model/u);
});

test('a control outside a running page has nowhere to insert anything', () => {
  assert.throws(() => controlFrom('select', ' data-setting="model"').parentNode.insertBefore(createdElement('input'), selectOf()),
    /not in a running page/u);
});

test('a prompt picker answers [data-prompt] and select, and never [data-setting]', () => {
  const page = runPanel(panelState('prompts'));

  assert.ok(page.prompts.length > 0, 'the prompts section draws prompt pickers');
  assert.ok(page.prompts.every((one) => one.tagName === 'SELECT' && one.dataset['prompt'] !== undefined));
  assert.ok(page.controls.every((one) => one.dataset['prompt'] === undefined), 'a setting query must not answer a prompt picker');
});

test('the webview state a run starts from is what getState answers, and what the page saves is kept as a copy', () => {
  const page = runPanel(panelState('reviewers'), { saved: { search: { kept: 'opus' } } });

  // The page writes its search state back on load; with no list long enough for a box, the stored query is dropped.
  assert.deepEqual(page.saved(), { search: {} });
});

/**
 * The panel harness is code under test (`generated-code-tests.md` §3): a fake more permissive — or,
 * as this one was, blind — where a real DOM is not, turns a suite green for the wrong reason.
 */

test('a hyphenated data attribute reaches dataset under the name a DOM gives it', () => {
  // The first copy of this harness read only single-word names, so `data-command-model` was simply
  // absent and a control routed on it could never have been written in a test.
  const control = controlFrom('select', ' id="x" data-setting="strongest" data-command-model="codex"');

  assert.deepEqual(control.dataset, { setting: 'strongest', commandModel: 'codex' });
});

test('a single-word data attribute and the value are read as before', () => {
  const control = controlFrom('input', ' type="text" data-setting="consultBaseUrl" data-caller="gemini" value="https://a"');

  assert.deepEqual(control.dataset, { setting: 'consultBaseUrl', caller: 'gemini' });
  assert.equal(control.value, 'https://a');
  assert.equal(control.type, 'text');
  assert.equal(control.tagName, 'INPUT');
});

test('a checkbox starts as the page rendered it — and a data attribute naming "checked" does not tick it', () => {
  // A DOM reads the boolean `checked` attribute; it does not read a substring of some other attribute.
  // The harness must not be more permissive than that, or a box the page drew unticked reads as ticked.
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex" checked').checked, true);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex" checked disabled').checked, true);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="feature" data-vendor="codex"').checked, false);
  assert.equal(controlFrom('input', ' type="checkbox" data-setting="checked" data-vendor="codex"').checked, false);
});

test('a dropdown carries the options the page drew, value and text, in order — and a text box carries none', () => {
  const page = runPanel(panelState('reviewers'));
  const model = page.controls.find((one) => one.tagName === 'SELECT' && one.dataset['setting'] === 'model');
  const path = page.controls.find((one) => one.dataset['setting'] === 'executablePath');

  assert.ok(model !== undefined && model.options.length > 0, 'the model dropdown came back with no options');
  assert.ok(model.options.some((one) => one.value === model.value), 'the selected value is not among the options read');
  assert.deepEqual(path?.options, []);
});

test('a data-command button is bound by the page and a click posts exactly its command and id', () => {
  const page = runPanel(panelState('reviewers'));

  assert.ok(page.commands.some((one) => one.dataset['command'] === 'removeVendor'), 'no command button was read off the page');
  click(page, 'removeVendor', 'codex');

  assert.deepEqual(page.posted.at(-1), { type: 'command', command: 'removeVendor', id: 'codex' });
  assert.throws(() => click(page, 'removeVendor', 'nobody'), /no removeVendor button for nobody/u);
});

test('a box carries the placeholder the page drew, and a data attribute naming it is not one', () => {
  assert.equal(controlFrom('input', ' type="number" data-setting="reviewMinutes" placeholder="20 — calibrated default"').placeholder, '20 — calibrated default');
  assert.equal(controlFrom('input', ' type="number" data-setting="reviewMinutes" data-placeholder="x"').placeholder, '');
});
