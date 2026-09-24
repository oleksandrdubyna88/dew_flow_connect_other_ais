import * as os from 'node:os';
import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { notify } from './notify';
import {
  mainModel,
} from './chatPresets';
import { ConversationSource, sourceOfFile } from './chatStore';
import { ChatEntry, ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { pulse } from './chatHost';
import { conversationHooks } from './chatHooks';
import { ask } from './chatTurn';
import { cliFor, remoteFor, started } from './chatLaunch';
import { pinSession } from './chatSessionJoin';
import { chatLanguage, show } from './chatShow';
import {
  NAMES_ARE_CASE_BLIND,
  originOf,
  whereToLook,
} from './chatRoots';
import { agentOffered } from './chatAccessRules';
import { Ready, chatCatalogFrom, openingPrompt, readyToChat, roleOf, savedModels, savedPick, savedPrompts, taskOf } from './chatConfig';
import { CANCELLED, fromTheEditor, matchedSource, passageFor } from './chatCapture';
import { ChatSession } from './chatSession';
import {
  chatProvidersFromPresets,
  isRemote,
  memoryOf,
  openingModel,
} from './chatModels';
import { coaiDataDir } from './dataDir';
import { Door, chatDoorRecord } from './chatDoors';
import { recordChatDoor } from './chatDoorsFile';
import { BugChat, BugChats } from './reviewChoose';
import { chatSettingsFrom } from './chatSettings';
import { CARRY_EVERYTHING } from './chatCarry';
import { chatTextTone, chatUiScale, createChatPanel, pushChatDraft, setChatDraft } from './chatPanel';
import {
  chatInstruction,
  openingTurn,
  serviceLines,
} from './chatPrompt';
import { sourceSession } from './sessionKey';
import { askedAsText } from './claudeQuestion';
import {
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
      // Text, always, for a new conversation — and the box offered only where agent mode could run.
      access: 'text',
      agentOffered: agentOffered(isRemote(ready.vendor), originOf(state.source).workspace),
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
      // Nor anything waiting: a queue is a thing that forms while an answer is running.
      waiting: [],
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
    // a queue is a thing that forms while an answer is running.
    waiting: [],
    home: first.home,
    passage: state.passage,
    models: ready.models,
    providers: ready.providers,
    attached: '',
    attachedPath: '',
    spend: [],
    ...originOf(state.source),
    // Every conversation starts answering from text; agent mode is ticked per job (issue #289).
    access: 'text',
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
    notReady(ready.refusal);

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
    notReady(ready.refusal);

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

/** Each bug's conversation for this window — see {@link BugChats}. */
const bugChats = new BugChats();

/**
 * One bug from the review page, into a chat of its own — the row's *CoAI: choose* (issue #487).
 *
 * <p>It cannot be the right-click command: that one names the conversation after the ACTIVE tab and
 * refuses a webview, and the review page is one. A bug has no tab, so the conversation is keyed by the
 * bug and filed under the bug's file, and the passage comes from the page rather than from a selection.
 * From there it is the same door: never sent, the model and the question the person's to choose.</p>
 *
 * <p><b>A second press on the same bug only brings the conversation back.</b> Refilling the composer
 * would throw away whatever the person had started writing there. (gemini, the plan round.)</p>
 */
export async function chooseFromBug(panels: ChatPanels, extensionUri: vscode.Uri, chat: BugChat): Promise<void> {
  const open = bugChats.openFor(chat.key, panels);
  if (open !== undefined) {
    open.panel.reveal();

    return;
  }
  const ready = readyForChat();
  if (!ready.ok) {
    notReady(ready.refusal);

    return;
  }
  const key = bugChats.keyFor(chat.key);
  await deliverPassage(panels, extensionUri, ready, { kind: 'new', key, label: chat.label }, { text: chat.text }, false, false, fileUriOf(chat));
  bugChats.remember(chat.key, panels);
}

/** The bug's file, which the conversation is filed under — or nothing, when the round recorded none. */
function fileUriOf(chat: BugChat): string {
  return chat.repoPath.length > 0 && chat.file.length > 0
    ? vscode.Uri.joinPath(vscode.Uri.file(chat.repoPath), chat.file).toString()
    : '';
}

/**
 * Why no model can answer, said the one way every door says it.
 *
 * <p>Three doors reach for a model and each refused in its own copy of this block; the third (issue
 * #487) is where the copies became one.</p>
 */
function notReady(refusal: string): void {
  void notify({
    as: 'warning', class: 'refusal', source: 'chat', code: 'chat-not-ready', title: refusal,
  });
}
