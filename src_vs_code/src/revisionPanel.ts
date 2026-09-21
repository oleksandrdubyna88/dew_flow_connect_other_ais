import { ReviewPair } from './reviewPair';
import { asText } from './asText';
import {
  affectedBy,
  currentFileIn,
  emptyMemory,
  FileAtRead,
  heldRevision,
  remember,
  rememberCurrent,
  RevisionDocument,
  RevisionMemory,
  stateOf,
} from './openAtRevision';
import { revisionActions, RevisionState, TREE_WORKING, TreeState, WORKING } from './revisionActions';
import { mountsNote, TreeRead, treeSentence } from './reviewTree';
import { AskedRevision } from './roundsDbRead';
import { AskedTree } from './reviewTreeRead';

/**
 * Reaching the code for one review page: what it has learned, what is out, and what each row says.
 *
 * <p><b>Why it is not in `bugzReviewPanel`.</b> It was, and the panel reached 831 lines against the
 * 800 the style rule allows — the implementer predicted exactly this and named this extraction. The
 * pieces come out together because they share three pieces of state that nothing else touches: what
 * the panel has learned about reaching code, which reads are in flight, and which rows have a press
 * still out. A cluster that takes its own fields with it is one that has found its seam.</p>
 *
 * <p>It posts through a function the panel gives it rather than holding a webview, so the panel
 * keeps the one place that talks to VS Code and this stays testable without an editor.</p>
 */
export interface RevisionReach {
  /** One pair's file at the commit the reviewers read — the server, never git from here. */
  readonly readFileAt: (asked: AskedRevision) => Promise<FileAtRead>;

  /** Shows a historical file, read-only, in a document of this product's own scheme. */
  readonly showRevision: (document: RevisionDocument) => Promise<void>;

  /** Shows the file as it is NOW, from the live filesystem. */
  readonly showCurrent: (file: string, line: number) => Promise<void>;

  /** The open workspace folders, which a recorded checkout must be inside of. */
  readonly folders: () => readonly string[];

  /** One pair's repository checked out at its commit — the server, never git from here. */
  readonly readTreeAt: (asked: AskedTree) => Promise<TreeRead>;

  /** Opens a checkout as a folder, in a window of its own. */
  readonly openFolder: (path: string) => Promise<void>;
}

/** One row's actions as markup, for the page's own container. */
export interface RevisionItem {
  readonly id: number;
  readonly html: string;
}

export class RevisionPanel {
  /** What this panel has learned about reaching code, for its lifetime. */
  private memory: RevisionMemory = emptyMemory();

  /** The reads in flight, keyed by the row AND its coordinates, so two presses share one process. */
  private opening: ReadonlyMap<string, Promise<FileAtRead>> = new Map<string, Promise<FileAtRead>>();

  /**
   * The rows whose press is still out.
   *
   * <p>Not something LEARNED about a row — it is what this side is doing to it right now, which is
   * why it is a separate set and why it vanishes with the window.</p>
   */
  private working: ReadonlySet<number> = new Set<number>();

  /** The rows whose CHECKOUT press is still out — its own set, because it is its own action. */
  private checking: ReadonlySet<number> = new Set<number>();

  /**
   * What the last checkout press said, per row.
   *
   * <p>Kept so a redraw says it again: a refusal a person cannot re-read after deciding a row is a
   * refusal they will meet twice. It is a note about a PRESS, not a fact learned about a repository,
   * which is why it lives here rather than in the revision memory.</p>
   */
  private treeNotes: ReadonlyMap<number, string> = new Map<number, string>();

  constructor(
    private readonly hooks: RevisionReach,
    private readonly say: (items: readonly RevisionItem[]) => void,
  ) {}

  /** A closed window remembers nothing: the answers were about a corpus that may have moved on. */
  forget(): void {
    this.memory = emptyMemory();
    this.opening = new Map<string, Promise<FileAtRead>>();
    this.working = new Set<number>();
    this.checking = new Set<number>();
    this.treeNotes = new Map<number, string>();
  }

  /** What each drawn row says about reaching its code. */
  stateFor(pairs: readonly ReviewPair[]): ReadonlyMap<number, RevisionState> {
    return new Map(pairs.map((pair) => [pair.findingId, this.stateOfRow(pair)] as const));
  }

  /**
   * Opens one row's file at the commit the reviewers read.
   *
   * <p>The in-flight state is said BEFORE the first await, because the whole point of it is to be
   * seen while the server and git are running; the `finally` is what makes that safe to say, since
   * every path out replaces it — including the one where the editor refuses the document.</p>
   */
  async openAt(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {
    if (!stateOf(this.memory, pair).offered) {
      return;
    }

    this.working = new Set([...this.working, pair.findingId]);
    this.tell([pair.findingId], rows);
    try {
      const read = heldRevision(this.memory, pair) ?? await this.fileAtOf(pair);
      this.memory = remember(this.memory, pair, read);
      if (read.ok && read.file.reason.length === 0) {
        await this.hooks.showRevision({
          sha: read.file.sha, file: read.file.path, text: read.file.text, line: pair.line,
        });
      }
    } catch (error_: unknown) {
      // A rejection from the EDITOR is not a fact about the revision, so what was read stays read;
      // what the row gains is the sentence and the offer to press again.
      this.memory = rememberCurrent(this.memory, pair, `the editor would not open it: ${asText(error_)}`);
    } finally {
      this.working = new Set([...this.working].filter((one) => one !== pair.findingId));
      this.tell(affectedBy(this.memory, pair, rows), rows);
    }
  }

  /**
   * Opens one row's file as it is NOW — the live filesystem, so it is guarded twice before anything
   * is opened: the recorded checkout inside an open workspace folder, and the file inside the
   * checkout, both as written and as they really lead. No server, no git.
   *
   * <p>A refusal is SAID, on the row, rather than swallowed: a button that quietly does nothing is a
   * button a person presses twice. And it is remembered, so a redraw says it again.</p>
   */
  async openCurrent(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {
    const target = await currentFileIn(this.hooks.folders(), pair.repoPath, pair.file);
    let why = target.ok ? '' : target.why;
    if (target.ok) {
      try {
        await this.hooks.showCurrent(target.path, pair.line);
      } catch (error_: unknown) {
        why = `the editor could not open ${target.path}: ${asText(error_)}`;
      }
    }
    this.memory = rememberCurrent(this.memory, pair, why);
    this.tell([pair.findingId], rows);
  }

  /**
   * Checks one row's commit out into a folder of its own and opens it in a new window.
   *
   * <p>The in-flight state is said BEFORE the first await, as the other two presses do and for a
   * stronger reason: this one can run for a minute, which is exactly long enough for a person to
   * conclude nothing happened and press again. Every ending replaces it in the `finally`.</p>
   *
   * <p>A refusal is SAID and remembered rather than swallowed — including the one a person can act
   * on, the cap. Only an answer that carries a path opens a window.</p>
   */
  async openTree(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {
    if (this.checking.has(pair.findingId)) {
      return;
    }

    this.checking = new Set([...this.checking, pair.findingId]);
    this.noteTree(pair.findingId, '');
    this.tell([pair.findingId], rows);
    try {
      const read = await this.hooks.readTreeAt(
        { findingId: pair.findingId, headSha: pair.headSha, repoPath: pair.repoPath });
      this.noteTree(pair.findingId, await this.openedFrom(read));
    } catch (error_: unknown) {
      this.noteTree(pair.findingId, `the checkout could not be opened: ${asText(error_)}`);
    } finally {
      this.checking = new Set([...this.checking].filter((one) => one !== pair.findingId));
      this.tell([pair.findingId], rows);
    }
  }

  /** Opens what the server made, and answers what the row should say about it. */
  private async openedFrom(read: TreeRead): Promise<string> {
    if (!read.ok) {
      return read.why;
    }
    if (read.tree.reason.length > 0) {
      return treeSentence(read.tree);
    }

    await this.hooks.openFolder(read.tree.path);

    return mountsNote(read.tree);
  }

  private noteTree(id: number, said: string): void {
    this.treeNotes = new Map([...this.treeNotes, [id, said]]);
  }

  /** A row wearing its press, or whatever has been learned about it. */
  private stateOfRow(pair: ReviewPair): RevisionState {
    return this.working.has(pair.findingId) ? WORKING : stateOf(this.memory, pair);
  }

  /** What the row says about checking out — the press if one is out, else what the last one said. */
  private treeOfRow(pair: ReviewPair): TreeState {
    return this.checking.has(pair.findingId)
      ? TREE_WORKING
      : { offered: true, note: this.treeNotes.get(pair.findingId) ?? '' };
  }

  /** Posts what these rows now say — into their own containers, never a redraw. */
  private tell(ids: readonly number[], rows: readonly ReviewPair[]): void {
    this.say(ids.flatMap((id) => {
      const pair = rows.find((one) => one.findingId === id);

      return pair === undefined
        ? []
        : [{ id, html: revisionActions(pair, this.stateOfRow(pair), this.treeOfRow(pair)) }];
    }));
  }

  /**
   * The file for one pair, from the server — one process per row in flight, however many presses.
   *
   * <p>A rejection is turned into a failed read rather than left to surface as an unhandled one.
   * Keyed by the row AND its coordinates, so a recollection under the page cannot be answered by a
   * read about the old commit.</p>
   */
  private fileAtOf(pair: ReviewPair): Promise<FileAtRead> {
    const key = `${pair.findingId}@${pair.headSha}:${pair.file}`;
    const running = this.opening.get(key);
    if (running !== undefined) {
      return running;
    }

    const started = this.hooks
      .readFileAt({ findingId: pair.findingId, headSha: pair.headSha, file: pair.file })
      .catch((error_: unknown): FileAtRead => ({ ok: false, tooOld: false, why: asText(error_) }))
      .finally(() => {
        this.opening = new Map([...this.opening].filter(([held]) => held !== key));
      });
    this.opening = new Map([...this.opening, [key, started]]);

    return started;
  }
}
