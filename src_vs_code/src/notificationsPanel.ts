import { randomBytes } from 'node:crypto';
import * as vscode from 'vscode';
import { coaiDataDir } from './dataDir';
import { Arrival, ReadPerLedger, group } from './notificationsRead';
import {
  NOTIFICATIONS_FILE,
  SERVER_NOTICES_FILE,
  PlacedRecord,
  notificationsPath,
  readNewestPlaced,
  serverNoticesPath,
} from './notificationsFile';
import { PageState, notificationsPageHtml, waitingPageHtml } from './notificationsPage';
import { Span, acknowledge, clampTo, olderRemain, readSeen, readSoFar } from './notificationsSeen';

/**
 * The window the notifications page lives in.
 *
 * <p>The thin half: everything it decides is decided in `notificationsPage.ts`,
 * `notificationsRead.ts` and `notificationsSeen.ts`, none of which imports `vscode` and all of
 * which therefore have tests. What is left here is a webview, three messages, and the one write.</p>
 *
 * <h2>Acknowledging is the only thing this does to the disk, and it is careful</h2>
 *
 * <p>It happens when the PAGE says it was shown, never when the host merely sent it markup — the
 * `shown` message carries the generation it was drawn for and whether a filter narrows it, and both
 * are checked. So a snapshot that was read but never rendered marks nothing read, a stale page
 * cannot acknowledge the snapshot that replaced it, and a filtered view cannot claim three thousand
 * records were read while three are on screen.</p>
 *
 * <p>It covers only the range that was LOADED, and only through the last complete newline. If the
 * append fails the state is not cleared, the person is told where they are looking, and the next
 * open tries again. A write that did not land is not an acknowledgement — the defect the S1 code
 * round found when a record the disk refused came back as though it had been written.</p>
 */

/**
 * How many records the page loads.
 *
 * <p>Fixed at 3000 by the S5 plan round: an open question is not a contract, and two builds
 * choosing 1000 and 10 000 would acknowledge different ranges of one ledger. ~400 B a record is
 * ~1.2 MB parsed per open, reached over 19 windows of 64 KB.</p>
 */
export const PAGE_LOADS = 3000;

/**
 * How long the page waits for the ledgers before it gives up and says so.
 *
 * <p>The data directory may be a NAS — the operator's is. Without a bound, `show()` awaits two
 * reads for ever and leaves a command in flight and a window that never appears, with nothing to
 * cancel. (codex, the S5 code round.)</p>
 */
export const READ_CEILING_MS = 20_000;

/** What the page sends back. Nothing else is acted on. */
interface FromThePage {
  readonly type?: unknown;
  readonly generation?: unknown;
  readonly filtered?: unknown;
}

/** What one ledger's read produced, or that it could not be read. */
interface LedgerRead {
  readonly records: readonly PlacedRecord[];
  readonly start: number;
  readonly end: number;
  readonly readable: boolean;
}

/** Whatever `work` answers, or nothing when the clock runs out first. */
export async function withinTheClock<T>(work: Promise<T>, msLeft: number): Promise<T | undefined> {
  let timer: NodeJS.Timeout | undefined;
  const clock = new Promise<undefined>((resolve) => {
    timer = setTimeout(() => resolve(undefined), Math.max(0, msLeft));
    // A pending timer is itself a reason a host will not exit, and this one can outlive the read.
    timer.unref?.();
  });
  try {
    return await Promise.race([work, clock]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

export class NotificationsPanel {
  private panel: vscode.WebviewPanel | undefined;

  /** What the last draw loaded, so an acknowledgement covers that and nothing else. */
  private loaded: ReadonlyMap<string, Span> = new Map();

  /** Which draw is on screen. Only a `shown` naming this one may acknowledge `loaded`. */
  private generation = 0;

  /** The newest generation already written down, so a repeated `shown` writes nothing twice. */
  private acknowledged = 0;

  /**
   * One draw at a time.
   *
   * <p>`show()` and *Mark everything read* both draw, and two in flight would overwrite each
   * other's `loaded` and `generation` — so a range could be acknowledged against a snapshot that is
   * no longer the one on screen. (Two reviewers, the S5 code round.)</p>
   */
  private drawing: Promise<void> = Promise.resolve();

  /** One write at a time, for the same reason, on a separate chain so a slow draw cannot block it. */
  private writing: Promise<void> = Promise.resolve();

  /** Something to tell the person that has to outlive the draw it happened in. */
  private notice = '';

  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  /** Opens it, or brings back the one already open and refreshes it. */
  async show(): Promise<void> {
    if (this.panel === undefined) {
      const panel = vscode.window.createWebviewPanel(
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
      this.panel = panel;
      // Something to read WHILE the disk is read. Without it a slow share shows a window with
      // nothing in it, which is indistinguishable from one that failed. (codex, the S5 code round.)
      panel.webview.html = waitingPageHtml(coaiDataDir(), randomBytes(16).toString('base64'));
      panel.onDidDispose(() => {
        this.panel = undefined;
      });
      panel.webview.onDidReceiveMessage((message: FromThePage) => {
        this.heard(message);
      });
      panel.reveal(vscode.ViewColumn.Active);
    } else {
      this.panel.reveal(vscode.ViewColumn.Active);
    }

    await this.redraw();
  }

  /** The page's two messages, and nothing else. */
  private heard(message: FromThePage): void {
    const generation = message.generation;
    if (message.type === 'shown' && typeof generation === 'number') {
      const filtered = message.filtered === true;
      this.write(() => this.acknowledgeWhatWasShown(generation, filtered));

      return;
    }
    if (message.type === 'markAll') {
      this.write(() => this.markEverythingRead());
    }
  }

  /** Serialised, so two draws cannot overwrite each other's snapshot. */
  private redraw(): Promise<void> {
    this.drawing = this.drawing.then(() => this.draw()).catch((reason: unknown) => {
      console.error('ConnectOtherAIs: the notifications page could not be drawn', reason);
    });

    return this.drawing;
  }

  /** Serialised, so two acknowledgements cannot interleave against one snapshot. */
  private write(work: () => Promise<void>): void {
    this.writing = this.writing.then(work).catch((reason: unknown) => {
      console.error('ConnectOtherAIs: a notifications acknowledgement failed', reason);
    });
  }

  /** One ledger, bounded by the clock, saying whether it could be read at all. */
  private async oneLedger(path: string, deadline: number): Promise<LedgerRead> {
    const read = await withinTheClock(readNewestPlaced(path, PAGE_LOADS), deadline - Date.now());

    return read === undefined
      ? { records: [], start: 0, end: 0, readable: false }
      : { ...read, readable: true };
  }

  /** Both ledgers, merged, grouped, and what of them has been seen. */
  private async draw(): Promise<void> {
    if (this.panel === undefined) {
      return;
    }
    const dataDir = coaiDataDir();
    const deadline = Date.now() + READ_CEILING_MS;

    const [mine, theirs, seen] = await Promise.all([
      this.oneLedger(notificationsPath(dataDir), deadline),
      this.oneLedger(serverNoticesPath(dataDir), deadline),
      withinTheClock(readSeen(dataDir), deadline - Date.now()),
    ]);

    // Re-read after the awaits. Every one of them is a place the event loop can run `onDidDispose`,
    // and drawing into a panel that has gone would leave `loaded` describing a window nobody has.
    const alive = this.panel;
    if (alive === undefined) {
      return;
    }

    const soFar = readSoFar(seen ?? []);
    const arrivals: Arrival[] = [
      ...mine.records.map((placed) => ({ ...placed, ledger: 'extension' as const })),
      ...theirs.records.map((placed) => ({ ...placed, ledger: 'server' as const })),
    ];
    // Kept APART, and cut to the file each one indexes. A byte offset means nothing without its
    // file, and one that outlives a rotated ledger marks records read that this file never held.
    const read: ReadPerLedger = {
      extension: clampTo(soFar.get(NOTIFICATIONS_FILE) ?? [], mine.end),
      server: clampTo(soFar.get(SERVER_NOTICES_FILE) ?? [], theirs.end),
    };

    const unreadable = !mine.readable || !theirs.readable || seen === undefined;
    this.generation += 1;
    const state: PageState = {
      rows: group(arrivals, read),
      dataDir,
      older: mine.start > 0 || theirs.start > 0
        || olderRemain(read.extension)
        || olderRemain(read.server),
      loaded: arrivals.length,
      generation: this.generation,
      ...(this.notice === '' ? {} : { notice: this.notice }),
      // An unreadable ledger is SAID, never rendered as empty tabs. A person who sees an empty page
      // concludes there is nothing to see, which is the opposite of what happened.
      ...(unreadable
        ? { unreadable: `the ledgers did not answer within ${Math.round(READ_CEILING_MS / 1000)} seconds` }
        : {}),
    };

    // The window this draw is offering. Nothing is written until the page says it rendered it, and
    // an unreadable draw offers nothing at all.
    this.loaded = unreadable
      ? new Map()
      : new Map([
        [NOTIFICATIONS_FILE, { from: mine.start, to: mine.end }],
        [SERVER_NOTICES_FILE, { from: theirs.start, to: theirs.end }],
      ]);
    this.notice = '';
    alive.webview.html = notificationsPageHtml(state, randomBytes(16).toString('base64'));
  }

  /**
   * One acknowledgement per ledger, covering exactly the range the page says it rendered.
   *
   * <p>Refused for a generation that is not the one on screen, for a filtered view, and for one
   * already written down — a page re-offers itself whenever its filter is cleared, and that must
   * cost nothing.</p>
   */
  private async acknowledgeWhatWasShown(generation: number, filtered: boolean): Promise<void> {
    const panel = this.panel;
    if (panel === undefined || filtered || generation !== this.generation
      || generation <= this.acknowledged) {
      return;
    }
    // Claimed BEFORE the first await, so a second `shown` for this draw cannot start a second pass.
    this.acknowledged = generation;
    const dataDir = coaiDataDir();
    const utc = new Date().toISOString();
    const refused: string[] = [];
    for (const [ledger, span] of this.loaded) {
      if (span.to <= span.from) {
        continue;
      }
      await acknowledge(dataDir, { utc, ledger, from: span.from, to: span.to }, () => {
        refused.push(ledger);
      });
    }
    if (refused.length > 0) {
      // Said where the person is looking, and the range stays unread so the next open retries. A
      // silent failure here would leave the page claiming records were marked read while the count
      // went on including them — the S1 defect in a new place.
      this.say(`${refused.join(' and ')} could not be marked read; it will be tried again next time.`);
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
    if (this.panel === undefined) {
      return;
    }
    try {
      const dataDir = coaiDataDir();
      const utc = new Date().toISOString();
      const deadline = Date.now() + READ_CEILING_MS;
      let landed = true;
      for (const [ledger, path] of [
        [NOTIFICATIONS_FILE, notificationsPath(dataDir)],
        [SERVER_NOTICES_FILE, serverNoticesPath(dataDir)],
      ] as const) {
        const whole = await withinTheClock(readNewestPlaced(path, 1), deadline - Date.now());
        if (whole === undefined) {
          landed = false;
          continue;
        }
        if (whole.end > 0) {
          // `[0, end)` at THIS instant, so a record written a moment later is still unread. A button
          // that acknowledged the future would silently swallow the next fault, which is the one
          // that matters.
          await acknowledge(dataDir, { utc, ledger, from: 0, to: whole.end }, () => {
            landed = false;
          });
        }
      }
      this.notice = landed
        ? 'Everything in both ledgers is marked read.'
        : 'Some of it could not be written down. Nothing was lost; try again.';
    } catch (reason: unknown) {
      console.error('ConnectOtherAIs: everything could not be marked read', reason);
      this.notice = 'That did not work. Nothing was lost.';
    }
    // The outcome travels in the state rather than in a message, because the redraw that follows
    // replaces the document a message would have landed in.
    await this.redraw();
  }

  /**
   * Tell the page something, and keep it for the next draw only if the page did not hear it.
   *
   * <p>`postMessage` answers whether it reached a live webview, so the fallback is a measurement
   * rather than a guess. Keeping it unconditionally would show a stale "could not be marked read"
   * on the next open, after the retry had already succeeded.</p>
   */
  private say(said: string): void {
    const panel = this.panel;
    if (panel === undefined) {
      this.notice = said;

      return;
    }
    const keepItForNextTime = (): void => {
      this.notice = said;
    };
    void Promise.resolve(panel.webview.postMessage({ type: 'notice', said })).then(
      (delivered) => {
        if (delivered !== true) {
          keepItForNextTime();
        }
      },
      keepItForNextTime,
    );
  }
}
