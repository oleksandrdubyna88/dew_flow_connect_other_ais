// The `sidebar` module (v2 E4 story s3-sidebar): the sidebar's REAL MENU, which does not wait for the legacy bundle.
// `../measure` FIRST: this bundle's own copy of the recorder evaluates before anything else here, and that moment is `moduleEval:sidebar`.
//
// What it does, in order, for a view VS Code asks the shell to resolve:
//   1. sets the view's options and writes the menu (`src/sidebarMenu.ts`): the section headers with the old panel's words, order and colours, every one a
//      `details` element that opens at once, each with a "loading…" line inside until live content exists. The page says `menuReady` when its headers are
//      on screen and usable; that message is the end of the cold-start measurement (owner, 2026-11-23: the start is over when the REAL menu is shown and
//      clickable, not when a skeleton or an inert copy is);
//   2. after `menuReady` (or after a person opens a section, or after FALLBACK_MS if the page never says it) loads the legacy module in the background and hands
//      it THIS view with the sections the person opened or closed; the legacy panel provider repaints the page with live data, as it always has.
//      The listener stays until the old code has the view, so no toggle in between is lost, and is removed just before the hand-over: the old code is then
//      the view's only reader;
//   3. after `menuReady` also starts the chat module in the background, so its context keys (`coai.terminalReadsSession`, the skip-list check) exist within
//      seconds. It runs on a timer after the message handler returned: it never delays `menuReady`;
//   4. after `menuReady` also starts the install work (the binary check and `coai-mcp --setup-clients`, story s5-install-module), the same way and for the same
//      reason: it is never inside the cold start's window. Only the first `menuReady` (or the fallback, when the page never says it) asks, and only once.
// It imports `node:crypto` (the nonce) and the small files of the menu, and nothing of the old tree. The bundle's budget and allowed inputs are in
// `scripts/bundle-entries.json`.
import { mark } from '../measure';
import type * as vscode from 'vscode';
import type { ShellModule, SidebarModule } from '../lazy';
import { failedPage, MENU_IDS, menuPage } from '../../src/sidebarMenu';
import { webviewNonce } from '../../src/webviewNonce';

/**
 * How long the page has to say `menuReady` before the old code is loaded anyway (a webview that is not shown yet may be slow to run its script). Long on
 * purpose: the old code loads in one block of the host's thread, and a load that began while the page's message was on its way would be counted as the menu's time.
 */
export const FALLBACK_MS = 5000;

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

/** The first paint: the view's options, then the menu, then the mark. */
function paintFirst(view: vscode.WebviewView): void {
  // The same options the old panel provider sets, so its own assignment later is no change to the webview.
  view.webview.options = { enableScripts: true };
  view.webview.html = menuPage(webviewNonce());
  mark('firstPaint');
}

interface Said {
  readonly type?: unknown;
  readonly id?: unknown;
  readonly open?: unknown;
}

const saidBy = (message: unknown): Said => (typeof message === 'object' && message !== null ? message as Said : {});
const typeOf = (said: Said): string => (typeof said.type === 'string' ? said.type : '');

/** The id of a section of the menu a page named, or `undefined` for anything else. */
const menuIdOf = (said: Said): string | undefined => (typeof said.id === 'string' && MENU_IDS.includes(said.id) ? said.id : undefined);

/** What the module is given for one view: the old code's and chat's loaders, and the things a hand-over passes on. */
interface World {
  readonly context: vscode.ExtensionContext;
  readonly view: vscode.WebviewView;
  readonly resolveContext: vscode.WebviewViewResolveContext;
  readonly token: vscode.CancellationToken;
  readonly legacy: () => Promise<ShellModule>;
  readonly startChat: () => Promise<unknown>;
  readonly startInstall: () => Promise<unknown>;
}

/** One resolved view's life: the menu, what the person does on it, and the hand-over of the view to the old code. */
class MenuSession {
  /** The hand-over has happened or the view has gone: the listener, the disposal watch and the fallback timer are released. */
  private over = false;
  /** The old code has been asked for (it may still be loading). */
  private asked = false;
  private chatAsked = false;
  private installAsked = false;
  private fallback: unknown;
  /** What the person did on the menu: a section is in one of these two sets, the one of its last press. */
  private readonly opened = new Set<string>();
  private readonly closed = new Set<string>();
  private readonly subscriptions: vscode.Disposable[] = [];
  private readonly heard: Readonly<Record<string, (said: Said) => void>> = {
    ready: () => { mark('ready'); },
    // The mark FIRST, in the handler; everything that costs anything is on a timer after it.
    menuReady: () => { mark('menuReady'); this.seams.setTimeout(this.handOver, 0); this.seams.setTimeout(this.askChat, 0); this.seams.setTimeout(this.askInstall, 0); },
    section: (said) => { this.toggled(said); },
  };

  constructor(private readonly seams: SidebarSeams, private readonly world: World) {}

  start(): void {
    this.subscriptions.push(
      this.world.view.webview.onDidReceiveMessage((message: unknown) => { this.hear(message); }),
      // A view that goes away before the hand-over is not handed over: VS Code resolves a new one when the sidebar is shown again.
      this.world.view.onDidDispose(() => { this.release(); }),
    );
    this.fallback = this.seams.setTimeout(() => { this.handOver(); this.askChat(); this.askInstall(); }, FALLBACK_MS);
  }

  private hear(message: unknown): void {
    if (this.over) {
      return;
    }
    const said = saidBy(message);
    const run = Object.hasOwn(this.heard, typeOf(said)) ? this.heard[typeOf(said)] : undefined;
    run?.(said);
  }

  private release(): void {
    this.over = true;
    this.seams.clearTimeout(this.fallback);
    for (const one of this.subscriptions.splice(0)) {
      one.dispose();
    }
  }

  private fail(error: unknown): void {
    this.seams.log('the sidebar could not be handed to the main module', error);
    this.release();
    try {
      this.world.view.webview.html = failedPage(error instanceof Error ? error.message : String(error));
    } catch {
      // The view was disposed under the write: there is nobody to tell.
    }
  }

  /** Loads the old code and gives it this view, with what the person did on the menu. The listener stays until that moment, so no press in between is lost. */
  private readonly handOver = (): void => {
    if (this.asked || this.over) {
      return;
    }
    this.asked = true;
    this.seams.clearTimeout(this.fallback);
    this.world.legacy()
      .then((module) => this.give(module))
      .catch((error: unknown) => { this.fail(error); });
  };

  private give(module: ShellModule): void | Thenable<void> {
    if (this.over) {
      // The view went away while the old code loaded: VS Code resolves a new one when the sidebar is shown again.
      return undefined;
    }
    const handed = { opened: [...this.opened], closed: [...this.closed] };
    this.release();

    return module.resolveView(this.world.context, this.world.view, this.world.resolveContext, this.world.token, handed);
  }

  private readonly askChat = (): void => {
    if (!this.chatAsked) {
      this.chatAsked = true;
      this.world.startChat().catch((error: unknown) => { this.seams.log('chat could not be started in the background', error); });
    }
  };

  private readonly askInstall = (): void => {
    if (!this.installAsked) {
      this.installAsked = true;
      this.world.startInstall().catch((error: unknown) => { this.seams.log('the install work could not be started in the background', error); });
    }
  };

  private toggled(said: Said): void {
    const id = menuIdOf(said);
    if (id === undefined) {
      return;
    }
    const open = said.open === true;
    this.record(id, open);
    if (open) {
      // A section that is open has nothing to show but "loading…": the live page is asked for NOW, not on the timer.
      this.seams.setTimeout(this.handOver, 0);
    }
  }

  private record(id: string, open: boolean): void {
    const [now, before] = open ? [this.opened, this.closed] : [this.closed, this.opened];
    before.delete(id);
    now.add(id);
  }
}

export function resolveWith(
  seams: SidebarSeams,
  context: vscode.ExtensionContext,
  view: vscode.WebviewView,
  resolveContext: vscode.WebviewViewResolveContext,
  token: vscode.CancellationToken,
  legacy: () => Promise<ShellModule>,
  startChat: () => Promise<unknown>,
  startInstall: () => Promise<unknown>,
): void {
  paintFirst(view);
  new MenuSession(seams, { context, view, resolveContext, token, legacy, startChat, startInstall }).start();
}

export const resolveSidebar: SidebarModule['resolveSidebar'] = (context, view, resolveContext, token, legacy, startChat, startInstall) => {
  resolveWith(REAL, context, view, resolveContext, token, legacy, startChat, startInstall);
};
