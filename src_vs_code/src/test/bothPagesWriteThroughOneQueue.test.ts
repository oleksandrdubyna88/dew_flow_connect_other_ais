import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blanked } from './blankedSource';

/**
 * A control on the Settings tab stores exactly what the same control stored in the sidebar, because
 * both pages are heard by ONE dispatcher and written through ONE queue (`research/PLAN_settings_page.md`,
 * constraint "one implementation of every section"; plan-round finding 14).
 *
 * <p>Structural, because `PanelProvider` imports `vscode` and no unit test here can construct it, and a
 * real-editor scenario cannot post into a webview. What is read is the wiring alone — that each page's
 * listener hands its messages to the same `receive`, naming the page it came from, and that `receive`
 * queues every setting through the one write path. What that path DOES with a message is run elsewhere:
 * the route by `settingWrite.test.ts`, the queue's ordering by `snapBackQueue.test.ts`, and the rule that
 * nothing queued waits on itself by `aQueuedWriteNeverWaitsOnItself.test.ts`.</p>
 */

const source = blanked(fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'panelProvider.ts'), 'utf8'));

test('both pages hand their messages to the one dispatcher, each naming itself', () => {
  assert.match(source, /onDidReceiveMessage\(\(m: PanelMessage\) => \{ this\.receive\(this\.sidebar, m\); \}\)/,
    'the sidebar is heard by something other than the shared dispatcher');
  assert.match(source, /onDidReceiveMessage\(\(m: PanelMessage\) => \{ this\.receive\(this\.settingsTab, m\); \}\)/,
    'the Settings tab is heard by something other than the shared dispatcher, so its controls can store differently');
});

test('the dispatcher queues every setting and every prompt pick through the one write path, for either page', () => {
  const at = source.indexOf('private receive(from: SurfaceSlot, m: PanelMessage): void {');
  // A search that found nothing must FAIL: slicing from -1 reads one character and passes (plan F7).
  assert.ok(at >= 0, 'the dispatcher could not be found, so this proves nothing');
  const receive = source.slice(at);
  const body = receive.slice(0, receive.indexOf('\n  }\n'));

  assert.match(body, /this\.enqueue\(\(\) => this\.write\(settingMessageFrom\(m\), from\)\)/,
    'a setting is written outside the queue, or without the page it came from');
  assert.match(body, /this\.enqueue\(\(\) => this\.choosePrompt\(role, round, String\(m\.value\), from\)\)/,
    'a prompt pick is written outside the queue, or without the page it came from');
});

test('a probe asks whether ANY page is watching, not the sidebar alone (F8)', () => {
  // anyHeld is run in surfaceSlot.test.ts; the line the behaviour depends on is the provider handing it to
  // the Claude probe, and a revert to the sidebar's handle would pass every run test there.
  assert.match(source, /watched: \(\) => anyHeld\(this\.slots\)/,
    'the Claude probe is told something other than whether any page is watching, so the Settings tab can have its probe cancelled');
});
