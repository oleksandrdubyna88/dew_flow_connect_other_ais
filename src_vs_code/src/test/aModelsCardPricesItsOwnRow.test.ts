import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ModelPrice } from '../modelPrices';
import { catalogHtml } from '../catalogPage';
import { cardPrices, PriceOf } from '../priceBook';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { panelState } from './panelPageHarness';
import { pageTree, selectorsOf } from './pageTree';
import { runPageHtml } from './pageScriptHarness';

/**
 * Every Models card shows the catalog price of ITS OWN row (research/PLAN_models_card_prices_every_row.md).
 *
 * <p>Found during catalog E5.3, 2026-10-09: the map the cards read was built from the reviewers only and keyed by model,
 * so a consultant-only row on a model no reviewer uses showed a dash where the published list prices it, and an `api`
 * consultant on a reviewer's model showed the reviewer's rate instead of its own endpoint's. The Settings page is drawn
 * and RUN on Models as the panel serves it, with the price map the panel builds (`cardPrices`), and the rate boxes are
 * read off the running page (CodeRabbit, PR #723).</p>
 */

const LIST: ModelPrice = { inPerMillion: 1.25, outPerMillion: 10, source: 'openrouter' };
const ROUTED: ModelPrice = { inPerMillion: 3, outPerMillion: 15, source: 'openrouter' };
const GLM: ModelPrice = { inPerMillion: 0.6, outPerMillion: 2.2, source: 'litellm' };
const ENDPOINT = 'https://api.example.test/v1';

/** The published lists as the book answers them: a model on a route. */
const LISTED: Partial<Record<string, ModelPrice>> = { 'gpt-5.6|': LIST, [`gpt-5.6|${ENDPOINT}`]: ROUTED, 'glm-5.3|': GLM };
const priceOf: PriceOf = (model, baseUrl = '') => LISTED[`${model}|${baseUrl}`];

const BASE = DEFAULT_VENDORS[0]!;
const REVIEWER: Vendor = { ...BASE, id: 'codex', runtime: 'codex', model: 'gpt-5.6', baseUrl: '', plan: true, code: true };
const API_CONSULTANT: Vendor = {
  ...BASE, id: 'consult-api', runtime: 'api', model: 'gpt-5.6', baseUrl: ENDPOINT, plan: false, code: false, uses: ['consultant'],
};
const CONSULT_ONLY: Vendor = {
  ...BASE, id: 'consult-glm', runtime: 'claude', model: 'glm-5.3', baseUrl: '', plan: false, code: false, uses: ['consultant'],
};

/** What the page's script binds at load — the same selectors the other page-running tests hand it. */
const AT_LOAD = ['[data-setting]', '[data-prompt]', '[data-command]', '[data-tab]', '[data-pane]'];

/** The Settings page opened on Models for these rows, running; the placeholder of one card's input rate. */
function listedRate(rows: readonly Vendor[], id: string): string {
  const state = { ...panelState('reviewers'), vendors: rows.filter((one) => one.plan || one.code), catalogRows: [...rows], cardPrices: cardPrices(rows, priceOf) };
  const html = catalogHtml(state, 'test-nonce', 'models');
  const page = pageTree(html);
  runPageHtml(html, selectorsOf(page, AT_LOAD), undefined, { value: undefined });
  const models = page.one((node) => node.dataset.pane === 'models', 'the Models pane');
  assert.equal(models.hidden, false, 'the page did not open on Models');

  return models.one((node) => node.id === `price-in-${id}`, `input rate box of ${id}`).attrs['placeholder'] ?? '';
}

test('a consultant-only row on a model no reviewer uses shows its catalog price', () => {
  assert.equal(listedRate([REVIEWER, CONSULT_ONLY], 'consult-glm'), '0.6', 'the card of a row that reviews nothing shows no price');
});

test('two rows on one model and two routes each show their own rate', () => {
  const rows = [REVIEWER, API_CONSULTANT];

  assert.equal(listedRate(rows, 'codex'), '1.25', 'the reviewer\'s card took another row\'s endpoint rate');
  assert.equal(listedRate(rows, 'consult-api'), '3', 'the api row\'s card shows the list price, not its own endpoint\'s');
});

test('a row the lists do not know keeps its dash', () => {
  const unknown: Vendor = { ...CONSULT_ONLY, id: 'private', model: 'some-private-model' };

  assert.equal(listedRate([REVIEWER, unknown], 'private'), '—');
});
