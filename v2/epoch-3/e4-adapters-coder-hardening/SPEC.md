# Spec — epoch 3, E4 "Adapters dotnet + node + coder hardening"

> Status: **plan only, nothing built, 2026-11-25.** Plan gate: `proceed`, 2026-11-25; its 5 accepted findings are section P. The
> owner answered questions 1, 2, 4 and 5 of section N on 2026-11-25; question 3 is open. After the gate one `doneWhen` line came
> from the security judgement of E3 (section Q). Scope: the server half (`src_mcp`, `shared/`,
> `.coai/toolchain.yaml`). Written by the planner (Fable) from `v2/` and the code at `feat/v2-ep3-e2-coder` `f19a7fcb`, read-only.
> Parent plan: [PLAN.md](../../PLAN.md); architecture: [server.md](../../architecture/server.md) §10, §11, §12, §15, §16 row 9, §23.
> Stories and tasks: the YAML tree under `stories/` (7 stories, 34 tasks, complexity sum 170, no task above complexity 6). No
> cornerstone story: the tasks that can break a boundary carry `security: true` and go to the important model list.

## 0. Goal, and what is there today

Goal (`epic.yaml` `doneWhen`): a repository gets its checks from its own manifests when it has no `.coai/toolchain.yaml`; failure
digests come from recorded real output; the fix loop has its caps, a no-progress stop and a flaky re-run, for every vendor whose
coder ships; every check still runs inside the check boundary of E2.

What the code has today (checked at `f19a7fcb`):

- The only source of checks is `.coai/toolchain.yaml`, read at the BASE commit from the main repository
  (`src_mcp/runners/Toolchain/ChecksAtBase.cs:24`, `:41`). No file is `not_configured`; a wrong file is `toolchain_refused`.
  Neither is green.
- A project has two steps, build and test (`src_mcp/core/Toolchain/CheckResult.cs:36`), each one argv
  (`src_mcp/runners/Toolchain/CheckRunner.cs:132`). `{jobs}` is the only placeholder (`CheckRunner.cs:225`). The `adapter` name
  only adds four .NET switches (`CheckRunner.cs:39`). The schema already lists six adapters and six steps a file may skip
  (`src_mcp/core/Toolchain/ToolchainTables.cs:46`, `:51`).
- Every step of a coder job runs in a named `systemd-run --user` unit (`src_mcp/runners/Toolchain/CheckBoundary.cs`). The build
  has the host network and the test only a loopback device (`src_mcp/runners/Toolchain/BoundaryJob.cs:31`, `:37`). The unit's PATH
  has the system folders and the `~/.nvm` folders of coai's own PATH (`src_mcp/runners/Toolchain/BoundaryHost.cs:106`).
- The digest reads MSBuild error lines and the output of this repository's test executable; everything else is the raw tail
  (`src_mcp/core/Toolchain/FailureDigest.cs:76`).
- The format slot is a constant `not_configured` (`src_mcp/src/Server/RoleJobs/CoderChecks.cs:66`,
  `CoderChecksReport.cs:26`). Every round runs the whole build and the whole test. A red round always costs a fix turn until the
  cap (`CoderFixLoop.cs:107`). The only clock is the job's `timeoutMinutes`, 30 by default
  (`src_mcp/src/Server/RoleJobs/RoleJobModels.cs:216`, `CoderFixLoop.cs:238`).
- Measured by E2 (`RESULTS_s2_check_boundary.md`, run E): one round of this repository is 34 s of build and 8 m 14 s of tests.
  Turn 0 and three fix turns do not fit in 30 minutes.

## 1. Decisions that change server.md

1. **One wall clock.** §10 names 45 minutes; E2 built the job's `timeoutMinutes`. There is one clock: `timeoutMinutes`, and each
   role has its own default in one table (section H). The VALUE of the coder's default (45, or 30 as today) is the owner's:
   question 1 of section N. Answered 2026-11-25: 60 minutes for a coder, 30 for every other role.
2. **Detection never guesses and never checks a part.** A repository with a manifest coai cannot turn into commands is
   `toolchain_refused` with the cure, also when another project of it could be checked (section A).
3. **Digest readers read the console output of the step.** §10 names TRX and JUnit files. A report file needs a folder outside the
   tree and a reader per format; the exit code is the verdict in every case, and a reader that finds nothing gives the raw tail
   (section C).
4. **Selection has two levels, and the full test command is the only one that can say green** (section E). §12 gave Node the
   related-tests mode of jest and vitest as a default. E4 has no default for the finer level: a selected run the repository did not
   name can fail where its own test command passes, and would cost a fix turn. It comes only from the file's `testFor`.
5. **The re-run of a failed test is narrow, bounded and never excuses the job's own tests** (section F). §10 said only "re-run
   once". It needs a complete list of the failures, and proof that each excused test ran again and passed.
6. **`no-progress` has a definition**: the same failure fingerprints after two fix turns in a row; a fix turn that changes no file
   counts as one (section G).
7. **A `review_code` started by coai is not built here.** §23 left it to E4. There is no scheduler before E5, and E5's `doneWhen`
   holds the gate on the candidate commit and the resolver. It is E5's (section I).
8. **Five step names run in E4**: restore, format, build, typecheck, test. `lint`, `formatCheck`, `audit` and `coverage` stay legal
   names of `skip` and nothing runs them (§11 gives them to epoch 5 E4).
9. **npm only.** pnpm, yarn and bun are detected and refused by name until each is measured. Whether that is enough is question 2
   of section N. Answered 2026-11-25: npm only.
10. **The toolchain file gets four names** (`restore`, `format`, `testFor`, `testFiles`), and a file project that states a
    command runs exactly what it states (section A2). `version` stays 1: no release has shipped the coder.
11. **[Superseded by the owner's decision of 2026-11-26: a changed definition is reported, never refused; see A4.]** ~~**A detected check cannot be changed by the job it checks.**~~ For a project the base has, the check definition (the `scripts`
    of `package.json`; for .NET the test projects of the solution, the runner, the MSBuild targets and imports) must be the same
    at the head, or the job is refused (section A4). §16 row 9 said only "detect on both trees".

## A. Where the checks of a repository come from

One pure function, `ToolchainResolve` (`src_mcp/core/Toolchain/`), is the only place that decides it. `ChecksAtBase` calls it once
for each round.

### A1. The order

1. **A file at the base commit wins.** `.coai/toolchain.yaml` at the base is read as today. Detection decides nothing for a project
   that states a command.
2. **No file at the base: detection at the base commit and at the head commit.** Both are read from the main repository's objects
   (`git ls-tree`, then the manifest blobs), never through the worktree's `.git` pointer and never from the working tree. The head
   is the commit coai made, so an untracked or ignored file changes nothing. The listing is one more method of the hardened git
   reader (`GitHistory`). E3's `GitWorkFolder` is not used for it: it is bounded to a plan folder of 2 000 entries.
3. **A file the coder adds** is reported as today (`ToolchainChange.Added`) and does not run for that job.
4. `COAI_TOOLCHAIN_DETECT=off` gives E2's answer: no file is `not_configured`. Default `on`.

### A2. The file

- A project that states `build`, `test`, `restore`, `format` or `testFor` is run exactly as written: only its stated steps run.
  This repository's file reads to the same model as today.
- A project that states NO command (`path` and `adapter` only) gets the adapter's defaults for its own folder, read at the base;
  the both-trees rule of A4 holds for it. The source is then `file+defaults`.
- New names of a project: `restore` (argv), `format` (argv; an item may hold `{file}`), `testFor` (argv; an item may hold `{file}`
  or `{stem}`), `testFiles` (a list of folder prefixes inside the project that hold test files). `testFor` needs `testFiles`.
- `skip: [restore]` and `skip: [format]` take that step out of a defaulted project. The reader's existing refusal rule for argv
  text covers the new lists.

### A3. Detection (`shared/toolchains.json` is the data; C# only reads manifests)

The search: manifest names of the six stacks, at most 4 folders deep, not under `node_modules`, `bin`, `obj` or a folder whose name
starts with a dot. A link and a submodule entry are never read. Limits: 20 projects, 500 manifest files, 256 KiB for one manifest,
16 MiB read in all, 32 MiB of listing. Over a limit is a refusal; nothing is cut.

**.NET.** A folder with one solution file (`.slnx` or `.sln`) is one unit; the unit owns every project file below it. A project file
outside every unit that no other project file references is a unit of its own. Project files are read as XML with DTD processing
prohibited and no resolver. Facts: the ProjectReference graph, which projects are test projects, their runner (VSTest or Microsoft
Testing Platform), the `test.runner` of `global.json`. The `.props`, `.targets` and `global.json` files of a unit are read with the
same limits: they are part of its check definition (A4).

**Node.** Each `package.json` is a project; a root with `workspaces` is ONE project and its members are not projects. Facts: the
lock file, the scripts, the runner (jest, vitest, node-test or unknown: it needs the package AND the word in `scripts.test`), a
`tsconfig.json`, a prettier configuration. A workspace member that has a `test` (or `typecheck`) script when the root has none is planned as a step of its own, `npm test --workspace=./<member folder>` (measured: it runs the member's script and returns its exit code); a root that has a test script is taken to run its members'. A test marker under a `Condition` (a `Microsoft.NET.Test.Sdk` reference, `IsTestProject` set to `true`) still makes a project a test project: the reader evaluates nothing, so the test step is planned conservatively.

**The defaults** (the exact argv is frozen by story s1; a line here is a proposal until s1 saw it work inside the unit):

| Step | dotnet, from the repository root | node, from the project folder | Network |
|---|---|---|---|
| restore | `dotnet restore {unit}` | `npm ci --ignore-scripts` | host |
| format | the form s1 t3 names, only with an `.editorconfig` at the root; else none | `npx --no-install prettier --write --ignore-unknown {file}`, only when prettier is a devDependency and has a configuration | none |
| build | `dotnet build {unit} -c Debug --no-restore -m:{jobs}` | `npm run build`, when the script is there | none when s1 measured it so; else host, as E2 |
| typecheck | — | `npm run typecheck`, else `npx --no-install tsc --noEmit` with a `tsconfig.json` and the typescript package | none |
| test | `dotnet test {unit} --no-build -c Debug`, in the form s1 measured for the runner | `npm test`, when the script is there and is not npm's placeholder | none |

Strict by construction, and a scan test holds each line: no shell; never `npm install`; never an install script (`--ignore-scripts`);
never a tool downloaded at check time (`--no-install`); never `dotnet test` that builds. A repository that needs one of these writes
its own file. There is no default `testFor` (decision 4).

**What is refused, and what a person sees** (`toolchain_refused` unless the line says otherwise; the sentence names the path and
the cure, which is always "name the commands in `.coai/toolchain.yaml`"):

| Found | Answer |
|---|---|
| no manifest of any known stack | `not_configured` |
| a manifest of php, rust, python or java, anywhere inside the limits | refused: the stack is known and has no adapter yet |
| two solution files in one folder; a solution inside another unit's folder | refused: both are named |
| test projects of two runners in one unit; a runner that needs a setting the repository does not have (as s1 measured) | refused |
| a `package.json` with no lock file, with two, or with a pnpm, yarn or bun lock file | refused: the package manager is named |
| a manifest that cannot be parsed, is over its size, or is a link | refused (a submodule is not entered and is named in the notes) |
| over a limit of the search | refused: the number and the limit |
| a check definition of a project of the base that differs at the head (A4) | **not refused** (owner decision 2026-11-26): the head's own definitions run, and the difference is reported: the project and the member are named |
| a project with no build, typecheck and test step | listed as `unchecked`, no step; when every project is so: `not_configured` |
| a unit with no test project | its build runs; the result says `tests: none` |

There is no partial answer: one refused project refuses the repository. A repository that is half checked would be shown green.

### A4. Both trees

**Owner decision 2026-11-26 — this section was rewritten; it counts as a plan change.** The first plan (decision 11, plan gate item 1) refused a head that changed the check definition, and two interim versions (the gate "frozen build definitions" of the E4 s3 security verifies, passes 2 and 3) refused a head for any change that is not a plain source file.
The owner replaced all of it: *"Let's widen it so the coder is comfortable. Let it restore packages from the internet itself; no proxy or address lists. If the work needs it, let it do it — allow everything."* **Nothing the head changes is refused. The head's own definitions run, and the difference is reported.**

Detection runs on the base and on the head, and **the head's projects are what runs**.

- A project only the head has is restored, built and tested with the defaults (decision 9 of §16), and the result marks it `definedByHead`. A package or a test project the coder adds is part of the head's definitions.
- A step (project and step name) that the base had and the head has not is **reported** as lost (`the worktree removed <step> of <project>`), never refused. A manifest of an unbuilt stack that appears at the head still refuses the job: nothing can be run for it.
- The head's `.coai/toolchain.yaml` is the file that runs; what the head did to the base's stated commands (a command removed, a command changed, the test command above all) is reported.
- **What is reported** (the envelope's `toolchain` section, its top-level `definitionsChanged` flag and one-line `definitionSummary`, and `checks/round-<n>/changed-definitions.txt` in the job's record): the counts of definition and configuration files the head changed (every changed path that is not a plain source file, by
  the lists of `shared/toolchains.json`), of symlinks and submodules, the spelled paths in the record file, the steps the base had and the head lost, the members of the check definition that differ (node: the `scripts` of the package and of its workspace members, the lock file's name, the runner and tool members, the configuration files; .NET: the test projects the solution names, the runner, `global.json`, the names of every
  `Target`, `Import` and `UsingTask`, the raw test-fact elements, the `.props` and `.targets` files) with the subset about tests as `changedTestCommands`, and the test files modified, added or deleted (`headTestFiles`).
- **The accepted risk, stated plainly:** a green check means the head's OWN definitions passed. The coder may have changed the base's tests or the commands that run them, and the envelope says when (`definitionsChanged`).

What the coder still cannot change, because it costs the coder nothing to keep: the tree the checks run in equals the commit (`git clean -ffdx`, an empty `status --ignored`, no `.git` entry below the root) before every round; a step gets a fresh home and temp, and the package caches are writable only in a restore step; coai's own git reads are hardened; no value of the head is echoed in a sentence.
Restore keeps the host network.

Scenarios: **D1** each of the seven fixture trees gives its golden projects, facts and argv. **D2** a head that adds a project: it is restored, built and tested, marked `definedByHead`. **D3** a head that removes a test script, a lock file, a test project: the head's definitions run and the lost step is reported. **D4** a head that adds `Cargo.toml` is refused by the stack's name. **D5** a file at the
base: detection decides nothing. **D6** a file project with no command: the defaults of its folder. **D7** nothing found;
documents only. **D8** a link, a submodule, an XML entity, a JSON 40 levels deep. **D9** each limit. **D10** this repository's own
HEAD with detection forced, and with its file. **D11** `COAI_TOOLCHAIN_DETECT=off`. **D12** a head that replaces `scripts.test` with
`echo passed` → **not refused**: the head's script runs, `definitionsChanged` is true and the changed test command is named. **D13** a head that adds a `VSTest` target, takes a test project out of the solution file or
changes the runner in `global.json` → **not refused**: reported the same way. **D14** a new test file → run, and not reported as a definition (a plain source file); a new package reference or a new dependency changes a project file or `package.json` → run (it is restored from the internet), and reported as a changed definition file.

## B. The steps and the check boundary

**The order of one round**, for each project that runs: restore → format (section D) → build → typecheck → test. The first step that
does not pass ends the round. A plan with no build, typecheck and test step is `not_configured`.

**One road.** Every step of a coder job — the new ones, the selected run and the re-run — starts through `CheckBoundary.RunAsync`,
after the preflight of E2. The scan test of E2 must find the new step code and nothing else that starts a process for a coder job.

**The boundary is not widened.** "Widen" means: a new bind, a new writable path, a variable copied from coai's environment, or the
host network for a step that has none in the table of A3. E4 adds none. A test compares the unit's property list and the names of
its environment for a node step and a dotnet step with E2's, item for item. Two things are added, both checked:

- **Adapter switches**: a closed table of names with constant values (`CI`, `FORCE_COLOR`, `npm_config_update_notifier`,
  `npm_config_fund`, `npm_config_audit`, `DOTNET_CLI_UI_LANGUAGE`, beside E2's four). No value comes from coai's environment.
- **The working folder of a detected or defaulted step is its project folder**, because npm and its tools read their configuration
  from the current folder. The folder goes through the one link resolver and must be the worktree or a real folder inside it;
  else the step is refused. A step of the file runs from the repository root, as today.

**The host network** is only for restore, and for the build of a file project (E2, unchanged). Each `host` entry of
`shared/toolchains.json` names the heading of a RESULTS file that measured why; a test fails for an entry without one.

**The rule of E2 stays**: the worktree and HEAD are the same after the checks as before. A restore that leaves a file git would add
(a `node_modules` that is not ignored) is `tree_changed`, never green. Only the format step may change tracked files, and only the
ones it was given (section D).

**No private registry.** No credential reaches a check step, and the home of a step is the job's own, so the person's `.npmrc` and
NuGet configuration are not seen. A repository that restores from a registry that needs a sign-in fails its restore step. E4 does
not solve this; the sentence says so.

Scenarios: **T1** a detected node project and a detected dotnet unit run in real units to green. **T2** the property and
environment lists equal E2's. **T3** the working folder is a link out of the worktree → refused. **T4** no `node` in the unit → a
failed step that names the program. **T5** a restore that leaves an untracked file → `tree_changed`. **T6** the network state and
its reason in every record.

## C. Failure digests

The exit code is the verdict. A reader only shapes what goes to the coder.

- New readers (`src_mcp/core/Toolchain/`), each pure, each tested on output RECORDED from the real tool by story s1: the console of
  `dotnet test` (VSTest, and the Microsoft Testing Platform form); `dotnet restore` errors; `tsc` lines; the error block of
  `npm ci`; the consoles of jest, vitest and node:test.
- **Choice.** By adapter, step name and runner fact. For a step of the file there is no runner fact: the adapter's readers are
  tried in one fixed order and the first that gives an item wins. Each reader needs a marker line of its own tool first, and a test
  runs every fixture through every reader. No item → the raw tail of E2. A digest is never empty.
- The limits of E2 hold for every reader: 40 items, 16 KB, the Redaction pass before any cut, a pattern timeout, a line over 4096
  characters skipped.
- **Fingerprint.** Every item gets one: SHA-256 over step, file, code and the message with digit runs and white space made equal.
  The line number is not in it. Section G reads it.
- **Run summary.** Every test reader also gives, beside the bounded digest: the tool's own counts (total, failed, passed), the
  name of EVERY failure (not cut at 40; a hard cap of 2 000 names) and the names of the passed tests when the tool prints them.
  The summary is `complete` only when the tool's summary line was found, its failed count equals the number of named failures, and
  the step's log was not cut. Section F reads the summary, never the digest.
- **Fixtures.** `src_mcp/tests/fixtures/toolchain/`: the recording, with the checkout path and the user name replaced. A scan test
  fails for a home path, the recording box's user name, or a line the Redaction pass would change.

Scenarios: **R1** one test per reader on its recording. **R2** every fixture through every reader. **R3** 1 MiB of random lines
never throws. **R4** the same failure with a line added above it has the same fingerprint. **R5** the fixture scan and its
companion. **R6** the summary of every recording. **R7** not complete: two named failures beside one the reader cannot name; no
summary line; a cut log. **R8** 45 failures: the digest holds 40 items, the summary 45 names.

## D. The formatter turn

After a turn's commit coai runs the formatter and commits the result as `style: format (coai)`. No model takes part.

1. **Which files.** The files that turn's commit added or changed, inside the project, with an extension of the adapter's list,
   that are not a manifest or a lock file. More than 200 → no run, state `too-many-files`.
2. **How a file name reaches the argv.** `{file}` is replaced in place by one item per file, each written `./<path>`. A name with a
   control character or a line break is left out. So a file named `--write-everything` reaches the tool as a file, not an option.
3. **Where.** Inside `CheckBoundary`, with only a loopback device, 5 minutes at most, after the restore step (prettier lives in
   `node_modules`). The restore step runs once in a round, before the format step; the build, the typecheck and the test then run
   on the head coai made.
4. **What it may change.** Only the files it was given. coai compares the worktree with the turn's commit afterwards: any other
   changed path, a new file git would add, or a HEAD that moved puts the worktree back to the turn's commit through `CoderRestore`,
   the restore step runs again, and the state is `refused` with the paths. A formatter that exits non-zero, runs over its time or
   cannot start gives the same reset and the state `failed`.
5. **The commit.** Through `CoderCommits`: the generation compared first, the `.git` pointer compared, and the search of a turn that
   ran on a pool key, as for any commit. It holds only files the formatter was given. No change is no commit.
6. **It is never a verdict.** A format state does not make a round red or green and is never sent to the coder. A file the
   formatter cannot parse fails the build or the test next, and that digest is the useful one.

Envelope: `format` per round: `{state: formatted | unchanged | not_configured | failed | refused | too-many-files, files, commit,
note}`.

Scenarios: **F1** a badly formatted file → one format commit with that file only; a second run changes nothing. **F2** the planted
option-like file name. **F3** a formatter that writes another file → reset, `refused`. **F4** a formatter that fails → reset,
`failed`, the round goes on. **F5** no formatter → no process. **F6** a stale runner writes no format commit.

## E. Test selection

**Level one: which projects run.** From `git diff --name-status <base> <head>`, both sides of a rename. A project is left out only
when coai can show that the diff does not reach it.

- **A node project always runs.** A workspace, a `file:` or `link:` dependency, an import by a relative path or a script can join
  a package to anything in the tree, and coai reads manifests, not source files: it cannot show that a diff does not reach a
  package. So a change in a shared package always runs the app that depends on it.
- **A .NET unit can be left out**, when all of this holds: no changed path is in its folder; none of its projects references,
  directly or through other projects, a project of a unit that has a changed path (`ProjectReference` is followed from one unit
  to another); every `ProjectReference` resolves to a project file of the resolved toolchain; and no project, `.props` or
  `.targets` file of the unit holds a path that leaves the unit's folder (`..`) other than a resolved `ProjectReference`.
- Inside an affected unit the test step names only the test projects that reach a changed project through the reverse
  ProjectReference graph. A changed solution file, `global.json`, `nuget.config`, or any `.props` or `.targets` file affects every
  test project of the unit.
- **Impact unknown means everything runs, in full**: a changed path outside every project folder (a root configuration, a shared
  folder, documents); a reference that cannot be resolved; a project file that cannot be read.
- **A project of the file is always affected**: coai does not know what its commands read.
- A selection never selects nothing. The round records each unit that was left out, and why.

Limit, stated: a test of one unit that reads a file of another unit at run time, by a path built in code, is not in any manifest.
A change of that file alone leaves the reading unit out. Nothing in E4 sees it.

**Level two: which tests run first.** Only where the FILE names a `testFor`. It is the repository's own word for "run the tests
of these files".

1. The selected files: what the job added or changed under a `testFiles` prefix. More than 50, an unsafe name, or a `{stem}` that
   is not letters, digits and underscores → no selected run.
2. A red selected run ends the round red with its digest. The full command does not run.
3. A green selected run is followed by the FULL test command on the same commit. **Green is only ever the word of a full run.**
4. "No test ran" is not green and not red: the exit code or marker line that `toolchains.json` holds for the adapter's runners
   (measured by s1) means "nothing selected", and the full command runs.
5. After a full run has failed once in a job, the later rounds run the full command only.

For this repository the file gets `testFiles` and `testFor` (story s5 t6): a red round then costs the build and one test class,
not the 8 minutes of the suite.

Scenarios: **S1** the affected projects of each fixture diff. **S2** a diff of documents only → everything. **S3** selected red.
**S4** selected green, full red → red, digest from the full run. **S5** nothing selected. **S6** 51 files; an unsafe stem. **S7**
full only after a failed full run. **S8** this repository: a recorded selected run and the full suite. **S9** a change in a shared
package (`file:../shared`, and a workspace member) runs the tests of the app that depends on it, and they go red. **S10** a change
in a project of one unit runs the test projects of another unit that reference it; a unit nothing reaches is left out. **S11**
impact unknown: a changed root file, a reference that cannot be resolved → everything runs; a unit with a `..` path always runs.

## F. The re-run of a test that failed once

Only a failed FULL test step is re-run, once.

- **A complete list first.** The re-run is decided from the run summary of section C, never from the digest, which stops at 40
  items. No re-run when the summary is not `complete`: a failure the reader could not name beside named ones, no summary line, a
  cut log, more names than the reader's cap. No re-run when more than 5 tests failed: that is not flakiness.
- **How.** With the file's `testFor` on the failed tests' files or classes when the file has one; else the whole step when its
  first run took at most 300 seconds; else not at all. In a new unit, with only a loopback device.
- **Never re-run:** a restore, build or typecheck step; a step that ran over its time, could not start or left a unit; **a test
  whose own file, or the file its failure names, is one the job added or changed**; a test that was already excused once in this
  job; a third excused test in one job.
- **Proof that it passed.** A test is excused only when the re-run SHOWS that it ran and passed: its name is exactly once among
  the passed tests the re-run names; or, for a re-run of the whole step with a tool that names no passed test, the summary is
  complete, has the same total as the first run and zero failed. A re-run in which no test ran (the "no test ran" code, a total
  of 0, a filter that matched nothing), a name that matches twice, or a total that differs excuses nothing: the failure goes to
  the coder with the digest of the first run.
- **Result.** An excused test is listed under `flaky` `{project, test, file, firstLog, rerun: narrowed | whole, proof: named |
  totals}`, leaves the digest, and one `coder-test-flaky` notice is raised for the job. When nothing is left the step counts as
  passed. Tests that fail again stay red with the digest of the re-run.
- `COAI_FLAKY_RERUNS` is 0 or 1 (default 1). The count of excused tests is a file in the job folder, so it holds across models.

Limit, stated: a narrowed re-run can excuse a test that fails only in the whole suite because of the job's change (an order
dependency). It is listed and noticed, never hidden; the envelope says `rerun: narrowed`.

Scenarios: **K1** fails once, passes with proof → `flaky`, green, one notice. **K2** fails twice → red. **K3** a test of the job's
own file → no re-run. **K4** the raw tail → no re-run. **K5** a build failure → no re-run. **K6** the third excused test. **K7** a
first run of 301 seconds with no `testFor`. **K8** `COAI_FLAKY_RERUNS=0`. **K9** two named failures beside one the reader cannot
name → no re-run. **K10** 41 failures with a digest of 40 items → no re-run; 6 failures → no re-run. **K11** a `testFor` filter
that matches nothing → nothing excused, red. **K12** a name that matches two tests; a whole re-run whose total differs → nothing
excused.

## G. The no-progress stop

`FixProgress` (`src_mcp/core/Coder/`, pure) holds the fingerprint set of the last red round.

- A red round whose set equals the set of the red round before it is one repeat. A different set puts the count back to 0.
- A fix turn after which coai has nothing to commit is one repeat, and the checks are NOT run again on the same commit. The next
  fix prompt says that the last turn changed no file.
- Two repeats in a row end the job with stop `no-progress`: R0 red F → fix 1 → R1 red F → fix 2 → R2 red F → stop, with a cap of 3.
- A selected round and a full round are never equal. A model switch puts the count back to 0; the cap of fix turns stays the job's.
- `no-progress` never fails over, like `not-green` (E1 spec A: the coder's own stops do not).

Limit, stated: a failure that goes A → B → A is not seen as a repeat. The cap ends it.

Scenarios: **L1** the same failure three times → `no-progress` after 2 fix turns. **L2** a new failure each turn → the cap,
`not-green`. **L3** a turn that writes nothing → one repeat, no new round. **L4** a model switch. **L5** a cut digest (over 40).

## H. Caps and the one clock

| Cap | Value | Setting | The job ends with |
|---|---|---|---|
| fix turns, for the whole job | 3 (0 to 10) | `COAI_FIX_ROUNDS` (E2) | `not-green` |
| wall clock of the job | `timeoutMinutes`; one default for each role: 30, and 60 for a coder (the owner's answer to question 1, 2026-11-25; the proposal was 45) | the `start_role` argument | `timeout` |
| repeats of the same failure | 2 | none | `no-progress` |
| re-runs of a failed test step | 1 for a step, only with at most 5 failed tests; 2 excused tests for a job | `COAI_FLAKY_RERUNS` 0 or 1 | — |
| one step | the file's `timeouts` (15 m build, 30 m test); restore and typecheck take the build's; format 5 m | the file | a failed step |
| files to a formatter / to a selected run | 200 / 50 | none | no run of that kind |
| detection | section A3 | none | `toolchain_refused` |
| digest, step log | 40 items and 16 KB; 20 MiB (E2) | none | — |

- There is no second timer, and the default of a role is one number in one table; a scan holds this.
- **A fix turn that could not be checked is not begun.** Before a fix turn the loop compares the time left with the duration of the
  last round of checks. With less left the job ends `timeout` at once, and no paid turn starts.
- The envelope gets `caps`: the value each cap had for this job, with the note of a setting that was not usable.
- Not a cap in E4: money. A role job has no spend limit of its own (risk 4 of the E3 spec). The paid proof stops from the ledger.

## I. A `review_code` started by coai

Not in E4. Today the main AI calls `review_code` on `coder/<jobId>`, and the branch inherits the feature's plan proceed (E2 s5).
That stays. A gate that coai starts needs the scheduler, the pending resolutions and the resolver of E5. The acceptance of E4
proves only that the inherited plan still works for a job whose checks were detected.

## J. Security rules

1. **Every check runs inside the boundary of E2, and E4 does not widen it** (section B). If story s1 finds that a default needs a
   property the unit does not have, the measure task stops. A change of the unit is a plan change and needs a security judgement by
   the important model first.
2. **What decides which commands run is not the coder's.** The file at the base, or shipped argv templates filled with facts of
   manifests read from git objects. Facts are data: no argv item is copied from a manifest. A step the base had cannot be lost,
   and the check definition of a project the base has cannot differ at the head (A4).
3. **A repository's files are untrusted input.** XML with DTD processing prohibited and no resolver; JSON with a depth limit; size
   limits before a read; a link and a submodule never read; a path checked by the rule of the toolchain file.
4. **A file name is never an option.** `./` before every expanded path; unsafe names left out; a stem is letters, digits and
   underscores; no shell anywhere.
5. **No secret in an argv or a log.** The argv is templates and paths. The reader's refusal rule covers the new lists. Every digest
   goes through the Redaction pass. The format commit goes through the pool-key search of E2 s7.
6. **Tests never touch the real home.** A temporary HOME and data folder; the real home is the `BoundaryHost.RealHome` seam, and the
   stand-in is compared byte for byte after a run. Recordings are made in scratch clones and scanned before they are committed.
7. **Defaults are embedded.** `shared/toolchains.json` is compiled into the binary. Nothing reads it from the repository or the data
   folder at run time.
8. **A measure you have not observed is a comment.** A property is tested by its effect in a real unit (a write that is refused, a
   file that is not read), not by a flag in an argv.

## K. Growth and interruption

| What grows | Size | Who retires it | When interrupted |
|---|---|---|---|
| step logs of a job: up to 7 steps of a project in a round (restore, format, build, typecheck, a selected and a full run, a re-run) against 2 in E2 | the real size is not measured (M9). The bound: 20 MiB a step, so 140 MiB a round and a project; 560 MiB for a job at the default cap (4 rounds), 1.5 GiB at a cap of 10 | the job-folder retention of E1 (30 days, 200 jobs) | a killed job leaves its folder for that sweep |
| `node_modules`, `bin`, `obj` in a coder worktree | the repository's own. Measured 2026-11-25 with `du`: `src_vs_code/node_modules` in three checkouts of this repository is 181 to 198 MiB, so 20 live worktrees are 4 GiB at most | with the worktree: 24 h after the job ends, 20 live worktrees a repository at most (E2) | the same sweep |
| the npm cache and the NuGet folder of a job | the dependencies once for a job; not measured (M9) | with the job folder (E2) | the same sweep |
| `checks/selection.json` and the excuse count in the job folder | under 4 KiB | with the job folder | written with a temporary file and a rename |
| recorded outputs and fixture trees | about 20 files, each under 64 KiB | static | — |
| `shared/toolchains.json` | static, under 16 KiB | — | — |

A unit that outlives coai is stopped by `RuntimeMaxSec` (E2). A format commit is never half made: the generation is compared
first, and a reset puts the worktree back.

## L. Not in E4, and the boundaries with the other epics

| Item | Built by | E4's part |
|---|---|---|
| Resume of a coder session (claude; codex, agy) | E2; E3 s5, s6 | none: E4's loop work sits above the vendor seam of E3 s7 |
| The vendor seam, the release table, the coder preflight rows | E3 s7, s5, s6 | none: E4 does not edit `CoderVendors`, `CoderUnit`, `CoderBoundary`, `BoundaryPreflight` |
| The caps, the no-progress stop, the flaky re-run, the format commit in the loop | **E4 s6**, after E3 | proved for claude and for every vendor the table of E3 ships |
| A `review_code` started by coai; parallel tasks; slots; merges | E5 | none (section I) |
| php, rust, python, java adapters | epoch 5 E3 | their manifests are known and refused by name |
| lint, audit, coverage, PR and CI steps | epoch 5 E4 | their names stay legal in `skip` |
| A status that coai writes into `task.yaml` | E5 / epoch 4 E5 | none |
| A run of the checks on the base commit before turn 0 (risk 8) | a task of E5 (the owner's answer to question 5, 2026-11-25) | none |
| The forwarder of a coder unit with no network (`doneWhen` line 8; the E3 judgement, G11) | **E4**, in a story that can be written only after E3 s6 t7 d6 measured agy behind a proxy (section Q) | no story yet |

**The files E3 still changes, and E4's order** (the E3 plan at `origin/feat/v2-ep3-e3-roles` `8216af35`):

| File | E3 task | E4 |
|---|---|---|
| `CoderFixLoop.cs`, `CoderWork.cs`, `RoleModelLauncher.cs`, `RoleFailover.cs` | s7 t2, s8 t1 | edited only in **s6**, which depends on E3's `s8-proof` |
| `Tools.cs`, `tests_fakecli/CoderVerbs.cs`, `tests_fakecli/Program.cs`, `src_mcp/src/Program.cs` | s7 t4, s5 t4, s6 t2, s6 t5 | edited only in s6 and s7; the fake's new verbs go in a new file |
| `BoundaryPreflight.cs`, `CoderBoundary.cs`, `CoderUnit.cs`, `CoderProgram.cs` | s5 t3, s6 t4, s7 t1 | never |
| `GitHistory.cs` | s3 t1 (built) | s2 t2 adds one method at the end |
| `shared/toolchain.schema.json`, `ToolchainParse.cs`, `ToolchainModel.cs`, `CoderEnvelope.cs` | s7 t3 (the instruction step: ONE top-level key of the file, `instructions`, and one member of the envelope; from the second plan round of E3, 2026-11-26) | s3 t1 adds four names of a PROJECT and s3 t4 its envelope members: the names do not overlap; the task that lands second merges the reader |
| `ARCHITECTURE.md`, `README.md`, `server.md`, `research/module_*.md` | s8 t5 | s7 t4, the last task |

Stories s1 to s5 touch none of the first three rows: they can be built while E3 is built. The toolchain reader (the row above) is
the one group of files both epics edit before s6: E3 s7 t3 and E4 s3 t1 each add names, and no name is shared. A step of E4
(restore, format, build, test) checks the coder's work; the instruction step of E3 runs before turn 0 and is not one of them. s6 names `e3-roles-complete/s8-proof`
because that id is in every outcome of E3's judgement. Its real need is E3 s7 t2 (the fix loop through the seam) and, for the
scenarios of each vendor, the stories of the vendors that ship.

Also not in E4: a spend cap of a role job; a network name for a command in the file; a private registry; `affectedBy` for a file
project; a cache of the restore step between rounds; a coder on native Windows or macOS (E2 refuses them); **a run of the checks
on the base commit before turn 0** (risk 8).

## M. Measure first (story s1) — the unknowns

A flag in a help text is a request until s1 sees its effect. No paid call: every measure is a tool run in a scratch clone.

| # | Unknown | Decides |
|---|---|---|
| M1 | Does `dotnet build --no-restore` pass with only a loopback device after a `dotnet restore` on the host network, on this repository (NU1900 is an error here)? | the network of a detected build |
| M2 | `dotnet test` on the installed SDK for a VSTest project, an MTP project with the runner in `global.json`, an MTP project without: the argv, the exit code of a failed test and of zero tests, the console lines | the default test argv, what is refused, the readers |
| M3 | This repository's test executable: the argv that runs the tests of two named files, its time, the exit code when nothing matches | `testFor` of this repository |
| M4 | Do `npm ci --ignore-scripts`, `npm run build`, `tsc` and `npm test` run inside the E2 unit as it is: node from the `~/.nvm` bind, the cache in the job folder, the project folder as the working folder? | whether node needs anything of the unit (rule J1) |
| M5 | What `npm ci` writes, and whether git would add any of it | the `tree_changed` sentence |
| M6 | jest, vitest, node:test: the console with `NO_COLOR` and `CI`; the exit code and text of a run that is given a file with no test | the readers, the "no test ran" entries |
| M7 | `dotnet format` with `--include` and `--no-restore`, the `whitespace --folder` and the `style` form: time, exit code, which files change, a second run | the dotnet default formatter, or none |
| M8 | prettier `--write` through `npx --no-install`: the same four answers; a file with a syntax error | the node default formatter |
| M9 | The size of the step logs of one round, and of the npm cache of a job | section K |
| M10 | For each runner and for this repository's test executable: the summary line, and whether and how a run names a test that passed | the run summary (section C), the proof of a re-run (section F) |

## N. Risks, and what only the owner can answer

Risks:

1. s1 can find that node needs a property the unit does not have. Then the node half waits for a security judgement (rule J1).
2. Console readers break when a tool changes its output. The cost is a worse digest (the raw tail), never a wrong verdict.
3. A narrowed re-run can excuse an order-dependent failure (section F). It is listed and noticed.
4. Strict detection refuses real repositories (a `requirements.txt` beside a solution; a fixture `package.json` with no lock file).
   The cure is a short file, and the sentence names it.
5. `npm ci` runs on the host network on what the coder wrote into `package.json`. Install scripts are off, the unit has no
   credential and no home; the packages run later, in the build and the test, where a detected project has only a loopback
   device (the build: when s1 measured it so).
6. s6 waits for E3's last story. When E3 is late, the loop hardening is late; s1 to s5 are not.
7. A 45-minute default (question 1) would make a stuck job cost more wall time than 30. The no-progress stop and the "could not
   be checked" rule end most of them earlier. The owner chose 60 minutes (2026-11-25): the same risk, with 30 more minutes for a
   stuck job at most.
8. **A base commit that is already red.** Nothing checks the base before turn 0, with a file or with detection. A coder then gets
   failures that are not its own, may change code outside its task, and the job ends at `no-progress` after two paid turns. A run
   of the checks on the base would cost one whole round for every job (9 minutes here) or a cache by commit, which is a new store
   with its own retention. It is not in E4; the envelope's `filesChanged` and the code gate are what show such a change today.
9. The frozen check definition (A4) refuses a task whose point is to change a script or a build target. Such a change lands as a
   commit of its own first; the refusal says so.
10. `doneWhen` line 8 (the forwarder, section Q) has no story. It waits for a measure of E3 (s6 t7 d6). The epic cannot close
    before that story is planned, or before the measure says that no vendor works behind a proxy.

Questions for the owner. Four were answered on 2026-11-25 (owner decision 38 of the v2 decisions list); each answer stands under
its question. Question 3 is open, and its line says what the plan does until there is an answer.

1. **The coder's default `timeoutMinutes`: 45 or 30.** Proposed: 45 (one round of this repository is 9 minutes). Until the owner
   answers it stays 30; s6 t3 builds the one table and takes the number from the answer.
   **Answered 2026-11-25: 60 minutes for a coder job; every other role keeps 30.** s6 t3 d1 names the number.
2. **npm only.** pnpm, yarn and bun are refused by name. Is npm enough for the repositories coai works on first? Another package
   manager is one more measure task and one more line of data: a plan change.
   **Answered 2026-11-25: npm only.** pnpm, yarn and bun stay refused by name. Nothing in the plan changes.
3. **This repository's file** gets `testFiles` and `testFor` (s5 t6), and `src_vs_code` is NOT made a node project of it (its
   tests need a compile step, its scenarios a VS Code host). s5 t6 starts after the answer.
   **Open.** Decision 38 does not name it.
4. **The paid proof** (s7 t3): one real coder job on claude haiku, 2 runs and 0.30 USD at most, on a pool key named through
   `COAI_CLAUDE_KEYS_FILE`. It needs a usable key of the pool on this machine, and it starts after the answer.
   **Answered 2026-11-25: allowed** — haiku, 2 runs and 0.30 USD at most, on a pool key. s7 t3 d1 says so.
5. **A red base commit** (risk 8) is not checked before turn 0. Proposed as a follow-up for E5: the scheduler starts many jobs
   from one integration head, so one check of that head serves them all. Nothing of it is in E4.
   **Answered 2026-11-25: a task of E5.** E4 does not change. The stories of E5 are not written yet: its planner takes the task
   from this line.

Recorded, not a question: `epic.yaml` of this epic has the E3 plan's wording of the resume line of `doneWhen` (the same text as on
`feat/v2-ep3-e3-roles`) and `spec: SPEC.md`; `v2/_gen/v2-spec.json` holds the same two edits. `dependsOn` is not changed; s6 names
the E3 story it waits for.

## O. Build order, test plan, Definition of Done

Build order: s1 (measure) → s2 (detection) → s3 (steps in the boundary); s4 (readers) after s1, beside s2 and s3; s5 (format,
selection, re-run) after s3 and s4; s6 (the loop) after s5 and after E3's `s8-proof`; s7 (proof) last. In a story the `dependsOn`
of its tasks.

Test plan: every scenario D1 to D14, T1 to T6, R1 to R8, F1 to F6, S1 to S11, K1 to K12 and L1 to L5 has a test that carries its
number in its name, or the task result says which test covers it. FakeCli is the only double, and only for the model; the units,
git, the tools, the detached runner and stdio are real. A structural scan has a companion that proves it matches something. The
whole MCP suite runs through the MTP executable, never `dotnet test`, one run at a time through
`.agents/conventions/tools/low-priority.mjs`. A recorded run is skipped unless its variable names a folder.

Definition of Done:

- [ ] A repository with no `.coai/toolchain.yaml` gets its steps from its manifests, read at the base and at the head from git
      objects; the seven fixture trees and this repository have golden answers; every refusal of section A3 has a test.
- [ ] `not_configured` and `toolchain_refused` are never green; a file at the base wins; a step the base had is never lost; a
      check definition that differs at the head refuses the job.
- [ ] Every step of a coder job runs through `CheckBoundary`; the unit's properties and environment names equal E2's.
- [ ] Each reader is tested on output recorded from the real tool; a digest is never empty.
- [ ] The format commit holds only files of the turn; green is only the word of a full test run; a project is left out only when
      coai can show that the diff does not reach it; a flaky test is excused only from a complete list and with proof that it ran
      again and passed, is listed, never sent, and is never one of the job's own.
- [ ] The loop stops with `no-progress` after two repeats, with `not-green` at the cap and with `timeout` on the one clock — for
      claude and for every vendor the table of E3 ships.
- [ ] The epic acceptance passes on the real binary, a client restart included; the paid proof is inside its cap and written down.
- [ ] `validate_work` accepts the `v2` folder; `plan-lifecycle.mjs` is clean; ARCHITECTURE.md, README.md, server.md and the
      knowledge base say what was built and what was not.

## P. Epic plan gate (proceed, 2026-11-25) — accepted changes

The gate read `EPIC_BUNDLE.md` at `1db073b6`. All 5 findings were accepted.

1. **[Superseded 2026-11-26 by the owner: reported, not refused; see A4.]** **A coder must not change its own checks** (A4, decision 11; s3 t5). The first plan only named a changed script in the result.
   Now the check definition of a project the base has is compared between the base and the head, and a difference refuses the job
   before any step. D12 tests a `scripts.test` replaced with `echo passed`; D13 the .NET forms; D14 what must stay allowed.
2. **Selection includes what depends on a change, or runs everything** (section E; s3 t6). The first plan left a node project out
   when no changed path was in its folder. Now a node project always runs, because coai cannot show from manifests that a diff
   does not reach it; a .NET unit is left out only when no `ProjectReference` path, across units too, leads to a changed unit and
   no path leaves its folder; impact unknown runs everything. S9 tests a shared package that breaks a dependent app.
3. **The re-run is decided from a complete list of failures** (sections C and F; s4 t5, s5 t4). The run summary is kept apart from
   the digest of 40 items and says whether it is complete; a list that is cut, or that cannot be shown complete, excuses nothing.
   K9 tests named failures beside one the reader cannot name; K10 more than 40 failures.
4. **The acceptance uses full runs for a detected project** (s7 t2). A detected project has no `testFor`. The selected run has a
   scenario of its own, on a repository whose file at the base names `testFor`.
5. **An excused test must be shown to have run and passed** (section F; s5 t4, s5 t5). By its name in the re-run, exactly once; or
   by equal totals for a re-run of the whole step. Zero matches and an ambiguous name excuse nothing. K11 tests a filter that
   matches nothing.

Moved with these changes: level one of the selection is s3 t6 (it was s5 t3); the re-run is two tasks (s5 t4 the rule, s5 t5 the
run); s4 t5 (the run summary) and s3 t5 (the frozen check definition) are new; s1 measures the summary lines (M10). 34 tasks,
complexity sum 170 (before the gate: 31 and 153).

The owner-facing points are open questions in section N. The plan decides none of them: the coder's default of 45 or 30 minutes;
npm only; `testFiles` and `testFor` for this repository, with `src_vs_code` not a node project; the paid proof of s7 t3; and a
check of the base commit before turn 0 as a follow-up for E5.

## Q. After the plan gate (2026-11-25): the owner's answers, and one line from the E3 judgement

Two things changed the plan after the gate of section P. Both are plan changes: the plan gate reads the bundle again.

1. **The owner's answers** (decision 38 of the v2 decisions list, 2026-11-25), each under its question in section N:
   - question 1: the coder's default `timeoutMinutes` is 60; every other role keeps 30 → decision 1, section H, s6 t3 d1;
   - question 2: npm only → decision 9, nothing else;
   - question 4: the paid run of s7 t3 is allowed (haiku, 2 runs and 0.30 USD at most, a pool key) → s7 t3 d1;
   - question 5: the check of a red base commit is a task of E5; E4 does not change → section L.
   Question 3 (this repository's `testFiles` and `testFor`) has no answer; s5 t6 still waits for it.
2. **`doneWhen` line 8**, from the security judgement of the codex and agy write boundary (E3 s4 t6,
   `v2/spikes/RESULTS_codex_agy_write_boundary_judgement.md` on `feat/v2-ep3-e3-roles` `d8f57ec7`, item G11 and section 10 item 4):
   "the coder of a vendor whose measure showed it works behind a proxy runs in a unit with no network but coai's forwarder with
   the vendor's hosts". `epic.yaml` and `v2/_gen/v2-spec.json` hold the line.
   - Why it is E4's: E3 is at its device limits (6 tasks in a story, 8 stories in an epic), and this is coder hardening.
   - Why it has no story yet: the judgement found no property of a user unit that limits the hosts of a process (U13, J5),
     and that a unit with no network can reach a program outside through a unix socket in a bound folder (J8). The other half —
     the vendor's CLI working behind a proxy — is not measured. E3 s6 t7 d6 measures it for agy; nothing measures it for codex.
     A forwarder cannot be designed before that.
   - What E4 does: when E3 s6 t7 has its results, one story (E4 has 7 of the 8 the device allows) is written from them and the
     epic gets a new plan round. When the measure shows that no vendor works behind a proxy, the line is met by that recorded
     result, no story is written, and accepted risk A2 of the judgement stays with the owner.
   - Until then: the agy and codex CLI processes have the whole network and the host's loopback (accepted risks A2 and C2).
