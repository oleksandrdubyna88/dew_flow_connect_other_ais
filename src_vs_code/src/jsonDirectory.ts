/**
 * The snapshot behind the three record watchers — consultations, escalations, question consults (S4b item 12): one
 * JSON file per record in a subdirectory of one or more data directories, read whole on every refresh, and kept as
 * the last GOOD snapshot.
 *
 * <p>Pure and `vscode`-free: the file system is handed in (`SnapshotFs`), so what a refresh keeps, drops and tells
 * is a test with a fake rather than an editor. `jsonDirectoryWatcher.ts` is the editor around it — the file events,
 * the debounce, the poll for a UNC folder — and the three watchers are configurations of that.</p>
 *
 * <p><b>What a refresh keeps.</b> A directory that is ABSENT is no records — nothing has been written there yet. A
 * directory that cannot be READ for any other reason keeps what it had: a rename landing on an open handle is the
 * ordinary Windows case, and blanking a region somebody is reading for it is the defect. One file that cannot be read
 * this pass keeps its last copy, for the same reason; one that does not parse is not a record. A shape may say
 * `unreadable: 'none'` instead — the escalation cards do.</p>
 *
 * <p><b>Which refresh wins (S4b item 10).</b> Refreshes overlap: a burst of events and a poll, or a slow share and a
 * fast one. Each is numbered as it STARTS, and one that finishes after a later-started one has already been applied
 * changes nothing — otherwise a slower, OLDER read would overwrite a newer snapshot, and with no poll left on a local
 * folder nothing would ever heal it.</p>
 */

/** One entry of a directory listing, as the snapshot needs it. */
export interface DirEntry {
  readonly name: string;
  readonly isFile: boolean;
}

/** What the snapshot asks of a file system — `vscode.workspace.fs` in the extension, a fake in a test. */
export interface SnapshotFs<Root> {
  readonly list: (root: Root, subdir: string) => Promise<readonly DirEntry[]>;
  readonly read: (root: Root, subdir: string, name: string) => Promise<string>;
  /** Whether a thrown listing means the directory is simply not there yet — which reads as no records. */
  readonly isAbsent: (error: unknown) => boolean;
}

/** One kind of record: where its files are, which names are records, how one reads, and what a person sees of them. */
export interface JsonDirectoryShape<Root, T> {
  readonly subdir: string;
  /** The names in a listing that are records — given the whole listing, because a question with its answer beside it is not one. */
  readonly records: (entries: readonly DirEntry[]) => readonly string[];
  readonly parse: (text: string, root: Root) => T | undefined;
  /** The record as it is SHOWN at `nowMs` — a question whose server stopped beating reads as interrupted (S4b item 9). */
  readonly shown?: (item: T, nowMs: number) => T;
  /**
   * What a directory or a file that cannot be read THIS pass contributes: `keep-last` (the default) its last good
   * snapshot, `none` nothing. The escalation cards keep the shape they had before the three watchers became one
   * (S4b item 12, "without changing its behaviour"): a named directory that is unreachable shows no cards — the panel
   * is where an unreadable directory is reported — rather than cards nobody can answer.
   */
  readonly unreadable?: 'keep-last' | 'none';
  /** Whether a record belongs in the snapshot at all. */
  readonly keep: (item: T, nowMs: number) => boolean;
  /** The file a record was read from — how a file that could not be read this pass keeps last pass's copy. */
  readonly fileOf: (item: T) => string;
  /**
   * What a person can SEE of the snapshot, as one string — a refresh that leaves it unchanged tells nobody. Built with
   * `sortedJoin` (`codeUnitOrder.ts`), never a sort of its own: the records arrive in directory order, and a collating
   * sort can tie two different parts and keep that order — `aSignatureIgnoresArrivalOrder.test.ts` holds every shape to it.
   */
  readonly signature: (items: readonly T[]) => string;
}

/** The record files of a listing: `.json`, never a write in flight (`.tmp`, `.tmp.json`), never a directory. */
export function jsonRecordFiles(entries: readonly DirEntry[]): readonly string[] {
  return entries
    .filter((entry) => entry.isFile && entry.name.endsWith('.json') && !entry.name.endsWith('.tmp.json'))
    .map((entry) => entry.name);
}

export class JsonDirectorySnapshot<Root, T> {
  private kept: ReadonlyMap<string, readonly T[]> = new Map();
  private started = 0;
  private applied = 0;

  /**
   * @param keyOf one spelling per root, so a root's last snapshot is found again — and a root no longer watched is dropped.
   */
  constructor(
    private readonly shape: JsonDirectoryShape<Root, T>,
    private readonly fs: SnapshotFs<Root>,
    private readonly keyOf: (root: Root) => string,
    private readonly now: () => number = Date.now,
  ) {}

  /** Every kept record, every root's in the order the roots were given. */
  get items(): readonly T[] {
    return [...this.kept.values()].flat();
  }

  /** Re-reads every root, in parallel; true when what a person sees changed. A superseded refresh changes nothing. */
  async refresh(roots: readonly Root[]): Promise<boolean> {
    const generation = ++this.started;
    const nowMs = this.now();
    const read = await Promise.all(roots.map(async (root) => [this.keyOf(root), await this.readRoot(root, nowMs)] as const));
    if (generation < this.applied) {
      return false; // a later-started refresh already landed: this older read must not overwrite it
    }
    this.applied = generation;
    const before = this.shape.signature(this.items);
    this.kept = new Map(read);

    return this.shape.signature(this.items) !== before;
  }

  /** One root's records — or, when its directory could not be read, what an unreadable one shows (absent is none). */
  private async readRoot(root: Root, nowMs: number): Promise<readonly T[]> {
    const last = this.kept.get(this.keyOf(root)) ?? [];
    const listed = await this.listed(root);
    if (listed.kind === 'failed') {
      return listed.absent ? [] : this.unreadable(last);
    }
    const each = await Promise.all(this.shape.records(listed.entries).map((name) => this.readOne(root, name, last, nowMs)));

    return each.flat();
  }

  /** The listing, or whether its failure means the directory is simply not there yet. */
  private async listed(root: Root): Promise<Listed> {
    try {
      return { kind: 'listed', entries: await this.fs.list(root, this.shape.subdir) };
    } catch (error) {
      return { kind: 'failed', absent: this.fs.isAbsent(error) };
    }
  }

  /** One file: its record if it is kept, what an unreadable file shows when it could not be read, nothing when it does not parse. */
  private async readOne(root: Root, name: string, last: readonly T[], nowMs: number): Promise<readonly T[]> {
    const text = await this.textOf(root, name);

    return text === undefined ? this.unreadable(last.filter((one) => this.shape.fileOf(one) === name)) : this.recordOf(text, root, nowMs);
  }

  private async textOf(root: Root, name: string): Promise<string | undefined> {
    try {
      return await this.fs.read(root, this.shape.subdir, name);
    } catch {
      return undefined;
    }
  }

  /** The record a file holds, as it is SHOWN now, if it is kept — or nothing. */
  private recordOf(text: string, root: Root, nowMs: number): readonly T[] {
    const parsed = this.shape.parse(text, root);
    if (parsed === undefined) {
      return [];
    }
    const shown = this.shownAt(parsed, nowMs);

    return this.shape.keep(shown, nowMs) ? [shown] : [];
  }

  private shownAt(item: T, nowMs: number): T {
    return this.shape.shown === undefined ? item : this.shape.shown(item, nowMs);
  }

  /** What a directory or a file that could not be read this pass shows: its last copy, or nothing (`unreadable: 'none'`). */
  private unreadable(last: readonly T[]): readonly T[] {
    return this.shape.unreadable === 'none' ? [] : last;
  }
}

/** A directory listing, or why there is none. */
type Listed =
  | { readonly kind: 'listed'; readonly entries: readonly DirEntry[] }
  | { readonly kind: 'failed'; readonly absent: boolean };
