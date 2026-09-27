import assert from 'node:assert/strict';
import { test } from 'node:test';

import { LITELLM_PRICES, PriceTable } from '../modelPrices';
import { PriceBook } from '../priceBook';

/**
 * The window's one price book (S3.7): the panel and the settings writer read the same two tables,
 * fetched at most once a day, and an `api` row's model is priced on its endpoint's route.
 */

const OPENROUTER: PriceTable = { 'grok-4.7': { inPerMillion: 1.6, outPerMillion: 4.8, source: 'openrouter' } };
const LITELLM: PriceTable = {
  'grok-4.7': { inPerMillion: 2, outPerMillion: 6, cachedPerMillion: 0.5, source: 'litellm' },
  'xai/grok-4.7': { inPerMillion: 2, outPerMillion: 6, cachedPerMillion: 0.5, source: 'litellm' },
};

function book(clock: { now: number }): { book: PriceBook; fetches: string[] } {
  const fetches: string[] = [];
  const fake = (url: string): Promise<PriceTable> => {
    fetches.push(url);

    return Promise.resolve(url === LITELLM_PRICES ? LITELLM : OPENROUTER);
  };

  return { book: new PriceBook(fake, () => clock.now), fetches };
}

test('the lists are fetched once, shared, and not again within the day', async () => {
  const clock = { now: 10 * 24 * 60 * 60 * 1000 };
  const { book: prices, fetches } = book(clock);

  const [first, second] = await Promise.all([prices.refresh(), prices.refresh()]);
  assert.equal(first, true);
  assert.equal(second, true, 'two callers at once share the one fetch');
  assert.equal(fetches.length, 2, 'one request per list, not two');

  clock.now += 60 * 60 * 1000;
  assert.equal(await prices.refresh(), false);
  assert.equal(fetches.length, 2);
});

test('a row on the vendor’s own endpoint is priced on that route; any other row as before', async () => {
  const { book: prices } = book({ now: 10 * 24 * 60 * 60 * 1000 });
  await prices.refresh();

  assert.equal(prices.priceOf('grok-4.7', 'https://api.x.ai/v1')?.inPerMillion, 2);
  assert.equal(prices.priceOf('grok-4.7')?.inPerMillion, 1.6);
  assert.equal(prices.priceOf(''), undefined, 'a CLI default model is not guessed at');
});
