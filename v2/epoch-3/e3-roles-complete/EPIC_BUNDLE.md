# coai v2 — epoch 3, epic E3 (roles complete: scout, judge, codex + agy coder, review_plan over a feature folder) — epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§2, §8, §10, §19, §20, §23); built on epoch-3 E1 (role jobs, router, failover, work-plan schema) and E2 (the coder, its boundaries, the fix loop). The full spec is SPEC.md in the same folder; its digest is below, before the YAML files. This is the plan AFTER the plan gate of 2026-11-25 (proceed, 5 findings accepted: SPEC section J).

| Story | Tasks | Complexity sum | Depends on |
|---|---|---|---|
| s1-scout-judge-contracts | 4 | 18 | — |
| s2-scout-judge-roles | 4 | 20 | s1-scout-judge-contracts |
| s3-plan-round-feature-folder (cornerstone) | 5 | 27 | — |
| s4-codex-agy-measure (cornerstone) | 6 | 29 | — |
| s5-codex-coder | 5 | 24 | s4-codex-agy-measure, s7-vendor-seam-rows |
| s6-agy-coder | 6 | 31 | s4-codex-agy-measure, s7-vendor-seam-rows |
| s7-vendor-seam-rows | 4 | 19 | s4-codex-agy-measure |
| s8-proof | 5 | 24 | s2-scout-judge-roles, s3-plan-round-feature-folder, s5-codex-coder, s6-agy-coder, s7-vendor-seam-rows |
| **Total** | 39 | 192 | |

## Spec digest (the full spec is SPEC.md beside this file; its section letters are used here and in the task files)

**Goal.** Scout and judge give strict JSON. A codex and an agy coder session is resumed for its fix turns. `review_plan(featureFolder)`
validates first and packs in topological order.

**Today.** Roles that start: probe, ba, coder. The coder is claude only; the codex and agy adapters refuse a coder launch.
`review_plan` takes `planText` or `committedPlan`. A coder job is bound to an approved plan FILE.

**Decisions that change server.md.** (1) The role name is `judge`; `important` stays the name of its model list and of the router
rule. (2) Evidence is an object `{file, line, text}` that coai checks, not a string `file:line`. (3) A folder round has a scope: the
node `workItem` names inside the folder, else the whole feature; the approved thing is the scope and the context the reviewers read
with it. (4) Resume of a codex and an agy
coder session is built in E3 for one task; E4 keeps the hardening. (5) A vendor's coder ships only on a judgement verdict. (6) A codex or agy coder runs only on a release that was measured: an exact
list, not a floor.

**A. Scout and judge.** Role cases of `start_role` on the BA's road: a read-only launch (Read, Glob, Grep), the tree compared around
every attempt, a strict reader, 2 repair turns at most in the same session, a declared refusal goes to the next model. Scout:
`{status, facts:[{claim, evidence:[{file, line, text}]}], files, unknown}`. Judge: `{status, verdict: approve|revise|reject, summary,
findings:[{id, severity: blocking|major|minor|nit, title, detail, evidence, fix}], decision}`; `approve` with a blocking finding and
`revise` or `reject` with no finding are errors. coai checks each evidence item against the file (the line holds the text) and builds
the envelope itself: an item that is not verified stays, marked `verified: false`. A path that leaves the repository or names a
credential-shaped file is an error and is never opened. An evidence text that is empty or only white space is an error before the
line check and can never be verified. The judge writes no file. Scenarios S1 to S12.

**B. `review_plan(featureFolder)`.** One input of three. The folder is read from git at the branch tip (`GitWorkFolder`, each blob
proved, links and submodules not followed). The WHOLE feature is validated with the device limits first; invalid means a refusal
with no reviewer launch, no round row and no stage change. The pack (`WorkPack`, pure): `feature.yaml`, the plan file, the ancestors
of the scope, then stories and tasks after what they depend on, ties by the number in the id; one line for each dependency outside
the scope. Each file is in a fence longer than any run of backticks in it. Limits: YAML file 64 KiB, folder 2 000 entries, pack 256
KiB; over a limit is a refusal, nothing is cut. A plan file that is edited, staged or untracked under the scope is a refusal when
HEAD is the round's commit. What is approved is everything the reviewers read: `PlanSource` origin `folder` with an **approval id**, SHA-256 over
blob id and path of EVERY packed file (the scope files and the context outside the scope: `feature.yaml`, the plan file, the
ancestors) and over every outside-dependency line (node and title). A RESULTS file written later is not in the pack and does not
change it. A coder job is bound when its task is inside the scope and the approval id at its base equals the approved one; a
changed scope file, ancestor, plan file or dependency title refuses the start and names what differs. The previous release reads the new origin as "no source"; this is
measured. Scenarios P1 to P12.

**C. The codex and agy coder.** The bar is the 15 measured rows of the claude boundary: B1 edit in the worktree works; B2 to B5 a
write to the main checkout, beside the worktree, to `.git`, to the real home is refused; B6, B7 a read and a search outside the
worktree are refused (for claude by flags only — the unit does not stop reads); B8 no write through a link; B9 no command leaves the
limit (claude has no shell); B10 the model has no network tool; B11 the model cannot read the sign-in; B12 the tool list is exact,
no MCP server; B13 files at the base commit do not run code; B14 a resumed turn has the same limit and a bound handle; B15 minimal
environment. A vendor ships only when EVERY row was measured as holding, fresh and resumed, by a CLI flag or by a property of the
unit. codex works through a shell and agy has command tools, so B6, B7, B9, B10 and B11 must come from the unit (hide, not only
read-only) or from the vendor's own sandbox seen working inside the unit.

The role refuses in three places. (a) One table, `shared/coder-boundary-releases.json`, filled only from the judgement: per vendor
the verdict, the measured releases and the ids of the properties the verdict relies on. (b) A write capability per vendor: the
installed release is EXACTLY one of the measured releases and every flag is declared in the help; an older, a newer and an unnamed
release are refused. (c) The job preflight with no model, inside the exact launch unit, before each job: one row for EVERY
property the verdict relies on (what is hidden, the sign-in files, the network limit, the guard); a wrong answer refuses the job.
No setting makes a weaker launch. A release is added by: the row measure of s4 on that release, a judgement for it, ONE commit
that adds it to the table; a test holds every entry to its results heading and its verdict.

Measure first (story s4, 13 unknowns U1 to U13): resume in a home of the job's own; `--output-schema`; the tool list of a codex
session; codex's sandbox inside the unit; hiding the main checkout except `.git`; the sign-in files and whether a child command can
read them; files of the repository at the base commit (`AGENTS.md`, `.codex`, `.agents` of the WORKTREE); agy's write and command
tool names; what a failed hook handler does; `--mode accept-edits` and `--sandbox`; the model lists. Paid calls use the vendor's
smallest model in scratch folders: 22 calls per vendor at most. Then the **security judgement** (s4 t6) by the important model,
before any launch code: `ship` or `refuse` per vendor, which layer holds each row, a must-change list applied to the s5 and s6 task
files, and with ids the list of every property each verdict relies on.

A refused vendor never blocks the other. s7 (the seam, the table, the fix loop through the seam, the rows) depends only on s4 and
is built in every case; s5 (codex) and s6 (agy) depend on s4 and s7, never on each other. The judgement commit deletes the story
folder of a refused vendor and its entry in the `dependsOn` of s8. Per combination: ship/ship → failover claude → codex → agy,
both resumed; ship/refuse → claude → codex, a start on agy refused; refuse/ship → claude → agy, a start on codex refused;
refuse/refuse → claude models only, no paid coder run. The failover test and the route test run all four combinations with an
injected table, and the fake CLI records zero launches of a refused vendor. `doneWhen` line 2 goes to the owner when a vendor is
refused.

The shape: codex turn 0 is `codex exec` with the measured set in the worktree with `CODEX_HOME` in the job folder; a fix turn is
`codex exec resume <thread of turn 0>` with the limit as a `-c` override. agy turn 0 runs a coder agent with a write guard (a hook
that allows a write tool only inside the job's worktree); a fix turn is `--conversation <id of turn 0>`. The handle carries the
vendor (the seam is story s7). A lost session is a crash: the next model starts from the last coai commit. Never in an argv: codex
`--dangerously-bypass-approvals-and-sandbox`, `-s danger-full-access`, `--trust`, `--ephemeral`; agy `--dangerously-skip-permissions`.
Scenarios V1 to V16.

**D. Failover.** The E1 rule is unchanged. Proposed lists (the owner confirms): scout sonnet → haiku → codex `terra`; important
fable → codex `astra` (no change); ba fable → opus (no change); coder the band model, then sonnet → opus → codex `terra` → agy
`gemini-4-argon-medium` (another vendor only with its row and a shipped write launch). The codex and agy main-AI rows get measured
names only.

**E. Security rules of every new launch road.** No secret in an argv or a log (file bind or variable NAME; a canary searched in
every recorded argv). A boundary is measured and refused when not effective. Tests never touch the real home. Exe and argv with a
timeout that kills the tree. One table decides the vendor, with a scan test. A model's words are data: paths are checked before a
file is opened.

**Order.** s1 → s2; s3 alone; s4 → s7 → s5 and s6 side by side; s8 last. The story numbers are not the order.

**Not in E3.** The formatter turn, test selection, flaky re-run, the no-progress stop, detection and other stacks (E4); parallel
work (E5); a pool of codex or agy credentials; a coder on native Windows or macOS; `review_feature(featureFolder)`; a document
written by the judge; the extension showing a `folder` source. s7 starts after the key-pool branch of E2 is merged.

**For the owner.** (1) Which credential the codex and agy measures and coder jobs may use. (2) `doneWhen` line 2 when
the judgement refuses a vendor. (3) The lists of D. (4) Done with the gate findings: E3 depends on E2, E4's resume line, two
cornerstones in `feature.yaml`; `PLAN.md` still says 10 cornerstone stories. (5) The paid caps: 44 small calls in s4, 0.10 USD and 0.50 USD in s8.

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
  - v2/epoch-3/e3-roles-complete/stories/s8-proof/story.yaml
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
title: "The codex coder: the write launch inside the OS layer on a measured release, each fix turn in the same thread with exec resume"
dependsOn:
  - s4-codex-agy-measure
  - s7-vendor-seam-rows
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t1-codex-write-capability/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-codex-write-capability
title: "CodexWriteCapability: only a measured release of the table and every flag of the measured set declared in the help, else a refusal with the cure"
kind: security
complexity: 4
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/CodexWriteCapability.cs
  - src_mcp/tests/ACodexWriteCapabilityTests.cs
dod:
  - id: d1
    item: "V6: a codex coder is launched only when the installed release is EXACTLY one of the measured releases of shared/coder-boundary-releases.json and its exec help and exec resume help declare each flag of the measured set; an older release, a newer release and a release the CLI does not name are each refused before the launch, and the cure says to measure and judge that release and then add it to the table (SPEC section C2); asked on every launch, never cached, never started with a weaker set"
  - id: d2
    item: "the release is in the job envelope; a CLI that cannot be started and a help that does not answer are refusals that say so; a test with the table of this repository proves that a release one patch above and one patch below a measured one is refused"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t2-codex-coder-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-codex-coder-launch
title: "The codex coder launch: the measured argv fresh and resumed, the worktree as cwd, CODEX_HOME in the job folder, the unit additions of the judgement, the minimal environment"
kind: security
complexity: 6
dependsOn:
  - t1-codex-write-capability
parallel: false
security: true
files:
  - src_mcp/runners/Consultation/CodexConsultant.cs
  - src_mcp/runners/Toolchain/CodexCoderUnit.cs
  - src_mcp/tests/ACodexCoderLaunchTests.cs
dod:
  - id: d1
    item: "V1 and V2: the fresh argv is exactly the flag set that RESULTS_codex_coder.md names, with the worktree as cwd; a fix turn is exec resume with the thread of turn 0 and the same limit; none of the never-flags of SPEC section C3 is reachable (a test tries each)"
  - id: d2
    item: "the unit has the additions the judgement listed (what is hidden from a child command, a home with CODEX_HOME inside the job folder); V14: only the allow-list and the one name of the vendor variable reach the CLI, read from inside a real unit, and a canary value is in no recorded argv"
  - id: d3
    item: "every existing codex consultant, question and reviewer launch builds the same argv as before, byte for byte"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t3-codex-preflight-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-codex-preflight-rows
title: "The preflight rows of a codex coder job: with no model, inside the exact launch unit, one row for EVERY property the judgement relies on; a wrong answer refuses the job"
kind: security
complexity: 5
dependsOn:
  - t2-codex-coder-launch
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/BoundaryPreflight.cs
  - src_mcp/runners/Toolchain/CoderBoundary.cs
  - src_mcp/tests/ACodexCoderPreflightTests.cs
dod:
  - id: d1
    item: "V9: before each codex coder job the preflight runs inside the EXACT launch unit (the same property list the launch gets, a test compares the two) and has one row for every id the table holds for codex: what is hidden from a child command, the sign-in file, the network limit, no user manager inside, the sandbox of codex where the verdict relies on it; a wrong answer refuses the job before any launch and names the row"
  - id: d2
    item: "a test holds the rows and the ids of the table equal (an id with no row and a row with no id both fail); each row goes red when its property is dropped through the test seam"
  - id: d3
    item: "V10: no user manager, native Windows and macOS are refused before any process starts"
```

## v2/epoch-3/e3-roles-complete/stories/s5-codex-coder/tasks/t4-codex-fix-turns/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-codex-fix-turns
title: "Fix-turn scenarios with FakeCli as codex: exec resume with the handle of turn 0, the report from the output file, the recorded no-session text"
kind: test
complexity: 4
dependsOn:
  - t3-codex-preflight-rows
parallel: false
files:
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/ACodexCoderFixTurnsTests.cs
dod:
  - id: d1
    item: "FakeCli as codex: red, then green after 2 fix turns; the recorded argv shows exec resume with the thread of turn 0 on both, from the worktree, with the same CODEX_HOME; the report is read from the output file; an invalid report gets ONE repair turn in the same thread"
  - id: d2
    item: "V13: a resume that answers with the recorded no-session text of the fixture ends the attempt as a crash, and the next model starts from the last coai commit; no fix turn starts a new session by itself"
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
dod:
  - id: d1
    item: "V7: the fake coder writes the main checkout, a folder beside the worktree, the .git pointer and the stand-in home: each is stopped by the OS, the job ends coder-boundary and is never green, and the main checkout is byte-equal"
  - id: d2
    item: "V8: a child command of the fake cannot read a file of the hidden main checkout, the worktree of another job or the stand-in sign-in file, cannot start a unit and cannot run a Windows .exe; a control unit without the additions shows that the same reads then work"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-agy-coder
title: "The agy coder: a coder agent with a write guard inside the OS layer on a measured release, each fix turn in the same conversation"
dependsOn:
  - s4-codex-agy-measure
  - s7-vendor-seam-rows
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t1-agy-write-capability/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-agy-write-capability
title: "AgyWriteCapability: only a measured release of the table and every flag of the measured set declared in the help, else a refusal with the cure"
kind: security
complexity: 4
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/AgyWriteCapability.cs
  - src_mcp/tests/AAgyWriteCapabilityTests.cs
dod:
  - id: d1
    item: "V6: a agy coder is launched only when the installed release is EXACTLY one of the measured releases of shared/coder-boundary-releases.json and its help declares each flag of the measured set; an older release, a newer release and a release the CLI does not name are each refused before the launch, and the cure says to measure and judge that release and then add it to the table (SPEC section C2); asked on every launch, never cached, never started with a weaker set"
  - id: d2
    item: "the release is in the job envelope; a CLI that cannot be started and a help that does not answer are refusals that say so; a test with the table of this repository proves that a release one patch above and one patch below a measured one is refused"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t2-agy-write-guard/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-agy-write-guard
title: "The agy write guard: a coder agent with the readers and the measured write tools, and a hook that allows a write only inside the worktree of the job"
kind: security
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Reviewers/AntigravityCoderGuard.cs
  - src_mcp/runners/Reviewers/AntigravityReadOnly.cs
  - src_mcp/src/Program.cs
  - src_mcp/tests/AAgyCoderGuardTests.cs
dod:
  - id: d1
    item: "V15: the handler allows a measured write tool only when the target, resolved through links, is inside the worktree of the job and is not .git; it denies a write outside, a command tool, a tool it does not know and a payload it cannot read; the agent file lists the readers and the measured write tools and nothing else"
  - id: d2
    item: "the folder of the guard is the own folder of the job (under the job folder), written and read back before every launch; the read-only agent, hook and folder are byte for byte what they were (their tests pass unchanged)"
  - id: d3
    item: "no new one-shot mode is added (the handler stays --agy-hook); if one is needed it is listed in .agents/PROJECT.md and never exits 64"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t3-agy-coder-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-agy-coder-launch
title: "The agy coder launch: the measured argv fresh and resumed with --conversation, the guard folder, the unit additions of the judgement, the minimal environment"
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
  - src_mcp/tests/AAgyCoderLaunchTests.cs
dod:
  - id: d1
    item: "V3 and V4: the fresh argv is exactly the flag set that RESULTS_agy_coder.md names; a fix turn has --conversation with the id of turn 0 and the same guard; none of the never-flags of SPEC section C3 is reachable (a test tries each)"
  - id: d2
    item: "the unit has the additions the judgement listed and a home of its own in the job folder; V14: only the allow-list and the sign-in of agy reach the CLI, read from inside a real unit, and a canary value is in no recorded argv"
  - id: d3
    item: "every existing agy consultant, question and reviewer launch builds the same argv as before, byte for byte"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t4-agy-preflight-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-agy-preflight-rows
title: "The preflight rows of an agy coder job: with no model, inside the exact launch unit, one row for EVERY property of the unit the judgement relies on, the answers of the guard handler and the bytes of the guard files"
kind: security
complexity: 5
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
    item: "V9, the unit: before each agy coder job the preflight runs inside the EXACT launch unit (the same property list the launch gets, a test compares the two) and has one row for every id the table holds for agy: the sign-in files cannot be read from a child command, the main checkout and the worktrees of other jobs are hidden, the network limit holds, no user manager and no D-Bus inside, a Windows .exe does not run, the home is the own home of the job, and each other property the verdict relies on"
  - id: d2
    item: "V9, the guard: in the same unit the handler is given a write inside the worktree (allow), a write outside, a write to .git, a write through a link out, a command tool and an unreadable payload (deny each); the agent file and the hook file are read back and are the expected bytes"
  - id: d3
    item: "a wrong answer of ANY row refuses the job before any launch and names the row; a test holds the rows and the ids of the table equal (an id with no row and a row with no id both fail); each row goes red when its property or its part of the guard is dropped through the test seam"
  - id: d4
    item: "V10: no user manager, native Windows and macOS are refused before any process starts"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t5-agy-fix-turns/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-agy-fix-turns
title: "Fix turns with an agy conversation: --conversation with the handle of turn 0, the usage of each turn, the recorded not-found text"
kind: code
complexity: 5
dependsOn:
  - t4-agy-preflight-rows
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/RoleFailover.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/AAgyCoderFixTurnsTests.cs
dod:
  - id: d1
    item: "FakeCli as agy: red, then green after 2 fix turns; the recorded argv shows --conversation with the id of turn 0 on both, with the same home and guard folder; an invalid report gets ONE repair turn in the same conversation"
  - id: d2
    item: "V13 and V16: a turn that answers with the recorded not-found text of the fixture ends the attempt as a crash and the next model starts from the last coai commit; the usage line of each turn holds that turn only (the sum agy reports is subtracted)"
  - id: d3
    item: "nothing here needs the codex story: the tests pass with a table in which codex is refused"
```

## v2/epoch-3/e3-roles-complete/stories/s6-agy-coder/tasks/t6-agy-boundary-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-agy-boundary-scenarios
title: "Boundary scenarios with FakeCli as the agy coder inside the real unit: writes outside, reads of what is hidden, a new unit, a Windows program"
kind: test
complexity: 5
dependsOn:
  - t5-agy-fix-turns
parallel: false
security: true
files:
  - src_mcp/tests/AAgyCoderBoundaryTests.cs
dod:
  - id: d1
    item: "V7: the fake coder writes the main checkout, a folder beside the worktree, the .git pointer and the stand-in home: each is stopped by the OS, the job ends coder-boundary and is never green, and the main checkout is byte-equal"
  - id: d2
    item: "V8: a child command of the fake cannot read a file of the hidden main checkout, the worktree of another job or the stand-in sign-in files, cannot start a unit and cannot run a Windows .exe; a control unit without the additions shows that the same reads then work"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s7-vendor-seam-rows
title: "The vendor seam of the coder, built whatever the verdicts are: one table of verdicts and measured releases, the fix loop through the seam, the measured lists in role-defaults, the route by shipped vendor"
dependsOn:
  - s4-codex-agy-measure
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t1-coder-vendor-seam/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-coder-vendor-seam
title: "One seam per vendor for the coder launch (capability, argv, home, sign-in, program), a resume handle that carries the vendor, and shared/coder-boundary-releases.json as the one table; claude unchanged"
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
  - src_mcp/runners/Toolchain/CoderProgram.cs
  - src_mcp/tests/ACoderVendorSeamTests.cs
dod:
  - id: d1
    item: "one type per vendor gives the write capability probe, the fresh and the resumed argv, the home layout, the sign-in bind or the variable name, and the real path of the program; the claude argv, the property list of the claude unit and the floor of ClaudeWriteCapability are what they were (the existing coder tests pass unchanged)"
  - id: d2
    item: "V5: a resume handle carries the vendor beside the job and the worktree, and a handle of another vendor, job or worktree is refused before a process starts"
  - id: d3
    item: "V11: shared/coder-boundary-releases.json is the ONE table: per vendor the verdict (ship or refuse), the measured releases (exact; each with the heading of its results and of its judgement) and the ids of the properties the verdict relies on; a vendor with the verdict refuse or with no release is refused with a sentence that names the shipped vendors; a scan test fails when a second place decides it, and its companion proves the scan matches the one place"
  - id: d4
    item: "the table is filled only from the judgement file: a test reads the repository and fails for a release whose results heading or judgement verdict is not there, and for a relied-on id that the judgement does not list"
  - id: d5
    item: "the seam is built for every combination of verdicts: with both vendors refused the table still refuses them by data and the tests of this task pass"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t2-fix-loop-through-the-seam/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-fix-loop-through-the-seam
title: "The fix loop, the repair turn and the failover read the session, the report and a lost session through the vendor seam; proved with a test vendor"
kind: code
complexity: 5
dependsOn:
  - t1-coder-vendor-seam
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Server/RoleJobs/RoleModelLauncher.cs
  - src_mcp/tests_fakecli/CoderVerbs.cs
  - src_mcp/tests/ACoderFixLoopThroughTheSeamTests.cs
dod:
  - id: d1
    item: "with a test vendor in an injected table (FakeCli) a coder job goes red, then green after 2 fix turns in the session of turn 0; an invalid report gets ONE repair turn in that session; the claude fix-loop tests pass unchanged"
  - id: d2
    item: "V13: a lost session on a fix turn ends the attempt as a crash and the next model starts from the last coai commit; no fix turn starts a new session by itself; one usage line for each launch"
  - id: d3
    item: "no line of this task names codex or agy: s5 and s6 each add their vendor without a change here"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t3-role-defaults-rows/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-role-defaults-rows
title: "shared/role-defaults.json: the codex and agy main-AI rows from the measured lists, and the failover entries of scout and coder"
kind: config
complexity: 4
dependsOn: []
parallel: false
files:
  - shared/role-defaults.json
  - shared/role-model-names.json
  - shared/role-route-vectors.json
  - src_mcp/tests/ARolesAnswerTests.cs
dod:
  - id: d1
    item: "the codex and agy main-AI rows hold roles and bands made of names from RESULTS_model_lists.md only, as the owner confirmed them (SPEC section D and section H, question 3); no name is added that the CLI did not print; the vendor-routing rule holds for every entry"
  - id: d2
    item: "the scout and coder lists of the claude main-AI row get the failover entries of SPEC section D behind whenVendorRow; a coder entry of another vendor is added only for a vendor whose verdict is ship; the route vectors cover each new list; the roles tool answers them"
```

## v2/epoch-3/e3-roles-complete/stories/s7-vendor-seam-rows/tasks/t4-coder-route-by-vendor/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-coder-route-by-vendor
title: "The route of a coder keeps the vendors the table ships; the words claude only leave the product"
kind: code
complexity: 4
dependsOn:
  - t1-coder-vendor-seam
  - t3-role-defaults-rows
parallel: false
files:
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
    item: "in every combination the fake records ZERO launches of a refused vendor, a model the caller named on a refused vendor is refused before any folder, and no test of a combination needs or waits for a launch of a refused vendor"
  - id: d3
    item: "the unit of each vendor holds only the sign-in bind or the variable name of that vendor (read from the properties of the unit); the fix-turn count stays the count of the job; a spent quota skips only the other models of that vendor"
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
    item: "the test reads the table of this repository, so it passes in each of the four combinations: for each vendor the table ships, a coder job routed to it goes red, then green after one fix turn, and the recorded argv shows the session of turn 0 resumed (exec resume, --conversation); for each vendor the table refuses, start_role with a model of that vendor is refused and the fake records no launch of it"
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
    item: "4 paid calls at most and 0.10 USD in total; a second run of a role only after a recorded change; nobody edits the clone while a job runs"
```

## v2/epoch-3/e3-roles-complete/stories/s8-proof/tasks/t4-coder-resume-paid-proof/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-coder-resume-paid-proof
title: "Real coder job of each shipped vendor with its smallest model: a planted red check, one fix turn in the session of turn 0, green"
kind: measure
complexity: 5
dependsOn:
  - t3-read-only-paid-proof
parallel: false
security: true
files:
  - v2/epoch-3/e3-roles-complete/RESULTS_roles_proof.md
dod:
  - id: d1
    item: "for each vendor the table ships: one real coder job with the smallest model of that vendor, on the measured release, on a fixture repository with its own toolchain file and a planted red check; the file holds the job id, the release, the session id of turn 0 and of the fix turn (equal, read from the own stream of the CLI), fixTurns, green, the properties of the unit, the preflight rows and the spend"
  - id: d2
    item: "2 paid runs per vendor at most and 0.50 USD in total (the count of tokens where the CLI names no cost); the real sign-in file has the same bytes and modification time before and after"
  - id: d3
    item: "a vendor the table refuses is written as not run, with the reason, and is not a missing proof; with both refused the task is done when the file says so"
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
    item: "ARCHITECTURE.md and README.md name the scout and judge roles with their envelopes, review_plan with featureFolder (validate first, the pack, the scope, the limits, what is approved) and the coder of each shipped vendor with the measured limits of its boundary and its measured releases; the list of roles in both files equals RoleDescriptors.Shipped (a test or a grep in the task result proves it)"
  - id: d2
    item: "the docs say how a new release of codex or agy is added to shared/coder-boundary-releases.json (measure, judgement, one commit) and what a person sees on a release that is not in it"
  - id: d3
    item: "server.md gets a section that records what this epic decided differently from its sections 2, 8, 10, 19, 20 and 23; research/module_server.md describes the code as it is"
```
