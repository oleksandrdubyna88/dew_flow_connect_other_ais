import { ChatMessage } from './chatPage';
import { TurnSpend } from './chatSpend';

/**
 * *New chat* — what a reset replaces, and what it leaves alone.
 *
 * <p>A chat tab accumulates. Ten turns about a lock, then a question about something else entirely,
 * and the model is still carrying the lock — worse on a Team server, where the whole conversation is
 * re-sent every turn and billed again, and where the three-turn cap then closes on a conversation
 * that is mostly history nobody wants. This is the clean slate behind that gesture.</p>
 *
 * <h2>Why the decision is a module and not a block</h2>
 *
 * <p>The reset touches fourteen fields of a thread, and the ones it must NOT touch matter as much as
 * the ones it must. Written inline it would be fourteen assignments nobody can check; written here it
 * is one value with a closed type, so a field added to {@link Freshened} is a compile error at the
 * one place that applies it rather than a line somebody forgets. `chatCommand.ts` is also far over
 * this repository's file-length limit, and the plan for this work says every new decision goes into a
 * module of its own.</p>
 *
 * <h2>The guarantee, stated no wider than it is verified</h2>
 *
 * <p><b>The next question reaches a model that was never told the old conversation</b> — a new
 * session object, a new process, an empty carry. What it is NOT is a promise that the old CLI's
 * process tree is gone on every platform: on Windows the launcher ends the tree, and on WSL and Linux
 * a grandchild can outlive its parent, which `PLAN_closing_a_chat_ends_its_whole_tree.md` owns and
 * the orphan ledger collects at the next activation. An orphan holds its own context and nothing
 * speaks to it: a process leak, not a leak of context BETWEEN conversations.</p>
 */

/**
 * Everything a reset REPLACES on a conversation, as one value.
 *
 * <p>Closed on purpose. The host spreads this over the thread in a single statement, so a field added
 * here is applied by construction and a field the reset must not touch cannot be added by accident —
 * every name below is a field of the thread, and every field of the thread NOT below survives.</p>
 *
 * <p>What survives, and why each one: the <b>title</b>, because the tab is still the conversation OF
 * that tab — it is the file's name or the Claude session's, never a summary of what was said, so
 * there is nothing in it to go stale. The <b>model</b>, the <b>provider</b>, the <b>prompt</b> and
 * the <b>role</b>, because a reset is a new subject and not a new setup. The <b>source</b> and the
 * <b>workspace</b>, because the conversation still belongs to the same tab in the same project.
 * <b>turn</b>, which is never reset anywhere: it exists so a stop can name the turn it means, and a
 * stop arriving a tick late must not be able to name a turn of the new conversation. And
 * <b>turns</b> and <b>writes</b>, the two chains, which are queues rather than contents.</p>
 */
export interface Freshened {
  /** A new conversation is a new record. Minted by the host, which owns the source of ids. */
  readonly saveId: string;
  /** Nothing on disk carries this id yet, so the first save is a create rather than a swap. */
  readonly rev: number;
  readonly messages: readonly ChatMessage[];
  /**
   * Empty, and this is the field the whole guarantee rests on.
   *
   * <p>`carry` is how a conversation reaches a process that never heard it — after a model switch,
   * and every single turn on a forgetful Team server. A reset that cleared the transcript and left
   * this would hand the next question the very history it was pressed to be rid of.</p>
   */
  readonly carry: readonly ChatMessage[];
  /** *Carry nothing above* marked a position in a transcript that no longer exists. */
  readonly carryFrom: number;
  /**
   * The Team-server cap opens again.
   *
   * <p>It counts the turns THIS model has answered, and it is there because a forgetful server is
   * billed for everything before the question as well as the question. A new conversation is not
   * carrying any of that, so it is not three turns old.</p>
   */
  readonly asked: number;
  /** What the old conversation cost belongs to the old conversation, and is archived with it. */
  readonly spend: readonly TurnSpend[];
  /** A pasted picture goes with the question it was pasted for, and there is no such question now. */
  readonly attached: string;
  readonly attachedPath: string;
  /**
   * The passage that started the OLD conversation.
   *
   * <p>It is at the top of the tab and it is not decoration — it is what the first question was
   * about. Keeping it would leave a new conversation captioned by the old one's subject, which is
   * exactly the stuck quotation this gesture was asked for: «Застрявшая цитата… новый чат».</p>
   */
  readonly passage: string;
  /**
   * The next question opens a process, exactly as after a reload.
   *
   * <p><b>This is what makes `codex` forget.</b> Its thread id is instance state of the session
   * object; the object is gone and the replacement mints a new one. Nothing is opened HERE, so a tab
   * somebody resets and never speaks to again spawns nothing — the rule the reload plan set.</p>
   */
  readonly reopen: boolean;
  /** Whatever was in flight has been ended and waited for, so nothing is running. */
  readonly running: boolean;
  /**
   * Nothing has failed in a conversation that has said nothing, so there is no retry to offer.
   *
   * <p>Carrying it over would put *Try again* on a new slate for a question the transcript no longer
   * holds — a press the host would rightly refuse, which to the person looks like a broken button.</p>
   */
  readonly failedWith: string;
  /** A new conversation began now; the old one keeps its own beginning in the record it is archived as. */
  readonly createdAt: number;
  readonly usedAt: number;
}

/**
 * The fields a reset REPLACES, for the conversation now called `saveId`, at `now`.
 *
 * @param saveId the id the new conversation is written under — minted by the host
 * @param now the instant the reset happened; an argument, because nothing here reads a clock
 */
export function freshened(saveId: string, now: number): Freshened {
  return {
    saveId,
    rev: 0,
    messages: [],
    carry: [],
    carryFrom: 0,
    asked: 0,
    spend: [],
    attached: '',
    attachedPath: '',
    passage: '',
    reopen: true,
    running: false,
    failedWith: '',
    createdAt: now,
    usedAt: now,
  };
}

/**
 * The marks that say what the disk already holds, which a reset must DELETE rather than set.
 *
 * <p>The push that writes a conversation down skips itself when these three still match what is on
 * the thread — by reference, so it costs nothing and is exact. A reset that left them would meet a
 * new empty transcript, decide nothing had changed, and never write the new record at all: the tab
 * would say one thing and the disk another until the next question. They are deleted rather than
 * emptied because the guard reads "never written" from their absence and "written as this" from
 * their value, and an empty array is a value. `forkOnDisk` clears the same three for the same
 * reason.</p>
 */
export const UNSAVED = ['savedMessages', 'savedModelId', 'savedCarryFrom'] as const;

/**
 * Is the conversation a turn BEGAN in still the one this tab is holding?
 *
 * <p>Waiting for the chain is not enough on its own, which is why this exists. A bridge message can
 * land a tick late — the same reason a stop names the turn it means — and a question QUEUED behind
 * the one that was running would otherwise begin a whole new turn after the reset: its first act is
 * to append what somebody typed, and by then that is the new conversation's transcript. So the
 * generation is bumped BEFORE the old turn is stopped, and asked twice: once before a turn begins,
 * so a queued one never starts and the reset is not held waiting for an answer nobody wants, and
 * once immediately before an answer is appended, with no `await` between the asking and the
 * appending — on a single-threaded host that is what makes it one decision rather than a check and
 * then an act. (Two vendors, the plan round, from three directions.)</p>
 */
export const sameSlate = <T>(began: T, now: T): boolean => began === now;

/** Said when the slate has been wiped: not a dialog, which the operator refused by name. */
export const ARCHIVED = 'The previous conversation was archived — find it again in CoAI: switch conversations…';

/**
 * Said when the reset did NOT happen, naming what stopped it.
 *
 * <p>A half-performed reset that reports success is the one outcome worse than no reset, so nothing
 * is archived and nothing is cleared when any part of ending the old conversation fails. The old
 * conversation stays live AND USABLE: the next question opens it a process the way a reload does.
 * (codex, the plan round — "the failure branch leaves the old record active while its session is
 * unusable".)</p>
 */
export const couldNotEnd = (reason: string): string =>
  `The previous conversation could not be ended (${reason}), so nothing was archived`
  + ' and this chat is unchanged. Your next question will open it again.';
