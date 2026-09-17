import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { randomUUID } from 'node:crypto';
import * as vscode from 'vscode';
import { isInside } from './chatMessages';
import { notify, notifyAndAsk } from './notify';
import { acknowledgement, answerToCopy, blockToCopy } from './answerCopy';
import { textCopier, type CopyDecision, type CopyReport } from './copyText';
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
  presetInForce,
  reaskFrom,
  retryFrom,
} from './chatPresets';
import { ARCHIVED, Freshened, UNSAVED, couldNotEnd, freshened, sameSlate } from './chatFresh';
import { ChatTabMemory, reloadedNote } from './chatTabs';
import { CONVERSATION_VERSION, ConversationRecord, ConversationSource, metaOf, sourceOfFile, sourceOfSession } from './chatStore';
import { ChatStoreFile, SaveOutcome } from './chatStoreFile';
import { ConversationIndex } from './chatStoreCache';
import { CONTINUED_ELSEWHERE, WriteNext, nextAfterSave } from './chatStoreWrite';
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
import { Door, chatDoorRecord } from './chatDoors';
import { recordChatDoor } from './chatDoorsFile';
import { DISCOVERY_KEY, EMPTY_DISCOVERY, catalogUsing, discoveryFrom } from './chatDiscovery';
import { chatSettingsFrom } from './chatSettings';
import { CARRY_EVERYTHING, carriedFrom, carryMark } from './chatCarry';
import { chatTextTone, chatUiScale, createChatPanel, pushChatCopied, pushChatDraft, pushChatFresh, pushChatNote, pushChatState, setChatDraft } from './chatPanel';
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
  reinstructedHead,
  serviceLines,
  stillOurs,
} from './chatPrompt';
import { LanguageCode } from './settingsShape';
import { isClaudeSessionTab, isOrdinaryEditorTab, sourceSession, TabSnapshot } from './sessionKey';
import { GotoAsked, sessionSourceOf, tabKindOf } from './chatGoto';
import { Moved, filedUnder, followable, knownFolder, movedTo, prepareMoves, sameRoot, sessionIdOf } from './chatSource';
import { askedAsText } from './claudeQuestion';
import {
  Asked,
  Found,
  foldersToSearch,
  oneAnswerFrom,
  pinnable,
  promptsFrom,
  sessionFileIn,
  sessionFileOf,
  severalMatch,
  waitingQuestion,
} from './claudeSessions';
import { EditorText, confirmWholeFile, passageFromEditor } from './editorPassage';
import { triggerPlan } from './chatTrigger';
import { OpenConversation } from './conversationPicker';
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
  /**
   * The passage this conversation was opened about — cleared by a reset, and by nothing else.
   *
   * <p>It was `readonly` until *New chat* existed, which was true of every gesture there was: the
   * passage is what the first question was about, and a conversation does not change its mind about
   * that. A reset is the one act that makes it a different conversation.</p>
   */
  passage: string;
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
  spend: readonly TurnSpend[];
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
  /**
   * Whether this conversation came from a Claude Code session tab rather than from a file.
   *
   * <p>Only then is there anything to read back: the button that shows what was asked joins the tab
   * to a session by its title, and a chat opened from a `.md` has no session behind it at all.</p>
   */
  fromSession: boolean;
  /**
   * Where a handed-over conversation begins — the index of the first message carried.
   *
   * <p>0 until somebody presses *Carry nothing above*, and 0 is the behaviour this tab has always
   * had. It narrows HANDOVERS only: a Team server, which is given the conversation every turn; a
   * model switch; a re-ask, which is a switch by another name; and the first turn after a reload,
   * where the process behind the tab died with the window and what comes back is a new model being
   * told a conversation it never heard.</p>
   *
   * <p>An ordinary local turn carries nothing at all — `oneTurn` sends the question alone when
   * `carry` is empty — so the model being spoken to right now is untouched by this. That is the
   * whole promise, and it holds by construction rather than by a special case.</p>
   */
  carryFrom: number;
  /**
   * The session file this tab belongs to, once it has been found. Empty until then, and empty
   * forever for a chat opened from a file.
   *
   * <p>The FILE, not the name. Claude Code refines a conversation's `ai-title` as it goes on and the
   * tab follows it, so the name captured when this chat was opened stops matching hours later —
   * which is precisely the window the Asked button exists for. Resolved in the background as the tab
   * opens, while the name is still current, and kept. (codex, the second code round.)</p>
   */
  sessionFile: string;
  /** Whether a turn is in flight. Only so a switch can say out loud that it is waiting for one. */
  running: boolean;
  /**
   * Which pair the last turn FAILED under, as `provider/model`, or empty when nothing is retryable.
   *
   * <p><b>A retry is the same question to the same model; a re-ask is the same question to a
   * different one.</b> Those are two features and the difference is the whole of what tells them
   * apart — so a retry that quietly went to whatever is selected NOW would be a re-ask wearing the
   * other one's label. Nothing else records it: a failed turn appends no answer, and an answer is the
   * only message that carries the model that produced it.</p>
   *
   * <p>So the pair is written down when the turn fails and compared when the button is drawn. Switch
   * model after a failure and the retry is withdrawn rather than silently redirected — Re-ask is the
   * control for that, and it is already on the Send button. (codex, the second code round.)</p>
   */
  failedWith: string;
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
   * Which SLATE this tab is on, counted from 0 and bumped the moment a reset begins.
   *
   * <p>Not a turn number and not a revision. `turns` serialises the turns of one conversation, and
   * waiting for that chain is most of what a reset needs — but a question QUEUED behind the running
   * one is on the same chain, and its first act is to append what somebody typed. After a reset that
   * would be the new conversation's transcript, and the reset would have waited out a whole answer
   * nobody wants first. So this is bumped BEFORE the old turn is stopped, and a turn whose queued
   * generation no longer matches never begins.</p>
   *
   * <p>It is deliberately NOT what guards the writes at the end of a turn — `saveId` is, because
   * those two questions are different. This one asks "has a reset begun", and between a reset
   * beginning and the slate actually being wiped the turn in flight is still writing into the OLD
   * conversation, which is exactly where its stopped line belongs. (Two vendors, D1's plan round.)</p>
   */
  generation: number;
  /**
   * Whether a reset is running for this conversation right now.
   *
   * <p>Here rather than in a module-level set keyed by entry, which is where it started: a set of
   * object identities outlives the threads in it if a `finally` is ever missed, and the rest of this
   * conversation's lifecycle already lives on this object. (gemini, the second code round.)</p>
   */
  resetting: boolean;
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
  saveId: string;
  /**
   * Which save of this conversation the disk last accepted from this window.
   *
   * <p>The baseline of the compare-and-swap in `chatStoreFile.ts`: a save carries it, and the store
   * refuses rather than overwrites when the disk has moved on. 0 until the first save lands, which
   * is what says "nothing of this conversation is on disk yet" — the only state allowed to create
   * one.</p>
   */
  rev: number;
  /**
   * When this conversation began, as opposed to when it was last written.
   *
   * <p>Stamped once, where the thread is built. `recordOf` used to put the current instant into BOTH
   * fields on every save, so a conversation started in January and answered in March was recorded as
   * having begun in March — and the picker story B2 draws "started" from exactly that field. All
   * three vendors' reviewers found it on A3's plan round.</p>
   */
  createdAt: number;
  /**
   * When something was last SAID in this conversation — what the picker orders an open row by.
   *
   * <p>Not a second copy of the record's `updatedAt`: that one is stamped by the store on a write,
   * and a window that could not reach the disk would then order its own open tabs by a number that
   * never moved. This is the same instant seen from the other side, kept where the picker can read it
   * without opening anything.</p>
   *
   * <p><b>Stamped where a change is PROVED, never on a repaint.</b> `show` runs on every push — a
   * turn starting, a queue position moving, a failure clearing — and stamping above its guard would
   * put a conversation nobody spoke in at the top of the list every time its tab redrew. It is the
   * same ruling A4 made for the record's own instant when it decided that a reload is not a use: a
   * restored tab starts at the instant the record carries, and stays there until somebody asks
   * something.</p>
   */
  usedAt: number;
  /**
   * WHAT THIS CONVERSATION WAS OPENED FROM — the identity that outlives the window.
   *
   * <p>The live key is the tab object, which dies with the window; this is the durable half, and it
   * is what *go to* will match a tab against. A file's uri, a Claude session's id, or `none` for a
   * conversation restored from a record written before story C1 — and `none` matches no tab at all,
   * deliberately, because a title is never a key.</p>
   */
  source: ConversationSource;
  /**
   * The workspace root this conversation is FILED under, captured when its source was.
   *
   * <p>Not derived on read, and that is the correction three reviewers made to this story's plan: a
   * Claude source is a session UUID, and a UUID names no directory, so a function of (source, roots)
   * would fall back to the first root every time — which is the very misfiling this story removes.
   * It is worked out once, from the file's own path or from the folder the session was found in, and
   * read back verbatim thereafter.</p>
   */
  workspace: string;
  /**
   * This conversation's store writes, one after another.
   *
   * <p>The same shape as `turns` above and for a sharper reason. A save carries the revision this
   * window last had accepted, so two writes issued before the first answers BOTH carry the old one:
   * the second is refused, and a refusal is read as another window — a tab would fork itself, and
   * tell the person it had become a copy of a conversation nobody else was in. Two reviewers found
   * it independently. `show` runs on every push, and pushes are not rare.</p>
   */
  writes: Promise<unknown>;
  /** The tab's heading, kept here because what is written down has to name the conversation. */
  readonly title: string;
  /** What was last written to the store, so a push that changed nothing writes nothing. */
  savedMessages?: readonly ChatMessage[];
  savedModelId?: string;
  /** The mark last written down, so a press that changed only it is still saved. */
  savedCarryFrom?: number;
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
 * One question asked of every folder this window has open, at once.
 *
 * <p>EVERY folder, never the first that answers: a workspace with two roots, each holding a session
 * by this tab's name, would otherwise be shown whichever VS Code happened to list first. What to do
 * with the answers is `oneAnswerFrom`'s to decide.</p>
 */
async function everyFolder<T>(ask: (folder: string, caseBlind: boolean) => Promise<T>): Promise<readonly T[]> {
  const caseBlind = NAMES_ARE_CASE_BLIND;

  return await Promise.all(whereToLook().map((folder) => ask(folder, caseBlind)));
}

/**
 * The folders a session may be in — the workspace's, or the home directory when it has none.
 *
 * <p>`foldersToSearch` is where the decision lives, in the module with no `vscode` in it, because a
 * decision inside this file is one no unit test can reach — and this one was wrong in two readers at
 * once until an operator with no folder open found it.</p>
 */
/**
 * Whether this filesystem treats two spellings of one name as the same name.
 *
 * <p>Windows and macOS do; Linux does not. It was already the rule the session search used, in two
 * places, and it is now also the rule paths are COMPARED by — folding case on a case-sensitive
 * filesystem would make /work/App and /work/app one folder, picking the wrong root for a conversation
 * and applying a rename to an unrelated one. One constant, so the answers cannot drift apart.
 * (CodeRabbit, on the pull request.)</p>
 */
const NAMES_ARE_CASE_BLIND = process.platform === 'win32' || process.platform === 'darwin';

function whereToLook(): readonly string[] {
  return foldersToSearch(
    (vscode.workspace.workspaceFolders ?? []).map((folder) => folder.uri.fsPath),
    os.homedir(),
  );
}

/**
 * The workspace a conversation is FILED under in the store: the first folder `whereToLook` gives,
 * so a conversation is filed where its Claude session is; empty for a window with none at all.
 *
 * <p>ONE function, and exported, because three writers must agree on it or a picker filtered on
 * this workspace shows two of the three: the dual write here, the migration of the memento, and
 * the serializer's memento fallback. Story C1 is what teaches a record which root it really belongs
 * to; until then every writer gives this answer, and the boundary is named in that story's row.</p>
 */
export function conversationWorkspace(): string {
  return whereToLook()[0] ?? '';
}

/**
 * The root a conversation with THIS source is filed under — the host's half of {@link filedUnder}.
 *
 * <p>A file says where it is; a Claude session does not, so its folder is carried from the search
 * that found it and this is not the path that files one (see {@link pinSession}). Anything else
 * falls back to the first root, which is what every record had before story C1.</p>
 */
function filedFor(source: ConversationSource): string {
  return filedUnder(knownFolder(source, fsPathOf), whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
}

/**
 * A conversation's durable origin, as ONE value: what it was opened from and where that is filed.
 *
 * <p>The two are one fact written in two fields, and nothing in the types made them move together —
 * a later repair that set `source` and forgot `workspace` would leave *go to* matching by the new
 * origin while the picker went on filtering by the old root. Every path that establishes an origin
 * goes through this, so the pair cannot be set by halves. (codex, the code round.)</p>
 */
function originOf(source: ConversationSource): { readonly source: ConversationSource; readonly workspace: string } {
  return { source, workspace: filedFor(source) };
}

/** Give a live conversation a new origin — both fields, from the one value. */
function reorigin(thread: Thread, source: ConversationSource): void {
  const origin = originOf(source);
  thread.source = origin.source;
  thread.workspace = origin.workspace;
}

/** A uri as a filesystem path, or empty for one that names no file. The host's spelling, so rules need none. */
function fsPathOf(uri: string): string {
  try {
    const parsed = vscode.Uri.parse(uri, true);

    return parsed.scheme === 'file' ? parsed.fsPath : '';
  } catch {
    // A source a person could not have produced, or one from a build that spelled them differently.
    // It names no file here, which is the honest answer and not a failure — but it is SAID, because
    // nothing else would ever mention it and the conversation quietly files under the fallback root.
    console.warn(`ConnectOtherAIs: a conversation names a source this build cannot read as a uri: ${uri}`);

    return '';
  }
}

/**
 * Where this tab's session is, however the window was opened.
 *
 * <p>A window with NO FOLDER still runs Claude Code — and it runs it in the HOME directory, which is
 * where a VS Code terminal starts when there is no folder to start in. So that is where this looks,
 * as though home were the workspace: the operator's own session, which the button could not see, is
 * filed under `C--Users-strug`.</p>
 *
 * <p><b>Not every project on the machine.</b> That was the first attempt and it was worse than the
 * bug: 77 project directories and a gigabyte of transcript on this machine, read to compare titles,
 * while the region sat on *Reading the session…*. The refusal names the directory it looked in, so a
 * window whose Claude Code was started somewhere else says where it did look rather than hunting.</p>
 */
async function findSession(title: string): Promise<readonly Found[]> {
  return (await findSessionIn(title)).map((one) => one.found);
}

/** One folder's answer, WITH the folder — the provenance a pinned session is filed under. */
interface FoundIn {
  readonly folder: string;
  readonly found: Found;
}

/**
 * The same search, keeping the folder each answer came from.
 *
 * <p>`findSession` flattens this away, and for its callers that is right: they are deciding whether
 * a tab has one session or several, and the folder is not part of that question. It IS part of
 * story C1's: a session's id names no directory, so the only honest way to know which root a Claude
 * conversation belongs to is to remember where it was found. Three reviewers refused the alternative
 * — decoding the root out of Claude's own encoded directory name — and so would I: it is another
 * program's encoding, undocumented, and a decoder that drifts misfiles conversations silently.</p>
 */
async function findSessionIn(title: string): Promise<readonly FoundIn[]> {
  return await everyFolder(async (folder, caseBlind) => ({
    folder,
    found: await sessionFileIn(os.homedir(), folder, caseBlind, title),
  }));
}

/**
 * Find this conversation's session by name, keep the file, and read it.
 *
 * <p>The path a tab takes when it has no pin — because it was restored from a reload, or because the
 * background walk at open time found nothing yet. What it resolves it keeps, on the same terms
 * `pinSession` uses: exactly one session, across every root, with nothing else in doubt.</p>
 */
async function resolveAndPin(entry: ChatEntry, mine: Thread): Promise<Asked> {
  // THE ID FIRST. A conversation that was pinned once keeps `source: {kind:'claude', sessionId}`,
  // and that id is what the file is CALLED — so the answer is a directory entry rather than every
  // transcript in the folder read for its title. It is also the only path that survives a rename:
  // the name walk below is hunting a string another program rewrites underneath it, which is the
  // defect this whole plan is about. Measured on the operator's folder: 5.2 s warm against one stat.
  const byId = await findSessionById(mine.source);
  // ONE root, on the same terms the name walk uses. A UUID naming a session in two roots at once is
  // not a shape this data has — which is exactly why answering with whichever root was listed first
  // would be a pick nobody would ever see fail. The conversation's OWN filed root answers first when
  // it is one of them, since that is where this session was found the day it was pinned. (gemini,
  // the plan round.)
  const own = byId.find((one) => one.found.kind === 'one' && sameRoot(one.folder, mine.workspace, NAMES_ARE_CASE_BLIND));
  const kept = own ?? (pinnable(byId.map((one) => one.found))
    ? byId.find((one) => one.found.kind === 'one')
    : undefined);
  if (kept !== undefined) {
    // Nothing is written: the id is already on the record, and the path is deliberately not.
    mine.sessionFile = (kept.found as Extract<Found, { kind: 'one' }>).file;

    return await promptsFrom(mine.sessionFile);
  }
  // The id missed — this conversation never had one, or the session it names has been deleted — so
  // the tab's name is asked, and what it finds is ADOPTED: source, workspace and a queued write, on
  // the terms `pinSession` uses. Until this, a resolution found here died with the window and the
  // same walk ran again after every reload. (codex, the plan round.)
  const found = await findSessionIn(mine.title);
  if (!pinnable(found.map((one) => one.found))) {
    // Nothing to keep, and the reason is the one the refusal would give anyway.
    return oneAnswerFrom(found.map((one) => asAsked(one.found)));
  }
  adoptFound(entry, mine, found.find((one) => one.found.kind === 'one')!);

  return await promptsFrom(mine.sessionFile);
}

/**
 * Each folder's answer for the session a conversation's own source NAMES, or nothing to ask.
 *
 * <p>A source that is not a Claude session — a file-opened chat, or one that was never pinned — has
 * no id to look for, and an empty list is what says so. Every root is asked, as the name walk asks
 * them: the id names no folder, and a workspace can hold two.</p>
 */
async function findSessionById(source: ConversationSource): Promise<readonly FoundIn[]> {
  if (source.kind !== 'claude') {
    return [];
  }

  return await everyFolder(async (folder, caseBlind) => ({
    folder,
    found: await sessionFileOf(os.homedir(), folder, caseBlind, source.sessionId),
  }));
}

/**
 * Keep the session this answer found: the file in memory, the identity on the record.
 *
 * <p>ONE road in, because there are two ways a session is discovered — the walk as a tab opens, and
 * a press that finds the tab unpinned — and they wrote different amounts of it. The press wrote the
 * file alone, so what it learned was lost at the next reload while the walk's version lasted for
 * ever. The difference was invisible: both produce prompts on screen.</p>
 *
 * <p><b>The FOLDER the session was found in</b>, never this window's first root: a session's id names
 * no directory, so the only honest answer is where it was found. That is why this cannot go through
 * `reorigin`, which computes the root from the source and gets `''` for a Claude one.</p>
 */
function adoptFound(entry: ChatEntry, mine: Thread, one: FoundIn): void {
  const found = one.found as Extract<Found, { kind: 'one' }>;
  mine.sessionFile = found.file;
  if (!found.complete) {
    // PINNED FOR THIS WINDOW, NEVER WRITTEN DOWN. The walk was cut short by its budget, so this is
    // the only session of that name among the ones that were READ — a fine answer to "show me this
    // conversation" and no proof at all that no namesake sits beyond the cut. Writing it would make
    // a guess permanent, which is the exact failure the namesake refusal exists to prevent. (codex,
    // the code round, twice.)
    return;
  }
  const sessionId = sessionIdOf(mine.sessionFile);
  if (sessionId.length === 0) {
    // A file of a shape this build does not recognise. The tab keeps its pin — the Asked button
    // reads that file happily — and the conversation keeps no source, which is the honest answer:
    // an id invented here would match a tab that is not this one.
    return;
  }
  mine.source = sourceOfSession(sessionId);
  mine.workspace = filedUnder(one.folder, whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
  // WRITTEN EXPLICITLY. `show`'s guard compares messages, model and mark, so a source arriving on
  // its own — which is exactly what this is, minutes after the last thing anybody said — would
  // never reach disk through that path. Queued behind the conversation's other writes, so it
  // cannot carry a stale revision.
  keepQueued(entry, mine);
}

/** A lookup answer as an answer about prompts — the refusals are word for word the same ones. */
function asAsked(found: Found): Asked {
  return found.kind === 'one' ? { kind: 'said', said: [] } : found;
}

/**
 * Find and keep this conversation's session file, in the background, as its tab opens.
 *
 * <p>Not awaited: a tab must appear at once, and this is a walk over a folder of session files whose
 * answer is not needed until somebody presses Asked. Not on a FILE-opened chat at all — there is no
 * session behind one. If it finds nothing, the button falls back to searching by name, which is
 * where it started.</p>
 */
function pinSession(entry: ChatEntry, title: string, fromSession: boolean): void {
  if (!fromSession || title.length === 0) {
    return;
  }
  void (async () => {
    let found: readonly FoundIn[];
    try {
      found = await findSessionIn(title);
    } catch {
      // Nothing is pinned and nothing is said: the button still works by name, and a tab must not
      // take down the extension host for a walk it started on its own.
      return;
    }
    const mine = threads.get(entry.id);
    if (mine === undefined || !pinnable(found.map((one) => one.found))) {
      return;
    }
    adoptFound(entry, mine, found.find((answer) => answer.found.kind === 'one')!);
  })().catch((reason: unknown) => {
    // The outer edge of a detached call. The `try` above covers only the walk; everything after it —
    // the id, the file, the queue — used to escape unobserved, which `reliability.md` forbids of any
    // edge nothing is above. (gemini, the code round.)
    console.error('ConnectOtherAIs: pinning a conversation to its Claude session threw', reason);
  });
}

/**
 * How many times each conversation has asked what was written in its session.
 *
 * <p>The number is the only thing that tells a late answer from a current one, and it lives here
 * rather than on the thread because it is about presses rather than about the conversation.</p>
 */
const asking = new WeakMap<object, number>();

/**
 * The MEMENTO — where conversations were kept before the store on disk, and still written until the
 * migration has confirmed every record is there. Set once, in `activate`; unset by {@link retireMemento}.
 *
 * <p>A module-level handle rather than a parameter on six signatures: the command has no context and
 * neither do the callbacks a panel is wired with, and threading a store through both to reach two
 * call sites would be a wide change for a narrow need. It is absent in tests of this file's pure
 * neighbours and in every window whose migration has succeeded, and every use is guarded.</p>
 */
let memory: ChatTabMemory | undefined;

export function rememberChatsIn(store: ChatTabMemory): void {
  memory = store;
}

/**
 * The store on disk is the ONLY store from here on, in this window.
 *
 * <p>Called by `activate` when the migration reports that every memento record is confirmed on disk
 * and the key is emptied. Until then the two are written together — a store that cannot be reached
 * must not leave a person's next words written NOWHERE, which is what an unconditional cut-over did
 * in the first draft of this story (A4's plan round). Unbinding the handle is the whole gate: every
 * `memory?.` below becomes a no-op, and nothing else has to know.</p>
 */
export function retireMemento(): void {
  memory = undefined;
}

/**
 * The conversation store on disk — the source of truth, read by the reload serializer and written on
 * every push.
 *
 * <p>Bound the same way and for the same reason as the memento above. Absent means no store, which
 * is every test of this file's pure neighbours: every use is guarded.</p>
 */
let store: ChatStoreFile | undefined;

export function keepChatsIn(onDisk: ChatStoreFile): void {
  store = onDisk;
}

/**
 * Told whenever the set of open conversations changes — a thread registered, restored, closed, or
 * re-minted under a new id — so this window's heartbeat (`chatStoreHeartbeat.ts`) announces it at once
 * rather than on its next minute. Bound the way the store above is; absent means no heartbeat, which
 * is every test of this file's pure neighbours.
 */
let pulse: (() => void) | undefined;

export function pulseChatsThrough(onChange: () => void): void {
  pulse = onChange;
}

/**
 * Which conversations this window holds open, by their store ids — what its heartbeat announces so
 * that no other window sweeps one of them by its age. Read from the registry each time, never cached:
 * a tab restored a moment ago must count.
 */
export function heldConversationIds(panels: ChatPanels): readonly string[] {
  return panels.known().flatMap(({ key }) => {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);

    return thread === undefined ? [] : [thread.saveId];
  });
}

/**
 * What this window holds open, described the way a STORED row is — for the picker's *Open* section.
 *
 * <p>A list of ids is what the heartbeat above needs and is not a list a person can choose from. Two
 * open conversations sharing a title are exactly the case the registry exists to handle, and the
 * model, the turn count and the last line are what tell them apart; a row that omitted them made the
 * open ones the least distinguishable rows on the list.</p>
 *
 * <p><b>Through `metaOf`, not by counting here.</b> A stored row is derived from a record by that
 * function, and a second derivation of "how many turns" and "the last line" would be two ways for an
 * open conversation and a closed one to describe the same thing differently — a conversation would
 * change its description the moment its tab closed. So a record is built for the thread and the same
 * derivation is applied to it; nothing is written.</p>
 *
 * <p>It lives here, with the accessor above, because the `Thread` map is private to this file: the
 * registry knows keys and labels, and every fact a row carries is on the thread. No decision is taken
 * here — the ORDER is `conversationChoice.ts`'s and the rows are `conversationPicker.ts`'s.</p>
 */
export function openConversations(panels: ChatPanels): readonly OpenConversation[] {
  return panels.known().flatMap(({ key }) => {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (thread === undefined) {
      return [];
    }
    // The record this conversation WOULD be saved as, stamped with when it was last used rather than
    // with now — `recordOf` takes that instant precisely so a caller that is not saving can say what
    // it means.
    const meta = metaOf(recordOf(thread, thread.usedAt));

    return [{
      id: meta.id,
      title: meta.title,
      modelId: meta.modelId,
      turns: meta.turns,
      lastLine: meta.lastLine,
      updatedAt: meta.updatedAt,
    }];
  });
}

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
    let touched = 0;
    for (const meta of index.entries({ kind: 'everywhere' })) {
      try {
        // ASKED NOW, not from a set taken before any awaiting began. A conversation that closed while
        // this loop was running is no longer followed by its thread, and a snapshot would have gone on
        // saying it was — so its rename would have been followed by neither half. (local, the code
        // round; it was my own open question.)
        if (heldConversationIds(panels).includes(meta.id) || !followable(meta.source)) {
          continue;
        }
        const moved = movedTo(meta.source.uri, moves, asUri, fsPathOf);
        if (moved.length === 0) {
          continue;
        }
        if (await follow(onDisk, meta.id, meta.source, sourceOfFile(moved))) {
          touched += 1;
        }
      } catch (reason) {
        // PER UNIT, as `reliability.md` requires of a loop over independent things: one conversation
        // that cannot be followed must not stop every other conversation following the same rename.
        console.error(`ConnectOtherAIs: a conversation threw while following a renamed file: ${meta.id}`, reason);
      }
    }
    if (touched > 0) {
      // The picker reads the INDEX, not the disk. Without this the rows go on naming the file they
      // left and sitting in the folder they left — and a second rename would compare against that
      // stale source and follow nothing. (gemini, the code round, three times.)
      await index.refresh();
    }
  })().catch((reason: unknown) => {
    console.error('ConnectOtherAIs: following a renamed file threw', reason);
  });
}

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
async function follow(onDisk: ChatStoreFile, id: string, was: ConversationSource, next: ConversationSource): Promise<boolean> {
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

/**
 * Whether this answer means the folder did not SAY, as against saying there is nothing of that name.
 *
 * <p>Exhaustive over `Found.none`'s reason rather than a comparison against one string: a third
 * reason added to that union — a cancelled walk, a second kind of partial read — must be a compile
 * error here rather than a new failure silently counted as a completed search. (codex, the code
 * round.)</p>
 */
function didNotAnswer(one: Found): boolean {
  if (one.kind !== 'none') {
    return false;
  }
  switch (one.why) {
    case 'unreadable':
      return true;
    case 'unmatched':
      return false;
    default: {
      const unhandled: never = one.why;

      throw new Error(`a walk outcome this build has no reading for: ${JSON.stringify(unhandled)}`);
    }
  }
}

/** A filesystem path as a uri, the way a record spells one. The host's, so `chatSource.ts` needs none. */
const asUri = (path: string): string => vscode.Uri.file(path).toString();

/**
 * Everything *go to* needs to know, gathered from the host — the other half of `chatGoto.ts`.
 *
 * <p>It lives here because every piece of it is private to this file: the thread registry, the tab
 * snapshot, the session walk, the roots. What it hands back is a value, so the decision itself stays
 * where a test can reach it. One snapshot answers for the tab and for what this window already
 * holds, so the two cannot disagree about which tab is active.</p>
 *
 * <p>The Claude walk is the only slow part and it is only done for a Claude tab that has no live
 * conversation — which is the case *go to* exists for, and the one where a person is already waiting
 * to be taken somewhere.</p>
 */
export async function askedForGoto(
  panels: ChatPanels,
  index: ConversationIndex,
): Promise<{ readonly asked: GotoAsked; readonly key: object | undefined }> {
  const { active, all } = snapshots();
  const known = panels.known();
  const claude = sourceSession(active, all, known);
  const matched = claude ?? sourceSession(active, all, known, isOrdinaryEditorTab);
  const tab = all.find((one) => one.key === matched?.key) ?? active;
  const kind = tabKindOf({ claude: tab !== undefined && isClaudeSessionTab(tab), document: tab !== undefined && isOrdinaryEditorTab(tab) });
  const path = kind === 'document' ? fsPathOf(tab?.uri ?? '') : '';
  const here = filedUnder(path, whereToLook(), conversationWorkspace(), NAMES_ARE_CASE_BLIND);
  const live = matched === undefined ? undefined : threads.get(panels.get(matched.key)?.id ?? {});
  // THE WALK, with progress: it reads a directory of session files and can take seconds, and a
  // person who has just pressed a chord and sees nothing presses it again. The same notification the
  // Asked button already shows for the same walk. (Four findings, the code round.)
  const walked = kind === 'claude' && live === undefined
    ? await vscode.window.withProgress(
      { location: vscode.ProgressLocation.Window, title: 'Finding this conversation…' },
      async () => sessionsNamed(tab?.label ?? ''),
    )
    : { found: [] as readonly Found[], unsure: false };
  const one = walked.found.find((answer) => answer.kind === 'one');
  const source = kind === 'claude' && pinnable(walked.found)
    ? sessionSourceOf(sessionIdOf(one?.kind === 'one' ? one.file : ''))
    : kind === 'claude' ? { kind: 'none' as const } : sourceOfFile(tab?.uri ?? '');

  const asked: GotoAsked = {
    tab: { kind, label: tab?.label ?? '', path },
    live: live?.saveId ?? '',
    source,
    // Only a Claude tab can be ambiguous in this sense, and `pinnable` is the same rule the pin uses.
    // A walk that could not be done counts as ambiguous too: not knowing which session a tab belongs
    // to is a reason to ASK, never a reason to offer to start a second conversation.
    ambiguous: kind === 'claude' && (walked.unsure || (walked.found.length > 0 && !pinnable(walked.found))),
    // THE POSITIVE FACT, and the only thing that licenses the name fallback in `goto`: more than one
    // session really does answer to this name. A walk that could not be done arrives as `false` and
    // asks, and so will any future reason to be ambiguous. Asked of the ANSWERS rather than of the
    // array — `findSession` returns one outcome per FOLDER and a single folder can answer `several`,
    // so a length test was false in a one-root workspace however many sessions shared the name, and
    // the fallback was dead in the commonest case there is. (CodeRabbit, on the pull request.)
    severalSessions: severalMatch(walked.found),
    // THE OTHER HALF of what `ambiguous` folds together: a walk that could not be DONE, as against
    // one that was done and matched nothing. Two situations, and the person needs a different thing
    // from each — "try again" against "none of them is called that, pick one".
    //
    // A THROWN walk is not the only way it fails. A directory that would not list, and a folder too
    // big to finish reading, both come back as an ordinary `none` — so asking only `unsure` would
    // have called them "no session is called that", which is the very mistake this fixes. The reason
    // is a field on the answer rather than the wording of its sentence. (codex, the plan round.)
    walkFailed: walked.unsure || walked.found.some(didNotAnswer),
    // How many tabs are called what this one is called — this tab included, so never below 1. It is
    // what lets a NAME be evidence: with two tabs of one name it identifies neither.
    namesakes: all.filter((one) => one.label === (tab?.label ?? '')).length,
    candidates: index.bySource(source),
    inRoot: index.entries({ kind: 'workspace', workspace: here }),
    roots: whereToLook(),
    fallback: conversationWorkspace(),
    caseBlind: NAMES_ARE_CASE_BLIND,
    index: index.state(),
  };

  // The KEY beside the decision, not inside it: `chatGoto.ts` is pure and a tab object is a handle
  // only this side can do anything with. It is what a conversation is bound to, and it is re-checked
  // before that happens.
  return { asked, key: tab?.key };
}

/**
 * Every session this name answers to — and whether the walk could be done at all.
 *
 * <p>A failed walk is NOT an empty result, and conflating them was a real defect: "no session is
 * called that" leads to offering a new conversation, so an unreadable session directory would have
 * produced a duplicate of a conversation that already existed. It is said on the console and
 * reported as `unsure`, which the caller turns into a question rather than an answer. (Two vendors,
 * the code round; the constraint — never swallow — was my own.)</p>
 */
async function sessionsNamed(title: string): Promise<{ readonly found: readonly Found[]; readonly unsure: boolean }> {
  if (title.length === 0) {
    return { found: [], unsure: false };
  }
  try {
    return { found: await findSession(title), unsure: false };
  } catch (reason) {
    console.warn(`ConnectOtherAIs: this tab's Claude sessions could not be read, so which conversation is its own is unknown: ${title}`, reason);

    return { found: [], unsure: true };
  }
}

/**
 * Where a conversation sits in the registry, by the id the STORE knows it by.
 *
 * <p>`ChatPanels.keyOf` answers for the entry's own id, which is an object a page carries; this
 * answers for the `saveId`, which is the only name a saved record has. *Go to* needs it to move a
 * conversation the picker or a reload opened without a tab onto the tab it belongs to.</p>
 */
export function whereConversationSits(panels: ChatPanels, saveId: string): object | undefined {
  for (const { key } of panels.known()) {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (thread?.saveId === saveId) {
      return key;
    }
  }

  return undefined;
}

/** Bring the panel under this key to the front, for a caller that already knows where it sits. */
export function revealUnder(panels: ChatPanels, key: object): void {
  panels.get(key)?.panel.reveal();
}

/**
 * Reveal a conversation this window already holds — moving its registration onto `onto` first, when
 * that is where it belongs.
 *
 * <p>ONE rule for the two paths that reach a live conversation: *go to*'s reopen arm and the picker's
 * own accept. They were two copies of it and only one of them had it, so a conversation chosen from a
 * narrowed picker was revealed and the tab it belongs to left unbound for ever — the next press asked
 * the same question again. (Two vendors, the second code round.)</p>
 */
export function revealBound(panels: ChatPanels, where: object, onto: object | undefined): void {
  if (onto !== undefined && onto !== where) {
    panels.rekey(where, onto);
    revealUnder(panels, onto);

    return;
  }
  revealUnder(panels, where);
}

/**
 * Is this tab still on screen?
 *
 * <p>Asked immediately before a conversation is bound to it. A person can close a tab, or switch
 * away and open a chat in it, while a record is being read or a picker is on screen — and binding a
 * conversation to a tab that has gone registers it against something nobody is looking at.
 * (codex, the plan round.)</p>
 */
export function tabStillOpen(key: object): boolean {
  return snapshots().all.some((tab) => tab.key === key);
}

/**
 * Bring the tab holding this conversation to the front, and say whether there was one.
 *
 * <p>By the STORE id, because that is the only name the picker has for a conversation: its rows come
 * from metadata files, and the live key is a `vscode.Tab` object that no row can carry. A caller that
 * gets `false` has asked for a conversation this window does not hold — the registry moved under the
 * picker between the list being drawn and the row being pressed — and must fall back to reopening it
 * rather than doing nothing.</p>
 */
export function revealConversation(panels: ChatPanels, id: string): boolean {
  for (const { key } of panels.known()) {
    const entry = panels.get(key);
    const thread = entry === undefined ? undefined : threads.get(entry.id);
    if (entry !== undefined && thread?.saveId === id) {
      entry.panel.reveal();

      return true;
    }
  }

  return false;
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
      const input = tab.input as { viewType?: unknown; uri?: vscode.Uri } | undefined;
      all.push({
        key: tab,
        label: tab.label,
        viewType: typeof input?.viewType === 'string' ? input.viewType : '',
        // A `TabInputText` carries the document's uri and no viewType; a webview carries the
        // reverse. Reading both is what lets one snapshot answer for both doors.
        scheme: typeof input?.uri?.scheme === 'string' ? input.uri.scheme : '',
        // And the WHOLE uri, which is what a conversation opened from this tab is filed under and
        // later found by. `toString()` rather than `fsPath`, because that is the spelling a record
        // keeps and the one `sameSource` compares.
        uri: input?.uri === undefined ? '' : input.uri.toString(),
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
function keepQueued(entry: ChatEntry, thread: Thread): void {
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
function recordOf(thread: Thread, at = Date.now()): ConversationRecord {
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
function closedSession(note: string): ChatSession {
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
async function freshStart(entry: ChatEntry): Promise<void> {
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
async function freshening(entry: ChatEntry, thread: Thread): Promise<void> {
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
function keepTheOldOne(entry: ChatEntry, thread: Thread, reason: string): void {
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
async function publish(entry: ChatEntry, thread: Thread, note: string): Promise<void> {
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
async function ended(thread: Thread): Promise<string> {
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
async function archiveConversation(thread: Thread): Promise<Archived> {
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
function whyNotClosed(closed: Exclude<SaveOutcome, { kind: 'ok' } | { kind: 'partial' }>): string {
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
  if (thread.resetting) {
    // TYPED WHILE THE SLATE WAS BEING WIPED. The generation has already moved, so the guard inside
    // the turn would let this through as the NEW conversation's — and it would then run against a
    // session being disposed and be dropped at the end, with the words gone. They go back to the
    // composer instead, which is where they were typed. (gemini, the code round.)
    pushChatDraft(entry, text);

    return Promise.resolve();
  }
  // WHICH SLATE THIS QUESTION WAS TYPED ON, captured as it JOINS the chain rather than as it runs:
  // that is the whole point of it. A question queued behind an answer waits, and *New chat* pressed
  // while it waits means the person who typed it is no longer in the conversation they typed it in.
  const began = thread.generation;
  const mine = thread.turns
    .then(() => oneTurn(entry, text, began))
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
  // The MARK comes back with it. Clamping at each use keeps this turn honest, but the STORED value
  // would stay past the end and point at an unrelated message once the conversation grew again.
  // (gemini, the code round.)
  thread.carryFrom = carryMark(thread.carryFrom, thread.messages.length);
  // What the NEXT model is handed. `carry` is what `oneTurn` sends ahead of the question, and it is
  // exactly the conversation before the answer nobody wanted.
  // A re-ask is a switch by another name — the same question, a different model — so the mark
  // applies to it exactly as it applies to one.
  thread.carry = carriedFrom(again.said, thread.carryFrom);
  await oneTurn(entry, again.question, thread.generation);
}

/**
 * Send the question that failed again, unchanged, to the same model.
 *
 * <p>Distinct from a re-ask, which is the same question put to a DIFFERENT model and therefore drops
 * an answer nobody wanted. A retry has no answer to drop: the turn produced none, which is why there
 * is a failure line to press the button in.</p>
 *
 * <p><b>`at` is the transcript length the button was drawn for, and a press that does not match it is
 * refused.</b> This message can land after the state it was made in has moved — a question fails, the
 * person types another and sends it, and in the width of a frame before the push that clears the
 * failure they press the button still sitting under it. Taking "the trailing question" as it stands
 * then would retry a question nobody pressed for, which is the same hazard the stop command carries a
 * turn number to avoid. (codex, the plan round.)</p>
 *
 * <p><b>The question leaves the transcript first, and that is the whole trap.</b> `oneTurn` appends
 * the question before it sends, and its failing branch leaves it there — so re-asking without
 * removing it would print the same question twice, with the second copy carried into the next
 * request as though the person had asked it again. Sliced off the thread's own messages rather than
 * taken from `retryFrom`'s `said`, so every message keeps the fields it had.</p>
 *
 * <p><b>`thread.carry` is left exactly as the failure left it.</b> The failing branch does not clear
 * it, deliberately, with a comment saying why: <em>the retry — the same question, one keypress
 * later</em>. This is that keypress. Recomputing the carry here would make this the second place
 * that decides what a failed turn carries, and two places deciding one thing is one place
 * disagreeing.</p>
 */
/**
 * The pair a turn would go to NOW, as `provider/model` — the key a retry is matched against.
 *
 * <p>One function because two callers must agree exactly: the failing branch writes it down and the
 * button is drawn from it. An empty `modelId` means "whatever the row is set to", which is what the
 * row itself says — the same fallback the ledger uses, and for the same reason.</p>
 */
function pairOf(thread: Thread): string {
  const answering = vendorFor(thread.providerId);

  return `${thread.providerId}/${thread.modelId.length > 0 ? thread.modelId : (answering?.model ?? '')}`;
}

async function oneRetry(entry: ChatEntry, thread: Thread, at: number): Promise<void> {
  const stillTheSame = thread.messages.length === at && thread.failedWith === pairOf(thread);
  const again = stillTheSame ? retryFrom(thread.messages) : undefined;
  if (again === undefined) {
    // REDRAWN, not merely declined. The page disabled the control the moment it was pressed, so a
    // decline that pushed nothing would leave a dead button on screen for as long as the tab is
    // open — the person having pressed the one thing offered to them and got a greyed-out control
    // and silence. Pushing the state rebuilds the region from what is true now. (gemini, the plan
    // round.)
    //
    // WITH THE THREAD'S OWN running, not a hard-coded false. Four reviewers found the same thing on
    // the code round: the press that gets refused is almost always the one made after a NEW question
    // was sent, so a push saying nothing is running would retire the thinking line and unlock the
    // composer over a turn that is still in flight — offering a second send down a pipe that carries
    // one. The failure is empty here because that newer turn cleared it, which is the same event that
    // moved the transcript and made this press stale.
    show(entry, thread.running, '');

    return;
  }
  thread.messages = thread.messages.slice(0, -1);
  // Clamped for the reason the re-ask above clamps it: a stored mark past the end would point at an
  // unrelated message as soon as the conversation grew again.
  thread.carryFrom = carryMark(thread.carryFrom, thread.messages.length);
  await oneTurn(entry, again, thread.generation);
}

async function oneTurn(entry: ChatEntry, text: string, began: number): Promise<void> {
  const thread = threads.get(entry.id);
  if (thread === undefined) {
    return;
  }
  if (!sameSlate(began, thread.generation)) {
    // ASKED IN A CONVERSATION THAT HAS BEEN RESET. Nothing is sent and nothing is appended: the
    // question below would land in a transcript its author never saw, and the reset waiting on this
    // chain would have waited out a whole answer to a question nobody is in the conversation for.
    // The words are not lost — they go back to the composer, which is where they were typed.
    pushChatDraft(entry, text);

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
  // Whatever failed before, this turn supersedes it: the failure line is cleared by the push below
  // and nothing on screen offers a retry of it any more.
  thread.failedWith = '';
  thread.turn += 1;
  show(entry, true, '');

  // A forgetful model is handed the conversation EVERY time, not only after a switch: the server
  // answers one question and forgets it, so turn three without the transcript is turn one wearing
  // a number. This is also why such a conversation is capped — the bill for turn N is the bill for
  // everything before it.
  if (thread.forgetful && thread.messages.length > 1) {
    // ONE slice, not two: everything below the mark and above the question being asked. Slicing
    // twice allocated a near-copy of the whole transcript on every single turn, and a Team
    // conversation is handed one every time. (codex, on the hot path.)
    thread.carry = carriedFrom(thread.messages, thread.carryFrom, thread.messages.length - 1);
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
  // The conversation this turn is FOR, so that everything written when it ends is written into it or
  // into nothing. See the check after the send.
  const mySlate = thread.saveId;
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
  if (!sameSlate(mySlate, thread.saveId)) {
    // THE SLATE WAS WIPED WHILE THIS TURN WAS IN FLIGHT. Not the same question as the generation
    // check above: a reset bumps the generation first and clears the conversation only once this
    // chain has finished, so a turn that ends DURING that wait is still writing into the old
    // conversation and its stopped line belongs there. This is the other case — a write arriving
    // after the wipe, which is the one thing that must never reach the new conversation. Nothing is
    // recorded anywhere, ledger included: a cost line filed against a conversation that never asked
    // the question is worse than a cost line missing. (local and gemini, D1's plan round.)
    //
    // SAID, though — on the console rather than to the person, whose tab is showing a conversation
    // this answer has nothing to do with. Swallowing it silently would make a turn that cost money
    // and produced nothing invisible to anyone looking for why. (gemini, the code round.)
    console.warn(`ConnectOtherAIs: an answer arrived after its conversation was reset, and was dropped: ${mySlate}`);
    show(entry, false, '');

    return;
  }
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
    // WHICH PAIR this failed under, so the retry offered for it goes to the same one. A retry is the
    // same question to the same model and a re-ask is the same question to a different one; without
    // this the retry would follow whatever is selected when the button is pressed, which is a re-ask
    // wearing the other one's label. (codex, the second code round.)
    thread.failedWith = pairOf(thread);
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
      thread.carry = carriedFrom(thread.messages, thread.carryFrom);
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
    // The RUNTIME behind that row, written down beside its id: a preset is a row in a list somebody
    // edits, and resolving an old line through the list as it is today would move history.
    vendor: turn.vendor.runtime,
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


/** A refusal nobody needs to read: the person just cancelled the question that caused it. */
const CANCELLED = '\u0000';

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
  const said = await notifyAndAsk({
    as: 'warning',
    class: 'confirmation',
    source: 'chat',
    code: 'send-the-whole-passage',
    modal: true,
    title: ask,
    action: 'Send all of it',
  });
  if (said === 'Send all of it') {
    return { text: passage.text, failure: '' };
  }

  // CANCELLED, which is not a failure and needs no sentence: the person was asked a question one
  // second ago and answered it. A refusal here showed an empty warning box. (codex, the code round.)
  return { text: '', failure: CANCELLED };
}

/**
 * Which conversation this belongs to, over ONE snapshot of the tabs.
 *
 * <p>Both doors judged from the same list, rather than two lookups that could see the tabs in two
 * states — and it is one function because two commands ask the same question now. `claude` is
 * `undefined` when the source is a file, which is what tells the caller where the passage comes
 * from. (gemini, the code round.)</p>
 */
function matchedSource(panels: ChatPanels): {
  claude: ReturnType<typeof sourceSession>;
  source: ReturnType<typeof sourceSession>;
  /** The active document's uri, from the SAME snapshot — what a file conversation is filed under. */
  uri: string;
} {
  const { active, all } = snapshots();
  const known = panels.known();
  const claude = sourceSession(active, all, known);

  const matched = claude ?? sourceSession(active, all, known, isOrdinaryEditorTab);

  return {
    claude,
    source: matched,
    // THE MATCHED TAB'S uri, not the active one's. They are the same tab only when a person presses
    // from the editor; from the chat panel itself — which is how *add the question* is used — the
    // active tab is a webview with no document, while the match falls back through `all` to the
    // editor. Reading `active` there filed the conversation with no source at all, leaving it
    // unmatchable by *go to* and unfollowable by a rename. Still from THIS snapshot, so the two
    // cannot see the tabs in two states. (gemini, the code round.)
    uri: all.find((tab) => tab.key === matched?.key)?.uri ?? '',
  };
}

/**
 * The active editor, narrowed to the two strings the decision needs — and only when it is the tab
 * the conversation is being keyed to.
 *
 * <p>`matchedSession` reads the active TAB and this reads the active EDITOR, and in a split with the
 * focus somewhere else those are two different documents. Then the conversation would be named after
 * one file and carry the text of another. They are checked against each other rather than assumed
 * equal. (gemini, the code round.)</p>
 */
function editorText(): EditorText | undefined {
  const editor = vscode.window.activeTextEditor;
  if (editor === undefined) {
    return undefined;
  }
  const tab = vscode.window.tabGroups.activeTabGroup.activeTab?.input as { uri?: { toString?: () => string } } | undefined;
  const named = typeof tab?.uri?.toString === 'function' ? tab.uri.toString() : '';
  if (named.length > 0 && named !== editor.document.uri.toString()) {
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
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-vendor-gone',
      subject: providerId,
      title: refusal,
    });
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
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-model-gone',
      subject: providerId,
      title: running.refusal,
    });
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
    void notify({
      as: 'information',
      class: 'outcome',
      source: 'chat',
      code: 'model-switch-queued',
      subject: modelId,
      title: `Switching to ${modelId} as soon as the current answer arrives.`,
    });
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
      void notify({
        as: 'warning',
        class: 'failure',
        source: 'chat',
        code: 'model-switch-failed',
        subject: modelId,
        title: failure,
        detail: asText(reason),
      });
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
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'chat-vendor-unusable',
      subject: providerId,
      title: refusal,
    });
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
    void notify({
      as: 'warning',
      class: 'refusal',
      source: 'chat',
      code: 'switch-target-cannot-run',
      subject: providerId,
      title: cannot,
    });
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
  // model chosen, and the button naming it has to look it — but ONLY when it is that button's model.
  // The two selects move independently, so picking a vendor in the first and a different model in
  // the second leaves a conversation matching no preset at all, and recording the vendor's id lit a
  // button for a model that was not answering. Photographed by the operator: GPT-5.6-Terra pressed
  // while GPT-5.6-Sol was in the dropdown underneath it.
  thread.chosenId = presetInForce(savedModels(vscode.workspace.getConfiguration('coai')), providerId, modelId);
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
  thread.carry = carriedFrom(thread.messages, thread.carryFrom);
  show(entry, false, '');
  // Said out loud: the next question costs more than the last one, because it carries everything
  // above it. A person who is not told reads the first answer as a model that mysteriously knows.
  void notify({
    as: 'information',
    class: 'outcome',
    source: 'chat',
    code: 'model-switched',
    subject: modelId,
    title: `Now asking ${modelId}. Your next question carries this conversation across to it.`,
  });

  return true;
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
  const warn = (message: string): void => {
    void vscode.window.showWarningMessage(message);
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
