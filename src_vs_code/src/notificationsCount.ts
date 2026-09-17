/**
 * What the panel says about the notification ledgers, in one line.
 *
 * <p>Pure, and its own module rather than another two hundred lines inside `panelView.ts` — which
 * is 2795 lines against the 800 the shared rule allows and has a plan of its own waiting to be
 * split. A new section is a chance to stop making that worse.</p>
 *
 * <h2>Three states that must not collapse into each other</h2>
 *
 * <p>On the Bugz precedent (`bugzView.ts`, `lastRunLine`), which keeps a distinct sentence for a
 * corpus that has not been read: <b>"nothing has happened yet", "this could not be read" and "you
 * are up to date" are three different facts</b>, and two of them render as zero if nobody is
 * careful. A broken read showing a clean `0` is how a person learns to stop trusting a count — and
 * this count exists because somebody missed one message.</p>
 */

/** What one glance at the ledgers found. */
export interface LedgerGlance {
  /**
   * Whether the ledgers could be read at all.
   *
   * <p>False is a permission error, a directory that has gone away, a disk that stopped answering
   * — never a missing file, which is an ordinary first run.</p>
   */
  readonly readable: boolean;
  /** Whether either ledger holds anything. A missing file is not a failure; it is a new install. */
  readonly anyRecords: boolean;
  /** New since the acknowledged watermark, capped. */
  readonly unread: number;
  /** Whether the count stopped at its cap rather than at the watermark. */
  readonly more: boolean;
  /** Whether records below an acknowledged range are still unacknowledged. */
  readonly older: boolean;
}

/** Nothing has been looked at yet — the state before the first glance lands. */
export const NOT_LOOKED: LedgerGlance = {
  readable: true,
  anyRecords: false,
  unread: 0,
  more: false,
  older: false,
};

/**
 * Nothing could be looked at.
 *
 * <p>Never a zero. It is a separate constant rather than a literal in each place that answers it,
 * because the whole feature turns on this state being distinguishable from "up to date", and two
 * copies of it are two chances for one of them to grow an `unread: 0, readable: true`.</p>
 */
export const UNREADABLE_GLANCE: LedgerGlance = {
  readable: false,
  anyRecords: false,
  unread: 0,
  more: false,
  older: false,
};

/** How many new records the badge may name before it stops counting and says "+". */
export const COUNT_CAP = 3000;

/**
 * How long the panel waits for a glance before it says the ledgers could not be read.
 *
 * <p>Two watcher ticks. Nothing cancels a filesystem read, so this is not a cancellation — it is
 * how long a person waits before being TOLD, rather than being shown a count that quietly stopped
 * moving. The caller keeps its single-flight until the read really settles, so a hung share costs
 * one pending read rather than one per tick. (local, the second S5 code round.)</p>
 */
export const GLANCE_CEILING_MS = 10_000;

function plural(count: number, one: string, many: string): string {
  return count === 1 ? one : many;
}

/**
 * The sentence under *Notifications*.
 *
 * <p>Every branch is a different fact, and none of them is a number standing in for a sentence.
 * "3000+ new" is deliberate where a bare 3000 would be a lie: past the cap the count stopped
 * walking, and a figure it would cost a megabyte of parsing to earn is not worth more than the
 * plus sign that replaces it.</p>
 */
export function countSentence(glance: LedgerGlance): string {
  if (!glance.readable) {
    // NOT "0 new". The Bugz section keeps its own words for this for the same reason, and its test
    // asserts the phrase is absent from the rest of the page so that one state cannot be mistaken
    // for the other.
    return 'The notifications could not be read.';
  }
  if (!glance.anyRecords) {
    return 'Nothing has been written down yet.';
  }
  if (glance.unread === 0) {
    return glance.older
      ? 'Nothing new. Some older ones have not been opened.'
      : 'Nothing new.';
  }

  const how = glance.more ? `${glance.unread}+` : String(glance.unread);
  const said = `${how} new ${plural(glance.unread, 'notification', 'notifications')}.`;

  return glance.older ? `${said} Some older ones have not been opened either.` : said;
}

/**
 * What the button under that sentence says.
 *
 * <p>It opens the page whatever the state, including when nothing has been written yet: a person
 * who wants to know where the ledger IS should not have to make something go wrong first.</p>
 */
export function openLabel(glance: LedgerGlance): string {
  return glance.unread > 0 ? 'Read them' : 'Open notifications';
}
