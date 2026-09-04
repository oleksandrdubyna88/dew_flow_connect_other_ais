# PLAN — the gate can give ORDERS, and three switches decide which

> Status: **IMPLEMENTED, 2026-09-03.** Extended 2026-09-04 with the loop's floor and measured on the
> real corpus — see *What came back the next day*, at the end.
>
> **What the two rounds changed.** The plan round tightened the contract: the split command belongs to
> the PLAN stage only (a code round has a diff and no plan, so a verdict computed there would be a
> number invented from source — raised independently by two reviewers); Fable is named only when an
> ENABLED provider is Fable; the autonomy command does not tell anybody to re-read epics that do not
> exist; and the extraction grammar is written down with a stated fallback.
>
> **The code round found two defects that had already shipped into the branch**, and one was visible
> to a reader: the script that added the switch paragraph to the help spliced it into the middle of
> *"Ask a human"* in three languages. The other was the important one — a plan that did NOT pass still
> carried the order to split it and start committing, because permission to build and the order to
> build had come apart. Also: Fable was matched by substring; files were counted by base name, so
> `src/a.cs` and `tests/a.cs` were one file against a threshold of fourteen; and the no-heading step
> count summed every numbered list in the document.
>
> **What was refused, and why it is worth saying:** reconciling a checkbox with a settings write that
> failed. True, and true of all twenty settings this panel has — it belongs to the mirror as a whole,
> not to the three switches that happened to arrive last.
>
> Scope: `src_mcp/core/Commands/{GateCommands,PlanShape}.cs`,
> `src_mcp/src/Server/{PanelSettings,PanelService,ServerJsonContext}.cs`,
> `src_vs_code/src/{settingsShape,panelView,help,claudeSnippet,helpContent,helpRu,helpUk,helpDe,helpEs}.ts`.
>
> Related docs: [module_server.md](module_server.md), [module_extension.md](module_extension.md).

## The goal

Today the gate answers a question: *are these findings gating, and may you proceed?* The AI that
called it decides everything else — whether to split the work, when to ask the person something,
which model to use for what. Three of those decisions are the operator's, not the assistant's, and
the panel is where the operator sits.

So the tool results gain **commands**: short imperative instructions, built from the settings at the
moment of the call, that the calling AI is told to follow with high priority. Three switches produce
them:

1. **Work autonomously.** Non-blocking questions are collected and asked at the END, in one batch;
   a blocking question is asked at once — but only after re-reading every epic, so that everything
   blocking is asked together rather than one question at a time.
2. **Split the plan.** After a plan round reaches `proceed`, the AI is told to split the plan into
   2–4 epics, each into 2–4 logically complete stories, and to call `review_code` after EVERY story —
   fix, update the docs and the tests, commit, then the next story.
3. **Fable for the split.** When Fable is available: the split itself is done by Fable at its highest
   version; implementation then runs on Opus for the ordinary stories and on Fable (max) for the ones
   where being wrong is expensive — payments, architecture, security.

## What must be true when this is done

1. **A tool result carries its commands**, in a field an AI reads as instructions, with the reason
   they are there — not buried in prose.
2. **The commands are built at the moment of the call.** A switch flipped one second before the AI
   calls a tool governs that call.
3. **The split command carries the plan's own numbers**, so "split this" is not advice in the
   abstract: the plan's size, its build steps and the subsystems it touches are stated with it.
4. **Whether to split is a measured judgement, not a slogan.** The rule is computed from the plan
   text and stated; the AI may disagree in writing rather than silently.
5. **The autonomy command distinguishes blocking from non-blocking**, and says explicitly that
   blocking questions are gathered across all epics before the person is interrupted once.
6. **The Fable command is only issued when Fable is actually configured**, and says what to switch to
   for what — never a model this machine does not have.
7. **The CLAUDE.md snippet tells the target repository's AI that these commands exist** and that they
   outrank its own habits, because an instruction nobody knows to look for is not an instruction.
8. **Every switch is off by default**, and a build with all three off produces exactly today's
   behaviour.

## Constraints

- The settings already reload per tool call (`PanelServiceHost` stamps the settings file by mtime and
  length). This plan must not weaken that, and must TEST it — the operator's requirement is a change
  made one second before a call.
- Commands are text for a model to obey, so they are written as instructions: short, imperative, and
  saying WHY in one clause. No JSON schema for the AI to interpret.
- The split thresholds are a heuristic and must be labelled as one. They are computed from the plan
  the caller sent, not from the repository.
- Nothing here changes what the gate DECIDES: the verdict, the threshold arithmetic and the resolve
  loop are untouched.
- The snippet is versioned; changing it must bump its version so a stale copy is reported as stale.

## The measurement behind the split rule

Every plan in this repository, measured (23 of them):

| | lines | build steps | files named | subsystems |
|---|---|---|---|---|
| median | 120 | 4 | 6 | 2 |
| max | 554 | 9 | 28 | 5 |

Only two plans are over 300 lines. The one plan that was actually split into epics —
`PLAN_connect_other_ais.md`, which became `epic_01`…`epic_06` — is 440 lines, names 16 files and
touches 5 subsystems, and has **no build order at all**: a rule that looks only at steps misses the
one case the corpus can answer for. So the rule is two-axis:

- **epics** when it is big AND broad: `lines > 300 && (steps ≥ 6 || subsystems ≥ 4)`, or it names
  14+ files;
- **stories** when it is ordinary work: `steps ≥ 4 || lines > 100`;
- **as it is** below that.

Applied to the corpus this splits 1 into epics, 18 into stories and leaves 4 alone — and it catches
both the master plan and the largest open one. Line count ALONE would not: at a 300-line threshold it
catches two plans and misses the plan whose own history says it needed six epics.

## Build order

1. **RED first**: a test that a tool result carries no commands with the switches off, and the three
   commands with them on.
2. **`PanelSettings`**: `Autonomous`, `SplitPlan`, `SplitWithFable` (env `COAI_AUTONOMOUS`,
   `COAI_SPLIT_PLAN`, `COAI_SPLIT_WITH_FABLE`), all false by default.
3. **`Commands`** (pure): given the settings, the plan text and the configured providers, produce the
   ordered list of instructions. Pure, so the wording is a test.
4. **The split heuristic** (pure): the metrics above from a plan's text, and the verdict.
5. **`review_plan` / `review_code` carry them** in the reply, under a `commands` field with a
   sentence saying they must be followed.
6. **The panel**: three checkboxes in *The gate*, each with its help text.
7. **The snippet**: one paragraph about commands, and its version bumped.
8. **The freshness test**: write the settings file, call a tool, rewrite it a second later, call
   again, and assert the second call obeyed the new value.
9. Docs: `module_server.md`, `module_extension.md`, the help article, the CHANGELOG.

## Test plan

`src_mcp`: the command list is empty with everything off; each switch adds exactly its own command;
Fable's command is absent when no Fable provider is configured; the split verdict is a table over the
corpus's own numbers (554/7/8/2 → epics, 120/0/0/0 → stories, 85/3/3/2 → as it is); the settings
reload picks up a file written one second later.

`src_vs_code`: the three checkboxes render, post their setting, and reach the settings file; the
snippet contains the commands paragraph and its version is newer than the previous one.

## Definition of Done

- [ ] Three switches, off by default, each producing exactly one command.
- [ ] Commands are in the tool result and say they must be followed.
- [ ] The split command carries the plan's own numbers and the verdict.
- [ ] Fable is never named when it is not configured.
- [ ] A switch flipped one second before a call governs that call, with a test that says so.
- [ ] The snippet tells a target repository's AI that commands exist and outrank its habits.
- [ ] `CoaiMcp.Tests.exe` and `npm test` pass; docs, help and CHANGELOG updated; the plan promoted.

## What came back the next day (2026-09-04)

The operator asked two questions of the shipped feature, and both were right.

**"Are the switches only fired on plan creation — and if the box is off, does nothing go?"** Yes, and
there are now tests that say so through the whole server rather than only over the pure function:
`SplitOrderTests` runs real plan rounds and asserts an empty `commands` with the box unticked, the
order present with it ticked, and — the case that had already been fixed once — nothing at all on a
plan the gate sent back to `revise`.

**"When an epic comes back for its own plan review, do we tell it to split again? That process would
be infinite."** It would have. The floor is now `CallerSessions`: the order is given ONCE per calling
AI, identified by `CLAUDE_CODE_SESSION_ID` — Claude Code exports it to every child it spawns, and an
MCP server on stdio is one of those children. A per-session memory could not have worked: our session
is repo+branch and its plan stage happens once, so an epic can only return as a different session on
its own branch. A caller already inside a split is told it is a piece and must build as one unit.

**And a defect the end-to-end test found on its first run:** the split verdict was computed from
`session.PlanText` — the plan of the PREVIOUS round, which is empty on the first plan round, i.e. the
ordinary case. Every command therefore carried *"Measured from the plan you sent: 0 lines, 0 build
step(s), 0 file(s) named"* and told the AI its 400-line plan was small enough to build as it stands.
One word; the reason it survived the original review is that the pure tests passed the plan in
directly and only the server had it wrong.

Measured rather than asserted: 66 calls, 11 real plans, three arms, two models —
[`RESULTS_commands_campaign.md`](RESULTS_commands_campaign.md).
