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
    title: 'привет',
    passage: 'The reviewers are read-only.',
    messages: [],
    models: MODELS,
    modelId: 'antigravity',
    running: false,
    capped: false,
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
  assert.match(chatStatusHtml(true, 4), /Thinking/);
  assert.match(chatStatusHtml(true, 4), /4 ahead/);

  // Zero is "no number to show", not position zero: the server says 0 both when it did not count
  // and when the turn has left the queue and is being answered. Neither is a place in a line.
  assert.strictEqual(chatStatusHtml(true, 0), '<p class="thinking">Thinking…</p>');
  assert.strictEqual(chatStatusHtml(true, -1), '<p class="thinking">Thinking…</p>');
  assert.strictEqual(chatStatusHtml(true, 1.5), '<p class="thinking">Thinking…</p>');

  // Nothing at all when no turn is in flight — the region is emptied, not left saying "Thinking…".
  assert.strictEqual(chatStatusHtml(false, 4), '');
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

function runChatPage(over: Partial<ChatPageState> = {}): RunningPage {
  const html = chatPageHtml(state(over), 'n0nce');
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
  const document_ = {
    getElementById: (id: string) => {
      const tag = rendered.get(id);
      if (tag === undefined) {
        return null;
      }
      if (seen[id] === undefined) {
        seen[id] = fake();
        seen[id].disabled = / disabled(?=[ >])/.test(tag);
      }

      return seen[id];
    },
    querySelectorAll: () => [],
    addEventListener() { /* the page listens on window */ },
    body: { style: emptyStyle() },
  };
  const window_ = {
    addEventListener(type: string, fn: (event: unknown) => void) { (onWindow[type] ??= []).push(fn); },
  };

  new Function('document', 'window', 'acquireVsCodeApi', 'requestAnimationFrame', body)(
    document_,
    window_,
    () => ({ postMessage: (message: Record<string, unknown>) => posted.push(message) }),
    (fn: () => void) => { pending.push(fn); },
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
    scrolledToBottom() {
      const scroll = region();
      scroll.scrollTop = 1500;

      return scroll;
    },
    scrolledUp() {
      const scroll = region();
      scroll.scrollTop = 200;

      return scroll;
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
