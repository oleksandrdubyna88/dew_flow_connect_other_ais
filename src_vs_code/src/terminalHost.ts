import type * as vscode from 'vscode';
import type { LiveTerminal } from './terminalSource';

/**
 * The `vscode` seams of the terminal conversation's re-attach (E3.S1, contract C2). Host code: every decision is
 * `terminalReattach.ts`'s (pure, tested by its own unit tests); this file only reads the window and moves panels.
 *
 * <p>Unit tests cannot load this module (it names `vscode`); `terminalReattachScenario.ts` runs it in a real editor, in CI.</p>
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
