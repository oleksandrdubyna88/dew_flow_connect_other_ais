import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

import { DomDocument } from './pageDom';
import type { DomElement } from './pageDom';

/**
 * The accounts tab, RUN: its own html parsed into a tree (`pageDom.ts`), its script run over that tree with a clock the test moves and the
 * webview's own state API, as the roles and rounds pages are (`.agents/PROJECT.md`: a webview page is tested by RUNNING it).
 */

export interface AccountsPage {
  readonly document: DomDocument;
  /** Everything the page posted to the host, in order, the `ready` among them. */
  readonly posted: readonly Record<string, unknown>[];
  /** What the page gave `setState`, in order. */
  readonly states: readonly unknown[];
  /** A message from the host, delivered as `postMessage` delivers it. */
  message: (data: unknown) => void;
  /** The clock goes on by `ms` and the page's interval runs, as it would have, with no message from the host. */
  advance: (ms: number) => void;
  nowMs: () => number;
  /** Every element of a tag whose own text is exactly `label`. */
  buttons: (label: string) => DomElement[];
  /** The one such button, or a failure that names it. */
  button: (label: string) => DomElement;
  /** Everything a person can read in the page. */
  text: () => string;
  /** What the page said of its own failure, `''` when it did not fail. */
  failure: () => string;
}

export interface RunOptions {
  readonly now?: number;
  /** What `getState` answers at the start. */
  readonly state?: unknown;
}

export function pageScriptOf(html: string): string {
  const open = html.indexOf('<script');
  const start = html.indexOf('>', open) + 1;
  const end = html.lastIndexOf('</script>');
  assert.ok(open >= 0 && end > start, 'the page has no script to run');

  return html.slice(start, end);
}

export function runAccountsPage(html: string, options: RunOptions = {}): AccountsPage {
  const document_ = new DomDocument(html);
  const posted: Record<string, unknown>[] = [];
  const states: unknown[] = [];
  const heard: ((event: { data: unknown }) => void)[] = [];
  const intervals: (() => void)[] = [];
  const clock = { now: options.now ?? Date.UTC(2026, 10, 26, 14, 0, 0) };
  let stored: unknown = options.state;
  class FakeDate extends Date {
    constructor(...args: [] | [number | string | Date]) {
      if (args.length === 0) {
        super(clock.now);
      } else {
        super(args[0] as number);
      }
    }

    static override now(): number { return clock.now; }
  }
  new Function('document', 'window', 'acquireVsCodeApi', 'setInterval', 'Date', pageScriptOf(html))(
    document_,
    { addEventListener(type: string, fn: (event: { data: unknown }) => void) { if (type === 'message') { heard.push(fn); } } },
    () => ({
      postMessage: (message: Record<string, unknown>) => { posted.push(message); },
      getState: (): unknown => stored,
      setState: (next: unknown): void => { stored = structuredClone(next); states.push(structuredClone(next)); },
    }),
    (fn: () => void) => { intervals.push(fn); return intervals.length; },
    FakeDate,
  );
  const buttons = (label: string): DomElement[] => document_.querySelectorAll('button').filter((one) => one.textContent === label);

  return {
    document: document_,
    posted,
    states,
    message: (data) => { for (const fn of heard) { fn({ data }); } },
    advance: (ms) => { clock.now += ms; for (const fn of intervals) { fn(); } },
    nowMs: () => clock.now,
    buttons,
    button: (label) => {
      const found = buttons(label);
      assert.equal(found.length, 1, `${found.length} buttons are labelled "${label}"`);

      return found[0] as DomElement;
    },
    text: () => document_.root.textContent,
    failure: () => {
      const box = document_.getElementById('failed');

      return box === null || box.hidden ? '' : box.textContent;
    },
  };
}

/** The tags of an element's descendants, counted: what a page DREW, apart from what it was told. */
export function tagCounts(root: DomElement): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const one of root.descendants()) {
    counts[one.tag] = (counts[one.tag] ?? 0) + 1;
  }

  return counts;
}

/** The page's own module, bundled and minified the way the extension ships it, so a function embedded by its source text is checked as it ships. */
export function bundledAccountsModule(): { readonly accountsHtml: (nonce: string) => string } {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-bundle-'));
  const entry = path.join(dir, 'entry.mjs');
  const out = path.join(dir, 'bundle.cjs');
  fs.writeFileSync(entry, `export { accountsHtml } from ${JSON.stringify(path.join(process.cwd(), 'src', 'remoteAccountsPage.ts'))};\n`);
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const esbuild = require('esbuild') as { buildSync: (options: Record<string, unknown>) => void };
  esbuild.buildSync({ entryPoints: [entry], outfile: out, bundle: true, format: 'cjs', platform: 'node', minify: true });
  const shim = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', fs.readFileSync(out, 'utf8'))(shim, shim.exports);
  fs.rmSync(dir, { recursive: true, force: true });

  return shim.exports as unknown as { accountsHtml: (nonce: string) => string };
}
