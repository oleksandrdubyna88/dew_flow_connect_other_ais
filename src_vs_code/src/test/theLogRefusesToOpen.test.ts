import '../../scripts/inRunFolder.cjs';
import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml } from '../escapeHtml';
import { escapeHtml as escapeHtmlWithQuote, escapeHtmlForHighlighting } from '../webviewHtml';
import { repoNameOf } from '../roundsLog';
import { ViewHandle, isDisposedRejection } from '../viewHandle';

/**
 * What stopped the rounds log opening.
 *
 * 2026-09-08: opening the rounds log produced two notifications — `Webview is disposed` and `e.replace is not a function`. (The held question the log used to
 * show at the top is gone with the held gate, v2 E4; what is left of the story is the two failures.)
 *
 * The second error is the one these tests hold. `escapeHtml` is typed `(text: string)` and calls
 * `text.replace` directly; TypeScript erases that type at run time and every value it escapes comes
 * from JSON on disk. A number or an object in any field produces that message verbatim.
 */

test('escaping survives a value that is not a string', () => {
  // The exact failure. An escaper is the LAST thing before text reaches a document: a page that
  // renders `[object Object]` is strictly better than a log that will not open, because the person
  // can still answer their question and the wrong-looking value is visible rather than fatal.
  const notStrings: readonly unknown[] = [42, { branch: 'main' }, ['a'], true, 0];

  for (const value of notStrings) {
    assert.doesNotThrow(
      () => escapeHtml(value as string),
      `escapeHtml(${JSON.stringify(value)}) must not throw`,
    );
    assert.doesNotThrow(() => escapeHtmlWithQuote(value as string));
    assert.doesNotThrow(() => escapeHtmlForHighlighting(value as string));
  }

  assert.equal(escapeHtml(42 as unknown as string), '42');
});

test('a missing value escapes to nothing, not to the word undefined', () => {
  // `String(undefined)` is `'undefined'`, and a log row reading `undefined` is a defect of its own
  // — one that looks like data. Nullish means absent, and absent renders as absent.
  assert.equal(escapeHtml(undefined as unknown as string), '');
  assert.equal(escapeHtml(null as unknown as string), '');
  assert.equal(escapeHtmlWithQuote(undefined as unknown as string), '');
  assert.equal(escapeHtmlForHighlighting(null as unknown as string), '');
});

test('escaping a real string is untouched by any of that', () => {
  // The guard on the other side: a coercion that changed what escaping DOES would be a behaviour
  // change smuggled in under a bug fix.
  assert.equal(escapeHtml('<b>&"x"</b>'), '&lt;b&gt;&amp;&quot;x&quot;&lt;/b&gt;');
  assert.equal(escapeHtmlWithQuote("it's"), 'it&#39;s');
});

test('a repo path that is not a string does not stop the log', () => {
  // The same class, one function over: `repoNameOf` calls `.replace` on `session.state.repoPath`
  // with no guard at all, and a session file is JSON nobody validates.
  assert.doesNotThrow(() => repoNameOf(7 as unknown as string));
  assert.doesNotThrow(() => repoNameOf(undefined as unknown as string));
  assert.equal(repoNameOf('D:/rsd/dew_flow_connect_other_ais/'), 'dew_flow_connect_other_ais');
});

// ---------- `Webview is disposed` ----------

test('the sidebar stops painting once its view is disposed', () => {
  // The first half of what a person saw. `panelProvider` held a WebviewView and never subscribed to
  // `onDidDispose`, while both sibling panels did — so the handle outlived the view and the
  // escalation watcher repainted into it every five seconds.
  const handle = new ViewHandle<string>();
  handle.hold('view-a');
  assert.equal(handle.view, 'view-a');

  handle.release('view-a');

  assert.equal(handle.view, undefined, 'nothing to paint is an ordinary state, not a failure');
});

test('a disposal from an old view does not blank the new one', () => {
  // The half that would have been a silent bug in the fix itself: VS Code re-creates a hidden view
  // when it is shown again, so A's disposal callback can arrive AFTER B is live. Clearing
  // unconditionally there stops the sidebar updating until something else resolves it.
  const handle = new ViewHandle<string>();
  handle.hold('view-a');
  handle.hold('view-b');

  handle.release('view-a');

  assert.equal(handle.view, 'view-b', "a late callback from a replaced view must not blank the live one");
});

test('the expected rejection is told apart from every other one', () => {
  // Swallowing every `postMessage` rejection would leave a stale sidebar with no reporter for a
  // payload that cannot be cloned or a host channel that fell over.
  assert.equal(isDisposedRejection(new Error('Webview is disposed')), true);
  assert.equal(isDisposedRejection('Webview is disposed'), true, 'a plain string reason still reads');

  assert.equal(isDisposedRejection(new Error('could not be cloned')), false);
  assert.equal(isDisposedRejection(undefined), false, 'a rejection with no reason is not a disposal');
});

test('every webview this extension holds is released when it is disposed', () => {
  // The test that would have caught the original defect, and the only one that can reach
  // `panelProvider` at all: it imports `vscode`, so there is no unit test of it here — the rules it
  // now follows were extracted into `ViewHandle` for exactly that reason, and this holds the wiring
  // that the extracted rules are actually USED.
  //
  // Structural, and therefore paired with a known instance below, so a pattern that stops matching
  // anything cannot pass by matching nothing.
  const read = (file: string) =>
    fs.readFileSync(path.join(__dirname, '..', '..', 'src', file), 'utf8');

  // DISCOVERED, not listed. A hand-written list is a list that goes stale the day somebody adds a
  // fifth webview — and `chatPanel.ts` had already appeared while this very change was in review.
  // RECURSIVE, because a webview owner added under `src/foo/` would otherwise never be discovered
  // and could hold a disposed view with the suite still green. Raised on the code round.
  const root = path.join(__dirname, '..', '..', 'src');
  const walk = (dir: string): string[] => fs.readdirSync(dir, { withFileTypes: true })
    .flatMap((entry) => {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        return entry.name === 'test' ? [] : walk(full);
      }

      return entry.name.endsWith('.ts') ? [path.relative(root, full)] : [];
    });
  const owners = walk(root)
    .filter((file) => /createWebviewPanel\(|resolveWebviewView\(/.test(read(file)));

  assert.ok(owners.length >= 4, `every webview owner is checked; found ${owners.join(', ')}`);
  for (const owner of owners) {
    assert.match(read(owner), /onDidDispose\(/, `${owner} must release the view it holds`);
  }

  // The known instance, so a scan that stopped matching anything cannot pass by matching nothing.
  assert.ok(owners.includes('panelProvider.ts'), 'the sidebar is one of the owners this finds');
  assert.match(read('helpPanel.ts'), /panel\.onDidDispose\(/);

  // And the sidebar releases through its slot, so a late callback from a replaced view cannot blank the
  // live one — the half a bare `this.view = undefined` would have got wrong. That the slot lets go only
  // of the view it holds is RUN in `surfaceSlot.test.ts`; what is read here is only that the sidebar
  // uses it.
  assert.match(read('panelProvider.ts'), /this\.sidebar\.detach\(view\)/);
});

// Two structural tests stood here: that the painted key was recorded only AFTER the html write, and that the
// view was re-read after the awaits. Both rules moved into `SurfaceSlot.paint` (`surfaceSlot.ts`), where they
// are RUN — 'the key is recorded only after the write succeeded' and 'disposing the view it holds lets go' in
// `surfaceSlot.test.ts`. The second had also stopped guarding anything: it sliced from a line that no longer
// existed, so it asserted over one character (`research/PLAN_settings_page.md`, F7).
