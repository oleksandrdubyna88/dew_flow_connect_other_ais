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

test('a page only ASSUMED to be listening is pushed at, and never recorded as told', () => {
  // The code round's sharpest finding, raised by both remote vendors from three roles: the fallback
  // that fires when a page never says `ready` used to call ready() itself, and postMessage answers
  // true for a webview that merely exists — so the ledger recorded every region as delivered to a
  // page that could not receive any of it, and the retry the whole design rests on stopped. The same
  // false delivery the change exists to remove, reintroduced by its own belt.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.assumeListening();

  const push = ledger.next('spots', 'the spots');
  assert.ok(push, 'an assumed page is still pushed at — an empty page for ever is worse');
  ledger.settle(push, true);

  assert.ok(ledger.next('spots', 'the spots'), 'but a true from postMessage is not evidence it arrived');

  // And a page that then finds its voice is believed.
  ledger.ready();
  const heard = ledger.next('spots', 'the spots', true);
  assert.ok(heard);
  ledger.settle(heard, true);
  assert.equal(ledger.next('spots', 'the spots'), undefined, 'once it said ready, delivery counts');
});

test('a push made for a page that has since been replaced is not recorded', () => {
  // The generation orders pushes WITHIN one page; it cannot order them across two, because a rebuild
  // resets what is newest. An in-flight push resolving after the rebuild would otherwise tell the
  // panel the NEW page holds what the OLD one was sent.
  const ledger = new PushLedger();
  ledger.rebuilt();
  ledger.ready();

  const old = ledger.next('rows', 'the rows');
  assert.ok(old);

  ledger.rebuilt();
  ledger.ready();
  ledger.settle(old, true);

  assert.ok(ledger.next('rows', 'the rows'), 'the new page has been told nothing');
});

// ---------- the other half of the handshake: the page ----------

/**
 * Runs the page's own script the way the webview does, and reports what it posted back.
 *
 * <p>Running it rather than matching its text, because what has to be true is an ORDER — the word
 * `ready` is a lie if it leaves before the listener that makes it true, and a regexp over the source
 * cannot see that.</p>
 */
type Element = { innerHTML: string; textContent: string; hidden: boolean; value: string; className: string };

interface RunPage {
  readonly posted: readonly { type?: string }[];
  readonly listeningWhenReady: boolean;
  /** What the page put on screen, by element id. */
  readonly seen: Record<string, Element>;
  /** Delivers a message the way the webview bridge does. */
  readonly deliver: (message: unknown) => void;
  /** Fires every timer the page set, in the order it set them. */
  readonly fireTimers: () => void;
}

function runPage(usageHtml = '', spotsHtml = ''): RunPage {
  const html = roundsLogHtml([], [], 'n0nce', usageHtml, spotsHtml);
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);

  let listening = false;
  let listeningWhenReady = false;
  const posted: { type?: string }[] = [];
  const timers: (() => void)[] = [];
  const listeners: ((event: { data: unknown }) => void)[] = [];
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
    addEventListener(event: string, handler: (event: { data: unknown }) => void) {
      if (event === 'message') {
        listening = true;
        listeners.push(handler);
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
  // Passed in rather than left to the ambient one: the page's own deadline is fifteen seconds, and a
  // test that waited for it would be a test nobody runs.
  const later = (fn: () => void) => {
    timers.push(fn);

    return timers.length;
  };

  new Function('document', 'window', 'acquireVsCodeApi', 'setTimeout', body)(document_, window_, api, later);

  return {
    posted,
    listeningWhenReady,
    seen,
    deliver: (message: unknown) => listeners.forEach((h) => h({ data: message })),
    fireTimers: () => timers.forEach((fire) => fire()),
  };
}

test('the page says it is listening, and only once it actually is', () => {
  // The handshake's page half. Without this word the panel is back to sending blind, and the whole
  // ledger above is bookkeeping over a message VS Code accepted and delivered to nobody.
  const { posted, listeningWhenReady } = runPage();

  assert.ok(posted.some((m) => m.type === 'ready'), 'the page never told the panel it was listening');
  assert.equal(listeningWhenReady, true, 'the page said ready BEFORE attaching its message listener');
});

test('a page that is never told anything says so, where the person is looking', () => {
  // Four findings across both remote vendors and three roles said the same thing: after the panel's
  // five-second fallback the only diagnostic was a console.warn in the extension host, which nobody
  // opens, while the tab read "Reading the log…" for ever. A loading state that never resolves is
  // WORSE than the empty tab it replaced, because it promises something is coming.
  const page = runPage();
  page.fireTimers();

  for (const id of ['usage-body', 'spots-body']) {
    assert.match(page.seen[id]?.innerHTML ?? '', /never received/, `${id} still claims to be reading`);
    assert.match(page.seen[id]?.innerHTML ?? '', /Reload the window/, `${id} says nothing to do about it`);
  }
});

test('a page that WAS told keeps what it was told when the deadline passes', () => {
  // The other side of it: the deadline must not overwrite a section that arrived, which is every
  // ordinary open of the page.
  const page = runPage();
  page.deliver({ type: 'spots', html: '<b>24 blind spots</b>' });
  page.deliver({ type: 'usage', html: '<b>spent</b>' });
  page.fireTimers();

  assert.equal(page.seen['spots-body']?.innerHTML, '<b>24 blind spots</b>');
  assert.equal(page.seen['usage-body']?.innerHTML, '<b>spent</b>');
});

test('a section painted with real data is never replaced by the deadline', () => {
  // The spending tab IS painted from the first render — only the blind spots and the decision counts
  // need the database. Telling somebody their spending "never arrived" because no push happened to
  // change it would be a lie, and a louder one than the silence it replaced.
  const page = runPage('<b>spent</b>');
  page.fireTimers();

  assert.equal(page.seen['usage-body']?.innerHTML ?? '', '', 'the spending section was written over');
  assert.match(page.seen['spots-body']?.innerHTML ?? '', /never received/, 'the empty one still speaks');
});

test('a tab with nothing in it yet says it is reading the log', () => {
  // The first paint is database-free on purpose, so both of these open empty and are filled a moment
  // later. An empty div made a push that never arrived look exactly like one that had not arrived
  // YET — which is why the operator's report could not say which it was.
  const html = roundsLogHtml([], [], 'n0nce');

  assert.match(html, /id="spots-body"><div class="empty">Reading the log…<\/div>/, 'the spots tab opens blank');
  assert.match(html, /id="usage-body"><div class="empty">Reading the log…<\/div>/, 'the spending tab opens blank');

  const filled = roundsLogHtml([], [], 'n0nce', '<b>spent</b>', '<b>missed</b>');
  assert.match(filled, /id="spots-body"><b>missed<\/b><\/div>/, 'real content replaces the hint');
  assert.match(filled, /id="usage-body"><b>spent<\/b><\/div>/, 'in both sections');
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
