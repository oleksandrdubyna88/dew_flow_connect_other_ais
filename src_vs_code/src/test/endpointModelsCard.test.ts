import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { EndpointListing } from '../endpointModels';
import type { PanelState } from '../panelView';
import { DEFAULT_VENDORS, type Vendor } from '../vendors';
import { click, panelState, runPanel, work, type Page } from './panelPageHarness';

/**
 * The reviewer card of a row on somebody else's endpoint, RUN: what its model dropdown offers, what its
 * caption says, and that ≡ asks the host to list the endpoint's models.
 *
 * <p>Reported from a screenshot on 2026-10-01: an `openrouter` card (runtime codex, base URL
 * `https://openrouter.ai/api/v1`) offered the Codex CLI's ten cached OpenAI slugs and no way to learn the
 * ids OpenRouter accepts (research/PLAN_custom_endpoint_model_list.md).</p>
 */

const OPENROUTER = 'https://openrouter.ai/api/v1';
const CODEX_CACHE = [{ id: 'gpt-6-luna', label: 'GPT-6 Luna' }, { id: 'gpt-6-astra', label: 'GPT-6 Astra' }];

/** The shipped codex row, renamed and pointed at OpenRouter — the row the operator added. */
function openrouter(overrides: Partial<Vendor> = {}): Vendor {
  const codex = DEFAULT_VENDORS.find((one) => one.runtime === 'codex');
  assert.ok(codex !== undefined, 'the shipped vendors carry a codex row');

  return { ...codex, id: 'openrouter', baseUrl: OPENROUTER, model: '', enabled: true, ...overrides };
}

function card(vendors: readonly Vendor[], overrides: Partial<PanelState> = {}): Page {
  return runPanel(panelState('reviewers', { vendors, codexModels: CODEX_CACHE, ...overrides }));
}

/** What the model dropdown of one row OFFERS, as the page drew it. */
function offered(page: Page, vendor: string): readonly string[] {
  const select = page.controls.find((one) => one.dataset['setting'] === 'model' && one.dataset['vendor'] === vendor);
  assert.ok(select !== undefined, `the page has no model dropdown for ${vendor}`);

  return select.options.map((option) => option.value);
}

/**
 * The markup of ONE row's card — what that row shows, and nothing else on the page.
 *
 * <p>A caption asserted over the whole page could be matched by another card or another section
 * (CodeRabbit, #630). The caption is drawn by the host, not by the page script, so its markup is the thing
 * to read; this narrows it to the card whose checkbox is `v-<id>`.</p>
 */
function cardOf(page: Page, vendor: string): string {
  // The new page's Models card (E3.2), whose switch is `on-<id>` — the page a person sees (E5.1 step 3).
  const mine = page.html.split('<article class="card').slice(1).filter((one) => one.includes(`id="on-${vendor}"`));
  assert.equal(mine.length, 1, `the page has one card for ${vendor}`);

  return mine[0] ?? '';
}

function listing(overrides: Partial<EndpointListing> = {}): EndpointListing {
  return {
    baseUrl: OPENROUTER,
    keyName: 'openrouter',
    ids: ['deepseek/deepseek-chat', 'openai/gpt-5'],
    reason: '',
    askedUtc: '2026-10-01T14:05:09.000Z',
    ...overrides,
  };
}

test('an OpenRouter card offers none of the Codex CLI cache', () => {
  const page = card([openrouter()]);

  assert.deepEqual(offered(page, 'openrouter').filter((id) => id.startsWith('gpt-6')), [],
    'OpenAI slugs are not names OpenRouter accepts');
  assert.doesNotMatch(cardOf(page, 'openrouter'), /models the Codex CLI has cached/u, 'nor is it captioned with them');
  assert.match(cardOf(page, 'openrouter'), /openrouter\.ai is not asked until you press ≡/u);
});

/** What the model dropdown of one row SAYS for each choice, as the page drew it. */
function labels(page: Page, vendor: string): readonly string[] {
  const select = page.controls.find((one) => one.dataset['setting'] === 'model' && one.dataset['vendor'] === vendor);
  assert.ok(select !== undefined, `the page has no model dropdown for ${vendor}`);

  return select.options.map((option) => option.text);
}

test('an endpoint card with no model says so, rather than offering the CLI’s default', () => {
  // The empty choice stores no model, and a codex row with no model passes no -m: the Codex CLI then sends ITS
  // default id to the endpoint, which OpenRouter does not serve (research/PLAN_model_search_and_busy_marks.md, symptom 3).
  const page = card([openrouter(), { ...openrouter(), id: 'codex', baseUrl: '' }]);

  assert.equal(labels(page, 'openrouter')[0], 'no model yet — press ≡ and pick one this endpoint lists');
  assert.equal(labels(page, 'codex')[0], "the CLI's default", 'a codex row on OpenAI keeps the CLI default it really has');
});

test('≡ on an endpoint card asks the host to list that endpoint’s models', () => {
  const page = card([openrouter()]);

  click(page, 'listEndpointModels', 'openrouter');

  assert.deepEqual(work(page).at(-1), { type: 'command', command: 'listEndpointModels', id: 'openrouter' });
});

test('what the endpoint listed is what the card offers, and the caption says when it was asked', () => {
  const page = card([openrouter({ model: 'openai/gpt-5' })], { endpointListings: { openrouter: listing() } });

  assert.ok(offered(page, 'openrouter').includes('deepseek/deepseek-chat'));
  assert.ok(offered(page, 'openrouter').includes('openai/gpt-5'));
  assert.match(cardOf(page, 'openrouter'), /2 models openrouter\.ai listed for the key under 'openrouter', asked 14:05 UTC/u);
});

test('while it is asking, the card says so', () => {
  const page = card([openrouter()], { askingEndpoints: ['openrouter'] });

  assert.match(cardOf(page, 'openrouter'), /asking openrouter\.ai/u);
});

test('a plain codex card keeps the Codex CLI list and gets no ≡', () => {
  const page = card([openrouter({ id: 'codex', baseUrl: '' })]);

  assert.deepEqual(offered(page, 'codex').filter((id) => id.startsWith('gpt-6')), ['gpt-6-luna', 'gpt-6-astra']);
  assert.ok(!page.commands.some((one) => one.dataset['command'] === 'listEndpointModels'),
    'a row with no endpoint of its own has nothing to ask');
});
