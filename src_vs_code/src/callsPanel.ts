import { ReviewPair } from './reviewPair';
import { Calls, CallEnd, methodOf, stillWanted } from './callHierarchy';
import { AskedAbout } from './callHierarchyAsk';
import { callsBlock, CallsState } from './callsBlock';

/**
 * What one review page knows about who calls its methods.
 *
 * <p>Its own object, as `RevisionPanel` is, for the same reason: this is a cluster of state nothing
 * else touches — which rows have a press out, which ATTEMPT each is waiting for, and what the last
 * answer for each row was — and `bugzReviewPanel.ts` is at its line ceiling. It posts through a
 * function the panel supplies rather than holding a webview.</p>
 *
 * <p><b>An attempt, not a generation.</b> A reviewer found the defect precisely: a request can time
 * out, a person can press again inside the same draw, and the first promise can still resolve and
 * overwrite the second answer. Every press mints an attempt, the row records which one it awaits,
 * and an answer is applied only if it is still the awaited one.</p>
 *
 * <p><b>Nothing is reused to avoid a call.</b> The last answer is kept so a redraw does not lose what
 * a person asked for; a press always asks again. That is what keeps a number from surviving a branch
 * switch — a reviewer's case — without this side having to learn what the workspace's HEAD is.</p>
 */

/** One row's answer and how it was reached. */
export interface CallsItem {
  readonly id: number;
  readonly html: string;
}

/** Everything this reaches the world through. */
export interface CallsReach {
  /** Ask the editor. The attempt rides along and comes back on the answer. */
  readonly ask: (about: AskedAbout) => Promise<Calls>;

  /** Open one end of a call, where a person would go to it. */
  readonly open: (end: CallEnd) => Promise<void>;
}

export class CallsPanel {
  /** Which attempt each row is waiting for. A row not here has nothing out. */
  private waiting: ReadonlyMap<number, string> = new Map<number, string>();

  /** The last answer per row, for redrawing — never to avoid asking again. */
  private held: ReadonlyMap<number, Calls> = new Map<number, Calls>();

  private minted = 0;

  constructor(
    private readonly hooks: CallsReach,
    private readonly say: (items: readonly CallsItem[]) => void,
  ) {}

  /** A closed window remembers nothing: the answers were about a checkout that may have moved on. */
  forget(): void {
    this.waiting = new Map<number, string>();
    this.held = new Map<number, Calls>();
  }

  /** What each drawn row says about who calls it. */
  blockFor(pair: ReviewPair): string {
    return callsBlock(pair.findingId, this.stateOf(pair));
  }

  /**
   * One press.
   *
   * <p>The asking state is said BEFORE the first await, because a cold language service takes
   * seconds — measured at 0.8–2.0 s against a warm 37 ms — and a row that looks idle for two seconds
   * is a row a person presses again. Every ending replaces it.</p>
   */
  async ask(pair: ReviewPair, rows: readonly ReviewPair[]): Promise<void> {
    const attempt = `a${++this.minted}`;
    this.waiting = new Map([...this.waiting, [pair.findingId, attempt]]);
    this.tell([pair.findingId], rows);
    try {
      const calls = await this.hooks.ask(about(pair, attempt));
      this.settle(calls);
    } catch {
      // A rejection from the adapter is not an answer about the method; the row goes back to its
      // control and the person can press again.
      this.drop(pair.findingId, attempt);
    } finally {
      this.tell([pair.findingId], rows);
    }
  }

  /**
   * A row was collapsed, so nothing it asked for is wanted any more.
   *
   * <p>The press is forgotten rather than the provider stopped: an extension cannot take a request
   * back out of a language server. What it can do is refuse to apply the answer, which is what
   * `stillWanted` is for.</p>
   *
   * <p><b>And the page is repainted.</b> The block lives in the detail row, which a collapse HIDES
   * rather than removes, so forgetting the answer here while leaving the old markup there would mean
   * re-expanding after a branch switch shows that count under the sentence *in the current
   * checkout* — the one thing this story promises never to say, reached from the other side.
   * (Round 2, two reviewers.)</p>
   */
  closed(ids: readonly number[], rows: readonly ReviewPair[]): void {
    const dropping = new Set(ids);
    this.waiting = new Map([...this.waiting].filter(([id]) => !dropping.has(id)));
    // The ANSWER goes too, so that reopening asks again. (Code round 1, three reviewers.)
    this.held = new Map([...this.held].filter(([id]) => !dropping.has(id)));
    this.tell(ids, rows);
  }

  /** Opens one end of a call that this side is holding — by index, never by a path from the page. */
  async open(id: number, which: 'in' | 'out', at: number): Promise<void> {
    const calls = this.held.get(id);
    if (calls?.prepared !== 'ok') {
      return;
    }

    const ends = which === 'in' ? calls.incoming.ends : calls.outgoing.ends;
    const end = ends[at];
    if (end !== undefined) {
      await this.hooks.open(end);
    }
  }

  private settle(calls: Calls): void {
    if (!stillWanted(calls, this.waiting)) {
      return;
    }

    this.held = new Map([...this.held, [calls.findingId, calls]]);
    this.waiting = new Map([...this.waiting].filter(([id]) => id !== calls.findingId));
  }

  private drop(id: number, attempt: string): void {
    if (this.waiting.get(id) === attempt) {
      this.waiting = new Map([...this.waiting].filter(([one]) => one !== id));
    }
  }

  /**
   * What one row shows — and a held answer is shown only if it is about the method the row is NOW.
   *
   * <p>A `findingId` is a key, not an identity: a collect can reuse it for another finding. Keyed on
   * the id alone this would render the previous method's callers under the new row's name — this
   * story's own worst case, reached through a refresh rather than a provider. (Round 2, coderabbit.)</p>
   */
  private stateOf(pair: ReviewPair): CallsState {
    if (this.waiting.has(pair.findingId)) {
      return { phase: 'asking' };
    }
    const calls = this.held.get(pair.findingId);

    return calls === undefined || calls.about !== methodOf(pair.file, pair.line, pair.symbolName)
      ? { phase: 'unasked' }
      : { phase: 'answered', calls };
  }

  private tell(ids: readonly number[], rows: readonly ReviewPair[]): void {
    this.say(ids.flatMap((id) => {
      const pair = rows.find((one) => one.findingId === id);

      return pair === undefined ? [] : [{ id, html: this.blockFor(pair) }];
    }));
  }
}

/** The three coordinates a row can be asked about, and the attempt that asks. */
function about(pair: ReviewPair, attempt: string): AskedAbout {
  return {
    findingId: pair.findingId,
    attempt,
    file: pair.file,
    line: pair.line,
    symbolName: pair.symbolName,
  };
}
