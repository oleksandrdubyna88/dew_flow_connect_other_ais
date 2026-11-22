// The loader: one `require` per module id, memoised, with a timing mark. It reads no file itself —
// `require` does, when a command or a view first needs the module — and this file imports only `node:path`.
// Allowed inputs of the entry bundle: shell/entry.ts, lazy.ts, ids.ts, measure.ts (scripts/entry-firewall.mjs).
import { join } from 'node:path';
import type * as vscode from 'vscode';
import { LOADING, mark } from './measure';
import { MODULE_LEGACY, MODULE_RUNTIME, SHELL_KEY_NAME } from './ids';

/** What every module under `dist/modules/<id>.js` exports; the shell calls these and nothing else. */
export interface ShellModule {
  /** Runs a command the shell registered; the answer goes back to `executeCommand`. */
  readonly command: (context: vscode.ExtensionContext, id: string, args: readonly unknown[]) => unknown;
  /** Resolves a view the shell registered a provider for. */
  readonly resolveView: (context: vscode.ExtensionContext, view: vscode.WebviewView, resolveContext: vscode.WebviewViewResolveContext, token: vscode.CancellationToken) => void | Thenable<void>;
  /** Answers a `vscode://` link addressed to this extension. */
  readonly handleUri: (context: vscode.ExtensionContext, uri: vscode.Uri) => void | Thenable<void>;
  /** What `activate` hands to a test host: the module's own start-up, run once. */
  readonly start: (context: vscode.ExtensionContext) => unknown;
  /** What `deactivate` forwards, when the module was loaded. */
  readonly deactivate: () => Thenable<void> | undefined;
}

/**
 * What the sidebar module exports (v2 E4): it paints the sidebar's last snapshot at once, and hands the SAME view to the legacy module
 * (`legacy().resolveView`) after the page has said it is ready. `legacy` is the entry's own loader, so there is one copy of the old code and one registration.
 */
export interface SidebarModule {
  readonly resolveSidebar: (
    context: vscode.ExtensionContext,
    view: vscode.WebviewView,
    resolveContext: vscode.WebviewViewResolveContext,
    token: vscode.CancellationToken,
    legacy: () => Promise<ShellModule>,
  ) => void;
}

/**
 * What the chat module exports (v2 E4): the part of `ShellModule` that chat answers, and the one call other modules make into it.
 * The shell registers the chat commands and the `coaiChat` serializer once; their thunks load THIS module, not the legacy one.
 */
export interface ChatModule extends Pick<ShellModule, 'command' | 'start' | 'deactivate'> {
  /** Restores a `coaiChat` tab after a reload, from the chat store, without the sidebar and without the legacy module. */
  readonly restorePanel: (context: vscode.ExtensionContext, panel: vscode.WebviewPanel, state: unknown) => void | Thenable<void>;
  /** The review page's `Chat` on a bug (the legacy sidebar's callback): puts the bug into a chat through the one chat registry. */
  readonly chooseFromBug: (context: vscode.ExtensionContext, chat: unknown) => Promise<void>;
}

/** What a `runtime` module exports: the one `HostRuntime` of the host (`src/runtimeState.ts`); this file never names its type. */
export interface RuntimeModule {
  readonly runtime: unknown;
}

/** What the loader publishes on `globalThis` under `Symbol.for(SHELL_KEY_NAME)`; `src/hostRuntime.ts` is the reader. */
export interface ShellHandle {
  /** The host's one runtime, from the memoised `runtime` module: the same object for every caller in every bundle. */
  readonly runtime: () => unknown;
  /** Loads a module by id through the loader, so a bundle that needs another module (the legacy sidebar's bug chat → chat) never requires it itself. */
  readonly module: <M>(id: string) => Promise<M>;
}

/** How long each module took to load: the measurement story (S2) reads this list. */
export interface LoadMark {
  readonly id: string;
  /** `performance.now()` when the load began. */
  readonly startedAt: number;
  /** Milliseconds `require` took. */
  readonly ms: number;
}

export const loadMarks: LoadMark[] = [];

const loading = new Map<string, Promise<unknown>>();
const loaded = new Map<string, unknown>();

/** The legacy module's marks keep their names (the measurement's table reads them); another module's carry its id, so two loads are two rows. */
const label = (name: string, id: string): string => (id === MODULE_LEGACY ? name : `${name}:${id}`);

/** Puts the loader's handle on `globalThis` once; bundles reach shared state through it (`src/hostRuntime.ts`). Nothing runs until a module is first loaded. */
function publish(): void {
  const holder = globalThis as unknown as Record<symbol, ShellHandle | undefined>;
  const key = Symbol.for(SHELL_KEY_NAME);
  if (holder[key] === undefined) {
    holder[key] = { runtime: () => loadNow<RuntimeModule>(MODULE_RUNTIME).runtime, module: lazy };
  }
}

/** The module, required now and memoised: a later call answers the same object. A load that threw is not remembered, so the next call tries again. */
function loadNow<M>(id: string): M {
  if (loaded.has(id)) {
    return loaded.get(id) as M;
  }
  publish();
  const startedAt = performance.now();
  mark(label('moduleRequire', id));
  // The module's own copy of measure.ts reads this when it evaluates, to name its `moduleEval` mark.
  const holder = globalThis as unknown as Record<symbol, string | undefined>;
  const outer = holder[LOADING];
  holder[LOADING] = id;
  let module: M;
  try {
    module = require(join(__dirname, 'modules', `${id}.js`)) as M;
  } finally {
    holder[LOADING] = outer;
  }
  mark(label('moduleLoaded', id));
  loadMarks.push({ id, startedAt, ms: performance.now() - startedAt });
  loaded.set(id, module);

  return module;
}

/** The module, loaded once; every later call answers the same promise. A load that failed is forgotten, so the next call tries again. */
export function lazy<M = ShellModule>(id: string): Promise<M> {
  let pending = loading.get(id) as Promise<M> | undefined;
  if (pending === undefined) {
    pending = new Promise<M>((resolve) => { resolve(loadNow<M>(id)); });
    pending.catch(() => { loading.delete(id); });
    loading.set(id, pending);
  }

  return pending;
}

/** The module when it has been loaded, else `undefined`: `deactivate` must not load what was never used. */
export function loadedModule<M = ShellModule>(id: string): M | undefined {
  return loaded.get(id) as M | undefined;
}
