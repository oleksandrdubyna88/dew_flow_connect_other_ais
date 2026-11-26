# s3-steps-in-boundary — what was built, and the checklist of the s2 security verify for t5

Order of work (the coordinator's, 2026-11-26): the must-changes M1-M5 and S1 of the verify first, then t5, then t1, t2, t3, t4, t6. The verify is `v2/epoch-3/e4-adapters-coder-hardening/RESULTS_e4_s2_security_verify.md`.

## The list "What s3 t5 must cover" (14 items): met or not met

| # | Item of the verify | State | Where |
|---|---|---|---|
| 1 | node: the whole `scripts` member, the lock file's name | **met** | `CheckDefinition` (node members); `D12_*`, `AnyScriptChangedOrAdded_*` |
| 2 | .NET: the test projects the solution names (`.slnx` and `.sln` parsed) | **met** | `CheckDefinition.SolutionProjects`; `D13_ATestProjectTakenOutOf…` (both formats) |
| 3 | .NET: the facts of a test project and its runner; the `test` member of `global.json`; the names of every `Target`, `Import`, `UsingTask` | **met** | `the runner of <project>`, `the test member of global.json`, `the Targets, Imports and UsingTasks of <file>` |
| 4 | conditions and case: the raw test-fact elements with their conditions, without case, in the project, in every props/targets, inside `Choose`/`When` | **met** | `ProjectGraph.TestFactElements`; `Item4_*` |
| 5 | a definition file that does not parse (a DOCTYPE) refuses, not drops out | **met** | detection (`DotnetDetector`, M5) and `Item5_*`; a member of an unreadable file is `unreadable` |
| 6 | files the search never sees: deeper than 4 folders, under `bin`/`obj`/`node_modules`/dot folders; wildcard and property imports | **partly** | MET: the `.props`/`.targets` files in an `obj` folder (MSBuild imports `obj/<project>.*.props` by a wildcard) are recorded by name and object id, and a head that adds one is refused (`Item6_*`). NOT MET: a file deeper than 4 folders, under `bin`, `node_modules` or a dot folder (`.config/dotnet-tools.json`), and a file that a wildcard or property `Import` of the base pulls in from a place the search does not read. It needs a wider listing walk or a rule that refuses a base project whose imports cannot be resolved; not decided here. |
| 7 | files not in the table that decide the run | **met for the listed names** | `nuget.config` (any case), `Directory.Build.rsp`, `MSBuild.rsp`, `*.runsettings`, `testconfig.json`, `.npmrc`, `jest.config.*`, `vitest.config.*`, `vitest.workspace.*`, `vite.config.*`, `.mocharc*`, `babel.config.*`, `.babelrc*`, `tsconfig.json` when it is the typecheck, the prettier configuration when prettier is the formatter, and the `package.json` members `jest`, `vitest`, `prettier`, `config`, `mocha`, `ava`, `babel`, `overrides`, `resolutions`, `packageManager` — by object id or by text. NOT covered: a file such a configuration points at (a setup file, a reporter): code of the head, as in E2. |
| 8 | the whole `global.json` at every level, not only `test` at the root | **met** | every `global.json` at, above or below the unit is compared by object id (`Item8_*`), so `sdk`, `msbuild-sdks` and a new nested `global.json` refuse |
| 9 | workspaces: the `workspaces` member and the `scripts` of every member | **met** | `package.json workspaces`, `scripts.<name> of <member>`; a new member with scripts refuses (`Item9_*`) |
| 10 | the package behind a default command (`typescript`, `prettier`, the runner as an alias or `file:` package) | **met for the watched list** | `typescript`, `prettier`, `jest`, `vitest`, `ts-jest`, `ts-node`, `mocha`, `ava`, and `overrides`/`resolutions`: the version specifier of each is part of the definition; any other new dependency is not (D14). A runner package that is not on the list is not watched. |
| 11 | ignored and untracked files at check time (the checks run in the worktree, the detection reads the commit) | **not met** | Not a definition problem: the checks must run on a tree proven equal to the commit, ignored files included (for example `CoderRestore`, or an empty `git status --ignored` apart from known build output, before each round). It belongs to t3 and is recorded there as open. |
| 12 | restore on the host network takes its addresses from head-controlled files | **interim, the coordinator's decision, for the owner** | A head that adds or changes `nuget.config`, `.npmrc`, any `global.json` (`msbuild-sdks`), or the `Targets`/`InitialTargets` of a project is refused relative to the base (fail closed): items 7 and 8 and `Item12_*`. NOT covered: the `resolved` URLs inside a lock file (a new dependency with its lock file is allowed by D14, so lock contents cannot be compared) and what `dotnet restore` evaluates from files the search does not read (item 6). A wider policy (compare the hosts with the base, or an allow-list) is the owner's. |
| 13 | M2's twin for `{file}` in the format step (s5), and the case of file names (S6) | **S6 met; `{file}` is s5's** | A file named like a manifest with another case is refused (`S6_*`). `{file}` is filled as `./<path>` in s5 (`{unit}` already is, M2). |
| 14 | a step that exists and tests nothing (runner fact moved, `--passWithNoTests`, `IsTestProject` false): the compare comes before any marker logic | **met** | the compare runs in `ToolchainResolve`, before any step; `Item14_*` |

## Limits that remain, said once

- A test project only the head has, inside a unit the base has, is not run by that unit (the unit's steps are the base's: S10 of the verify). It is not refused and it shows in `Differences`. Whether a new test project must get a test step is open for the owner of the plan.
- `Differences` lists the facts that are not a definition; what the head adds is not named in the notes (S5 of the verify).

## t3, t4, t6 — measured and open (2026-11-26)

- **Item 11 of the verify stays open.** The detected steps run in the worktree and the detection reads the HEAD commit. Only the E2 `tree_changed` rule (worktree and HEAD equal after the checks) guards the gap; ignored files present before the checks are not compared. A proof that the tree equals the commit before each round is not built.
- **A detection defect the t3 test found:** two project files in one folder with no solution made the first a unit that owned the second (a project unit added a moment ago counted as a folder owner). Fixed: only a solution owns its folder; `TwoProjectFilesInOneFolder_AndNoSolution_AreTwoUnits_NotOneOwningTheOther`.
- **A compare defect the t6 test found:** a project file the head removed made its own members (`Targets, Imports and UsingTasks`) a "change"; the removal of a test project is refused as a lost step already, so a removed non-test project's own members are ignored.
- **t6 stated limit:** a test of one .NET unit that reads a file of another unit at run time by a path built in code is in no manifest; a change of that file alone leaves the reading unit out. A node project is never left out for the same reason (S9 is measured with real node: a change in a shared package alone runs the app's tests through a `file:` dependency and through a workspace, and they go red).
- **t6 level two** (`testFor`, selection of single test files) is s5.

## Pass 2 of the security verify (2026-11-26, `RESULTS_e4_s3_security_verify.md` at 68164c23): what was done, and the INTERIM decision for the owner

**Interim, owner decides — "frozen build definitions".** The coordinator consulted two models (consask 97493a75); both chose option A; it is the interim decision until the owner decides. It is not in SPEC.md (counted as a possible plan change for the owner).

- **What it is.** Before any restore, and after the worktree has been proven equal to the commit, a coder job's checks compare the WHOLE tree of the head with the base (`git diff-tree -r -z --no-renames --raw`, main repository, hardened reads: paths, modes, ids). The checks are refused (`toolchain_refused`, stop `toolchain-refused`, no step run) with a fixed sentence and two counts when the head added, deleted, modified or mode-changed any path whose file name (without case, at any depth) is on the list in `shared/toolchains.json` (`frozen`), or any symlink or submodule anywhere. It covers detected AND stated (toolchain-file) commands. The list is data: .NET project, props, targets, solution and runner-configuration files, `global.json`, `nuget.config`, lock files, `.config/dotnet-tools.json`; node `package.json` (the whole file), lock files, workspace and runner configuration, `tsconfig*.json`, babel, jest, vitest, mocha, prettier; `.coai/toolchain.yaml`, `.gitmodules`, `.gitattributes`. A scan test holds the code to it (`AFrozenDefinitionsTests`).
- **What it BLOCKS (by design).** A coder cannot add a package or a test project, or change any build file. Such a head is refused, and the person can take it in by hand (the sentence says so). It also blocks fixing a build file when the red test is a build file's fault.
- **Residual, an owner question too.** A script or helper file that a frozen definition invokes (for example `scripts/test.sh` under `scripts.test`) is not frozen, and an existing test of the base can be edited by the head (a test that is changed to pass is not a definition change). The name-level compare of t5 stays as a second layer.
- **Restore on the host network (M9, item 12)** is covered for the head's changes by the gate (a project, a lock file, a `nuget.config`, an `.npmrc` cannot change). Not covered: what the BASE's own files do on the host network, and a repository whose base is already hostile. The options (a) proxy with an allow-list, (b) restore from a cache coai fills stay the owner's.

**Must-change, one by one (the report's numbers):**

| # | State | How |
|---|---|---|
| M6 | fixed | `CoderChecks.ProveEqualToTheCommitAsync`: `git clean -ffdxq` through the coder-tree door, then `git status --porcelain --ignored` must be empty, before every round; else the round is `tree_changed` with a fixed sentence. Tests: `M6_*` (an ignored `tests/obj/x.targets`; an ignored `.npmrc` with the head's `.gitignore` line). |
| M7, M8, M9 (both parts), M10, M12 | refused by construction | the gate; one test per measured exploit in `AFrozenBuildDefinitionsTests.EveryMeasuredExploit_*` (red on 68164c23: all 22 failed; the `.gitmodules` case was moved out because the commit layer of E2 already refuses it with `coder-commit`). The name-level compare was not widened: M7's closed-element rule, M8's last-key map and M10's every-props-applies stay open as second-layer work if the owner wants the narrow rule instead of the gate. |
| M11 | fixed | members are keyed by the raw path and spelled only in a sentence (`DefinitionMember.Label`); `DetectedProject.OwnedPaths/TestPaths` replace `Split(',')` on paths (also in `AffectedProjects`). Tests `M11_D6_*`, `M11_D7_*`. |
| M13 | fixed for tonight | the selection is information only: every detected unit runs; the envelope's `toolchain.selection` says `applied: false`. `AffectedProjects` is not deleted. Test `M13_*`. |
| M14 | fixed | a script name is spelled only when it matches `^[A-Za-z0-9:._-]{1,64}$`, else "a script with an unusual name". Test `M14_*`. |

**Shoulds (by the report's numbers):**

- **S11 (red-first of the pass 1 tests)** — open: `M5_AnIsTestProjectInLowerCase…` should use a `PackageReference` in the usual case; no test plants a promisor remote.
- **S12 (the reads that decide the checks without `--no-replace-objects`)** — fixed: `rev-parse HEAD` runs as `--no-replace-objects rev-parse HEAD` (the one option the coder-tree door now lets before the verb); the selection's diff is `git diff-tree` of the two commits from the main repository through the hardened reads.
- **S13 (exit 0 is green; the `noTestRan` rows are read by nothing)** — open.
- **S14 (the solution file: a regex timeout is not caught; a project kept and marked not built)** — open.
- **S15 (the unit record names every step "test")** — fixed: `step.Step.Wire()`.
- **S16 (raw names that remain: the `tree_changed` sentence, `project.Id` in the envelope)** — open.
- **S17 (a check unit that could not be stopped)** — fixed: a step whose unit survived ends the round as `boundary_refused`, so the job ends `coder-boundary` and no fix turn starts beside a live process. Test `S17_*`.
- **S18 (a placeholder of 24 characters or fewer is spelled in a refusal of the toolchain file)** — open.
- **S19 (names the compare does not know: `jest.unit.config.js`, `test/jest-e2e.json`, a `tsconfig.base.json` reached by `extends`)** — `tsconfig*.json` and `jest.config.*` are now frozen; the other two are open.
- **S20 (open from pass 1: S2, S5, S7, S8, S9, S10)** — open.

**Existing tests that changed meaning.** A head that adds a project file is now refused before any step (`D14_*`, the envelope `D1_*` test). `.coai/toolchain.yaml` added or changed by the head is refused by the gate for a coder job (E2's "reported, and the base's file ran" now applies to a run with no coder job only).
