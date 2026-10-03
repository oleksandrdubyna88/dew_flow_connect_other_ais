import assert from 'node:assert/strict';
import { test } from 'node:test';

import type { CadenceLine } from '../cadenceLine';
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

test('the sidebar is Notifications, Active gates, Active consultations, Consultation cadence, Phrases and Bugz, in that order', () => {
  // The cadence lines left Active gates for a section of their own on 2026-09-29 (the operator:
  // `research/PLAN_the_cadence_has_its_own_section.md`).
  assert.deepEqual(sections(HTML), [
    ['notifications', 'Notifications'],
    ['rounds', 'Active gates'],
    ['consultations', 'Active consultations'],
    ['cadence', 'Consultation cadence'],
    ['phrases', 'Phrases'],
    ['bugz', 'Bugz'],
  ]);
});

test('the running rounds are drawn once, under Active gates, and the consultations once, under their own section', () => {
  for (const [region, section] of [['rounds', 'rounds'], ['consultations', 'consultations'], ['cadence', 'cadence']] as const) {
    assert.equal(HTML.split(`id="live-${region}"`).length - 1, 1, `the ${region} region is not drawn exactly once`);
    assert.ok(sectionOf(HTML, section).includes(`id="live-${region}"`), `the ${region} region is not inside the ${section} section`);
  }
  assert.ok(!sectionOf(HTML, 'rounds').includes('id="live-consultations"'), 'the consultations are still inside Active gates');
});

for (const id of ['consultations', 'cadence']) {
  test(`the ${id} section has a heading colour of its own, as every section does`, () => {
    const heading = stylesheet(HTML).filter((rule) => rule.selector.trim() === `.sec-${id} > summary`);

    assert.equal(heading.length, 1, 'the section has no heading colour, so it reads as unstyled');
    assert.match(heading[0]!.body, /color:/);
  });
}

const LINE: readonly CadenceLine[] = [{
  repoPath: 'D:/repo', branch: 'feat/epic-4',
  answer: {
    plan: 'todo/PLAN_x.md', mode: 'remind', epics: 14, epicsClosed: [1, 2, 3, 4],
    groups: [{ range: '1-3', consulted: true }, { range: '4-6', consulted: false }],
    risk: [], riskAnswered: true, unreadable: '',
  },
}];

test('a cadence line is drawn under Consultation cadence, and no longer under Active gates', () => {
  const html = panelHtml(panelState('', { cadence: LINE }), 'n');

  assert.ok(sectionOf(html, 'cadence').includes('PLAN_x.md · epics closed 4/14'), 'the line is not in its own section');
  assert.ok(!sectionOf(html, 'rounds').includes('PLAN_x.md'), 'the line is still drawn with the running rounds');
});

test('an empty Consultation cadence says why: the cadence is off, or no round has named a plan yet', () => {
  const base = panelState('');
  const off = panelHtml({ ...base, settings: { ...base.settings, cadence: { ...base.settings.cadence, mode: 'off' } } }, 'n');
  const on = panelHtml({ ...base, settings: { ...base.settings, cadence: { ...base.settings.cadence, mode: 'remind' } }, cadence: [] }, 'n');

  assert.match(sectionOf(off, 'cadence'), /The consultation cadence is off/, 'an empty section with the cadence off says nothing');
  assert.match(sectionOf(off, 'cadence'), /Settings → Consultant/, 'and does not say where it is switched on');
  // True in every case the probe answers nothing, not only 'no plan named yet': a server too old for the mode,
  // none installed, a first probe still out, a plan quiet for a day — each leaves the list empty too.
  assert.match(sectionOf(on, 'cadence'), /No plan's cadence has been read yet/, 'an empty section with the cadence on says nothing');
  assert.match(sectionOf(on, 'cadence'), /in the last day/, 'it promises a line where the probe would never draw one');
  assert.match(sectionOf(on, 'cadence'), /the server answers for it/, 'it does not say the server has to answer');
});
