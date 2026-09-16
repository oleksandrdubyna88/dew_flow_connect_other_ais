import assert from 'node:assert/strict';

import { rolesHtml, type RolesPageState } from '../rolesPage';

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
  hidden: boolean;
  parent: Node | undefined;

  constructor(dataset: Record<string, string> = {}, tagName = 'INPUT') {
    this.dataset = dataset;
    this.tagName = tagName;
    this.type = 'text';
    this.value = '';
    this.checked = false;
    this.className = '';
    this.hidden = false;
  }

  /** The chain upwards, matching `[data-x]` and `[data-x="y"]` — the only two shapes the page uses. */
  closest(selector: string): Node | null {
    const exact = /^\[data-([a-z-]+)="([^"]*)"\]$/.exec(selector);
    const any = /^\[data-([a-z-]+)\]$/.exec(selector);
    const key = camel((exact ?? any)?.[1] ?? '');
    for (let at: Node | undefined = this; at !== undefined; at = at.parent) {
      const held = at.dataset[key];
      if (held !== undefined && (exact === null || held === exact[2])) {
        return at;
      }
    }

    return null;
  }

  under(parent: Node): Node {
    this.parent = parent;

    return this;
  }
}

/** `data-remove-prompt` reaches the script as `dataset.removePrompt`, as a browser spells it. */
export function camel(attribute: string): string {
  return attribute.replace(/-([a-z])/g, (_, letter: string) => letter.toUpperCase());
}

/** The `<script>` the page ships, cut out of its own html. */
export function pageScript(html: string): string {
  const open = html.indexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the roles page has no script to run');

  return html.slice(start, end);
}

/** What the running page offers a test: what it has posted, and a way to press something. */
export interface Page {
  readonly posted: readonly Record<string, unknown>[];
  fire(kind: string, target: Node): void;
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
): Page {
  const posted: Record<string, unknown>[] = [];
  const listeners = new Map<string, ((event: { target: Node }) => void)[]>();
  const add = (kind: string, handler: (event: { target: Node }) => void): void => {
    listeners.set(kind, [...(listeners.get(kind) ?? []), handler]);
  };
  const fakeDocument = {
    addEventListener: add,
    querySelectorAll: (selector: string): readonly Node[] => inDocument[selector] ?? [],
    getElementById: (): null => null,
    body: { style: { fontSize: '' } },
  };

  const script = pageScript(rolesHtml(state, 'test-nonce'));
  // eslint-disable-next-line no-new-func -- the shipped script is the thing under test, which is the
  // whole point: a scan of its text cannot tell a matching selector from one that matches nothing.
  const body = new Function('acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', script);
  body(
    () => ({ postMessage: (message: Record<string, unknown>) => { posted.push(message); } }),
    fakeDocument,
    { addEventListener: add },
    (fn: () => void): number => { fn(); return 0; },
    (): void => undefined,
  );

  return {
    posted,
    fire: (kind, target) => {
      for (const handler of listeners.get(kind) ?? []) {
        handler({ target });
      }
    },
  };
}
