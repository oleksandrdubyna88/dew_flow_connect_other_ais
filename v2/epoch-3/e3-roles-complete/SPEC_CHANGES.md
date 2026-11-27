# Spec changes — epoch 3, E3 "Roles complete": the plan gates and the changes after them

> Status: **the record of the plan rounds of this epic and of every change of its plan after an approval, 2026-11-26.** The
> plan as it stands is [SPEC.md](SPEC.md); this file says how it got there. Parent plan: [PLAN.md](../../PLAN.md), whose
> ledger (`## Plan changes`) counts the changes. Sections J, K and L were moved here from SPEC.md on 2026-11-26, word for
> word, and so were the records of what is done (0-T, C3-H, C4-U, C7-E, C8-T, H-Q): the `spec` file of an epic may hold 64 KiB (SPEC.md, B item 5), and with section M it held more. This file is not
> in the pack of a folder round: a free `.md` file beside the nodes is on the line Not sent.

## C7-E. The evidence for the coder prompt of each vendor (moved from SPEC.md, section C7, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold. The runs themselves are in
`v2/epoch-3/e2-coder-one-task/RESULTS_coder_comparison.md`.

**The evidence** (`v2/epoch-3/e2-coder-one-task/RESULTS_coder_comparison.md`, one task, one base). codex `gpt-6.1-terra` answered
`status: refused` twice on the shipped prompt. Run A, the prompt as claude gets it: `coder.md` names claude's tools and says "You
have no shell"; codex has none of those names and read no file. Run B, the tools bullet in codex's words: codex read
`.agents/conventions/ENTRY.md` and `.agents/PROJECT.md`, then refused, because `AGENTS.md` of this repository orders a command (the
rule resolver) before any edit and the prompt forbids commands. Run C, B plus one bullet about such commands: green, 0 fix turns.
agy `gemini-4-argon-medium` was green on the shipped prompt (its guard denied the two reads under `.agents`). claude was green on
the shipped prompt.

## 0-T. What the code had when E3 was planned (moved from SPEC.md, section 0, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold. It describes the code at `0e8e8941`, before
any task of E3 was built; "today" in it is 2026-11-25 and 2026-11-26.

What the code has today (checked at `0e8e8941`):

- Three roles can start: `probe`, `ba`, `coder` (`src_mcp/src/Server/RoleJobs/RoleDescriptors.cs:19`). The roles table has five
  lists: orchestrator, coder, scout, important, ba (`src_mcp/src/Roles/RoleDefaults.cs:24`). The scout and important lists have no
  role that runs.
- The coder is claude only: `CoderRoute.cs:18`, `CoderLaunches.cs:21`. The codex and agy adapters refuse a coder launch
  (`CodexConsultant.cs:58`, `AntigravityConsultant.cs:52`).
- `review_plan` takes `planText` or `committedPlan` (`src_mcp/src/Tools.cs:89`, `src_mcp/src/Server/Stages/PlanInput.cs:10`).
  `IWorkFolder` was made so that E3 can read a commit (`src_mcp/core/Work/IWorkFolder.cs:27`).
- A plan round records `PlanSource` with origin `commit` or `text` (`src_mcp/core/Rounds/PlanSource.cs:4`). A coder job is bound to
  a plan FILE (`src_mcp/src/Server/RoleJobs/CoderPlanBinding.cs:56`).
- The key pool (E2 s7) is on its own branch. E3 uses only `KEY_POOL_SPEC.md` §7: a claude launch of scout or judge goes through
  `RoleModelLauncher`, so it gets a pool key when that branch is merged. E3 adds no key code and no pool for codex or agy.
- Since the merge of `feat/v2-ep3-e2-coder` `ec24ee4c` into this branch (`3c089f7f`, 2026-11-26) the key pool, the coder proof and
  the coder comparison (`v2/epoch-3/e2-coder-one-task/RESULTS_coder_comparison.md`) are in this tree.

## C8-T. What the coder comparison of E2 did not show (moved from SPEC.md, section C8, on 2026-11-26)

Moved word for word, to keep SPEC.md under the 64 KiB a spec file may hold.

| Not shown by the comparison | Who shows it |
|---|---|
| the unit and the preflight rows of the PRODUCT for codex and agy (the harness ran a probe of 17 paths, not the rows) | s5 t3, s6 t4; s8 t4 on a real job |
| the check units: the harness kept the sign-in copies in a scratch folder under `/tmp`, which a check unit replaces; in the product they are in the job folder | s7 t5 |
| a fix turn, a resumed thread or conversation, the guard in a red round (nothing went red) | s5 t4, s6 t5 with FakeCli; s8 t4 with a planted red check |
| coai's own handler (`--agy-hook`): the harness used the shell guard of s4, made fail-closed | s6 t2, s6 t4 |
| the report through coai's strict reader: codex wrote `"reason":null,"notes":null` (the flat schema makes every member required); agy has no schema flag | s5 t2 d2 (V19), s8 t4 |
| the vendor's prompt as the PRODUCT renders it, on a repository whose instruction file orders a command | s8 t4 d4 |
| claude with the main checkout hidden (claude ran in E2's unit: read-only, not hidden) | nobody in E3: the claude unit stays byte for byte |
| more than one task and one run for each vendor | nobody; s8 t4 adds one run for each vendor |

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

Moved with it on the same day, word for word:

Known from the code: codex names its thread in `thread.started` and a lost session says "no rollout found"
(`CodexConsultant.cs:144`); agy reports usage as the sum of the conversation (`AntigravityConsultant.cs:138`); agy's read-only block
is an agent with one tool plus a hook that also checks the path (`AntigravityReadOnly.cs:46`).

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

Moved with it on the same day, word for word:

Each paid measure uses the vendor's smallest model (the vendor-routing rule: never a Claude model on codex or agy), in scratch
folders, with a home in the scratch folder. Caps of s4 (spent): resume tasks 6 calls each, row tasks 16 calls each; every call and
its spend are written down (tokens where the CLI names no cost). The caps of the open measures and proofs are in section H.

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
  before their open rows are measured. (Replaced the same day by the owner's condition, entry 1.16: section O.)
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

## O. The owner's answer on the addenda 2 and 3 of the judgement (2026-11-26) — ledger entries 1.15 to 1.17

The measures s5 t6 and s6 t7 are done, and the judge wrote two addenda: sections 13 (codex-cli 0.171.0) and 14 (agy 1.5.18) of
`v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`, at commit `d0dca16d` on `feat/v2-ep3-e3-measure`. That branch is
not merged into this one when the answer was folded in, so the plan cited the commit; it is merged since, as `a9187e76`. The owner answered the same day. The planner has the answer
from the lead, not in the owner's own words. The plan stays stopped.

| Entry | The owner's answer | Where it is in E3 |
|---|---|---|
| 1.15 | the risks the addenda added are accepted: codex C5, C6, C7 and C9; agy A10, A11, A12 and A13 (A1 to A9 and C1 to C4 were accepted in entry 1.3; C8 was closed by the measure). G13 is answered | SPEC.md H (risk 9), C8 |
| 1.16 | codex and agy become launchable coders only after the must-changes of the judgements are built and green — G15; the exact rule of CX-T4; the `environments.toml` refusal; a refresh before the launch, so that an expired copy never reaches a start (A11). Not before | SPEC.md C2 item 1, C6, V25 to V29, I; s5 t5 d4 and s8 t6 d4 (new); s7 t1 d3 and d5; s8 t1 d2; s8 t4 (d1, d3, `dependsOn`) |
| 1.17 | the property and behaviour checks stay — an item type of the stream that nobody measured, a flag the help does not declare, the preflight rows, the turn checks. One of them may stop a job after a CLI updated itself. That is accepted as protection, and it is not a version gate | SPEC.md C2 item 2; s5 t4 d3; s6 t5 d3 |

**How entry 1.16 is built.** The table's history (the key `measured`; both vendors have a record since the addenda) makes
nothing launchable. A vendor's row carries a mark, `launchable`. It is off for codex and for agy. ONE commit turns it on
for a vendor, when the must-changes of its addendum are built and green: s5 t5 d4 for codex, s8 t6 d4 for agy. Until then
the product refuses that vendor's coder by data. This replaces the planner's reading under entry 1.8 (section M: "a vendor
with no history record is not shipped"): the history records exist now, and the owner's condition is what gates.
s8 t4, the paid proof, now depends on s8 t6, so that the agy proof runs after agy is launchable.

**The lists 13.11 and 14.8 of the judgement, mapped on the task files.**

| The judgement says | Task |
|---|---|
| 13.4, 13.9 item 1: the argv is CX1 and `-c agents.enabled=false`, on every turn; X9 is closed, nothing waits | s5 t2 d1 |
| 13.11: no `environments.toml` in the job's `CODEX_HOME` before every launch (the launch half of CX-C1) | s5 t2 d2 |
| 13.3, 13.9 item 2: CX-T1 with the job's own model and the 11 names; the `doctor` row for CX-C4 and CX-C3; CX-R4 is in the table. 13.11: CX-T1 is no evidence about the web search (CX-T6, `release`) | s5 t3 d3 |
| 13.6, 13.9 item 3: the three signs of a lost sign-in, the text that is not one, the second fixture | s5 t4 d2 |
| 13.11: the exact rule of CX-T4, with FakeCli playing a list that is not empty, another server and an unknown item type; 13.3: CX-T5, the rollout file | s5 t4 d3 |
| 13.5, 13.9 item 4: no release and no hash gates | s5 t1 d1 and d2 (done under entry 1.8) |
| 13.6, 13.9 item 5: the copy with the placeholder; K3 over the access token and the id token | s7 t6 d2 and d5 |
| 13.9 item 6: the reader, the key `measured`, what makes a vendor launchable | s7 t1 d3 |
| 14.8 item 1, s6 t2: the guard's record with the conversation id and the step index | s6 t2 d3 |
| 14.8 item 1, s6 t5: G15, the signs of AG-M2, the failover texts and the two fixtures | s6 t5 d2 and d3 |
| 14.8 item 1, s6 t3: the read-only guard folder; the copy with no refresh token and the margin before every launch; the turn deadline under the life of a token | s6 t3 d2 and d3 |
| 14.8 item 1, s6 t1: no release and no hash gates; the program folder bound read-only | s6 t1 (done under entry 1.8); s6 t3 d2 |
| 14.8 item 1, s7 t6: the refresh unit and where its full copy lives (A12); a refresh only when the real file has no margin | s7 t6 d2 and d3 |
| 14.3: AG-B3 and AG-R2 are in the table; AG-M4 is class `release`; AG-M3 is not built | s6 t4 d1 and d4; s6 t5 d3; s6 t3 d1 |
| 14.8 item 2: the owner's word on 14.7 before agy is launchable (G13) | entry 1.15 |
| 14.8 item 3: the SPEC — the store is gone, the tie of the turn check, AG-M4 and A9, the risks | SPEC.md C6, C8, H |

**Tasks.** None added, none removed: 8 stories, 44 tasks. New dod items: s5 t5 d4, s8 t6 d4. One `dependsOn` is new: s8 t4 on
s8 t6. `shared/coder-boundary-releases.json` is new in the files of s5 t5 and s8 t6. No complexity number changed.

**For the lead, not done here.** (1) The mark `launchable` is the planner's name for what the owner's condition needs; the
table on the measure branch has no such member, and the reader is s7 t1's. (2) "Built and green" is said by the commit that
turns the mark on: it names the suite run it rests on. Nobody but that commit's author checks it. (3) The judgement file
is not edited: its 14.1 still says that the agy coder waits for the owner's word. (4) 13.11 says CX-T5 and CX-T6 are new
ids and C9 is worth one model-free probe when the story is built; the probe is not a task and not a condition. (5) To keep
SPEC.md under 64 KiB, five more records of what is done moved here word for word: C7-E, 0-T, C8-T, and one paragraph each
under C3-H and C4-U.

## P. What the security verify of the built coder changed (passes 5 to 10, 2026-11-27)

These changes came after the plan gates, from the passes of the security verify (`RESULTS_e3_s7_t3_security_verify.md`); the code holds them and each has a test. None adds a task.

- **The turn check runs for every launch that has a process** (pass 5, M11), not only for a clean exit: on a cut stream (a non-zero exit, a deadline, a cancel) the vendor answers `coder-boundary` for what one side alone proves — a mode sign, an event of a tool outside the three, a DONE event with no record or with a record that is not `allow` — and a boundary wins over the crash that follows. A turn that the JOB's own token cut is checked too, with a time of its own (pass 6, M11 leg a). G15's literal rule is unchanged.
- **agy's preflight has three more rows** (pass 5, M12): `keys-folder-hidden`, `sign-in-not-in-unit` (the shared fragments) and `other-vendor-folder-hidden`.
- **Each vendor has its own temp folders** (pass 5, M13), and since pass 6 (R-M15) they lie OUTSIDE every folder a unit binds writable: `<job>/coder-temp/<vendor>/tmp` and `vartmp`. Each name is asked with `Lstat` on the host before every admission.
- **The refresh of the agy sign-in is forced by a stale scratch copy** (pass 5, M14): coai sets the expiry of ITS OWN copy two hours into the past, so `agy models` refreshes it; the real file is never written. The envelope counts the refreshes. The refresh unit hides the key pool and every real sign-in file and asks the home rule (pass 10, S26, S54).
- **What a unit can write, the host reads by one road** (passes 7 to 9, M18, M19, M20, S55): `UnitWrittenFile` — no wait, no link, a regular file, a size limit, a time bound. It covers the guard's records, the sign-in copy, the codex rollouts and `report.txt`, `CodexSessions.Clear`, and the worktree's `.coai/toolchain.yaml`. `ICoderVendor.HostReadEntries` names the entries a vendor's unit can write; after every turn each must still be a plain file, else the job ends `coder-boundary`. Every vendor's turn check has one fixed limit of 30 seconds and answers `coder-boundary` when it does not finish.
- **`checks-changed-tree` has a second meaning** (pass 9, M20): the worktree's toolchain file is a FIFO, or cannot be read. A FIFO there proves no command of the model (a check can make it), so the word is `checks-changed-tree` and not `coder-boundary`: the tree is not what coai committed and expected. It is never sent to the coder and never fails over.
- **Both vendors are launchable** (codex: s5 t5; agy: s8 t6): the `launchable` marks of `shared/coder-boundary-releases.json` are on.

## Q. The owner's decision of 2026-11-27 — a codex name with no version is the newest model of its family; the tool list is a known set (ledger entry 2.1)

**The decision.** The owner's words of 2026-11-27, as the lead gave them, in translation: "Default: the latest terra. When
6.2 or 7 comes out, it must pick up the latest available one by itself." The planner has the words from the lead, not in
the owner's own language. It is entry 2.1 of the parent plan's ledger ([PLAN.md](../../PLAN.md)): the first change of
ledger epoch 2. The plan is not stopped. The next change of epoch 2 stops it.

**Changed the same day by entry 2.2 (section R): the plan IS stopped.** One rule of this section changed: a tool outside
the known set no longer ends the job. The text below holds the rule as it stands, and each changed place says so.

**The facts it answers** (`RESULTS_e3_real_codex_pass7.md`, `RESULTS_model_lists.md`):

- The default codex coder name of the plan is `terra` (SPEC.md section D, `shared/role-defaults.json`,
  `shared/role-route-vectors.json`). The codex CLI has no model of that name: its own list holds `gpt-6.1-terra` and
  `gpt-5.6-terra`. With `-m terra` the row `tool-list` of the preflight could not read the tool list. The job was refused
  `coder-boundary`, which never fails over, so the agy entry behind it was never tried.
- The row `tool-list` asks for exactly the 11 names measured with `gpt-6.1-terra`. `gpt-5.6-terra` shows 9 of them and no
  other name, and it is refused. So every new codex model needs a hand edit and a measure before it can be a coder.
- `astra` has the same fault by the list: the CLI lists `gpt-6.2-astra` and `gpt-6-astra` and no name `astra`. No launch
  with the name `astra` was made for this section. `sol` and `luna` are in `shared/role-model-names.json` as names, and
  the CLI lists them only with a version too.
- The owner's rule of 2026-11-26 (entry 1.8): a new version of a CLI stops nothing and nobody measures again. This
  decision says the same for a model: a new model needs no hand edit.

### Q1. The name

1. **A family.** A codex name with no version is a family: `terra`, `astra`, `sol`, `luna`. The families are data: a
   list `families` in the codex part of `shared/role-model-names.json`. A family means "the newest model of this family
   that the installed CLI lists". The defaults keep the family names: the tables of SPEC.md section D do not change.
2. **Where the list comes from.** The CLI's own command `codex debug models` (s4 t1; `RESULTS_model_lists.md`: a network
   call, no model call). It is the live list, without `--bundled`: the live list is what this account is offered today,
   and the bundled list holds `gpt-5.5`, which the account is not offered. The command runs with the same home and the
   same environment as the launch that follows. For a coder it runs in a unit with the property list of the launch,
   with the job's own `CODEX_HOME` and its sign-in copy, never with the real home. It has a deadline and a size limit.
3. **Which name is the newest.** Only a name of the exact shape `gpt-<version>-<family>` with `visibility` `list` is a
   candidate. `<version>` is numbers with dots between them, and no number has a leading zero (`gpt-06-terra` and
   `gpt-6.01-terra` have not the shape; section T). Versions are compared number by number, a missing number
   is 0: 7 is newer than 6.2, 6.10 is newer than 6.9, 6.1 is newer than 6. Two candidates with equal versions: the one
   the CLI lists first. A name with other words (`gpt-terra-latest`, `gpt-6.1-terra-preview`) and a hidden name are
   never taken. So the text that reaches the argv after `-m` always has this shape, whatever the list holds.
4. **When.** When the job reaches the entry: for the first entry of a route that is the start of the job; for a later
   entry it is when the failover reaches it. Never before: a route whose first model answers makes no codex call. The
   resolved name is written into the job's record. Every later turn of that session uses the recorded name: a fix turn,
   a repair turn, a resumed session. A resume never resolves again, so a model that comes out in the middle of a job
   changes nothing in that job. The next job takes it.
5. **A name given in full is used as given.** `gpt-6.1-terra` in `COAI_ROLES_MODELS`, in `COAI_ROLES_BANDS` or in the
   `model` argument is never replaced by a newer one. A person who writes the family (`terra`) gets the family rule.
   A hidden model that a person names in full is allowed (section T): only the family rule reads `visibility`.
6. **Every codex entry of every role.** The coder, the scout, `important`, the BA, the orchestrator and the bands use
   the same names, so they use the same rule. The read-only roles were not measured with a real launch for this section.
7. **What is recorded.** The envelope, the job's record and the line of a model switch name the name as asked and the
   name as resolved. The usage line names the resolved name.

**The rule of `shared/role-model-names.json`: a resolved name needs no hand edit.** Before a folder is made (as today) a
codex name is accepted when (a) it is in `names`, or (b) it is a family of `families`, or (c) it has the exact shape
`gpt-<version>-<family>` with a family of `families`. Any other name is `route-invalid`, as today, and the sentence
names the setting. Whether the installed CLI really has a name of shape (c) is asked when the job reaches the entry
(Q2). The key `measured` stays what it is: history. It gates nothing. Why a shape is right here and wrong for claude:
the codex CLI refuses a name it does not know before any paid call (`codex-bad-model.json`), and the name must also be
in the CLI's own list. The claude CLI runs and bills its default model for a name it does not know. So **the claude
rule does not change: a claude id is never accepted by its shape.** The agy list does not change.

### Q2. The stop word `model-unavailable`

A new word of `attempts[].stop`. It means: this entry's model cannot be used on this machine now, and nothing was
launched. **It fails over at once**, like `limit`, `refusal` and `crash`. On the last entry the job ends `failed` with
this word and does not wait.

| Case | When | Word | What follows |
|---|---|---|---|
| a name that is in no list and has not the shape (a typing error) | before a folder is made, as today | `route-invalid` | the job ends; the sentence names the setting. This is the person's configuration error |
| a family, and the list of the CLI holds no model of it | the job reaches the entry | `model-unavailable` | the next entry at once; the other models of the vendor are still tried |
| a full name of the shape, and the list of the CLI does not hold it | the job reaches the entry | `model-unavailable` | the same; the sentence names the setting when a person gave the name |
| the list of the CLI cannot be read: an exit that is not 0, the deadline, not the expected JSON, over the size limit | the job reaches the entry | `model-unavailable` | the next entry; the other models of the vendor are skipped, they would stop the same way |
| the tool list of a codex coder cannot be read, or holds no name, in both reads, and every other row holds (Q3) | the preflight | `model-unavailable` | the next entry; the other models of the vendor are still tried |
| a tool outside the known set, the same names in both reads, and every other row holds (Q3; since entry 2.2) | the preflight | `model-unavailable` | the next entry; the other models of the vendor are still tried; the notice `coder-tool-unknown` names the tool. Under entry 2.1 this was `coder-boundary` and the job ended |
| the two reads of the tool list differ (Q3) | the preflight | `coder-boundary` | the job ends, as for every row |

- **The run that reads the list is read by the classifier of a launch first.** A missing CLI, a lost sign-in and a
  spent quota keep their words and their sentences. Only what is left is `model-unavailable`.
- **Why not `coder-boundary`.** That word says that the OS layer or a row of the boundary refused. Here no boundary was
  asked and none broke. The word would send a person to look for a security fault, and it ends the job with the next
  entry not tried.
- **Why not `crash`.** Nothing was launched and nothing ended without an answer. The person must see in the attempt
  that the cause is the model name.
- **Why not `route-invalid` for a name that does not resolve.** `route-invalid` ends the whole job and names a setting
  of the person. A family name is a default of coai, and a model can leave the vendor's list with no error of the
  person. The entry behind it can do the work.
- **Why the failover is safe.** Nothing ran with this model: no session, no write, no commit. The next entry has its
  own preflight. A coder restarts from the last coai commit, as after every failover.

### Q3. The tool list: no tool outside the known set

The row `tool-list` (id CX-T1) and the row `no-project-tools` (id CX-C3) change. The known set is the 11 names of the
judgement's section 13.3.

1. **A name outside the known set refuses the launch of this model**: the row `tool-list` is red. This is all of the
   row's security work, and it is kept whole: no `collaboration.*`, no `mcp__`, no web tool, no image tool, no name
   that nobody measured. No model is ever launched with such a tool. **What follows the refusal changed with entry 2.2**
   (section R): the entry ends `model-unavailable` and the job goes to the next model. Under entry 2.1 the job ended
   `coder-boundary`. It is still `coder-boundary` when the two reads differ (item 3) or when another row is red too.
2. **Fewer names pass.** A list of 9 of the 11 is admitted. The envelope names the known tools that this model does
   not have. They are coai's own strings, so they can be shown.
3. **The two reads must be equal.** The list in the worktree and the list in the stand-in project with its own MCP
   config must hold the same names. Before, both had to be the 11, so they were equal. Now it is asked. A project that
   adds OR removes a tool is a project whose config the CLI read: `coder-boundary`, row `no-project-tools`.
4. **A list that cannot be read, or that holds no name, passes nothing.** With the new rule an empty list would hold
   no outside name. So it is not a list. In both reads, with every other row holding: `model-unavailable` (Q2). In one
   read only: item 3, `coder-boundary`. With another row red too: `coder-boundary`, the boundary row wins.
5. **The person is told the name of the tool.** The sentence of the attempt, the envelope and one notice
   `coder-tool-unknown` for each entry that is refused this way (since entry 2.2 a job can meet more than one): the
   job, the vendor, the name as asked and as resolved, the installed CLI version, the names. A name
   is text that a program printed. So it is shown only when it is 1 to 64 characters of letters, digits, `_`, `.` and
   `-`; any other name is "a name that cannot be shown" with its length. At most 5 names are shown, then the count.
   This is the one exception to s5 t3 d4 ("a row never carries the output of a program it ran"). The tab that shows
   the notice is epoch 4 `e4-local-pages` (a new `doneWhen` line there). Until it is built, the stop sentence and the
   envelope name the tool, and the notice is one row among the other notices.

**Does "fewer tools" open a hole? The planner's reading: no.** A tool of the list is something the model can DO. A guard
is something that stops the model. If a measured tool were a guard, a model without it could do more. None of the 11 is:

| Tool | What it is | Guard? | Without it |
|---|---|---|---|
| `functions.exec` | runs the model's JavaScript, which calls the nested tools | no | no nested tool is reached that way |
| `functions.wait` | waits for a running `exec` | no | less |
| `functions.request_user_input`, `functions.request_user_input_async` | a question to the person; nobody answers in a job | no | less. Approvals are closed by the argv (`approval_policy="never"`) and seen by the row `permission-profile`, not by these tools |
| `apply_patch` | changes a file | no | the model writes with a shell command, under the same vendor sandbox (CX-V2) |
| `exec_command`, `write_stdin` | a shell command | no | less |
| `list_mcp_resources`, `list_mcp_resource_templates`, `read_mcp_resource` | reads of MCP resources; measured empty | no | less |
| `clock__curr_time` | the time | no | less |

What limits a codex coder is not in the tool list, and each part has its own row or check, which does not change: the
unit (U1 to U9), the vendor sandbox tried with `codex sandbox` (CX-V1 to CX-V6), the profile and the approval policy
read from `codex doctor` (CX-C4), the flags of the argv (CX-C2), the config read back (CX-C1), the sign-in copy (CX-R2
to CX-R4), and after every turn the stream rule (CX-T4) and the rollout files (CX-T5). So no measured tool is kept
required.

What the planner cannot say, and what the row no longer says:

- **This is not a judgement.** Section 13.3 of the judgement says of CX-T1: "A list that differs for another model
  refuses the job; that is the row working, and the lead then has a measure to order, not a list to widen." The
  owner's decision replaces "a measure to order" for a list that is SMALLER. For a list that is larger the sentence
  stands. The judge did not read this rule. The task is `security: true`, so it runs on the `important` list; an
  addendum of the judgement is the lead's to order.
- **Another mode of the CLI.** A model that does not work through `functions.exec` would show its tools with other
  names, for example `functions.exec_command`. Those names are outside the known set, so it is refused. This was not
  seen: `gpt-5.6-terra` showed 0 names outside the set.
- **The coder prompt of codex names `apply_patch` and shell commands** (SPEC.md C7 item 3). A model without one of them
  gets a prompt that names a tool it does not have. This is not a hole. The envelope names the missing tools.

### Q4. What "by itself" does not cover

Two checks stay as protection (entry 1.17) and can stop a new model until a person looks:

- **A new model with a tool that is not in the known set** is not launched. Since entry 2.2 the job goes to the next
  model, and the notice names the tool. That codex model stays unused, and every job that reaches it pays one
  preflight, until a person looks at the tool and one commit adds it to the known set.
- **A new model whose stream holds an item type that nobody measured** (rule 4 of CX-T4) ends the job after its turn.
  The measured streams hold no `reasoning` item (pass 7, `gpt-6.1-terra`, effort medium).

- **A new name scheme** (section T). The family rule knows one shape, `gpt-<version>-<family>`, and four families. A
  model that the vendor names in another way (a new family word, a version after the family, a name with no `gpt-`)
  is not picked up by itself, and the name check refuses it when a person writes it. It needs one edit of
  `shared/role-model-names.json`: a new family, or the name in `names`.

A model that the CLI lists and nobody priced is launched like any other: the usage line names it.

### Q5. Risk 14, scenarios, tasks

**Risk 14 (new; SPEC.md H).** A codex model that nobody measured runs as a coder on the day the CLI lists it. It is
risk 13 for a model: the properties of class `release` were measured with `gpt-6-luna` (two calls of pass 7 ran
`gpt-6.1-terra`) and are assumed for the next model. What asks every job, for whatever model: the preflight rows, with the tool rule of Q3, and
the checks of every turn. The envelope names the resolved model. The decision of 2026-11-27 brings this risk with it;
it is written here so that the owner sees the choice whole. **The owner accepted it on 2026-11-27: "I accept"**
(section R).

**Scenarios** (the test plan of SPEC.md section I now counts V1 to V35):

- **V30** a family resolves to the newest listed model of the exact shape (Q1 items 3 and 4); a full name is used as
  given; the recorded name is the name of every later turn.
- **V31** the names that `shared/role-model-names.json` accepts without an edit, and the claude rule that does not change.
- **V32** `model-unavailable`: every row of the table of Q2, the failover, the last entry, zero launches.
- **V33** the tool list: fewer names pass; an outside name, two reads that differ and an empty list do not.
- **V34** the name of an unknown tool in the stop sentence, the envelope and the notice; a name that cannot be shown.
- **V35** the real preflight with 0 model calls: the default `terra`, the full name `gpt-5.6-terra`, the family `astra`.

**Tasks.** One task is new: `s2-scout-judge-roles` / `t5-codex-model-name-and-tool-set`. 8 stories, 45 tasks. It is
in s2 because s5, s7 and s8 each hold 6 tasks, the limit of the device (`maxTasksPerStory`), and E3 holds 8 stories,
the limit too. The plan did the same with s8 t6. s2 has room, and its roles use the same names: the scout fails over
to codex `terra` and the judge to codex `astra`. So `s2-scout-judge-roles` now depends on `s5-codex-coder`: the task
changes rows that s5 t3 built. The order of SPEC.md section G holds for the first four tasks of s2; s2 t5 comes after
s5. Two dod items of s5 t3 (d3, d4) point to the new rule. Growth: nothing new. The notice is one record in the notice
ledger of E1, and the resolved name is one more member of the job's record.

**Where it is in SPEC.md.** The status block; B12 of C1; C2 item 3; C6 (the codex must-changes); D (the rule of the
failover and one paragraph under the main-AI table); G (the tab, the order); H (risk 14); I (V1 to V35). To keep
SPEC.md under 64 KiB, two records of what is gone moved here word for word (H-7 and F-S, below).

**For the lead, not done here.** (1) The lead asked for the task in s5 or s8. Both are at the limit, so it is in s2.
A move is one `git mv` if a limit changes. (2) The band 0 to 2 of the codex main-AI row is the full name `gpt-6-luna`.
By the same decision it could be the family `luna`. It is not changed. (3) agy has no family names: its defaults are
full ids of one generation (`gemini-4-argon-medium`). The decision names terra, so agy is not changed. (4) Section 13.3
of the judgement is not edited. (5) `EPIC_BUNDLE.md` is the bundle of plan commit `6125f2ce`; it is not made again
here, so it holds 44 tasks. (6) Section P lists changes that the security verify made after the approval of epoch 2.
They are not entries of the ledger. If one of them is counted, entry 2.1 is not the first of the epoch.

## R. The owner's answers of 2026-11-27 — ledger entry 2.2, and the stop

Four answers came later on 2026-11-27. The planner has them from the lead, in translation, not in the owner's own
language.

| The owner's answer | An entry? | Why | Where it is |
|---|---|---|---|
| "Move to the next model": a new codex model with a tool outside the known set fails over, with the notice `coder-tool-unknown`. It does not end the job `coder-boundary` | **yes, entry 2.2** (contract, dod) | entry 2.1 was accepted and pushed with `coder-boundary` for this case; a later decision changed that contract | Q2 (the table), Q3 items 1 and 5, Q4; s2 t5 title, d4, d5, d6; s5 t3 d4; the `doneWhen` line of epoch 4 `e4-local-pages` |
| Risk 14: "I accept" | no | it changes no scope, goal, Definition of Done, merge condition, epic, story, build order, contract or approach: entry 2.1 already launches the newest model and nothing waited for this word. It is unlike entries 1.3 and 1.15, where a vendor could ship only after the owner's word | Q5; SPEC.md H, risk 14 |
| The turn of an agy coder: 30 minutes, not 45 | no | the plan names no number. s6 t3 d3 says that the deadline of an agy turn fits under the life of a token, about one hour: 30 minutes, 1 minute of backstop and 5 minutes of margin are 36. The number 45 is in the code (`AgyCoderVendor.TurnDeadline`), and the security verify left it to the owner (question (b) of pass 5: "the 45-minute cap stays until the owner answers") | here. No task text changes (changed by S: s6 t3 d3 now names 30 minutes and its test). The code number changes after the stop (R3) |
| E4 question 3 (this repository's `testFiles` and `testFor`; `src_vs_code` is not a node project): "yes" | no, and not E3's | an open question of the E4 plan, with the words "s5 t6 starts after the answer" | section N of the E4 SPEC, on `feat/v2-ep3-e4-plan` |

If the lead or the owner reads one of the three as a change, it is one more entry of the stopped epoch. The count is two
with or without them, so no stop depends on this reading.

### R1. The security reading of the failover (the planner's, not a judgement)

- **The failover for an unknown tool opens no hole.** The refusal is the same as before: the row `tool-list` is red and
  the model is not launched. Only what follows changed. The next entry has its own preflight and its own tool list. If
  the cause is the CLI and not the model (the switch `agents.enabled=false` lost its effect, or a connector of the
  account reached the session), the next codex model shows the same name and is refused the same way. So the rule
  "no launch with a tool outside the known set" holds for every entry of the route.
- **It applies only to the narrow case**: the same names in both reads, and every other row holds. With another row
  red the job ends `coder-boundary`, as before: the boundary row wins.
- **Two reads that differ stay `coder-boundary`. The planner's reading agrees with the owner.** The stand-in project
  differs from the worktree only by its own `.codex/config.toml`. A difference means that the CLI read the config of a
  project that is not trusted (id CX-C3). That is a fact about the CLI and the repository, not about one model: another
  codex model would run on the same repository with the same CLI. And it is a sign that text of the repository can
  change a session. A job on such a repository must end, not go on with another model.
- **What the failover costs.** Before, a job that ended told the person at once. Now the job can end green on another
  model, and the person learns of the unused codex model only from the notice and from the line of the model switch.
  Until the tab of epoch 4 is built the notice is one row among the others.
- **One mapping for every name outside the set**, `collaboration.*` and `mcp__` too. The planner found no reason for
  a case of its own: for each of them no launch is made, and the notice names the tool.
- **The older model of the family is not tried.** "The next model" is the next entry of the route. coai does not go
  from the newest `gpt-<version>-terra` to the one before it.
- **Not answered, so not changed:** a new model whose stream holds an item type that nobody measured (rule 4 of CX-T4)
  still ends the job `coder-boundary` after its turn. There a turn has run, so it is not the same case.

### R2. The stop, and what the re-review must cover

Entry 2.2 is the second change of ledger epoch 2. By the rule of plan changes the plan is **stopped for re-review**:
the line of epoch 2 says `state: stopped` and the status of [PLAN.md](../../PLAN.md) says so. The steps of the rule:

1. **One consultation** (`cons`) for the re-review, with the entries 2.1 and 2.2 quoted. A question for the owner goes
   to `consask` first.
2. **The WHOLE plan is revised** against what is known now: the goal, the Definition of Done, the merge conditions,
   the epics and the build order. Not a patch of the two entries. What changes there is the baseline of ledger
   epoch 3, never an entry.
3. **An Opus judge sorts the done work**: every commit since the approval of epoch 2 (commit `6125f2ce`, 2026-11-26),
   pushed or local, on `feat/v2-ep3-e3-roles`, `feat/v2-ep3-e4-plan` and every other branch that carries work of this
   plan, as `fits` or `cut`. The record is `### Re-review for epoch 3` in PLAN.md.
4. **The cuts**: what contradicts the revised plan is reverted or fixed by a new commit. No history rewrite.
5. **A plan round** on the revised plan, then **the owner's approval when the plan got narrower**. The planner's
   view: it did — the tool list no longer has to be the exact 11 names (entry 2.1), and a tool outside the set no
   longer ends a job (entry 2.2). So the record says `Narrowed: yes`, and the line of epoch 3 says `by owner`.
6. **Resume**: the line of epoch 3, `open`; epoch 2 becomes `closed`. Epoch 3 starts at 0 changes.

The open points that the revision must decide (each is in this file or in the ledger; none was decided here):

- Is entry 2.1 one cause or two (the name, and the tool rule)?
- Section P: the security verify changed rows, words and checks after the approval of epoch 2, and none is an entry.
- The tool rule goes against one sentence of the judgement's section 13.3, and risk 14 is new. Both ask for an
  addendum of the judgement by the important model.
- The item type that nobody measured (R1, last line): does "by itself" need a rule for it?
- s2 t5 is in s2 because s5, s7 and s8 are at the limit of 6 tasks, and s2 now depends on s5.
- The band 0 to 2 of the codex main-AI row is the full name `gpt-6-luna`; agy has no family names.
- The three answers that are not entries (the table above), to be folded in: the 30 minutes, risk 14, E4 question 3.
- `EPIC_BUNDLE.md` is the bundle of plan commit `6125f2ce`. The plan round needs a new one.
- The status block of SPEC.md still says that s5 to s8 are plan only.

### R3. What waits for the end of the stop

No new code on any epic of the plan, and no merge of a pull request that carries its work. So these wait: the rest of
s2 t5; the change of the mapping for a tool outside the set from `coder-boundary` to `model-unavailable`; the number
30 in place of 45 for an agy turn; s5 t6 of E4. Allowed: the re-review, its reverts, work outside the plan, and an
incident fix that the owner orders.

**Tasks.** None added, none removed: 8 stories, 45 tasks. Changed: s2 t5 (title, d4, d5, d6), s5 t3 d4 (its
pointer), one `doneWhen` line of epoch 4 `e4-local-pages` (its words). No scenario number is new: V32 to V34 hold the
rule as it stands.

## S. The revision of the re-review for ledger epoch 3 (2026-11-27)

The plan stopped on entry 2.2 (section R). This section is step 4 of the rule of plan changes: the whole plan read
again and revised against what is known now. **What it changes is the baseline that ledger epoch 3 is approved on. It is
not an entry.** Done before it: the consultation (step 3; consultation `aee9490c9c834b51a284184a36a53427`, codex
`gpt-6.2-astra`, one turn; advice only). Each point of the advice was checked by reading the code before it was used
(S3). Beside it: the Opus judge's sorting of the done work (step 5; the record is in [PLAN.md](../../PLAN.md),
`### Re-review for epoch 3`). After it: the plan round and the owner's approval. Neither is in this commit, and epoch 3
of the ledger is not open.

### S1. The whole plan, read again

| Part of the plan | What was read | Result |
|---|---|---|
| The goal (PLAN.md, `## Goal`) | the section | holds. Entries 2.1 and 2.2 serve "never waits for a person": no job waits for a hand edit of a model name |
| The owner's decisions of 2026-11-22 | the section | hold |
| Epochs and epics | `feature.yaml`, the 5 `epoch.yaml`, the `epic.yaml` files of epochs 3 and 4; a search of epochs 1, 2 and 5 and of E1, E2 and E5 of epoch 3 for the codex names, the tool list and the two stop words | 5 epochs, 24 epics; none added, removed or moved. No other epic depends on what changed. One `doneWhen` line of epoch 4 `e4-local-pages` came with entry 2.1 and holds the words of entry 2.2 |
| The Definition of Done | SPEC.md section I of E3 | two lines are new and one changed (S4). The other epics: not changed |
| The merge conditions | SPEC.md C2 item 1, C6; entry 1.16 | they hold: a vendor is launchable only after its must-changes are built and green. New in the baseline: the commit that changes the tool rows lands only with addendum 4 of the judgement (S2, point 3) |
| The build order | SPEC.md section G; the `dependsOn` of the 8 stories | s1, then the first four tasks of s2; s3 alone; s4, s7, then s5 and s6; s2 t5 after s5; s8 last. The doc duty of s2 t5 moved to s8 t5, so no task of s2 waits for s8 |
| The stories and tasks of E3 | the 54 YAML files | 8 stories, 45 tasks. No task added or removed by the revision. Changed: s2 t5, s8 t5, s6 t3 d3 (S5) |
| Narrower? | entries 2.1 and 2.2 | **yes**: the tool list no longer has to be the exact 11 names, and a tool outside the set no longer ends a job. So the owner approves epoch 3 |

Not checked, so not rewritten: the status paragraph of PLAN.md still says "plan, nothing built, 2026-11-22". The planner
read epoch 3 E3 against the git history of this branch and section N of the E4 SPEC. It did not read epochs 1, 2, 4
and 5 against their code.

### S2. The nine points of R2, each with its disposition

| # | Point | Disposition |
|---|---|---|
| 1 | Is entry 2.1 one cause or two? | **Kept as one entry.** An entry is never edited, and the count does not depend on it: entry 2.2 stopped the plan. The revision treats the two halves apart: the name (S4 items 1 to 6) and the tool list (items 7 to 11) |
| 2 | Section P: changes of the security verify that are no entries | **Two of them are contract changes by the rule, and they were not recorded on their day:** a host-read entry that is not a plain file after a turn, and a turn check over 30 seconds, end the job `coder-boundary` (passes 7 to 9); `checks-changed-tree` has a second meaning (pass 9). The other items of P only make the plan true as written ("after every turn", "one row for every id of class `pre`"). No entry is written now: the plan is stopped, the judge sorts their commits, and they are part of this baseline. Said plainly: with them counted on 2026-11-27 morning, the stop was due at entry 2.1. Whether they become entries is the lead's decision, as the ledger says for sections J to L |
| 3 | The tool rule goes against a sentence of the judgement's 13.3; risk 14 is new | **Addendum 4 of the judgement, by the important model, before the rows change.** It rules on: CX-T1 as a known set with the complete-inventory rule (S4 item 8), the failover before a launch (item 10), the terminal rule after a turn (item 12) and risk 14, which the owner accepted. Its verdict goes into the history of `shared/coder-boundary-releases.json` by the rule of SPEC.md C2 ("How the history grows"). A verdict other than `ship` stops s2 t5 and goes to the owner. It is s2 t5 d9. Since section T: the addendum is a judge job of its own, which the lead orders and commits unchanged; it is not written by the task, and it rules on the run that reads the list of models too |
| 4 | An item type of the stream that nobody measured | **Stays terminal** (S4 item 12): `coder-boundary`, nothing of the turn is committed, no failover. The lead's order of 2026-11-27, and the consultant's advice. The reason is measured: a web search left the tool list of the preflight the same and showed only in the stream (judgement 13.11), so the stream check is the only check there |
| 5 | s2 t5 is in s2, and s2 depends on s5 | **Kept.** s5, s7 and s8 hold 6 tasks, the limit. The consultant found one conflict and it is true: s8 depends on s2, and s2 t5 d9 waited for s8 t5. The doc duty is now s8 t5 d4; s2 t5 d9 is the addendum |
| 6 | The band 0 to 2 is the full name `gpt-6-luna`; agy has no family names | **Baseline, not changed.** The owner's words name terra. A person can write `luna` in `COAI_ROLES_BANDS` today. agy's ids stay full ids. (The revision also made one name rule for `gpt-5.5`, S4 item 5; the lead's review cut it: section T) |
| 7 | The three answers that are no entries | **Folded in.** Risk 14: accepted (SPEC.md H). The 30 minutes: s6 t3 d3 names the number; the code changes after the stop. E4 question 3: section N of the E4 SPEC, commit `9897dc2b` on `feat/v2-ep3-e4-plan` |
| 8 | `EPIC_BUNDLE.md` is the bundle of `6125f2ce` | **Made again** from the commit of this revision, by the commit after it |
| 9 | The status block of SPEC.md says that s5 to s8 are plan only | **Rewritten** from the git history of this branch. The block as it stood is 0-S below |

### S3. What the consultation added, and what the check of each point found

| The advice | Checked at | Found | Where it went |
|---|---|---|---|
| A shorter PARSED tool list does not prove fewer tools | `CodexToolList.cs:47-68` at `62c6878c`, and the same lines of the local commit `62685e2b` | **true.** The nested tools of `functions.exec` are read from the free text of its description with the pattern `### \`(\w+)\``. If that text changes its form, the nested names are not found, the list is 4 known names, both reads agree, and under "fewer pass" the row holds — with tools coai did not see. Under the exact 11 names this case was refused. By reading; no replay was run | S4 item 8; s2 t5 d5; V36 |
| After a turn, an unknown item of the stream stays terminal | judgement 13.11 (CX-T4, CX-T6) | **true**, as point 4 of S2 says | S4 item 12 |
| `model-unavailable` has two outcomes, and the last entry fails at once; say the three in SPEC.md D | section Q2 | they were in Q2 only | SPEC.md D |
| `gpt-5.5` is accepted, is not in the live list, and is never looked up | `shared/role-model-names.json:19`; `RESULTS_model_lists.md:37`; `CodexEntryModel.AsksTheList` of `62685e2b` | **true.** `gpt-5.5` and `gpt-5.4` are in `names`, have no family, and section Q1 asked the list only for a family and for the shape. Not a hole: the CLI refuses the launch. But the person gets a crash where a clear sentence was possible | S4 item 5 — **cut by the lead's review** (section T): the CLI's own refusal and its classification answer it |
| The router checks `important` and removes doubles BEFORE a family is resolved | `RoleRouter.cs:114-126` and `:172-180` at `62c6878c` | **true, both.** With `important` = [`astra`] a caller that names `gpt-6.2-astra` is refused, because the list is compared by the written name. And a route that holds `terra` and `gpt-6.1-terra` keeps both, so the same model can be launched twice | S4 item 6: lists are compared by the written name (s2 t5 d3). The skip of a second launch was **cut by the lead's review** (section T) |
| s8 depends on s2, and s2 t5 d9 waits for s8 t5 | the two `story.yaml` files; s2 t5 d9 | **true** | S2 point 5 |
| The status says "s5 to s8 are plan only" | SPEC.md line 3; the git history | **true, it was stale** | S2 point 9 |
| Tell the shipped coder paths apart from the empty main-AI defaults of codex and agy | `shared/role-defaults.json` at `62c6878c`; s7 `todo.md` | **true.** The rows of codex and of agy as the main AI are empty: s7 t4 d4 is not built. A codex or agy coder runs today through the failover of the claude row, or through a person's setting | the status block of SPEC.md |

### S4. The rule as it stands (sections Q and R, with what the revision changed, and what section T changed after the plan round)

The item numbers are kept, so that a task text that names one stays right.

The name:

1. A codex name with no version (`terra`, `astra`, `sol`, `luna`) is a family. When a job reaches the entry it becomes
   the newest `gpt-<version>-<family>` with `visibility` `list` of the live list of the installed CLI (Q1). A number of
   the version has no leading zero.
2. The resolved name is recorded once. Every later turn of the session uses it. A resume never resolves again.
3. A name given in full is used as given and is never replaced by a newer one. A hidden model that a person names in
   full is allowed: only the family rule reads `visibility`.
4. `shared/role-model-names.json` accepts a name of `names`, a family, and the exact shape. The claude rule does not
   change.
5. (Cut by the lead's review, section T. The revision looked up every full codex name in the live list. Now: a full
   name with no family, such as `gpt-5.5`, is NOT looked up. The CLI refuses a name it does not serve, and the
   classifier of a launch reads that refusal as it does today. The look-up of a full name of the exact shape stays as
   entry 2.1 wrote it in Q2.)
6. The route is not changed by a resolution. The `important` list and every other list are compared by the written
   name, before a folder is made, as today. So `gpt-6.2-astra` is not on a list that holds `astra`, and the refusal says
   to name the family or to change the list. (The skip of a second launch of the same model was cut: section T.)

The stop word and the tool list:

7. `model-unavailable` fails over at once. Three outcomes (Q2): the list of models cannot be read — the other models of
   the vendor are skipped; no model of the family, a name of the shape that the list does not hold, a tool list that
   cannot be read, or a tool outside the known set — the next entry is tried, the same vendor's too; on the last entry
   the job ends `failed` with the word and does not wait. The word exists only before the first launch of an entry
   (item 12).
8. **Fewer tools pass only when the tool inventory was read whole.** A read is whole when: the report is the expected
   JSON; every tool entry has a name; a tool that holds tools is walked; and, when `functions.exec` is in the list,
   EVERY line of its description that starts with `###` matches the grammar ``^### `[A-Za-z0-9_.-]+`$``, and there is
   at least one such line. One `###` line of another form makes the read not whole. A read that is not whole is a list
   that cannot be read (item 7) in both reads; in one read only it is `coder-boundary`. The person is told: the notice
   `coder-tool-unknown` is raised for it too, with a reason of its own and no name. Two tests: the recorded report with
   the nested inventory in another form in both copies does not hold; and the recorded reports of `gpt-6.1-terra` and
   of `gpt-5.6-terra` ARE read whole. If a recorded report is not whole under this grammar, the task stops and says so.
9. No name outside the known set of 11 is ever launched. The two reads must hold the same names; if they differ the
   job ends `coder-boundary`.
10. A tool outside the known set, the same in both reads, with every other row holding, at the first launch of an
    entry: `model-unavailable`, the next entry, one notice `coder-tool-unknown` for the entry (entry 2.2).
11. The name of a tool is shown only when BOTH hold: the shape rule of Q3 item 5, and the shared secret redactor
    leaves the name as it is (finding F0 of the plan round, section T: a name of allowed characters only can still
    have the shape of a secret). Any other name is "a name that cannot be shown", with its length.

After the first launch of an entry:

12. **`model-unavailable` exists only BEFORE the first launch of an entry.** After it, every boundary stop is
    terminal: `coder-boundary`, nothing of that turn is committed, no failover. This holds for:
    - **a red preflight of any later launch of the entry** — a repair turn, a fix turn, a resumed session — whatever
      row is red, the tool rows too (section T). A tool that shows on a fix turn was not there at turn 0: the session
      changed under the job.
    - an item type of the stream that nobody measured (rule 4 of CX-T4), a `web_search` item, a `collab_tool_call`
      item, and the rollout check (CX-T5).

    A turn of a model was run and something outside the measure was seen: another model would go on in a worktree and
    a session state that nobody can vouch for.

The run that reads the list of models:

13. (Section T.) For a coder, `codex debug models` runs in a folder that holds no text of the repository: the stand-in
    project, or an empty folder of the job. Never in the worktree: the run talks to the vendor, and `AGENTS.md` and
    `.codex` of a repository must not be beside it. It is a unit like a launch. A unit that is refused, a write that
    the OS layer stopped, and a unit that does not stop, during the list read, are `coder-boundary`, not
    `model-unavailable`. Addendum 4 of the judgement rules on this run.

### S5. Scenarios, tasks, risks

**Scenarios** (the test plan of SPEC.md section I counts V1 to V36): **V36** a tool inventory that is not read whole,
in both reads and in one; the two recorded reports read whole. V31 also holds that `important` is compared by the
written name. V32 also holds item 12 (after the first launch of an entry nothing fails over) and item 13. (V37, one
launch of a model in a job, was cut: section T.)

**Tasks.** None added, none removed: 8 stories, 45 tasks. Changed by the revision and by section T:

- s2 t5: every dod item (the task text was written again from S4; d9 is the gate of addendum 4, it was the docs).
- s5 t3 d3: a pointer to s2 t5 d5 ("fewer names pass only when the inventory was read whole").
- s8 t5: d4 is new (the docs of the family rule, the stop word, the tool rule and the notice).
- s6 t3 d3: the number 30 for the deadline of an agy turn (the owner, 2026-11-27), and the test that holds it.

**Risks.** Risk 14 is accepted. One line is added to it (SPEC.md H): a tool inventory is read from text that the vendor
writes for the model, and item 8 is what keeps a change of that text from passing as a smaller tool set.

**The definition of done of E3** (SPEC.md I): one line is new (the name, the stop word, the tool rule, the terminal
rule after a turn); the line of the main-AI rows says "names of the lists or families" where it said "measured names
only".

### S6. For the lead, not done here

1. The plan round and the owner's approval. `Narrowed: yes`, so the line of epoch 3 says `by owner`.
2. The code. Nothing of this section is built. The local commit `62685e2b` holds s2 t5 as entry 2.1 wrote it; items
   8, 10, 11 (the redactor), 12 and 13 are not in it.
3. Addendum 4 of the judgement is ordered by the lead (s2 t5 d9).
4. The number 30 for an agy turn is in the task text and not in the code.
5. The parent plan's status paragraph (S1, last lines).

### S7. After the judge's sorting (added the same day, with the record in PLAN.md)

The Opus judge sorted 67 commits made since the approval of epoch 2: 66 fit, and one, local and never pushed, is cut in
part. The record is `### Re-review for epoch 3` of [PLAN.md](../../PLAN.md); the file with one row for each commit is
[RESULTS_epoch2_done_code_sorting.md](../RESULTS_epoch2_done_code_sorting.md), copied unchanged. The judge read the plan at
`62c6878c`, before this revision.

- **The cut:** the part of the local commit `62685e2b` that ends a job `coder-boundary` for a tool outside the known set,
  with ONE notice for the job. It is entry 2.1, which entry 2.2 replaced. Fix forward by a new commit on top of it, when
  epoch 3 is open and before any push.
- **Not a cut, not built:** against this revision the same commit lacks items 8, 12 and 13 of S4 (items 5 and the
  launch-once half of 6 were cut after the plan round: section T).
- **Three commits were made during the stop** (the deviations of the record): `9897dc2b` (plan text, E4), `62685e2b` and
  `dee4b684` (local).
- **`02fb2b36`** (the claude alias `haiku` in plan mode) is on a branch from `main`, for the released product, by the
  owner's order: work outside the plan, not a deviation.
- **The 5 paid agy turns of `4466732e`** are inside the owner's cap of 30 agy turns (entry 1.2): 16 of 30 are used.
- **E4:** three findings of its task s5 t6 are in section S of the E4 SPEC, commit `3995acc6` on `feat/v2-ep3-e4-plan`.
- **Carried:** the tool text at `src_mcp/src/Tools.cs:583` still says that codex and agy are not launchable (s7 t4 d3).

## T. The plan round of the re-review, and the lead's review (2026-11-27)

**The plan round.** coai session `da911775`, on the ref `plan/v2-ep3-e3-rereview-2`, read the bundle of commit
`cdda0526`. Verdict: `proceed`. Two findings. The planner has the round and the decisions from the lead.

- **F0, accepted.** A tool name that passes the character rule can still have the shape of a secret. The shared secret
  redactor runs on the name before it is shown; a name it would change is "a name that cannot be shown" (S4 item 11).
  s2 t5 d6 has a test with a secret-shaped name made only of allowed characters.
- **F1, rejected.** It is about the argv of the instruction step. The lead's reason, as given: the instruction argv is
  the repository's own text, the unit holds no secret of coai, and the owner's rule of 2026-11-26 applies. The planner
  did not see the finding's own text.

**The lead's own Opus review** said "revise", with small changes. The lead decided each item; the planner did not see
the review's own text, only the decisions. What they changed is the baseline of ledger epoch 3, like the rest of the
revision: none is an entry.

| # | The lead's decision | Where it is |
|---|---|---|
| 1 | Must: a red preflight of any launch AFTER the first launch of an entry (repair, fix, resume) is `coder-boundary`, with no failover | S4 item 12; s2 t5 d4 and d5 (the test: a new tool shows on a fix turn, zero launches of the next entry) |
| 2 | The coder's `codex debug models` runs in a folder with no repository text, not in the worktree; a unit refusal, a stopped write or a unit that does not stop during the list read stays `coder-boundary`; addendum 4 rules on this run | S4 item 13; s2 t5 d2, d4, d9 |
| 3 | The inventory grammar is named: every `###` line of the description of `functions.exec` matches ``^### `[A-Za-z0-9_.-]+`$``, or the read is not whole; a positive test on the recorded reports of `gpt-6.1-terra` and `gpt-5.6-terra` | S4 item 8; s2 t5 d5. The repository holds no recorded report today, so the task records the two, with no model call |
| 4 | d1 names the base of the red run (`62c6878c`) and the red reasons; it names the tests that are green on that base as guards, each with the mutation that turns it red | s2 t5 d1 |
| 5 | Not needed (it fell with 13 b) | — |
| 6 | The list "Open" of the status gets s6 t3 d3, s7 t4 d3, s8 t3 d1 (a new run after the fix of the `haiku` alias) and s8 t5 d4; "t5 built" and "t3 has its real runs" are corrected | the status block of SPEC.md |
| 7 | The judgement file leaves the `files` of s2 t5: the addendum is a judge job of its own, which the lead orders and commits unchanged. A line of the definition of done: the commit of the tool rows lands only with addendum 4 = `ship` | s2 t5 `files` and d9; SPEC.md I |
| 8 | The notice is raised too, with a reason of its own, when the inventory is not read whole | S4 item 8; s2 t5 d6; the `doneWhen` line of epoch 4 `e4-local-pages` |
| 9 | (a) A leading zero in a version is refused. (b) A hidden model named in full is allowed; written and tested. (c) New name schemes are a limit of "by itself" | Q1 items 3 and 5, Q4; S4 items 1 and 3; s2 t5 d2 and d3 |
| 10 | The row "30 minutes" of the table of R said "no task text changes"; S changed s6 t3 d3. The row is marked. s6 t3 d3 gets its test: a turn limit of 30 minutes and a V28 margin of 36 minutes | R; s6 t3 d3 |
| 11 | s5 t3 d3 said only "fewer names pass": a pointer to s2 t5 d5 | s5 t3 d3 |
| 12 | SPEC.md was 65,380 of 65,536 bytes: more records move here, so that the next plan edit fits | H-9, H-D, C5-F and D-F below; SPEC.md is now under 63,000 bytes |
| 13 a | Cut: full codex names are not looked up in the live list. The consultant's point on `gpt-5.5` is answered by the CLI's own refusal and its classification | S4 item 5 |
| 13 b | Cut: the skip of a second launch of the same model. Only "lists are compared by the written name" stays. V37 is gone | S4 item 6; S5 |

**One reading of the planner, for the lead.** Decision 13 a says "do not look up full codex names". The planner took
out item 5 of S4, which the revision had added, and the parts of d2 and d4 that came with it. The row of Q2 that entry
2.1 wrote — a full name of the exact shape that the list does not hold ends the entry `model-unavailable` — is kept: it
was in the plan the owner's entry made, and no decision names it. If 13 a means that row too, a full name is never
looked up at all, and the CLI's own refusal answers every full name; that is one more edit of Q2, S4 item 7 and d4.
**The lead accepted this reading on 2026-11-27:** the row of Q2 for a full name of the exact shape stays.

**The lead's decision on section P** (S2, point 2), 2026-11-27: its two contract changes are recorded as a deviation in
the ledger and are not entries. Counting them would only move a stop that has already happened.

**The owner approved the revised plan on 2026-11-27.** The owner's word was "approve", in answer to the lead's summary of
the plan at commit `e23bd404`; the planner has it from the lead. Epoch 3 of the ledger is open, `by owner`, and epoch 2
is closed. The stop of section R is over: what R3 lists as waiting may go on. Addendum 4 of the judgement is still to
come; it gates the commit of the tool rows (s2 t5 d9), not the rest of the work.

**Scenarios and tasks after this section.** V1 to V36. 8 stories, 45 tasks; none added, none removed.

## 0-S. The status block of SPEC.md as it stood until the revision of 2026-11-27 (moved from SPEC.md, word for word)

> Status: **s1 to s3 are built (s3 t6 is open) and s4 is measured and judged; s5 to s8 are plan only, 2026-11-26.** Plan gate:
> `proceed`, 2026-11-25 (section J); the changes after it (section K); second plan round: `proceed`, 2026-11-26 (section L).
> The owner's decisions of 2026-11-26 (sections M and O) are plan changes in the parent plan's ledger
> ([PLAN.md](../../PLAN.md), `## Plan changes`). The re-review round of E3: `proceed`, 2026-11-26, 3 findings accepted
> (section N). The owner accepted the revised plan on 2026-11-26; epoch 2 of the ledger is **stopped for re-review** (entries 2.1 and 2.2, 2026-11-27: sections Q and R). Sections J to R are in
> [SPEC_CHANGES.md](SPEC_CHANGES.md): a spec file may hold 64 KiB (B item 5). Scope: the server half (`src_mcp`, `shared/`,
> `.coai/toolchain.yaml`). Written by the planner
> (Fable) from `v2/` and the code at `feat/v2-ep3-e2-coder` `0e8e8941`, read-only. Parent plan: [PLAN.md](../../PLAN.md);
> architecture: [server.md](../../architecture/server.md) §2, §8, §10, §19, §20, §23. Stories and tasks: the YAML tree under
> `stories/` (8 stories, 45 tasks, no task above complexity 6). Two cornerstones: **s3** (what a plan round over a folder
> approves) and **s4** (the codex and agy write boundary: measure first, then a security judgement).

## D-L. The lists of the claude row, from SPEC.md section D (moved from SPEC.md on 2026-11-27, word for word)

**The lists** (main AI claude; `shared/role-defaults.json`). The names there are the owner's, so the right column is a proposal
(section H, question 3):

| Role | Today | Proposed for E3 |
|---|---|---|
| scout | sonnet | sonnet → haiku → codex `terra` (only with a codex row) |
| judge and important tasks (`important`) | fable → codex `astra` (only with a codex row) | no change |
| ba | fable → opus | no change |
| coder | the band model, then sonnet | the band model, then sonnet → opus → codex `terra` → agy `gemini-4-argon-medium` (each other vendor only with its row AND a shipped write launch) |

The owner accepted the right column for scout and coder on 2026-11-25 (decision 35).

## H-9. Risk 9 of SPEC.md, section H (moved from SPEC.md on 2026-11-27, word for word)

9. **The accepted risks of the judgement** (its sections 7, 13.8 and 14.7). **The owner accepted all of them on 2026-11-26:
   agy A1 to A9 and codex C1 to C4 first, then the risks the addenda added — codex C5 (the model's JavaScript runs outside
   the vendor sandbox), C6 (no version gate), C7 (an agent's work is not in the stream) and C9 (a third program that can
   run commands as an "environment"); agy A10 (no version gate on a CLI that replaces itself daily), A11 (an expired copy
   opens a login prompt on the channel of the job's prompt), A12 (coai's refresh unit holds the refresh token) and A13
   (the guard is tied to the stream by a step index).** C8 was closed by the measure. So G13 is answered. A9 is now real,
   not conditional (C8). Only a risk that a later addendum adds or widens goes to the owner, and it alone. agy: A1 nothing of the OS stops a process inside the unit from reading the sign-in copy — with G10 an access
   token of at most one hour, without it the owner's Google refresh token; A2 the agy process has the whole network and the
   host's loopback; A3 the tool list cannot be asked before a job; A4 agy is open on "exit 0, no output", which coai's handler
   never answers; A5 "a refresh does not rotate the token" is three observations; A6 the cost of the release pin (fell away on 2026-11-26: there is no pin); A7 rule text
   of the repository steers the model; A8 an agy coder cannot edit `.git`, `.agents/**` or `.codex/**`; A9 (addendum 1, only
   when the init line does not tell the modes apart) the mode a turn ran in cannot be read. codex: C1 for a command of the
   model, B6 inside the job folder, B10 and B11 are one layer, the vendor sandbox; C2 the codex process has the whole network;
   C3 a real refresh was never run; C4 `AGENTS.md` reaches the model.

## H-D. The owner's decisions of 2026-11-26, from SPEC.md section H (moved from SPEC.md on 2026-11-27, word for word)

**The owner's decisions of 2026-11-26** (each is a plan change; the owner's words and the ledger entries are in
`SPEC_CHANGES.md`, section M):

- **Caps.** s8 t3 and s8 t4 together: at most 30 USD. s5 t6: at most 30 codex calls. s6 t7: at most 30 agy turns. They
  replace 8 calls (s5 t6), 12 turns (s6 t7), 4 calls and 0.10 USD (s8 t3), 2 runs for a vendor and 0.50 USD (s8 t4). Every
  call and its spend is still written down. **The 30 USD cap is enforced BEFORE each call, not from the ledger afterwards**
  (re-review round, F3): a call reserves its priced worst case — the vendor's smallest model, a bound on the tokens of the
  call, the price of a table written down before the first call — and is not launched when the reservation is more than
  what is left of the 30 USD; a call with no bound or no price is not launched; the reservation is recorded beside the
  actual cost.
- **Versions gate nothing**, for any vendor: what was measured is history (C2 item 2, risk 13; ledger entry 1.8).
- **Launchable only after the must-changes are built and green** (C2 item 1, C6; entry 1.16), and **the property and
  behaviour checks stay** as protection (C2 item 2; entry 1.17).
- **Accepted:** the risks agy A1 to A9 and codex C1 to C4 (risk 9). **Three full vendors:** claude, codex and agy each ship
  as coder, reviewer and consultant; the codex and agy main-AI rows: yes, both (section D).
- The instruction step (C9 items 4, 9 and 10), the pack (B item 3) and a coder's checks in E4 (section G).

## C5-F. What "folder removed" means, from SPEC.md section C5 (moved from SPEC.md on 2026-11-27, word for word)

"Folder removed" means: the judgement commit deletes the story folder of the refused vendor and its entry in the `dependsOn` of
s8, and writes the reason in the judgement file. `validate_work` accepts the folder after that commit, and no story waits for one
that is not built. The plan changed, so the epic gets a new plan round. A refused vendor is never a required launch: the route
drops it, the tests of s7 and s8 run all four combinations with an injected table, and in every one the fake CLI records zero
launches of a refused vendor.

## D-F. The files of the reviewer and the consultant of each vendor, from SPEC.md section D (moved from SPEC.md on 2026-11-27, word for word)

The reviewer and the consultant of all three exist since before v2 (`src_mcp/runners/Reviewers/ClaudeRuntime.cs`,
`CodexRuntime` in `ReviewerRuntime.cs:587`, `AntigravityRuntime.cs`; `src_mcp/runners/Consultation/ClaudeConsultant.cs`,
`CodexConsultant.cs`, `AntigravityConsultant.cs`).

## H-7. Risk 7 of SPEC.md, section H (moved from SPEC.md on 2026-11-27, word for word)

7. (Gone on 2026-11-26; it said that an update of a CLI refuses that vendor's coder until the new release is measured. No
   version refuses anything now: C2 item 2. What came in its place is risk 13.)

## F-S. The row of the agy program store of SPEC.md, section F (moved from SPEC.md on 2026-11-27, word for word)

| What grows | Size | Who retires it | When interrupted |
|---|---|---|---|
| ~~the agy program store (judgement G9)~~ | not built: no pin and no store since the owner's decision of 2026-11-26 (C2 item 2) | — | — |
