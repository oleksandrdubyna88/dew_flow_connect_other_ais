import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml } from '../panelView';
import { stylesheet } from './cssRules';
import { panelState } from './panelPageHarness';

/**
 * The sidebar's Active rounds is two sections: **Active gates** — the rounds running now, and where each
 * plan's gate stands — and **Active consultations** — the consultations being had
 * (`research/PLAN_every_page_reads_alike.md`, S4; the operator, 2026-09-29).
 */

const HTML = panelHtml(panelState(''), 'n');

/** The sidebar's sections as drawn, in order: id and heading. */
function sections(html: string): readonly (readonly [string, string])[] {
  return [...html.matchAll(/<details class="section sec-(\w+)" data-section="\w+"[^>]*>\s*<summary>([^<]*)<\/summary>/g)]
    .map((found) => [found[1]!, found[2]!] as const);
}

/** The markup of one section, from its opening tag to its closing one. */
function sectionOf(html: string, id: string): string {
  const at = html.indexOf(`data-section="${id}"`);
  assert.ok(at >= 0, `the sidebar has no ${id} section`);

  return html.slice(at, html.indexOf('</details>', at));
}

test('the sidebar is Notifications, Active gates, Active consultations, Phrases and Bugz, in that order', () => {
  assert.deepEqual(sections(HTML), [
    ['notifications', 'Notifications'],
    ['rounds', 'Active gates'],
    ['consultations', 'Active consultations'],
    ['phrases', 'Phrases'],
    ['bugz', 'Bugz'],
  ]);
});

test('the running rounds are drawn once, under Active gates, and the consultations once, under their own section', () => {
  for (const [region, section] of [['rounds', 'rounds'], ['consultations', 'consultations']] as const) {
    assert.equal(HTML.split(`id="live-${region}"`).length - 1, 1, `the ${region} region is not drawn exactly once`);
    assert.ok(sectionOf(HTML, section).includes(`id="live-${region}"`), `the ${region} region is not inside the ${section} section`);
  }
  assert.ok(!sectionOf(HTML, 'rounds').includes('id="live-consultations"'), 'the consultations are still inside Active gates');
});

test('Active consultations has a heading colour of its own, as every section does', () => {
  const heading = stylesheet(HTML).filter((rule) => rule.selector.trim() === '.sec-consultations > summary');

  assert.equal(heading.length, 1, 'the new section has no heading colour, so it reads as unstyled');
  assert.match(heading[0]!.body, /color:/);
});
