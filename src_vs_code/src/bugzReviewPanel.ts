import * as vscode from 'vscode';

import { ReviewPair, reviewPageHtml } from './bugzReviewPage';

/**
 * The window the review page lives in.
 *
 * <p>A panel of its own rather than a section of the sidebar, on `RoundsLogPanel`'s precedent: a
 * person reading two hundred before/after pairs is reading, and a sidebar column is not where
 * reading happens.</p>
 *
 * <p><b>It never paints a decision itself.</b> The page posts what a person pressed; the extension
 * writes it; the page is then redrawn from what the database SAYS. A page that congratulated itself
 * and was wrong is exactly the failure `keep` was made a column to prevent — and it is the failure
 * story 4's Collect button had, when it was drawn from a flag instead of from a row.</p>
 */
export interface ReviewHooks {
  /** The pairs as the server has them now. */
  readonly read: () => Promise<readonly ReviewPair[]>;

  /** Writes a batch of decisions and answers how many rows it actually decided. */
  readonly decide: (ids: readonly number[], keep: number) => Promise<number>;
}

export class BugzReviewPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** Posts and redraws, in order — two decisions in flight would race the redraw between them. */
  private inFlight: Promise<void> = Promise.resolve();

  constructor(private readonly hooks: ReviewHooks) {}

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** Opens it, or brings back the one that is already open and refreshes it. */
  async show(): Promise<void> {
    if (this.panel === undefined) {
      this.panel = vscode.window.createWebviewPanel(
        'coai.bugzReview',
        'Review bugs',
        vscode.ViewColumn.Active,
        {
          enableScripts: true,
          retainContextWhenHidden: true,
          // `enableFindWidget` is the whole of Ctrl+F: the editor's find bar never reaches a
          // webview on its own, and a page of two hundred pairs that cannot be searched is a page
          // nobody can use. Fixed at creation — there is no API to turn it on afterwards.
          enableFindWidget: true,
        },
      );
      this.panel.onDidDispose(() => {
        this.panel = undefined;
      });
      this.panel.webview.onDidReceiveMessage((m: { type?: string; keep?: number; ids?: number[] }) => {
        if (m.type === 'decide' && Array.isArray(m.ids)) {
          this.queue(m.ids, Number(m.keep));
        }
      });
    }

    await this.draw();
    this.panel.reveal(vscode.ViewColumn.Active);
  }

  /**
   * Writes one batch, then redraws from storage.
   *
   * <p>Serialised through {@link inFlight} because a person can press Keep and then Drop faster than
   * a write completes, and two redraws racing would leave the page showing whichever finished last
   * rather than what is true.</p>
   */
  private queue(ids: readonly number[], keep: number): void {
    this.inFlight = this.inFlight.then(async () => {
      const decided = await this.hooks.decide(ids, keep);
      if (decided !== ids.length) {
        // Said out loud rather than swallowed: a decision that did not land is the one thing a
        // person cannot see from a page that redrew itself successfully.
        await vscode.window.showWarningMessage(
          `${decided} of ${ids.length} decisions were written. The rest name pairs this database `
          + 'does not have — collect again and they will come back.');
      }

      await this.draw();
    }).catch(async (wrong: unknown) => {
      await vscode.window.showErrorMessage(`The decision could not be saved: ${String(wrong)}`);
      await this.draw();
    });
  }

  private async draw(): Promise<void> {
    const open = this.panel;
    if (open === undefined) {
      return;
    }

    const pairs = await this.hooks.read();
    if (this.panel === undefined) {
      // Disposed while the server was being asked. Painting into it now is the `Webview is
      // disposed` this repository has already been caught by once.
      return;
    }

    open.webview.html = reviewPageHtml(pairs, nonce());
  }
}

/** One nonce per paint: the CSP admits our one script and nothing else. */
function nonce(): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
  let text = '';
  for (let i = 0; i < 32; i++) {
    text += alphabet.charAt(Math.floor(Math.random() * alphabet.length));
  }

  return text;
}
