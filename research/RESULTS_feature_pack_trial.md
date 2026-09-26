# RESULTS — the feature pack, measured: 86 cells, 319 blinded verdicts, and the hybrid wins

> Status: **measurement complete, 2026-09-26.** 21 frozen tasks (3 per outline language) over real merged
> work in 14 repositories; 63 packs; **86 model cells** (66 Codex, 20 Fable), every one a valid final answer;
> **319 findings judged blind** against the code in three assessment passes, then joined to the key.
> Pinned: `coai-mcp` AOT publishes of `docs/feature-pack-trial` = `43383765` for `--outline`; `codex exec -m
> gpt-6-astra`; `claude --model claude-fable-5-1`; the harness's own argv, confined as the product confines a
> reviewer (no tools, empty working directory).
>
> The plan this measures: [PLAN_feature_review.md](../todo/PLAN_feature_review.md) — §2 D15, D18–D22, §4.6–§4.9,
> §6. Related: [RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md) (why CLI token columns are not
> comparable), [RESULTS_findings_that_are_worth_something.md](RESULTS_findings_that_are_worth_something.md) (the
> judge-by-reading-the-code method this reuses).
>
> Raw data — packs, records, blinded inputs and verdicts, the keys — stays on the operator's machine and is
> **not** committed: seven of the fourteen repositories are corporate, and the packs carry their source.
> Those seven appear here only under neutral labels, corporate A–G.

## The question

The feature review (`review_feature`) sends a reviewer the plan, the epics, the implementer's lessons, the
gate's own history of the work and an **AST outline** of every changed file — no code bodies — and lets the
reviewer ask for source by name (D3, D4). Before Epic 2 builds the stage, four things had to be measured
rather than argued:

1. **Does the pack fit, and what dominates it?** (§4.6 budgets, `FeatureBudget`.)
2. **Is an outline enough?** Against the raw diff in the same budget (arm C), and against a hybrid —
   the outline plus the changed members' hunks (arm D, decision D22).
3. **Do lessons and history earn their bytes?** (arm B, decision D21.)
4. **Does a prompt that points at seams, with a severity calibration and three follow-ups instead of one,
   find more of the planted cross-epic defects?** (arm E, decisions D19/D20.) And the two together — the
   hybrid pack under that protocol, which is what Epic 2 will ship (arm F: D22 + D19/D20).

And, because a feature gate is only worth its latency and money if the reviewer finds what a slice review
missed: what each model finds, how fairly it rates it, and what it costs.

## Method

**Tasks.** 21 tasks, frozen before any later-fix search ran: three per language the outliner supports
(C#, TypeScript, TSX, JavaScript, Rust, PHP, Python — D5). Each task is a batch of real merged work (1–10
feature commits: squash, merge or rebase-merged PRs, or a coherent run of direct commits where a repository
has no PRs) treated as ONE feature. `base` = first parent of the first feature commit, `head` = the last one;
unrelated work merged inside the range stays in `base..head`, as it would for the product. Selection used
pre-head intent only (subjects, tickets, plans); later subjects were on screen in the survey logs, which is
recorded as a caveat rather than proven harmless.

**Packs.** Assembled in §4.6's order under `FeatureBudget` (plan 64 KB, outline 168 KB, collapse above 4 KB,
8 KB omissions reserve, 400 files) and the plan's not-yet-measured figures (epics 16, lessons 16, history
24 KB); rules at the `RuleFiles` 80 000-char budget (D18). Outlines by the product's own AOT `coai-mcp
--outline --json` (win-x64 and linux-x64); files read at head with one `git cat-file --batch`; `*` marks from
`git diff -U0 -M`. The prompt = a trial-authored role prompt + a third `WhatYouHave` truth + `FeatureJson` +
context + task, imitating `ComposePrompt`.

**Arms.**

| arm | what the reviewer gets | cells |
|---|---|---|
| **A** | the full pack: outline, one follow-up turn | 21 tasks + 7 seeded variants |
| **B** | A minus lessons and minus gate history | the 7 control tasks (one per language) |
| **C** | A with the raw `base..head` diff in place of the outline, cut to the same 168 KB | the 7 control tasks |
| **D** | A plus `## Changed hunks` — every changed member's diff, largest change first; outline + hunks together inside 168 KB, cut smallest-first, every cut unit named outside the budget | the 7 control tasks, Codex only |
| **E** | A plus two paragraphs (seams + severity calibration, quoted below) and **three** follow-ups | the 7 seeded variants, Codex only |
| **F** | D's pack (outline + changed hunks, same construction and 168 KB budget, from `base..variant`) with E's two paragraphs and three follow-ups — the shipped combination | the 7 seeded variants, Codex only |

Arm E's added text, verbatim:

> **Where to look first.** The defects a slice-by-slice review misses sit on the seams between epics: a caller in one epic's files that still relies on a signature, a parameter order, a default, a status value, a unit or a meaning that another epic changed. The outline marks every changed member with `*`. For each of them ask who calls or consumes it — above all from files another epic touched — and whether that caller still matches what the member now does. A signature tells you a member changed, not how its body changed: when you cannot judge a changed member from its signature, request its source in `sourceRequests` (by `symbol`), together with the caller on the other side of the seam, instead of guessing or skipping it.
>
> **Severity calibration.** `blocking` = releasing this ships a broken contract between components, loses or corrupts data, or opens a security hole. `major` = a real defect with a likely trigger in normal use. `minor` = a real defect whose trigger is unlikely, or whose effect is cosmetic. `nit` = style only.

and the one-turn sentence of `What you have` became *"You get up to THREE further turns: each one serves
the code you asked for in the turn before it, and you may ask again in each. Only your last answer counts."*

Arm D's material sentence names the hunks: *"an OUTLINE of every file the feature changed, and the changed
HUNKS of every changed member (unified diff, 3 lines of context), cut to a byte budget — and NOTHING else."*
Construction: every `git diff -U3 -M` line is assigned to the innermost changed member whose head span
contains it; lines outside every changed member (imports, headers) are dropped; one unit per member, ordered
by change size.

Arm F is D's construction applied to each seeded variant, then E's text applied to that pack; F minus E's
additions is D byte-for-byte.

**Seeds.** One task per language carries a variant commit that replaces head in a scratch clone with two
planted defects — **14 seeds, 8 of them cross-epic** (a caller in one epic relying on what another epic
changed). The outline, the served source and the assessor all read the variant. Only one seed (py3-S1, swapped
parameters) is visible in a signature; the other 13 live in bodies and can be reached only by asking for source.

**Protocol per cell.** Turn 1 = the pack byte-for-byte on stdin. A non-schema answer gets one repair launch
(none was needed). An answer with `sourceRequests` is served from git at head (≤ 8 requests, 48 KB, ≤ 400 lines
per file, credential-looking files refused) and the next turn is the previous prompt byte-for-byte plus a tail
(the stateless resend of §4.9). Only the final turn counts. A pre-send scan redacted every e-mail address and
password-in-URL in packs (25 cases in 9 packs, plus 3 test-DSN cases in the php1 F pack's hunks) and in served
files (15 cases; none in arm F); no key-shaped token was found in any of the 42 original packs or the 7 F packs.

**Assessment.** Findings were exported anonymised (task, text, file/line, head path and SHA, later-fix
candidates — no model, no arm, no cell) and judged by read-only model assessors, several in parallel, each
reading the code at head (at the variant SHA for seeded rows), never the key. Per finding: verdict
(supported / refuted / unresolved), value (high / medium / low / none), severity fair (yes / overstated /
understated), grounded (yes / near / no), a cluster key naming the underlying issue, the seed it hits, and an
exact later fix among the candidates. The coordinator spot-checked 3 verdicts in pass 1 and 11 in pass 3 against
the code; all held. **Only after the second pass finished were the keys opened and joined** — every number
below that names an arm or a model comes from that join. Arm F, run afterwards, was exported alone and judged in
a fourth pass by the same method, blind to its key, then joined the same way.

## Deviations — every one

1. **One follow-up turn instead of three in arms A–D.** §4.9 and D20 say three; the trial ran one so that the
   arms differ only in the pack. Arms E and F ran three. The A–D numbers are therefore a floor for what the shipped
   protocol would find.
2. **Blinded assessment by a model, not transplanted regression tests.** The plan's ideal ground truth (a
   test that fails on the defect) was not attempted; a model read the code and judged. Its known weaknesses are
   recorded below: "supported" often means "supported with corrections", and the two passes do not rate
   severity identically.
3. **Synthetic inputs where the repositories had none.** Plans are REAL for 11 of 21 tasks (a touched
   `PLAN_*.md`), SYNTHETIC for 10 (from PR titles and bodies, all dated ≤ head). Epics are SYNTHETIC everywhere
   (one per PR/commit). Lessons are SYNTHETIC everywhere (pre-head commit messages classified mechanically — no
   review threads were read). History is REAL for 5 tasks and otherwise ABSENT, never synthesised. The role
   prompt and the third `WhatYouHave` truth are trial-authored; Epic 2 has not written them.
4. **Fable ran on a subset only**, by the operator's cost decision of 2026-09-26: 20 of the 46 Fable cells the
   original 91-cell plan held (26 never run, two of them interrupted at start). Arms D, E and F are Codex only.
   Every Fable-versus-Codex figure is therefore over a smaller, different task mix unless it says "paired".
5. **Two assessment passes, joined by cluster key.** Pass 1 judged 61 findings from 14 cells (000–014 except 012 —
   everything finished when it was exported). Pass 3 judged 237 findings from 63 cells (015–041 and all of
   part 2); each assessor was handed pass 1's cluster keys for its tasks so that one issue keeps one key
   (32 of pass 3's 132 clusters reuse a pass-1 key). A pass-2 export of the part-2 cells alone was prepared and
   folded into pass 3 before any verdict was written. **Pass 4** judged arm F's 21 findings from 7 cells, with
   the prior cluster keys (18 of its 21 clusters reuse one); every F-versus-E or F-versus-A figure below therefore
   crosses an assessor pass (see *Two reading caveats*). The coordinator corrected one count in the assessor's
   report (its summary said 8 seeds, its own per-seed table 7 — 7 is used).
6. **One cell fell between the two exports**: `012-js2-C-fable-r1` (6 findings) finished after pass 1 was
   exported and was below pass 3's range, so it was never judged. It is counted as a cell in the raw metrics and
   excluded from every per-cell assessment rate (fable/C is over its 3 judged cells). An instrument defect, not a
   result.
7. **Token counts are the CLIs' own**, not a tokenizer's. Codex's `input` includes its cached tokens; Claude's
   `input` excludes cache reads and writes, which are reported separately. The columns are not comparable across
   the two CLIs (see [RESULTS_reviewer_input_sizes.md](RESULTS_reviewer_input_sizes.md)).
8. **Missing cost is unknown, never zero.** Codex reports no cost; all 138 Codex turns are `unknown`.
9. **CLI wall is not model latency.** Every seconds figure is one CLI invocation and includes CLI start,
   transport and provider queue.
10. **Smaller deviations from the order:** §4.8's history window was implemented with rule (b) widened to
    T0 − 90 d (strict counts reported beside it); rules were read at head from git, not from a working tree;
    `FeatureJson` was rebuilt from `FindingSchema.cs`'s text (content-identical); arm C and arm D use the
    outline's 168 KB budget, not the A pack's actual outline bytes (which would leave arm D no room for a hunk).

## The frozen manifest

| lang | task | repository | what a "PR" is | feature / range commits | files changed | role |
|---|---|---|---|---|---|---|
| C# | cs1 | coai | squash (#N) | 4 / 33 | 203 | control |
| C# | cs2 | dew_flow_mcp | direct commits under one plan | 3 / 6 | 27 | seed |
| C# | cs3 | dew_flow_rag_qln | direct commits | 3 / 7 | 58 | — |
| TS | ts1 | coai | squash | 3 / 3 | 44 | — |
| TS | ts2 | corporate A | merge commits | 3 / 3 | 86 | seed |
| TS | ts3 | dew_flow_creds_for_devs | rebase-merged PR | 5 / 5 | 60 | control |
| TSX | tsx1 | corporate B | squash + tickets | 4 / 5 | 96 | control |
| TSX | tsx2 | OpenHands | squash | 3 / 22 | 146 | seed |
| TSX | tsx3 | corporate C | rebase-merged tickets | 10 / 13 | 24 | — |
| JS | js1 | corporate D | one merge commit | 1 / 1 | 34 | — |
| JS | js2 | dew_flow_sidecar_rust | direct commits | 8 / 9 | 6 | control |
| JS | js3 | coai | direct commits under one plan | 2 / 2 | 5 | seed |
| Rust | rs1 | dew_flow_sidecar_rust | direct commits under one plan | 3 / 5 | 8 | — |
| Rust | rs2 | dew_flow_sidecar_rust | direct commits under one plan | 7 / 7 | 31 | control |
| Rust | rs3 | dew_flow_sidecar_rust | direct commits under one plan | 5 / 6 | 22 | seed |
| PHP | php1 | laravel/framework | squash | 3 / 9 | 13 | seed |
| PHP | php2 | corporate B | squash + tickets | 3 / 7 | 64 | — |
| PHP | php3 | corporate E | merge commits | 2 / 3 | 54 | control |
| Python | py1 | corporate F | merge commits | 4 / 5 | 33 | — |
| Python | py2 | software-agent-sdk | squash, numbered [k/4] | 4 / 6 | 19 | control |
| Python | py3 | corporate G | direct commits under one plan | 6 / 6 | 46 | seed |

Seed tasks were drawn with RNG seed 20260925 among tasks able to host a cross-epic defect (js1, js2 and rs1
have one code file or one PR and cannot). Model cells ran in an order shuffled with seed 20260926 (part 2:
20260927), at most two in flight.

## Local numbers (no model involved)

**Pack size, arm A, 21 tasks:** min **52.5 KB**, median **135.2 KB**, max **327.6 KB**; one pack (cs1) above
the ≈ 256 KB document-stage precedent. The bytes/4 estimate gives a median of ≈ 34.6 k tokens; Codex's own
count ran at a median **3.18 bytes per token** on turn 1 (2.25–3.66), so bytes/4 undercounts by about a fifth.

**The rules dominate, not the outline:** rules are a median **43 %** of a pack, the outline **28 %**. The 80 000-
char `RuleFiles` budget is hit in 7 of 21 tasks — every repository that mounts the shared conventions. The
smallest feature (js2, 1.4 KB of outline) carries 79 K chars of rules. The operator kept the full budget (D18).

**Budget cuts:**

| budget | cut in | detail |
|---|---|---|
| `OutlineBytes` 168 KB | **1 of 21** | php3: 196.0 → 138.1 KB by collapsing one file's unchanged members; no file dropped |
| `PlanBytes` 64 KB | 2 of 21 | cs1 (65.2 KB), ts2 (79.4 KB) |
| `OmissionsReserveBytes` 8 KB | 1 of 21 | cs1: 8.1 KB, 83 files not outlined |
| `RuleFiles` 80 000 chars | 7 of 21 | all conventions-mounting repositories |

An outline is a median **11.4 %** of the source it describes (1.7–20.1 %); S0.2 measured 4.6–5.3 % on this
repository's C#. Gate history is REAL for 5 tasks; under §4.8 as written (rule b's window from the base's
committer time) **3 of those 5 would attach nothing** — a rebase-merged epic is reviewed on its branch before
main reaches the base.

**`--outline` per file** (each figure includes process start and grammar load):

| env | run | files | p50 ms | p90 | max |
|---|---|---|---|---|---|
| win-x64 | first | 343 | 55.0 | 75.1 | 202.8 |
| win-x64 | repeated | 343 | 61.7 | 84.9 | 197.8 |
| linux-x64 | first | 736 | 36.3 | 57.5 | 250.1 |
| linux-x64 | repeated | 736 | 30.6 | 42.1 | 214.7 |

A 1-line file costs 48.2–60.0 ms per process on win-x64 and 21.3–36.9 ms on linux-x64 — almost the whole
per-file figure is process start, which the product's in-process builder does not pay.

**Local wall per pack** (three git reads + outline + render): median **1.4 s** on both runs, max **7.4 s** (cs1,
119 files). The three git reads are a median 103 ms; the per-file outline processes are 26–99 % (median 93 %)
of the wall. Simulated source serving took a median 13 ms per turn; real serving in the model phase took
0.2–2.8 s per follow-up (including the pre-send scan).

**Arm pack sizes** on the control tasks: C adds 33–138 KB over A (the diff fills the 168 KB the outline
left partly empty); D adds 32–146 KB (D section 33.7–172.0 KB). D's cutting is severe on the big features —
cs1 kept 8 of 883 member units, php3 1 of 229 (one 1,458-line test data provider cut up front), tsx1 11 of 374 —
and whole on js2 (21/21) and py2 (147/177). On the seeded variants F adds 23.6–152.5 KB over A and keeps every
unit on cs2, js3, rs3 and php1, but only **87 of 537 on ts2 and 6 of 741 on tsx2** (py3: 30 of 292).

## Model phase — raw

| | Codex (`gpt-6-astra`) | Fable (`claude-fable-5-1`) |
|---|---|---|
| cells | 66 | 20 |
| valid final answer | **66 / 66** | **20 / 20** |
| repairs, timeouts, failed calls, tool breaches | 0, 0, 0, 0 | 0, 0, 0, 0 |
| working dir non-empty after a turn | 0 | 0 |
| turns (1 / 2 / 3 / 4) | 4 / 54 / 6 / 2 → 138 | 1 / 19 / 0 / 0 → 39 |
| rate-limit waits | 0 | 10, in 2 cells (a spend-limit 429 on turn 2, waited out at 15…75 min) |
| findings (blocking / major / minor / nit) | 193 (2 / 169 / 22 / 0) | 132 (2 / 50 / 63 / 17) |
| turn-1 `sourceRequests` median; at the cap of 8; none | 7; 29; 4 | 8; 14; 1 |
| prompt tokens, total | 8,579,176 (incl. 819,840 cached = 9.6 %) | 5,538,096 = 2,297,011 input + 3,206,239 cache write + 34,846 cache read |
| prompt tokens per turn, median | 57.7 k | 133.1 k |
| output tokens, total; per turn median | 85,349; 596 | 1,071,154; 26,645 |
| cost | **unknown** (138 turns, the CLI reports none) | **$119.96** — mean $6.00 per cell, median $5.95, range $2.58–$9.59 |
| CLI wall, turn 1 p50 / max | **15.2 s / 36.3 s** | **331 s / 590 s** |
| CLI wall, follow-ups p50 / max | 18.8 s / 31.4 s | 333 s / 546 s |
| CLI wall, all turns p50 / p90 | 17.6 s / 23.7 s | 331 s / 518 s |
| cell wall p50 / max | 35 s / 72 s | 725 s / 14,349 s (the max includes 4 h of spend-limit waits) |

**No follow-up reused a cache.** Codex cached at most a constant 7,808 tokens per turn — its turn-2 input was a
median **1.21×** turn 1 (1.06–1.43). The Claude CLI read a constant 917 cached tokens per turn and wrote the whole
prompt to its cache every turn; Fable's turn-2 cost was a median **1.12×** turn 1 (0.79–2.00). §6 S0.3 set the rule
"resume (C3) only if turn 2 costs > 40 % of turn 1": on both CLIs measured here it costs more than turn 1 itself.
Arm F confirms it over up to four turns and the largest prompts of the trial: its 16 turns each resent the previous
prompt byte-for-byte (a 140–395 KB identical prefix, `prefix_identical` true on every turn), and Codex's cached count
stayed flat at **7,808** tokens — 0 on three of them — while input grew turn over turn (tsx2: 93 k → 104 k → 115 k →
119 k). Only 7.7 % of F's input was cached.

**The two models use turn 1 differently.** In all 56 Codex cells that had a follow-up, turn 1 carried **zero
findings** — only source requests; everything was decided in the last turn. Fable gave preliminary findings in 17
of 19, then revised them (the six part-1 Fable cells each dropped 2–5 turn-1 findings after reading the code).
Arm E's extra turns were used: 6 of 7 cells took a third turn, one of them the fourth as well. Arm F, with the hunks
in the pack, needed fewer: 4 of 7 cells stopped after one follow-up, one took a third turn, one the fourth, and one
(rs3) asked for nothing and answered in turn 1.

**Serving in the first follow-up** refused or cut 56 requests: 32 over the 48 KB per-turn budget, 16 files longer
than 400 lines served only in part, **4 refused by the credential-word rule** (`tokenPng.mjs` three times, a
`PLAN_tokenizer_registry.md`), 3 symbols not found (a qualified name such as `Config::from_env` against a file
declaring `from_env`), 1 guessed path that does not exist.

## Assessment

| | pass 1 | pass 3 | pass 4 (arm F) | combined |
|---|---|---|---|---|
| findings (cells) | 61 (14) | 237 (63) | 21 (7) | **319 (84)** |
| supported / refuted / unresolved | 59 / 2 / 0 | 222 / 9 / 6 | 20 / 1 / 0 | **301 / 12 / 6** (94.4 % supported) |
| value high / medium / low / none | 20 / 24 / 15 / 2 | 69 / 118 / 41 / 9 | 7 / 11 / 2 / 1 | **96 / 153 / 58 / 12** (30.1 % high) |
| grounded yes / near / no | 57 / 4 / 0 | 210 / 25 / 2 | 18 / 3 / 0 | **285 / 32 / 2** (89.3 % exact, 99.4 % right file) |
| severity fair / overstated / understated | 45 / 13 / 3 | 144 / 92 / 1 | 14 / 7 / 0 | **203 / 112 / 4** (35.1 % overstated) |
| distinct clusters | 55 | 132 (32 shared with pass 1) | 21 (18 reuse a prior key) | **158** |
| seeded-task rows hitting a seed | 2 of 14 | 12 of 38 | 7 of 21 | 21 of 73 |
| later-fix matches (rows / commits) | 3 / 3 | 10 / 5 | 0 / 0 | 13 / 5 |

| by model | findings | supported | high | medium | overstated | grounded exact |
|---|---|---|---|---|---|---|
| Codex | 193 | 189 (97.9 %) | 70 (36.3 %) | 102 | 85 (44.0 %) — 84 of them `major`: **84 of 169 majors**, 1 minor | 167 (86.5 %) |
| Fable | 126 | 112 (88.9 %) | 26 (20.6 %) | 51 | 27 (21.4 %) — 18 majors, 9 minors | 118 (93.7 %) |

**Two reading caveats.** First, "supported" is "supported with corrections" more often than the column shows:
the assessors' notes record a wrong method, number, trigger or side consequence on 5 pass-1 rows and 7 pass-3 rows
whose core mechanism is nonetheless real. Second, the passes do not rate severity alike: pass 3 found 39 % of its
findings overstated against pass 1's 21 %, and the gap holds within each model (Codex 32 % → 47 %, Fable 15 % →
24 %) — part assessor drift, part task mix. **Arms A, D and E sit almost entirely in pass 3** (A: 19 of 22 Codex
cells; D and E: all), so the arm comparisons below are within one assessor calibration; a Fable-versus-Codex
comparison is not unless it is paired. **Arm F sits alone in pass 4**: same method, same cluster keys, but a
separate run — its overstatement rate in particular is compared across passes. Its seed hits are the firmer
figure, since a seed hit is checked against a written seed specification. The assessors also belong to the same vendor family as one of the two
reviewed models; blinding hid the reviewer, not its style.

One cross-pass disagreement is known: pass 1 refuted the bare "Postmark refuses attachments" finding as a
documented deferral; pass 3 kept two narrower rows on the same cluster as supported, lowered to value low.

## The arms

Per group — cells, findings, and per-cell rates (Fable's `C` row is over its 3 judged cells, see deviation 6):

| model / arm | cells | findings | supported | high | **high / cell** | medium / cell | overstated | seed hits (distinct seeds) |
|---|---|---|---|---|---|---|---|---|
| codex / A | 22 | 52 | 98 % | 15 | **0.68** | 1.36 | 50 % | — |
| codex / A, seeded | 7 | 19 | 95 % | 6 | 0.86 | 1.29 | 47 % | **5** of 14 |
| codex / B | 8 | 28 | 96 % | 10 | 1.25 | 2.13 | 54 % | — |
| codex / C | 8 | 27 | 100 % | 10 | 1.25 | 1.88 | 44 % | — |
| codex / **D** | 7 | 26 | 100 % | 14 | **2.00** | 1.57 | **35 %** | — |
| codex / **E**, seeded | 7 | 20 | 100 % | 8 | 1.14 | 1.29 | **35 %** | **5** of 14 |
| codex / **F**, seeded (pass 4) | 7 | 21 | 95 % | 7 | 1.00 | 1.57 | **33 %** | **7** of 14 (5 of 8 cross-epic) |
| fable / A | 9 | 57 | 96 % | 17 | **1.89** | 2.56 | **14 %** | — |
| fable / A, seeded | 2 | 13 | 100 % | 2 | 1.00 | 2.50 | 23 % | 3 of 4 (4 rows) |
| fable / B | 5 | 34 | **76 %** | 3 | 0.60 | 2.80 | 29 % | — |
| fable / C | 3 judged (4 run) | 22 | 82 % | 4 | 1.33 | 3.00 | 27 % | — |

**Paired, Codex, the 7 control tasks — high-value findings per cell:**

| task | A | B | C | **D** | D findings (overstated) |
|---|---|---|---|---|---|
| js2 | 0 | 0 | 1 (2 cells) | **1** | 3 (2) |
| cs1 | 4 | 7 | 4 | **8** | 8 (0) |
| py2 | 2 (2 cells) | 1 | 2 | **2** | 3 (1) |
| ts3 | 0 | 0.5 (2 cells) | 0 | **0** | 4 (4) |
| tsx1 | 0 | 0 | 2 | **2** | 4 (1) |
| php3 | 1 | 1 | 0 | **1** | 2 (0) |
| rs2 | 0 | 0 | 0 | **0** | 2 (1) |
| **sum** | 7 | 9.5 | 9 | **14** | 26 (9) |

**D is at least A and at least C on every control task**, better than A on three (js2, cs1, tsx1) and better
than C on two (cs1, php3). On cs1 the D pack kept only 8 of 883 member hunks — the eight largest — and every one
of its 8 findings was judged high; that one cell also matched **4 of the 5** later-fix commits in the whole trial.
D's price is input: summed over the seven tasks, a Codex cell read 798 k tokens under A, 885 k under C and
**1,113 k under D (+39 % over A)**.

**B (no lessons, no history) points both ways.** Codex did slightly better without them (control tasks: 1.25 high
per cell against A's 1.12); Fable did much worse (0.60 against A's 2.60, five control-task cells each) and had
its worst supported rate of the trial, 7 of 34 refuted — several of them behaviour the code or plan documents as
intended. That lessons would have prevented those is a hypothesis, not a measurement.

**E (seam prompt, calibration, three follow-ups)** found the same number of seeds as A on the same seven variants
(5 and 5), but not the same seeds (below). What moved was calibration: overstatement **47 % → 35 %**, supported
95 % → 100 %. It cost +67 % input (187 k against 112 k tokens per cell) and 46 s against 35 s per cell.

**F (the hybrid pack under E's protocol — what Epic 2 ships)** found **7 of 14 seeds, 5 of 8 cross-epic**, against
5 of 14 for both A and E on the same seven variants, including py3-S2, which no cell had found before. Its high-value
rate (1.00 per cell) sits between A's 0.86 and E's 1.14; overstatement 33 % against A's 47 % and E's 35 %; one of its
21 findings was refuted (a menu portal the assessor showed is correctly scoped). It cost 189 k input tokens per cell —
the same as E's 187 k, +70 % over A — and 41 s per cell (median), less than E's 46 s because the hunks answered
questions E had to ask for: 16 turns against E's 21.

## Seeds

Every seed, by cell. "hit" = an assessor matched trigger and mechanism, not merely the file.

| seed | cross-epic | where it lives | codex A | codex E | **codex F** | in F's hunks | fable A |
|---|---|---|---|---|---|---|---|
| cs2-S1 — the invoke gate built from the unfiltered tool list | yes | dew_flow_mcp | **hit** | miss | **hit** | yes | not run |
| cs2-S2 — a description hash over the unsorted order | no | dew_flow_mcp | miss | miss | miss | yes | not run |
| ts2-S1 — a case-insensitive check with a case-preserving return | yes | corporate A | miss | miss | miss | cut | not run (interrupted) |
| ts2-S2 — an error's cause chain no longer inspected | no | corporate A | miss | miss | miss | cut | not run (interrupted) |
| tsx2-S1 — a shared helper stops encoding the id in a URL | yes | OpenHands | miss | miss | miss | cut | miss |
| tsx2-S2 — the enabled check moved after the run check | no | OpenHands | miss | miss | miss | cut | **hit** |
| js3-S1 — a timeout reported as a clean close, code 0 | yes | coai | **hit** | **hit** | **hit** | yes | not run |
| js3-S2 — a final line without a newline discarded | no | coai | miss | miss | miss | yes | not run |
| rs3-S1 — a healed poisoned lock returns None, read as "busy" | yes | dew_flow_sidecar_rust | **hit** | **hit** | **hit** | yes | not run |
| rs3-S2 — the input shape dropped from the compile-cache path | no | dew_flow_sidecar_rust | **hit** | **hit** | **hit** | yes | not run |
| php1-S1 — `read` missing from the list the unpooled connection strips | yes | laravel | miss | miss | miss | yes | **hit** |
| php1-S2 — `=== null` instead of `blank()` for an empty override | no | laravel | miss | **hit** | **hit** | not a changed line | **hit** |
| py3-S1 — positional parameters swapped in the callee only | yes | corporate G | **hit** | **hit** | **hit** | yes | not run |
| py3-S2 — a status value renamed at the producer only | yes | corporate G | miss | miss | **hit** | yes | not run |
| **found** | | | **5 of 14** (4 of 8 cross-epic) | **5 of 14** (3 of 8) | **7 of 14** (5 of 8) | 9 yes, 4 cut, 1 neither | 3 of 4 run (1 of 2) |

"In F's hunks" says whether the seeded line reached F's pack as a hunk line: *cut* = its member was cut by the
hunk budget; php1-S2 restores base's line, so it is not a changed line of `base..variant` and no hunk shows it.

Across every cell, **9 of 14 seeds were found at least once, 6 of the 8 cross-epic ones.** Five were never found by
anyone who looked: both ts2 seeds, tsx2-S1 (missed by all four cells), cs2-S2 and js3-S2 — every one a body-level
change whose file the reviewer did not ask for or read past. The only seed an outline shows (py3-S1, in
`evaluate`'s signature) was found by all three Codex arms. **F found every seed that A or E found** (their union is
6) and added py3-S2.

**Where F's recall went — mostly the budget.** The seeded line reached F's pack as a hunk for 9 of the 14 seeds, and
F hit **6 of those 9**; it hit **0 of the 4** whose member the hunk budget cut (both ts2 and both tsx2 seeds); and it
found the fourteenth, php1-S2, which no hunk could show, from the source it asked for. So 4 of F's 7 misses are
seeds it was never shown and never asked for, and the other 3 (cs2-S2, js3-S2, php1-S1) were on the page and missed
anyway. The budget is the largest single part of the remaining gap, not the whole of it. The two cut packs fail
differently:

- **tsx2 is starvation.** Largest change first, six units filled the whole hunk room (62 KB; the largest one alone
  26 KB), and 735 were cut — 664 of them with 20 changed lines or fewer. Both tsx2 seeds sit in
  small members.
- **ts2 is the plain budget.** 87 of 537 units fit; every unit cut had 13 changed lines or fewer. The smallest-first
  cut removes exactly the small body edits a planted defect — and many real ones — consist of.

## Later fixes

A later-fix candidate is a commit within 56 days after head that touches a changed code file and reads as a fix.
The window is truncated at the repository tip for 20 of 21 tasks; cs1 is the only task with a long candidate list
(18), and **all 13 matching rows are cs1**, covering 5 distinct commits:

| fix commit (coai) | what it fixed | found by |
|---|---|---|
| `32ca590bec` | the HTTPS gate reads a forwarded header `UseForwardedHeaders` already consumed — a correctly proxied host answers 403 | fable A, codex B, codex D |
| `959695f4cf` | a throw before the pump's handshake leaves `started` unsignalled | fable A, codex A, codex D |
| `23e0493f16` | the reviewer is pointed at a schema file that is never written | codex C, codex B, codex D |
| `7dd21f72bf` | the launcher maps the executor's success shape to NotStarted — no review ever completes | codex A, codex B, codex D |
| `fe85c45ca6` | an Unparseable failure loses its token counts | codex A |

Further issues in corporate repositories were fixed later by commits outside the candidate lists; they are
recorded as no match.

## Fable against Codex

- **Per group**, Fable/A found 1.89 high-value findings per cell against Codex/A's 0.68 — **2.8×** — but over a
  different task mix (Fable's nine A cells include cs1, the richest task). **Paired on the same pack and arm**
  (19 pairs), Fable averaged **1.37** against **0.92** — **1.5×**; Fable ahead in 9 pairs, Codex in 4, 6 tied.
  Fable writes 2–4× more findings per cell, so per finding Codex is the denser (36.3 % high against 20.6 %).
- **Calibration is Fable's clearest advantage:** 14 % overstated in arm A against Codex's 50 %; paired, 21 % against
  48 %. Codex files nearly everything as `major`, never uses `nit`, and used `blocking` only under the calibration paragraph (twice, arm F).
- **Accuracy is Codex's:** 98 % supported against Fable's 89 %; ten of the trial's twelve refutations are Fable's.
- **Price:** Fable ~331 s per turn against Codex's ~18 s (≈ 19×); $6.00 per cell for Fable, unknown for Codex.
- **They find different things — the product's premise.** In pass 1 only **6 of 55** clusters were found by both
  models. Across both passes, on the 12 tasks both reviewed, **28 of 129** clusters were shared (27 Codex only, 74
  Fable only); of the 35 supported high-value clusters, 14 were found by both, 14 by Codex alone and 7 by Fable
  alone (Codex ran three times as many cells).

## Failure modes seen

1. **Severity inflation (Codex).** 169 of 193 findings `major`, half of those overstated; the calibration
   paragraph (arm E) and the hunks (arm D) each brought it to 35 %, the two together (arm F) to 33 %.
2. **Everything deferred to the last turn (Codex).** Zero turn-1 findings in 62 of 62 follow-up cells; a lost or
   refused follow-up would leave nothing. With the raw diff (arm C) Codex asked for no source in 3 of 8 cells
   and answered in one turn.
3. **Body-level defects in unrequested files are missed** — five seeds by every cell that looked; under arm F,
   every seed whose member the hunk budget cut.
4. **Refutations under arm B (Fable)** — "defects" the code or plan documents as intended.
5. **Serving limits bind:** the 48 KB per-turn budget refused 31 requests; qualified symbol names did not resolve;
   the credential-word rule refused real code files (the D15 narrowing of 2026-09-26, observed live).
6. **Supported, with a wrong detail** — 12 of the 281 supported rows of passes 1 and 3 carry a wrong method, number or consequence.
7. **A spend limit** stopped two Fable cells for up to 75 min each; they were waited out, never faked.
8. **The instrument's own gap** — one cell (six findings) fell between the assessment exports.

## Decisions for Epic 2 and Epic 3

| decision | outcome | from |
|---|---|---|
| **D22 — outline plus changed hunks** | **CONFIRMED.** The hybrid replaces the pure outline in Epic 2 (S2.2 builds it): ≥ A and ≥ C on every control task, better than A on three; 2.00 against 0.68 high-value per cell; overstatement 35 % against 50 %; 4 of 5 later-fixed defects from one cell. Its cost, +39 % input, is accepted. | the arm table, the paired table |
| **D19 — the seam prompt and severity calibration** | **Stays.** It did not raise seed recall (5 against 5, on Codex) but cut overstatement 47 % → 35 %. | arm E |
| **D20 — three follow-ups** | **Stay.** Used by 6 of 7 E cells; no recall gain measured on its own. Under F, 2 of 7 cells went past one follow-up. | arm E, arm F |
| **D22 + D19/D20 together — the shipped protocol** | **The best measured arm on seeds: 7 of 14 (5 of 8 cross-epic)** against 5 of 14 for A and for E on the same variants, every A or E hit kept; overstatement 33 %; the same input as E (189 k tokens per cell). Its misses are mostly the hunk budget (0 of the 4 seeds whose member was cut; 6 of the 9 shown) — see recommendation 8. Measured on Codex only, one cell per variant, in a separate assessor pass. | arm F |
| **D21 — lessons and history in the pack** | **INCONCLUSIVE** — Codex better without them, Fable worse. D21 stays as the operator's decision, labelled **unproven**. | arm B |
| D18 — the full `RuleFiles` budget | not tested by an arm; the local numbers stand (rules 43 % of a pack) | local |
| D15 — no credential words on file names | confirmed by 4 live refusals of real code files | serving |

The plan's own rows D19, D21 and D22 quote pass-1 figures (2 of 6 seeds, 21 % overstated; 0.5 against 1.6;
2.5 against 1.6 over two cells). This document supersedes them.

**Recommendations:**

1. **Epic 3, follow-up cost:** by §6 S0.3's own rule, a stateless resend is past its threshold — turn 2 costs
   1.12× (Fable) and 1.21× input (Codex) of turn 1, with no cache reuse on either CLI. Decide resume (C3) or a
   cache-stable prefix before three follow-ups ship; measure the `api` runtime, which this trial did not reach.
2. **Epic 3, serving:** resolve qualified symbol names (`Type::member`, `Class.method`) against the outline's
   declarations; revisit the 48 KB per-turn budget, which refused more requests than anything else.
3. **Epic 2, severity:** do not read a Codex `major` as a Fable `major`. The calibration paragraph belongs in the
   shipped role prompt (D19) and the hunks help too; neither brings Codex to Fable's 14 %.
4. **Epic 2, §4.8:** widen rule (b) of the history window (T0 − 90 d, or anchor on the first epic's first round);
   as written it attaches nothing for 3 of 5 rebase-merged features.
5. **Epic 2, budgets:** keep `OutlineBytes` at 168 KB (20 of 21 fit whole); raise `OmissionsReserveBytes` to 12 KB
   or list non-code files by directory — cs1 exceeded 8 KB.
6. **Epic 2, the builder:** read files through one `git cat-file --batch` and outline in-process; per-file outline
   processes were a median 93 % of the local wall.
7. **Reviewer choice:** Fable and Codex are complements, not substitutes — Fable for calibration and breadth at
   ~$6 and ~12 min per cell, Codex for speed and a denser, less calibrated list.
8. **Epic 2, the hunk budget (S2.2):** do not let a few huge members starve the rest. Either give the hunks a larger
   budget of their own, or cap each member's hunk (for example a few KB, the rest named under *What this context
   left out* like any cut unit) so the room reaches the many small edits. On tsx2 six units took the whole room and
   664 members of ≤ 20 changed lines were cut; both of F's missed tsx2 seeds and both ts2 seeds were among the cut.
   Measure the choice on these seven variants — the F packs are the baseline.

## What this can and cannot show

- **Small n per cell.** Most arm × task cells hold one run; the three repeated cells that were judged (py2 A,
  js2 C and ts3 B, all Codex) moved by at most one high-value finding between repeats, which is the size of several deltas
  above. The D verdict rests on its being ≥ on all seven tasks and the sum (14 against 7 and 9), not on any one task.
- **Three tasks per language** give existence proofs and gross failures — no outline broke, every pack fit, every
  language produced findings — not per-language rates. A seed recall of 1 or 2 of 2 is an anecdote.
- **Fable is a subset** (20 of 46 planned cells, no D, E or F), so Fable-versus-Codex rests on the 19 pairs; the
  group-level 2.8× is a task-mix artefact and the paired 1.5× is the figure to quote.
- **The ground truth is a model's reading** plus seeds the trial planted plus a truncated later-fix window. It
  cannot count what every reviewer and the assessor missed; "found 9 of 14 seeds" says nothing about natural
  defects nobody reported.
- **F against A and E is one cell per variant** and crosses an assessor pass. 7 against 5 of 14 is two seeds; what
  makes it more than noise is the per-seed table — F kept every hit A or E made and added one no cell had found —
  not the total.
- **Synthetic lessons** make arm B a test of these lessons, not of lessons an implementer would write — another
  reason D21 stays unproven.
- **CLIs, not the `api` runtime.** The reviewers the stage is aimed at (xAI Grok, Qwen, D9) were not measured;
  their tokens, caching and calibration are unknown.
