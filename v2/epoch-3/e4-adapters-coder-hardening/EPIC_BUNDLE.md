# coai v2 — epoch 3, epic E4 (adapters dotnet + node, coder hardening) — epic plan for the gate (generated)

Parent plan: v2/PLAN.md; architecture: v2/architecture/server.md (§10, §11, §12, §15, §16 row 9, §23); built on epoch-3 E2 (the coder, the toolchain file, the check boundary, the fix loop, the claude key pool) and, for story s6, on E3 (the vendor seam; the codex and agy coder). The full spec is SPEC.md in the same folder; its digest is below, before the YAML files. The plan gate has not run yet.

| Story | Tasks | Complexity sum | Depends on |
|---|---|---|---|
| s1-measure-first | 3 | 14 | — |
| s2-detection | 6 | 31 | s1-measure-first |
| s3-steps-in-boundary | 4 | 20 | s2-detection |
| s4-digest-readers | 4 | 20 | s1-measure-first |
| s5-format-selection-rerun | 6 | 30 | s3-steps-in-boundary, s4-digest-readers |
| s6-fix-loop-hardening | 4 | 21 | s5-format-selection-rerun, e3-roles-complete/s8-proof |
| s7-proof | 4 | 17 | s6-fix-loop-hardening |
| **Total** | 31 | 153 | |

## Spec digest (the full spec is SPEC.md beside this file; its section letters are used here and in the task files)

**Goal.** A repository with no `.coai/toolchain.yaml` gets its checks from its own manifests. Failure digests come from recorded
real output. The fix loop has its caps, a no-progress stop and a flaky re-run, for every vendor whose coder ships. Every check still
runs inside the check boundary of E2.

**Today** (E2, `f19a7fcb`). The only source of checks is the file, read at the base commit. A project has a build argv and a test
argv; `{jobs}` is the only placeholder. Every step of a coder job runs in a `systemd-run --user` unit: the build with the host
network, the test with only a loopback device. The digest reads MSBuild lines and this repository's test executable. The format
slot says `not_configured`. Every round runs the whole build and the whole suite (34 s and 8 m 14 s here). The only clock is
`timeoutMinutes`, 30 by default.

**Decisions that change server.md.** (1) One wall clock: the job's `timeoutMinutes`; 45 by default for a coder, 30 for the other
roles. (2) Detection never guesses and never checks a part: what it cannot turn into commands refuses the repository. (3) Digest
readers read the console output, not TRX or JUnit files; the exit code is the verdict. (4) Selection has two levels, only the
full test command can say green, and the finer level comes only from the file's `testFor`. (5) The re-run of a failed test is narrow, bounded and never excuses the job's own tests. (6)
`no-progress` is defined: the same failure fingerprints after two fix turns in a row. (7) A `review_code` started by coai is E5's,
not E4's (§23 said E4; there is no scheduler before E5). (8) Five step names run: restore, format, build, typecheck, test; lint,
audit and coverage stay names of `skip`. (9) npm only; pnpm, yarn and bun are refused by name. (10) The toolchain file gets
`restore`, `format`, `testFor` and `testFiles`; `version` stays 1.

**A. Where the checks come from.** One pure function, `ToolchainResolve`. A file at the base wins, and a project that states a
command runs exactly what it states (this repository's file reads as today). A file project that states no command gets the
adapter's defaults. No file: detection at the base commit AND at the head commit, both read from the main repository's git objects
(never the working tree, never through the worktree's `.git` pointer). The data is `shared/toolchains.json`, embedded in the
binary; C# only reads manifests. Search limits: 4 folders deep, 20 projects, 500 manifests, 256 KiB a manifest, 16 MiB read; a link
and a submodule are never read; over a limit is a refusal. .NET: a folder with one solution file is a unit; a project file nobody
references is a unit; the ProjectReference graph, the test projects and their runner are facts. Node: each `package.json` is a
project, a workspace root is one project; the lock file, the scripts, the runner and a prettier configuration are facts. Defaults
(frozen by s1): `dotnet restore`, `dotnet build --no-restore`, `dotnet test --no-build`; `npm ci --ignore-scripts`, `npm run
build`, `npm run typecheck` or `npx --no-install tsc --noEmit`, `npm test`. A scan test holds the strict rules: no shell, never
`npm install`, never an install script, never a tool downloaded at check time. **Refused** (`toolchain_refused`, the sentence
names the path and the cure "name the commands in `.coai/toolchain.yaml`"): a manifest of php, rust, python or java; two solution
files in one folder; test projects of two runners; a `package.json` with no lock file, two, or a pnpm, yarn or bun one; a manifest
that cannot be parsed; a limit passed. No manifest at all is `not_configured`. One refused project refuses the repository.
**Both trees:** a project only the head has is added; a step the base had and the head has not is refused (`the worktree removed
<step> of <project>`), so a coder cannot delete a test script and be green; a changed script text is named in the result. `COAI_TOOLCHAIN_DETECT=off` gives E2's answer.
Scenarios D1 to D11.

**B. The steps and the boundary.** Order of a round: restore, format, build, typecheck, test. Every step of a coder job, the
selected run and the re-run included, starts through `CheckBoundary.RunAsync` after E2's preflight. **The boundary is not
widened**: no new bind, no new writable path, no variable from coai's environment, no host network for a step that had none. A
test compares the unit's property list and environment names with E2's, item for item. Added, both checked: a closed table of
adapter switches with constant values; and the project folder as the working folder of a detected step (through the one link
resolver, inside the worktree, else refused). The host network is only for restore and for the build of a file project (E2,
unchanged); every `host` entry of the data names the RESULTS heading that measured why. The E2 rule that the worktree and HEAD are
unchanged after the checks stays. No credential reaches a step, so a private registry is not supported. Scenarios T1 to T6.

**C. Digests.** New pure readers, each tested on output recorded from the real tool by s1: `dotnet test` (VSTest and MTP form),
restore errors, tsc, the `npm ci` error block, jest, vitest, node:test. The reader is chosen by adapter, step and runner fact; for
a step of the file the adapter's readers are tried in a fixed order, each needs its own marker line, and a test runs every fixture
through every reader. No item gives the raw tail of E2: a digest is never empty. E2's limits hold (40 items, 16 KB, Redaction
first). Every item gets a fingerprint without the line number. A scan keeps home paths and user names out of the recordings.
Scenarios R1 to R5.

**D. The formatter turn.** After a turn's commit the formatter runs on the files that commit added or changed (the adapter's
extensions, no manifest, no lock file, 200 at most), each written `./<path>` so a file name is never an option, inside the
boundary with only a loopback device, 5 minutes at most. It may change only the files it was given: anything else, or a failure,
puts the worktree back to the turn's commit through `CoderRestore`. The result is committed as `style: format (coai)` through the
one commit road (generation first, the pool-key search of E2 s7). A format state is never a verdict and never goes to the coder.
The default formatter runs only with evidence that the repository uses one (prettier with a configuration; for .NET the form s1
measures, or none). Scenarios F1 to F6.

**E. Selection.** Level one, which projects run: from the diff of base to head; a detected project is affected by a changed path
in its folder, and in a dotnet unit only the test projects that reach a changed project through the reverse ProjectReference
graph; a project of the file is always affected; nothing affected means everything runs. Level two, which tests run first: only
where the FILE names a `testFor` (there is no default: a selected run the repository did not name could fail where its own test
command passes); a red selected run ends the round red; a green one is followed by the FULL test command on the same commit; **green is only ever the word of a full run**; "no test ran" is neither
green nor red and goes to the full command; after one failed full run the later rounds are full only. This repository's file gets
`testFiles` and `testFor` (s5 t6). Scenarios S1 to S8.

**F. The re-run.** Only a failed FULL test step whose every failure the digest names: narrowed with the file's `testFor` when
there is one and at most 5 tests failed, else the whole step when its first run took at most 300 s, else none. Never: another step kind; a timeout; a failure
with no name; **a test whose own file, or the file its failure names, the job itself changed**; a test excused once already; a third excuse in a job. A test that
passes is listed under `flaky`, leaves the digest and raises one notice. Stated limit: a narrowed re-run can excuse an
order-dependent failure; it is listed, never hidden. `COAI_FLAKY_RERUNS` 0 or 1. Scenarios K1 to K8.

**G. No progress.** A red round whose fingerprint set equals the one before it is a repeat; a fix turn that changes no file is a
repeat and its checks are not run again; two repeats in a row end the job with `no-progress`, which never fails over. Scenarios
L1 to L5.

**H. Caps.** Fix turns 3 (`COAI_FIX_ROUNDS`, E2); the wall clock; 2 repeats; 1 re-run a step and 2 excuses a job; step timeouts
from the file; 200 and 50 files; the detection limits. A fix turn is not begun when the time left is less than the last round of
checks took. Money is not a cap in E4.

**J. Security.** The boundary of E2, not widened; a property the unit lacks stops the measure task and needs a security judgement.
What decides the commands is the file at the base or shipped templates filled with facts of manifests from git objects; no argv
item is copied from a manifest. Manifests are untrusted: XML with DTD prohibited, JSON depth limit, size limits, no link. No
secret in an argv or a log. Tests never touch the real home. An effect is observed in a real unit, not asserted from an argv.

**Collisions with E3.** E3 still changes `CoderFixLoop.cs`, `CoderWork.cs`, `RoleModelLauncher.cs`, `RoleFailover.cs`, `Tools.cs`,
the fake's verbs and both `Program.cs`. E4 edits those only in s6 and s7; s6 depends on `e3-roles-complete/s8-proof`. E4 never
edits `CoderVendors`, `CoderUnit`, `CoderBoundary` or `BoundaryPreflight`. s1 to s5 touch none of them and can be built beside E3.

**Order.** s1 (measure) → s2 (detection) → s3 (steps in the boundary); s4 (readers) after s1; s5 after s3 and s4; s6 after s5 and
E3; s7 last.

**Measure first (s1, no paid call).** M1 a build without restore and without network; M2 `dotnet test` for each runner; M3 the
filter of this repository's test executable; M4 the npm commands inside the unit as it is; M5 what `npm ci` writes; M6 the
consoles of jest, vitest, node:test; M7 `dotnet format` on named files offline; M8 prettier; M9 sizes.

**Not in E4.** A `review_code` started by coai, parallel work (E5); php, rust, python, java (epoch 5 E3); lint, audit, coverage,
PR and CI (epoch 5 E4); a spend cap; a private registry; pnpm, yarn, bun; native Windows and macOS; a run of the checks on the
base commit before turn 0 (a red base ends at `no-progress` after two paid turns: risk 8).

**For the owner.** (1) 45 minutes as the default of a coder job. (2) npm only in E4. (3) This repository's file gets `testFiles`
and `testFor`; `src_vs_code` is not added as a project. (4) The paid proof: claude haiku, 2 runs and 0.30 USD at most, on a pool
key. (5) `epic.yaml` here has the E3 plan's wording of the resume line and `spec: SPEC.md` (also in `v2/_gen/v2-spec.json`);
`dependsOn` is unchanged. (6) Should a later epic check the base commit before the first paid turn?

## v2/epoch-3/e4-adapters-coder-hardening/epic.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: e4-adapters-coder-hardening
title: "Adapters dotnet + node + coder hardening"
half: server
dependsOn:
  - e2-coder-one-task
doneWhen:
  - "golden detection and argv on fixture repos (both trees, base..head diff)"
  - "parsers tested on recorded real output"
  - ".coai/toolchain.yaml override; not_configured is never green"
  - "this repository is a fixture that runs a real test"
  - "same session resumed is built before this epic (claude by e2-coder-one-task; codex and agy by e3-roles-complete, for each vendor its judgement shipped); here: caps and no-progress stop; flaky re-run, for every shipped vendor"
  - "write boundary at launch; task session inherits the feature's plan proceed"
  - "job lease with generation; survives an MCP client restart"
spec: SPEC.md
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s1-measure-first/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s1-measure-first
title: "Measure first: the .NET and Node commands, the formatters and a selected test run inside the E2 check unit, on the real tools, with no model call"
dependsOn: []
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s1-measure-first/tasks/t1-dotnet-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-dotnet-measure
title: "Measure the .NET commands inside the check unit: restore, build without restore, dotnet test for each runner, and the filter of this repository's test executable"
kind: measure
complexity: 5
dependsOn: []
parallel: false
files:
  - v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_dotnet.md
  - src_mcp/tests/fixtures/toolchain
  - src_mcp/tests/ADotnetStepsAreMeasuredTests.cs
dod:
  - id: d1
    item: "M1: the results file has one row per command with the SDK version, the exact argv, the exit code and the time, each run inside the check unit of E2 with no property changed: `dotnet restore` on the host network, then `dotnet build --no-restore` and `dotnet test --no-build` with only a loopback device, on a scratch clone of this repository and on a scratch VSTest project"
  - id: d2
    item: "M2: for `dotnet test` in three cases (a VSTest project; an MTP project with the runner named in global.json; an MTP project without it) the file has the argv that works or the error, the exit code of a failed test and of a run with zero tests, and the console lines that name a failed test, its message and its file and line; the recorded outputs are in fixtures/toolchain with the checkout path and the user name replaced"
  - id: d3
    item: "M3: for this repository's test executable the file has the argv that runs only the tests of two named test files, how long it takes, and the exit code when the filter matches no test"
  - id: d4
    item: "a command that needs a property the E2 unit does not have is written down as a finding and the task stops there: the unit is not changed in this task (SPEC rule J1); the runs are recorded tests that are skipped unless their variable names a folder, and they never use the real home"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s1-measure-first/tasks/t2-node-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-node-measure
title: "Measure the Node commands inside the check unit: npm ci without install scripts, build, tsc and npm test, and the consoles of jest, vitest and node:test"
kind: measure
complexity: 5
dependsOn: []
parallel: false
files:
  - v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_node.md
  - src_mcp/tests/fixtures/toolchain
  - src_mcp/tests/ANodeStepsAreMeasuredTests.cs
dod:
  - id: d1
    item: "M4: one row per command with the node and npm versions, the argv, the exit code and the time, inside the check unit of E2 with no property changed and the project folder as the working folder: `npm ci --ignore-scripts` on the host network with the cache in the job folder, then `npm run build`, `npx --no-install tsc --noEmit` and `npm test` with only a loopback device"
  - id: d2
    item: "M5: every path `npm ci` made in the worktree is listed with the answer of `git status` for it (ignored or not); nothing is written outside the worktree and the job folder, and the stand-in of the real home has the same bytes and times after the run"
  - id: d3
    item: "M6: for jest, vitest and node:test, each a scratch project with one passing and two failing tests: the console output recorded with NO_COLOR and CI set, and the exit code and the text of a run that is given a file with no test; a tsc output with two errors and a failed `npm ci` are recorded too"
  - id: d4
    item: "M9: the size of the step logs of one round and of the npm cache of one job are in the file; a command that needs a property the E2 unit does not have is a finding and the task stops there (SPEC rule J1)"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s1-measure-first/tasks/t3-format-measure/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-format-measure
title: "Measure the formatters inside the check unit: dotnet format on named files without restore and without network, prettier on named files; name one default per adapter or none"
kind: measure
complexity: 4
dependsOn: []
parallel: false
files:
  - v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s1_format.md
  - src_mcp/tests/fixtures/toolchain
  - src_mcp/tests/AFormattersAreMeasuredTests.cs
dod:
  - id: d1
    item: "M7: `dotnet format` is measured in the check unit with only a loopback device and `--no-restore`, in the `whitespace --folder` form and in the `style` form, each with `--include` of three files of a scratch clone of this repository: the time, the exit code, the files it changed (only the included ones, or more) and whether a second run changes nothing"
  - id: d2
    item: "M8: prettier `--write` through `npx --no-install` on three named files of a scratch project: the same four answers; for a file with a syntax error the exit code and the text; for a file named `--help` written as `./--help` whether it is read as a file"
  - id: d3
    item: "the file names ONE default per adapter (an argv, or `none` with the reason) and the longest time measured; a form that changed a file it was not given, needed the network or took more than 60 seconds for three files is not a default"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s2-detection
title: "Detection and defaults: a repository with no .coai/toolchain.yaml gets its checks from its manifests, read at the base commit and at the head commit"
dependsOn:
  - s1-measure-first
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t1-toolchains-data/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-toolchains-data
title: "shared/toolchains.json: the stacks, their manifest names, the default argv of every step, the switches, the network state and the measured exit codes, with a strict reader"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - shared/toolchains.json
  - src_mcp/core/Toolchain/ToolchainDefaults.cs
  - src_mcp/core/CoaiMcp.Core.csproj
  - src_mcp/tests/AToolchainDefaultsTests.cs
dod:
  - id: d1
    item: "the file names dotnet and node as built and php, rust, python and java as known and not built, each with its manifest names; for a built stack it has the argv of restore, build, typecheck, test and format exactly as the results of s1 name them (there is no default testFor), the switch names of its steps, the network state of each step, the exit codes and marker lines that mean no test ran, and the file extensions a formatter is given"
  - id: d2
    item: "the binary reads only the embedded copy; a test holds the file and the reader's tables equal; an argv item may hold only the placeholders {jobs} and {unit}, and an item of format {file}; an unknown placeholder or an unknown member fails the test"
  - id: d3
    item: "a scan test holds the strict rules, each with a planted bad line that it finds: no shell program, no `npm install`, no `npx` without `--no-install`, no `npm ci` without `--ignore-scripts`, no `dotnet test` without `--no-build`; and every step with the host network names a heading of a RESULTS file of this repository that measured why"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t2-manifests-at-a-commit/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-manifests-at-a-commit
title: "The manifests of a commit: one listing of the tree from the main repository and the manifest files read with limits; a link and a submodule are never read"
kind: code
complexity: 5
dependsOn:
  - t1-toolchains-data
parallel: false
security: true
files:
  - src_mcp/runners/Collecting/GitHistory.cs
  - src_mcp/runners/Toolchain/ManifestsAtCommit.cs
  - src_mcp/core/Toolchain/ManifestSet.cs
  - src_mcp/tests/AManifestsAtACommitTests.cs
dod:
  - id: d1
    item: "for a commit, read from the MAIN repository path and never through the worktree's .git pointer, the answer is the manifest files of toolchains.json with their texts: found at most 4 folders deep, outside node_modules, bin, obj and folders whose name starts with a dot; the same commit gives the same answer; nothing of the working tree is read"
  - id: d2
    item: "a manifest that is a link is a refusal that names it and is never read; a submodule is never entered and is named in the notes; a manifest over 256 KiB, more than 500 manifest files, more than 16 MiB read in all and a listing over 32 MiB are each a refusal with the number and the limit, never a cut list"
  - id: d3
    item: "a commit that is not in the repository and a git that does not answer are refusals, never an empty set; a path with a control character, or one that fails the path rule of the toolchain file (absolute, a drive, `..`), is a refusal that names it"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t3-dotnet-detector/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-dotnet-detector
title: "The .NET detector: units from solution files and unreferenced project files, the ProjectReference graph, the test projects and their runner; what is ambiguous is refused"
kind: code
complexity: 6
dependsOn:
  - t2-manifests-at-a-commit
parallel: false
files:
  - src_mcp/core/Toolchain/DotnetDetector.cs
  - src_mcp/core/Toolchain/ProjectGraph.cs
  - src_mcp/tests/ADotnetDetectorTests.cs
dod:
  - id: d1
    item: "pure, from a manifest set: a folder with one solution file is one unit and owns every project file below it; a project file outside every unit that no other project file references is a unit of its own; two solution files in one folder and a solution inside another unit's folder are refusals that name both"
  - id: d2
    item: "project files are read as XML with DTD processing prohibited and no resolver (a test plants an external entity and sees that no file is read and no address is asked for); a ProjectReference that leaves the repository is a note and is never read"
  - id: d3
    item: "the test projects and their runner are read by the facts s1 t1 measured (the package names, the two MTP properties, the runner of global.json); the steps are the default argv of toolchains.json with {unit} replaced; a unit whose test projects have two runners, or that needs a runner setting the repository does not have, is a refusal that names the cure: the test command in .coai/toolchain.yaml"
  - id: d4
    item: "a unit with no test project has no test step and its answer says `tests: none`; more than 20 units is a refusal"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t4-node-detector/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-node-detector
title: "The Node detector: projects from package.json, npm from the lock file, the steps from the scripts, the runner and the prettier evidence; other package managers are refused by name"
kind: code
complexity: 5
dependsOn:
  - t2-manifests-at-a-commit
parallel: false
files:
  - src_mcp/core/Toolchain/NodeDetector.cs
  - src_mcp/tests/ANodeDetectorTests.cs
dod:
  - id: d1
    item: "pure: each package.json is a project; a root with `workspaces` is ONE project and its members are not projects; the JSON is read with a depth limit of 32, and a text that is not JSON is a refusal that names the file"
  - id: d2
    item: "package-lock.json or npm-shrinkwrap.json beside it means npm; pnpm-lock.yaml, yarn.lock, bun.lock and bun.lockb are a refusal that names the package manager and the cure; two lock files and no lock file are refusals"
  - id: d3
    item: "the steps come from facts only: restore always; build when `scripts.build` is there; typecheck when `scripts.typecheck` is there, else when tsconfig.json and the typescript package are both there; test when `scripts.test` is there and is not npm's placeholder; the runner fact (jest, vitest, node-test or unknown) needs BOTH the package and the word in `scripts.test`; no argv item is copied from the manifest"
  - id: d4
    item: "prettier is the formatter only when it is a devDependency AND has a configuration (a .prettierrc file, a prettier.config file or the `prettier` member); a project with no build, typecheck and test step gives no step and is listed as unchecked"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t5-resolve-both-trees/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-resolve-both-trees
title: "ToolchainResolve: the file at the base wins; else detection at the base and at the head, and a check the base had is never lost at the head"
kind: code
complexity: 6
dependsOn:
  - t3-dotnet-detector
  - t4-node-detector
parallel: false
security: true
files:
  - src_mcp/core/Toolchain/ToolchainResolve.cs
  - src_mcp/core/Toolchain/ResolvedToolchain.cs
  - src_mcp/tests/AToolchainIsResolvedTests.cs
dod:
  - id: d1
    item: "a file at the base commit gives the source `file`, and detection decides nothing for a project that states a command; a project of the file that states NO command gets the adapter's defaults of its own folder at the base (source `file+defaults`), and a refusal of the detector there refuses the file"
  - id: d2
    item: "no file at the base: detection at the base and at the head; a project only the head has is added with the defaults; a step (project and step name) that the base had and the head has not is toolchain_refused with the sentence `the worktree removed <step> of <project>`"
  - id: d3
    item: "a manifest of a stack that is known and not built (php, rust, python, java), at the base or at the head, is toolchain_refused and names the stack, the path and the cure; no manifest of any known stack is not_configured; one refused project refuses the repository; neither state is ever green and no step is planned"
  - id: d4
    item: "COAI_TOOLCHAIN_DETECT=off gives the answer of E2 (no file is not_configured); a value other than on and off is on, with a note; the answer carries the source, the facts of each project and what differs between the base and the head, a changed text of `scripts.build`, `scripts.test` or `scripts.typecheck` included"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s2-detection/tasks/t6-golden-fixtures/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-golden-fixtures
title: "Golden detection and argv on fixture trees: six shapes, one monorepo, and this repository read without its file"
kind: test
complexity: 5
dependsOn:
  - t5-resolve-both-trees
parallel: false
files:
  - src_mcp/tests/fixtures/toolchain/trees
  - src_mcp/tests/ADetectionIsGoldenTests.cs
dod:
  - id: d1
    item: "D1: the fixture trees are data files (the path and the text of each file) that the test makes into a temporary git repository, so this repository holds no fixture manifest that its own detection could find; the seven trees are a solution with a VSTest project, a solution with MTP projects and global.json, two project files with no solution, an npm package with jest, an npm package with vitest and TypeScript, an npm workspace, and a monorepo with a solution and two packages"
  - id: d2
    item: "D2 to D4: each tree has a golden answer (projects, facts, every step's argv, working folder and network state) at the base and after a head commit that adds a project, removes a test script, adds a Cargo.toml and renames a folder; the test fails on any difference and prints both answers"
  - id: d3
    item: "D7 and D10: an empty tree and a tree of documents only give not_configured; this repository's own HEAD commit, with detection forced, gives one dotnet unit (the solution file) with the MTP runner and the node project src_vs_code with npm, and with its file read the answer is the file's"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s3-steps-in-boundary/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s3-steps-in-boundary
title: "The steps of a resolved toolchain run inside the E2 check boundary: restore, build, typecheck and test, each with its network state; the toolchain file gets four names"
dependsOn:
  - s2-detection
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s3-steps-in-boundary/tasks/t1-toolchain-file-names/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-toolchain-file-names
title: "The toolchain file gets restore, format, testFor and testFiles; a project that states a command runs exactly what it states"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - shared/toolchain.schema.json
  - src_mcp/core/Toolchain/ToolchainTables.cs
  - src_mcp/core/Toolchain/ToolchainParse.cs
  - src_mcp/core/Toolchain/ToolchainModel.cs
  - src_mcp/tests/AToolchainFileTests.cs
dod:
  - id: d1
    item: "restore, format and testFor are argv lists and testFiles is a list of folder prefixes inside the project; an item of format may hold {file} and an item of testFor {file} or {stem}; a placeholder in another command, an unknown placeholder, a testFor without testFiles and a prefix that leaves the project are each refused with a sentence that names the member"
  - id: d2
    item: "the reader's existing refusal rule for argv text covers the four new lists (a test plants the fixture of that rule in each); version stays 1, and this repository's committed file reads to the same model as before (a test compares the two)"
  - id: d3
    item: "the test that holds the schema file and the reader's tables equal passes with the four names; the comment of the schema says the rule of SPEC A2: defaults only for a project that states no command, and `skip` of restore or format takes that step out of a defaulted project"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s3-steps-in-boundary/tasks/t2-step-plan-in-boundary/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-step-plan-in-boundary
title: "CheckRunner runs the step plan of a resolved toolchain: restore, build, typecheck and test, every one through CheckBoundary for a coder job, with the network state of the table"
kind: security
complexity: 6
dependsOn:
  - t1-toolchain-file-names
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/CheckRunner.cs
  - src_mcp/runners/Toolchain/CheckBoundary.cs
  - src_mcp/runners/Toolchain/BoundaryUnit.cs
  - src_mcp/core/Toolchain/CheckResult.cs
  - src_mcp/core/Toolchain/StepPlan.cs
  - src_mcp/tests/AStepPlanRunsInsideTheBoundaryTests.cs
dod:
  - id: d1
    item: "the step names are restore, build, typecheck and test; the order is the projects' order and, in a project, that order; the first step that does not pass ends the run; a plan with no build, typecheck and test step is not_configured; {jobs} and {unit} are replaced and every argv is started with no shell"
  - id: d2
    item: "T6: restore runs on the host network; build on the host network for a project of the file and, for a detected project, with the state toolchains.json holds; typecheck and test with only a loopback device; every record holds the state and its reason; a coder job has no road around CheckBoundary: the scan test of E2 finds the new step code and nothing else that starts a process for a coder job"
  - id: d3
    item: "T2: the unit's property list and the names of its environment are equal, item for item, to E2's for the same network state (a test compares the lists for a node step and for a dotnet step); the only additions are the adapter switches, a closed table of names with constant values, and no value comes from coai's own environment"
  - id: d4
    item: "T3: a detected or defaulted step runs with its project folder as the working folder; the folder goes through the one link resolver and must be the worktree or a real folder inside it, else the step is refused before a unit is described; a step of the file runs from the repository root as before"
  - id: d5
    item: "T4: a step whose program is not in the unit (no node, no dotnet) is a failed step whose log names the program, never a skipped one; the existing tests of the check runner and of the check boundary pass unchanged"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s3-steps-in-boundary/tasks/t3-checks-at-base-resolve/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-checks-at-base-resolve
title: "ChecksAtBase resolves through ToolchainResolve: the file at the base, or the manifests of the base and of the head, all read from the main repository; a coder still cannot change its own checks"
kind: security
complexity: 5
dependsOn:
  - t2-step-plan-in-boundary
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/ChecksAtBase.cs
  - src_mcp/src/Server/RoleJobs/CoderChecks.cs
  - src_mcp/tests/AChecksAtTheBaseShaTests.cs
  - src_mcp/tests/ADetectedChecksOfACoderJobTests.cs
dod:
  - id: d1
    item: "T1: the checks of a coder job on a fixture repository with no file run the detected steps in real units and end green, for a node project and for a dotnet unit; with a file the steps are the file's, and every existing test of ChecksAtBase passes unchanged"
  - id: d2
    item: "D3: a head commit that deletes the test script, the lock file or a test project is toolchain_refused and no step runs; a head commit that adds .coai/toolchain.yaml is reported as today (ToolchainChange.Added) and the detected steps run, not the new file"
  - id: d3
    item: "the head is read as the COMMIT coai made, from the main repository's objects: an untracked or ignored manifest in the worktree changes nothing; T5: the rule that the worktree and HEAD are the same after the checks holds for the restore step (a restore that leaves a file git would add is tree_changed, never green)"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s3-steps-in-boundary/tasks/t4-envelope-toolchain/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-envelope-toolchain
title: "The envelope says where the checks came from: the toolchain's source, projects, facts and notes; every step carries its name, network state and working folder"
kind: code
complexity: 4
dependsOn:
  - t3-checks-at-base-resolve
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderChecksReport.cs
  - src_mcp/src/Server/RoleJobs/CoderEnvelope.cs
  - src_mcp/tests/ACoderEnvelopeNamesItsToolchainTests.cs
dod:
  - id: d1
    item: "result.json has `toolchain` {source: file | file+defaults | detected, projects: [{path, adapter, facts, steps}], notes, changedBetweenBaseAndHead}; each entry of `checks` has the step name (restore, build, typecheck, test), network and workingFolder beside the fields of E2"
  - id: d2
    item: "every field of E2 keeps its name and its meaning (a test reads a result of this task with the field list of E2 and finds the same values); role_status returns the same JSON that result.json holds"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s4-digest-readers/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s4-digest-readers
title: "Failure digests from recorded real output: dotnet test, restore errors, tsc, npm, jest, vitest and node:test; a fingerprint on every item"
dependsOn:
  - s1-measure-first
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s4-digest-readers/tasks/t1-dotnet-test-readers/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-dotnet-test-readers
title: "Readers for the console of dotnet test (VSTest and the MTP form) and for dotnet restore errors, on the recordings of s1"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/core/Toolchain/DotnetTestFailures.cs
  - src_mcp/core/Toolchain/MsBuildLines.cs
  - src_mcp/tests/ADotnetTestDigestTests.cs
dod:
  - id: d1
    item: "R1: the recorded VSTest console of s1 with two failed tests and one failed theory case gives one item per failure {step, file, line, code: the full name of the test, message}; the recorded MTP console of `dotnet test` gives the same, through the reader E2 has or a new one, as the recording decides"
  - id: d2
    item: "the recorded failed `dotnet restore` gives one item per error with its code; the reader is pure; a line longer than 4096 characters is skipped and every pattern has a match timeout"
  - id: d3
    item: "R3: 1 MiB of random lines never throws and gives no item"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s4-digest-readers/tasks/t2-node-build-readers/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-node-build-readers
title: "Readers for tsc error lines and for the error block of a failed npm ci, on the recordings of s1"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_mcp/core/Toolchain/TscLines.cs
  - src_mcp/core/Toolchain/NpmErrors.cs
  - src_mcp/tests/ANodeBuildDigestTests.cs
dod:
  - id: d1
    item: "R1: the recorded tsc output with two errors gives two items {file, line, code, message}; the recorded failed `npm ci` gives one item with npm's error code and the first lines of its message"
  - id: d2
    item: "the readers are pure and bounded as the readers of t1; the existing Redaction pass runs before any cut (the planted fixture of that pass is changed by it)"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s4-digest-readers/tasks/t3-node-test-readers/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-node-test-readers
title: "Readers for the consoles of jest, vitest and node:test, on the recordings of s1"
kind: code
complexity: 6
dependsOn: []
parallel: false
files:
  - src_mcp/core/Toolchain/JestFailures.cs
  - src_mcp/core/Toolchain/VitestFailures.cs
  - src_mcp/core/Toolchain/NodeTestFailures.cs
  - src_mcp/tests/ANodeTestDigestTests.cs
dod:
  - id: d1
    item: "R1: the recorded jest console (one passed, two failed) gives two items with the name of the test, the test file, the line of the first frame inside the project and the assertion message; the same for the recorded vitest console and for the recorded node:test output"
  - id: d2
    item: "R2: each reader needs a marker line of its own tool before it reads anything, so the output of one tool gives no item in another tool's reader (a test runs every fixture through every reader)"
  - id: d3
    item: "the readers are pure and bounded as the readers of t1, and R3 holds for each"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s4-digest-readers/tasks/t4-reader-choice-fingerprints/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-reader-choice-fingerprints
title: "The choice of a reader by adapter, step and runner fact with the raw tail as the last one; a fingerprint on every item; the scan that keeps the recordings clean"
kind: code
complexity: 5
dependsOn:
  - t1-dotnet-test-readers
  - t2-node-build-readers
  - t3-node-test-readers
parallel: false
files:
  - src_mcp/core/Toolchain/FailureDigest.cs
  - src_mcp/core/Toolchain/DigestFingerprint.cs
  - src_mcp/tests/AFailureDigestTests.cs
  - src_mcp/tests/ARecordedOutputIsCleanTests.cs
dod:
  - id: d1
    item: "the reader is chosen by adapter, step name and runner fact; for a step of the file (no runner fact) the adapter's readers are tried in one fixed order and the first that gives an item wins; no item from any reader gives the raw tail of E2, so a digest is never empty; the 40 items and 16 KB of E2 hold for every reader, and the existing digest tests pass unchanged"
  - id: d2
    item: "R4: every item has a fingerprint: SHA-256 over the step, the file, the code and the message with digit runs and white space made equal; the line number is not in it; two recordings of one failure, before and after a line was added above it, give the same fingerprint"
  - id: d3
    item: "R5: a scan test over fixtures/toolchain fails when a recording holds a home folder path, the user name of the box that recorded it, or a line the Redaction pass would change; a companion proves that the scan finds a planted line of each kind"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s5-format-selection-rerun
title: "Inside one round of checks: the formatter on the files of the turn, selected tests first with the full run as the verdict, and one re-run of a test that failed once"
dependsOn:
  - s3-steps-in-boundary
  - s4-digest-readers
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t1-format-step/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-format-step
title: "The format step: the formatter runs on the files of the turn only, inside the boundary with only a loopback device, and what it changes outside those files is undone"
kind: security
complexity: 6
dependsOn: []
parallel: false
security: true
files:
  - src_mcp/runners/Toolchain/FormatStep.cs
  - src_mcp/core/Toolchain/FileArguments.cs
  - src_mcp/src/Server/RoleJobs/CoderFormat.cs
  - src_mcp/tests/AFormatStepTests.cs
dod:
  - id: d1
    item: "the files given are the ones the turn's commit added or changed, inside the project, with an extension of the adapter's list, that are not a manifest or a lock file; each is written `./<path from the working folder>`; a name with a control character or a line break is left out; more than 200 files means no run and the state `too-many-files`"
  - id: d2
    item: "F2: {file} is replaced in place by one argv item per file; the argv is started with no shell, through CheckBoundary, with only a loopback device and a limit of 5 minutes; a planted file named `--write-everything` reaches the formatter as `./--write-everything` and is read as a file"
  - id: d3
    item: "F3 and F4: after the run coai compares the worktree with the turn's commit: a changed path that was not given, a new file git would add or a HEAD that moved puts the worktree back to the turn's commit through CoderRestore, the restore step runs again and the state is `refused` with the paths; a formatter that exits non-zero, runs over its time or cannot start gives the same reset and the state `failed`"
  - id: d4
    item: "F5: no formatter in the resolved toolchain is the state `not_configured` and no process starts; a format state never makes a round red or green and is never sent to the coder"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t2-format-commit/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-format-commit
title: "The format commit: `style: format (coai)`, made by coai through the one commit road, after the turn's commit"
kind: security
complexity: 4
dependsOn:
  - t1-format-step
parallel: false
security: true
files:
  - src_mcp/src/Server/RoleJobs/CoderCommits.cs
  - src_mcp/src/Server/RoleJobs/CoderFormat.cs
  - src_mcp/tests/AFormatCommitTests.cs
dod:
  - id: d1
    item: "F1: when the format step changed given files only, coai commits them as `style: format (coai)` through CoderCommits: the .git pointer is compared and the search that E2 s7 runs before the commit of a turn on a pool key runs for this commit too; no change means no commit"
  - id: d2
    item: "the commit holds only files that were given to the formatter (a test reads `git show --name-only`); a second format run on that commit changes nothing; the function returns the new head, or the turn's head when there was no commit, and nothing else moves HEAD"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t3-affected-projects/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-affected-projects
title: "Selection, level one: only the projects the diff of base to head touches run; a project of the file always runs; nothing touched means everything runs"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/core/Toolchain/AffectedProjects.cs
  - src_mcp/runners/Toolchain/ChecksAtBase.cs
  - src_mcp/tests/AAffectedProjectsTests.cs
dod:
  - id: d1
    item: "S1: pure, from `git diff --name-status <base> <head>` with both sides of a rename and from the resolved toolchain: a detected node project is affected when a changed path is in its folder, a detected dotnet unit when one is in its folder; a project of the file is ALWAYS affected"
  - id: d2
    item: "inside an affected dotnet unit the test step names only the test projects that reach a changed project through the reverse ProjectReference graph of the head, with a project the head removed counted as changed; a changed solution file, global.json, nuget.config, or any .props or .targets file makes every test project affected"
  - id: d3
    item: "S2: no affected project (a diff of documents only) runs every project in full: a selection never selects nothing; the round records which projects were left out and why"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t4-selected-then-full/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-selected-then-full
title: "Selection, level two: testFor on the changed test files first; when it is green the full test command runs on the same commit and is the verdict"
kind: code
complexity: 6
dependsOn:
  - t3-affected-projects
parallel: false
files:
  - src_mcp/runners/Toolchain/CheckRunner.cs
  - src_mcp/core/Toolchain/TestSelection.cs
  - src_mcp/src/Server/RoleJobs/CoderChecks.cs
  - src_mcp/tests/ASelectedThenFullTests.cs
dod:
  - id: d1
    item: "a selected run exists only where the FILE names testFor: the selected files are what the job added or changed (base to head) under a testFiles prefix; {file} and {stem} are replaced in place, one item per file; S6: a stem that is not letters, digits and underscores, an unsafe file name or more than 50 files means no selected run"
  - id: d2
    item: "S3 and S4: a red selected run ends the round red with its digest and the full command does not run; a green selected run is followed by the full test command on the SAME commit; green is only ever the word of a full run (selected green and full red is red, with the digest of the full run)"
  - id: d3
    item: "S5: an exit code or a marker line that toolchains.json holds as `no test ran` for the adapter is `nothing selected`: not green, not red, and the full command runs"
  - id: d4
    item: "S7: after a full run has failed once in a job the later rounds run the full command only; that fact is a file in the job folder (written with a temporary file and a rename), so it holds across models; every step record says the scope `selected` or `full` and the number of files"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t5-flaky-rerun/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t5-flaky-rerun
title: "One re-run of a test that failed once in a full run: passing makes it `flaky`, listed and never sent to the coder; the kinds of failure that are never excused"
kind: code
complexity: 6
dependsOn:
  - t4-selected-then-full
parallel: false
files:
  - src_mcp/runners/Toolchain/FlakyRerun.cs
  - src_mcp/core/Toolchain/FlakyRule.cs
  - src_mcp/src/Server/RoleJobs/CoderChecks.cs
  - src_mcp/core/Notices/ServerNoticeCodes.cs
  - src_mcp/tests/AFlakyRerunTests.cs
dod:
  - id: d1
    item: "only a failed FULL test step whose every failure the digest names is run again, once, in a new unit with only a loopback device: with the file's testFor on the files or classes of the failed tests when there is one and at most 5 failed, else the whole step when its first run took at most 300 seconds (K7), else not at all"
  - id: d2
    item: "K3 to K6: never run again: a restore, build or typecheck step; a step that ran over its time, could not start or left a unit; a failure with no name (the raw tail); a test whose own file, or the file its failure names, is one the job added or changed; a test that was already excused once in this job; a third excuse in one job"
  - id: d3
    item: "K1 and K2: a test that passes in the re-run is listed under `flaky` {project, test, file, firstLog, rerun: narrowed | whole}, leaves the digest, and ONE coder-test-flaky notice is raised for the job; when nothing is left the step counts as passed and the round goes on; tests that fail again stay red with the digest of the re-run"
  - id: d4
    item: "K8: COAI_FLAKY_RERUNS is 0 or 1 (1 by default; another value is 1 with a note) and with 0 no re-run starts; the count of excuses is a file in the job folder, so it holds across models"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s5-format-selection-rerun/tasks/t6-this-repository-selects/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t6-this-repository-selects
title: ".coai/toolchain.yaml of this repository gets testFiles and testFor; a recorded run proves a selected real test and the full suite under the boundary"
kind: config
complexity: 3
dependsOn:
  - t4-selected-then-full
parallel: false
files:
  - .coai/toolchain.yaml
  - v2/epoch-3/e4-adapters-coder-hardening/RESULTS_s5_this_repository.md
  - src_mcp/tests/AThisRepositoryToolchainFileTests.cs
dod:
  - id: d1
    item: "the committed file names testFiles src_mcp/tests and the testFor argv that s1 t1 measured (M3); the test that reads the committed file gets both, and its build and test argv are unchanged"
  - id: d2
    item: "S8: a recorded run on a clean clone under the boundary (skipped unless its variable names a folder): a commit that adds one failing test class gives a red selected run with that test in the digest; the fix gives a green selected run and then the full suite green; the results file holds the argv, the times and the exit codes of every step"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s6-fix-loop-hardening/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s6-fix-loop-hardening
title: "The fix loop: the order of a turn with the format commit, the no-progress stop, the caps and one wall clock, for claude and for every vendor whose coder ships"
dependsOn:
  - s5-format-selection-rerun
  - e3-roles-complete/s8-proof
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s6-fix-loop-hardening/tasks/t1-turn-order/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-turn-order
title: "The order of a turn in the loop: the turn's commit, restore, format, the format commit, then the checks of the head coai made"
kind: code
complexity: 5
dependsOn: []
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Server/RoleJobs/CoderChecks.cs
  - src_mcp/src/Server/RoleJobs/CoderChecksReport.cs
  - src_mcp/tests/ATurnHasItsFormatCommitTests.cs
dod:
  - id: d1
    item: "after turn 0 and after every fix turn the branch has the turn's commit and, when the formatter changed something, one `style: format (coai)` commit after it; the restore step runs once in a round, before the format step; the build, the typecheck and the test run on the last commit, and the envelope's headSha and filesChanged are of that head"
  - id: d2
    item: "F6: the generation is compared before the format commit as before any commit, and a stale runner writes neither; a model switch puts the worktree back to the last commit coai made, the format commit included"
  - id: d3
    item: "the envelope's `format` is, per round, {state: formatted | unchanged | not_configured | failed | refused | too-many-files, files, commit, note}; the heartbeat phase is `format` while the formatter runs"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s6-fix-loop-hardening/tasks/t2-no-progress-stop/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-no-progress-stop
title: "The no-progress stop: the same failure fingerprints after two fix turns in a row, or fix turns that change nothing, end the job with `no-progress`"
kind: code
complexity: 6
dependsOn:
  - t1-turn-order
parallel: false
files:
  - src_mcp/core/Coder/FixProgress.cs
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Server/RoleJobs/RoleStops.cs
  - src_mcp/src/Server/RoleJobs/CoderPrompt.cs
  - src_mcp/tests/ANoProgressStopsTheLoopTests.cs
dod:
  - id: d1
    item: "L1 and L2: FixProgress is pure: a red round whose fingerprint set equals the set of the red round before it is one repeat, and a different set puts the count back to 0; two repeats in a row end the job with the stop `no-progress` (R0 red F, fix 1, R1 red F, fix 2, R2 red F: stopped after 2 fix turns with a cap of 3); a new failure each turn ends at the cap with not-green"
  - id: d2
    item: "L3: a fix turn after which coai has nothing to commit is one repeat and the checks are NOT run again on the same commit; the next fix prompt says that the last turn changed no file"
  - id: d3
    item: "L4 and L5: a round of scope selected and a round of scope full are never equal; a model switch puts the count back to 0 and the cap of fix turns stays the job's; a digest that was cut at 40 items is compared by the items it kept; `no-progress`, like not-green, never fails over; the envelope has `noProgress` {repeats, turn} and lastDigest"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s6-fix-loop-hardening/tasks/t3-one-clock-and-caps/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-one-clock-and-caps
title: "One wall clock: the job's timeoutMinutes, 45 by default for a coder; a fix turn that could not be checked in the time left is not begun"
kind: code
complexity: 4
dependsOn:
  - t2-no-progress-stop
parallel: false
files:
  - src_mcp/src/Server/RoleJobs/RoleJobModels.cs
  - src_mcp/src/Server/RoleJobs/RoleJobService.cs
  - src_mcp/src/Server/RoleJobs/CoderFixLoop.cs
  - src_mcp/src/Tools.cs
  - src_mcp/tests/ACoderJobHasOneClockTests.cs
dod:
  - id: d1
    item: "start_role with the role coder and no timeoutMinutes gets 45; every other role keeps 30; a given value is used as given; the tool text and the refusal sentence name the two defaults; there is no second timer and no constant of 45 minutes anywhere else (a scan with a companion)"
  - id: d2
    item: "before a fix turn the loop compares the time left with the duration of the last round of checks: with less left the job ends `timeout` at once, no model is started, and the sentence says both numbers"
  - id: d3
    item: "the envelope has `caps` {fixRounds, timeoutMinutes, noProgressRepeats, flakyReruns} with the value each one had for this job and, for a setting that was not usable, its note"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s6-fix-loop-hardening/tasks/t4-loop-scenarios-every-vendor/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-loop-scenarios-every-vendor
title: "Loop scenarios with FakeCli, for claude and for every vendor the table ships: the cap, no-progress, a turn that changes nothing, a flaky test, the format commit"
kind: test
complexity: 6
dependsOn:
  - t3-one-clock-and-caps
parallel: false
files:
  - src_mcp/tests_fakecli/HardeningVerbs.cs
  - src_mcp/tests_fakecli/Program.cs
  - src_mcp/tests/TheHardenedFixLoopTests.cs
dod:
  - id: d1
    item: "on a fixture repository with detected checks and real units: always red with a new failure each turn gives exactly COAI_FIX_ROUNDS fix turns and not-green; the same failure gives `no-progress` after 2; a turn that writes nothing gives one repeat and no new round of checks; a test outside the job's files that fails once gives green with `flaky` and one notice; a file the fake wrote badly formatted gives one format commit"
  - id: d2
    item: "each scenario runs with the fake as claude and, through the vendor seam of E3 with an injected table, as every vendor the table ships: the recorded argv of a fix turn is that vendor's resume form with the session of turn 0, and a vendor the table refuses has zero launches"
  - id: d3
    item: "no scenario starts a real model; each runs with a temporary HOME and data folder, and the stand-in of the real home has the same bytes after the run"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s7-proof/story.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: s7-proof
title: "Proof: the one-shot that shows a repository's checks, the epic acceptance on the real binary with FakeCli, one paid run on a repository with no toolchain file, the documents"
dependsOn:
  - s6-fix-loop-hardening
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s7-proof/tasks/t1-validate-toolchain-mode/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t1-validate-toolchain-mode
title: "coai-mcp --validate-toolchain <repo>: the resolved toolchain of a commit as JSON, with every refusal"
kind: code
complexity: 4
dependsOn: []
parallel: false
files:
  - src_mcp/src/Program.cs
  - src_mcp/src/ValidateToolchainMode.cs
  - .agents/PROJECT.md
  - src_mcp/tests/TheValidateToolchainModeTests.cs
dod:
  - id: d1
    item: "`--validate-toolchain <repo> [--base <sha>] [--head <sha>]` prints one JSON object: the source, the projects with their facts, every step's argv, working folder and network state, and the notes; a refusal prints the state and its sentences; the mode starts no step, reads no file of the working tree and writes nothing"
  - id: d2
    item: "the exit code is 0 for a resolved toolchain, 1 for not_configured and toolchain_refused and 65 for a wrong argument, never 64; the mode is in the one-shot list of .agents/PROJECT.md in the same commit; stdout holds the JSON and nothing else"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s7-proof/tasks/t2-epic-acceptance/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t2-epic-acceptance
title: "Epic acceptance on the real binary over stdio with FakeCli: a repository with no toolchain file to green, the boundary, the inherited plan, a client restart"
kind: test
complexity: 6
dependsOn:
  - t1-validate-toolchain-mode
parallel: false
files:
  - src_mcp/tests/TheAdaptersEpicAcceptanceTests.cs
  - src_mcp/tests_fakecli/HardeningVerbs.cs
dod:
  - id: d1
    item: "one test on a real coai-mcp with the detached runner: start_role(coder) on a node fixture repository with NO .coai/toolchain.yaml: red in the selected run, one fix turn in the session of turn 0, a format commit, the selected run green, the full run green; the envelope says the source `detected`"
  - id: d2
    item: "the same binary on a dotnet fixture repository with a file runs the file's steps and detection decides nothing; a repository with a Cargo.toml and no file ends failed with the stop toolchain-refused and zero steps; a repository with no manifest ends with not-configured"
  - id: d3
    item: "the MCP client is killed while the full run is going: a new client reads the same job to its end, green, with the generation unchanged and no lost notice; review_code on coder/<jobId> is accepted through the plan the branch inherits (E2 s5); nothing outside the worktree changed, and every step record names the unit boundary"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s7-proof/tasks/t3-paid-detected-run/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t3-paid-detected-run
title: "One real coder job (claude haiku on a pool key) on a scratch node repository with no toolchain file: a planted red test to green"
kind: measure
complexity: 4
dependsOn:
  - t2-epic-acceptance
parallel: false
security: true
files:
  - v2/epoch-3/e4-adapters-coder-hardening/RESULTS_e4_proof.md
dod:
  - id: d1
    item: "the job runs on a scratch clone with a scratch data folder, on a claude key of the pool named through COAI_CLAUDE_KEYS_FILE and never on the person's sign-in file; with no usable key the task does not start and says so"
  - id: d2
    item: "the results file holds the job id, the route, the detected toolchain, the rounds with their scope, the format state, fixTurns (at most 2), green true, the wall time and the cost the CLI reported; it quotes no prompt, no answer and no key"
  - id: d3
    item: "at most 2 paid runs and 0.30 USD in total, read from the ledger after each run; the second run only after a recorded change; a run that would pass the cap is not started"
```

## v2/epoch-3/e4-adapters-coder-hardening/stories/s7-proof/tasks/t4-docs/task.yaml
```yaml
# coai v2 work contract (draft schema; validated by validate_work from epoch 1 E3)
id: t4-docs
title: "Docs: detection and defaults, the steps, the formatter turn, selection, the re-run, the no-progress stop and the caps in ARCHITECTURE.md, README.md and the knowledge base; server.md records what E4 decided differently"
kind: docs
complexity: 3
dependsOn:
  - t3-paid-detected-run
parallel: false
files:
  - ARCHITECTURE.md
  - README.md
  - v2/architecture/server.md
  - research/module_runners.md
  - research/module_server.md
  - research/module_tests.md
dod:
  - id: d1
    item: "ARCHITECTURE.md and README.md name the order of resolution, the refusals of detection, the step names with their network state, the four new names of the toolchain file, the new fields of the envelope, the settings COAI_TOOLCHAIN_DETECT and COAI_FLAKY_RERUNS and the stop `no-progress`; research/module_tests.md lists the new scenarios as covered or not covered"
  - id: d2
    item: "server.md gets a section `What epoch 3 E4 decided differently from sections 10, 12 and 23`: one clock, console readers, the two levels of selection, the rule of the re-run, npm only, and that a review_code started by coai is E5's (section 23 said E4)"
```
