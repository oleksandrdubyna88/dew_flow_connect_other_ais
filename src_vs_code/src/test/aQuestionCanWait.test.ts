import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatPageState, NO_MARKS, chatPageHtml, chatWaitingHtml } from '../chatPage';
import { ChatModelChoice } from '../chatContracts';

/**
 * The composer while an answer is running — issue #288.
 *
 * <p>A person asked something and Send went dead until the answer arrived. A real explanation was
 * measured at 9.4 s, eight of them silent, and they would rather type the next question and walk
 * away. The host has queued turns since the chain was built — `ask` chains onto `thread.turns`, so
 * the KEYBINDING already waits its turn — and the only door that could not reach that queue was the
 * one they were looking at.</p>
 *
 * <p><b>Two facts wore one name.</b> `locked` was `running || capped`, and those are not the same
 * thing at all: `running` means an answer is on its way, which is exactly when queueing is useful,
 * while `capped` means a remote conversation is FULL and cannot take another turn however long
 * anybody waits. Queueing into a capped conversation would promise something that can never run, so
 * that half of the lock stays exactly as it was.</p>
 */

const MODELS: readonly ChatModelChoice[] = [
  { id: 'antigravity', label: 'Gemini 3.8 Flash', caption: 'local · keeps the conversation' },
];

function state(over: Partial<ChatPageState> = {}): ChatPageState {
  return {
    id: 'conversation-1',
    title: 'привет',
    promptId: '',
    passage: 'The reviewers are read-only.',
    messages: [],
    models: MODELS,
    providers: [],
    reask: '',
    canRetry: false,
    attached: '',
    spend: '',
    promptPresets: [],
    modelPresets: [],
    providerId: 'antigravity',
    chosenModelId: 'antigravity',
    fromSession: true,
    asked: [],
    carryFrom: 0,
    modelId: 'antigravity',
    running: false,
    capped: false,
    turn: 0,
    failure: '',
    draft: '',
    marks: NO_MARKS,
    uiScale: 0,
    textTone: 0,
    waiting: [],
    ...over,
  };
}

/** The composer's own controls, as the rendered page carries them. */
function composer(page: string): { send: string; box: string } {
  const send = /<button type="button" id="send"([^>]*)>/u.exec(page);
  const box = /<textarea id="say"([^>]*)>/u.exec(page);
  assert.ok(send, 'the page has no Send button at all');
  assert.ok(box, 'the page has no composer box at all');

  return { send: send[1] ?? '', box: box[1] ?? '' };
}

// ---------- the composer is live while an answer is running ----------

test('Send is live while an answer is on its way, because that is when queueing is for', () => {
  const { send, box } = composer(chatPageHtml(state({ running: true, turn: 1 }), 'n0nce'));

  assert.ok(!send.includes('disabled'), 'Send is still dead while a turn runs — the whole issue');
  assert.ok(!box.includes('disabled'), 'the box cannot be typed in while a turn runs');
});

test('and a FULL conversation is still locked, because nothing queued into it could ever run', () => {
  // The two halves that used to wear one name. A cap is not a wait: no amount of waiting makes room.
  const { send, box } = composer(chatPageHtml(state({ capped: true }), 'n0nce'));

  assert.ok(send.includes('disabled'), 'a capped conversation would take a question it can never ask');
  assert.ok(box.includes('disabled'), 'and let somebody type it');
});

test('a conversation that is both running and capped stays locked', () => {
  // The cap outranks, for the same reason: the turn in flight is the last one it will answer.
  const { send } = composer(chatPageHtml(state({ running: true, capped: true, turn: 3 }), 'n0nce'));

  assert.ok(send.includes('disabled'));
});

// ---------- what is waiting is on the page ----------

test('a question waiting its turn is shown, with a control to withdraw it', () => {
  const html = chatWaitingHtml([{ id: 'w1', text: 'and what does the sweep do?' }]);

  assert.match(html, /and what does the sweep do\?/u, 'the words are not on the page at all');
  assert.match(html, /data-command="withdraw"/u, 'there is no way to take it back');
  assert.match(html, /data-id="w1"/u, 'the control does not name which one');
});

test('several wait in the order they were typed', () => {
  const html = chatWaitingHtml([
    { id: 'w1', text: 'first' },
    { id: 'w2', text: 'second' },
    { id: 'w3', text: 'third' },
  ]);

  assert.ok(
    html.indexOf('first') < html.indexOf('second') && html.indexOf('second') < html.indexOf('third'),
    'the queue is drawn out of order, which is the one thing a queue must not be',
  );
});

test('nothing waiting draws nothing', () => {
  assert.equal(chatWaitingHtml([]), '', 'an empty queue left a region on the page');
});

test('a waiting question that is markup is escaped, not rendered', () => {
  // A waiting row is text a person PASTED, and the webview holds a privileged message bridge.
  // `.claude/rules/shared/common/security.md` names this exact anti-pattern by example.
  const html = chatWaitingHtml([{ id: 'w1', text: '<img src=x onerror=alert(1)>' }]);

  assert.ok(!html.includes('<img src=x'), 'the question reached the page as markup');
  assert.ok(html.includes('&lt;img src=x'), 'the question was not escaped');
});

test('an id that would break the attribute is escaped rather than trusted', () => {
  const html = chatWaitingHtml([{ id: 'w1" data-command="fresh', text: 'hello' }]);

  assert.ok(!html.includes('data-id="w1" data-command="fresh"'), 'the id forged a second attribute');
});

test('the whole page carries the waiting rows, not only the fragment', () => {
  // The fragment can be perfect while nothing calls it — this repository has shipped that twice.
  const html = chatPageHtml(state({ running: true, turn: 1, waiting: [{ id: 'w1', text: 'the queued one' }] }), 'n0nce');

  assert.match(html, /the queued one/u, 'the page does not render its own waiting list');
});

// ---------- a row is a reminder, not a second composer ----------

test('a very long question is DRAWN short, and the whole of it is still what waits', () => {
  // The ceiling is 64 KB across eight questions, so a row can be a pasted file. Drawing all of it
  // would rebuild tens of kilobytes of escaped HTML on every push that touches this region, and
  // bury the crosses under a wall nobody scrolls. (gemini, the code round.)
  const long = 'x'.repeat(5_000);

  const html = chatWaitingHtml([{ id: 'w1', text: long }]);

  assert.ok(html.length < 2_000, `a 5 000-character question drew ${html.length} characters of row`);
  assert.match(html, /…/u, 'the row was cut without saying so, which reads as a badly typed question');
});

test('and a short one is drawn whole, with no ellipsis invented for it', () => {
  const html = chatWaitingHtml([{ id: 'w1', text: 'and what does the sweep do?' }]);

  assert.match(html, /and what does the sweep do\?</u, 'a short question was cut');
  assert.doesNotMatch(html, /…/u);
});

test('what is cut is still ESCAPED, because cutting is not sanitising', () => {
  // The obvious way to get this wrong is to escape and then slice, which can cut an entity in half
  // and put a bare '&' or a half-written '&lt' on the page.
  const html = chatWaitingHtml([{ id: 'w1', text: `${'<img src=x onerror=alert(1)>'.repeat(40)}` }]);

  assert.ok(!html.includes('<img src=x'), 'the cut question reached the page as markup');
  assert.match(html, /&lt;img src=x/u);
});
