import type { ChatAccess } from './chatAdapter';

/**
 * The agent-mode box beside the model picker (issue #289), as markup.
 *
 * <p>Its own module because the page is already the largest file in the extension, and because what
 * it draws is decided entirely by the HOST: `offered` is false for a Team-server model and for a
 * conversation with no workspace, and the box is then not drawn at all — hidden, so a push can bring it
 * back when the person switches to a local model. Disabled while a turn runs: a change of access
 * relaunches the model, and a box that could be ticked mid-answer would claim a mode the answer is
 * not being given in (codex, local and gemini, the plan round).</p>
 */
export function chatAgentToggleHtml(offered: boolean, access: ChatAccess, running: boolean): string {
  const on = access === 'agent';

  return `<label id="agentBox" class="agent${when(on, ' agentOn')}"${when(!offered, ' hidden')} `
    + `title="${AGENT_TITLE}">`
    + `<input type="checkbox" id="agent"${when(on, ' checked')}${when(running, ' disabled')}> Agent mode</label>`;
}

function when(yes: boolean, text: string): string {
  return yes ? text : '';
}

/** What the box allows, said where the person decides — in its tooltip, and again in the confirmation. */
export const AGENT_TITLE = 'Full access to this computer: the model may read and write any file and run any command, '
  + 'without asking, starting in this conversation’s workspace folder. Turning it on asks you first.';

/** The box's styling: a warning outline while it is on, so a tab in agent mode never looks like one that is not. */
export const AGENT_TOGGLE_CSS = `
  .agent { display: inline-flex; align-items: center; gap: 4px; font-size: .85em; white-space: nowrap; padding: 1px 6px; border-radius: 3px; border: 1px solid transparent; }
  .agent.agentOn { border-color: var(--vscode-inputValidation-warningBorder, #b89500); }
  .agent[hidden] { display: none; }
`;
