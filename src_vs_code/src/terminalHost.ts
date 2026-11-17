import * as vscode from 'vscode';
import type { ChatPanels } from './chatPanels';
import { type Marks, type Reattach, type ReattachWindow, confirmBound, reasonOf, reattachIn } from './terminalReattach';
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

/**
 * Re-attach the conversation of the terminal called `name`, when nothing is in doubt: the window is read twice by `reattachOnce`
 * and the re-key follows the second read with no `await` between. `panels.rekey` itself still refuses a taken destination; that
 * refusal answers `leave`, not an error. Said in the console for the ambiguous answers only — it is housekeeping, not a notification.
 */
export function reattachByName(panels: ChatPanels, name: string): Reattach {
  const answer = reattachIn(windowOf(panels), name);
  said(name, answer);

  return answer;
}

/**
 * The person used a conversation from its terminal (the door, *take* or *add* found it, or go-to showed it): its bind is final —
 * but only while the plan for the terminal's name still answers `bound` for THIS terminal (`confirmBound`).
 */
export function confirmReattach(panels: ChatPanels, terminal: vscode.Terminal, name: string = terminal.name): void {
  confirmBound(windowOf(panels), terminal, name);
}

/** Activation: the trigger of a terminal that opens — creds opens its terminals late, after the serializer restored the chats. */
export function bindReattach(context: Pick<vscode.ExtensionContext, 'subscriptions'>, panels: ChatPanels): void {
  context.subscriptions.push(vscode.window.onDidOpenTerminal((opened) => {
    try {
      reattachByName(panels, opened.name);
    } catch (reason) {
      console.warn(`ConnectOtherAIs: re-attaching a conversation to the terminal “${opened.name}” failed: ${reason instanceof Error ? reason.message : String(reason)}`);
    }
  }));
}
