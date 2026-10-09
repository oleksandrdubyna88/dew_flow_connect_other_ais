import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import type { QuestionRowSetting } from '../qconsultSettings';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { lastWrite, panelState, runPanel } from './panelPageHarness';

/**
 * Found while building E4.2 of research/PLAN_one_model_catalog.md: epic 1's migration turns a question row's own definition
 * into a reference to a catalog row (`ask-<id>`) that reviews nothing — and the current page resolved question rows
 * against the panel's `vendors`, which since E1.4 holds the REVIEWERS only. So a migrated row read as having no runtime:
 * its switch was disabled ("'' is not a runtime the question consultant can launch") and its vendor "not in the
 * catalogue". A row is resolved against every catalog row, as its writes already were. Asked of the Settings page's
 * Question consultant place since E5.1 step 5 removed the page this was found on.
 */

const ask: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'ask-q-1', runtime: 'codex', plan: false, code: false, uses: ['qconsult'] };
const migrated: QuestionRowSetting = {
  id: 'q-1', vendor: 'ask-q-1', runtime: '', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'question-opinion', enabled: false,
};

test('a migrated question row can still be switched on from the Settings page', () => {
  const state = {
    ...panelState('questionconsultant', { settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, rows: [migrated] } } }),
    vendors: DEFAULT_VENDORS,
    catalogRows: [...DEFAULT_VENDORS, ask],
  };
  const page = runPanel(state, { html: catalogHtml(state, 'test-nonce', 'consultants/qconsult') });
  const toggle = page.controls.find((one) => one.dataset['setting'] === 'qconsultRowEnabled' && one.dataset['caller'] === 'q-1');
  assert.ok(toggle !== undefined, 'the row has no switch');
  assert.equal(toggle.disabled, false, 'the switch is refused');

  toggle.checked = true;
  toggle.fire('change');

  const { key, value, caller } = lastWrite(page);
  assert.deepEqual({ key, value, caller }, { key: 'qconsultRowEnabled', value: true, caller: 'q-1' });
});
