import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';

import { presetEdit } from '../chatPresetsMessages';
import { notificationsPageHtml, waitingPageHtml } from '../notificationsPage';
import { panelHtml } from '../panelView';
import { phraseEdit } from '../phrasesPage';
import { roleEdit } from '../rolesMessages';
import { scalePx } from '../zoomControl';
import { blanked } from './blankedSource';
import { stylesheet } from './cssRules';
import { panelState } from './panelPageHarness';

/**
 * What the code review beside the gate found the first cut of `research/PLAN_every_page_reads_alike.md` still
 * missing: parsers nobody asked about the tone, pages that pinned the size on a variant with no script,
 * boxes that did not grow, and hosts that sent a press through their own write queue.
 */

test('the three pages that had only the size read a tone press as a tone press', () => {
  const tone = { type: 'tone', delta: 1, field: '' };

  assert.deepEqual(presetEdit(tone), { kind: 'tone', delta: 1 }, 'Chat presets');
  assert.deepEqual(phraseEdit(tone), { kind: 'tone', delta: 1 }, 'Phrases');
  assert.deepEqual(roleEdit(tone), { kind: 'tone', delta: 1 }, 'Review roles');
});

/** The page's body rules, joined. */
const body = (html: string): string =>
  stylesheet(html).filter((rule) => rule.selector.trim() === 'body').map((rule) => rule.body).join(' ');

test('the Notifications pages with no script — waiting, unreadable — are drawn in the chosen size too', () => {
  // They carry no control, so no push can reach them: whatever size they are drawn in is the size they stay.
  const unreadable = notificationsPageHtml({ rows: [], dataDir: 'd', older: false, loaded: 0, generation: 1, unreadable: 'EACCES', uiScale: 3 }, 'n');
  const waiting = waitingPageHtml('d', 'n', { size: 3, tone: 0 });

  assert.ok(body(unreadable).includes(`font-size: ${scalePx(3)}px`), `the unreadable page ignores the chosen size: ${body(unreadable)}`);
  assert.ok(body(waiting).includes(`font-size: ${scalePx(3)}px`), `the waiting page ignores the chosen size: ${body(waiting)}`);
});

test('the Notifications title keeps the space it had above and below it', () => {
  const html = notificationsPageHtml({ rows: [], dataDir: 'd', older: false, loaded: 0, generation: 1 }, 'n');
  const header = stylesheet(html).filter((rule) => rule.selector.trim() === 'header').map((rule) => rule.body).join(' ');

  assert.match(header, /margin:\s*16px 0 8px/, `the header that now holds the title lost the title's own margin: ${header}`);
});

test('a number box in the Settings tab grows with the text, or a price is cut off at a large size', () => {
  const fixed = stylesheet(panelHtml(panelState(''), 'n'))
    .filter((rule) => rule.selector.includes('input[type="number"]') && /\bwidth:\s*[\d.]+px/.test(rule.body))
    .map((rule) => rule.selector.trim());

  assert.deepEqual(fixed, [], `these number boxes are still a fixed pixel width: ${fixed.join(' · ')}`);
});

// ---------------------------------------------------------------- the hosts, which import vscode

const SRC = path.join(__dirname, '..', '..', 'src');
const code = (file: string): string => blanked(fs.readFileSync(path.join(SRC, file), 'utf8'));

test('a host whose page posts into a queue of its own takes a text press out of it first', () => {
  // Structural, because these hosts import vscode. Otherwise a press flushed a half-typed edit, and a
  // failed tone write surfaced as the page's own "your text was not saved".
  // The roles' and the commands' queues are their shared editing cores since PLAN_one_model_catalog.md E4.3 / E4.4.
  for (const [file, queue] of [['phrasesPanel.ts', 'writes.queue('], ['rolesPanel.ts', 'queueRoleEdit('], ['commandsPanel.ts', 'queueCommandEdit(']] as const) {
    const source = code(file);
    const heard = source.indexOf('onDidReceiveMessage(');
    const queued = source.indexOf(queue, heard);
    // Found, or the slice below runs to the end of the file and the check passes on anything (it did, after E4.3).
    assert.ok(heard >= 0 && queued > heard, `${file} no longer queues with ${queue} — this guard must name what it does now`);
    const handler = source.slice(heard, queued);

    assert.ok(handler.includes('if (!appliedTextControl('), `${file} queues a text press with its own writes`);
  }
});

test('the Settings tab reads the size and tone when its page is BUILT, not before the render\'s awaits', () => {
  // A press during a render in flight would otherwise be drawn over by the old value, and the next render
  // finds the same paint key and never repaints.
  const source = code('panelProvider.ts');
  const pageFor = source.slice(source.indexOf('private pageFor('), source.indexOf('private pageFor(') + 1600);

  assert.match(pageFor, /settingsHtml\(\{ \.\.\.withCaret\(\), uiScale: currentUiScale\(\), textTone: currentTextTone\(\) \}/);
  // And the new page in the same slot (PLAN_one_model_catalog.md D5), for the same reason.
  assert.match(pageFor, /catalogHtml\(\{ \.\.\.withCaret\(\), uiScale: currentUiScale\(\), textTone: currentTextTone\(\) \}/);
  // Over the raw source: the page name is a string, which the blanked source has emptied.
  assert.match(fs.readFileSync(path.join(SRC, 'panelProvider.ts'), 'utf8'), /private receive\(from: SurfaceSlot, m: PanelMessage\): void \{\s*[\s\S]{0,400}if \(appliedTextControl\(m, 'settings'\)\) \{\s*return;/,
    'the Settings tab\'s presses are not taken out of the dispatcher first');
});
