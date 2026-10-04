import { WriteQueue } from './writeQueue';

/**
 * The catalog's three keys (`vendors`, `consultants`, `qconsultRows`) are touched one turn at a time
 * (PLAN_one_model_catalog.md E1.3/E1.4; PR #681's code round).
 *
 * <p>Two kinds of turn share one queue. A MIGRATION reads a layer and writes what `migrateLayer` says. An EDIT is the
 * old page's save: it reads the rows to fold the change in, then writes `vendors` and then the entry. Outside the
 * queue the two interleaved — the `vendors` write's change event started a run at once, which read the new rows beside
 * the OLD definition, moved it into a fresh row, and the save's reference then left that row orphaned.</p>
 *
 * <p>A migration that has not started yet reads the layer when it does, so a second request before then is the same
 * run: one save is two triggers, and was two full passes. A request made DURING a run is a new run, because that one
 * has already read.</p>
 *
 * <p>An edit's work must not wait on a migration it asks for — it would wait on itself. It never does: the triggers it
 * fires are `void`, as every `scheduleCatalogMigration` caller's are.</p>
 */
export class CatalogTurns {
  private readonly queue = new WriteQueue();

  private waiting: Promise<void> | undefined;

  /** A migration run, shared with any request already waiting to start. */
  migrate(work: () => Promise<void>): Promise<void> {
    this.waiting ??= this.queue.run(async () => {
      this.waiting = undefined;
      await work();
    });

    return this.waiting;
  }

  /** An edit, whole: nothing else touches the catalog between its read and its last write. */
  edit<T>(work: () => Promise<T>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      void this.queue.run(() => work().then(resolve, reject));
    });
  }

  /** Once every turn queued so far has finished. */
  settled(): Promise<void> {
    return this.queue.settled();
  }
}
