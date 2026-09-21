import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import Module from 'node:module';
import { createRequire } from 'node:module';
import { dirname, join, relative } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { batches as runnerBatches } from '../../scripts/run-tests.mjs';

/**
 * The shipped bundle can be LOADED, and `activate` is there to be called.
 *
 * <p><b>Why this test exists, precisely.</b> Three reviewers called it Blocking at the plan round of
 * the chat command split, and they were right: nothing else in this repository executes a line of
 * this extension. The unit tests exercise the modules that need no editor; the wiring tests read
 * source text; the bundle step proves esbuild can RESOLVE the graph. None of them runs it.</p>
 *
 * <p>What that leaves invisible is the failure a fifteen-module split is most likely to cause. Two
 * modules that import each other bundle without complaint and throw at load with
 * <code>Cannot access 'X' before initialization</code>, because the one still initialising reached
 * for a binding the other had not reached yet. `importCycles.test.mjs` looks for that in the graph;
 * this looks for it in the thing that actually runs.</p>
 *
 * <p>And it is not hypothetical that module-level code runs here: loading this bundle against an
 * EMPTY `vscode` throws on `ThemeIcon`, because work happens at import time. That is the window
 * this test stands in.</p>
 *
 * <p><b>What it is NOT.</b> It does not call `activate`, and it does not pretend to be an extension
 * host. Calling it would need a `context`, a workspace, a filesystem and a webview, and a stub deep
 * enough to survive that would be a fiction whose agreement with VS Code nobody checks. This asks
 * the one question a stub can answer honestly: does the module graph come up, and is the entry point
 * a function. The honest gap — that no behaviour is exercised — is
 * `research/module_tests.md`'s largest, and this does not close it.</p>
 */

const HERE = dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/u, '$1'));
const ROOT = join(HERE, '..', '..');

/**
 * Enough of the `vscode` API for module-level code to run, and no more.
 *
 * <p>Every member here is one the bundle reaches for while loading. Anything else is left off on
 * purpose: a stub that answers everything would let a module quietly start depending on an API this
 * test claims to have checked.</p>
 */
function fakeVscode() {
  const nothing = () => undefined;
  const disposable = { dispose: nothing };

  class ThemeIcon {
    constructor(id) {
      this.id = id;
    }
  }

  class EventEmitter {
    constructor() {
      this.event = () => disposable;
    }

    fire() {
      return undefined;
    }

    dispose() {
      return undefined;
    }
  }

  return {
    ThemeIcon,
    EventEmitter,
    Uri: {
      file: (one) => ({ fsPath: one, path: one, scheme: 'file', toString: () => `file://${one}` }),
      parse: (one) => ({ fsPath: one, path: one, scheme: 'file', toString: () => one }),
      joinPath: (base, ...rest) => ({ fsPath: [base?.fsPath, ...rest].join('/'), toString: () => rest.join('/') }),
    },
    Disposable: class { dispose() { return undefined; } },
    ThemeColor: class { constructor(id) { this.id = id; } },
    Position: class { constructor(line, character) { this.line = line; this.character = character; } },
    Range: class { constructor(a, b) { this.start = a; this.end = b; } },
    MarkdownString: class { constructor(value) { this.value = value; } },
    TreeItem: class { constructor(label) { this.label = label; } },
    CancellationTokenSource: class { constructor() { this.token = { isCancellationRequested: false }; } },
    ViewColumn: { One: 1, Two: 2, Active: -1, Beside: -2 },
    TreeItemCollapsibleState: { None: 0, Collapsed: 1, Expanded: 2 },
    StatusBarAlignment: { Left: 1, Right: 2 },
    ProgressLocation: { Notification: 15, Window: 10, SourceControl: 1 },
    ConfigurationTarget: { Global: 1, Workspace: 2, WorkspaceFolder: 3 },
    QuickPickItemKind: { Separator: -1, Default: 0 },
    ExtensionMode: { Production: 1, Development: 2, Test: 3 },
    UIKind: { Desktop: 1, Web: 2 },
    env: { clipboard: { readText: async () => '', writeText: async () => undefined }, uiKind: 1, openExternal: nothing },
    commands: { registerCommand: () => disposable, executeCommand: async () => undefined, getCommands: async () => [] },
    window: new Proxy({}, {
      get: (_, key) => {
        if (key === 'activeTextEditor') {
          return undefined;
        }
        if (key === 'tabGroups') {
          return { all: [], activeTabGroup: { activeTab: undefined }, onDidChangeTabs: () => disposable };
        }
        if (key === 'onDidChangeActiveTextEditor' || key === 'onDidChangeWindowState') {
          return () => disposable;
        }

        return () => disposable;
      },
    }),
    workspace: new Proxy({}, {
      get: (_, key) => {
        if (key === 'workspaceFolders') {
          return undefined;
        }
        if (key === 'getConfiguration') {
          return () => ({ get: () => undefined, update: async () => undefined, has: () => false, inspect: () => undefined });
        }
        if (key === 'fs') {
          return { readFile: async () => new Uint8Array(), writeFile: async () => undefined, stat: async () => ({}) };
        }

        return () => disposable;
      },
    }),
    languages: new Proxy({}, { get: () => () => disposable }),
    extensions: { getExtension: () => undefined, all: [] },
  };
}

test('the shipped bundle loads with a stubbed editor, and exports activate', () => {
  const bundle = join(ROOT, 'dist', 'extension.js');
  // BUILT EVERY TIME, not only when missing. The first version built only when `dist/` was absent
  // and then loaded whatever was there — so after any run that left a stale bundle behind it was
  // testing yesterday's code, which it proved immediately by reporting a failure that had already
  // been fixed. A guard reading a stale artefact is the shape of defect this whole series exists to
  // stop, and it is not allowed in the guard itself. esbuild reads the TypeScript directly, so this
  // needs no compile and costs about a second.
  execFileSync('npm', ['run', 'bundle'], { cwd: ROOT, stdio: 'ignore', shell: process.platform === 'win32' });
  assert.ok(existsSync(bundle), 'the bundle step reported success and produced no file');

  const stub = fakeVscode();
  const load = Module._load;
  Module._load = function loadWithFakeEditor(request, ...rest) {
    return request === 'vscode' ? stub : load.call(this, request, ...rest);
  };
  try {
    const required = createRequire(import.meta.url)(bundle);

    assert.equal(typeof required.activate, 'function',
      'the bundle loaded but exports no activate — VS Code would have nothing to call');
    assert.equal(typeof required.deactivate, 'function',
      'the bundle exports no deactivate, so nothing runs when the host shuts the extension down');
  } finally {
    Module._load = load;
  }
});

test('every credential word reaches the SHIPPED bundle, so redaction is not generated away', () => {
  // WHY IT LIVES IN THIS FILE. A test of its own would run `npm run bundle` a second time, and the
  // comment below this one is about exactly that hazard: `prepare-gate.mjs` deletes a generated
  // file for about two seconds mid-build, `node --test` runs files in parallel, and a second
  // builder makes the first one's tree vanish under it. This file already builds the bundle and
  // already runs alone, so the assertion joins it rather than racing it.
  //
  // WHAT IT PROVES, and the limit is worth stating. `dist/extension.js` is minified and exports
  // only `activate`/`deactivate`, so nothing here can call the redactor. It proves that the words
  // SURVIVED bundling — that esbuild did not tree-shake `credentialWords.generated.ts` away and
  // that the module really is reachable from the entry point. It does NOT prove the redactor uses
  // them; `generate-credential-words.mjs --check` is the guard for the copy being current, and the
  // C# side has its own.
  const bundle = readFileSync(join(ROOT, 'dist', 'extension.js'), 'utf8');
  const seed = JSON.parse(readFileSync(join(ROOT, '..', 'shared', 'credential-words.json'), 'utf8'));
  const words = [...seed.anywhere, ...seed.wholePart];

  assert.ok(words.length > 0, 'the seed carries no words, which the generator should have refused');
  for (const word of words) {
    // As a QUOTED literal: a bare substring search would find `key` inside `keys`, `monkey` or any
    // minified identifier, and would pass on a bundle that carries none of this list.
    assert.ok(
      bundle.includes(`"${word}"`) || bundle.includes(`'${word}'`),
      `the shipped bundle carries no "${word}" — the extension would stop treating it as a secret `
      + 'while the server still does, which is a secret in a file on one path and not the other',
    );
  }
});

/**
 * A test that runs the BUILD runs ALONE, and this is the check for it.
 *
 * <p><b>Measured, because it was found the expensive way.</b> The test above shells out to
 * `npm run bundle`, whose `prebundle` hook is `scripts/prepare-gate.mjs` — and that script
 * deliberately INVALIDATES its previous output before it verifies the pinned conventions:
 * `removeOutput(output)`, then `rules.mjs check` under a 30-second timeout, then the write. So
 * `src/generated/gateRule.ts` does not exist for the whole of that verification. Measured on this
 * machine while one bundle ran: <b>absent for 2 047 ms</b>, and a CI runner is slower.</p>
 *
 * <p>`node --test` runs the files it is given in PARALLEL processes. Put this file in a batch
 * beside one that walks the source tree — `notificationSites.test.mjs` reads every `.ts` under
 * `src/` — and the walker lists a file this one is in the middle of replacing, then opens it:
 * `ENOENT ... src/generated/gateRule.ts`. It fails at random, which is worse than failing, and it
 * did exactly that on PR #356 while passing the identical suite one step earlier.</p>
 *
 * <p>The rule is therefore not “retry” and not “exclude generated files from the walk” — both leave
 * a build mutating the tree other tests are reading. It is that a file which runs the build gets a
 * `node --test` invocation of its own. This asserts the RUNNER still arranges that, because the
 * failure it prevents is invisible until somebody's unrelated pull request goes red.</p>
 *
 * <p>It used to read the batches out of `package.json`, which named every `.mjs` test by hand. That
 * list is gone — it was letting a seventh test run nowhere at all — and the batches now come from
 * `run-tests.mjs`, which reads the directories: one parallel batch of compiled tests, then each
 * source test alone. The invariant is the same one; only its home moved. The check got WIDER in the
 * move: the compiled files were never examined here before, because the script never named
 * them.</p>
 */
test('a test that runs the build gets a node --test invocation to itself', () => {
  // ASKED OF THE RUNNER, not rebuilt from `discover()`. The first version of this port did rebuild
  // it — and stayed green while the runner was deliberately changed to put every source test in one
  // parallel batch, because it was checking its own arithmetic. `batches()` is what will actually
  // be invoked, so breaking the arrangement breaks this case.
  const batches = runnerBatches(ROOT)
    .map((batch) => batch.map((file) => relative(ROOT, file).replaceAll('\\', '/')));

  assert.ok(batches.length > 0, 'the runner no longer discovers any test file at all');

  // THIS file has to be in the script, or everything below is vacuous: take it out and no batch has
  // a builder, every batch is skipped by the `continue`, and the case passes having asserted
  // nothing — a test that survives its own break. Derived from `import.meta.url` rather than typed,
  // so a rename goes red here instead of quietly emptying the check. (CodeRabbit, PR #356, and it
  // was right: the first version asked only whether SOME batch existed.)
  const self = relative(ROOT, fileURLToPath(import.meta.url)).replaceAll('\\', '/');

  assert.ok(batches.some((batch) => batch.includes(self)),
    `${self} is not in the test script's node --test batches at all, so the isolation this case `
    + 'checks is not in force — and every assertion below it would pass by skipping.');

  for (const batch of batches) {
    // A file that spawns npm against the real root drives the build, and the build is what
    // rewrites the tree. Read the files rather than naming them, so a SECOND such test is caught
    // the day it is written rather than the day it flakes.
    // Every shape node offers for starting a process, and both quote styles — `'npm'` for the argv
    // form and `'npm ` for the one-string one. Narrow enough that a comment mentioning npm does not
    // force a pointless split, wide enough that the next author need not guess which single spelling
    // this test happens to know.
    const builders = batch.filter((file) =>
      /(?:execFileSync|execSync|execFile|exec|spawnSync|spawn)\(\s*['"]npm['" ]/u
        .test(readFileSync(join(ROOT, file), 'utf8')));
    if (builders.length === 0) {
      continue;
    }
    assert.deepEqual(batch, builders,
      `${builders.join(', ')} runs the build, which leaves src/generated/gateRule.ts absent for `
      + `seconds. It shares a parallel node --test batch with ${batch.filter((one) => !builders.includes(one)).join(', ')}, `
      + 'so any of those reading the source tree can open a file this one is replacing. Give it its own invocation.');
    assert.equal(builders.length, 1,
      'two tests that both run the build are in one batch, so they invalidate the tree under each other');
  }
});
