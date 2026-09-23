/**
 * How often split work comes back through the gate (issue #131), and what an older server does instead.
 *
 * <p>Pure and `vscode`-free, so the sentence is a unit test rather than a paint.</p>
 */

import { compareVersions } from './coaiInstall';

/**
 * The first `coai-mcp` whose split order gates once per epic or per task rather than after every story.
 *
 * <p>The same release as the per-caller models (#117), which is the next minor after mcp 0.32.0.
 * Set too LOW this stays silent on a server that still orders a round per story — the unsafe
 * direction; whoever cuts the release keeps it level with the tag.</p>
 */
export const GATE_PER_SINCE = '0.33.0';

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
