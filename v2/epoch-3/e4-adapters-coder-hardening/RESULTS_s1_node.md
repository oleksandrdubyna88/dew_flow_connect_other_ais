# Epic e4-adapters-coder-hardening, story s1-measure-first, task t2 — the Node commands, measured inside the check unit

Recorded 2026-11-26 on the WSL2 box of E2 (see `RESULTS_s1_dotnet.md` for the machine, and for what "inside the unit" means: the product's own `CheckBoundary` with a `BoundaryJob`, one unit per command, the preflight held, **no property of the unit changed**). Harness:
`src_mcp/tests/ANodeStepsAreMeasuredTests.cs` on `src_mcp/tests/MeasureBed.cs`; run as `COAI_RECORD_NODE=/home/jinx/coai-t3/e4/run-node ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests --filter-class '*ANodeStepsAreMeasuredTests'`. Scratch projects only (jest, vitest, node:test,
typescript, a project whose lock file is out of step, a project that is not cached), written by the test; the registry is the real one. node **v26.12.1**, npm **11.20.0**; resolved by the lock files of the run: jest 30.5.2, vitest 5.1.2, typescript **7.0.2** (the native compiler: see Surprises), is-odd 3.0.1.
No model, no paid call.

## Setup

- The unit's node comes from the `~/.nvm` bind of the product (`BoundaryHost.NvmEntries` from coai's PATH). **The stand-in home holds a COPY of node's install** (`.nvm/versions/node/v26.12.1`, 5 831 entries), so the unit's one bind under the home is the copy, never the real home. The harness hashed every file and time of the stand-in
  before and after the whole run: **the same 5 831 entries, the same hash** — nothing wrote there.
- The working folder is the project folder: `/bin/sh -c 'cd DIR && exec "$@"'` inside the unit (a product step with a working folder is s3). The environment is E2's plus `NO_COLOR=1` and `CI=true`; **no `npm_config_*` switch was set** (`update_notifier`, `fund`, `audit` of SPEC §B are unmeasured and not in `shared/toolchains.json` until a run shows what they change).
- Lock files are made in the unit on the host network (`npm install --package-lock-only --ignore-scripts --no-audit --no-fund`, 0.3 to 6.1 s) and committed by the harness, so that a later `git status` shows only what a step changed. Every `npm ci` runs on the host network with the cache in the job's own folder (`npm_config_cache`), cold.
- Prediction (SPEC §A3, before the run): `npm ci --ignore-scripts` host; `npm run build`, `npx --no-install tsc --noEmit` and `npm test` with only a loopback device; "`--no-install`: never a tool downloaded at check time".

## M4 — the commands, one row each (private network unless it says host)

| Row | Command (cwd = project) | Network | Exit | Time |
|---|---|---|---|---|
| n02 | `npm ci --ignore-scripts` (jest, 278 packages) | host | 0 | 5.5 s (cold cache) |
| n04/n06 | `npm ci --ignore-scripts` (vitest, node:test, typescript ×2) | host | 0 | 2.1 s, 0.9 s, 1.4 s, 1.2 s |
| m6 | `npm test` of jest / vitest / node:test (3 tests, 2 fail) | **none** | **1**, 1, 1 | 1.6 s, 0.6 s, 0.3 s |
| n08 | `npm run build` (`tsc -p .`, a clean project) | none | 0 | 0.6 s |
| n08 | `npx --no-install tsc --noEmit`; `npm run typecheck` (clean) | none | 0, 0 | 0.7 s, 0.5 s |
| n07 | `npx --no-install tsc --noEmit`; `npm run typecheck`; `npm run build` (two type errors) | none | **1, 1, 2** | 0.7 s, 0.6 s, 0.6 s |
| n09 | `npm ci --ignore-scripts` with a lock file out of step | host | **1** | 0.7 s |

**M4 answer: `npm ci --ignore-scripts` on the host network, then `npm run build`, `tsc` and `npm test` with only a loopback device, all run inside the unit as it is, node from the copy bound read-only, the cache in the job's folder, the project folder as the working folder.**
No command needed a property the unit does not have (rule J1 does not trigger). Nothing was written outside the worktree and the job folder: the job folder after the run holds the same seven entries as before (`coder dotnet-home home npm-cache nuget-packages tmp vartmp`), the main checkout's `git status` is empty,
the stand-in home is unchanged.

### The "no tool downloaded" rule (rule J8 of the SPEC: "a measure you have not observed is a comment")

`--no-install` is **not** the strict flag the SPEC took it for. A tool that is NOT installed (`prettier` in a project that does not have it), asked for with each spelling:

| Command | Network | Exit | Time | Seen |
|---|---|---|---|---|
| `npx --no-install prettier --version` | none | 1 | **70.5 s** | `npm error code EAI_AGAIN … request to https://registry.npmjs.org/prettier failed` — it **asks the registry which version it would run**, and with no network waits out the retries |
| the same | **host** | 1 | 1.2 s | `npm error npx canceled due to missing packages and no YES option: ["prettier@3.10.1"]` — it asked, was refused by `--no-install`, and **downloaded nothing** (`node_modules` and the cache have no prettier afterwards) |
| `npm exec --no -- prettier --version` | none | 1 | 71.3 s | the same registry request |
| `npm exec --offline --no -- prettier --version` | none | 1 | **0.9 s** | `npm error code ENOTCACHED … cache mode is 'only-if-cached' but no cached response is available` — never reaches for the network |
| `./node_modules/.bin/prettier --version` | none | 127 | 0.2 s | `sh: 1: exec: ./node_modules/.bin/prettier: not found` |
| `npx --no prettier --version` | none | 0 | 0.3 s | **prints npm's own version, `11.20.0`**: `--version` is taken by npx, not by the tool — a misleading green; never used |

The same three that avoid the network, with the tool PRESENT (`typescript` in the project): `npm exec --offline --no -- tsc --noEmit` 2.5 s, `npx --offline --no-install tsc --noEmit` 2.2 s, `./node_modules/.bin/tsc --noEmit` 1.7 s — exit 0 for the clean project, 1 for the two-error project (`npm exec --offline --no` 2.8 s).

**Reading:** `npx --no-install` does what the rule wants — nothing is downloaded (measured on the host network) — but it contacts the registry first, and with no network it costs about 70 s before it fails. The defaults of `shared/toolchains.json` keep `npx --no-install` for the two tools (the form measured WITH the tool present, 0.5 to 1.2 s, private network), and are only chosen when the manifest shows the tool is there (typescript package, prettier devDependency and configuration); the 70 s case is then a repository whose `node_modules` lacks a package its manifest names, and the step's own timeout bounds it. `--offline` (measured with `tsc`, 2.2 s) is the form that fails in 1 s; it is a candidate for the s3 step task, not a default recorded here.
`shared/toolchains.json` names `npx --no-install` for prettier and tsc as the SPEC proposed, **with `--offline` added** (measured with the tool present: 2.2 s) so that an absent tool fails in seconds. The step timeout also bounds the 70 s case.

## M5 — what `npm ci` writes

- In the project folder: one new entry, `node_modules/` (before: `.gitignore empty.test.js helper.js package-lock.json package.json sum.test.js`; after: the same plus `node_modules`). 54.9 MB for jest's 278 packages (`du`, 54 936 KiB; the cache it came from: 181 144 KiB).
- `git status --porcelain --ignored` of the worktree: `!! jest/node_modules/` — **ignored** when the project has a `.gitignore` with `node_modules/` (the usual case). In a project **with no ignore file** the same run leaves `?? noignore/node_modules/` — **git would add it**: the `tree_changed` state of SPEC §B is real and is triggered by a repository that does not ignore its `node_modules`.
- `dist/` after `npm run build` (`tsc -p .` with `outDir: dist`): `!! tsok/dist/`, ignored by the project's `.gitignore`; a project that does not ignore its build output would show `??` the same way (not run separately).
- Outside the worktree: nothing but the job folder. `npm-cache/` of the job: 205 680 KiB after the whole run (181 144 KiB after the first `npm ci`); the job folder in all 208 492 KiB; the worktree 159 000 KiB (seven `node_modules`). The main checkout's `git status` is empty. The stand-in home has the same hash before and after.
- `npm ci` with a WARM cache and no network (`tsok`, whose packages were fetched before): exit 0, 1.8 s, `added 2 packages in 1s`. With a **cold** cache and no network (`uncached`, `left-pad`): exit 1, **70.9 s**, `npm error code EAI_AGAIN … request to https://registry.npmjs.org/left-pad/-/left-pad-1.3.0.tgz failed` (fixture `node-npm-ci-no-network.txt`): a restore needs its host-network state and fails slowly, not wrongly, without it.
- The error block of `npm ci` with a lock file out of step (fixture `node-npm-ci-error.txt`): `npm error code EUSAGE`, `npm error \`npm ci\` can only install packages when your package.json and package-lock.json or npm-shrinkwrap.json are in sync. Please update your lock file with \`npm install\` before continuing.`, `npm error Missing: is-odd@3.0.1 from lock file`, then ~25 lines of usage text. The first lines are the useful ones; the rest is noise a reader must cut.

## M6 and M10 — the consoles (NO_COLOR and CI set; stdout and stderr are two streams: `[stderr]` in the fixtures)

Each scratch project has `sum.test.js` with `passes always`, `fails on the value` (assertion) and `fails by throwing`, an `empty.test.js` with no test, and a `helper.js`.

| | jest 30.5.2 | vitest 5.1.2 | node:test (node 26) |
|---|---|---|---|
| `npm test` | exit **1**; **stderr**: `FAIL ./sum.test.js`, `  ● fails on the value`, `Expected: 3 / Received: 2`, a code frame, `at Object.toBe (sum.test.js:2:50)`; summary on stderr: `Test Suites: 2 failed, 2 total`, `Tests:       2 failed, 1 passed, 3 total` | exit **1**; stdout has ` RUN  v5.1.2 <path>`, then stderr the failure blocks: ` FAIL  sum.test.js > fails on the value`, `AssertionError: expected 2 to be 3 // Object.is equality`, ` ❯ sum.test.js:4:50`; summary on stdout ` Test Files  2 failed (2)` / `      Tests  2 failed \| 1 passed (3)` | exit **1**; stdout, default reporter: `✔ passes always (0.6ms)`, `✖ fails on the value (0.8ms)`, `ℹ tests 4`, `ℹ pass 2`, `ℹ fail 2`, then `✖ failing tests:`, `test at sum.test.js:4:1`, the error with a stack of `file:///` URLs |
| summary line | `Tests:       2 failed, 1 passed, 3 total` | `      Tests  2 failed \| 1 passed (3)` | `ℹ tests 4` … `ℹ fail 2` — **the count includes `empty.test.js` as one passing "test"** (4, not 3) |
| names of passed tests | only with `--verbose`: `  ✓ passes always (2 ms)` | only with `--reporter=verbose`: ` ✓ sum.test.js > passes always 2ms` | always: `✔ passes always (0.6ms)` |
| a file that is not a test (`helper.js`) | exit **1**: `No tests found, exiting with code 1` + `Run with \`--passWithNoTests\` to exit with code 0` + the pattern lines | exit **1**: `No test files found, exiting with code 1`, `filter: helper.js` | exit **0**: `✔ helper.js` — **it runs the file as a test and counts it passed** |
| a test file with no test (`empty.test.js`) | exit **1**: `Test suite failed to run … Your test suite must contain at least one test.`, `Tests: 0 total` | exit **1**: ` ❯ empty.test.js (0 test)`, `Error: No test suite found in file <path>` | exit **0**: `✔ empty.test.js`, `ℹ tests 1`, `ℹ pass 1` |
| a file that is not there | exit **1**: `No tests found, exiting with code 1` | exit **1**: `No test files found, exiting with code 1` | exit **1**: `Could not find 'nothere.test.js'` |
| only the passing test by name | exit 1 (the `empty.test.js` of the same run still fails the run) — `PASS ./sum.test.js`, `  ✓ passes always (2 ms)`, `  ○ skipped fails on the value`, `Tests: 2 skipped, 1 passed, 3 total` | exit 1, same reason | exit 0 (`--test-name-pattern`) |

- **"No test ran" for node has no exit code of its own**: it is **exit 1 with a marker line** for jest (`No tests found, exiting with code 1`) and vitest (`No test files found, exiting with code 1`) — the same exit as a failure — and for node:test it is **not detected at all** (exit 0, the file counted as a passing test). The marker lines are the entries of `shared/toolchains.json`.
- The summary of **jest is on stderr**, vitest's on stdout, node:test's on stdout: a reader must read both streams. The consoles differ in whether a test's path is `./sum.test.js`, `sum.test.js > name` or `sum.test.js:4:1`.
- Fixtures: `node-jest-*.txt`, `node-vitest-*.txt`, `node-node-test-*.txt` (`three-tests`, `verbose`, `passing-only`, `file-not-a-test`, `file-with-no-test`, `file-missing`), `node-tsc-two-errors.txt`, `node-npm-run-typecheck-two-errors.txt`, `node-npm-run-build-two-errors.txt`, `node-npm-ci-error.txt`, `node-npm-ci-no-network.txt`.
- **tsc** (typescript 7.0.2) with two errors, `npx --no-install tsc --noEmit`, exit 1, no summary line, no colour:

  ```
  src/index.ts(1,7): error TS2322: Type 'string' is not assignable to type 'number'.
  src/index.ts(3,7): error TS2345: Argument of type 'number' is not assignable to parameter of type 'string'.
  ```
  `npm run typecheck` and `npm run build` print a two-line `> name@1.0.0 typecheck / > tsc --noEmit` header first, then the same lines. The `file(line,col): error TSnnnn:` shape is the classic one; it is the same under 7.0.2.

## M9 — sizes

52 step logs of the whole run (every command above): **22 271 bytes in all**, the largest 4 337 bytes (`npm ci` with a bad lock file). A round of a detected project is far under the bound of SPEC §K (20 MiB a step). The npm cache of the job after seven `npm ci` and a prettier probe: 205 680 KiB (about 200 MiB; jest alone 181 MiB); a job with a jest dependency tree is the case that
fills it. `node_modules` 54.9 MB for jest.

## Surprises

1. **`npx --no-install` is not offline** (above): it does not download, but it asks the registry and waits 70 s for it when there is none. `--offline` makes it fail in 2 s; with the tool present it is as fast.
2. **node:test counts a file as a test**, and a file with no test as a passing one. Totals of node:test (`ℹ tests N`) include one entry per file that has no test of its own; the proof by "equal totals" of SPEC §F must read the names, not only the count, for this runner.
3. **typescript 7.0.2 refuses a default `tsconfig`**: `error TS5011: The common source directory of 'tsconfig.json' is './src'. The 'rootDir' setting must be explicitly set` (first recording: `npm run build` exit 2 on a project that 5.x builds). A repository with no `rootDir` whose build worked in the past now needs one; coai does not guess it. The exit codes of `tsc` are its own: **1** = errors, no output written (`--noEmit`); **2** = errors, output written anyway (`tsc -p .` with `noEmitOnError` off, as in the recorded `npm run build` of the two-error project, and the TS5011 run of the first recording). So a build step and a typecheck step of one project exit differently for the same fault, and any non-zero code is a failure.
4. **An empty test file fails jest and vitest for the whole run**, so a selected run of a file with no test is red, not "nothing ran".
5. **The first recording's "no ignore file" project was not one**: a root `.gitignore` covered it. Fixed before the run recorded here (each project now has its own).

## What this does not settle

One node (v26.12.1), one npm (11.20.0); jest, vitest and typescript at "latest" on 2026-11-26, not pinned by the test (the lock files of the run are in `/home/jinx/coai-t3/e4/run-node/c/…/<project>/package-lock.json`, uncommitted). pnpm, yarn and bun were not run (npm only, owner's answer 1.2). A `package.json` with install scripts, a workspace, `file:` dependencies and a private registry were not run.
`npm_config_*` switches were not set. Windows and macOS (refused by E2). A project whose tests need the network.

## Files

Raw logs and `RESULT.txt`: `/home/jinx/coai-t3/e4/run-node/` (not committed). First recordings with the two harness faults (the `tsconfig`, the root `.gitignore`) are under `/home/jinx/coai-t3/e4/first-run/`.
