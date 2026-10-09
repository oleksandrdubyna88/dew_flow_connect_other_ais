import assert from 'node:assert/strict';
import { test } from 'node:test';
import { modelsTabHtml } from '../modelsTab';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';

/**
 * The Models tab draws EVERY catalog row (research/PLAN_one_model_catalog.md E4): a row that exists for its catalog uses
 * alone — the consultant rows epic 1's migration made, a chat row — is a model the person added and must be able to
 * edit. The panel's `vendors` holds the current page's reviewers only, so the new page reads `catalogRows`.
 */

test('a row that reviews nothing — a migrated consultant — has its card on Models', () => {
  const consultant: Vendor = { ...DEFAULT_VENDORS[0]!, id: 'consult-claude', plan: false, code: false, uses: ['consultant'] };
  const state = { ...panelState('reviewers'), vendors: DEFAULT_VENDORS, catalogRows: [...DEFAULT_VENDORS, consultant] };

  assert.match(modelsTabHtml(state), /data-model-card="consult-claude"/);
});
