import assert from 'node:assert/strict';
import { test } from 'node:test';

import { usersPageHtml } from '../bugsKeysPage';
import { reviewPageHtml } from '../bugzReviewPage';
import { chatPresetsHtml } from '../chatPresetsPage';
import { commandsHtml } from '../commandsPage';
import { renderHelpHtml } from '../helpPage';
import { notificationsPageHtml } from '../notificationsPage';
import { phrasesHtml } from '../phrasesPage';
import { rolesHtml } from '../rolesPage';
import { roundsLogHtml } from '../roundsLog';
import { settingsHtml } from '../panelView';
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
 * <p>The Chat page is the eleventh; its state is large enough that its own tests
 * (`chatPage.test.ts`, `aQuestionCanWait.test.ts`) are where both of its controls are run.</p>
 */

/** Elements a page's own script looks up by id before it can run, beyond the two offset labels every page has. */
const HELP_IDS = ['index', 'article', 'crumbs', 'back', 'searchRow', 'noHits', 'search', 'language'];
const BUGZ_IDS = ['keep', 'drop', 'picked', 'pickall'];
const ROUNDS_IDS = ['failed', 'rows', 'empty', 'count', 'exportpicked', 'clearpicked', 'pickall', 'prev', 'next', 'pageinfo', 'recorded', 'tab-rounds', 'search', 'from', 'to', 'today', 'alldates', 'clear', 'consultations-body', 'consultations-none', 'qconsults-body', 'spots-body', 'usage-body', 'questions'];
const NOTIFICATION_IDS = ['mark-all', 'notice', 'from', 'to', 'range-note', 'find', 'source', 'where', 'prev', 'next', 'ack-note', 'clear'];

const PAGES: readonly (readonly [name: string, render: () => string, ids?: readonly string[]])[] = [
  ['Chat presets', () => chatPresetsHtml({ prompts: [], models: [], providers: [], unreadable: [], uiScale: 0 }, 'n')],
  ['Phrases', () => phrasesHtml({ rows: [], uiScale: 0 }, 'n')],
  ['Review roles', () => rolesHtml({ rows: [], texts: {}, serverVersion: '', perSide: false, uiScale: 0 }, 'n')],
  ['Gate commands', () => commandsHtml({ rows: [], texts: {}, serverVersion: '', perSide: false }, 'n')],
  ['Notifications', () => notificationsPageHtml({ rows: [], dataDir: 'd', older: false, loaded: 0, generation: 1 }, 'n'), NOTIFICATION_IDS],
  ['Review rounds', () => roundsLogHtml([], [], 'n'), ROUNDS_IDS],
  ['Who holds a key', () => usersPageHtml({ view: { kind: 'no-key', said: '' } }, 'n')],
  ['Settings tab', () => settingsHtml(panelState(''), 'n', 'reviewers')],
  // The new Settings page, in the same tab while it is a preview (PLAN_one_model_catalog.md E3).
  ['new Settings page', () => catalogHtml(panelState(''), 'n', 'models')],
  ['Help', () => renderHelpHtml({ language: 'en' }), HELP_IDS],
  ['Review bugs', () => reviewPageHtml({ pairs: [], nonce: 'n' }), BUGZ_IDS],
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

for (const [name, render, ids] of PAGES) {
  test(`${name}: both controls are drawn, and each press is posted`, () => {
    const html = render();
    const controls = controlsOf(html);
    assert.ok(controls !== undefined, `${name} does not draw both the text-size and the text-tone control`);
    const page = run(html, controls, ids);

    controls.larger.click();
    controls.dimmer.click();

    const presses = page.posted.filter((m) => m['type'] === 'zoom' || m['type'] === 'tone').map((m) => `${String(m['type'])} ${String(m['delta'])}`);
    assert.deepEqual(presses, ['zoom 1', 'tone -1'], `${name} did not post the two presses`);
  });

  test(`${name}: what the host pushes for either lands on the page`, () => {
    const html = render();
    const controls = controlsOf(html);
    assert.ok(controls !== undefined, `${name} does not draw both controls`);
    const page = run(html, controls, ids);

    page.message({ type: 'uiScale', px: 20.94, label: '+5' });
    page.message({ type: 'textTone', color: 'rgb(1, 2, 3)', read: 'rgb(4, 5, 6)', label: '+1' });

    assert.equal(page.body.fontSize, '20.94px', `${name} ignored a pushed text size`);
    assert.equal(page.body.custom['--coai-text'], 'rgb(1, 2, 3)', `${name} ignored a pushed text tone`);
    assert.equal(textOf(controls.sizeLabel), '+5', `${name} did not show the new size beside its control`);
    assert.equal(textOf(controls.toneLabel), '+1', `${name} did not show the new tone beside its control`);
  });
}

test('the census renders the eleven pages it names — a table that lost a row would pass for the ones left', () => {
  assert.equal(PAGES.length, 11);
});
