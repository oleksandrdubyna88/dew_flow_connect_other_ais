import * as assert from 'node:assert/strict';
import * as vscode from 'vscode';

import { ChatPanels } from '../../chatPanels';
import { sourceOfTerminal } from '../../chatStore';
import { bindReattach } from '../../terminalHost';
import { registeredChat, until } from './hostHelpers';

/**
 * A terminal that opens takes back the conversation of its name, and a second terminal of the name takes it off again
 * (S1-H1 of the E3 stories document, the chat finds its terminal again).
 *
 * <p>What a value cannot answer: `window.onDidOpenTerminal` and the window's terminal list exist only in a host. The scenario runs the
 * REAL `bindReattach` over its OWN `ChatPanels` (as `terminalReturnScenario.ts` uses its own registry), so nothing of the person's
 * is touched. It runs only in CI's job `extension · a real editor`: a real VS Code does not start on a machine without `libnss3`.
 * A real reload with creds is the owner's R8.</p>
 */
export async function aTerminalTakesBackItsConversationInARealEditor(): Promise<void> {
  const subscriptions: vscode.Disposable[] = [];
  const opened: vscode.Terminal[] = [];
  const open = (name: string): vscode.Terminal => {
    const terminal = vscode.window.createTerminal({ name });
    opened.push(terminal);

    return terminal;
  };
  try {
    const panels = new ChatPanels();
    bindReattach({ subscriptions }, panels);

    // (1) A conversation restored before its terminal opens: the open trigger binds it.
    const one = 'Creds: h / s';
    const restored = registeredChat(panels, {}, 'host-reattach-one', sourceOfTerminal(one));
    const terminal = open(one);
    await until(() => panels.get(terminal)?.id === restored, 'a terminal that opened after its chat was restored did not take the chat back', 5000);

    // (2) Case A in a real editor: the first terminal of a name binds, the second undoes it. Waiting between the two is what makes
    // the undo, not a never-bind, the thing tested.
    const two = 'Creds: h / t';
    const second = registeredChat(panels, {}, 'host-reattach-two', sourceOfTerminal(two));
    const first = open(two);
    await until(() => panels.get(first)?.id === second, 'the first terminal of the name did not take the conversation', 5000);
    const namesake = open(two);
    await until(() => panels.get(first) === undefined && panels.get(namesake) === undefined, 'a second terminal of the name did not take the conversation off the first', 5000);
    const own = panels.keyOf(second);

    assert.ok(own !== undefined && own !== first && own !== namesake, 'the undone conversation is not on a key of its own');
  } finally {
    subscriptions.forEach((one) => { one.dispose(); });
    opened.forEach((one) => { one.dispose(); });
  }
}
