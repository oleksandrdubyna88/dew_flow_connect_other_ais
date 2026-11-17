import * as vscode from 'vscode';
import type { ChatPanels } from './chatPanels';
import type { ConversationSource } from './chatStore';
import { type Marks, type Reattach, type ReattachWindow, confirmBound, guarded, reasonOf, reattachIn, reattachRestoredIn } from './terminalReattach';
import type { LiveTerminal } from './terminalSource';

/**
 * The `vscode` seams of the terminal conversation's re-attach (E3.S1, contract C2). Host code: every decision AND every orchestration
 * is `terminalReattach.ts`'s (pure, tested by its own unit tests); this file only reads the window, owns the marks and logs.
 *
 * <p>Unit tests cannot load this module (it imports `vscode`); `terminalReattachScenario.ts` runs it in a real editor, in CI.</p>
 */

/**
 * The window's terminals, as the decisions read them: the key is the `vscode.Terminal` object, and a terminal whose process has
 * ended (`exitStatus` set) is `exited`.
 *
 * <p>It takes the list as a parameter, never reading `window.terminals` itself: a caller that both decides and acts reads the
 * window ONCE, so the decision and the action see one list. This is the one mapper — a second copy is a second answer to
 * "is this terminal alive".</p>
 */
export function liveTerminals(terminals: readonly vscode.Terminal[]): readonly LiveTerminal[] {
  return terminals.map((one) => ({ key: one, name: one.name, exited: one.exitStatus !== undefined }));
}

/** The provisional marks of this window (see `Marks`): one WeakMap, so a mark goes with its terminal. */
const marks: Marks = new WeakMap();

/** The registry and the marks with the window read afresh on every look. */
const windowOf = (panels: ChatPanels): ReattachWindow => ({ panels, marks, terminals: () => liveTerminals(vscode.window.terminals) });

/** One `console.info` line — the name and the reason — for `several terminals`, `several orphans` and `undo` only. */
function said(name: string, answer: Reattach): void {
  const reason = reasonOf(answer);
  if (reason !== undefined) {
    console.info(`ConnectOtherAIs: the conversation of the terminal “${name}”, ${reason}.`);
  }
}

/** What a step that failed answers: nothing was done. */
const HELD: Reattach = { kind: 'leave', why: 'held' };

/** A failed step is one `console.warn` line; it never reaches the caller (`guarded`). */
const warnOf = (what: string) => (reason: string): void => {
  console.warn(`ConnectOtherAIs: ${what} failed: ${reason}`);
};

/** One re-attach look at the window for `name`, said in the console when it is ambiguous, and guarded: it never throws. */
function reattachBy(panels: ChatPanels, name: string, look: (win: ReattachWindow) => Reattach): Reattach {
  return guarded(() => {
    const answer = look(windowOf(panels));
    said(name, answer);

    return answer;
  }, HELD, warnOf(`re-attaching a conversation to the terminal “${name}”`));
}

/**
 * Re-attach the conversation of the terminal called `name`, when nothing is in doubt: the window is read twice by `reattachOnce`
 * and the re-key follows the second read with no `await` between. `panels.rekey` itself still refuses a taken destination; that
 * refusal answers `leave`, not an error. Said in the console for the ambiguous answers only — it is housekeeping, not a notification.
 * A defect in it is a `console.warn` and `leave` `held`, never a thrown error: the door and the restore go on.
 */
export const reattachByName = (panels: ChatPanels, name: string): Reattach => reattachBy(panels, name, (win) => reattachIn(win, name));

/** The restore trigger (`reattachRestoredIn`): a restored terminal conversation finds its terminal. Guarded like `reattachByName`. */
export function reattachRestored(panels: ChatPanels, source: ConversationSource): Reattach {
  return reattachBy(panels, source.kind === 'terminal' ? source.name : '', (win) => reattachRestoredIn(win, source));
}

/**
 * The person used a conversation from its terminal (the door, *take* or *add* found it, or go-to showed it): its bind is final —
 * but only while the plan for `name` still answers `bound` for THIS terminal (`confirmBound`). `name` is the name the caller judged the
 * terminal under (the door reads it ONCE, before it awaits), never the terminal's name now: a rename in between confirms nothing.
 * Guarded: a defect is a `console.warn`, never a failed press.
 */
export function confirmReattach(panels: ChatPanels, terminal: vscode.Terminal, name: string): void {
  guarded(() => confirmBound(windowOf(panels), terminal, name), false, warnOf(`confirming the conversation of the terminal “${name}”`));
}

/** Activation: the trigger of a terminal that opens — creds opens its terminals late, after the serializer restored the chats. */
export function bindReattach(context: Pick<vscode.ExtensionContext, 'subscriptions'>, panels: ChatPanels): void {
  context.subscriptions.push(vscode.window.onDidOpenTerminal((opened) => {
    reattachByName(panels, opened.name);
  }));
}
