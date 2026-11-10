import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { STILL_COPYING, fromTheTerminal } from '../../chatCapture';
import { NO_TERMINAL_SELECTION } from '../../terminalSource';
import { activated, settle } from './missingDataRootScenario';

/**
 * A terminal's selection through the clipboard borrow, in a real editor (todo/PLAN_the_chat_opens_from_a_terminal_E1.md, S1-H1).
 *
 * <p>What a value cannot answer: that `workbench.action.terminal.copySelection`, awaited, has put the selection on the clipboard
 * by the time the borrow reads it, and that the borrow gives the clipboard back. The unit tests (`terminalCapture.test.ts`) fake the
 * command; this runs the real one against a real terminal. It runs only in CI's `extension · a real editor` job: a real VS Code
 * does not start on a machine without `libnss3`.</p>
 *
 * <p>The terminal is named like a CredsForDevs one (`Creds: h / s`) because that is the terminal this door is for. It prints a
 * random probe, and is selected whole and captured until the probe is in the text — the shell needs a moment to start and to
 * echo. Then the selection is cleared and the capture must say there is none. The clipboard holds a sentinel the whole time, and
 * must hold it again after every capture, which is the borrow's promise.</p>
 */

const WAIT_MS = 10_000;
const STEP_MS = 250;

/** The clipboard, read through the host. */
const clipboardNow = async (): Promise<string> => vscode.env.clipboard.readText();

/** Select everything in the active terminal and capture it, until `probe` is in the text. */
async function captureUntil(probe: string): Promise<string> {
  const deadline = Date.now() + WAIT_MS;
  let last = '';
  while (Date.now() < deadline) {
    await vscode.commands.executeCommand('workbench.action.terminal.selectAll');
    const passage = await fromTheTerminal();
    last = passage.text.length > 0 ? passage.text : passage.failure;
    if (passage.text.includes(probe)) {
      return passage.text;
    }
    await settle(STEP_MS);
  }
  assert.fail(`the terminal's selection never held the probe within ${WAIT_MS / 1000}s; the last capture said: ${last}`);
}

export async function aTerminalSelectionIsCapturedAndTheClipboardGivenBack(): Promise<void> {
  await activated();
  const sentinel = `coai-sentinel-${Date.now()}`;
  const probe = `coai-probe-${Math.random().toString(36).slice(2)}`;
  await vscode.env.clipboard.writeText(sentinel);
  const terminal = vscode.window.createTerminal({ name: 'Creds: h / s' });
  try {
    terminal.show();
    terminal.sendText(`echo ${probe}`);

    const text = await captureUntil(probe);
    assert.ok(text.includes(probe), 'the capture does not hold what the terminal printed');
    assert.equal(await clipboardNow(), sentinel, 'the clipboard was taken for the capture and not given back');

    // Two presses at once share ONE latch, because both captures borrow the one clipboard: exactly one is refused with the
    // latch's sentence, and the other is the capture. A door with a latch of its own would let both through.
    await vscode.commands.executeCommand('workbench.action.terminal.selectAll');
    const both = await Promise.all([fromTheTerminal(), fromTheTerminal()]);
    const refused = both.filter((one) => one.failure === STILL_COPYING);
    const captured = both.filter((one) => one.text.includes(probe));
    assert.equal(refused.length, 1, `two simultaneous presses: ${refused.length} were refused by the shared latch, not exactly one`);
    assert.equal(captured.length, 1, `two simultaneous presses: ${captured.length} captured the probe, not exactly one`);
    assert.equal(await clipboardNow(), sentinel, 'two simultaneous presses left the clipboard changed');

    await vscode.commands.executeCommand('workbench.action.terminal.clearSelection');
    const none = await fromTheTerminal();
    assert.equal(none.text, '', 'a cleared selection still produced a passage');
    assert.equal(none.failure, NO_TERMINAL_SELECTION, 'no selection did not give the owner\'s sentence');
    assert.equal(await clipboardNow(), sentinel, 'a capture with no selection left the marker on the clipboard');
  } finally {
    terminal.dispose();
  }
}

/**
 * The skip list's DEFAULT, as the host reports it (todo/PLAN_the_chat_opens_from_a_terminal_E1.md, S3-H1).
 *
 * <p>The manifest test (`terminalDoorWiring.test.ts`) proves `package.json` asks for the default; only a real window can say
 * the host took it. If this is red in CI, `configurationDefaults` does not work for this setting and the chord goes to the
 * shell: stop and tell the owner, do not work around it.</p>
 */
export async function theSkipListDefaultHoldsTheTerminalDoor(): Promise<void> {
  await activated();
  const inspected = vscode.workspace.getConfiguration().inspect<readonly string[]>('terminal.integrated.commandsToSkipShell');
  const fallback = inspected?.defaultValue ?? [];

  assert.ok(
    fallback.includes('coai.askFromTerminal'),
    `the default of terminal.integrated.commandsToSkipShell does not hold coai.askFromTerminal, so Ctrl+Alt+A in a terminal goes to the shell; it holds ${fallback.length} commands`,
  );
}
