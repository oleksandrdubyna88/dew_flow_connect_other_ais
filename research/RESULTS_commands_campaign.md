# RESULTS — do the gate's orders change anything, and does the split have a floor?

> Measured 2026-09-04 · 66 calls · 11 real plans from this repository · three arms · two local models
> · strictly sequential on one GPU. Raw data — every answer kept whole — in [data/commands_campaign/](data/commands_campaign/):
> `runs-A.json`, `runs-B.json`, and `predictions.md`, which was written before the runs started.
>
> Records: [PLAN_commands_and_autonomy.md](PLAN_commands_and_autonomy.md) ·
> [module_server.md](module_server.md)

## Why this was run

Three switches shipped on 2026-09-03 that make a round's reply carry **commands** — split the plan,
use Fable for the split and the risky half, work autonomously. They were tested as a pure function:
given these settings, this text comes back. That says nothing about the only question that matters,
which is whether an AI reading them does anything differently.

Then the operator asked two more questions of the shipped feature, and both turned out to be right:

1. **Do the switches fire only on plan creation** — box ticked, the order goes after the plan passes;
   box unticked, nothing goes at all?
2. **When an epic comes back for its own plan review, are we telling it to split again?** Because
   that process has no floor: epics of epics, for ever.

This report answers all three: the orders' effect, measured; the guard, measured; and the split
metric, checked against what this repository actually did.

## Method

**The arms.** Each plan is put to a model three times, sequentially, with the same task prompt:

| arm | what precedes the task |
|---|---|
| `plain` | nothing — the plan, and "you are about to implement this; how will you proceed?" |
| `commands` | the preamble and commands the gate returns on a FIRST plan round, verbatim |
| `epic` | what the gate returns to a plan that is already a PIECE of a split |

The command text is not retyped by the harness. `CommandFixtures` — a test — calls the product's own
`GateCommands.For(...)` for all 24 plans and writes what it returns; a harness that paraphrased the
wording would be measuring the harness.

**The task prompt says nothing about epics, stories, reviews, commits, Fable, Opus, or when to ask a
question.** It asks for an approach, a list of units with optional children, and four fields. So
anything the `plain` arm produces on those axes, it produced unprompted — which is exactly what makes
it the control.

**The answers are schema-constrained JSON** and are scored structurally: the number of units, the
depth, whether the shape matches what was ordered for that plan's measured verdict, and regex checks
over the free-text fields. No answer is judged by reading, and every answer is kept whole in the run
files, which is what made the one broken metric cost a rescore rather than an hour of GPU (see
*Where this measurement was wrong about itself*).

**Two runs, two models** — `Qwen3.5-35B-A3B-Q5` (run A) and `Gemma4-26B-A4B` (run B). Not the same
prompt twice: the shim seeds each request from the prompt BYTES, so the same prompt is deliberately
the same request and a literal repeat would measure the engine's determinism rather than the
instruction's effect. A second model is a second opinion.

**Strictly sequential**, on the operator's standing rule: never two local calls at once, whatever
Ollama's parallelism is set to. The engine lease would have serialised them anyway.

## The corpus, and what actually happened to it

Every plan in this repository, measured by `PlanShapeReader`, with the one column no metric can
produce — what the plan's own history says was done with it.

| plan | where | lines | steps | files | areas | verdict | what actually happened |
|---|---|---|---|---|---|---|---|
| `PLAN_multi_repo_and_uncommitted` | todo | 554 | 7 | 23 | 7 | **Epics** | not built yet |
| `PLAN_connect_other_ais` | research | 440 | 0 | 32 | 10 | **Epics** | split into 6 epics |
| `PLAN_wsl_local_engine` | research | 233 | 4 | 16 | 8 | **Epics** | built whole |
| `PLAN_server_version_per_side` | research | 230 | 9 | 23 | 8 | **Epics** | built whole |
| `PLAN_conventions_pass` | research | 179 | 0 | 21 | 10 | **Epics** | built whole |
| `PLAN_local_models` | research | 170 | 5 | 11 | 8 | **Stories** | built whole |
| `PLAN_one_gpu_one_reviewer` | research | 160 | 7 | 7 | 6 | **Stories** | built whole |
| `PLAN_commands_and_autonomy` | research | 141 | 9 | 6 | 6 | **Stories** | built whole |
| `PLAN_per_role_gate_and_dealt_prompts` | research | 141 | 0 | 6 | 6 | **Stories** | built whole |
| `PLAN_local_trust_and_vllm` | todo | 139 | 4 | 11 | 7 | **Stories** | not built yet |
| `PLAN_engine_lease` | research | 128 | 7 | 5 | 5 | **Stories** | built whole |
| `PLAN_round_card_detail` | research | 122 | 7 | 6 | 6 | **Stories** | built whole |
| `PLAN_epic_02_core` | research | 120 | 0 | 2 | 2 | **Stories** | is a piece |
| `PLAN_epic_04_server` | research | 118 | 0 | 5 | 6 | **Stories** | is a piece |
| `PLAN_epic_05_extension` | research | 115 | 0 | 4 | 4 | **Stories** | is a piece |
| `PLAN_epic_03_runners` | research | 114 | 0 | 4 | 2 | **Stories** | is a piece |
| `PLAN_cli_update_button` | research | 112 | 4 | 9 | 7 | **Stories** | built whole |
| `PLAN_epic_01_foundation` | research | 96 | 0 | 16 | 6 | **Epics** | is a piece |
| `PLAN_escalation_loopback` | research | 89 | 0 | 3 | 2 | **AsItIs** | built whole |
| `PLAN_panel_probing_state` | todo | 85 | 3 | 9 | 5 | **AsItIs** | not built yet |
| `PLAN_epic_06_proof` | research | 77 | 0 | 2 | 3 | **AsItIs** | is a piece |
| `PLAN_snippet_version` | research | 77 | 5 | 10 | 7 | **Stories** | built whole |
| `PLAN_rule_formatting` | todo | 76 | 4 | 8 | 6 | **Stories** | not built yet |
| `PLAN_provider_liveness` | todo | 70 | 5 | 9 | 6 | **Stories** | not built yet |

**The ground truth is one case wide, and it has to be said out loud.** Exactly one plan here was
really split into epics: `PLAN_connect_other_ais`, which became `PLAN_epic_01`…`epic_06`. Everything
else was built whole. So the metric can be checked for *misses* on a sample of one and for
*over-calls* on a sample of eighteen — which is what the numbers below are worth, and no more.

**Stability check.** Every plan was re-measured from its FIRST version in git rather than its
promoted text, because a promoted plan carries the outcome of its own story and that could inflate
the numbers that decide its verdict. Result: 22 of 24 are byte-identical to their first commit, two
grew (102 → 139 lines, 62 → 85), and **no verdict changed**. The one case where contamination would
matter cannot be checked at all: the master plan was already promoted, epic table and all, in the
commit that created this repository.

## The numbers

**Both runs, 11 plans each**

| arm | n | median units | median sub-units | units 2-4 | shape as ordered | nested | calls a unit an epic | batches questions | names Fable | reviews every unit | commits every unit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `plain` | 22 | 5 | 16 | 7/22 | 6/22 | 22/22 | 0/22 | 0/22 | 0/22 | 22/22 | 22/22 |
| `commands` | 22 | 4 | 12 | 16/22 | 11/22 | 17/22 | 12/22 | 21/22 | 22/22 | 22/22 | 22/22 |
| `epic` | 22 | 5 | 12 | 9/22 | 4/22 | 17/22 | 0/22 | 17/22 | 0/22 | 14/22 | 14/22 |

**Run A — Qwen3.5-35B-A3B-Q5_vk128:latest**

| arm | n | median units | median sub-units | units 2-4 | shape as ordered | nested | calls a unit an epic | batches questions | names Fable | reviews every unit | commits every unit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `plain` | 11 | 6 | 16 | 2/11 | 2/11 | 11/11 | 0/11 | 0/11 | 0/11 | 11/11 | 11/11 |
| `commands` | 11 | 4 | 5 | 9/11 | 7/11 | 6/11 | 5/11 | 10/11 | 11/11 | 11/11 | 11/11 |
| `epic` | 11 | 5 | 5 | 4/11 | 2/11 | 6/11 | 0/11 | 11/11 | 0/11 | 3/11 | 3/11 |

**Run B — Gemma4-26B-A4B-Uncensored_vk128:latest**

| arm | n | median units | median sub-units | units 2-4 | shape as ordered | nested | calls a unit an epic | batches questions | names Fable | reviews every unit | commits every unit |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `plain` | 11 | 5 | 15 | 5/11 | 4/11 | 11/11 | 0/11 | 0/11 | 0/11 | 11/11 | 11/11 |
| `commands` | 11 | 4 | 12 | 7/11 | 4/11 | 11/11 | 7/11 | 11/11 | 11/11 | 11/11 | 11/11 |
| `epic` | 11 | 5 | 13 | 5/11 | 2/11 | 11/11 | 0/11 | 6/11 | 0/11 | 11/11 | 11/11 |

**Per plan — the shape each arm proposed (units/sub-units), run A then run B**

| plan | verdict | plain | commands | epic | commands as ordered | epic re-split |
|---|---|---|---|---|---|---|
| `PLAN_multi_repo_and_uncommitted` | Epics | 6/17 · 7/20 | 3/16 · 5/14 | 1/10 · 3/12 | no · no | no · no |
| `PLAN_connect_other_ais` | Epics | 6/22 · 7/22 | 6/30 · 6/16 | 6/0 · 7/24 | no · no | no · no |
| `PLAN_wsl_local_engine` | Epics | 4/12 · 4/15 | 5/16 · 3/6 | 4/0 · 4/17 | no · yes | no · no |
| `PLAN_conventions_pass` | Epics | 6/16 · 5/20 | 4/12 · 4/12 | 6/15 · 5/19 | yes · yes | no · no |
| `PLAN_epic_01_foundation` | Epics | 3/11 · 3/11 | 3/5 · 1/3 | 3/0 · 3/12 | no · no | no · no |
| `PLAN_local_models` | Stories | 5/15 · 5/22 | 4/0 · 4/13 | 5/0 · 5/13 | yes · no | no · no |
| `PLAN_engine_lease` | Stories | 7/18 · 5/16 | 4/0 · 5/11 | 6/7 · 5/14 | yes · no | no · no |
| `PLAN_round_card_detail` | Stories | 8/17 · 4/12 | 4/0 · 4/15 | 6/12 · 6/15 | yes · no | no · no |
| `PLAN_provider_liveness` | Stories | 6/16 · 6/15 | 4/0 · 4/12 | 5/9 · 5/13 | yes · no | no · no |
| `PLAN_escalation_loopback` | AsItIs | 5/16 · 4/13 | 2/14 · 3/9 | 3/0 · 3/12 | yes · yes | no · no |
| `PLAN_panel_probing_state` | AsItIs | 5/8 · 4/11 | 3/0 · 3/10 | 3/5 · 4/11 | yes · yes | no · no |

Calls: 66. Failed to answer: 0. Median call 10.2s, slowest 41s.

## What the numbers say

### 1. The orders are read and obeyed — and two of them are obeyed perfectly

Three axes separate completely, in both models, with no overlap at all:

| axis | `plain` | `commands` | `epic` |
|---|---|---|---|
| names **Fable** for the risky half | **0/22** | **22/22** | **0/22** |
| **batches** its questions instead of asking as it goes | **0/22** | 21/22 | 17/22 |
| calls a unit an **epic** | 0/22 | 12/22 | **0/22** |

Nothing in the task prompt mentions Fable, Opus, batching, or epics. A control that produces an
effect 0 times in 22 and a treatment that produces it 22 times in 22 is as clean a result as this
kind of measurement gives. **The commands are not decoration: the model reads them and does what they
say.**

The Fable row is the strongest of the three because it is also the tightest test of the *conditional*
wiring: Fable is named in exactly one command, that command is issued only on a first plan round with
a Fable provider configured, and the arms confirm all of it — the piece never hears about Fable, and
neither does an assistant working without the gate.

### 2. The order changes the SIZE of the split, and the ceiling is what it changes

| | `plain` | `commands` |
|---|---|---|
| median units | 5 | **4** |
| units within the ordered 2–4 | 7/22 | **16/22** |
| median sub-units | 16 | 12 |

Left alone, both models split anyway — `plain` nested in **22 of 22** answers, with a median of
sixteen sub-items. So the honest comparison is never "split against no split"; it is *an ad-hoc
five-phase breakdown with sixteen loose items* against *2–4 units each closed by review, docs, tests
and a commit*. The order supplies a ceiling and a closing ritual, not the idea of breaking work up.

**The strict shape matched only half the time (11/22), and the reason is a model difference worth
recording.** Qwen respects the depth the verdict asks for (flat for a stories plan, nested for an
epics one) in 7 of 11; Gemma nests everything, always — 11 of 11 across every arm, including the
control. So the *count* generalises across models and the *depth* does not. If the depth mattered,
the command would have to say "no sub-lists" rather than "stories"; it does not currently, and this
is the one place the wording could be tightened.

### 3. The loop has a floor, and it holds

**No answer in the `epic` arm proposed a second generation of epics: 0 of 22.** The order not to
split again is obeyed, and it is obeyed at the level of meaning rather than vocabulary — Qwen's
answer for `PLAN_engine_lease` opens *"I will implement the `EngineLease` protocol as a single,
self-contained unit"* and then lists the tasks inside it.

That "single unit" reading also explains the one row that looks like a regression:
`reviewsEachUnit` falls from 22/22 in the control to 14/22 in the `epic` arm, and every one of the
eight falls is Qwen answering **false** to *"will you review EVERY unit before starting the next
one?"* — which is the correct answer when you have been told there is one unit and it is reviewed
once. Gemma, which kept a multi-unit breakdown, answered true 11 times out of 11. The field is
ill-posed for that arm; the prose in the same answers still names the review and the commit.

**Would silence have done as well?** For these single-shot answers, yes — the control never says
"epic" either. That is worth stating plainly, because it means the explicit wording is not what stops
a model inventing epics of epics. **What stops the loop is that the gate no longer ISSUES the order**,
and the guard is where that decision lives. The sentence that goes to the piece earns its place for a
different reason: it carries the per-piece review, docs, test and commit loop, which the control
volunteers but which nothing would otherwise guarantee once the split order is withheld.

### 4. Against the predictions

| # | prediction | result |
|---|---|---|
| P1 | `plain` overshoots 4 units more often than `commands` | **confirmed** — 7/22 within range against 16/22 |
| P2 | `commands` matches the ordered shape in a clear majority | **half** — the 2–4 count yes (16/22), the depth no (11/22) |
| P3 | review/commit true in every arm, therefore worthless as evidence | **confirmed for the control** (22/22); the `epic` arm turned out to be ill-posed rather than saturated |
| P4 | questions batched under `commands`/`epic`, not under `plain` | **confirmed** — 0/22 against 21/22 and 17/22 |
| P5 | Fable named only under `commands` | **confirmed exactly** — 22/22, 0/22, 0/22 |
| P6 | the `epic` arm does not re-split | **confirmed** — 0/22 |
| P7 | the `epic` arm is no worse than `plain` at re-splitting | **confirmed, and stronger than expected**: both are 0 |
| P8 | the plan that really became six epics is verdicted `Epics` | **confirmed** |
| P9 | the epics it produced are verdicted below `Epics` | **five of six**; `epic_01` is called `Epics` |
| P10 | P9 will have an exception, and it will be `epic_01` | **confirmed** — 16 files across 6 areas on 96 lines |
| P11 | the verdict correlates with the work the plan actually took | **not answerable** — see below |

### 5. The metric: what it gets right, and what it over-calls

- It **catches the one case the corpus can answer for.** `PLAN_connect_other_ais` — 440 lines, no
  build order at all, 32 files, 10 areas — is verdicted `Epics`, and it is the one plan that really
  became six epics. A rule that counted build steps alone would have missed it (it has none), and a
  300-line threshold alone catches it only by accident.
- It **over-calls by roughly 4×.** Of eighteen implemented plans, one needed epics and the rule names
  four (`wsl_local_engine`, `server_version_per_side`, `conventions_pass`, and `epic_01`, which is
  itself a piece). Each of those three shipped whole, in a day.
- **The over-call on a piece is now harmless**, and that is the guard's second dividend: `epic_01`
  would have been told to split into epics again, and now it is told it is a piece before its shape
  is ever measured.
- **P11 cannot be answered from this repository.** The obvious effort proxy — commits between a
  plan's creation and its promotion — is 0 for sixteen of the twenty-four plans, because the plan was
  written and promoted in one commit, and the six epics' ranges all overlap inside a single day. The
  metric is therefore validated against *what happened*, not against *how much work it was*.

**So: can "does this need epics" be decided by a number?** On this evidence — partly. The two-axis
rule never misses the case that mattered, at the price of proposing a split about four times too
often. That trade is acceptable *only* because of how the order is worded: it states the numbers it
was computed from and says in as many words that it is a heuristic the assistant should contradict in
writing if it is wrong. A rule this loose must never be silent about being a rule.

### 6. Is a commanded split any good?

The one plan whose real split is known reproduced almost exactly — the commanded arm proposed
*Foundation & Conventions · Core Contract & State Machine · Reviewer Runners & Concurrency · MCP
Server & Release · VS Code Extension UI · Proof & Promotion*, against the six epics that actually
shipped: `epic_01_foundation`, `epic_02_core`, `epic_03_runners`, `epic_04_server`,
`epic_05_extension`, `epic_06_proof`.

**That result must be discounted, and here is why.** The plan in `research/` is the PROMOTED text, and
it contains a table of its own six epics. Both arms were reading the answer off the page. The version
before the split does not exist in this repository's history — the master plan was already promoted in
the commit that created the repo — so the most attractive number in this report is the one that proves
least.

What can be said without that contamination, from the plans whose splits were never written down:
the commanded splits are ordinary, sequential and buildable (`PLAN_engine_lease` → *lease protocol ·
shim integration and heartbeat · queue estimation and failure sentences · measurement, docs and
commit*), and they are consistently **smaller** than the control's. Nothing in 22 commanded answers
was incoherent or off-topic. There is no evidence here that ordering a split makes the work worse; the
positive claim — that it makes it better — this corpus cannot support.

## Where this measurement was wrong about itself

**The `batchesQuestions` metric was broken on the first pass**, and it mattered: it read 0/11 for the
`epic` arm, which would have said the autonomy order stopped working for a piece. The models write
*"deferred to the end of the summary"* and *"interrupt the operator only once"*; the regex looked for
"at the end" and "final summary". The behaviour was right and the ruler was wrong.

It cost a rescore rather than a rerun only because the harness stores every answer whole. That is the
design decision worth keeping: **a measurement that discards the raw answers can only ever be as good
as the metric you thought of first.**

Two smaller things: `reviewsEachUnit` is ill-posed for an arm that is told to build one unit, and the
plan corpus's ground truth is one positive wide. Both are stated where they apply rather than
averaged away.

## What this changes

1. **The guard ships as it is.** 0 of 22 re-splits, and the order is withheld at the source.
2. **The three switches ship as they are.** The Fable and autonomy orders separate perfectly from the
   control; the split order moves the median unit count to exactly what it asks for.
3. **One wording change is worth considering, and is NOT made here**: the stories command could say
   "flat, no sub-lists" if the depth is meant to be honoured, since one of the two models nests
   regardless. It is left alone because nothing in the product depends on the depth, and a command
   that tightens something nobody needs is a command spending attention it has not earned.
4. **The split metric stays labelled a heuristic**, and the label is doing real work: it over-calls
   four times out of five.
