import type { ChatAccess } from './chatAdapter';

/**
 * The rules of agent mode (issue #289), with nothing of VS Code in them so every one can be tested.
 *
 * <p>The host decides all of this; the page only draws what it is told. A remote model runs on a Team
 * server, so "this computer" is not the one it would act on, and a conversation with no workspace has
 * no folder to act IN — the home directory is never a silent stand-in (gemini, the plan round).</p>
 */

/**
 * The kinds of conversation source — the `kind` of a `ConversationSource` (`chatStore.ts`).
 *
 * <p>Spelled out here and not imported: `chatStore` reaches the chat page, and the page reaches this module through the launch,
 * so an import of `chatStore` closes an import cycle (`importCycles.test.mjs`). The two lists cannot drift in the way that
 * matters: a `ConversationSource` of a kind missing here is not assignable to {@link AgentSource}, so every caller fails to
 * compile, and the table below must name every kind listed.</p>
 */
export type SourceKind = 'claude' | 'file' | 'terminal' | 'none';

/** What agent mode reads of a conversation's source: its kind. A `ConversationSource` is one. */
export interface AgentSource {
  readonly kind: SourceKind;
}

/**
 * Which kinds of source rule agent mode out. A terminal does: the session it came from may be on another machine, and agent
 * mode acts on THIS one.
 *
 * <p>A record keyed by every kind, not a default: a fifth kind does not compile until a person has placed it here.</p>
 */
const RULED_OUT: Readonly<Record<SourceKind, boolean>> = {
  claude: false,
  file: false,
  none: false,
  terminal: true,
};

/** Whether the conversation's SOURCE rules agent mode out. */
export function agentRuledOutBy(source: AgentSource): boolean {
  return RULED_OUT[source.kind];
}

/** Whether this conversation may be put into agent mode at all. The source is REQUIRED, so the compiler lists every site that decides. */
export function agentOffered(remote: boolean, workspace: string, source: AgentSource): boolean {
  return !remote && workspace.length > 0 && !agentRuledOutBy(source);
}

/**
 * The access a launch actually gets: what was asked for, unless the row it lands on cannot have it.
 *
 * <p>Used by every relaunch — a model switch, a reload's reopen — so a conversation in agent mode that
 * moves to a Team-server model comes out of agent mode rather than being refused the move (codex, the
 * plan round).</p>
 */
export function accessOn(remote: boolean, asked: ChatAccess, workspace: string, source: AgentSource): ChatAccess {
  return asked === 'agent' && agentOffered(remote, workspace, source) ? 'agent' : 'text';
}

/** Why the box cannot be ticked here, or empty when it can. */
export function agentRefusal(remote: boolean, workspace: string): string {
  if (remote) {
    return 'Agent mode is for a model on this computer. This one runs on a Team server.';
  }

  return workspace.length === 0
    ? 'Agent mode needs a workspace folder to work in, and this conversation has none. Open a folder and start the conversation from it.'
    : '';
}

/** What a person is told when a terminal conversation is asked for agent mode. */
export const TERMINAL_NO_AGENT =
  'Agent mode is not offered for a terminal conversation: the session it came from may be on another machine.';

/**
 * Why the box cannot be ticked on THIS conversation, or empty when it can.
 *
 * <p>{@link agentRefusal} keeps its two arguments: the launch (`cliChatLaunch.ts`) has no source to give, and a terminal
 * conversation never reaches a launch in agent mode, because {@link accessOn} and the box refuse first.</p>
 */
export function agentRefusalFor(remote: boolean, workspace: string, source: AgentSource): string {
  return agentRuledOutBy(source) ? TERMINAL_NO_AGENT : agentRefusal(remote, workspace);
}

/** Why a folder that was a conversation's workspace cannot be one now, or empty when it can. */
export function agentFolderGone(workspace: string, isFolder: boolean): string {
  return isFolder ? '' : `Agent mode works in ${workspace}, and that is no longer a folder on this computer.`;
}

/** The button that confirms turning agent mode on. Anything else — Cancel, Escape — leaves it off. */
export const TURN_AGENT_ON = 'Turn agent mode on';

/** The question asked before a model is given the computer. */
export function agentQuestion(model: string, workspace: string): string {
  return `${model} will be able to read, write and run anything on this computer, without asking. `
    + `It starts in ${workspace}. Turn agent mode on?`;
}

/** What a change of access says once it has happened. */
export function accessNotice(access: ChatAccess, model: string): string {
  return access === 'agent'
    ? `Agent mode is on: ${model} may now read, write and run anything on this computer, without asking.`
    : `Agent mode is off: ${model} answers from the text alone.`;
}

/** Said with a model switch that had to take agent mode away. Empty when it did not. */
export function accessDropped(before: ChatAccess, after: ChatAccess): string {
  return before === 'agent' && after === 'text'
    ? ' Agent mode is off: that model cannot act on this computer.'
    : '';
}
