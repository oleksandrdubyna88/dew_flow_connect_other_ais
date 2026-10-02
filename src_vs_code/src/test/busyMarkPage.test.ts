import assert from 'node:assert/strict';
import { test } from 'node:test';

import { BUSY_AFTER_MS } from '../busyMark';
import type { BusySnapshot } from '../inFlight';
import { settingsKey, staticKey } from '../panelView';
import { type Control, type Page, panelState, runPanel } from './panelPageHarness';

/**
 * The busy mark, RUN — the panel's own script on a clock that moves only when the test moves it
 * (research/PLAN_model_search_and_busy_marks.md §3.7, §3.11–§3.13).
 *
 * <p>Asked for on 2026-10-02: a setting change froze the panel for seconds with nothing on screen. Every assertion
 * answers "what would I see if the behaviour were deleted": no bar after the delay, a bar that flashes for a quick
 * write, a bar that sticks once the host said it was done, a repainted page that forgets what is running or invents
 * something that is not.</p>
 */

const CODEX_MODELS = [{ id: 'gpt-6-astra', label: 'GPT-6 Astra' }, { id: 'gpt-6-luna', label: 'GPT-6 Luna' }];

function reviewers(busy?: BusySnapshot): Page {
  return runPanel(panelState('reviewers', { codexModels: CODEX_MODELS, ...(busy === undefined ? {} : { busy }) }));
}

function codexModel(page: Page): Control {
  const select = page.controls.find((one) => one.dataset['setting'] === 'model' && one.dataset['vendor'] === 'codex');
  assert.ok(select !== undefined, 'the page has a model dropdown for codex');

  return select;
}

/** Picks a model as a person does, and answers the number the page sent the write under. */
function pick(page: Page, model = 'gpt-6-luna'): number {
  const select = codexModel(page);
  select.value = model;
  select.fire('change');
  const write = page.posted.filter((one) => one['type'] === 'setting').at(-1);
  assert.ok(write !== undefined && typeof write['seq'] === 'number', 'the write was numbered');

  return write['seq'] as number;
}

/** The document id this page sends its work under — read off what it posted, never guessed. */
function docOf(page: Page): string {
  const numbered = page.posted.find((one) => typeof one['seq'] === 'number');
  assert.ok(numbered !== undefined && typeof numbered['doc'] === 'string' && numbered['doc'].length > 0,
    'a numbered post names the document that sent it');

  return numbered['doc'] as string;
}

/** What the host posts when it has finished the work this page numbered `seq`. */
function settled(page: Page, seq: number, ok = true): Record<string, unknown> {
  return { type: 'settled', seq, doc: docOf(page), ok };
}

function bar(page: Page): Control {
  assert.ok(page.bar !== null, 'the page draws a busy bar');

  return page.bar;
}

const shown = (page: Page): boolean => !bar(page).hidden;

test('a write the host has not settled shows nothing before the delay, and the bar and the mark from it on', () => {
  const page = reviewers();
  assert.equal(shown(page), false, 'drawn hidden');
  pick(page);

  page.clock.advance(BUSY_AFTER_MS - 1);
  assert.equal(shown(page), false, 'nothing for a write that may still finish in time');
  assert.equal(codexModel(page).getAttribute('aria-busy'), null);

  page.clock.advance(1);
  assert.equal(shown(page), true, `the bar at ${BUSY_AFTER_MS} ms`);
  assert.equal(bar(page).getAttribute('aria-busy'), 'true', 'and it says so to a screen reader');
  assert.equal(codexModel(page).getAttribute('aria-busy'), 'true', 'the control that started it is marked');
});

test('the host settling it takes the bar and the mark away — whether the work went well or not', () => {
  for (const ok of [true, false]) {
    const page = reviewers();
    const seq = pick(page);
    page.clock.advance(BUSY_AFTER_MS);
    page.deliver(settled(page, seq, ok));

    assert.equal(shown(page), false, `gone on settled, ok ${String(ok)}`);
    assert.equal(codexModel(page).getAttribute('aria-busy'), null);
    assert.equal(bar(page).getAttribute('aria-busy'), null);
  }
});

test('a write settled before the delay never shows the bar at all', () => {
  const page = reviewers();
  const seq = pick(page);
  page.clock.advance(300);
  page.deliver(settled(page, seq));
  page.clock.advance(5_000);

  assert.equal(shown(page), false, 'no flash for a quick write');
  assert.equal(codexModel(page).getAttribute('aria-busy'), null);
});

test('a command is numbered and marked too; a focus report is neither', () => {
  const page = reviewers();
  const button = page.commands.find((one) => one.dataset['command'] === 'removeVendor' && one.dataset['id'] === 'codex');
  assert.ok(button !== undefined);
  button.fire('click');
  const command = page.posted.filter((one) => one['type'] === 'command').at(-1);
  assert.equal(typeof command?.['seq'], 'number');

  codexModel(page).fire('focusin');
  const focus = page.posted.filter((one) => one['type'] === 'focus').at(-1);
  assert.equal(focus?.['seq'], undefined, 'a focus report is state, not work');
  page.clock.advance(BUSY_AFTER_MS);
  assert.equal(button.getAttribute('aria-busy'), 'true');
});

test('a page painted while work runs shows the bar after what is LEFT of the delay, not a fresh one', () => {
  const young = reviewers({ count: 1, oldestMs: 400 });
  young.clock.advance(99);
  assert.equal(shown(young), false);
  young.clock.advance(1);
  assert.equal(shown(young), true, 'work already 400 ms old shows 100 ms later');

  const old = reviewers({ count: 1, oldestMs: 900 });
  assert.equal(shown(old), true, 'work already past the delay shows at once');

  const idle = reviewers({ count: 0, oldestMs: 0 });
  idle.clock.advance(5_000);
  assert.equal(shown(idle), false);
});

test('a host-only busy state marks no control, only the bar', () => {
  const page = reviewers({ count: 1, oldestMs: 900 });

  assert.equal(shown(page), true);
  assert.ok(page.controls.every((one) => one.getAttribute('aria-busy') === null), 'no control started this page’s work');
});

test('the page says it is ready as the last thing it does, and the answer replaces what it was painted with', () => {
  const page = reviewers({ count: 1, oldestMs: 0 });
  assert.deepEqual(page.posted.at(-1), { type: 'ready' }, 'its last post');

  page.clock.advance(200);
  page.deliver({ type: 'busy', count: 0, oldestMs: 0 });
  page.clock.advance(5_000);
  assert.equal(shown(page), false, 'the host said nothing is running, so the painted timer is gone');

  page.deliver({ type: 'busy', count: 2, oldestMs: 700 });
  assert.equal(shown(page), true, 'and an announcement of old work shows at once');
});

test('a replaced page owns nothing: its predecessor’s seq changes nothing, and the host’s count clears it', () => {
  const before = reviewers();
  const seq = pick(before);
  // The document is replaced mid-write: the new one is painted with what the host has in flight.
  const after = reviewers({ count: 1, oldestMs: 100 });
  after.clock.advance(BUSY_AFTER_MS);
  assert.equal(shown(after), true, 'the write is still running, and the new page knows it');

  after.deliver(settled(before, seq));
  assert.equal(shown(after), true, 'a seq this page never posted is not its own to clear');
  after.deliver({ type: 'busy', count: 0, oldestMs: 0 });
  assert.equal(shown(after), false, 'the host’s count reaching nothing does');
  assert.ok(after.controls.every((one) => one.getAttribute('aria-busy') === null));
});

test('what is in flight never changes the paint key, so drawing the bar never repaints a page', () => {
  // A changed key replaces the whole document; a bar that did that would make the wait it reports longer (§3.11).
  const idle = panelState('reviewers', { codexModels: CODEX_MODELS });
  const busy = { ...idle, busy: { count: 3, oldestMs: 1_200 } };

  assert.equal(staticKey(busy), staticKey(idle));
  assert.equal(settingsKey(busy), settingsKey(idle));
});

test('a new document’s own seq 1 is not cleared by its predecessor’s seq 1 finishing', () => {
  // Own review of E3: every document numbers from 1, and `settled` goes to the slot's CURRENT document — so the old
  // page's long command finishing cleared the new page's mark on a control the person had just used.
  const before = reviewers();
  const old = pick(before);
  const after = reviewers();
  const mine = pick(after);
  assert.equal(mine, old, 'both documents numbered their first write 1 — the collision this guards');
  after.clock.advance(BUSY_AFTER_MS);
  assert.equal(codexModel(after).getAttribute('aria-busy'), 'true');

  after.deliver(settled(before, old));
  assert.equal(codexModel(after).getAttribute('aria-busy'), 'true', 'the old document’s work is not this one’s');
  after.deliver(settled(after, mine));
  assert.equal(codexModel(after).getAttribute('aria-busy'), null);
});

test('a control with two operations in flight keeps its mark until both are settled', () => {
  // Final E3 round (gemini): settling the first removed the control's aria-busy while the second was still running.
  const page = reviewers();
  const first = pick(page, 'gpt-6-luna');
  const second = pick(page, 'gpt-6-astra');
  page.clock.advance(BUSY_AFTER_MS);
  page.deliver(settled(page, first));

  assert.equal(codexModel(page).getAttribute('aria-busy'), 'true', 'the second write on this control is still running');
  page.deliver(settled(page, second));
  assert.equal(codexModel(page).getAttribute('aria-busy'), null);
});

test('two writes in flight keep the bar until BOTH are settled', () => {
  const page = reviewers();
  const first = pick(page, 'gpt-6-luna');
  const second = pick(page, 'gpt-6-astra');
  page.clock.advance(BUSY_AFTER_MS);
  page.deliver(settled(page, first));

  assert.equal(shown(page), true, 'one is still running');
  page.deliver(settled(page, second));
  assert.equal(shown(page), false);
});
