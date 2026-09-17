import { ChatMemory, ChatProvider } from './chatModels';
import { ChatHome } from './cliChatLaunch';
import { ChatMessage, ChatModelChoice } from './chatPage';
import { ChatSession } from './chatSession';
import { ConversationSource } from './chatStore';
import { TurnSpend } from './chatSpend';

/**
 * What a conversation IS while a window holds it, and where the window holds them.
 *
 * <p>Extracted from `chatCommand.ts` unchanged, and extracted FIRST because everything else depends
 * on it. Every module that came out after this one takes a `Thread`, so leaving the type where the
 * entry points are would make each of those moves import it back out of the command file — a cycle
 * fifteen times over.</p>
 *
 * <p>A type and a `WeakMap`, nothing else. There is no behaviour here to test and nothing that needs
 * an editor, which is why this module is NOT in `sonar.coverage.exclusions` — that list is exactly
 * the modules importing `vscode`, and this one does not.</p>
 */

/**
 * What a conversation is, beyond its process. Keyed by the entry's own id, weak so it dies with it.
 *
 * <p>It EXTENDS `ChatMemory` rather than restating its fields, which is what makes them impossible
 * to update in one of the two places a session starts and forget in the other: `memoryOf` is checked
 * against that type at its source, and both callers take the whole object.</p>
 */
export interface Thread extends ChatMemory {
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

export const threads = new WeakMap<object, Thread>();
