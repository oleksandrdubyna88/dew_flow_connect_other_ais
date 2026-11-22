// The loader: one `require` per module id, memoised, with a timing mark. It reads no file itself —
// `require` does, when a command or a view first needs the module — and this file imports only `node:path`.
// Allowed inputs of the entry bundle: shell/entry.ts, lazy.ts, ids.ts, measure.ts (scripts/entry-firewall.mjs).
import { join } from 'node:path';
import type * as vscode from 'vscode';
import { LOADING, mark } from './measure';
import { MODULE_LEGACY } from './ids';

/** What every module under `dist/modules/<id>.js` exports; the shell calls these and nothing else. */
export interface ShellModule {
  /** Runs a command the shell registered; the answer goes back to `executeCommand`. */
  readonly command: (context: vscode.ExtensionContext, id: string, args: readonly unknown[]) => unknown;
  /** Resolves a view the shell registered a provider for. */
  readonly resolveView: (context: vscode.ExtensionContext, view: vscode.WebviewView, resolveContext: vscode.WebviewViewResolveContext, token: vscode.CancellationToken) => void | Thenable<void>;
  /** Restores a panel after a reload. */
  readonly restorePanel: (context: vscode.ExtensionContext, panel: vscode.WebviewPanel, state: unknown) => void | Thenable<void>;
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

/** The module, loaded once; every later call answers the same promise. A load that failed is forgotten, so the next call tries again. */
export function lazy<M = ShellModule>(id: string): Promise<M> {
  let pending = loading.get(id) as Promise<M> | undefined;
  if (pending === undefined) {
    pending = new Promise<M>((resolve) => {
      const startedAt = performance.now();
      mark(label('moduleRequire', id));
      // The module's own copy of measure.ts reads this when it evaluates, to name its `moduleEval` mark.
      const holder = globalThis as unknown as Record<symbol, string | undefined>;
      holder[LOADING] = id;
      let module: M;
      try {
        module = require(join(__dirname, 'modules', `${id}.js`)) as M;
      } finally {
        holder[LOADING] = undefined;
      }
      mark(label('moduleLoaded', id));
      loadMarks.push({ id, startedAt, ms: performance.now() - startedAt });
      loaded.set(id, module);
      resolve(module);
    });
    pending.catch(() => { loading.delete(id); });
    loading.set(id, pending);
  }

  return pending;
}

/** The module when it has been loaded, else `undefined`: `deactivate` must not load what was never used. */
export function loadedModule(id: string): ShellModule | undefined {
  return loaded.get(id) as ShellModule | undefined;
}
