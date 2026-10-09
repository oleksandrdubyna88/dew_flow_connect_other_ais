import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The Models cards are priced from EVERY catalog row, each row by its own id (todo/PLAN_models_card_prices_every_row.md).
 *
 * <p>Was (PLAN_one_model_catalog.md E1.4; PR #681's review): the page priced only the rows it showed as reviewers,
 * because the map was keyed by MODEL and an `api` row's routed price was spread last — priced from every row, a hidden
 * `api` consultant with a reviewer's model put ITS endpoint's rate on the reviewer's card. Now the map is keyed by row
 * (`priceBook.cardPrices`), so pricing every row cannot move a price between cards, and a row that reviews nothing gets
 * its own price. `aModelsCardPricesItsOwnRow.test.ts` runs the page for the behaviour; this pins the render's wiring,
 * which is the extension host's and is not run by a unit test — as `thePanelReadsThisSide` pins its reads.</p>
 */

const source = (file: string): string => fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

test('the render prices the cards from every row, through the price service', () => {
  const panel = source('panelProvider.ts');

  assert.match(panel, /cardPrices: await this\.cardPrices\(vendors\),/u, 'the cards are not priced from every catalog row');
  assert.doesNotMatch(panel, /cardPrices: await this\.cardPrices\(shown\),/u,
    'priced from the reviewers only, a consultant-only row shows a dash where the list prices it');
  assert.match(panel, /return cardPrices\(rows, \(model, baseUrl\) => PRICE_BOOK\.priceOf\(model, baseUrl\)\);/u,
    'the cards are not priced by the window\'s one price book');
});

test('a card reads its own row\'s price, never its model\'s', () => {
  const view = source('panelView.ts');

  assert.match(view, /price: state\.cardPrices\[v\.id\],/u);
  assert.doesNotMatch(view, /state\.cardPrices\[v\.model\]/u,
    'read by model, two rows on one model and two endpoints share one rate again');
});
