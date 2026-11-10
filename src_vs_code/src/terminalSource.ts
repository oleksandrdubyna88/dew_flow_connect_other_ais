import { Borrowed, Capture, Clipboard, borrowClipboard } from './selectionCapture';

/**
 * Copying the active terminal's selection — pure, with no `vscode` in it.
 *
 * <p><b>No keystroke, ever.</b> A synthetic Ctrl+C reaches a terminal as SIGINT: in `ssh`, `tmux` or
 * the Claude CLI that interrupts the program instead of copying anything. The copy is VS Code's own
 * `copySelection` command, which the host runs through the `execute` seam below, inside the same
 * clipboard borrow the keyboard capture uses. There is no PowerShell side, no launch and no reach
 * check here: nothing about a terminal copy needs the Windows half of the machine.</p>
 */

/** The command that copies the active terminal's selection to the clipboard. */
export const TERMINAL_COPY = 'workbench.action.terminal.copySelection';

/** What a person is told when nothing was selected — the owner's own words. */
export const NO_TERMINAL_SELECTION = 'Select text in the terminal first.';

/**
 * What a finished borrow means for a terminal.
 *
 * <p>Nothing landed, or only white space landed: no selection. The text is NOT trimmed — the
 * selection is the passage, and indentation in a terminal is content.</p>
 */
export function terminalPassage(borrowed: Borrowed<unknown>): Capture {
  if (!borrowed.landed || borrowed.text.trim().length === 0) {
    return { text: '', failure: NO_TERMINAL_SELECTION };
  }

  return { text: borrowed.text, failure: '' };
}

/**
 * Copy the active terminal's selection through the clipboard, and give the clipboard back.
 *
 * <p>A copy command that REJECTS is not "select text first": the person did select, and a sentence
 * that blames them hides the real cause. It says what the command said. The borrow has restored the
 * clipboard by then, whichever way the command ended.</p>
 *
 * @param execute runs one VS Code command by id — `vscode.commands.executeCommand` in the host
 * @param clipboard the clipboard, injected
 */
export async function captureTerminalSelection(
  execute: (command: string) => PromiseLike<unknown>,
  clipboard: Clipboard,
): Promise<Capture> {
  try {
    return terminalPassage(await borrowClipboard(clipboard, () => execute(TERMINAL_COPY)));
  } catch (reason) {
    const said = reason instanceof Error ? reason.message : String(reason);

    return { text: '', failure: `The terminal selection could not be copied: ${said}` };
  }
}
