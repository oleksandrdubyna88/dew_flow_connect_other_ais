import * as vscode from 'vscode';
import { ChatEntry, ChatPanels } from './chatPanels';
import { threads } from './chatThread';
import { whereConversationSits } from './chatRegistry';
import { asText } from './chatShow';
import { snapshots } from './chatCapture';
import type { ConversationSource } from './chatStore';
import {
  BackTo,
  NOWHERE,
  backTo,
  bufferGone,
  fileFailed,
  focusGroupCommand,
  recordedRoute,
  recoveredTab,
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

const NO_CHAT = 'There is no coai chat here to go back from.';

/** The chat the command is about: its registry key, the label it was registered under, and the entry. */
interface AskedChat {
  readonly key: object;
  readonly label: string;
  readonly entry: ChatEntry;
}

/**
 * @param asked the menu command's argument: `{webview: 'coaiChat', coaiConversation: <saveId>}` from a
 *   right-click (the page's `data-vscode-context`), nothing from the chord
 */
export async function backToSource(panels: ChatPanels, asked: unknown): Promise<void> {
  const chat = chatAsked(panels, asked);
  const thread = chat === undefined ? undefined : threads.get(chat.entry.id);
  if (chat === undefined || thread === undefined) {
    refuse(NO_CHAT);

    return;
  }
  const tab = sourceTab(chat, thread.source);
  await go(backTo({ liveTab: tab !== undefined, source: thread.source }), tab, thread.source);
}

/**
 * The right-clicked chat by the id its page carries; the ACTIVE chat only when no id came at all (the
 * chord). An id that names no chat here is refused rather than swapped for the active one, which could be
 * a different conversation with a different source. (codex and gemini, the code round.)
 */
function chatAsked(panels: ChatPanels, asked: unknown): AskedChat | undefined {
  const id = conversationIdOf(asked);

  return id.length > 0 ? chatNamed(panels, id) : activeChat(panels);
}

function conversationIdOf(asked: unknown): string {
  const id = (asked as { coaiConversation?: unknown } | undefined)?.coaiConversation;

  return typeof id === 'string' ? id : '';
}

function chatNamed(panels: ChatPanels, id: string): AskedChat | undefined {
  const key = whereConversationSits(panels, id);

  return key === undefined ? undefined : chatUnder(panels, key);
}

function activeChat(panels: ChatPanels): AskedChat | undefined {
  const active = panels.known().find(({ key }) => panels.get(key)?.panel.isActive() === true);

  return active === undefined ? undefined : chatUnder(panels, active.key);
}

function chatUnder(panels: ChatPanels, key: object): AskedChat | undefined {
  const entry = panels.get(key);
  const label = panels.known().find((one) => one.key === key)?.label ?? '';

  return entry === undefined ? undefined : { key, label, entry };
}

/**
 * The tab this chat came from, when it is open and the recipe can reach it: the very `Tab` it was
 * registered under, or — once that object stopped being one of the tabs — the tab still showing its
 * source. MEASURED on VS Code 1.139: moving a tab to another group replaces its `Tab` object, and without
 * this the file was opened a second time in whichever group was active. (Our own code review; the
 * real-editor scenario went red before this.)
 */
function sourceTab(chat: AskedChat, source: ConversationSource): vscode.Tab | undefined {
  const all = vscode.window.tabGroups.all.flatMap((group) => group.tabs);
  const recovered = recoveredTab(snapshots().all, source, chat.label);
  const tab = all.find((one) => one === chat.key) ?? all.find((one) => one === recovered?.key);

  return tab !== undefined && tabReachable(tab.group.viewColumn, tab.group.tabs.indexOf(tab)) ? tab : undefined;
}

async function go(answer: BackTo, tab: vscode.Tab | undefined, source: ConversationSource): Promise<void> {
  if (answer.kind === 'tab') {
    return tab === undefined ? refuse(NOWHERE) : toTabOrRecord(tab, source);
  }

  return fromRecord(answer);
}

/**
 * The tab, and — when it will not come forward — what the conversation recorded, before giving up. A
 * failed tab is not the end of the road while a session or a file is known. (codex, the code round.)
 */
async function toTabOrRecord(tab: vscode.Tab, source: ConversationSource): Promise<void> {
  if (await toTab(tab)) {
    return;
  }
  const recorded = recordedRoute(source);

  return recorded.kind === 'nowhere' ? refuse(tabNotActivated(tab.label)) : fromRecord(recorded);
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
 * at the tab's index, and wait for the tab to say it is active. Calls nothing of Claude's. Whether it
 * worked is the answer — a command that rejected is a tab that did not come forward.
 */
async function toTab(tab: vscode.Tab): Promise<boolean> {
  try {
    await vscode.commands.executeCommand(focusGroupCommand(tab.group.viewColumn));
    await vscode.commands.executeCommand('workbench.action.openEditorAtIndex', tab.group.tabs.indexOf(tab));

    return await becameActive(tab);
  } catch {
    return false;
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

/** The recorded document — except an unsaved buffer that is gone, which would open as a NEW empty one. */
async function toFile(uri: string): Promise<void> {
  const gone = bufferGone(uri, vscode.workspace.textDocuments.map((document) => document.uri.toString()));
  if (gone.length > 0) {
    refuse(fileFailed(uri, gone));

    return;
  }
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
