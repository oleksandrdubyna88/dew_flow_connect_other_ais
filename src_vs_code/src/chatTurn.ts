import * as vscode from 'vscode';
import { ChatEntry } from './chatPanels';
import { Thread, threads } from './chatThread';
import { answeredBy, asText, chatLanguage, pairOf, show } from './chatShow';
import { cliFor, remoteFor, started } from './chatLaunch';
import { readyToChat, taskOf, vendorFor } from './chatConfig';
import { TurnResult } from './chatSession';
import { isRemote, memoryOf } from './chatModels';
import { reaskFrom, retryFrom } from './chatPresets';
import { sameSlate } from './chatFresh';
import { imageTurn } from './chatImage';
import { carriedFrom, carryMark } from './chatCarry';
import { CARRY_BUDGET, REMOTE_CARRY_BUDGET, carriedTurn } from './chatPrompt';
import { chatSettingsFrom } from './chatSettings';
import { ChatOutcome, ReportedUsage, chatTurnRecord } from './chatUsage';
import { recordChatTurn } from './chatUsageFile';
import { coaiDataDir } from './dataDir';
import { Vendor } from './vendors';
import { pushChatDraft } from './chatPanel';

/**
 * One turn of a conversation, start to finish — and the three gestures that are turns wearing
 * another name.
 *
 * <p>Extracted from `chatCommand.ts` unchanged. A re-ask and a retry both go THROUGH `oneTurn`
 * rather than beside it, which is the point: the lock, the turn number a stop can name, the
 * transcript, the model recorded on the answer and the cap on a forgetful conversation all happen
 * because neither is a second path.</p>
 *
 * <p>The question is appended BEFORE the turn is sent, so the page shows it while the model is
 * thinking — nine measured seconds of silence otherwise look like a tab that ignored a keypress.</p>
 *
 * <p><b>Over the 400 lines the style rule calls typical, and it says why here because the rule asks
 * it to.</b> `oneTurn` is most of them and is one sequence — the slate guard, the carry, the image,
 * the send, the stop, the transcript, the ledger — so a cut at a line count would put half a turn in
 * one file and half in another and buy a reader nothing. The rule's own words are "extract a named
 * unit with its own responsibility", and there is one responsibility here. Well under the 800
 * ceiling; the next honest reduction is a seam somebody finds inside the turn.</p>
 */

/**
 * Ask, once whatever this conversation is already doing has finished.
 *
 * <p>Two things the chain buys, and both were found by the gate. The transcript stays in order when
 * the keybinding is pressed twice in a row. And a `send` that REJECTS — which `CliChatSession`
 * promises never to do, but a `ChatSession` is an interface and the remote one is not written yet —
 * cannot leave the composer locked forever behind a `void ask(...)` nobody is watching.</p>
 */
export function ask(entry: ChatEntry, text: string): Promise<void> {
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

/**
 * Give a restored conversation the process it has not got, or say why it cannot have one.
 *
 * <p>Returns an empty string when there is nothing to do, which is every ordinary turn. A refusal is
 * the sentence the page shows: the model this conversation was having may no longer be configured,
 * its CLI may be gone, a Team server may have signed out. All three are the same three checks the
 * command makes before opening a tab at all — asked again here, because a reload can be a week and
 * a machine rebuild away from the conversation it is restoring.</p>
 */
export async function reopened(thread: Thread): Promise<string> {
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
export async function oneReask(entry: ChatEntry, thread: Thread): Promise<void> {
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
export async function oneRetry(entry: ChatEntry, thread: Thread, at: number): Promise<void> {
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

export async function oneTurn(entry: ChatEntry, text: string, began: number): Promise<void> {
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
export function outcomeOf(result: TurnResult): ChatOutcome {
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
export function ledger(
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
