import type { ConversationSource } from './chatStore';

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
  return asked.liveTab ? { kind: 'tab' } : fromSource(asked.source);
}

function fromSource(source: ConversationSource): BackTo {
  switch (source.kind) {
    case 'claude':
      return { kind: 'session', sessionId: source.sessionId };
    case 'file':
      return { kind: 'file', uri: source.uri };
    default:
      return { kind: 'nowhere' };
  }
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
