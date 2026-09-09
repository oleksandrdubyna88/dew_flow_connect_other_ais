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
const EMBEDDED = ['compareRows', 'rowMatches', 'money', 'cost3', 'costTitle', 'asInstant'];

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
      document_, { addEventListener() {} }, () => ({ postMessage() {} })),
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

/** The chat page as it ships: bundled, minified, then rendered. */
function bundledChatPage(): { bundle: string; html: string } {
  const bundle = bundleOf('chatPage.ts', 'chatPageHtml');
  const shim = { exports: {} as Record<string, unknown> };
  new Function('module', 'exports', bundle)(shim, shim.exports);
  const module_ = shim.exports as unknown as {
    chatPageHtml: (state: unknown, nonce: string) => string;
  };
  const html = module_.chatPageHtml(
    {
      title: 'привет',
      passage: 'The reviewers are read-only.',
      messages: [{ role: 'model', text: 'because the worktree is pinned' }],
      models: [
        { id: 'antigravity', label: 'Gemini', caption: 'local' },
        { id: 'remsoftdev-codex', label: 'GPT (team)', caption: 'remote · no memory' },
      ],
      modelId: 'antigravity',
      running: false,
      failure: '',
      draft: '',
      uiScale: 0,
    },
    'n0nce',
  );

  return { bundle, html };
}

test('the chat page the bundle produces runs, and its Send button sends exactly one turn', () => {
  // The rounds log has had this since a minifier renamed a binding out from under it — twice. The
  // chat page had only ever been READ here, and story 2 of this plan is about to embed a function
  // by `toString()` into its script, which is precisely the shape that broke there. So the page is
  // run as it SHIPS: bundled, minified, then executed against a stub DOM.
  const { html } = bundledChatPage();
  const script = html.split('<script nonce="n0nce">')[1].split('</script>')[0];
  const posted: Array<Record<string, unknown>> = [];
  const rendered = new Map(
    [...html.matchAll(/<[a-z]+[^>]*\bid="([^"]+)"[^>]*>/g)].map((match) => [match[1], match[0]]),
  );
  const listeners: Record<string, Record<string, Array<() => void>>> = {};
  const nodes: Record<string, Record<string, unknown>> = {};
  const node = (id: string) => (nodes[id] ??= {
    innerHTML: '', textContent: '', hidden: false, value: '', className: '',
    disabled: / disabled(?=[ >])/.test(rendered.get(id) ?? ''),
    addEventListener(type: string, fn: () => void) { ((listeners[id] ??= {})[type] ??= []).push(fn); },
    focus() { /* the stub is focusable */ },
    getAttribute: () => null,
    setAttribute() { /* the page sets none */ },
    querySelectorAll: () => [],
  });

  assert.doesNotThrow(
    () => new Function('document', 'window', 'acquireVsCodeApi', script)(
      {
        getElementById: (id: string) => (rendered.has(id) ? node(id) : null),
        querySelectorAll: () => [],
        addEventListener() { /* the page listens on window */ },
        body: { style: {} },
      },
      { addEventListener() { /* no host push in this test */ } },
      () => ({ postMessage: (message: Record<string, unknown>) => posted.push(message) }),
    ),
    'the minified chat page script threw on its first render',
  );

  // The button exists in the shipped markup, its listener survived minification, and it reaches the
  // one send. A page whose button was renamed away would attach nothing and post nothing here.
  assert.ok(rendered.has('send'), 'the shipped chat page has no Send button');
  nodes['say']['value'] = 'does the shipped button work';
  for (const fire of listeners['send']?.['click'] ?? []) {
    fire();
  }

  const sends = posted.filter((message) => message['command'] === 'send');
  assert.strictEqual(sends.length, 1, 'the shipped Send button did not send exactly one turn');
  assert.strictEqual(sends[0]?.['text'], 'does the shipped button work');
  assert.strictEqual(nodes['say']['disabled'], true, 'the shipped page left the composer open after a send');
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

  assert.doesNotThrow(
    () => new Function('document', 'window', 'acquireVsCodeApi', body)(
      document_, window_, () => ({ postMessage() {} })),
    'the chat page script threw on its first render',
  );
  // The trap must be installed, not merely written: a webview swallows a thrown error, and a page
  // that stops answering Enter with no sign of why is the defect it exists to name.
  assert.strictEqual(typeof window_['onerror'], 'function', 'the page installed no error trap');
});
