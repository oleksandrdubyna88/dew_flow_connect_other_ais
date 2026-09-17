import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import Module from 'node:module';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { test } from 'node:test';

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
