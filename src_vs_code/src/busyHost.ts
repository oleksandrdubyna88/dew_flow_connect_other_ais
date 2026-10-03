import { type BusySnapshot } from './busySnapshot';
import { askOf, announce, InFlight, type Poster, settleEverything, tracked } from './inFlight';

/**
 * The busy mark's host half for one webview that is not the panel (todo/PLAN_busy_marks_on_every_webview.md §4.2).
 *
 * <p>The panel keeps its own `InFlight` because it paints two pages; every other webview is one page, so it gets one of
 * these. It is the panel's machinery, not a copy of it: `tracked()` numbers, settles and announces; `askOf()` decides what
 * is numbered; `whileWorking` (inside `tracked`) makes a VS Code prompt pause the mark. What this adds is only the shape
 * one webview needs — its one page as the only slot, the `ready` answer, and the snapshot a repaint paints.</p>
 *
 * <p><b>Growth.</b> One entry per press still running, each removed in its own `finally`; {@link dispose} settles the rest
 * when the webview closes.</p>
 */

/**
 * Any message a page posts. Each webview has its own message type, so this takes any object and reads only what the mark
 * needs — `type`, `seq` and `doc`, as `unknown`, checked where they are read (`askOf`, {@link BusyHost.heard}).
 */
export type PageMessage = object;

/** The three fields of a page message the mark reads; anything else on it is the page's own business. */
type MarkFields = { readonly type?: unknown; readonly seq?: unknown; readonly doc?: unknown };

/** What a failure inside a tracked action is reported through when the panel names nothing better. */
const SAY_IT = (error: unknown): void => { console.error('ConnectOtherAIs: a page action failed', error); };

export class BusyHost {
  private readonly flight: InFlight<Poster>;

  constructor(
    private readonly page: Poster,
    now: () => number = () => Date.now(),
    private readonly report: (error: unknown) => void = SAY_IT,
  ) {
    this.flight = new InFlight<Poster>(now);
  }

  /**
   * Runs what the page asked for — under the record when the page numbered it, plainly when it did not (a page from
   * before the mark, or a post the page deliberately left unnumbered, such as typing). Never rejects: a tracked action's
   * failure is reported and settled `ok: false`; an untracked one's is reported.
   */
  async track(message: PageMessage, work: () => Promise<void>): Promise<void> {
    const ask = askOf(message as MarkFields);
    if (ask !== undefined) {
      await tracked(this.flight, [this.page], this.page, ask, work, this.report);

      return;
    }
    try {
      await work();
    } catch (error) {
      this.report(error);
    }
  }

  /**
   * A fresh document says it is listening: it is told what is running, and the message is consumed. Anything announced
   * while it was being built reached the document it replaced; this answer supersedes what it was painted with.
   */
  heard(message: PageMessage): boolean {
    if ((message as MarkFields).type !== 'ready') {
      return false;
    }
    announce(this.flight, [this.page], this.report);

    return true;
  }

  /** What a page built now must paint — read when the page is BUILT, so a repaint mid-action keeps the bar. */
  snapshot(): BusySnapshot {
    return this.flight.snapshot();
  }

  /** The webview closed: whatever is still running is settled as not done. */
  dispose(): void {
    settleEverything(this.flight, this.report);
  }
}
