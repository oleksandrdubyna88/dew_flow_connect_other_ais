import * as vscode from 'vscode';
import { ChatEntry } from './chatPanels';
import { Thread, threads } from './chatThread';
import { memory } from './chatHost';
import { keepQueued } from './chatPersist';
import { AnsweredBy } from './chatPage';
import { chatSettingsFrom } from './chatSettings';
import { LanguageCode } from './settingsShape';
import { reaskFrom, retryFrom } from './chatPresets';
import { savedModels, savedPrompts, taskOf, vendorFor } from './chatConfig';
import { serviceLines } from './chatPrompt';
import { spendLabel, spendSoFar } from './chatSpend';
import { remoteIsFull } from './remoteAsk';
import { pushChatState } from './chatPanel';

/**
 * What the page is told, and the five small facts every caller of it needs first.
 *
 * <p>Extracted from `chatCommand.ts` unchanged, and extracted before the archive, the turn, the
 * launch and the hooks because every one of them calls `show`. Left where it was, each of those
 * would have imported it back out of the command file.</p>
 *
 * <p>`asText` is here for the same reason rather than a better one: a thrown thing becomes a
 * sentence, and every place that sentence goes is this page.</p>
 */

/**
 * Push the page's whole visible state, with the thread's own model list rather than an empty one.
 *
 * <p>A thread that is GONE pushes nothing. That is the guard for a tab closed while a turn was still
 * in flight: the answer can arrive up to a poll later, and posting into a webview VS Code has torn
 * down throws out of a callback nobody is catching. Forgetting the thread on close is what makes
 * every reader of it — this one included — a no-op afterwards.</p>
 *
 * @param queued how many turns are ahead of this one on a Team server; 0 for none, and for a local
 *   model, which has no queue
 */
export function show(entry: ChatEntry, running: boolean, failure: string, queued = 0): void {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  const config = vscode.workspace.getConfiguration('coai');
  pushChatState(entry, {
    messages: thread.messages,
    carryFrom: thread.carryFrom,
    running,
    // Built in epic 2 and set for the first time here: a remote conversation stops at three turns
    // and the page offers the local model that has a memory instead.
    capped: thread.forgetful && remoteIsFull(thread.asked),
    failure,
    models: thread.models,
    providers: thread.providers,
    providerId: thread.providerId,
    modelId: thread.modelId,
    // The two lists and the prompt in force, so the rows of buttons can be redrawn with the right
    // one pressed — read HERE rather than held on the thread, because the person edits them in
    // another tab and a copy taken when the conversation opened would be the list as it was then.
    promptId: thread.promptId,
    // The button the PERSON pressed, which is ahead of the session while a switch is still running.
    chosenModelId: thread.chosenId,
    promptPresets: savedPrompts(config),
    modelPresets: savedModels(config),
    // Which words are which, so the box and the transcript above it draw the same turn the same
    // way. The ROLE comes from the thread rather than from the model in force: a queued switch holds
    // the two apart for as long as an answer lasts, and what is marked has to be what is THERE.
    marks: {
      role: thread.role,
      task: taskOf(config, thread.promptId, chatSettingsFrom((key) => config.get(key)).prompt),
      service: serviceLines(chatLanguage()),
    },
    queued,
    // The turn a stop would name. Zero while nothing runs, which is also what the page renders no
    // control for — a stop that names no turn is refused by the seam rather than obeyed loosely.
    turn: running ? (thread?.turn ?? 0) : 0,
    // Who would answer a re-ask. Empty while a turn runs: the button is locked anyway, and offering
    // to re-ask something that is still being answered is offering a question nobody asked yet.
    reask: running || thread === undefined ? '' : reaskLabel(thread),
    // Whether the failure on screen still has its question behind it. False while a turn runs, for
    // the reason the re-ask above is empty then: the composer is locked, and sending again something
    // that is still being answered is a second turn down a pipe that carries one.
    //
    // AND only when something actually failed. The builder already draws nothing without a failure,
    // so this changes no pixel — but it keeps the invariant in one place instead of leaving it to
    // emerge from two, which is what a later reader of the flag would trip over. (gemini, the plan
    // round.)
    // AND under the pair it failed under. Switch model after a failure and the retry is withdrawn
    // rather than silently redirected: a retry is the same question to the same model, and a re-ask
    // is the same question to a different one — which is already the Send button's other face.
    canRetry: running || failure.length === 0
      ? false
      : thread.failedWith === pairOf(thread) && retryFrom(thread.messages) !== undefined,
    attached: thread?.attached ?? '',
    spend: spendLabel(spendSoFar(thread?.spend ?? [])),
  });
  // The one place the transcript reaches a page is the one place it is written down — but only when
  // there is something new to write. `show` runs on every state push: a turn starting, a queue
  // position moving, a failure clearing. Each of those used to rewrite up to twenty whole
  // transcripts into one key, which is a lot of JSON on the extension host for a page that said the
  // same words. The comparison is by REFERENCE, because `thread.messages` is replaced rather than
  // mutated, so it is exact and costs nothing. (gemini, the code round.)
  // The MARK is compared too. Pressing Carry nothing above changes neither the transcript nor the
  // model, so a comparison of those two alone skipped the write — and the rule the person had just
  // drawn would not have survived a reload, silently.
  if (thread.savedMessages === thread.messages
    && thread.savedModelId === thread.modelId
    && thread.savedCarryFrom === thread.carryFrom) {
    return;
  }
  thread.savedMessages = thread.messages;
  thread.savedModelId = thread.modelId;
  thread.savedCarryFrom = thread.carryFrom;
  // AND WHEN. Below the guard, so it records that something CHANGED rather than that a page redrew —
  // the picker orders its Open section by this, and a conversation nobody has spoken in must not
  // climb to the top of it because its tab repainted. See `Thread.usedAt`.
  thread.usedAt = Date.now();
  // The MEMENTO, until this window's migration has confirmed every record is on disk and
  // `retireMemento` has unbound it — a no-op from then on. It is kept this long because a store that
  // cannot be reached must not leave the person's next words written nowhere (A4's plan round).
  memory?.remember({
    id: thread.saveId,
    title: thread.title,
    passage: thread.passage,
    modelId: thread.modelId,
    messages: thread.messages,
    fromSession: thread.fromSession,
    carryFrom: thread.carryFrom,
  });
  // AND to the store on disk, which is the source of truth: the reload serializer reads it, and the
  // memento above is a fallback for as long as it holds anything. Detached on purpose — nobody waits
  // for a disk to see their own words — and therefore ending in a catch of its own, which
  // `reliability.md` requires of every edge nothing is above.
  // CHAINED, not fired. Two writes issued before the first answers would both carry the revision
  // this window last had accepted, so the second would be refused — and a refusal reads as another
  // window, so a tab would fork itself and say it had become a copy of a conversation nobody else
  // was in. The chain also recovers from a rejection instead of staying poisoned, which is the
  // bargain `chatTabs.ts` already makes for the memento's queue.
  keepQueued(entry, thread);
}

/** The answer language, read fresh: a follow-up turn is asked long after the command ran. */
export function chatLanguage(): LanguageCode {
  return chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key)).language;
}

/** A thrown thing, as a sentence. */
export function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

/**
 * The pair a turn would go to NOW, as `provider/model` — the key a retry is matched against.
 *
 * <p>One function because two callers must agree exactly: the failing branch writes it down and the
 * button is drawn from it. An empty `modelId` means "whatever the row is set to", which is what the
 * row itself says — the same fallback the ledger uses, and for the same reason.</p>
 */
export function pairOf(thread: Thread): string {
  const answering = vendorFor(thread.providerId);

  return `${thread.providerId}/${thread.modelId.length > 0 ? thread.modelId : (answering?.model ?? '')}`;
}

/**
 * The name of the model that a re-ask would go to, or empty when there is nothing to re-ask.
 *
 * <p>The LABEL rather than the id, because it goes on a button a person reads. `reaskFrom` decides
 * whether there is anything to re-ask at all; this only names who would take it.</p>
 */
export function reaskLabel(thread: Thread): string {
  return reaskFrom(thread.messages, thread.providerId) === undefined ? '' : answeredBy(thread).label;
}

/**
 * Which model a turn was answered by, as the page will caption it.
 *
 * <p>The label is the one the picker offered, so the caption reads as the name a person chose from
 * rather than an id. A model that is not in the list any more — a Team server that withdrew it — is
 * still named by its id: what answered is a fact about the past, and the page's job is to say it.</p>
 */
export function answeredBy(thread: Thread): AnsweredBy {
  const chosen = thread.models.find((model) => model.id === thread.modelId);
  // An empty label is not a label. `??` keeps one, and the caption would then fall through to
  // "The other AI" for a model whose id was known all along. (gemini, the code round.)
  const named = chosen?.label.trim() ?? '';

  return { id: thread.modelId, label: named.length > 0 ? named : thread.modelId };
}
