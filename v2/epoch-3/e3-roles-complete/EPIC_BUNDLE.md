# coai v2 — epoch 3, epic E3 (roles complete: scout, judge, codex + agy coder, review_plan over a feature folder) — epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§2, §8, §10, §19, §20, §23); built on epoch-3 E1 (role jobs, router, failover, work-plan schema) and E2 (the coder, its boundaries, the fix loop, the claude key pool), which is merged into this branch. The full spec is SPEC.md in the same folder; its digest is below, before the YAML files. This is the plan AFTER the plan gate of 2026-11-25 (proceed, 5 findings accepted: section J), after four later changes (section K: the security judgement of s4 t6, the owner's decision 39, the coder comparison of E2 and the owner's decision 40), after the second plan round of 2026-11-26 (proceed, 2 findings, both accepted: section L), after the owner's decisions of 2026-11-26 (section M), and after the re-review round of E3 (coai session `1089eaa8`, read at `c48b3a5c`, `proceed`; 3 findings accepted, 1 rejected: section N) with two owner decisions that came after it: a version of a CLI gates nothing, for any vendor (section M, entry 1.8), and the answer on the addenda 2 and 3 of the judgement (section O, entries 1.15 to 1.17). Sections J to O are in SPEC_CHANGES.md beside the spec. **The owner accepted the revised plan of epoch 3 on 2026-11-26 («план принят»), after a summary of the whole epoch that covered every ledger entry from 1.1 to 1.17 and both rejected findings. Epoch 1 of the ledger is closed with its seventeen entries (1.12 to 1.14 are E4's), the record "Re-review for epoch 2" is written (`## Plan changes` of v2/PLAN.md; narrowed: yes), and epoch 2 is open, approved by the owner, with no entry. The plan is not stopped. What is still open of the re-review: a judge sorts the code that was written under epoch 1 as fits or cut.**

Plan commit: `6125f2ce` on `feat/v2-ep3-e3-roles`: the owner's acceptance, epoch 2 open (before it: the re-review round read `c48b3a5c`, `ca0a3b7d` folded its findings in, `bafc871e` the owner's answer on the judgement's addenda). The commit that carries this bundle changes one wording after `6125f2ce` and nothing else of the plan: the measure branch is merged into this branch as `a9187e76`, so "not merged yet" left SPEC.md (C6), SPEC_CHANGES.md (section O), the ledger's note and s5 t2 d1. The 53 sections below are the files of that commit: they are the files of `6125f2ce` but for that one sentence of s5 t2 d1. The approval id of the scope `v2/epoch-3/e3-roles-complete` was NOT computed for this bundle: the planner does not build in a tree a coder builds in, and no one-shot mode prints the id. It is the id that `review_plan(featureFolder: "v2", workItem: "v2/epoch-3/e3-roles-complete")` records for that commit. No earlier id holds: packed files changed again (`v2/PLAN.md` and `SPEC.md` at `6125f2ce`; `v2/PLAN.md`, `SPEC.md` and one task file with this bundle), and by finding F1 the id of every scope moves once more when the pack format version is built. The pack holds the same 57 files; their sizes sum to 188428 bytes with this bundle (188382 at `6125f2ce`, 186991 at `bafc871e`, 175889 at `ca0a3b7d`, 167120 at `66bc4eea`, 158730 at `99fb13fd`, where the pack was 166321 bytes), so the pack stays far under 256 KiB. `SPEC.md` is in the pack (the `spec` file of `epic.yaml`) and is 64873 bytes of the 65536 a file of the pack may hold. This bundle and `SPEC_CHANGES.md` are not in the pack: a free `.md` file beside the nodes is on the line Not sent. `coai-mcp --validate-work v2` on these files: valid (5 epochs, 24 epics, 37 stories, 134 tasks; limits from the defaults).

| Story | Tasks | Complexity sum | Depends on |
|---|---|---|---|
| s1-scout-judge-contracts | 4 | 18 | — |
| s2-scout-judge-roles | 4 | 20 | s1-scout-judge-contracts |
| s3-plan-round-feature-folder (cornerstone) | 6 | 30 | — |
| s4-codex-agy-measure (cornerstone) | 6 | 29 | — |
| s5-codex-coder | 6 | 33 | s4-codex-agy-measure, s7-vendor-seam-rows |
| s6-agy-coder | 6 | 36 | s4-codex-agy-measure, s7-vendor-seam-rows |
| s7-vendor-seam-rows | 6 | 33 | s4-codex-agy-measure |
| s8-proof | 6 | 29 | s2-scout-judge-roles, s3-plan-round-feature-folder, s5-codex-coder, s6-agy-coder, s7-vendor-seam-rows |
| **Total** | 44 | 228 | |

## Spec digest (the full spec is SPEC.md beside this file; its section letters are used here and in the task files)

**Goal.** Scout and judge give strict JSON. A codex and an agy coder session is resumed for its fix turns. `review_plan(featureFolder)`
validates first and packs in topological order.

**Where the epic stands.** This digest says what the PLAN is; which task is built is in the task results and in git, and the
judge of the re-review sorts that work. At the second plan round: s1 and s2 (scout, judge) and s3 (the plan round over a folder) were built,
s4 (the codex and agy write boundary) was measured and judged, s5 to s8 were plan only. Since then code of s7 and s8 was
written on this branch, and the two measures s5 t6 and s6 t7 are done on `feat/v2-ep3-e3-measure`, with the addenda 2 and 3 of
the judgement (commit `d0dca16d`; that branch is merged into this one as `a9187e76`). Both verdicts are `ship`. Neither vendor is
launchable: the mark `launchable` of its row is off until the must-changes of its addendum are built and green (entry 1.16
below).

**The owner's decisions of 2026-11-26 — plan changes; they stopped the plan for a whole re-review, which the owner's acceptance ended** (`SPEC_CHANGES.md` section M; the
ledger is `## Plan changes` of `v2/PLAN.md`, entries 1.1 to 1.7, all with the source `owner`). The owner on counting them and
on reviewing epoch 3 whole: «да, пересматриваем».
(1.1) Decision 40 of 2026-11-25 counts as a change: a coder prompt for each vendor (C7).
(1.2) Caps: the paid proofs s8 t3 and s8 t4 together at most 30 USD; the measure s5 t6 at most 30 codex calls; the measure s6
t7 at most 30 agy turns. They replace 8 calls, 12 turns, 0.10 USD and 0.50 USD.
(1.3) The accepted risks agy A1 to A9 and codex C1 to C4 are accepted. «все 3 ии должны быть полноценными, и если нужно
работать как кодеры, ревьюверы, консультанты»: claude, codex and agy each ship as coder, reviewer and consultant, and the
codex and agy main-AI rows of s7 t4 are both filled (D below).
(1.4) The instruction step keeps its 64 KiB limit; the rule resolver gets a short form later, outside epoch 3.
(1.5) «секреты в выводе — не останавливают ничего. это нужно просто норм в интерфейсе в нотификациях отдельным табом
показывать, что б человек мог узнать про это, и сам решит, проблема это или нет»: a secret-shaped text in the step's output
stops nothing; a notice `instructions-secret` names the class and the line, never the value (s7 t3 d6, new); the tab of the
notifications is a new `doneWhen` line of epoch 4 `e4-local-pages`, not a task of E3 (epoch 3 has no extension epic and holds
5 epics; E3 holds 8 stories: the device limits).
(1.6) An outside dependency is in the pack text of EVERY scope, epoch and epic included («естественно добавлять, что б
проверка была полноценной»); whole-epic text ids move, approval ids stay (s3 t3 d5, new).
(1.7) E4, a coder's checks: the head's own definitions run, nothing the head changed is refused, every difference from the
base is reported (`definitionsChanged`, the changed definition files, the tests changed or deleted); the restore takes
packages from the internet itself («рестор пакетов пусть делает из интернета сам. прокси и адреса не нужны»; on tests and
helper scripts «разрешаем все»). E3's text agrees: C9 item 2, section G, risk 10. E4's own SPEC is on `feat/v2-ep3-e4-plan`.
Tasks: 44, none added, none removed; two new dod items (s3 t3 d5, s7 t3 d6); changed text in s5 t6 d5, s6 t7 (title, d7, d8,
d9), s7 t3 d5, s7 t4 d4, s8 t3 d2, s8 t4 d2. Sections J, K and L of the spec moved word for word into `SPEC_CHANGES.md`,
because a `spec` file may hold 64 KiB and SPEC.md with section M held more.

**After the re-review round of E3 (coai session `1089eaa8`, branch ref `plan/v2-ep3-e3-rereview`, read at `c48b3a5c`, verdict
`proceed`): four more ledger entries, 1.8 to 1.11** (`SPEC_CHANGES.md` sections M and N).
(1.8, the owner, after the round) "a new version must never stop anything, or rewrite docs, pins and so on. It came out, it
came out. We move on. We can write down for history which version the check was on." It holds for EVERY vendor: claude,
codex and agy. No version, release, hash or pin of a CLI gates, refuses, stops a job or forces a new measure, a new
judgement or a doc rewrite; what was measured is history, "measured on X"; a vendor ships by its verdict; the envelope names
the installed version beside the last measured one. Out of the plan: "only a measured release", the pin by bytes, the agy
program store (G9), the refusal after a self-update, accepted risk A6, the version floor of claude. What still refuses asks
the installed CLI what it DOES: a flag its help does not declare, the preflight rows, the launch checks, the turn checks.
New risk 13: a boundary is measured on one version and runs on every later one; a property of class `release` is assumed
there, not measured. One reading of the planner's: codex and agy still do not ship before the addendum of their open-rows
measure, because that is the judgement's condition and not a version (replaced the same day by the owner's condition, 1.16). s6 t7 runs on the version installed now (1.5.18)
inside 30 turns; a row that does not fit is recorded as not measured; d5 (G9) is dropped.
(1.9, reviewer, F0) The failure record of the instruction step redacts the argv and the last lines of standard error before
they are recorded or shown: a rule of records, not of the prompt (s7 t3 d7, new; V24).
(1.10, reviewer, F1) The approval id covers a pack format version, raised with the outside-dependency change; an approval
of an older format binds no coder job and the refusal asks for a new plan round. So the approval id of every scope moves
once, and "approval ids stay" of 1.6 no longer holds (s3 t4 d6, new; P13).
(1.11, reviewer, F3) The 30 USD cap of the paid proofs is enforced BEFORE each call: a call reserves its priced worst case
(the smallest model, a bound on its tokens, a price table written down first) and is not launched when that is more than
the budget left; the reservation is recorded beside the actual cost (s8 t3 d3 and s8 t4 d5, new).
Rejected: F2 (this repository has no instruction step until the resolver has a short form) — it is the owner's decision 1.4.
Tasks: still 44. New dod items: s3 t4 d6, s7 t1 d6, s7 t3 d7, s8 t3 d3, s8 t4 d5. To keep SPEC.md under 64 KiB, three records
of what is done moved word for word into `SPEC_CHANGES.md`: the help table of 2026-11-25, the unknowns of s4, the questions
to the owner with the answers of 2026-11-25.

**The owner's answer on the addenda 2 and 3 of the judgement (2026-11-26): ledger entries 1.15 to 1.17** (`SPEC_CHANGES.md`
section O, with the map of the judgement's lists 13.11 and 14.8 on the task files; the planner has the answer from the
lead, not in the owner's own words).
(1.15) The risks the addenda added are accepted: codex C5 (the model's JavaScript runs outside the vendor sandbox), C6 (no
version gate), C7 (an agent's work is not in the stream), C9 (a third program that can run commands as an "environment");
agy A10 (no version gate on a CLI that replaces itself daily), A11 (an expired copy opens a login prompt on the channel of
the job's prompt), A12 (coai's refresh unit holds the refresh token), A13 (the guard is tied to the stream by a step
index). C8 was closed by the measure. A9 is now real, not conditional. G13 is answered.
(1.16) codex and agy become launchable coders ONLY after the must-changes of the judgements are built and green — G15, the
exact rule of CX-T4, the `environments.toml` refusal, a refresh before the launch so that an expired copy never reaches a
start — "not before". Built as a mark, `launchable`, in the vendor's row: off for both, turned on by ONE commit (s5 t5 d4
for codex, s8 t6 d4 for agy) that names the green suite run it rests on. The history of measures makes nothing launchable.
This replaces the planner's reading under 1.8. s8 t4 now depends on s8 t6.
(1.17) The property and behaviour checks stay — an item type of the stream that nobody measured, a flag the help does not
declare, the preflight rows, the turn checks. One of them may stop a job after a CLI updated itself. That is accepted as
protection and is not a version gate.
The must-changes, by task. codex: the argv is CX1 and `-c agents.enabled=false` on every turn (s5 t2 d1); no
`environments.toml` in the job's `CODEX_HOME` before every launch (s5 t2 d2); the tool list with the job's own model, 11
names, and `codex doctor` (s5 t3 d3); the three signs of a lost sign-in and the second fixture (s5 t4 d2); the rule of
CX-T4 — `agent_message`, `command_execution`, `file_change` and `error` pass, an `mcp_tool_call` only for the two list tools
of the server `codex` with an empty list, a `web_search` and a `collab_tool_call` end the job, and an item of any other type
ends the job naming the type — and CX-T5, the rollout file (s5 t4 d3); the copy with a placeholder for the refresh token
(s7 t6). agy: the guard's record holds the conversation id, the step index, the tool and the decision (s6 t2 d3); G15 —
every tool event has exactly one record with the same three, `DONE` goes with `allow` and `deny` with `ERROR`, the counts
are equal, the tools are the three file tools — and the signs of AG-M2 (s6 t5 d3); the failover texts (s6 t5 d2); the
read-only guard folder and program folder (s6 t3 d2); the copy with the access token only, a fresh one for every turn, its
expiry at least now + `RuntimeMaxSec` + 5 minutes before EVERY launch or no launch, the refresh in a unit of its own and
only when the real file has no margin (s6 t3 d3, s7 t6). AG-M3 is not built: the init line says the same for every mode.
Tasks: still 44. New dod items: s5 t5 d4, s8 t6 d4. New scenarios V25 to V29.

**What changed since the gate of 2026-11-25** (section K, now in `SPEC_CHANGES.md`). (1) The security judgement of s4 t6: codex `ship`, agy `ship` with
a must-change list and accepted risks; four tasks added (s5 t6, s6 t7, s7 t5, s7 t6), one moved (s6 t6 → s8 t6). (2) The owner's
decision 39: agy is allowed as a coder. (3) The coder comparison of E2 (claude, codex and agy on one task): codex refused the
shipped coder prompt twice; agy's init line named another mode than its argv; codex wrote null members into its report. (4) The
owner's decision 40 (2026-11-25, «да, (а)»): a coder prompt for each vendor, the claude text unchanged. Causes 3 and 4 added no
task: every story of s5 to s8 holds 6 tasks, the device limit, so the new work is dod items of existing tasks.

**The second plan round (2026-11-26, `proceed`, coai session `26308fe5`, read at `6ec9a211`): 2 findings, both accepted and
folded in (section L, now in `SPEC_CHANGES.md`).** It said that no new round follows; the stop above ends that. (1) The prompt told a coder that coai runs required commands and nothing
made coai run them: now the instruction step, C9 below. (2) A changed context file outside the scope could pass the working
tree guard in silence, by the plan's words: now B item 6 names every packed file, and the new task s3 t6 tests the context.
Tasks: 44. In s7 the rows of `role-defaults.json` moved from t3 into t4, and t3 is now the instruction step.

**Decisions that change server.md.** (1) The role name is `judge`; `important` stays the name of its model list and of the router
rule. (2) Evidence is an object `{file, line, text}` that coai checks, not a string `file:line`. (3) A folder round has a scope: the
node `workItem` names inside the folder, else the whole feature; the approved thing is the scope and the context the reviewers read
with it. (4) Resume of a codex and an agy coder session is built in E3 for one task; E4 keeps the hardening. (5) A vendor's coder
ships only on a judgement verdict. (6) A version of a CLI gates nothing, for claude, codex and agy alike; what was measured is history (it replaces "only on a
release that was measured").

**A. Scout and judge (built).** Role cases of `start_role` on the BA's road: a read-only launch (Read, Glob, Grep), the tree compared
around every attempt, a strict reader, 2 repair turns at most in the same session, a declared refusal goes to the next model. Scout:
`{status, facts:[{claim, evidence:[{file, line, text}]}], files, unknown}`. Judge: `{status, verdict: approve|revise|reject, summary,
findings:[{id, severity: blocking|major|minor|nit, title, detail, evidence, fix}], decision}`; `approve` with a blocking finding and
`revise` or `reject` with no finding are errors. coai checks each evidence item against the file (the line holds the text) and builds
the envelope itself: an item that is not verified stays, marked `verified: false`. A path that leaves the repository or names a
credential-shaped file is an error and is never opened. An evidence text that is empty or only white space is an error before the
line check and can never be verified. The judge writes no file. Scenarios S1 to S12.

**B. `review_plan(featureFolder)` (built).** One input of three. The folder is read from git at the branch tip (`GitWorkFolder`, each
blob proved, links and submodules not followed). The WHOLE feature is validated with the device limits first; invalid means a refusal
with no reviewer launch, no round row and no stage change. The pack (`WorkPack`, pure): `feature.yaml`, the plan file, the ancestors
of the scope, then stories and tasks after what they depend on, ties by the number in the id; one line for each dependency outside
the scope, in the pack text of EVERY scope — an epoch, an epic and a story (the owner, 2026-11-26; until then the code wrote it
for a story scope only, so the text id of an epoch or epic pack with an outside dependency moves; the change raises the pack
format version, so the approval id of EVERY scope moves once — re-review finding F1). Each file is in a fence longer than any run of backticks in it. Limits: YAML file 64 KiB, folder 2 000 entries, pack 256
KiB; over a limit is a refusal, nothing is cut. When HEAD is the round's commit, EVERY file of the pack is compared with the working
tree — the scope files and the context outside the scope — and an edited, staged or deleted one is a refusal that names it, as is
an untracked `.yaml` under the scope (the guard takes its list from the pack; s3 t6 tests the context). What is approved is everything the reviewers read: `PlanSource` origin `folder` with an **approval id**,
SHA-256 over the pack format version (first line; one constant, raised by every change of what a pack's text or approval
lines hold) and over blob id and path of EVERY packed file (the scope files and the context outside the scope: `feature.yaml`, the plan
file, the ancestors) and over every outside-dependency line (node and title). A RESULTS file written later is not in the pack and
does not change it. A coder job is bound when its task is inside the scope and the approval id at its base equals the approved one;
a changed scope file, ancestor, plan file or dependency title refuses the start and names what differs. An approval made under an
older pack format binds no coder job: the binding computes the id at the round's own commit first, and when that is not the
approved id the refusal says that the format changed and a new plan round is needed, and names no path. The previous release reads
the new origin as "no source"; this is measured. Scenarios P1 to P13.

**C. The codex and agy coder.** The bar is the 15 measured rows of the claude boundary: B1 edit in the worktree works; B2 to B5 a
write to the main checkout, beside the worktree, to `.git`, to the real home is refused; B6, B7 a read and a search outside the
worktree are refused (for claude by flags only — the unit does not stop reads); B8 no write through a link; B9 no command leaves the
limit (claude has no shell); B10 the model has no network tool; B11 the model cannot read the sign-in; B12 the tool list is exact,
no MCP server; B13 files at the base commit do not run code; B14 a resumed turn has the same limit and a bound handle; B15 minimal
environment. A vendor ships only when EVERY row was measured as holding, fresh and resumed, by a CLI flag or by a property of the
unit. codex works through a shell and agy has command tools, so B6, B7, B9, B10 and B11 must come from the unit (hide, not only
read-only) or from the vendor's own sandbox seen working inside the unit.

The role refuses in three places. (a) One table, `shared/coder-boundary-releases.json`, filled only from the judgement: per vendor
the verdict, the ids of the properties the verdict relies on, each with its class, and the HISTORY of its measures (the release,
the date, the sha256 of the program file, the stream values a turn check compares): information, compared with nothing. The history
makes nothing launchable: a vendor with `ship` is launchable only when the mark `launchable` of its row is on (entry 1.16). (b) A write
capability per vendor that asks only what the installed CLI declares: every flag of the argv is in its help. The installed version
is recorded beside the measured one and gates nothing, for claude too; an older, a newer and an unnamed version are launched. (c) The job preflight with no model, inside the exact launch unit, before each job: one
row for EVERY id of class `pre`; a wrong answer refuses the job. The other classes: `launch` is asked by coai before every launch,
turn 0 and every fix turn; `turn` is read from the CLI's own stream after every turn, before anything of that turn is committed;
`release` cannot be asked without a model turn — it was measured on the release its history record names and is assumed on
another installed version (risk 13); it has no preflight row, because such a row would prove nothing. No setting makes a weaker
launch. Nobody has to measure a new version. When a measure is made, ONE commit writes the addendum's verdict and the history
record into the table; a test holds every record to its results heading and its verdict.

**The judgement (s4 t6, done; `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`).** codex 0.171.0: `ship`. agy 1.5.17: `ship`
by the owner's decision 39, with a must-change list (G1 to G14) and accepted risks (A1 to A9). The owner accepted A1 to A9 and
the codex risks C1 to C4 on 2026-11-26, so agy's verdict and history record enter the table on the addendum of s6 t7 with no further word of
the owner; only a risk an addendum adds or widens goes to the owner. A6 (the cost of the pin) fell away with the pin. The table started with NO history record for either vendor; one entered with the addendum that followed
the open-rows measure of s5 t6 (codex: a `spawn_agent` child, the JavaScript of `functions.exec`, a `CODEX_HOME` outside `/tmp`, late
`.codex` files, the refresh) or s6 t7 (agy: B13 on a resumed turn, the guard folder read-only, the stream beside the hook, the mode
of the init line, a sign-in copy with no refresh token, agy behind a proxy; the program from a read-only copy is dropped). Both measures are done and both addenda say `ship`
(sections 13 and 14 of the judgement at `d0dca16d`); the product still launches neither until its mark is on. What the judgement made of the launch: in the unit of codex and of agy the main checkout and the coder
root are HIDDEN, not read-only; a sign-in reaches a unit only as a COPY that coai makes for the job, deleted when the job ends, and
the commit search also looks for the copy's tokens (s7 t6); no CHECK unit sees the coder folder or another job's folder — a build
step with the host network could read the sign-in copy and rewrite the agy guard before this (measured; s7 t5); the judgement's pin
of the release by its bytes is NOT built (the owner, 2026-11-26); codex can never refresh in a job (a margin on the access token's `exp` before every launch, and a dead local
address as the refresh override); the agy guard is coai's own binary, closed by construction (an explicit decision for every
input), it denies `.git`, `.agents` and `.codex`, and its working directory is the guard folder, never the worktree; the turn check
of the seam reads every turn's own stream (thread or conversation id, tools outside the measured list) and ends the job
`coder-boundary` before anything of that turn is committed. NOT in E3: a network limit for the CLI process — no property of a user
unit limits the hosts of a process; a unit with no network and coai's forwarder is E4's `doneWhen` line 8, after s6 t7 d6.

Measure first (story s4, done, 13 unknowns U1 to U13): resume in a home of the job's own; `--output-schema`; the tool list of a
codex session; codex's sandbox inside the unit; hiding the main checkout except `.git`; the sign-in files and whether a child
command can read them; files of the repository at the base commit; agy's write and command tool names; what a failed hook handler
does; `--mode accept-edits` and `--sandbox`; the model lists. Credential (the owner, decisions 35 and 37): subscriptions only, as a
copy of the sign-in in a scratch home.

A refused vendor never blocks the other. s7 (the seam, the table, the fix loop through the seam, the rows, the check units, the
sign-in copies) depends only on s4 and is built in every case; s5 (codex) and s6 (agy) depend on s4 and s7, never on each other.
Per combination: ship/ship → failover claude → codex → agy, both resumed; ship/refuse → claude → codex, a start on agy refused;
refuse/ship → claude → agy, a start on codex refused; refuse/refuse → claude models only, no paid coder run. The failover test and
the route test run all four combinations with an injected table, and the fake CLI records zero launches of a refused vendor. A
vendor with the verdict `ship` whose mark `launchable` is off counts as refused; a vendor whose installed version is not the measured
one is shipped like any other.

The shape: codex turn 0 is `codex exec` with the measured set and `-c agents.enabled=false` in the worktree with `CODEX_HOME` in the job folder and the permission
profile in the job's `config.toml`, written and read back before every turn; a fix turn is `codex exec resume <thread of turn 0>`.
agy turn 0 runs the coder agent behind the write guard with `--mode accept-edits` and `--agent coai-coder` on every argv; a fix
turn adds `--conversation <id of turn 0>`. The handle carries the vendor (the seam is story s7). A lost session is a crash: the next
model starts from the last coai commit. Never in an argv: codex `--dangerously-bypass-approvals-and-sandbox`,
`-s danger-full-access`, `--trust`, `--ephemeral`; agy `--dangerously-skip-permissions`. Scenarios V1 to V29 (V6 now: an
installed version other than the measured one is launched, for each of the three vendors, with both versions in the envelope).

**C7. The coder prompt of each vendor (new; the owner's decision 40).** The evidence: on one task and base codex `gpt-6.1-terra`
answered `refused` twice on the shipped prompt — first because `coder.md` names claude's tools and says "You have no shell", then
(with the tools bullet in its own words) because `AGENTS.md` of this repository orders a command, the rule resolver, before any
edit and the prompt forbids commands; with one more bullet about such commands it was green with 0 fix turns. agy was green on the
shipped prompt; claude too. The decision («да, (а)»): a version of the prompt for each vendor — the vendor's own tool names, and a
line that commands which the repository's instruction files order first are not run in the job, because coai runs the checks; the
claude text stays exactly as it is. The shape: `coder.md` (claude, unchanged: a test holds its sha256), `coder.codex.md`,
`coder.agy.md`; the seam names the prompt and a vendor with no prompt of its own cannot be launched; a vendor's file differs from
`coder.md` in exactly the tools bullet and ONE added bullet, and a test holds every other line byte-equal; the tools bullet names
only tools of the vendor's measured list; the added bullet says only what is true (the model does not run such a command, that is
no reason to refuse, and what coai ran before the turn is at the end of the prompt) and is narrow — it lifts no rule about the
code, the paths or secrets and gives the model no command; the fix and repair prompts go the same road (`CoderPrompt.Fix` says "you have no shell" today, which is
false for codex); an override a person wrote is per vendor; the envelope names the prompt id and the sha256 of the rendered prompt.
The line is not a boundary: the unit, the vendor sandbox and the guard hold. Tasks: s7 t1 d1, s7 t2 d5, s5 t4 d4, s6 t3 d6, s8 t2
d2, s8 t4 d4.

**C8. What the comparison does not prove, and the agy mode (new).** A harness ran codex and agy in the unit shape of the s4
measure; the product ran neither. Still to show, each with its task: the product's own unit and preflight rows (s5 t3, s6 t4); the
check units after s7 t5; a fix turn, a resumed session and the guard in a red round (s5 t4, s6 t5; s8 t4 with a planted red check);
coai's own guard handler (s6 t2, s6 t4); the report through the strict reader — codex wrote `"reason":null,"notes":null`, so only
a null of a member the status does not use is read as absent (s5 t2 d2); the vendor's prompt as the product renders it, on a
repository whose instruction file orders a command (s8 t4 d4). The agy mode: the init line said `permission_mode: request-review`
with `--mode accept-edits` on the argv, and the s4 measure saw `--mode auto` fall back to the default mode, where a command RAN.
The rule: the argv is a request and proves no mode. Measured by s6 t7 d9 on agy 1.5.18: the init line says `request-review` for
every mode, so it is not evidence and AG-M3 is not built. The mode is a property of class `release` (AG-M4: with `accept-edits` a
command is denied, with no `--mode` it RAN); row B9 rests for a job on the agent file and the guard, and the signs of AG-M2 (a
mode warning on stderr, a `denied_actions` that is not empty, the permission line) are read after every turn. That is accepted
risk A9, now real and not conditional; the owner accepted it. No launch is passed on the flag alone.

**C9. The instruction step (new; second plan round).** A coder of coai runs no command of its own, so a command that a
repository's instruction file orders before an edit is run by coai or by nobody, and the coder is told which. `.coai/toolchain.yaml`
gets ONE optional top-level key, `instructions` (`argv`; `perFile`, appended once for each file of the task; `timeoutSeconds`),
read at the base commit (the step runs before any model, so the worktree is the base; the checks after a turn are E4's and
run the head's own definitions). coai never finds a command by reading prose. coai runs the step once for a job, before any model, in a
check unit of E2 (no network, no credential, the tree compared before and after); the output is standard output, UTF-8, at most 64
KiB, exit code 0. A declared step that cannot run (non-zero exit, timeout, over the cap, not UTF-8, a changed tree, no unit) ends
the job `instructions-failed` with zero model launches; nothing is cut. The record of such a failure keeps the argv and the last
lines of standard error, and both go through the redactor before they are written to the envelope, the job's record or a log
line, as the argv does wherever else it is recorded (re-review finding F0; a rule of records — the prompt is another matter). The output is appended to the turn-0 prompt of EVERY vendor,
claude too, as one block that coai writes after the rendered prompt — not a slot of a prompt file, so `coder.md` and its sha test
do not change; what a claude coder is sent changes only on a repository that declares a step. With no step a codex and an agy
coder get a block that says nothing was run, and the envelope says `declared: false`. This repository: the resolver's payload
for the files of a task was measured at 334 362 bytes; the owner keeps the limit at 64 KiB (2026-11-26), so this repository
declares NO step, a test holds that, and a short form of the resolver is a follow-up outside epoch 3 (the resolver is the
tool of the conventions repository). A secret-shaped text in the output stops nothing (the owner, 2026-11-26): the job goes
on, the output reaches the prompt as it was, and coai records the finding — a notice `instructions-secret` whose subject is
the job, the member `secrets [{class, line}]` of `instructions` in the envelope, one log line — with the class and the line
and never the value; this replaces must-change M1 of the security verify of s7 t3, which ended such a job; the tab that shows
the findings is epoch 4 `e4-local-pages`. Scenarios V20 to V24. Tasks: s7 t3 (d6 is the secret finding, d7 the redacted failure
record), s7 t2 d5 and d6, s5
t4 d4, s6 t3 d6, s8 t2 d2, s8 t4 d4 (the paid proof needs a commit that follows a rule only the step's output holds).

**D. Failover.** The E1 rule is unchanged. The lists the owner accepted (decision 35): scout sonnet → haiku → codex `terra`;
important fable → codex `astra` (no change); ba fable → opus (no change); coder the band model, then sonnet → opus → codex `terra` →
agy `gemini-4-argon-medium` (another vendor only with its row and a shipped write launch). The codex and agy main-AI rows are
BOTH filled (the owner, 2026-11-26: all three vendors are full ones — coder, reviewer, consultant; the reviewer and the
consultant of all three exist since before v2, E3 adds the coder of codex and agy and the two rows). The owner's word names
no model; the names are the planner's proposal for this review, from `RESULTS_model_lists.md` only, and a person's
`COAI_ROLES_MODELS` and `COAI_ROLES_BANDS` go over them. A row starts with its own vendor and fails over to the other two
(an entry of another vendor only with its row; a coder entry only while the table ships its vendor). Main AI codex:
orchestrator `astra`; coder `terra` → `astra` → claude `sonnet` → agy `gemini-4-argon-medium`; scout `terra` → claude
`sonnet`; important `astra` → claude `fable`; ba `astra` → claude `fable`; bands 0–2 `gpt-6-luna`, 3–6 `terra`, 7–10 `astra`.
Main AI agy: orchestrator `gemini-4-argon-high`; coder `gemini-4-argon-medium` → `gemini-4-argon-high` → claude `sonnet` →
codex `terra`; scout `gemini-3.8-flash-medium` → `gemini-4-argon-medium` → claude `sonnet`; important `gemini-4-argon-high` →
claude `fable` → codex `astra`; ba `gemini-4-argon-high` → claude `fable`; bands 0–2 `gemini-3.8-flash-low`, 3–6
`gemini-4-argon-medium`, 7–10 `gemini-4-argon-high`. agy prints no size and no price: its Neon, Carbon, Hydrogen and
Orchestrator names are not used, because nothing measured says what they are.

**E. Security rules of every new launch road.** No secret in an argv or a log (a copy, a file bind or a variable NAME; a canary
searched in every recorded argv). A boundary is measured and refused when not effective. Tests never touch the real home. Exe and
argv with a timeout that kills the tree. One table decides the vendor, with a scan test. A model's words are data: paths are checked
before a file is opened.

**F. Growth.** New since the gate: the history of measures in the table (one small record for each measure that is made); the agy
program store is NOT built (no pin since 2026-11-26); the sign-in copy of a job (deleted when the job ends and by the E2 sweep); the
guard's decision records (with the job folder).

**Order.** s1 → s2; s3 alone; s4 → s7 → s5 and s6 side by side; s8 last. The story numbers are not the order. In s5 the measure t6
comes first; in s6 the measure t7. The key pool of E2 is merged into this branch.

**Not in E3.** The formatter turn, test selection, flaky re-run, the no-progress stop, detection and other stacks (E4); what a
coder may change of its own checks (E4: reported, never refused; E3 builds no gate there); the network forwarder (E4,
`doneWhen` line 8); parallel work (E5); the short form of the rule resolver (a follow-up; the conventions repository); the
tab of the notifications for secret findings (epoch 4 `e4-local-pages`); a pool of codex or agy credentials; a coder on native
Windows or macOS; `review_feature(featureFolder)`; a document written by the judge; the extension showing a `folder` source.

**Risks added on 2026-11-26** (SPEC section H). Risk 12: a secret in the output of the instruction step reaches the vendor of
the model; coai tells the person the class and the line and the person decides. Risk 10, wider: a green check means that the
head's OWN definitions passed, for a coder of every vendor; the envelope says when a definition changed. Risk 13: a boundary is
measured on one version and runs on every later one; after an update nothing is measured again, and what holds a job is what coai
can ask the installed CLI. Risk 7 (an update refuses the vendor) is gone, and accepted risk A6 fell away. Risk 9 now holds every accepted risk of the
judgement and its addenda: agy A1 to A13 without A6, codex C1 to C9 without C8, which the measure closed.

**For the owner.** Answered: the credential (decisions 35, 37), agy as a coder (39), the scout and coder lists (35), the prompt of
each vendor (40; its line was reworded after the second round), and on 2026-11-26 the caps, the accepted risks, the main-AI
rows, the instruction step, the pack, the checks of E4 and the rule that a version gates nothing (above). On the addenda of the judgement: the new
risks accepted, the launch condition, the checks that stay (entries 1.15 to 1.17). Nothing of E3 waits for
the owner now. What can still come: a risk a later addendum adds. For the
whole review, not the owner's words: the names of the two main-AI rows; that codex and agy do not ship before the addendum of their
open-rows measure (the planner's reading of entry 1.8); and whether «прокси и адреса не нужны», said of the
restore step, also means the forwarder of a coder unit (judgement G11) and the measure s6 t7 d6 — they are unchanged here.

## v2/epoch-3/e3-roles-complete/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e3-roles-complete
title: "Roles complete: scout, important/judge, codex + agy rows, review_plan(featureFolder)"
half: server
dependsOn:
  - e1-role-jobs-ba
  - e2-coder-one-task
doneWhen:
  - "scout and judge strict JSON outputs"
  - "codex and agy coder sessions resumed"
  - "review_plan(featureFolder) validates first, packs in topological order"
spec: SPEC.md
```

## v2/epoch-3/e3-roles-complete/stories/s1-scout-judge-contracts/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-scout-judge-contracts
title: "Scout and judge answer strict JSON: two schemas with their strict readers, one evidence check, two prompts"
dependsOn: []
```

## v2/epoch-3/e3-roles-complete/stories/s1-scout-judge-contracts/tasks/t1-evidence-check/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-evidence-check
title: "The evidence check: an item {file, line, text} of a scout or judge answer is compared with the file; a path that leaves the repository and a blank text are errors before any file is opened"
kind: security
complexity: 5
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/core/Roles/RoleEvidence.cs
  - src_mcp/src/Server/RoleJobs/RoleEvidenceFiles.cs
  - src_mcp/tests/ARoleEvidenceIsCheckedTests.cs
dod:
  - id: d1
    item: "an item is verified when the file is in the tree the job read and that line holds the text after white space is collapsed; a line out of range, a file that is not there, a file over 2 MiB or not UTF-8, and a text that is not on the line each give verified false with a reason"
  - id: d2
    item: "S12: a text that is empty or only white space (spaces, tabs, line ends, the other Unicode space characters) is an error of the answer, decided BEFORE any line is read; a test gives such a text for a blank line, for a line of spaces, for a line out of range and for a file that is not there, and no case answers verified true"
  - id: d3
    item: "S5: a path that is absolute, has a drive or `..`, runs through a link, or is a credential-shaped file (the existing rule of CredentialFiles) is an error of the answer and the file is never opened; files are read through RealPath only"
  - id: d4
    item: "the pure part has no IO (lines come through an interface) and core stays IsAotCompatible"
```

## v2/epoch-3/e3-roles-complete/stories/s1-scout-judge-contracts/tasks/t2-scout-contract/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-scout-contract
title: "The scout contract: shared/scout-output.schema.json and its strict reader (facts with evidence, files, unknown, the refused arm)"
kind: code
complexity: 4
dependsOn:
  - t1-evidence-check
parallel: true
files:
  - shared/scout-output.schema.json
  - src_mcp/core/Roles/ScoutOutput.cs
  - src_mcp/tests/AScoutContractTests.cs
dod:
  - id: d1
    item: "the reader accepts {status: ok, facts:[{claim, evidence:[{file, line, text}]}], files, unknown} and {status: refused, reason}; an unknown member, a fact with no evidence, more than 40 facts, 5 evidence items, 60 files or 20 unknown lines, and a text over its length are errors that name the member"
  - id: d2
    item: "S12: an evidence text that is empty or only white space is an error of the reader too, with the member named; a claim and an unknown line that are only white space are errors as well"
  - id: d3
    item: "a test holds the schema file and the tables of the reader equal; the contract tests of the ba and the coder pass unchanged"
```

## v2/epoch-3/e3-roles-complete/stories/s1-scout-judge-contracts/tasks/t3-judge-contract/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-judge-contract
title: "The judge contract: shared/judge-output.schema.json and its strict reader (verdict, summary, findings with the four severity words of the gate, decision, the refused arm)"
kind: code
complexity: 5
dependsOn:
  - t1-evidence-check
parallel: true
files:
  - shared/judge-output.schema.json
  - src_mcp/core/Roles/JudgeOutput.cs
  - src_mcp/tests/AJudgeContractTests.cs
dod:
  - id: d1
    item: "the reader accepts {status: ok, verdict: approve|revise|reject, summary, findings:[{id, severity, title, detail, evidence, fix}], decision} and {status: refused, reason}; severity is one of blocking, major, minor, nit; an unknown member and a text over its length are errors that name the member"
  - id: d2
    item: "S8: approve with a blocking finding, revise or reject with no finding, and a finding id used twice are errors with a sentence; S12: an evidence text that is empty or only white space is an error of the reader"
  - id: d3
    item: "a test holds the schema file and the tables of the reader equal"
```

## v2/epoch-3/e3-roles-complete/stories/s1-scout-judge-contracts/tasks/t4-prompts/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-prompts
title: "The scout prompt and the judge prompt: the brief, the output shape with its limits, the read-only rule; an override is used first"
kind: code
complexity: 4
dependsOn:
  - t2-scout-contract
  - t3-judge-contract
parallel: false
files:
  - src_mcp/src/Roles/prompts/scout.md
  - src_mcp/src/Roles/prompts/judge.md
  - src_mcp/src/Server/RoleJobs/ScoutPrompt.cs
  - src_mcp/src/Server/RoleJobs/JudgePrompt.cs
  - src_mcp/tests/AScoutAndJudgePromptTests.cs
dod:
  - id: d1
    item: "each built prompt holds the brief, the path of taskFile when one was given, the output shape, the rule that the role writes nothing and that an evidence text is copied from the file and is never blank, and the refused arm; an override in RolePrompts is used first"
  - id: d2
    item: "a changed limit shows in the built prompt: the numbers come from the tables of the reader, not from the prompt file"
```

## v2/epoch-3/e3-roles-complete/stories/s2-scout-judge-roles/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-scout-judge-roles
title: "scout and judge are role cases of start_role: a read-only launch, repair in the same session, an envelope that coai builds, the judge on the important list"
dependsOn:
  - s1-scout-judge-contracts
```

## v2/epoch-3/e3-roles-complete/stories/s2-scout-judge-roles/tasks/t1-scout-work/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-scout-work
title: "ScoutWork: the request check, the launch on the scout list, repair turns, the envelope with checked evidence; the descriptor is registered"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/ScoutWork.cs
  - src_mcp/src/Server/RoleJobs/RoleDescriptors.cs
  - src_mcp/tests_fakecli/ModelMap.cs
  - src_mcp/tests/AScoutRunTests.cs
dod:
  - id: d1
    item: "S1 to S3 with FakeCli: valid at turn 0 with the read-only argv; one repair turn in the same session (the handle of turn 0 is in the recorded argv); wrong three times ends failed output-invalid with no result and every answer kept under attempts"
  - id: d2
    item: "S4: result.json is built by coai and holds each evidence item with verified true or false and a reason, each file with exists, the counts, repairTurns and the HEAD commit of the checkout at the start; an item that is not verified is never shown as verified and never dropped; every stored text passes the Redaction rule"
  - id: d3
    item: "S11: an empty brief is refused before any folder is made; the route is the scout list of the roles table"
```

## v2/epoch-3/e3-roles-complete/stories/s2-scout-judge-roles/tasks/t2-judge-work/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-judge-work
title: "JudgeWork: the launch on the important list, the three rules of the judge as repair turns, the envelope; a named model outside the important list is refused"
kind: code
complexity: 6
dependsOn:
  - t1-scout-work
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/JudgeWork.cs
  - src_mcp/src/Server/RoleJobs/JudgeRoute.cs
  - src_mcp/src/Server/RoleJobs/RoleRoute.cs
  - src_mcp/src/Server/RoleJobs/RoleDescriptors.cs
  - shared/role-route-vectors.json
  - src_mcp/tests/AJudgeRunTests.cs
dod:
  - id: d1
    item: "with FakeCli a judge job ends succeeded with verdict, summary, findings (their evidence checked as the scout checks it) and decision when the brief asked to choose; an error of S8 goes back as a repair turn, 2 at most, then output-invalid"
  - id: d2
    item: "S9: the route of the judge is the important list; a model the caller named that is not in that list is refused before any folder with a sentence that names COAI_ROLES_MODELS; the new route vectors pass"
  - id: d3
    item: "the judge writes no file: a test compares the tree before and after a job"
```

## v2/epoch-3/e3-roles-complete/stories/s2-scout-judge-roles/tasks/t3-role-tool-text/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-role-tool-text
title: "start_role names scout and judge: the tool description, the refusal list of roles, role_status shows the envelope"
kind: code
complexity: 3
dependsOn:
  - t2-judge-work
parallel: false
files:
  - src_mcp/src/Tools.cs
  - src_mcp/src/Server/RoleJobs/RoleJobService.cs
  - src_mcp/src/Server/RoleJobs/RoleJobAnswers.cs
  - src_mcp/tests/ARoleToolTextNamesScoutAndJudgeTests.cs
dod:
  - id: d1
    item: "over real stdio the description of start_role names scout and judge, what each takes (brief, optional taskFile) and what each answers; the refusal of an unknown role lists them; there is no new tool"
  - id: d2
    item: "role_status returns the same envelope JSON that result.json holds"
```

## v2/epoch-3/e3-roles-complete/stories/s2-scout-judge-roles/tasks/t4-read-only-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-read-only-scenarios
title: "Scenarios of the read-only roles with FakeCli: a declared refusal, an exhausted list, a tree that changes, a codex row and an agy row"
kind: test
complexity: 5
dependsOn:
  - t3-role-tool-text
parallel: false
files:
  - src_mcp/tests/AScoutAndJudgeScenariosTests.cs
dod:
  - id: d1
    item: "S6 and S7: a declared refusal goes to the next model with one switch line and one notice; an exhausted list ends failed with every attempt named; a tree that changes during an attempt ends tree-changed with no failover and no answer"
  - id: d2
    item: "S10: with a codex row and an agy row as scout and as judge the recorded argv is the read-only one of each CLI and the read-only folder of agy is prepared; a test proves that no scout or judge launch can carry the coder case of LaunchConfinement"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-plan-round-feature-folder
title: "review_plan(featureFolder): read from git at the branch tip, validate first, pack in topological order, record what was approved"
cornerstone: true
dependsOn: []
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t1-git-work-folder/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-git-work-folder
title: "GitWorkFolder: the work-plan folder of a commit through IWorkFolder (one ls-tree, each blob proved against its id; a link and a submodule are not followed)"
kind: code
complexity: 5
dependsOn: []
parallel: true
files:
  - src_mcp/runners/Collecting/GitWorkFolder.cs
  - src_mcp/runners/Collecting/GitHistory.cs
  - src_mcp/tests/AFeatureFolderIsReadFromGitTests.cs
dod:
  - id: d1
    item: "the validator over GitWorkFolder at a commit gives the same answer, byte for byte, as over a clean checkout of that commit (fixture repositories in a temp folder; the v2 folder of this repository at HEAD is one case); an edited, a staged and an untracked file of the working tree do not change it"
  - id: d2
    item: "P8: a link and a submodule entry inside the folder are not followed; every blob is proved against its id as the committed-plan reader proves one; a folder over 2000 entries and a file over 64 KiB are answered as errors and are never read whole"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t2-validate-first/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-validate-first
title: "featureFolder as the third plan input: one input only, the tip resolved once, the whole feature validated with the device limits before any round"
kind: code
complexity: 5
dependsOn:
  - t1-git-work-folder
parallel: false
files:
  - src_mcp/src/Server/Stages/PlanInput.cs
  - src_mcp/src/Server/Stages/FeatureFolderPlan.cs
  - src_mcp/tests/AFeatureFolderPlanIsValidatedFirstTests.cs
dod:
  - id: d1
    item: "P3: exactly one of planText, committedPlan and featureFolder is accepted and each wrong pair is refused with its own sentence; expectedCommit works with featureFolder as with committedPlan; the branch tip is resolved once and is the commit of the round"
  - id: d2
    item: "P2 and P12: an invalid folder is refused with 10 errors at most (code, node, message), the total and the source of the limits; the validator runs with the device limits; nothing is launched and nothing is written"
  - id: d3
    item: "P11: the whole preparation stays inside the 45-second deadline of PlanInputs, and a git that does not answer is a refusal before any round"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t3-topological-pack/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-topological-pack
title: "WorkPack: the pack of a scope in topological order, with its limits, as a pure function with vectors"
kind: code
complexity: 6
dependsOn: []
parallel: true
files:
  - src_mcp/core/Work/WorkPack.cs
  - shared/work-pack-vectors.json
  - src_mcp/tests/AWorkPackIsTopologicalTests.cs
dod:
  - id: d1
    item: "P1 and P7 from the vectors: feature.yaml, the plan file, the ancestors of the scope, then stories and tasks so that every node comes after the nodes it depends on, ties by the number in the id; the same input gives the same bytes; no IO and no clock"
  - id: d2
    item: "P6: a scope that is an epoch, an epic or a story holds the YAML of its ancestors, its own subtree and one line (node and title) for each node outside it that a scope node depends on; a scope that names no node of the folder is refused"
  - id: d3
    item: "P5: a pack over 256 KiB is refused with its size and its three largest sections and is never cut; each file is in a fence one backtick longer than the longest run in it, so a file cannot end its own section; the line Not sent names the architecture files and the free md files with their sizes"
  - id: d4
    item: "the pack also answers its approval lines: one line with the blob id and the path for EVERY file whose text is in the pack (the scope files, and feature.yaml, the plan file, the YAML of the ancestors and a spec file outside the scope), and one line with the node and the title for every outside dependency as the pack shows it, in pack order"
  - id: d5
    item: "the owner's decision of 2026-11-26 (SPEC section B items 3 and 4; it ends the story-only rule of the code checkpoint of 2026-11-26): the line of the outside dependencies is in the pack TEXT of EVERY scope that has one - an epoch, an epic and a story - in the words of the D lines of the approval; a vector for each of the three kinds of scope, and one for a scope with no outside dependency, whose text has no such line; a test builds the pack of an epoch scope and of an epic scope with and without the line and shows that the D lines of the approval are the same (the dependency was approved before and is now also read) and that the text id is not; the change raises the pack format version (re-review round of 2026-11-26, finding F1: t4 d6), so the approval id of every scope moves once with it; the task result lists, for every epoch and epic scope of the v2 folder of this repository, whether its text id moved"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t4-plan-identity-binding/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-plan-identity-binding
title: "What a folder round approves: PlanSource origin folder with an approval id over everything the reviewers read (the scope and the context outside it), and the plan binding of a coder job to it"
kind: security
complexity: 6
dependsOn:
  - t2-validate-first
  - t3-topological-pack
parallel: false
security: true
files:
  - src_mcp/core/Rounds/PlanSource.cs
  - src_mcp/src/Server/RoleJobs/CoderPlanBinding.cs
  - src_mcp/tests/APlanFolderRoundIsTheApprovedThingTests.cs
dod:
  - id: d1
    item: "a folder round records the origin folder, the scope path, the commit, the approval id (SHA-256 over the approval lines of the pack: every packed file of the scope AND of the context outside it, and every outside-dependency line) and the id of the pack text; IsWellFormed accepts that shape and no other new one"
  - id: d2
    item: "P9: a coder job started under a folder round is bound when its task file is inside the scope, its base descends from the commit of the round and the approval id computed at the base equals the approved one; a task outside the scope is refused with a sentence of its own"
  - id: d3
    item: "P9: after the approval a change of a scope file, of feature.yaml, of the plan file, of the YAML of an ancestor, and of the id or the title of an outside dependency EACH refuses a coder start (one test for each), and the sentence names the first path or node that differs, found by comparing the lines at the commit of the round with the lines at the base"
  - id: d4
    item: "P9: a new RESULTS file inside the scope, a file the pack did not hold, and a change of a node outside the scope that no scope node depends on do not break the binding"
  - id: d5
    item: "P10: the rule of the previous release (IsWellFormed before this task, and the reviewedFrom rule of the extension) reads a folder source as no source and no error; the binding tests of a committed plan and of a pasted plan pass unchanged"
  - id: d6
    item: "P13, the re-review round of 2026-11-26, finding F1 (SPEC section B items 7 and 8): the approval id is versioned with the pack format; the first approval line is V with the pack format version, ONE constant beside the pack code, and a test fails when the text of a pack or its approval lines change while the constant does not (golden vectors pinned to the version); the constant is raised in the same change that puts the line of the outside dependencies into the text of every scope (t3 d5); an approval made under an older format binds no coder job: the binding computes the id at the commit of the round first, and when that is not the approved id the start is refused with a sentence that says the pack format changed since the approval and that a new plan round is needed, and that names no path; test rows: an approval id made without the V line and one made with a lower version are each refused with that sentence, the same scope approved under the current version binds, and a changed file still gets the sentence of d3 that names the path"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t5-tool-and-round-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-tool-and-round-scenarios
title: "review_plan takes featureFolder: the tool text, the working tree guard, and the round scenarios over real stdio with FakeCli reviewers"
kind: code
complexity: 5
dependsOn:
  - t4-plan-identity-binding
parallel: false
files:
  - src_mcp/src/Tools.cs
  - src_mcp/src/Server/PanelService.cs
  - src_mcp/tests/APlanRoundOverAFeatureFolderTests.cs
dod:
  - id: d1
    item: "over real stdio with FakeCli reviewers: review_plan with featureFolder on a valid fixture launches the reviewers with the pack and its source line in the prompt; the description of review_plan names featureFolder and the scope by workItem"
  - id: d2
    item: "P2 end to end: an invalid folder gives a refusal, zero reviewer launches, no new round row, the same stage and round count, and no worktree"
  - id: d3
    item: "P4: with HEAD at the commit of the round an edited, a staged or an untracked plan file under the scope is refused and the files are named; with HEAD at another commit the note says that the working tree was not compared"
```

## v2/epoch-3/e3-roles-complete/stories/s3-plan-round-feature-folder/tasks/t6-working-tree-guard-context/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-working-tree-guard-context
title: "The working tree guard of a folder round compares every packed file: the scope and the context outside it, each named in the refusal"
kind: test
complexity: 3
dependsOn:
  - t5-tool-and-round-scenarios
parallel: false
files:
  - src_mcp/src/Server/Stages/FeatureFolderPlan.cs
  - src_mcp/tests/APlanRoundOverAFeatureFolderTests.cs
  - research/module_server.md
dod:
  - id: d1
    item: "P4, second plan round finding 2: with HEAD at the commit of the round and a scope that is ONE story, an edit of feature.yaml, of the plan file, of epoch.yaml, of the YAML of the epic and of its spec file EACH refuses the round before any reviewer is launched, and the sentence names the file (one test for each of the five); the same for a staged change and for a deleted file of the context (one test each)"
  - id: d2
    item: "the guard takes its list from the pack: a test builds the pack of a fixture and fails when a packed path is not in the compared set, so a file that enters the pack later is compared without a change of the guard; a file the reviewers do not read (a RESULTS file, a free .md, a node outside the scope that no scope node depends on) is still no reason to refuse"
  - id: d3
    item: "the refusal says file(s) the reviewers read, not file(s) of the scope, and names at most ten; the task result says for each test of d1 whether it was red before a change of the code (the code of t5 compares the packed paths already); research/module_server.md says what is compared"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-codex-agy-measure
title: "Measure first: what codex and agy give for resume and for a write limit, each row of the claude boundary tried on both, then the security judgement"
cornerstone: true
dependsOn: []
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t1-model-lists/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-model-lists
title: "Measure the model names of the installed codex and agy as the CLIs print them, and name the smallest model of each"
kind: measure
complexity: 3
dependsOn: []
parallel: true
files:
  - v2/epoch-3/e3-roles-complete/RESULTS_model_lists.md
dod:
  - id: d1
    item: "the file holds, for the installed codex and agy with their versions, every model name as the CLI itself printed it (agy models; for codex the place each name was seen), and names the smallest model of each vendor, which the measure and proof tasks use"
  - id: d2
    item: "no model is called; a name that only a web page shows is not in the file"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t2-codex-resume-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-codex-resume-measure
title: "Measure codex exec resume: the session in a CODEX_HOME of its own, the argv that keeps the write limit, --output-schema, the text of a lost session"
kind: measure
complexity: 4
dependsOn:
  - t1-model-lists
parallel: false
security: true
files:
  - v2/spikes/RESULTS_codex_coder.md
  - src_mcp/tests/fixtures/role-stops/codex-resume-no-session.json
dod:
  - id: d1
    item: "U1: with the smallest model, 6 paid calls at most and the spend written down: a thread started by codex exec in a scratch worktree with a CODEX_HOME of its own is resumed by exec resume with the id from the stream and returns a number planted in turn 1; the same is tried from another folder and with another CODEX_HOME, and each answer is recorded"
  - id: d2
    item: "U2: recorded: where the session file is, the argv of a resumed turn that keeps the write limit, whether --output-schema takes shared/coder-report.schema.json on exec and on exec resume, and the text of a resume that finds no session (saved as the fixture)"
  - id: d3
    item: "nothing under the real home is written: the bytes and the modification time of the real sign-in file are the same before and after; the credential used is the one the owner named (SPEC section H, question 1)"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t3-codex-write-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-codex-write-rows
title: "Measure the rows B1 to B15 of the claude boundary on codex, fresh and resumed, with its own sandbox inside the coder unit"
kind: measure
complexity: 6
dependsOn:
  - t2-codex-resume-measure
parallel: false
security: true
files:
  - v2/spikes/RESULTS_codex_coder.md
dod:
  - id: d1
    item: "under a heading with the release: one row for each flag set, and for each of B1 to B15 of SPEC section C1 the measured answer, fresh and resumed, with the hash of each canary before and after; 16 paid calls at most and the spend written down"
  - id: d2
    item: "U3 and U4, with no model: the tools a session has and the switches that leave the shell and the patch tool only; the sandbox of codex started INSIDE the property set of the coder unit (codex sandbox with a probe: a write outside, a network call), and what the CLI does when that sandbox cannot start"
  - id: d3
    item: "U5 to U7 and U13, inside the unit: the main checkout hidden except its .git, the worktree of another job hidden, the sign-in file and /proc/<cli pid>/environ tried from a child command, a network limit to the hosts of the vendor; files at the base commit (AGENTS.md, a .codex folder, a rules file, a hook) with a canary outside"
  - id: d4
    item: "the file names ONE flag set and the additions of the unit to ship, or says REFUSE and names the row that did not hold"
  - id: d5
    item: "the steps, the canaries and the fixture folders are written as a procedure, so that a later release is measured the same way (SPEC section C2, how the list of releases is updated)"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t4-agy-resume-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-agy-resume-measure
title: "Measure agy --conversation: the conversation in a home of its own, the usage of turn 2, the files of the sign-in, the text of a lost conversation"
kind: measure
complexity: 4
dependsOn:
  - t1-model-lists
parallel: false
security: true
files:
  - v2/spikes/RESULTS_agy_coder.md
  - src_mcp/tests/fixtures/role-stops/agy-conversation-not-found.json
dod:
  - id: d1
    item: "U11: with the smallest model, 6 paid calls at most and the spend written down: a conversation started in a scratch folder with a HOME of its own is continued by --conversation with the id from the stream and returns a number planted in turn 1; the same from another folder; where agy keeps the conversation; whether the usage of turn 2 is the sum; the text of a conversation that is not found (saved as the fixture)"
  - id: d2
    item: "U6: which files of the real home agy needs to be signed in, found with an empty home plus one file at a time; nothing under the real home is written (bytes and modification time the same before and after)"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t5-agy-write-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-agy-write-rows
title: "Measure the rows B1 to B15 of the claude boundary on agy, fresh and resumed: the write tools, the hook, the modes, the .agents folder of the worktree"
kind: measure
complexity: 6
dependsOn:
  - t4-agy-resume-measure
parallel: false
security: true
files:
  - v2/spikes/RESULTS_agy_coder.md
dod:
  - id: d1
    item: "under a heading with the release: one row for each flag set, and for each of B1 to B15 of SPEC section C1 the measured answer, fresh and resumed, with the hash of each canary before and after; 16 paid calls at most and the spend written down"
  - id: d2
    item: "U8 and U9: the names of the write tools and of the command tools agy offers, read from the hook payloads; whether the tools list of an agent limits them; whether the PreToolUse hook sees every write with its target path; whether a tool runs when the handler exits non-zero, prints nothing or runs past its time"
  - id: d3
    item: "U5 to U7, U10 and U13: inside the unit, with no model where that is possible: the main checkout and the worktrees of other jobs hidden, the sign-in files and /proc/<cli pid>/environ tried from a child command, a network limit to the hosts of the vendor; what --mode accept-edits and --sandbox each do to a write outside the worktree and to a command; whether agy loads .agents (agents, hooks, rules) from the WORKTREE when it is an --add-dir root and when it is the cwd, with a canary hook committed at the base"
  - id: d4
    item: "the file names ONE flag set, the guard and the additions of the unit to ship, or says REFUSE and names the row that did not hold"
  - id: d5
    item: "the steps, the canaries and the fixture folders are written as a procedure, so that a later release is measured the same way (SPEC section C2, how the list of releases is updated)"
```

## v2/epoch-3/e3-roles-complete/stories/s4-codex-agy-measure/tasks/t6-security-judgement/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-security-judgement
title: "The security judgement of the codex and agy write boundary by the important model, before any launch code: ship or refuse per vendor, the properties each verdict relies on, and the must-change list"
kind: security
complexity: 6
dependsOn:
  - t3-codex-write-rows
  - t5-agy-write-rows
parallel: false
security: true
files:
  - v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md
  - v2/epoch-3/e3-roles-complete/stories/s5-codex-coder
  - v2/epoch-3/e3-roles-complete/stories/s6-agy-coder
  - v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows
  - v2/epoch-3/e3-roles-complete/stories/s8-proof
dod:
  - id: d1
    item: "the important model (the important list, read-only, no other model call) reads the two results files and the claude files they are compared with, and writes per vendor: the verdict ship or refuse for the measured release, for each of B1 to B15 which layer holds it, and a must-change list"
  - id: d2
    item: "every row that the CLI flags do not hold is closed by a MEASURED property of the unit, or the verdict is refuse; a property that was asked for and not observed counts as not held"
  - id: d3
    item: "for each verdict ship the file lists, with an id each, EVERY property of the unit and of the guard that the verdict relies on (what is hidden, the sign-in files, the network limit, no user manager inside, the sandbox or the guard of the vendor): these ids are the preflight rows of s5 t3 and s6 t4 and the ids of the table of s7 t1"
  - id: d4
    item: "the commit applies the row of SPEC section C5 for the combination that came out: the task files of a shipped vendor are changed from the must-change list; the story folder of a refused vendor and its entry in the dependsOn of s8 are removed, with the reason in the judgement file; validate_work accepts the v2 folder after the commit, and no story left depends on a story that is not built"
  - id: d5
    item: "when a vendor is refused, line 2 of the doneWhen of the epic is put to the owner before any code of s5, s6 or s8"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-codex-coder
title: "The codex coder: the write launch inside the OS layer on whatever version of codex is installed (the measured release is history), each fix turn in the same thread with exec resume"
dependsOn:
  - s4-codex-agy-measure
  - s7-vendor-seam-rows
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t1-codex-write-capability/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-codex-write-capability
title: "CodexWriteCapability: every flag of the argv declared in the help of the installed codex, and a sign-in copy that cannot need a refresh during the launch; else a refusal that names the property; the installed version is recorded and gates nothing"
kind: security
complexity: 5
dependsOn:
  - t6-codex-open-rows-measure
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/CodexWriteCapability.cs
  - src_mcp/tests/ACodexWriteCapabilityTests.cs
dod:
  - id: d1
    item: "V6, the owner's decision of 2026-11-26 (SPEC section C2 item 2; it replaces id CX-P1 of the judgement, the pin by release and bytes): a codex coder is launched on whatever version is installed; no release, version or hash is compared with the table, and an older, a newer and an unnamed version are each launched; the capability asks only what the installed CLI declares: its exec help and exec resume help declare each flag of the argv, and a flag that is not declared is a refusal that names the flag, never the version; asked on every launch, never cached, never started with a weaker set"
  - id: d2
    item: "the installed version as the CLI printed it and the last measured release of the table are both in the job envelope and in the record of the job, for information; a CLI that cannot be started and a help that does not answer are refusals that say so; a test with a table whose history names one release proves that a version one patch above it, one patch below it and one the CLI does not name are each LAUNCHED, and that no sentence of the product asks for a new measure or a new judgement"
  - id: d3
    item: "id CX-R1 and section 9 of the judgement: before EVERY launch (turn 0 and every fix turn) the access token of the job's sign-in copy must have an exp that can be read and exp >= now + the RuntimeMaxSec of the unit + 60 minutes; less, or an exp that cannot be read, is a refusal before the launch that tells the person to run codex once themselves; tests with an injected clock: a crafted copy one second under the margin is refused and one at the margin is started; no token text is in a log, a refusal or the envelope"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t2-codex-coder-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-codex-coder-launch
title: "The codex coder launch: the measured argv fresh and resumed, the worktree as cwd, the profile in config.toml before every turn, CODEX_HOME in the job folder, the measured unit with the main checkout hidden, the minimal environment with the refresh override"
kind: security
complexity: 6
dependsOn:
  - t1-codex-write-capability
parallel: false
security: true
files:
  - src_mcp/runners/Consultation/CodexConsultant.cs
  - src_mcp/runners/Toolchain/CodexCoderUnit.cs
  - shared/coder-report.codex.schema.json
  - src_mcp/tests/ACodexCoderLaunchTests.cs
dod:
  - id: d1
    item: "V1 and V2, id CX-C2: the fresh argv is exactly the set of the judgement's addendum 2, section 13.4 (commit d0dca16d of feat/v2-ep3-e3-measure, merged into this branch as a9187e76): the flag set CX1 of RESULTS_codex_coder.md AND -c agents.enabled=false, with --disable hooks, the approval policy never and the web search disabled among them, with the worktree as cwd; a fix turn is exec resume with the thread of turn 0 and EVERY flag of the fresh argv (no flag of the set is dropped on a resumed turn; a test compares the two sets); the rows X9 are closed (section 13.11), so this task waits for no measure; none of the never-flags of SPEC section C3 is reachable (a test tries each)"
  - id: d2
    item: "id CX-C1, judgement X4 and X8: the job's config.toml (the permission profile coai as default_permissions) is written and read back before EVERY launch, fresh and resumed, and no turn starts without it; the launch half of CX-C1 (section 13.11; a must-change the owner named on 2026-11-26): before EVERY launch, turn 0 and every fix turn, the job's CODEX_HOME holds no file named environments.toml, and one that is there refuses the launch as coder-boundary (a test plants one before turn 0 and one before a fix turn, and FakeCli records no launch); the output schema is the flat variant the API accepts, a test holds it equal to coai's report reader, and the reader stays the check of a report; V19: the flat variant makes every member required, and codex then writes a member the status does not use as null (seen in the coder comparison of E2: reason and notes null on an ok report) — only such a null is read as absent, a null of any other member and an unknown member stay errors, and a test gives the reader a report of that shape"
  - id: d3
    item: "judgement X5, ids U2, U3, U6 and U8: the unit is the measured one — the main checkout and the coder root HIDDEN and not read-only, only <main>/.git and the worktree bound back, the folder of all jobs hidden, the /tmp shape under which codex sandbox starts and the same shape in the preflight and in the launch, a home with CODEX_HOME inside the job's coder folder that holds the sign-in COPY of s7 t6; never RestrictNamespaces, never ReadWritePaths=/mnt"
  - id: d4
    item: "V14, ids U7 and CX-R2: only the allow-list, CODEX_HOME and CODEX_REFRESH_TOKEN_URL_OVERRIDE with the dead local address of the judgement, section 9, reach the CLI, read from inside a real unit and compared name by name; a canary value is in no recorded argv"
  - id: d5
    item: "every existing codex consultant, question and reviewer launch builds the same argv as before, byte for byte"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t3-codex-preflight-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-codex-preflight-rows
title: "The preflight rows of a codex coder job: with no model, inside the exact launch unit, one row for EVERY id of class pre of the judgement — the unit, the vendor sandbox probed with codex sandbox, the tool list, the config, the refresh override; a wrong answer refuses the job"
kind: security
complexity: 6
dependsOn:
  - t2-codex-coder-launch
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/BoundaryPreflight.cs
  - src_mcp/runners/Toolchain/CoderBoundary.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/ACodexCoderPreflightTests.cs
dod:
  - id: d1
    item: "V9, the unit: before each codex coder job the preflight runs inside the EXACT launch unit (the same property list the launch gets, a test compares the two) and has one row for each of U1 to U9 of the judgement, section 8: the worktree; the main checkout and the coder root HIDDEN (a file of them cannot be read or listed); .git; no user manager, no bus, no interop, no Windows program; the job's own HOME and temp folders; the environment compared name by name; the folder of another job; the program with its sha256"
  - id: d2
    item: "V9, the vendor sandbox, ids CX-V1 to CX-V6, with codex sandbox -P coai and a probe inside that unit: it starts; a write in the worktree works; a write into the job folder and beside the worktree does not reach the disk; the job's auth.json, the job folder and the main checkout cannot be read; a listener that coai opens on the host loopback is reached from the unit and NOT from the sandbox; no foreign process and no canary name in /proc; .git, .agents and .codex of the worktree cannot be written"
  - id: d3
    item: "V9, the rest, as the judgement's addendum 2 sets it (sections 13.3 and 13.11, commit d0dca16d on feat/v2-ep3-e3-measure): CX-T1 (codex debug prompt-input with the exact flags AND the job's own model and effort gives exactly the 11 measured names and no name that starts with collaboration., also with a stand-in project that holds a .codex/config.toml with an MCP server; a list that differs refuses the job; the row is NO evidence about the web search — its flag is CX-T6, class release), CX-C4 and CX-C3 (codex doctor --json inside the unit, with the flags of the launch and the job's CODEX_HOME, names the permission profile coai, the filesystem sandbox and the network sandbox restricted, the approval policy Never and mcp servers = 0; the row ignores the exit code, has a deadline, and is red when a field is missing or the report says the config could not be loaded), CX-C1 (config.toml read back, byte for byte), CX-R2 (the override is in the environment and a connection to its address is REFUSED), CX-R3 (the sign-in is the job's copy, 0600; the real ~/.codex is not there) and CX-R4 (the copy holds exactly the placeholder coai wrote in the place of the refresh token; in the table since addendum 2)"
  - id: d4
    item: "a wrong answer of ANY row refuses the job before any launch and names the row; a test holds the pre ids of the table for codex and the rows equal (an id with no row and a row with no id both fail); each row goes red when its property is dropped through the test seam; a row never carries the output of a program it ran, and no row prints a token"
  - id: d5
    item: "V10: no user manager, native Windows and macOS are refused before any process starts"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t4-codex-fix-turns/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-codex-fix-turns
title: "Fix turns with FakeCli as codex: exec resume with the handle of turn 0, the report from the output file, the recorded no-session and refresh-failed texts, the check of every turn's own stream, and the codex coder prompt"
kind: code
complexity: 5
dependsOn:
  - t3-codex-preflight-rows
parallel: false
security: true
files:
  - src_mcp/runners/Consultation/CodexTurnCheck.cs
  - src_mcp/src/Roles/prompts/coder.codex.md
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/ACodexCoderFixTurnsTests.cs
dod:
  - id: d1
    item: "FakeCli as codex: red, then green after 2 fix turns; the recorded argv shows exec resume with the thread of turn 0 on both, from the worktree, with the same CODEX_HOME and the same flags; the report is read from the output file; an invalid report gets ONE repair turn in the same thread"
  - id: d2
    item: "V13: a resume that answers with the recorded no-session text of the fixture ends the attempt as a crash, and the next model starts from the last coai commit; no fix turn starts a new session by itself; the failover reads a codex turn as a lost sign-in — the other models of codex are skipped and the cure tells the person to run codex once themselves — when ANY of the three signs of the judgement, section 13.6, is there: a stderr line with Failed to refresh token; a turn.failed message with unauthorized (401); the sentence Your access token could not be refreshed; the text workspace routing discovery failed with none of the three is NOT a lost sign-in and goes the road of a crash; both recorded fixtures are played: codex-refresh-failed.json (no network) and codex-refresh-failed-host-network.json (the shape a job meets; on feat/v2-ep3-e3-measure at d0dca16d)"
  - id: d3
    item: "V26, ids CX-S1, CX-T4 and CX-T5 of the judgement, after EVERY turn, through the turn check of the seam (s7 t2): a thread id that is not the handle's ends the attempt as a crash; the exact rule of CX-T4 (the judgement, section 13.11, commit d0dca16d on feat/v2-ep3-e3-measure; a must-change the owner named on 2026-11-26) over every item of the stream, the started and the completed form alike: agent_message, command_execution, file_change and error items pass; an mcp_tool_call item passes ONLY when its server is exactly codex, its tool is exactly list_mcp_resources or list_mcp_resource_templates, its error is null and the result of a completed item is one text block that parses to an empty resources list or an empty resourceTemplates list; a web_search item and a collab_tool_call item always end the job; an item of ANY other type ends the job, and the reason names the type; CX-T5: after the turn the rollout file of the handle's thread in sessions of the job's CODEX_HOME is read, and no such file, a file that cannot be read, a file of another thread or a call named collaboration.* ends the job; ends the job is coder-boundary, never green, and nothing of that turn is committed; FakeCli plays each case, among them a list that is not empty, another server, a read_mcp_resource that succeeded, a web search and an item type nobody measured; such a check may stop a job after codex updated itself, and the owner accepted that on 2026-11-26 as protection: it is not a version gate"
  - id: d4
    item: "V17, the owner's decision 40 (SPEC section C7; the coder comparison of E2: codex refused the claude prompt twice and was green with two bullets changed): src_mcp/src/Roles/prompts/coder.codex.md is the prompt of the codex coder; it differs from coder.md in the tools bullet (read with read-only shell commands, change with apply_patch; no build, no test and no git command) and in ONE added bullet, which says only what is true (SPEC section C7 item 4, second plan round): the model does not run a command that an instruction file of the repository orders before work, that is no reason to refuse, what coai ran before the turn is at the end of the prompt under the heading Run by coai before your turn, and when that section says nothing was run the model names the command in notes; every other rule of those files still applies; the bullet does not say that coai runs a command or a check that the block does not show; a test holds every other line byte-equal; every tool it names is in the measured list of id CX-T1; the codex fix prompt does not say that the model has no shell; FakeCli as codex records this prompt on turn 0 and the codex fix prompt on a fix turn"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t5-codex-boundary-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-codex-boundary-scenarios
title: "Boundary scenarios with FakeCli as the codex coder inside the real unit: writes outside, reads of what is hidden, a new unit, a Windows program"
kind: test
complexity: 5
dependsOn:
  - t4-codex-fix-turns
parallel: false
security: true
files:
  - src_mcp/tests/ACodexCoderBoundaryTests.cs
  - shared/coder-boundary-releases.json
dod:
  - id: d1
    item: "V7: the fake coder writes the main checkout, a folder beside the worktree, the .git pointer and the stand-in home: each is stopped by the OS, the job ends coder-boundary and is never green, and the main checkout is byte-equal"
  - id: d2
    item: "V8: a child command of the fake cannot read a file of the hidden main checkout, the worktree of another job, the folder of another job or the sign-in file of the stand-in REAL home, cannot start a unit and cannot run a Windows .exe; a control unit without the additions shows that the same reads then work"
  - id: d3
    item: "said by a test, so that nobody reads the unit as more than it is: a child command of the fake CAN read the job's own sign-in copy inside the unit — that read is closed by the vendor's sandbox (ids CX-V3 and CX-V5, the preflight rows of t3), which the fake does not have"
  - id: d4
    item: "V29, the owner's condition of 2026-11-26 (SPEC section C2 item 1 and section C6; ledger entry 1.16): the codex coder becomes launchable by ONE commit of this task, the last of the story, which turns on the mark launchable of the codex row of shared/coder-boundary-releases.json — and only when the must-changes of the judgement's addendum 2 are built and green: the argv of section 13.4 (t2 d1), the environments.toml refusal (t2 d2), the preflight rows with doctor and the job's own model (t3 d3), the rule of CX-T4 and CX-T5 (t4 d3), the three signs of the failover (t4 d2), the margin CX-R1 (t1 d3) and the copy with the placeholder (s7 t6); the commit names the green run of the whole MCP suite it rests on; before it the product refuses a codex coder by data, and a test holds the mark off until then"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t6-codex-open-rows-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-codex-open-rows-measure
title: "Measure what the judgement left open for codex before any argv is frozen: a spawn_agent child, the JavaScript of functions.exec, a CODEX_HOME outside /tmp, late .codex files, the refresh texts and a copy with a placeholder refresh token"
kind: measure
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - v2/spikes/RESULTS_codex_coder.md
  - v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md
  - shared/coder-boundary-releases.json
  - src_mcp/tests/fixtures/role-stops/codex-refresh-failed.json
dod:
  - id: d1
    item: "judgement X1 a, with the smallest model, inside the unit and the flag set of RESULTS_codex_coder.md sections 2 and 3: a command run by a spawn_agent child, fresh and resumed, tries the rows B2, B6, B10 and B11 with the canaries of s4 t3 and prints its tool list; recorded: each answer, whether the stream names the agent call (so that coai can refuse a turn that used it), and whether any switch of the release removes the six collaboration tools"
  - id: d2
    item: "judgement X1 b: the JavaScript of functions.exec, without exec_command: a read of the main canary and of the job's auth.json, the environment, a new process, a network call; recorded: each answer, and where the JavaScript runs (in the CLI process or inside the sandbox)"
  - id: d3
    item: "judgement X1 c and d: one fresh call with CODEX_HOME in a folder that is not under /tmp (the place of a real job): which helper binaries and PATH entries codex makes, and the rows B2, B6, B10 and B11 again; on the resumed turn of d1 a .codex/config.toml and a hooks file that were written into the worktree AFTER turn 0 (as a check can write them) do not run and do not widen the limit, with a canary outside"
  - id: d4
    item: "judgement X1 e, with no model request where that is possible: a crafted copy with exp far away and last_refresh 9 days old makes no refresh POST (a local listener counts them); a crafted copy with exp in 60 s and the override at 127.0.0.1:9, run in a unit with no network, gives a text and an exit code that are saved as the fixture, and nothing in them names the vendor's own address; one paid pair: a copy with the REAL access token and a placeholder in place of the refresh token answers exec and exec resume"
  - id: d5
    item: "recorded: whether a model-free command prints the permission profile that exec will use for this CODEX_HOME and these flags (id CX-C4); 30 paid codex calls at most (the owner's cap of 2026-11-26; it replaces 8), every call and its tokens written down; the real sign-in file has the same bytes and modification time before and after; every copy is deleted; nothing in the results is a token or a part of one"
  - id: d6
    item: "the results are a new section of RESULTS_codex_coder.md under a heading with the release, with one line holds or does not hold for each of d1 to d4; the important model then writes an addendum of the judgement for that release (ship or refuse; which of the ids CX-T2, CX-T3, CX-T4, CX-C4 and CX-R4 enter the table and in which class), and ONE commit writes the addendum's verdict and the history record (measured on that release, the date, its two headings, the sha256 of the program file) into shared/coder-boundary-releases.json (SPEC section C2); the measure runs on the version of codex that is installed on the day, a version other than the one of sections 2 and 3 of RESULTS_codex_coder.md does not stop it, and the rows of s4 t3 are not measured again; the record is history: no later version of codex needs a commit of this kind to run (the owner's decision of 2026-11-26); with refuse the row of SPEC section C5 applies and the owner is asked before any code of this story"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-agy-coder
title: "The agy coder: a coder agent with a write guard inside the OS layer on whatever version of agy is installed (the measured release is history), each fix turn in the same conversation"
dependsOn:
  - s4-codex-agy-measure
  - s7-vendor-seam-rows
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t1-agy-write-capability/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-agy-write-capability
title: "AgyWriteCapability: every flag of the argv declared in the help of the installed agy; else a refusal that names the property; the installed version is recorded and gates nothing, so a self-update of agy stops nothing"
kind: security
complexity: 6
dependsOn:
  - t7-agy-open-rows-measure
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/AgyWriteCapability.cs
  - src_mcp/tests/AAgyWriteCapabilityTests.cs
dod:
  - id: d1
    item: "V6, the owner's decision of 2026-11-26 (SPEC section C2 item 2; it replaces id AG-P1 of the judgement, the pin by release and bytes): an agy coder is launched on whatever version is installed; no release, version or hash is compared with the table, and an older, a newer and an unnamed version are each launched; the capability asks only what the installed CLI declares: its help declares --mode with accept-edits, --agent, --add-dir, --conversation and the stream formats, and a flag that is not declared is a refusal that names the flag, never the version; asked on every launch, never cached, never started with a weaker set"
  - id: d2
    item: "the installed version as the CLI printed it and the last measured release of the table are both in the job envelope and in the record of the job, for information; a CLI that cannot be started and a help that does not answer are refusals that say so; a test with a table whose history names one release proves that a version one patch above it, one patch below it and one the CLI does not name are each LAUNCHED, and that no sentence of the product asks for a new measure or a new judgement"
  - id: d3
    item: "judgement G9, id AG-P2, the program store: NOT built (the owner's decision of 2026-11-26: no pin and no store); coai keeps no copy of the agy program and launches the installed file; no file of a store is in this task"
  - id: d4
    item: "a self-update of agy between two jobs, and between two launches of one job, refuses nothing and stops nothing: a test replaces the stand-in installed file with one that prints another version, the next launch runs it, and the envelope names the new version beside the measured one"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t2-agy-write-guard/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-agy-write-guard
title: "The agy write guard: the measured coder agent, and a hook handler of coai's own that is closed by construction — an explicit decision for every input, allow only for a measured file tool inside the worktree of the job, one record for each decision"
kind: security
complexity: 6
dependsOn:
  - t7-agy-open-rows-measure
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/AntigravityCoderGuard.cs
  - src_mcp/runners/Reviewers/AntigravityReadOnly.cs
  - src_mcp/src/Program.cs
  - src_mcp/tests/AAgyCoderGuardTests.cs
dod:
  - id: d1
    item: "V15, judgement G3: the handler allows only read_file, write_file and edit_file (the names the hook sees) and only when Path, resolved through links, is inside the worktree that coai wrote into the guard's own file AND that the payload's workspacePaths hold, and is not under .git, .agents or .codex of it; it denies a write or a read outside, a file link and a folder link out, the guard folder, the home and the sign-in file, run_command, every network tool, manage_task, a tool it does not know and a payload it cannot read; the coder case is chosen by the guard's own file in the handler's folder, never by a field of the payload"
  - id: d2
    item: "judgement G2, closed by construction: the handler is this binary (--agy-hook), started by the hook's command with exec and nothing else; it writes an explicit decision for EVERY input — an empty input, text that is not JSON and a payload with no Path are each deny; a deadline of its own (5 s, under the hook's 10 s) ends in deny; an exception ends in deny; a decision that cannot be written is a non-zero exit; tests give it each of these and a closed standard output, and NONE ends with exit 0 and no output (the one answer agy 1.5.17 takes as allow)"
  - id: d3
    item: "judgement G4 as G15 amends it (addendum 3, section 14.4, commit d0dca16d on feat/v2-ep3-e3-measure; a must-change the owner named on 2026-11-26): the handler appends one record for each decision — the conversation id, the step index, the tool name and the decision; never the content of a write; the id the hook sees is in no stream, so it is not the tie — to a file of the job's coder folder that is outside the worktree and outside the guard folder; a record that cannot be written is a deny"
  - id: d4
    item: "judgement G5: the agent file is the measured bytes of RESULTS_agy_coder.md section 2 and nothing else; the folder of the guard is the own folder of the job (under the job's coder folder), written and read back before EVERY launch; the read-only agent, hook and folder are byte for byte what they were (their tests pass unchanged)"
  - id: d5
    item: "no new one-shot mode is added (the handler stays --agy-hook); if one is needed it is listed in .agents/PROJECT.md and never exits 64"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t3-agy-coder-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-agy-coder-launch
title: "The agy coder launch: the measured argv with the mode and the agent on every turn, --conversation on a fix turn, the guard folder as the working directory and never the worktree, the measured unit with the main checkout hidden, the sign-in copy by its recipe, the minimal environment, and the agy coder prompt"
kind: security
complexity: 6
dependsOn:
  - t1-agy-write-capability
  - t2-agy-write-guard
parallel: false
security: true
files:
  - src_mcp/runners/Consultation/AntigravityConsultant.cs
  - src_mcp/runners/Toolchain/AgyCoderUnit.cs
  - src_mcp/src/Roles/prompts/coder.agy.md
  - src_mcp/tests/AAgyCoderLaunchTests.cs
dod:
  - id: d1
    item: "V3 and V4, id AG-M1, judgement G5 and G6: the fresh argv is exactly the flag set AG1 that RESULTS_agy_coder.md names, with --mode accept-edits AND --agent coai-coder on EVERY argv; a fix turn adds --conversation with the id of turn 0 and changes nothing else, so a conversation that agy starts in silence is still the coder agent behind the guard; none of the never-flags of SPEC section C3 is reachable (a test tries each); the argv is a request and proves no mode: the mode a turn ran in cannot be read from anything agy prints (AG-M4 is class release; accepted risk A9), the signs of AG-M2 are read by the turn check of t5 (SPEC section C8), and nothing is taken from the flag"
  - id: d2
    item: "judgement G7, id AG-B1: the working directory of the unit is the guard folder of the job and NEVER the worktree (the seam of s7 t1 carries it; a test fails for an agy launch whose cwd is the worktree); the worktree is the one --add-dir root; the guard folder is bound read-only in the unit (AG-B3 is in the table since addendum 3 of the judgement), and so is the program folder (U9: G9 keeps the read-only bind and loses the store)"
  - id: d3
    item: "ids U2, U3, U6 and U8: the unit is the measured one — the main checkout and the coder root HIDDEN and not read-only, only <main>/.git and the worktree bound back, the folder of all jobs hidden, a home of its own in the job's coder folder with the sign-in copy of s7 t6 by the recipe of addendum 3 (G10, AG-R2; section 14.4, commit d0dca16d on feat/v2-ep3-e3-measure): the copy holds the access token only; a fresh copy is made for every turn; V28: before EVERY launch its expiry is at least now + the RuntimeMaxSec of the unit + 5 minutes, and a launch that cannot have the margin is refused and never started on less — a must-change the owner named on 2026-11-26, so that an expired copy never reaches a start and never opens the login prompt on the channel of the job's prompt (risk A11); the deadline of an agy turn fits under the life of a token (about one hour); never the full copy and never the real sign-in file in the coder's unit"
  - id: d4
    item: "V14, id U7: only the allow-list reaches the CLI, compared name by name from inside a real unit; agy gets no variable for its sign-in; a canary value is in no recorded argv"
  - id: d5
    item: "every existing agy consultant, question and reviewer launch builds the same argv as before, byte for byte"
  - id: d6
    item: "V17, the owner's decision 40 (SPEC section C7): src_mcp/src/Roles/prompts/coder.agy.md is the prompt of the agy coder; it differs from coder.md in the tools bullet (the tool names of the measured agent file of t2; no command tool) and in the ONE added bullet of SPEC section C7 item 4, which says only what is true (the model does not run a command that an instruction file of the repository orders before work, that is no reason to refuse, and what coai ran before the turn is at the end of the prompt under the heading Run by coai before your turn; the same sentence as in the codex file when that file exists), and a test holds every other line byte-equal and every tool name equal to a tool of the agent file; FakeCli as agy records this prompt on turn 0; nothing here needs the codex file"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t4-agy-preflight-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-agy-preflight-rows
title: "The preflight rows of an agy coder job: with no model, inside the exact launch unit, one row for EVERY id of class pre of the judgement — the unit, the answers of the guard handler with an empty input among them, the bytes of the guard files, the sign-in copy"
kind: security
complexity: 6
dependsOn:
  - t3-agy-coder-launch
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/BoundaryPreflight.cs
  - src_mcp/runners/Toolchain/CoderBoundary.cs
  - src_mcp/tests/AAgyCoderPreflightTests.cs
dod:
  - id: d1
    item: "V9, the unit: before each agy coder job the preflight runs inside the EXACT launch unit (the same property list and the same working directory the launch gets, a test compares the two) and has one row for each of U1 to U9 of the judgement, section 8, for AG-B1 (the working directory is the guard folder, not the worktree), for AG-R1 (the sign-in is the job's copy, 0600, the only sign-in file there; the real ~/.gemini is not there) for AG-B3 (the guard folder cannot be written) and for AG-R2 (the copy holds no refresh token and its expiry has the margin of t3 d3); both are in the table since addendum 3 of the judgement"
  - id: d2
    item: "V9, the guard, in the same unit, through the EXACT command line of hooks.json run from the guard folder: AG-G1 — one payload for each case of t2 d1, allow for the write inside the worktree and deny for each other (outside, .git, .agents, .codex, a file link out, a folder link out, the guard folder, the sign-in file, a command tool, a network tool, manage_task, an unknown tool, no Path); AG-G2 — an EMPTY input and text that is not JSON are each answered with an explicit deny; AG-G3 — hooks.json, the agent file, the hook script and the guard's own file are read back and are the expected bytes"
  - id: d3
    item: "a wrong answer of ANY row refuses the job before any launch and names the row; a test holds the pre ids of the table for agy and the rows equal (an id with no row and a row with no id both fail); each row goes red when its property or its part of the guard is dropped through the test seam"
  - id: d4
    item: "the envelope says what the rows do not prove: that agy calls the hook for every tool and that the agent file limits the tools are ids of class release (AG-G4, AG-A1, AG-B2 and AG-M4: the mode a turn ran in) — named as measured, with the release they were measured on beside the installed one, never as probed before the job"
  - id: d5
    item: "V10: no user manager, native Windows and macOS are refused before any process starts"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t5-agy-fix-turns/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-agy-fix-turns
title: "Fix turns with an agy conversation: --conversation with the handle of turn 0, the conversation id compared on every turn, the usage of each turn, and the check of every turn's own stream against the guard's records (G15) and for the signs of a mode that did not hold"
kind: code
complexity: 6
dependsOn:
  - t4-agy-preflight-rows
parallel: false
security: true
files:
  - src_mcp/src/Server/RoleJobs/RoleFailover.cs
  - src_mcp/runners/Consultation/AgyTurnCheck.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/AAgyCoderFixTurnsTests.cs
dod:
  - id: d1
    item: "FakeCli as agy: red, then green after 2 fix turns; the recorded argv shows --conversation with the id of turn 0 on both, with --mode accept-edits and --agent coai-coder, the same home and the same guard folder as cwd; an invalid report gets ONE repair turn in the same conversation"
  - id: d2
    item: "V13 and judgement G8, id AG-S1: a resumed turn whose conversation id is not the handle's — also with exit code 0 — or whose stderr has the recorded not-found line of the fixture ends the attempt as a crash; the worktree goes back to the last coai commit before the next model starts; no fix turn goes on in a new conversation; the failover reads as a lost agy sign-in the stderr text Authentication required or authentication timed out, a result with authentication failed or timed out (fixture agy-copy-expired.json) and the Please sign in text of agy models (addendum 3 of the judgement, section 14.4), so the other models of agy are skipped; the stderr of that case holds a login address and is not copied into an envelope or a notice"
  - id: d3
    item: "V25, G15 (addendum 3 of the judgement, section 14.4, commit d0dca16d on feat/v2-ep3-e3-measure; a must-change the owner named on 2026-11-26), ids AG-G5 and AG-A2, after EVERY turn through the turn check of the seam (s7 t2), before anything is committed: coai lays the stream beside the guard's records of that turn — every tool event of the stream has exactly ONE record with the same conversation id, step index and tool name; an event that ended DONE has the decision allow, and an event whose record says deny ended ERROR; every record has its event and the two counts are equal; every tool name is read_file, write_file or edit_file; an event with no record, a record with no event, two records for one step and another tool name each end the job; V18, the signs of AG-M2: a stderr line that starts with warning: --mode or warning: unrecognized --mode (fixture agy-mode-fallback.json), a result with a denied_actions that is not empty, and the stderr line that a tool required the command permission each end the job; AG-M3 is NOT built: the init line says the same for every mode (addendum 3), so nothing compares it; ends the job is coder-boundary, never green, and nothing of that turn is committed; FakeCli plays each case, and one case has the right argv with a mode warning on stderr, so that the flag alone never passes; such a check may stop a job after agy updated itself, and the owner accepted that on 2026-11-26 as protection: it is not a version gate"
  - id: d4
    item: "V16: the usage line of each turn holds that turn only (the sum agy reports is subtracted)"
  - id: d5
    item: "nothing here needs the codex story: the tests pass with a table in which codex is refused"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t7-agy-open-rows-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t7-agy-open-rows-measure
title: "Measure what the judgement left open for agy before any code of this story: B13 on a resumed turn, the guard folder read-only, the stream beside the hook, the mode the init line names, a sign-in copy with no refresh token, the program from a read-only copy, agy behind a proxy; the owner accepted the risks A1 to A9 on 2026-11-26, so the verdict and the history record enter the table on the addendum; the measure runs on the version that is installed now"
kind: measure
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - v2/spikes/RESULTS_agy_coder.md
  - v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md
  - shared/coder-boundary-releases.json
  - src_mcp/tests/fixtures/role-stops/agy-mode-fallback.json
  - src_mcp/tests/fixtures/role-stops/agy-copy-expired.json
dod:
  - id: d1
    item: "before anything else the installed release is read with a scratch home and written down beside the release of RESULTS_agy_coder.md (1.5.17; agy updated itself to 1.5.18 on 2026-11-26); the task runs on the version that is installed NOW, whatever it is (the owner's decision of 2026-11-26: a new version stops nothing); the rows of s4 t5 are NOT measured again on it; the sha256 of the program file that ran is recorded, as history"
  - id: d2
    item: "judgement G1, row B13, with the smallest model, the coder agent, the guard and the unit of RESULTS_agy_coder.md sections 2 and 3, cwd = the guard folder, the worktree an --add-dir root: a RESUMED turn with canary PreToolUse and SessionStart hooks, an agent and a rule committed in .agents of the worktree; and a resumed turn after a hooks file and an agent file were written into the worktree between the turns (as a check can write them); recorded for each: no canary hook ran, and the guard still saw and decided every tool call"
  - id: d3
    item: "the guard folder bound read-only in the unit: does a turn work, and what agy writes into its cwd at all (the tree diff of the guard folder); the tool events of the stream beside the hook payloads: is the id of a tool event the toolCall.id the hook saw, which name the stream uses, how a denied tool looks; where the stream names the mode; the stderr line of a mode fall-back saved as the fixture; the hook name of the multi-edit tool when the model can be made to use it"
  - id: d4
    item: "judgement G10, the sign-in: a copy with a valid access token and NO refresh token: agy models and one turn; the text and exit code of such a copy whose expiry is in the past, saved as the fixture; agy models in a unit with no repository refreshes an expired full copy, and a full copy whose expiry was set to the past is refreshed at the start; the 8-hex fingerprint of the refresh token (never the value) is the same after each"
  - id: d5
    item: "judgement G9, the program from a read-only copy: NOT measured and not built (the owner's decision of 2026-11-26: no pin and no program store); no turn of the cap is spent on it"
  - id: d6
    item: "judgement G11, the network: a unit with PrivateNetwork=yes and the proxy variables at a relay inside the unit that passes each connection through a unix socket to a forwarder outside (harness scripts; they log host and port only): do agy models and one turn work; the list of hosts; which connection, if any, does not use the proxy"
  - id: d7
    item: "30 paid agy turns at most (the owner's cap of 2026-11-26; it replaces 12), every call and its tokens written down; a row of d2 to d4, d6 or d9 that does not fit in the cap is recorded as not measured, and the addendum of d8 names it and says what the verdict rests on without it; the real sign-in file and the manifest of the real ~/.gemini are the same before and after; no agy command runs in the real home; every copy is deleted; nothing in the results is a token or a part of one"
  - id: d8
    item: "the results are a new section of RESULTS_agy_coder.md under a heading with the release, with one line holds, does not hold or not measured for each of d2 to d4, d6 and d9; the important model writes an addendum of the judgement (ship or refuse; which of the ids AG-G5, AG-B2, AG-B3 and AG-R2 enter the table — AG-P2, the store, does not; and which of AG-M3 and AG-M4; the accepted risks of its section 7 as they then stand); the owner accepted the risks A1 to A9 on 2026-11-26 (SPEC section H, risk 9), so with the verdict ship ONE commit writes the verdict and the history record (measured on the installed release, the date, its two headings, the sha256) into shared/coder-boundary-releases.json (SPEC section C2) and no further word of the owner is needed; the record is history, and no later version of agy needs a commit of this kind to run; only a risk the addendum ADDS, or one it makes wider than the text of section 7, is put to the owner first, and the commit waits for that risk alone; when B13 does not hold on a resumed turn the addendum says refuse and the row of SPEC section C5 applies"
  - id: d9
    item: "addendum 1 of the judgement (2026-11-26), G14 (the coder comparison of E2 saw permission_mode request-review in the init line while --mode accept-edits was on the argv): the permission_mode of the init line is recorded for EVERY turn of this task, fresh and resumed, and for a start with no --mode, with --mode plan and with --mode auto (the fall-back), each with no model turn (standard input closed before a message) when agy prints the line so — a start that needs a model turn counts in the cap of d7 and comes last; recorded: the value for each, and whether the value for accept-edits differs from the value of the default mode and of the fall-back on every start; when it differs the value goes into the table with the release (id AG-M3, class turn), when it does not, one resumed turn of a scratch agent that has a command tool shows whether a command is still denied under accept-edits, and the addendum names AG-M4 (class release) and the accepted risk A9, which the owner accepted on 2026-11-26"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s7-vendor-seam-rows
title: "The vendor seam of the coder, built whatever the verdicts are: one table of verdicts and of the history of measures (no version gates anything), the fix loop through the seam, the measured lists in role-defaults, the route by shipped vendor"
dependsOn:
  - s4-codex-agy-measure
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t1-coder-vendor-seam/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-coder-vendor-seam
title: "One seam per vendor for the coder launch (capability, argv, unit, working directory, home, sign-in, program), a resume handle that carries the vendor, and shared/coder-boundary-releases.json as the one table of verdicts, of the history of measures and of property ids; no version of any vendor's CLI gates anything; the claude argv and unit unchanged"
kind: security
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - shared/coder-boundary-releases.json
  - src_mcp/runners/Consultation/CoderVendors.cs
  - src_mcp/runners/Consultation/CoderLaunches.cs
  - src_mcp/runners/Consultation/CoderHandle.cs
  - src_mcp/runners/Toolchain/CoderUnit.cs
  - src_mcp/runners/Toolchain/CoderBoundary.cs
  - src_mcp/runners/Toolchain/CoderProgram.cs
  - src_mcp/tests/ACoderVendorSeamTests.cs
dod:
  - id: d1
    item: "judgement S6: one type per vendor gives the write capability probe, the fresh and the resumed argv, the property list of the unit (so a vendor can HIDE the main checkout and the coder root where claude binds them read-only), the working directory of the unit (CoderBoundary forces the worktree today; agy needs its guard folder), the environment allow-list, the home layout, the sign-in recipe, the real path of the program and the id of the vendor's coder prompt (SPEC section C7; a vendor with no prompt of its own cannot be launched as a coder); the claude argv and the property list of the claude unit are what they were (the existing coder tests of both pass unchanged); the version floor of ClaudeWriteCapability is NOT kept: d6"
  - id: d2
    item: "V5: a resume handle carries the vendor beside the job and the worktree, and a handle of another vendor, job or worktree is refused before a process starts"
  - id: d3
    item: "V11, judgement S5: shared/coder-boundary-releases.json is the ONE table: per vendor the verdict (ship or refuse), the ids of the properties the verdict relies on, each with its class (pre, launch, turn or release, judgement section 8), and the HISTORY of its measures (for each one: the release, the date, the heading of its results, the heading of its judgement, the sha256 of the measured program file and the values of the CLI's own stream that a turn check compares); the history is information: no code compares an installed version or hash with it (the owner's decision of 2026-11-26, SPEC section C2); the history (the key measured; both vendors have a record since the addenda 2 and 3 of the judgement) makes nothing launchable; a vendor is launchable only when its verdict is ship AND its row carries the mark launchable (the owner's condition of 2026-11-26, SPEC section C2 item 1): the mark is off for codex and for agy and is turned on by ONE commit — of s5 t5 for codex, of s8 t6 for agy — after the must-changes of the judgement's addenda are built and green; a vendor that is not launchable is refused with a sentence that names the launchable vendors; a scan test fails when a second place decides it, and its companion proves the scan matches the one place"
  - id: d4
    item: "the table is filled only from the judgement file: a test reads the repository and fails for a history record whose results heading, judgement verdict or sha256 is not there, for a relied-on id that the judgement does not list, and for an id whose class differs from the judgement's"
  - id: d5
    item: "the seam is built for every combination of verdicts: with both vendors refused, or with the mark off for either, the table still refuses them by data and the tests of this task pass"
  - id: d6
    item: "the owner's decision of 2026-11-26, for EVERY vendor (ledger entry 1.8; SPEC section C2 item 2): no CLI version, release, hash or pin of claude, codex or agy refuses a launch, stops a job, or asks for a new measure, a new judgement or a doc rewrite; the floor of ClaudeWriteCapability (2.1.330) stops refusing and becomes the history record of claude in the table, and its cure sentence (update claude) leaves the product; before the change a scan lists every place of src_mcp where a version, a release or a hash of a vendor's CLI decides a refusal or a stop, for the coder, the reviewers and the consultants, and the list is in the task result; each place is changed to record the version instead, or is named in the result with the reason it is not a version gate (a flag that the help does not declare is not one); the scan stays as a test, with a companion that proves it matches something; for each of the three vendors a test launches a version below and a version above the measured one; the envelope and the record of every coder job name the installed version beside the last measured one"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t2-fix-loop-through-the-seam/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-fix-loop-through-the-seam
title: "The fix loop, the repair turn and the failover read the session, the report, a lost session, the check of a turn's own stream and the prompt of the vendor through the vendor seam; proved with a test vendor"
kind: code
complexity: 6
dependsOn:
  - t1-coder-vendor-seam
  - t3-instruction-step
parallel: false
security: true
files:
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Server/RoleJobs/RoleModelLauncher.cs
  - src_mcp/src/Server/RoleJobs/CoderPrompt.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/ACoderFixLoopThroughTheSeamTests.cs
dod:
  - id: d1
    item: "with a test vendor in an injected table (FakeCli) a coder job goes red, then green after 2 fix turns in the session of turn 0; an invalid report gets ONE repair turn in that session; the claude fix-loop tests pass unchanged"
  - id: d2
    item: "V13: a lost session on a fix turn ends the attempt as a crash and the next model starts from the last coai commit; no fix turn starts a new session by itself; one usage line for each launch"
  - id: d3
    item: "judgement S7, the ids of class turn: the seam gives a check of the turn's own stream that runs after EVERY turn, turn 0 too, before anything of that turn is committed; a wrong answer ends the job coder-boundary and is never green; a session that is not the handle's ends the attempt as a crash; proved with the test vendor: a planted bad item stops the job and the worktree holds no commit of that turn; claude gets the check that says nothing, and its tests pass unchanged"
  - id: d4
    item: "no line of this task names codex or agy: s5 and s6 each add their vendor without a change here"
  - id: d5
    item: "V17, the owner's decision 40 (SPEC section C7): the turn-0 prompt, the fix prompt and the repair prompt are rendered from the prompt the seam names, and an override a person wrote is read by that id, so an override of the claude prompt is never sent to another vendor; the three claude prompts are byte for byte what they were: a test holds the sha256 of src_mcp/src/Roles/prompts/coder.md (ee31db20b05e779b817131fdb615be1225271dfad715e3b647cfa38d7d92d7bf) and, for a repository that declares no instruction step, compares the claude prompts of a fixture task with texts recorded from the code before this change (equal, byte for byte); the prompt of the test vendor is what FakeCli records on turn 0 and on a fix turn; the envelope names the prompt id and the sha256 of the rendered turn-0 prompt, never its text"
  - id: d6
    item: "V20 and V22, SPEC section C9 items 6 and 7: the output of the instruction step of t3 is appended to the turn-0 prompt of EVERY vendor, claude too, as ONE block that coai writes after the rendered prompt (the heading Run by coai before your turn, the argv, the base commit, the output in a fence longer than any run of backticks in it) — not a slot of a prompt file, so coder.md and its sha test do not change; with a step the claude prompt is the recorded text followed by the block, and FakeCli as claude and as the test vendor each record the canary line of the fixture step on turn 0; with no step claude is sent no block, and a vendor whose prompt points at the block is sent the block that says nothing was run; a fix turn and a repair turn do not repeat the block; a new session after a failover carries it again"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t3-instruction-step/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-instruction-step
title: "The instruction step: a command the repository declares in .coai/toolchain.yaml is run by coai before turn 0 in a check unit at the base commit, its output is kept for the prompt, and a declared step that cannot run refuses the job"
kind: security
complexity: 5
dependsOn: []
parallel: false
security: true
files:
  - shared/toolchain.schema.json
  - src_mcp/core/Toolchain/ToolchainModel.cs
  - src_mcp/core/Toolchain/ToolchainParse.cs
  - src_mcp/src/Server/RoleJobs/CoderInstructions.cs
  - src_mcp/src/Server/RoleJobs/CoderEnvelope.cs
  - .coai/toolchain.yaml
  - src_mcp/tests/ACoderInstructionStepTests.cs
dod:
  - id: d1
    item: "SPEC section C9 items 1 and 2: the file gets ONE optional top-level key, instructions, with argv (a block list, no shell, the refusal rule of the reader for argv text), perFile (optional: appended once for EACH entry of the files of the task; an item may hold {file}) and timeoutSeconds (1 to 300; 60 when absent); version stays 1; it is read at the BASE commit; an unknown member, an empty argv, {file} outside perFile and a number out of range are each toolchain-refused with the line; the schema file and the reader are held equal by a test; coai never reads an instruction file to find a command"
  - id: d2
    item: "V20, C9 items 3 and 4: for a job whose base declares the step coai runs it ONCE, before any model is launched, through CheckBoundary with the property list of a test step (no network, the own home of the job, no credential), in the worktree at the base commit; the tree and HEAD are compared before and after; standard output is kept in the job folder and used again by a later attempt of the same job; on a real unit a step cannot read a stand-in sign-in file of the home and cannot reach a listener on the host loopback, and a control shows that the same step sees both outside the unit"
  - id: d3
    item: "V21, C9 item 5, the failure path: a step that exits non-zero, runs past its time, prints more than 64 KiB, prints bytes that are not UTF-8 or changes the tree, and a unit that cannot start, EACH end the job with the stop instructions-failed before any model is launched: FakeCli records ZERO launches, there is no failover and no green, nothing is cut, and the envelope names the argv, the exit code, the size and the last lines of standard error (one test for each cause)"
  - id: d4
    item: "V22, C9 item 8: the envelope has instructions {declared, argv, exitCode, bytes, sha256, ms}; a job whose base declares no step runs nothing, and its envelope says declared false; no text of the output is in a log line or in the envelope"
  - id: d5
    item: "C9 item 9, this repository, measure first: the output size of the rule resolver's read for the files of three past tasks of this repository is in the task result; when each is at most 64 KiB, .coai/toolchain.yaml of this repository declares that argv with one --file pair for each file of the task, and a test reads the committed file; when one is larger the step is NOT declared with a cut text and the result says so. Measured: 334 362 bytes for each of the three. Decided by the owner on 2026-11-26: the limit stays 64 KiB, so the file of this repository declares no instructions step, says why in a comment, and a test holds that; the short form of the resolver that would let it declare one is a follow-up outside this epic (SPEC section G)"
  - id: d6
    item: "V23, C9 item 10, the owner's decision of 2026-11-26 (it replaces must-change M1 of the security verify of this task, which ended such a job instructions-failed): a secret-shaped text in the output of the step stops NOTHING; the output is searched with the classes of the redactor, the one road; on a hit the job goes on, the output is kept and reaches the turn-0 prompt as it was, and coai records the finding in a notice instructions-secret whose subject is the job, in the member secrets of instructions in the envelope (class and line; written only when there is a finding) and in one log line; each names the class of the secret and the line of the output, and no part of the value is in the notice, in the envelope, in the record of the job or in the log (a test searches all four for the value and for its tail); a step whose output holds no such text writes no notice and no secrets member; the notice is what the tab of epoch 4 e4-local-pages reads"
  - id: d7
    item: "V24, C9 item 5, the re-review round of 2026-11-26, finding F0: the failure record of the step keeps the argv and the last lines of standard error, and BOTH go through the redactor, the one road, before they are written to the envelope, to the record of the job or to a log line, or shown; the argv goes the same road wherever else it is recorded (the instructions member of the envelope of a job whose step passed); a test runs a step that fails with one sentinel secret in its standard error and another in an argv item, and neither sentinel, nor its tail, is in the record of the job, in the envelope or in the log, while the exit code and the size are; this is a rule of RECORDS: the argv that is run is the real one, and d6 still holds for the output that reaches the prompt"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t4-coder-route-by-vendor/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-coder-route-by-vendor
title: "shared/role-defaults.json gets the codex and agy main-AI rows from the measured lists and the failover entries of scout and coder; the route of a coder keeps the vendors the table ships; the words claude only leave the product"
kind: code
complexity: 6
dependsOn:
  - t1-coder-vendor-seam
parallel: false
files:
  - shared/role-defaults.json
  - shared/role-model-names.json
  - shared/role-route-vectors.json
  - src_mcp/tests/ARolesAnswerTests.cs
  - src_mcp/src/Server/RoleJobs/CoderRoute.cs
  - src_mcp/src/Tools.cs
  - src_mcp/tests/ACoderRouteByVendorTests.cs
dod:
  - id: d1
    item: "a coder route keeps the entries whose vendor the table ships and drops the others with the reason in the why of the route; a route with none is refused and names COAI_ROLES_MODELS; a model the caller named on a vendor that is not shipped is refused with a sentence that names the vendor"
  - id: d2
    item: "tested with an injected table for each of the four combinations of ship and refuse for codex and agy: the route holds exactly the shipped vendors, and a refused vendor is in no route"
  - id: d3
    item: "a Claude model on codex or agy is still refused by the vendor-routing rule; the tool description and the refusals name the shipped vendors and no longer say claude only"
  - id: d4
    item: "(moved here from the rows task) the codex and agy main-AI rows are BOTH filled (the owner's decision of 2026-11-26: every one of the three vendors is a full main AI and ships as coder, reviewer and consultant; SPEC section D): each row holds the roles and the bands of the table of SPEC section D, made of names from RESULTS_model_lists.md only; an entry of another vendor is behind whenVendorRow, and a coder entry of another vendor only while the table of coder boundaries ships it; no name is added that the CLI did not print, and a name new to shared/role-model-names.json is added there with RESULTS_model_lists.md as its source; the vendor-routing rule holds for every entry; the note of both rows (no measured model list) is removed; the route vectors cover each new list and the roles tool answers both rows"
  - id: d5
    item: "(moved here from the rows task, unchanged) the scout and coder lists of the claude main-AI row get the failover entries of SPEC section D behind whenVendorRow; a coder entry of another vendor is added only for a vendor whose verdict is ship; the route vectors cover each new list; the roles tool answers them"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t5-check-units-hide-the-coder-folder/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-check-units-hide-the-coder-folder
title: "No check unit sees the coder folder or another job's folder: the job folder is hidden and the check's own folders are bound one by one, with a preflight row for each (a measured hole: a build step read the sign-in copy and wrote the guard file)"
kind: security
complexity: 4
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/BoundaryUnit.cs
  - src_mcp/runners/Toolchain/BoundaryPreflight.cs
  - src_mcp/runners/Toolchain/CheckBoundary.cs
  - src_mcp/tests/ACheckUnitHidesTheCoderFolderTests.cs
  - src_mcp/tests/ACheckBoundaryUnitTests.cs
  - research/module_runners.md
dod:
  - id: d1
    item: "judgement S1 and section 4 (measured there: with BindPaths of the whole job folder a command of a check unit read the sign-in copies and wrote hooks.json): BoundaryUnit.Properties no longer binds the job folder; in every check unit the job folder is an empty read-only tmpfs and the check's own folders (BoundaryJob.Folders) are bound one by one, so a folder made later under the job folder is hidden unless it is named there; the check's HOME, temp folders and caches work as before"
  - id: d2
    item: "judgement S2: the folder that holds all job folders is hidden in every check unit before the job's own folders are bound, also when COAI_CODER_JOBS puts it outside the home"
  - id: d3
    item: "ids K1 and K2: the preflight of the CHECK unit, in BOTH network states, has a row for a canary that coai plants in the job's coder folder (it cannot be read, listed or written) and a row for a canary in a stand-in folder of another job (it cannot be read); a wrong answer refuses the job before any check; each row goes red when its property is dropped through the test seam, and a control unit with the old bind shows that the read then works"
  - id: d4
    item: "on a real unit: a check step with the host network (the fake as a build) cannot read a stand-in sign-in copy in the coder's home and cannot write a stand-in guard folder; the real build and the whole suite of this repository still pass under the new property list, and the numbers are in the task result; the property list of the claude CODER's unit is byte for byte what it was"
  - id: d5
    item: "research/module_runners.md says what a check unit sees; the task result carries the follow-up for E2 as a written todo: what claude 2.1.330 does with a file that a check planted in the coder's home before this fix was not measured"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t6-sign-in-copies/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-sign-in-copies
title: "The sign-in of a vendor reaches its coder unit only as a copy coai makes for the job: the recipe of the seam, a refresh step in a unit with no model and no repository, the gate before every launch, the real file never touched, and the commit search for the copy's tokens"
kind: security
complexity: 6
dependsOn:
  - t1-coder-vendor-seam
  - t5-check-units-hide-the-coder-folder
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/SignInCopy.cs
  - src_mcp/src/Server/RoleJobs/CoderCommits.cs
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/tests/ASignInCopyTests.cs
dod:
  - id: d1
    item: "judgement S3: the sign-in of a vendor that signs in with a file reaches its coder unit only as a COPY that coai makes for the job from the recipe of the seam (which file, which fields are kept): mode 0600 in a 0700 folder of the job's coder home, never a bind of the real file, made for the job and never kept after it — removed when the job ends and by the sweep of E2 after a kill; claude does not change"
  - id: d2
    item: "the recipe can put a placeholder in the place of a field (codex: the refresh token, CX-R4) and can name a refresh step with no model (agy: AG-R2); both ids are in the table since the addenda 2 and 3 of the judgement (commit d0dca16d on feat/v2-ep3-e3-measure); the step runs in a unit of its own with the isolation of the family and the network, a scratch home with the full copy, and NO repository, worktree, guard or model; the full copy lives in a folder that no coder unit and no check unit can see (risk A12: the rules S1 and S2 hold for that folder too) and is deleted when the unit ends, before the coder starts; only the access token and its expiry go into the coder's copy; the step runs ONLY when the access token of the real file has no margin: when it has, the coder's copy is cut from the real file and no refresh runs (section 14.6)"
  - id: d3
    item: "the gate of a recipe is asked before EVERY launch (codex: CX-R1 of s5 t1; agy with AG-R2: expiry >= now + the RuntimeMaxSec of the unit + 5 minutes), and a turn of a vendor whose copy cannot be refreshed is capped by the life of its token; a launch that cannot have the margin is refused and never started on less, so that an expired copy never reaches a start (risk A11; a must-change the owner named on 2026-11-26); for agy a fresh copy is made for every turn"
  - id: d4
    item: "the real sign-in file is opened for reading only; its sha256 and modification time before and after the job are in the envelope (a hash, never text), and a difference is a notice to the person"
  - id: d5
    item: "judgement S4, id K3: the commit search of E2 s7 (staged blobs and names, before and after git add, fail closed) also gets the token texts of the job's copy as they are AFTER the turn (for codex the access token and the id token; its refresh token is a placeholder); a hit ends the job coder-boundary and nothing is committed; a test plants a token of a made-up copy in a file, in a file name and behind a working-tree-encoding attribute"
  - id: d6
    item: "no token text reaches an argv, a log, a notice, the envelope or an exception (the sentinel tests of E2 s7, extended to this type); every test runs with a temp home and never reads a real sign-in"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s8-proof
title: "Proof: failover and acceptance for each combination of verdicts on the real binary with FakeCli, small paid runs where only a real CLI can show it, docs"
dependsOn:
  - s2-scout-judge-roles
  - s3-plan-round-feature-folder
  - s5-codex-coder
  - s6-agy-coder
  - s7-vendor-seam-rows
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t1-cross-vendor-failover/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-cross-vendor-failover
title: "Failover of a coder across vendors, for each combination of verdicts: from the last coai commit, in a new session of the new vendor, in a unit that holds only that vendor"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/RoleFailover.cs
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/CoderOsLayer.cs
  - src_mcp/tests/ACoderFailsOverAcrossVendorsTests.cs
dod:
  - id: d1
    item: "V12 with FakeCli and an injected table, once for EACH of the four combinations of ship and refuse for codex and agy: the job fails over through exactly the shipped vendors in route order (both: claude, codex, agy; one: claude and that one; none: the claude models only), each switch from the last coai commit (git status clean) in a new session with a handle bound to the new vendor, one switch line and one notice that names the vendor"
  - id: d2
    item: "in every combination the fake records ZERO launches of a refused vendor, a model the caller named on a refused vendor is refused before any folder, and no test of a combination needs or waits for a launch of a refused vendor; a vendor with the verdict ship whose mark launchable is off (the must-changes of its judgement are not built and green yet) counts as refused; a vendor whose installed version is not the measured one is shipped like any other"
  - id: d3
    item: "the unit of each vendor holds only the sign-in copy or the variable name of that vendor (read from the properties of the unit and from inside it); the sign-in copy of the vendor that was left is deleted before the unit of the next vendor starts; the fix-turn count stays the count of the job; a spent quota skips only the other models of that vendor"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t2-epic-acceptance/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-epic-acceptance
title: "Epic acceptance on the real binary over stdio with FakeCli: scout, judge, the coder of each shipped vendor resumed, a refused vendor refused, a plan round over a folder"
kind: test
complexity: 6
dependsOn:
  - t1-cross-vendor-failover
parallel: false
files:
  - src_mcp/tests/TheRolesCompleteEpicAcceptanceTests.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
dod:
  - id: d1
    item: "on a real coai-mcp over stdio with the real detached runner and FakeCli as every CLI: a scout job and a judge job end succeeded with their strict JSON envelopes"
  - id: d2
    item: "the test reads the table of this repository, so it passes in each of the four combinations: for each vendor the table ships, a coder job routed to it goes red, then green after one fix turn, the recorded argv shows the session of turn 0 resumed (exec resume, --conversation), and the prompt the fake recorded on turn 0 is that vendor's own (V17) and ends with the block of the instruction step that the fixture repository declares (V20); in the same run a fixture whose declared step exits non-zero ends instructions-failed with no launch (V21); for each vendor the table refuses, start_role with a model of that vendor is refused and the fake records no launch of it"
  - id: d3
    item: "in the same run: review_plan with featureFolder on an invalid fixture is refused with zero reviewer launches; on a valid one the prompt that FakeCli recorded holds the sections in topological order; the pack of the v2 folder of this repository for this epic is under 256 KiB"
  - id: d4
    item: "no test calls a vendor or reads a real sign-in; every test of this epic runs with a temp HOME and a temp data folder"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t3-read-only-paid-proof/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-read-only-paid-proof
title: "Real scout job and real judge job with haiku on a scratch clone: the envelopes, the repair turns, the cost"
kind: measure
complexity: 4
dependsOn:
  - t2-epic-acceptance
parallel: false
files:
  - v2/epoch-3/e3-roles-complete/RESULTS_roles_proof.md
dod:
  - id: d1
    item: "one real scout job and one real judge job with haiku (a scratch data folder sets the scout list and the important list to haiku) on a scratch clone of this repository: the file holds the job id, route, attempts, repair turns, the counts of verified evidence and the cost from the ledger"
  - id: d2
    item: "the owner's cap of 2026-11-26: this task and t4 together spend at most 30 USD (it replaces 4 calls and 0.10 USD here); the file holds the spend of this task and what is left of the 30 USD for t4; the cap is enforced before each call (d3), never by reading the ledger afterwards; a second run of a role only after a recorded change; nobody edits the clone while a job runs"
  - id: d3
    item: "the re-review round of 2026-11-26, finding F3: the 30 USD cap is enforced BEFORE each call; before the first call the file holds a price table (for each model that will run: the price of input and of output tokens, its source and its date) and the budget left (30 USD); each call reserves its priced worst case: the smallest model of the vendor, a bound on the tokens of the call (the flag or setting of the CLI that bounds them where it has one; where it has none, the context window of the model and its largest output for EVERY turn the job may make: turn 0 and the repair turns), at the price of the table; a call whose reservation is more than the budget left is NOT launched, and neither is a call for which no bound or no price can be named; the file holds, for each call, the reservation beside the actual cost from the ledger, and the budget left after it is the budget before minus the actual cost; a dry run with a budget smaller than one reservation shows the refusal with no job started"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t4-coder-resume-paid-proof/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-coder-resume-paid-proof
title: "Real coder job of each shipped vendor with its smallest model, started by the product in the product's own unit: a repository whose instruction file orders a command, a planted red check, one fix turn in the session of turn 0, green"
kind: measure
complexity: 5
dependsOn:
  - t3-read-only-paid-proof
  - t6-agy-boundary-scenarios
parallel: false
security: true
files:
  - v2/epoch-3/e3-roles-complete/RESULTS_roles_proof.md
dod:
  - id: d1
    item: "for each vendor that is launchable (the verdict ship and the mark on): one real coder job with the smallest model of that vendor, on the version that is installed on the day (no version is waited for and none is measured again), on a fixture repository with its own toolchain file and a planted red check; the file holds the job id, the installed version beside the last measured one and the sha256 of the program that ran (history), the session id of turn 0 and of the fix turn (equal, read from the own stream of the CLI), fixTurns, green, the properties of the unit, the preflight rows by id, the answer of the turn check of each turn and the spend"
  - id: d2
    item: "the owner's cap of 2026-11-26: t3 and this task together spend at most 30 USD (it replaces 2 runs for a vendor and 0.50 USD here); the task starts from what t3 left and writes down every run and its spend (the count of tokens where the CLI names no cost); the cap is enforced before each call (d5), never by reading the ledger afterwards; a second run of a vendor only after a recorded change; the real sign-in file has the same bytes and modification time before and after, and the job's sign-in copy is gone after the job"
  - id: d3
    item: "a vendor the table refuses, or that is not launchable yet (its mark is off: the must-changes of its judgement are not built and green), is written as not run, with the reason, and is not a missing proof; with both so the task is done when the file says so"
  - id: d4
    item: "SPEC section C8, what the coder comparison of E2 could not show (a harness ran codex and agy in the unit shape of the s4 measure; the product ran neither): the job is started by start_role and runs in the unit, with the preflight rows, the turn check and, for agy, the handler of the product; the fixture repository carries an AGENTS.md that orders a command before any edit AND declares that command as its instruction step; the output of the step holds one rule that no file of the worktree holds (a name the new test must carry); turn 0 of each vendor is not refused, and the commit of the job follows that rule (read from the diff) — the effect of the step, not only the absence of a refusal; the file holds the instructions member of the envelope; the file holds the prompt id and the sha256 of the rendered turn-0 prompt, how the strict reader took the report (for codex: its null members), and for agy the permission_mode of the init line of each turn beside the value of the table"
  - id: d5
    item: "the re-review round of 2026-11-26, finding F3: the reservation rule of t3 d3 holds for every call of this task, with the budget t3 left; a coder job reserves for EVERY turn it may make (turn 0, the repair turns, the fix turns up to the cap of the fix loop); the price table holds the smallest model of each vendor that runs, also where the CLI names no cost (a subscription): its worst case is priced from the vendor's own price of that model, with the source and the date; a vendor whose model has no price in the table, or whose call cannot be bounded, is NOT launched and is written as not run with that reason; the file holds each reservation beside the actual cost (or the count of tokens priced by the same table)"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t5-docs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-docs
title: "Docs: the scout and judge roles, review_plan with featureFolder, the codex and agy coder in ARCHITECTURE.md and README.md; server.md records what E3 decided differently"
kind: docs
complexity: 3
dependsOn:
  - t4-coder-resume-paid-proof
parallel: false
files:
  - ARCHITECTURE.md
  - README.md
  - v2/architecture/server.md
  - research/module_server.md
dod:
  - id: d1
    item: "ARCHITECTURE.md and README.md name the scout and judge roles with their envelopes, review_plan with featureFolder (validate first, the pack, the scope, the limits, what is approved) and the coder of each shipped vendor with the measured limits of its boundary and the release each was measured on (history); the list of roles in both files equals RoleDescriptors.Shipped (a test or a grep in the task result proves it)"
  - id: d2
    item: "the docs say that no version of the CLI of claude, codex or agy refuses or stops anything and none asks for a new measure (the owner's decision of 2026-11-26), that shared/coder-boundary-releases.json holds the verdicts and the history of the measures (measured on which release, when), where a person sees the installed version beside the measured one, and what still refuses a coder (the verdict, a flag the help does not declare, a preflight row, a launch check, a turn check); a new version of a CLI is no reason to edit a doc"
  - id: d3
    item: "server.md gets a section that records what this epic decided differently from its sections 2, 8, 10, 19, 20 and 23; research/module_server.md describes the code as it is"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t6-agy-boundary-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-agy-boundary-scenarios
title: "Boundary scenarios with FakeCli as the agy coder inside the real unit: writes outside, reads of what is hidden, a new unit, a Windows program (moved here from s6, which is at the limit of 6 tasks)"
kind: test
complexity: 5
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/tests/AAgyCoderBoundaryTests.cs
  - shared/coder-boundary-releases.json
dod:
  - id: d1
    item: "V7: the fake coder writes the main checkout, a folder beside the worktree, the .git pointer and the stand-in home: each is stopped by the OS, the job ends coder-boundary and is never green, and the main checkout is byte-equal"
  - id: d2
    item: "V8: a child command of the fake cannot read a file of the hidden main checkout, the worktree of another job, the folder of another job or the sign-in files of the stand-in REAL home, cannot start a unit and cannot run a Windows .exe; a control unit without the additions shows that the same reads then work"
  - id: d3
    item: "said by a test, so that nobody reads the unit as more than it is: a child command of the fake CAN read the job's own sign-in copy inside the unit (accepted risk A1 of the judgement) — for agy only the agent file, the guard and the mode keep a command from existing"
  - id: d4
    item: "V29, the owner's condition of 2026-11-26 (SPEC section C2 item 1 and section C6; ledger entry 1.16): the agy coder becomes launchable by ONE commit of this task, the last task of agy, which turns on the mark launchable of the agy row of shared/coder-boundary-releases.json — and only when the must-changes of the judgement's addendum 3 are built and green: the guard's record with the conversation id and the step index (s6 t2 d3), G15 and the signs of AG-M2 (s6 t5 d3), the read-only guard folder (s6 t3 d2), the copy with no refresh token, the margin before every launch and the refresh unit (s6 t3 d3, s7 t6), the preflight rows (s6 t4) and the failover texts (s6 t5 d2); the commit names the green run of the whole MCP suite it rests on; before it the product refuses an agy coder by data, and a test holds the mark off until then"
```
