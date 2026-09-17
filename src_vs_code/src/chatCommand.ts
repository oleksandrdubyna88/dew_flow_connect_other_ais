import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { isInside } from './chatMessages';
import { notify } from './notify';
import { acknowledgement, answerToCopy, blockToCopy } from './answerCopy';
import { textCopier, type CopyDecision, type CopyReport } from './copyText';
import { imageFileName, imageRefusal, pastedImage } from './chatImage';
import {
  ModelPreset,
  PromptPreset,
  mainModel,
  mainPrompt,
} from './chatPresets';
import { reloadedNote } from './chatTabs';
import { ConversationRecord, ConversationSource, sourceOfFile } from './chatStore';
import { ChatEntry, ChatPanels } from './chatPanels';
import { Thread, threads } from './chatThread';
import { pulse } from './chatHost';
import { ask, oneReask, oneRetry } from './chatTurn';
import { closedSession, freshStart } from './chatArchive';
import { cliFor, remoteFor, started, switchModel } from './chatLaunch';
import { pinSession, resolveAndPin } from './chatSessionJoin';
import { chatLanguage, show } from './chatShow';
import {
  NAMES_ARE_CASE_BLIND,
  originOf,
  whereToLook,
} from './chatRoots';
import { Ready, chatCatalogFrom, openingPrompt, readyToChat, roleOf, savedModels, savedPick, savedPrompts, taskOf } from './chatConfig';
import { CANCELLED, fromTheEditor, matchedSource, passageFor } from './chatCapture';
import { ChatSession } from './chatSession';
import { ChatPageState } from './chatPage';
import {
  LegacyPick,
  chatProvidersFromPresets,
  isRemote,
  memoryOf,
  openingModel,
} from './chatModels';
import { coaiDataDir } from './dataDir';
import { Door, chatDoorRecord } from './chatDoors';
import { recordChatDoor } from './chatDoorsFile';
import { chatSettingsFrom } from './chatSettings';
import { CARRY_EVERYTHING, carriedFrom, carryMark } from './chatCarry';
import { chatTextTone, chatUiScale, createChatPanel, pushChatCopied, pushChatDraft, setChatDraft } from './chatPanel';
import {
  chatInstruction,
  openingTurn,
  reinstructed,
  reinstructedHead,
  serviceLines,
  stillOurs,
} from './chatPrompt';
import { sourceSession } from './sessionKey';
import { askedAsText } from './claudeQuestion';
import {
  promptsFrom,
  waitingQuestion,
} from './claudeSessions';
import { triggerPlan } from './chatTrigger';

/**
 * The one command the person actually presses.
 *
 * <p>The `vscode` half, and the only place the pieces meet: which door the invocation came through
 * (`chatTrigger`), where the passage comes from (`selectionCapture` or the clipboard), which tab it
 * belongs to (`sessionKey`), which conversation that is (`chatPanels`), what to say (`chatPrompt`),
 * and who answers (`chatModels` → `cliChatLaunch` → `cliChatSession`). Every one of those decides
 * without a host and is tested; this file wires them and does nothing clever, which is the same
 * reason `chatPanel.ts` is thin — what cannot be tested should be small.</p>
 */


/**
 * How many times each conversation has asked what was written in its session.
 *
 * <p>The number is the only thing that tells a late answer from a current one, and it lives here
 * rather than on the thread because it is about presses rather than about the conversation.</p>
 */
const asking = new WeakMap<object, number>();

/**
 * Put this conversation's instruction into its composer, around the passage it is about.
 *
 * <p>The two rows of buttons are the same gesture with different halves — a prompt chooses the TASK,
 * a model chooses the ROLE — so they write through one function and cannot drift into behaving
 * differently, which is what they had done: *"переключение промта все ок, а модели подвисает"*.</p>
 *
 * @param draft what the page says is in the box, or `undefined` when the caller did not ask for it —
 *   a prompt button IS the instruction "ask this instead", so it replaces without asking.
 * @returns whether it was written. A box somebody has typed their own question into is left alone,
 *   which two vendors refused to ship without on the plan round.
 */
function writeInstruction(
  entry: ChatEntry,
  thread: Thread,
  draft: string | undefined,
  was: string,
  config: vscode.WorkspaceConfiguration,
): boolean {
  const now = instructionOf(thread, config);
  // FIRST, AND ALMOST ALWAYS: swap the instruction where it stands. The box is the only place that
  // knows what is really in it, so keeping everything after the instruction byte for byte is both
  // the safest thing to do with somebody's words and the only version of this that cannot drift out
  // of step with the conversation's memory of the passage.
  // AND THE SAME SWAP when the instruction in the box is not the one this side wrote, because
  // somebody typed over it. That is ordinary — edit the role, press a preset, and the preset has to
  // win — and it used to fall through to the rebuild below, which refused and left the box alone
  // with a message about it. Cut at the service lines, everything below them kept byte for byte.
  const swapped = draft === undefined
    ? undefined
    : reinstructed(draft, was, now) ?? reinstructedHead(draft, now, chatLanguage());
  if (swapped !== undefined) {
    thread.ourDraft = swapped;
    setChatDraft(entry, swapped);

    return true;
  }
  // The instruction is not at the front any more — somebody wrote over it, or this is a box we have
  // never written to. Then the old test applies: a whole turn goes in when the box is empty or still
  // holds what this side built, and a question somebody typed is left alone.
  if (draft !== undefined && draft !== thread.ourDraft && !stillOurs(draft, thread.passage)) {
    return false;
  }
  const next = openingTurn(now, chatLanguage(), thread.passage);
  thread.ourDraft = next;
  setChatDraft(entry, next);

  return true;
}

/**
 * The person chose a model — from a preset button, or from the picker under them.
 *
 * <p>ONE implementation for both, because they are one decision. The picker used to switch the
 * session and leave the composer instructed by the model before it, so the next turn went to B
 * carrying A's role while the marking said A was answering. (codex, the second code round.)</p>
 *
 * <p>Everything about the SCREEN happens now; the process follows. A switch that is refused leaves
 * the old model answering, so the button goes back to it — and nothing else does: the box may have
 * been typed into while the switch resolved, and the refusal is said out loud instead.</p>
 */
function chooseModel(
  entry: ChatEntry,
  thread: Thread,
  preset: ModelPreset,
  draft: string | undefined,
  config: vscode.WorkspaceConfiguration,
): void {
  const wasChosen = thread.chosenId;
  const wasRole = thread.role;
  const was = instructionOf(thread, config);
  thread.presses += 1;
  const press = thread.presses;
  thread.chosenId = preset.id;
  thread.role = preset.startingPrompt ?? '';
  if (!writeInstruction(entry, thread, draft, was, config)) {
    // The box holds something the person wrote, so it keeps the instruction it has — and the
    // conversation keeps the role that MATCHES it, or the two would disagree about the same words
    // and the marking behind the box would stop finding them.
    thread.role = wasRole;
    if ((preset.startingPrompt ?? '').length > 0) {
      void notify({
        as: 'information',
        class: 'outcome',
        source: 'chat',
        code: 'preset-prompt-left-alone',
        subject: preset.id,
        title: `${preset.name} opens with its own prompt, and the box holds something you wrote — so it was left alone.`,
      });
    }
  }
  show(entry, thread.running, '');
  void switchModel(entry, preset.id, preset.model).then((switched) => {
    // ONLY IF THIS PRESS IS STILL THE LAST ONE — by count, not by id: pressing A, then B, then A
    // again would otherwise let the first A's refusal undo the second. (codex, the code round.)
    if (switched || thread.presses !== press) {
      return;
    }
    thread.chosenId = wasChosen;
    show(entry, thread.running, '');
  });
}

/** What this conversation is instructing with: the model's role, then the prompt's task. */
function instructionOf(thread: Thread, config: vscode.WorkspaceConfiguration): string {
  return chatInstruction(
    thread.role,
    taskOf(config, thread.promptId, chatSettingsFrom((key) => config.get(key)).prompt),
  );
}

/** Everything one new conversation is made of. Called ONLY when a tab has no panel yet. */
function newConversation(
  panels: ChatPanels,
  ready: Extract<Ready, { ok: true }>,
  state: {
    readonly title: string;
    readonly passage: string;
    readonly draft: string;
    readonly fromSession: boolean;
    /**
     * What this conversation is opened FROM — a file's uri, or `none` for a Claude tab, whose id is
     * not known yet and is written by {@link pinSession} when the background walk finds it.
     */
    readonly source: ConversationSource;
  },
  resolved: string,
  remote: ChatSession | undefined,
  extensionUri: vscode.Uri,
): ChatEntry {
  const first = started(ready.vendor, resolved, ready.modelId, remote);
  const session = first.session;
  // The saved lists, read here rather than passed in: every other setting this function needs it
  // reads for itself, and threading two more parameters through for one render is a seam nobody
  // gains from.
  const config = vscode.workspace.getConfiguration('coai');
  // Minted here, once, and never seen by anybody: it goes into the page, comes back from the page
  // after a reload, and names this conversation in the store. A title could not — two Claude Code
  // sessions can share one, which is the defect `chatPanels.ts` was keyed by identity to avoid.
  const saveId = randomUUID();
  const entry = createChatPanel(
    {
      id: saveId,
      title: state.title,
      passage: state.passage,
      messages: [],
      models: ready.models,
      providers: ready.providers,
      promptPresets: savedPrompts(config),
      modelPresets: savedModels(config),
      // A conversation that has just opened has said nothing, so there is nothing to ask again.
      reask: '',
      // Nor anything to send again: nothing has failed yet, and the failure line is empty.
      canRetry: false,
      attached: '',
      spend: '',
      providerId: ready.providerId,
      modelId: ready.modelId,
      chosenModelId: ready.providerId,
      promptId: openingPrompt(config),
      running: false,
      capped: false,
      // Nothing is in flight on a page that has just opened, so there is no turn to stop.
      turn: 0,
      failure: '',
      draft: state.draft,
      fromSession: state.fromSession,
      asked: [],
      carryFrom: CARRY_EVERYTHING,
      marks: {
        role: roleOf(config, ready.providerId),
        task: taskOf(config, openingPrompt(config), chatSettingsFrom((key) => config.get(key)).prompt),
        service: serviceLines(chatLanguage()),
      },
      uiScale: chatUiScale(),
      textTone: chatTextTone(),
    },
    session,
    conversationHooks(panels),
    extensionUri,
  );
  // Recorded against the entry's OWN id, which `createChatPanel` made — not against the tab key,
  // which can move under a live conversation. That distinction cost a whole code round.
  threads.set(entry.id, {
    session,
    home: first.home,
    passage: state.passage,
    models: ready.models,
    providers: ready.providers,
    attached: '',
    attachedPath: '',
    spend: [],
    ...originOf(state.source),
    providerId: ready.providerId,
    modelId: ready.modelId,
    // The MAIN prompt, and the chosen model's own role: what a capture opens on, with both buttons
    // drawn pressed. Asked for with a screenshot of a tab that had opened on neither.
    promptId: openingPrompt(config),
    role: roleOf(config, ready.providerId),
    chosenId: ready.providerId,
    presses: 0,
    ourDraft: state.draft,
    fromSession: state.fromSession,
    carryFrom: CARRY_EVERYTHING,
    sessionFile: '',
    running: false,
    // A conversation that has said nothing has failed at nothing.
    failedWith: '',
    // Counted from 1 by the first turn, so 0 is "this conversation has not asked anything yet" and
    // can never be mistaken for a turn a stop could name.
    turn: 0,
    // The first slate this tab has held. Bumped by every reset; see `Thread.generation`.
    generation: 0,
    resetting: false,
    ...memoryOf(ready.vendor),
    carry: [],
    messages: [],
    turns: Promise.resolve(),
    saveId,
    // Nothing of this conversation is on disk yet, and 0 is what says so to the store's swap — the
    // only baseline allowed to create a record rather than replace one.
    rev: 0,
    createdAt: Date.now(),
    // Opened counts as used: a tab somebody has just made is the newest thing in this window, and a
    // conversation with nothing said in it would otherwise sort to the bottom of the picker at the
    // instant it is most likely to be looked for.
    usedAt: Date.now(),
    writes: Promise.resolve(),
    title: state.title,
    reopen: false,
  });
  // The heartbeat, on the next tick — by which time the caller's `panels.open` has registered this
  // entry, which is what `heldConversationIds` reads.
  pulse?.();

  // In the background, while this tab's name still matches the session it came from.
  pinSession(entry, state.title, state.fromSession);

  return entry;
}

/**
 * Everything a chat page can ask of the host, for a conversation that is opened OR restored.
 *
 * <p>One object built in one place. Both paths create a panel, and a second copy of these six
 * callbacks would be six chances for a restored tab to stop stopping turns, or to leak a session on
 * close, the day one of them changes.</p>
 */
function conversationHooks(panels: ChatPanels): Parameters<typeof createChatPanel>[2] {
  // ONE sentence-teller for this whole object. Three reviewers found the acknowledgement path's
  // empty catch, and `coding-style.md` is explicit that an error is never silently swallowed; a
  // second inline `showWarningMessage` beside the first is how one of them ends up without the
  // other's wording.
  //
  // It goes through the funnel rather than to `vscode.window` directly, because this is the one
  // sentence in the object that appears while the person is looking somewhere ELSE by definition:
  // what they were waiting for is a tick on a control that simply never came. A toast nobody was
  // facing is the case the notifications ledger exists for.
  const warn = (message: string): void => {
    void notify({
      as: 'warning',
      class: 'failure',
      source: 'chatPage',
      code: 'copy-not-acknowledged-on-the-page',
      title: message,
    });
  };
  // ONE copier for both controls on an answer, so the whole and the part cannot disagree about what
  // happens when the clipboard refuses — and so two quick presses land in the order they were made.
  // Everything it knows about `vscode` is these two functions; the decisions are in `answerCopy.ts`,
  // where a test can reach them.
  const answerCopier = textCopier({
    writeText: (text) => Promise.resolve(vscode.env.clipboard.writeText(text)),
    say: (message, forMs) => vscode.window.setStatusBarMessage(message, forMs),
  });

  return {
      onSend: (id, text) => {
        const found = panels.entryOf(id);
        if (found !== undefined) {
          void ask(found, text);
        }
      },
      onPick: (id, providerId, modelId, draft) => {
        const config = vscode.workspace.getConfiguration('coai');
        const found = panels.entryOf(id);
        const mine = threads.get(id);
        const preset = savedModels(config).find((one) => one.id === providerId);
        if (found === undefined) {
          return;
        }
        // A PRESET is what the first select names, so choosing one there is choosing a model in
        // every sense the buttons above mean it. The bare switch is what is left for a pick this
        // side cannot match to a preset — a page one write behind, or the model half alone.
        if (mine !== undefined && preset !== undefined && modelId.length === 0) {
          chooseModel(found, mine, preset, draft, config);

          return;
        }
        void switchModel(found, providerId, modelId);
      },
      onUsePrompt: (id, presetId, draft) => {
        const found = panels.entryOf(id);
        const preset = savedPrompts(vscode.workspace.getConfiguration('coai'))
          .find((one) => one.id === presetId);
        const thread = threads.get(id);
        if (found === undefined || thread === undefined) {
          return;
        }
        // Said out loud, like the model button beside it: a press can outlive the row it names.
        if (preset === undefined) {
          const gone = 'That saved prompt is no longer in your presets — it was removed or renamed.';
          void notify({
            as: 'warning',
            class: 'refusal',
            source: 'chat',
            code: 'prompt-preset-gone',
            title: gone,
          });
          show(found, thread.running, gone);

          return;
        }
        // The INSTRUCTION changes and the captured passage stays. The composer holds both — the
        // prompt, the language line, the fence and the text that was selected — so replacing the
        // whole box threw away the passage the conversation is about, which is what a person watched
        // happen every time they pressed a second button. Rebuilt from the same three parts instead.
        const config = vscode.workspace.getConfiguration('coai');
        const was = instructionOf(thread, config);
        const wasPrompt = thread.promptId;
        // The TASK changes; the role the model carries is untouched, because pressing "explain" does
        // not stop it being an architect. The two buttons run the same three lines with different
        // halves, which is the only way they can go on behaving the same.
        thread.promptId = presetId;
        if (!writeInstruction(found, thread, draft, was, config)) {
          // The box was not rewritten, so the conversation keeps the task the words in it were
          // written with — otherwise the marking, and the pair recorded with the question when it is
          // sent, would both name a prompt nothing on screen used. (gemini, the second code round.)
          thread.promptId = wasPrompt;
          void notify({
            as: 'information',
            class: 'outcome',
            source: 'chat',
            code: 'preset-instruction-left-alone',
            subject: preset.id,
            title: `${preset.name} replaces the instruction, and the box holds something you wrote — so it was left alone.`,
          });
        }
        show(found, thread.running, '');
      },
      onUseModel: (id, presetId, draft) => {
        const config = vscode.workspace.getConfiguration('coai');
        const found = panels.entryOf(id);
        const preset = savedModels(config).find((one) => one.id === presetId);
        const mine = threads.get(id);
        if (found === undefined || mine === undefined) {
          return;
        }
        // A BUTTON THAT NAMES NOTHING SAYS SO. The rows are redrawn on every state push, so a preset
        // deleted in the other tab normally takes its button with it — but a press can outlive the
        // row it names, and a button that does nothing and explains nothing is the defect this whole
        // change started from. (CodeRabbit, PR #200.)
        if (preset === undefined) {
          const gone = 'That saved model is no longer in your presets — it was removed or renamed.';
          void notify({
            as: 'warning',
            class: 'refusal',
            source: 'chat',
            code: 'model-preset-gone',
            title: gone,
          });
          show(found, mine.running, gone);

          return;
        }
        chooseModel(found, mine, preset, draft, config);
      },
      onMarkGone: (id, which) => {
        const mine = threads.get(id);
        const found = panels.entryOf(id);
        if (mine === undefined || found === undefined) {
          return;
        }
        // The button stops looking pressed. It was lit because its words were the instruction in
        // force; they are not, and a button claiming something nothing is using lies about what the
        // next question will carry. Two halves, two buttons: a PROMPT preset owns the task, and a
        // MODEL preset owns the role it put there — edit either away and that one goes dark.
        const lit = which === 'task' ? mine.promptId : mine.chosenId;
        if (lit.length === 0) {
          return;
        }
        if (which === 'task') {
          mine.promptId = '';
        } else {
          mine.chosenId = '';
        }
        show(found, mine.running, '');
      },
      onCarryFrom: (id, at) => {
        const mine = threads.get(id);
        const found = panels.entryOf(id);
        if (mine === undefined || found === undefined) {
          return;
        }
        // CLAMPED HERE TOO, and FORWARD ONLY. The page's number is checked for being a position at
        // all; this one is checked against the conversation it is a position IN, which the page
        // cannot do — the transcript it rendered may be a turn behind the one the host holds.
        //
        // And never backwards. The button is offered on the last answer alone, so a real press can
        // never name a position above the mark already set; a lower one is a stale page or a forged
        // message, and taking it would put back a conversation somebody deliberately excluded — on
        // a Team server, at a price. (codex, the code round, as a security finding.)
        mine.carryFrom = Math.max(
          carryMark(at, mine.messages.length),
          carryMark(mine.carryFrom, mine.messages.length),
        );
        // AND THE CARRY ALREADY STAGED. A switch, a restore and a lost context all fill `carry`
        // ahead of the next question — so pressing the button after switching and before asking
        // moved the rule and sent the whole conversation anyway, which is the one case this feature
        // was built for. (gemini, the code round.)
        if (mine.carry.length > 0) {
          mine.carry = carriedFrom(mine.messages, mine.carryFrom);
        }
        // `show` writes the tab down and pushes it back, in that order, so the rule the person ends
        // up looking at is the rule that will survive a reload rather than one the store never
        // heard about. (codex, the plan round.)
        show(found, mine.running, '');
      },
      onShowAsked: (id) => {
        const found = panels.entryOf(id);
        const mine = threads.get(id);
        if (found === undefined) {
          // The tab is gone; there is nobody to answer.
          return;
        }
        if (mine === undefined) {
          // SAID, not left silent. The page paints "Reading the session…" the moment the region
          // opens, and a hook that returns without posting leaves that there for as long as the tab
          // is open. (gemini, the code round.)
          found.panel.post({
            type: 'asked',
            at: Number.MAX_SAFE_INTEGER,
            asked: [],
            refusal: 'This conversation is no longer held by the extension, so its session cannot be found.',
          });

          return;
        }
        // WHICH PRESS THIS IS. Opening, folding and opening again starts a second read while the
        // first is still going, and the slower one landing last would replace what the person just
        // asked for with what they asked for before. The page keeps the highest it has seen and
        // ignores anything older. (codex and gemini, the code round, on both halves of it.)
        const at = (asking.get(id) ?? 0) + 1;
        asking.set(id, at);
        void (async () => {
          // THE FILE FIRST, when this tab has one. It was resolved as the tab opened, while its name
          // still matched — and a name is what goes stale here, never a path.
          //
          // A RESTORED tab has none: the reload lost it, so the first press resolves by name and
          // KEEPS what it found. Without that it resolved afresh every time, by a name that is
          // exactly as stale on the second press as on the first — so a conversation Claude renamed
          // after the reload would never be found again. (CodeRabbit, PR #207.)
          const answer = mine.sessionFile.length > 0
            ? await promptsFrom(mine.sessionFile)
            : await resolveAndPin(found, mine);
          // A REASON, never a blank region. Four situations look identical from an empty box — no
          // session file, no folder, a namesake it refuses to pick between, and a conversation the
          // person has not spoken in yet — and the box is the only place they are looking.
          found.panel.post(answer.kind === 'said'
            ? { type: 'asked', at, asked: answer.said, refusal: '' }
            : { type: 'asked', at, asked: [], refusal: answer.refusal });
        })();
      },
      onStop: (id, turn) => {
        const found = panels.entryOf(id);
        const thread = threads.get(id);
        if (found === undefined || thread?.running !== true) {
          // Nothing is running, or the tab is already gone. A stop is a message about a turn, and
          // there is no turn — killing the process for it would cost the conversation for a keypress
          // that arrived too late to mean anything.
          return;
        }
        if (turn !== thread.turn) {
          // The page named a turn that is no longer the running one, which is what a late or a
          // repeated press looks like from here. Refused rather than applied to whatever happens to
          // be in flight now — that turn is a different question the person has not asked to stop.
          // There is no wildcard to fall back to: the bridge already refuses a stop that names no
          // turn, so `turn` here is always a real number a page chose. (codex and gemini, the code
          // round, on both halves of this rule.)
          return;
        }
        thread.session.stop();
      },
      onClosed: (id) => {
        // The registry disposes the session the ENTRY was created with, which after a model switch
        // is no longer the one that is running. So the thread's own current session is ended here
        // too — disposal is idempotent, and the alternative is an authenticated child nobody owns.
        const thread = threads.get(id);
        panels.closeById(id);
        // Forgotten, not merely disposed. A remote turn can be answered a poll after the tab went
        // away, and `show` would then post state into a webview VS Code has torn down — a throw out
        // of a callback nobody catches. Every reader of a thread starts by looking it up, so
        // removing it turns all of them into no-ops at once. (codex and gemini, the code round.)
        threads.delete(id);
        pulse?.();
        thread?.session.dispose();
        thread?.home.release();
      },
      onAttach: (id, dataUrl) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void attachPicture(entry, thread, dataUrl);
        }
      },
      onUnattach: (id) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          forgetPicture(thread);
          show(entry, false, '');
        }
      },
      onReask: (id) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void oneReask(entry, thread);
        }
      },
      onRetry: (id, at) => {
        const thread = threads.get(id);
        const entry = panels.entryOf(id);
        if (thread !== undefined && entry !== undefined) {
          void oneRetry(entry, thread, at);
        }
      },
      onRestart: (id) => {
        // *New chat*: the capped notice's button since the cap existed, and D2's header button. One
        // implementation for both, because they are the same gesture with the same words.
        const entry = panels.entryOf(id);
        if (entry !== undefined) {
          void freshStart(entry);
        }
      },
      onUseLocal: () => undefined,
      onPageError: (_id, message) => {
        void notify({
          as: 'warning',
          class: 'failure',
          source: 'chatPage',
          code: 'chat-page-reported-an-error',
          title: `The chat page reported: ${message}`,
          detail: message,
        });
      },
      onOpenFile: (id, requested, line) => {
        void openWorkspaceFile(id, requested, line, (message) => {
          void notify({
            as: 'warning',
            class: 'failure',
            source: 'chatPage',
            code: 'file-from-an-answer-not-opened',
            subject: requested,
            title: message,
          });
        });
      },
      onCopyAnswer: (id, index, sig) => {
        // The SOURCE, out of the thread the page was rendered from. A person copying an answer wants
        // the markdown they can paste into a plan or an issue, and that is the one thing selecting
        // the page cannot give them - a selection gives what the page shows.
        // RESOLVED at press time, not when its turn in the queue comes. A block control carries a
        // signature and is refused if the answer changed under it; this one carries nothing, so a
        // press queued behind a slow write could otherwise copy whatever had replaced the message at
        // that index by the time it ran. (codex, the code round.)
        const said = threads.get(id)?.messages[index];
        const decision: CopyDecision = said === undefined || said.role !== 'model'
          ? { kind: 'refused', said: 'That answer is not on this page any more.' }
          : answerToCopy(said.text);
        tellThePage(warn, panels, id, index, undefined, sig, answerCopier.copy(() => decision));
      },
      onCopyBlock: (id, index, block, sig) => {
        // The SAME markdown the page was drawn from, walked by the SAME function that numbered the
        // control. Nothing the page sent becomes text: it named a position and echoed a signature,
        // and both are checked here against what this host holds.
        tellThePage(warn, panels, id, index, block, sig, answerCopier.copy(() => {
          const said = threads.get(id)?.messages[index];

          return said === undefined || said.role !== 'model'
            ? { kind: 'refused', said: 'That answer is not on this page any more.' }
            : blockToCopy(said.text, block, sig);
        }));
      },
  };
}

/**
 * Tick the control that was pressed — but only once the clipboard actually took the text.
 *
 * <p>Both copy hooks discarded their report with a bare `void`, which is why neither control could
 * ever move: the one fact worth showing was thrown away at the point it became known. It is a
 * function rather than two copies of four lines because the two hooks differ in one argument, and
 * two sites that each need a catch is how one of them ends up without one.</p>
 *
 * <p><b>Nothing is shown for a copy that did not land.</b> `CopyReport.copied` is true only when the
 * write RESOLVED; a refusal and a clipboard held by another program both come back false, and the
 * sentence `copyText.ts` has already put in the status bar is then the only thing the person sees,
 * which is right — a tick there would be a lie about where their paste is coming from.</p>
 *
 * <p>The rejection is caught rather than left to float. `textCopier.copy` answers a report on every
 * path it knows, but an unowned rejection from a boundary like this one surfaces as an extension-host
 * error rather than as anything the tab can say, and the panel's own message dispatch already guards
 * itself the same way.</p>
 */
function tellThePage(
  said: (message: string) => void,
  panels: ChatPanels,
  id: object,
  index: number,
  block: number | undefined,
  sig: string,
  copying: Promise<CopyReport>,
): void {
  void copying.then((report) => {
    // THE RULE IS IN `answerCopy.ts`, where a test can reach it. Nothing in this file can be
    // imported by the suite, so a condition written here is a condition nothing checks.
    const landed = acknowledgement(report, { index, ...(block === undefined ? {} : { block }), sig });
    if (landed === undefined) {
      return;
    }
    const entry = panels.entryOf(id);
    if (entry !== undefined) {
      pushChatCopied(entry, landed.index, landed.block, landed.sig);
    }
  }).catch((reason: unknown) => {
    // NAMED, not swallowed. The person already has the sentence `copyText.ts` put in the status bar,
    // so this is not a second thing to tell them about the copy — it is the extension saying that its
    // own acknowledgement path failed, which is otherwise invisible: the control simply never ticks
    // and nothing anywhere says why. (Three reviewers, the code round; `coding-style.md` forbids a
    // silently swallowed error.)
    said(`The copy could not be acknowledged on the page: ${reason instanceof Error ? reason.message : String(reason)}`);
  });
}

/**
 * Open a file an ANSWER named, if it is really inside this workspace.
 *
 * <p>The renderer checked the shape and `chatCommandOf` checked it again, and neither is enough: a
 * string can look confined and still leave through a folder that merely starts with the same
 * letters. So the path is resolved against each workspace root and the RESULT is what
 * `isInside` decides on — a boundary at a separator, not a prefix.</p>
 *
 * <p>A reference that resolves nowhere is SAID rather than swallowed: a link that quietly does
 * nothing is a link a person presses twice.</p>
 */
async function openWorkspaceFile(
  _id: object,
  requested: string,
  line: number,
  refuse: (message: string) => void,
): Promise<void> {
  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    const target = vscode.Uri.joinPath(folder.uri, requested);
    if (!isInside(folder.uri.path, target.path)) {
      continue;
    }
    try {
      await vscode.workspace.fs.stat(target);
    } catch {
      continue;
    }
    const editor = await vscode.window.showTextDocument(target);
    if (line > 0) {
      const at = new vscode.Position(Math.max(0, line - 1), 0);
      editor.revealRange(new vscode.Range(at, at), vscode.TextEditorRevealType.InCenter);
      editor.selection = new vscode.Selection(at, at);
    }

    return;
  }

  refuse(`There is no ${requested} in this workspace.`);
}

/**
 * Where a conversation's pictures live: one directory per conversation, under the extension's own.
 *
 * <p>The vendor process OPENS these files — that is the mechanism phase 0 measured — so they are
 * real files with a real path, and their lifetime is a contract rather than a detail. One directory
 * per conversation is what makes forgetting them possible: the tab closing removes it whole, the
 * way `chatOrphans.ts` ends the processes.</p>
 */
function pictureDir(id: string): string {
  return path.join(coaiDataDir(), 'pictures', id.replace(/[^\w-]/g, ''));
}

/** Take the picture off, and take the FILE with it — a file nobody will open is a file left behind. */
function forgetPicture(thread: Thread): void {
  if (thread.attachedPath.length > 0) {
    try {
      fs.rmSync(thread.attachedPath, { force: true });
    } catch {
      // A file that cannot be removed is not worth a sentence to the person: it is in a temp
      // directory the tab's own close sweeps, and saying so would explain nothing they can act on.
    }
  }
  thread.attached = '';
  thread.attachedPath = '';
}

/**
 * Keep a pasted picture, or say why it cannot be kept.
 *
 * <p>Refused BY NAME where the chosen provider cannot take one. The worst outcome this feature has
 * is a picture that silently does not arrive: somebody pastes a screenshot, asks about it, and is
 * answered about the text alone with nothing anywhere saying the image was dropped.</p>
 */
async function attachPicture(entry: ChatEntry, thread: Thread, dataUrl: string): Promise<void> {
  const refusal = imageRefusal(thread.providerId);
  if (refusal.length > 0) {
    show(entry, thread.running, refusal);

    return;
  }
  const picture = pastedImage(dataUrl);
  if (picture === undefined) {
    show(entry, thread.running, 'That is not a picture this can send — PNG, JPEG, WebP and GIF only.');

    return;
  }
  forgetPicture(thread);
  const dir = pictureDir(entry.id.toString());
  try {
    await fs.promises.mkdir(dir, { recursive: true });
    const file = path.join(dir, imageFileName(picture.type, thread.turn + 1));
    await fs.promises.writeFile(file, Buffer.from(picture.base64, 'base64'));
    thread.attached = dataUrl;
    thread.attachedPath = file;
  } catch {
    show(entry, thread.running, 'The picture could not be written to disk, so nothing was attached.');

    return;
  }
  show(entry, thread.running, '');
}

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

/**
 * The command.
 *
 * @param panels the registry of open conversations
 * @param args what VS Code handed it — a menu item passes the webview, a keybinding passes nothing
 */
export async function chatWithOtherAi(
  panels: ChatPanels,
  extensionUri: vscode.Uri,
  args: readonly unknown[],
  asked?: boolean,
): Promise<void> {
  const settings = chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key));
  // ONE resolution for both commands, in `readyForChat`. It was written twice — here and beside the
  // question command — two places deciding which model answers. (gemini, the code round.)
  const ready = readyForChat();
  if (!ready.ok) {
    void notify({
      as: 'warning', class: 'refusal', source: 'chat', code: 'chat-not-ready', title: ready.refusal,
    });

    return;
  }

  // ASKED FIRST, before anything expensive or anything that touches what belongs to the person.
  // The keybinding is scoped to the assistant panel, but the command palette is not: invoked from
  // the wrong tab this used to spend 1.7 seconds, borrow the clipboard and synthesise a keystroke,
  // and only then say it was the wrong tab. (gemini, the second code round.)
  // TWO DOORS, and the active tab says which. From Claude Code's own panel the passage has to be
  // copied out through the OS, because that webview cannot be read; from an ordinary file it is
  // simply read. The second door is what the operator asked for: *"хочу чтоб можно было через
  // Ctrl+Alt+A в обычных окнах тоже вызывать. например на md файлах, cs файлах"*.
  const { claude: match, source, uri } = matchedSource(panels);
  if (source === undefined) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'no-source-tab',
      title: 'Open this from a Claude Code session tab or from a file — the conversation is named after it.',
    });

    return;
  }
  if (source.kind === 'rekey') {
    panels.rekey(source.from, source.key);
  }

  const plan = triggerPlan(args, settings.autoSend, asked);
  const passage = match === undefined ? await fromTheEditor() : await passageFor(plan.path);
  if (passage.text.trim().length === 0) {
    if (passage.failure !== CANCELLED) {
      void notify({
        as: 'warning',
        class: 'refusal',
        source: 'chat',
        code: 'nothing-to-explain',
        title: passage.failure.length > 0 ? passage.failure : 'Nothing to explain — copy the text first.',
      });
    }

    return;
  }

  await deliverPassage(panels, extensionUri, ready, source, passage, plan.send, match !== undefined, uri);
}

/**
 * Everything a door does once it HOLDS a passage: resolve the CLI, resolve the remote session,
 * open or reveal the conversation, and either ask or leave the turn in the composer.
 *
 * <p>Extracted when the second door arrived, and the third made it certain: the part that differs
 * between doors is where the passage came from and nothing else. A copy of this per door would be
 * three places for the opening instruction, the temp directory and the reveal to drift apart.</p>
 */
async function deliverPassage(
  panels: ChatPanels,
  extensionUri: vscode.Uri,
  ready: Extract<Ready, { ok: true }>,
  source: NonNullable<ReturnType<typeof sourceSession>>,
  passage: { readonly text: string },
  send: boolean,
  fromSession: boolean,
  /** The active document's uri, carried from the one snapshot the match was made over. */
  sourceUri: string,
  append = false,
): Promise<void> {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = chatSettingsFrom((key) => config.get(key));
  const opening = { providerId: ready.providerId };
  // Resolved BEFORE a tab exists, because `spawn` does not search PATHEXT: a bare `codex` on
  // Windows means `codex.cmd`, and spawning the bare name fails with ENOENT at the first turn —
  // deep inside a conversation, where it reads as the model refusing rather than as a CLI that is
  // not installed. Said here instead, in a sentence somebody can act on.
  const cli = await cliFor(ready.vendor);
  if (cli.refusal.length > 0) {
    void notify({
      as: 'warning', class: 'refusal', source: 'chat', code: 'chat-cli-not-found', title: cli.refusal,
    });

    return;
  }
  const remote = isRemote(ready.vendor) ? await remoteFor(ready.vendor) : { session: undefined, refusal: '' };
  if (remote.refusal.length > 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'team-server-not-reachable-for-chat',
      title: remote.refusal,
    });

    return;
  }

  // THE TURN THE TAB IS BUILT WITH, instruction and all. It used to be the panel's prompt alone,
  // with the role added a moment later by a posted draft — so a page that was still loading when
  // that arrived showed a composer the conversation had already moved past. The page bootstrap
  // carries it instead; nothing about what a capture opens with depends on a message landing.
  // (codex, the plan round.)
  const turn = openingTurn(
    chatInstruction(roleOf(config, opening.providerId), taskOf(config, openingPrompt(config), settings.prompt)),
    settings.language,
    passage.text,
  );
  // A factory, not a value: nothing is built — no process, no temp directory — for a tab that
  // already holds a conversation.
  const opened = panels.open(source.key, source.label, () => newConversation(panels, ready, {
    title: source.label,
    passage: passage.text,
    draft: send ? '' : turn,
    // A session tab has a file behind it; a file does not. The button that reads one back is
    // offered only where there is something to read.
    fromSession,
    // A FILE tab is identified by its document; a Claude tab has no id until its session is found,
    // and `pinSession` writes that one when the walk lands.
    source: fromSession ? { kind: 'none' } : sourceOfFile(sourceUri),
  }, cli.resolved, remote.session, extensionUri));

  opened.entry.panel.reveal();
  // ADDED, not substituted — but only to a conversation that is already open. `add the question`
  // is the verb for "this too": the box already holds a turn somebody composed, and the question
  // belongs under it as more of the material, below the fence the instruction already drew. A tab
  // that did not exist a moment ago has nothing to add to, so it is built the ordinary way and the
  // two items are the same thing there.
  if (append && opened.outcome === 'revealed') {
    pushChatDraft(opened.entry, passage.text);
    show(opened.entry, false, '');

    return;
  }

  // WHICH INSTRUCTION THIS CONVERSATION OPENS WITH, in the order the three of them mean something.
  //
  // 1. The prompt button pressed IN THIS TAB. It is a decision about this conversation and it lasts
  //    as long as the conversation does — capturing a second passage used to go back to the panel's
  //    choice without saying so.
  // 2. The chosen MODEL's own starting prompt. "What the composer opens with when this model is
  //    chosen" is what the field says, and choosing it by opening a chat on it is choosing it: it
  //    used to apply only when its button was pressed, so a model configured with "you are an
  //    architect" answered as if it had no instruction at all.
  // 3. The prompt the panel names, which is the answer when neither of the other two has spoken.
  const mine = threads.get(opened.entry.id);
  if (mine !== undefined) {
    // The ROLE of the model that will answer — read here because the tab may have been opened long
    // ago, on a model the person has since switched away from.
    mine.role = roleOf(config, mine.providerId);
  }
  const instruction = chatInstruction(
    mine?.role ?? roleOf(config, opening.providerId),
    taskOf(config, mine?.promptId ?? '', settings.prompt),
  );
  const asking = instruction.length === 0
    ? turn
    : openingTurn(instruction, settings.language, passage.text);
  if (send) {
    await ask(opened.entry, asking);

    return;
  }
  // REPLACED, not added to. A capture is a new question about a new passage; joining it onto the
  // last one left two passages and two instructions in the box, which is what a person saw after
  // pressing a preset and capturing again.
  //
  // And for a NEW panel as well as a revealed one. The page is built with `turn` — the panel's own
  // prompt, with no role in it — so a tab that opened on a model carrying "you are an architect"
  // showed a composer that did not say so.
  if (mine !== undefined) {
    mine.ourDraft = asking;
  }
  setChatDraft(opened.entry, asking);
  // The rows, redrawn: the model that is answering and the prompt in force both look pressed. A tab
  // that opens with an instruction nothing on screen names is a tab that looks like it ignored the
  // presets.
  show(opened.entry, false, '');
}

/**
 * One invocation of one door, written down before anything can refuse it.
 *
 * <p>This is the ONLY thing that can answer "how many times did I use take the question", because a
 * turn is recorded when a turn finishes and `add the question` normally finishes none — it fills the
 * composer and stops. So invocations are recorded, and they are recorded HERE, at the command,
 * rather than further down where the passage is delivered: a door that cannot resolve a CLI, or that
 * the person dismisses, never reaches the delivery, and an attempt is exactly the thing the count is
 * about. (gemini, the plan round, as the blocking finding.)</p>
 *
 * <p>The provider and model are the ones IN FORCE at that moment — who WOULD answer. Not who did: a
 * door may never be answered at all, and a conversation can switch model afterwards. A window with
 * nothing configured records empty strings, which the spending page groups as one named bucket
 * rather than inventing a row for.</p>
 */
export function noteChatDoor(door: Door, at = new Date()): void {
  const ready = readyForChat();
  // The record is BUILT in the pure module and only written here. The clock is the one thing this
  // function contributes that a test cannot pin, so it is a parameter with the ambient value as its
  // default - the shape the UTC rule asks for, at the only boundary that can hold it.
  void recordChatDoor(coaiDataDir(), chatDoorRecord(
    door,
    ready.ok ? ready.providerId : '',
    ready.ok ? ready.modelId : '',
    at,
    ready.ok ? ready.vendor.runtime : '',
  ));
}

/** Which model answers, resolved the one way both commands resolve it. */
function readyForChat(): Ready {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = chatSettingsFrom((key) => config.get(key));
  const ticked = mainModel(savedModels(config));
  const saved = ticked === undefined ? savedPick(config, settings.model) : undefined;
  const opening = saved ?? { providerId: ticked?.id ?? '', modelId: ticked?.model ?? '' };
  const model = saved === undefined
    ? opening.modelId
    : openingModel(chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config)), saved, settings.modelName);

  return readyToChat(config, opening.providerId, model);
}

/**
 * The question waiting in this window, or the sentence saying why there is none.
 *
 * <p>EVERY workspace folder, not the first: a multi-root window has a Claude session per root, and
 * reading only `workspaceFolders[0]` refuses in one root while a question waits in another. One
 * waiting question across all of them is the answer; two is a refusal that says so. (codex, the
 * code round.)</p>
 */
async function questionWaitingHere(looking: string): Promise<{ text: string; refusal: string }> {
  // A WINDOW WITH NO FOLDER still runs Claude Code, in the home directory. This used to answer "Open
  // a folder first — a Claude Code session belongs to one", which is untrue, and meant the command
  // had never once worked for an operator who works that way.
  const folders = whereToLook();
  // Case-blindness is the FILESYSTEM's, not the platform's in general: Windows and macOS treat two
  // names differing only in case as one, and Linux does not.
  const caseBlind = NAMES_ARE_CASE_BLIND;
  const found = await vscode.window.withProgress(
    { location: vscode.ProgressLocation.Window, title: 'Reading Claude Code\u2019s sessions\u2026' },
    async () => Promise.all(folders.map((folder) => waitingQuestion(os.homedir(), folder, caseBlind, looking))),
  );
  const waiting = found.flatMap((one) => (one.kind === 'one' ? [one.session] : []));
  if (waiting.length > 1 || found.some((one) => one.kind === 'several')) {
    return {
      text: '',
      refusal: 'More than one Claude Code session here is waiting for an answer.'
        + ' Close the ones you do not mean, and press it again.',
    };
  }
  if (waiting.length === 1) {
    return { text: askedAsText(waiting[0]!.asked), refusal: '' };
  }
  const elsewhere = found.find((one) => one.kind === 'elsewhere');
  if (elsewhere !== undefined) {
    return {
      text: '',
      refusal: 'A Claude Code session here is waiting for an answer, but it is not the one this tab'
        + ' is showing. Open that conversation and press it there.',
    };
  }
  if (found.some((one) => one.kind === 'answered')) {
    // A DIFFERENT SENTENCE from "nothing was asked". Handing a second model a question that is
    // already settled is the worst outcome this command has.
    return { text: '', refusal: 'The last question in this session has already been answered — there is nothing waiting.' };
  }
  const failed = found.find((one) => one.kind === 'failed');

  return {
    text: '',
    refusal: failed?.kind === 'failed' ? failed.refusal : 'Claude Code has asked nothing in this folder yet.',
  };
}

/**
 * Take the question Claude Code is asking and hand it to a second model.
 *
 * <p>Asked for as *"перехватывать целиком что спрашивает Клод, потому что сейчас приходится делать
 * скриншоты"*. The screenshots are not a habit — they are the only way, because the question widget
 * cannot be selected: a select-all in that panel highlights the transcript above it and stops at the
 * widget's edge. So this reads the question off the session file Claude Code writes, where it is
 * text, with every option and every description.</p>
 *
 * <p>It goes where a captured paragraph goes — the conversation named after the tab it was invoked
 * from, whether that is Claude's own panel or a file, fenced as MATERIAL like any other passage. So
 * the second model sees the question and every option, and is asked to obey none of it.</p>
 */
export async function takeTheQuestion(
  panels: ChatPanels,
  extensionUri: vscode.Uri,
  append = false,
): Promise<void> {
  const ready = readyForChat();
  if (!ready.ok) {
    void notify({
      as: 'warning', class: 'refusal', source: 'chat', code: 'chat-not-ready', title: ready.refusal,
    });

    return;
  }
  const { claude, source, uri } = matchedSource(panels);
  if (source === undefined) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'no-source-tab',
      title: 'Open this from a Claude Code session tab or from a file — the conversation is named after it.',
    });

    return;
  }
  if (source.kind === 'rekey') {
    panels.rekey(source.from, source.key);
  }
  // THE TAB'S OWN LABEL, which is the only thing that can tell two waiting sessions apart: Claude
  // Code writes the conversation's title into its session file, and the tab shows that same title.
  const question = await questionWaitingHere(claude?.label ?? '');
  if (question.text.length === 0) {
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'no-question-waiting-here',
      title: question.refusal,
    });

    return;
  }

  // NEVER sent by itself, whatever the auto-send setting says. A question taken off disk is one a
  // person is in the middle of answering; it goes into the composer so they can look at it, add
  // what they think, and press send themselves.
  await deliverPassage(panels, extensionUri, ready, source, { text: question.text }, false, claude !== undefined, uri, append);
}
