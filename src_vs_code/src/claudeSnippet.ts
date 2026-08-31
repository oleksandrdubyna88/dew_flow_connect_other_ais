/**
 * The instruction text a person pastes into a target repository's CLAUDE.md, teaching that
 * repo's main AI when to call the `coai` tools.
 *
 * <p>Offered as a paste, never written into someone's CLAUDE.md: adoption should be explicit and
 * reviewable, and that file is often hand-curated. Pure, so its claims are tests.</p>
 *
 * <p><b>It names no repository.</b> It used to interpolate the open workspace's folder name, which
 * was wrong twice over: the snippet is pasted into whichever repo you are adopting it for — not
 * necessarily the one that was open when you copied it — and the AI reading it is already working
 * in a checkout it can name for itself. "This checkout" is both shorter and always true.</p>
 */

export function claudeSnippet(): string {
  return `## Multi-model review gate (ConnectOtherAIs)

This repository is reviewed by OTHER vendors' models before and after implementation, through the
\`coai\` MCP server. The tools are \`mcp__coai__providers\`, \`mcp__coai__open\`,
\`mcp__coai__review_plan\`, \`mcp__coai__review_code\`, \`mcp__coai__resolve\`,
\`mcp__coai__status\` and \`mcp__coai__ask_human\`.

**The order is a contract, and the server enforces it — \`review_code\` REFUSES until a plan round
has reached \`proceed\`.**

1. **Before implementing anything non-trivial**, call \`open\` for the repository you are working in:
   \`repoPath\` is that checkout's own path (\`git rev-parse --show-toplevel\`), \`branch\` is
   \`git branch --show-current\`. Never a path from this file — read them from the checkout you are in.
2. Call \`review_plan\` with your plan document verbatim as \`planText\`. You get merged findings,
   a gating count against the threshold, and a verdict.
3. Call \`resolve\` with a decision for EVERY finding — \`accept\` or \`reject\`, and a rejection
   needs a reason. A reasoned rejection is discounted in later rounds unless a reviewer raises it
   again with a genuinely new argument, so disagreeing honestly is cheap and disagreeing silently
   is impossible.
4. Verdict \`revise\` → fix the accepted findings, run \`review_plan\` again. Verdict \`proceed\`
   → implement.
5. **When the branch is written**, call \`review_code\` with the same \`planText\` and the
   \`baseRef\` you branched from. Three independent reviewers per vendor read the diff. Same
   \`resolve\` duty, same loop.
6. Verdict \`call_human\` → surface the open findings to the person and stop.
   **Do not proceed on your own judgement.** Verdict \`escalated\` → apply the named step and run
   a fresh round.

Report the verdicts and the reviewer counts in your summary. A round that ran with four of six
reviewers says so — pass that on rather than implying a full panel agreed.
`;
}
