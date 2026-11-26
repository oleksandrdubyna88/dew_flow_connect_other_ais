# Spec changes — epoch 3, E3 "Roles complete": the plan gates and the changes after them

> Status: **the record of the plan rounds of this epic and of every change of its plan after an approval, 2026-11-26.** The
> plan as it stands is [SPEC.md](SPEC.md); this file says how it got there. Parent plan: [PLAN.md](../../PLAN.md), whose
> ledger (`## Plan changes`) counts the changes. Sections J, K and L were moved here from SPEC.md on 2026-11-26, word for
> word: the `spec` file of an epic may hold 64 KiB (SPEC.md, B item 5), and with section M it held more. This file is not
> in the pack of a folder round: a free `.md` file beside the nodes is on the line Not sent.

## J. Epic plan gate (proceed, 2026-11-25) — accepted changes

The gate read `EPIC_BUNDLE.md` at `db3a0d8e`. All 5 findings were accepted.

1. **Only measured releases for the codex and agy boundary** (s5 t1, s6 t1, s7 t1). The write capability accepts an exact list of
   measured releases, not a floor. Section C2 says how a release is added: measure, judgement, one commit, a test that holds the
   table to its evidence.
2. **The preflight probes every property the verdict relies on** (s5 t3, s6 t4, s4 t6). The judgement lists the properties with
   ids; the preflight has one row per id inside the exact launch unit before each job; a wrong answer refuses the job. For agy this
   is more than the answers of the hook: the sign-in files, what is hidden, the network limit.
3. **The approval covers the reviewed context outside the scope** (s3 t4; B7, B8). The approval id holds every packed file and
   every outside-dependency line. A changed ancestor, plan file, `feature.yaml` or dependency title refuses a coder start.
4. **A refused vendor never blocks the other** (C5). The seam moved out of the codex story into s7; s5 and s6 depend on s4 and s7
   only; the judgement commit removes a refused vendor's story; the failover test and the acceptance run per combination.
5. **A blank evidence text is an error** (s1 t1 to t3; S12). It is refused before the line check and can never be verified.

Parent plan, corrected in the same change: `epic.yaml` of E3 gets `e2-coder-one-task` in `dependsOn` and `spec: SPEC.md` (the pack
of section B reads the `spec` file; E1 has the same line); E4's `doneWhen` says that the resume is built by E2 and E3;
`feature.yaml` `cornerstones` gets s3 and s4 of this epic. `v2/_gen/v2-spec.json` holds the same three edits.

## K. After the plan gate (2026-11-25) — what changed, for the next plan round

The gate of section J read the plan at `db3a0d8e`; its findings were folded in at `8d2cc1d5`. Four causes changed the plan since.
The bundle (`EPIC_BUNDLE.md`) holds the plan as it is now.

1. **The security judgement of s4 t6** (`v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`, `d8f57ec7`): codex `ship`, agy
   `ship` with a must-change list and accepted risks. Four tasks were added (s5 t6, s6 t7, s7 t5, s7 t6) and one moved (the agy
   boundary scenarios: s6 t6 → s8 t6): 43 tasks; s5, s6, s7 and s8 hold 6 each, the device limit. The sentences its section 10
   left for this round are folded in: the header, C2 items 1, 3 and 4 (the classes of the ids), C6 (what the guard denies, the
   guard folder as the working directory, hidden and not read-only), section F (the program store, the sign-in copy, the guard's
   records), section G (the forwarder is E4's; the follow-up for E2), risk 9 of section H.
2. **The owner's decision 39**: agy is allowed as a coder. It is why the judgement ships agy; `doneWhen` line 2 is not narrowed.
3. **The coder comparison of E2** (`RESULTS_coder_comparison.md`): codex refused the shipped prompt twice; agy's init line named
   another mode than the argv; codex wrote null members. Sections C7 and C8; scenarios V17 to V19; addendum 1 of the judgement
   (ids AG-M3 and AG-M4, accepted risk A9); tasks s5 t2 d2, s6 t3 d1, s6 t4 d4, s6 t5 d3, s6 t7 d8 and d9, s7 t1 d3, s8 t4 d4.
4. **The owner's decision 40** (2026-11-25, «да, (а)»): a coder prompt for each vendor; the claude text unchanged. Section C7;
   tasks s7 t1 d1, s7 t2 d5, s5 t4 d4, s6 t3 d6, s8 t2 d2, s8 t4 d4.

No task was added or removed by causes 3 and 4: every story of s5 to s8 is at the device limit of 6 tasks, so the new work is
dod items of existing tasks. No complexity number changed.

For the lead: each cause is a change of an approved plan. Whether the parent plan's change count stops the plan is the lead's
decision (the judgement, section 10 item 2). In E4's plan the forwarder line and the owner's answers of decision 38 are on
`feat/v2-ep3-e4-plan`.

## L. Second plan round (proceed, 2026-11-26) — accepted changes

The round (coai session `26308fe5`) read `EPIC_BUNDLE.md` at `6ec9a211`. Both findings were accepted. No new round follows.

1. **C7 and s8 t4 told a coder that coai runs required commands, and nothing made coai run them** (major, security). Now:
   section C9, the instruction step. A repository declares the command in `.coai/toolchain.yaml`; coai runs it before turn 0 in
   a check unit at the base commit and appends its output to the prompt of every vendor's coder, claude's too; a declared step
   that cannot run refuses the job (`instructions-failed`); the prompt bullet of codex and agy points at the block and is
   true with and without a step (C7 item 4). The claude prompt FILE and its sha test do not change; what a claude coder is
   sent on a repository that declares a step does (C9 item 6). The proof checks the effect: s8 t4 d4 needs a commit that
   follows a rule only the step's output holds, and s7 t3 d3 tests every failure with zero model launches. Scenarios V20 to
   V22.
2. **B and s3 t5 let a changed context file outside the scope pass in silence** (major, reliability). The plan text said "under
   the scope". Now B item 6 says every file of the pack, the context too; P4 names both. The code of s3 t5 already takes its
   list from the pack (`WorkingTreeGuard.Differing`); what was missing is a test of the context and a sentence that names it:
   the new task s3 t6.

Tasks. One added: s3 t6 (44 tasks). In s7, which holds the device limit of 6: the rows of `shared/role-defaults.json` (the old
t3) moved into t4 (`t4-coder-route-by-vendor`, d4 and d5, complexity 6), and t3 is now `t3-instruction-step` (complexity 5). t2
depends on the new t3 and has a d6. Changed text: s5 t4 d4, s6 t3 d6, s7 t2 d5, s8 t2 d2, s8 t4 d4.

For the lead: the bullet of the vendor prompts no longer holds the words of decision 40 "(coai runs the checks)". The owner's
intent is kept (a coder of codex or agy does not refuse because of such a command), and the line now says what coai ran. Whether
this repository's own file declares the resolver depends on the size s7 t3 measures. Both findings are plan changes.

## M. The owner's decisions of 2026-11-26 — plan changes, and the stop

After the second plan round the owner decided the open points of section H of SPEC.md and widened two things. Each decision
is a change of an approved plan, and so is decision 40 of the day before (the lead, 2026-11-26). The owner on counting them
and on what follows: «да, пересматриваем». So the changes are entries of the parent plan's ledger
([PLAN.md](../../PLAN.md), `## Plan changes`), the plan is **stopped for re-review**, and epoch 3 is revised and reviewed
whole. The lead runs that review; a new plan round follows, so the "no new round follows" of section L no longer holds.
The section letters and items of the right column are those of SPEC.md.

| Entry | Cause (the owner; 2026-11-26 unless a date is given) | Where it is in E3 |
|---|---|---|
| 1.1 | decision 40, 2026-11-25: a coder prompt for each vendor, the claude text unchanged | C7 (unchanged by this section) |
| 1.2 | the caps: s8 t3 and s8 t4 together at most 30 USD; s5 t6 at most 30 codex calls; s6 t7 at most 30 agy turns | C4, H, I; s5 t6 d5, s6 t7 d7, s8 t3 d2, s8 t4 d2 |
| 1.3 | the risks agy A1 to A9 and codex C1 to C4 are accepted; claude, codex and agy each ship as coder, reviewer and consultant; the codex and agy main-AI rows are both filled | C8, D, H (risk 9), I; s6 t7 title, d8 and d9; s7 t4 d4 |
| 1.4 | the instruction step keeps 64 KiB; the rule resolver gets a short form later, outside epoch 3 | C9 items 4 and 9, G; s7 t3 d5 |
| 1.5 | a secret in the output of the instruction step stops nothing; a notice with the class and the line; a tab of its own in the notifications | C9 items 8 and 10, V23, H (risk 12), G, I; s7 t3 d6 (new); the tab: `doneWhen` of epoch 4 `e4-local-pages` |
| 1.6 | an outside dependency is in the pack text of every scope, epoch and epic included | B items 3 and 4, P7, I; s3 t3 d2 and d5 (new) |
| 1.7 | a coder's checks (E4): the head's own definitions run, nothing is refused, every difference is reported; restore from the internet | C9 item 2, G, H (risk 10); no task of E3 changed. E4's own text is on `feat/v2-ep3-e4-plan` |

**The owner's words** (2026-11-26; the rest of the table is the lead's summary of them):

- on counting the changes and reviewing the epoch whole: «да, пересматриваем»;
- entry 1.3: «все 3 ии должны быть полноценными, и если нужно работать как кодеры, ревьюверы, консультанты»;
- entry 1.5: «секреты в выводе — не останавливают ничего. это нужно просто норм в интерфейсе в нотификациях отдельным табом
  показывать, что б человек мог узнать про это, и сам решит, проблема это или нет»;
- entry 1.6: «естественно добавлять, что б проверка была полноценной»;
- entry 1.7: «давай расширим, что б кодеру было удобно. рестор пакетов пусть делает из интернета сам. прокси и адреса не
  нужны», and on tests and helper scripts «разрешаем все».

**Tasks.** None added and none removed in E3: 8 stories, 44 tasks. Two dod items are new: s3 t3 d5 and s7 t3 d6. No complexity
number changed. The tab of the notifications is not a task yet: `e4-local-pages` has no stories, so it is a `doneWhen` line
there, and its tasks are written when that epic starts. The lead placed it there on 2026-11-26: epoch 3 has no extension
epic and holds 5 epics, and E3 holds 8 stories, the device limits of `validate_work`.

**The names of the two main-AI rows** (SPEC.md, section D) are the planner's proposal, not the owner's words. Why these: codex
describes its models (`astra` "most capable … complex coding", `terra` "efficient", `gpt-6-luna` "fast and affordable …
easier tasks"; `RESULTS_model_lists.md`), and `terra` and `astra` are the names the owner accepted for codex (decision 35,
server.md §20). agy prints no size and no price, so its row uses only the two families this repository already runs:
`gemini-4-argon-*` (accepted for the coder, decision 35) and `gemini-3.8-flash-*` (`gemini-3.8-flash-low` is the smallest
by its name). Its Neon, Carbon, Hydrogen and Orchestrator names are in the list and are NOT used: nothing measured says
what they are.

**The move of sections J to M.** SPEC.md was 62 502 bytes before this section and would have been about 79 600 with it. The
`spec` file of an epic may hold 64 KiB (`src_mcp/core/Work/WorkPack.cs:60`); over it a folder round of this epic is refused.
So the record of the rounds lives here, and SPEC.md holds the plan as it stands (64 865 bytes after the move: under 1 KiB is
left, so the next addition needs a move as well).

**Not entries.** The ledger was opened with these seven. The earlier causes of this epic — the judgement of s4 t6 and the coder
comparison (section K, causes 1 and 3) and the two findings of the second plan round (section L) — are named in those sections
as plan changes and are not entries: the lead named seven causes to record. Whether they are added is the lead's decision; the
plan is stopped either way.

**What the stop means** (the rule of plan changes): no new code on any epic of the plan and no merge of its work until the
whole plan was reviewed again and a new epoch of the ledger is open. Allowed: the re-review, its reverts, work outside the
plan, an incident fix the owner orders. Work that was running when the stop was written — the server side of entry 1.5, the
measures s5 t6 and s6 t7 — is sorted by the re-review's judge as `fits` or `cut`, like every commit made under this plan.

For the lead, not edited by the planner (outside its files):

1. `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`: section 7 still says "the owner confirms these before the release
   enters the table", and section 10 item 6 still lists the caps and section 7 as the owner's. The answer is risk 9 of
   SPEC.md, section H.
2. `stories/s7-vendor-seam-rows/todo.md`: three entries are answered now — t3 d5 (C9 item 9), t4 d4 (section D) and the M1
   note "fail closed, no redaction" (C9 item 10 replaces it).
3. `v2/_gen/v2-spec.json` and `v2/REVIEW_BUNDLE.md` do not hold the new `doneWhen` line of epoch 4 `e4-local-pages`.
4. The short form of the resolver (entry 1.4) has no plan in the conventions repository yet; a boundary between two
   repositories is named on both sides.
5. «прокси и адреса не нужны» was said of the restore step. The forwarder of a coder unit (judgement G11; E4 `doneWhen`
   line 8) and the measure s6 t7 d6 behind a proxy were not changed by it here; whether the owner meant them too is a
   question for the whole review.
6. The re-review record of the ledger (`### Re-review for epoch 2`: the narrowed line and the table of the done work) is
   written by the re-review, not here.
