import { keepPlan, keptCopy, restoredRows, type DroppedValue, type KeptRow, type KeptStore } from './cliPathKeep';

/**
 * The keeper of the per-side CLI paths (`cliPathKeep.ts` holds every decision): it looks at the rows at every start and on
 * every settings change, keeps the copies, says a dropped member ONCE per window run and per row — when it APPEARS, as the
 * path watch says a missing path — and restores only on a click.
 *
 * <p>`vscode`-free: the store, the settings, the write and the voice are handed in (`cliPathKeepHost.ts` is the wiring), so
 * a test runs an older extension's save, a stale click and a reused id without an editor.</p>
 */

export interface KeeperDeps<R extends KeptRow> {
  readonly store: KeptStore;
  /**
   * The settings layer the catalog rows are read from and saved to — `undefined` when this side keeps its own settings
   * (`coai.perSideSettings`): only this side reads those rows, so a member another side's save dropped cannot be in them.
   */
  layer(): string | undefined;
  rows(): readonly R[];
  /** The ONE settings write — the rows whole, as every vendor save writes them. */
  write(rows: readonly R[]): Promise<void>;
  /** A dropped member appeared: offer it back (the host's notice, with **Restore**). */
  say(dropped: DroppedValue): void;
  /** A dropped member is no longer dropped: the next time it is, is news. */
  resolved(subject: string): void;
  /** A click on **Restore** found the row changed since the offer: nothing was written, and why. */
  refused(dropped: DroppedValue, reason: string): void;
  /** A store or settings failure — logged, never thrown out of a listener. */
  failed(error: unknown): void;
}

export class CliPathKeeper<R extends KeptRow> {
  /** The dropped members said in this run, by subject — replaced, never changed in place. */
  private said: ReadonlySet<string> = new Set<string>();
  /** Every look waits for the one before it: two settings events must not write the same copies twice at once. */
  private queue: Promise<void> = Promise.resolve();

  constructor(private readonly deps: KeeperDeps<R>) {}

  /** Look at the rows now — at activation and on every `coai` settings change. Never rejects. */
  check(): Promise<void> {
    this.queue = this.queue.then(() => this.look()).catch((error: unknown) => { this.deps.failed(error); });

    return this.queue;
  }

  /** **Restore** was clicked: the row as it is NOW decides, and nothing is written when it changed since the offer. Never rejects. */
  restore(offer: DroppedValue): Promise<void> {
    this.queue = this.queue.then(() => this.restoreNow(offer)).catch((error: unknown) => { this.deps.failed(error); });

    return this.queue;
  }

  private async look(): Promise<void> {
    const layer = this.deps.layer();
    const plan = layer === undefined ? { writes: [], dropped: [] } : keepPlan(this.deps.store, layer, this.deps.rows());
    this.tell(plan.dropped);
    // One write per key, each to a different key, so they need no order among themselves.
    await Promise.all(plan.writes.map(([key, value]) => this.deps.store.update(key, value)));
  }

  /** Say what appeared; settle what ended. */
  private tell(dropped: readonly DroppedValue[]): void {
    const now = new Set(dropped.map((one) => one.key));
    const ended = [...this.said].filter((key) => !now.has(key));
    const fresh = dropped.filter((value) => !this.said.has(value.key));
    this.said = new Set([...[...this.said].filter((key) => now.has(key)), ...fresh.map((one) => one.key)]);
    ended.forEach((key) => { this.deps.resolved(key); });
    fresh.forEach((one) => { this.deps.say(one); });
  }

  private async restoreNow(offer: DroppedValue): Promise<void> {
    const verdict = this.deps.layer() === undefined
      ? 'this side keeps its own settings now'
      : restoredRows(this.deps.rows(), offer, keptCopy(this.deps.store.get(offer.key)));
    if (typeof verdict === 'string') {
      this.deps.refused(offer, verdict);
      // Offered again on the next look when it is still dropped — with the row as it is now.
      this.said = new Set([...this.said].filter((key) => key !== offer.key));
      this.deps.resolved(offer.key);
      await this.look();

      return;
    }
    await this.deps.write(verdict);
  }
}
