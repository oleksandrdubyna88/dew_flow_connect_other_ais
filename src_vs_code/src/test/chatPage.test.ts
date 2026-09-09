import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ChatModelChoice, ChatPageState, FOLLOW_SLACK_PX, chatCappedHtml, chatMessagesHtml, chatPageHtml, chatPickerHtml, chatStatusHtml, shouldFollow } from '../chatPage';

/**
 * The page, as a string.
 *
 * <p>Every value it renders comes from somewhere untrusted — the passage is another AI's answer, the
 * model captions come from a Team server's catalog, and the title is a tab somebody named. So the
 * escaping is not a nicety here: it is the only thing between a webview and text that was written to
 * be executed somewhere else.</p>
 */

const MODELS: readonly ChatModelChoice[] = [
  { id: 'antigravity', label: 'Gemini 3.8 Flash', caption: 'local · keeps the conversation' },
  { id: 'remsoftdev-codex', label: 'GPT-5.6 (team)', caption: 'remote · no memory, the transcript is re-sent' },
];

function state(over: Partial<ChatPageState> = {}): ChatPageState {
  return {
    id: 'conversation-1',
    title: 'привет',
    passage: 'The reviewers are read-only.',
    messages: [],
    models: MODELS,
    modelId: 'antigravity',
    running: false,
    capped: false,
    turn: 0,
    failure: '',
    draft: '',
    uiScale: 0,
    ...over,
  };
}

test('the passage the conversation is about is on the page', () => {
  const html = chatPageHtml(state({ passage: 'a worktree pinned to a SHA' }), 'n0nce');

  assert.ok(html.includes('a worktree pinned to a SHA'), 'the passage is not shown at all');
});

test('a passage that is markup is escaped, not rendered', () => {
  const html = chatPageHtml(state({ passage: '<img src=x onerror=alert(1)>' }), 'n0nce');

  assert.ok(!html.includes('<img src=x'), 'the passage reached the page as markup');
  assert.ok(html.includes('&lt;img src=x'), 'the passage was not escaped');
});

test('a message containing a closing script tag cannot break out of the page', () => {
  const html = chatPageHtml(
    state({ messages: [{ role: 'model', text: 'write </script><script>alert(1)</script> here' }] }),
    'n0nce',
  );

  // Exactly one script element: the page's own. A second one would mean the message closed it.
  assert.strictEqual(html.split('<script').length - 1, 1, 'a message opened a second script element');
});

test('the zoom control is on the page, because the tab exists to be read', () => {
  const html = chatPageHtml(state(), 'n0nce');

  assert.ok(html.includes('zoomCtl'), 'the ± control is missing');
});

/**
 * The rendered thinking REGION, not the whole document.
 *
 * <p>The page's own script carries the same sentence, because it re-renders this region when the
 * host pushes a state — so a search of the whole HTML for "Thinking…" is true whatever the state is.
 * The first version of these two tests did exactly that, and the idle one failed on its first run,
 * which is the only reason this helper exists.</p>
 */
function thinkingRegion(html: string): string {
  const at = html.indexOf('<div id="thinking">');

  return html.slice(at, html.indexOf('</div>', at));
}

test('a running turn says so, and locks the composer', () => {
  const html = chatPageHtml(state({ running: true }), 'n0nce');

  // Eight of the 9.4 measured seconds are silent; without this the page reads as hung.
  assert.ok(thinkingRegion(html).includes('Thinking…'), 'a running turn is invisible');
  assert.ok(/<textarea[^>]*disabled/.test(html), 'a second turn could be typed into the same pipe');
});

test('an idle page has no thinking line and an open composer', () => {
  const html = chatPageHtml(state(), 'n0nce');

  assert.ok(!thinkingRegion(html).includes('Thinking…'), 'the page claims to be working when it is not');
  assert.ok(!/<textarea[^>]*disabled/.test(html), 'the composer is locked with nothing running');
});

test('a failure is shown in words rather than left to be guessed', () => {
  const html = chatPageHtml(state({ failure: 'agy exited before answering' }), 'n0nce');

  assert.ok(html.includes('agy exited before answering'));
});

test('the picker is hidden when there is only one model to pick', () => {
  assert.strictEqual(chatPickerHtml([MODELS[0]!], 'antigravity'), '');
  assert.strictEqual(chatPickerHtml([], ''), '');
});

test('the picker offers every model and marks the chosen one', () => {
  const picker = chatPickerHtml(MODELS, 'remsoftdev-codex');

  assert.ok(picker.includes('Gemini 3.8 Flash'));
  assert.ok(/value="remsoftdev-codex" selected/.test(picker), 'the chosen model is not selected');
});

test('a remote model says what it cannot do, where the choice is made', () => {
  const picker = chatPickerHtml(MODELS, 'remsoftdev-codex');

  // The panel must not know which kind it holds; the person must, because the difference shows up
  // in latency and in cost.
  assert.ok(picker.includes('no memory'), 'a remote model looks identical to a local one');
});

test('an empty conversation says so rather than showing nothing', () => {
  assert.ok(chatMessagesHtml([]).includes('Nothing asked yet'));
});

test('both sides of the conversation are named', () => {
  const html = chatMessagesHtml([
    { role: 'you', text: 'why' },
    { role: 'model', text: 'because' },
  ]);

  assert.ok(html.includes('You'));
  assert.ok(html.includes('The other AI'));
  assert.ok(html.indexOf('why') < html.indexOf('because'), 'the conversation is out of order');
});

test('a capped conversation offers a way out rather than a locked box', () => {
  const html = chatPageHtml(state({ capped: true }), 'n0nce');

  // The owner capped remote threads at three turns. A page that only disabled the composer would
  // leave somebody with a conversation they cannot continue and no idea what to do next - which is
  // the failure the plan round named. (gemini and codex, independently.)
  assert.ok(html.includes('Start a new conversation'), 'the capped page offers no restart');
  assert.ok(html.includes('Continue with a local model'), 'the capped page does not name the way out');
  assert.ok(/<textarea[^>]*disabled/.test(html), 'a fourth turn could still be typed');
});

test('an uncapped conversation says nothing about a limit', () => {
  const html = chatPageHtml(state(), 'n0nce');

  assert.strictEqual(chatCappedHtml(false), '');
  assert.ok(!html.includes('Start a new conversation'), 'a limit was announced before it was reached');
});

test('the page tells the host when it traps an error, not only itself', () => {
  const html = chatPageHtml(state(), 'n0nce');

  // A trap that only writes into the page leaves the extension believing the conversation is fine
  // while the tab has stopped answering Enter.
  assert.match(html, /postMessage\(\{ type: 'pageError'/, 'the error trap reports to nobody');
});

test('a turn waiting behind other people says so, and one being answered does not', () => {
  // A shared Team server queues twenty deep per person by design, so "still waiting" is minutes —
  // and an unchanging spinner is the one shape a busy server and a broken tab look identical in.
  // The position was already parsed out of every poll and thrown away. (gemini, the code round.)
  assert.match(chatStatusHtml(true, 4, 0), /Thinking/);
  assert.match(chatStatusHtml(true, 4, 0), /4 ahead/);

  // Zero is "no number to show", not position zero: the server says 0 both when it did not count
  // and when the turn has left the queue and is being answered. Neither is a place in a line.
  assert.strictEqual(chatStatusHtml(true, 0, 0), '<p class="thinking">Thinking…</p>');
  assert.strictEqual(chatStatusHtml(true, -1, 0), '<p class="thinking">Thinking…</p>');
  assert.strictEqual(chatStatusHtml(true, 1.5, 0), '<p class="thinking">Thinking…</p>');

  // Nothing at all when no turn is in flight — the region is emptied, not left saying "Thinking…".
  assert.strictEqual(chatStatusHtml(false, 4, 0), '');
});

/**
 * The stylesheet is PARSED, not just concatenated.
 *
 * <p>Found while building the pinned composer: the page's styles opened with a bare
 * `font-size: 13px;` — the zoom — outside any rule, and CSS has no such thing at the top level. A
 * parser consuming a qualified rule appends every token to the prelude until it meets `{`, and `;`
 * does not end one, so the prelude became `font-size: 13px; body` — an invalid selector, and the
 * whole `body` rule with it. Confirmed against a real parser (esbuild reads exactly that as the
 * selector), which is why this is a structural test rather than a string match: a selector cannot
 * contain a semicolon, on this page or any rule added to it later.</p>
 */
interface Rule {
  readonly selector: string;
  readonly body: string;
}

/**
 * The stylesheet as rules — comments stripped FIRST, because that is the order a parser works in
 * and a comment may contain anything, this file's own explanations included. Flat rules only, which
 * is all these pages have; an at-rule with a nested block would need a real parser and this would
 * have to be told so rather than quietly mis-reading it.
 */
function rules(css: string): Rule[] {
  assert.ok(!css.includes('@media') && !css.includes('@supports'), 'this reader cannot see inside a nested at-rule');

  return css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .split('}')
    .flatMap((chunk) => {
      const [selector, body] = chunk.split('{');
      return body === undefined ? [] : [{ selector: selector.trim(), body: body.trim() }];
    });
}

function ruleFor(css: string, selector: string): string {
  const found = rules(css).find((rule) => rule.selector === selector);
  assert.ok(found, `there is no rule for ${selector}`);

  return found.body;
}

test('no rule in the page styles is swallowed by a declaration outside a rule', () => {
  const html = chatPageHtml(state(), 'n0nce');
  const css = html.split('<style>')[1].split('</style>')[0];

  for (const rule of rules(css)) {
    assert.ok(
      !rule.selector.includes(';'),
      `a selector cannot contain ";" — this prelude swallowed the rule after it: ${JSON.stringify(rule.selector)}`,
    );
  }
});

test('the text size the person chose is inside the body rule, so it applies before anything is pushed', () => {
  // scalePx(2) is 13 × 1.1² = 15.73. It must land in the body block itself: the zoom's live push
  // sets body.style.fontSize, so a size that only arrives with a push is a page that opens at the
  // wrong size and corrects itself when something unrelated happens.
  const css = chatPageHtml(state({ uiScale: 2 }), 'n0nce').split('<style>')[1].split('</style>')[0];
  const bodyRule = ruleFor(css, 'body');

  assert.ok(bodyRule.includes('15.73px'), `the chosen text size is not in the body rule: ${bodyRule}`);
});


/**
 * The page's script, RUN.
 *
 * <p>Every test above reads the page as a string, which is the right shape for markup and escaping
 * and the wrong one for a lock, a focus or a scroll — those are behaviour, and a string match on
 * behaviour asserts that a line was written rather than that it works.</p>
 *
 * <p>`bundledPage.test.ts` already RUNS the shipped script — bundled and minified, which is the only
 * place a minifier-renamed binding shows up — but against a stub that answers every id and records
 * nothing. This harness is the other half: the script from source, elements only where the page
 * renders one, listeners captured by name, and host messages delivered through the same `window`
 * listener the webview uses. Neither replaces the other and both are cheap.</p>
 *
 * <p>Modelled on `runPage()` in `theLogLosesItsFirstPush.test.ts`. The fakes record what the page
 * did — listeners by event name, focus calls, the three scroll numbers — and `deliver` pushes a
 * host message through the same `window` listener the webview would.</p>
 */
interface Fake {
  innerHTML: string;
  /** Called when the page writes innerHTML — an insertion, which is what makes the region taller. */
  onWrite?: (value: string) => void;
  textContent: string;
  hidden: boolean;
  value: string;
  disabled: boolean;
  focused: number;
  scrollTop: number;
  clientHeight: number;
  scrollHeight: number;
  style: Record<string, string>;
  listeners: Record<string, Array<(event: unknown) => void>>;
  addEventListener(type: string, fn: (event: unknown) => void): void;
  focus(): void;
  getAttribute(): null;
  setAttribute(): void;
  querySelectorAll(): never[];
}

/** A style bag the page writes into. Its own function so no fixture needs an `as` cast. */
function emptyStyle(): Record<string, string> {
  return {};
}

function fake(): Fake {
  let written = '';

  return {
    get innerHTML() { return written; },
    set innerHTML(value: string) {
      written = value;
      this.onWrite?.(value);
    },
    textContent: '', hidden: false, value: '', disabled: false, focused: 0,
    scrollTop: 0, clientHeight: 0, scrollHeight: 0, style: emptyStyle(), listeners: {},
    addEventListener(type, fn) { (this.listeners[type] ??= []).push(fn); },
    focus() { this.focused += 1; },
    getAttribute: () => null,
    setAttribute() { /* the page sets none */ },
    querySelectorAll: () => [],
  };
}

interface RunningPage {
  seen: Record<string, Fake>;
  posted: Array<Record<string, unknown>>;
  fire(id: string, type: string, event?: Record<string, unknown>): void;
  deliver(message: Record<string, unknown>): void;
  frames(): void;
  /** Put the reader at the bottom of a page tall enough to have one, and make writes grow it. */
  scrolledToBottom(): Fake;
  /** Put the reader a screen above the bottom, same page. */
  scrolledUp(): Fake;
  /** Resolve `document.fonts.ready`. Await it: what it queued runs on the microtask queue. */
  fontsSettle(): Promise<void>;
  /** The composer's height changed — what a growing or shrinking box does to the footer. */
  composerResized(): void;
}

/**
 * An element whose content growing makes the SCROLLING REGION taller, which is what an arriving
 * answer does. Without this the before/after ordering the gate flagged as blocking is invisible: a
 * test whose scrollHeight never moves passes whichever side of the write the snapshot is taken.
 */
function growsTheRegion(region: Fake, by: number): (value: string) => void {
  return (value: string) => {
    if (value.length > 0) {
      region.scrollHeight += by;
    }
  };
}

interface RunOptions extends Partial<ChatPageState> {
  /** Model an older engine: the same hosts that lack `field-sizing` are the case this guards. */
  readonly withoutResizeObserver?: boolean;
}

function runChatPage(over: RunOptions = {}): RunningPage {
  const { withoutResizeObserver, ...state_ } = over;
  const html = chatPageHtml(state(state_), 'n0nce');
  const body = html.split('<script nonce="n0nce">')[1].split('</script>')[0];
  const seen: Record<string, Fake> = {};
  const posted: Array<Record<string, unknown>> = [];
  const pending: Array<() => void> = [];
  const onWindow: Record<string, Array<(event: unknown) => void>> = {};
  // Only the ids the page actually RENDERS answer, and each starts in the state the markup gives it.
  // A registry that fabricates an element for any id asked of it lets a test pass against a page
  // that has nothing to click — the listener is attached to a fake, the behaviour is asserted on a
  // fake, and the shipped webview is inert. (The gate raised this looking ahead to story 3's jump
  // control.) The `disabled` attribute is read across for the same reason: a page rendered locked
  // whose fakes start unlocked is a page the tests cannot see the lock on.
  const rendered = new Map(
    [...html.matchAll(/<[a-z]+[^>]*\bid="([^"]+)"[^>]*>/g)].map((match) => [match[1], match[0]]),
  );
  // The page corrects its opening scroll once the fonts have settled. A test that cannot say WHEN
  // that happens cannot show what a reader who moved in the meantime experiences.
  let settleFonts = () => { /* replaced by the promise below */ };
  const fontsReady = new Promise<void>((resolve) => { settleFonts = () => { resolve(); }; });
  const document_ = {
    getElementById: (id: string) => {
      const tag = rendered.get(id);
      if (tag === undefined) {
        return null;
      }
      if (seen[id] === undefined) {
        seen[id] = fake();
        // Boolean attributes are read ACROSS from the markup. A fake that starts open where the page
        // renders it locked, or visible where the page renders it hidden, is a fake the tests cannot
        // see the initial state on — and both of those were caught by tests that then passed for the
        // wrong reason until this was here.
        seen[id].disabled = / disabled(?=[ >])/.test(tag);
        seen[id].hidden = / hidden(?=[ >])/.test(tag);
      }

      return seen[id];
    },
    querySelectorAll: () => [],
    addEventListener() { /* the page listens on window */ },
    body: { style: emptyStyle() },
    fonts: { ready: fontsReady },
  };
  const window_ = {
    addEventListener(type: string, fn: (event: unknown) => void) { (onWindow[type] ??= []).push(fn); },
  };
  // Captured so a test can say "the composer changed height", which is the event story 4 reacts to.
  let observed: (() => void) | undefined;
  class FakeResizeObserver {
    constructor(callback: () => void) { observed = callback; }
    observe() { /* the page observes one element */ }
    disconnect() { /* nothing to release in a fake */ }
  }


  new Function('document', 'window', 'acquireVsCodeApi', 'requestAnimationFrame', 'ResizeObserver', body)(
    document_,
    window_,
    // `setState` as well as `postMessage`: the page tells VS Code which conversation it is the
    // moment it loads, so a fake without it is a fake the real page cannot run against.
    () => ({ postMessage: (message: Record<string, unknown>) => posted.push(message), setState: () => undefined }),
    (fn: () => void) => { pending.push(fn); },
    withoutResizeObserver === true ? undefined : FakeResizeObserver,
  );

  const region = () => {
    // The page's own opening scroll has already happened by the time a reader scrolls anywhere, so
    // run it out first. Leaving it queued would mean a test that positions a reader and then lets a
    // frame run is watching the page OPEN, not the page follow.
    for (const fn of pending.splice(0)) {
      fn();
    }
    const scroll = document_.getElementById('scroll');
    assert.ok(scroll, 'the page has no scrolling region');
    scroll.clientHeight = 500;
    scroll.scrollHeight = 2000;
    for (const id of ['messages', 'thinking', 'capped', 'failure']) {
      const written = document_.getElementById(id);
      if (written) {
        written.onWrite = growsTheRegion(scroll, 400);
      }
    }

    return scroll;
  };

  return {
    seen,
    posted,
    // Both fire the scroll event a real reader's movement fires. A test that placed somebody
    // silently would be testing a position nobody can reach: you get anywhere in a scrolling region
    // BY scrolling, and the page is entitled to learn where they are the same way.
    scrolledToBottom() {
      const scroll = region();
      scroll.scrollTop = 1500;
      this.fire('scroll', 'scroll');

      return scroll;
    },
    scrolledUp() {
      const scroll = region();
      scroll.scrollTop = 200;
      this.fire('scroll', 'scroll');

      return scroll;
    },
    composerResized() {
      assert.ok(observed, 'the page is not watching the composer for a height change');
      observed();
    },
    fontsSettle() {
      settleFonts();
      // Awaited by the caller: the page registered its own `.then` first, so by the time this
      // resolves for us it has already run. A synchronous "drain" does not drain anything, and a
      // test written that way passes because the callback never ran at all.
      return fontsReady;
    },
    fire(id, type, event = {}) {
      assert.ok(seen[id], `the page never asked for #${id}, so nothing is listening on it`);
      for (const fn of seen[id]?.listeners[type] ?? []) {
        fn({ preventDefault() { /* the page calls this on Enter */ }, ...event });
      }
    },
    deliver(message) {
      for (const fn of onWindow['message'] ?? []) {
        fn({ data: message });
      }
    },
    frames() {
      for (const fn of pending.splice(0)) {
        fn();
      }
    },
  };
}

test('the composer, the Send button, the picker and the hint sit in a pinned footer', () => {
  const html = chatPageHtml(state(), 'n0nce');
  const footer = html.slice(html.indexOf('<footer'), html.indexOf('</footer>'));
  const region = html.slice(html.indexOf('<main'), html.indexOf('</main>'));

  assert.ok(html.indexOf('<footer') > html.indexOf('id="messages"'), 'the footer is not after the conversation');
  for (const inFooter of ['id="pickerBox"', 'id="say"', 'id="send"', 'class="hint"']) {
    assert.ok(footer.includes(inFooter), `${inFooter} is not in the pinned footer`);
  }
  for (const inRegion of ['id="passage"', 'id="messages"', 'id="thinking"', 'id="capped"']) {
    assert.ok(region.includes(inRegion), `${inRegion} is not in the scrolling region`);
  }
});

test('the page has one scrolling region: the body cannot scroll and the region can', () => {
  // The `min-height: 0` is the whole of it. Without it a flex child refuses to shrink below its
  // content, the region never scrolls, the body does instead — and the composer leaves the screen,
  // which is the symptom this plan exists for. (The gate raised it; it cannot be unit-tested any
  // other way than by reading the rule, because there is no layout engine here.)
  const css = chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0];

  assert.match(ruleFor(css, 'body'), /overflow: hidden/, 'the body can still scroll');
  assert.match(ruleFor(css, 'body'), /flex-direction: column/, 'the body is not a flex column');
  assert.match(ruleFor(css, '#scroll'), /min-height: 0/, 'the scrolling region will refuse to shrink');
  assert.match(ruleFor(css, '#scroll'), /overflow-y: auto/, 'the scrolling region does not scroll');
});

test('the passage has no inner scroll and is still visibly the text being discussed', () => {
  const passage = ruleFor(chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0], '.passage');

  assert.ok(!passage.includes('max-height'), 'the passage is still capped, so it has its own scrollbar');
  assert.ok(!passage.includes('overflow-y'), 'the passage still scrolls inside itself');
  assert.match(passage, /border-left/, 'the passage stopped looking like the text being discussed');
});

test('the Send button is locked exactly when the textarea is', () => {
  for (const [over, locked] of [[{ running: true }, true], [{ capped: true }, true], [{}, false]] as const) {
    const html = chatPageHtml(state(over), 'n0nce');
    const button = html.slice(html.indexOf('id="send"'), html.indexOf('>', html.indexOf('id="send"')));
    const box = html.slice(html.indexOf('id="say"'), html.indexOf('>', html.indexOf('id="say"')));

    assert.strictEqual(button.includes('disabled'), locked, `the Send button's lock disagrees with ${JSON.stringify(over)}`);
    assert.strictEqual(box.includes('disabled'), locked, `the textarea's lock disagrees with ${JSON.stringify(over)}`);
  }

  // And it keeps agreeing when the host pushes a state, which is where the two could drift apart.
  const page = runChatPage();
  page.deliver({ type: 'state', running: true, capped: false });
  assert.strictEqual(page.seen['send']?.disabled, true, 'a running turn left the Send button open');
  page.deliver({ type: 'state', running: false, capped: false });
  assert.strictEqual(page.seen['send']?.disabled, false, 'the finished turn left the Send button locked');
});

test('the Send button and Enter share one send, wired inside the nonced script', () => {
  const html = chatPageHtml(state(), 'n0nce');
  const script = html.split('<script nonce="n0nce">')[1].split('</script>')[0];
  const body = html.slice(html.indexOf('<body>'), html.indexOf('<script'));

  assert.strictEqual(script.split('function send()').length - 1, 1, 'there is more than one way to send');
  assert.match(html, /<button type="button" id="send"/, 'Send is not a plain button');
  // CSP is `script-src 'nonce-…'`, so an inline handler would be blocked and the button would be
  // dead. Every listener is attached in the nonced script.
  assert.doesNotMatch(body, / on[a-z]+="/, 'a control carries an inline handler the CSP will block');

  // Both paths reach that one send — with the turn between them finished, because a send now locks
  // the composer until the host says the turn is over. Firing them back to back would be asserting
  // the defect the gate found rather than the two callers.
  const page = runChatPage();
  page.seen['say'].value = 'what does this do';
  page.fire('send', 'click');
  page.deliver({ type: 'state', running: false, capped: false });
  page.seen['say'].value = 'and this';
  page.fire('say', 'keydown', { key: 'Enter', shiftKey: false });

  const sends = page.posted.filter((message) => message['command'] === 'send');
  assert.strictEqual(sends.length, 2, 'the button and Enter did not both send');
  assert.deepStrictEqual(sends.map((message) => message['text']), ['what does this do', 'and this']);
});

test('a send locks the composer at once, before the host has said anything', () => {
  // The gap the gate found, raised by two vendors independently: between posting and the host's
  // `running: true` the composer stayed open, so a second Enter — or a click, now that there is a
  // button — put a second turn down a pipe that carries one. Nothing on the host end had gone wrong
  // yet; the page simply had not been told, and it did not need telling.
  const page = runChatPage();
  page.seen['say'].value = 'the first question';
  page.fire('send', 'click');

  assert.strictEqual(page.seen['say'].disabled, true, 'the box stayed open between the send and the answer');
  assert.strictEqual(page.seen['send'].disabled, true, 'the Send button stayed open between the send and the answer');

  page.seen['say'].value = 'and immediately a second';
  page.fire('send', 'click');
  page.fire('say', 'keydown', { key: 'Enter', shiftKey: false });
  assert.strictEqual(
    page.posted.filter((message) => message['command'] === 'send').length,
    1,
    'a second turn went down the pipe while the first was still in flight',
  );
});

test('the composer comes back with the focus when the turn ends', () => {
  // send() deliberately does NOT focus: it locks, and focusing a control you have just disabled is
  // how a caret ends up in a box nobody can type in. The page already returned focus when a lock
  // lifts, and that is now the single path — one place decides, whichever way the turn went.
  const page = runChatPage();
  page.seen['say'].value = 'ask';
  page.fire('send', 'click');
  const stolen = page.seen['say'].focused;

  page.deliver({ type: 'state', running: false, capped: false });

  assert.ok(page.seen['say'].focused > stolen, 'the box did not get the focus back when the turn ended');
  assert.strictEqual(page.seen['say'].disabled, false, 'the finished turn left the box locked');
});

test('a locked composer posts nothing, however it is asked', () => {
  const page = runChatPage({ running: true });
  page.seen['say'].value = 'while a turn runs';

  page.fire('send', 'click');
  page.fire('say', 'keydown', { key: 'Enter', shiftKey: false });

  assert.strictEqual(page.posted.filter((message) => message['command'] === 'send').length, 0,
    'a locked composer sent a turn');
});


/* ------------------------------------------------------------------------------------------------
 * Story 2: the decided scroll rule. Entry 23 of the operator's bug list, settled on 2026-09-09 and
 * implemented ONCE here, because three separate fixes each deciding it their own way is how a page
 * ends up behaving differently depending on which of them you provoked.
 * ---------------------------------------------------------------------------------------------- */

test('shouldFollow: at the bottom follows, within the slack follows, a screen up does not', () => {
  // The boundary is a number, not "a line or two" — the gate asked for exactly that. 48px is about
  // two lines at the base size and three at the largest zoom step, and it is a constant rather than
  // something derived from the font, because a reader one pixel short of the bottom considers
  // themselves at the bottom whatever size they read at.
  assert.strictEqual(FOLLOW_SLACK_PX, 48);

  const at = (top: number) => shouldFollow(top, 500, 1000, FOLLOW_SLACK_PX);
  assert.strictEqual(at(500), true, 'the reader was exactly at the bottom');
  assert.strictEqual(at(452), true, 'the reader was inside the slack');
  assert.strictEqual(at(451), false, 'one pixel past the slack still followed');
  assert.strictEqual(at(0), false, 'a reader a screen up was yanked to the bottom');

  // Overscroll, and a page with nothing to scroll, are both "at the bottom".
  assert.strictEqual(shouldFollow(600, 500, 1000, 48), true, 'overscroll did not count as the bottom');
  assert.strictEqual(shouldFollow(0, 500, 300, 48), true, 'a page shorter than its viewport did not follow');

  // A page that cannot measure itself is a page whose reader has not scrolled: follow.
  assert.strictEqual(shouldFollow(Number.NaN, 500, 1000, 48), true, 'an unmeasurable page refused to follow');
});

test('the page runs the shouldFollow the tests ran, and the slack they share', () => {
  // The rounds log learned this twice: a function referenced only from a template string is a
  // function the minifier renames out from under the page. Embedding the source is what makes the
  // tested function and the shipped function the same one.
  const script = chatPageHtml(state(), 'n0nce').split('<script nonce="n0nce">')[1];

  assert.ok(script.includes(shouldFollow.toString()), 'the page does not run the tested shouldFollow');
  assert.ok(script.includes(String(FOLLOW_SLACK_PX)), 'the page does not use the tested slack');
});

test('the at-bottom decision is taken before anything is inserted, on every path', () => {
  // The blocking finding: scrollHeight read AFTER a write is the height including the write, so a
  // reader who was at the bottom reads as "not at the bottom" and is never followed — rule 2 turns
  // silently into "never follow". Each region is pushed on its own, because each is an insertion.
  for (const push of ['messagesHtml', 'thinkingHtml', 'cappedHtml', 'failureHtml']) {
    const page = runChatPage();
    const scroll = page.scrolledToBottom();

    page.deliver({ type: 'state', [push]: '<p>an answer</p>' });
    page.frames();

    assert.strictEqual(scroll.scrollTop, scroll.scrollHeight,
      `a reader at the bottom was not followed when ${push} arrived`);
  }
});

test('a reader who scrolled up is left where they were, on every path', () => {
  for (const push of ['messagesHtml', 'thinkingHtml', 'cappedHtml', 'failureHtml']) {
    const page = runChatPage();
    const scroll = page.scrolledUp();

    page.deliver({ type: 'state', [push]: '<p>an answer</p>' });
    page.frames();

    assert.strictEqual(scroll.scrollTop, 200, `a reader was yanked to the bottom when ${push} arrived`);
  }
});

test('the follow happens after layout, not in the tick that inserted the content', () => {
  // Setting scrollTop in the same tick as the write scrolls to a height the browser has not laid
  // out yet, which lands short. The scroll is handed to the next frame.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  assert.strictEqual(scroll.scrollTop, 1500, 'the page scrolled inside the tick that inserted');

  page.frames();
  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'the page never scrolled after layout');
});

test('a state that inserts nothing does not scroll a reader anywhere', () => {
  const page = runChatPage();
  const scroll = page.scrolledUp();

  page.deliver({ type: 'state', running: true, capped: false });
  page.frames();

  assert.strictEqual(scroll.scrollTop, 200, 'a state with no insertion moved the page');
});

test('on open the page lands on the last message, and again once the fonts have settled', () => {
  // Rule 1, and the second blocking finding: scrolling at script time lands mid-page, because the
  // heights are not final until the fonts are. Immediately, then after layout, then after the fonts
  // — idempotent, so three calls cost nothing and the first paint is already close.
  const page = runChatPage({ messages: [{ role: 'model', text: 'the last thing said' }] });
  const scroll = page.seen['scroll'];
  assert.ok(scroll, 'the page has no scrolling region');

  // The page scrolled once at script time, before any of this test ran.
  scroll.scrollHeight = 3000;
  page.frames();
  assert.strictEqual(scroll.scrollTop, 3000, 'the page did not land on the last message after layout');
});


test('a reader who scrolls away before the frame runs is not yanked back', () => {
  // The gate's best finding on this story, raised from two vendors: the follow is DEFERRED to the
  // next frame, and a reader can move in between — by dragging, by a wheel, by Page Up. The scroll
  // that was correct when it was scheduled is a hijack by the time it runs. Re-measuring in the
  // callback cannot answer it, because the insertion has already made a bottom reader measure short;
  // what is needed is knowing whether the READER moved, which only they can tell us.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  scroll.scrollTop = 100;
  page.fire('scroll', 'scroll');
  page.frames();

  assert.strictEqual(scroll.scrollTop, 100, 'the pending follow overrode a reader who had scrolled away');
});

test('the page scrolling itself does not count as the reader scrolling', () => {
  // Setting scrollTop fires a scroll event. If that cancelled the next follow, the page would
  // follow one answer and then never again — the rule would work once per tab.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>first</p>' });
  page.frames();
  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'the first answer was not followed');

  scroll.scrollTop = scroll.scrollHeight - 10;
  page.deliver({ type: 'state', messagesHtml: '<p>second</p>' });
  page.frames();
  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'a follow after the page moved itself was cancelled');
});

test('the slack the page follows by is the one that was tested, at the boundary', () => {
  // A pure-function test passes while the call site hands the page a different number, or none. This
  // drives the real script at exactly the boundary, both sides of it, through a real push.
  for (const [remaining, followed] of [[FOLLOW_SLACK_PX, true], [FOLLOW_SLACK_PX + 1, false]] as const) {
    const page = runChatPage();
    const scroll = page.scrolledToBottom();
    scroll.scrollTop = scroll.scrollHeight - scroll.clientHeight - remaining;
    const before = scroll.scrollTop;

    page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
    page.frames();

    assert.strictEqual(scroll.scrollTop === scroll.scrollHeight, followed,
      `with ${remaining}px remaining the page ${followed ? 'did not follow' : 'followed'}`);
    if (!followed) {
      assert.strictEqual(scroll.scrollTop, before, 'a reader past the slack was moved anyway');
    }
  }
});

test('a push that changes nothing scrolls nobody, however many times it arrives', () => {
  // A retry, or a poll that pushes the same state again, is not an insertion. Counting the
  // ASSIGNMENT rather than the change would scroll a reader for content they are already looking at.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  page.frames();
  const after = scroll.scrollTop;

  scroll.scrollTop = after - 300;
  page.fire('scroll', 'scroll');
  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  page.frames();

  assert.strictEqual(scroll.scrollTop, after - 300, 'an identical push moved the reader');
});


test('a reader who leaves and comes back to where the page left them still cancels the follow', () => {
  // The position marker was permanent, and that is a magic pixel: a reader who scrolls away and
  // returns to it — which is exactly what "scroll back down to the bottom" is — read as the page's
  // own scroll for the rest of the tab's life, so their next movement never cancelled anything.
  // It is one-shot now: our own scroll event consumes it, and everything after is the reader.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>first</p>' });
  page.frames();
  const wherePageLeftThem = scroll.scrollTop;
  page.fire('scroll', 'scroll');

  page.deliver({ type: 'state', messagesHtml: '<p>second</p>' });
  scroll.scrollTop = wherePageLeftThem;
  page.fire('scroll', 'scroll');
  page.frames();

  assert.strictEqual(scroll.scrollTop, wherePageLeftThem,
    'a reader standing where the page had left them was moved by a follow they had cancelled');
});

test('the fonts settling does not undo a reader who has already scrolled away', async () => {
  // Three findings from one vendor said the same thing: the opening follow is cancellable, and the
  // fonts-settled correction was not — it made a FRESH token, so a cancelled open came back to life
  // whenever the fonts happened to settle. A reader who opened a tab and started reading upward was
  // dragged to the bottom by a font.
  const page = runChatPage({ messages: [{ role: 'model', text: 'something long' }] });
  const scroll = page.seen['scroll'];
  assert.ok(scroll, 'the page has no scrolling region');
  scroll.clientHeight = 500;
  scroll.scrollHeight = 3000;

  scroll.scrollTop = 200;
  page.fire('scroll', 'scroll');
  await page.fontsSettle();
  page.frames();

  assert.strictEqual(scroll.scrollTop, 200, 'the fonts settling yanked a reader who had scrolled up');
});

test('a state that omits the capped notice clears it, as it always did', () => {
  // A regression this branch introduced and the gate caught: requiring a string before writing meant
  // an omitted field left the old notice on screen. The protocol has always treated "not mentioned"
  // as "gone" for these two regions, and a stale cap notice is one a person acts on.
  const page = runChatPage();

  page.deliver({ type: 'state', cappedHtml: '<div class="capped">no more turns</div>', failureHtml: '<p>it broke</p>' });
  assert.match(page.seen['capped'].innerHTML, /no more turns/, 'the capped notice was never shown');
  assert.match(page.seen['failure'].innerHTML, /it broke/, 'the failure was never shown');

  page.deliver({ type: 'state', running: false, capped: false });

  assert.strictEqual(page.seen['capped'].innerHTML, '', 'an omitted capped notice stayed on screen');
  assert.strictEqual(page.seen['failure'].innerHTML, '', 'an omitted failure stayed on screen');
});


/* ------------------------------------------------------------------------------------------------
 * Story 3: rule 3 of the decided behaviour — a reader who was NOT followed is told that something
 * arrived, and given one way back to it.
 * ---------------------------------------------------------------------------------------------- */

test('the jump control is an overlay, so showing it moves no layout', () => {
  // If it took room in the footer, showing it would shrink the scrolling region — which changes
  // clientHeight, which is one of the three numbers the follow decision is made from. A control that
  // appears BECAUSE a reader was not followed must not alter what "at the bottom" means.
  const html = chatPageHtml(state(), 'n0nce');
  const css = html.split('<style>')[1].split('</style>')[0];
  const footer = html.slice(html.indexOf('<footer'), html.indexOf('</footer>'));

  assert.ok(footer.includes('id="jump"'), 'the jump control is not in the footer');
  assert.match(html, /<button type="button" id="jump"[^>]*hidden/, 'the jump control is not a hidden plain button');
  assert.match(ruleFor(css, '.jump'), /position: absolute/, 'the jump control takes room in the layout');
  assert.match(ruleFor(css, '#composer'), /position: relative/, 'the overlay has nothing to be positioned against');
});

test('an answer that lands out of view offers the way back to it; one that lands in view does not', () => {
  const away = runChatPage();
  away.scrolledUp();
  away.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  away.frames();
  assert.strictEqual(away.seen['jump'].hidden, false, 'a reader who was not followed was never told an answer arrived');

  const there = runChatPage();
  there.scrolledToBottom();
  there.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  there.frames();
  assert.strictEqual(there.seen['jump'].hidden, true, 'a reader who was followed was offered a jump to where they already are');
});

test('a push that inserts nothing offers no jump', () => {
  const page = runChatPage();
  page.scrolledUp();

  page.deliver({ type: 'state', running: true, capped: false });
  page.frames();

  assert.strictEqual(page.seen['jump'].hidden, true, 'a state with no insertion offered a jump to nothing new');
});

test('pressing jump goes to the newest, retires itself, and hands the box back the focus', () => {
  const page = runChatPage();
  const scroll = page.scrolledUp();
  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  page.frames();
  const focusedBefore = page.seen['say'].focused;

  page.fire('jump', 'click');

  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'the jump did not go to the newest message');
  assert.strictEqual(page.seen['jump'].hidden, true, 'the jump control stayed up after doing its one job');
  assert.ok(page.seen['say'].focused > focusedBefore, 'the jump left the focus on itself');
});

test('a reader who scrolls back down on their own retires the jump control', () => {
  // Its lifecycle is not only "click me": a person who returns under their own steam has answered
  // the question it was asking, and a control that stays up after that is one nobody trusts.
  const page = runChatPage();
  const scroll = page.scrolledUp();
  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  page.frames();
  assert.strictEqual(page.seen['jump'].hidden, false, 'the jump control was never offered');

  scroll.scrollTop = scroll.scrollHeight - scroll.clientHeight;
  page.fire('scroll', 'scroll');

  assert.strictEqual(page.seen['jump'].hidden, true, 'the jump control outlived the reader arriving');
});

test('a locked composer does not get the focus from a jump', () => {
  // While a turn runs the box is disabled; focusing it would put the caret where nobody can type.
  const page = runChatPage({ running: true });
  const scroll = page.scrolledUp();
  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>', running: true, capped: false });
  page.frames();
  const focusedBefore = page.seen['say'].focused;

  page.fire('jump', 'click');

  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'the jump did not scroll while a turn ran');
  assert.strictEqual(page.seen['say'].focused, focusedBefore, 'the jump focused a box nobody can type in');
});


/* ------------------------------------------------------------------------------------------------
 * Story 4: the composer grows with the question, up to 30 % of the viewport, and its height changing
 * never moves a reader who did not scroll.
 * ---------------------------------------------------------------------------------------------- */

test('the composer has a ceiling of thirty percent of the viewport, a floor, and scrolls inside past it', () => {
  const css = chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0];
  const box = ruleFor(css, 'textarea');

  assert.match(box, /max-height: 30vh/, 'the composer has no ceiling');
  assert.match(box, /min-height: 64px/, 'the composer lost its floor');
  assert.match(box, /overflow-y: auto/, 'past the ceiling the text has nowhere to go');
  assert.match(box, /field-sizing: content/, 'the composer does not size itself to its content');
});

test('a host without field-sizing gets the input handler, and the ceiling stays the one in the CSS', () => {
  // The gate's blocking finding on the plan: `field-sizing` is Chromium-only and this extension
  // declares support back to VS Code 1.85, whose engine has never heard of it. On such a host the
  // box would silently never grow — it works on the machine it was written on, which is the whole
  // shape of the defect. Node has no `CSS` at all, so the harness IS that host.
  const page = runChatPage();
  const box = page.seen['say'];

  box.value = 'one\ntwo\nthree\nfour';
  box.scrollHeight = 220;
  page.fire('say', 'input');

  assert.deepStrictEqual(box.style['height'], '220px', 'the fallback did not size the box to its content');
  const script = chatPageHtml(state(), 'n0nce').split('<script nonce="n0nce">')[1];
  assert.match(script, /CSS\.supports\('field-sizing', 'content'\)/, 'the page does not ask whether it needs the fallback');
  assert.doesNotMatch(script, /innerHeight \* 0\.3|30 \/ 100/, 'the page computed a second ceiling of its own');
});

test('the composer shrinks back after a send and after the text is deleted', () => {
  // A ceiling with no way down is a box that stays tall for the rest of the conversation.
  const page = runChatPage();
  const box = page.seen['say'];

  box.value = 'one\ntwo\nthree';
  box.scrollHeight = 180;
  page.fire('say', 'input');
  assert.strictEqual(box.style['height'], '180px');

  box.scrollHeight = 64;
  page.fire('send', 'click');
  assert.strictEqual(box.style['height'], '64px', 'the box stayed tall after the question was sent');

  box.value = 'one\ntwo';
  box.scrollHeight = 120;
  page.fire('say', 'input');
  box.value = 'one';
  box.scrollHeight = 64;
  page.fire('say', 'input');
  assert.strictEqual(box.style['height'], '64px', 'the box stayed tall after its text was deleted');
});

test('a composer that grows keeps a reader at the bottom at the bottom', () => {
  // The other side of rule 2: a height change nobody asked for must not move the conversation out
  // from under someone. Here the flag IS right where re-measuring is not, because the reader did not
  // scroll — the region shrank underneath them.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();
  scroll.scrollTop = scroll.scrollHeight - scroll.clientHeight;

  scroll.clientHeight -= 100;
  page.composerResized();
  page.frames();

  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight, 'a growing composer pushed the last answer out of view');
});

test('a composer that grows leaves a reader who scrolled up where they were', () => {
  const page = runChatPage();
  const scroll = page.scrolledUp();

  scroll.clientHeight -= 100;
  page.composerResized();
  page.frames();

  assert.strictEqual(scroll.scrollTop, 200, 'a growing composer moved a reader who was reading something else');
});

test('a follow the reader cancels leaves the jump control behind it', () => {
  // The narrow race the gate found: an answer arrives while the reader is at the bottom, so a follow
  // is scheduled rather than done — and the reader scrolls up inside that frame. The follow is
  // correctly cancelled, and without this the reader is left scrolled up, with an answer they were
  // never taken to and nothing on screen saying it came.
  const page = runChatPage();
  const scroll = page.scrolledToBottom();

  page.deliver({ type: 'state', messagesHtml: '<p>an answer</p>' });
  scroll.scrollTop = 100;
  page.fire('scroll', 'scroll');
  page.frames();

  assert.strictEqual(scroll.scrollTop, 100, 'the cancelled follow ran anyway');
  assert.strictEqual(page.seen['jump'].hidden, false, 'an answer arrived, nobody was taken to it, and nobody was told');
});

test('a composer that grows re-pins the reader even where nothing observes it', () => {
  // Requirement 8 was promised unconditionally and delivered only through ResizeObserver. Where the
  // host has none — the same older engines that lack field-sizing — the box grows, the footer eats
  // the room, and the last answer slides behind it with nothing to notice. The fallback that sizes
  // the box knows it changed the height, so it is the one place that can say so.
  const page = runChatPage({ withoutResizeObserver: true });
  const scroll = page.scrolledToBottom();
  scroll.scrollTop = scroll.scrollHeight - scroll.clientHeight;

  page.seen['say'].value = 'one\ntwo\nthree';
  page.seen['say'].scrollHeight = 180;
  scroll.clientHeight -= 100;
  page.fire('say', 'input');
  page.frames();

  assert.strictEqual(scroll.scrollTop, scroll.scrollHeight,
    'the growing box pushed the last answer out of view on a host with no ResizeObserver');
});


/* ------------------------------------------------------------------------------------------------
 * An answer reads like a document: entries 4, 5, 6, 7, 8 and 18 of the operator's list, which are
 * one rendering pass.
 * ---------------------------------------------------------------------------------------------- */

test('a model answer is rendered as a document; what the person typed is not', () => {
  const html = chatMessagesHtml([
    { role: 'you', text: '### not a heading, I typed this' },
    { role: 'model', text: '### a heading\n\n- and a list' },
  ]);

  const mine = html.slice(html.indexOf('msg you'), html.indexOf('msg model'));
  const theirs = html.slice(html.indexOf('msg model'));

  assert.match(theirs, /<h3>a heading<\/h3>/, 'the answer was not rendered');
  assert.match(theirs, /<ul><li>and a list<\/li><\/ul>/, 'the list was not rendered');
  assert.match(mine, /### not a heading/, 'what the person typed was rendered as markdown');
  assert.doesNotMatch(mine, /<h3>/, 'the person typed a hash and got a heading');
});

test('the person is on the right and the model on the left, each in its own colour', () => {
  const css = chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0];

  assert.match(ruleFor(css, '.msg.you'), /margin-left: auto/, 'the person is not on their own side');
  assert.match(ruleFor(css, '.msg.you'), /border-right/, 'the person has no colour of their own');
  assert.match(ruleFor(css, '.msg.model'), /border-left/, 'the model has no colour of its own');
  assert.match(ruleFor(css, '.msg'), /max-width/, 'a line spans the whole tab, so the sides say nothing');
  // The edge is the colour, not a filled box — the decision already shipped for the reviewer cards.
  assert.doesNotMatch(ruleFor(css, '.msg.you'), /background/, 'the sides became filled boxes');
});

test('an answer ends with a rule, and the person\'s own message does not', () => {
  const html = chatMessagesHtml([{ role: 'you', text: 'ask' }, { role: 'model', text: 'answer' }]);

  assert.strictEqual((html.match(/<hr class="end">/g) ?? []).length, 1,
    'the end rule is not exactly once, after the answer');
  assert.ok(html.indexOf('<hr class="end">') > html.indexOf('answer'), 'the rule is not after the answer');
});

test('every answer carries a way to copy the markdown it arrived as', () => {
  const html = chatMessagesHtml([{ role: 'you', text: 'ask' }, { role: 'model', text: '**answer**' }]);

  assert.match(html, /<button type="button" class="copy" data-copy="1"/, 'an answer cannot be copied');
  assert.strictEqual((html.match(/class="copy"/g) ?? []).length, 1, 'the person\'s own message got a copy control');
});

test('the prose is the editor\'s foreground and has room to breathe', () => {
  const css = chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0];

  // Scoped to the prose rather than reset on body: the chrome around it — inputs, the hint, the
  // picker — belongs to `--vscode-foreground`, and a seam between the two is what a blanket
  // override produces. (gemini, the plan round.)
  assert.match(ruleFor(css, '.msg .what'), /var\(--vscode-editor-foreground\)/, 'the prose is the chrome colour');
  assert.match(ruleFor(css, '.msg .what'), /line-height/, 'the lines have no room between them');
  assert.doesNotMatch(ruleFor(css, '.msg.you .what'), /opacity/, 'the person\'s own words are dimmed');
});

test('a link posts to the host and never navigates the page itself', () => {
  const page = runChatPage();
  page.deliver({
    type: 'state',
    messagesHtml: '<div class="msg model"><div class="what">'
      + '<a class="link" data-open="https://example.com">docs</a>'
      + '<a class="link" data-file="src/a.ts" data-line="12">a.ts</a></div></div>',
  });

  page.fire('messages', 'click', { target: { closest: () => ({ dataset: { open: 'https://example.com' } }) } });
  page.fire('messages', 'click', { target: { closest: () => ({ dataset: { file: 'src/a.ts', line: '12' } }) } });

  const opened = page.posted.filter((message) => message['command'] === 'openLink');
  assert.strictEqual(opened.length, 2, 'the page did not ask the host to open either link');
  assert.deepStrictEqual(opened[0], { type: 'command', command: 'openLink', url: 'https://example.com' });
  assert.deepStrictEqual(opened[1], { type: 'command', command: 'openLink', file: 'src/a.ts', line: 12 });
});

test('a copy control asks the host for the message it names', () => {
  const page = runChatPage();
  page.deliver({ type: 'state', messagesHtml: '<button type="button" class="copy" data-copy="3"></button>' });

  page.fire('messages', 'click', { target: { closest: () => ({ dataset: { copy: '3' } }) } });

  assert.deepStrictEqual(
    page.posted.filter((message) => message['command'] === 'copyAnswer'),
    [{ type: 'command', command: 'copyAnswer', index: 3 }],
  );
});


test('the copy control can be found without a mouse', () => {
  // Hidden until hover is hidden entirely for a keyboard, a touch screen and a screen reader — and
  // `opacity: 0` leaves it in the tab order as an invisible target. It is dimmed, not erased, and
  // the whole message revealing it means arriving anywhere in the answer is enough.
  const css = chatPageHtml(state(), 'n0nce').split('<style>')[1].split('</style>')[0];
  const resting = ruleFor(css, '.msg .copy');

  assert.doesNotMatch(resting, /opacity: 0[;\s}]/, 'the copy control is invisible until a pointer finds it');
  assert.match(css, /\.msg:focus-within \.copy/, 'reaching the answer by keyboard does not reveal it');
});

test('the page module carries no backtick inside its own template literals', () => {
  // Three times in one plan a comment written inside chatStyle or chatScript quoted a CSS property
  // with backticks, and a backtick ENDS the template literal it sits in — the build broke each time,
  // in a place the comment made look innocent. A test is cheaper than a fourth time. (The gate
  // raised it as a convention; it is really a hazard of this file's shape.)
  const source = fs.readFileSync(path.join(__dirname, '..', '..', 'src', 'chatPage.ts'), 'utf8');

  assert.doesNotMatch(source, /\\`/, 'an escaped backtick — inside a template literal, write it as words instead');
});


/* ------------------------------------------------------------------------------------------------
 * The page half of "a turn nobody can stop". The seam landed first and has been waiting: it takes a
 * TURN NUMBER and refuses a stop that names none, so the page has to know which turn it is showing.
 * ---------------------------------------------------------------------------------------------- */

test('a running turn offers a way to stop it, and names the turn it would stop', () => {
  assert.match(chatStatusHtml(true, 0, 4), /<button type="button" id="stop" data-turn="4"/,
    'a turn in flight offers no way out of it');
  assert.match(chatStatusHtml(true, 0, 4), /Thinking/, 'the thinking line lost its words');
});

test('nothing offers a stop when nothing is running', () => {
  assert.strictEqual(chatStatusHtml(false, 0, 4), '', 'an idle page offered to stop something');
});

test('a turn the page cannot name offers no stop at all', () => {
  // The seam refuses a stop with no turn — deliberately, because a wildcard stop ends whatever is
  // running, which by then can be the turn AFTER the one somebody pressed for. A page that cannot
  // name the turn must not offer the button rather than post a message the host will drop.
  for (const turn of [0, -1, 1.5, Number.NaN]) {
    const html = chatStatusHtml(true, 0, turn);

    assert.doesNotMatch(html, /id="stop"/, `a stop was offered for turn ${String(turn)}`);
    assert.match(html, /Thinking/, `the thinking line vanished for turn ${String(turn)}`);
  }
});

/** A stop control as the page sees one: a click on the region, with the control as its target. */
function pressStop(page: RunningPage, control: { dataset: { turn: string }; disabled: boolean }): void {
  page.fire('thinking', 'click', { target: { closest: () => control } });
}

test('pressing stop names its turn, and cannot name it twice', () => {
  // Delegated on the region, because the thinking line is replaced wholesale on every push and a
  // listener bound to the old button dies with it — the same reason the answers' links are.
  const page = runChatPage({ running: true, turn: 7 });
  const control = { dataset: { turn: '7' }, disabled: false };

  pressStop(page, control);
  pressStop(page, control);

  assert.deepStrictEqual(
    page.posted.filter((message) => message['command'] === 'stop'),
    [{ type: 'command', command: 'stop', turn: 7 }],
    'the second press sent a second stop, which by then could be the next turn',
  );
  assert.strictEqual(control.disabled, true, 'the control stayed pressable after being pressed');
});

test('the stop control names the turn it was rendered with, not one the page remembers', () => {
  // Nothing in the script remembers a turn across a push: the number is in the markup, so a control
  // on screen can only ever name the turn it was drawn for.
  const page = runChatPage({ running: true, turn: 7 });

  pressStop(page, { dataset: { turn: '8' }, disabled: false });

  assert.deepStrictEqual(
    page.posted.filter((message) => message['command'] === 'stop'),
    [{ type: 'command', command: 'stop', turn: 8 }],
    'the page stopped a turn other than the one its control named',
  );
});

test('a stop control that names no turn posts nothing', () => {
  const page = runChatPage({ running: true, turn: 7 });

  for (const turn of ['0', '-1', '1.5', 'seven', '']) {
    pressStop(page, { dataset: { turn }, disabled: false });
  }

  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'stop'), [],
    'a control with no usable turn posted a stop the host would only drop');
});
