# coai v2 — epoch 3, epic E2 (coder: one task to green) — stories written by the BA role; epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§2 coder delegation, §10 fix loop, §12 adapters, §16 rows 1/5/8/9, §17.4, §18.2-18.3, §19-§22); built on epoch-3 E1 (role jobs, router, failover, work-plan schema).

## v2/epoch-3/e2-coder-one-task/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e2-coder-one-task
title: "Coder: one task to green on this repository"
mvp: true
half: server
dependsOn:
  - e1-role-jobs-ba
doneWhen:
  - "coder is one role case; this repository through .coai/toolchain.yaml; claude only"
  - "green in ≤ 3 fix turns; main-AI tokens and wall time below the baseline; accepted gate findings not higher"
  - "task session inherits the feature plan proceed; write boundary at launch"
```

## v2/epoch-3/e2-coder-one-task/stories/s1-toolchain-file/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-toolchain-file
title: "This repository's checks through .coai/toolchain.yaml: schema, reader, check runner, failure digest"
dependsOn: []
```

## v2/epoch-3/e2-coder-one-task/stories/s1-toolchain-file/tasks/t1-toolchain-schema-reader/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-toolchain-schema-reader
title: "shared/toolchain.schema.json and its strict reader (version, projects with path, adapter, build argv, test argv, memoryMb; skip; timeouts)"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - shared/toolchain.schema.json
  - src_mcp/core/Toolchain/ToolchainFile.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "a valid file read through IYamlReader (StrictYamlReader) gives the projects with their build and test argv as lists; an unknown key, an argv that is not a list of strings, a project path outside the repository (absolute, drive, `..`) and a version other than 1 are each refused with a sentence that names the key"
  - id: d2
    item: "no file gives the state not_configured, not an error and not an empty project list; a test holds the schema file and the reader's key tables equal"
```

## v2/epoch-3/e2-coder-one-task/stories/s1-toolchain-file/tasks/t2-check-runner/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-check-runner
title: "Check runner: run build then test argv of each project in a given tree, record argv, exit code, duration and log; not_configured is never green"
kind: code
complexity: 6
dependsOn:
  - t1-toolchain-schema-reader
parallel: false
files:
  - src_mcp/runners/Toolchain/CheckRunner.cs
  - src_mcp/core/Toolchain/CheckResult.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "on a fixture tree with a toolchain file: build exit 0 and test exit 0 gives green true with one record per step (argv, exit code, duration, log path); a build that fails gives green false and the test step is not run"
  - id: d2
    item: "a tree with no toolchain file gives checks not_configured and green false; a step over its timeout is stopped with its process tree and recorded as failed; the argv is started without a shell and the `{jobs}` placeholder is replaced"
  - id: d3
    item: "the toolchain file is read from the tree that is checked (the coder worktree at its head), and a log is cut at 20 MB"
```

## v2/epoch-3/e2-coder-one-task/stories/s1-toolchain-file/tasks/t3-failure-digest/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-failure-digest
title: "Failure digest from a failed step: MSBuild error lines and the test executable's failed tests, with a raw tail as fallback"
kind: code
complexity: 5
dependsOn:
  - t2-check-runner
parallel: false
files:
  - src_mcp/core/Toolchain/FailureDigest.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "a recorded real MSBuild output of this repository with 2 errors gives 2 items {step, file, line, code, message}; a recorded real failed run of the CoaiMcp.Tests executable gives one item per failed test"
  - id: d2
    item: "the digest never has more than 40 items or 16 KB; an output no parser knows gives one item with the last lines of the raw output; credential-like text is redacted by the existing Redaction rule"
```

## v2/epoch-3/e2-coder-one-task/stories/s1-toolchain-file/tasks/t4-this-repository-file/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-this-repository-file
title: ".coai/toolchain.yaml of this repository: src_mcp build and its MTP test executable"
kind: config
complexity: 3
dependsOn:
  - t2-check-runner
parallel: false
files:
  - .coai/toolchain.yaml
dod:
  - id: d1
    item: "a test reads the committed .coai/toolchain.yaml with the reader of t1 and gets the src_mcp project with a build argv and a test argv that names the MTP test executable (never `dotnet test`); the file is of the strict YAML subset (block lists, no flow list other than [])"
  - id: d2
    item: "the check runner on a clean checkout of this repository ends the build step with exit 0 and starts a real test of the suite (a recorded run is in the task's results)"
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-write-boundary
title: "Write boundary at launch: the coder worktree, the claude write launch and the check commands cannot write outside the worktree"
cornerstone: true
dependsOn: []
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/tasks/t1-boundary-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-boundary-measure
title: "Measure on the installed claude CLI which launch flags let a session edit and run commands in one folder only (permission mode, tools, restricted, add-dir, cwd), fresh and resumed"
kind: measure
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - v2/spikes/RESULTS_coder_write_boundary.md
dod:
  - id: d1
    item: "the results file has one row per flag set with the CLI version, and for each row the measured answer to: an edit in the worktree, an edit of a canary outside it, a shell command that writes outside it, a read outside it, each for a fresh and a resumed session"
  - id: d2
    item: "the file names ONE flag set to ship and says plainly what it does not stop (for example a shell command), and which OS-level boundary (read-only bind of the main checkout through systemd-run properties, or none) closes that gap on Linux, WSL and Windows"
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/tasks/t2-coder-worktree/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-coder-worktree
title: "Coder worktree: git worktree add -b coder/<jobId> at the base SHA under the machine-local root, removed by coai, at most 20 live per repository"
kind: code
complexity: 5
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Worktrees/CoderWorktree.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "a start makes the branch coder/<jobId> and the folder coai-coder-<jobId> at exactly the given base SHA, outside the main checkout; the job id is the only free part of both names and a name that would leave the root is refused before git runs"
  - id: d2
    item: "the 21st live coder worktree of one repository is refused (never evicted); a base SHA that does not exist is refused with a sentence; the main checkout's HEAD, index and files are byte-equal before and after"
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/tasks/t3-claude-write-launch/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-claude-write-launch
title: "The claude coder launch: a write arm of the launch confinement with the measured flag set, cwd = the worktree, resumable with --resume"
kind: security
complexity: 8
dependsOn:
  - t1-boundary-measure
  - t2-coder-worktree
parallel: false
security: true
files:
  - src_mcp/runners/Consultation/LaunchConfinement.cs
  - src_mcp/runners/Consultation/ClaudeConsultant.cs
  - src_mcp/core/QuestionConsult/ConfinementPlanner.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "the argv of a coder launch is exactly the flag set that RESULTS_coder_write_boundary.md names, with the worktree as cwd and as the only added directory and no MCP server; a resumed turn has the same argv plus --resume <handle>"
  - id: d2
    item: "every existing consultant and question-row launch builds the same argv as before, byte for byte (the existing argv tests pass unchanged); a claude CLI that lacks a needed flag is refused before the launch with the cure, never started with a weaker boundary"
  - id: d3
    item: "a vendor other than claude asked for a write launch is refused with a sentence that says the coder is claude only in this epic"
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/tasks/t4-checks-inside-boundary/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-checks-inside-boundary
title: "The check commands (build, test, formatter) run inside the same boundary: cwd in the worktree, minimal environment, main checkout read-only where the launcher can do it"
kind: security
complexity: 7
dependsOn:
  - t1-boundary-measure
  - t2-coder-worktree
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/CheckBoundary.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "a check command is started with the worktree as cwd and an allow-listed environment (no vendor key and no COAI_ secret name reaches it; a test lists what does)"
  - id: d2
    item: "under the systemd-run launcher a fixture build command that writes a file into the main checkout fails and the main checkout is unchanged; on a launcher with no OS boundary the result records `boundary: cwd-only` and the filesystem snapshot of the main checkout is kept as evidence"
```

## v2/epoch-3/e2-coder-one-task/stories/s2-write-boundary/tasks/t5-boundary-tests/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-boundary-tests
title: "Boundary scenarios with FakeCli: a write outside the worktree, an edit of the main checkout, a person's edit of the main checkout during the job"
kind: test
complexity: 6
dependsOn:
  - t3-claude-write-launch
  - t4-checks-inside-boundary
parallel: false
security: true
files:
  - src_mcp/tests_fakecli
  - src_mcp/tests
dod:
  - id: d1
    item: "FakeCli as coder that writes into the main checkout or beside the worktree: the launch-level boundary is what stops or reports it; the answer names the path and the job does not publish a green result"
  - id: d2
    item: "an edit of the main checkout by another process during a coder job does NOT fail the job (unlike a read-only role's tree-changed): it is recorded as snapshot evidence only (server.md section 16, decision 8)"
```

## v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-coder-role-case
title: "coder is one role case of start_role: contract, task check, the first turn, the envelope"
cornerstone: true
dependsOn:
  - s2-write-boundary
```

## v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/tasks/t1-coder-contract/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-coder-contract
title: "The coder's contract: shared/coder-report.schema.json with a refused arm, its strict reader, the coder prompt (task, DoD, files, the rules of the worktree)"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - shared/coder-report.schema.json
  - src_mcp/core/Coder/CoderReport.cs
  - src_mcp/src/Server/RoleJobs/CoderPrompt.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "the reader accepts {status: ok, dod:[{id, met, evidence}], notes} and {status: refused, reason}; an unknown key, a dod id that the task does not have and a missing dod id are errors with the key named; a test holds the schema file and the reader equal"
  - id: d2
    item: "the prompt holds the task's title, kind, files and every dod item, the rule that coai (not the coder) commits, builds and tests, and no path outside the worktree; an override of the coder prompt in RolePrompts is used first"
```

## v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/tasks/t2-coder-task-check/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-coder-task-check
title: "The coder's request check: taskFile is a task.yaml of a valid plan; complexity and security go to the router; refusals before any folder"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderTask.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "start_role(role: coder) with a taskFile that is not a file called task.yaml inside the repository, that fails the work-plan task rules, or with no taskFile, is refused with a sentence and no job folder is made"
  - id: d2
    item: "a task of complexity 4 gets the route [band model, then the coder list]; a task with security true or complexity 10 gets the important list; a route entry whose vendor is not claude is dropped from a coder route and a route with no claude model is refused naming COAI_ROLES_MODELS"
```

## v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/tasks/t3-coder-first-turn/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-coder-first-turn
title: "CoderWork, turn 0: descriptor registered, worktree made, claude launched with the write boundary, coai commits the work, the envelope is the result"
kind: code
complexity: 8
dependsOn:
  - t1-coder-contract
  - t2-coder-task-check
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/CoderEnvelope.cs
  - src_mcp/src/Server/RoleJobs/RoleDescriptors.cs
  - src_mcp/src/Server/RoleJobs/RoleJobModels.cs
  - src_mcp/tests_fakecli
  - src_mcp/tests
dod:
  - id: d1
    item: "with FakeCli as claude: start_role(role: coder, taskFile) ends succeeded with a result that holds jobId, baseSha, headSha, branch coder/<jobId>, schemaValid, fixTurns 0 and filesChanged taken from `git diff --name-status <baseSha> <headSha>` only (a report that claims other files changes nothing)"
  - id: d2
    item: "the coder's changes are one coai commit on coder/<jobId>; a report that is not valid gets ONE repair turn in the same session and then schemaValid false; a report that claims met true never makes the job green by itself"
  - id: d3
    item: "the runner compares its generation before the commit and before the result: a stale runner writes neither (exit 75); a cancel stops the CLI child and leaves the worktree for the retention sweep"
```

## v2/epoch-3/e2-coder-one-task/stories/s3-coder-role-case/tasks/t4-coder-tool-text/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-coder-tool-text
title: "start_role names coder: tool description, the refusal list of roles, optional baseSha, role_status shows the envelope"
kind: code
complexity: 3
dependsOn:
  - t3-coder-first-turn
parallel: false
files:
  - src_mcp/src/Tools.cs
  - src_mcp/src/Server/RoleJobs/RoleJobService.cs
  - src_mcp/src/Server/RoleJobs/RoleJobAnswers.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "over real stdio the start_role description names coder, an unknown role's refusal lists coder, and there is no start_coder tool (the coder is the role case, server.md section 19)"
  - id: d2
    item: "start_role with baseSha starts the worktree at that commit; without it the base is the HEAD of repoPath at the start, and role_status returns the same envelope JSON that result.json holds"
```

## v2/epoch-3/e2-coder-one-task/stories/s4-fix-loop/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-fix-loop
title: "To green: coai formats, builds and tests after each turn; failures go back to the same claude session, at most 3 fix turns"
dependsOn:
  - s1-toolchain-file
  - s3-coder-role-case
```

## v2/epoch-3/e2-coder-one-task/stories/s4-fix-loop/tasks/t1-checks-after-turn/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-checks-after-turn
title: "After each coder turn coai runs the checks in the worktree and writes them into the envelope (checks, green)"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderChecks.cs
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/CoderEnvelope.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "a FakeCli turn whose work builds and passes gives green true with checks [{step, argv, exitCode, durationMs, logRef}]; a repository with no .coai/toolchain.yaml gives checks not_configured, green false and the job state failed with stop not-configured"
  - id: d2
    item: "the heartbeat phase is `checks` while a build or test runs, so a long build is never stalled or lost; the checks run through the boundary of s2 t4"
```

## v2/epoch-3/e2-coder-one-task/stories/s4-fix-loop/tasks/t2-fix-turns/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-fix-turns
title: "Fix turns: the failure digest goes to the SAME session with --resume; cap COAI_FIX_ROUNDS (default 3); each turn is one coai commit"
kind: code
complexity: 8
dependsOn:
  - t1-checks-after-turn
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/src/Server/RoleJobs/RoleJobSettings.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "FakeCli red, then green after 2 fix turns: FakeCli's recorded argv shows --resume with the handle of turn 0 on both fix turns and the worktree as cwd; no fix turn starts a new session; the envelope says fixTurns 2, green true"
  - id: d2
    item: "FakeCli always red: exactly 3 fix turns, then the job is failed with stop not-green and the last digest in the result; COAI_FIX_ROUNDS=1 gives exactly 1; a value that is not a whole number from 0 to 10 falls back to 3 with a note"
  - id: d3
    item: "the whole job stays inside timeoutMinutes: a turn or a check that would pass the budget is stopped and the job ends failed with stop timeout, the branch and its commits kept"
```

## v2/epoch-3/e2-coder-one-task/stories/s4-fix-loop/tasks/t3-coder-failover/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-coder-failover
title: "Failover of a coder: limit, refusal or crash goes to the next claude model in the same worktree, from the last coai commit, in a new session"
kind: code
complexity: 7
dependsOn:
  - t2-fix-turns
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/RoleFailover.cs
  - src_mcp/src/Server/RoleJobs/CoderWork.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "FakeCli model 1 crashes in mid-turn with files half written: the worktree is put back to the last coai commit (uncommitted changes dropped, proven by git status), model 2 starts from turn 0 with the task prompt, one switch line and one role-model-switched notice"
  - id: d2
    item: "the fix-turn count is per job, not per model (a switch does not give 3 new turns); output-invalid, cancelled and not-green never fail over; the read-only roles (ba, probe) behave as before (their tests pass unchanged)"
```

## v2/epoch-3/e2-coder-one-task/stories/s4-fix-loop/tasks/t4-fix-loop-scenarios/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-fix-loop-scenarios
title: "Fix-loop scenarios on the real binary over stdio with FakeCli: green at turn 0, green after 2, never green, server killed in mid-loop"
kind: test
complexity: 6
dependsOn:
  - t3-coder-failover
parallel: false
files:
  - src_mcp/tests_fakecli
  - src_mcp/tests
dod:
  - id: d1
    item: "the four scenarios pass against a real coai-mcp and the real detached runner on a fixture repository with its own .coai/toolchain.yaml (a real small build and test, no model call)"
  - id: d2
    item: "the MCP server killed while a check runs: a new server reads the same job and it ends succeeded and green, generation unchanged, no role-job-lost notice"
```

## v2/epoch-3/e2-coder-one-task/stories/s5-plan-proceed-inherited/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-plan-proceed-inherited
title: "The task session inherits the feature's plan proceed: review_code works on a coder branch without its own plan round"
dependsOn:
  - s3-coder-role-case
```

## v2/epoch-3/e2-coder-one-task/stories/s5-plan-proceed-inherited/tasks/t1-plan-binding/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-plan-binding
title: "Bind a coder job's session (repository + coder/<jobId>) to the approved plan revision of the feature it was started from"
kind: security
complexity: 8
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/src/Server/RoleJobs/CoderPlanBinding.cs
  - src_mcp/src/Server/PanelService.cs
  - src_mcp/core/Rounds/RoundMachine.cs
  - src_mcp/core/Rounds/SessionState.cs
  - src_mcp/tests
dod:
  - id: d1
    item: "review_code on coder/<jobId> is accepted when the branch the job was started from has a plan that proceeded; the session records the inherited plan revision (source session id and plan hash) and the plan text the round is judged against"
  - id: d2
    item: "a branch named coder/x that no coder job of this data folder made is refused as today (PlanProceeded false); so is a coder job whose starting branch has no proceeded plan, and a job whose source plan changed after the start (the hash differs)"
  - id: d3
    item: "the binding is read from job.json (the authority), never from the branch name alone; every existing RoundMachine and PanelService test passes unchanged"
```

## v2/epoch-3/e2-coder-one-task/stories/s5-plan-proceed-inherited/tasks/t2-plan-binding-tests/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-plan-binding-tests
title: "Scenarios: two sibling task sessions resolve their findings independently; the feature session is not changed by them"
kind: test
complexity: 5
dependsOn:
  - t1-plan-binding
parallel: false
files:
  - src_mcp/tests
dod:
  - id: d1
    item: "two coder jobs of one feature each run a code round with FakeCli reviewers: a resolve in one does not move the stage, the round count or the findings of the other or of the feature's own session"
  - id: d2
    item: "status on a coder branch names the plan it inherited and from which session; a second code round with again true works on the coder branch"
```

## v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-proof-and-measure
title: "Proof: one real task of this repository to green by the coder, measured against the main-AI baseline"
dependsOn:
  - s4-fix-loop
  - s5-plan-proceed-inherited
```

## v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t1-baseline/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-baseline
title: "Define the measure and record the baseline: main-AI token count, wall time and accepted gate findings for one task done by the main AI itself"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - v2/epoch-3/e2-coder-one-task/BASELINE.md
dod:
  - id: d1
    item: "the file says how each of the three numbers is read (which transcript or ledger, from which event to which event, what counts as an accepted finding) so that a second person gets the same numbers"
  - id: d2
    item: "the file holds the baseline numbers of at least one real task of this repository of a named kind and complexity, with its commit, and names the task the coder run of t3 will do (same kind, complexity within 1)"
```

## v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t2-epic-acceptance/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-epic-acceptance
title: "Epic acceptance on the real binary over stdio with FakeCli: coder job on a fixture task to green, boundary, inherited plan, one code round"
kind: test
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/tests/TheCoderEpicAcceptanceTests.cs
  - src_mcp/tests_fakecli
dod:
  - id: d1
    item: "one test drives start_role(coder) to green after 1 fix turn, then review_code on coder/<jobId> to a verdict without a plan round on that branch, on a real coai-mcp with the detached runner"
  - id: d2
    item: "the same run proves: the route held claude models only, FakeCli saw --resume once, nothing outside the worktree changed, and the envelope's filesChanged equals the git diff of base to head"
```

## v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t3-real-coder-run/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-real-coder-run
title: "Real coder job (claude) on the task named in the baseline: green in at most 3 fix turns, the three numbers compared with the baseline"
kind: measure
complexity: 5
dependsOn:
  - t1-baseline
  - t2-epic-acceptance
parallel: false
files:
  - v2/epoch-3/e2-coder-one-task/RESULTS_coder_proof.md
dod:
  - id: d1
    item: "the results file holds the job id, route, attempts, fixTurns (at most 3), green true from this repository's .coai/toolchain.yaml checks, the wall time, the coder's own usage and cost, and what was paid in total"
  - id: d2
    item: "main-AI token count and wall time are below the baseline and the accepted findings of the code gate on the coder branch are not higher than the baseline's; a number that misses is written as a miss with its cause, not left out"
  - id: d3
    item: "at most two paid runs, the second only after a recorded change; the worktree of the main checkout is not edited while the job runs"
```

## v2/epoch-3/e2-coder-one-task/stories/s6-proof-and-measure/tasks/t4-docs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-docs
title: "Docs: the coder role, .coai/toolchain.yaml, the fix loop and the write boundary in ARCHITECTURE.md and README.md"
kind: docs
complexity: 3
dependsOn:
  - t3-real-coder-run
parallel: false
files:
  - ARCHITECTURE.md
  - README.md
  - v2/architecture/server.md
dod:
  - id: d1
    item: "ARCHITECTURE.md and README.md name start_role role coder, the envelope keys, COAI_FIX_ROUNDS, the toolchain file's keys and the measured limits of the write boundary; the list of roles in both files equals RoleDescriptors.Shipped (a test or a grep in the task result proves it)"
  - id: d2
    item: "server.md records what this epic decided differently from its sections 2, 10 and 12 (no start_coder tool, the toolchain file in the strict YAML subset, what is left to e4)"
```
