import type { ConversationSource } from './chatStore';
import { type TabSnapshot, isOrdinaryEditorTab, rekeysByLabel } from './sessionKey';

/**
 * Where a coai chat goes back to — issue #314. The decision alone, with nothing of VS Code in it.
 *
 * <p>*Go to* (`chatGoto.ts`) takes a Claude Code tab or an editor to its conversation; this is the road
 * the other way. It only FOLLOWS what the conversation already records and never binds anything: the
 * live tab it was opened from while that tab is open and reachable, else the recorded Claude session,
 * else the recorded file. A conversation that knows none of them says so — a title is never a key
 * (`chatGoto.ts`).</p>
 */

export interface ReturnAsked {
  /** The chat's live key is a tab that is still open and that the editor's recipe can reach. */
  readonly liveTab: boolean;
  readonly source: ConversationSource;
}

export type BackTo =
  /** The very tab — activated with the editor's own recipe, the one Claude Code uses itself. */
  | { readonly kind: 'tab' }
  /** The recorded Claude Code session, through `claude-vscode.editor.open`. */
  | { readonly kind: 'session'; readonly sessionId: string }
  /** The recorded document. */
  | { readonly kind: 'file'; readonly uri: string }
  /** Nothing this chat can be taken back to. */
  | { readonly kind: 'nowhere' };

/**
 * The one decision. The live tab wins over the durable source because it is exact — it is the tab the
 * chat was opened from even before the session walk pinned a source — and going there needs no other
 * extension's internals.
 */
export function backTo(asked: ReturnAsked): BackTo {
  return asked.liveTab ? { kind: 'tab' } : recordedRoute(asked.source);
}

/** The road the conversation RECORDED — also what a tab that would not come forward falls back to. */
export function recordedRoute(source: ConversationSource): Exclude<BackTo, { kind: 'tab' }> {
  switch (source.kind) {
    case 'claude':
      return { kind: 'session', sessionId: source.sessionId };
    case 'file':
      return { kind: 'file', uri: source.uri };
    case 'none':
      return { kind: 'nowhere' };
    default:
      // Exhaustive: a new kind of source is a compile error here, not a chat that silently goes
      // nowhere. (codex, the code round.)
      return unknownSource(source);
  }
}

function unknownSource(source: never): Exclude<BackTo, { kind: 'tab' }> {
  return source;
}

/**
 * The tab to go back to when the chat's own key is no longer one of the tabs — which happens while the
 * tab is still on screen: adding or removing an editor group rebuilds the host's tab model, and the
 * `Tab` a chat was registered under stops being one of them (our own code review of issue #314).
 *
 * <p>The same identities `sessionKey.ts` re-keys by: a FILE by its document's uri (any tab showing it —
 * the document is the same wherever it is split), and — for a chat that recorded no session — a Claude
 * tab by its label, only when ONE Claude tab wears it. Two of one name are never guessed between, and a document tab is never taken for
 * a Claude session because it happens to wear the chat's name.</p>
 */
export function recoveredTab(all: readonly TabSnapshot[], source: ConversationSource, label: string): TabSnapshot | undefined {
  if (source.kind === 'file') {
    return all.find((tab) => isOrdinaryEditorTab(tab) && tab.uri === source.uri);
  }

  // A RECORDED session has an exact road of its own (`session`), and Claude Code reveals the panel
  // already showing it; a name is only the fallback for a chat that recorded nothing.
  return source.kind === 'none' ? onlyClaudeTabNamed(all, label) : undefined;
}

function onlyClaudeTabNamed(all: readonly TabSnapshot[], label: string): TabSnapshot | undefined {
  const named = all.filter((tab) => rekeysByLabel(tab) && tab.label === label);

  return named.length === 1 ? named[0] : undefined;
}

/**
 * Why an unsaved buffer cannot be gone back to, or empty. `showTextDocument` on an `untitled:` uri that is
 * not open does not fail — it CREATES an empty buffer of that name, which after a reload may even be
 * somebody else's. So such a source is opened only while it is still one of the open documents. (Our own
 * code review.)
 */
export function bufferGone(uri: string, openUris: readonly string[]): string {
  return uri.startsWith('untitled:') && !openUris.includes(uri)
    ? 'the unsaved buffer is no longer open'
    : '';
}

/**
 * The editor's fixed commands for focusing a group, by `viewColumn`. Eight, because that is how many
 * VS Code ships — and the same list Claude Code's own `bringTabToFront` uses.
 */
const FOCUS_GROUP: readonly string[] = [
  'workbench.action.focusFirstEditorGroup',
  'workbench.action.focusSecondEditorGroup',
  'workbench.action.focusThirdEditorGroup',
  'workbench.action.focusFourthEditorGroup',
  'workbench.action.focusFifthEditorGroup',
  'workbench.action.focusSixthEditorGroup',
  'workbench.action.focusSeventhEditorGroup',
  'workbench.action.focusEighthEditorGroup',
];

/** The command that focuses this group, or empty for a group no fixed command reaches. */
export function focusGroupCommand(viewColumn: number): string {
  return Number.isInteger(viewColumn) && viewColumn >= 1 ? FOCUS_GROUP[viewColumn - 1] ?? '' : '';
}

/**
 * Whether the recipe can reach this tab: a group a fixed command focuses, and a position in that group's
 * list. `-1` is a tab that moved or closed since the snapshot; attempting it would activate whatever now
 * sits at the index, so the decision falls through instead. (gemini, the plan round.)
 */
export function tabReachable(viewColumn: number, index: number): boolean {
  return focusGroupCommand(viewColumn).length > 0 && index >= 0;
}

export const NOWHERE = 'This conversation does not know where it came from — it was opened from the picker, '
  + 'or restored before its source was recorded, and the tab it came from is not open.';

export function sessionFailed(sessionId: string, reason: string): string {
  return `Claude Code could not open session ${sessionId}: ${reason}`;
}

export function fileFailed(uri: string, reason: string): string {
  return `${uri} could not be opened: ${reason}`;
}

export function tabNotActivated(label: string): string {
  return `The tab “${label}” did not come to the front.`;
}
