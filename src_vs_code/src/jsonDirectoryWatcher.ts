import * as vscode from 'vscode';
import { POLL_MS, REAL_TIMER, type Timer, WATCH_DEBOUNCE_MS, debounced, needsPoll } from './debounced';
import { type JsonDirectoryShape, JsonDirectorySnapshot, type SnapshotFs } from './jsonDirectory';

/** `vscode.workspace.fs` as the snapshot asks for it. Absent is the one error that reads as no records. */
export const WORKSPACE_FS: SnapshotFs<vscode.Uri> = {
  list: async (root, subdir) => (await vscode.workspace.fs.readDirectory(vscode.Uri.joinPath(root, subdir)))
    .map(([name, kind]) => ({ name, isFile: kind === vscode.FileType.File })),
  read: async (root, subdir, name) => new TextDecoder().decode(await vscode.workspace.fs.readFile(vscode.Uri.joinPath(root, subdir, name))),
  // A code is a contract; the absence of an answer is not. `stat` was asked once, and its own failure read as "gone",
  // which wiped every live consultation off the panel on a permission error. (CodeRabbit, on the pull request.)
  isAbsent: (error) => error instanceof vscode.FileSystemError && error.code === 'FileNotFound',
};

/** What makes one watcher: the record shape, and — for a question consult — when to read again though no file moved. */
export interface JsonDirectoryWatcherOptions<T> {
  readonly shape: JsonDirectoryShape<vscode.Uri, T>;
  /**
   * When the kept snapshot must be read again although no file changed — a question whose server stopped beating
   * turns `interrupted` at a moment no event announces (S4b item 9). Undefined is never.
   */
  readonly recheckAt?: (items: readonly T[], nowMs: number) => number | undefined;
  readonly timer?: Timer;
}

/**
 * One data directory's (or several's) records, watched — the shape the consultation, escalation and question-consult
 * watchers each wrote by hand (S4b item 12), once: a glob per root, because the subdirectory may not exist until the
 * first record; every file event gathered into one refresh within 175 ms (A5); the five-second poll only for a UNC
 * folder, where events are not delivered; the last good snapshot and the generation guard in `JsonDirectorySnapshot`.
 *
 * <p>`onChanged` is told only when what a person SEES changed — a burst of rewrites, a poll over an unchanged folder
 * and a superseded refresh all repaint nothing.</p>
 */
export class JsonDirectoryWatcher<T> implements vscode.Disposable {
  private readonly snapshot: JsonDirectorySnapshot<vscode.Uri, T>;
  private readonly timer: Timer;
  private readonly changed: ReturnType<typeof debounced>;
  private readonly watchers: vscode.Disposable[] = [];
  private roots: readonly vscode.Uri[];
  private poll: ReturnType<typeof setInterval> | undefined;
  private recheck: unknown;

  /** Called after a refresh that changed what a person would see. */
  public onChanged: () => void = () => {};

  constructor(roots: readonly vscode.Uri[], private readonly options: JsonDirectoryWatcherOptions<T>) {
    this.roots = roots;
    this.timer = options.timer ?? REAL_TIMER;
    this.snapshot = new JsonDirectorySnapshot(options.shape, WORKSPACE_FS, (root) => root.toString());
    this.changed = debounced(() => void this.refresh(), WATCH_DEBOUNCE_MS, this.timer);
  }

  /** Every kept record, from every root. */
  get items(): readonly T[] {
    return this.snapshot.items;
  }

  start(): void {
    this.watch();
    void this.refresh();
  }

  /** A new list of roots — the escalation setting moved: watchers rebuilt, the poll decided again, the snapshot read again. */
  setRoots(roots: readonly vscode.Uri[]): void {
    this.roots = roots;
    this.watch();
    void this.refresh();
  }

  async refresh(): Promise<void> {
    const moved = await this.snapshot.refresh(this.roots);
    this.scheduleRecheck();
    if (moved) {
      this.onChanged();
    }
  }

  dispose(): void {
    this.unwatch();
    this.changed.cancel();
    this.timer.clear(this.recheck);
  }

  /** A watcher on each root that can have one — each in its own try, so one that cannot be watched leaves the others watched. */
  private watch(): void {
    this.unwatch();
    for (const root of this.roots) {
      try {
        const watcher = vscode.workspace.createFileSystemWatcher(new vscode.RelativePattern(root, `${this.options.shape.subdir}/*.json`));
        this.watchers.push(watcher, watcher.onDidCreate(this.changed), watcher.onDidChange(this.changed), watcher.onDidDelete(this.changed));
      } catch {
        // Watched by the poll alone from here, if it is a folder that needs one. Nothing is lost but the immediacy.
      }
    }
    // The poll only where events are not delivered: a \\wsl.localhost or network folder.
    this.poll = needsPoll(this.roots.map((root) => root.fsPath)) ? setInterval(() => void this.refresh(), POLL_MS) : undefined;
  }

  private unwatch(): void {
    for (const one of this.watchers.splice(0)) {
      one.dispose();
    }
    if (this.poll !== undefined) {
      clearInterval(this.poll);
      this.poll = undefined;
    }
  }

  /** One timer at the next moment the snapshot's own shape says it will look different, replacing any earlier one. */
  private scheduleRecheck(): void {
    this.timer.clear(this.recheck);
    const nowMs = Date.now();
    const at = this.options.recheckAt?.(this.snapshot.items, nowMs);
    this.recheck = at === undefined ? undefined : this.timer.set(() => void this.refresh(), Math.max(0, at - nowMs));
  }
}
