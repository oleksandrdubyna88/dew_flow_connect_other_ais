import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { STILL_COPYING, fromTheTerminal } from '../../chatCapture';
import { NOTHING_COPIED } from '../../terminalSource';
import { activated } from './missingDataRootScenario';

/**
 * A terminal press takes the text the person copied, in a real editor (S1r-H1 of the E1 stories document, the chat opens from a terminal).
 *
 * <p>What a value cannot answer: that the real clipboard is read as it is, is left as it is, that the one latch holds against two
 * real presses, and that an empty clipboard gives the owner's sentence. The unit tests (`terminalCapture.test.ts`) fake the
 * clipboard. It runs only in CI's `extension · a real editor` job: a real VS Code does not start on a machine without
 * `libnss3`.</p>
 *
 * <p>The terminal is named like a CredsForDevs one (`Creds: h / s`) and shown, so the press happens as it would; the read does not
 * use the terminal. The clipboard holds a random probe, and must hold it after every press: the door writes nothing.</p>
 */

/** The clipboard, read through the host. */
const clipboardNow = async (): Promise<string> => vscode.env.clipboard.readText();

export async function aTerminalPressTakesTheCopiedTextAndLeavesTheClipboard(): Promise<void> {
  await activated();
  const probe = `coai-probe-${Math.random().toString(36).slice(2)}`;
  const terminal = vscode.window.createTerminal({ name: 'Creds: h / s' });
  try {
    terminal.show();
    await vscode.env.clipboard.writeText(probe);

    const taken = await fromTheTerminal();
    assert.deepEqual(taken, { text: probe, failure: '' }, `the door did not deliver the copied text; it said: ${taken.failure}`);
    assert.equal(await clipboardNow(), probe, 'the door changed the clipboard');

    // Two presses at once share ONE latch, because the keyboard path writes a marker to the clipboard while it runs: exactly one
    // is refused with the latch's sentence, and the other is the copied text. A door with no latch, or a new latch for each call,
    // would let both through; that the latch is the one the keyboard path holds is proved by the source scan S1r-T6.
    const both = await Promise.all([fromTheTerminal(), fromTheTerminal()]);
    const refused = both.filter((one) => one.failure === STILL_COPYING);
    const read = both.filter((one) => one.text === probe);
    assert.equal(refused.length, 1, `two simultaneous presses: ${refused.length} were refused by the latch, not exactly one`);
    assert.equal(read.length, 1, `two simultaneous presses: ${read.length} read the probe, not exactly one`);
    assert.equal(await clipboardNow(), probe, 'two simultaneous presses left the clipboard changed');

    for (const blank of ['', '  \n']) {
      await vscode.env.clipboard.writeText(blank);
      const none = await fromTheTerminal();
      assert.deepEqual(none, { text: '', failure: NOTHING_COPIED }, `a clipboard of ${JSON.stringify(blank)} did not give the owner's sentence`);
      assert.equal(await clipboardNow(), blank, 'an empty press changed the clipboard');
    }
  } finally {
    terminal.dispose();
  }
}

/**
 * The skip list's DEFAULT, as the host reports it (S3-H1 of the E1 stories document, the chat opens from a terminal).
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
