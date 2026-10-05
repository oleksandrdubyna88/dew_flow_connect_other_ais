import assert from 'node:assert/strict';
import { test } from 'node:test';
import { settingsHtml } from '../panelView';
import type { QuestionRowSetting } from '../qconsultSettings';
import { DEFAULTS } from '../settingsShape';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';

/**
 * Found while building E4.2 of todo/PLAN_one_model_catalog.md: epic 1's migration turns a question row's own definition
 * into a reference to a catalog row (`ask-<id>`) that reviews nothing — and the current page resolved question rows
 * against the panel's `vendors`, which since E1.4 holds the REVIEWERS only. So a migrated row read as having no runtime:
 * its switch was disabled ("'' is not a runtime the question consultant can launch") and its vendor "not in the
 * catalogue". A row is resolved against every catalog row, as its writes already were. (The current page still labels such a
 * row "not in the catalogue" — its picker lists the vendor presets, not the catalog; E5 retires that page.)
 */

const ask: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'ask-q-1', runtime: 'codex', plan: false, code: false, uses: ['qconsult'] };
const migrated: QuestionRowSetting = {
  id: 'q-1', vendor: 'ask-q-1', runtime: '', model: '', baseUrl: '', executablePath: '', key: '', prompt: 'question-opinion', enabled: false,
};

test('a migrated question row can still be switched on from the current page', () => {
  const state = {
    ...panelState('questionconsultant', { settings: { ...DEFAULTS, qconsult: { ...DEFAULTS.qconsult, rows: [migrated] } } }),
    vendors: DEFAULT_VENDORS,
    catalogRows: [...DEFAULT_VENDORS, ask],
  };
  const html = settingsHtml(state, 'test-nonce', 'questionconsultant');
  const toggle = /<input type="checkbox" id="qconsultRowEnabled-q-1"[^>]*>/.exec(html)?.[0] ?? '';

  assert.ok(toggle.length > 0, 'the row has no switch');
  assert.doesNotMatch(toggle, /disabled/, `the switch is refused: ${toggle}`);
});
