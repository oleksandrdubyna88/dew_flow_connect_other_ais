import { ReviewPair } from './bugzReviewPage';
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
import { revisionActions, RevisionState, WORKING } from './revisionActions';
import { AskedRevision } from './roundsDbRead';

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

  constructor(
    private readonly hooks: RevisionReach,
    private readonly say: (items: readonly RevisionItem[]) => void,
  ) {}

  /** A closed window remembers nothing: the answers were about a corpus that may have moved on. */
  forget(): void {
    this.memory = emptyMemory();
    this.opening = new Map<string, Promise<FileAtRead>>();
    this.working = new Set<number>();
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

  /** A row wearing its press, or whatever has been learned about it. */
  private stateOfRow(pair: ReviewPair): RevisionState {
    return this.working.has(pair.findingId) ? WORKING : stateOf(this.memory, pair);
  }

  /** Posts what these rows now say — into their own containers, never a redraw. */
  private tell(ids: readonly number[], rows: readonly ReviewPair[]): void {
    this.say(ids.flatMap((id) => {
      const pair = rows.find((one) => one.findingId === id);

      return pair === undefined ? [] : [{ id, html: revisionActions(pair, this.stateOfRow(pair)) }];
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
