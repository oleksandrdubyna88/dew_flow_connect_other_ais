import * as fs from 'node:fs';
import * as path from 'node:path';
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { ModelPreset } from '../chatPresets';
import { ChatProvider } from '../chatModels';
import { ChatModelChoice, ChatPageState, NO_MARKS, markedTurn, FOLLOW_SLACK_PX, chatCappedHtml, chatMessagesHtml, chatPresetRowsHtml, chatPageHtml, chatPickerHtml, chatStatusHtml, shouldFollow } from '../chatPage';
import { chatCommandOf } from '../chatMessages';

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
    promptId: '',
    passage: 'The reviewers are read-only.',
    messages: [],
    models: MODELS,
    providers: [],
    reask: '',
    attached: '',
    spend: '',
    promptPresets: [],
    modelPresets: [],
    providerId: 'antigravity',
    chosenModelId: 'antigravity',
    fromSession: true,
    asked: [],
    modelId: 'antigravity',
    running: false,
    capped: false,
    turn: 0,
    failure: '',
    draft: '',
    marks: NO_MARKS,
    uiScale: 0,
    textTone: 0,
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

test('a lone provider is still SHOWN, and nothing at all is hidden', () => {
  // The flat list hid itself when it had one entry, and that made sense while an entry was a whole
  // configured row: there was nothing to choose. With two steps there always is — the row still has
  // models — and hiding the row would leave a person unable to see what will answer, which is the
  // complaint the two steps exist for.
  const one = chatPickerHtml(
    { providers: [{ id: 'a', vendor: 'antigravity', label: 'a', caption: 'local', models: [{ id: 'm', label: 'M' }] }], refused: [] },
    'a',
    'm',
  );

  assert.match(one, /<select id="provider"/, 'a single provider was hidden along with its models');
  assert.strictEqual(chatPickerHtml({ providers: [], refused: [] }, '', ''), '',
    'a page with nothing configured drew a picker of nothing');
});

test('the picker offers every provider and marks the chosen one', () => {
  const picker = chatPickerHtml(
    {
      providers: [
        { id: 'antigravity', vendor: 'antigravity', label: 'Gemini 3.8 Flash', caption: 'local', models: [{ id: 'g', label: 'G' }] },
        { id: 'remsoftdev-codex', vendor: 'remote', label: 'GPT (team)', caption: 'team server · no memory', models: [{ id: 'p', label: 'P' }] },
      ],
      refused: [],
    },
    'remsoftdev-codex',
    'p',
  );

  // THE GUARANTEE CHANGED: the first select names the VENDOR, not the saved model, because two
  // dropdowns both naming the saved model are two dropdowns saying one thing — which is what the
  // operator asked about, holding a screenshot of `GPT-5.6-Terra` beside `GPT-5.6-Terra`.
  assert.ok(picker.includes('>antigravity</option>'), 'the first select does not name the vendor');
  assert.ok(picker.includes('>remote</option>'), 'the second vendor is missing');
  assert.ok(/value="remsoftdev-codex" selected/.test(picker), 'the chosen vendor is not selected');
  assert.ok(picker.includes('>P</option>'), 'the chosen one’s models are not offered');
});

test('a remote model says what it cannot do, where the choice is made', () => {
  const picker = chatPickerHtml(
    {
      providers: [{ id: 'remsoftdev-codex', vendor: 'remote', label: 'GPT (team)', caption: 'team server · no memory', models: [{ id: 'p', label: 'P' }] }],
      refused: [],
    },
    'remsoftdev-codex',
    'p',
  );

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
  /** A region learning the elements a push just wrote into it. */
  reparse?: ((markup: string) => void) | undefined;
  /** Called when the page writes innerHTML — an insertion, which is what makes the region taller. */
  onWrite?: (value: string) => void;
  textContent: string;
  hidden: boolean;
  value: string;
  disabled: boolean;
  focused: number;
  /** The element's `data-*` attributes, as the page wrote them. */
  dataset: Record<string, string>;
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
      // What a push writes into a region IS new elements — the page looks them up by id and reads
      // their attributes, so the registry has to learn them or every such read comes back stale.
      this.reparse?.(value);
      this.onWrite?.(value);
    },
    textContent: '', hidden: false, value: '', disabled: false, focused: 0, dataset: {},
    scrollTop: 0, clientHeight: 0, scrollHeight: 0, style: emptyStyle(), listeners: {},
    addEventListener(type, fn) { (this.listeners[type] ??= []).push(fn); },
    reparse: undefined,
    focus() { this.focused += 1; },
    getAttribute: () => null,
    setAttribute() { /* the page sets none */ },
    querySelectorAll: () => [],
  };
}

/** The opening turn a capture builds, as the host would hand it to the page. */
const ROLE = 'You are an architect of distributed systems';
const TASK = 'Explain this passage';
const SERVICE = ['Answer in English.', 'Everything below', '---'];
const MARKS = { role: ROLE, task: TASK, service: SERVICE };

const OPENING = [ROLE, '', TASK, '', 'Answer in English.', '', 'Everything below', '---', 'the passage'].join('\n');

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
  /** Put the keyboard on an element, as a person tabbing to it would. */
  focusOn(id: string): void;
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
  const taggedIn = (markup: string): Map<string, string> => new Map(
    [...markup.matchAll(/<[a-z]+[^>]*\bid="([^"]+)"[^>]*>/g)].map((match) => [match[1] ?? '', match[0] ?? '']),
  );
  const seed = (element: Fake, tag: string): void => {
    element.disabled = / disabled(?=[ >])/.test(tag);
    element.hidden = / hidden(?=[ >])/.test(tag);
    element.dataset = {};
    for (const attribute of tag.matchAll(/ data-([a-z-]+)="([^"]*)"/g)) {
      element.dataset[attribute[1] ?? ''] = attribute[2] ?? '';
    }
  };
  // WHAT A TEXTAREA HOLDS, not only its attributes. The composer is rendered with the draft already
  // in it, and a fake that starts empty cannot see anything the page does with that text — which is
  // exactly the defect these tests exist for: the box's own text is drawn transparent, so a page
  // that never paints its backdrop renders a composer that LOOKS empty while holding a whole turn.
  const written = (markup: string): Map<string, string> => new Map(
    [...markup.matchAll(/<textarea[^>]*\bid="([^"]+)"[^>]*>([\s\S]*?)<\/textarea>/g)]
      .map((match) => [match[1] ?? '', (match[2] ?? '')
        .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&quot;/g, '"').replace(/&amp;/g, '&')]),
  );
  const drafts = written(html);
  const rendered = taggedIn(html);
  const reparse = (markup: string): void => {
    for (const [id, tag] of taggedIn(markup)) {
      rendered.set(id, tag);
      if (seen[id] !== undefined) {
        seed(seen[id], tag);
      }
    }
  };
  // The page corrects its opening scroll once the fonts have settled. A test that cannot say WHEN
  // that happens cannot show what a reader who moved in the meantime experiences.
  let settleFonts = () => { /* replaced by the promise below */ };
  const fontsReady = new Promise<void>((resolve) => { settleFonts = () => { resolve(); }; });
  let focused: Fake | undefined;
  const document_ = {
    get activeElement() { return focused; },
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
        seen[id].reparse = reparse;
        seed(seen[id], tag);
        seen[id].value = drafts.get(id) ?? '';
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
  // What a browser gives the page for reading a pasted file. Node has none, and the page correctly
  // refuses without one — so a harness without it tests the refusal rather than the paste.
  class FakeFileReader {
    public result = '';
    public onload: (() => void) | undefined;
    readAsDataURL(): void {
      this.result = 'data:image/png;base64,iVBORw0KGgo=';
      this.onload?.();
    }
  }
  class FakeResizeObserver {
    constructor(callback: () => void) { observed = callback; }
    observe() { /* the page observes one element */ }
    disconnect() { /* nothing to release in a fake */ }
  }


  new Function('document', 'window', 'acquireVsCodeApi', 'requestAnimationFrame', 'ResizeObserver', 'FileReader', body)(
    document_,
    window_,
    // `setState` as well as `postMessage`: the page tells VS Code which conversation it is the
    // moment it loads, so a fake without it is a fake the real page cannot run against.
    () => ({ postMessage: (message: Record<string, unknown>) => posted.push(message), setState: () => undefined }),
    (fn: () => void) => { pending.push(fn); },
    withoutResizeObserver === true ? undefined : FakeResizeObserver,
    FakeFileReader,
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
    focusOn(id) {
      const element = document_.getElementById(id);
      assert.ok(element, `the page renders no #${id} to focus`);
      focused = element;
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
  //
  // Through the tone PROPERTY since the stepper arrived, whose default IS the editor foreground: a
  // rule that named the theme variable directly ignored the tone, and the prose is the text somebody
  // dimming their screen is reading. (CodeRabbit, PR #206.)
  assert.match(ruleFor(css, '.msg .what'), /var\(--coai-read\)/, 'the prose does not follow the tone');
  assert.match(ruleFor(css, ':root'), /--coai-read: var\(--vscode-editor-foreground\)/, 'the prose default moved');
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


test('a push while a stop is in flight does not hand the control back', () => {
  // The gate's finding, and it is real: the thinking line is replaced wholesale on every push — a
  // Team server pushes its queue position while a turn waits — and the replacement carried a fresh,
  // pressable control for a turn the person had already asked to stop. They would press it again,
  // and see it come back a second time.
  const page = runChatPage({ running: true, turn: 7 });
  page.fire('thinking', 'click', { target: { closest: () => ({ dataset: { turn: '7' }, disabled: false }) } });

  page.deliver({
    type: 'state',
    thinkingHtml: '<p class="thinking">Thinking…<button type="button" id="stop" data-turn="7">Stop</button></p>',
    running: true,
    capped: false,
  });

  assert.strictEqual(page.seen['stop'].disabled, true,
    'a push handed back a control for a turn the person had already stopped');
  page.fire('thinking', 'click', { target: { closest: () => page.seen['stop'] } });
  assert.strictEqual(page.posted.filter((message) => message['command'] === 'stop').length, 1,
    'the re-rendered control sent a second stop for the same turn');
});

test('the next turn gets its own control, live', () => {
  // The memory is about ONE turn. A conversation that carries on must not inherit a stop nobody
  // asked for.
  const page = runChatPage({ running: true, turn: 7 });
  page.fire('thinking', 'click', { target: { closest: () => ({ dataset: { turn: '7' }, disabled: false }) } });

  page.deliver({
    type: 'state',
    thinkingHtml: '<p class="thinking">Thinking…<button type="button" id="stop" data-turn="8">Stop</button></p>',
    running: true,
    capped: false,
  });

  assert.strictEqual(page.seen['stop'].disabled, false, 'the next turn inherited the last one\'s stop');
});

test('a turn that ends forgets the stop it was asked for', () => {
  // `stopAsked` was never cleared, and *Start a new conversation* keeps the same page: the turns
  // begin again at 1, and a stale 7 would have disabled the seventh question of the next
  // conversation before anybody pressed anything. It is about the turn in flight, so when nothing is
  // in flight it is about nothing.
  const page = runChatPage({ running: true, turn: 7 });
  page.fire('thinking', 'click', { target: { closest: () => ({ dataset: { turn: '7' }, disabled: false }) } });

  page.deliver({ type: 'state', thinkingHtml: '', running: false, capped: false });
  page.deliver({
    type: 'state',
    thinkingHtml: '<p class="thinking">Thinking…<button type="button" id="stop" data-turn="7">Stop</button></p>',
    running: true,
    capped: false,
  });

  assert.strictEqual(page.seen['stop'].disabled, false,
    'a later turn with the same number inherited a stop nobody had asked for');
});

test('pressing stop says that it is stopping', () => {
  // Stopping is not instant — a vendor process has to be killed and a turn resolved — and a greyed
  // out control still reading "Stop" cannot be told from one that did nothing.
  const page = runChatPage({ running: true, turn: 7 });
  const control = { dataset: { turn: '7' }, disabled: false, textContent: 'Stop' };
  page.fire('thinking', 'click', { target: { closest: () => control } });

  assert.strictEqual(control.textContent, 'Stopping…', 'the control said nothing about what it had done');
});

test('a redrawn control that is already stopping says so, and does not take the keyboard', () => {
  // Focusing a disabled control leaves a keyboard on something with no action left. The place to be
  // is wherever the person was, not on an inert button.
  const page = runChatPage({ running: true, turn: 7 });
  page.focusOn('stop');
  const before = page.seen['stop'].focused;
  page.fire('thinking', 'click', { target: { closest: () => ({ dataset: { turn: '7' }, disabled: false }) } });

  page.deliver({
    type: 'state',
    thinkingHtml: '<p class="thinking">Thinking…<button type="button" id="stop" data-turn="7">Stop</button></p>',
    running: true,
    capped: false,
  });

  assert.strictEqual(page.seen['stop'].disabled, true);
  assert.strictEqual(page.seen['stop'].textContent, 'Stopping…', 'the redrawn control forgot what was asked of it');
  assert.strictEqual(page.seen['stop'].focused, before, 'the keyboard was put on a control with nothing left to do');
});

test('a stop control keeps the focus a push would have taken from it', () => {
  // Replacing the line destroys the element the keyboard was on. For a remote turn the line is
  // replaced every time the queue position moves, so somebody who tabbed to Stop would lose it
  // repeatedly, silently, while waiting — which is exactly when they are most likely to want it.
  const page = runChatPage({ running: true, turn: 7 });
  // Focus first: nothing has asked the page for #stop yet, and the registry answers only for
  // elements somebody looked up — which is the same property that catches a control the page never
  // rendered.
  page.focusOn('stop');
  const before = page.seen['stop'].focused;

  page.deliver({
    type: 'state',
    thinkingHtml: '<p class="thinking">Thinking…<button type="button" id="stop" data-turn="7">Stop</button></p>',
    running: true,
    capped: false,
  });

  assert.ok(page.seen['stop'].focused > before, 'the keyboard lost the control when the line was redrawn');
});


/* ------------------------------------------------------------------------------------------------
 * Who said it. Entry 17 of the operator's list: every answer was captioned `The other AI`, while
 * switching the model mid-conversation is a shipped feature — so one tab routinely holds answers
 * from two models with nothing on screen telling them apart.
 * ---------------------------------------------------------------------------------------------- */

test('an answer is captioned with the model that gave it', () => {
  const html = chatMessagesHtml([
    { role: 'you', text: 'ask' },
    { role: 'model', text: 'first', model: { id: 'antigravity', label: 'Gemini 3.8 Flash' } },
    { role: 'model', text: 'second', model: { id: 'remsoftdev-codex', label: 'GPT-5.6 (team)' } },
  ]);

  assert.match(html, /Gemini 3\.8 Flash/, 'the first answer does not say who gave it');
  assert.match(html, /GPT-5\.6 \(team\)/, 'the second answer does not say who gave it');
  assert.doesNotMatch(html, /The other AI/, 'an answer that knows its model still says "The other AI"');
});

test('what the person said is captioned as theirs, whatever else is going on', () => {
  // A reviewer read the plan's sentence about the fallback as covering BOTH kinds of message and
  // asked whether a person's own words could end up captioned `The other AI`. The code has never
  // done that — the caption is chosen by role before anything looks at a model — but the question is
  // fair enough to be worth a test rather than a paragraph.
  const html = chatMessagesHtml([
    { role: 'you', text: 'ask' },
    { role: 'model', text: 'answered', model: { id: 'antigravity', label: 'Gemini' } },
  ]);
  const mine = html.slice(0, html.indexOf('msg model'));

  assert.match(mine, />You</, "the person's own message lost its caption");
  assert.doesNotMatch(mine, /The other AI|Gemini/, 'the person was captioned as a model');
});

test('an answer from before this existed still says something', () => {
  // A conversation restored from a tab that predates the field, or a turn a failure produced. The
  // caption falls back rather than rendering an empty line where a name should be.
  const html = chatMessagesHtml([{ role: 'model', text: 'answered' }]);

  assert.match(html, /The other AI/, 'an answer with no model recorded lost its caption entirely');
});

test('the caption wears its vendor colour, and two vendors do not share one', () => {
  // The same colour the rounds list and the reviewer cards give that vendor — one call to
  // `vendorPalette`, so a vendor a person has learned is the same colour everywhere.
  const html = chatMessagesHtml([
    { role: 'model', text: 'a', model: { id: 'antigravity', label: 'Gemini' } },
    { role: 'model', text: 'b', model: { id: 'remsoftdev-codex', label: 'GPT' } },
  ]);
  const colours = [...html.matchAll(/class="author model-([a-z0-9-]+)"/g)].map((match) => match[1]);

  assert.strictEqual(colours.length, 2, 'the captions carry no vendor of their own');
  assert.notStrictEqual(colours[0], colours[1], 'two vendors were given the same class');
});

test('the caption is its own element, not a second one wearing the container\'s class', () => {
  // A `.who` span inside a `.who` div: every rule written for the row — its flex, its gap, its
  // margin, its opacity — landed on the label as well, and the next person to change the row's
  // layout would have moved the text with it without knowing why. (gemini, the code round, twice.)
  const html = chatMessagesHtml([
    { role: 'you', text: 'ask' },
    { role: 'model', text: 'answered', model: { id: 'antigravity', label: 'Gemini' } },
  ]);

  assert.doesNotMatch(html, /class="who"[^>]*>\s*<span class="who/, 'a .who is nested inside a .who');
  assert.match(html, /<span class="author model-antigravity">Gemini<\/span>/, 'the label has no class of its own');
  assert.match(html, /<span class="author">You<\/span>/, 'the person\'s caption has no class of its own');
});

test('an answer from a model the picker no longer offers keeps its colour', () => {
  // The colours were built from the picker's CURRENT list, so switching models — which carries the
  // whole thread across, and is the feature this caption exists for — left every earlier answer
  // with a class and no rule. The ids are in the messages; the page can simply read them.
  const html = chatPageHtml(state({
    models: [{ id: 'claude', label: 'Claude', caption: 'local' }],
    modelId: 'claude',
    messages: [{ role: 'model', text: 'from before', model: { id: 'antigravity', label: 'Gemini' } }],
  }), 'n0nce');
  const css = html.split('<style>')[1].split('</style>')[0];

  assert.match(css, /\.author\.model-antigravity \{/, 'an answer from a withdrawn model lost its colour');
  assert.match(css, /\.author\.model-claude \{/, 'the offered model lost its colour');
});

test('a model label that is markup is escaped like everything else', () => {
  // It comes from a Team server's catalog, which is somebody else's text.
  const html = chatMessagesHtml([
    { role: 'model', text: 'x', model: { id: 'a', label: '<img src=x onerror=alert(1)>' } },
  ]);

  assert.doesNotMatch(html, /<img/i, 'a model label reached the page as markup');
  assert.match(html, /&lt;img/, 'the label was dropped rather than shown');
});


/* ------------------------------------------------------------------------------------------------
 * Entry 21: the picker was one flat list of configured ROWS, each labelled with the one model it
 * happened to be set to — so choosing a different model meant leaving the tab and reconfiguring a
 * reviewer. Two steps: the provider, then its models.
 * ---------------------------------------------------------------------------------------------- */

const PROVIDERS: readonly ChatProvider[] = [
  {
    id: 'antigravity',
    vendor: 'antigravity',
    label: 'antigravity · gemini-3.8-flash',
    caption: 'local · antigravity · keeps the conversation',
    models: [{ id: 'gemini-3.8-flash', label: 'Gemini 3.8 Flash' }, { id: 'gemini-3.8-pro', label: 'Gemini 3.8 Pro' }],
  },
  {
    id: 'remsoftdev-codex',
    vendor: 'remote',
    label: 'remsoftdev-codex · gpt-5.6',
    caption: 'team server · no memory',
    models: [{ id: 'gpt-5.6', label: 'GPT-5.6' }],
  },
];

test('the picker asks for a provider and then for one of its models', () => {
  const html = chatPickerHtml({ providers: PROVIDERS, refused: [] }, 'antigravity', 'gemini-3.8-pro');

  assert.match(html, /<select id="provider"/, 'there is no provider to choose');
  assert.match(html, /<select id="model"/, 'there is no model to choose');
  // The chosen provider's models, and only those: a list holding another row's models is a list
  // somebody can pick a combination from that has no adapter.
  assert.match(html, /<option value="gemini-3\.8-pro" selected>/, 'the chosen model is not marked');
  assert.doesNotMatch(html, /value="gpt-5\.6"/, 'another provider\'s models are on offer');
});

test('changing the provider offers that provider\'s models, not the last one\'s', () => {
  const html = chatPickerHtml({ providers: PROVIDERS, refused: [] }, 'remsoftdev-codex', 'gpt-5.6');

  assert.match(html, /value="gpt-5\.6"/);
  assert.doesNotMatch(html, /value="gemini-3\.8-flash"/, 'the previous provider\'s models stayed');
});

test('a provider with one model still shows the model it will use', () => {
  // Hiding a list of one would leave a person unable to see WHAT will answer — which is the whole
  // complaint the two steps exist for.
  const html = chatPickerHtml({ providers: PROVIDERS, refused: [] }, 'remsoftdev-codex', 'gpt-5.6');

  assert.match(html, /<select id="model"[^>]*>[\s\S]*?GPT-5\.6/, 'a single model was hidden');
});

test('a row that cannot answer is named, not quietly dropped', () => {
  const html = chatPickerHtml(
    { providers: PROVIDERS, refused: [{ id: 'ollama', reason: 'the chat cannot speak to local yet' }] },
    'antigravity',
    'gemini-3.8-flash',
  );

  assert.match(html, /the chat cannot speak to local yet/, 'a configured row vanished with no reason given');
});

test('picking either half posts both halves, so the host never has to guess', () => {
  const page = runChatPage({ providers: [...PROVIDERS], providerId: 'antigravity', modelId: 'gemini-3.8-flash' });

  // Changing the PROVIDER carries no model: which of the new row's models answers is the host's to
  // decide, because it holds the catalog, and sending the old one would name a model belonging to
  // somebody else.
  page.seen['provider'].value = 'remsoftdev-codex';
  page.fire('provider', 'change');
  // Then a model — of the provider that is now chosen, which is the only kind the page can offer.
  page.seen['model'].value = 'gpt-5.6';
  page.fire('model', 'change');

  // WITH the composer, like the preset buttons above: choosing a model in the dropdown is the same
  // decision, and the host can only swap the role in text it can see.
  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'pick'), [
    { type: 'command', command: 'pick', provider: 'remsoftdev-codex', model: '', text: '' },
    { type: 'command', command: 'pick', provider: 'remsoftdev-codex', model: 'gpt-5.6', text: '' },
  ]);
});

test('a provider label that is markup is escaped', () => {
  const html = chatPickerHtml(
    {
      providers: [{ id: 'x', vendor: 'antigravity', label: '<img src=x onerror=alert(1)>', caption: '<b>c</b>', models: [{ id: 'm', label: '<i>m</i>' }] }],
      refused: [],
    },
    'x',
    'm',
  );

  assert.doesNotMatch(html, /<img|<b>|<i>/, 'a catalog label reached the page as markup');
});


/* ------------------------------------------------------------------------------------------------
 * Entries 12 and 21b: two rows of buttons above the composer — the prompts a person saved, and the
 * models they saved. TWO rows and not one merged list, decided 2026-09-09: they are two kinds of
 * decision, what shall be ASKED and what shall ANSWER, and one button that sets both silently is a
 * button whose effect cannot be predicted from its name.
 * ---------------------------------------------------------------------------------------------- */

const PROMPT_PRESETS = [
  { id: 'p1', name: 'Explain', text: 'Explain this', main: true },
  { id: 'p2', name: 'What would you answer?', text: 'What would you answer?', main: false },
];

const MODEL_PRESETS: readonly ModelPreset[] = [
  { id: 'm1', name: 'Fast', main: true, runtime: 'antigravity', executablePath: '', baseUrl: '', model: 'gemini-3.8-flash' },
  { id: 'm2', name: 'Deep', main: false, runtime: 'antigravity', executablePath: '', baseUrl: '', model: 'opus', startingPrompt: 'Think hard' },
];

test('both rows are there, each button naming its preset', () => {
  const html = chatPresetRowsHtml(PROMPT_PRESETS, MODEL_PRESETS);

  assert.match(html, /<button type="button" class="preset prompt" data-prompt-preset="p1">Explain<\/button>/);
  assert.match(html, /<button type="button" class="preset model" data-model-preset="m2"[^>]*>Deep<\/button>/);
  // Two rows, not one: the model row and the prompt row are separate elements.
  assert.strictEqual((html.match(/class="presets/g) ?? []).length, 2, 'the two kinds share one row');
});

test('a button wears the colour of the half it changes, and says so in a class', () => {
  // The stripe answers "what does this button do to my question": a model changes WHO is answering,
  // a prompt changes WHAT is asked, and those two already have a colour each in the composer below.
  // It used to be the VENDOR's colour — a rule that belongs to the review gate, where the question
  // is whose model this is.
  const html = chatPresetRowsHtml(PROMPT_PRESETS, MODEL_PRESETS);
  const model = html.slice(html.indexOf('data-model-preset="m1"'));
  const prompt = html.slice(html.indexOf('data-prompt-preset="p1"'));

  assert.doesNotMatch(model, /vendorColour/, 'a model button still wears a vendor colour');
  assert.doesNotMatch(html, /style="border-left-color/, 'a stripe is still painted inline, where a style rule cannot reach it');
  // The CLASS is what the stripe hangs on, so it is what this asserts: a rule can only reach a
  // button that says which kind it is.
  assert.match(html, /class="preset model"[^>]*data-model-preset="m1"/, 'a model button does not say it is one');
  assert.match(html, /class="preset prompt"[^>]*data-prompt-preset="p1"/, 'a prompt button does not say it is one');
  assert.ok(prompt.length > 0, 'the prompt button was not drawn at all');
});

test('the stripe colours are the same two the words are written in', () => {
  const page = chatPageHtml(state({ promptPresets: PROMPT_PRESETS, modelPresets: MODEL_PRESETS }), 'n0nce');

  assert.match(page, /\.preset\.model \{ border-left-color: var\(--coai-role\); \}/, 'the model stripe is not the role colour');
  assert.match(page, /\.preset\.prompt \{ border-left-color: var\(--coai-task\); \}/, 'the prompt stripe is not the task colour');
  assert.match(page, /mark\.role \{ color: var\(--coai-role\); \}/, 'the role text and the model stripe drifted apart');
  assert.match(page, /mark\.task \{ color: var\(--coai-task\); \}/, 'the task text and the prompt stripe drifted apart');
});

test('neither row appears when there is nothing saved in it', () => {
  assert.strictEqual(chatPresetRowsHtml([], []), '', 'an empty pair of rows took up space anyway');
  const onlyPrompts = chatPresetRowsHtml(PROMPT_PRESETS, []);
  assert.match(onlyPrompts, /data-prompt-preset/);
  assert.doesNotMatch(onlyPrompts, /data-model-preset/, 'an empty model row was drawn');
});

test('a preset name that is markup is escaped', () => {
  const html = chatPresetRowsHtml(
    [{ id: 'p', name: '<img src=x onerror=alert(1)>', text: 't', main: true }],
    [],
  );

  assert.doesNotMatch(html, /<img/i, 'a name a person typed reached the page as markup');
});

test('pressing a preset names it to the host and nothing else', () => {
  const page = runChatPage({ promptPresets: PROMPT_PRESETS, modelPresets: MODEL_PRESETS });

  page.fire('presets', 'click', { target: { closest: () => ({ dataset: { promptPreset: 'p2' } }) } });
  page.fire('presets', 'click', { target: { closest: () => ({ dataset: { modelPreset: 'm2' } }) } });

  assert.deepStrictEqual(page.posted.filter((message) => String(message['command']).startsWith('use')), [
    // WITH the composer, BOTH of them: each preset changes one half of the instruction at the front
    // of that text, and a half can only be swapped where it can be seen. Only this side knows what
    // is in the box.
    { type: 'command', command: 'usePromptPreset', id: 'p2', text: '' },
    { type: 'command', command: 'useModelPreset', id: 'm2', text: '' },
  ]);
});

test('a click on the row itself does nothing', () => {
  const page = runChatPage({ promptPresets: PROMPT_PRESETS, modelPresets: MODEL_PRESETS });

  page.fire('presets', 'click', { target: { closest: () => null } });

  assert.deepStrictEqual(page.posted.filter((message) => String(message['command']).startsWith('use')), []);
});


/* ------------------------------------------------------------------------------------------------
 * Entry 24: "maybe I don't like gemini's answer and want to switch to Fable. If I switch the model
 * and press Enter with an empty box — take the previous context (except the last answer) and feed it
 * to the new model." An empty box has always been refused, so the gesture was free to take; what it
 * needed was a way to be discovered.
 * ---------------------------------------------------------------------------------------------- */

test('when a re-ask is on offer the button says so, and names who would answer', () => {
  const html = chatPageHtml(state({ reask: 'Claude Opus' }), 'n0nce');
  const button = html.slice(html.indexOf('id="send"'), html.indexOf('</button>', html.indexOf('id="send"')));

  assert.match(button, /Re-ask · Claude Opus/, 'the only way in is a gesture nobody can see');
});

test('with nothing to re-ask the button is a Send button', () => {
  const html = chatPageHtml(state(), 'n0nce');
  const button = html.slice(html.indexOf('id="send"'), html.indexOf('</button>', html.indexOf('id="send"')));

  assert.match(button, />Send$/, 'the button is not a plain Send button');
  assert.doesNotMatch(button, /Re-ask/);
});

test('an empty box re-asks when there is something to re-ask, and does nothing when there is not', () => {
  const offered = runChatPage({ reask: 'Claude Opus' });
  offered.seen['say'].value = '';
  offered.fire('send', 'click');

  assert.deepStrictEqual(offered.posted.filter((message) => message['command'] === 'reask'),
    [{ type: 'command', command: 'reask' }]);
  assert.deepStrictEqual(offered.posted.filter((message) => message['command'] === 'send'), [],
    'an empty box sent an empty question');

  const plain = runChatPage();
  plain.seen['say'].value = '';
  plain.fire('send', 'click');
  assert.deepStrictEqual(plain.posted.filter((message) => message['command'] === 'reask'), [],
    'a page with nothing to re-ask re-asked anyway');
});

test('a box with something in it sends it, re-ask or no re-ask', () => {
  // The re-ask is what an EMPTY box means. Text in the box is a question, and it must not be
  // swallowed by a gesture that happens to be available.
  const page = runChatPage({ reask: 'Claude Opus' });
  page.seen['say'].value = 'a different question';
  page.fire('send', 'click');

  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'send'),
    [{ type: 'command', command: 'send', text: 'a different question' }]);
  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'reask'), []);
});

test('a re-ask locks the composer like any other turn', () => {
  const page = runChatPage({ reask: 'Claude Opus' });
  page.seen['say'].value = '';
  page.fire('send', 'click');
  page.fire('send', 'click');

  assert.strictEqual(page.seen['say'].disabled, true, 'the composer stayed open during a re-ask');
  assert.strictEqual(page.posted.filter((message) => message['command'] === 'reask').length, 1,
    'a second press re-asked a second time');
});

test('a model label in the button is escaped like everything else', () => {
  const html = chatPageHtml(state({ reask: '<img src=x onerror=alert(1)>' }), 'n0nce');

  assert.doesNotMatch(html, /<img/i, 'a model label reached the page as markup');
});


/* ------------------------------------------------------------------------------------------------
 * Entry 13: paste a picture into the composer. Phase 0 measured that the mechanism is a file PATH
 * named in the prompt, which the vendor process opens itself.
 * ---------------------------------------------------------------------------------------------- */

test('the page may show an image and may load nothing else', () => {
  // The CSP is `default-src 'none'`, which blocks images too. A thumbnail of what was just pasted
  // needs `img-src data:` and NOTHING wider: no remote host, no file:, and the page still loads
  // nothing from disk because `localResourceRoots` is empty.
  const html = chatPageHtml(state(), 'n0nce');
  const csp = html.slice(html.indexOf('Content-Security-Policy'), html.indexOf('>', html.indexOf('Content-Security-Policy')));

  assert.match(csp, /img-src data:/, 'a pasted image cannot be shown at all');
  assert.doesNotMatch(csp, /img-src[^;]*https?:/, 'the page may load an image from the network');
  assert.doesNotMatch(csp, /img-src[^;]*file:/, 'the page may load an image from the filesystem');
});

test('a pasted image is offered to the host, and anything else pastes as text', () => {
  const page = runChatPage();

  page.fire('say', 'paste', {
    clipboardData: {
      items: [{ kind: 'file', type: 'image/png', getAsFile: () => ({ name: 'x.png' }) }],
    },
  });

  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'attach'),
    [{ type: 'command', command: 'attach', data: 'data:image/png;base64,iVBORw0KGgo=' }],
    'a pasted picture was not offered to the host');

  const text = runChatPage();
  text.fire('say', 'paste', { clipboardData: { items: [{ kind: 'string', type: 'text/plain' }] } });
  assert.deepStrictEqual(text.posted.filter((message) => message['command'] === 'attach'), [],
    'pasted text was treated as a picture');
});

test('an attached picture is shown, and can be taken off again', () => {
  const html = chatPageHtml(state({ attached: 'data:image/png;base64,iVBORw0KGgo=' }), 'n0nce');

  assert.match(html, /<img class="attached" src="data:image\/png;base64,/, 'the attachment is not shown');
  assert.match(html, /id="unattach"/, 'an attachment cannot be taken off');

  const none = chatPageHtml(state(), 'n0nce');
  assert.doesNotMatch(none, /class="attached"/, 'an empty attachment drew a box anyway');
});

test('taking the picture off names it to the host', () => {
  const page = runChatPage({ attached: 'data:image/png;base64,iVBORw0KGgo=' });

  page.fire('unattach', 'click');

  assert.deepStrictEqual(page.posted.filter((message) => message['command'] === 'unattach'),
    [{ type: 'command', command: 'unattach' }]);
});

test('an attachment that is not an image cannot be shown at all', () => {
  // The state comes from the host, which built it from what the page sent — but a page rendering a
  // src it has not looked at is a page that would render `javascript:` if the host ever handed it
  // one. The check is here as well because this is where it becomes an attribute.
  for (const attached of ['javascript:alert(1)', 'https://example.com/a.png', 'data:text/html;base64,PHA+']) {
    const html = chatPageHtml(state({ attached }), 'n0nce');

    assert.doesNotMatch(html, /<img class="attached"/, `${attached} was rendered as an attachment`);
  }
});


test('the tab says what the conversation has cost, where the decision is made', () => {
  // Beside the picker, which is where somebody decides whether to ask again or start fresh — and a
  // turn carries the whole conversation, so that decision is exactly the one the number is for.
  const html = chatPageHtml(state({ spend: '~$0.4200' }), 'n0nce');
  const footer = html.slice(html.indexOf('<footer'), html.indexOf('</footer>'));

  assert.match(footer, /id="spend"[^>]*>~\$0\.4200</, 'the running cost is not on the page');
});

test('a conversation that has cost nothing says nothing', () => {
  const html = chatPageHtml(state(), 'n0nce');

  // Empty CONTENT, not an absent element: the element is always there so a push can fill it without
  // the page being rebuilt. `\S` was the first assertion and it matched the `<` of `</span>`.
  assert.match(html, /id="spend"[^>]*><\/span>/, 'an empty total drew a line anyway');
});

test('the running total is updated by a push, not only at open', () => {
  const page = runChatPage({ spend: '$0.0100' });

  page.deliver({ type: 'state', spend: '$0.0200', running: false, capped: false });

  assert.strictEqual(page.seen['spend'].textContent, '$0.0200', 'the total stayed at what it opened with');
});

/**
 * THE SEAM, asserted in one place — what the page posts is what the host accepts.
 *
 * <p>Reported 2026-09-11 as "I press the model button and the dropdown below stays gemini". The
 * button was one half of it; the other half is that the PICKER had been dead since the two-step
 * choice shipped. The page posts `{command: 'pick', provider, model}` and `chatCommandOf` read
 * `message.id`, which the page has not sent since — so every model switch in an open tab resolved to
 * `ignore` and nothing happened.</p>
 *
 * <p><b>Both suites were green about opposite wire formats</b>, which is the exact failure
 * `PLAN_contract_across_the_seam.md` was written for after 0.31.1 shipped a sign-in nobody could use:
 * `chatPage.test.ts` asserted the page posts the pair, `chatMessages.test.ts` asserted the parser
 * takes an id, and neither ever met the other. This test is where they meet.</p>
 */
test('every message the picker posts is one the host understands', () => {
  const page = runChatPage({ providers: [...PROVIDERS], providerId: 'antigravity', modelId: 'gemini-3.8-flash' });

  page.seen['provider'].value = 'remsoftdev-codex';
  page.fire('provider', 'change');
  page.seen['model'].value = 'gpt-5.6';
  page.fire('model', 'change');

  const picks = page.posted.filter((message) => message['command'] === 'pick');
  assert.ok(picks.length > 0, 'the picker posted nothing at all');
  for (const posted of picks) {
    assert.notStrictEqual(chatCommandOf(posted).kind, 'ignore',
      `the host ignores what the page posts: ${JSON.stringify(posted)}`);
  }
});

test('the preset buttons post messages the host understands too', () => {
  const page = runChatPage({
    providers: [...PROVIDERS],
    providerId: 'antigravity',
    modelId: 'gemini-3.8-flash',
  promptId: '',
    promptPresets: PROMPT_PRESETS,
    modelPresets: MODEL_PRESETS,
  });

  page.fire('presets', 'click', { target: { closest: () => ({ dataset: { promptPreset: 'p2' } }) } });
  page.fire('presets', 'click', { target: { closest: () => ({ dataset: { modelPreset: 'm2' } }) } });

  const pressed = page.posted.filter((message) =>
    message['command'] === 'usePromptPreset' || message['command'] === 'useModelPreset');
  assert.strictEqual(pressed.length, 2, 'the preset buttons posted nothing');
  for (const posted of pressed) {
    assert.notStrictEqual(chatCommandOf(posted).kind, 'ignore',
      `the host ignores what a preset button posts: ${JSON.stringify(posted)}`);
  }
});

test('a pushed draft is APPENDED, and a set one REPLACES — two operations, not one', () => {
  // CodeRabbit on PR #200: `pushChatDraft` posts `draft`, and the page appends it — deliberately, so
  // that capturing a second passage does not throw away a half-typed follow-up. A prompt preset is
  // the opposite instruction: "ask this instead". It had been sharing the appending operation, so
  // pressing it while something was in the box produced a question neither of them wrote.
  const page = runChatPage();
  page.seen['say'].value = 'half a question';

  page.deliver({ type: 'state', draft: 'a captured passage' });
  assert.strictEqual(page.seen['say'].value, 'half a question\n\na captured passage', 'a capture stopped appending');

  page.deliver({ type: 'state', setDraft: 'Explain this simply' });
  assert.strictEqual(page.seen['say'].value, 'Explain this simply', 'a preset did not replace what was there');
});


test('a composer that opens with a draft in it DRAWS that draft', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  page.frames();

  assert.match(page.seen.backdrop?.innerHTML ?? '', /architect of distributed systems/,
    'the composer rendered its draft invisibly');
});

test('the role and the task are marked apart, in their own colours', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  page.frames();
  const drawn = page.seen.backdrop?.innerHTML ?? '';

  assert.match(drawn, new RegExp(`<mark class="role">${ROLE}</mark>`), 'the role was not marked');
  assert.match(drawn, new RegExp(`<mark class="task">${TASK}</mark>`), 'the task was not marked');
  assert.doesNotMatch(drawn, /<mark[^>]*>Answer in English/, 'the language line was marked as instruction');
});

test('a task with no role in front of it is still the task', () => {
  // The old painter marked "the first block", so a conversation on a model with no starting prompt
  // painted its TASK in the role's colour - two different decisions drawn as one.
  const page = runChatPage({ draft: [TASK, '', 'Answer in English.'].join('\n'), marks: { ...MARKS, role: '' } });
  page.frames();

  assert.match(page.seen.backdrop?.innerHTML ?? '', new RegExp(`<mark class="task">${TASK}</mark>`),
    'a task standing alone was not marked as one');
});

test('typing repaints the layer that draws the typing', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  const box = page.seen['say'];
  box.value = `${OPENING} and one more thing`;
  page.fire('say', 'input');
  page.frames();

  assert.match(page.seen.backdrop?.innerHTML ?? '', /and one more thing/, 'what was typed was not drawn');
});

test('an edited instruction stops being marked rather than marking half a sentence', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  const box = page.seen['say'];
  box.value = `Actually, ${OPENING}`;
  page.fire('say', 'input');
  page.frames();

  assert.doesNotMatch(page.seen.backdrop?.innerHTML ?? '', /<mark/, 'a prefix that no longer matches was marked');
});

test('a push says which words are the instruction, and the layer follows', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  const next = ['You are a business analyst', '', TASK].join('\n');
  page.deliver({ type: 'state', marks: { ...MARKS, role: 'You are a business analyst' }, setDraft: next });
  page.frames();

  assert.match(page.seen.backdrop?.innerHTML ?? '', /<mark class="role">You are a business analyst<\/mark>/,
    'the new role was not marked');
});

test('the pressed model button is the one CHOSEN, not the one still answering', () => {
  // A switch disposes one process, resolves a CLI and starts another. While that runs the choice has
  // been made and the session has not moved - and a button drawn from the session stayed unpressed
  // for all of it, which is what a model switch hanging looks like from the outside.
  const html = chatPageHtml(state({ modelPresets: MODEL_PRESETS, providerId: 'm1', chosenModelId: 'm2' }), 'n0nce');
  const buttons = html.split('<button').filter((one) => one.includes('data-model-preset='));
  const chosen = buttons.find((one) => one.includes('data-model-preset="m2"')) ?? '';
  const answering = buttons.find((one) => one.includes('data-model-preset="m1"')) ?? '';

  assert.strictEqual(buttons.length, 2, 'both model buttons were drawn');
  assert.match(chosen, /class="preset model on"/, 'the model that was pressed does not look pressed');
  assert.match(chosen, /aria-pressed="true"/, 'the pressed button says nothing to a screen reader');
  assert.doesNotMatch(answering, /class="preset model on"/, 'the model being switched away from still looked chosen');
});

test('the machinery of a turn is underlined, so an eye can step over it', () => {
  const drawn = markedTurn(OPENING, MARKS);

  assert.match(drawn, /<u class="service">Answer in English\.<\/u>/, 'the language line was not marked');
  assert.match(drawn, /<u class="service">Everything below<\/u>/, 'the material note was not marked');
  assert.match(drawn, /<u class="service">---<\/u>/, 'the fence was not marked');
  assert.match(drawn, /the passage$/, 'the passage did not survive the marking');
  assert.doesNotMatch(drawn, /<u[^>]*>the passage/, 'the passage was marked as machinery');
});

test('a question that has been SENT keeps the colours it had in the box', () => {
  // Asked for looking at a sent turn: the role, the task and three lines of machinery, all one grey.
  // A question does not stop being readable because it moved four centimetres up the page.
  const html = chatMessagesHtml([{ role: 'you', text: OPENING }], MARKS);

  assert.match(html, new RegExp(`<mark class="role">${ROLE}</mark>`), 'the role lost its colour on sending');
  assert.match(html, new RegExp(`<mark class="task">${TASK}</mark>`), 'the task lost its colour on sending');
  assert.match(html, /<u class="service">Answer in English\.<\/u>/, 'the machinery was not marked in the transcript');
});

test('an ANSWER is not marked up as a turn - it is the other side speaking', () => {
  const html = chatMessagesHtml([{ role: 'model', text: `${ROLE}\n\nsomething it said` }], MARKS);

  assert.doesNotMatch(html, /<mark class="role">/, 'an answer was marked as if it were a question');
});

test('what somebody typed is still escaped, marked or not', () => {
  const html = chatMessagesHtml([{ role: 'you', text: '<img src=x onerror=alert(1)>' }], MARKS);

  assert.doesNotMatch(html, /<img/i, 'a question reached the page as markup');
  assert.match(html, /&lt;img src=x onerror=alert\(1\)&gt;/, 'the text was dropped rather than shown');
});

test('the composer underlines the same machinery the transcript does', () => {
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  page.frames();

  assert.match(page.seen.backdrop?.innerHTML ?? '', /<u class="service">Answer in English\.<\/u>/,
    'the box did not mark the machinery');
});

/** Whether `needle` falls inside the div with this id, by walking to that div's matching close. */
function insideDiv(html: string, id: string, needle: string): boolean {
  const open = html.indexOf(`<div id="${id}"`);
  if (open < 0) {
    return false;
  }
  const tags = /<\/?div\b[^>]*>/g;
  tags.lastIndex = open;
  let depth = 0;
  let tag = tags.exec(html);
  while (tag !== null) {
    depth += tag[0].startsWith('</div') ? -1 : 1;
    if (depth === 0) {
      return html.slice(open, tag.index).includes(needle);
    }
    tag = tags.exec(html);
  }

  return false;
}

test('Send sits on the line that names the model, at the end of it', () => {
  const html = chatPageHtml(state({ providers: [...PROVIDERS], providerId: 'antigravity' }), 'n0nce');
  const row = html.slice(html.indexOf('<div class="pickerRow">'), html.indexOf('<div class="compose">'));

  assert.match(row, /id="send"/, 'Send is not on the picker row');
  assert.doesNotMatch(html.slice(html.indexOf('<div class="compose">')), /id="send"/,
    'Send was left beside the composer as well');
  assert.ok(html.indexOf('id="pickerBox"') < html.indexOf('id="send"'), 'Send comes before the model it belongs beside');
});

test('Send is NOT inside the picker, which a push rewrites wholesale', () => {
  // Every state push replaces the picker's innerHTML. A control living in there is destroyed on the
  // first one, along with the click listener bound to it once when the page loaded - and a Send
  // button that stops sending after the first answer is the worst version of this change.
  const html = chatPageHtml(state({ providers: [...PROVIDERS], providerId: 'antigravity' }), 'n0nce');

  assert.strictEqual(insideDiv(html, 'pickerBox', 'id="send"'), false, 'Send is inside the region a push replaces');
  assert.strictEqual(insideDiv(html, 'pickerBox', 'id="provider"'), true, 'the harness cannot see inside the picker');
});

test('a sent question keeps the colours it was ASKED with, not the ones in force now', () => {
  // The conversation moves on. Switch model after asking and the current role no longer matches the
  // words above it, so a question sent a minute ago quietly lost its colours. (codex and local, the
  // plan round.) The question carries what it was asked with.
  const asked = { role: 'You are a business analyst', task: TASK };
  const box = [asked.role, '', TASK, '', 'Answer in English.'].join('\n');
  const html = chatMessagesHtml([{ role: 'you', text: box, marks: asked }], MARKS);

  assert.match(html, /<mark class="role">You are a business analyst<\/mark>/, 'the question lost the role it was asked with');
  assert.match(html, /<u class="service">Answer in English\.<\/u>/, 'the machinery is named by the page, not by the message');
});

test('a question from a build before that field falls back to the conversation', () => {
  const html = chatMessagesHtml([{ role: 'you', text: OPENING }], MARKS);

  assert.match(html, new RegExp(`<mark class="role">${ROLE}</mark>`), 'an older question was left unmarked');
});

test('a burst of keystrokes paints once, not once each', () => {
  // The layer is a full rebuild of everything in the box, and a keystroke can arrive faster than the
  // browser draws. (gemini and local, the code round.)
  const page = runChatPage({ draft: OPENING, marks: MARKS });
  page.frames();
  const box = page.seen['say'];
  let painted = 0;
  const layer = page.seen.backdrop;
  assert.ok(layer, 'there is no layer to paint');
  layer.onWrite = () => { painted += 1; };
  box.value = `${OPENING}a`;
  page.fire('say', 'input');
  box.value = `${OPENING}ab`;
  page.fire('say', 'input');
  box.value = `${OPENING}abc`;
  page.fire('say', 'input');

  assert.strictEqual(painted, 0, 'a keystroke painted before the frame it asked for');
  page.frames();
  assert.strictEqual(painted, 1, 'three keystrokes painted more than once');
  assert.match(layer.innerHTML, /abc/, 'the paint that ran was not the latest text');
});

test('the chosen tone lands INSIDE the body rule, where the size already does', () => {
  // The trap this whole parsing helper exists for: a declaration placed above the body rule drops
  // the entire rule and the page renders unstyled. The tone is a second declaration in the same
  // place, so it gets the same parse rather than a substring match.
  const html = chatPageHtml(state({ textTone: -2 }), 'n0nce');
  const css = html.split('<style>')[1].split('</style>')[0];
  const bodyRule = ruleFor(css, 'body');

  assert.ok(bodyRule.includes('color-mix'), 'the chosen tone is not in the body rule: ' + bodyRule);
  assert.ok(bodyRule.includes('--coai-tone-warm'), 'a step down did not reach the body rule');
});

test('an untouched tone leaves the body rule exactly as it was', () => {
  const html = chatPageHtml(state({ textTone: 0 }), 'n0nce');
  const css = html.split('<style>')[1].split('</style>')[0];

  assert.doesNotMatch(ruleFor(css, 'body'), /color-mix/, 'a control nobody touched painted the page');
});

test('both steppers are in the header, each with its own buttons', () => {
  const html = chatPageHtml(state({ uiScale: 1, textTone: 1 }), 'n0nce');
  const header = html.slice(html.indexOf('<header'), html.indexOf('</header>'));

  assert.match(header, /data-zoom="1"/, 'the size stepper left the header');
  assert.match(header, /data-tone="1"/, 'the tone stepper is not beside it');
});

test('the Asked button is in the header with the steppers, and the region it opens is outside the scroll', () => {
  // Asked for exactly there: *"вот тут в начале справа на одной строке с плюсиками должна быть
  // кнопка показать вопрос"*. And outside `#scroll`, because *"на этот текст не должна влиять
  // прокрутка — неважно где я, в начале или в конце"*.
  const html = chatPageHtml(state({ fromSession: true }), 'n0nce');
  const header = html.slice(html.indexOf('<header'), html.indexOf('</header>'));

  assert.match(header, /id="asked"/, 'the Asked button is not beside the steppers');
  assert.ok(
    html.indexOf('id="asking"') < html.indexOf('<main id="scroll">'),
    'the region that shows the question is inside the scrolling one, so scrolling would move it',
  );
});

test('a chat opened from a FILE has no button, and no region to open', () => {
  // There is no session behind it to read, so it gets nothing rather than a button that apologises.
  const html = chatPageHtml(state({ fromSession: false }), 'n0nce');

  assert.doesNotMatch(html, /id="asked"/, 'a chat with no session behind it offered to read one');
  assert.doesNotMatch(html, /id="asking"/, 'a region nothing can open was rendered anyway');
});

test('a session file full of markup cannot break out of the page either', () => {
  // The same guarantee as the message above, by a different route: what the host reads off disk
  // arrives as a script LITERAL rather than as markup, and jsonForScript encodes its angle brackets.
  const closing = '</' + 'script>';
  const html = chatPageHtml(state({ fromSession: true, asked: [closing + '<img src=x onerror=alert(1)>'] }), 'n0nce');

  assert.strictEqual(html.split('<script').length - 1, 1, 'a session file opened a second script element');
});

test('the region opens over half a second, because that is what was asked for', () => {
  // *"добавь легкую анимацию на 0.5 сек, что б не было резкого рывка"* — a number in the request is
  // a number in the test, or the next refactor rounds it to whatever feels right.
  const css = chatPageHtml(state({ fromSession: true }), 'n0nce').split('<style>')[1].split('</style>')[0];
  const asking = ruleFor(css, '.asking');

  assert.match(asking, /transition:[^;]*max-height \.5s/, 'the region appears instead of opening');
  assert.match(asking, /overflow: hidden/, 'a closed region would spill its text over the conversation');
  assert.match(ruleFor(css, '.askedText'), /overflow-y: auto/, 'a long question would push the conversation away');
});
