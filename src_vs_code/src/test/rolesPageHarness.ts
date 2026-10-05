import assert from 'node:assert/strict';

import { rolesHtml, type RolesPageState } from '../rolesPage';
import { busyBarOf, type Control, PageClock } from './panelPageHarness';
import { camel } from './datasetName';

/**
 * The roles page's own script, RUN — the harness two test files share.
 *
 * <p>It was written inside `rolesPageScript.test.ts` and extracted here the moment a second file
 * needed it, rather than copied: a second DOM shim is two shims that drift, and the one thing a
 * shim must be is the same for everybody asserting against it.</p>
 *
 * <p><b>Why any of this exists.</b> A page is assembled as a template literal and handed to VS Code
 * as text, so a substring assertion over that text cannot see a control wired to the wrong branch —
 * the string contains everything it was supposed to contain. `.agents/PROJECT.md` makes that an
 * operator ruling: a webview page is tested by RUNNING it.</p>
 *
 * <p>The context is explicit and small — five names, no ambient environment, no filesystem, no
 * network — and the script is synchronous, so it needs no deadline of its own.</p>
 */

/** Just enough of an element for the page's own `closest`, `dataset`, class and value reads. */
export class Node {
  readonly dataset: Record<string, string>;
  readonly tagName: string;
  type: string;
  value: string;
  checked: boolean;
  className: string;
  /** What `setAttribute` has written — the page keeps `aria-selected` in step with the class. */
  readonly attributes: Record<string, string>;
  hidden: boolean;
  parent: Node | undefined;
  /** Whether the page moved focus here — the arrow keys of a tab strip must. */
  focused: boolean;
  /**
   * What a click on this node does: dispatch through the document's listeners, as a browser's
   * `element.click()` bubbles to a delegated handler. Wired by {@link runPageHtml} for the nodes it is
   * handed; a node nobody wired has no click, which fails loudly rather than doing nothing.
   */
  dispatchClick: ((node: Node) => void) | undefined;

  constructor(dataset: Record<string, string> = {}, tagName = 'INPUT') {
    this.dataset = dataset;
    this.tagName = tagName;
    this.type = 'text';
    this.value = '';
    this.checked = false;
    this.className = '';
    this.attributes = {};
    this.hidden = false;
    this.focused = false;
    this.dispatchClick = undefined;
  }

  /**
   * Focus, refused under a hidden ancestor — as a browser refuses it for an element that is not rendered.
   * Stricter than a permissive fake on purpose: the Settings page once restored a caret into a pane the
   * page had not shown yet, and a fake that accepted that focus would have passed the test for it.
   */
  focus(): void {
    if (this.rendered()) {
      this.focused = true;
    }
  }

  /** Neither this node nor any ancestor is hidden. */
  private rendered(): boolean {
    return !this.hidden && (this.parent?.rendered() ?? true);
  }

  /** The kinds of the listeners a page bound to this element, in order. */
  readonly listeners: string[] = [];

  /** The listeners themselves: a click runs this element's own before it bubbles to the document's. */
  private readonly handlers: { readonly kind: string; readonly run: (event: unknown) => void }[] = [];

  addEventListener(kind: string, run?: (event: unknown) => void): void {
    this.listeners.push(kind);
    if (run !== undefined) {
      this.handlers.push({ kind, run });
    }
  }

  /**
   * A click: this element's own click listeners first, then the document's, as a browser bubbles it.
   * The text-size and text-tone controls bind each button directly, so a shim that only bubbled would
   * never reach them. A node with neither is not in the running page, and says so.
   */
  click(): void {
    const own = this.handlers.filter((handler) => handler.kind === 'click');
    assert.ok(own.length > 0 || this.dispatchClick !== undefined, 'this node was clicked but is not in the running page');
    for (const handler of own) {
      handler.run({ target: this, currentTarget: this, preventDefault: (): void => undefined });
    }
    this.dispatchClick?.(this);
  }

  /**
   * This element's own listeners of one kind, handed an event whose target is `target` — one step of a bubbling event.
   * Public for `pageTree.ts`'s `bubbled`, which walks the ancestors as a browser does: a script that binds a listener on
   * an element (the panel binds every `[data-prompt]`) hears an event fired at something inside it.
   */
  runOwn(kind: string, target: Node, extra: Readonly<Record<string, unknown>> = {}): void {
    for (const handler of this.handlers.filter((one) => one.kind === kind)) {
      handler.run({ ...extra, target, currentTarget: this, preventDefault: (): void => undefined });
    }
  }

  /** The chain upwards, matching `[data-x]` and `[data-x="y"]` — the only two shapes the page uses. */
  closest(selector: string): Node | null {
    const exact = /^\[data-([a-z-]+)="([^"]*)"\]$/.exec(selector);
    const any = /^\[data-([a-z-]+)\]$/.exec(selector);
    const key = camel((exact ?? any)?.[1] ?? '');
    // Walking up a DOM chain from this node IS the operation: the loop variable starts at `this`
    // and is reassigned to each parent, which is not an alias kept around.
    // eslint-disable-next-line @typescript-eslint/no-this-alias -- deliberate, see above
    for (let at: Node | undefined = this; at !== undefined; at = at.parent) {
      const held = at.dataset[key];
      if (held !== undefined && (exact === null || held === exact[2])) {
        return at;
      }
    }

    return null;
  }

  setAttribute(name: string, value: string): void {
    this.attributes[name] = value;
  }

  under(parent: Node): Node {
    this.parent = parent;

    return this;
  }
}


/** The `<script>` the page ships, cut out of its own html. */
export function pageScript(html: string): string {
  const open = html.indexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the roles page has no script to run');

  return html.slice(start, end);
}

/**
 * An element's `style`, recording what the page writes: the two properties the text controls set by name,
 * and every custom property through `setProperty` — the tone is two of those.
 */
export class Style {
  fontSize = '';
  color = '';
  readonly custom: Record<string, string> = {};

  setProperty(name: string, value: string): void {
    this.custom[name] = value;
  }
}

/** An event as the page's handlers receive it, with the one thing a test reads back. */
export interface FiredEvent {
  readonly defaultPrevented: boolean;
}

/** What the running page offers a test: what it has posted, a way to press something, and the host's voice. */
export interface Page {
  readonly posted: readonly Record<string, unknown>[];
  /** An event at `target`, carrying `extra` (a key, say) — returned so a test can see `preventDefault`. */
  fire(kind: string, target: Node, extra?: Readonly<Record<string, unknown>>): FiredEvent;
  /** A message from the host, delivered to every `window` message listener, as `postMessage` does. */
  message(data: unknown): void;
  /** What the page wrote to `document.body.style`. */
  readonly body: Style;
  /** What the page wrote to `document.documentElement.style` — the root, which `rem` is measured from. */
  readonly root: Style;
  /** The busy bar, when the page was run on a clock and drew one (research/PLAN_busy_marks_on_every_webview.md, E3). */
  readonly bar: Control | null;
}

/**
 * What the person's presses posted: every post but the page's own load-time `ready`, without the busy mark's `seq` and
 * `doc`. Whether a press is NUMBERED is rolesBusyMark.test.ts's question; these tests ask what it SAYS
 * (research/PLAN_busy_marks_on_every_webview.md, E3).
 */
export function presses(page: Page): readonly Record<string, unknown>[] {
  return page.posted.filter((one) => one['type'] !== 'ready').map(({ seq: _seq, doc: _doc, ...rest }) => rest);
}

/**
 * Run the roles page's script and collect everything it posts.
 *
 * <p>`inDocument` are the nodes `document.querySelectorAll` will answer with, keyed by the selector
 * the page asks for. A test that presses a tab needs the page to FIND the other tabs and the
 * sections, and a shim that answers every selector with nothing would let a broken switch look
 * exactly like a working one.</p>
 */
export function runRolesPage(
  state: RolesPageState,
  inDocument: Readonly<Record<string, readonly Node[]>> = {},
  clock?: PageClock,
): Page {
  return runPageHtml(rolesHtml(state, 'test-nonce'), inDocument, clock);
}

/**
 * Run ANY page's script — the roles page's shim, widened for the commands page (issue #467) rather than
 * copied: two copies of a DOM shim drift, and the one that drifts is the one that stops finding a
 * selector and passes.
 */
export function runPageHtml(
  html: string,
  inDocument: Readonly<Record<string, readonly Node[]>> = {},
  /**
   * A clock the test moves. Given, the page's timers run on it and the page's busy bar is answered by id; without it
   * every timer runs at once and no bar is found, which is what every test written before the busy mark expects.
   */
  clock?: PageClock,
  /**
   * What the webview's state API holds — read by `getState`, replaced by `setState`, and readable by the test after. Absent:
   * nothing saved and nothing kept, which is a first load.
   */
  state?: { value: unknown },
): Page {
  const bar = clock === undefined ? null : busyBarOf(html);
  const posted: Record<string, unknown>[] = [];
  const listeners = new Map<string, ((event: unknown) => void)[]>();
  const heard: ((event: { data: unknown }) => void)[] = [];
  const add = (kind: string, handler: (event: unknown) => void): void => {
    listeners.set(kind, [...(listeners.get(kind) ?? []), handler]);
  };
  const fire = (kind: string, target: Node, extra: Readonly<Record<string, unknown>> = {}): FiredEvent => {
    let prevented = false;
    const event = { ...extra, target, preventDefault: (): void => { prevented = true; } };
    for (const handler of listeners.get(kind) ?? []) {
      handler(event);
    }

    return { defaultPrevented: prevented };
  };
  for (const nodes of Object.values(inDocument)) {
    for (const node of nodes) {
      node.dispatchClick = (clicked) => { fire('click', clicked); };
    }
  }
  const fakeDocument = {
    addEventListener: add,
    querySelectorAll: (selector: string): readonly Node[] => inDocument[selector] ?? [],
    // The first node a test put in the document under that selector, or nothing — never a stand-in.
    querySelector: (selector: string): Node | null => inDocument[selector]?.[0] ?? null,
    // An id answers only with the node a test put in the document under `#id` — never an inert stand-in,
    // which would let a page that looks up an element it never draws run as if it worked.
    getElementById: (id: string): Node | Control | null => (id === 'busy-bar' && bar !== null ? bar : inDocument[`#${id}`]?.[0] ?? null),
    body: { style: new Style() },
    documentElement: { style: new Style() },
  };

  const script = pageScript(html);
   
  // whole point: a scan of its text cannot tell a matching selector from one that matches nothing.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', script);
  body(
    // A webview always has its state API; this one has nothing saved, which is a first load.
    () => ({
      postMessage: (message: Record<string, unknown>) => { posted.push(message); },
      getState: (): unknown => state?.value,
      setState: (next: unknown): void => { if (state !== undefined) { state.value = structuredClone(next); } },
    }),
    fakeDocument,
    {
      addEventListener: (kind: string, handler: (event: unknown) => void): void => {
        if (kind === 'message') {
          heard.push(handler);
        }
        add(kind, handler);
      },
    },
    clock?.setTimeout ?? ((fn: () => void): number => { fn(); return 0; }),
    clock?.clearTimeout ?? ((): void => undefined),
  );

  return {
    posted,
    fire,
    message: (data) => {
      for (const handler of heard) {
        handler({ data });
      }
    },
    body: fakeDocument.body.style,
    root: fakeDocument.documentElement.style,
    bar,
  };
}
