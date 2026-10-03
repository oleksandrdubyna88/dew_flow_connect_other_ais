import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { blanked } from './blankedSource';

/**
 * The host's half of the busy mark, as WIRED in `PanelProvider` (research/PLAN_model_search_and_busy_marks.md §3.8–§3.12).
 *
 * <p>Structural, for the reason `bothPagesWriteThroughOneQueue.test.ts` gives: `PanelProvider` imports `vscode` and no
 * unit test here can construct it. What each piece DOES is run elsewhere — the settle order by `settleAfterWrite` and
 * the record by `tracked` in `inFlight.test.ts`, the page by `busyMarkPage.test.ts`. What is read here is only that the
 * provider calls them where it must: the own review of E3 found that deleting any of these lines left the suite green.</p>
 */

const SRC = path.join(__dirname, '..', '..', 'src');
const source = blanked(fs.readFileSync(path.join(SRC, 'panelProvider.ts'), 'utf8'));
const raw = fs.readFileSync(path.join(SRC, 'panelProvider.ts'), 'utf8');

/** One method's body, found by its signature — a search that finds nothing fails, never slices from -1. */
function body(signature: string, text = source): string {
  const at = text.indexOf(signature);
  assert.ok(at >= 0, `${signature} could not be found, so this proves nothing`);
  const rest = text.slice(at);

  return rest.slice(0, rest.indexOf('\n  }\n') >= 0 ? rest.indexOf('\n  }\n') : rest.indexOf('\r\n  }\r\n'));
}

test('a setting and a prompt are queued first, then settled — in that order, in the same turn', () => {
  const receive = body('private receive(from: SurfaceSlot, m: PanelMessage): void {');

  assert.match(receive, /this\.enqueue\(\(\) => this\.write\(settingMessageFrom\(m\), from\)\);\s*this\.settleQueued\(from, m\);/,
    'a write is not followed by its settle, or the settle comes first and takes its mark before the write exists');
  assert.match(receive, /this\.enqueue\(\(\) => this\.choosePrompt\(role, round, String\(m\.value\), from\)\);\s*this\.settleQueued\(from, m\);/);
  assert.match(body('private settleQueued(from: SurfaceSlot, m: PanelMessage): void {'),
    /this\.track\(from, m, settleAfterWrite\(this\.writes, this\.renders\), false\)/,
    'the settle no longer waits for the queue and a render after its mark');
});

test('a command is tracked, and a fresh document is answered with what is running', () => {
  const receive = body('private receive(from: SurfaceSlot, m: PanelMessage): void {');

  assert.match(receive, /this\.track\(from, m, \(\) => this\.run\(m\.command, m\.id, from\)\)/, 'a command runs untracked');
  // Over the raw source: the message type is a string, which the blanked source has emptied.
  assert.match(body('private receive(from: SurfaceSlot, m: PanelMessage): void {', raw),
    /m\.type === 'ready'\) \{[\s\S]{0,400}from\.post\(\{ type: 'busy', \.\.\.this\.inFlight\.snapshot\(\) \}\);/,
    'a document that loaded while work ran is never told it is running');
});

test('every painted document carries what is in flight, beside the caret', () => {
  assert.match(body('private pageFor(slot: SurfaceSlot, state: PanelState)'),
    /const withCaret = \(\): PanelState => \(\{ \.\.\.state, focus: slot\.focus\(\), busy: this\.inFlight\.snapshot\(\) \}\);/,
    'a repainted page forgets the work that is still running');
});

test('a render a newer one has already painted over skips its own paint, and renders are counted with a grace', () => {
  // Final E3 round: renders run side by side, so an older one finishing late would put its state back over the newer.
  assert.match(body('private async renderNow(number: number): Promise<void> {'),
    /if \(this\.renders\.superseded\(number\)\) \{\s*return;\s*\}\s*const live = /,
    'a superseded render paints its older state over the newer one');
  assert.match(source, /new RenderTracker\(\(number\) => this\.renderNow\(number\), SETTLE_GRACE_MS\)/,
    'renders are not numbered for the tracker, or a write starts its own render beside the listener’s');
});

test('the panel settles everything when it goes away, and the extension disposes it', () => {
  assert.match(body('dispose(): void {'), /settleEverything\(this\.inFlight\);/);
  const extension = blanked(fs.readFileSync(path.join(SRC, 'extension.ts'), 'utf8'));
  assert.match(extension, /context\.subscriptions\.push\(\{ dispose: \(\) => \{ panel\.dispose\(\); \} \}\);/,
    'nothing calls the panel’s dispose, so a closing window leaves pages marked busy');
});
