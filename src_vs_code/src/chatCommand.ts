import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { isInside } from './chatMessages';
import { imageFileName, imageRefusal, imageTurn, pastedImage } from './chatImage';
import { TurnSpend, spendLabel, spendSoFar } from './chatSpend';
import {
  ModelPreset,
  PromptPreset,
  chatModelPresetsFrom,
  chatPromptPresetsFrom,
  chatRunSpec,
  mainModel,
  mainPrompt,
  reaskFrom,
} from './chatPresets';
import { ChatTabMemory, SavedTab, reloadedNote } from './chatTabs';
import { ChatEntry, ChatPanels } from './chatPanels';
import { ChatSession, TurnResult } from './chatSession';
import { AnsweredBy, ChatMessage, ChatModelChoice, ChatPageState } from './chatPage';
import { CliChatSession, REAL_TIMERS } from './cliChatSession';
import { DEFAULT_BUDGETS } from './chatSession';
import {
  ChatCatalog,
  ChatMemory,
  LegacyPick,
  ChatProvider,
  chatModelsFrom,
  chatProvidersFromPresets,
  isRemote,
  legacyPick,
  memoryOf,
  modelToRun,
  openingModel,
  resolveChatPick,
} from './chatModels';
import { remoteIsFull } from './remoteAsk';
import { remoteChatFor } from './chatRemote';
import { TeamServer, rowBelongsTo, teamServersFrom } from './teamServers';
import { readToken } from './teamServerAuth';
import { coaiDataDir } from './dataDir';
import { ChatOutcome, ReportedUsage, chatTurnRecord } from './chatUsage';
import { recordChatTurn } from './chatUsageFile';
import { DISCOVERY_KEY, EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from './chatDiscovery';
import { chatSettingsFrom } from './chatSettings';
import { chatTextTone, chatUiScale, createChatPanel, pushChatDraft, pushChatState, setChatDraft } from './chatPanel';
import { captureSelection, COPY_SCRIPT, RunOutcome, argvFor, ran } from './selectionCapture';
import { windowsReach } from './hostSide';
import { ChatHome, adapterFor, chatHome, chatRuntimeRefusal, defaultExecutableFor } from './cliChatLaunch';
import { chatProcessFor } from './chatProcess';
import { launch } from './processLauncher';
import { resolvedExecutable } from './versionProbe';
import {
  CARRY_BUDGET,
  REMOTE_CARRY_BUDGET,
  carriedTurn,
  chatInstruction,
  openingTurn,
  reinstructed,
  serviceLines,
  stillOurs,
} from './chatPrompt';
import { LanguageCode } from './settingsShape';
import { isClaudeSessionTab, isOrdinaryEditorTab, sourceSession, TabSnapshot } from './sessionKey';
import { askedAsText } from './claudeQuestion';
import { waitingQuestion } from './claudeSessions';
import { EditorText, confirmWholeFile, passageFromEditor } from './editorPassage';
import { triggerPlan } from './chatTrigger';
import { Vendor } from './vendors';

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
 * What a conversation is, beyond its process. Keyed by the entry's own id, weak so it dies with it.
 *
 * <p>It EXTENDS `ChatMemory` rather than restating its fields, which is what makes them impossible
 * to update in one of the two places a session starts and forget in the other: `memoryOf` is checked
 * against that type at its source, and both callers take the whole object.</p>
 */
interface Thread extends ChatMemory {
  /**
   * The conversation itself — REPLACED when the person picks another model.
   *
   * <p>Held here rather than widened into `ChatPanels`: the registry was written to need only
   * that a session can be ENDED, which is what makes its tests a dozen lines of fakes. Asking it
   * to know how a turn is sent would buy nothing and cost that.</p>
   */
  session: ChatSession;
  /** The directory that session runs in. Replaced with it, and released with it. */
  home: ChatHome;
  readonly passage: string;
  readonly models: readonly ChatModelChoice[];
  /** Every row that can answer, each with its own models. Replaced whenever a pick resolves. */
  providers: readonly ChatProvider[];
  /**
   * What each finished turn cost, in the order they finished.
   *
   * <p>Kept beside the conversation rather than read back from the ledger: the ledger is a file this
   * window shares with every other, and a tab asking it for its own total on every push would be
   * reading a growing file to answer a question it already knows. The ledger is the record; this is
   * the running count.</p>
   */
  spend: TurnSpend[];
  /** The picture waiting to go with the next question, as the page shows it. */
  attached: string;
  /** Where that picture IS — the file a vendor process will open. Empty when there is none. */
  attachedPath: string;
  /** The row that answers, and which of its models — empty for whatever the row is set to. */
  providerId: string;
  modelId: string;
  /**
   * WHICH saved prompt this conversation is using, empty for the one the panel names.
   *
   * <p>A second capture into an open tab used the panel's choice, so pressing a prompt button and
   * then capturing again silently went back to the other prompt. The button is a decision about THIS
   * conversation, and it lasts as long as the conversation does.</p>
   */
  promptId: string;
  /**
   * WHO this conversation's composer says is answering — the model preset's own starting prompt.
   *
   * <p>Held here rather than read back off the model on every rebuild, because the two are chosen at
   * different moments and a switch can be QUEUED behind an answer: read late, the role is whichever
   * model happened to have landed by then. Pressing a model button is a decision about the words in
   * the box, and words in a box need no process to change.</p>
   */
  role: string;
  /**
   * The model preset the PERSON pressed — which button looks pressed.
   *
   * <p>Held apart from `providerId`, which is who is actually ANSWERING. They are the same thing
   * for all but a second or two: a switch disposes a process, resolves a CLI and starts another,
   * and while that ran the button stayed unpressed — *"модели подвисает"*. A press is a decision
   * and it is recorded when it is made; `providerId` goes on telling the truth about the session,
   * which is what the picker below the buttons shows.</p>
   */
  chosenId: string;
  /**
   * How many times a model button has been pressed in this conversation.
   *
   * <p>So a switch that comes back knows whether it is still the last thing asked for. A refusal
   * waits for the answer in flight and for a CLI to resolve, so it can arrive after two more
   * presses — including another press of the SAME preset, which an id alone cannot tell apart.
   * (codex, the code round.)</p>
   */
  presses: number;
  /**
   * Exactly what this side last wrote into the composer.
   *
   * <p>So "is the box still ours to replace" can be answered by comparing rather than by guessing
   * from what the text contains. The guess is kept as a second chance — a page can be a repaint
   * behind — but a box that holds precisely what was put in it is not somebody's typing.</p>
   */
  ourDraft: string;
  /** Whether a turn is in flight. Only so a switch can say out loud that it is waiting for one. */
  running: boolean;
  /**
   * Which turn of this conversation is running, counted from 1. Never reset.
   *
   * <p>It exists so a STOP can name what it means to stop. The page renders its control against the
   * turn it is watching and posts that number back; a stop naming anything else is refused here. A
   * bridge message can land a tick after the answer did, and by then the next question can already
   * be in flight — without the number the second press of a button would end the turn that the first
   * press was too late to reach. (codex and gemini, the plan round.)</p>
   */
  turn: number;
  /**
   * The conversation to hand the NEXT turn, because the process it goes to never heard it.
   *
   * <p>Empty in the ordinary case. Filled when the person switches model: a vendor CLI keeps its
   * context inside its own process, so the replacement starts with nothing, and the only way to
   * carry the questions and the answers across is to say them again in the next turn. Cleared once
   * that turn has been sent — from then on the new process remembers, exactly as the old one did,
   * and nobody pays to re-send a conversation twice.</p>
   */
  carry: readonly ChatMessage[];
  messages: readonly ChatMessage[];
  /**
   * The turns of THIS conversation, one after another.
   *
   * <p>The session already refuses to interleave two turns down one pipe, but the transcript is
   * kept here and two overlapping `ask` calls would write it out of order: pressing the keybinding
   * twice against an open tab put both questions above both answers. The page cannot prevent it —
   * it disables its own composer, and the keybinding does not go through the composer. So the
   * chain is here, mirroring the one inside `cliChatSession`. (gemini, the code round.)</p>
   */
  turns: Promise<unknown>;
  /**
   * This conversation's id in the store, so what it is holding can be written down as it changes.
   *
   * <p>Not the entry's identity object — that one dies with the window. This is the string the page
   * hands back to the serializer after a reload.</p>
   */
  readonly saveId: string;
  /** The tab's heading, kept here because what is written down has to name the conversation. */
  readonly title: string;
  /** What was last written to the store, so a push that changed nothing writes nothing. */
  savedMessages?: readonly ChatMessage[];
  savedModelId?: string;
  /**
   * A conversation restored from a reload, whose vendor process does not exist yet.
   *
   * <p>Nothing is started when a tab comes back: the process behind it died with the window, a
   * restored tab may never be spoken to again, and starting one per tab at every reload would spawn
   * a CLI for each of them for nothing. The first question opens the session and hands it the whole
   * transcript through `carry`, which is the handover a model switch already ships.</p>
   */
  reopen: boolean;
}

const threads = new WeakMap<object, Thread>();

/**
 * Where conversations are kept so a window reload does not empty them. Set once, in `activate`.
 *
 * <p>A module-level handle rather than a parameter on six signatures: the command has no context and
 * neither do the callbacks a panel is wired with, and threading a store through both to reach two
 * call sites would be a wide change for a narrow need. It is absent only in tests of this file's
 * pure neighbours, and every use is guarded.</p>
 */
let memory: ChatTabMemory | undefined;

export function rememberChatsIn(store: ChatTabMemory): void {
  memory = store;
}

/**
 * This extension host, so the chat can read the settings of the side it is actually on.
 *
 * <p>The same bind-once shape as `rememberChatsIn` above and as `chatOrphans.openLedger`, and for the
 * same reason: an extension host has ONE context for its whole life, and threading it from `activate`
 * through the command, the conversation, the picker and the model switch would put a parameter on six
 * signatures to carry a value that never changes.</p>
 *
 * <p>A worry raised on the plan round — that this could go stale across a workspace switch — does not
 * arise: `ExtensionContext` is made once per extension host, and a different workspace is a different
 * host. What CAN change between two invocations is the settings themselves, which is why the reader
 * below is built per call rather than kept here.</p>
 */
let hostContext: vscode.ExtensionContext | undefined;

/** Bind the host whose settings this chat reads. Called once, from `activate`. */
export function chatReadsThisSide(context: vscode.ExtensionContext): void {
  hostContext = context;
}


/** The tabs, narrowed to what `sessionKey` judges on. */
function snapshots(): { active: TabSnapshot | undefined; all: TabSnapshot[] } {
  const all: TabSnapshot[] = [];
  for (const group of vscode.window.tabGroups.all) {
    for (const tab of group.tabs) {
      const input = tab.input as { viewType?: unknown; uri?: { scheme?: unknown } } | undefined;
      all.push({
        key: tab,
        label: tab.label,
        viewType: typeof input?.viewType === 'string' ? input.viewType : '',
        // A `TabInputText` carries the document's uri and no viewType; a webview carries the
        // reverse. Reading both is what lets one snapshot answer for both doors.
        scheme: typeof input?.uri?.scheme === 'string' ? input.uri.scheme : '',
      });
    }
  }
  const activeTab = vscode.window.tabGroups.activeTabGroup.activeTab;

  return { active: all.find((tab) => tab.key === activeTab), all };
}

/** A directory with nothing in it, and the way to take it away. See `cliChatLaunch` for both. */
function emptyTempDir(): ChatHome {
  return chatHome(
    () => fs.mkdtempSync(path.join(os.tmpdir(), 'coai-chat-')),
    (dir) => fs.rmSync(dir, { recursive: true, force: true }),
    // Not a message box: nothing a person can do about a locked temp directory, and a modal for it
    // would be worse than the leak. The extension host's own log is where this belongs.
    (failure) => console.warn(`[coai] ${failure}`),
  );
}

/**
 * Run the copy helper, and say how it finished.
 *
 * <p><b>The same launch on both sides of the machine, and that is measured rather than hoped.</b> In
 * a Remote-WSL window this is a Linux process starting a Windows one: `spawn` resolves a bare name
 * through the PATH, WSL's interop puts the Windows directories on it, and binfmt hands the PE over.
 * The extension host's own environment was inspected on the operator's machine — 33 Windows entries
 * including `WindowsPowerShell/v1.0`, `WSL_INTEROP` set — and the whole helper ran in 1.07 s against
 * the 6 s cap.</p>
 *
 * <p>`os.tmpdir()` is `/tmp` there, and it is the FASTER of the two candidates: 1.07 s against 1.4–1.9 s
 * from `/mnt/c`. So this line is unchanged, deliberately — a reviewer reading it should not take it
 * for an oversight. It matters not at all to the script, which is handed no path: `COPY_SCRIPT`
 * travels base64-encoded, opens nothing and takes no argument.</p>
 */
function pressCopy(): Promise<RunOutcome> {
  const child = launch('powershell.exe', argvFor(COPY_SCRIPT), { cwd: os.tmpdir() });

  return ran(child, (ms, run) => {
    const handle = setTimeout(run, ms);

    return () => clearTimeout(handle);
  });
}

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
function show(entry: ChatEntry, running: boolean, failure: string, queued = 0): void {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  const config = vscode.workspace.getConfiguration('coai');
  pushChatState(entry, {
    messages: thread.messages,
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
    attached: thread?.attached ?? '',
    spend: spendLabel(spendSoFar(thread?.spend ?? [])),
  });
  // The one place the transcript reaches a page is the one place it is written down — but only when
  // there is something new to write. `show` runs on every state push: a turn starting, a queue
  // position moving, a failure clearing. Each of those used to rewrite up to twenty whole
  // transcripts into one key, which is a lot of JSON on the extension host for a page that said the
  // same words. The comparison is by REFERENCE, because `thread.messages` is replaced rather than
  // mutated, so it is exact and costs nothing. (gemini, the code round.)
  if (thread.savedMessages === thread.messages && thread.savedModelId === thread.modelId) {
    return;
  }
  thread.savedMessages = thread.messages;
  thread.savedModelId = thread.modelId;
  memory?.remember({
    id: thread.saveId,
    title: thread.title,
    passage: thread.passage,
    modelId: thread.modelId,
    messages: thread.messages,
  });
}

/**
 * Ask, once whatever this conversation is already doing has finished.
 *
 * <p>Two things the chain buys, and both were found by the gate. The transcript stays in order when
 * the keybinding is pressed twice in a row. And a `send` that REJECTS — which `CliChatSession`
 * promises never to do, but a `ChatSession` is an interface and the remote one is not written yet —
 * cannot leave the composer locked forever behind a `void ask(...)` nobody is watching.</p>
 */
function ask(entry: ChatEntry, text: string): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return Promise.resolve();
  }
  const mine = thread.turns
    .then(() => oneTurn(entry, text))
    .catch((reason: unknown) => {
      // Including the flag: a turn that threw is not a turn still running, and leaving it set would
      // make every later switch claim to be waiting for an answer that will never arrive.
      const thrown = threads.get(entry.id);
      if (thrown !== undefined) {
        thrown.running = false;
      }
      show(entry, false, `the turn failed unexpectedly: ${asText(reason)}`);
    });
  thread.turns = mine;

  return mine;
}

/** The answer language, read fresh: a follow-up turn is asked long after the command ran. */
function chatLanguage(): LanguageCode {
  return chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key)).language;
}

/** A thrown thing, as a sentence. */
function asText(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason);
}

/**
 * Give a restored conversation the process it has not got, or say why it cannot have one.
 *
 * <p>Returns an empty string when there is nothing to do, which is every ordinary turn. A refusal is
 * the sentence the page shows: the model this conversation was having may no longer be configured,
 * its CLI may be gone, a Team server may have signed out. All three are the same three checks the
 * command makes before opening a tab at all — asked again here, because a reload can be a week and
 * a machine rebuild away from the conversation it is restoring.</p>
 */
async function reopened(thread: Thread): Promise<string> {
  if (!thread.reopen) {
    return '';
  }
  const config = vscode.workspace.getConfiguration('coai');
  const ready = readyToChat(config, thread.providerId, thread.modelId);
  if (!ready.ok) {
    return ready.refusal;
  }
  const cli = await cliFor(ready.vendor);
  if (cli.refusal.length > 0) {
    return cli.refusal;
  }
  const remote = isRemote(ready.vendor) ? await remoteFor(ready.vendor) : { session: undefined, refusal: '' };
  if (remote.refusal.length > 0) {
    return remote.refusal;
  }

  const opened = started(ready.vendor, cli.resolved, ready.modelId, remote.session);
  thread.session.dispose();
  thread.home.release();
  thread.session = opened.session;
  thread.home = opened.home;
  thread.providers = ready.providers;
  thread.providerId = ready.providerId;
  thread.modelId = ready.modelId;
  // The WHOLE memory object, not one field of it: a switch that set `forgetful` and forgot `asked`
  // is a defect this file has already had once, and the type is what stops it happening twice.
  Object.assign(thread, memoryOf(ready.vendor));
  thread.reopen = false;

  return '';
}

/**
 * One turn, start to finish.
 *
 * <p>The question is appended BEFORE the turn is sent, so the page shows it while the model is
 * thinking — nine measured seconds of silence otherwise look like a tab that ignored a keypress.</p>
 */
/** What a stopped turn leaves in the transcript, so it never ends on a dangling question. */
const STOPPED_ANSWER = '(you stopped this answer)';

/**
 * Ask the model chosen NOW the question the last answer was given to.
 *
 * <p>Entry 24. The conversation goes across MINUS that answer — an answer somebody rejected, handed
 * to the next model, is a model being asked to agree with it — and the question is re-sent verbatim,
 * because it is what they want answered again rather than re-typed.</p>
 *
 * <p>It goes through `oneTurn`, which is the point: a re-ask is a turn. Everything a turn already
 * does — the lock, the turn number a stop can name, the transcript, the model recorded on the
 * answer, the cap on a forgetful conversation — happens because this is not a second path.</p>
 */
async function oneReask(entry: ChatEntry, thread: Thread): Promise<void> {
  const again = reaskFrom(thread.messages, thread.providerId);
  if (again === undefined) {
    return;
  }
  // The rejected answer and its question leave the transcript: the question comes back as this
  // turn's own, and keeping the answer would show it twice in a conversation that has moved past it.
  thread.messages = thread.messages.slice(0, -2);
  // What the NEXT model is handed. `carry` is what `oneTurn` sends ahead of the question, and it is
  // exactly the conversation before the answer nobody wanted.
  thread.carry = [...again.said];
  await oneTurn(entry, again.question);
}

async function oneTurn(entry: ChatEntry, text: string): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  // A conversation that came back from a reload has no process yet. It is opened HERE, on the first
  // question and not before, so a window with five restored tabs starts nothing until one of them is
  // spoken to — and the transcript is already in `carry`, so the model that answers is handed the
  // whole thread exactly as it is after a model switch.
  const refused = await reopened(thread);
  if (refused.length > 0) {
    show(entry, false, refused);
    // And the question comes BACK. The page cleared its composer when it sent, so a refusal that
    // said only what was wrong would also have thrown away what the person typed — and they would
    // have to write it again to try the fix they were just told to make. (gemini, the code round.)
    pushChatDraft(entry, text);

    return;
  }
  // WITH what it was asked with. The conversation moves on — a model switched after the question
  // was sent would leave the words above it uncoloured, because the role in force is no longer the
  // one they were written with. (codex and local, the plan round.)
  thread.messages = [...thread.messages, {
    role: 'you',
    text,
    marks: {
      role: thread.role,
      task: taskOf(
        vscode.workspace.getConfiguration('coai'),
        thread.promptId,
        chatSettingsFrom((key) => vscode.workspace.getConfiguration('coai').get(key)).prompt,
      ),
    },
  }];
  thread.running = true;
  thread.turn += 1;
  show(entry, true, '');

  // A forgetful model is handed the conversation EVERY time, not only after a switch: the server
  // answers one question and forgets it, so turn three without the transcript is turn one wearing
  // a number. This is also why such a conversation is capped — the bill for turn N is the bill for
  // everything before it.
  if (thread.forgetful && thread.messages.length > 1) {
    thread.carry = thread.messages.slice(0, -1);
  }

  // What is SHOWN is what the person typed; what is SENT may carry the whole conversation with it,
  // because the process it is going to never heard any of it. Putting the carried version in the
  // transcript would print the entire history back at them under their own one-line question.
  const carrying = thread.carry;
  // Named, because a budget is the kind of thing that must be readable at a glance: a server takes
  // less than a pipe, and which one this conversation is is the whole difference.
  const budget = thread.forgetful ? REMOTE_CARRY_BUDGET : CARRY_BUDGET;
  // A picture goes with the question it was pasted for, and with that one only: the file is named
  // in the turn, the vendor process opens it, and the attachment is spent. Keeping it would send the
  // same screenshot with every question after it.
  const asked = thread.attachedPath.length > 0 ? imageTurn(text, thread.attachedPath) : text;
  const sent = carrying.length > 0 ? carriedTurn(carrying, asked, chatLanguage(), budget) : asked;

  // When the person asked, which ROW heard it, and which of that row's models — all taken BEFORE the
  // turn, because the ledger must name what actually answered rather than what is configured by the
  // time the answer lands.
  //
  // The row is `providerId` and the model is `modelId`, and keeping those apart matters here more
  // than anywhere: this file used to hold ONE id that was both, and a rebase onto the split brought
  // `vendorFor(thread.modelId)` — which looks a row up by a MODEL name, finds nothing, and made the
  // ledger silently record no turn at all. An empty `modelId` means "whatever the row is set to",
  // which is what the row itself says.
  const askedUtc = new Date().toISOString();
  const askedMs = Date.now();
  const answering = vendorFor(thread.providerId);
  const answeringModel = thread.modelId.length > 0 ? thread.modelId : (answering?.model ?? '');

  // The queue position, pushed as it changes. A local session never calls this back; a Team server
  // does on every poll, which is the difference between "the model is thinking" and "somebody else's
  // round has the vendor and you are fourth". Guarded by the flag, because a turn that finished
  // while a poll was in flight must not re-open the thinking line. (gemini, the code round.)
  const result = await thread.session.send(sent, (position) => {
    if (thread.running) {
      show(entry, true, '', position);
    }
  });
  thread.running = false;
  // Written down HERE, before either branch, so no way of ending a turn can skip it. A turn that was
  // stopped or that failed cost real money as surely as one that answered — the gate raised exactly
  // that against the plan, which recorded only answers — and it is the stopped ones a person hunting
  // for waste is looking for.
  ledger(thread, {
    utc: askedUtc,
    seconds: Math.round((Date.now() - askedMs) / 1000),
    vendor: answering,
    model: answeringModel,
    outcome: outcomeOf(result),
    // BOTH arms. A turn that was stopped or that fell over can still have been priced by its vendor
    // — `codex` sends its numbers on a line of their own — and those are the turns worth finding.
    usage: result.usage,
  });
  if (!result.ok) {
    // A STOPPED turn is written down before anything is carried, and the order is the whole finding.
    // The question was appended before the turn was sent, so a turn that ends without an answer
    // leaves the transcript ending on a dangling question. Handing THAT to a fresh process gives the
    // next model a question nobody answered with no sign it was abandoned — and the turn after it
    // appends a second `you` directly on top of the first. One line saying what happened makes the
    // transcript true, and it is only then worth carrying. (gemini, the plan round, Blocking.)
    if (result.stopped === true) {
      // One line, deliberately: a structural guard in `chatWiring.test.ts` reads the ORDERING here
      // as a regex, and breaking the append across lines breaks its pattern without changing what it
      // guards. The ordering is load-bearing and the guard is right to watch it.
      thread.messages = [...thread.messages, { role: 'model', text: STOPPED_ANSWER, model: answeredBy(thread) }];
    }
    // And only a vendor that LOST the conversation needs it re-sent. `contextLost` on the failure arm
    // is the session saying which of the two it is: a killed process that held the thread says yes, a
    // per-turn vendor resuming by id and a server that never remembered anything say nothing. Set
    // after the line above, so what is carried is the transcript a reader would recognise.
    if (result.contextLost === true) {
      thread.carry = [...thread.messages];
    }
    // The carry is NOT cleared here. A turn that failed carried nothing anywhere, and clearing it
    // would mean the retry — the same question, one keypress later — reaches the new model with no
    // conversation behind it, which is the exact thing the switch existed to prevent. (gemini, the
    // plan round, twice.)
    show(entry, false, result.failure);

    return;
  }
  // Cleared only now, and only once: from here the process remembers, and nobody pays to re-send a
  // conversation twice. `thread.carry` rather than `carrying` — a switch may have queued another.
  if (thread.carry === carrying) {
    thread.carry = [];
  }

  // A restart is said in the transcript, not only in a flag nobody sees: the answer genuinely does
  // not remember the earlier turns, and a reader comparing it with them deserves to know why. A
  // model the person SWITCHED to is not this case — that one was handed the conversation.
  const answer = result.contextLost === true
    ? `(the conversation restarted — this answer does not remember the earlier ones)\n\n${result.answer}`
    : result.answer;
  // Recorded from the model that ANSWERED, at the moment it did. Reading the setting later would
  // relabel every earlier answer the day somebody switches models, and switching mid-conversation
  // is a shipped feature — the thread is carried across, so one tab routinely holds two.
  thread.messages = [...thread.messages, { role: 'model', text: answer, model: answeredBy(thread) }];
  thread.asked += 1;
  show(entry, false, '');
}

/** Which of the three words describes how this turn ended. */
function outcomeOf(result: TurnResult): ChatOutcome {
  if (result.ok) {
    return 'answered';
  }

  return result.stopped === true ? 'stopped' : 'failed';
}

/**
 * Write one finished turn to the chat usage ledger, and remember what its vendor said.
 *
 * <p>Deliberately not awaited by the turn: an answer must not wait on a disk, and a ledger that
 * cannot be written costs its own line and nothing else (`chatUsageFile.ts` says so and swallows
 * nothing).</p>
 *
 * <p><b>A vendor row that has since been deleted writes nothing.</b> `vendorFor` reads the settings
 * fresh, so a person who removed the row mid-turn leaves this without a provider or a model to name —
 * and a record whose provider is empty cannot be priced, cannot be filtered and cannot be recognised.
 * Skipping it loses one line; inventing one would put a row on the page that means nothing.</p>
 */
function ledger(
  thread: Thread,
  turn: {
    readonly utc: string;
    readonly seconds: number;
    readonly vendor: Vendor | undefined;
    /** Which of that row's models answered — the row's own when the tab named none. */
    readonly model: string;
    readonly outcome: ChatOutcome;
    readonly usage: ReportedUsage | undefined;
  },
): void {
  if (turn.vendor === undefined) {
    return;
  }
  // The same number the ledger writes down, counted as it goes. `chatUsage` decides what a turn
  // cost; this only adds them up, so the line in the tab and the line in the log cannot disagree.
  thread.spend = [
    ...thread.spend,
    {
      costUsd: turn.usage?.costUsd ?? null,
      // What a vendor CHARGED is a bill; what this product worked out from tokens is not, and the
      // three vendors do not even count tokens the same way. Only claude reports its own cost.
      estimated: turn.vendor.id !== 'claude',
    },
  ];
  void recordChatTurn(coaiDataDir(), chatTurnRecord({
    utc: turn.utc,
    provider: turn.vendor.id,
    model: turn.model,
    conversation: thread.saveId,
    title: thread.title,
    seconds: turn.seconds,
    outcome: turn.outcome,
    // Already the cost of ONE turn: the session differenced it, because a session's life is exactly
    // its vendor thread's life and nothing else here can say that. See `cliChatSession.perTurnUsage`.
    usage: turn.usage,
  }));
}

/**
 * Everything needed to start one conversation, or the sentence saying why it cannot start.
 *
 * <p>Two arms rather than one shape with an empty field. The single-shape version had to put
 * SOMETHING in `vendor` on the refusal path and reached for `vendors[0] as Vendor` — a cast that is
 * `undefined` whenever the person has no vendors configured at all, and a promise to keep a shape
 * by hand that comes due the first time somebody reads the field before checking the sentence.
 * The compiler keeps that promise instead. (gemini, the code round.)</p>
 */
type Ready =
  | {
    readonly ok: true;
    readonly vendor: Vendor;
    readonly models: readonly ChatModelChoice[];
    /** Every row that can answer, each with its own models — what the picker offers. */
    readonly providers: readonly ChatProvider[];
    /** The row that answers. */
    readonly providerId: string;
    /** Which of that row's models, or empty for whatever the row is set to. */
    readonly modelId: string;
  }
  | { readonly ok: false; readonly refusal: string };

/**
 * What each row may be pointed at.
 *
 * <p><b>Discovery is not here, and that is the honest limit of this step.</b> Three of the four
 * sources are FETCHED rather than read: a local engine's models and the codex and agy CLIs' own
 * lists are discovered by asking the machine, and a Team server's allowlist is fetched from the
 * server. All three live in the panel, which has already done that work and holds the answers; the
 * chat command has no such state and starting subprocesses or HTTP calls to open a tab would trade
 * the operator's complaint for a slower one.</p>
 *
 * <p>So a row whose list must be fetched offers the model it is CONFIGURED to and nothing else —
 * exactly what the flat list offered before, which makes this strictly not worse. What gains a real
 * choice today is the one source that needs no fetching: Claude's curated three. Handing the panel's
 * discovered lists to this function is the plan's open tail, and it is written down as one.</p>
 */
/**
 * What a row can be pointed at, with the panel's discoveries in it.
 *
 * <p>This used to pass every discovered list EMPTY, with a comment saying the fetches live in the
 * panel — true, and harmless while the panel offered a flat list of rows. The moment the panel
 * offered a two-step picker over those discoveries it became a defect: somebody picks a model that
 * exists only in what `agy models` answered, and this catalog has never heard of it, so the
 * conversation opens on the row's own model instead. The panel leaves what it found in
 * `DISCOVERY_KEY`; this reads it back. Nothing is fetched here — a command must not wait on three
 * probes to open a tab.</p>
 */
function chatCatalogFrom(config: vscode.WorkspaceConfiguration): ChatCatalog {
  const discovery = hostContext === undefined
    ? EMPTY_DISCOVERY
    : discoveryFrom(hostContext.globalState.get(DISCOVERY_KEY));

  return catalogUsing(discovery, teamServersFrom(config.get('teamServers')));
}

/**
 * The saved prompts, carrying the migration with them.
 *
 * <p>One reader, because two call sites reading the same settings is two places to forget the second
 * argument — and the second argument IS the migration: without it a person's own prompt, the one
 * they have been editing since the chat shipped, simply does not appear.</p>
 */
function savedPrompts(config: vscode.WorkspaceConfiguration): readonly PromptPreset[] {
  const legacy = config.get('chatPrompt');

  return chatPromptPresetsFrom(
    config.get('chatPromptPresets'),
    typeof legacy === 'string' ? legacy : '',
  );
}

function savedModels(config: vscode.WorkspaceConfiguration): readonly ModelPreset[] {
  return chatModelPresetsFrom(config.get('chatModelPresets'));
}

/**
 * The ROLE this conversation's model carries, or none.
 *
 * <p>A model preset's starting prompt — "you are an architect of distributed systems" — is a
 * standing fact about who is answering, so it belongs to the model rather than to a turn, and it is
 * read from the model in force rather than remembered separately.</p>
 */
function roleOf(config: vscode.WorkspaceConfiguration, providerId: string): string {
  return savedModels(config).find((one) => one.id === providerId)?.startingPrompt ?? '';
}

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
  const swapped = draft === undefined ? undefined : reinstructed(draft, was, now);
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
      void vscode.window.showInformationMessage(
        `${preset.name} opens with its own prompt, and the box holds something you wrote — so it was left alone.`,
      );
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

/**
 * The prompt a conversation OPENS on: the one ticked main, or the first when nothing is ticked.
 *
 * <p>A preset list is the configuration now — *"Пресет И ЕСТЬ конфиг"* — so "which prompt does a
 * capture use" is answered by the tick in that list and not by a separate setting. A tab that opened
 * on none of them showed a row of buttons with nothing pressed and sent something the row did not
 * name.</p>
 */
function openingPrompt(config: vscode.WorkspaceConfiguration): string {
  return mainPrompt(savedPrompts(config))?.id ?? '';
}

/**
 * The TASK this conversation is asking for: the prompt button pressed in it, or the panel's choice.
 */
function taskOf(config: vscode.WorkspaceConfiguration, promptId: string, fallback: string): string {
  if (promptId.length === 0) {
    return fallback;
  }

  return savedPrompts(config).find((one) => one.id === promptId)?.text ?? fallback;
}

/**
 * A value saved before the pair existed, read as the pair it always was.
 *
 * <p>`coai.chatModel` and a restored tab's `modelId` have always held a ROW id — both predate the
 * two-step choice — so passing either as a MODEL would look for a model of that name and find
 * nothing. `legacyPick` is the pure half's function for exactly this, and it is called through one
 * place so the two callers cannot drift.</p>
 */
function savedPick(config: vscode.WorkspaceConfiguration, saved: string): LegacyPick {
  const specs = savedModels(config).map(chatRunSpec);

  return legacyPick(chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config)), specs, saved);
}

/**
 * The row that will answer, and which of its models.
 *
 * <p>A PROVIDER is a vendor row, not a runtime, and that was settled by measurement rather than by
 * preference: three vendors' reviewers independently overturned the plan's recommendation on the
 * pure half's round. The row carries the runtime, the executable, the base URL, the price and — for
 * a Team server — the server and the vendor name on it, so it is the identity a saved choice stores
 * and the identity resolution looks up.</p>
 *
 * <p>A row the person NAMED and which cannot answer is refused BY NAME — never quietly replaced by
 * another vendor's, which is somebody else's model, billed, in a voice nobody chose.</p>
 */
function readyToChat(
  config: vscode.WorkspaceConfiguration,
  askedProvider: string,
  askedModel: string,
): Ready {
  const specs = savedModels(config).map(chatRunSpec);
  const list = chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config));
  const pick = resolveChatPick(specs, list, askedProvider, askedModel);
  if (!pick.ok) {
    return { ok: false, refusal: pick.refusal };
  }

  const refusal = chatRuntimeRefusal(pick.row);

  return refusal.length > 0
    ? { ok: false, refusal }
    : {
      ok: true,
      vendor: pick.row,
      models: chatModelsFrom(specs).offered,
      providers: list.providers,
      providerId: pick.row.id,
      modelId: pick.model,
    };
}

/** The clipboard, as `selectionCapture` wants it. VS Code answers a Thenable, not a Promise. */
const hostClipboard = {
  read: async (): Promise<string> => vscode.env.clipboard.readText(),
  write: async (value: string): Promise<void> => {
    await vscode.env.clipboard.writeText(value);
  },
};

/**
 * Where the passage comes from, and a status line while it is being fetched.
 *
 * <p>The keybinding path takes about 1.7 seconds — PowerShell's own startup, mostly — and until the
 * tab appears there is nothing at all to see. A person who presses a shortcut and watches nothing
 * happen presses it again, which is how one question becomes two. The menu path is instant and says
 * nothing.</p>
 *
 * <p>The reach is asked once, here, and handed down: `captureSelection` is pure enough to be tested
 * against every side of the machine precisely because it does not go looking for one. Through interop
 * the round trip is ~1 s rather than the ~1.7 s this label was written for, so the label and the cap
 * both stand as they are.</p>
 */
async function passageFor(path: 'menu' | 'keyboard'): Promise<{ text: string; failure: string }> {
  if (path === 'menu') {
    return hostClipboard.read().then((text) => ({ text, failure: '' }));
  }
  const reach = await windowsReach();

  return vscode.window.withProgress(
    { location: vscode.ProgressLocation.Window, title: 'Copying the selection…' },
    () => captureSelection(pressCopy, hostClipboard, reach),
  );
}

/** Which conversation this belongs to, or nothing when the active tab is not an eligible source. */
function matchedSession(
  panels: ChatPanels,
  eligible: (tab: TabSnapshot) => boolean = isClaudeSessionTab,
): ReturnType<typeof sourceSession> {
  const { active, all } = snapshots();

  return sourceSession(active, all, panels.known(), eligible);
}

/**
 * The passage from the active editor — and the one question this door asks before it sends.
 *
 * <p>A SELECTION goes without a word, however large: choosing it was the choice. An empty selection
 * sends the whole document, which the operator chose over a refusal — but over a bound it asks
 * first, because the accident this guards against is the chord pressed to focus a window, in a
 * minified bundle, becoming a paid turn nobody meant. Cancelling is not an error and says nothing
 * more: the person has just been asked and has just answered.</p>
 */
async function fromTheEditor(): Promise<{ text: string; failure: string }> {
  const editor = editorText();
  const passage = passageFromEditor(editor);
  if (!passage.ok) {
    return { text: '', failure: passage.refusal };
  }
  if (!passage.whole || editor === undefined) {
    return { text: passage.text, failure: '' };
  }
  const ask = confirmWholeFile(editor);
  if (ask.length === 0) {
    return { text: passage.text, failure: '' };
  }
  const said = await vscode.window.showWarningMessage(ask, { modal: true }, 'Send all of it');

  return said === 'Send all of it' ? { text: passage.text, failure: '' } : { text: '', failure: ' ' };
}

/** The active editor, narrowed to the two strings the decision needs. */
function editorText(): EditorText | undefined {
  const editor = vscode.window.activeTextEditor;
  if (editor === undefined) {
    return undefined;
  }

  return {
    whole: editor.document.getText(),
    selected: editor.document.getText(editor.selection),
    // The file name rather than the path: it is for a sentence a person reads, and the path is
    // already in the tab they are looking at.
    name: editor.document.uri.path.split('/').pop() ?? 'this file',
  };
}

/**
 * A vendor process in an empty directory of its own, and the directory, held together.
 *
 * <p>The launch is built per TURN rather than once, because a vendor that keeps no process needs a
 * different command line for its second question than for its first: `codex` resumes a thread by
 * id, and the id is not known until the first turn has been answered. A persistent vendor ignores
 * the argument entirely and gets the same argv every time.</p>
 */
function started(
  vendor: Vendor,
  resolved: string,
  /**
   * The model this conversation is on — `modelId`, never `vendor.model`. The row's model is the
   * default a person is choosing away from, and handing it here would relabel every answer with a
   * model the CLI was never told about. (Audit finding 7 of 2026-09-09.)
   */
  model: string,
  remote?: ChatSession,
): { session: ChatSession; home: ChatHome } {
  if (remote !== undefined) {
    // A Team server needs no process and no directory: the home is a stub whose release does
    // nothing, so the rest of this file does not have to know which kind it holds.
    return { session: remote, home: { dir: '', release: () => undefined } };
  }
  const home: ChatHome = emptyTempDir();
  const adapter = adapterFor(vendor.runtime);

  return {
    session: new CliChatSession(
      chatProcessFor(vendor, home.dir, resolved, model),
      DEFAULT_BUDGETS,
      REAL_TIMERS,
      adapter,
    ),
    home,
  };
}

/**
 * The FILE this vendor's CLI is, or the sentence saying it could not be found.
 *
 * <p>`spawn` searches neither PATHEXT nor the shell's own rules, so a bare name that every terminal
 * resolves fails here with `ENOENT`. Asked once, before anything is created, so a missing CLI is a
 * message about a missing CLI rather than a conversation that dies at its first turn.</p>
 */
/**
 * The conversation for a REMOTE row, or the sentence saying why there is none.
 *
 * <p>Two things can be missing and they are different sentences: the server the row belongs to may
 * have been removed from the settings, and this side may not be signed in to it. A person can act
 * on either, and neither is "the model did not answer".</p>
 */
async function remoteFor(vendor: Vendor): Promise<{ session: ChatSession | undefined; refusal: string }> {
  const servers = teamServersFrom(vscode.workspace.getConfiguration('coai').get('teamServers'));
  const server: TeamServer | undefined = servers.find((one) => rowBelongsTo(vendor, one));
  if (server === undefined) {
    return { session: undefined, refusal: `${vendor.id} belongs to a Team server this side no longer has.` };
  }
  const token = await readToken(coaiDataDir(), server.url);
  // The whole server, not just its URL: its id is half of this row's own name, so it is what tells
  // the row's id apart from the vendor name the server actually knows. See `serverVendorOf`.
  const session = remoteChatFor(vendor, server, token);

  return session === undefined
    ? { session: undefined, refusal: `Sign in to ${server.name} to chat with ${vendor.id}.` }
    : { session, refusal: '' };
}

async function cliFor(vendor: Vendor): Promise<{ resolved: string; refusal: string }> {
  if (isRemote(vendor)) {
    // A Team server has no executable to find. Saying so here keeps the caller's shape: one
    // question, one refusal, before anything is created.
    return { resolved: '', refusal: '' };
  }
  const asked = vendor.executablePath.length > 0 ? vendor.executablePath : defaultExecutableFor(vendor.runtime);
  const resolved = await resolvedExecutable(asked);

  return resolved.length > 0
    ? { resolved, refusal: '' }
    : {
      resolved: '',
      refusal: `${asked} could not be found. Install the ${vendor.runtime} CLI, or put its full path in that reviewer's settings.`,
    };
}

/** The vendor row behind a model id, read fresh — the person may have edited settings since. */
/**
 * How to RUN the preset with this id — built from the preset itself, never from a reviewer row.
 *
 * <p>This looked the id up in `coai.vendors`, which is what made the chat depend on the review gate:
 * a reviewer switched off, renamed or removed took a conversation with it. A preset carries its own
 * vendor, model, CLI path and endpoint, and `chatRunSpec` shapes them into the `Vendor` every
 * launcher, session and Team-server client here was already written against.</p>
 */
function vendorFor(presetId: string): Vendor | undefined {
  const preset = savedModels(vscode.workspace.getConfiguration('coai')).find((one) => one.id === presetId);

  return preset === undefined ? undefined : chatRunSpec(preset);
}

/**
 * The person chose a different model in the open tab.
 *
 * <p>A conversation is a process, so this is a new process — but not a new conversation. The whole
 * transcript, questions and answers both, is handed to the next turn (`carriedTurn`), because that
 * is the only way context crosses a process boundary here. Asked for directly by the owner, and the
 * alternative shipped for about an hour: a switch that started again and said so.</p>
 *
 * <p>It waits for a turn in flight instead of killing it. The page disables its composer while the
 * model is thinking but not its picker, and disposing the session under a running turn would fail
 * that turn with "the conversation was closed" — an error about something the person did on
 * purpose. The switch simply joins the queue the turns already run in.</p>
 */
function switchModel(entry: ChatEntry, providerId: string, asked: string): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return Promise.resolve(false);
  }
  const provider = thread.providers.find((one) => one.id === providerId);
  if (provider === undefined) {
    // A saved preset can name a row that has since been removed or switched off. Said out loud, like
    // every other refusal on this path — a button that does nothing and explains nothing is the
    // defect this whole change started from. (CodeRabbit, PR #200.)
    const refusal = `${providerId} is not a reviewer this conversation can be sent to any more.`;
    void vscode.window.showWarningMessage(refusal);
    show(entry, false, refusal);

    return Promise.resolve(false);
  }
  // An EMPTY model is the page saying the provider moved: which of the new row's models answers is
  // decided here, because this side holds the catalog. The row's own configured model, which is what
  // every other entry point into this feature falls back to.
  // A model that was NAMED and is not offered is REFUSED, never swapped for another — four reviewers
  // across three vendors on one round, and they were right: a person presses a button labelled
  // `gpt-5.6-luna`, a catalog moves under them, and the turns go somewhere else. The fallback belongs
  // to the EMPTY ask alone, which is the page saying the provider moved.
  const running = modelToRun(provider.models, asked, vendorFor(providerId)?.model ?? '');
  if (!running.ok) {
    void vscode.window.showWarningMessage(running.refusal);
    // The page has already moved its own select; put the state back so it stops claiming otherwise.
    show(entry, false, running.refusal);

    return Promise.resolve(false);
  }
  const modelId = running.model;
  if (thread.providerId === providerId && thread.modelId === modelId) {
    // ALREADY the one answering, which is not a refusal: there is nothing to switch, and everything
    // else pressing that button means still applies. It used to answer `false` like a refusal, so a
    // model preset pressed while its own model was already chosen did nothing at all.
    return Promise.resolve(true);
  }
  if (thread.running) {
    void vscode.window.showInformationMessage(
      `Switching to ${modelId} as soon as the current answer arrives.`,
    );
  }
  // The ANSWER is what the queued switch did, not that it was queued. A caller that acts on the
  // switch — a model preset putting its own prompt in the composer — would otherwise act while
  // `switchNow` was still to find that the CLI is not installed, and offer those words to the model
  // that is still answering. (CodeRabbit, PR #200.)
  const done = thread.turns
    .then(() => switchNow(entry, providerId, modelId))
    .catch((reason: unknown) => {
      // Not swallowed: a switch that failed leaves the thread on the OLD model, and a person who
      // believes otherwise reads the next answer as the new model's. (codex, the code round.)
      const failure = `The chat could not switch to ${modelId}: ${asText(reason)}`;
      void vscode.window.showWarningMessage(failure);
      show(entry, false, failure);

      return false;
    });
  thread.turns = done;

  return done;
}

async function switchNow(entry: ChatEntry, providerId: string, modelId: string): Promise<boolean> {
  const thread = threads.get(entry.id);
  if (thread === undefined || (thread.providerId === providerId && thread.modelId === modelId)) {
    return false;
  }
  // The PROVIDER is the row — it carries the runtime, the executable, the base URL, the price and,
  // for a Team server, the server and the vendor name on it. This used to look the row up by the
  // MODEL id, which is what it meant before a provider and a model were two questions.
  const vendor = vendorFor(providerId);
  const refusal = vendor === undefined
    ? `The reviewer ${providerId} is no longer configured.`
    : chatRuntimeRefusal(vendor);
  if (vendor === undefined || refusal.length > 0) {
    void vscode.window.showWarningMessage(refusal);
    // The page has already moved its own select; put the state back so it stops claiming otherwise.
    show(entry, false, refusal);

    return false;
  }

  // The same resolution the first launch did, because the model being switched TO may be a vendor
  // whose CLI is not installed — and finding that out by spawning it would kill a conversation that
  // was working a moment ago.
  const cli = await cliFor(vendor);
  const remote = isRemote(vendor) ? await remoteFor(vendor) : { session: undefined, refusal: '' };
  const cannot = cli.refusal.length > 0 ? cli.refusal : remote.refusal;
  if (cannot.length > 0) {
    void vscode.window.showWarningMessage(cannot);
    show(entry, false, cannot);

    return false;
  }

  thread.session.dispose();
  thread.home.release();
  const replacement = started(vendor, cli.resolved, modelId, remote.session);
  thread.session = replacement.session;
  thread.home = replacement.home;
  thread.providerId = providerId;
  thread.modelId = modelId;
  // The ROW as well as the session: a model chosen in the dropdown below the buttons is still a
  // model chosen, and the button naming it has to look it.
  thread.chosenId = providerId;
  // The memory rules move WITH the model. Left behind, they described the one just thrown away:
  // switching to a Team server kept `forgetful` false, so the server — which remembers nothing —
  // was asked turn two with no transcript behind it and the three-turn cap never applied; switching
  // back kept it true, so a CLI that HAS a memory was refused a fourth question. Spread from the one
  // function both callers use, so a field added there cannot be applied here and forgotten there.
  // (codex, the code round.)
  Object.assign(thread, memoryOf(vendor));
  // Everything said so far travels with the next question. Not sent now: nobody should be billed
  // for a conversation they moved and then never continued. Taken HERE rather than when the switch
  // was asked for, because this runs after the turn queue has drained — so an answer that was still
  // arriving when the person changed their mind is in the transcript by now. (codex, the plan round.)
  thread.carry = [...thread.messages];
  show(entry, false, '');
  // Said out loud: the next question costs more than the last one, because it carries everything
  // above it. A person who is not told reads the first answer as a model that mysteriously knows.
  void vscode.window.showInformationMessage(
    `Now asking ${modelId}. Your next question carries this conversation across to it.`,
  );

  return true;
}

/** Everything one new conversation is made of. Called ONLY when a tab has no panel yet. */
function newConversation(
  panels: ChatPanels,
  ready: Extract<Ready, { ok: true }>,
  state: { readonly title: string; readonly passage: string; readonly draft: string },
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
    providerId: ready.providerId,
    modelId: ready.modelId,
    // The MAIN prompt, and the chosen model's own role: what a capture opens on, with both buttons
    // drawn pressed. Asked for with a screenshot of a tab that had opened on neither.
    promptId: openingPrompt(config),
    role: roleOf(config, ready.providerId),
    chosenId: ready.providerId,
    presses: 0,
    ourDraft: state.draft,
    running: false,
    // Counted from 1 by the first turn, so 0 is "this conversation has not asked anything yet" and
    // can never be mistaken for a turn a stop could name.
    turn: 0,
    ...memoryOf(ready.vendor),
    carry: [],
    messages: [],
    turns: Promise.resolve(),
    saveId,
    title: state.title,
    reopen: false,
  });

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
          void vscode.window.showWarningMessage(gone);
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
          void vscode.window.showInformationMessage(
            `${preset.name} replaces the instruction, and the box holds something you wrote — so it was left alone.`,
          );
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
          void vscode.window.showWarningMessage(gone);
          show(found, mine.running, gone);

          return;
        }
        chooseModel(found, mine, preset, draft, config);
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
      onRestart: () => undefined,
      onUseLocal: () => undefined,
      onPageError: (_id, message) => {
        void vscode.window.showWarningMessage(`The chat page reported: ${message}`);
      },
      onOpenFile: (id, requested, line) => {
        void openWorkspaceFile(id, requested, line, (message) => {
          void vscode.window.showWarningMessage(message);
        });
      },
      onCopyAnswer: (id, index) => {
        // The SOURCE, out of the thread the page was rendered from. A person copying an answer wants
        // the markdown they can paste into a plan or an issue, and that is the one thing selecting
        // the page cannot give them - a selection gives what the page shows.
        const said = threads.get(id)?.messages[index];
        if (said === undefined || said.role !== 'model') {
          return;
        }
        void vscode.env.clipboard.writeText(said.text);
      },
  };
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
 * The name of the model that a re-ask would go to, or empty when there is nothing to re-ask.
 *
 * <p>The LABEL rather than the id, because it goes on a button a person reads. `reaskFrom` decides
 * whether there is anything to re-ask at all; this only names who would take it.</p>
 */
function reaskLabel(thread: Thread): string {
  return reaskFrom(thread.messages, thread.providerId) === undefined ? '' : answeredBy(thread).label;
}

/**
 * Which model a turn was answered by, as the page will caption it.
 *
 * <p>The label is the one the picker offered, so the caption reads as the name a person chose from
 * rather than an id. A model that is not in the list any more — a Team server that withdrew it — is
 * still named by its id: what answered is a fact about the past, and the page's job is to say it.</p>
 */
function answeredBy(thread: Thread): AnsweredBy {
  const chosen = thread.models.find((model) => model.id === thread.modelId);
  // An empty label is not a label. `??` keeps one, and the caption would then fall through to
  // "The other AI" for a model whose id was known all along. (gemini, the code round.)
  const named = chosen?.label.trim() ?? '';

  return { id: thread.modelId, label: named.length > 0 ? named : thread.modelId };
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
  saved: SavedTab,
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
      marks: {
        role: presets.modelPresets.find((one) => one.id === (ready.ok ? ready.providerId : restored.providerId))?.startingPrompt ?? '',
        task: mainPrompt(presets.promptPresets)?.text ?? '',
        service: serviceLines(chatLanguage()),
      },
      uiScale: chatUiScale(),
      textTone: chatTextTone(),
    };
}

export function restoreConversation(
  panels: ChatPanels,
  panel: vscode.WebviewPanel,
  saved: SavedTab,
  extensionUri: vscode.Uri,
): void {
  const config = vscode.workspace.getConfiguration('coai');
  const restored = savedPick(config, saved.modelId);
  const presets = { promptPresets: savedPrompts(config), modelPresets: savedModels(config) };
  const ready = readyToChat(config, restored.providerId, restored.modelId);
  // A dead session, and the page can never reach it: `reopened` replaces it before the first turn is
  // sent. It answers rather than throws, because a `ChatSession` that rejects is a contract this
  // codebase does not have — every failure here is a sentence.
  const closed: ChatSession = {
    send: () => Promise.resolve({ ok: false, failure: reloadedNote(saved.modelId) }),
    stop: () => undefined,
    dispose: () => undefined,
  };
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
    running: false,
    turn: 0,
    // Replaced by `reopened` with the rules of whatever model actually answers — this conversation
    // asks nothing until then, so neither field is consulted before it is right. Written out rather
    // than taken from `memoryOf`, which needs a vendor, and a restored tab has none yet.
    forgetful: false,
    asked: 0,
    // The whole transcript, ready to travel with the first question — the same handover a model
    // switch performs, and the reason nothing has to resume a vendor thread.
    carry: [...saved.messages],
    messages: [...saved.messages],
    turns: Promise.resolve(),
    saveId: saved.id,
    title: saved.title,
    reopen: true,
  });
  // Its own key: the tab this conversation was opened FROM may be gone, may be a different object,
  // or may already hold a live conversation of its own. A restored tab is its own thing until
  // somebody closes it.
  panels.open({}, saved.title, () => entry);
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
): Promise<void> {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = chatSettingsFrom((key) => config.get(key));
  // THE TICKED MODEL, and the saved setting only when nothing is ticked. A list nobody has ticked
  // must not overrule a model the person named in the panel, and `legacyPick` already answers "that
  // id names nothing" with the first provider.
  //
  // A ticked preset is NAMED, though, so it does not go through that fallback: `legacyPick` would
  // quietly open the first provider instead, which is a different vendor's model, billed, in a voice
  // nobody chose — the rule this feature keeps everywhere else. `readyToChat` refuses it by name.
  // (codex, the second code round.)
  const ticked = mainModel(savedModels(config));
  const saved = ticked === undefined ? savedPick(config, settings.model) : undefined;
  const opening = saved ?? { providerId: ticked?.id ?? '', modelId: ticked?.model ?? '' };
  // The panel names a provider AND, since the pair reached it, one of that provider's models. A name
  // it does not offer is not a pick — the row's own model answers — so a value gone stale in
  // `settings.json` or withdrawn by a Team server opens a conversation rather than a refusal.
  // A TICKED PRESET BRINGS ITS OWN MODEL. `coai.chatModelName` is the panel's answer to the same
  // question from before presets existed, and it was winning: a button labelled "Gemini 3.7 Flash
  // (High)" opened a conversation whose picker said 3.8 Flash (Medium), because the panel's saved
  // name was one this vendor also offers. The preset is the configuration now.
  const model = saved === undefined
    ? opening.modelId
    : openingModel(chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config)), saved, settings.modelName);
  const ready = readyToChat(config, opening.providerId, model);
  if (!ready.ok) {
    void vscode.window.showWarningMessage(ready.refusal);

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
  const match = matchedSession(panels);
  const fromFile = match === undefined ? matchedSession(panels, isOrdinaryEditorTab) : undefined;
  const source = match ?? fromFile;
  if (source === undefined) {
    void vscode.window.showWarningMessage(
      'Open this from a Claude Code session tab or from a file — the conversation is named after it.',
    );

    return;
  }
  if (source.kind === 'rekey') {
    panels.rekey(source.from, source.key);
  }

  const plan = triggerPlan(args, settings.autoSend);
  const passage = match === undefined ? await fromTheEditor() : await passageFor(plan.path);
  if (passage.text.trim().length === 0) {
    void vscode.window.showWarningMessage(
      passage.failure.length > 0 ? passage.failure : 'Nothing to explain — copy the text first.',
    );

    return;
  }

  await deliverPassage(panels, extensionUri, ready, source, passage, plan.send);
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
    void vscode.window.showWarningMessage(cli.refusal);

    return;
  }
  const remote = isRemote(ready.vendor) ? await remoteFor(ready.vendor) : { session: undefined, refusal: '' };
  if (remote.refusal.length > 0) {
    void vscode.window.showWarningMessage(remote.refusal);

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
  }, cli.resolved, remote.session, extensionUri));

  opened.entry.panel.reveal();
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
 * Take the question Claude Code is asking and hand it to a second model.
 *
 * <p>Asked for as *"перехватывать целиком что спрашивает Клод, потому что сейчас приходится делать
 * скриншоты"*. The screenshots are not a habit — they are the only way, because the question widget
 * cannot be selected: a select-all in that panel highlights the transcript above it and stops at the
 * widget's edge. So this reads the question off the session file Claude Code writes, where it is
 * text, with every option and every description.</p>
 *
 * <p>It goes to the same place a captured paragraph goes: the conversation named after the tab it
 * came from, fenced as MATERIAL like any other passage. Which means the second model sees the
 * question, sees every option, and is never asked to obey any of it.</p>
 */
export async function takeTheQuestion(
  panels: ChatPanels,
  extensionUri: vscode.Uri,
): Promise<void> {
  const config = vscode.workspace.getConfiguration('coai');
  const settings = chatSettingsFrom((key) => config.get(key));
  const ticked = mainModel(savedModels(config));
  const saved = ticked === undefined ? savedPick(config, settings.model) : undefined;
  const opening = saved ?? { providerId: ticked?.id ?? '', modelId: ticked?.model ?? '' };
  const model = saved === undefined
    ? opening.modelId
    : openingModel(chatProvidersFromPresets(savedModels(config), chatCatalogFrom(config)), saved, settings.modelName);
  const ready = readyToChat(config, opening.providerId, model);
  if (!ready.ok) {
    void vscode.window.showWarningMessage(ready.refusal);

    return;
  }
  // FROM THE PANEL, like the capture beside it — the conversation is named after the tab, and this
  // question belongs to the session in that tab.
  const source = matchedSession(panels);
  if (source === undefined) {
    void vscode.window.showWarningMessage(
      'Open this from a Claude Code session tab — the question and the conversation both belong to it.',
    );

    return;
  }
  if (source.kind === 'rekey') {
    panels.rekey(source.from, source.key);
  }
  const folder = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath ?? '';
  if (folder.length === 0) {
    void vscode.window.showWarningMessage('Open a folder first — a session belongs to one.');

    return;
  }
  const waiting = waitingQuestion(os.homedir(), folder);
  if (waiting.kind === 'failed') {
    void vscode.window.showWarningMessage(waiting.refusal);

    return;
  }
  if (waiting.kind === 'none') {
    void vscode.window.showWarningMessage('Claude Code has asked nothing in this folder yet.');

    return;
  }
  if (waiting.kind === 'answered') {
    // A DIFFERENT SENTENCE from "nothing was asked". Handing a second model a question that is
    // already settled is the worst outcome this command has, so it is refused by default rather
    // than offered quietly. (local, three findings on the plan round.)
    void vscode.window.showWarningMessage(
      'The last question in this session has already been answered — there is nothing waiting.',
    );

    return;
  }
  if (waiting.kind === 'several') {
    // NEVER a pick. The host cannot see into Claude Code's webview, so it cannot tell which tab is
    // being looked at; a plausible guess here delivers somebody else's question, silently, which is
    // the failure `sessionKey.ts` spends its whole design preventing.
    void vscode.window.showWarningMessage(
      `${waiting.sessions.length} Claude Code sessions in this folder are waiting for an answer.`
      + ' Close the ones you do not mean, and press it again.',
    );

    return;
  }

  await deliverPassage(
    panels,
    extensionUri,
    ready,
    source,
    { text: askedAsText(waiting.session.asked) },
    // NEVER sent by itself, whatever the auto-send setting says. A question taken off disk is one a
    // person is in the middle of answering; it goes into the composer so they can look at it, add
    // what they think, and press send themselves.
    false,
  );
}
