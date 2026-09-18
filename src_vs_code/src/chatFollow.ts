import { ChatStoreFile } from './chatStoreFile';
import { ConversationIndex } from './chatStoreCache';
import { ConversationSource, sourceOfFile } from './chatStore';
import { ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { store } from './chatHost';
import { keepQueued } from './chatPersist';
import { NAMES_ARE_CASE_BLIND, asUri, filedFor, fsPathOf, reorigin } from './chatRoots';
import { heldConversationIds } from './chatRegistry';
import { Moved, followable, movedTo, prepareMoves } from './chatSource';
import { abreast } from './abreast';
import { followReport } from './followReport';
import { notify } from './notify';

/**
 * A conversation follows the file it was opened from.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. A rename arrives from the editor with an explicit
 * old-to-new mapping, and every conversation filed under the old uri is rewritten to the new one —
 * a LIVE one through its thread and its write queue, a closed one through the store, never both.</p>
 *
 * <p>A module of its own rather than part of the registry beside it because they share nothing:
 * this one writes, and the registry only reads.</p>
 */

/**
 * A file has MOVED: follow every conversation that was opened from it.
 *
 * <p>Two halves, and they must not both write the same record. A conversation this window holds
 * OPEN is followed on its thread and written through the conversation's own queue — the thread is
 * the authority for a live record, and its chain is what keeps two writes from both carrying the
 * revision this window last accepted. Everything else is followed on DISK, through the store's
 * `refile`, which reads and replaces inside one claim; the ids this window holds are skipped there,
 * or the two halves would race for one conversation.</p>
 *
 * <p><b>Both facts move together</b> — the uri and the root it now belongs to. A file dragged from
 * one workspace root into another changes both, and rewriting the uri alone would leave the
 * conversation filed under the root it left, invisible in exactly the folder the person is looking
 * at. (Two vendors, the plan round.)</p>
 *
 * <p>An UNTITLED buffer that is saved is not followed at all, and `chatSource.ts` says why: the
 * editor reports no previous uri for it, so there is nothing to match a conversation against and a
 * listener would attach one to the wrong file as readily as to the right one.</p>
 *
 * <p>Detached, and therefore ending in a catch that says something: a rename must not wait on a
 * disk, and nothing above this is listening.</p>
 */
export function followRenames(panels: ChatPanels, index: ConversationIndex, renames: readonly Moved[]): void {
  if (renames.length === 0) {
    return;
  }
  // Normalised ONCE, before anything is asked about them: a folder refactor reports many moves, and
  // the old paths were being put into comparable form again for every conversation in the store.
  const moves = prepareMoves(renames, NAMES_ARE_CASE_BLIND);
  for (const { key } of panels.known()) {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (entry === undefined || thread === undefined || !followable(thread.source)) {
      continue;
    }
    const moved = movedTo(thread.source.uri, moves, asUri, fsPathOf);
    if (moved.length === 0) {
      continue;
    }
    reorigin(thread, sourceOfFile(moved));
    keepQueued(entry, thread);
  }
  const onDisk = store;
  if (onDisk === undefined) {
    return;
  }
  void (async () => {
    // COUNTED, so the person hears ONE sentence about a folder refactor rather than one per
    // conversation. Every failure on this path used to reach the console and nowhere else.
    let refreshFailed = false;
    // WHICH CONVERSATIONS THIS RENAME REACHES, decided before any of them is touched. Asked NOW
    // rather than from a set taken earlier: a conversation that closed while this ran is no longer
    // followed by its thread, and a stale snapshot would have its rename followed by neither half.
    // (local, the code round; it was my own open question.)
    // WHICH RECORDS THIS RENAME EVEN TOUCHES, and where each one went — worked out once, because the
    // narrowing that makes `meta.source.uri` readable lives in this branch and the walk over every
    // rename is the expensive half. What is NOT decided here is whether a conversation is open: that
    // is asked inside the job, at the moment its record is reached.
    const mine = [...index.entries({ kind: 'everywhere' })].flatMap((meta) => {
      if (!followable(meta.source)) {
        return [];
      }
      const moved = movedTo(meta.source.uri, moves, asUri, fsPathOf);

      return moved.length === 0 ? [] : [{ id: meta.id, was: meta.source, now: sourceOfFile(moved) }];
    });
    // A FEW AT A TIME rather than strictly one after another, through the pool this repository
    // already has. MEASURED against a real store on 2026-09-17: one refile is 5.76 ms, so ten
    // thousand of them sequentially is about 58 SECONDS — not the hours the plan estimated, which
    // assumed every record hitting the five-try retry path. A minute of a stale picker is still a
    // minute, and eight abreast is the cheap end of it. Different conversations take different
    // locks, so the width costs nothing in contention.
    const done = await abreast(mine.map((one) => async (): Promise<'followed' | 'couldNot' | 'live'> => {
      try {
        // ASKED NOW, not from a set taken before any awaiting began — and hoisting it out of here is
        // exactly the regression `chatSourceWiring` refuses. A conversation that CLOSED while this
        // ran is no longer followed by its thread, so a stale snapshot would have its rename
        // followed by neither half. (local, the code round; caught again by that test on
        // 2026-09-17 when this loop was made concurrent.)
        if (heldConversationIds(panels).includes(one.id)) {
          return 'live';
        }

        return await follow(onDisk, one.id, one.was, one.now) ? 'followed' : 'couldNot';
      } catch (reason) {
        // PER UNIT, as `reliability.md` requires of a loop over independent things: one conversation
        // that cannot be followed must not stop every other conversation following the same rename.
        console.error(`ConnectOtherAIs: a conversation threw while following a renamed file: ${one.id}`, reason);

        return 'couldNot';
      }
    }), AT_A_TIME);
    // A conversation that is OPEN is not a failure and not a move: its thread followed the rename
    // above, through the queue, and counting it either way would make the sentence wrong.
    const touched = done.filter((one) => one === 'followed').length;
    const couldNot = done.filter((one) => one === 'couldNot').length;
    if (touched > 0) {
      // The picker reads the INDEX, not the disk. Without this the rows go on naming the file they
      // left and sitting in the folder they left — and a second rename would compare against that
      // stale source and follow nothing. (gemini, the code round, three times.)
      //
      // ITS OWN GUARD since 2026-09-17. It was inside the outer catch, which only logs — and this is
      // the one failure on this path where the DATA is right and the SCREEN is wrong: the records
      // moved, the list that finds them did not, and every row on it is then a name that no longer
      // exists. That is not a console matter.
      try {
        await index.refresh();
      } catch (reason) {
        refreshFailed = true;
        console.error('ConnectOtherAIs: the conversation index could not be refreshed after a rename', reason);
      }
    }
    // SAID, at last. Both of this path's failures were console-only: a conversation that could not
    // be refiled, and a list that could not be re-read after some were. One sentence, chosen by
    // `followReport`, so a folder refactor is not a wall of notifications.
    const report = followReport({ moved: touched, couldNot, refreshFailed });
    if (report !== undefined) {
      void notify({
        as: report.severity,
        class: 'outcome',
        source: 'chat',
        code: 'rename-not-followed',
        title: report.title,
      });
    }
  })().catch((reason: unknown) => {
    console.error('ConnectOtherAIs: following a renamed file threw', reason);
  });
}

/**
 * How many conversations follow a rename at once.
 *
 * <p>Small on purpose. The retries below exist for a store that is already contended, so a wide fan
 * would be this window competing with itself for locks it is about to wait on. Eight is the cheap end
 * of the measurement: one refile is 5.76 ms against a real store, so the sequential worst case for
 * ten thousand conversations is about a minute rather than the hours the plan estimated.</p>
 */
const AT_A_TIME = 8;

/** How many times a conversation held by somebody else is asked again before the rename is given up on. */
const FOLLOW_TRIES = 5;

/** How long between those asks. Short: a save is milliseconds, and nothing is waiting on this. */
const FOLLOW_WAIT_MS = 200;

/**
 * Move one closed conversation to where its file went, waiting out an ordinary concurrent save.
 *
 * <p>A `busy` claim is the commonest outcome there is — another window writing a turn — and treating
 * it as final loses the rename FOR EVER, because a rename happens once and is not replayed. Four
 * reviewers said so independently. So it is asked again a few times, and only a lock that never
 * clears is reported.</p>
 */
export async function follow(onDisk: ChatStoreFile, id: string, was: ConversationSource, next: ConversationSource): Promise<boolean> {
  // The answer is "does the index need re-reading", NOT "did I write it". They come apart when
  // another window followed the same rename first: nothing was written here and the rows in memory
  // are stale all the same.
  for (let tries = 0; tries < FOLLOW_TRIES; tries += 1) {
    const done = await onDisk.refile(id, was, next, filedFor(next));
    if (done.kind === 'followed') {
      return true;
    }
    if (done.kind === 'unindexed') {
      // The conversation moved; the row that lists it did not. Reading the record repairs that entry
      // under the conversation's own lock — which is what `read` already does whenever it finds the
      // index stale — so the index has something true to refresh from afterwards.
      await onDisk.read(id);
      console.warn(`ConnectOtherAIs: a conversation followed a renamed file but its index entry did not: ${id} — ${done.reason}`);

      return true;
    }
    if (done.kind === 'kept') {
      if (done.why !== 'busy') {
        // Somebody else followed it first, or it is gone. Neither is a failure and neither is ours to
        // report — but the DISK has moved under our index either way, so it still counts as touched:
        // if every match came back like this the index would never be refreshed, and the picker would
        // go on naming the file the conversation left while a later rename compared against that
        // stale source. (CodeRabbit, on the pull request.)
        return true;
      }
    } else if (done.kind === 'failed') {
      console.warn(`ConnectOtherAIs: a conversation could not follow a renamed file: ${id} — ${done.reason}`);

      return false;
    } else {
      // EXHAUSTIVE BY NAME: a future outcome must decide here rather than falling through into the
      // busy retry, which is what a chain of ifs would have let it do. (codex, the code round.)
      const unhandled: never = done;

      throw new Error(
        `a refile answer this build has no arm for: ${JSON.stringify(unhandled)}`
        + ' — the answers it may give are followed, unindexed, kept and failed',
      );
    }
    await new Promise<void>((resolve) => {
      setTimeout(resolve, FOLLOW_WAIT_MS);
    });
  }
  // SAID. The conversation keeps the path it had, so *go to* will not find it by the file's new
  // name — and the person is not interrupted, because there is nothing they can do about another
  // window and the conversation is still in the picker.
  console.warn(`ConnectOtherAIs: a conversation was busy in another window and could not follow a renamed file: ${id}`);

  return false;
}
