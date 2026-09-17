/**
 * One action at a time, and a page that is never left with every control disabled.
 *
 * <p>A panel with buttons has two problems that look like one. The first is ORDER: a person can
 * press Revoke and then Next faster than a request completes, and two redraws racing leave the page
 * showing whichever finished last rather than what is true. Three panels in this extension solve
 * that the same way — a promise chain each action is appended to.</p>
 *
 * <p>The second is what the chain does NOT solve, and it is the reason this is a module rather than
 * a fourth copy of six lines. While an action runs, the page is drawn with every control disabled —
 * deliberately, because a second Issue pressed during the first is a second live key nobody
 * accounted for. When the action ends, the flag drops in a `finally`, and the page on screen was
 * painted BEFORE that: it is still disabled, and nothing repaints it. Refresh is disabled too, so
 * there is no button left that could fix it. `bugzReviewPanel.ts` carries a comment about being
 * caught by exactly this shape from the other end. So the flag and the repaint live together here:
 * whoever sets the flag is the one who must give the controls back. (Code round 2, codex.)</p>
 *
 * <p>Nothing in this file imports the editor, which is what lets a test drive it — the standing
 * arrangement in this repository for anything a `vscode` host would otherwise make unreachable.</p>
 */

/** What a coordinator does at the three moments a caller cannot see. */
export interface TurnHooks {
  /**
   * An action has begun and nothing has been asked for yet.
   *
   * <p>Called with {@link Turns.busy} already true. This is the half the round-1 fix was missing:
   * the page was given a `busy` flag but nothing painted between pressing a button and the answer
   * arriving, so during the ten seconds that matter every control was still live and Issue pressed
   * twice queued two issuances — the very thing the flag was added for.</p>
   */
  readonly started: () => Promise<void>;
  /**
   * An action has ended — painted, refused, or thrown — and nothing is in flight.
   *
   * <p>Called with {@link Turns.busy} already false, so a repaint reads the state it is about to
   * show rather than the state it is leaving.</p>
   */
  readonly settled: () => Promise<void>;
  /** An action threw. The person is told; the coordinator carries on. */
  readonly failed: (reason: unknown) => Promise<void>;
}

/** Actions, in order, with the controls given back after every one of them. */
export class Turns {
  private chain: Promise<void> = Promise.resolve();

  private running = false;

  constructor(private readonly hooks: TurnHooks) {}

  /** Whether an action is in flight, which is what a page disables its controls on. */
  get busy(): boolean {
    return this.running;
  }

  /** Queues one action. What comes back is over when THAT action, and its repaint, are over. */
  run(work: () => Promise<void>): Promise<void> {
    this.chain = this.chain.then(() => this.turn(work));

    return this.chain;
  }

  private async turn(work: () => Promise<void>): Promise<void> {
    this.running = true;
    // BEFORE the work, so the page says what is happening for as long as it happens.
    await this.guarded(this.hooks.started);
    await this.guarded(work);
    // BEFORE the repaint, so what it paints is a page whose controls are live again.
    this.running = false;
    await this.guarded(this.hooks.settled);
  }

  /**
   * Runs one half of a turn, and tells rather than throws.
   *
   * <p>A rejection escaping here would be appended to {@link chain} and every later action would be
   * skipped — a panel that silently stops responding, which is worse than the failure that caused
   * it. Painting is a webview write and a webview can be disposed mid-flight, so this is an
   * ordinary event rather than a theoretical one.</p>
   */
  private async guarded(work: () => Promise<void>): Promise<void> {
    try {
      await work();
    } catch (reason: unknown) {
      await this.told(reason);
    }
  }

  /** And a reporter that fails is not the end of the panel either. */
  private async told(reason: unknown): Promise<void> {
    try {
      await this.hooks.failed(reason);
    } catch {
      // Nowhere left to say it: the thing whose job is saying things is what failed.
    }
  }
}
