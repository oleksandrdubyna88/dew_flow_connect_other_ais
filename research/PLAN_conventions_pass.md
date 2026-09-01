# PLAN — the gate reads the project's own rules, and one pass checks nothing else

> Status: **IMPLEMENTED, 2026-09-01.** Scope: `src_mcp/core/Rounds` (per-stage config, prompt
> catalog), `src_mcp/runners/Context` (rule discovery), `src_mcp/src/Server/PanelService.cs`,
> `src_mcp/src/prompts/`, and the panel's Prompts and Gate sections.
>
> Related docs: [module_server.md](module_server.md), [module_runners.md](module_runners.md),
> [module_extension.md](module_extension.md),
> [review-gate.md](../.claude/rules/common/review-gate.md),
> [RESULTS_conventions_prompt.md](RESULTS_conventions_prompt.md).
>
> **Deviations from the plan, and its open tail:**
>
> - **No floor on the plan stage.** The conventions pass shipped for the code stage as planned — but
>   the SCOPE floor made the same day was briefly applied to both stages and then withdrawn from the
>   plan stage: a three-line plan is a bad plan, and saying so is the reviewers' job. Refusing it at
>   the gate does their work for them.
> - **The prompt measurement did not decide anything.** Three variants, two vendors, plus a variance
>   control: indistinguishable. Variant C shipped for a reason the experiment could not test, and the
>   record says so rather than claiming a win.
> - **Still open:** all eight cells missed the one seeded violation whose rule is written as a table
>   row rather than a prose sentence — extracted into
>   [PLAN_rule_formatting.md](../todo/PLAN_rule_formatting.md).
> - **`RuleFiles` reads what is on disk in the worktree**, so a `.claude/rules/shared` submodule that
>   git has not populated contributes nothing. Right for a review of a commit, a gap on a fresh clone.
> - The panel shows the conventions default for round 1 as TEXT rather than disabling the picker,
>   because an explicit choice still wins.

## The symptom

Every repository this gate reviews carries its own written conventions — `CLAUDE.md`, `AGENTS.md`,
`GEMINI.md`, `.claude/rules/**`, `.cursor/rules/**` — and the reviewers have never been shown a line
of them. So the gate can tell you a change is well written by the reviewer's own standards while it
breaks four rules the project wrote down and enforces on humans. Measured on this repository's own
rounds: not one finding in three rounds referenced a project rule, because the rules were not in the
prompt.

Two consequences, both real:

1. **A reviewer cannot flag what it was never told.** Primary constructors, `record` for DTOs, no
   null in business logic, cyclomatic complexity ≤ 4, tests as executables, the reuse-first
   discipline — a reviewer that has not read those cannot notice a violation, and its silence reads
   as approval.
2. **The rules the AI author was given are the rules its reviewer should judge against.** Otherwise
   the two halves are held to different standards and the disagreement is guaranteed.

A second symptom, from the same rounds: **one threshold for both stages is wrong.** A plan is a
document — two open findings is a lot. A diff is hundreds of lines across a dozen files — three is
normal and a threshold of two turns the code stage into a permanent `call_human`.

## The goal

1. The server DISCOVERS the target repository's conventions and puts them in the reviewers' context.
2. **Round 1 of the code stage checks compliance with those conventions and nothing else**, in every
   reviewer role, under its own prompt. Rounds 2 and 3 are the existing lenses.
3. Rounds and threshold are configured PER STAGE: plan 3 rounds / 2 findings; code 3 rounds /
   3 findings, as defaults.
4. The panel shows the plan role in its own frame and the three code roles in one frame, each
   colour-coded, using the CredsForDevs palette.

## Build order

### 1. Per-stage rounds and threshold

`PanelConfig` (`core/Rounds/SessionState.cs:28`) is `(MaxRounds, Threshold, OnExhausted)` and is
consulted by `RoundMachine` for whichever stage is current. Split it:

```csharp
public sealed record StageGate(int MaxRounds, int Threshold);
public sealed record PanelConfig(StageGate Plan, StageGate Code, StagePolicy OnExhausted);
```

- Defaults: `Plan = (3, 2)`, `Code = (3, 3)`.
- `RoundMachine` picks the gate by `state.Stage` — one method, `GateFor(Stage)`, so no call site
  chooses by hand.
- Env/settings keys: `COAI_MAX_ROUNDS_PLAN`, `COAI_THRESHOLD_PLAN`, `COAI_MAX_ROUNDS_CODE`,
  `COAI_THRESHOLD_CODE`. The old `COAI_MAX_ROUNDS`/`COAI_GATE_THRESHOLD` keep working as the value
  for BOTH stages when the new ones are absent — a person who set them once should not have their
  gate silently change.

### 2. Rule discovery

New `RuleFiles` in `runners/Context/`, pure apart from one directory walk:

| Source | Why |
|---|---|
| `CLAUDE.md`, `AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md` | the four instruction files the major CLIs read |
| `.claude/rules/**/*.md`, `.cursor/rules/**/*.mdc` | where the real conventions live once a repo has more than a page |
| `.editorconfig` | the machine-checkable half, and short |

- Read from the WORKTREE the round already checked out, never from the developer's live tree: the
  reviewer must see the rules as of the commit under review.
- Follows `.claude/rules/shared` when it is a submodule checkout, because in this family that is
  where most rules actually are.
- **Budgeted.** Rules can be 100 KB; a prompt cannot. Cap at 40 KB total, take the instruction files
  first (they are the entry points and reference the rest), then rule files by path order, and say
  in the prompt what was cut: `[3 rule files omitted for length: …]`. A silent truncation would let
  a reviewer report compliance with rules it never saw.
- Skipped when the repo has none — the pass then says so rather than inventing standards.

### 3. The conventions prompt

`src_mcp/src/prompts/conventions.md`, one file, used by all three code roles in round 1. It must:

- Judge the diff against the QUOTED rules only. A finding must name the rule it breaks, in the
  rule's own words, and the file and line that breaks it.
- Forbid the reviewer's own taste explicitly: "a convention you believe in that this project has not
  written down is NOT a finding here." That is the whole reason for a separate pass, and without the
  prohibition it becomes a second general review.
- Keep the same finding schema and the same four not-a-finding prohibitions as the other prompts.
- Say that an empty list is the expected answer for a compliant diff.

### 4. Forcing it into round 1

`PromptCatalog.ForRound(role, round, chosen, rotating)` already resolves a prompt per round. Add:
the code roles' round 1 resolves to `conventions` **unless the person explicitly chose something
else for that round**, and only when rules were actually found. The explicit choice must still win —
this is a default, not a lock — and the panel must SHOW that round 1 is the conventions pass so
nobody wonders where their lens went.

### 5. Measuring the prompt (three variants, minimum)

Per the measurement discipline that produced `research/RESULTS_prompt_measurement.md`, and because
the last prompt conclusion I reached was refuted by its own control:

| Variant | The hypothesis |
|---|---|
| A — **rules verbatim, judge freely** | the rules pasted, "report violations" |
| B — **rules as a checklist** | the rules pre-split into numbered checkable statements, "walk each" |
| C — **rules verbatim + explicit prohibition** | A, plus the "your own taste is not a finding" clause |

Run each against the same real diff on both vendors, and record: violations found, how many name a
rule that exists, how many are the reviewer's own taste smuggled in (the failure mode that matters),
and tokens. **With a variance control**: the winning variant run three times on the same input, so a
difference smaller than the model's own spread is not reported as a result. Write it to
`research/RESULTS_conventions_prompt.md`.

### 6. The panel

- The Prompts section becomes two frames: **Plan review** alone, and the three code roles inside one
  frame, each in its own box with a coloured header.
- Colours from CredsForDevs (`--vscode-charts-*` with hex fallbacks, as
  `creds/src_vs_code/src/entityFormStyles.ts:114`): Architecture blue `#569cd6`, Security & reliability
  orange `#ce9178`, Performance & UX-DX green `#b5cea8`, Plan review purple `#c586c0`.
- The colour is a border and a header, never the only signal: the role's name stays written out.
- Round 1 of each code role shows the conventions pass as its default, named.
- The Gate section splits into plan and code rounds/threshold, with the current single-value
  behaviour explained where it still applies.

## Test plan

- `StageGate`: the code stage reads the code numbers, the plan stage the plan numbers; a legacy
  single value applies to both; a threshold change on one stage does not move the other. RED first.
- `RuleFiles`: finds each of the six sources; reads from the worktree, not the live tree; respects
  the budget and NAMES what it cut; returns empty for a repo with no rules.
- `PromptCatalog`: code round 1 is `conventions` when rules exist; is NOT when they do not; an
  explicit choice for round 1 still wins; the plan role is untouched.
- `conventions.md` carries the schema and the prohibitions — the same test that holds every other
  prompt (`helpPrompts.test.ts` byte-for-byte, plus `RolePromptsTests`).
- Panel: the plan frame is separate; each code role has its own colour class; the colours are
  `var(--vscode-charts-*)` with a fallback and no bare hex outside the fallback; the gate section
  posts the four new settings.
- The measurement in step 5 is not a unit test and does not gate the build; its record is.

## Definition of Done

- [ ] Plan and code stages have independent rounds and thresholds, defaulting 3/2 and 3/3.
- [ ] Legacy single-value settings still work and are documented as applying to both.
- [ ] The reviewers' context carries the target repo's rules, from the worktree, budgeted, with
      omissions named.
- [ ] Code round 1 is the conventions pass for all three roles when rules exist, overridable.
- [ ] `conventions.md` exists, is measured against two alternatives with a variance control, and the
      result is written to `research/RESULTS_conventions_prompt.md`.
- [ ] The panel shows one frame for the plan role and one for the three code roles, colour-coded
      from the CredsForDevs palette.
- [ ] Every rule above has a test that was watched fail first.
- [ ] `research/module_*.md` updated; this plan promoted per
      [planning-docs](../.claude/rules/shared/common/planning-docs.md).
