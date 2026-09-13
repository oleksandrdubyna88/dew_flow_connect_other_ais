import { isPrefixOf } from './chatStore';
import { SaveOutcome } from './chatStoreFile';

/**
 * What a conversation does about the answer its save came back with.
 *
 * <p>Pure, and its own module rather than a branch inside `chatCommand.ts`, for the reason every
 * other decision in this feature has been put where a test can reach it: that file is 2 484 lines
 * against a limit of 800, and a rule buried in wiring is the one thing a reviewer cannot check.</p>
 *
 * <p><b>Nothing here throws a conversation away.</b> The transcript lives in memory on the thread;
 * the disk is where it is kept for tomorrow. So a save that could not land where it was aimed is not
 * a loss — it is a conversation that needs somewhere else to go, and the only question this module
 * answers is where, and what the tab should say about it.</p>
 */

/** What the page says when this tab has become a copy. Exported so the test can read it, once. */
export const CONTINUED_ELSEWHERE =
  'This conversation was continued in another window, so this tab is now a copy of it. '
  + 'Everything above is kept here, and what you ask from now on is saved separately.';

/** What is said when the store was busy with this very conversation and could not compare. */
export const BUSY_ELSEWHERE =
  'This conversation was being written just now, so this change has not been saved yet. '
  + 'It will be saved with the next one.';

/** What follows a disk failure, because the reason alone reads as though the words were lost. */
export const STILL_SAFE =
  'The conversation is still here and still saved where it was; the next change will try again.';

/**
 * What the page says when the record landed and the list did not.
 *
 * <p>About the LIST, never about the conversation: the transcript is on disk and safe, and only the
 * row that finds it again is behind. A sentence saying the conversation was not saved would be the
 * false half, and the false half is the one a person would act on.</p>
 */
export const INDEX_BEHIND =
  'This conversation is saved, but the list that finds it again could not be updated just now. '
  + 'It will catch up the next time the conversation is opened.';

/**
 * The next state of a conversation, given what the store answered.
 *
 * <p>`kept` carries the revision to hold as the next baseline — for a half-commit as much as for a
 * whole one, which is the point of telling them apart: a caller that failed to advance would refuse
 * against its OWN write on the next turn.</p>
 */
export type WriteNext =
  | { readonly kind: 'kept'; readonly rev: number; readonly note?: string }
  /**
   * Our OWN record was already there, at a number — and a beginning — we did not know.
   *
   * <p>`began` is the adopted record's own creation instant. Without carrying it, taking over a
   * conversation started in January and answered in March would record it as having started in
   * March, because this side's best guess is when the memento was last written. (codex.)</p>
   */
  | { readonly kind: 'adopt'; readonly rev: number; readonly began?: number }
  /** Somebody else is in this conversation, or what is on disk cannot be read: write elsewhere. */
  | { readonly kind: 'fork'; readonly note: string }
  /** The disk would not answer. The conversation is unchanged and the next turn tries again. */
  | { readonly kind: 'said'; readonly note: string };

/**
 * What to do about one save.
 *
 * <p><b>A refusal and an unreadable record both fork, and a failure does not.</b> The first two say
 * something about the CONVERSATION at that id — another window owns it, or what is there is not
 * ours to replace — and neither is mended by trying again, so the only way to keep these words is to
 * keep them somewhere else. A disk that would not answer says nothing about the id at all: forking
 * on it would mint one every time a save failed and scatter a single conversation across the store,
 * while the transcript is in memory and the next turn will try the same place again.</p>
 *
 * <p><b>Except where the record on disk is this conversation's own earlier self.</b> A conversation
 * restored into a tab has held its id since it was minted, so the record under that id is very often
 * the session it was restored FROM — and forking there would mint a second copy every time somebody
 * reopened one, for ever. Such a record may be adopted: its revision taken, and the save tried once
 * more against it.</p>
 *
 * <p><b>But never on the strength of the revision alone, which is what the first version did and
 * what A3's plan round refused.</b> Two windows can BOTH be at baseline 0 for one conversation —
 * both restored it, neither has written. The first saves and the disk moves to revision 1; the
 * second is refused, and adoption by number would have it take that revision and write over turns it
 * has never seen. That is the lost update this whole mechanism exists to prevent, re-entering
 * through the door that was meant to make reopening painless.</p>
 *
 * <p>So what is compared is the WORDS. Our own earlier session is CONTAINED in what we hold, because
 * we restored it and then added to it; a disk that has said anything we have not is somebody else,
 * whatever its number. A refusal that reports nothing about the disk — the record was held by
 * another mutation and never read — cannot be compared and is therefore never adopted.</p>
 *
 * <p>An `incompatible` record is never adopted either, at any baseline: adoption means *that is my
 * own record, at a number I did not know*, and a torn one or one a newer build wrote cannot be read,
 * so it cannot be claimed. Taking its revision would be volunteering to overwrite it on the next
 * turn — the downgrade the store refuses on purpose.</p>
 *
 * @param baseline the revision this window last had accepted, 0 before its first
 * @param ours every message this window holds, in order, as text
 */
export function nextAfterSave(
  outcome: SaveOutcome,
  baseline: number,
  ours: readonly string[] = [],
): WriteNext {
  switch (outcome.kind) {
    case 'ok':
      return { kind: 'kept', rev: outcome.rev };
    case 'partial':
      return { kind: 'kept', rev: outcome.rev, note: INDEX_BEHIND };
    case 'busy':
      // The store was mid-mutation of THIS conversation and never probed it, so there is nothing to
      // compare and nothing has been lost. Forking here would mint a copy because a lock was held
      // for a few milliseconds; nothing changes and the next turn tries again.
      //
      // It is its own outcome rather than a refusal carrying no transcript, which is how it was
      // first written and which wedges: a genuine conflict over an ABSENT record reports no
      // transcript either, so reading that as a busy store means waiting for ever and never forking.
      // (local raised the case, gemini found what the shortcut cost.)
      return { kind: 'said', note: BUSY_ELSEWHERE };
    case 'refused':
      // Somebody has been in this conversation since we last wrote. Adoption is the one exception
      // and it is decided by the WORDS, never the revision — see the header.
      return baseline === 0 && outcome.diskRev > 0 && isPrefixOf(outcome.said, ours)
        ? { kind: 'adopt', rev: outcome.diskRev, ...(outcome.began === undefined ? {} : { began: outcome.began }) }
        : { kind: 'fork', note: CONTINUED_ELSEWHERE };
    case 'incompatible':
      // A torn record, or one a newer build wrote. The store refuses to replace it, which is what
      // stops a downgrade destroying a conversation — so this tab needs somewhere else for its own
      // words. Never adopted at any baseline: what cannot be read cannot be claimed.
      return { kind: 'fork', note: CONTINUED_ELSEWHERE };
    default:
      // `failed`. What the disk said, and then what it MEANS for the person: a reason on its own
      // reads as though the conversation had been lost, when it is on screen, in the store of
      // record, and about to be tried again. (codex and gemini, A3's code round.)
      return { kind: 'said', note: `${outcome.reason} ${STILL_SAFE}` };
  }
}

// "Is the disk the beginning of what we hold" is `isPrefixOf` in `chatStore.ts`: the migration of
// story A4 asks the same question the other way round, so the one implementation lives where both
// can import it rather than being copied here a second time.
