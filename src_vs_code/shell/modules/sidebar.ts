// The `sidebar` module (v2 E4 story s3-sidebar): the sidebar's FIRST paint, which does not wait for the legacy bundle.
// `../measure` FIRST: this bundle's own copy of the recorder evaluates before anything else here, and that moment is `moduleEval:sidebar`.
//
// What it does, in order, for a view VS Code asks the shell to resolve:
//   1. sets the view's options and writes the page: the sidebar's LAST SNAPSHOT (the page the old code last painted, `src/sidebarSnapshot.ts`) with a
//      "Refreshing…" line and a fresh nonce, or the skeleton when there is none (or it is damaged, from another build, or credential-like);
//   2. listens for that page's `ready`, which is the cold-start measurement's M2;
//   3. after `ready` (or after FALLBACK_MS if the page never says it), loads the legacy module in the background and hands it THIS view; the legacy
//      panel provider repaints the page with live data, as it always has. The listener is removed first, so the old code is the view's only reader.
// It imports `node:fs` and `node:path` (the snapshot's reader) and `node:crypto` (the nonce), and nothing of the old tree: the snapshot's format
// is a small file of its own. The bundle's budget and allowed inputs are in `scripts/bundle-entries.json`.
import { mark } from '../measure';
import type * as vscode from 'vscode';
import type { ShellModule, SidebarModule } from '../lazy';
import { extensionVersionOf, failedPage, readSnapshot, skeletonPage, snapshotPage } from '../../src/sidebarSnapshot';
import { webviewNonce } from '../../src/webviewNonce';

/** How long the page has to say `ready` before the old code is loaded anyway (a webview that is not shown yet may be slow to run its script). */
export const FALLBACK_MS = 1500;

/** What the module needs from the world besides the view; a test replaces the timers. */
export interface SidebarSeams {
  readonly setTimeout: (run: () => void, ms: number) => unknown;
  readonly clearTimeout: (timer: unknown) => void;
  readonly log: (message: string, error: unknown) => void;
}

const REAL: SidebarSeams = {
  setTimeout: (run, ms) => setTimeout(run, ms),
  clearTimeout: (timer) => { clearTimeout(timer as ReturnType<typeof setTimeout>); },
  log: (message, error) => { console.error(`ConnectOtherAIs: ${message}`, error); },
};

/** The page the view opens on: the last snapshot with a fresh nonce, else the skeleton. A read that fails is no snapshot. */
export function firstPageFor(context: vscode.ExtensionContext): string {
  const nonce = webviewNonce();
  const stored = readSnapshot(context.globalStorageUri.fsPath, extensionVersionOf(context));

  return stored === undefined ? skeletonPage(nonce) : snapshotPage(stored, nonce);
}

/** The first paint: the view's options, then its page, then the mark. */
function paintFirst(context: vscode.ExtensionContext, view: vscode.WebviewView): void {
  // The same options the old panel provider sets, so its own assignment later is no change to the webview.
  view.webview.options = { enableScripts: true };
  view.webview.html = firstPageFor(context);
  mark('firstPaint');
}

/** Loads the old code and gives it the view; when that fails the page says so, because "Refreshing…" for ever would be a lie. */
function loadAndHandOver(
  seams: SidebarSeams,
  context: vscode.ExtensionContext,
  view: vscode.WebviewView,
  resolveContext: vscode.WebviewViewResolveContext,
  token: vscode.CancellationToken,
  legacy: () => Promise<ShellModule>,
): void {
  legacy()
    .then((module) => module.resolveView(context, view, resolveContext, token))
    .catch((error: unknown) => {
      seams.log('the sidebar could not be handed to the main module', error);
      try {
        view.webview.html = failedPage(error instanceof Error ? error.message : String(error));
      } catch {
        // The view was disposed under the write: there is nobody to tell.
      }
    });
}

const isReady = (message: unknown): boolean => (message as { readonly type?: unknown } | null)?.type === 'ready';

export function resolveWith(
  seams: SidebarSeams,
  context: vscode.ExtensionContext,
  view: vscode.WebviewView,
  resolveContext: vscode.WebviewViewResolveContext,
  token: vscode.CancellationToken,
  legacy: () => Promise<ShellModule>,
): void {
  paintFirst(context, view);
  // `over`: the hand-over has happened or the view has gone. Either way the listener, the disposal watch and the fallback timer are released.
  const hold = { over: false, fallback: undefined as unknown };
  const subscriptions: vscode.Disposable[] = [];
  const release = (): void => {
    hold.over = true;
    seams.clearTimeout(hold.fallback);
    for (const one of subscriptions.splice(0)) {
      one.dispose();
    }
  };
  const handOver = (): void => {
    if (!hold.over) {
      release();
      loadAndHandOver(seams, context, view, resolveContext, token, legacy);
    }
  };
  subscriptions.push(
    // Only the first `ready` of this document counts, and it is the one thing this module reads: every other message is dropped, because the page
    // is inert until it is replaced (`snapshotPage`), so a person has nothing here to send.
    view.webview.onDidReceiveMessage((message: unknown) => {
      if (!hold.over && isReady(message)) {
        mark('ready');
        seams.setTimeout(handOver, 0);
      }
    }),
    // A view that goes away before the hand-over is not handed over: VS Code resolves a new one when the sidebar is shown again.
    view.onDidDispose(release),
  );
  hold.fallback = seams.setTimeout(handOver, FALLBACK_MS);
}

export const resolveSidebar: SidebarModule['resolveSidebar'] = (context, view, resolveContext, token, legacy) => {
  resolveWith(REAL, context, view, resolveContext, token, legacy);
};
