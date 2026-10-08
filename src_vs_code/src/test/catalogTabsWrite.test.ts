import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { panelState, runPanel, withoutSeq, type Control, type Page } from './panelPageHarness';

/**
 * E4.1 of todo/PLAN_one_model_catalog.md: a section moved onto the new page is the SAME section — each control on it
 * writes through the same shared script. Until E5.1 step 5 every setting control of the current Settings page was changed
 * on both pages and what each posted compared; with that page gone, every setting control of the Settings page is
 * changed in turn and must write ITS OWN setting — a control that writes nothing, or another key, is a control wired to
 * nothing.
 */

/** A control's identity across two pages: its tag and its `data-*`, and which of the identical ones it is. */
function keysOf(controls: readonly Control[]): readonly string[] {
  const seen = new Map<string, number>();

  return controls.map((one) => {
    const base = `${one.tagName} ${JSON.stringify(one.dataset)}`;
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);

    return `${base} #${count}`;
  });
}

/** What changing one control posts once its debounce has run — a checkbox flipped, any other control just changed. */
function writeOf(page: Page, key: string): string {
  const control = page.controls[keysOf(page.controls).indexOf(key)];
  if (control === undefined) {
    return 'not drawn';
  }
  if (control.disabled) {
    return 'disabled';
  }
  const before = page.posted.length;
  control.checked = !control.checked;
  control.fire('change');
  control.fire('input');
  page.clock.advance(5_000);

  return JSON.stringify(page.posted.slice(before).filter((one) => one['type'] === 'setting').map(withoutSeq));
}

/** What each control posts, a fresh page per control, so one write never changes what the next one starts from. */
function writesOn(html: string, keys: readonly string[]): ReadonlyMap<string, string> {
  const state = panelState('reviewers');

  return new Map(keys.map((key) => [key, writeOf(runPanel(state, { html }), key)]));
}

test('every setting control on the Settings page writes its own setting', () => {
  const state = panelState('reviewers');
  const html = catalogHtml(state, 'test-nonce', 'models');
  const keys = keysOf(runPanel(state, { html }).controls).filter((key) => key.includes('"setting":'));
  assert.ok(keys.length > 40, `the sweep found only ${keys.length} controls — the harness no longer reads the page`);

  const writes = writesOn(html, keys);
  const wrong = keys.filter((key) => {
    const setting = (JSON.parse(key.slice(key.indexOf(' ') + 1, key.lastIndexOf(' #'))) as Record<string, string>)['setting'];
    const wrote = writes.get(key) ?? '';

    return wrote !== 'disabled' && !wrote.includes(`"key":"${setting}"`);
  }).map((key) => `${key}: ${writes.get(key)}`);

  assert.deepEqual(wrong, [], 'these controls write nothing, or another setting, on the Settings page');
});
