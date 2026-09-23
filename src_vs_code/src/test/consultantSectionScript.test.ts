import assert from 'node:assert/strict';
import { test } from 'node:test';

import { CUSTOM_ENDPOINT } from '../consultSettings';
import { type PanelFocus } from '../panelView';
import { type Control, type Page, lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * The Consultant section's controls, RUN — the page's own script over the page's own markup.
 *
 * <p><b>Why this file exists rather than four more substring assertions.</b> A section is assembled
 * as a template literal, so a scan of that text sees everything it was supposed to contain: a box
 * wired to the wrong caller, an attribute the write path routes on that never reaches the control,
 * and a caret restored into somebody else's row all read as PRESENT. `.agents/PROJECT.md` refuses a
 * new behavioural assertion over page source text for exactly that reason, and this is the shape it
 * names — `panelPhrasesScript.test.ts`, whose own first tests stayed green over an unterminated
 * character class that made every control in the sidebar dead.</p>
 *
 * <p>The controls under test are NOT fixtures. They are parsed out of the page the section really
 * renders, so deleting `data-caller` from the endpoint box, or the box itself, is a test that goes
 * red rather than a string that still matches. Ask of each assertion what it would SEE if the
 * behaviour were deleted; every one below answers "nothing at this caller".</p>
 */

/** Render the panel with the Consultant section open, and run its own script over it. */
function run(focus?: PanelFocus): Page {
  return runPanel(panelState('consultant', {}, focus));
}

/** The one control of that setting in that caller's row — and a failure that names the row. */
function control(page: Page, setting: string, caller: string): Control {
  const found = page.controls.filter((one) => one.dataset['setting'] === setting && one.dataset['caller'] === caller);
  assert.equal(found.length, 1,
    `the ${caller} row has no ${setting} control of its own — ${found.length} matched`);

  return found[0]!;
}

test('the vendor a caller is pointed at is written for THAT caller', () => {
  const page = run();
  const picker = control(page, 'consultVendor', 'gemini');

  picker.value = 'claude';
  picker.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultVendor', value: 'claude', vendor: undefined, role: undefined, caller: 'gemini',
  }, 'four rows share one setting name, so a write with no caller lands in whichever row the document holds first');
});

test('the consultant’s OWN endpoint is written for the caller whose row holds it', () => {
  const page = run();
  const endpoint = control(page, 'consultBaseUrl', 'gemini');

  endpoint.value = 'https://api.deepseek.com/v1';
  endpoint.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultBaseUrl', value: 'https://api.deepseek.com/v1',
    vendor: undefined, role: undefined, caller: 'gemini',
  }, 'the write path has accepted consultBaseUrl since story A2 and nothing in the section could send it');
});

test('the consultant’s OWN CLI path is written for the caller whose row holds it', () => {
  const page = run();
  const path = control(page, 'consultExecutablePath', 'codex');

  path.value = 'D:/tools/claude.cmd';
  path.fire('change');

  assert.deepEqual(lastWrite(page), {
    type: 'setting', key: 'consultExecutablePath', value: 'D:/tools/claude.cmd',
    vendor: undefined, role: undefined, caller: 'codex',
  }, 'a consultant that looks up its CLI on PATH cannot be pointed at one until this box exists');
});

test('every caller kind has its own controls, and each writes for the caller whose row holds it', () => {
  // Counted over a page that has been RUN, rather than over its source text. The source cannot tell
  // a control wired to the right caller from one wired to the wrong one — every `data-caller` it
  // needs is present either way — so this asserts both halves at once: that each caller has exactly
  // one of each control, and that changing it writes for that caller. (codex, C6's code round; the
  // count used to be a regular expression over the markup.)
  const page = run();

  for (const caller of ['claude', 'codex', 'gemini', 'other']) {
    for (const setting of ['consultVendor', 'consultModel']) {
      const one = control(page, setting, caller);
      one.value = setting === 'consultVendor' ? 'claude' : 'haiku';
      one.fire('change');

      assert.equal(lastWrite(page)['caller'], caller, `${setting} in the ${caller} row wrote for somebody else`);
      assert.equal(lastWrite(page)['key'], setting);
    }
  }
});

test('choosing the custom endpoint asks the HOST, for THAT caller, and writes no setting at all', () => {
  // An id keys the vault entry and the usage ledger, so the one thing that must not happen is the
  // sentinel reaching disk: `{ vendor: '' }` is an entry a person can see in their settings file and
  // cannot choose in the panel. The picker is a `<select>`, so the only way to ask for something
  // that is not a value is to post a COMMAND — the shape `__other__` already uses for a model.
  const page = run();
  const picker = control(page, 'consultVendor', 'codex');
  const before = picker.value;

  picker.value = CUSTOM_ENDPOINT;
  picker.fire('change');

  assert.deepEqual(page.posted.filter((one) => one['type'] === 'setting'), [],
    'the sentinel must never be stored — it is a request for a name, not a vendor');
  assert.deepEqual(page.posted.filter((one) => one['type'] === 'command'),
    [{ type: 'command', command: 'customConsultant', id: 'codex' }],
    'four rows share this control, so a command with no caller configures whichever the document holds first');
  assert.equal(picker.value, before,
    'and the select goes back: nothing is written, so no repaint comes, and it would sit on an option that is not a vendor');
});

test('a repaint puts the caret back in the row that was being edited, not the first row sharing its name', () => {
  // The shipped map points three callers at a codex consultant, so three rows carry an endpoint box
  // under one setting name. Without the caller in the focus id the script refocuses the first of
  // them — silently, mid-edit, which is the failure `FOCUS_ID`'s fourth segment exists to prevent.
  const page = run({ id: 'consultBaseUrl||\u007Cother', start: 2, end: 2 });

  assert.deepEqual(
    page.controls.filter((one) => one.focused).map((one) => one.dataset['caller']),
    ['other'],
    'the caret came back into somebody else’s consultant row',
  );
});
