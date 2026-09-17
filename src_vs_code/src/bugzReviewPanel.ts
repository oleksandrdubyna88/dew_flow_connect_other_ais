import { randomBytes } from 'node:crypto';

import * as vscode from 'vscode';

import { asText } from './asText';
import { notify } from './notify';
import { KeepWrite, PairsRead } from './roundsDbRead';

import { reviewPageHtml } from './bugzReviewPage';

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
  /** The pairs as the server has them now, or why it could not say. */
  readonly read: () => Promise<PairsRead>;

  /** Writes a batch of decisions, or says why it could not. */
  readonly decide: (ids: readonly number[], keep: number) => Promise<KeepWrite>;

  /**
   * Something was decided.
   *
   * <p>The sidebar shows how many pairs are still waiting, and a decision made HERE changed that.
   * Without this the count sat stale until something unrelated repainted the panel — the review
   * window and the section were reading one database and telling two stories. (Code round, gemini.)</p>
   */
  readonly changed: () => Promise<void>;
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
    // `?.` because `draw` awaits the server and a person can close the window while it does; the
    // dispose handler then clears this. The command dispatcher starts `run()` with `void`, so the
    // rejection would surface as an unhandled one rather than as anything anybody could act on.
    this.panel?.reveal(vscode.ViewColumn.Active);
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
      const written = await this.hooks.decide(ids, keep);
      if (!written.ok) {
        // A FAILURE, not "none of them existed". The two send a person to different places, and
        // saying the wrong one sends them to collect again for nothing. (Code round, codex.)
        // `notify`, which waits for the DISK and not for the person. It was `notifyAndAsk` here,
        // carried over from an `await vscode.window.showErrorMessage` in the code this replaced, and
        // both have the same defect: a VS Code error toast does not dismiss itself, so `this.draw()`
        // below did not run until somebody noticed the toast in the corner and closed it — leaving
        // this panel frozen with its controls disabled and no terminal state. The record is still on
        // disk before the toast appears; what stops is waiting for a human who has no decision to
        // make. (gemini, the code round.)
        await notify({
          as: 'error',
          class: 'failure',
          source: 'bugzReview',
          code: 'bugz-decision-not-saved',
          title: `The decision could not be saved: ${written.why}`,
          detail: written.why,
        });
      } else if (written.decided !== ids.length) {
        await notify({
          as: 'warning',
          class: 'failure',
          source: 'bugzReview',
          code: 'bugz-decisions-partly-written',
          title: `${written.decided} of ${ids.length} decisions were written. The rest name pairs this `
            + 'database does not have — collect again and they will come back.',
          detail: `${written.decided} of ${ids.length}`,
        });
      }

      await this.draw();
      await this.hooks.changed();
    }).catch(async (error_: unknown) => {
      // The same CODE as the refusal above, deliberately: one condition, reached two ways. The key
      // is what a repeat is counted on, and "the decision could not be saved" is one thing however
      // it arrived - and the same door, for the same reason: the redraw below must not wait for
      // somebody to close a toast that asks them nothing.
      await notify({
        as: 'error',
        class: 'failure',
        source: 'bugzReview',
        code: 'bugz-decision-not-saved',
        title: `The decision could not be saved: ${asText(error_)}`,
        detail: asText(error_),
      });
      await this.draw();
    });
  }

  private async draw(): Promise<void> {
    const open = this.panel;
    if (open === undefined) {
      return;
    }

    const answer = await this.hooks.read();
    if (this.panel === undefined) {
      // Disposed while the server was being asked. Painting into it now is the `Webview is
      // disposed` this repository has already been caught by once.
      return;
    }

    // A read that FAILED is not an empty corpus. Rendering the empty page for it would tell a
    // person their two hundred pairs are gone because a process timed out. (Code round, codex.)
    open.webview.html = answer.ok
      ? reviewPageHtml(answer.pairs, nonce())
      : reviewPageHtml([], nonce(), answer.why);
  }
}

/**
 * One nonce per paint: the CSP admits our one script and nothing else.
 *
 * <p><b>Cryptographically random, not `Math.random()`.</b> The nonce is the whole of the content
 * security policy here — a predictable one is a policy an injected script can satisfy, which is
 * the point of having it. `Math.random()` is seeded per process and its sequence is recoverable
 * from a few outputs.</p>
 *
 * <p>The panel's own `nonce()` (`panelProvider.ts`) still uses `Math.random()`. I copied it here
 * and a scanner refused the copy; the reuse rule says to write the new one well and SAY what was
 * found rather than imitate it, so this is the correct one and that one is reported rather than
 * quietly rewritten in a change about something else.</p>
 */
function nonce(): string {
  return randomBytes(24).toString('base64url');
}
