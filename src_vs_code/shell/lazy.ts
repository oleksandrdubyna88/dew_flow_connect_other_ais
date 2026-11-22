// The loader: one `require` per module id, memoised, with a timing mark. It reads no file itself —
// `require` does, when a command or a view first needs the module — and this file imports only `node:path`.
// Allowed inputs of the entry bundle: shell/entry.ts, lazy.ts, ids.ts, measure.ts (scripts/entry-firewall.mjs).
import { join } from 'node:path';
import type * as vscode from 'vscode';
import { mark } from './measure';

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

/** How long each module took to load: the measurement story (S2) reads this list. */
export interface LoadMark {
  readonly id: string;
  /** `performance.now()` when the load began. */
  readonly startedAt: number;
  /** Milliseconds `require` took. */
  readonly ms: number;
}

export const loadMarks: LoadMark[] = [];

const loading = new Map<string, Promise<ShellModule>>();
const loaded = new Map<string, ShellModule>();

/** The module, loaded once; every later call answers the same promise. A load that failed is forgotten, so the next call tries again. */
export function lazy(id: string): Promise<ShellModule> {
  let pending = loading.get(id);
  if (pending === undefined) {
    pending = new Promise<ShellModule>((resolve) => {
      const startedAt = performance.now();
      mark('moduleRequire');
      const module = require(join(__dirname, 'modules', `${id}.js`)) as ShellModule;
      mark('moduleLoaded');
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
  return loaded.get(id);
}
