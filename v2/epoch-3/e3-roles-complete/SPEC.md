# Spec — epoch 3, E3 "Roles complete: scout, important/judge, codex + agy rows, review_plan(featureFolder)"

> Status: **plan only, nothing built, 2026-11-25.** Plan gate: `proceed`, 2026-11-25; its 5 accepted findings are section J. Scope: the server half (`src_mcp`, `shared/`). Written by the planner
> (Fable) from `v2/` and the code at `feat/v2-ep3-e2-coder` `0e8e8941`, read-only. Parent plan: [PLAN.md](../../PLAN.md);
> architecture: [server.md](../../architecture/server.md) §2, §8, §10, §19, §20, §23. Stories and tasks: the YAML tree under
> `stories/` (8 stories, 39 tasks, no task above complexity 6). Two cornerstones: **s3** (what a plan round over a folder
> approves) and **s4** (the codex and agy write boundary: measure first, then a security judgement).

## 0. Goal, and what is there today

Goal (`epic.yaml` `doneWhen`): scout and judge give strict JSON; codex and agy coder sessions are resumed; `review_plan(featureFolder)`
validates first and packs in topological order.

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

## 1. Decisions that change server.md

1. **The role name is `judge`; its model list is `important`.** `important` stays the name of the list and of the router rule
   (complexity 10, `security: true`, kind `security`). §19 and §20 used one word for the two things.
2. **Evidence is an object `{file, line, text}` and coai checks it.** §19 had strings `file:line`. A string cannot be checked;
   an object with the line's text can.
3. **`review_plan(featureFolder)` has a scope.** The scope is the node that `workItem` names inside the folder (an epoch, an epic or
   a story); with no `workItem` it is the whole feature. The approved thing is the scope and the context the reviewers read with it, not the whole folder (section B).
4. **Resume of a codex and an agy coder session is built here.** §23 left it to E4, and E4's `doneWhen` names it too. E3 builds
   it for one task; E4 keeps the hardening (section G).
5. **A vendor's coder ships only on a judgement verdict.** After s4 a vendor is `ship` or `refuse`. A refused vendor stays refused
   for the coder, as today (section C).
6. **A codex or agy coder runs only on a release that was measured.** An exact list of releases, not a floor (section C2).

## A. Scout and judge

Both are role cases of `start_role` on the road the BA uses: a read-only launch through the consultant adapters, the tree compared
around every attempt (`tree-changed` never fails over), the answer read by a strict reader, at most 2 repair turns in the SAME
session (`RoleFailover.cs:111`), a declared refusal (`status: "refused"`) goes to the next model.

**Request.** `start_role(role: "scout" | "judge", repoPath, brief, taskFile?, model?, timeoutMinutes?, workItem?)`. `brief` is the
question and is required (the cap of `RoleRequestChecks.MaxBriefLength`, 65 536 characters, stays). `taskFile` is optional: a file
inside the repository that the brief is about. The model reads it by its path. Tools: Read, Glob, Grep. No shell, no write.

**Scout answer** — `shared/scout-output.schema.json`, reader `src_mcp/core/Roles/ScoutOutput.cs`:

```
{ status: "ok",
  facts:   [ { claim (1..400 chars), evidence: [ { file, line (>= 1), text (1..300 chars) } ] (1..5) } ] (0..40),
  files:   [ repository-relative path ] (0..60),        the files a reader of the task needs
  unknown: [ text (1..300 chars) ] (0..20) }            what the scout looked for and did not find
{ status: "refused", reason }
```

**Judge answer** — `shared/judge-output.schema.json`, reader `src_mcp/core/Roles/JudgeOutput.cs`:

```
{ status: "ok",
  verdict: "approve" | "revise" | "reject",
  summary (1..1500 chars),
  findings: [ { id ("f1", "f2", ...), severity: "blocking" | "major" | "minor" | "nit", title (1..120), detail (1..1500),
                evidence: [ { file, line, text } ] (0..5), fix (0..600) } ] (0..30),
  decision: { chosen (1..120), why (1..1500), rejected: [ { option, why } ] (0..8) } }      only when the brief asks to choose
{ status: "refused", reason }
```

The severity words are the gate's own four (`src_mcp/core/Findings/FindingSchema.cs:35`). Rules of the judge reader: `approve` with
a `blocking` finding is an error; `revise` or `reject` with no finding is an error; a finding id is used once.

**The house shape.** Both files have the two arms of `ba-output.schema.json` and `coder-report.schema.json`; an unknown member is an
error; neither half has a JSON Schema engine, so a test holds each file and its reader equal.

**The evidence check** (`src_mcp/core/Roles/RoleEvidence.cs`, pure; files through `RealPath`, the one road that resolves links). An
item is `verified` when the file is in the tree the job read and that line holds `text` after white space is collapsed. A path that
is absolute, has a drive or `..`, runs through a link, or is a credential-shaped file is an ERROR of the answer; the file is not
opened. A `text` that is empty or only white space is an ERROR of the answer too. It is decided before any line is read (in the
reader and again in the check), so such an item can never be verified, also not against a blank line. A file over 2 MiB or not
UTF-8 gives `verified: false`.

**The envelope** (`result.json`, built by coai, never copied from the model): the answer, with `verified` and `why` on every
evidence item, `exists` on every file, `counts {facts, evidence, verified, filesMissing}`, `repairTurns` and the HEAD commit of
the checkout at the start of the job. A shape error and an unverified item both go back as a repair turn. After the last turn a shape error ends `output-invalid`;
an unverified item stays in the envelope with `verified: false`. It is never shown as verified and never dropped.

**The judge writes no file.** A design decision is `decision`; a review is `findings`. A cornerstone spec as a document is not a
judge output in E3 (section G).

**Routes.** Scout: the `scout` list. Judge: the `important` list, and a model the caller names that is not in that list is refused
before any folder (`JudgeRoute`, the twin of `CoderRoute.Narrow`). A scout for a task that is important takes the important list
(the router's rule 1, unchanged).

Scenarios: **S1** valid at turn 0; the recorded argv is the read-only set. **S2** a shape error once → one repair turn in the same
session. **S3** wrong three times → `output-invalid`, no result. **S4** a line out of range, a text not on the line, a file that is
not there → repair, then `verified: false` with the count. **S5** a path that leaves the repository or a credential-shaped file →
error, never opened. **S6** a declared refusal → next model, one line and one notice; list exhausted → `failed`. **S7** the tree
changes → `tree-changed`, no failover. **S8** the three judge rules. **S9** the judge's route; a named model outside the list.
**S10** scout and judge on a codex row and an agy row: the read-only argv of each CLI; no path to the coder case of
`LaunchConfinement`. **S11** an empty brief is refused before any folder. **S12** an evidence text that is empty or only white
space → error before the line check; never `verified: true`.

## B. `review_plan(featureFolder)`

`review_plan(repoPath, branch, planText? | committedPlan? | featureFolder?, expectedCommit?, workItem?)`: exactly one of the three.

1. **Read from git at the branch tip.** The tip is resolved once and is the round's commit (as for `committedPlan`).
   `GitWorkFolder` (`src_mcp/runners/Collecting/`) implements `IWorkFolder` over that commit: one `ls-tree`, each blob proved against
   its id. A link and a submodule entry are not followed. The working tree is never read for the text.
2. **Validate first.** The whole feature is checked by `WorkPlanValidator` with the device limits, the same as `validate_work`.
   Invalid → refused with at most 10 errors (code, node, message), the total and the limits' source. No reviewer is launched, no
   round row is written, the stage does not move, no worktree is made.
3. **Pack in topological order.** `src_mcp/core/Work/WorkPack.cs`, pure. Order: `feature.yaml`; the feature's `plan` file; the
   ancestors of the scope (`epoch.yaml`, `epic.yaml`, the epic's `spec` file); then each story after the stories it depends on,
   and in a story each task after the tasks it depends on. Ties go by the number in the id. One line (node and title) for each node
   outside the scope that a scope node depends on. The same commit gives the same bytes.
4. **What the reviewers see.** A heading, then the source line: `(packed by the server from <scope> at commit <sha>, approval <id>,
   N files, M bytes; validate_work: valid, limits <source>)`, then a line **Not sent**: the `architecture` files of
   `feature.yaml` with their sizes, the free `*.md` files beside the nodes, the nodes outside the scope (a count). Then one section
   per file: `## <repository-relative path>` and the file in a fence. The fence is one backtick longer than the longest run in the
   file, so a file cannot end its own section.
5. **Size limits.** A YAML file: 64 KiB (`StrictYamlReader.cs:22`). A `plan` or `spec` file: 64 KiB. The folder: 2 000 entries.
   The pack: 256 KiB (server.md §8; the ceiling a committed plan has today, `src_mcp/core/Rounds/DocumentRules.cs:31`). Over a
   limit → refused with the size and the three largest sections. Nothing is cut. Measured on 2026-11-25, before the files of this
   epic: the whole `v2` folder was 149 YAML files and 94 KB.
6. **The working tree guard.** When HEAD is the round's commit, an edited, staged or untracked plan file under the scope is a
   refusal that names the files. When HEAD is another commit, the note says that the working tree was not compared.
7. **What is approved: everything the reviewers read.** `PlanSource` gets origin `folder`: `Path` is the scope folder, `Blob` is
   the **approval id**, `TextId` is the id of the pack. The approval id is SHA-256 over these lines, in pack order:
   - `F <blob id> <path>` for EVERY file whose text is in the pack: the scope files, and the context outside the scope
     (`feature.yaml`, the plan file, the YAML of the ancestors, a `spec` file);
   - `D <node> <title>` for every dependency outside the scope, exactly as the pack shows it.
   It is not the git tree id: a `RESULTS_*.md` written into the epic folder later is not in the pack and does not change it. The
   files of the line **Not sent** were not reviewed and are not in the id.
8. **The coder binding** (`CoderPlanBinding`): a job started under a folder round is bound when its task file is inside the scope,
   its base descends from the round's commit, and the approval id computed at the base equals the approved one. A task outside the
   scope is refused. After the approval a change of a scope file, of `feature.yaml`, of the plan file, of an ancestor's YAML, or of
   the id or title of an outside dependency refuses a coder start. The sentence names the first path or node that differs: coai
   builds the list at the round's commit and at the base and compares them. The cure is a new plan round.
9. **Old readers.** `folder` is a new value of a stored field. The previous release reads it as "no source" and no error
   (`PlanSource.IsWellFormed` and the extension's `reviewedFrom` rule). This is measured, not assumed (s3 t4 d5).

Scenarios: **P1** valid → reviewers, the pack in order, the same bytes twice. **P2** invalid → refused, zero launches, no round
row. **P3** two inputs at once; `expectedCommit` not the tip. **P4** the working tree guard. **P5** a pack over 256 KiB. **P6**
scope by `workItem`; a `workItem` that names no node of the folder. **P7** order vectors (ties, a story that depends on a story of
another epic, a dependency outside the scope). **P8** a link, a submodule. **P9** the binding: a task inside and outside the scope; a changed scope file; changed context (`feature.yaml`, the plan file, an
ancestor, the title of an outside dependency); a new RESULTS file and a node nobody in the scope depends on do not break it. **P10** the old reader. **P11** a slow git inside the 45 s deadline of `PlanInputs`. **P12** a device cap of 3 with
4 epics → refused, source `device`.

## C. The codex and agy coder

### C1. The bar: the rows of the claude boundary

"At least as strong as claude" is this list. Each row was measured for claude 2.1.330 in
`v2/spikes/RESULTS_coder_write_boundary.md` (rows R11, R11g, R11l, M1 to M7), `RESULTS_s2_check_boundary.md` (the unit and its
preflight) and `KEY_POOL_SPEC.md` §1.

| Row | What must be true | What holds it for claude |
|---|---|---|
| B1 | an edit of a file in the worktree works | flags, unit |
| B2 | a write to the main checkout is refused | flags (R11), unit (read-only) |
| B3 | a write beside the worktree or into another job's worktree is refused | flags, unit |
| B4 | a write to `<worktree>/.git` and to the main `.git` is refused | flags (R11g), unit |
| B5 | a write to the real home is refused | flags (M6), unit (a home of the job's own) |
| B6 | a read of a file outside the worktree is refused | **flags only** (R11, M1); the unit does not stop reads |
| B7 | a list or a search outside the worktree is refused | **flags only** (M2, M3) |
| B8 | a write through a file link or a folder link out of the worktree is refused | flags (R11l, M4), unit |
| B9 | no command leaves the limit: no new unit, no D-Bus, no Windows `.exe` | **claude has no shell** (M5) |
| B10 | the model has no network tool of its own | **the tool list** (M5: five file tools) |
| B11 | the model cannot read the sign-in or the launch's secret | flags (B6); KEY_POOL_SPEC §1 C6 |
| B12 | the tools are exactly the measured list; no MCP server, connector or plugin | M5 |
| B13 | files of the repository at the base commit do not run code and do not widen the limit | `--restricted` (t3 d0) |
| B14 | a resumed turn has the same limit; the handle is bound to the job and the worktree | R11 resumed, M7, `CoderHandle` |
| B15 | only the allow-list environment reaches the CLI | E2 s2 t3 d4, t5 |

**The rule.** A vendor's coder ships only when EVERY row was measured as holding for that vendor, fresh and resumed, by a CLI flag
or by a property of the unit. "Asked for" is not "held" (the security rule *a measure you have not observed is a comment*).

**Why this is not a copy of the claude work.** A codex session works through a shell. agy has command tools. So B6, B7, B9, B10
and B11 cannot come from a tool list. They must come from the unit (hide a path, not only make it read-only) or from the vendor's
own sandbox, seen working INSIDE the unit. The unit of E2 binds the main checkout read-only: a child command can read it.

### C2. How the role refuses

1. **Build:** one table, `shared/coder-boundary-releases.json`, read by `CoderVendors` (`src_mcp/runners/Consultation/`). Per
   vendor it holds the verdict (`ship` or `refuse`), the measured releases, and the ids of the properties the verdict relies on.
   It is filled only from the judgement file. A vendor with `refuse`, or with no release, is dropped from a coder route with a
   sentence; a model the caller named on it is refused (the rule of `CoderRoute.Narrow`).
2. **Launch: only a measured release.** A codex or agy coder starts only when the installed release is EXACTLY one of the releases
   of the table, and the help declares every flag of the measured set. An older release, a newer release and a release the CLI
   does not name are each refused before the launch. There is no floor and no range in E3: a release between two measured ones was
   not measured. (claude keeps the floor of E2, `ClaudeWriteCapability.cs:23`; E3 does not change it.)
3. **Job: every property the verdict relies on is probed.** The preflight runs with no model, inside the EXACT launch unit, before
   each job. It has one row for each id of the table: what is hidden from a child command, the sign-in files, the network limit,
   no user manager inside, the vendor's own sandbox or guard where the verdict relies on it. A wrong answer ends `coder-boundary`
   before any launch and names the row. A test holds the rows and the ids of the table equal, and each row goes red when its
   property is dropped.

**How the list of releases is updated.** (1) Run the row measure of s4 on the new release: the same rows B1 to B15, fresh and
resumed, the same canaries; the results go under a heading with the release in the vendor's results file. (2) The important model
writes a judgement for that release (a new section of the judgement file). (3) ONE commit adds the release to the table with the
two headings. A test reads the repository and fails for an entry whose results heading or judgement verdict is not there. Until
then the vendor's coder is refused on that release with this cure, and the route goes on to the next vendor.

No setting makes a weaker launch. No `cwd-only` mode. Native Windows and macOS stay refused.

### C3. What each CLI offers (read from `--help` on 2026-11-25; a flag in a help text is a request until s4 sees its effect)

| | codex-cli 0.171.0 | agy 1.5.16 |
|---|---|---|
| Resume | `codex exec resume <id>`: has `-c`, `-m`, `--json`, `-o`, `--output-schema`; has no `-s`, no `-C`, no `--add-dir` | `--conversation <id>` (also `--resume`, `-r`) |
| Write limit | `exec -s read-only \| workspace-write \| danger-full-access`; on resume only `-c sandbox_mode=…` | `--mode accept-edits \| auto \| plan`; `--sandbox` ("terminal restrictions"); `--agent`; a `PreToolUse` hook |
| Model-free probe | `codex sandbox <command>` runs a command in codex's own sandbox | the hook handler is coai's own program (`--agy-hook`) |
| Structured answer | `--output-schema <file>` on `exec` and on `exec resume` | `--json-schema` |
| Never in a coai argv | `--dangerously-bypass-approvals-and-sandbox`, `--dangerously-bypass-hook-trust`, `-s danger-full-access`, `--trust`, `--worktree`, `--ephemeral` (a coder turn), `--add-dir` | `--dangerously-skip-permissions`, `--remote-agent`, `--remote-control`, `--continue` |

Known from the code: codex names its thread in `thread.started` and a lost session says "no rollout found"
(`CodexConsultant.cs:144`); agy reports usage as the sum of the conversation (`AntigravityConsultant.cs:138`); agy's read-only block
is an agent with one tool plus a hook that also checks the path (`AntigravityReadOnly.cs:46`).

### C4. Measure first (story s4) — the unknowns

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

Each paid measure uses the vendor's smallest model (the vendor-routing rule: never a Claude model on codex or agy), in scratch
folders, with a home in the scratch folder. Caps: resume tasks 6 calls each, row tasks 16 calls each; every call and its spend are
written down (tokens where the CLI names no cost).

### C5. The security judgement point (s4 t6)

Before any argv is frozen and before any code of s5 or s6: the important model (the `important` list), read-only, reads the two
results files and the claude files. It writes `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md`: per vendor `ship` or
`refuse`, for each row B1 to B15 which layer holds it, and a must-change list. The task files of s5 and s6 are changed from that
list in the same commit (E2 did this for s2). The judgement also lists, with ids, every property each verdict relies on (C2).

**A refused vendor never blocks the other one.** The stories are split for this: s7 (the seam, the table, the fix loop through
the seam, the rows) depends only on s4 and is built in every case. s5 (codex) and s6 (agy) each depend on s4 and s7, never on each
other. The judgement commit applies its own verdicts:

| codex | agy | s5 | s6 | Failover test of s8 t1 | Acceptance and paid proof | `doneWhen` line 2 |
|---|---|---|---|---|---|---|
| ship | ship | built | built | claude → codex → agy | both vendors resumed | met |
| ship | refuse | built | folder removed | claude → codex | codex resumed; a start on agy is refused | the owner narrows it to codex |
| refuse | ship | folder removed | built | claude → agy | agy resumed; a start on codex is refused | the owner narrows it to agy |
| refuse | refuse | folder removed | folder removed | claude models only | a start on each is refused; no paid coder run | not met; the owner decides |

"Folder removed" means: the judgement commit deletes the story folder of the refused vendor and its entry in the `dependsOn` of
s8, and writes the reason in the judgement file. `validate_work` accepts the folder after that commit, and no story waits for one
that is not built. The plan changed, so the epic gets a new plan round. A refused vendor is never a required launch: the route
drops it, the tests of s7 and s8 run all four combinations with an injected table, and in every one the fake CLI records zero
launches of a refused vendor.

### C6. The launch and the resume (the shape; the argv is frozen by s4)

- One seam per vendor (`CoderVendors`, story s7): the capability probe, the fresh and the resumed argv, the home layout, the sign-in bind or
  the variable name, the program's real path. The claude argv and the claude unit stay byte for byte.
- codex: turn 0 is `codex exec` with the measured set, cwd = the worktree, `CODEX_HOME` inside the job's coder home. A fix turn is
  `codex exec resume <thread of turn 0>` with the limit as a `-c` override, from the same folder, with the same home.
- agy: turn 0 with a coder agent (the readers and the measured write tools) and a write guard: the hook allows a write tool only
  when the target, resolved through links, is inside the job's worktree and is not `.git`; it denies a command tool, an unknown
  tool and a payload it cannot read. A fix turn is `--conversation <id of turn 0>`. The read-only agent and hook do not change.
- `CoderHandle` (`CoderHandle.cs:17`) gets the vendor. A handle of another vendor, job or worktree is refused before a process
  starts.
- The job's coder home lives as long as the job: the session file of a CLI is in it (the rule E2 found for claude).
- A lost session on a fix turn is a `crash`: the next model starts from the last coai commit. No fix turn starts a new session by
  itself.
- The fix loop, the commits, the checks and their boundary do not change (`CoderFixLoop`, `CheckBoundary`).

Scenarios: **V1** codex fresh argv = the measured set. **V2** codex fix turn = `exec resume` with the thread of turn 0. **V3**, **V4**
the same for agy. **V5** a handle of another job, worktree or vendor. **V6** a release that is not in the table (older, newer, not named), or a flag not in
its help. **V7** the fake as coder inside the real unit: a write to the main checkout, beside the worktree, to `.git`, to the home
→ stopped, `coder-boundary`, never green. **V8** a child command cannot read what is hidden, cannot start a unit, cannot run an
`.exe`; a control unit shows the reads work without the additions. **V9** one preflight row for every id of the table; each goes red when its property is dropped.
**V10** no user manager, native Windows, macOS → refused. **V11** a vendor that is not shipped. **V12** failover in each of the four combinations of C5; zero launches of a refused
vendor. **V13** a lost session. **V14** the environment and no secret in an argv. **V15** the agy write guard. **V16** agy usage per
turn.

## D. Rows and the failover order

**The rule does not change** (E1 spec A): a limit, a refusal or a crash goes to the next model at once; only the last model waits;
a spent quota, a missing CLI or a lost sign-in skips that vendor's other models; `cancelled`, `tree-changed`, `output-invalid` and
the coder's own stops never fail over. A read-only role restarts from turn 0. A coder restarts from the last coai commit.

**The lists** (main AI claude; `shared/role-defaults.json`). The names there are the owner's, so the right column is a proposal
(section H, question 3):

| Role | Today | Proposed for E3 |
|---|---|---|
| scout | sonnet | sonnet → haiku → codex `terra` (only with a codex row) |
| judge and important tasks (`important`) | fable → codex `astra` (only with a codex row) | no change |
| ba | fable → opus | no change |
| coder | the band model, then sonnet | the band model, then sonnet → opus → codex `terra` → agy `gemini-4-argon-medium` (each other vendor only with its row AND a shipped write launch) |

**The codex and agy main-AI rows** are empty today ("no measured model list"). s4 t1 measures the lists; s7 t1 fills the rows with
measured names only. Which model takes which role is the owner's word. Seen on 2026-11-25, not yet a record: `agy models` of 1.5.16
prints ids (27 names; `gemini-4-argon-medium` and `gemini-3.8-flash-low` are among them; it also lists Claude models, which the
vendor-routing rule never uses there). s4 t1 writes the record.

Every switch is one log line and one `role-model-switched` notice that names the vendor the repository text now goes to (E1).

## E. Security rules of every new launch road

1. **No secret in an argv or a log.** A vendor's credential reaches a CLI as a file bind or by variable NAME, with the inner command
   shape that `KEY_POOL_SPEC.md` §1 row C4 measured (0 of 300 in `/proc/<pid>/cmdline`). A canary value is searched in every recorded argv (V14).
2. **A boundary is measured, and refused when it is not effective** (C1, C2). Presence of a flag is not a test.
3. **Tests never touch the real home.** Every test runs with a temp HOME and data folder; the real home is the `BoundaryHost.RealHome`
   seam. A measure task works in scratch folders and proves by hash and time that the real sign-in file did not change.
4. **Exe and argv, no shell string, a timeout that kills the tree**: every new process goes through `ProcessLauncher` or a unit that is
   stopped by name.
5. **One road.** The vendor decision is one table, and a scan test fails when a second place decides it. The evidence check is the
   one way a scout or judge path reaches a file.
6. **A model's words are data.** A path in an answer is checked before a file is opened; a file of the plan folder cannot end its
   own section of the pack; a plan file name from YAML (`plan`, `spec`) goes through the path rule of `committedPlan`.
7. **Failover sends repository text to another vendor.** The notice names the vendor (E1 risk 5). A unit holds only its own
   vendor's sign-in.

## F. Growth and interruption

| What grows | Size | Who retires it | When interrupted |
|---|---|---|---|
| scout and judge job folders | the E1 job folder (attempts cut at 20 MB) | E1 retention: 30 days, 200 at most | E1 lease and sweep |
| a codex or agy coder home (session files, caches) under the job's coder folder | not measured; s4 records it | the sweep that removes the coder worktree and job folder (E2) | a killed job leaves it for that sweep |
| the agy write guard folder | 3 small files per job, inside the job folder | with the job folder | written and read back before every launch |
| the pack text kept with a session | 256 KiB at most per plan round | the session's own retention | nothing is written before a round exists |
| `shared/` schemas and vectors, `role-stops` fixtures | static files | — | — |

## G. Not in E3, and the boundaries with the other epics

| Item | Built by | E3's part |
|---|---|---|
| Resume of a codex and an agy coder session, for one task | **E3** (s5, s6) | E4's `doneWhen` line now says so: the resume is from E2 (claude) and E3 (codex, agy); E4 keeps the caps, the no-progress stop and the flaky re-run for every shipped vendor |
| The formatter turn, test selection, flaky re-run, the stop on a repeated failure | E4 | none |
| Detection, default argv, parsers of other stacks | E4 | none |
| Parallel tasks, slots, merges, a `review_code` started by coai | E5 | none |
| The claude key pool | E2 s7 (branch `feat/v2-ep3-e2-s7-keypool`) | s7 starts after that branch is merged: both change `CoderHandle`, `CoderUnit`, `CoderLaunches` and `RoleFailover` |
| A status that coai writes into `task.yaml` | E5 / epoch 4 E5 | it would change the approval id of a scope; that epic decides how |

Also not in E3: a pool of codex or agy credentials; `cons`, `consask` and reviewers on the pool; a coder on native Windows or macOS;
`review_feature(featureFolder)` (server.md §8 names it; no epic's `doneWhen` holds it); a document written by the judge; attachments;
the extension showing a `folder` source (extension half).

Order inside the epic: s1 → s2; s3 alone; s4 → s7 → s5 and s6 (side by side, neither needs the other); s8 last. s3 and s4 can
start at once. The story numbers are not the order: s7 comes before s5 and s6. The codex and agy stories need E2's coder, and
`epic.yaml` of E3 now names `e2-coder-one-task` in `dependsOn`.

## H. Risks, and what only the owner can answer

Risks:

1. A vendor can come out as `refuse`. Then line 2 of `doneWhen` is not met for it, by design.
2. The sign-in of codex or agy inside a unit: a token refresh that cannot be saved may end the person's sign-in. The same risk was
   open for claude and led to the key pool. s4 does not test a refresh with the real sign-in.
3. `evidence.text` costs repair turns when a model copies a line badly. The paid proof counts them.
4. A role job has no spend cap of its own. The paid tasks state a cap and stop from the ledger.
5. A plan valid on one device can be invalid on another (limits are per device); the refusal names the source.
6. The fable judge is the most costly model. The proof runs the judge on haiku through a scratch `important` list.
7. An update of the codex or agy CLI refuses that vendor's coder until the new release is measured and judged (C2). This is the
   cost of "only a measured release"; the route goes on to the next vendor.
8. A change of `feature.yaml` or of the plan file after an approval refuses every coder start under that approval (B7). The cure
   is a new plan round.

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

## I. Build order, test plan, Definition of Done

Build order: the story order of section G; in a story the `dependsOn` of its tasks. Measure tasks come before the code that
depends on them, and the judgement comes before any codex or agy launch code.

Test plan: every scenario S1 to S12, P1 to P12 and V1 to V16 has a test that carries its number in its name, or the task result says
which test covers it. FakeCli is the only double; the units, git, the detached runner and stdio are real. Structural scans have a
companion that proves they match something. The whole MCP suite runs through the MTP executable, never `dotnet test`.

Definition of Done:

- [ ] `start_role` runs `scout` and `judge`; each envelope is strict JSON built by coai, with checked evidence.
- [ ] `review_plan(featureFolder)` refuses an invalid folder before any round and packs a valid one in topological order; a coder
      job is bound to the approved scope and to the context the reviewers read.
- [ ] For each vendor the judgement shipped: a coder job fixes a red check in the SAME session of turn 0, on a measured release,
      inside a boundary whose rows B1 to B15 were measured and whose relied-on properties the preflight probes before each job. For a vendor it refused: the refusal is in the product and in the results, and the owner
      decided line 2.
- [ ] The codex and agy rows of `shared/role-defaults.json` hold measured names only.
- [ ] The epic acceptance passes on the real binary; the paid proofs are inside their caps and written down.
- [ ] `validate_work` accepts the `v2` folder; `plan-lifecycle.mjs` is clean; ARCHITECTURE.md, README.md and server.md say what was
      built and what was not.

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
