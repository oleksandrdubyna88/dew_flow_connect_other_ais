# Spec — epoch 3, E3 "Roles complete: scout, important/judge, codex + agy rows, review_plan(featureFolder)"

> Status: **the revised plan of the re-review of 2026-11-27. Its plan round said `proceed` (coai session `da911775`),
> and the owner approved it on 2026-11-27: epoch 3 of the ledger is open** (entries 2.1 and 2.2 of
> [PLAN.md](../../PLAN.md) closed epoch 2).
> [SPEC_CHANGES.md](SPEC_CHANGES.md) holds sections J to T — Q and R are the two entries, S the revision, T the plan
> round and the lead's review — and the records moved out of this file: a spec file may hold 64 KiB. By the git
> history of this branch: s1 to s7 have their code, and the codex and the agy coder are launchable since 2026-11-27
> (`85134aec`, `b9df00dc`); of s8, t1, t2 and t6 are built, t5 is built but for d4, and the real runs of t3 ran on
> Sonnet, not on haiku. **Open:** s2 t5 (not built); s6 t3 d3 (the 30 minutes); s7 t4 d3 (the tool text still says
> that codex and agy are not launchable) and d4 (the main-AI rows of codex and agy are empty: their coder runs
> through the failover of the claude row or a person's setting); s8 t3 d1 (a new run after the fix of the `haiku`
> alias); s8 t4; s8 t5 d4. Scope: the server half (`src_mcp`, `shared/`, `.coai/toolchain.yaml`). Parent plan:
> [PLAN.md](../../PLAN.md); architecture: [server.md](../../architecture/server.md) §2, §8, §10, §19, §20, §23. Stories
> and tasks: the YAML tree under `stories/` (8 stories, 45 tasks, no task above complexity 6). Cornerstones: s3 and s4.

## 0. Goal, and what is there today

Goal (`epic.yaml` `doneWhen`): scout and judge give strict JSON; codex and agy coder sessions are resumed; `review_plan(featureFolder)`
validates first and packs in topological order.

What the code had when this epic was planned (checked at `0e8e8941`, before any task of E3) is in `SPEC_CHANGES.md`,
section 0-T, word for word. It is history: the code as it is, is in `research/`.

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
6. **A version of a CLI gates nothing, for claude, codex and agy alike** (the owner, 2026-11-26; it replaces "only on a
   release that was measured"). A vendor ships by its verdict; what was measured is history, "measured on X" (section C2).

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
   outside the scope that a scope node depends on, **in the pack text of EVERY scope: an epoch, an epic and a story** (the
   owner, 2026-11-26). The same commit gives the same bytes. Until then the code wrote the line for a story scope only
   (`src_mcp/core/Work/WorkPack.cs:108`), so the text id of an epoch or epic pack that has an outside dependency moves.
   The `D` lines were in the approval since s3 t4: the dependency was approved before and is now also read. The change
   raises the pack format version (item 7; re-review round, F1), so the approval id of EVERY scope moves once with it.
4. **What the reviewers see.** A heading, then the source line: `(packed by the server from <scope> at commit <sha>, approval <id>,
   N files, M bytes; validate_work: valid, limits <source>)`, then a line **Not sent**: the `architecture` files of
   `feature.yaml` with their sizes, the free `*.md` files beside the nodes, the nodes outside the scope (a count). Then, for
   every scope that has one, the line **Depends on, outside the scope**: each node with its title, in the words of the `D`
   lines of the approval, so what the approval id covers is what the reviewers read. Then one section
   per file: `## <repository-relative path>` and the file in a fence. The fence is one backtick longer than the longest run in the
   file, so a file cannot end its own section.
5. **Size limits.** A YAML file: 64 KiB (`StrictYamlReader.cs:22`). A `plan` or `spec` file: 64 KiB. The folder: 2 000 entries.
   The pack: 256 KiB (server.md §8; the ceiling a committed plan has today, `src_mcp/core/Rounds/DocumentRules.cs:31`). Over a
   limit → refused with the size and the three largest sections. Nothing is cut. Measured on 2026-11-25, before the files of this
   epic: the whole `v2` folder was 149 YAML files and 94 KB.
6. **The working tree guard.** When HEAD is the round's commit, EVERY file of the pack is compared with the working tree: the
   scope files AND the context outside the scope (`feature.yaml`, the plan file, the YAML of the ancestors, a `spec` file). An
   edited, a staged or a deleted one is a refusal that names the files, and so is an untracked `.yaml` file under the scope.
   The guard takes its list from the pack, so what the reviewers read and what is compared cannot differ. A file that is not
   in the pack is no reason to refuse. When HEAD is another commit, the note says that the working tree was not compared.
   (The code of s3 t5 compares the packed paths already; its tests cover the scope only. s3 t6 adds the tests of the
   context and the sentence that names it.)
7. **What is approved: everything the reviewers read.** `PlanSource` gets origin `folder`: `Path` is the scope folder, `Blob` is
   the **approval id**, `TextId` is the id of the pack. The approval id is SHA-256 over these lines, in pack order:
   - first `V <pack format version>` (re-review round, F1): one constant beside the pack code, raised by every change of
     what a pack's text or its approval lines hold. The dependency line of item 3 makes it 2; an older id has no `V` line;
   - `F <blob id> <path>` for EVERY file whose text is in the pack: the scope files, and the context outside the scope
     (`feature.yaml`, the plan file, the YAML of the ancestors, a `spec` file);
   - `D <node> <title>` for every dependency outside the scope, exactly as the pack shows it.
   It is not the git tree id: a `RESULTS_*.md` written into the epic folder later is not in the pack and does not change it. The
   files of the line **Not sent** were not reviewed and are not in the id.
8. **The coder binding** (`CoderPlanBinding`): a job started under a folder round is bound when its task file is inside the scope,
   its base descends from the round's commit, and the approval id computed at the base equals the approved one. A task outside the
   scope is refused. After the approval a change of a scope file, of `feature.yaml`, of the plan file, of an ancestor's YAML, or of
   the id or title of an outside dependency refuses a coder start. The sentence names the first path or node that differs: coai
   builds the list at the round's commit and at the base and compares them. The cure is a new plan round. **An approval
   made under an older pack format binds no coder job** (F1): the binding first computes the id at the round's OWN commit,
   and when that is not the approved id, the refusal says that the pack format changed since the approval and that a new
   plan round is needed. It names no path: no file changed.
9. **Old readers.** `folder` is a new value of a stored field. The previous release reads it as "no source" and no error
   (`PlanSource.IsWellFormed` and the extension's `reviewedFrom` rule). This is measured, not assumed (s3 t4 d5).

Scenarios: **P1** valid → reviewers, the pack in order, the same bytes twice. **P2** invalid → refused, zero launches, no round
row. **P3** two inputs at once; `expectedCommit` not the tip. **P4** the working tree guard: a scope file, and each file of the packed context outside the scope. **P5** a pack over 256 KiB. **P6**
scope by `workItem`; a `workItem` that names no node of the folder. **P7** order vectors (ties, a story that depends on a story of
another epic, a dependency outside the scope, and the line of the outside dependencies in the text of an epoch, an epic and a story scope). **P8** a link, a submodule. **P9** the binding: a task inside and outside the scope; a changed scope file; changed context (`feature.yaml`, the plan file, an
ancestor, the title of an outside dependency); a new RESULTS file and a node nobody in the scope depends on do not break it. **P10** the old reader. **P11** a slow git inside the 45 s deadline of `PlanInputs`. **P12** a device cap of 3 with
4 epics → refused, source `device`. **P13** an approval whose id was made under an older pack format → a coder start is refused,
the sentence says that a new plan round is needed and names no path; the same approval made under today's format binds.

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
| B12 | the tools are exactly the measured list (codex since section Q: no tool outside it); no MCP server, connector or plugin | M5 |
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
   vendor it holds the verdict (`ship` or `refuse`), the ids of the properties the verdict relies on, each with its class,
   and the HISTORY of its measures: for each one the release, the date, the sha256 of the program file and the two
   headings ("measured on X", the key `measured`; both vendors have one since the addenda 2 and 3 of the judgement). It is
   filled only from the judgement file. The history makes nothing launchable. A vendor with `refuse` is dropped from a
   coder route with a sentence; a model the caller named on it is refused (`CoderRoute.Narrow`). **A vendor with `ship` is
   launchable only when the must-changes of its judgement are built and green** (the owner, 2026-11-26: "not before"):
   the row carries a mark, `launchable`, that is off for codex and agy and that ONE commit turns on — s5 t5 for codex,
   s8 t6 for agy. Until then the product refuses the vendor's coder by data. The must-changes are in C6.
2. **Launch: the installed version is recorded and gates nothing** (the owner, 2026-11-26, for EVERY vendor — claude, codex
   and agy: a new version "must never stop anything, or rewrite docs, pins and so on"). No release, version, hash or pin
   of a CLI is compared with a record: an older, a newer and an unnamed version are launched like the measured one, and
   none refuses, stops a job, or asks for a new measure, a new judgement or a doc rewrite. The envelope and the job's
   record name the installed version beside the last measured one, for information. So the floor of claude
   (`ClaudeWriteCapability.cs:23`, 2.1.330) stops refusing and becomes claude's history record, and there is no pin by
   bytes and no program store (judgement G9, ids CX-P1, AG-P1, AG-P2). What still refuses asks the installed CLI what it
   DOES, never which version it is: a flag of the argv that its help does not declare, an item type of the stream that
   nobody measured, and the checks of items 3 and 4. Such a refusal names the property. These checks stay, and one of
   them may stop a job after a CLI updated itself: the owner accepted that on 2026-11-26 as protection, not a version gate.
3. **Job: every property the verdict relies on is probed.** The preflight runs with no model, inside the EXACT launch unit, before
   each job. It has one row for each id of class `pre` of the table: what is hidden from a child command, the sign-in files,
   the network limit where one exists (for codex: a command of the model has no network, CX-V4), no user manager inside, the
   vendor's own sandbox or guard where the verdict relies on it. A wrong answer ends `coder-boundary`
   before any launch and names the row (a codex tool list that cannot be read or holds an unknown tool, at the first launch of an entry: `model-unavailable`, section S). A test holds the rows and the `pre` ids of the table equal, and each row goes red when
   its property is dropped.
4. **The other ids are asked where they can be asked** (the judgement, section 8). Class `launch`: by coai before every launch,
   turn 0 and every fix turn, on the files the unit will get. Class `turn`: read by coai from the CLI's own stream after every
   turn, before anything of that turn is committed. Class `release`: it cannot be asked without a model turn, so it was measured,
   fresh and resumed, on the release its history record names; on another installed version it is assumed (risk 13). A `release` id has no
   preflight row: such a row could only check coai's own half and would prove nothing. The network of the CLI process itself
   has no row either: no property of a user unit limits the hosts of a process (judgement G11; the forwarder is E4's).

**How the history grows.** Nobody has to measure a new version. When a measure is made (the open rows of s5 t6 and s6 t7 now;
any later one by choice), its results go under a heading with the release in the vendor's results file, the important model
writes its addendum of the judgement, and ONE commit writes the addendum's verdict and the history record into the table. A
test reads the repository and fails for a record whose results heading or judgement verdict is not there. No commit of this
kind is needed for a vendor to keep running after its CLI updated itself.

No setting makes a weaker launch. No `cwd-only` mode. Native Windows and macOS stay refused.

### C3. What each CLI offers (read from `--help` on 2026-11-25; a flag in a help text is a request until s4 sees its effect)

What each CLI offered when its help was read (codex-cli 0.171.0, agy 1.5.16) is history: `SPEC_CHANGES.md`, section C3-H,
word for word. The rule that stays, for every version — never in a coai argv: codex `--dangerously-bypass-approvals-and-sandbox`, `--dangerously-bypass-hook-trust`, `-s danger-full-access`, `--trust`, `--worktree`, `--ephemeral` (a coder turn), `--add-dir`; agy `--dangerously-skip-permissions`, `--remote-agent`, `--remote-control`, `--continue`.

### C4. Measure first (story s4) — the unknowns

The 13 unknowns U1 to U13 that s4 measured (done; the task files of s4 cite them by number) are in `SPEC_CHANGES.md`,
section C4-U, word for word.

The rule of every paid measure and the spent caps of s4 are there too. The caps of the open measures and proofs are in section H.

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

"Folder removed" (the judgement commit deletes the story folder of a refused vendor; no vendor was refused):
`SPEC_CHANGES.md`, C5-F.

### C6. The launch and the resume (the shape; the argv is frozen by s4)

- One seam per vendor (`CoderVendors`, story s7): the capability probe, the fresh and the resumed argv, the property list and
  the working directory of the unit, the environment allow-list, the home layout, the sign-in recipe, the program's real path
  and the coder prompt (C7). The claude argv and the claude unit stay byte for byte.
- In the unit of codex and of agy the main checkout and the coder root are HIDDEN, not read-only (judgement X5, S6). A sign-in
  reaches it only as a copy that coai makes for the job (S3). No check unit sees the coder folder (S1; s7 t5).
- codex: turn 0 is `codex exec` with the measured set, cwd = the worktree, `CODEX_HOME` inside the job's coder home. A fix turn is
  `codex exec resume <thread of turn 0>` with the limit as a `-c` override, from the same folder, with the same home.
- agy: turn 0 with a coder agent (the readers and the measured write tools) and a write guard: the hook allows a write tool only
  when the target, resolved through links, is inside the job's worktree and is not under `.git`, `.agents` or `.codex` of it
  (the last two until an addendum of the judgement drops them); it denies a command tool, an unknown tool and a payload it
  cannot read, and it answers EVERY input with an explicit decision (judgement G2, G3). The working directory of the unit is
  the job's guard folder, never the worktree; the worktree is the one `--add-dir` root (G7). `--mode accept-edits` and
  `--agent coai-coder` are on every argv; the mode a turn ran in is read as C8 says. A fix turn is `--conversation <id of turn
  0>`. The read-only agent and hook do not change.
- `CoderHandle` (`CoderHandle.cs:17`) gets the vendor. A handle of another vendor, job or worktree is refused before a process
  starts.
- The job's coder home lives as long as the job: the session file of a CLI is in it (the rule E2 found for claude).
- A lost session on a fix turn is a `crash`: the next model starts from the last coai commit. No fix turn starts a new session by
  itself.
- The fix loop, the commits, the checks and their boundary do not change (`CoderFixLoop`, `CheckBoundary`).

**The must-changes of the judgement's addenda 2 and 3** (sections 13 and 14 of the judgement file at commit `d0dca16d` on
`feat/v2-ep3-e3-measure`; that branch is merged into this one as `a9187e76`). A vendor is launchable only when its list is built
and green (C2 item 1). The owner named four of them; the rest are the lists 13.11 and 14.8.

- codex. The argv is the set of 13.4 on EVERY turn: the set CX1 and `-c agents.enabled=false` (s5 t2 d1). **Before every
  launch the job's `CODEX_HOME` holds no `environments.toml`; one that is there refuses the launch** (CX-C1, s5 t2 d2). The
  preflight asks the tool list with the job's own model (no name outside the 11 known ones; fewer pass when the inventory was read whole: section S, S4) and reads `codex doctor`
  (CX-C4, CX-C3); the tool list says nothing about the web search (s5 t3 d3). **The exact rule of CX-T4 after every turn**
  (s5 t4 d3): `agent_message`, `command_execution`, `file_change` and `error` items pass; an `mcp_tool_call` passes only
  for the two list tools of the server `codex` with an empty list and no error; a `web_search` and a `collab_tool_call`
  item end the job; **an item of any other type ends the job and the reason names the type**. CX-T5: the rollout file of
  the thread is read, and a missing or unreadable one, one of another thread, or a call named `collaboration.*` ends the
  job. The failover reads the three signs of a lost sign-in of 13.6 (s5 t4 d2). The copy holds a placeholder for the
  refresh token (CX-R4), and the margin CX-R1 and the override CX-R2 both stay (s5 t1 d3, s7 t6).
- agy. **G15**: the guard's record holds the conversation id, the step index, the tool name and the decision (s6 t2 d3);
  after every turn each tool event of the stream has exactly one record with the same three, `DONE` goes with `allow`
  and `deny` with `ERROR`, the counts are equal, and every tool is `read_file`, `write_file` or `edit_file`; anything else
  ends the job (s6 t5 d3). The guard folder is bound read-only (AG-B3, s6 t3 d2). **A refresh before the launch, so that
  an expired copy never reaches a start** (risk A11): the coder's copy holds the access token only (AG-R2); before EVERY
  launch its `expiry` is at least now + the unit's `RuntimeMaxSec` + 5 minutes, else no launch; the refresh runs in a
  unit of its own, and only when the real file has no margin (s7 t6; s6 t3 d3); a turn's deadline fits under the life
  of a token. The signs of AG-M2 and the texts of a lost sign-in are read after every turn (s6 t5 d2 and d3).

Scenarios: **V1** codex fresh argv = the measured set. **V2** codex fix turn = `exec resume` with the thread of turn 0. **V3**, **V4**
the same for agy. **V5** a handle of another job, worktree or vendor. **V6** an installed version other than the measured one (older, newer, not named), for each of the three vendors: launched,
with both versions in the envelope; a flag of the argv that the help does not declare: refused, naming the flag. **V7** the fake as coder inside the real unit: a write to the main checkout, beside the worktree, to `.git`, to the home
→ stopped, `coder-boundary`, never green. **V8** a child command cannot read what is hidden, cannot start a unit, cannot run an
`.exe`; a control unit shows the reads work without the additions. **V9** one preflight row for every id of the table; each goes red when its property is dropped.
**V10** no user manager, native Windows, macOS → refused. **V11** a vendor that is not shipped. **V12** failover in each of the four combinations of C5; zero launches of a refused
vendor. **V13** a lost session. **V14** the environment and no secret in an argv. **V15** the agy write guard. **V16** agy usage per
turn. **V17** the prompt of each vendor (C7). **V18** an agy turn with a sign of AG-M2 (a mode warning on stderr, a `denied_actions`
that is not empty, the permission line): `coder-boundary`. **V19** a codex report with null members (C8). **V25** G15, each
broken tie. **V26** the rule of CX-T4, each arm, and CX-T5. **V27** an `environments.toml` in the job's `CODEX_HOME`. **V28** an
agy copy under the margin, or expired: no launch. **V29** a vendor with `ship` and the mark off: not launchable.

### C7. The coder prompt of each vendor (the owner's decision 40, 2026-11-25)

**The evidence** (the coder comparison of E2: codex refused the shipped prompt twice, agy and claude were green) is in
`SPEC_CHANGES.md`, section C7-E, word for word.

**The decision.** The owner chose option (a) on 2026-11-25 («да, (а)»): the coder prompt has a version for each vendor — the
vendor's own tool names, and a line that says that commands which the repository's instruction files order first are not run in
this job, because coai runs the checks. The claude text stays exactly as it is today.

**The shape.**

1. Three embedded files: `src_mcp/src/Roles/prompts/coder.md` (claude; NOT changed: 2 628 bytes, sha256
   `ee31db20b05e779b817131fdb615be1225271dfad715e3b647cfa38d7d92d7bf`), `coder.codex.md` and `coder.agy.md`. The seam of s7 names
   the prompt of a vendor and `CoderPrompt` takes it from the seam. A vendor with no prompt of its own cannot be launched as a
   coder, so no vendor gets claude's tool names by default.
2. A vendor's file differs from `coder.md` in exactly two places: the tools bullet, and ONE added bullet after it. A test holds
   every other line byte-equal, so the three texts cannot drift apart.
3. The tools bullet names only tools of the vendor's measured list. codex (id CX-T1): read with read-only shell commands, change
   with `apply_patch`, no build, no test and no git command. agy: the tool names of the measured agent file; no command tool.
4. The added bullet is the same for both vendors, and it says only what is true (second plan round, section L): an instruction
   file of the repository (`AGENTS.md`, `CLAUDE.md` and the files they name) may order a command before work; the model does
   not run it, and that is no reason to refuse; what coai ran for this job before the turn is at the end of the prompt, under
   the heading "Run by coai before your turn"; when that section says that nothing was run, the model does the task from what
   it can read and names the command in `notes`; every other rule of those files still applies. The first wording ("because
   coai runs the checks") is gone: it promised a run that nothing in the plan made. What coai does run before a turn is the
   instruction step of C9. The bullet is narrow on purpose: it lifts no rule about the code, the paths or secrets, and it
   gives the model no command.
5. The fix prompt and the repair prompt go the same road. `CoderPrompt.Fix` says "you have no shell" today: true for claude,
   false for codex. The words that differ come from the vendor's file. The claude fix and repair prompts stay byte for byte.
6. An override a person wrote is per vendor: `<dataDir>/prompts/coder.md` is claude's; `coder.codex.md` and `coder.agy.md` are
   the others'. An override of the claude prompt is never sent to another vendor.
7. The envelope names the prompt id and the sha256 of the rendered turn-0 prompt. It never holds the text.

**What the line is not.** It is not a boundary: the unit, the vendor sandbox and the guard hold what a model may do (accepted
risks C4 and A7). Rule text of the repository still reaches a codex model. An agy model cannot read `.agents` at all (the guard),
so for agy the text of a repository's rules arrives only through the instruction step of C9.

Tasks: s7 t1 d1 and s7 t2 d5 (the seam and the road; claude byte for byte), s5 t4 d4 (codex), s6 t3 d6 (agy), s8 t2 d2 and s8 t4
d4 (the proof).

### C8. What the comparison does not prove, and the agy mode

A harness (`v2/epoch-3/e2-coder-one-task/comparison/`) ran codex and agy in the unit shape of the s4 measure. The product ran
neither. So the comparison is evidence for the prompt, and for nothing else. The table of what it did not show, row by row with the task that shows it (s5 t2 d2, s5 t3, s5 t4, s6 t2, s6 t4, s6 t5,
s7 t5, s8 t4), is in `SPEC_CHANGES.md`, section C8-T, word for word.

**The agy mode.** The init line of the agy turn said `permission_mode: request-review` while `--mode accept-edits` was on the
argv; the edits went through. The measure of s4 saw that in `accept-edits` a command is denied, and that `--mode auto` fell back
to the default mode with one warning on stderr, where a command RAN. So neither the flag nor that one line says which mode a turn
ran in. The rule (addendum 1 of the judgement, G14):

1. The argv is a request. coai asserts its own half (id AG-M1); that proves no mode.
2. Measured by s6 t7 d9 on agy 1.5.18 (addendum 3): the init line says `request-review` for every mode, so it is not
   evidence, and AG-M3 is not in the table. Nothing reads the init line for the mode.
3. The mode is a property of class `release` (id AG-M4: with `accept-edits` a command of an agent that has a command tool
   is denied, fresh and resumed; with no `--mode` it RAN). For a job, row B9 rests on the agent file and the guard, and
   the signs of AG-M2 are read after every turn. That is accepted risk A9, now real and not conditional; the owner
   accepted it (section H).

No launch is passed on the flag alone.

### C9. The instruction step: what coai runs before turn 0 (second plan round, section L)

**Why.** An instruction file of a repository can order a command before any edit: a rule resolver, a security check. A coder
of coai runs no command of its own (claude and agy have none; codex is told to run none). So either coai runs that command,
or nobody does, and the coder must be told which of the two happened. coai never finds the command by reading prose.

1. **Declared.** `.coai/toolchain.yaml` gets ONE optional top-level key, `instructions`: `argv` (a block list, run with no
   shell; the reader's refusal rule for argv text holds), `perFile` (optional: a block list that is appended once for EACH entry
   of the task's `files`; an item may hold `{file}`) and `timeoutSeconds` (1 to 300; 60 when absent). `version` stays 1. A
   repository whose instruction files order a command declares it here. With no key, coai runs nothing and says so.
2. **Read at the base commit**: the step runs before any model, so the worktree IS the base and a job cannot change the
   step that instructs it. The checks AFTER a turn are E4's: they run the head's own definitions (section G).
3. **Run by coai, once for a job, before any model is launched**, in a check unit of E2 (`CheckBoundary`): the worktree at the
   base commit, the job's own home, no credential, no network, the property list of a test step. The tree and HEAD are
   compared before and after it (the rule of the checks). After s7 t5 the unit does not see the coder folder.
4. **The output** is its standard output: UTF-8, at most 64 KiB, exit code 0. Standard error goes to the step log. The text is
   kept in the job folder and used again for a later attempt of the same job (a failover starts a new session). The limit
   stays 64 KiB (the owner, 2026-11-26): it is not raised and no output is cut to fit it.
5. **A declared step that cannot run refuses the job.** A non-zero exit, a run past its time, more than 64 KiB, bytes that are
   not UTF-8, a changed tree, a unit that cannot start: the job ends with the stop `instructions-failed` before any model is
   launched. It is a coder's own stop: no failover, never green. The envelope names the argv, the exit code, the size and the
   last lines of standard error. Both go through the redactor (`Redaction`, the one road) before they are written to
   the envelope, the job's record or a log line, and so does the argv wherever else it is recorded (re-review round, F0).
   That is a rule of RECORDS; what reaches the prompt is item 10's. Nothing is cut and no job starts with half an instruction. A key the reader cannot read is
   `toolchain-refused`, as for every other key of the file.
6. **Into the prompt of every vendor, claude too.** coai appends ONE block after the rendered prompt, under the heading "Run by
   coai before your turn": the argv, the base commit, and the output in a fence longer than any run of backticks in it. It is a
   block that coai writes, not a slot of a prompt file. So `coder.md` keeps its bytes and its sha256, and the sha test of s7 t2
   d5 is not changed. **What does change for claude, said plainly:** on a repository that declares the step, a claude coder is
   sent today's prompt followed by the block; on a repository that declares none it is sent today's bytes and nothing else.
   A fix turn and a repair turn resume the session and do not repeat the block.
7. **With no step declared**, a codex and an agy coder get the block too, and it says: nothing was run, this repository
   declares no `instructions` step. Their prompt bullet (C7 item 4) points at the block in both cases, so it is true in both.
8. **The envelope** gets `instructions {declared, argv, exitCode, bytes, sha256, ms}`. `declared: false` is in the envelope of
   every job that ran with no step, so an orchestrator sees that a coder worked without the repository's own command. A job
   whose step printed a secret-shaped text has one more member there, `secrets` (item 10).
9. **This repository** (measured by s7 t3 d5; the owner, 2026-11-26). Its `AGENTS.md` orders the rule resolver. The
   resolver's payload for the files of three past tasks is 334 362 bytes each, and the resolver prints at most 32 KiB in
   one `read`. The limit stays, so `.coai/toolchain.yaml` of this repository declares NO `instructions` step and says why
   in a comment; a test holds it; a coder here is told that nothing was run. The cure is a short form of the resolver's
   output, taught to the resolver later, outside E3 (section G).
10. **A secret-shaped text in the output stops nothing** (the owner, 2026-11-26: «секреты в выводе — не останавливают
    ничего»; the whole sentence is in `SPEC_CHANGES.md`, section M). coai searches the output with the redactor's classes
    (`Redaction.FirstSecretClass`, the one road). On a hit the job goes on, and the output is kept and reaches the prompt
    as it was: nothing is cut or replaced. coai records the finding in three places: a notice `instructions-secret` whose
    subject is the job; the member `secrets [{class, line}]` of `instructions` in the envelope, written only when there is
    a finding; one log line. Each names the class of the secret and the line of the output, and none holds the value or a
    part of it. This replaces must-change M1 of the security verify of s7 t3, which ended such a job
    `instructions-failed`. What to do about a finding is the person's decision (risk 12 of section H). **The UI tab is
    epoch 4 `e4-local-pages`** (its `doneWhen`): a tab of the extension's notifications for the secret findings of jobs.

**What the step is not.** It is not a check of the coder's work: the build and the tests after the turn are that. It is not a
boundary. Its output is text of the repository at the base commit, as `AGENTS.md` is, and it reaches the vendor of the model.

Scenarios: **V20** a declared step: its output is in the turn-0 prompt that each vendor's CLI was sent, claude's too, and in
the envelope by its sha256. **V21** each failure of item 5 ends `instructions-failed` with zero model launches. **V22** no step
declared: the claude prompt is byte for byte today's; a codex and an agy prompt hold the block that says nothing was run; the
envelope says `declared: false`. **V23** a secret-shaped text on line 2 of the output: the job ends green, the turn-0 prompt
holds the output as it was, one notice `instructions-secret` and the envelope name the class and line 2, and no part of the
value is in the notice, the envelope, the job's record or the log; with no such text there is no notice and no `secrets`.
**V24** a step that fails with a sentinel secret in its standard error and another in an argv item: neither sentinel is in
the job's record, the envelope or the log.

Tasks: s7 t3 (the key, the run, the refusal, the envelope, this repository's file; d6 the secret finding), s7 t2 d5 and d6
(the block), s5 t4 d4 and s6 t3 d6 (the bullet), s8 t2 d2 and s8 t4 d4 (the proof: the effect, not only "not refused").

## D. Rows and the failover order

**The rule does not change** (E1 spec A): a limit, a refusal or a crash goes to the next model at once; only the last model waits;
a spent quota, a missing CLI or a lost sign-in skips that vendor's other models; `cancelled`, `tree-changed`, `output-invalid` and
the coder's own stops never fail over. `model-unavailable` (section S, S4) goes to the next model too, and exists only before the first launch of an entry: a model list that cannot be read skips the vendor, every other case tries the next entry, the last entry fails at once. After the first launch of an entry, a red preflight and a boundary stop of a turn are terminal: no commit of the turn, no failover. A read-only role restarts from turn 0. A coder restarts from the last coai commit.

**The lists of the claude row** are built as `shared/role-defaults.json` holds them. The table of the proposal and
decision 35: `SPEC_CHANGES.md`, D-L.

**The codex and agy main-AI rows: both are filled** (the owner, 2026-11-26: all three AIs are full ones; the words are in
`SPEC_CHANGES.md`, section M). So claude, codex and agy each ship as a coder, a reviewer and a consultant, and each can be
the main AI. The reviewer and the consultant of all three exist since before v2 (the files: `SPEC_CHANGES.md`, D-F). E3
adds what was missing: the coder of codex and of agy (s5, s6) and the two main-AI rows (s7 t4 d4), from the lists that
s4 t1 measured (`RESULTS_model_lists.md`).

The owner's word names no model. The names below are the planner's proposal of 2026-11-26 for the whole review of epoch 3;
a person's `COAI_ROLES_MODELS` and `COAI_ROLES_BANDS` go over them. The rule: a row starts with its own vendor's model and
fails over to the other two vendors, as the claude row does; an entry of another vendor applies only with that vendor's
row (`whenVendorRow`); a coder entry only while the table of coder boundaries ships its vendor; every name is in
`RESULTS_model_lists.md`, and a name new to `shared/role-model-names.json` is added there.

| Role | Main AI codex | Main AI agy |
|---|---|---|
| orchestrator | `astra` | `gemini-4-argon-high` |
| coder | `terra` → `astra` → claude `sonnet` → agy `gemini-4-argon-medium` | `gemini-4-argon-medium` → `gemini-4-argon-high` → claude `sonnet` → codex `terra` |
| scout | `terra` → claude `sonnet` | `gemini-3.8-flash-medium` → `gemini-4-argon-medium` → claude `sonnet` |
| judge and important tasks (`important`) | `astra` → claude `fable` | `gemini-4-argon-high` → claude `fable` → codex `astra` |
| ba | `astra` → claude `fable` | `gemini-4-argon-high` → claude `fable` |
| bands 0–2, 3–6, 7–10 | `gpt-6-luna`, `terra`, `astra` | `gemini-3.8-flash-low`, `gemini-4-argon-medium`, `gemini-4-argon-high` |

**A codex name with no version is a family** (the owner, 2026-11-27; ledger entry 2.1): when a job reaches the entry,
`terra` or `astra` becomes the newest `gpt-<version>-terra` or `-astra` that the installed CLI lists. A name given in
full is used as given. `SPEC_CHANGES.md`, section Q.

Why these names, and why agy's Neon, Carbon, Hydrogen and Orchestrator names are not used: `SPEC_CHANGES.md`, section M.
`agy models` also lists Claude models, which the vendor-routing rule never runs there.

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
| the history of measures in `shared/coder-boundary-releases.json` | one small record for each measure that is made; nothing adds one by itself | kept: it is the history | a static file |
| the sign-in copy of a job (judgement S3) | one small file per job, in the job's coder home | deleted when the job ends, and by the E2 sweep after a kill | the same sweep |
| the agy guard's decision records (G4) | one line per tool call, in the job's coder folder | with the job folder | a record that cannot be written is a deny |
| the pack text kept with a session | 256 KiB at most per plan round | the session's own retention | nothing is written before a round exists |
| the `instructions-secret` notice (C9 item 10) | one record for a job with a finding | not a new surface: the notice ledger that holds `role-model-switched` (E1), by its rule | — |
| `shared/` schemas and vectors, `role-stops` fixtures | static files | — | — |

## G. Not in E3, and the boundaries with the other epics

| Item | Built by | E3's part |
|---|---|---|
| Resume of a codex and an agy coder session, for one task | **E3** (s5, s6) | E4's `doneWhen` line now says so: the resume is from E2 (claude) and E3 (codex, agy); E4 keeps the caps, the no-progress stop and the flaky re-run for every shipped vendor |
| The formatter turn, test selection, flaky re-run, the stop on a repeated failure | E4 | none |
| Detection, default argv, parsers of other stacks | E4 | none |
| Parallel tasks, slots, merges, a `review_code` started by coai | E5 | none |
| The claude key pool | E2 s7 (branch `feat/v2-ep3-e2-s7-keypool`) | s7 starts after that branch is merged: both change `CoderHandle`, `CoderUnit`, `CoderLaunches` and `RoleFailover`. Merged: it came with `feat/v2-ep3-e2-coder` on 2026-11-26 (section 0) |
| The reader of `.coai/toolchain.yaml` (`ToolchainParse.cs`, `ToolchainModel.cs`, `shared/toolchain.schema.json`) | E3 s7 t3 adds ONE top-level key, `instructions`; E4 s3 t1 adds four names of a project (`restore`, `format`, `testFor`, `testFiles`) | the names do not overlap; the task that lands second merges the reader. E4's SPEC section L names the same boundary |
| A unit with no network and coai's forwarder with the vendor's hosts (judgement G11) | E4 (`doneWhen` line 8 of its `epic.yaml` on `feat/v2-ep3-e4-plan`); its story is written after s6 t7 d6 | s6 t7 d6 measures agy behind a proxy; nothing else |
| What claude does with a file that a check planted in the coder's home (judgement, section 4 point 3) | a follow-up for E2 | s7 t5 closes the hole for every vendor and writes the follow-up as a todo (d5) |
| A status that coai writes into `task.yaml` | E5 / epoch 4 E5 | it would change the approval id of a scope; that epic decides how |
| What a coder may change of its own checks (E4 SPEC section A4, scenarios D12 and D13; the owner, 2026-11-26) | E4: the checks run the head's OWN definitions; nothing the head changed is refused; every difference from the base is REPORTED (`definitionsChanged`, the changed definition files, the tests changed or deleted); the restore step takes packages from the internet itself, with no proxy and no address list | none: E3 builds no gate on a changed definition and no network limit for a restore. E3 agrees with it in C9 item 2, in risk 10 and in C6 (the seam changes neither the fix loop nor the check boundary) |
| A short form of the rule resolver's output, so that this repository can declare its instruction step inside 64 KiB (C9 item 9; the owner, 2026-11-26) | a follow-up outside epoch 3: the resolver is `tools/rules.mjs` of the conventions repository (mounted at `.agents/conventions`), so the short form is a plan there. Here the rest is a data edit (`.coai/toolchain.yaml`, the test of s7 t3 d5) | none: E3 keeps the limit and declares no step for this repository |
| The tab of the extension's notifications for the secret findings of jobs (C9 item 10) | epoch 4 `e4-local-pages` (extension half; its `doneWhen`). Epoch 3 has no extension epic; its 5 epics and E3's 8 stories are the device limits | s7 t3 d6 writes what the tab reads: the notice `instructions-secret`. s2 t5 d6 writes `coder-tool-unknown` (section Q), which the page lists |

Also not in E3: a pool of codex or agy credentials; `cons`, `consask` and reviewers on the pool; a coder on native Windows or macOS;
`review_feature(featureFolder)` (server.md §8 names it; no epic's `doneWhen` holds it); a document written by the judge; attachments;
the extension showing a `folder` source (extension half).

Order inside the epic: s1 → s2; s3 alone; s4 → s7 → s5 and s6 (side by side, neither needs the other); s8 last; s2 t5 (section Q) after s5. s3 and s4 can
start at once. The story numbers are not the order: s7 comes before s5 and s6. The codex and agy stories need E2's coder, and
`epic.yaml` of E3 now names `e2-coder-one-task` in `dependsOn`.

## H. Risks, and what only the owner can answer

Risks:

1. A vendor can come out as `refuse`. Then line 2 of `doneWhen` is not met for it, by design.
2. The sign-in of codex or agy inside a unit: a token refresh that cannot be saved may end the person's sign-in. The same risk was
   open for claude and led to the key pool. s4 does not test a refresh with the real sign-in.
3. `evidence.text` costs repair turns when a model copies a line badly. The paid proof counts them.
4. A role job has no spend cap of its own. The paid proofs reserve a priced worst case before each call (s8 t3 d3, s8 t4
   d5); the ledger records what was spent, after the fact.
5. A plan valid on one device can be invalid on another (limits are per device); the refusal names the source.
6. The fable judge is the most costly model. The proof runs the judge on haiku through a scratch `important` list.
7. (Gone on 2026-11-26: `SPEC_CHANGES.md`, H-7. What came in its place is risk 13.)
8. A change of `feature.yaml` or of the plan file after an approval refuses every coder start under that approval (B7). The cure
   is a new plan round.
9. **The accepted risks of the judgement** (its sections 7, 13.8 and 14.7): agy A1 to A13, codex C1 to C7 and C9. **The
   owner accepted all of them on 2026-11-26**; C8 was closed by the measure. The list, risk by risk: `SPEC_CHANGES.md`,
   H-9. Only a risk that a later addendum adds or widens goes to the owner, and it alone.
10. The prompt line of C7 tells a model not to run a command that the repository's own instructions order. coai runs that
    command only when the repository declares it (C9). A repository that orders a command in prose and declares no step gets
    a coder that worked without it: the prompt says so to the model, the envelope says `declared: false`, and the checks of
    coai and the code gate are what catch the difference. Since the owner's decision of 2026-11-26 on E4 (section G) a
    green check means that the head's OWN definitions passed: a coder of any vendor may have changed the tests or the
    commands that run them, and the envelope says when (`definitionsChanged`, E4).
11. The output of the instruction step is paid for in every job (up to 64 KiB of prompt), and it is repository text sent to
    the vendor of the model, as the rest of the prompt is.
12. **A secret in the output of the instruction step reaches the vendor of the model** (C9 item 10; the owner's decision of
    2026-11-26). coai does not stop the job and does not cut the text; it tells the person the class and the line, and the
    person decides, also whether to rotate what was printed. Until the tab of epoch 4 `e4-local-pages` is built, the
    notice is one row among the other notices of the extension.
13. **A boundary is measured on one version and runs on every later one** (the owner's decision of 2026-11-26, for claude,
    codex and agy; C2 item 2). The CLIs update themselves daily (agy went from 1.5.17 to 1.5.18 on that morning). After an
    update nothing is measured again and nothing is pinned. What still holds a job is what coai can ask the installed
    CLI: the flags its help declares, the preflight rows inside the unit before each job, the launch checks, and the
    stream of every turn. What is only ASSUMED on a version that was not measured: every property of class `release`
    (it cannot be asked without a model turn), and that a flag of the same name still does what it did. The envelope
    names the installed version beside the measured one, so a person can see how far apart they are.
14. **A codex model that nobody measured runs as a coder on the day the CLI lists it** (the owner, 2026-11-27;
    accepted the same day): `SPEC_CHANGES.md`, sections Q to T.

The questions that were put to the owner on 2026-11-25 (questions 1 to 5, which task files cite as "SPEC section H,
question n") and the answers of that day (decisions 35 to 40) are in `SPEC_CHANGES.md`, section H-Q, word for word.

**The owner's decisions of 2026-11-26** (each is a plan change; the words and the ledger entries are in `SPEC_CHANGES.md`,
section M; the list as this section held it is H-D there): the caps of the paid runs — s8 t3 and s8 t4 together at most
30 USD, reserved BEFORE each call; s5 t6 at most 30 codex calls; s6 t7 at most 30 agy turns — versions gate nothing; a
vendor is launchable only after its must-changes; the risks are accepted; three full vendors.

What waits for the owner: the approval of ledger epoch 3 (the status block). What can still come to the owner: a vendor an addendum refuses (C5), a risk an
addendum adds (risk 9).

## I. Build order, test plan, Definition of Done

Build order: the story order of section G; in a story the `dependsOn` of its tasks. Measure tasks come before the code that
depends on them, and the judgement comes before any codex or agy launch code.

Test plan: every scenario S1 to S12, P1 to P13 and V1 to V36 (V30 to V36: sections Q, S) has a test that carries its number in its name, or the task result says
which test covers it. FakeCli is the only double; the units, git, the detached runner and stdio are real. Structural scans have a
companion that proves they match something. The whole MCP suite runs through the MTP executable, never `dotnet test`.

Definition of Done:

- [ ] `start_role` runs `scout` and `judge`; each envelope is strict JSON built by coai, with checked evidence.
- [ ] `review_plan(featureFolder)` refuses an invalid folder before any round and packs a valid one in topological order; a file
      of the pack that differs in the working tree, in the scope or in the context outside it, refuses the round; a coder
      job is bound to the approved scope and to the context the reviewers read.
- [ ] For each vendor the judgement shipped: a coder job fixes a red check in the SAME session of turn 0, on the version that
      is installed (no version, hash or pin of any vendor's CLI refuses or stops anything; the envelope names it beside
      the measured one), inside a boundary whose rows B1 to B15 were measured and whose relied-on properties the preflight probes before each job. For a vendor it refused: the refusal is in the product and in the results, and the owner
      decided line 2.
- [ ] The pack text of every scope that has an outside dependency names it, an epoch and an epic scope included; the
      approval id covers the pack format version, which was raised with it, and an approval of an older format binds no
      coder job and asks for a new plan round.
- [ ] The codex and agy main-AI rows of `shared/role-defaults.json` are both filled, with names of the lists or families.
- [ ] A codex family name becomes the newest listed model when a job reaches the entry. A model that cannot be used, or
      that offers a tool outside the known set, is not launched, and the job goes to the next model. Fewer tools pass
      only when the inventory was read whole. After the first launch of an entry, a red preflight and a boundary stop
      are terminal (section S, S4). The commit that changes the tool rows lands only with addendum 4 of the judgement,
      verdict `ship`.
- [ ] Each shipped vendor's coder gets its own prompt: its own tool names and the one added line; the claude prompt file is byte
      for byte what it was.
- [ ] A command that a repository declares as its instruction step is run by coai before turn 0, and its output is in the
      prompt of every vendor's coder; a declared step that cannot run refuses the job; with no step the prompt says that
      nothing was run. On a repository whose instruction file orders a command and declares it, a real job of each shipped
      vendor is not refused and its commit follows a rule that only the step's output holds.
- [ ] A secret-shaped text in the output of the step stops nothing: the job goes on with the output as it was, and the notice
      `instructions-secret` and the envelope name the class and the line and never the value. The argv and the standard
      error of a failed step are redacted in every record.
- [ ] The mode of an agy turn is named as measured, with the release it was measured on, and the signs of AG-M2 are read
      after every turn; no launch is passed on the flag.
- [ ] codex and agy are launchable only by the commit that turns their mark on, after the must-changes of C6 are built and
      green: G15, the rule of CX-T4, the `environments.toml` refusal and the refresh before a launch among them.
- [ ] The epic acceptance passes on the real binary; the paid proofs and the two open measures are inside the owner's caps
      of 2026-11-26 (section H) and written down; every call of the paid proofs was reserved before it was launched.
- [ ] `validate_work` accepts the `v2` folder; `plan-lifecycle.mjs` is clean; ARCHITECTURE.md, README.md and server.md say what was
      built and what was not.

