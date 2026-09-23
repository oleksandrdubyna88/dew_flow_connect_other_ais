import type { ChatAccess } from './chatAdapter';

/**
 * The rules of agent mode (issue #289), with nothing of VS Code in them so every one can be tested.
 *
 * <p>The host decides all of this; the page only draws what it is told. A remote model runs on a Team
 * server, so "this computer" is not the one it would act on, and a conversation with no workspace has
 * no folder to act IN — the home directory is never a silent stand-in (gemini, the plan round).</p>
 */

/** Whether this conversation may be put into agent mode at all. */
export function agentOffered(remote: boolean, workspace: string): boolean {
  return !remote && workspace.length > 0;
}

/**
 * The access a launch actually gets: what was asked for, unless the row it lands on cannot have it.
 *
 * <p>Used by every relaunch — a model switch, a reload's reopen — so a conversation in agent mode that
 * moves to a Team-server model comes out of agent mode rather than being refused the move (codex, the
 * plan round).</p>
 */
export function accessOn(remote: boolean, asked: ChatAccess, workspace: string): ChatAccess {
  return asked === 'agent' && agentOffered(remote, workspace) ? 'agent' : 'text';
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
