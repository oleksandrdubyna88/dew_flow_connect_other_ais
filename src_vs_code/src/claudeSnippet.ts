/**
 * The instruction text a person pastes into a target repository's CLAUDE.md, teaching that
 * repo's main AI when to call the `coai` tools.
 *
 * <p><b>It carries a version, and the panel reads it back out of whatever it was pasted into.</b>
 * The copy in one repository here was two revisions behind the button's text and predated the SCOPE
 * rule, so the AI obeying it would call `review_code` with a commit subject and meet a refusal
 * nothing in its instructions explained. Nobody was careless: that is what happens to text somebody
 * pastes. The source moves, the copy does not, and the copy is the one being obeyed.</p>
 *
 * <p><b>A number, not a hash — and both.</b> A hash cannot be forgotten but can only say
 * "different", while the useful sentence is "OLDER than the current one": a stale paste and a
 * locally edited one want opposite advice, and only an ordered number can tell them apart. So the
 * number is ordered and `snippetVersion.test.ts` pins it to the text's hash, which makes bumping it
 * unforgettable without making it meaningless.</p>
 *
 * <p>Offered as a paste, never written into someone's CLAUDE.md: adoption should be explicit and
 * reviewable, and that file is often hand-curated. Pure, so its claims are tests.</p>
 *
 * <p><b>It names no repository.</b> It used to interpolate the open workspace's folder name, which
 * was wrong twice over: the snippet is pasted into whichever repo you are adopting it for — not
 * necessarily the one that was open when you copied it — and the AI reading it is already working
 * in a checkout it can name for itself. "This checkout" is both shorter and always true.</p>
 */

/**
 * The revision of the pasted text. Raise it whenever the snippet changes — the hash guard in
 * `snippetVersion.test.ts` will not let you forget, and will tell you the new hash.
 *
 * <p>Numbering starts at 2, and that is not an off-by-one: v1 names the text that was pasted around
 * before this marker existed, which is a real generation sitting in real repositories. Copies of it
 * carry no marker and report `unversioned`; if anybody ever hand-marks one, v1 is the honest number
 * for it.</p>
 */
export const SNIPPET_VERSION = 5;

/** The snippet body's hash, so the version above cannot silently stop meaning anything. */
export const SNIPPET_BODY_SHA = '4e951347aa178972';

/** What a workspace's pasted copy is, relative to what this build hands out. */
export type SnippetStatus =
  | { readonly kind: 'current'; readonly current: number }
  | { readonly kind: 'older'; readonly found: number; readonly current: number }
  | { readonly kind: 'ahead'; readonly found: number; readonly current: number }
  | { readonly kind: 'unversioned'; readonly current: number }
  | { readonly kind: 'absent'; readonly current: number };

const MARKER = /<!-- coai-snippet v(\d+) -->/;

/** The version out of a file the snippet was pasted into, or nothing when it carries no marker. */
export function snippetVersionIn(text: string): number | undefined {
  const found = MARKER.exec(text)?.[1];

  return found === undefined ? undefined : Number.parseInt(found, 10);
}

/**
 * How a workspace's instruction files compare with the snippet this build would hand out.
 *
 * <p>Five answers rather than a boolean, because they want different sentences. `absent` is not a
 * problem to report — a repository that has deliberately not adopted the gate is entitled to — while
 * `unversioned` means "pasted before this existed", which is true of every copy made until today
 * and is not the same as version zero. `ahead` is a real case too: an extension older than the
 * repository, on a machine that has not updated.</p>
 */
export function snippetStatus(pasted: string | undefined): SnippetStatus {
  const current = SNIPPET_VERSION;
  if (pasted === undefined || !pasted.includes('Multi-model review gate (ConnectOtherAIs)')) {
    return { kind: 'absent', current };
  }
  const found = snippetVersionIn(pasted);
  if (found === undefined) {
    return { kind: 'unversioned', current };
  }
  if (found === current) {
    return { kind: 'current', current };
  }

  return found < current ? { kind: 'older', found, current } : { kind: 'ahead', found, current };
}

/** One line for the panel, saying what to do about it — or nothing when there is nothing to say. */
export function snippetNote(status: SnippetStatus): string {
  switch (status.kind) {
    case 'older':
      return `The CLAUDE.md snippet in this workspace is v${status.found}; v${status.current} is current. `
        + 'Copy it again from the ⋯ menu and replace the old block — what changed is what the AI '
        + 'reading it is told to do.';
    case 'unversioned':
      return 'The CLAUDE.md snippet in this workspace predates versioning, so it is at least one '
        + 'revision behind. Copy it again from the ⋯ menu and replace the old block.';
    case 'ahead':
      return `This workspace has snippet v${status.found} and this extension hands out v${status.current}. `
        + 'Somebody updated the repository from a newer build — update this one rather than pasting over it.';
    case 'current':
      return '';
    case 'absent':
      return '';
    default: {
      const unhandled: never = status;

      return unhandled;
    }
  }
}

export function claudeSnippet(): string {
  return `<!-- coai-snippet v${SNIPPET_VERSION} -->
## Multi-model review gate (ConnectOtherAIs)

This repository is reviewed by OTHER vendors' models before and after implementation, through the
\`coai\` MCP server.

**This is IN ADDITION to your own review, never instead of it.** If your workflow ends a task by
launching your own reviewers — the way \`feature-dev\`'s quality phase launches three in parallel —
run them exactly as you would have. Start them and this gate AT THE SAME TIME: a code round is
minutes of somebody else's CLI, and there is nothing to wait for. They are not substitutes for each
other and that is the entire point: your reviewers read the whole change with this repository in
context, and this gate asks a different vendor's model the questions your own model is worst placed
to answer. Dropping either half saves time by discarding the half you did not measure. The tools are \`mcp__coai__providers\`, \`mcp__coai__open\`,
\`mcp__coai__review_plan\`, \`mcp__coai__review_code\`, \`mcp__coai__resolve\`,
\`mcp__coai__status\` and \`mcp__coai__ask_human\`.

**A round's reply can carry COMMANDS, and they outrank your own defaults.** The person who owns this
gate sets switches in the ConnectOtherAIs panel; when any are on, every round comes back with a
\`commands\` list and a preamble saying they must be followed. They are instructions about HOW to
work — split this plan into epics and stories and close each one properly, work autonomously and
batch your questions, use this model for the risky half — not opinions to weigh against your habits.
Follow them, and say in your summary which ones you applied. An empty list means the operator has set
nothing, which is the default.

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

   **Reject in round 1, not only when the rounds run out.** A finding that is wrong, outside this
   task's scope, or already covered gets its reasoned rejection the FIRST time it appears. Accepting
   everything to be agreeable is what stops the loop converging: each accepted finding rewrites the
   plan, and the next round is handed fresh text with new things to find in it, so the count never
   falls. Rejecting early is not a way to move faster — it is the only way the round after this one
   is about the same document.
4. Verdict \`revise\` → fix the accepted findings, run \`review_plan\` again. Verdict \`proceed\`
   → implement.
5. **When the branch is written**, call \`review_code\` with the same \`planText\` and the
   \`baseRef\` you branched from. Three independent reviewers per vendor read the diff. Same
   \`resolve\` duty, same loop.

   **A code round is never given a bare diff.** \`planText\` is the SCOPE — what this change was
   supposed to achieve — and the server refuses a code round without one. A reviewer holding only a
   diff can judge whether the code is defensible; it cannot judge whether the code is what was
   ASKED for, and those come apart constantly: a change can be well written, well tested, and solve
   the wrong problem. Only the second question catches that.

   So the scope must say the symptom or goal, what must be true when it is done, and the
   constraints — not a commit subject. Reviewing an EXISTING commit works the same way: state what
   that commit was supposed to do as the scope, pass the commit as \`branch\` and its parent as
   \`baseRef\`. The plan you passed at step 2 is kept with the session and reused automatically,
   so in the normal flow this costs you nothing.

6. Verdict \`call_human\` → surface the open findings to the person and stop.
   **Do not proceed on your own judgement.** Verdict \`escalated\` → apply the named step and run
   a fresh round.

   **The server will not take another round until a person answers, and this is enforced.** After
   \`call_human\`, \`review_plan\` and \`review_code\` REFUSE — running the review again is not one
   of your options, and neither is resolving your way past it: recording decisions no longer
   reopens the gate. Call \`ask_human\`. Their answer decides: *keep going* and *stop and act on the
   findings* each grant a fresh set of rounds, *stop and talk to me* advances nothing, and if they
   would rather ship with the findings open they say so and you pass
   \`humanDecision: "proceed"\` to \`resolve\`.

   This is enforced because it was not, and the cost is measured: on a three-round budget a stage
   reached round TEN, every round after the third a full panel of reviewers. The AI running it
   judged rounds 1–3 to have found real defects, 4–9 to have chased "progressively narrower crash
   windows", and round 10 to have INTRODUCED a bug. A gate that asks for a person and then lets you
   carry on is not a gate.

   "Stop" here means stop SHIPPING over open findings — it does not end the task. Your own review,
   your summary, and anything else your workflow does still run: this gate decides whether the
   change may proceed, not what else you owe the person.

Report the verdicts and the reviewer counts in your summary. A round that ran with four of six
reviewers says so — pass that on rather than implying a full panel agreed.
`;
}
