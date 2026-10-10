import assert from 'node:assert/strict';
import { test } from 'node:test';

import { usersPageHtml } from '../bugsKeysPage';
import { reviewPageHtml } from '../bugzReviewPage';
import { renderHelpHtml } from '../helpPage';
import { notificationsPageHtml } from '../notificationsPage';
import { phrasesHtml } from '../phrasesPage';
import { roundsLogHtml } from '../roundsLog';
import { catalogHtml } from '../catalogPage';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml, type Page } from './pageScriptHarness';

/**
 * Every ConnectOtherAIs page carries BOTH text controls — the size and the tone — and RUNS them: a press
 * posts, and what the host pushes lands on the page (`research/PLAN_every_page_reads_alike.md`, S1).
 *
 * <p>Asked for by the operator on 2026-09-29: "we have them; you forgot to apply them". Three pages had
 * both, three had the size only, and five — the Settings tab among them — had neither, and nothing
 * noticed, because each page wired the controls by hand. Each page here is rendered through its real
 * builder and its OWN script is run in the shared shim, because a page that draws a button and wires it to
 * nothing reads exactly like a working one in its source text.</p>
 *
 * <p>The Chat presets, Review roles and Gate commands tabs were three more, until E5.1 step 4 of
 * research/PLAN_one_model_catalog.md deleted them — their editors are places of the Settings page, whose controls are the
 * page's own. The Chat page is the ninth; its state is large enough that its own tests
 * (`chatPage.test.ts`, `aQuestionCanWait.test.ts`) are where both of its controls are run.</p>
 */

/** The two labels every page has, which the shim hands over as the controls' own nodes rather than as plain elements. */
const OFFSET_LABELS = new Set(['zoomOffset', 'toneOffset']);

/**
 * Every element a page's own script can look up by id — read off the markup its real builder returned.
 *
 * <p>This used to be a hand-written list per page, and every control a page added that its script looks up on load
 * turned this test red until somebody extended the list (the Team server tab added five). A list the test repeats can
 * only lag the page; the markup cannot. An id the script asks for that the markup does NOT carry is still not supplied,
 * so a page that looks up an element it never drew keeps failing here.</p>
 */
function idsIn(html: string): readonly string[] {
  return [...new Set([...html.matchAll(/\sid="([^"]+)"/g)].map((match) => match[1] ?? ''))].filter(
    (id) => id.length > 0 && !OFFSET_LABELS.has(id),
  );
}

const PAGES: readonly (readonly [name: string, render: () => string])[] = [
  ['Phrases', () => phrasesHtml({ rows: [], uiScale: 0 }, 'n')],
  ['Notifications', () => notificationsPageHtml({ rows: [], dataDir: 'd', older: false, loaded: 0, generation: 1 }, 'n')],
  ['Review rounds', () => roundsLogHtml([], [], 'n')],
  ['Who holds a key', () => usersPageHtml({ view: { kind: 'no-key', said: '' } }, 'n')],
  // The Settings page (PLAN_one_model_catalog.md E3) — the only one in its tab since E5.1 step 5 removed the page it replaced.
  ['Settings page', () => catalogHtml(panelState(''), 'n', 'models')],
  ['Help', () => renderHelpHtml({ language: 'en' })],
  ['Review bugs', () => reviewPageHtml({ pairs: [], nonce: 'n' })],
];

interface Controls {
  readonly smaller: Node;
  readonly larger: Node;
  readonly dimmer: Node;
  readonly brighter: Node;
  readonly sizeLabel: Node;
  readonly toneLabel: Node;
}

/** The four buttons, as the page's own markup draws them — and none when it draws none. */
function controlsOf(html: string): Controls | undefined {
  const drawn = (attribute: string, value: string): boolean =>
    new RegExp(`<button type="button" class="icon" data-${attribute}="${value}"`).test(html);
  if (!drawn('zoom', '-1') || !drawn('zoom', '1') || !drawn('tone', '-1') || !drawn('tone', '1')) {
    return undefined;
  }

  return {
    smaller: new Node({ zoom: '-1' }, 'BUTTON'),
    larger: new Node({ zoom: '1' }, 'BUTTON'),
    dimmer: new Node({ tone: '-1' }, 'BUTTON'),
    brighter: new Node({ tone: '1' }, 'BUTTON'),
    sizeLabel: new Node({}, 'SPAN'),
    toneLabel: new Node({}, 'SPAN'),
  };
}

function run(html: string, controls: Controls, ids: readonly string[] = []): Page {
  return runPageHtml(html, {
    'button[data-zoom]': [controls.smaller, controls.larger],
    'button[data-tone]': [controls.dimmer, controls.brighter],
    '#zoomOffset': [controls.sizeLabel],
    '#toneOffset': [controls.toneLabel],
    ...Object.fromEntries(ids.map((id) => [`#${id}`, [new Node({}, 'DIV')]])),
  });
}

/** What the page wrote as a label's text. */
const textOf = (node: Node): unknown => (node as unknown as { textContent?: unknown }).textContent;

for (const [name, render] of PAGES) {
  test(`${name}: both controls are drawn, and each press is posted`, () => {
    const html = render();
    const controls = controlsOf(html);
    assert.ok(controls !== undefined, `${name} does not draw both the text-size and the text-tone control`);
    const page = run(html, controls, idsIn(html));

    controls.larger.click();
    controls.dimmer.click();

    const presses = page.posted.filter((m) => m['type'] === 'zoom' || m['type'] === 'tone').map((m) => `${String(m['type'])} ${String(m['delta'])}`);
    assert.deepEqual(presses, ['zoom 1', 'tone -1'], `${name} did not post the two presses`);
  });

  test(`${name}: what the host pushes for either lands on the page`, () => {
    const html = render();
    const controls = controlsOf(html);
    assert.ok(controls !== undefined, `${name} does not draw both controls`);
    const page = run(html, controls, idsIn(html));

    page.message({ type: 'uiScale', px: 20.94, label: '+5' });
    page.message({ type: 'textTone', color: 'rgb(1, 2, 3)', read: 'rgb(4, 5, 6)', label: '+1' });

    assert.equal(page.body.fontSize, '20.94px', `${name} ignored a pushed text size`);
    assert.equal(page.body.custom['--coai-text'], 'rgb(1, 2, 3)', `${name} ignored a pushed text tone`);
    assert.equal(textOf(controls.sizeLabel), '+5', `${name} did not show the new size beside its control`);
    assert.equal(textOf(controls.toneLabel), '+1', `${name} did not show the new tone beside its control`);
  });
}

test('the census renders the seven pages it names — a table that lost a row would pass for the ones left', () => {
  assert.equal(PAGES.length, 7);
});

test('the ids read off the Review rounds markup cover every id this test once had to list by hand', () => {
  // The hand list as it stood when it was retired (2026-10-10): the derivation must reach each of these, or it is a
  // narrower thing than what it replaced.
  const handListed = ['failed', 'rows', 'empty', 'count', 'exportpicked', 'clearpicked', 'pickall', 'prev', 'next', 'pageinfo', 'recorded', 'tab-rounds', 'search', 'from', 'to', 'today', 'alldates', 'clear', 'consultations-body', 'consultations-none', 'qconsults-body', 'spots-body', 'usage-body', 'questions', 'team-q', 'team-sort', 'team-server', 'team-refresh', 'team-status'];
  const derived = new Set(idsIn(roundsLogHtml([], [], 'n')));
  assert.deepEqual(handListed.filter((id) => !derived.has(id)), [], 'an id the page needs is missing from its own markup');
});

test('the offset labels are never handed over as plain elements — they are the controls’ own nodes', () => {
  assert.deepEqual(idsIn('<span id="zoomOffset"></span><span id="toneOffset"></span><div id="kept"></div>'), ['kept']);
});
