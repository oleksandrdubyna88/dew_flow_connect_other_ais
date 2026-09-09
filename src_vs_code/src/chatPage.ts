import { escapeHtml, jsonForScript } from './webviewHtml';
import { ZOOM_CSS, zoomControlHtml, zoomScript, zoomStyle } from './zoomControl';

/**
 * The conversation tab: the passage that started it, what has been said, and a box to say more.
 *
 * <p>PURE — no `vscode`, no `node:`. `bundledPage.test.ts` bundles this module the way the shipped
 * page is bundled and refuses a `node:` import in it, which is what makes "a page module imports
 * nothing from the host" a check rather than a sentence. Everything the page needs arrives as a
 * value; everything it wants done goes back as a message.</p>
 *
 * <p><b>The page escapes nothing it is handed at render time.</b> Every string that reaches
 * `innerHTML` — the messages, the failure, the capped region, the picker — is built by a function in
 * THIS file or by `chatPanel.ts`, and escaped there, at the point it is built. That is the invariant:
 * escaping happens where the string is made, never where it is written. A reviewer looking at the
 * `innerHTML` assignments in the script below should follow the value back to its builder rather
 * than conclude the page is unsafe. (Raised on the code round; it was true, and it was implicit.)</p>
 *
 * <p><b>The passage is at the top and it is not decoration.</b> The menu path takes whatever is in
 * the clipboard and cannot know whether it is the passage just selected or something copied an hour
 * ago — the gate raised that three times. Showing the text the conversation is ABOUT is how a person
 * sees a stale clipboard instead of discovering it in the answer.</p>
 *
 * <p><b>It used to be capped in height with a scrollbar of its own, and is not any more.</b> The cap
 * existed because "a fifty-line selection would otherwise push the composer off the screen on open"
 * — and the composer is pinned now, so nothing can push it anywhere. What a long selection pushes
 * down is the conversation, which is what the page scrolls to anyway. Two scrollbars on one page was
 * the price of the old arrangement and the operator named it.</p>
 *
 * <p><b>The composer is disabled while a turn runs, and that is a feature.</b> A real explanation
 * took 9.4 s when it was measured, and eight of those seconds are silent. Two turns down one NDJSON
 * pipe would interleave; the page makes that impossible rather than the session refusing it late.</p>
 */

/** One thing said, by one of the two parties. */
export interface ChatMessage {
  readonly role: 'you' | 'model';
  readonly text: string;
}

/** A model the picker may offer. `remote` models say what they cannot do. */
export interface ChatModelChoice {
  readonly id: string;
  readonly label: string;
  readonly caption: string;
}

export interface ChatPageState {
  /**
   * This conversation's own id, minted where the tab is made and never shown to anybody.
   *
   * <p>The page hands it straight back through `setState`, which is the only thing VS Code preserves
   * about a webview across a window reload — so it is the whole of how a restored tab knows WHICH
   * conversation it is. A title cannot do that job: two Claude Code sessions can both be called
   * `main`, which is why `chatPanels.ts` is keyed by object identity rather than by name.</p>
   */
  readonly id: string;
  /** The Claude Code session this tab belongs to — its label, shown as the heading. */
  readonly title: string;
  readonly passage: string;
  readonly messages: readonly ChatMessage[];
  readonly models: readonly ChatModelChoice[];
  readonly modelId: string;
  /** A turn is in flight: the composer is locked and the thinking line is shown. */
  readonly running: boolean;
  /**
   * The conversation has reached its limit and cannot take another turn.
   *
   * <p>Only a remote model reaches this: it holds no conversation, so turn four re-sends everything
   * said so far for the fourth time, and the thread's cost grows while its usefulness does not. The
   * owner capped it at three on 2026-09-08. A capped page must OFFER something — a locked box with
   * no way out is the failure the gate named — so it shows the two honest actions, start again or
   * move the thread to a local model, which does have memory.</p>
   */
  readonly capped: boolean;
  /** Empty when nothing failed. A sentence when something did. */
  readonly failure: string;
  /**
   * What the composer opens with, unsent.
   *
   * <p>The menu path fills it rather than sending: it took whatever was on the clipboard and
   * cannot know how old that is, so the person presses send once and the wrong-content vendor
   * call becomes impossible. A draft is therefore a first-class part of the page state, not a
   * decoration — it is the whole difference between the two doors.</p>
   */
  readonly draft: string;
  readonly uiScale: number;
}

/** The messages region on its own, so the host can push it without re-rendering the page. */
export function chatMessagesHtml(messages: readonly ChatMessage[]): string {
  if (messages.length === 0) {
    return '<p class="empty">Nothing asked yet.</p>';
  }

  return messages
    .map(
      (message) =>
        `<div class="msg ${message.role === 'you' ? 'you' : 'model'}">`
        + `<div class="who">${message.role === 'you' ? 'You' : 'The other AI'}</div>`
        + `<div class="what">${escapeHtml(message.text)}</div></div>`,
    )
    .join('');
}

/** The picker, or nothing at all when there is only one model to pick. */
export function chatPickerHtml(models: readonly ChatModelChoice[], chosen: string): string {
  if (models.length < 2) {
    return '';
  }
  const options = models
    .map(
      (model) =>
        `<option value="${escapeHtml(model.id)}"${model.id === chosen ? ' selected' : ''}>`
        + `${escapeHtml(model.label)}</option>`,
    )
    .join('');
  const caption = models.find((model) => model.id === chosen)?.caption ?? '';

  return `<div class="picker"><select id="model" aria-label="Which model answers">${options}</select>`
    + `<span class="caption" id="caption">${escapeHtml(caption)}</span></div>`;
}

/** What a capped conversation offers instead of a composer nobody can use. */
export function chatCappedHtml(capped: boolean): string {
  if (!capped) {
    return '';
  }

  return '<div class="capped"><p>This model keeps no conversation, so every follow-up re-sends the whole '
    + 'thread. Three turns is the limit.</p>'
    + '<button type="button" id="restart">Start a new conversation</button> '
    + '<button type="button" id="useLocal">Continue with a local model</button></div>';
}

/** The page's own styles. Its own function so the document below stays readable. */
function chatStyle(uiScale: number): string {
  // The zoom goes INSIDE the body rule, and that is not a tidiness preference. It used to sit above
  // it, where CSS has no such thing as a declaration: a parser consuming a qualified rule appends
  // every token to the prelude until it meets `{`, and `;` does not end one — so the selector became
  // `font-size: 13px; body`, and the whole body rule was dropped. The page had no margin, no
  // padding, no font and no background of its own for as long as that stood. The help page always
  // did it this way (`helpPage.ts`); this one did not.
  return `  html, body { height: 100%; }
  body { font-family: var(--vscode-font-family); color: var(--vscode-foreground); background: var(--vscode-editor-background); margin: 0; padding: 0; display: flex; flex-direction: column; overflow: hidden; ${zoomStyle(uiScale)} }
  /* The conversation scrolls; the page does not. The min-height of 0 is what makes that true: a flex
     child refuses to shrink below its content without it, so the region would never scroll, the
     body would instead, and the composer would leave the screen — the symptom this layout exists
     to end. (The gate raised it, and it is invisible to every test but a reading of this rule.) */
  #scroll { flex: 1 1 auto; min-height: 0; overflow-y: auto; padding: 16px 20px 0; }
  /* Positioned, because the jump control hangs above it. */
  #composer { flex: 0 0 auto; padding: 8px 20px 12px; position: relative; }
  /* OUT OF FLOW, and that is the point rather than the styling. In the flow it would take room in
     the footer, which shrinks the scrolling region, which changes clientHeight - one of the three
     numbers the follow decision is made from. A control that appears BECAUSE a reader was not
     followed must not alter what "at the bottom" means. No display property either, so the hidden
     attribute keeps working. */
  .jump { position: absolute; bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%); font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 12px; padding: 4px 12px; cursor: pointer; box-shadow: 0 2px 6px rgba(0, 0, 0, .35); }
  .jump:hover { background: var(--vscode-button-hoverBackground); }
  .compose { display: flex; gap: 8px; align-items: flex-end; }
  header { display: flex; align-items: baseline; gap: 12px; margin-bottom: 12px; }
  h1 { font-size: 1.2em; margin: 0; }
  .passage { border-left: 3px solid var(--vscode-panel-border); padding: 6px 0 6px 12px; margin: 0 0 16px; white-space: pre-wrap; opacity: .85; }
  .msg { margin: 0 0 14px; }
  .msg .who { font-size: .85em; opacity: .7; margin-bottom: 3px; }
  .msg .what { white-space: pre-wrap; }
  .msg.you .what { opacity: .85; }
  .empty { opacity: .6; }
  .thinking { opacity: .75; margin: 0 0 12px; }
  .queued { opacity: .8; font-size: .9em; }
  .failure { border: 1px solid var(--vscode-inputValidation-errorBorder, var(--vscode-panel-border)); border-radius: 4px; padding: 8px 10px; margin: 0 0 12px; }
  .capped { border: 1px solid var(--vscode-panel-border); border-radius: 4px; padding: 10px 12px; margin: 0 0 10px; }
  .capped p { margin: 0 0 8px; }
  .picker { display: flex; gap: 8px; align-items: center; margin: 0 0 8px; }
  .caption { font-size: .85em; opacity: .7; }
  /* The 30 % lives HERE and only here, so the CSS path and the JavaScript fallback below cannot
     disagree about where the ceiling is: the fallback sets a height and this caps it. field-sizing
     is Chromium-only, which is not a limitation in a page that renders nowhere but VS Code's own
     webview - but the manifest declares support back to 1.85, whose engine has never heard of it,
     and there the fallback does the growing. */
  textarea { flex: 1 1 auto; min-width: 0; box-sizing: border-box; min-height: 64px; max-height: 30vh; field-sizing: content; overflow-y: auto; resize: none; font: inherit; color: var(--vscode-input-foreground); background: var(--vscode-input-background); border: 1px solid var(--vscode-input-border, var(--vscode-panel-border)); border-radius: 4px; padding: 8px; }
  textarea[disabled] { opacity: .6; }
  #send { flex: 0 0 auto; font: inherit; color: var(--vscode-button-foreground); background: var(--vscode-button-background); border: none; border-radius: 4px; padding: 8px 14px; cursor: pointer; }
  #send:hover:not([disabled]) { background: var(--vscode-button-hoverBackground); }
  #send[disabled] { opacity: .6; cursor: default; }
  .hint { font-size: .85em; opacity: .6; margin-top: 4px; }
${ZOOM_CSS}`;
}

/**
 * The line shown while a turn is in flight, and where in a queue it is.
 *
 * <p>"Thinking…" is the whole truth for a local CLI: the process is running and it is thinking. It
 * is a guess for a Team server, where the honest answer for most of the wait is that somebody else's
 * round has the vendor. A shared server queues twenty deep per person by design, so an unchanging
 * spinner for minutes is the one shape a busy server and a broken tab look identical in — and the
 * position was already on the wire. (gemini, the code round.)</p>
 *
 * <p>`position` is 0 when the server did not say, or when the turn has left the queue and is being
 * answered. Both are "no number to show", and neither is position zero.</p>
 */
export function chatStatusHtml(running: boolean, position: number): string {
  if (!running) {
    return '';
  }
  const where = Number.isInteger(position) && position > 0
    ? ` <span class="queued">· waiting in the queue, ${position} ahead</span>`
    : '';

  return `<p class="thinking">Thinking…${where}</p>`;
}

/**
 * What each pushed region holds, for the page as it is first rendered.
 *
 * <p>One function because two callers need the SAME four strings: the markup writes them, and the
 * script remembers them as "what is already on screen" so that the first pushed state can tell a
 * change from a repeat. Two copies of these expressions would drift on the day one of them gains a
 * condition, and the symptom would be a page that scrolls on a push that changed nothing.</p>
 */
type Regions = Record<'messages' | 'thinking' | 'capped' | 'failure', string>;

function regionsOf(state: ChatPageState): Regions {
  return {
    messages: chatMessagesHtml(state.messages),
    thinking: chatStatusHtml(state.running, 0),
    capped: chatCappedHtml(state.capped),
    failure: state.failure.length === 0 ? '' : `<div class="failure">${escapeHtml(state.failure)}</div>`,
  };
}

/** The document, without its head or its script. */
function chatBody(state: ChatPageState, regions: Regions): string {
  const locked = state.running || state.capped;

  return `<main id="scroll">
<header><h1>${escapeHtml(state.title)}</h1>${zoomControlHtml(state.uiScale)}</header>
<div class="passage" id="passage">${escapeHtml(state.passage)}</div>
<div id="failure">${regions.failure}</div>
<div id="messages">${regions.messages}</div>
<div id="thinking">${regions.thinking}</div>
<div id="capped">${regions.capped}</div>
</main>
<footer id="composer">
<button type="button" id="jump" class="jump" hidden>Jump to newest ↓</button>
<div id="pickerBox">${chatPickerHtml(state.models, state.modelId)}</div>
<div class="compose">
<textarea id="say" rows="3" placeholder="Ask about the text above…"${locked ? ' disabled' : ''}>${escapeHtml(state.draft)}</textarea>
<button type="button" id="send"${locked ? ' disabled' : ''}>Send</button>
</div>
<div class="hint">Enter sends · Shift+Enter for a new line</div>
</footer>`;
}

/**
 * How close to the bottom still counts as being AT the bottom, in pixels.
 *
 * <p>The operator's rule says "a line or two of slack"; the gate asked for a number, and it is right
 * to — a boundary a reader can land on is a boundary a test has to name. 48 is about two lines at
 * the base size and three at the largest zoom step down. It is a constant rather than something
 * derived from the font size on purpose: a reader a hair short of the bottom considers themselves at
 * the bottom whatever size they are reading at, and a slack that shrinks with the text would make
 * the rule behave differently for the people most likely to have scrolled.</p>
 */
export const FOLLOW_SLACK_PX = 48;

/**
 * Was the reader at the bottom — and therefore should a new answer be scrolled to?
 *
 * <p>Rule 2 of the decided scroll behaviour (entry 23, settled 2026-09-09). Pure, and taking its
 * slack as a parameter rather than reading the constant, because its SOURCE is embedded into the
 * page: it must reference nothing outside itself or the page gets a function whose free variable
 * does not exist there.</p>
 *
 * <p>Written as `!(distance > slack)` rather than `distance <= slack` for the case that reaches it
 * most rarely and matters anyway: a page that cannot measure itself yields NaN, every comparison
 * with NaN is false, and the negation turns that into "follow". A page whose numbers are unreadable
 * is a page whose reader has not scrolled away from anything.</p>
 */
export function shouldFollow(
  scrollTop: number,
  clientHeight: number,
  scrollHeight: number,
  slack: number,
): boolean {
  return !(scrollHeight - (scrollTop + clientHeight) > slack);
}

/** The page's behaviour. Its own function for the same reason the styles are. */
function chatScript(state: ChatPageState, regions: Regions): string {
  return `(function () {
  const vscode = acquireVsCodeApi();
  // The one thing VS Code keeps about this page across a window reload, and it hands it back to
  // deserializeWebviewPanel. It is how a restored tab says which conversation it is — a title could
  // not, because two Claude Code sessions can share one.
  vscode.setState(${jsonForScript({ id: state.id })});
  let captions = ${jsonForScript(Object.fromEntries(state.models.map((model) => [model.id, model.caption])))};
  // The webview swallows a thrown error silently, and a page that stops responding to Enter with no
  // sign of why is the defect this trap exists to name. Same shape as the rounds log's.
  window.onerror = function (message) {
    const box = document.getElementById('failure');
    if (box) { box.textContent = 'The page hit an error: ' + message; }
    // And TELL the host. A trap that only writes into the page leaves the extension believing the
    // conversation is fine while the tab has stopped answering Enter.
    vscode.postMessage({ type: 'pageError', message: String(message) });
  };
  ${zoomScript()}
  // Bound by ASSIGNMENT, not declared: the minifier renames a declaration and leaves the name in the
  // template string that calls it, which is how the rounds log shipped a dead page twice. Embedding
  // the source is also what makes the function the tests exercise the function the page runs.
  var shouldFollow = ${shouldFollow.toString()};
  var SLACK = ${FOLLOW_SLACK_PX};
  var pendingFollow = 0;
  // Whether the reader was at the bottom BEFORE the last thing that moved the layout under them.
  // Recomputed on every scroll, which is the only event that means the READER moved; a resize or a
  // growing composer must ask what this remembers, because after one the numbers no longer describe
  // where the person put themselves.
  var wasAtBottom = true;
  // A follow is out and has not run yet. If the reader cancels it, they are left scrolled up with an
  // answer nobody took them to - so the cancel hands them the jump control instead.
  var followOutstanding = false;
  // What was last PUT into each region, so a repeat can be told from a change without asking the
  // browser to serialise the DOM back to us on every push. Seeded with what the MARKUP holds, or the
  // first push would compare against nothing, read as a change, and scroll a reader for content that
  // was already on their screen.
  var lastWritten = ${jsonForScript(regions)};
  // The ONE scroll event the page owes itself, armed by its own write and consumed by the event it
  // causes. Two shapes were tried and both were wrong: a flag cleared on a later frame stays raised
  // until that frame comes, so every scroll in between reads as the page's own; and a position kept
  // indefinitely turns the bottom into a magic pixel - a reader who scrolls away and comes back to
  // it, which is what "scroll back down" IS, was still being read as the page for the rest of the
  // tab's life. Armed only when the write actually moved anything, or nothing would consume it.
  var selfScrollTo = null;
  // A scroll set in the same tick as the write scrolls to a height the browser has not laid out yet
  // and lands short. The next frame has the real height, and setTimeout is the fallback for a host
  // that has no requestAnimationFrame - the bundled page runs in exactly such a stub.
  function afterLayout(fn) {
    if (typeof requestAnimationFrame === 'function') { requestAnimationFrame(fn); }
    else if (typeof setTimeout === 'function') { setTimeout(fn, 0); }
  }
  // THE TWO ENTRY POINTS for moving this page, and the contract stories 3 and 4 are held to: the
  // jump control and the composer's resize go through scheduleFollow or landOnNewest, never through
  // scrollTop of their own. One place decides, or the rule is four rules that agree by accident.
  function landOnNewest() {
    const scroll = document.getElementById('scroll');
    if (!scroll) { return; }
    // Our own write fires a scroll event. Without this flag the page would cancel its own next
    // follow, and the rule would work exactly once per tab.
    const was = scroll.scrollTop;
    scroll.scrollTop = scroll.scrollHeight;
    // Whoever brought them here, they are here: the control has nothing left to offer. Retiring it
    // in the click handler alone left it standing for every other path that lands on the newest.
    offerJump(false);
    // What it CLAMPED to, not what we asked for: a browser answers scrollHeight - clientHeight. And
    // null when nothing moved, because then no scroll event is coming to consume the arming.
    selfScrollTo = scroll.scrollTop === was ? null : scroll.scrollTop;
    // Known NOW rather than sampled next frame. A run of keystrokes calls the re-pin several times
    // before any frame arrives, and each one was reading a flag that had not been updated yet.
    wasAtBottom = true;
  }
  // Deferred to the next frame, and CANCELLABLE. The reader can move between the frame being asked
  // for and the frame arriving - a drag, a wheel, Page Up - and a scroll that was right when it was
  // scheduled is a hijack by the time it runs. Re-measuring in the callback cannot tell: the content
  // is already in, so a reader who WAS at the bottom now measures short of it. Only the reader knows
  // they moved, so the cancel is their scroll event. (Two vendors raised this on the plan round.)
  // ASKED, not assumed. The gate's blocking finding on the plan: a page that takes field-sizing on
  // faith works on the machine it was written on and silently never grows anywhere else, which is
  // the one failure mode a person cannot report because nothing happens.
  var sizesItself = typeof CSS !== 'undefined' && typeof CSS.supports === 'function'
    && CSS.supports('field-sizing', 'content');
  // Height from content, ceiling from the CSS. Called on input, after a send has emptied the box and
  // after a draft is pushed into it - shrinking back is a call, not a hope.
  function fitComposer() {
    if (sizesItself) { return; }
    const box = document.getElementById('say');
    if (!box || !box.style) { return; }
    box.style.height = 'auto';
    box.style.height = box.scrollHeight + 'px';
    // Only where nothing else is watching. On a host WITH ResizeObserver this same height change
    // reaches the observer too, and both firing means two re-pins and two queued samplings racing
    // over one event. The engines without field-sizing are the ones this fallback exists for, and
    // requirement 8 was promised to them as well. (gemini, the code round.)
    if (typeof ResizeObserver !== 'function') { repinIfTheyWereAtTheBottom(); }
  }
  // A height change nobody asked for must not move the conversation out from under a reader. The
  // remembered flag is right here where re-measuring is not: they did NOT scroll, the region shrank
  // underneath them, so measuring now would call them scrolled away and leave the last answer behind
  // the box they are typing into.
  function repinIfTheyWereAtTheBottom() {
    if (wasAtBottom) {
      landOnNewest();

      return;
    }
    // Not at the bottom, so nothing moves - but the resize changed the numbers under them, and the
    // next frame is when they can be read again.
    afterLayout(rememberWhereTheyAre);
  }
  function rememberWhereTheyAre() {
    const scroll = document.getElementById('scroll');
    if (scroll) {
      wasAtBottom = shouldFollow(scroll.scrollTop, scroll.clientHeight, scroll.scrollHeight, SLACK);
    }
  }
  function offerJump(show) {
    const control = document.getElementById('jump');
    if (control) { control.hidden = !show; }
  }
  function scheduleFollow() {
    const token = ++pendingFollow;
    followOutstanding = true;
    afterLayout(function () {
      if (token === pendingFollow) {
        landOnNewest();
        followOutstanding = false;
      }
    });
  }
  function send() {
    const box = document.getElementById('say');
    if (!box || box.disabled) { return; }
    const text = box.value.trim();
    if (text.length === 0) { return; }
    box.value = '';
    vscode.postMessage({ type: 'command', command: 'send', text: text });
    fitComposer();
    // Locked HERE, not when the host gets round to saying so. Between the post and the state that
    // comes back there was a window - small, and the width of a second Enter - in which a second
    // turn went down a pipe that carries one. Two vendors found it independently; the page did not
    // need telling that it had just sent something.
    lock(true);
    // And no focus call: focusing a control you have just disabled is how a caret ends up in a box
    // nobody can type in. The unlock below already hands focus back when the turn ends, whichever
    // way it went, and one place deciding that is the point.
  }
  // The one place either control's lock is written. Two controls deciding the same thing from the
  // same inputs is two chances to disagree, and the one that disagrees is the one that sends.
  function lock(locked) {
    const box = document.getElementById('say');
    const button = document.getElementById('send');
    if (box) { box.disabled = locked; }
    if (button) { button.disabled = locked; }
  }
  const say = document.getElementById('say');
  if (say) {
    say.addEventListener('keydown', function (event) {
      if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); send(); }
    });
    say.addEventListener('input', fitComposer);
  }
  // The second caller of the ONE send. Attached here rather than as an onclick attribute: the page's
  // CSP is script-src 'nonce-...', so an inline handler is not merely untidy, it is a dead button.
  const sendButton = document.getElementById('send');
  if (sendButton) {
    sendButton.addEventListener('click', function () { send(); });
  }
  function wirePicker() {
    const model = document.getElementById('model');
    if (!model) { return; }
    model.addEventListener('change', function () {
      const caption = document.getElementById('caption');
      if (caption) { caption.textContent = captions[model.value] || ''; }
      vscode.postMessage({ type: 'command', command: 'pick', id: model.value });
    });
  }
  function wireCapped() {
    const restart = document.getElementById('restart');
    if (restart) {
      restart.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'restart' });
      });
    }
    const useLocal = document.getElementById('useLocal');
    if (useLocal) {
      useLocal.addEventListener('click', function () {
        vscode.postMessage({ type: 'command', command: 'useLocal' });
      });
    }
  }
  wirePicker();
  wireCapped();
  // Rule 1: the tab opens looking at the last thing said, not at the passage above it. Three times,
  // because the height is not final until the fonts are: now (the first paint is already close),
  // after layout, and after the fonts settle where the host reports them. Idempotent, so the two
  // extra calls cost nothing on a page that was already there.
  landOnNewest();
  scheduleFollow();
  if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === 'function') {
    // The correction belongs to the OPENING follow and dies with it. Scheduling unconditionally
    // here made a fresh token, so a cancelled open came back to life whenever the fonts happened to
    // settle - a reader who opened the tab and started reading upward was dragged down by a font.
    // Three findings from one vendor said this, and they were right.
    const openedAt = pendingFollow;
    document.fonts.ready.then(function () {
      if (pendingFollow === openedAt) { scheduleFollow(); }
    });
  }
  const scrollRegion = document.getElementById('scroll');
  if (scrollRegion) {
    scrollRegion.addEventListener('scroll', function () {
      if (selfScrollTo !== null && scrollRegion.scrollTop === selfScrollTo) {
        selfScrollTo = null;
      } else {
        pendingFollow++;
        // They cancelled a follow that had not run. An answer arrived, they were not taken to it,
        // and without this nothing on the page would say so. (codex, the plan round.)
        if (followOutstanding) {
          followOutstanding = false;
          offerJump(true);
        }
      }
      rememberWhereTheyAre();
      // A reader who came back under their own steam has answered the question the control was
      // asking. One that stays up after that is one nobody trusts.
      if (shouldFollow(scrollRegion.scrollTop, scrollRegion.clientHeight, scrollRegion.scrollHeight, SLACK)) {
        offerJump(false);
      }
    });
  }
  // A footer whose height changes takes the room from the conversation above it. A reader who was at
  // the bottom did NOT scroll - the region shrank underneath them - so re-measuring would call them
  // scrolled away and leave the last answer behind the box they are typing into: rule 2 broken from
  // the other side. What they WERE is the only measurement that survives the resize, so it is taken
  // before the layout changes and acted on after. A host without ResizeObserver keeps today's
  // behaviour rather than breaking.
  // Where the host has one, it catches every way the composer's height can change, including the
  // CSS path where no script of ours runs at all. Where it has none, fitComposer says so instead -
  // and those are the same hosts, since an engine without field-sizing is an old engine.
  if (typeof ResizeObserver === 'function') {
    const composer = document.getElementById('composer');
    if (composer) { new ResizeObserver(repinIfTheyWereAtTheBottom).observe(composer); }
  }
  const jump = document.getElementById('jump');
  if (jump) {
    jump.addEventListener('click', function () {
      landOnNewest();
      offerJump(false);
      // The box, unless a turn is running - focusing a disabled control puts the caret where
      // nobody can type, which is the same rule send() follows.
      const box = document.getElementById('say');
      if (box && !box.disabled && typeof box.focus === 'function') { box.focus(); }
    });
  }
  window.addEventListener('message', function (event) {
    const data = event.data || {};
    if (data.type !== 'state') { return; }
    // BEFORE any write. Read after one, scrollHeight already includes what just arrived, so a reader
    // who was at the bottom measures as a screen short of it and is never followed - rule 2 turns
    // silently into "never follow", and the only place that shows is the real webview. One snapshot
    // at the head of the one handler every region arrives through is what makes it true by
    // construction rather than by remembering to do it in five places.
    const scroll = document.getElementById('scroll');
    const follow = !scroll || shouldFollow(scroll.scrollTop, scroll.clientHeight, scroll.scrollHeight, SLACK);
    let wrote = false;
    const messages = document.getElementById('messages');
    const thinking = document.getElementById('thinking');
    const capped = document.getElementById('capped');
    const failure = document.getElementById('failure');
    // A CHANGE, not an assignment. A retry, or a poll that pushes the state again, assigns the same
    // html - and counting that as an insertion scrolls a reader for content already in front of
    // them. Compared against what we LAST WROTE rather than against the element: reading innerHTML
    // back makes the browser serialise the whole subtree on every push, and what comes back is
    // normalised - attributes reordered, entities decoded - so an unchanged push can read as
    // different and a changed one as the same.
    if (messages && typeof data.messagesHtml === 'string' && lastWritten.messages !== data.messagesHtml) {
      messages.innerHTML = data.messagesHtml;
      lastWritten.messages = data.messagesHtml;
      wrote = true;
    }
    if (thinking && typeof data.thinkingHtml === 'string' && lastWritten.thinking !== data.thinkingHtml) {
      thinking.innerHTML = data.thinkingHtml;
      lastWritten.thinking = data.thinkingHtml;
      wrote = true;
    }
    // These two clear when they are NOT mentioned - the protocol has always meant that, and a
    // capped notice or a failure left on screen after it stopped being true is one a person acts on.
    // Requiring a string here was this branch's own regression; the gate caught it.
    const nextCapped = typeof data.cappedHtml === 'string' ? data.cappedHtml : '';
    if (capped && lastWritten.capped !== nextCapped) {
      capped.innerHTML = nextCapped;
      lastWritten.capped = nextCapped;
      wireCapped();
      wrote = true;
    }
    // The picker is re-rendered rather than nudged: after "continue with a local model" the whole
    // list can be different, and a select that only had its value set would show a model it no
    // longer offers.
    const pickerBox = document.getElementById('pickerBox');
    if (pickerBox && typeof data.pickerHtml === 'string') { pickerBox.innerHTML = data.pickerHtml; wirePicker(); }
    const nextFailure = typeof data.failureHtml === 'string' ? data.failureHtml : '';
    if (failure && lastWritten.failure !== nextFailure) {
      failure.innerHTML = nextFailure;
      lastWritten.failure = nextFailure;
      wrote = true;
    }
    const passage = document.getElementById('passage');
    if (passage && typeof data.passage === 'string' && passage.textContent !== data.passage) {
      passage.textContent = data.passage;
      wrote = true;
    }
    // A draft pushed into an OPEN tab. Appended rather than assigned, so a half-typed follow-up
    // is never thrown away by a second invocation - losing what somebody typed is the one thing
    // the queue in the session was also built to avoid.
    if (typeof data.draft === 'string' && data.draft.length > 0) {
      const box2 = document.getElementById('say');
      if (box2) {
        box2.value = box2.value.length > 0 ? box2.value + '\\n\\n' + data.draft : data.draft;
        fitComposer();
        if (typeof box2.focus === 'function') { box2.focus(); }
      }
    }
    const box = document.getElementById('say');
    // Only a real boolean moves the lock. A state that says nothing about running - a partial push,
    // or a null across the bridge - must leave the composer as it is rather than quietly unlocking
    // it while a turn is still in flight. (local, the second code round.)
    if (box && typeof data.running === 'boolean' && typeof data.capped === 'boolean') {
      const wasLocked = box.disabled;
      lock(data.running || data.capped);
      // Back to the box when the turn ends. Without this every single follow-up costs a mouse click,
      // nine seconds after the last one — which is the whole conversation, one click at a time.
      if (wasLocked && !box.disabled && typeof box.focus === 'function') { box.focus(); }
    }
    // Rules 2 and 3, applied once for whatever this push contained - an answer, a failure, a
    // thinking line or a capped notice. There is one rule, not one per outcome, and the two halves
    // are exclusive by construction: something arrived, and either the reader was taken to it or
    // they are told it is there.
    if (wrote) {
      if (follow) { scheduleFollow(); } else { offerJump(true); }
    }
  });
}());`;
}

export function chatPageHtml(state: ChatPageState, nonce: string): string {
  // Built once and handed to both. The markup writes these four strings and the script remembers
  // them; serialising a long conversation twice per render is a cost that grows with the thing a
  // person is most likely to have a lot of. (local, the code round.)
  const regions = regionsOf(state);

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(state.title)}</title>
<style>
${chatStyle(state.uiScale)}
</style>
</head>
<body>
${chatBody(state, regions)}
<script nonce="${nonce}">
${chatScript(state, regions)}
</script>
</body>
</html>`;
}
