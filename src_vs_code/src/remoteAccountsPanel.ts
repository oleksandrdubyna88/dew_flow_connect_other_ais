import * as vscode from 'vscode';
import { accountsHtml, ACCOUNTS_TITLE } from './remoteAccountsPage';
import { accountsCommandOf } from './remoteAccountsMessages';
import type { AccountsCommand } from './remoteAccountsMessages';
import { changedBlocks } from './remoteAccountsTable';
import type { TableModel } from './remoteAccountsTable';
import { appliedTextControl, pushTextControlsTo } from './textControlsHost';
import { currentTextTone } from './textToneHost';
import { currentUiScale } from './uiScaleHost';
import { webviewNonce } from './webviewNonce';

/**
 * The accounts tab (E2.S3): ONE editor tab for each window, on the pattern of `RoundsLogPanel` — reveal when it is open, a find bar, kept while it is
 * hidden, no local roots, released when it is closed. **No serializer**: after a reload the tab is not restored and the icon opens it again, because
 * a restored tab would draw a model this window does not have yet.
 *
 * <p>The panel only PUSHES. The page says `ready` and is answered with the whole model; after each place that landed the host calls `landed`, and
 * the panel posts the blocks that changed (that place's, and the blocks of other places that share its account, whose figures moved with it). What
 * the page asks for comes back as a closed command (`remoteAccountsMessages.ts`, checked against the model this panel holds) and is handed to the
 * host's own doors: this class decides nothing about what is read.</p>
 */

const same = (one: unknown, other: unknown): boolean => JSON.stringify(one) === JSON.stringify(other);

/** The permission or the empty sentence changed: the page draws everything again. */
const wholeAgain = (before: TableModel, next: TableModel): boolean => before.empty !== next.empty || !same(before.permission, next.permission);

export interface AccountsPanelHooks {
  /** A checked command of the page: `ready` is answered here and never comes. */
  readonly run: (command: AccountsCommand) => void;
}

export class AccountsPanel {
  private panel: vscode.WebviewPanel | undefined;
  private model: TableModel | undefined;
  /** The page said `ready`: a post is received only after it. */
  private listening = false;
  private inFlight: Promise<void> = Promise.resolve();

  constructor(private readonly hooks: AccountsPanelHooks) {}

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** The model the tab holds, for the host's own checks and its test door. */
  get current(): TableModel | undefined {
    return this.model;
  }

  /** Opens the tab, or reveals the one that is open. The whole model goes to the page when it says `ready`. */
  show(model: TableModel): void {
    this.model = model;
    if (this.panel !== undefined) {
      this.panel.reveal();
      this.push({ type: 'model', model });

      return;
    }
    const panel = vscode.window.createWebviewPanel(
      'coaiTerminalAccounts',
      ACCOUNTS_TITLE,
      vscode.ViewColumn.Active,
      {
        enableScripts: true,
        retainContextWhenHidden: true,
        enableFindWidget: true,
        localResourceRoots: [],
      },
    );
    this.panel = panel;
    this.listening = false;
    panel.webview.html = accountsHtml(webviewNonce(), { size: currentUiScale(), tone: currentTextTone() });
    const text = pushTextControlsTo(panel.webview);
    const heard = panel.webview.onDidReceiveMessage((message: unknown) => { this.heard(message); });
    panel.onDidDispose(() => {
      text.dispose();
      heard.dispose();
      this.panel = undefined;
      this.listening = false;
    });
  }

  /** The whole model again: the permission, the Stop or a refusal changed. */
  update(model: TableModel): void {
    this.model = model;
    this.push({ type: 'model', model });
  }

  /** A place landed: the blocks that changed since the model the tab holds, and the toolbar; the whole model when its permission changed. */
  landed(next: TableModel): void {
    const before = this.model;
    this.model = next;
    if (before === undefined || wholeAgain(before, next)) {
      this.push({ type: 'model', model: next });
    } else {
      this.pushBlocks(before, next);
    }
  }

  private pushBlocks(before: TableModel, next: TableModel): void {
    const blocks = changedBlocks(before, next);
    for (const block of blocks) {
      this.push({ type: 'block', block, toolbar: next.toolbar });
    }
    if (blocks.length === 0 && !same(before.toolbar, next.toolbar)) {
      this.push({ type: 'model', model: next });
    }
  }

  private heard(message: unknown): void {
    if (appliedTextControl(message, 'AI accounts page')) {
      return;
    }
    const command = accountsCommandOf(message, this.model);
    if (command === undefined) {
      return;
    }
    if (command.kind === 'ready') {
      this.ready();

      return;
    }
    this.hooks.run(command);
  }

  /** The page listens from now on, and is answered once, with the whole model. */
  private ready(): void {
    this.listening = true;
    if (this.model !== undefined) {
      this.push({ type: 'model', model: this.model });
    }
  }

  /** Posts, in order. Nothing is posted before the page listens: it is answered once, with the whole model, when it says `ready`. */
  private push(message: unknown): void {
    const panel = this.panel;
    if (panel === undefined || !this.listening) {
      return;
    }
    this.inFlight = this.inFlight.then(async () => {
      try {
        await panel.webview.postMessage(message);
      } catch {
        // A disposed webview is the ordinary case. The line is fixed: nothing a reply held may reach a log.
        console.error('ConnectOtherAIs: the accounts tab did not take a post');
      }
    });
  }
}
