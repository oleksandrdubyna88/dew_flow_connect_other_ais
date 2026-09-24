import type { ChatAccess } from './chatAdapter';
import { ChatModelChoice } from './chatContracts';
import { ChatProvider } from './chatModels';
import { ChatMessage, TurnMarks, WaitingQuestion, chatCappedHtml, chatFailureHtml, chatMessagesHtml, chatPickerHtml, chatPresetRowsHtml, chatStatusHtml, chatWaitingHtml } from './chatPage';
import { ModelPreset, PromptPreset } from './chatPresets';

/**
 * The state message a chat tab is pushed after it is open, and the one place it is sent from.
 *
 * <p>Moved out of `chatPanel.ts`, which imports `vscode`, when issue #492 found that the message left
 * out two of the fields its own state carries: a builder no test could run was a builder nobody
 * could see drop a field. Here, free of `vscode`, a test drives the real send and hands what it
 * posted to the running page.</p>
 */

/** Everything the page shows that can change after it is open. */
export interface ChatPushState {
  readonly messages: readonly ChatMessage[];
  readonly running: boolean;
  readonly capped: boolean;
  /** Typed and not yet asked, oldest first. Empty for every conversation that is idle. */
  readonly waiting: readonly WaitingQuestion[];
  readonly failure: string;
  readonly models: readonly ChatModelChoice[];
  /** Every row that can answer, each with its own models — what the picker offers. */
  readonly providers: readonly ChatProvider[];
  /** Who would answer a re-ask, or empty when there is nothing to re-ask. */
  readonly reask: string;
  /** The failed turn can be sent again — the host's answer, never the page's. */
  readonly canRetry: boolean;
  /** The picture waiting to go with the next question, as a data URL, or empty. */
  readonly attached: string;
  /** What this conversation has cost so far, as a line, or empty. */
  readonly spend: string;
  /** What the model may do on this computer, and whether the box is offered — issue #289. */
  readonly access: ChatAccess;
  readonly agentOffered: boolean;
  readonly providerId: string;
  readonly modelId: string;
  /** Which saved PROMPT is in force — the button that looks pressed. */
  readonly promptId: string;
  /** The two lists, so the rows of buttons can be redrawn with the right one pressed. */
  readonly promptPresets: readonly PromptPreset[];
  readonly modelPresets: readonly ModelPreset[];
  /**
   * Which words in the turn are which — who is answering, what is asked, and the machinery.
   *
   * <p>Pushed because the page cannot tell them apart: it holds one string, and it draws the box's
   * own text transparent so a layer behind it can colour part of it. Empty marks nothing.</p>
   */
  readonly marks: TurnMarks;
  /** Which model button is pressed: the preset chosen, which is ahead of the session mid-switch. */
  readonly chosenModelId: string;
  /** Where a handover starts — the index of the first message carried. Zero carries everything. */
  readonly carryFrom: number;
  /**
   * How many turns are ahead of this one on a Team server, or 0 for none and for a local model.
   *
   * <p>Not optional. A push that says nothing about the queue would leave the last number on screen
   * while the turn is being answered — and this state is deduplicated by its serialisation, so a
   * stale position would be pushed exactly once and then stick.</p>
   */
  readonly queued: number;
  /**
   * Which turn is in flight, counted from 1 — 0 when none is, or when it cannot be named.
   *
   * <p>The control that stops a turn is rendered INTO the thinking line with this number in it, so a
   * control on screen names the turn it was drawn for and no other. That is the whole reason it
   * travels with the state rather than being remembered by the page.</p>
   */
  readonly turn: number;
}

/**
 * What was last pushed to a conversation, so an unchanged state is not pushed again.
 *
 * <p>Keyed by the conversation's id and weak, so a closed tab's record goes with it. The page
 * replaces its whole message region on every push, and pushing an identical one costs a re-render
 * that drops the reader's text selection for nothing. The same shape `roundsLogPanel.ts` uses.</p>
 */
const lastPushed = new WeakMap<object, string>();

/** The message itself — every region the page redraws, and every value it reads. */
export function chatStateMessage(state: ChatPushState): Record<string, unknown> {
  return {
    type: 'state',
    // MARKED like the composer below it: a question that has been sent is the same words, and they
    // stop being readable if the colours go when it moves.
    // RUNNING too. The full-page render was given it and this one was not, so every push during a
    // turn redrew the button the full render had just withheld. (CodeRabbit, PR #208.)
    messagesHtml: chatMessagesHtml(state.messages, state.marks, state.carryFrom, state.running),
    running: state.running,
    capped: state.capped,
    thinkingHtml: chatStatusHtml(state.running, state.queued, state.turn),
    // What is typed and not yet asked. Its own region rather than part of the thinking line: the
    // line is replaced on every push and carries the stop control, and a queue that came and went
    // with it would take the focus off a cross somebody was reaching for. (issue #288.)
    waitingHtml: chatWaitingHtml(state.waiting),
    cappedHtml: chatCappedHtml(state.capped),
    // THE SAME BUILDER the first render uses. This expression lived here and in `regionsOf`, and a
    // retry button added to one of them would have shipped on one of the two paths.
    failureHtml: chatFailureHtml(state.failure, state.canRetry, state.messages.length),
    pickerHtml: chatPickerHtml({ providers: state.providers, refused: [] }, state.providerId, state.modelId),
    // The ROWS as well as the picker. They were drawn once, when the page was built, so pressing a
    // prompt changed the words in the box and left every button looking exactly as it had — and a row
    // of buttons where the one in force looks like the others is a row you have to remember.
    presetsHtml: chatPresetRowsHtml(state.promptPresets, state.modelPresets, state.promptId, state.chosenModelId),
    // Trimmed, because the instruction they were built into is: an untrimmed mark would not match
    // the text in the box and the page would silently colour nothing.
    marks: { ...state.marks, role: state.marks.role.trim(), task: state.marks.task.trim() },
    modelId: state.modelId,
    // The agent-mode box, drawn from what the HOST holds: a cancelled confirmation or a refusal pushes
    // the old access back, and the page's own tick is undone by it.
    access: state.access,
    agentOffered: state.agentOffered,
    // Issue #492. Both were in the state and in the page's handler, and missing HERE: the spending line
    // kept the cost of the first render however many turns were paid for, and the Re-ask caption — and
    // the empty-box re-ask behind it — kept the model the tab was opened on.
    spend: state.spend,
    reask: state.reask,
  };
}

/**
 * Build the message and post it — unless it is exactly what this conversation was last sent.
 *
 * @returns whether anything was actually sent
 */
export function sendChatState(
  entry: { readonly id: object; readonly panel: { post(message: unknown): void } }, state: ChatPushState,
): boolean {
  const payload = chatStateMessage(state);
  const serialised = JSON.stringify(payload);
  if (lastPushed.get(entry.id) === serialised) {
    return false;
  }
  lastPushed.set(entry.id, serialised);
  entry.panel.post(payload);

  return true;
}
