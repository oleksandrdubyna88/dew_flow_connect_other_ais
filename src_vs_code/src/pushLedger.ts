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
 *
 * <p><b>It is a stateful service, and it mutates.</b> `coding-style.md` requires immutability of
 * DATA CONTAINERS and says in the same breath to use a class for stateful services; this is the
 * latter, and the state it keeps is the whole point of it. (codex, the code round, read the rule as
 * covering this too.)</p>
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
 *
 * <p>The PAGE is what the generation cannot do: a rebuild resets what is newest, so a push still in
 * flight when the page was replaced would otherwise be recorded against its successor — telling the
 * panel the new page holds what the old one was sent. (The code round.)</p>
 */
export interface Push {
  readonly region: Region;
  readonly content: string;
  readonly generation: number;
  readonly page: number;
}

export class PushLedger {
  private readonly delivered = new Map<Region, string>();
  private readonly newest = new Map<Region, number>();
  private generation = 0;
  private page = 0;

  /**
   * Whether the page's script has said it is listening.
   *
   * <p>False until it does, and false again every time the page is rebuilt. This is the half the
   * bookkeeping alone cannot cover: `postMessage` answers TRUE for a webview that exists, and a
   * webview exists for the whole moment between `webview.html = …` and its script attaching a
   * listener. A message sent in that window is accepted by VS Code and delivered to nobody.</p>
   */
  private listening = false;

  /**
   * Whether a `true` from `postMessage` may be BELIEVED.
   *
   * <p>Only a page that said `ready` earns that. The panel also pushes at a page that never said it —
   * an empty tab for ever is worse than a repaint — but it must not then record those pushes as
   * delivered, because `postMessage` answers true for a webview that merely exists. Recording them
   * would stop the retry and reproduce the exact defect this class was written for, which is what
   * both remote vendors raised across three roles on the code round.</p>
   */
  private trusted = false;

  get isListening(): boolean {
    return this.listening;
  }

  /** Whether what the page is told is being recorded, or only sent in hope. */
  get isTrusted(): boolean {
    return this.trusted;
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
    this.trusted = false;
    this.page += 1;
    this.delivered.clear();
    this.newest.clear();
  }

  /** The page says its listeners are attached, so what it is told may be recorded. */
  ready(): void {
    this.listening = true;
    this.trusted = true;
  }

  /**
   * Nobody said `ready`, and waiting longer helps nobody: push, but believe nothing.
   *
   * <p>A page whose script threw before attaching its listener never says the word, and gating every
   * push on it would leave that page empty for ever. So the panel sends anyway — and keeps sending,
   * every tick, until something acknowledges. The page has its own deadline for saying so on
   * screen.</p>
   */
  assumeListening(): void {
    this.listening = true;
    this.trusted = false;
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

    return { region, content, generation: this.generation, page: this.page };
  }

  /**
   * A push came back. Recorded ONLY when it arrived, and only when nothing newer has.
   *
   * <p>`false` — or a throw, which the caller turns into `false` — leaves the region unrecorded, so
   * the next tick sends it again. That is the whole cure for the reported defect: an undelivered
   * push must not look like a delivered one.</p>
   */
  settle(push: Push, arrived: boolean): void {
    if (!arrived || !this.trusted || push.page !== this.page) {
      return;
    }
    if (push.generation < this.newestFor(push.region)) {
      return;
    }

    this.delivered.set(push.region, push.content);
    this.newest.set(push.region, push.generation);
  }

  private newestFor(region: Region): number {
    return this.newest.get(region) ?? 0;
  }
}
