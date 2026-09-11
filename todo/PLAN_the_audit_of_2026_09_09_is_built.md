# PLAN — the audit of 2026-09-09 is built, epic by epic

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: the build order over the ten
> plans that close [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md).
> This document holds the epics, the stories and the order; each story's *what* and *why* stays in its
> own plan and is not repeated here.
>
> Related docs: the ten plans listed below; [architecture.md](../research/architecture.md).

## Where this came from

The audit produced ten findings; each has a plan of its own in `todo/`. Those ten went to the review
gate as one plan round on 2026-09-10 — **three reviewers of three answered, twelve findings, ten
gating, verdict `good_enough`** — and every one of the twelve was accepted and folded into the plan it
belonged to, under a *What the gate's plan round changed* heading. The split below was made by Fable
at the operator's instruction, from those revised plans.

## The epics

**1 — The launcher and the diff** (`src_mcp`, `CoaiMcp.Tests`). The one process launcher both binaries
share bounds its own time, output and environment, and the diff collector hands it real paths. One
project, one suite, and the floor every server story stands on.

**2 — What an employee's job can reach on the Team server** (`src_server`, `CoaiServer.Tests`). The
three ways an authorised prompt reaches past itself — another application's token, the box's
environment and tools, the server's memory — each closed by a test that observes the refusal.

**3 — The queue uses what it has** (`src_server`). The server keeps pumping when a slot throws, starts
as many jobs as it has accounts, and answers the usage page in time proportional to the window.

**4 — The local chat does what the tab says** (`src_vs_code`, the npm suite). The picked model reaches
the CLI, and closing a tab ends the tree.

## The stories

| # | Story | Plan | RED symptom | Risk |
|---|---|---|---|---|
| 1.1 | Give the launcher one deadline and a byte ceiling | [launcher](PLAN_the_launcher_owns_its_own_deadline.md) | a sleeping child with 1 MiB on stdin returns after ~30 s, `TimedOut=false` | ordinary |
| 1.2 | Let a launch be confined | [confinement](PLAN_a_reviewer_on_the_team_server_is_confined_to_its_prompt.md) | the canary variable is printed by the child | **expensive** |
| 1.3 | Parse `numstat -z`, diff a rename as one | [rename](PLAN_a_renamed_file_still_reaches_the_reviewer.md) | a renamed file's `Text` is empty | ordinary |
| 2.1 | Refuse Google without an audience — and put the server tests in CI | [google](PLAN_google_needs_an_audience_too.md) + `ci.yml` | `Guard` does not throw | **expensive** |
| 2.2 | The server launches every reviewer confined | [confinement](PLAN_a_reviewer_on_the_team_server_is_confined_to_its_prompt.md) | the fake launcher sees an inherited environment and `Read` allowed | **expensive** |
| 2.3 | A prompt has a ceiling, refused before it is bound | [ceiling](PLAN_a_prompt_has_a_ceiling_and_a_finished_job_forgets_it.md) | a 1 MiB + 1 prompt answers 202 | **expensive** |
| 3.1 | Answer the start signal on every exit | [pump](PLAN_a_slot_that_throws_cannot_stop_the_pump.md) | the await on `started` never completes | ordinary |
| 3.2 | Walk the ranking, take the first lease granted | [slots](PLAN_the_second_account_actually_runs.md) | launched 1, queued 1, with two slots | ordinary |
| 2.4 | A finished job forgets its prompt and bounds its answer | [ceiling](PLAN_a_prompt_has_a_ceiling_and_a_finished_job_forgets_it.md) | 40 MiB of prompt after `Sweep` | ordinary |
| 3.3 | Read the usage window from the end | [usage](PLAN_the_usage_page_reads_the_window_not_the_history.md) | `BytesRead` equals the file | ordinary |
| 4.1 | Carry the chosen model into the launch | [model](PLAN_the_chosen_model_reaches_the_cli.md) | byte-identical argv for two different models | ordinary |
| 4.2 | End the whole process tree, resolve on exit | [tree](PLAN_closing_a_chat_ends_its_whole_tree.md) | the grandchild is alive after `handle.kill()` | ordinary |

**Build order:** 1.1 → 1.2 → 1.3 → 2.1 → 2.2 → 2.3 → 3.1 → 3.2 → 2.4 → 3.3 → 4.1 → 4.2.

Two orderings are not free and are the reason the table is not sorted by epic. `ProcessLauncher.cs` is
edited by 1.1 then 1.2, so the security diff reviews as an environment change rather than as one tangled
with a stream rewrite. `JobRunner.cs` is edited by 3.1, then 3.2, then 2.4: the guard wraps the body
before the body grows a loop, so a bug in the walk fails a test instead of hanging it.

## What each story owes, and what each EPIC owes

A story that is not documented, tested and committed is not finished. For every one:

1. the RED test first, watched failing for the symptom the table names;
2. the change;
3. that test green, and the whole suite of the project it touched green;
4. `research/module_*.md` updated where the story changed what a module does;
5. one commit.

**The review gate runs per EPIC, not per story** — the operator's decision on 2026-09-11, which
overrides the gate command's own *"after EVERY story: call review_code"*. One code round over the
epic's whole diff, every finding resolved, the accepted ones fixed and committed before the next epic
starts. The reason is cost rather than taste: story 1.1's code round alone was twelve reviewers and
520k input tokens against the operator's own paid subscriptions, and twelve of those is most of the
budget for this work.

What it trades is real and worth naming, because a later reader will otherwise read the change as
free: an epic's diff is three stories wide, so a reviewer holds more at once and localises a defect
less precisely than it would on one story — and a finding that would have stopped story 1.2 now
arrives after 1.3 is also written. The mitigation is the order in the table: the two file-level
orderings below exist so that a finding against an earlier story lands on code the later ones have
not yet buried.

Story 1.1 was gated on its own before this decision, and story 1.2's plan round ran on its own branch;
both are recorded in their commits.

## Risks carried from the split

1. **1.1's reader rewrite sits under every later story.** A lost unterminated last line or a blocked
   Windows pipe write surfaces as flakiness in 1.2, 1.3 and 2.2 and gets blamed on them.
2. **No server story is CI-checked until 2.1 lands**, because `ci.yml` runs only `CoaiMcp.Tests` and
   `CoaiBench.Tests` today. If 2.1's loopback-OIDC test proves unreachable from
   `WebApplicationFactory`, the `ci.yml` step is split out as its own story rather than leaving the
   whole server lane unchecked.
3. **2.1 and 2.2 fail at DEPLOYMENT, not in any suite here.** A `--disallowedTools` name the box's
   `claude` does not know is `NotStarted` on every Team-server review; a live environment with Google
   enabled and no audiences refuses to start. Only `POST_DEPLOY.md` sees either.
4. **`JobRunner.cs` is edited by three consecutive stories and `ProcessLauncher.cs` by two.** Each
   rebases on the previous one right before its code round, or the gate reads the earlier story's work
   as this story's deletions — which has happened three times in this repository.

## Definition of Done

- [ ] Twelve stories, each with its RED observation and its green one recorded.
- [ ] Twelve code rounds resolved; what was accepted and what was declined is in the final summary.
- [ ] The ten plans are promoted to `research/` as their work lands, per
      [planning-docs](../.agents/conventions/common/planning-docs.md); this document goes with the last of them.
- [ ] `todo/README.md` matches the folder at every step.
