import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { pulse } from './chatHost';
import { closedSession } from './chatArchive';
import { conversationHooks } from './chatHooks';
import { pinSession } from './chatSessionJoin';
import { LegacyPick } from './chatModels';
import { Ready, readyToChat, savedModels, savedPick, savedPrompts } from './chatConfig';
import { ModelPreset, PromptPreset, mainPrompt } from './chatPresets';
import { ChatPageState } from './chatPage';
import { ConversationRecord } from './chatStore';
import { reloadedNote } from './chatTabs';
import { carriedFrom, carryMark } from './chatCarry';
import { serviceLines } from './chatPrompt';
import { chatLanguage } from './chatShow';
import { chatTextTone, chatUiScale, createChatPanel } from './chatPanel';

/**
 * How a conversation comes back into a panel VS Code has just restored, or one the picker built.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. NO process is started: the one behind the tab died
 * with the window, a restored tab may never be spoken to again, and starting one per tab at every
 * reload would spawn a CLI for each of them for nothing. The first question opens the session and
 * hands it the whole transcript, which is the handover a model switch already ships.</p>
 */

/**
 * Bring one conversation back into a panel VS Code has just restored.
 *
 * <p>No process is started. The transcript is rendered, the composer works, and the session is opened
 * by the first question — see `reopened`. The panel is built by `createChatPanel` like every other
 * one, so the icon, the message wiring, the zoom hook and the disposal are the same code and cannot
 * drift apart.</p>
 */
/**
 * The page a restored conversation opens with.
 *
 * <p>Its own function because `restoreConversation` had grown long enough that the guard watching it
 * — `the first question after a restore carries the whole transcript`, which reads the first 2500
 * characters for the carry — was measuring distance rather than ordering. Shortening a comment to
 * stay inside a window is how a guard stops being about what it guards; taking twenty lines out of
 * the middle is how it goes on being about it.</p>
 */
function restoredPage(
  saved: ConversationRecord,
  ready: Ready,
  restored: LegacyPick,
  presets: { readonly promptPresets: readonly PromptPreset[]; readonly modelPresets: readonly ModelPreset[] },
): ChatPageState {
  return {
      id: saved.id,
      title: saved.title,
      passage: saved.passage,
      messages: saved.messages,
      models: ready.ok ? ready.models : [],
      providers: ready.ok ? ready.providers : [],
      ...presets,
      reask: '',
      // A restored tab shows no failure — the failure line is live state and is not saved with the
      // conversation — so it has nothing to offer a retry of until the next turn fails.
      canRetry: false,
      attached: '',
      spend: '',
      // The row this tab was speaking to, read by `savedPick` out of the old `modelId`.
      providerId: ready.ok ? ready.providerId : restored.providerId,
      chosenModelId: ready.ok ? ready.providerId : restored.providerId,
      modelId: saved.modelId,
      // A restored tab starts on the MAIN prompt, like a new one: the button that was pressed lived
      // in a conversation whose process is gone, and the main one is what this list says to use when
      // nobody has pressed anything.
      promptId: mainPrompt(presets.promptPresets)?.id ?? '',
      running: false,
      capped: false,
      // Nothing is in flight on a page that has just opened, so there is no turn to stop.
      turn: 0,
      failure: ready.ok ? reloadedNote(saved.modelId) : ready.refusal,
      draft: '',
      fromSession: saved.fromSession,
      asked: [],
      carryFrom: carryMark(saved.carryFrom, saved.messages.length),
      marks: {
        role: presets.modelPresets.find((one) => one.id === (ready.ok ? ready.providerId : restored.providerId))?.startingPrompt ?? '',
        task: mainPrompt(presets.promptPresets)?.text ?? '',
        service: serviceLines(chatLanguage()),
      },
      uiScale: chatUiScale(),
      textTone: chatTextTone(),
    };
}

/**
 * @param panel the panel VS Code handed back after a reload — and `undefined` for a caller that has
 *   none. The picker is the first of those: it reopens a conversation nobody reloaded, so there is no
 *   panel to fill and one is CREATED instead. It is created inside `createChatPanel` rather than here,
 *   because the icon, the message wiring, the disposal and the zoom hook are all set up in that one
 *   function and a second place that made a chat panel would have to remember every one of them
 *   forever. Optional as a value rather than as a trailing parameter so that the serializer's call —
 *   which is pinned by `chatRestore.test.ts` — reads exactly as it did.
 * @param saved the record as the store holds it — or, while the memento still holds anything, the
 *   memento's copy mapped through `fromLegacy` with `rev` 0, which is what says "no disk revision
 *   known" to the swap and lets the write decision adopt the store's copy on the first save
 * @returns the entry, for a caller that has to reveal the tab it has just had built
 */
export function restoreConversation(
  panels: ChatPanels,
  panel: vscode.WebviewPanel | undefined,
  saved: ConversationRecord,
  extensionUri: vscode.Uri,
  /**
   * The tab this conversation is being bound TO, for story C3's *go to* — absent for the reload
   * serializer and for the picker, which both give a restored conversation its own key.
   *
   * <p>The tab is checked for a conversation BEFORE anything is built. `panels.open` would reveal
   * what is there and not call the factory, but the panel would already exist by then: a webview
   * created for a tab that turned out to have one, with nobody to close it. The check and the
   * registration happen with no `await` between them, which on a single-threaded host is what makes
   * this one decision rather than a check and then an act.</p>
   */
  bindTo?: { readonly key: object; readonly label: string },
): ChatEntry {
  const already = bindTo === undefined ? undefined : panels.get(bindTo.key);
  if (already !== undefined) {
    // The tab holds a conversation. Nothing is built and nothing is swapped: replacing what is in a
    // live tab under somebody is worse than leaving them where they are. (gemini, the plan round.)
    already.panel.reveal();

    return already;
  }
  const config = vscode.workspace.getConfiguration('coai');
  const restored = savedPick(config, saved.modelId);
  const presets = { promptPresets: savedPrompts(config), modelPresets: savedModels(config) };
  const ready = readyToChat(config, restored.providerId, restored.modelId);
  // ONE array for the transcript and for "what the store already holds", so the first push after a
  // reload — which redraws exactly what was read — writes nothing. A reload is not a use: a write
  // here would stamp a new `updatedAt` and put a conversation nobody spoke in at the top of the
  // picker's Recent. The guard in `show` compares by reference, which is why they must be the same
  // object and not two copies of one.
  const messages = [...saved.messages];
  const closed = closedSession(reloadedNote(saved.modelId));
  const entry = createChatPanel(
    restoredPage(saved, ready, restored, presets),
    closed,
    conversationHooks(panels),
    extensionUri,
    panel,
  );
  threads.set(entry.id, {
    session: closed,
    home: { dir: '', release: () => undefined },
    passage: saved.passage,
    models: ready.ok ? ready.models : [],
    providers: ready.ok ? ready.providers : [],
    attached: '',
    attachedPath: '',
    spend: [],
    // CARRIED, never recomputed. The record knows which tab it came from and which root it was filed
    // under; working either out again from this window would file a conversation restored in a
    // second window under that window's first root instead of its own.
    source: saved.source,
    workspace: saved.workspace,
    providerId: ready.ok ? ready.providerId : restored.providerId,
    modelId: saved.modelId,
    // The MAIN prompt and the restored model's own role: a reloaded tab shows the same pressed
    // buttons a new one does, because the list is the configuration and a reload changes no part
    // of it.
    promptId: mainPrompt(presets.promptPresets)?.id ?? '',
    role: presets.modelPresets.find((one) => one.id === (ready.ok ? ready.providerId : restored.providerId))?.startingPrompt ?? '',
    chosenId: ready.ok ? ready.providerId : restored.providerId,
    presses: 0,
    ourDraft: '',
    // Which door opened it is remembered across the reload, so a chat restored from a `.md` does
    // not come back offering to read a session there was never one of.
    //
    // A record written BEFORE the field existed says nothing, and nothing is read as not a session.
    // The plan round argued the other way — absent as `true`, so a long-running session tab keeps
    // its button across the upgrade — and the code round answered it with the case that settles it:
    // a file chat called `README.md` in a folder holding a session Claude named `README.md` would
    // show that session's words inside the file's tab. Handing over another conversation silently is
    // the one outcome this whole join exists to prevent, and it outranks a button that is missing
    // from stored tabs until they are opened again. (codex, twice, from two roles.)
    fromSession: saved.fromSession,
    // Restored with the transcript it belongs to: without it, a reload would put the whole
    // conversation back on the wire and the next Team turn would quietly cost what it used to.
    carryFrom: carryMark(saved.carryFrom, saved.messages.length),
    // What the store already holds, so the first push after a reload does not rewrite the tab to say
    // exactly what it already said. (gemini, the code round.)
    savedCarryFrom: carryMark(saved.carryFrom, saved.messages.length),
    // A reload loses it, and the first press resolves it again — by a name that may by then have
    // moved on. Nothing better is available: the file is not in the store, and putting it there
    // would be a path to somebody's home directory living in workspace state.
    sessionFile: '',
    running: false,
    // A restored tab shows no failure — it is live state and is not saved with the conversation — so
    // there is nothing for a retry to be offered of until the next turn fails.
    failedWith: '',
    turn: 0,
    generation: 0,
    resetting: false,
    // Replaced by `reopened` with the rules of whatever model actually answers — this conversation
    // asks nothing until then, so neither field is consulted before it is right. Written out rather
    // than taken from `memoryOf`, which needs a vendor, and a restored tab has none yet.
    forgetful: false,
    asked: 0,
    // The whole transcript, ready to travel with the first question — the same handover a model
    // switch performs, and the reason nothing has to resume a vendor thread.
    carry: carriedFrom(saved.messages, carryMark(saved.carryFrom, saved.messages.length)),
    messages,
    savedMessages: messages,
    savedModelId: saved.modelId,
    turns: Promise.resolve(),
    saveId: saved.id,
    // The record's OWN revision, read back with it, so the first save after a reload is a swap
    // against exactly what the disk holds: another window that has moved it on is refused and the
    // tab forks, which is the honest answer. A copy that came from the memento arrives at 0 — no
    // disk revision known — and the first save then meets whatever is there; `nextAfterSave` ADOPTS
    // it when its words are the beginning of ours, the rule kept for exactly that case.
    rev: saved.rev,
    // And when it was last USED, which is the record's own instant rather than now: a reload is not
    // a use, and neither is a picker reopening a conversation to look at it. The Open row it draws
    // says how long ago somebody last said something, which is what a person is looking for.
    usedAt: saved.updatedAt,
    // When it BEGAN, as the record says — not when it was last written. A memento copy carries only
    // its last write, and `fromLegacy` puts that in both fields, which is the best answer it has.
    createdAt: saved.createdAt,
    writes: Promise.resolve(),
    title: saved.title,
    reopen: true,
  });
  // ITS OWN KEY unless a caller named one. The tab a conversation was opened from may be gone, may
  // be a different object, or may already hold a live conversation — so a reload and a picker choice
  // both give it a key of its own, and only *go to* binds it to the tab a person is looking at.
  panels.open(bindTo?.key ?? {}, bindTo?.label ?? saved.title, () => entry);
  // A restored conversation is an open one, and the sweep in every other window must hear so.
  pulse?.();
  // AND THIS IS THE SELF-HEALING, which until the code round was only claimed. A conversation whose
  // host died between the pin and its write comes back saying it came from a session and carrying no
  // source — unmatchable by *go to* for ever, because nothing on this path ever pinned again. It
  // pins again now. A record that already has its source is left alone: `pinSession` walks folders,
  // and doing that for every restored tab on every reload would be a directory walk per tab to
  // rediscover something already written down. (codex, the code round.)
  if (saved.fromSession && saved.source.kind === 'none') {
    pinSession(entry, saved.title, true);
  }

  // HANDED BACK, for the caller that has no panel of its own: the serializer is given one by VS Code
  // and ignores this, while the picker needs it to bring the tab it has just built to the front.
  return entry;
}
