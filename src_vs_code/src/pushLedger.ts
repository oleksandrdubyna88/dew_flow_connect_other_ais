/**
 * What a webview has actually been told, as opposed to what was sent at it.
 *
 * <p>No `vscode` import, because these are decisions rather than API calls — and a decision inside
 * `RoundsLogPanel` is a decision no test in this suite can reach.</p>
 *
 * <p><b>The failure it exists for.</b> 2026-09-08: the rounds log showed no accepted/rejected counts
 * and an empty *What it keeps missing* tab. Nothing was wrong with the data — the binary emitted
 * 207 rounds, 24 blind spots and 205 decisions in 143 ms, the parser read all of it, and
 * `blindSpotsHtml` built 2968 characters from it. What went wrong is that the page is painted
 * WITHOUT the database on purpose (reading it spawns a process, and nobody should wait on that to
 * see their log), the push that fills it in was sent blind with `void postMessage`, and the panel
 * recorded that push as delivered before making it. Every later tick then compared against that
 * record, found nothing changed, and sent nothing. One lost message, permanent.</p>
 */

/** One region of the page, and the last thing it is known to have received. */
export type Region = 'rows' | 'usage' | 'spots';

/**
 * A push that has been decided on but not yet confirmed.
 *
 * <p>The GENERATION is what stops an older push overwriting a newer one's record. Two pushes can be
 * in flight — an ordinary tick and the forced answer to `ready` — and the older can resolve second;
 * committing it then tells the panel the page holds something it does not. Raised by two reviewers
 * on the plan round, from opposite directions.</p>
 */
export interface Push {
  readonly region: Region;
  readonly content: string;
  readonly generation: number;
}

export class PushLedger {
  private readonly delivered = new Map<Region, string>();
  private generation = 0;

  /**
   * Whether the page's script has said it is listening.
   *
   * <p>False until it does, and false again every time the page is rebuilt. This is the half the
   * bookkeeping alone cannot cover: `postMessage` answers TRUE for a webview that exists, and a
   * webview exists for the whole moment between `webview.html = …` and its script attaching a
   * listener. A message sent in that window is accepted by VS Code and delivered to nobody.</p>
   */
  private listening = false;

  get isListening(): boolean {
    return this.listening;
  }

  /**
   * The page was rebuilt: nothing it was ever told still holds.
   *
   * <p>Called for a first paint and for every rebuild VS Code performs on its own — a hidden tab
   * whose context was not retained comes back empty, and a panel that believed otherwise would
   * leave it that way.</p>
   */
  rebuilt(): void {
    this.listening = false;
    this.delivered.clear();
  }

  /** The page says its listeners are attached. */
  ready(): void {
    this.listening = true;
  }

  /**
   * The push to make for one region, or nothing when the page already has that content.
   *
   * <p>`force` says "send it even if the content is unchanged" — what a `ready` answers with,
   * because a page that has just started listening has been told nothing at all.</p>
   */
  next(region: Region, content: string, force = false): Push | undefined {
    if (!this.listening) {
      return undefined;
    }

    if (!force && this.delivered.get(region) === content) {
      return undefined;
    }

    this.generation += 1;

    return { region, content, generation: this.generation };
  }

  /**
   * A push came back. Recorded ONLY when it arrived, and only when nothing newer has.
   *
   * <p>`false` — or a throw, which the caller turns into `false` — leaves the region unrecorded, so
   * the next tick sends it again. That is the whole cure for the reported defect: an undelivered
   * push must not look like a delivered one.</p>
   */
  settle(push: Push, arrived: boolean): void {
    if (!arrived || push.generation < this.newestFor(push.region)) {
      return;
    }

    this.delivered.set(push.region, push.content);
    this.newest.set(push.region, push.generation);
  }

  private readonly newest = new Map<Region, number>();

  private newestFor(region: Region): number {
    return this.newest.get(region) ?? 0;
  }
}
