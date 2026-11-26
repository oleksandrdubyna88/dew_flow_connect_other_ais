# Spec changes — epoch 3, E3 "Roles complete": the plan gates and the changes after them

> Status: **the record of the plan rounds of this epic and of every change of its plan after an approval, 2026-11-26.** The
> plan as it stands is [SPEC.md](SPEC.md); this file says how it got there. Parent plan: [PLAN.md](../../PLAN.md), whose
> ledger (`## Plan changes`) counts the changes. Sections J, K and L were moved here from SPEC.md on 2026-11-26, word for
> word, and so were three records of what is done (C3-H, C4-U, H-Q): the `spec` file of an epic may hold 64 KiB (SPEC.md, B item 5), and with section M it held more. This file is not
> in the pack of a folder round: a free `.md` file beside the nodes is on the line Not sent.

## C3-H. What each CLI offered, read from `--help` on 2026-11-25 (moved from SPEC.md, section C3, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold. It is a reading of two versions that are no
longer installed: history, as every measured version is since the owner's decision of 2026-11-26 (section M, entry 1.8).

| | codex-cli 0.171.0 | agy 1.5.16 |
|---|---|---|
| Resume | `codex exec resume <id>`: has `-c`, `-m`, `--json`, `-o`, `--output-schema`; has no `-s`, no `-C`, no `--add-dir` | `--conversation <id>` (also `--resume`, `-r`) |
| Write limit | `exec -s read-only \| workspace-write \| danger-full-access`; on resume only `-c sandbox_mode=…` | `--mode accept-edits \| auto \| plan`; `--sandbox` ("terminal restrictions"); `--agent`; a `PreToolUse` hook |
| Model-free probe | `codex sandbox <command>` runs a command in codex's own sandbox | the hook handler is coai's own program (`--agy-hook`) |
| Structured answer | `--output-schema <file>` on `exec` and on `exec resume` | `--json-schema` |
| Never in a coai argv | `--dangerously-bypass-approvals-and-sandbox`, `--dangerously-bypass-hook-trust`, `-s danger-full-access`, `--trust`, `--worktree`, `--ephemeral` (a coder turn), `--add-dir` | `--dangerously-skip-permissions`, `--remote-agent`, `--remote-control`, `--continue` |

## C4-U. The unknowns of the measure of s4 (moved from SPEC.md, section C4, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold. s4 is done: the answers are in
`v2/spikes/RESULTS_codex_coder.md`, `v2/spikes/RESULTS_agy_coder.md` and `RESULTS_model_lists.md`.

| # | Unknown | Free or paid |
|---|---|---|
| U1 | codex: does `exec resume` find the session in a `CODEX_HOME` of the job's own; from another folder; with another home | paid, small |
| U2 | codex: does `--output-schema` take `shared/coder-report.schema.json` (it has `allOf`); on resume too | paid, small |
| U3 | codex: which tools a session has (apps, browser, computer use and others are on by default in `codex features list`), and the switches that leave the shell and the patch tool only | free |
| U4 | codex: does its own sandbox start INSIDE the unit (`PrivateUsers`, `NoNewPrivileges`); does it stop a write outside and the network of a command; what the CLI does when it cannot start | free (`codex sandbox`) |
| U5 | both: can the unit hide the main checkout except its `.git`, and other jobs' worktrees, and does the CLI still work | free |
| U6 | both: which files of the real home are the sign-in; can a child command read them or `/proc/<cli pid>/environ` (a token refresh with the real sign-in is NOT tested: risk 2 of section H) | free, and paid, small |
| U7 | both: do files of the repository at the base commit run code or widen the limit (codex: `AGENTS.md`, `.codex`, rules, hooks; agy: `.agents/agents`, `.agents/hooks.json` in the WORKTREE — this repository has an `.agents` folder) | paid, small |
| U8 | agy: the names of its write tools and command tools; does an agent's `tools` list limit them; does the hook see every write with its target, fresh and resumed | paid, small |
| U9 | agy: does a tool run when the hook handler exits non-zero, prints nothing or runs past its time (on Linux) | paid, small |
| U10 | agy: what `--mode accept-edits` and `--sandbox` each do to a write outside and to a command | paid, small |
| U11 | agy: `--conversation` in a home of the job's own; the text of a conversation that is not found | paid, small |
| U12 | both: the model names as the CLI prints them, and the smallest model of each vendor | free |
| U13 | is there a property of a user unit that limits the network to the vendor's hosts | free |

## H-Q. The questions to the owner and the answers of 2026-11-25 (moved from SPEC.md, section H, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold. "Below" in the answers to questions 3 and 5
means the owner's decisions of 2026-11-26: SPEC.md, section H, and section M here.

Questions for the owner:

1. **The credential for the codex and agy measures and coder jobs.** A copy of the person's sign-in file in a scratch home (risk 2),
   or a separate API credential with a spend limit? Without an answer s4 t2 to t5 do not start.
2. **A refused vendor.** The table of C5 says what the plan does in each case. The owner decides line 2 of `doneWhen` when a
   vendor is refused.
3. **The lists of section D**: the scout and coder failover entries, and which measured model takes which role when codex or agy
   is the main AI.
4. **The parent plan** was corrected with the gate findings (section J): E3 depends on E2, E4's resume line, two cornerstones in
   `feature.yaml`. Not changed: `PLAN.md` still says "10 cornerstone stories"; the list now has 12.
5. **Paid caps**: 44 small calls in s4 (22 per vendor), 0.10 USD for the scout and judge proof, 0.50 USD for the coder proof. agy
   draws on a quota that is not unlimited.

**The owner's answers** (the numbers are those of the v2 decisions list; all dated 2026-11-25, decision 39 2026-11-25/26):

- Question 1 — decision 35 asked for separate API keys with a spend limit; decision 37 replaced it: there is no API key for
  codex or agy, only subscriptions, so s4 first measured whether a COPY of the sign-in in a scratch home leaves the real
  sign-in as it was. It does, for both (`RESULTS_codex_coder.md`, `RESULTS_agy_coder.md`); the judgement keeps a codex refresh
  from happening in a job (its section 9).
- Question 2 — decision 39: agy IS allowed as a coder. The judgement ships agy with its must-change list instead of refusing it,
  and line 2 of `doneWhen` stays for both vendors.
- Question 3 — decision 35: the scout and coder failover lists of section D are accepted. The main-AI rows of codex and agy:
  answered on 2026-11-26, yes, both (below, and section D).
- Question 4 — done: `PLAN.md` counts 12 cornerstone stories since `baf9930c`.
- Question 5 — the calls of s4 are spent and written down in the two results files. The caps of the open tasks: answered on
  2026-11-26 (below).
- **Decision 40** (2026-11-25, «да, (а)»): the coder prompt has a version for each vendor — its own tool names, and a line that
  commands which the repository's instruction files order first are not run in the job, because coai runs the checks. The claude
  text stays exactly as it is today. Section C7.

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
| 1.8 | a new version of a CLI never stops anything, for EVERY vendor — claude, codex and agy: no version, release, hash or pin gates, refuses, stops a job or forces a new measure, a new judgement or a doc rewrite; what was measured is history, "measured on X"; a vendor ships by its verdict (recorded after the re-review round, with its findings) | section 1 decision 6, C2 (items 1, 2 and 4, "How the history grows"), V6, C8, F, H (risk 7 gone, A6 fell away, risk 13 new), I; s5 and s6 and s7 story titles; s5 t1 (title, d1, d2); s6 t1 (title, files, d1 to d4); s7 t1 (title, d1, d3, d4, d5, d6 new); s5 t6 d6; s6 t7 (title, d1, d5, d7, d8); s6 t4; s6 t5; s8 t1 d2; s8 t4 d1 and d3; s8 t5 d1 and d2 |

**Entry 1.8, said plainly.** The owner's words as the lead gave them: "a new version must never stop anything, or rewrite
docs, pins and so on. It came out, it came out. We move on. We can write down for history which version the check was on."
The CLIs update themselves daily: agy went from 1.5.17 to 1.5.18 on that morning. And the owner's addition: it holds for
EVERY AI, claude too — a claude release row or a version check of the coder or of a reviewer is history as well.

- **What it takes out of the plan:** "only a measured release" (the first plan gate's finding 1, section J; the old C2
  item 2); the pin by bytes and the agy program store (judgement G9; ids CX-P1, AG-P1, AG-P2); the refusal of a vendor
  after its CLI updated itself (the old risk 7); the cost of the pin (accepted risk A6); the version floor of claude
  (`ClaudeWriteCapability.cs:23`); the need to measure the rows of s4 t5 again before s6 t7 may run.
- **What stays, and why it is not a version gate:** the verdict of the judgement; the flags the help of the installed CLI
  declares; the preflight rows inside the unit before each job; the launch checks; the turn checks on the CLI's own
  stream. Each asks the installed CLI what it DOES. A refusal of one of them names the property, never the version.
- **One reading of the planner's, for the lead:** "a vendor ships by its verdict" is written so that codex and agy do NOT
  ship today. The table holds `ship` for both with no history record, because the judgement tied its verdict to the open
  rows (s5 t6, s6 t7). So a vendor with no history record is not shipped until the addendum's commit writes the verdict
  and the record. That is the judgement's condition, not a version. Read the other way, both vendors would ship now,
  before their open rows are measured.
- **What the plan now says about the cost** (SPEC.md, risk 13): a boundary is measured on one version and runs on every
  later one; a property of class `release` is assumed, not measured, on a version that was not measured.
- **s6 t7** runs on the version installed now (1.5.18) inside the cap of 30 turns; a row that does not fit is recorded as
  not measured; d5 (the program from a read-only copy, G9) is dropped.
- **Not edited by the planner:** the judgement file still holds G9, CX-P1, AG-P1 and AG-P2 as must-change and A6 as a risk;
  the E2 SPEC and its tests still hold the claude floor; other epics' plans were not swept for version gates.

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

**Rejected in the re-review round (finding F2; the round is section N).** The finding: this repository has no instruction
step until the short form of the resolver exists, so a coder of this repository works without the repository's own
command. Rejected, with this reason: it is the owner's decision of 2026-11-26, entry 1.4 — the limit stays 64 KiB, no
output is cut to fit it, and the resolver is taught a short form later, outside epoch 3. The plan says what follows from
it and hides nothing: `.coai/toolchain.yaml` of this repository declares no step and a test holds that; a coder here is
told that nothing was run; the envelope says `declared: false` (SPEC.md, C9 items 7 to 9, risk 10). A reviewer cannot
reopen it; only the owner can.

## N. Re-review round of E3 (proceed, 2026-11-26) — accepted changes

The round (coai session `1089eaa8`, branch ref `plan/v2-ep3-e3-rereview`) read the plan at `c48b3a5c`. Verdict `proceed`.
Four findings: three accepted (F0, F1, F3), one rejected (F2, section M). The plan stays stopped until the lead opens the
next epoch of the ledger. The three are entries 1.9 to 1.11 of the ledger, source `reviewer`, by the lead's order (entry 1.8 is the owner's
decision on versions, which came after the round and is in section M); the
rule of plan changes also says that what a re-review changes is the baseline of the next epoch (the note under the
entries says both).

1. **F0 — the failure record of the instruction step can hold a secret** (C9 item 5). The record keeps the argv and the
   last lines of standard error. Now both go through the redactor before they are written to the envelope, the job's
   record or a log line, or shown, and the argv goes the same road wherever else it is recorded. It is a rule of RECORDS.
   The owner's "a secret in the output stops nothing" (entry 1.5) still holds for the output that reaches the prompt, and
   the argv that is run is the real one. Scenario V24; task s7 t3 d7 (new): a failing step with a sentinel secret in its
   standard error and one in an argv item shows neither in the job's record, the envelope or the log.
2. **F1 — the approval id is not versioned with the pack format** (B items 3, 7 and 8). The outside-dependency change
   (entry 1.6) changes what a pack's text holds while the id of an older approval stays valid. Now the first approval
   line is `V <pack format version>`, one constant beside the pack code, raised by every change of what a pack's text or
   its approval lines hold, and raised with the dependency change. An approval made under an older format binds no coder
   job, and the refusal says that a new plan round is needed. **This ends the words "approval ids stay" of entry 1.6: the
   approval id of every scope moves once.** Scenario P13; task s3 t4 d6 (new, with its test rows); s3 t3 d5 reworded.
   How an older format is told from a changed file without a new stored field: the binding computes the id at the
   round's own commit first; when that is not the approved id, the format differs.
3. **F3 — the 30 USD cap of the paid proofs was checked from the ledger, after the money was spent** (H; s8 t3, s8 t4).
   Now the cap is enforced before each call: a call reserves its priced worst case — the vendor's smallest model, a bound
   on the tokens of the call, the price of a table written down before the first call — and is not launched when the
   reservation is more than the budget left, or when no bound or no price can be named; the reservation is recorded
   beside the actual cost. Tasks s8 t3 d3 and s8 t4 d5 (new); d2 of both reworded. The caps of s5 t6 (30 calls) and s6
   t7 (30 turns) are counts and were already checked before a call.

**Tasks.** None added, none removed: 8 stories, 44 tasks. Four dod items are new: s3 t4 d6, s7 t3 d7, s8 t3 d3, s8 t4 d5
(and s7 t1 d6 by entry 1.8). No complexity number changed.

**For the lead.** (1) F1 makes every folder approval that was recorded before the format version is built stop binding
coder jobs, the approval of this round included if it was a folder round: a new plan round is then needed, once. (2)
SPEC.md is at its limit again; to make room, three records of what is done moved here word for word: the help table of
2026-11-25 (C3-H; the never-in-argv rule stays in SPEC.md), the unknowns of s4 (C4-U) and the questions to the owner with
the answers of 2026-11-25 (H-Q).
