import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The old Settings page prices the reviewers it SHOWS (PLAN_one_model_catalog.md E1.4; PR #681's review).
 *
 * <p>The page hides a row that exists for its catalog uses alone (`shownOnTheOldPage`), but built its price map from
 * every row. The map is keyed by model and an `api` row's routed price is spread last, so a hidden consultant on an
 * `api` endpoint, with the same model as a visible reviewer, put ITS endpoint's rate on the reviewer's card. The
 * spending and consultation tabs keep every row on purpose: a consultant's runs are billed too.</p>
 *
 * <p>A structural pin, as `thePanelReadsThisSide` is: the render is the extension host's, and what is checked is that
 * the rows drawn and the rows priced are one list.</p>
 */

const panel = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8');

test('the page draws and prices one list of rows — the ones it shows', () => {
  assert.match(panel, /const shown = vendors\.filter\(shownOnTheOldPage\);/u, 'the shown rows are named once');
  assert.match(panel, /\n\s+vendors: shown,/u, 'the page draws the shown rows');
  assert.match(panel, /modelPrices: await this\.modelPrices\(shown\),/u, 'and prices exactly those');
  assert.doesNotMatch(panel, /modelPrices: await this\.modelPrices\(vendors\),/u,
    'priced from every row, a hidden api consultant overwrote a visible reviewer\'s price for the same model');
});
