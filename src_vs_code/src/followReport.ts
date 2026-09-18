/**
 * What a person is told after a rename has been followed — and it used to be nothing at all.
 *
 * <p><b>The defect.</b> When a file or folder is renamed, every conversation filed under it is
 * refiled and the picker's index is refreshed. Every failure on that path went to the console and
 * nowhere else: a conversation that could not be refiled, and an `index.refresh()` that threw after
 * records HAD been refiled. The second is the worse one — the data is then correct and the surface is
 * not, so the picker goes on offering names that have moved, and the person has no way to know that
 * what they are looking at is stale.</p>
 *
 * <p><b>What measuring it changed.</b> The plan said a permission failure was retried five times and
 * reported exactly as a lock is. It is not: `chatStoreLock` returns `false` only on `EEXIST` and
 * throws for anything else, so a permission error becomes `failed` and `follow` gives up at once
 * without retrying. The classification this story was written to add ALREADY EXISTS, one layer down.
 * What is missing is not the distinction — it is that neither outcome is ever said out loud. So the
 * two stories that pointed here are one change: the follow path tells somebody.</p>
 *
 * <p><b>ONE sentence, not one per conversation.</b> A folder refactor moves many conversations at
 * once; a notification each would be a wall of them for a single gesture. The counts are gathered and
 * said once, which is also why this is a value rather than a call at the failure site.</p>
 */

/** What a run of the follower actually did. */
export type FollowCounts = {
  /** Conversations that followed their file, or were already where they belong. */
  readonly moved: number;
  /** Conversations that could not be refiled — busy elsewhere, or a disk that refused. */
  readonly couldNot: number;
  /** Whether the picker's index could not be re-read AFTER records had been refiled. */
  readonly refreshFailed: boolean;
};

/** What the person is told about a folder rename this window tried to follow. */
export type FollowNotice = {
  readonly severity: 'information' | 'warning';
  readonly title: string;
};

/**
 * The sentence, or nothing when there is nothing worth interrupting anybody for.
 *
 * <p>Silence is the common case and it stays silent: a rename that worked is a rename nobody needs
 * to be told about. What breaks that silence is only ever a state on screen that is now WRONG.</p>
 */
export function followReport(counts: FollowCounts): FollowNotice | undefined {
  const conversations = (n: number): string => (n === 1 ? '1 conversation' : `${n} conversations`);

  if (counts.refreshFailed) {
    // THE LOUDEST, because it is the one where the data is right and the screen is wrong. The records
    // moved; the list that finds them did not. Anything a person does from that list is done against
    // names that no longer exist, and nothing else on screen says so.
    return {
      severity: 'warning',
      title: counts.moved > 0
        ? `${conversations(counts.moved)} followed a renamed file, but the list that finds them could not be`
          + ' updated. Reopen the conversation list before using it — what it shows is out of date.'
        : 'The list of conversations could not be updated after a rename. Reopen it before using it —'
          + ' what it shows is out of date.',
    };
  }
  if (counts.couldNot > 0) {
    // A conversation is not LOST by failing to follow — it keeps the path it had, is still in the
    // picker, and still opens. What a person loses is finding it by the file's NEW name, which is
    // exactly what the sentence says rather than implying something was destroyed.
    return {
      severity: 'warning',
      title: `${conversations(counts.couldNot)} could not be moved to follow a renamed file, so`
        + ' *go to* will not find them under the new name. They are still in the conversation list'
        + ' under the old one.',
    };
  }

  return undefined;
}
