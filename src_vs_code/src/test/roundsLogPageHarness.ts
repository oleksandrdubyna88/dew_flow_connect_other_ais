import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

import { DomDocument, DomElement } from './pageDom';

/**
 * The Review rounds page as it SHIPS — bundled, minified, rendered and RUN over a stub DOM — for any
 * test that asserts on what the page does rather than on its source text (`.agents/PROJECT.md`).
 *
 * <p>Extracted from `bundledPage.test.ts` when a second file needed the running page (the security
 * evidence disclosure): a second copy of the bundler and the stub beside the first is the duplication
 * `reuse-first.md` names.</p>
 */

/** The repository's `src_vs_code`. The suite runs from its root, which is what `npm test` does. */
export const ROOT = process.cwd();

/**
 * The fixture's day is TODAY, taken from the real clock, and that is load-bearing rather than lazy.
 *
 * <p>The page opens on today's range — `setToday()` reads `new Date()`, because the question
 * somebody has when they open it is almost always "what happened today". A fixture pinned to a
 * literal date therefore renders rows on exactly ONE day and an empty table on every day after it,
 * which is what this file did: written on 2026-09-05, green that afternoon, red from the next
 * morning onwards with "the page rendered no rows" — a failure that says nothing about the page and
 * everything about the calendar.</p>
 *
 * <p>Local NOON, not midnight and not a UTC literal: the page's range is built from the LOCAL day
 * while a round's stamp is UTC, so an instant near either edge lands on the neighbouring local day
 * under some offsets and the bomb comes back wearing a timezone. Noon is the only hour no offset on
 * earth can move out of its own day.</p>
 */
export function todayAtLocal(hours: number, minutes: number, seconds = 0): Date {
  const day = new Date();
  day.setHours(hours, minutes, seconds, 0);

  return day;
}

export const STARTED = todayAtLocal(12, 0);
export const COMPLETED = todayAtLocal(12, 2, 10);
export const PRICED = todayAtLocal(12, 1);
/** After the round finished, so the row has an age — and still the same local day. */
export const NOW = todayAtLocal(12, 17).getTime();

/** One finished round, priced from one ledger line — enough to exercise every cell of a row. */
export const SESSION = {
  state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
  rounds: [{
    stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 1,
    reviewers: 'all 2 reviewers answered', status: 'done',
    startedUtc: STARTED.toISOString(), completedUtc: COMPLETED.toISOString(),
    subject: 'SCOPE - something', tokensIn: 1_000_000, tokensOut: 200_000,
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: 23 }],
  }],
};

export const USED = {
  utc: PRICED.toISOString(), provider: 'codex', model: 'gpt-5.6-sol', role: 'Architecture',
  stage: 'CodeReview', seconds: 23, tokensIn: 1_000_000, tokensOut: 200_000, costUsd: null, outcome: 'ok',
};

/** The same round as the database has it: a gate that was closed, nine accepted and four rejected. */
export const LOG = {
  rounds: [{
    repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', number: 1,
    startedUtc: '2026-09-05T11:41:00.000Z', sessionId: 's1', accepted: 9, rejected: 4,
    findings: [{
      ordinal: 1, severity: 'Major', category: 'Reliability', file: 'src/Panel.cs', line: 40,
      title: 'a finding', why: '', fix: '', role: 'Architecture', isGating: true,
      providers: 'codex', resolution: 'accept', reason: '', reRaised: false,
    }],
    cursor: '2026-09-05T11:41:00.000Z|1',
    foundCount: 1,
  }],
  blindSpots: [],
  defended: [],
  totals: { rounds: 1, findings: 1, accepted: 1, rejected: 0, gating: 1, tokensIn: 0, tokensOut: 0, costUsd: 0 },
  paged: true,
  read: true,
};

export const PRICES = (model: string) =>
  model === 'gpt-5.6-sol' ? { inPerMillion: 2, outPerMillion: 10 } : undefined;

/** The page module as it SHIPS: bundled and minified, its two page-building exports mapped back to their names. */
export interface BundledLog {
  roundsLogHtml: (rows: unknown[], questions: unknown[], nonce: string, usage?: string, spots?: string, totals?: unknown, options?: Record<string, unknown>) => string;
  rowsFrom: (sessions: unknown[], now: number, priceOf?: unknown, usage?: unknown[], log?: unknown) => unknown[];
}

export function bundledLogModule(): BundledLog {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-bundle-'));
  const entry = path.join(dir, 'entry.mjs');
  const out = path.join(dir, 'bundle.cjs');
  // A tiny entry so the bundle exports exactly what the assertion needs; `--minify` is what the
  // shipped bundle uses, and the renaming it does is the whole point of this test.
  fs.writeFileSync(entry, `export { roundsLogHtml, rowsFrom } from ${JSON.stringify(path.join(ROOT, 'src', 'roundsLog.ts'))};\n`);
  // esbuild's JS API, not its binary: on Linux `node_modules/esbuild/bin/esbuild` is a shell shim,
  // and running it through node is a SyntaxError — which is exactly how this test passed on Windows
  // and failed on the CI runner the day it was written.
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const esbuild = require('esbuild') as { buildSync: (options: Record<string, unknown>) => void };
  esbuild.buildSync({
    entryPoints: [entry], outfile: out, bundle: true, format: 'cjs', platform: 'node', minify: true,
  });
  const bundle = fs.readFileSync(out, 'utf8');
  // CJS, because minification renames the exported bindings too: the export CLAUSE is what maps them
  // back to their public names, and `module.exports` keeps that map where an ESM bundle loses it to
  // any attempt to evaluate the text directly.
  const shim = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', bundle)(shim, shim.exports);
  fs.rmSync(dir, { recursive: true, force: true });

  return shim.exports as unknown as BundledLog;
}

export function bundledPage(sessions: unknown[] = [SESSION], log: unknown = LOG): { html: string; script: string } {
  const module_ = bundledLogModule();
  // WITH a row, and a priced one. This helper used to render an empty page, and an empty page never
  // calls the functions that format a row — which is exactly how 0.29.12 shipped "R is not defined":
  // `cost3` is embedded by its source text and CALLED `money`, a module-level binding the minifier
  // had renamed to `R`. The page defines `money`, so nothing looked missing until a row asked for
  // its cost.
  const html = module_.roundsLogHtml(module_.rowsFrom(sessions, NOW, PRICES, [USED], log), [], 'n0nce');

  return { html, script: html.slice(html.indexOf('<script'), html.lastIndexOf('</script>')) };
}

/** A stub element that REMEMBERS its listeners, so the page's own wiring can be fired. */
export interface Stub {
  innerHTML: string;
  textContent: string;
  hidden: boolean;
  value: string;
  className: string;
  disabled: boolean;
  indeterminate: boolean;
  checked: boolean;
  listeners: Record<string, Array<() => void>>;
  addEventListener: (type: string, fn: () => void) => void;
  getAttribute: () => null;
  setAttribute: () => void;
  removeAttribute: () => void;
  querySelectorAll: () => never[];
}

export function stub(): Stub {
  const listeners: Record<string, Array<() => void>> = {};

  return {
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    disabled: false, indeterminate: false, checked: false, listeners,
    addEventListener(type: string, fn: () => void) {
      const registered = listeners[type] ?? [];
      listeners[type] = registered;
      registered.push(fn);
    },
    getAttribute: () => null,
    setAttribute() { /* the page sets attributes it never reads back here */ },
    // A real element has one, and the busy bar calls it as the page loads (research/PLAN_busy_marks_on_every_webview.md).
    removeAttribute() { /* nor removes any it reads back */ },
    querySelectorAll: () => [],
  };
}

/** The page, actually running, with every element it asks for and every message it sends. */
export function runningPage(sessions: unknown[] = [SESSION], log: unknown = LOG): {
  seen: Record<string, Stub>;
  sent: Array<Record<string, unknown>>;
  click: (event: unknown) => void;
} {
  const { script } = bundledPage(sessions, log);
  const body = script.slice(script.indexOf('>') + 1);
  const seen: Record<string, Stub> = {};
  const sent: Array<Record<string, unknown>> = [];
  const clicks: Array<(event: unknown) => void> = [];
  const document_ = {
    getElementById: (id: string) => (seen[id] ??= stub()),
    querySelectorAll: () => [],
    addEventListener(type: string, fn: (event: unknown) => void) {
      if (type === 'click') { clicks.push(fn); }
    },
  };
  new Function('document', 'window', 'acquireVsCodeApi', body)(
    document_,
    { addEventListener() { /* the page listens on document for clicks */ } },
    () => ({ postMessage: (message: Record<string, unknown>) => sent.push(message), setState() {} }));

  return {
    seen,
    sent,
    click: (event: unknown) => { for (const fn of clicks) { fn(event); } },
  };
}

/**
 * A click on something INSIDE a row — which is the whole point.
 *
 * <p>Both selectors match, exactly as they do in a browser: the control and the row it sits in. A
 * branch that forgot to return would therefore also open the row, and that is observable here
 * because opening a row asks the host for its findings.</p>
 */
export function clickInRow(selector: string, attribute: string, key: string): unknown {
  const answers: Record<string, { getAttribute: (name: string) => string | null; className: string }> = {
    [selector]: { getAttribute: (name: string) => (name === attribute ? key : null), className: '' },
    'tr[data-key]': { getAttribute: (name: string) => (name === 'data-key' ? key : null), className: '' },
  };

  return { target: { closest: (asked: string) => answers[asked] ?? null } };
}

/** The key of the first row the page actually rendered. */
export function firstRenderedKey(html: string): string {
  const found = /data-export="([^"]+)"/.exec(html);
  assert.notEqual(found, null, 'the page rendered no Export button at all');

  return found?.[1] ?? '';
}

/** The page RUN on a document built from its own html (`pageDom.ts`): what it shows, what it sent, and its timers. */
export interface DomPage {
  readonly document: DomDocument;
  readonly sent: Array<Record<string, unknown>>;
  /** A message from the host, as `postMessage` delivers it. */
  message: (data: unknown) => void;
  /** An element the page drew — a test fails here, by name, when the page did not draw it. */
  element: (id: string) => DomElement;
  /** Run every timer the page set, once — the fifteen-second watchdog among them. */
  runTimers: () => void;
}

/**
 * The shipped page, run on a real tree of its own markup. Its timers are held, not run — a test turns them by hand —
 * and nothing else is in reach: the document, the window's listener, the VS Code bridge and the two timer functions.
 */
export function runningDomPage(module_: BundledLog = bundledLogModule(), sessions: unknown[] = [SESSION]): DomPage {
  const html = module_.roundsLogHtml(module_.rowsFrom(sessions, NOW, PRICES, [USED], LOG), [], 'n0nce');
  const document_ = new DomDocument(html);
  const sent: Array<Record<string, unknown>> = [];
  const heard: Array<(event: { data: unknown }) => void> = [];
  const timers: Array<() => void> = [];
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  new Function('document', 'window', 'acquireVsCodeApi', 'setTimeout', 'clearTimeout', script.slice(script.indexOf('>') + 1))(
    document_,
    { addEventListener(type: string, fn: (event: { data: unknown }) => void) { if (type === 'message') { heard.push(fn); } } },
    () => ({ postMessage: (message: Record<string, unknown>) => sent.push(message), setState() {}, getState: () => undefined }),
    (fn: () => void) => { timers.push(fn); return timers.length; },
    () => undefined,
  );
  const failed = document_.getElementById('failed');
  assert.ok(failed !== null && failed.hidden, `the page stopped on an error: ${failed?.textContent ?? ''}`);

  return {
    document: document_,
    sent,
    message: (data) => { for (const fn of heard) { fn({ data }); } },
    element: (id) => {
      const found = document_.getElementById(id);
      assert.ok(found !== null, `the page draws no #${id}`);

      return found;
    },
    runTimers: () => { for (const fn of timers.splice(0)) { fn(); } },
  };
}
