/**
 * How often split work comes back through the gate (issue #131), and what an older server does instead.
 *
 * <p>Pure and `vscode`-free, so the sentence is a unit test rather than a paint.</p>
 */

import { compareVersions } from './coaiInstall';
import { COMMAND_MODELS_SINCE } from './commandModels';

/**
 * The first `coai-mcp` whose split order gates once per epic or per task rather than after every story.
 *
 * <p>The SAME release as the per-caller models (#117) — the issue run ships as one release — so it is
 * that constant rather than a second number to keep level with the tag. If the two ever ship apart,
 * this becomes a literal of its own. Set too LOW it stays silent on a server that still orders a round
 * per story, the unsafe direction.</p>
 */
export const GATE_PER_SINCE = COMMAND_MODELS_SINCE;

/**
 * The sentence beside the choice while the installed server would ignore it — or nothing.
 *
 * <p>Only while the split switch is ON and the server is KNOWN and strictly older: an older server
 * orders "After EVERY story: call review_code" whichever of the two is chosen, so the warning is about
 * the split order it gives, not about the radio.</p>
 */
export function gatePerSkewNote(installedServerVersion: string, splitPlan: boolean): string {
  if (!splitPlan || installedServerVersion.length === 0 || compareVersions(GATE_PER_SINCE, installedServerVersion) <= 0) {
    return '';
  }

  return `The coai-mcp you have installed (${installedServerVersion}) still orders a gate round after EVERY `
    + `story, whichever you choose here — a new branch and two rounds for each one. Update it to ${GATE_PER_SINCE} `
    + 'or later — the MCP server section below.';
}

/**
 * The first `coai-mcp` that reads `COAI_STOP_LOCAL_WHEN_QUIET` (issue #485) — the release after 0.33.0.
 * Set too LOW it stays silent on a server that ignores the switch, the unsafe direction.
 */
export const STOP_LOCAL_SINCE = '0.34.0';

/** The sentence beside the switch while the installed server would ignore it — or nothing. */
export function stopLocalSkewNote(installedServerVersion: string, on: boolean): string {
  if (!on || installedServerVersion.length === 0 || compareVersions(STOP_LOCAL_SINCE, installedServerVersion) <= 0) {
    return '';
  }

  return `The coai-mcp you have installed (${installedServerVersion}) does not read this switch: the local reviewer `
    + `still runs every round. Update it to ${STOP_LOCAL_SINCE} or later — the MCP server section below.`;
}
