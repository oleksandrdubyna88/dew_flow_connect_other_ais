import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { test } from 'node:test';

/**
 * The half of the Users tab that no unit test here can reach.
 *
 * <p>`bugsKeysPanel.ts` imports `vscode`, which throws at the first `require` outside an extension
 * host, so the decisions live in modules a test can load — `bugsKeysFlow`, `bugsKeysPage`,
 * `bugsAdminWire`, `bugsKeysTurns` — and what is left in the panel is the WIRING. This repository
 * has no extension-host harness (`research/module_tests.md` records that as its largest gap), so
 * what is pinned here is the source, which is the house pattern: see `chatFreshWiring.test.ts`.</p>
 *
 * <p>Every assertion below is one of round 2's findings, and every one of them was red on this file
 * before the fix.</p>
 */

const source = (file: string): string =>
  fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

/** Where one member begins and the next one does, so an assertion cannot match a neighbour. */
const between = (text: string, from: string, to: string): string => {
  const start = text.indexOf(from);
  const end = text.indexOf(to);
  assert.ok(start >= 0, `the region does not begin: ${from}`);
  assert.ok(end > start, `the region does not end: ${to}`);

  return text.slice(start, end);
};

/**
 * The same text with every comment gone.
 *
 * <p>Because a structural assertion that reads PROSE goes red on a sentence explaining why the code
 * is the way it is — this repository has been caught by that twice, and both times the comment was
 * right and the test was wrong.</p>
 */
const code = (text: string): string =>
  text.replace(/\/\*[\s\S]*?\*\//gu, '').split('\n').filter((line) => !/^\s*(\/\/|\*)/u.test(line)).join('\n');

test('every door into the panel goes through the one coordinator', () => {
  // A person can open the tab, or set the key from the command palette, while an Issue is in
  // flight. Both used to call `draw` straight out, so a listing fetched before the issuance
  // completed could land AFTER it and paint the page without the pending key on it — the only copy
  // of a live credential, hidden by a redraw nobody asked for. (Code round 2, codex.)
  const panel = code(source('bugsKeysPanel.ts'));

  assert.match(between(panel, 'async show(', 'async askForKey('), /this\.turns\.run\(/u,
    'opening the tab draws outside the coordinator');
  assert.match(between(panel, 'async askForKey(', 'private async askForKeyNow('), /this\.turns\.run\(/u,
    'the command draws outside the coordinator');
  assert.match(between(panel, 'onDidReceiveMessage', 'await this.turns'), /this\.turns\.run\(/u,
    'what the page posts does not reach the coordinator');
});

test('an action that has finished gives the controls back', () => {
  // `busy` disables EVERY control, Refresh included, so a page painted while an action was in
  // flight and never painted again is a dead tab: nothing on it can be pressed, and only closing
  // and reopening it helps. The flag lives in the coordinator now, and the coordinator repaints
  // when it drops. (Code round 2, codex — and `bugzReviewPanel.ts` was caught by the same thing.)
  const panel = code(source('bugsKeysPanel.ts'));

  assert.match(panel, /new Turns\(\{[\s\S]{0,120}started: \(\) => this\.showControls\(true\)/u,
    'nothing says an action has begun, so every control stays live for the ten seconds it runs');
  assert.match(panel, /new Turns\(\{[\s\S]{0,240}settled: \(\) => this\.showControls\(false\)/u,
    'nothing repaints when a turn ends, so the page stays disabled with Refresh disabled too');
  assert.match(between(panel, 'private async users(', 'private paint('), /busy: this\.turns\.busy/u,
    'the page is told it is busy by a flag the coordinator does not own');
});

test('setting the key keeps the page the administrator was on', () => {
  // The trail is a walk through ONE listing and the cursors stay valid across a credential change:
  // there is a single admin surface, so a new key sees the same rows. Resetting to the newest page
  // threw away somebody's position for nothing. (Code round 2, gemini.)
  const asking = code(between(source('bugsKeysPanel.ts'), 'private async askForKeyNow(', 'private async act('));

  assert.match(asking, /await this\.draw\(\);/u, 'the key is set and nothing is redrawn');
  assert.doesNotMatch(asking, /draw\(START\)/u, 'setting the key walks the administrator back to the first page');
});

test('discarding a key is confirmed, like every other revoke', () => {
  // Discard REVOKES. It is the same irreversible act as the Revoke button in the table, which asks
  // first through the funnel — and this one is one press away from the key it is destroying.
  // (Code round 2, gemini.)
  const settling = code(between(source('bugsKeysPanel.ts'), 'private async settlePending(', 'private async discard('));
  const asked = settling.indexOf('notifyAndAsk(');

  assert.ok(asked >= 0, 'discarding a key revokes it without asking');
  assert.ok(settling.indexOf('this.discard(') > asked, 'the key is discarded before the question is answered');
  assert.match(settling, /discard-a-pending-key/u, 'the question does not go through the notification funnel');
});
