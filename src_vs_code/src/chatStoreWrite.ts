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
  /** Our OWN record was already there at a number we did not know. Take it and save again. */
  | { readonly kind: 'adopt'; readonly rev: number }
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
 * <p><b>Except on the very first save, where a refusal is usually not a rival at all.</b> A
 * conversation restored into a tab has held its id since it was minted, so the record under that id
 * is its own previous session — and this window, having written nothing yet, has nothing of its own
 * to lose by taking the number the disk reports and saving against it. Forking there would mint a
 * second copy of a conversation every time somebody reopened one, for ever. `baseline` is what tells
 * the two cases apart, and it is the only thing that can: 0 means this window has never had a save
 * accepted, and after one has, a disk that has moved on has no innocent explanation left.</p>
 *
 * <p>An `incompatible` record is never adopted, at any baseline. Adoption means *that is my own
 * record, at a number I did not know*; a torn one or one a newer build wrote cannot be read, so it
 * cannot be claimed, and taking its revision would be volunteering to overwrite it on the next turn
 * — the downgrade the store refuses on purpose.</p>
 *
 * @param baseline the revision this window last had accepted, 0 before its first
 */
export function nextAfterSave(outcome: SaveOutcome, baseline: number): WriteNext {
  switch (outcome.kind) {
    case 'ok':
      return { kind: 'kept', rev: outcome.rev };
    case 'partial':
      return { kind: 'kept', rev: outcome.rev, note: INDEX_BEHIND };
    case 'refused':
      return baseline === 0 && outcome.diskRev > 0
        ? { kind: 'adopt', rev: outcome.diskRev }
        : { kind: 'fork', note: CONTINUED_ELSEWHERE };
    case 'incompatible':
      return { kind: 'fork', note: CONTINUED_ELSEWHERE };
    default:
      return { kind: 'said', note: outcome.reason };
  }
}
