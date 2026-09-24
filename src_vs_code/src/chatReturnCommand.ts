import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { whereConversationSits } from './chatRegistry';
import { asText } from './chatShow';
import {
  BackTo,
  NOWHERE,
  backTo,
  fileFailed,
  focusGroupCommand,
  sessionFailed,
  tabNotActivated,
  tabReachable,
} from './chatReturn';
import { notify } from './notify';

/**
 * *CoAI: back to where this chat came from* — issue #314. The decision is `chatReturn.ts`; this finds
 * the conversation, does the one thing the decision names, and says so when it cannot.
 */

/** How long the recipe waits for the tab to become active — Claude Code's own `bringTabToFront` waits a second. */
const ACTIVATION_MS = 1000;
const ACTIVATION_POLL_MS = 50;

/**
 * @param asked the menu command's argument: `{webview: 'coaiChat', coaiConversation: <saveId>}` from a
 *   right-click (the page's `data-vscode-context`), nothing from the chord
 */
export async function backToSource(panels: ChatPanels, asked: unknown): Promise<void> {
  const entry = chatAsked(panels, asked);
  const thread = entry === undefined ? undefined : threads.get(entry.id);
  if (entry === undefined || thread === undefined) {
    refuse('There is no coai chat here to go back from.');

    return;
  }
  const tab = reachableTab(panels.keyOf(entry.id));
  await go(backTo({ liveTab: tab !== undefined, source: thread.source }), tab);
}

/** The right-clicked chat by the id its page carries, else the active chat panel (the chord). */
function chatAsked(panels: ChatPanels, asked: unknown): ChatEntry | undefined {
  const id = conversationIdOf(asked);
  const key = id.length > 0 ? whereConversationSits(panels, id) : undefined;

  return key !== undefined ? panels.get(key) : activeChat(panels);
}

function conversationIdOf(asked: unknown): string {
  const id = (asked as { coaiConversation?: unknown } | undefined)?.coaiConversation;

  return typeof id === 'string' ? id : '';
}

function activeChat(panels: ChatPanels): ChatEntry | undefined {
  return panels.known()
    .map(({ key }) => panels.get(key))
    .find((entry) => entry?.panel.isActive() === true);
}

/**
 * The tab this chat was opened from, when it is still open and the recipe can reach it. A synthetic key
 * (a restored or picker-opened chat) is no tab at all, and finds nothing here.
 */
function reachableTab(key: object | undefined): vscode.Tab | undefined {
  const tab = vscode.window.tabGroups.all.flatMap((group) => group.tabs).find((one) => one === key);

  return tab !== undefined && tabReachable(tab.group.viewColumn, tab.group.tabs.indexOf(tab)) ? tab : undefined;
}

async function go(answer: BackTo, tab: vscode.Tab | undefined): Promise<void> {
  if (answer.kind === 'tab') {
    return tab === undefined ? refuse(NOWHERE) : toTab(tab);
  }

  return fromRecord(answer);
}

/** Everything but the live tab: what the conversation RECORDED. Exhaustive — a new arm fails to compile. */
async function fromRecord(answer: Exclude<BackTo, { kind: 'tab' }>): Promise<void> {
  switch (answer.kind) {
    case 'session':
      return toSession(answer.sessionId);
    case 'file':
      return toFile(answer.uri);
    case 'nowhere':
      return refuse(NOWHERE);
    default:
      return unreachable(answer);
  }
}

function unreachable(answer: never): void {
  refuse(`CoAI does not know how to go back to ${JSON.stringify(answer)}.`);
}

/**
 * The editor's own recipe, as Claude Code's `bringTabToFront` does it: focus the group, open the editor
 * at the tab's index, and wait for the tab to say it is active. Calls nothing of Claude's.
 */
async function toTab(tab: vscode.Tab): Promise<void> {
  await vscode.commands.executeCommand(focusGroupCommand(tab.group.viewColumn));
  await vscode.commands.executeCommand('workbench.action.openEditorAtIndex', tab.group.tabs.indexOf(tab));
  if (!(await becameActive(tab))) {
    refuse(tabNotActivated(tab.label));
  }
}

async function becameActive(tab: vscode.Tab): Promise<boolean> {
  for (let waited = 0; waited < ACTIVATION_MS && !tab.isActive; waited += ACTIVATION_POLL_MS) {
    await new Promise((resolve) => setTimeout(resolve, ACTIVATION_POLL_MS));
  }

  return tab.isActive;
}

/**
 * Claude Code's own command, with `programmatic: true` so it neither moves Claude's preferred location
 * nor grabs its input. Another extension's internal command, not an API — so the CALL is the check, and
 * a rejection (not installed, or failing on a session it no longer knows) is said by name.
 */
async function toSession(sessionId: string): Promise<void> {
  try {
    await vscode.commands.executeCommand(
      'claude-vscode.editor.open', sessionId, undefined, undefined, undefined, undefined, { programmatic: true },
    );
  } catch (reason) {
    refuse(sessionFailed(sessionId, asText(reason)));
  }
}

async function toFile(uri: string): Promise<void> {
  try {
    await vscode.window.showTextDocument(vscode.Uri.parse(uri), { preview: false });
  } catch (reason) {
    refuse(fileFailed(uri, asText(reason)));
  }
}

function refuse(title: string): void {
  void notify({
    as: 'warning',
    class: 'refusal',
    source: 'backToSource',
    code: 'chat-cannot-go-back',
    title,
  });
}
