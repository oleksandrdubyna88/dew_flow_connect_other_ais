import * as vscode from 'vscode';
import { DbLog, EMPTY_LOG } from './roundsDb';
import {
  Found,
  FoundRound,
  keysFileIn,
  MAX_LIMIT,
  readFindings,
  readLog,
  readManyFindings,
  RoundKey,
  serverRun,
} from './roundsDbRead';
import { serverPath } from './installer';

/**
 * What the rounds log page reads, and the few seconds it is allowed to remember.
 *
 * <p><b>The second extraction from `panelProvider.ts`</b>, per
 * `research/PLAN_the_panel_provider_is_too_big.md`. Two fields and four methods, and the seam is one
 * thing: `context.globalStorageUri`, which every one of them turns into a server path. Nothing else
 * in the class touches `roundsLogCache` or `roundsLogAt`.</p>
 *
 * <p><b>It differs from the first extraction in one way worth naming.</b> The Claude probe's methods
 * were private and had a single caller inside `render`; these four are PUBLIC and are called from
 * `extension.ts`. So the panel keeps four one-line delegations rather than losing the methods — the
 * plan's own rule for this case — and `extension.ts` is untouched. The comments came here with the
 * bodies, because they are the part that records why each decision is what it is.</p>
 *
 * <p>Names are unchanged, for the reason the first extraction measured: `scripts/prove-move.mjs`
 * matches whole lines, so a renamed field or collaborator turns a moved line into residue somebody
 * has to check by hand.</p>
 */
export class RoundsLogCache {
  private roundsLogCache: DbLog = EMPTY_LOG;

  private roundsLogAt = 0;

  constructor(private readonly context: vscode.ExtensionContext) {}

  /**
   * Forget the cached log, because something it describes has just moved.
   *
   * <p>The cache exists so a page refreshing every tick does not spawn a process every tick. That is
   * right for a page ticking on its own and wrong for a real change: a consultation ending is the
   * last watcher event there will be, so a cache entry written a second earlier would have kept the
   * old rows on screen until somebody clicked something. The watcher clears it and asks again.
   * (CodeRabbit, on the pull request.)</p>
   */
  forgetRoundsLog(): void {
    this.roundsLogAt = 0;
  }

  async roundsLog(): Promise<DbLog> {
    // Cached for a few seconds, because the log page refreshes every tick while a round runs and
    // each read is a process spawn plus a walk of the whole findings table. The gate called that
    // out twice: a hot path is not where a child process belongs. A few seconds is shorter than any
    // round and longer than any burst of ticks.
    const AGE_MS = 10_000;
    if (Date.now() - this.roundsLogAt < AGE_MS) {
      return this.roundsLogCache;
    }
    const server = serverPath(this.context.globalStorageUri);
    this.roundsLogAt = Date.now();
    this.roundsLogCache = server === undefined
      ? EMPTY_LOG
      // The whole window rather than one page: this list is what gives every row its decision
      // counts, and the page paginates the rows it already holds. See MAX_LIMIT for the arithmetic.
      : await readLog(server.fsPath, { limit: MAX_LIMIT });

    return this.roundsLogCache;
  }

  /**
   * What ONE round found, read when somebody opens its row.
   *
   * <p>Not cached, and not on the tick: this is a read a person asked for by clicking, and it is
   * small — one round's findings against the 3.78 MB the list used to carry for every round whether
   * or not anybody looked. A machine with no server installed answers `failed`, which the row draws
   * as a retry rather than as "this round found nothing".</p>
   */
  async roundFindings(sessionId: string, stage: string, number: number): Promise<Found> {
    const server = serverPath(this.context.globalStorageUri);

    return server === undefined
      ? { state: 'failed', findings: [] }
      : readFindings(server.fsPath, { sessionId, stage, number });
  }

  /**
   * The findings of a whole SELECTION, in one spawn.
   *
   * <p>What a bulk export asks. The per-round call above is what a person opening one row asks, and
   * the two stay separate because they answer different questions: one row wants the answer now, a
   * selection wants five hundred answers without five hundred processes.</p>
   */
  async roundFindingsMany(keys: readonly RoundKey[], stop?: () => boolean): Promise<readonly FoundRound[]> {
    const server = serverPath(this.context.globalStorageUri);
    if (server === undefined) {
      return keys.map((key) => ({ key, found: { state: 'failed' as const, findings: [] } }));
    }

    // `stop` reaches the CHILD, which is the whole point: one process answers for the whole
    // selection, so a cancel that only stopped listening would leave it reading the database while
    // the person who cancelled watched nothing happen. It is handed to the per-round fallback too.
    return readManyFindings(
      server.fsPath,
      keys,
      keysFileIn(this.context.globalStorageUri.fsPath),
      // Through `serverRun`, not `capture` directly: that is the door that carries the data
      // directory this window chose, and a batch read that skipped it would export the rounds of a
      // different directory from the one the list beside it is showing.
      serverRun(server.fsPath, stop),
      readFindings,
      stop);
  }
}
