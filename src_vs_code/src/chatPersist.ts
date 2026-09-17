import { randomUUID } from 'node:crypto';
import { ChatEntry } from './chatPanels';
import { Thread } from './chatThread';
import { memory, pulse, store } from './chatHost';
import { CONVERSATION_VERSION, ConversationRecord } from './chatStore';
import { CONTINUED_ELSEWHERE, WriteNext, nextAfterSave } from './chatStoreWrite';
import { pushChatNote } from './chatPanel';

/**
 * How a conversation reaches the disk, and what its answer does to the tab.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. One module because it is one rule: ONE queue per
 * conversation, and everything that writes goes through it. Two writes issued before the first
 * answers both carry the revision this window last accepted, so the second is refused — and a
 * refusal reads as another window, which is a tab forking itself and telling the person it has
 * become a copy of a conversation nobody else is in.</p>
 *
 * <p>What a refusal MEANS is `chatStoreWrite.ts`, pure and tested as values. What is here is the
 * disk and the page.</p>
 */

/**
 * Queue a write of this conversation to the store, behind whatever it is already writing.
 *
 * <p>Extracted from `show` because story C1 gave the store a SECOND writer: a Claude session's id
 * arrives from a background walk, long after the page last changed, and `show`'s dedupe guard
 * compares messages, model and mark — so a source landing on its own would never have reached disk
 * through that path. Both writers go through this one queue, which is what keeps two writes from
 * both carrying the revision this window last accepted and the second being read as another
 * window.</p>
 */
export function keepQueued(entry: ChatEntry, thread: Thread): void {
  const step = (): Promise<void> => keepOnDisk(entry, thread).catch((reason: unknown) => {
    // The outer edge of a detached call, and therefore a catch that SAYS something: the store
    // answers in outcomes and never rejects, so anything arriving here is a defect rather than a
    // disk, and the page is told as well as the console.
    console.error('ConnectOtherAIs: a conversation could not be written to the store', reason);
    pushChatNote(entry, thread.saveId, 'This conversation could not be written to disk just now.');
  });
  thread.writes = thread.writes.then(step, step);
}

/**
 * The conversation as the store keeps it — built here, because only this side knows a thread.
 *
 * <p>`rev` is what the store stamps, so the value put in is the one it will replace; `source` is
 * `none` until story C1 teaches a conversation what it was opened from, and `none` matches no tab,
 * which is the honest answer while nothing knows better.</p>
 */
export function recordOf(thread: Thread, at = Date.now()): ConversationRecord {
  return {
    version: CONVERSATION_VERSION,
    rev: thread.rev,
    id: thread.saveId,
    title: thread.title,
    passage: thread.passage,
    modelId: thread.modelId,
    messages: thread.messages,
    fromSession: thread.fromSession,
    carryFrom: thread.carryFrom,
    source: thread.source,
    workspace: thread.workspace,
    // WHEN IT BEGAN, not when it was last written. The two were the same instant here until A3's
    // plan round; a conversation answered three months after it started was recorded as having
    // started that day, and the picker draws its "started" from this field.
    createdAt: thread.createdAt,
    updatedAt: at,
  };
}

/**
 * Write this conversation to the store, and do what its answer says.
 *
 * <p>The store's swap can refuse, and what a refusal MEANS is `chatStoreWrite.ts` — pure, and tested
 * as values. What is left here is the disk and the page. The rule worth carrying in your head while
 * reading it: nothing below can lose a conversation, because the transcript is on the thread and the
 * disk is only where it is kept for tomorrow.</p>
 */
async function keepOnDisk(entry: ChatEntry, thread: Thread): Promise<void> {
  if (store === undefined) {
    return;
  }
  // MAPPED ONLY WHEN IT IS ASKED FOR. The words are needed by one branch of one outcome — the
  // containment check on a refusal — and `show` runs on every push that changes anything. Building
  // an array of every message's text before knowing whether a conflict even happened is an
  // allocation per turn for a comparison that almost never runs. (gemini and local, the code round.)
  const ours = (): readonly string[] => thread.messages.map((message) => message.text);
  const next = nextAfterSave(await store.save(recordOf(thread), thread.rev), thread.rev, ours());
  if (next.kind === 'adopt') {
    // Our own record, from a session before this window ever wrote. Take the number the disk reports
    // and save once more against it; a SECOND refusal has no innocent reading left and forks.
    thread.rev = next.rev;
    // AND WHEN IT BEGAN. The adopted record is this conversation's own earlier session, so its
    // creation instant is the true one; without taking it, a conversation started in January and
    // answered in March would be rewritten as having started in March, and the picker draws its
    // "started" from that field. (codex, the code round.)
    if (next.began !== undefined) {
      thread.createdAt = next.began;
    }
    await settle(entry, thread, nextAfterSave(await store.save(recordOf(thread), thread.rev), thread.rev, ours()));

    return;
  }
  await settle(entry, thread, next);
}

/** This conversation's words, for the one comparison that asks for them. */
const ours0 = (thread: Thread): readonly string[] => thread.messages.map((message) => message.text);

/** What one answer does to the thread and to the page. Never called with `adopt`, which is a retry. */
async function settle(entry: ChatEntry, thread: Thread, next: WriteNext): Promise<void> {
  if (next.kind === 'kept') {
    thread.rev = next.rev;
    if (next.note !== undefined) {
      // A half-commit, said once and only where it matters: the transcript is safe on disk and the
      // row that finds it again is behind, which the next read of it repairs.
      pushChatNote(entry, thread.saveId, next.note);
    }

    return;
  }
  if (next.kind === 'said') {
    pushChatNote(entry, thread.saveId, next.note);

    return;
  }
  await forkOnDisk(entry, thread);
}

/**
 * This conversation belongs to another window now; keep ours under a new id.
 *
 * <p>Every message stays — they are on the thread, and the record written below carries all of them
 * — so what a person loses is nothing, and what they gain is a tab that says which of the two
 * windows they are looking at.</p>
 *
 * <p><b>It does not go back through `settle`, and that is deliberate.</b> It used to, which made the
 * two functions mutually recursive: a fork whose own save was refused would fork again, and again,
 * with nothing bounding it. It also pushed the settled note and then immediately overwrote it with
 * its own. A forked id is freshly minted and nobody else holds it, so the only answers its save can
 * give are that it landed or that the disk would not take it — both handled here, in one place, with
 * ONE sentence reaching the page. (gemini, A3's second code round.)</p>
 */
async function forkOnDisk(entry: ChatEntry, thread: Thread): Promise<void> {
  thread.saveId = randomUUID();
  thread.rev = 0;
  // The dedupe marks go first: this is the same transcript under a different name, and the guard in
  // `show` would otherwise decide nothing had changed and skip writing it anywhere.
  delete thread.savedMessages;
  delete thread.savedModelId;
  delete thread.savedCarryFrom;
  // THE MEMENTO FIRST, and WAITED FOR — while it is still bound. Written the other way round, a crash
  // in between leaves the fork on disk under an id the memento has never heard of: while the memento
  // is a fallback the tab could reload as the original it no longer owns, and the copy holding the
  // person's words would be orphaned from both the reload and the migration meant to carry it
  // across. Three reviewers from two vendors asked for the order, and codex for the wait —
  // `remember` queues rather than writes, so without it the two are only in invocation order and the
  // crash window stays open. Once `retireMemento` has run this is a no-op and the fork rests on the
  // store's own swap, which is what the page's `setState` of the new id reloads against.
  memory?.remember({
    id: thread.saveId,
    title: thread.title,
    passage: thread.passage,
    modelId: thread.modelId,
    messages: thread.messages,
    fromSession: thread.fromSession,
    carryFrom: thread.carryFrom,
  });
  await memory?.settled();

  // THE SENTENCE, and the new id with it: the page hands that id back to the serializer after a
  // reload, so a tab that forked and was then reloaded comes back as the copy rather than as the
  // original. A save that then failed adds its own sentence to this one rather than replacing it —
  // the fork is the fact that matters, and the disk is a detail underneath it.
  let note = CONTINUED_ELSEWHERE;
  if (store !== undefined) {
    const next = nextAfterSave(await store.save(recordOf(thread), 0), 0, ours0(thread));
    if (next.kind === 'kept') {
      thread.rev = next.rev;
      note = next.note === undefined ? note : `${note} ${next.note}`;
    } else if (next.kind === 'said') {
      // Not swallowed, which is the house rule: a fork whose own save failed used to say nothing.
      note = `${note} ${next.note}`;
    }
    // `fork` and `adopt` cannot arrive: the id was minted a moment ago, so nothing is there to
    // conflict with and nothing is there to adopt.
  }
  pushChatNote(entry, thread.saveId, note);
  // The heartbeat names ids, and this conversation has a new one.
  pulse?.();
}
