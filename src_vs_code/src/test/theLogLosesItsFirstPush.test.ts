import assert from 'node:assert/strict';
import { test } from 'node:test';
import { PushLedger } from '../pushLedger';
import { logCommandOf } from '../roundsLogMessages';
import { roundsLogHtml } from '../roundsLog';

/**
 * What emptied the rounds log's blind-spot tab and its accepted/rejected counts.
 *
 * 2026-09-08. Nothing was wrong with the data, and that was checked at every step: the installed
 * binary emitted 207 rounds, 24 blind spots and 205 decisions in 143 ms; `parseLog` read all of it;
 * `blindSpotsHtml` built 2968 characters from it.
 *
 * The page is painted WITHOUT the database on purpose — reading it spawns a process, and nobody
 * should wait on that to see their log — and the push that fills it in was sent blind, with the
 * panel recording it as delivered before making it. Every later tick then compared against that
 * record, found nothing changed, and sent nothing. One lost message, permanent.
 */

test('nothing is pushed at a page that has not said it is listening', () => {
  // The half the bookkeeping alone cannot cover: `postMessage` answers TRUE for a webview that
  // EXISTS, and one exists for the whole moment between `webview.html = …` and its script attaching
  // a listener. A message sent in that window is accepted by VS Code and delivered to nobody.
  const ledger = new PushLedger();
  ledger.rebuilt();

  assert.equal(ledger.isListening, false);
  assert.equal(ledger.next('spots', '<div>…</div>'), undefined, 'a push before ready is not made');
});

test('a push that was not delivered is sent again', () => {
  // The defect itself. `void postMessage(…)` with the record written first meant a dropped message
  // could never be noticed, let alone repeated.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const first = ledger.next('spots', '<div>the spots</div>');
  assert.ok(first, 'a listening page is pushed to');
  ledger.settle(first, false);

  assert.ok(ledger.next('spots', '<div>the spots</div>'), 'an undelivered push is made again');
});

test('a push that was delivered is not sent twice', () => {
  // The guard on the other side: this bookkeeping exists so a tick every few seconds does not
  // repaint a page that already holds the same thing.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const push = ledger.next('rows', 'the rows');
  assert.ok(push);
  ledger.settle(push, true);

  assert.equal(ledger.next('rows', 'the rows'), undefined, 'unchanged content is not resent');
  assert.ok(ledger.next('rows', 'different rows'), 'changed content is');
});

test('an older push that arrives late does not overwrite a newer one', () => {
  // Two pushes are in flight whenever an ordinary tick meets the forced answer to `ready`, and the
  // older can resolve second. Committing it then records content the page does not hold, and the
  // page stays stale until something else changes. Raised by two reviewers on the plan round, from
  // opposite directions.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const older = ledger.next('rows', 'old');
  const newer = ledger.next('rows', 'new');
  assert.ok(older && newer);

  ledger.settle(newer, true);
  ledger.settle(older, true);

  assert.equal(ledger.next('rows', 'new'), undefined, 'the page holds the NEW content');
  assert.ok(ledger.next('rows', 'old'), 'and the old content would be a change, not a repeat');
});

test('a forced push is made even when the content has not changed', () => {
  // What `ready` answers with: a page that has just started listening has been told nothing, and
  // the content the panel last delivered to its PREDECESSOR is no guide at all.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const push = ledger.next('usage', 'the usage');
  assert.ok(push);
  ledger.settle(push, true);

  assert.equal(ledger.next('usage', 'the usage'), undefined);
  assert.ok(ledger.next('usage', 'the usage', true), 'forced sends it regardless');
});

test('a rebuilt page is told everything again', () => {
  // VS Code destroys a hidden webview's context and rebuilds it when the tab comes back. A panel
  // that believed its old delivery record would leave the new page exactly as empty as the one the
  // operator reported.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const push = ledger.next('spots', 'the spots');
  assert.ok(push);
  ledger.settle(push, true);
  assert.equal(ledger.next('spots', 'the spots'), undefined);

  ledger.rebuilt();

  assert.equal(ledger.isListening, false, 'and it is not pushed at until it says it is listening');
  ledger.ready();
  assert.ok(ledger.next('spots', 'the spots'), 'the same content is new to a new page');
});

// ---------- the other half of the handshake: the page ----------

/**
 * Runs the page's own script the way the webview does, and reports what it posted back.
 *
 * <p>Running it rather than matching its text, because what has to be true is an ORDER — the word
 * `ready` is a lie if it leaves before the listener that makes it true, and a regexp over the source
 * cannot see that.</p>
 */
function runPage(): { posted: readonly { type?: string }[]; listeningWhenReady: boolean } {
  const html = roundsLogHtml([], [], 'n0nce');
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);

  let listening = false;
  let listeningWhenReady = false;
  const posted: { type?: string }[] = [];
  const element = () => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const seen: Record<string, ReturnType<typeof element>> = {};
  const document_ = {
    getElementById: (id: string) => (seen[id] ??= element()),
    querySelectorAll: () => [],
    addEventListener() {},
  };
  const window_ = {
    addEventListener(event: string) {
      if (event === 'message') {
        listening = true;
      }
    },
  };
  const api = () => ({
    postMessage(message: { type?: string }) {
      if (message.type === 'ready') {
        listeningWhenReady = listening;
      }
      posted.push(message);
    },
  });

  new Function('document', 'window', 'acquireVsCodeApi', body)(document_, window_, api);

  return { posted, listeningWhenReady };
}

test('the page says it is listening, and only once it actually is', () => {
  // The handshake's page half. Without this word the panel is back to sending blind, and the whole
  // ledger above is bookkeeping over a message VS Code accepted and delivered to nobody.
  const { posted, listeningWhenReady } = runPage();

  assert.ok(posted.some((m) => m.type === 'ready'), 'the page never told the panel it was listening');
  assert.equal(listeningWhenReady, true, 'the page said ready BEFORE attaching its message listener');
});

test('a tab with nothing in it yet says it is reading the log', () => {
  // The first paint is database-free on purpose, so both of these open empty and are filled a moment
  // later. An empty div made a push that never arrived look exactly like one that had not arrived
  // YET — which is why the operator's report could not say which it was.
  const html = roundsLogHtml([], [], 'n0nce');

  assert.match(html, /id="spots-body">\s*<div class="empty">Reading the log/, 'the spots tab opens blank');
  assert.match(html, /id="usage-body">\s*<div class="empty">Reading the log/, 'the spending tab opens blank');

  const filled = roundsLogHtml([], [], 'n0nce', '<b>spent</b>', '<b>missed</b>');
  assert.ok(filled.includes('<b>missed</b>'), 'and real content replaces the hint rather than joining it');
  assert.ok(!filled.includes('Reading the log'), 'the hint is gone once there is something to show');
});

// ---------- and what the panel makes of what comes back ----------

test('ready is read as ready, and everything unknown is ignored', () => {
  // The branch that no test in this suite could reach while it lived inside a class importing
  // `vscode`: a typo in this one word ships with every test green and leaves the page empty.
  assert.deepEqual(logCommandOf({ type: 'ready' }), { kind: 'ready' });

  assert.deepEqual(logCommandOf(undefined), { kind: 'ignore' });
  assert.deepEqual(logCommandOf(null), { kind: 'ignore' });
  assert.deepEqual(logCommandOf('ready' as never), { kind: 'ignore' }, 'a bare string is not a message');
  assert.deepEqual(logCommandOf({ type: 'command' }), { kind: 'ignore' }, 'a command with no id is nothing');
  assert.deepEqual(logCommandOf({ type: 'command', command: 'nope', id: 'x' }), { kind: 'ignore' });
});

test('the three things the page can ask for still arrive', () => {
  // The regression guard on the extraction: the log's commands worked before this change and must
  // not be collateral of it.
  assert.deepEqual(logCommandOf({ type: 'command', command: 'answer', id: 'e7' }), { kind: 'answer', id: 'e7' });
  assert.deepEqual(
    logCommandOf({ type: 'command', command: 'usageWindow', id: 'month' }),
    { kind: 'usageWindow', window: 'month' });
  assert.deepEqual(
    logCommandOf({ type: 'command', command: 'forgetUsage', id: 'codex' }),
    { kind: 'forget', provider: 'codex' });
});
