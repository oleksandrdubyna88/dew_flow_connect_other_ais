import assert from 'node:assert/strict';
import { test } from 'node:test';
import { catalogHtml } from '../catalogPage';
import { settingsHtml } from '../panelView';
import { panelState, runPanel, withoutSeq, type Control, type Page } from './panelPageHarness';

/**
 * E4.1 of todo/PLAN_one_model_catalog.md: a section moved onto the new page is the SAME section — each control on it
 * writes exactly what it writes on the current page, through the same shared script. Every setting control of the
 * current Settings page is changed in turn, on both pages, and what each posted is compared.
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

test('every setting control of a moved section writes on the new page exactly what it writes on the current one', () => {
  const state = panelState('reviewers');
  const current = settingsHtml(state, 'test-nonce', 'reviewers');
  // The reviewer cards are Models' (epic 3), drawn by another builder: their writes are that tab's tests. A consultant
  // caller's own definition is replaced on the new page by a pick from the catalog (E4.2, consultantPicks.test.ts), and
  // a Security lane prompt card offers only the rows ticked Security lane (E4.2, securityPicks.test.ts).
  const keys = keysOf(runPanel(state, { html: current }).controls)
    .filter((key) => !key.includes('"vendor"') && !key.includes('"caller"') && !key.includes('"securityField":"pair:')
      // A role's one switch is on Roles & prompts (E4.3, rolesOnTheNewPage.test.ts): Stages draws no tick of its own.
      && !key.includes('"setting":"roleEnabled"')
      // The model a chat opens on is a radio per row on Chat (E4.6b, chatOnTheNewPage.test.ts), posted as the presets' own
      // main edit, which sets the chat model and clears a stale model name: neither select is drawn there.
      && !key.includes('"setting":"chatModel"') && !key.includes('"setting":"chatModelName"'));
  assert.ok(keys.length > 40, `the sweep found only ${keys.length} controls — the harness no longer reads the page`);

  const before = writesOn(current, keys);
  const after = writesOn(catalogHtml(state, 'test-nonce', 'models'), keys);
  const differ = keys.filter((key) => before.get(key) !== after.get(key)).map((key) => `${key}: ${before.get(key)} → ${after.get(key)}`);

  assert.deepEqual(differ, [], 'these controls write differently, or not at all, on the new page');
});
