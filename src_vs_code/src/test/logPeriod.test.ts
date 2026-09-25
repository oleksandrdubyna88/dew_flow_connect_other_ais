import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DEFAULT_PERIOD, LOG_PERIODS, periodButtonsHtml, periodOf, periodStart, sinceOf } from '../logPeriod';
import { WINDOWS, windowStart } from '../usage';

const NOW = new Date(2026, 8, 25, 15, 30);

test('the periods are the spending tab\'s four, in its order, and then All', () => {
  assert.deepEqual(LOG_PERIODS.map((one) => one.id), [...WINDOWS.map((w) => w.id), 'all']);
  assert.deepEqual(LOG_PERIODS.map((one) => one.label), ['Today', 'Week', 'Month', 'Year', 'All']);
  assert.equal(DEFAULT_PERIOD, 'day', 'the operator asked for Today, like the spending tab');
});

test('a period starts where the spending tab\'s window does, and All has no start', () => {
  for (const w of WINDOWS) {
    assert.equal(periodStart(w.id, NOW), windowStart(w.id, NOW), `${w.id} is not the spending tab's ${w.id}`);
  }
  assert.equal(periodStart('all', NOW), Number.NEGATIVE_INFINITY);
});

test('the server is handed the period\'s first instant in UTC, and nothing for All', () => {
  // Today is since LOCAL midnight, so the instant handed over is local midnight as UTC — an instant,
  // never a date the server would have to guess a zone for. (gemini, the plan round.)
  assert.equal(sinceOf('day', NOW), new Date(2026, 8, 25).toISOString());
  assert.equal(sinceOf('all', NOW), '');
});

test('only a period that exists is a period', () => {
  assert.equal(periodOf('week'), 'week');
  assert.equal(periodOf('all'), 'all');
  for (const junk of ['forever', '', undefined, 7, null, 'Week']) {
    assert.equal(periodOf(junk), undefined, `${JSON.stringify(junk)} was taken for a period`);
  }
});

test('a client tab\'s buttons filter the page; the spots tab\'s ask the host', () => {
  const page = periodButtonsHtml('consultations', 'week');
  const host = periodButtonsHtml('spots', 'all');

  assert.match(page, /^<div class="windows" data-periods="consultations">/);
  assert.equal((page.match(/data-period="/g) ?? []).length, 5);
  assert.ok(!page.includes('data-command'), 'a page-filtered tab sent its presses to the host');
  assert.match(page, /class="tab on" data-period="week">Week</, 'the chosen period is not marked');
  assert.equal((page.match(/class="tab on"/g) ?? []).length, 1, 'more than one period is marked');
  assert.match(host, /class="tab on" data-command="spotsPeriod" data-id="all">All</);
  assert.equal((host.match(/data-command="spotsPeriod"/g) ?? []).length, 5);
});
