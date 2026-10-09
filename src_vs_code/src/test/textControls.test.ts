import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { usersPageHtml } from '../bugsKeysPage';
import { notificationsPageHtml } from '../notificationsPage';
import { roundsLogHtml } from '../roundsLog';
import { catalogHtml, catalogKey } from '../catalogPage';
import { textControlFrom, textControlsScript } from '../textControls';
import { toneColour } from '../textTone';
import { scalePx } from '../zoomControl';
import { blanked } from './blankedSource';
import { stylesheet } from './cssRules';
import { panelState } from './panelPageHarness';
import { Node, runPageHtml } from './pageScriptHarness';

/**
 * The shared text-controls unit, and what the pages that took it must keep
 * (`research/PLAN_every_page_reads_alike.md`, S1). The census itself is `everyPageHasBothTextControls.test.ts`.
 */

test('a press is read as one step of one control, and anything else is not a press', () => {
  const cases: readonly (readonly [unknown, unknown])[] = [
    [{ type: 'zoom', delta: 1 }, { kind: 'zoom', delta: 1 }],
    [{ type: 'tone', delta: -1 }, { kind: 'tone', delta: -1 }],
    [{ type: 'zoom', delta: 7 }, { kind: 'zoom', delta: 1 }],
    [{ type: 'tone', delta: -40 }, { kind: 'tone', delta: -1 }],
    // A step of nothing is not a press: the controls only ever send one step, and a zero would write the
    // setting back unchanged for no reason.
    [{ type: 'tone', delta: 0.6 }, undefined],
    [{ type: 'zoom', delta: 0 }, undefined],
    [{ type: 'zoom', delta: '1' }, undefined],
    [{ type: 'zoom', delta: Number.NaN }, undefined],
    [{ type: 'zoom' }, undefined],
    [{ type: 'section', delta: 1 }, undefined],
    [null, undefined],
    ['zoom', undefined],
  ];

  for (const [message, expected] of cases) {
    assert.deepEqual(textControlFrom(message), expected, `read ${JSON.stringify(message)} wrongly`);
  }
});

test('a page whose handle is not called vscode gets presses sent through its own handle', () => {
  const smaller = new Node({ zoom: '-1' }, 'BUTTON');
  const script = `var api = acquireVsCodeApi();\n${textControlsScript('api')}`;
  const page = runPageHtml(`<script nonce="n">${script}</script>`, { 'button[data-zoom]': [smaller] });

  smaller.click();

  assert.deepEqual(page.posted, [{ type: 'zoom', delta: -1, field: '' }]);
});

/** The page's `body` rule, parsed — where the chosen size and tone must sit for the FIRST paint. */
function bodyRule(html: string): string {
  const body = stylesheet(html).filter((rule) => rule.selector.trim() === 'body').map((rule) => rule.body).join(' ');
  assert.ok(body.length > 0, 'the page has no body rule');

  return body;
}

// These three pages replace their whole document after they open; the host's push reaches a page once,
// so a page whose builder ignored the two values would lose them on its next draw. (Gate commands was a fourth, until
// E5.1 step 4 of research/PLAN_one_model_catalog.md deleted it; its editor is a place of the Settings page, below.)
const REDRAWN: readonly (readonly [string, (size: number, tone: number) => string])[] = [
  ['Notifications', (size, tone) => notificationsPageHtml({ rows: [], dataDir: 'd', older: false, loaded: 0, generation: 1, uiScale: size, textTone: tone }, 'n')],
  ['Review rounds', (size, tone) => roundsLogHtml([], [], 'n', '', '', undefined, { text: { size, tone } })],
  ['Who holds a key', (size, tone) => usersPageHtml({ view: { kind: 'no-key', said: '' } }, 'n', { size, tone })],
];

for (const [name, render] of REDRAWN) {
  test(`${name} is drawn in the chosen size and tone, so a redraw keeps them`, () => {
    const body = bodyRule(render(3, 2));

    assert.ok(body.includes(`font-size: ${scalePx(3)}px`), `${name}'s body rule does not carry the chosen size: ${body}`);
    assert.ok(body.includes(`--coai-text: ${toneColour(2)}`), `${name}'s body rule does not carry the chosen tone: ${body}`);
  });
}

test('the Settings tab moves its ROOT with a pushed size, because its small print is measured in rem', () => {
  const html = catalogHtml(panelState(''), 'n', 'models');
  const page = runPageHtml(html, {});

  page.message({ type: 'uiScale', px: 20.94, label: '+5' });

  assert.equal(page.root.fontSize, '20.94px');
});

test('the Settings tab draws its root and body in the chosen size, and a press does not move its paint key', () => {
  const plain = panelState('');
  const large = { ...plain, uiScale: 3, textTone: 2 };
  const rules = stylesheet(catalogHtml(large, 'n', 'models'));
  const root = rules.filter((rule) => rule.selector.trim() === 'html').map((rule) => rule.body).join(' ');

  assert.ok(root.includes(`font-size: ${scalePx(3)}px`), `the root is not in the chosen size: ${root}`);
  assert.ok(bodyRule(catalogHtml(large, 'n', 'models')).includes(`font-size: ${scalePx(3)}px`));
  assert.equal(catalogKey(large), catalogKey(plain), 'a size or tone change moved the paint key, so every press would reload the tab');
});

// ---------------------------------------------------------------- the hosts

const SRC = path.join(__dirname, '..', '..', 'src');

/** Every host file that creates a webview panel, and whether it pushes BOTH settings to it. */
function hosts(): readonly { readonly file: string; readonly pushesBoth: boolean }[] {
  return fs.readdirSync(SRC)
    .filter((file) => file.endsWith('.ts'))
    .map((file) => ({ file, source: blanked(fs.readFileSync(path.join(SRC, file), 'utf8')) }))
    .filter(({ source }) => source.includes('vscode.window.createWebviewPanel('))
    .map(({ file, source }) => ({
      file,
      pushesBoth: source.includes('pushTextControlsTo(') || (source.includes('pushUiScaleTo(') && source.includes('pushTextToneTo(')),
    }));
}

test('every host that opens a page pushes both the size and the tone to it', () => {
  // Structural, because every host imports vscode and no unit test here can construct one; the run tests
  // are the census. A page that is pushed only one setting keeps the other at whatever it was drawn with.
  const missing = hosts().filter((host) => !host.pushesBoth).map((host) => host.file);

  assert.deepEqual(missing, [], `these hosts open a page and push it only one setting, or none: ${missing.join(', ')}`);
});

test('the host scan still finds the eight hosts, so it is not passing on an empty list', () => {
  const found = hosts().map((host) => host.file).sort();

  assert.deepEqual(found, [
    'bugsKeysPanel.ts', 'bugzReviewPanel.ts', 'chatPanel.ts', 'helpPanel.ts',
    'notificationsPanel.ts', 'phrasesPanel.ts', 'roundsLogPanel.ts', 'settingsPanel.ts',
  ]);
});
