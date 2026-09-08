import * as crypto from 'node:crypto';
import * as vscode from 'vscode';
import { Escalation } from './escalations';
import { LogRow, questionsHtml, roundsLogHtml } from './roundsLog';
import { Push, PushLedger, Region } from './pushLedger';
import { LogCommand, logCommandOf, LogPageMessage } from './roundsLogMessages';

/** What the page can ask the extension to do. Everything else is page state and never comes back. */
export interface RoundsLogHooks {
  /** Answer… under an open question. */
  readonly onAnswer: (id: string) => Promise<void>;
  /** Today / Week / Month / Year on the spending tab. */
  readonly onUsageWindow: (window: string) => Promise<void>;
  /** ✕ beside a vendor on the spending tab. */
  readonly onForget: (provider: string) => Promise<void>;
}

/**
 * How long the panel waits for the page to say it is listening before assuming it is.
 *
 * <p>Long enough that a slow machine is not mistaken for a broken page, short enough that a person
 * who opened the log does not sit in front of an empty tab wondering.</p>
 */
const ASSUME_READY_MS = 5_000;

/**
 * The rounds log page: one webview panel per window, reused while open.
 *
 * <p>The provider only ever PUSHES data. Sorting, filtering, searching, the date range and
 * expanding a row are the page's own state, and nothing about them comes back here — which is
 * deliberate, and the lesson of the sidebar's disclosures: a page state that round-trips through
 * the extension host is a page state that can be re-applied by a patch and fire its own event
 * again. The three things that DO come back are commands: answer a question, choose a spending
 * window, forget a vendor's spending — and one word, `ready`.</p>
 *
 * <p>A push happens only when the rows, the questions or the spending region actually changed.
 * The watcher ticks every five seconds whether or not anything did, and a page re-rendering its
 * table for nothing would drop the scroll position of somebody reading it.</p>
 */
export class RoundsLogPanel {
  private panel: vscode.WebviewPanel | undefined;

  /**
   * What the page has actually been TOLD, rather than what was sent at it.
   *
   * <p>It was three `last…` strings assigned BEFORE a `void postMessage(…)`, so a message the page
   * never received was recorded as delivered and every later tick found nothing changed and sent
   * nothing. That is what emptied the blind-spot tab and the accepted/rejected counts on
   * 2026-09-08: the first paint is database-free by design, and the push that fills it in was the
   * one that went missing.</p>
   */
  private readonly ledger = new PushLedger();

  /** The newest of everything, so a page that says `ready` can be answered from here. */
  private latest: {
    rows: readonly LogRow[];
    questions: readonly Escalation[];
    usage: string;
    spots: string;
  } = { rows: [], questions: [], usage: '', spots: '' };

  /**
   * The belt to the handshake's braces.
   *
   * <p>A page whose script throws before attaching its listener never says `ready`, and gating every
   * push on that word would leave such a page empty for ever with nothing anywhere saying why. So
   * after this long the panel assumes it is listening and pushes anyway — which is exactly what it
   * did before this change, and therefore no worse. Raised as blocking by two reviewers.</p>
   */
  private assumeReadyIn: NodeJS.Timeout | undefined;

  /** Posts, in order. Two are in flight whenever a tick meets the forced answer to `ready`. */
  private inFlight: Promise<void> = Promise.resolve();

  constructor(private readonly hooks: RoundsLogHooks) {}

  /** Whether anybody is looking. When nobody is, the tick reads nothing for this page. */
  get isOpen(): boolean {
    return this.panel !== undefined;
  }

  show(rows: readonly LogRow[], questions: readonly Escalation[], usageHtml: string, spotsHtml = ''): void {
    if (this.panel !== undefined) {
      this.panel.reveal();
      this.update(rows, questions, usageHtml, true, spotsHtml);
      return;
    }
    const panel = vscode.window.createWebviewPanel(
      'coaiRoundsLog',
      'ConnectOtherAIs — Review rounds',
      vscode.ViewColumn.Active,
      // Kept alive while hidden behind another tab: the sort, the filters and the search text are
      // page state, and a page that is torn down when it is not visible loses them every time.
      { enableScripts: true, retainContextWhenHidden: true, localResourceRoots: [] },
    );
    this.panel = panel;
    panel.webview.html = roundsLogHtml(rows, questions, crypto.randomBytes(16).toString('hex'), usageHtml, spotsHtml);
    this.latest = { rows, questions, usage: usageHtml, spots: spotsHtml };
    this.rebuilt();

    panel.webview.onDidReceiveMessage((message: LogPageMessage) => this.received(logCommandOf(message)));

    panel.onDidDispose(() => {
      this.panel = undefined;
      this.clearAssumption();
      this.ledger.rebuilt();
    });
  }

  /** Pushes what changed — the rows, the spending region, or both — and nothing when nothing did. */
  update(rows: readonly LogRow[], questions: readonly Escalation[], usageHtml: string, force = false, spotsHtml = ''): void {
    if (this.panel === undefined) {
      return;
    }
    this.latest = { rows, questions, usage: usageHtml, spots: spotsHtml };
    void this.pushAll(force);
  }

  /**
   * One decided message, acted on.
   *
   * <p>`ready` is the load-bearing one: the page's script is running and its listeners are attached,
   * so a push can now actually be RECEIVED. Until this word one is accepted by VS Code and delivered
   * to nobody — `postMessage` answers true for a webview that merely exists, and one exists from the
   * moment `webview.html` is assigned.</p>
   */
  private received(command: LogCommand): void {
    if (command.kind === 'ready') {
      this.ledger.ready();
      this.clearAssumption();
      void this.pushAll(true);

      return;
    }
    if (command.kind === 'answer') {
      void this.hooks.onAnswer(command.id);
    }
    if (command.kind === 'usageWindow') {
      void this.hooks.onUsageWindow(command.window);
    }
    if (command.kind === 'forget') {
      void this.hooks.onForget(command.provider);
    }
  }

  /** A page was built: it has been told nothing, and it has not yet said it is listening. */
  private rebuilt(): void {
    this.ledger.rebuilt();
    this.clearAssumption();
    this.assumeReadyIn = setTimeout(() => {
      // A catch-all at the outermost edge of a detached execution, per `reliability.md`: nothing is
      // above this frame, so a throw here would be an unhandled rejection in the extension host and
      // would leave the panel believing it had started pushing. (gemini and local, the code round.)
      try {
        console.warn('ConnectOtherAIs: the rounds log never said it was ready; pushing at it anyway');
        this.ledger.assumeListening();
        void this.pushAll(true);
      } catch (reason: unknown) {
        console.error('ConnectOtherAIs: the rounds log fallback push failed', reason);
      }
    }, ASSUME_READY_MS);
  }

  private clearAssumption(): void {
    if (this.assumeReadyIn !== undefined) {
      clearTimeout(this.assumeReadyIn);
      this.assumeReadyIn = undefined;
    }
  }

  /**
   * Send each region the page does not already hold, and record only what arrived.
   *
   * <p>SERIALISED, because two pushes are in flight whenever an ordinary tick meets the forced
   * answer to `ready` — and an older one resolving second would otherwise record content the page
   * does not have. The ledger refuses a stale generation; this keeps the posts themselves in
   * order.</p>
   */
  private async pushAll(force: boolean): Promise<void> {
    this.inFlight = this.inFlight
      .then(() => this.pushEach(force))
      // Never silently, per `coding-style.md`: a region that failed to build its message is left
      // unrecorded and retried on the next tick, but a page stuck stale with nothing anywhere saying
      // why is the shape of the defect this file exists for. (codex and gemini, the code round.)
      .catch((reason: unknown) => {
        console.error('ConnectOtherAIs: a rounds log push failed', reason);
      });

    return this.inFlight;
  }

  private async pushEach(force: boolean): Promise<void> {
    const { rows, questions, usage, spots } = this.latest;
    // A RECORD over `Region` rather than a list: adding a region to the union without giving it a
    // push here is then a compile error rather than a region that silently never updates.
    // (gemini, the code round.)
    const regions: Record<Region, { readonly content: string; readonly message: () => unknown }> = {
      rows: {
        content: payloadOf(rows, questions),
        message: () => ({ type: 'rows', rows, questions: questionsHtml(questions) }),
      },
      usage: { content: usage, message: () => ({ type: 'usage', html: usage }) },
      spots: { content: spots, message: () => ({ type: 'spots', html: spots }) },
    };

    for (const region of Object.keys(regions) as Region[]) {
      const push = this.ledger.next(region, regions[region].content, force);
      if (push !== undefined) {
        await this.send(push, regions[region].message());
      }
    }
  }

  /**
   * One post, and the truth about whether it landed.
   *
   * <p>A rejection is a failure like any other — a webview disposed mid-push answers that way — and
   * it must leave the region UNRECORDED so the next tick sends it again, rather than becoming an
   * unhandled rejection that says nothing to anybody.</p>
   */
  private async send(push: Push, message: unknown): Promise<void> {
    const panel = this.panel;
    if (panel === undefined) {
      return;
    }

    try {
      this.ledger.settle(push, await panel.webview.postMessage(message));
    } catch (reason: unknown) {
      // Logged, not swallowed: a disposed webview is the ordinary case and says nothing new, but any
      // other reason for a region going stale has to be findable. (codex, the code round.)
      console.error(`ConnectOtherAIs: the rounds log did not take its ${push.region}`, reason);
      this.ledger.settle(push, false);
    }
  }
}

/**
 * What "changed" means for the table: the serialised rows and questions.
 *
 * <p>A running round's `seconds` advances every tick by construction, so while something runs the
 * page is pushed every five seconds — which is right, that is the number somebody is watching.
 * When nothing runs, the payload is stable and nothing is sent.</p>
 */
function payloadOf(rows: readonly LogRow[], questions: readonly Escalation[]): string {
  return JSON.stringify([rows, questions]);
}
