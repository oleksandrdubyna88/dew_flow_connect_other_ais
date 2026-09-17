import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { Arrival, group } from './notificationsRead';
import { NOTIFICATIONS_FILE, SERVER_NOTICES_FILE, notificationsPath, readNewestPlaced, serverNoticesPath } from './notificationsFile';
import { PageState, notificationsPageHtml } from './notificationsPage';
import { Span, acknowledge, olderRemain, readSeen, readSoFar } from './notificationsSeen';

/**
 * The window the notifications page lives in.
 *
 * <p>The thin half: everything it decides is decided in `notificationsPage.ts`,
 * `notificationsRead.ts` and `notificationsSeen.ts`, none of which imports `vscode` and all of
 * which therefore have tests. What is left here is a webview, a message, and the one write.</p>
 *
 * <h2>Acknowledging is the only thing this does to the disk, and it is careful</h2>
 *
 * <p>It happens AFTER a snapshot has been read and rendered, never on open, so a transient read
 * error cannot mark everything read. It covers only the range that was LOADED, and only through
 * the last complete newline. If the append fails the state is not cleared and the next open tries
 * again — a write that did not land is not an acknowledgement, which is the same defect the S1 code
 * round found when a record the disk refused came back as though it had been written.</p>
 */

/** How many records the page loads. Fixed at 3000 by the S5 plan round: an open question is not a
 *  contract, and two builds choosing 1000 and 10 000 would acknowledge different ranges of one
 *  ledger. ~400 B a record is ~1.2 MB parsed per open, reached over 19 windows of 64 KB. */
export const PAGE_LOADS = 3000;

export class NotificationsPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** What the last draw loaded, so the acknowledgement covers what was shown and nothing else. */
  private loaded: ReadonlyMap<string, Span> = new Map();

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** Opens it, or brings back the one already open and refreshes it. */
  async show(): Promise<void> {
    if (this.panel === undefined) {
      this.panel = vscode.window.createWebviewPanel(
        'coai.notifications',
        'Notifications',
        vscode.ViewColumn.Active,
        {
          enableScripts: true,
          retainContextWhenHidden: true,
          // The whole of Ctrl+F: the editor's find bar never reaches a webview on its own, and a
          // page of a year of notifications that cannot be searched is a page nobody uses. Fixed at
          // creation — there is no API to turn it on afterwards.
          enableFindWidget: true,
        },
      );
      this.panel.onDidDispose(() => {
        this.panel = undefined;
      });
      this.panel.webview.onDidReceiveMessage((message: { type?: string }) => {
        if (message.type === 'markAll') {
          void this.markEverythingRead();
        }
      });
    }

    await this.draw();
    // `?.` because `draw` awaits two files and a person can close the window while it does.
    this.panel?.reveal(vscode.ViewColumn.Active);
  }

  /** Both ledgers, merged, grouped, and what of them has been seen. */
  private async draw(): Promise<void> {
    const open = this.panel;
    if (open === undefined) {
      return;
    }
    const dataDir = coaiDataDir();

    const [mine, theirs, seen] = await Promise.all([
      readNewestPlaced(notificationsPath(dataDir), PAGE_LOADS),
      readNewestPlaced(serverNoticesPath(dataDir), PAGE_LOADS),
      readSeen(dataDir),
    ]);

    const soFar = readSoFar(seen);
    const arrivals: Arrival[] = [
      ...mine.records.map((placed) => ({ ...placed, ledger: 'extension' as const })),
      ...theirs.records.map((placed) => ({ ...placed, ledger: 'server' as const })),
    ];
    const read = [
      ...(soFar.get(NOTIFICATIONS_FILE) ?? []),
      ...(soFar.get(SERVER_NOTICES_FILE) ?? []),
    ];

    const state: PageState = {
      rows: group(arrivals, read),
      dataDir,
      older: mine.start > 0 || theirs.start > 0
        || olderRemain(soFar.get(NOTIFICATIONS_FILE) ?? [])
        || olderRemain(soFar.get(SERVER_NOTICES_FILE) ?? []),
      loaded: arrivals.length,
    };

    open.webview.html = notificationsPageHtml(state, randomBytes(16).toString('base64'));

    this.loaded = new Map([
      [NOTIFICATIONS_FILE, { from: mine.start, to: mine.end }],
      [SERVER_NOTICES_FILE, { from: theirs.start, to: theirs.end }],
    ]);

    // AFTER the render, never before it: a read that threw would otherwise have marked everything
    // read on a page nobody saw.
    await this.acknowledgeWhatWasShown(dataDir);
  }

  /** One acknowledgement per ledger, covering exactly the range that was loaded. */
  private async acknowledgeWhatWasShown(dataDir: string): Promise<void> {
    const utc = new Date().toISOString();
    for (const [ledger, span] of this.loaded) {
      if (span.to > span.from) {
        await acknowledge(dataDir, { utc, ledger, from: span.from, to: span.to });
      }
    }
  }

  /**
   * A person's own act: everything in both ledgers, whether it was rendered or not.
   *
   * <p>The page may only claim what it showed; a person may say "I do not need the rest". Without
   * this the count cannot realistically reach zero on a machine with history — the page covers the
   * newest 3000 of maybe 40 000 and reaching the rest is hundreds of page-turns. Operator,
   * 2026-09-17.</p>
   */
  private async markEverythingRead(): Promise<void> {
    const dataDir = coaiDataDir();
    const utc = new Date().toISOString();
    for (const [ledger, path] of [
      [NOTIFICATIONS_FILE, notificationsPath(dataDir)],
      [SERVER_NOTICES_FILE, serverNoticesPath(dataDir)],
    ] as const) {
      const whole = await readNewestPlaced(path, 1);
      if (whole.end > 0) {
        // `[0, end)` at THIS instant, so a record written a moment later is still unread. A button
        // that acknowledged the future would silently swallow the next fault, which is the one that
        // matters.
        await acknowledge(dataDir, { utc, ledger, from: 0, to: whole.end });
      }
    }
    await this.draw();
  }
}
