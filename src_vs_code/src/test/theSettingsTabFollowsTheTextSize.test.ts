import assert from 'node:assert/strict';
import { test } from 'node:test';

import { panelHtml, settingsHtml } from '../panelView';
import { stylesheet } from './cssRules';
import { panelState } from './panelPageHarness';

/**
 * The Settings tab's text follows its size control — every line of it — and its MCP server tab is drawn
 * twice as large (`todo/PLAN_every_page_reads_alike.md`, S2).
 *
 * <p>The tab is drawn from the sidebar's stylesheet, and that sheet sized its small print in PIXELS: 11px
 * notes, 10px badges. A size control scales the root and what is measured from it; it cannot reach a pixel,
 * so the headings grew and the notes did not — and those same notes were why the MCP server tab read small.
 * The sheet now sizes them in `rem` — `calc(11rem / 13)` is the old 11px at the default 13px root — and
 * the root is the theme's size in the sidebar and the size control's in the Settings tab.</p>
 */

const SHEET = stylesheet(panelHtml(panelState(''), 'n'));

/** The declarations of every rule whose selector is exactly `selector`, joined. */
const declared = (rules: ReturnType<typeof stylesheet>, selector: string): string =>
  rules.filter((rule) => rule.selector.trim() === selector).map((rule) => rule.body).join(' ');

test('no rule in the shared stylesheet sizes its text in pixels, which the size control cannot reach', () => {
  const inPixels = SHEET.filter((rule) => /font-size:\s*[\d.]+px/.test(rule.body)).map((rule) => rule.selector.trim());

  assert.deepEqual(inPixels, [], `these rules still size text in pixels: ${inPixels.join(' · ')}`);
});

test('what text depends on to fit — the ? circle — is measured from the root too, or it clips at a large size', () => {
  const circle = declared(SHEET, '.help');

  assert.ok(circle.length > 0, 'the ? circle has no rule');
  assert.doesNotMatch(circle, /\b(width|height|line-height):\s*[\d.]+px/, `the ? circle is still a fixed pixel box: ${circle}`);
});

test('the sidebar roots its text in the theme\'s size, so the rem units read exactly as the pixels did', () => {
  assert.match(declared(SHEET, 'html'), /font-size:\s*var\(--vscode-font-size\)/);
});

test('the MCP server tab is drawn twice as large — every length in it, the rem-sized notes included', () => {
  const rules = stylesheet(settingsHtml(panelState(''), 'n', 'server'));
  const pane = rules.filter((rule) => /\.sec-server\b/.test(rule.selector)).map((rule) => rule.body).join(' ');

  // zoom, not font-size: 2em. A parent's font-size does not reach anything measured in rem, which is how
  // the notes are sized now — so 2em would have doubled only the unsized text and left the small print.
  assert.match(pane, /zoom:\s*2\b/, `the MCP server pane is not drawn at twice the size: ${pane}`);
});
