import '../../scripts/inRunFolder.cjs';
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
  // Consultation cadence had a section of its own from 2026-09-29 (`research/PLAN_the_cadence_has_its_own_section.md`); the owner removed it for v2 on 2026-11-23.
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

for (const id of ['consultations']) {
  test(`the ${id} section has a heading colour of its own, as every section does`, () => {
    const heading = stylesheet(HTML).filter((rule) => rule.selector.trim() === `.sec-${id} > summary`);

    assert.equal(heading.length, 1, 'the section has no heading colour, so it reads as unstyled');
    assert.match(heading[0]!.body, /color:/);
  });
}

test('Consultation cadence is not on the sidebar: no section, no heading colour, and nothing of it is drawn', () => {
  const html = panelHtml(panelState('', {}), 'n');

  assert.equal(html.includes('data-section="cadence"'), false, 'the section is still drawn');
  assert.equal(html.includes('Consultation cadence'), false, 'its heading is still on the page');
  assert.equal(stylesheet(html).some((rule) => rule.selector.includes('.sec-cadence')), false, 'its colour rule is still shipped');
});
