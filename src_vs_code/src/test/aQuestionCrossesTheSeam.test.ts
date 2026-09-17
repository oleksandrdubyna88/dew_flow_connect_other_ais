import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatModelChoice, ChatPageState, NO_MARKS, chatPageHtml } from '../chatPage';
import { chatCommandOf } from '../chatMessages';
import { began, isWanted, join, withdraw } from '../chatQueue';
import type { WaitingQuestion } from '../chatPage';

/**
 * The page and the host, joined — issue #288.
 *
 * <p><b>Why this file exists.</b> A page test can be green while a host test is green and the thing
 * between them is wrong: the page can post a command by a name the parser does not know, or leave
 * out a field the host needs, and every assertion on either side passes. That is not hypothetical
 * here — `forgetAChatRow.test.ts` was written because the chat ✕ had perfect markup and the page
 * dropped half its message, and a reviewer asked for exactly this coverage before a line of the
 * queue was written.</p>
 *
 * <p>So this drives the whole crossing that CAN be driven without a `vscode` host: the page's own
 * script is run, its controls are pressed, what it posts goes through the real parser, and the
 * decoded commands are applied to the real queue. Only the extension-host wiring beyond that —
 * `chatCommand.ts`, which needs `vscode` — is out of reach, and it is the one piece the compiler
 * checks end to end.</p>
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
    waiting: [],
    failure: '',
    draft: '',
    marks: NO_MARKS,
    uiScale: 0,
    textTone: 0,
    ...over,
  };
}

interface Running {
  readonly posted: readonly Record<string, unknown>[];
  readonly click: (on: string) => void;
  readonly box: { value: string; disabled: boolean };
}

/**
 * The page, RUN — its script executed the way the webview executes it.
 *
 * <p>Deliberately small: the only elements it needs are the ones the queue touches. A click is
 * delivered to the region's delegated listener with a target that answers `closest` the way a real
 * one would, which is how the waiting rows are reached — they are replaced wholesale on every push,
 * so nothing may be bound to an individual row.</p>
 */
function runPage(over: Partial<ChatPageState> = {}): Running {
  const html = chatPageHtml(state(over), 'n0nce');
  const body = html.split('<script nonce="n0nce">')[1]?.split('</script>')[0] ?? '';
  assert.ok(body.length > 0, 'the page has no script to run');

  const posted: Record<string, unknown>[] = [];
  const listeners = new Map<string, (event: { target: unknown }) => void>();
  const box = { value: '', disabled: false, style: {}, scrollHeight: 0, focus() {}, addEventListener() {} };
  const made = (id: string): Record<string, unknown> => ({
    id,
    innerHTML: '',
    textContent: '',
    hidden: false,
    value: '',
    disabled: false,
    className: '',
    style: {},
    dataset: {},
    scrollTop: 0,
    scrollHeight: 0,
    clientHeight: 0,
    getAttribute: () => null,
    setAttribute() {},
    querySelectorAll: () => [],
    addEventListener(kind: string, fn: (event: { target: unknown }) => void) {
      listeners.set(`${id}:${kind}`, fn);
    },
  });
  const elements = new Map<string, Record<string, unknown>>();
  const document_ = {
    getElementById(id: string) {
      if (id === 'say') { return box; }
      const held = elements.get(id) ?? made(id);
      elements.set(id, held);

      return held;
    },
    addEventListener() {},
    querySelectorAll: () => [],
    querySelector: () => made('any'),
    body: made('body'),
    activeElement: undefined,
  };

  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_,
    {
      addEventListener() {},
      matchMedia: () => ({ matches: false, addEventListener() {} }),
      requestAnimationFrame: () => 0,
      setTimeout: () => 0,
      ResizeObserver: class { observe() {} disconnect() {} },
    },
    () => ({
      postMessage: (message: Record<string, unknown>) => posted.push(message),
      getState: () => undefined,
      setState() {},
    }),
  );

  return {
    posted,
    box,
    click: (on: string) => {
      const fire = listeners.get(`${on}:click`);
      assert.ok(fire, `the page registered no click listener on #${on}, so this test drives nothing`);
      fire({ target: { closest: () => null } });
    },
  };
}

test('two questions typed in a row both reach the host, in the order they were typed', () => {
  // The whole of issue #288, at the seam. The second press used to reach nothing at all, because
  // the page had disabled its own composer the instant the first one was sent.
  const page = runPage({ running: false });

  page.box.value = 'the first question';
  page.click('send');
  page.box.value = 'and the second, while the first is still running';
  page.click('send');

  const sent = page.posted
    .map((message) => chatCommandOf(message))
    .filter((command) => command.kind === 'send');

  assert.deepEqual(
    sent.map((command) => (command.kind === 'send' ? command.text : '')),
    ['the first question', 'and the second, while the first is still running'],
    'the two presses did not arrive as two questions in order',
  );
});

test('what the page posts for a send is a command the parser KNOWS', () => {
  // The failure mode this file exists for: a name or a field the other side does not take. The
  // parser answering `ignore` is a question silently dropped, with both suites green.
  const page = runPage();
  page.box.value = 'ask';
  page.click('send');

  const posts = page.posted.filter((message) => message['command'] === 'send');

  assert.equal(posts.length, 1, 'the press posted no send at all');
  assert.notEqual(chatCommandOf(posts[0]!).kind, 'ignore', `the host ignores what Send posts: ${JSON.stringify(posts[0])}`);
});

test('a withdrawal crosses the seam by ID, and the queue it lands on drops that one only', () => {
  // The page half is asserted where the row is rendered; this is the crossing. The id the row
  // carries is the id the parser hands the host, and the host's queue removes by that id.
  const decoded = chatCommandOf({ type: 'command', command: 'withdraw', id: 'w2' });

  assert.deepEqual(decoded, { kind: 'withdraw', id: 'w2' });

  const waiting: readonly WaitingQuestion[] = [
    { id: 'w1', text: 'first' },
    { id: 'w2', text: 'second' },
    { id: 'w3', text: 'third' },
  ];
  const after = withdraw(waiting, decoded.kind === 'withdraw' ? decoded.id : '');

  assert.deepEqual(after.waiting.map((one) => one.id), ['w1', 'w3']);
  assert.equal(after.returned, 'second', 'the withdrawn words were not handed back');
});

test('a withdrawal that names nothing is IGNORED rather than taken as "the first one"', () => {
  // A stale retained webview posts exactly this. Treating an empty id as a position would cancel
  // somebody's oldest question because a page nobody is looking at woke up.
  assert.equal(chatCommandOf({ type: 'command', command: 'withdraw', id: '' }).kind, 'ignore');
  assert.equal(chatCommandOf({ type: 'command', command: 'withdraw' }).kind, 'ignore');
});

test('the turn that begins asks the queue by name, so a withdrawn question never runs', () => {
  // The end of the crossing: the host holds an id from the moment Send was pressed, and the thing
  // that begins the turn asks whether it is still wanted. This is what makes withdrawal a
  // CANCELLATION rather than an un-drawing — the callback cannot be un-chained.
  const first = join([], 'the one that runs', 'w1');
  assert.ok(first.kind === 'queued');
  const both = join(first.waiting, 'the one taken back', 'w2');
  assert.ok(both.kind === 'queued');

  const afterWithdrawal = withdraw(both.waiting, 'w2').waiting;

  assert.equal(isWanted(afterWithdrawal, 'w1'), true, 'the wrong question was cancelled');
  assert.equal(isWanted(afterWithdrawal, 'w2'), false, 'the withdrawn question would still have run');
  // And the one that does run leaves the queue as it begins, so a row means "not yet started".
  assert.deepEqual(began(afterWithdrawal, 'w1'), []);
});
