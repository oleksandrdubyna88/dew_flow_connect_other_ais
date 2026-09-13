import { abreast } from './abreast';
import { ConversationMeta, ConversationSource, sameSource } from './chatStore';
import { ChatStoreFile } from './chatStoreFile';
import { ChatStoreKeeper } from './chatStoreKeeper';
import { Stamp } from './chatStoreSweep';

/**
 * The index the picker draws from: every conversation's metadata, in memory, built from the METADATA
 * FILES ALONE and refreshed against the directory rather than rebuilt from it.
 *
 * <p>At thirty conversations a day over ninety days the store is thousands of files, and a picker
 * that lists a directory on every keystroke is a picker nobody uses. So the rows live here, built in
 * the background at activation — AFTER the sweep, so the picker never holds a row for a record the
 * sweep is removing — and {@link ConversationIndex.refresh} brings them up to date by asking the
 * keeper for the directory's stamps and re-reading only what has changed. No transcript is ever
 * opened here; `ChatStoreFile.entry` reads one metadata file, and that is the only read there is.</p>
 *
 * <h2>Staleness is `mtime >= last load`, with the size beside it — not `>`</h2>
 *
 * <p>Two writes inside one filesystem timestamp granularity are real (a network or FAT volume rounds
 * to seconds), and a clock that steps backwards is real (two machines write one directory here). A
 * file whose `mtime` is at or after the instant the last load began is re-read; so is one whose SIZE
 * differs from the one last read, whatever its `mtime` says. What the pair still cannot see is a
 * rewrite to the same size with an older stamp, and that is the residual, stated here rather than
 * pretended away. The FILENAME SET is reconciled on every refresh regardless: a conversation another
 * window trashed leaves no newer file to notice and would otherwise sit in this window's picker until
 * it was chosen and failed, so a row whose file is gone is dropped and a file the index has never
 * seen is read.</p>
 *
 * <h2>Published at once, sorted once; last good rows kept</h2>
 *
 * <p>The rows are one map and one list, the list newest-first, both replaced at the end of a refresh
 * with no await between the two assignments — a reader sees the old index or the new one, never half
 * of each and never a list that disagrees with the map. The list is SORTED ONCE, there: every reader
 * gets the same published order, and {@link ConversationIndex.entries} filters it without copying or
 * sorting — the picker draws these on every keystroke of a filter, and a copy, a filter and a sort per
 * call was a sort of the whole store per keystroke, before the picker sorted it again (the code round).
 * A refresh whose survey fails keeps the last good rows and says `unavailable` with the reason, which
 * the picker shows beside them; "could not look" and "nothing here" are different facts, and the
 * store's typed listing exists so this module can tell them apart. Two refreshes asked for at once are
 * one refresh: the second joins the first.</p>
 *
 * <p><b>`now` is the wall clock, or a clock a test has placed the files' stamps against.</b> It is
 * compared with filesystem `mtime`s, so it must be the same clock the filesystem stamps with; a test
 * that pins it sets each file's time relative to it with `utimes`. No `vscode` here.</p>
 */

/** Which conversations to list: this window's workspace, or every one. */
export type IndexScope =
  | { readonly kind: 'workspace'; readonly workspace: string }
  | { readonly kind: 'everywhere' };

/** Where the index stands. `unavailable` still serves the last good rows, and says when they are from. */
export type IndexState =
  | { readonly kind: 'building' }
  | { readonly kind: 'ready'; readonly at: number }
  | { readonly kind: 'unavailable'; readonly reason: string; readonly lastGoodAt: number };

/** What a refresh did: how many entries were re-read, kept as they were, and dropped — or why it could not look. */
export type RefreshOutcome =
  | { readonly kind: 'refreshed'; readonly read: number; readonly kept: number; readonly dropped: number }
  | { readonly kind: 'unavailable'; readonly reason: string };

interface Row {
  readonly meta: ConversationMeta;
  /** The stamp of the metadata file as it was when this row was read. */
  readonly stamp: Stamp;
}

/** How many metadata files are read at once on a refresh. The store's own listing width. */
const READ_WIDTH = 8;

/**
 * Whether a metadata file must be read again: the index has never seen it, it was written at or after
 * the last load began, or its size is not the size that was read. The rule from the header, as a value.
 */
export const mustReread = (known: Stamp | undefined, seen: Stamp, loadedAt: number): boolean =>
  known === undefined || seen.mtimeMs >= loadedAt || seen.size !== known.size;

/** Newest first; ties by id, so two rows with one instant have one order. */
const newestFirst = (left: ConversationMeta, right: ConversationMeta): number =>
  right.updatedAt - left.updatedAt || left.id.localeCompare(right.id);

export class ConversationIndex {
  private rows: ReadonlyMap<string, Row> = new Map();

  /** The same rows as a list, newest update first — sorted ONCE when published; what every reader is handed. */
  private sorted: readonly ConversationMeta[] = [];

  /** The instant the last successful load BEGAN. A file stamped at or after it is re-read next time. */
  private loadedAt = 0;

  private status: IndexState = { kind: 'building' };

  private inFlight: Promise<RefreshOutcome> | undefined;

  public constructor(private readonly keeper: ChatStoreKeeper, private readonly store: ChatStoreFile) {}

  public state(): IndexState {
    return this.status;
  }

  public get size(): number {
    return this.rows.size;
  }

  /**
   * Every entry in scope, newest update first — what the picker's Recent section is drawn from. The
   * published list itself for `everywhere`, a filter of it for one workspace; nothing is sorted here.
   */
  public entries(scope: IndexScope): readonly ConversationMeta[] {
    return scope.kind === 'everywhere' ? this.sorted : this.sorted.filter((meta) => meta.workspace === scope.workspace);
  }

  /**
   * Every entry opened from this source, newest first — what *go to* matches a tab against.
   *
   * <p>`none` matches nothing, including another `none`: `sameSource` says so, and this hands the
   * question to it rather than answering twice.</p>
   */
  public bySource(source: ConversationSource): readonly ConversationMeta[] {
    return this.sorted.filter((meta) => sameSource(meta.source, source));
  }

  /** Forget one row now — for a caller that has just removed the record and must not offer it until the next refresh. */
  public drop(id: string): void {
    if (!this.rows.has(id)) {
      return;
    }
    const next = new Map(this.rows);
    next.delete(id);
    this.publish(next, this.sorted.filter((meta) => meta.id !== id));
  }

  /** PUBLISHED AT ONCE: two assignments with no await between them, so a reader never sees half the rows, or a list that disagrees with the map. */
  private publish(rows: ReadonlyMap<string, Row>, sorted: readonly ConversationMeta[]): void {
    this.rows = rows;
    this.sorted = sorted;
  }

  /**
   * Bring the index up to date with the directory. Concurrent calls share one refresh.
   *
   * @param now the wall clock's reading as the refresh begins; see the header for why it must be that clock
   */
  public refresh(now = Date.now()): Promise<RefreshOutcome> {
    if (this.inFlight === undefined) {
      this.inFlight = this.reload(now).finally(() => {
        this.inFlight = undefined;
      });
    }

    return this.inFlight;
  }

  private async reload(now: number): Promise<RefreshOutcome> {
    const surveyed = await this.keeper.survey();
    if (surveyed.kind === 'unavailable') {
      // The last good rows stay, and the state says they may be behind. Nothing is read.
      this.status = { kind: 'unavailable', reason: surveyed.reason, lastGoodAt: this.loadedAt };

      return { kind: 'unavailable', reason: surveyed.reason };
    }
    const before = this.rows;
    const seen = [...surveyed.survey.metas];
    const toRead = seen.filter(([id, stamp]) => mustReread(before.get(id)?.stamp, stamp, this.loadedAt));
    const metas = await abreast(toRead.map(([id]) => () => this.store.entry(id)), READ_WIDTH);
    const next = new Map<string, Row>();
    for (const [id, stamp] of seen) {
      const known = before.get(id);
      if (known !== undefined && !mustReread(known.stamp, stamp, this.loadedAt)) {
        next.set(id, { meta: known.meta, stamp });
      }
    }
    toRead.forEach(([id, stamp], at) => {
      const meta = metas[at];
      if (meta !== undefined) {
        // A torn or vanished entry is dropped; `entry` has said so with the path. It comes back on
        // the next refresh if it is there, because a row the index does not hold is always read.
        next.set(id, { meta, stamp });
      }
    });
    // Sorted ONCE, here, and published at once.
    this.publish(next, [...next.values()].map((row) => row.meta).sort(newestFirst));
    this.loadedAt = now;
    this.status = { kind: 'ready', at: now };

    return {
      kind: 'refreshed',
      read: toRead.length,
      kept: seen.length - toRead.length,
      dropped: [...before.keys()].filter((id) => !next.has(id)).length,
    };
  }
}
