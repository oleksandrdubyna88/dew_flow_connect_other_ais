import assert from 'node:assert/strict';
import { test } from 'node:test';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';

/**
 * The page is checked as it SHIPS — bundled and minified — not as it compiles.
 *
 * <p>0.29.10 shipped a Review rounds page that came up as a header row over nothing. The same HTML
 * rendered all ninety-eight rows in node and in headless Chromium; only the installed extension
 * failed, with `rowMatches is not defined`. The page embeds its sort and filter functions by their
 * SOURCE TEXT so the tested function is the one that runs — and esbuild, minifying, renames a
 * function that is not a top-level export. The page received `function m(a, b, c, d)` and called
 * `rowMatches()`.</p>
 *
 * <p>Every test in this suite ran against `out/`, which is compiled and not bundled, so none of them
 * could see it. This one bundles the module the way `npm run bundle` does and asserts on what the
 * page then produces. It is slow by the standards of this suite (a second or so) and it is the only
 * test that can catch this class of defect at all.</p>
 */

/** The repository's `src_vs_code`. The suite runs from its root, which is what `npm test` does. */
const ROOT = process.cwd();

/** Every function the page embeds by its SOURCE TEXT, and therefore every one this file guards. */
const EMBEDDED = ['compareRows', 'rowMatches', 'inView', 'money', 'cost3', 'costTitle', 'asInstant'];

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
function todayAtLocal(hours: number, minutes: number, seconds = 0): Date {
  const day = new Date();
  day.setHours(hours, minutes, seconds, 0);

  return day;
}

const STARTED = todayAtLocal(12, 0);
const COMPLETED = todayAtLocal(12, 2, 10);
const PRICED = todayAtLocal(12, 1);
/** After the round finished, so the row has an age — and still the same local day. */
const NOW = todayAtLocal(12, 17).getTime();

/** One finished round, priced from one ledger line — enough to exercise every cell of a row. */
const SESSION = {
  state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
  rounds: [{
    stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 1,
    reviewers: 'all 2 reviewers answered', status: 'done',
    startedUtc: STARTED.toISOString(), completedUtc: COMPLETED.toISOString(),
    subject: 'SCOPE - something', tokensIn: 1_000_000, tokensOut: 200_000,
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: 23 }],
  }],
};

const USED = {
  utc: PRICED.toISOString(), provider: 'codex', model: 'gpt-5.6-sol', role: 'Architecture',
  stage: 'CodeReview', seconds: 23, tokensIn: 1_000_000, tokensOut: 200_000, costUsd: null, outcome: 'ok',
};

/** The same round as the database has it: a gate that was closed, nine accepted and four rejected. */
const LOG = {
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

const PRICES = (model: string) =>
  model === 'gpt-5.6-sol' ? { inPerMillion: 2, outPerMillion: 10 } : undefined;

function bundledPage(): { html: string; script: string } {
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
  const module_ = shim.exports as unknown as {
    roundsLogHtml: (rows: unknown[], questions: unknown[], nonce: string, usage?: string) => string;
    rowsFrom: (sessions: unknown[], now: number, priceOf?: unknown, usage?: unknown[], log?: unknown) => unknown[];
  };
  // WITH a row, and a priced one. This helper used to render an empty page, and an empty page never
  // calls the functions that format a row — which is exactly how 0.29.12 shipped "R is not defined":
  // `cost3` is embedded by its source text and CALLED `money`, a module-level binding the minifier
  // had renamed to `R`. The page defines `money`, so nothing looked missing until a row asked for
  // its cost.
  const html = module_.roundsLogHtml(module_.rowsFrom([SESSION], NOW, PRICES, [USED], LOG), [], 'n0nce');
  fs.rmSync(dir, { recursive: true, force: true });

  return { html, script: html.slice(html.indexOf('<script'), html.lastIndexOf('</script>')) };
}

test('the minified bundle still defines the functions the page calls', () => {
  const { script } = bundledPage();

  for (const name of EMBEDDED) {
    assert.ok(
      new RegExp(`var ${name}\\s*=\\s*function`).test(script),
      `${name} is not defined in the page the bundle produces — this is the 0.29.10 defect`,
    );
  }
});

test('the page script the bundle produces parses and runs', () => {
  const { script } = bundledPage();
  // Cut after the opening tag rather than matching it: a regexp shaped like an HTML tag filter is
  // one CodeQL flags on sight (js/bad-tag-filter), and it is right that the shape is fragile — the
  // nonce cannot contain a '>' but a reader has to know that to believe the pattern.
  const body = script.slice(script.indexOf('>') + 1);

  // Parses at all — the cheapest half of what the webview does with it.
  assert.doesNotThrow(() => new Function(body), 'the page script is not valid JavaScript');

  // And runs: a stub DOM and the VS Code API bridge, exactly as the webview provides them.
  const seen: Record<string, { innerHTML: string; textContent: string; hidden: boolean }> = {};
  const element = () => ({
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    addEventListener() {}, getAttribute: () => null, setAttribute() {}, querySelectorAll: () => [],
  });
  const document_ = {
    getElementById: (id: string) => (seen[id] ??= element() as never),
    querySelectorAll: () => [],
    addEventListener() {},
  };
  assert.doesNotThrow(
    () => new Function('document', 'window', 'acquireVsCodeApi', body)(
      document_, { addEventListener() {} }, () => ({ postMessage() {}, setState() {} })),
    'the page script threw on its first render',
  );
  assert.equal(seen['failed']?.textContent ?? '', '', 'the page reported an error to itself on first render');
  assert.match(
    seen['rows']?.innerHTML ?? '',
    /<tr/,
    'the page rendered no rows, so nothing that formats a row was ever called');
  // And that the row went through the cell that reads the database's own counts: a page which
  // rendered the badge but not this would look right and still hide whether the gate was closed.
  assert.match(
    seen['rows']?.innerHTML ?? '',
    /9 ✓ 4 ✗/,
    'the status cell did not render what the gate closed at');
});

/* ------------------------------------------------------------------------------------------------
 * The export controls, RUN rather than matched.
 *
 * The conventions rule is that an executable artefact is tested by EXECUTING it: a substring of a
 * generated page cannot see that a button is wired to the wrong branch, because the string contains
 * everything it is supposed to contain. The code round raised exactly that against the rounds-log
 * page tests, and these are the two behaviours this plan added to it.
 * --------------------------------------------------------------------------------------------- */

/** A stub element that REMEMBERS its listeners, so the page's own wiring can be fired. */
interface Stub {
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
  querySelectorAll: () => never[];
}

function stub(): Stub {
  const listeners: Record<string, Array<() => void>> = {};

  return {
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    disabled: false, indeterminate: false, checked: false, listeners,
    addEventListener(type: string, fn: () => void) { (listeners[type] ??= []).push(fn); },
    getAttribute: () => null,
    setAttribute() { /* the page sets attributes it never reads back here */ },
    querySelectorAll: () => [],
  };
}

/** The page, actually running, with every element it asks for and every message it sends. */
function runningPage(): {
  seen: Record<string, Stub>;
  sent: Array<Record<string, unknown>>;
  click: (event: unknown) => void;
} {
  const { script } = bundledPage();
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
function clickInRow(selector: string, attribute: string, key: string): unknown {
  const answers: Record<string, { getAttribute: (name: string) => string | null; className: string }> = {
    [selector]: { getAttribute: (name: string) => (name === attribute ? key : null), className: '' },
    'tr[data-key]': { getAttribute: (name: string) => (name === 'data-key' ? key : null), className: '' },
  };

  return { target: { closest: (asked: string) => answers[asked] ?? null } };
}

/** The key of the first row the page actually rendered. */
function firstRenderedKey(html: string): string {
  const found = /data-export="([^"]+)"/.exec(html);
  assert.notEqual(found, null, 'the page rendered no Export button at all');

  return found?.[1] ?? '';
}

test('the shipped Export button sends that ROUND, and does not open the row as well', () => {
  const page = runningPage();
  const key = firstRenderedKey(page.seen['rows']?.innerHTML ?? '');

  page.click(clickInRow('[data-export]', 'data-export', key));

  const exports = page.sent.filter((message) => message['command'] === 'export');
  assert.equal(exports.length, 1, 'one press is one export');
  const rounds = exports[0]?.['rounds'];
  assert.ok(Array.isArray(rounds) && rounds.length === 1, 'it carries exactly the round pressed');
  assert.equal((rounds[0] as { key?: string }).key, key);

  // Falling through to the row branch OPENS the row, and an open row paints a detail row under
  // itself. Verified by removing the branch's `return`: this assertion is what goes red.
  assert.doesNotMatch(page.seen['rows']?.innerHTML ?? '', /class="detail"/,
    'pressing Export also opened the row — the export branch fell through to it');
});

test('the shipped tick box selects the row, and does not open it', () => {
  const page = runningPage();
  const key = firstRenderedKey(page.seen['rows']?.innerHTML ?? '');

  page.click(clickInRow('[data-pick]', 'data-pick', key));

  assert.doesNotMatch(page.seen['rows']?.innerHTML ?? '', /class="detail"/,
    'ticking the box also opened the row — the pick branch fell through to it');
  assert.match(page.seen['rows']?.innerHTML ?? '', /checked/,
    'the box was ticked but the page repainted it unticked');

  // And the selection is what the toolbar button then exports.
  const button = page.seen['exportpicked'];
  assert.equal(button?.disabled, false, 'the toolbar button stayed disabled with a row selected');
  for (const fn of button?.listeners['click'] ?? []) { fn(); }

  const exports = page.sent.filter((message) => message['command'] === 'export');
  assert.equal(exports.length, 1);
  const rounds = exports[0]?.['rounds'];
  assert.ok(Array.isArray(rounds) && rounds.length === 1);
  assert.equal((rounds[0] as { key?: string }).key, key);
});

test('a function embedded by its source calls nothing the minifier can rename', () => {
  // The rule this file exists for, stated as a CHECK rather than left to a runtime error. A function
  // embedded by `.toString()` lands in a scope where only its own name was re-declared, so any other
  // module-level binding it calls arrives under the minified name - `R`, `q`, `Ee` - and the page
  // dies the moment that line runs. Self-contained is the whole contract.
  const { script } = bundledPage();

  for (const name of EMBEDDED) {
    assert.deepEqual(
      strangers(embedded(script, name), name),
      [],
      `${name} calls a name that only exists inside the bundle - inline what it needs`);
  }
});

/** The source of one embedded function, from `var name = function` to its closing brace. */
function embedded(script: string, name: string): string {
  const start = script.indexOf(`var ${name} = function`);
  assert.notEqual(start, -1, `${name} is not embedded in the page`);
  let depth = 0;
  for (let at = script.indexOf('{', start); at < script.length; at++) {
    depth += script[at] === '{' ? 1 : script[at] === '}' ? -1 : 0;
    if (depth === 0) {
      return script.slice(start, at + 1);
    }
  }

  return assert.fail(`${name} is never closed`);
}

/**
 * Names this function calls that it did not declare and cannot see.
 *
 * <p>Short ones only: a minifier writes one to three characters and nothing here is written that way
 * by hand, so `R(` inside an embedded function is a call into the bundle's own scope. Methods are
 * skipped - `a.b()` is a property of something already in hand.</p>
 */
function strangers(body: string, name: string): string[] {
  const declared = [...body.matchAll(/\b(?:function|var|let|const)\s+([A-Za-z_$][\w$]*)/g)].map((m) => m[1]);
  const params = body.slice(body.indexOf('(') + 1, body.indexOf(')')).split(',').map((p) => p.trim());
  const mine = new Set([name, 'if', 'for', 'while', 'switch', 'catch', 'return', 'typeof', ...declared, ...params]);

  return [...new Set(
    [...body.matchAll(/(?:^|[^\w$.])([A-Za-z_$][\w$]{0,2})\s*\(/g)]
      .map((m) => m[1] as string)
      .filter((called) => !mine.has(called)))];
}

test('the fixture lands on the local day the page opens on', () => {
  // The dependency the file used to carry silently, stated as a check. The two tests above assert
  // on RENDERED rows, so anything that puts the fixture outside the page's opening range empties
  // the table and they fail claiming the page formats no rows - a sentence that sends the next
  // reader into the bundler. This one fails first and names the calendar instead.
  const localDay = (at: Date) =>
    `${at.getFullYear()}-${String(at.getMonth() + 1).padStart(2, '0')}-${String(at.getDate()).padStart(2, '0')}`;
  const today = localDay(new Date());

  for (const [what, when] of [['started', STARTED], ['completed', COMPLETED], ['priced', PRICED]] as const) {
    assert.equal(
      localDay(when), today,
      `the fixture's ${what} stamp is not on today's local day, so the page opens with it filtered out`);
  }
});

/* ------------------------------------------------------------------------------------------------
 * The SECOND page. Until 2026-09-08 this file bundled one module, so "a page module imports nothing
 * from the host" was a rule the chat page could have broken on its first day without anything
 * noticing. The split adds a parameter rather than a copy: one bundler, two entry points.
 * ---------------------------------------------------------------------------------------------- */

/** Bundles one module the way `npm run bundle` does, and returns the text esbuild produced. */
function bundleOf(module_: string, exported: string): string {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'coai-bundle-'));
  const entry = path.join(dir, 'entry.mjs');
  const out = path.join(dir, 'bundle.cjs');
  fs.writeFileSync(entry, `export { ${exported} } from ${JSON.stringify(path.join(ROOT, 'src', module_))};\n`);
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const esbuild = require('esbuild') as { buildSync: (options: Record<string, unknown>) => void };
  esbuild.buildSync({
    entryPoints: [entry], outfile: out, bundle: true, format: 'cjs', platform: 'node', minify: true,
  });
  const bundle = fs.readFileSync(out, 'utf8');
  fs.rmSync(dir, { recursive: true, force: true });

  return bundle;
}

/**
 * The chat page as it ships: bundled, minified, then rendered.
 *
 * @param over what this render needs that the default state does not — a failure to show, a retry to
 *   offer. Spread LAST, so a scenario names only the field it is about.
 */
function bundledChatPage(over: Record<string, unknown> = {}): { bundle: string; html: string } {
  const bundle = bundleOf('chatPage.ts', 'chatPageHtml');
  const shim = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', bundle)(shim, shim.exports);
  const module_ = shim.exports as unknown as {
    chatPageHtml: (state: unknown, nonce: string) => string;
  };
  const html = module_.chatPageHtml(
    {
      id: 'conversation-1',
      title: 'привет',
      passage: 'The reviewers are read-only.',
      messages: [{ role: 'model', text: 'because the worktree is pinned' }],
      models: [
        { id: 'antigravity', label: 'Gemini', caption: 'local' },
        { id: 'remsoftdev-codex', label: 'GPT (team)', caption: 'remote · no memory' },
      ],
      providers: [
        { id: 'antigravity', label: 'Gemini', caption: 'local', models: [{ id: 'g', label: 'G' }] },
      ],
      providerId: 'antigravity',
      reask: '',
      promptPresets: [],
      modelPresets: [],
      modelId: 'g',
      running: false,
      // Typed `unknown` on purpose here — this suite drives the BUNDLE rather than the source — so
      // the compiler cannot supply this the way it does at every real construction site.
      waiting: [],
      failure: '',
      draft: '',
      promptId: '',
      chosenModelId: 'antigravity',
      fromSession: true,
      asked: [],
      marks: { role: '', task: '', service: [] },
      uiScale: 0,
      textTone: 0,
      canRetry: false,
      ...over,
    },
    'n0nce',
  );

  return { bundle, html };
}

/**
 * The shipped script, run against a stub DOM — the page exactly as it ships.
 *
 * <p>One stub, not one per test. The rounds log has been broken twice by a minifier renaming a
 * binding out from under it, and a second copy of this harness would drift from the first — which
 * is the very thing it exists to catch.</p>
 */
function stubClassList(): StubClassList {
  const on = new Set<string>();

  return {
    add: (name: string) => { on.add(name); },
    remove: (name: string) => { on.delete(name); },
    contains: (name: string) => on.has(name),
    toggle: (name: string, force?: boolean) => {
      const want = force === undefined ? !on.has(name) : force;
      if (want) { on.add(name); } else { on.delete(name); }

      return want;
    },
  };
}

/** What a real element's `classList` does, as much of it as the page uses. */
interface StubClassList {
  add(name: string): void;
  remove(name: string): void;
  contains(name: string): boolean;
  toggle(name: string, force?: boolean): boolean;
}

/** One stubbed element. Typed rather than cast: a cast is a promise kept by hand, and it comes due. */
interface StubNode extends Record<string, unknown> {
  readonly classList: StubClassList;
}

function runPage(over: Record<string, unknown> = {}): {
  readonly html: string;
  readonly rendered: Map<string, string>;
  readonly nodes: Record<string, StubNode>;
  readonly listeners: Record<string, Record<string, Array<() => void>>>;
  readonly onWindow: Record<string, Array<(event: { data: unknown }) => void>>;
  readonly posted: Array<Record<string, unknown>>;
  readonly press: (id: string) => void;
  readonly clickIn: (id: string, dataset: Record<string, string>) => void;
  readonly typeIn: (id: string, value: string) => void;
  readonly push: (data: unknown) => void;
  readonly frame: () => void;
} {
  const { html } = bundledChatPage(over);
  const script = html.split('<script nonce="n0nce">')[1].split('</script>')[0];
  const posted: Array<Record<string, unknown>> = [];
  const rendered = new Map(
    [...html.matchAll(/<[a-z]+[^>]*\bid="([^"]+)"[^>]*>/g)].map((match) => [match[1], match[0]]),
  );
  const listeners: Record<string, Record<string, Array<() => void>>> = {};
  const nodes: Record<string, StubNode> = {};
  const node = (id: string): StubNode => (nodes[id] ??= {
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    scrollTop: 0, clientHeight: 0, scrollHeight: 0,
    disabled: / disabled(?=[ >])/.test(rendered.get(id) ?? ''),
    addEventListener(type: string, fn: () => void) { ((listeners[id] ??= {})[type] ??= []).push(fn); },
    focus() { /* the stub is focusable */ },
    getAttribute: () => null,
    setAttribute() { /* the page sets none */ },
    querySelectorAll: () => [],
    // A real element has one, and the page opens the region it pins by adding a class to it.
    classList: stubClassList(),
  });

  const onWindow: Record<string, Array<(event: { data: unknown }) => void>> = {};
  const frames: Array<() => void> = [];
  const frame = (): void => {
    for (const fn of frames.splice(0)) {
      fn();
    }
  };

  assert.doesNotThrow(
    () => new Function('document', 'window', 'acquireVsCodeApi', 'requestAnimationFrame', script)(
      {
        getElementById: (id: string) => (rendered.has(id) ? node(id) : null),
        querySelectorAll: () => [],
        addEventListener() { /* the page listens on window */ },
        body: { style: {} },
      },
      {
        addEventListener(type: string, fn: (event: { data: unknown }) => void) {
          (onWindow[type] ??= []).push(fn);
        },
      },
      // `setState` too: the page hands VS Code its conversation id on load, so a fake without it
      // is a fake the shipped page cannot run against.
      () => ({ postMessage: (message: Record<string, unknown>) => posted.push(message), setState: () => undefined }),
      (fn: () => void) => { frames.push(fn); },
    ),
    'the minified chat page script threw on its first render',
  );
  frame();

  const press = (id: string): void => {
    for (const fire of listeners[id]?.['click'] ?? []) {
      fire();
    }
  };
  /**
   * A press inside a DELEGATED region, delivered the way a real one arrives.
   *
   * <p>The transcript's controls are written into an HTML string and listened for on the region,
   * because a push replaces the whole of it. So the handler never sees the button — it sees whatever
   * was under the pointer and walks up with `closest`. A stub that calls the listener with no event
   * exercises none of that.</p>
   */
  const clickIn = (id: string, dataset: Record<string, string>): void => {
    // `closest` HONOURS THE SELECTOR, because the selector is half of what this is testing. A stub
    // that hands the element back whatever was asked for made this test hollow: the page's selector
    // had not in fact been widened to include the new control, and the assertion passed anyway.
    // Found by breaking it on purpose and watching nothing go red.
    const closest = (selector: string): { dataset: Record<string, string> } | null =>
      // The WHOLE attribute name. `includes('data-cut')` is also true of `[data-cutoff]`, so the
      // stub would have accepted a selector that does not match the button at all — the same
      // hollowness this helper was written to end, one layer down. (CodeRabbit, PR #208.)
      Object.keys(dataset).some((key) => selector.includes(`[data-${key}]`)) ? { dataset } : null;
    for (const fire of listeners[id]?.['click'] ?? []) {
      (fire as (event: unknown) => void)({ target: { closest } });
    }
  };
  /**
   * TYPING, as the page hears it: the value set AND the input listeners fired.
   *
   * <p>Assigning `nodes.say.value` and calling it typing is how a test of the backdrop first passed
   * with the repaint removed — nothing had ever painted the layer, so the assertion that it was
   * empty was true of a thing that had never held anything.</p>
   */
  const typeIn = (id: string, value: string): void => {
    const node = nodes[id] ?? {};
    node['value'] = value;
    for (const fire of listeners[id]?.['input'] ?? []) {
      (fire as (event?: unknown) => void)();
    }
    frame();
  };
  const push = (data: unknown): void => {
    for (const fire of onWindow['message'] ?? []) {
      fire({ data });
    }
    frame();
  };

  return { html, rendered, nodes, listeners, onWindow, posted, press, clickIn, typeIn, push, frame };
}

test('the chat page the bundle produces runs, and its Send button sends exactly one turn', () => {
  // The rounds log has had this since a minifier renamed a binding out from under it — twice. The
  // chat page had only ever been READ here, and story 2 of this plan is about to embed a function
  // by `toString()` into its script, which is precisely the shape that broke there. So the page is
  // run as it SHIPS: bundled, minified, then executed against a stub DOM.
  const { rendered, nodes, onWindow, posted, press, push } = runPage();

  // The button exists in the shipped markup, its listener survived minification, and it reaches the
  // one send. A page whose button was renamed away would attach nothing and post nothing here.
  assert.ok(rendered.has('send'), 'the shipped chat page has no Send button');
  nodes['say']['value'] = 'does the shipped button work';
  press('send');

  const sends = posted.filter((message) => message['command'] === 'send');
  assert.strictEqual(sends.length, 1, 'the shipped Send button did not send exactly one turn');
  assert.strictEqual(sends[0]?.['text'], 'does the shipped button work');
  // AND IT STAYS OPEN. This asserted the opposite until issue #288: the page locked itself the
  // instant it posted, so the next question could not be typed until the answer arrived. The turns
  // are serialised by the host chain and the session refuses to interleave them, so what the page
  // owes the person is a box they can keep using.
  assert.strictEqual(nodes['say']['disabled'], false, 'the shipped page locked the composer after a send');

  // And a real push, which is what exercises the function the page carries as EMBEDDED SOURCE. A
  // free identifier in it — a default parameter, a transpiler helper, a constant it closed over —
  // does not exist in the page's scope and throws a ReferenceError only here, on the shipped
  // bundle, with a state arriving. The gate raised exactly that hazard.
  const scroll = nodes['scroll'];
  assert.ok(scroll, 'the shipped page has no scrolling region');
  scroll['clientHeight'] = 500;
  scroll['scrollHeight'] = 2000;
  scroll['scrollTop'] = 1500;
  assert.ok((onWindow['message'] ?? []).length > 0, 'the shipped page is not listening for host state');
  push({ type: 'state', messagesHtml: '<p>an answer</p>' });

  assert.strictEqual(scroll['scrollTop'], 2000,
    'the shipped page did not follow a reader who was at the bottom — the embedded rule did not run');
});

test('the shipped Asked button asks the host, and paints what comes back', () => {
  // The one thing a person does with this button is press it in a four-hour-old window, and the one
  // place it can break silently is the bundle: the region it opens, the arrows beside it and the
  // message it answers are four more names a minifier gets to rewrite.
  const { rendered, nodes, posted, press, push } = runPage();

  assert.ok(rendered.has('asked'), 'the shipped page from a session has no Asked button');
  press('asked');

  // Pressed once: the region is open, and the host has been asked to go and read the file. The page
  // cannot read one itself, so a press that posts nothing would show an empty box forever.
  const asking = nodes['asking']?.classList;
  assert.ok(asking?.contains('open'), 'the shipped page did not open the region');
  assert.strictEqual(posted.filter((message) => message['type'] === 'showAsked').length, 1,
    'the shipped Asked button did not ask the host for the session');

  push({ type: 'asked', at: 1, asked: ['make the task text green', 'and now the arrows'], refusal: '' });

  // textContent, never innerHTML: what came back is somebody's typing off a file on disk, and the
  // one safe way to put it on a page is as text. (codex, the plan round.)
  assert.strictEqual(nodes['askedText']?.['textContent'], 'make the task text green',
    'the shipped page did not paint what the host read back');
  assert.strictEqual(nodes['askedText']?.['innerHTML'], '', 'a session file was written into the page as markup');
  assert.strictEqual(nodes['askedAt']?.['textContent'], '1 / 2', 'the shipped page did not say where it was');
  assert.strictEqual(nodes['askingHead']?.['hidden'], false, 'two turns to step through and no arrows to do it');

  // The arrows step, and the count follows them — and they STOP, rather than walking off the end
  // into an empty box.
  press('askedNext');
  assert.strictEqual(nodes['askedText']?.['textContent'], 'and now the arrows', 'the arrow moved nothing');
  assert.strictEqual(nodes['askedAt']?.['textContent'], '2 / 2');
  press('askedNext');
  assert.strictEqual(nodes['askedAt']?.['textContent'], '2 / 2', 'the forward arrow walked off the end');
  assert.strictEqual(nodes['askedNext']?.['disabled'], true, 'the forward arrow still looked pressable at the end');
  press('askedBack');
  press('askedBack');
  press('askedBack');
  assert.strictEqual(nodes['askedAt']?.['textContent'], '1 / 2', 'the back arrow walked off the front');
  assert.strictEqual(nodes['askedBack']?.['disabled'], true, 'the back arrow still looked pressable at the front');

  // Pressed again it folds away, and asks for nothing while it is closed.
  press('asked');
  assert.strictEqual(asking?.contains('open'), false, 'a second press did not fold it away');
  assert.strictEqual(posted.filter((message) => message['type'] === 'showAsked').length, 1,
    'the shipped page asked again while folding away');

  // OPENED AGAIN, it reads again. The window this exists for is four hours old and still being
  // typed into; a list read once would be missing everything said since. (gemini, the plan round.)
  press('asked');
  assert.strictEqual(posted.filter((message) => message['type'] === 'showAsked').length, 2,
    'the shipped page kept a list that had four more hours of typing after it');

  // And where they were is kept when a re-read merely found MORE.
  press('askedNext');
  push({ type: 'asked', at: 2, asked: ['make the task text green', 'and now the arrows', 'and one more'], refusal: '' });
  assert.strictEqual(nodes['askedAt']?.['textContent'], '2 / 3', 'a re-read threw away the arrow they just pressed');
});

test('an answer from an earlier press cannot replace the one just asked for', () => {
  // Open, fold, open again: two reads are in flight and the slower one is the older one. Landing
  // second it would put back what the person had already moved on from. (codex and gemini, the code
  // round, on the host half and the page half of the same race.)
  const { nodes, press, push } = runPage();

  press('asked');
  press('asked');
  press('asked');
  push({ type: 'asked', at: 2, asked: ['the newer read'], refusal: '' });
  push({ type: 'asked', at: 1, asked: ['the older read, finishing late'], refusal: '' });

  assert.strictEqual(nodes['askedText']?.['textContent'], 'the newer read',
    'a read started earlier overwrote the one the person just asked for');
});

test('the shipped New chat button reaches the host', () => {
  // The page ships MINIFIED, and this repository has already shipped a page whose embedded function
  // was renamed out from under its caller. The button a person presses is the one in the bundle, so
  // that is the one pressed here.
  const { rendered, nodes, posted, press, push } = runPage();

  assert.ok(rendered.has('fresh'), 'the shipped chat page has no New chat button');
  press('fresh');

  const resets = posted.filter((message) => message['command'] === 'restart');
  assert.strictEqual(resets.length, 1, 'the shipped New chat button did not ask for exactly one reset');
  assert.strictEqual(resets[0]?.['type'], 'command');

  // AND IT SAYS IT HEARD THE PRESS. The work behind it takes seconds — a running turn ended and
  // waited for, the write queue drained, the old conversation archived — and a control that looks
  // untouched through all of that is one somebody presses again.
  assert.strictEqual(nodes['fresh']?.['disabled'], true, 'the shipped button stayed pressable through a reset it had already asked for');
  assert.strictEqual(nodes['fresh']?.['textContent'], 'Starting…', 'the shipped button said nothing about what it was doing');

  // Given back by the next state the host pushes, which the reset ends in whether it worked or not.
  push({ type: 'state', messagesHtml: '<p>a new slate</p>' });
  assert.strictEqual(nodes['fresh']?.['disabled'], false, 'one press disabled the shipped button for the life of the tab');
  assert.strictEqual(nodes['fresh']?.['textContent'], 'New chat');
});

test('the shipped page says WHY there is nothing, rather than showing an empty box', () => {
  // Four situations look identical from an empty region — no session file, no folder open, two
  // sessions sharing this tab's name, and a conversation nobody has spoken in yet. The box is the
  // only place a person is looking. (codex, on a protocol with no failure result.)
  const { nodes, press, push } = runPage();

  press('asked');
  // Before the host has said anything, the region says it is working rather than sitting blank.
  assert.strictEqual(nodes['askedText']?.['textContent'], 'Reading the session…');
  push({ type: 'asked', at: 1, asked: [], refusal: '2 sessions in this folder are called “main”.' });

  assert.strictEqual(nodes['askedText']?.['textContent'], '2 sessions in this folder are called “main”.');
  assert.strictEqual(nodes['askedAt']?.['textContent'], '', 'a count was drawn for nothing');
  assert.strictEqual(nodes['askedNext']?.['disabled'], true, 'arrows offered to step through nothing');
  // GONE, not merely disabled. On the operator's own screenshot the pair sat above the reason as two
  // empty boxes, which reads as something broken rather than as the answer it was.
  assert.strictEqual(nodes['askingHead']?.['hidden'], true, 'a dead row of arrows was drawn anyway');
});

test('the shipped Carry-nothing-above button asks the host for the position it names', () => {
  // Two more names a minifier gets to rewrite: the control is written into the transcript as an HTML
  // string, and the press is delivered through a DELEGATED listener that walks up with `closest`.
  // The rounds log has been broken exactly that way twice.
  const { posted, clickIn } = runPage();

  clickIn('messages', { cut: '4' });

  const asked = posted.filter((message) => message['type'] === 'carryFrom');
  assert.strictEqual(asked.length, 1, 'the shipped button did not ask the host where to start');
  assert.strictEqual(asked[0]?.['at'], 4, 'the shipped button named a different position than it carries');

  // And it ASKS — it does not draw. The rule comes back from the host with the transcript, so a
  // press that failed to record shows nothing rather than a line that will not survive a reload.
  assert.strictEqual(
    posted.filter((message) => message['command'] === 'carryFrom').length,
    0,
    'the button went through the command channel, where nothing decodes it',
  );
});

test('the shipped block control asks the host for the block it names', () => {
  // The third control to be reached through the ONE delegated listener on the transcript, and the
  // selector at its top is where a new one is forgotten. `clickIn` resolves `closest` against the
  // REAL selector precisely so that omission goes red here rather than shipping as a dead button.
  const { posted, clickIn } = runPage();

  clickIn('messages', { block: '2', at: '3', sig: '7-abc' });

  const asked = posted.filter((message) => message['command'] === 'copyBlock');
  assert.strictEqual(asked.length, 1, 'the shipped control did not ask the host for a block');
  assert.strictEqual(asked[0]?.['block'], 2, 'the shipped control named a different block than it carries');
  assert.strictEqual(asked[0]?.['index'], 3, 'the shipped control named a different message than it carries');
  // The signature travels as the STRING the renderer wrote. Coerced to a number it would be NaN for
  // every signature that is not all digits, and the host would refuse every press.
  assert.strictEqual(asked[0]?.['sig'], '7-abc', 'the signature did not survive the press');

  // And it carries no text: the host reads the block out of its own markdown, so a press is a
  // coordinate and a claim, never a payload.
  assert.strictEqual(asked[0]?.['text'], undefined, 'the shipped control sent text the host did not ask for');
});

test('a control the renderer drew, pressed on the shipped page, copies that block', async () => {
  // THE JOIN. Two halves were each exercised and never met: this file proved the listener forwards a
  // dataset, and `answerCopy.test.ts` proved a coordinate resolves to the right text — but nothing
  // carried a coordinate the RENDERER produced through the SHIPPED listener and on into the host.
  // A parser or a listener that dropped a field would have passed both. (CodeRabbit, on #273.)
  const { chatMessagesHtml } = await import('../chatPage');
  const { chatCommandOf } = await import('../chatMessages');
  const { blockToCopy } = await import('../answerCopy');
  const { textCopier } = await import('../copyText');

  const answer = ['Before.', '', '```ts', 'const first = 1;', '```', '', '```reply', 'Send this onward.', '```'].join('\n');
  const markup = chatMessagesHtml([{ role: 'you', text: 'ask' }, { role: 'model', text: answer }]);
  // The real attributes of the real second control, not a dataset invented for the test.
  const drawn = [...markup.matchAll(/data-block="(\d+)" data-at="(\d+)" data-sig="([^"]+)"/g)]
    .map((one) => ({ block: one[1] ?? '', at: one[2] ?? '', sig: one[3] ?? '' }));
  assert.strictEqual(drawn.length, 2, 'the fixture did not draw the two controls this test is about');

  // BOTH controls, each against its own expected text. Pressing only the second would pass against a
  // listener or parser that always answered the second block. (codex, the code round.)
  const expected = ['const first = 1;', 'Send this onward.'];

  for (let at = 0; at < drawn.length; at += 1) {
    const { posted, clickIn } = runPage();
    clickIn('messages', drawn[at] ?? {});

    const asked = posted.filter((message) => message['command'] === 'copyBlock');
    assert.strictEqual(asked.length, 1, `the shipped page did not forward the press on control ${at}`);

    const command = chatCommandOf(asked[0] as Record<string, unknown>);
    assert.strictEqual(command.kind, 'copyBlock', 'the host refused a message its own page produced');

    const wrote: string[] = [];
    // eslint-disable-next-line no-await-in-loop
    await textCopier({
      writeText: (text) => { wrote.push(text); return Promise.resolve(); },
      say: () => ({ dispose: () => undefined }),
    }).copy(() => (command.kind === 'copyBlock'
      ? blockToCopy(answer, command.block, command.sig)
      : { kind: 'refused', said: 'unreachable' }));

    assert.deepStrictEqual(wrote, [expected[at]],
      `the press that crossed the whole seam on control ${at} copied the wrong block`);
  }
});

test('the shipped Clear button empties the composer, and repaints it', () => {
  // The box draws its own text TRANSPARENT and a layer behind it does the drawing, so emptying the
  // value without repainting leaves the old words on screen over an empty box. A screenshot of a
  // full-looking empty composer is where that lesson came from.
  const { nodes, rendered, press, typeIn, frame } = runPage();

  assert.ok(rendered.has('clear'), 'the shipped page has no way to empty the composer');
  // TYPED, so the layer behind the box is actually painted with those words — there has to be
  // something there to fail to clear.
  typeIn('say', 'half a question nobody wants to finish');
  assert.notStrictEqual(nodes['backdrop']?.['innerHTML'], '', 'the layer was never painted, so this proves nothing');

  press('clear');
  // The repaint is DEFERRED to a frame, so the frame has to be run — asserting before it passes
  // against a layer nothing has written to yet, which is how this test first passed while the
  // repaint was removed.
  frame();

  assert.strictEqual(nodes['say']['value'], '', 'the button did not empty the box');
  assert.strictEqual(nodes['backdrop']?.['innerHTML'], '', 'the words stayed on the layer behind the box');
});

test('editing the instruction away tells the host which button to un-light', () => {
  // A preset button is lit because its words are the instruction IN FORCE. The page is the only
  // side that can see them leave the box - the host hears the composer only when it next asks - so
  // the page says so once per half and the host clears its own mark. Two halves, two buttons: the
  // prompt preset owns the task, and a MODEL preset owns the role it puts in front of it.
  const { nodes, posted, push, typeIn } = runPage();
  const role = 'You are a careful reviewer.';
  const task = 'Explain what this diff changes.';
  const gone = (): Array<Record<string, unknown>> => posted.filter((one) => one['type'] === 'markGone');

  push({ type: 'state', marks: { role, task, service: [] } });
  // BOTH halves in the box, in the order a preset leaves them: the marks only hold at the front.
  typeIn('say', role + '\n\n' + task + '\n\nand my own question');

  assert.deepStrictEqual(gone(), [], 'the page called an instruction gone while it was still in the box');
  // Painted, so there is something to lose. A layer nothing ever wrote to is a layer whose
  // emptiness proves nothing - the lesson the Clear test above records.
  assert.ok(
    String(nodes['backdrop']?.['innerHTML']).includes('<mark class="task">'),
    'the task half was never marked, so its removal proves nothing',
  );

  // The prompt edited away, the role left exactly where it was.
  typeIn('say', role + '\n\nand my own question');

  assert.deepStrictEqual(
    gone(), [{ type: 'markGone', which: 'task' }],
    'editing the prompt out of the box did not un-light the prompt button',
  );

  // And now the role, which is the half a model preset puts there.
  typeIn('say', 'and my own question');

  assert.deepStrictEqual(
    gone(), [{ type: 'markGone', which: 'task' }, { type: 'markGone', which: 'role' }],
    'editing the role out of the box did not un-light the model button',
  );
});

test('a preset rewriting the composer is not the person editing the instruction away', () => {
  // THE ORDER ON THE WIRE IS THE WHOLE BUG. Pressing a prompt preset sends TWO messages: the new
  // text of the box first (setDraft), and the state that describes it - the marks, the lit button -
  // second. Between them the page holds the new words and the OLD marks, which is a box whose task
  // half does not match: exactly what somebody editing it away looks like. The page reported it
  // gone, the host un-lit the button it had just lit, and the operator saw the first press after a
  // model change do nothing while the second worked.
  const { posted, push, typeIn } = runPage();
  const role = 'You are a careful reviewer.';
  const first = 'Explain what this diff changes.';
  const second = 'Rewrite this in plain words.';
  const gone = (): Array<Record<string, unknown>> => posted.filter((one) => one['type'] === 'markGone');

  push({ type: 'state', marks: { role, task: first, service: [] } });
  typeIn('say', role + '\n\n' + first);

  // The host swapping one instruction for another, message by message, as the panel really sends it.
  push({ type: 'state', setDraft: role + '\n\n' + second });

  assert.deepStrictEqual(gone(), [], 'the page called the prompt gone while the host was replacing it');

  push({ type: 'state', marks: { role, task: second, service: [] } });

  assert.deepStrictEqual(gone(), [], 'the marks arriving after the text they describe read as an edit');
});

test('emptying the composer un-lights both buttons', () => {
  // Clear is the fastest way to delete the instruction by hand, so it reads as exactly that: an
  // empty box holds neither half, and a button lit for words nobody is using lies about what the
  // next question will carry.
  const { frame, posted, press, push, typeIn } = runPage();
  const role = 'You are a careful reviewer.';
  const task = 'Explain what this diff changes.';

  push({ type: 'state', marks: { role, task, service: [] } });
  typeIn('say', role + '\n\n' + task);
  press('clear');
  // The repaint is DEFERRED to a frame, and the report rides on it.
  frame();

  assert.deepStrictEqual(
    posted.filter((one) => one['type'] === 'markGone'),
    [{ type: 'markGone', which: 'task' }, { type: 'markGone', which: 'role' }],
    'the box was emptied and the two buttons went on claiming an instruction nothing holds',
  );
});

test('the chat page carries nothing from the host into the webview', () => {
  const { bundle } = bundledChatPage();

  // The rule, as a check. A page module that reaches for `node:child_process` would be shipped into
  // a webview that has no such thing, and the failure is a blank tab with a console nobody opens.
  //
  // WHAT THIS DOES NOT CATCH, measured by breaking it on purpose: a DEAD import. esbuild tree-shakes
  // an import nothing on the rendering path uses, and the built-in disappears from the bundle with
  // it — so `export const leak = spawn` passed this test while `spawn()` inside `chatPageHtml` fails
  // both of these. That is the right boundary for a bundle check (it asserts on what SHIPS, and a
  // dead import ships nothing) but it is not the boundary the sentence above implies, so it is
  // written down rather than left to be re-discovered.
  assert.ok(!bundle.includes('require("node:'), 'the chat page bundle pulled in a node built-in');
  assert.ok(!bundle.includes('child_process'), 'the chat page bundle mentions child_process');
});

test('the chat page script the bundle produces parses and runs', () => {
  const { html } = bundledChatPage();
  const script = html.slice(html.indexOf('<script'), html.lastIndexOf('</script>'));
  const body = script.slice(script.indexOf('>') + 1);

  assert.doesNotThrow(() => new Function(body), 'the chat page script is not valid JavaScript');

  const seen: Record<string, Record<string, unknown>> = {};
  const element = (): Record<string, unknown> => ({
    innerHTML: '', textContent: '', value: '', disabled: false, hidden: false,
    addEventListener() {}, dataset: {}, style: {},
  });
  const document_ = {
    getElementById: (id: string) => (seen[id] ??= element()),
    querySelectorAll: () => [],
    body: { style: {} },
    addEventListener() {},
  };
  const window_: Record<string, unknown> = { addEventListener() {} };

  // `setState` as well as `postMessage`: the page hands VS Code its conversation id the moment it
  // loads, so a fake without that method is a fake the real page cannot run against — which is what
  // this test caught the day the id was added.
  let kept: unknown;
  assert.doesNotThrow(
    () => new Function('document', 'window', 'acquireVsCodeApi', body)(
      document_, window_, () => ({ postMessage() {}, setState(value: unknown) { kept = value; } })),
    'the chat page script threw on its first render',
  );
  assert.deepEqual(kept, { id: 'conversation-1' },
    'the page did not tell VS Code which conversation it is, so a reload could not restore it');
  // The trap must be installed, not merely written: a webview swallows a thrown error, and a page
  // that stops answering Enter with no sign of why is the defect it exists to name.
  assert.strictEqual(typeof window_['onerror'], 'function', 'the page installed no error trap');
});

/**
 * The retry flow, on the page AS IT SHIPS.
 *
 * <p>The unit tests drive the page module's own script; this drives the bundled, minified one, which
 * is what a person actually presses. It exists because that is where this repository has been bitten:
 * a function referenced only from inside a template string gets renamed by the minifier, and the page
 * dies with a name that is not defined — twice, in the rounds log. A control added to a region and
 * listened for by a delegated handler is exactly that shape.</p>
 */
test('the shipped page offers Try again on a failure, and one press sends one retry naming its state', () => {
  const page = runPage({
    failure: 'the model returned an empty answer',
    canRetry: true,
    messages: [{ role: 'you', text: 'the question that failed' }],
  });

  // THE MARKUP, not the whole document: the page's own script carries the selector `[data-retry]`
  // as a string, so a whole-document match is true whatever the state is. The file's header records
  // that this exact mistake has been made here before.
  const markup = page.html.split('<script')[0];

  assert.match(markup, /id="failure"/, 'the shipped page rendered no failure region at all');
  assert.match(markup, /data-retry="1"/, 'the shipped page offered no retry naming the state it was drawn for');
  assert.match(markup, /Try again/, 'the control does not say what pressing it does');

  // One press. The guard against a SECOND press is unit-level, where the harness can hand the
  // handler the same element twice — this stub fabricates a fresh one per click, so a disabled flag
  // written on the first would be invisible to the second and the assertion would prove nothing.
  page.clickIn('failure', { retry: '1' });

  assert.deepEqual(
    page.posted.filter((message) => message['command'] === 'retry'),
    [{ type: 'command', command: 'retry', at: 1 }],
    'the minified page sent no retry — a binding the minifier renamed is how this has failed before',
  );
});

test('the shipped page folds a long question and opens it again on a press', () => {
  const long = 'x'.repeat(401);
  const page = runPage({ messages: [{ role: 'you', text: long }] });
  const markup = page.html.split('<script')[0];

  assert.match(markup, /class="msg you long"/, 'the shipped page did not fold a long question');
  const key = /data-fold="([a-z0-9]+)"/.exec(markup);
  assert.ok(key, 'the folded question carries no key for the page to open it by');
  assert.match(markup, /Show all 401 characters/, 'the control does not say how much is hidden');

  page.clickIn('messages', { fold: key[1] });

  assert.match(
    String(page.nodes['folds']?.['textContent'] ?? ''),
    new RegExp(`\\[data-folded="${key[1]}"\\] \\.what \\{ max-height: none;`),
    'the minified page opened nothing — a binding the minifier renamed is how this has failed before',
  );
});

test('reading a folded question does not fold it — only its control does', () => {
  // The container carries the key for the STYLESHEET to match on, and the button carries the one the
  // listener matches on. They were the same attribute at first, so `closest('[data-fold]')` found the
  // message itself and any click inside the text — including the first click of selecting it to copy
  // — toggled the fold under the reader. This stub honours the selector, which is what makes the
  // difference visible. (gemini, the code round, twice.)
  const long = 'x'.repeat(401);
  const page = runPage({ messages: [{ role: 'you', text: long }] });
  const key = /data-fold="([a-z0-9]+)"/.exec(page.html.split('<script')[0]);
  assert.ok(key, 'the folded question carries no key on its control');

  page.clickIn('messages', { folded: key[1] });

  assert.strictEqual(
    String(page.nodes['folds']?.['textContent'] ?? ''),
    '',
    'clicking the body of a folded question toggled it',
  );

  page.clickIn('messages', { fold: key[1] });

  assert.match(
    String(page.nodes['folds']?.['textContent'] ?? ''),
    new RegExp(`\\[data-folded="${key[1]}"\\]`),
    'the control itself stopped opening the question',
  );
});

test('the shipped page does not offer a retry when the host says there is nothing behind it', () => {
  const markup = runPage({ failure: 'the page hit an error', canRetry: false }).html.split('<script')[0];

  assert.match(markup, /the page hit an error/, 'a failure with no retry stopped being readable');
  assert.doesNotMatch(markup, /data-retry/, 'the shipped page offered a button the host did not offer');
});

/* ------------------------------------------------------------------------------------------------
 * The THIRD page, and the reason it is here is a correction rather than a feature.
 *
 * Epic 1 of PLAN_the_review_page_can_be_read wrote, in three docblocks, that a review-page module
 * reaching for `node:` or `vscode` "fails the bundle test". It would not have: this file bundled the
 * rounds log and the chat page, and `theBundleLoads.test.mjs` loads the whole extension bundle, where
 * `node:` is available and a page module importing it proves nothing. The review page's purity was
 * held by discipline and by comments CLAIMING a guard that did not exist — which is worse than no
 * comment, because the next person reads it and stops checking.
 *
 * Found by the agent that built story 2.1, reading the code rather than the docblocks.
 * ---------------------------------------------------------------------------------------------- */

test('the review page bundles without dragging the host into it', () => {
  const bundle = bundleOf('bugzReviewPage.ts', 'reviewPageHtml');

  // `node:` in a bundle of a PAGE module means a host import survived tree-shaking — the page runs
  // in a webview, where none of it exists. Shiki and its three grammars come along; none is a host.
  assert.doesNotMatch(bundle, /require\("node:/u,
    'a review-page module imports something only the extension host has');
  assert.doesNotMatch(bundle, /require\("vscode"\)/u,
    'a review-page module imports the vscode API, which a webview does not have');

  // And the companion, without which the two assertions above pass on an empty bundle: the page is
  // really in there, and it really runs.
  const shim = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', bundle)(shim, shim.exports);
  const render = (shim.exports as { reviewPageHtml?: (view: unknown) => string }).reviewPageHtml;
  assert.equal(typeof render, 'function', 'the bundle exports no page to render');
  assert.match(render!({ pairs: [], nonce: 'n' }), /Review bugs/u);
});
