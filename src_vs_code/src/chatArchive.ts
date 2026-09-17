import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { ChatEntry } from './chatPanels';
import { Thread, threads } from './chatThread';
import { store } from './chatHost';
import { recordOf } from './chatPersist';
import { asText, show } from './chatShow';
import { ChatSession } from './chatSession';
import { SaveOutcome } from './chatStoreFile';
import { ARCHIVED, Freshened, UNSAVED, couldNotEnd, freshened } from './chatFresh';
import { reloadedNote } from './chatTabs';
import { carriedFrom } from './chatCarry';
import { pushChatFresh } from './chatPanel';
import { drained } from './chatQueue';
import { pushChatDraft } from './chatPanel';

/**
 * New chat: ending the conversation a tab holds, filing it, and opening a clean one in its place.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. It is all-or-nothing by construction, and the order
 * is the feature: the generation is bumped BEFORE anything is stopped so a question queued behind
 * the running answer never begins; the ending is stop, wait for the turn chain, drain the disk
 * queue, dispose, release; the old record is archived only once the ending succeeded and BEFORE the
 * new id is published; and a reset that failed archives nothing and leaves the conversation not
 * merely live but usable.</p>
 *
 * <p>What the slate IS lives in `chatFresh.ts`, pure and tested as values — including the
 * compile-time partition that makes a per-conversation field added later and forgotten a build
 * error rather than something that silently survives into the new conversation.</p>
 */

/**
 * A conversation with no process behind it, which answers rather than throws.
 *
 * <p>Written once because it is wanted twice: a tab restored after a reload, and a tab somebody has
 * just reset. Both are a conversation whose words are all here and whose process is gone, and both
 * open one on the NEXT question rather than now — so a window of five restored tabs, and a tab
 * somebody resets and never speaks to again, spawn nothing at all. The page can never reach this:
 * `reopened` replaces it before the first turn is sent. It answers with a sentence because a
 * `ChatSession` that rejects is a contract this codebase does not have.</p>
 */
export function closedSession(note: string): ChatSession {
  return {
    send: () => Promise.resolve({ ok: false, failure: note }),
    stop: () => undefined,
    dispose: () => undefined,
  };
}

/**
 * Every field of a thread a reset must LEAVE ALONE.
 *
 * <p>Its only job is the check below, and that check is the answer to a real objection: `Partial<Thread>`
 * proves every field of {@link Freshened} is a thread field of the right type, but it cannot prove the
 * other direction — a per-conversation field added to `Thread` and forgotten here would simply survive
 * the reset, carrying the old conversation into the new one with nothing to say so. (codex, both code
 * rounds.)</p>
 */
type KeptByAReset =
  // The tab is still the conversation OF that tab, and a reset is a new subject rather than a new setup.
  | 'title' | 'modelId' | 'providerId' | 'promptId' | 'role' | 'chosenId' | 'presses' | 'models' | 'providers'
  // It still belongs to the same tab in the same project.
  | 'source' | 'workspace' | 'fromSession' | 'sessionFile'
  // Never reset anywhere: a stop names the turn it means, and a late one must not name a turn of the
  // new conversation. `generation` and `resetting` belong to the reset itself rather than to a slate.
  | 'turn' | 'generation' | 'resetting'
  // Queues rather than contents.
  | 'turns' | 'writes'
  // Replaced by the host, which owns them: a dead session and a released directory are not values.
  | 'session' | 'home'
  // Deleted rather than assigned — see `UNSAVED` — and decided by whichever model answers next.
  | 'savedMessages' | 'savedModelId' | 'savedCarryFrom' | 'forgetful' | 'ourDraft';

/** Any field of a thread that a reset neither replaces nor has been told to keep. Must be none. */
type Unclassified = Exclude<keyof Thread, keyof Freshened | KeptByAReset>;

/**
 * THE PARTITION, checked by the compiler rather than by hand.
 *
 * <p>When every field of `Thread` is either replaced by a reset or named in {@link KeptByAReset},
 * `Unclassified` is `never` and this is a `true` that compiles. Add a field to `Thread` and classify
 * it as neither, and the type becomes that field's own name — which `true` is not assignable to, so
 * the build stops and names the field. It is exported so that nothing prunes it as unused.</p>
 */
export const RESET_DECIDES_EVERY_THREAD_FIELD: Unclassified extends never ? true : Unclassified = true;

/**
 * *New chat* — the clean slate, behind the button that has always been there.
 *
 * <p>`chatPage.ts` has rendered *Start a new conversation* in the capped notice since the cap
 * existed, and the hook it posts to did nothing. This is what it does.</p>
 *
 * <p><b>Nothing is switched until the old conversation is provably finished.</b> The order below is
 * the part the plan round spent most of itself on, and every step of it answers a way of getting a
 * question or an answer into the wrong conversation.</p>
 */
export async function freshStart(entry: ChatEntry): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined || thread.resetting) {
    return;
  }
  thread.resetting = true;
  try {
    await freshening(entry, thread);
  } finally {
    thread.resetting = false;
  }
}

/** The reset itself, with the latch held. */
export async function freshening(entry: ChatEntry, thread: Thread): Promise<void> {
  // 0. THE WORDS COME BACK FIRST, before the slate moves and takes their questions with it. Three
  // reviewers objected to losing typed words from three directions, and this codebase already has
  // the answer: they go back to the composer, which is where they were typed. All of them, oldest
  // first — a count on its own is not a recovery. The page APPENDS each one below what is there,
  // which is `pushChatDraft`'s own rule and not a second copy of it here. (issue #288.)
  for (const unasked of drained(thread.waiting)) {
    pushChatDraft(entry, unasked);
  }
  thread.waiting = [];
  // 1. THE SLATE MOVES FIRST, before anything is stopped. A question queued behind the answer that
  // is running would otherwise begin a whole new turn after the stop — the reset would wait it out,
  // and it would append what somebody typed into a transcript that is about to be replaced.
  thread.generation += 1;
  // 2. ENDED, THEN ARCHIVED, both inside the one notification: archiving a thousand-turn transcript
  // is a serialisation and a disk write, and a progress that stopped before it would leave the
  // longest part of the wait looking like nothing happening. (codex, the code round.)
  const done = await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Window, title: 'Ending the previous conversation…' },
    async (): Promise<Archived> => {
      const failed = await ended(thread);

      return failed.length > 0 ? { kind: 'refused', reason: failed } : archiveConversation(thread);
    },
  );
  if (done.kind === 'refused') {
    // 3. AND IF EITHER FAILED, NOTHING HAPPENS — including a failure to ARCHIVE, which the first
    // draft let through: it said the conversation was archived, wiped the slate, and left the old
    // record open on disk. Five findings from two vendors, all naming the same gap. All or nothing
    // means all of it.
    keepTheOldOne(entry, thread, done.reason);

    return;
  }
  await publish(entry, thread, done.note);
}

/**
 * Nothing was archived and nothing was cleared: leave the conversation not merely live but USABLE.
 *
 * <p>A half-performed reset that reports success is the one outcome worse than no reset. "Live" is
 * not enough on its own either: by this point the session may have been disposed, so a conversation
 * left pointing at it is one the next question cannot use. It takes the dead stub and `reopen`, and
 * the next question opens it a process exactly as a reload does — with its transcript carried, or
 * the model that answers is handed nothing. (codex, the plan round.)</p>
 */
export function keepTheOldOne(entry: ChatEntry, thread: Thread, reason: string): void {
  thread.session = closedSession(reloadedNote(thread.modelId));
  thread.carry = carriedFrom(thread.messages, thread.carryFrom);
  thread.reopen = true;
  thread.running = false;
  show(entry, false, couldNotEnd(reason));
}

/**
 * The new slate — written to disk BEFORE its id is published to the page.
 *
 * <p>The order is the last of this story's, and it is the same rule as the archive's. The page hands
 * its id back to the serializer after a reload, so publishing an id whose record is still queued
 * would leave a crash in between restoring a tab that names a conversation nothing has ever written.
 * Waiting costs one write of an empty record. A crash BEFORE the publish leaves the tab on the id it
 * already had — an archived conversation, which is still there and still opens: the reset did not
 * happen, the honest worst case. (codex, the code round.)</p>
 */
export async function publish(entry: ChatEntry, thread: Thread, note: string): Promise<void> {
  // AS ONE VALUE, and typed as part of a thread on the way in — so a field of `Freshened` that is
  // not a field of `Thread`, or is one of another type, is a compile error here rather than a silent
  // property nothing reads. (local, the code round: `Object.assign` alone checks neither.)
  const slate: Partial<Thread> = freshened(randomUUID(), Date.now());
  Object.assign(thread, slate);
  // THE DEAD STUB ON THE SUCCESS PATH TOO. `reopen` says the next QUESTION opens a process, but
  // between here and that question the thread is still reachable — a stop, a switch, a tab closing —
  // and every one of those would reach the session this reset has just disposed. (gemini, the code
  // round; `closedSession`'s own contract said it was for this and the success path did not use it.)
  thread.session = closedSession(reloadedNote(thread.modelId));
  // And the marks that say what the disk holds, DELETED rather than emptied: the push below reads
  // "never written" from their absence, and would otherwise compare the new empty transcript against
  // the old one, decide nothing had changed, and never write the new record at all.
  for (const mark of UNSAVED) {
    delete thread[mark];
  }
  // THE SENTENCE TRAVELS WITH THE STATE, and that was a real defect: it used to be written straight
  // into the page's line and then wiped by this very push a tick later, because a state message is
  // the whole truth about every region it mentions and this one said the line was empty. Nobody ever
  // saw it. (gemini, twice, the code round.)
  show(entry, false, note.length > 0 ? note : ARCHIVED);
  await thread.writes;
  if (thread.rev === 0) {
    // DRAINED IS NOT SAVED. The write chain is built so it cannot reject — that is what makes
    // awaiting it safe — so a save that failed still lets the drain above resolve. `rev` is the disk's
    // own answer: zero until the store has accepted this record, non-zero from the moment it has. The
    // id is NOT published, so the tab stays on the one it had: an archived conversation, whole and
    // still opening, which is a far better thing to reload into than a name nothing was ever written
    // under. (codex and gemini, the second code round.)
    show(entry, false, 'This new conversation could not be written to disk, so it will not survive a reload. The previous one was archived and is safe.');

    return;
  }
  pushChatFresh(entry, thread.saveId);
}

/**
 * End the conversation that is running: stop the turn, wait for the chain, dispose, release.
 *
 * <p>The disposal and the release are INDEPENDENT of the stop and of each other, and run whatever
 * happened before them. A session that would not stop must still be disposed, or a CLI outlives the
 * reset and keeps writing into a directory nothing will collect; and a directory that will not go is
 * one the sweep takes later, never a reason to refuse a reset. Written the other way round — one try
 * for all four — a stop that threw returned before either cleanup, and the thread was then handed a
 * stub with the real session unreferenced and still running. (codex, the code round.)</p>
 *
 * @returns an empty string when the conversation is over, or what stopped it
 */
export async function ended(thread: Thread): Promise<string> {
  let stopped = '';
  try {
    // The turn in flight, ended — and then WAITED FOR, so its answer has landed in the OLD
    // transcript and been written down before anything is cleared. A stop that merely stopped
    // waiting would let that answer arrive into a conversation that had already been archived.
    thread.session.stop();
    // The CHAIN rather than the turn, because a queued question is on it too — and it is the
    // generation bumped before the stop, not a bound on this wait, that stops such a question
    // beginning. The turn itself is already bounded: `CliChatSession` settles the one in flight by
    // any of four routes, its own 180-second budget among them.
    await thread.turns;
    // AND THE DISK QUEUE with it. Writes are chained the way turns are, and the archive saves
    // against `thread.rev` — draining first is what makes that the revision the disk actually holds
    // rather than one a push still in the queue is about to move on. Neither chain can reject: both
    // are built with a handler on the failure side, which is what makes awaiting them safe here.
    await thread.writes;
  } catch (reason) {
    stopped = asText(reason);
  }
  try {
    // Safe to call twice, so a session already gone stays gone.
    thread.session.dispose();
  } catch (reason) {
    console.warn('ConnectOtherAIs: a chat session would not be disposed on a reset', reason);
  }
  try {
    // AFTER the disposal, never before it: a CLI writing on its way out into a directory that has
    // already been removed throws where nobody is listening.
    thread.home.release();
  } catch (reason) {
    console.warn('ConnectOtherAIs: a chat temp directory could not be released after a reset', reason);
  }

  return stopped;
}

/** Whether the old conversation was filed, and anything worth saying about how. */
type Archived =
  | { readonly kind: 'filed'; readonly note: string }
  | { readonly kind: 'refused'; readonly reason: string };

/**
 * Stamp the old conversation closed, so it is archived rather than lost.
 *
 * <p><b>A refusal stops the reset.</b> The first draft said a sentence and carried on, which left the
 * old record open on disk while the page said it had been archived, and the new slate in place over
 * it — the all-or-nothing rule broken at the one step that decides where a person's conversation
 * went. A `partial` is not a refusal: the record itself landed and only the row that finds it again
 * is behind, which the next read of that record repairs.</p>
 */
export async function archiveConversation(thread: Thread): Promise<Archived> {
  if (thread.messages.length === 0) {
    // NOTHING SAID IN IT, nothing to keep: a tab reset before anybody typed would otherwise leave an
    // empty conversation in Recent for every press. Asked BEFORE the store, because a conversation
    // with nothing in it needs no store to be archived correctly.
    return { kind: 'filed', note: '' };
  }
  if (store === undefined) {
    // NO STORE AND SOMETHING TO KEEP. Reading that as a successful archive would wipe a conversation
    // with nowhere for it to have gone — "archived, never deleted" broken in the one case where the
    // deletion is total. (codex, the second code round.)
    return { kind: 'refused', reason: 'this window has nowhere to keep conversations' };
  }
  try {
    const closed = await store.save({ ...recordOf(thread), closedAt: Date.now() }, thread.rev);
    if (closed.kind === 'ok') {
      return { kind: 'filed', note: '' };
    }
    if (closed.kind === 'partial') {
      return { kind: 'filed', note: 'The previous conversation was archived, and the list may take a moment to show it.' };
    }

    return { kind: 'refused', reason: whyNotClosed(closed) };
  } catch (reason) {
    // A store that THREW rather than answered. It answers in outcomes and does not reject, so this
    // is a defect somewhere below — and a defect must not take the conversation with it.
    return { kind: 'refused', reason: asText(reason) };
  }
}

/** Why the store would not close it, in words a person can act on. */
export function whyNotClosed(closed: Exclude<SaveOutcome, { kind: 'ok' } | { kind: 'partial' }>): string {
  switch (closed.kind) {
    case 'busy':
      return 'another window is writing to it';
    case 'refused':
      return 'another window has moved it on';
    case 'incompatible':
    case 'failed':
      return closed.reason;
    default: {
      // Exhaustive by name, as every decision in this feature is: a sixth way for a save to end must
      // be a compile error here rather than a reset that refuses without saying why.
      const unhandled: never = closed;

      throw new Error(`a save outcome this build has no arm for: ${JSON.stringify(unhandled)}`);
    }
  }
}
