/**
 * What the move into the catalog does while this window does not know a key it writes
 * (todo/PLAN_catalog_migration_waits_for_its_settings.md): try once more, and then say so.
 *
 * <p>VS Code can update an extension in place, before it activates, and the window's settings registry may not hold
 * the new version's keys yet — a write to one is refused. Whether the registry catches up without a reload is VS Code's
 * business and not something this extension can promise, so the wait is short and happens once: the next change to
 * `coai` settings or a short timer, whichever comes first, runs the move again. A second wait in the same window is
 * the answer — the window will not learn the keys — and the person is told once, with the cure: reload.</p>
 *
 * <p>Without `vscode`, so the arming, the two triggers, the single warning and the stop are values a test drives; the
 * host passes the event, the timer and the warning.</p>
 */

/** Something armed that can be taken down again — a listener, a timer. */
export interface Armed {
  dispose(): void;
}

export interface WaitHooks {
  /** Calls `fire` on the next change to this extension's settings. */
  readonly onSettingsChange: (fire: () => void) => Armed;
  /** Calls `fire` once, after `ms`. */
  readonly after: (ms: number, fire: () => void) => Armed;
  /** Runs the move again. */
  readonly retry: () => void;
  /** Tells the person the window has to be reloaded. */
  readonly warn: () => void;
}

/** How long the one retry waits for a settings change before it runs anyway. */
export const RETRY_AFTER_MS = 10_000;

export class MigrationWait {
  private armed: readonly Armed[] = [];
  private waits = 0;

  constructor(private readonly hooks: WaitHooks, private readonly ms = RETRY_AFTER_MS) {}

  /** The move could not write a key this window does not know. The first time arms one retry; any later time warns, once. */
  wait(): void {
    this.waits += 1;
    if (this.waits === 1) {
      this.arm();
    } else if (this.waits === 2) {
      this.disarm();
      this.hooks.warn();
    }
  }

  /** Nothing left armed — the extension is going away. */
  dispose(): void {
    this.disarm();
  }

  private arm(): void {
    // Both triggers can be on their way at once (a timer due in the same tick as the event): the retry runs once.
    let fired = false;
    const fire = (): void => {
      if (fired) {
        return;
      }
      fired = true;
      this.disarm();
      this.hooks.retry();
    };
    this.armed = [this.hooks.onSettingsChange(fire), this.hooks.after(this.ms, fire)];
  }

  private disarm(): void {
    const armed = this.armed;
    this.armed = [];
    armed.forEach((one) => one.dispose());
  }
}
