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

const NOW = Date.parse('2026-09-05T08:00:00.000Z');

/** One finished round, priced from one ledger line — enough to exercise every cell of a row. */
const SESSION = {
  state: { sessionId: 's1', repoPath: 'D:/repo', branch: 'main', stage: 'CodeReview', awaitingResolve: false },
  rounds: [{
    stage: 'CodeReview', number: 1, verdict: 'proceed', gatingCount: 1,
    reviewers: 'all 2 reviewers answered', status: 'done',
    startedUtc: '2026-09-05T07:41:00.000Z', completedUtc: '2026-09-05T07:43:10.000Z',
    subject: 'SCOPE - something', tokensIn: 1_000_000, tokensOut: 200_000,
    reviewerStates: [{ provider: 'codex', role: 'Architecture', status: 'done', findings: 1, note: '', seconds: 23 }],
  }],
};

const USED = {
  utc: '2026-09-05T07:42:00.000Z', provider: 'codex', model: 'gpt-5.6-sol', role: 'Architecture',
  stage: 'CodeReview', seconds: 23, tokensIn: 1_000_000, tokensOut: 200_000, costUsd: null, outcome: 'ok',
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
    rowsFrom: (sessions: unknown[], now: number, priceOf?: unknown, usage?: unknown[]) => unknown[];
  };
  // WITH a row, and a priced one. This helper used to render an empty page, and an empty page never
  // calls the functions that format a row — which is exactly how 0.29.12 shipped "R is not defined":
  // `cost3` is embedded by its source text and CALLED `money`, a module-level binding the minifier
  // had renamed to `R`. The page defines `money`, so nothing looked missing until a row asked for
  // its cost.
  const html = module_.roundsLogHtml(module_.rowsFrom([SESSION], NOW, PRICES, [USED]), [], 'n0nce');
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
