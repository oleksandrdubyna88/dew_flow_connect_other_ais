import * as assert from 'node:assert/strict';
import * as fs from 'node:fs';
import * as path from 'node:path';
import * as vscode from 'vscode';

import { matchedSource, snapshots } from '../../chatCapture';
import { ChatPanels } from '../../chatPanels';
import { sourceOfTerminal } from '../../chatStore';
import { terminalsShareName } from '../../chatReturn';
import { backToSource } from '../../chatReturnCommand';
import { isClaudeSessionTab } from '../../sessionKey';
import { registeredChat, until } from './hostHelpers';

/**
 * How many times the notice ledger holds this exact refusal. The ledger (`notifications.jsonl` in the launch's data directory) is where
 * every `notify` is written down, and the sentence is the one the road back says for several terminals of one name — taken from the
 * code that says it, so the two cannot drift. A row of an earlier press is counted too: the caller compares before and after.
 */
function severalNamedSaid(count: number, name: string): number {
  const ledger = path.join(process.env['COAI_DATA_DIR'] ?? '', 'notifications.jsonl');
  const lines = fs.existsSync(ledger) ? fs.readFileSync(ledger, 'utf8').split('\n') : [];
  const sentence = JSON.stringify(terminalsShareName(count, name)).slice(1, -1);

  return lines.filter((line) => line.includes('"chat-cannot-go-back"') && line.includes(sentence)).length;
}

/**
 * The chat's terminal road and the steal filter, in a real editor (S2-H1 and S2-H2 of the E1 stories document, the chat opens from a terminal).
 *
 * <p>What a value cannot answer: `Terminal.show()` and the window's terminal list exist only in a host, and a webview of
 * Claude Code's view type is what makes the tab match by label real. They run only in CI's job `extension · a real editor`: a
 * real VS Code does not start on a machine without `libnss3`.</p>
 */

/**
 * The way back for a chat opened from a terminal: its OWN terminal while it runs, then the ONE live terminal of its recorded name,
 * and never a guess between two. `away()` runs as the last thing before each press, so the terminal in front is never the one the
 * step expects.
 */
export async function backToATerminalInARealEditor(): Promise<void> {
  const name = 'Creds: h / s';
  const own = vscode.window.createTerminal({ name });
  const other = vscode.window.createTerminal({ name: 'other' });
  const namesake: vscode.Terminal[] = [];
  try {
    const away = async (): Promise<void> => {
      other.show();
      await until(() => vscode.window.activeTerminal === other, 'the terminal in front could not be set to the other one before a press', 5000);
    };

    // (1) The chat's own terminal.
    const panels = new ChatPanels();
    registeredChat(panels, own, 'host-back-to-terminal', sourceOfTerminal(name));
    await away();
    await backToSource(panels, { webview: 'coaiChat', coaiConversation: 'host-back-to-terminal' });
    await until(() => vscode.window.activeTerminal === own, 'the chat\u2019s own terminal did not come to the front', 5000);

    // (2) A restored chat is keyed by a plain object, so it goes by the recorded name.
    const restored = new ChatPanels();
    registeredChat(restored, {}, 'host-back-restored', sourceOfTerminal(name));
    await away();
    await backToSource(restored, { webview: 'coaiChat', coaiConversation: 'host-back-restored' });
    await until(() => vscode.window.activeTerminal === own, 'the ONE terminal of the recorded name did not come to the front', 5000);

    // (3) With a namesake open the name names two terminals: nothing is guessed.
    namesake.push(vscode.window.createTerminal({ name }));
    // The refusal is awaited by name as well as the terminal in front being unchanged: `away()` has already made `other` active, so
    // "nothing changed" alone would also pass when the door did nothing at all, or when it said nothing.
    assert.ok(process.env['COAI_DATA_DIR'], 'the launcher named no data directory, so the notice ledger cannot be read');
    const saidBefore = severalNamedSaid(2, name);
    await away();
    await backToSource(restored, { webview: 'coaiChat', coaiConversation: 'host-back-restored' });
    await until(() => severalNamedSaid(2, name) > saidBefore, `the refusal "${terminalsShareName(2, name)}" was never said for this press`, 5000);
    assert.equal(vscode.window.activeTerminal, other, 'a terminal was picked between two of one name');

    // (4) The chat's own terminal still wins while its namesake is open.
    await away();
    await backToSource(panels, { webview: 'coaiChat', coaiConversation: 'host-back-to-terminal' });
    await until(() => vscode.window.activeTerminal === own, 'the chat\u2019s own terminal did not win over its namesake', 5000);
  } finally {
    own.dispose();
    other.dispose();
    namesake.forEach((one) => one.dispose());
  }
}

/**
 * A tab that looks like Claude Code's and wears the name of a terminal conversation must not take it: `sourceSession` reads a panel
 * whose key is not a tab as "its tab has gone" and re-keys it by label. The webview here has Claude Code's view type, so the tab is
 * matched by label for real. The positive (3) is what makes (1) and (2) mean something.
 */
export async function aClaudeLookingTabDoesNotTakeATerminalChat(): Promise<void> {
  const label = 'host-terminal-steal';
  const panel = vscode.window.createWebviewPanel('claudeVSCodePanel', label, vscode.ViewColumn.One, {});
  const terminal = vscode.window.createTerminal({ name: label });
  try {
    await until(() => vscode.window.tabGroups.activeTabGroup.activeTab?.label === label, `the webview ${label} never became the active tab`, 5000);
    const active = snapshots().active;
    assert.ok(active !== undefined, `the webview has no active tab; the active label is ${vscode.window.tabGroups.activeTabGroup.activeTab?.label ?? 'none'}`);
    assert.ok(isClaudeSessionTab(active), `the host reports the view type ${JSON.stringify(active.viewType)}, which is not Claude Code's panel`);

    // (1) A live terminal conversation.
    const panels = new ChatPanels();
    registeredChat(panels, terminal, label, sourceOfTerminal(label));
    const first = matchedSource(panels).source;
    assert.equal(first?.kind, 'new', `a tab took a terminal conversation: ${JSON.stringify(first?.kind)}`);
    assert.equal(first?.key, vscode.window.tabGroups.activeTabGroup.activeTab, 'the new conversation is not keyed by the tab');
    assert.ok(panels.get(terminal) !== undefined, 'the terminal conversation was taken out of the registry');

    // (2) The restored form: keyed by a plain object.
    const restored = new ChatPanels();
    registeredChat(restored, {}, label, sourceOfTerminal(label));
    assert.equal(matchedSource(restored).source?.kind, 'new', 'a tab took a RESTORED terminal conversation');

    // (3) The positive: an orphaned panel of no source IS taken by label, so (1) and (2) mean something.
    const orphan = new ChatPanels();
    registeredChat(orphan, {}, label, { kind: 'none' });
    assert.equal(matchedSource(orphan).source?.kind, 'rekey', 'the scenario\u2019s tab is not matched by label, so the two answers above prove nothing');
  } finally {
    panel.dispose();
    terminal.dispose();
  }
}
