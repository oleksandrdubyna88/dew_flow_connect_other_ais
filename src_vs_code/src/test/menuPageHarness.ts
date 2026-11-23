import assert from 'node:assert/strict';

import { DomDocument, type DomElement } from './pageDom';

/**
 * A webview page that is the sidebar's menu (or an old page that stands where it stood), RUN on the shared DOM shim (`pageDom.ts`): its own script over
 * the document its own html drew. Used by the tests of the shell's menu page and of the old page's `menuReady`, which must agree.
 *
 * <p>What the shim does NOT do is what a browser does by default, and this file adds exactly one thing of that kind and says so: a press on a
 * `summary` opens or closes its `details` and fires that element's `toggle` — the default action of the element — unless the page stopped it.
 * Everything else a press does, the page's own listeners do.</p>
 */

export interface MenuRun {
  readonly doc: DomDocument;
  /** Every message the page posted to the host, in order. */
  readonly posted: readonly Record<string, unknown>[];
  /** The section elements the page drew, in document order. */
  readonly sections: () => readonly DomElement[];
  /** A person pressing a section's header. */
  readonly press: (id: string) => void;
}

/** The last `<script>` of a page, cut out of its html. */
function scriptOf(html: string): string {
  const open = html.lastIndexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.indexOf('</script>', start);
  assert.ok(open >= 0 && end > start, 'the page has no script to run');

  return html.slice(start, end);
}

/**
 * Runs the page. `deliver` is told of each post AS IT IS MADE (the webview hands them to the host one by one), so a test can act on the host's side
 * while the page is still running.
 */
export function runMenuPage(html: string, deliver: (message: Record<string, unknown>) => void = () => undefined, stubs: Record<string, unknown> = {}): MenuRun {
  const doc = new DomDocument(html);
  const posted: Record<string, unknown>[] = [];
  const body = doc.querySelector('body');
  const fakeDocument = {
    querySelectorAll: (selector: string) => doc.querySelectorAll(selector),
    querySelector: (selector: string) => doc.querySelector(selector),
    getElementById: (id: string) => doc.getElementById(id),
    addEventListener: (type: string, listener: (event: never) => void) => { doc.addEventListener(type, listener as never); },
    createElement: () => { throw new Error('the page created an element, which this run does not model'); },
    body: body ?? { style: {} },
    documentElement: { style: { setProperty: () => undefined } },
  };
  const api = () => ({
    postMessage: (message: Record<string, unknown>): void => { posted.push(message); deliver(message); },
    getState: (): unknown => undefined,
    setState: (): void => undefined,
  });
  const names = ['acquireVsCodeApi', 'document', 'window', 'setTimeout', 'clearTimeout', 'Event', ...Object.keys(stubs)];
  const values = [
    api, fakeDocument, { addEventListener: () => undefined }, () => 0, () => undefined, class { constructor(readonly type: string) {} }, ...Object.values(stubs),
  ];
  new Function(...names, scriptOf(html))(...values);

  const sections = (): readonly DomElement[] => doc.querySelectorAll('.section');
  const press = (id: string): void => {
    const section = sections().find((one) => one.getAttribute('data-section') === id);
    const summary = section?.querySelector('summary') ?? null;
    assert.ok(section !== undefined && summary !== null, `the page draws no header for ${id}`);
    // A header inside an inert element takes no press, as a browser's.
    if (summary.closest('[inert]') !== null) {
      return;
    }
    const event = doc.dispatch('click', summary);
    if (!event.defaultPrevented) {
      section.open = !section.open;
      doc.dispatch('toggle', section);
    }
  };

  return { doc, posted, sections, press };
}
