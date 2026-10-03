# PLAN — the claude reviewer is confined by an allowlist, as the consultant now is

> Status: **plan only, nothing implemented yet, 2026-10-03.** Scope: `src_mcp/runners/Reviewers/ClaudeRuntime.cs`
> (its argv), the reviewer tests that pin it, and `POST_DEPLOY.md` for the Team server's own CLI.
>
> Related docs: [module_runners.md](../research/module_runners.md),
> [RESULTS_claude_consultant_confinement.md](../research/RESULTS_claude_consultant_confinement.md),
> [PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) (which confined the
> CONSULTANT and left this one by the operator's decision).

## The symptom

`ClaudeRuntime.Build` confines a reviewer with a deny-list: `--disallowedTools` naming `Edit Write NotebookEdit`
for every reviewer, plus `Bash Read Glob Grep WebFetch WebSearch Task Agent` for a CONFINED one
(`ClaudeRuntime.cs`, `WriteTools` / `ReachTools`). Neither list names PowerShell. Measured 2026-10-01 on Windows
(claude 2.1.258, branch `feat/question-consultant`, `RESULTS_question_consultant_capabilities.md`): the
consultant's deny-list of the same shape leaked a canary outside the repository 6 of 6, through PowerShell,
and offered the user's whole environment (Artifact, CronCreate, RemoteTrigger, SendMessage …). The reviewer's
two modes have the same hole:

- an **unconfined** reviewer keeps every tool but the three writers — PowerShell and `Bash` included — in its
  pinned review worktree, and can read outside it;
- a **confined** reviewer is meant to judge the prompt alone, yet PowerShell is not on its list.

The code's own remark already says the denial "is a request to the CLI, not an observed effect".

## What was decided, and why this is its own plan

The operator chose on 2026-10-02 to confine the CONSULTANT in
[PLAN_the_consultant_works_on_every_vendor.md](../research/PLAN_the_consultant_works_on_every_vendor.md) and the reviewer
separately: changing the reviewer's tool set changes what a review round can do, and that is a decision about
rounds, not about consultations. The coai plan round of that plan's epic 3 raised it as Blocking; it was
rejected there on that decision, with this plan as the commitment that it is tracked.

## The shape

Reuse `ClaudeCapability.SupportsRestricted` (built by the consultant plan's epic 3) so an older claude that
refuses `--restricted` still launches:

| reviewer | argv (supported) | argv (no `--restricted`) |
|---|---|---|
| unconfined | `--permission-mode plan --restricted --tools Read,Glob,Grep --add-dir <worktree>` | `--tools Read,Glob,Grep`, flagged as not confined to the worktree |
| confined | `--permission-mode plan --tools ""` (no tool at all) | the same — `--tools ""` needs no `--restricted` |

`--tools ""` offering nothing was measured on 2026-10-01 (same RESULTS document). Whether an unconfined
reviewer losing `Bash` costs review quality is the question to measure first.

## Build order

1. Measure: the reviewer argv above with the consultant plan's probe harness, fresh, Windows and WSL; and a
   review quality spot-check with and without `Bash` on three recorded diffs.
2. Change `ClaudeRuntime.Build`; delete `WriteTools`/`ReachTools`; rewrite the tests that pin the old argv.
3. `POST_DEPLOY.md`: the Team server's installed claude accepts the argv.

## Test plan

- Golden argvs for confined/unconfined × supported/unsupported, labelled as what is SENT.
- The probe re-run as the evidence of effect, recorded in `research/`.

## Definition of Done

- [ ] Measured before the change, on both sides.
- [ ] No `--disallowedTools` left in the reviewer argv.
- [ ] The Team server's CLI checked after deploy.
