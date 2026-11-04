# Build brief — coai E5 story S4: the Node producers wrap the reference (commit 3 of the producer PR)

> Status: **plan only, nothing implemented yet, 2026-11-04 — the build brief of E5.S4 by Opus, with § 16 (the decisions of
> the rollout's epoch-5 re-review, approved by the owner 2026-11-04), which wins where it differs; its plan gate runs on the
> branch ref `docs/e5-s4-brief`.**

Written 2026-11-04 by Opus, read-only, against the worktree `<the producer worktree>`, branch
`feat/test-leftovers-e5-producer`, head `d3791c00` (docs-only over the pushed `6ec356a1`; every code line cited here is the
same at both). Conventions pin: `cc24f9e` (the mount `.agents/conventions`). The plan is
`todo/PLAN_test_leftovers_E5.md` (cited as `PLAN:<line>`); the S2 spec is `todo/PLAN_test_leftovers_E5_S2_spec.md`.

You (the coder) build from this brief alone. Where this brief and the plan differ, this brief says so in § 13 and the
difference is a plan problem for the main session, not yours to resolve. Two groups of sites (§ 7.4, § 7.5) wait for a
recorded decision; build them only when § 12 says the decision is recorded.

---

## 0. Read first (in this order)

1. `AGENTS.md`, `.agents/conventions/ENTRY.md`, `.agents/PROJECT.md`; then
   `node .agents/conventions/tools/rules.mjs explain --repo . --task implement --task test --task docs --file src_vs_code/scripts/owned.mjs --file src_vs_code/scripts/run-tests.mjs --file scripts/run-lib-tests.mjs --file .github/workflows/ci.yml`
   and `read` every selected rule (the test-leftovers core and detail, the baseline rule, local-cpu-budget, review-gate are among them).
2. The plan: § S4 `PLAN:1083-1172`; D3 `PLAN:415-422`; D10 `PLAN:578-595`; the D10 inventory `PLAN:143-153`; must-not-copy #12, #14, #15
   `PLAN:713`, `:715`, `:716`; D8's merge gates `PLAN:550-566`; D11 `PLAN:597-607`; § S5 as revised `PLAN:1201-1278`.
3. What S2-B already built (you EXTEND it, never a second module):
   - `src_vs_code/scripts/owned.mjs:1-38` — the ONE module that binds the token (`REPO_TOKEN = 'coai'` `:15`, `REPO_ROW` `:18`,
     `owned = ownedFor(...)` `:21`, `loadStartOfRunSweep` `:32-38`).
   - `src_vs_code/scripts/run-tests.mjs` — `REAL_TEMP_ROOT` `:103`, `mountMissingLine` `:130-132`, `loadOwned` `:138-145`,
     `startOfRunSweep` `:201-219`, the LEGACY writer `RUN_PREFIX` `:222`, `claimText` `:228-231`, `linuxStartSeconds` `:238-242`,
     `makeRunTemp` `:263-271` (mkdir FIRST, claim AFTER, no `wx`), `removeRunTemp` `:293-300`, `beginRun` `:318-327` (the "as before"
     fallback at `:324`), `main` `:349-362`.
   - `scripts/run-lib-tests.mjs:1-52` (sweep, count, then `node --test 'scripts/**/*.test.mjs'` at `:46`; it reads `tmpdir()` at `:25`).
   - `src_vs_code/scripts/startOfRun.mjs` (the switch reader; unchanged by S4).
   - `ci.yml:197-198` (`node scripts/run-lib-tests.mjs`), `ci.yml:377-379` (Windows: `node --test src/test/leftoversSweep.test.mjs`, direct).
4. The reference at the pin (read, never edit): `.agents/conventions/tools/lib/owned.mjs` — `tempRoot` `:67`, `cleanupScope` `:231`,
   `parseOwner` `:409`, `runRecord` `:438-447` (adopts `TESTSTODELETE_RUN` when it names a LIVE run of this user), `writeOwner` `:450-454`
   (`flag: "wx"` at `:452`), `dirFor` `:641-671` (claim → `scope.after` → `mkdirSync` `:659`; throws `owned: could not claim …` on a
   non-EEXIST claim error), `ownedFor` `:751`. The contract: `common/test-leftovers.md:31-36` (claim first, cleanup never at module load,
   "a child that makes temp paths gets an owned folder as TMPDIR, TEMP and TMP"), `common/test-leftovers-detail.md:120` (TESTSTODELETE_RUN),
   `:326` (the `coai` row). The baseline rule with F24 (conventions `origin/main`, `common/test-leftovers-baseline.md:15-24`, `:52-64`):
   a scope's proof file holds the TEMP assertion AND the child runs (forced folder failure, forced claim failure, each of TEMP/TMP/TMPDIR
   mismatched alone), each exiting non-zero before discovery.
5. The C# twin S3/S2-B built, for the shape you mirror: `src_mcp/tests_leftovers/RunTemp.cs:26-36` (`RefusedExitCode = 78`,
   `RefusalPrefix = "coai tests: no test ran — "`, `SelfTestVariable = "COAI_RUNTEMP_SELFTEST"`), `:107-150` (the checks and their
   sentences), `:204-217` (`InheritedRunWithheld`); `src_mcp/tests_leftovers/RunTempFolder.cs:100-105` (`WithholdInheritedRun`);
   `src_server/tests/RunFolderTests.cs` (the C# proof file: the TEMP assertion + forced child runs with a discovery sentinel).

## 1. Goal and the five things S4 settles

Every Node runner, script test, editor-host run and person-run script that runs outside a run folder owns its folder through the
ONE wrap module (`src_vs_code/scripts/owned.mjs`, token `coai`), in the contract's grammar, and leaks nothing (`PLAN:1087-1088`).
Concretely:

1. **`beginRun` fails closed.** No folder or no claim ⇒ ONE stderr line, exit **78**, before any `node --test` is spawned. No
   "as before" fallback. The claim is written exclusively FIRST (the reference's `dirFor` order — the wrap never makes a folder
   itself) and names THIS process: an inherited `TESTSTODELETE_RUN` is withheld for the claim and put back exactly after.
2. **A direct start refuses.** Every test file (compiled, source, `scripts/**`, the four `*.contract.ts`) has, as its FIRST import,
   a tiny guard that checks the runner's marker `COAI_TEST_RUN_FOLDER` and refuses with one line before the file's other imports and
   its tests run. `sonarcloud.yml:302` and `ci.yml:379` go through the runner (c8 still covers; § 9).
3. **The deferred Node sites get an owned folder** (§ 7, every site listed with its new container). The S5 renames stay S5's,
   EXCEPT the renames this brief names (the plan puts them in S4: `PLAN:1147-1151`, `:1133-1141`).
4. **Two Node proof files** (for S6 to mark): `src_vs_code/src/test/runFolder.test.mjs` (scope `src_vs_code/src/test/`) and
   `scripts/runFolder.test.mjs` (scope `scripts/`), each with the TEMP assertion and the five forced child runs.
5. **`release.yml`**: `:307` and `:1154` make their folder in `$RUNNER_TEMP`; every other Node invocation there is listed with its
   outcome (§ 8).

## 2. Decisions this brief makes (each with its reason)

| # | Decision | Reason / evidence |
|---|---|---|
| K1 | The marker is a NEW variable `COAI_TEST_RUN_FOLDER` = the absolute run folder path. NOT `TESTSTODELETE_RUN`. | The reference reads `TESTSTODELETE_RUN` as the RECORD every child claim is written with (`owned.mjs:438-447`, `:452`); it may be inherited from an outer process (so its presence proves nothing about THIS runner), and its value is a record, not a path, so it cannot be compared with TEMP. Probed: with `TESTSTODELETE_RUN` naming a live process of the same user, the reference's `ownedDir` writes THAT pid into the claim (scratch probe p3: `claim pid = 151833` = the parent, not `151835` = the caller); withheld, it writes the caller's. |
| K2 | The runner does NOT export `TESTSTODELETE_RUN` to its children; it withholds an inherited one only while it claims, and puts it back exactly (absent stays absent). | Mirrors the C# side (`RunTemp.cs:204-217`, `RunTempFolder.cs:100-105`). Nothing a child makes is outside the run folder, so no child claim needs the run's record. See § 13 P8 (the detail's sentence). |
| K3 | Refusals exit **78** with ONE stderr line. The test-run refusal lines start with `coai tests: no test ran — ` (the C# prefix). A missing mount stays exit **2** with `mountMissingLine` (D3). | Same code and prefix as C# (`RunTemp.cs:26-29`); 78 is EX_CONFIG. Node writes UTF-8 to stderr, so the em dash survives on Windows (the C# needed a fix, `RunTemp.cs:181-188`; Node does not). |
| K4 | The self-test seam is the C# variable `COAI_RUNTEMP_SELFTEST`, read by the wrap. Values: `fail-claim`, `mismatch-temp`, `mismatch-tmp`, `mismatch-tmpdir`, `exit-after-begin`. Any other value is ignored. Each value makes the run FAIL (never pass). | One seam across languages. `fail-claim` is forced through the REAL reference path: the CommonJS `fs.writeFileSync` is wrapped to throw `EACCES` for a path ending `.owner`, then `syncBuiltinESMExports()` (node:module) makes the reference's `import * as fs` see it; both are restored in `finally`. Probed (p3): the reference throws `owned: could not claim <dir>: EACCES` and the root is left empty. A forced folder failure needs no seam: TEMP/TMP/TMPDIR at a missing path. |
| K5 | The per-file guard is a dependency-free CommonJS pair in `src_vs_code/scripts/`: `runFolderGuard.cjs` (the checks, exported) and `inRunFolder.cjs` (one line: run the check, refuse on failure). Every test file imports `inRunFolder.cjs` FIRST. | A direct `node --test x` loads nothing but the file and its imports, so the check must be in the file's own import graph. `.cjs` works from compiled CommonJS, from `.mjs`, without a compile and without npm packages (the Windows step has no `npm ci`). Probed with this package's TypeScript 6.0.3 and its `tsconfig.json`: `import '../../scripts/runFolderGuard.cjs';` compiles with exit 0 and is emitted as the FIRST `require(...)` of `out/test/x.test.js`, and `out/test/../../scripts` is `src_vs_code/scripts` (scratch probe p2). Probed with Node 26: under `node --test` a file whose first import exits 78 is reported `✖ <file>` with the line in the runner's output, exit 1, and the file's body never ran (sentinel untouched, probe p1). |
| K6 | The guard checks, in order: marker set; marker absolute; the marker is a directory (lstat, not a link); `<marker>.owner` is a regular file; TEMP, TMP and TMPDIR each EXACTLY equal the marker (string equality, as C# `Ordinal`). It does NOT check the name shape. | No `testsToDelete` literal or owner-grammar regex in the guard, so S6's guard has nothing to classify there. The runner's own gate checks the name (K7). |
| K7 | The runner's gate (run-tests.mjs, run-lib-tests.mjs) runs the SAME `refusal()` after the folder is made and the variables are set, before spawning; plus `owned.classifyName(basename)` is `{form:'owner', token:'coai'}` and the basename starts with `owned.ownedPrefix('run')`. | This is the "process-wide gate before discovery" of the baseline rule (`test-leftovers-baseline.md:18-21`): nothing is discovered until it passes. |
| K8 | Runners and person-run entry scripts get ONE run folder each (`what` per § 6) with TEMP/TMP/TMPDIR and the marker pointed at it; everything they and their children make in temp lands inside and keeps its own name. Library modules (the 12 `seam-*.mjs` legs that make temp folders, `browserLayout.mjs`) get NOTHING: their entry's folder contains them. | `seam-*.mjs` are imported by `run-seam.mjs:53-73` and by tests (`src/test/seamLegs.test.mjs:7-8`, `ledgerFieldsSeam.test.mjs:6-7`); `browserLayout.mjs` by `measure-help-clicks.mjs:19`, `measure-failure-layout.mjs:18`, `measure-answer-code-layout.mjs:19`, `render-page.mjs:26`. A folder per library at import would nest and duplicate. § 13 P2. |
| K9 | `run-host.mjs` gets one run folder `host` (TEMP/TMP/TMPDIR moved) and its three folders INSIDE it as plain names `ws`, `data`, `profile`; `--user-data-dir=<folder>/profile`. | The plan's run-host paragraph (three top-level owned folders `host-ws`, `host-data`, `host-profile`, `PLAN:1107-1111`) leaves TEMP unmoved, so the editor's and the product's own temp files would still land in the machine temp, against S4's goal "leaks nothing" and the generic runner rule (`PLAN:1094-1096`). § 13 P3. |
| K10 | Person-run probe scripts that must not have TEMP moved (they MEASURE where a vendor CLI writes) get an owned SCRATCH (`ownedScratch(what)`): owner form, claimed by the script, removed at exit; TEMP is not touched. | `probe-native-linux-host.mjs` and `dotenv-name-vectors.mjs` remove their scratch today (`:109`, `:143`); the four that KEEP it wait for § 12 D-b. |
| K11 | Removal at exit is a synchronous `process.on('exit')` backstop registered by the wrap (it runs on `process.exit`, a normal end and an uncaught exception); runners ALSO call `run.end()` in their `finally` before `process.exit`, as `run-tests.mjs:357-361` does today. `end()` is idempotent. | The `then(exit)…finally` trap (#14) cannot leak through `process.exit` any more. A SIGKILL leaves the folder and its claim (named by the dead pid) for the next start-of-run sweep — the contract's path for a killed run. |
| K12 | `run-tests.mjs` and `run-lib-tests.mjs` take optional FILE ARGUMENTS: given files, they run exactly those (inside the run folder, behind the gate); none, they run what they run today. | The Windows step and the SonarCloud step need one runner command; the refusal line can name a working command for one file; the proof files need a discovery sentinel the runner would reach. |
| K13 | Inside a run folder, nothing written in a test or script spells the bare prefix or the owner grammar as a literal. Build expected names from the wrap: `owned.ownedPrefix(what)`, `owned.classifyName(name)`, `owned.thisOwnerToken()`. | S6's guard classes `legacy-name` and `owner-blind` (rule §7; `leftovers-check.mjs:1-24`); `owner-blind` takes no inline exemption. |

## 3. Files

New:

| File | What |
|---|---|
| `src_vs_code/scripts/runFolderGuard.cjs` | `MARKER = 'COAI_TEST_RUN_FOLDER'`, `VARIABLES = ['TEMP','TMP','TMPDIR']`, `REFUSAL_PREFIX = 'coai tests: no test ran — '`, `REFUSED_EXIT_CODE = 78`, `refusal({ env = process.env, io = require('node:fs') } = {})` → `null` or a reason (K6), `line(reason)` → the one line, `enforce({ env, io, write, exit } = {})`. `node:` modules only. |
| `src_vs_code/scripts/inRunFolder.cjs` | `'use strict'; require('./runFolderGuard.cjs').enforce();` and a header comment saying what it is and that it must be each test file's FIRST import. |
| `src_vs_code/scripts/ownedRun.mjs` | `startOwnedRun({ what, prefix = what, importOwned, error, exit })` and `startOwnedScratch({ what, prefix, … })` for entry scripts: `loadOwned` (from `run-tests.mjs`, exit 2 on a missing mount), then the wrap's `beginOwnedRun`/`ownedScratch`; a `RunFolderRefused` is one line `<prefix>: nothing ran — <reason>. Make that folder writable, or point TMPDIR, TEMP and TMP at a folder you can write.` and exit 78. No static import of the mount, no token. |
| `src_vs_code/src/test/runFolder.test.mjs` | PROOF FILE, scope `src_vs_code/src/test/` (§ 5). S6 adds its marker line; S4 does not. |
| `scripts/runFolder.test.mjs` | PROOF FILE, scope `scripts/` (§ 5). |
| `src_vs_code/src/test/runTests.test.mjs` | Replaces `runTemp.test.mjs` (§ 4.1). |
| `src_vs_code/src/test/owned.test.mjs` | The wrap (§ 4.2). |
| `src_vs_code/src/test/directStartRefuses.test.mjs` | The guard (§ 4.3). |
| `src_vs_code/src/test/runnerScripts.test.mjs` | The entry scripts (§ 4.4). |
| `src_vs_code/src/test/workflowsStartTestsThroughARunner.test.mjs` | The workflows (§ 4.5; the plan's `sonarcloudRunsThroughTheRunner` is its first test). |
| `src_vs_code/src/test/runHost.test.mjs` | #14 (§ 4.6). |
| `src_vs_code/src/test/fixtures/discoverySentinel.test.js` (or written by the tests into their private roots — preferred, no committed fixture) | A test file WITHOUT the guard that writes `$COAI_S4_SENTINEL` at load. If committed, it lives in a sub-folder the runner's `discover()` does not read (`run-tests.mjs:38-57` reads only the top of `out/test` and `src/test`). |

Changed:

| File | Change |
|---|---|
| `src_vs_code/scripts/owned.mjs` | Add `beginOwnedRun`, `ownedScratch`, `RunFolderRefused`, `SELF_TEST_VARIABLE` (§ 6.1). Keep every S2-B export. Still the only file that calls `ownedFor`. |
| `src_vs_code/scripts/run-tests.mjs` | Delete `RUN_PREFIX`, `claimText`, `linuxStartSeconds`, `ownStartSeconds`, `makeRunTemp`, `freeFolder`, `removeRunTemp`, `removed`, `beginRun` (`:221-327`); `main` per § 6.2; file arguments (K12); keep `discover`, `batches`, `TEST_CONCURRENCY`, `nodeTestArgs`, `REAL_TEMP_ROOT`, `reportLeftoversFirst`, `mountMissingLine`, `loadOwned`, `startOfRunSweep` and their signatures (other files and tests import them). Fix the header comment of `reportLeftoversFirst` (`:108-112`) so it no longer spells the legacy run name. |
| `scripts/run-lib-tests.mjs` | Run folder + gate + file arguments (§ 6.3); drop `import { tmpdir }` and `:25` (the root comes from the wrap's `owned.tempRoot()`, read before the folder is made). |
| `scripts/run-lib-tests.test.mjs` | Extend for the run folder, the gate, file arguments (§ 4.7). |
| every test file (≈ 550) | ONE new first import line (§ 6.5). |
| `src_vs_code/src/test/chatTestsLeaveNoTemp.test.mjs` | Its private child temp becomes `owned.ownedDir(t, 'chatleak')` and the child env also sets `COAI_TEST_RUN_FOLDER` to it (§ 7.6). |
| the entry scripts of § 6.4 | `startOwnedRun` first. |
| `src_vs_code/scripts/run-host.mjs` | § 6.6. |
| `http/run-contracts.mjs` | `startOwnedRun({ what: 'http-contracts' })` first; `:127` becomes `const dataDir = path.join(run.folder, 'data'); mkdirSync(dataDir);` (no `mkdtemp`, no `tmpdir`). |
| the § 7 sites | as listed there. |
| `.github/workflows/sonarcloud.yml:302` | `./node_modules/.bin/c8 --reporter=lcovonly --report-dir=../coverage/c8 --src=src node scripts/run-tests.mjs "out/test/"*.test.js` — plus a comment: why the runner (the run folder, the refusal of a direct start, c8 follows children through `NODE_V8_COVERAGE`). |
| `.github/workflows/ci.yml` | § 10. |
| `.github/workflows/release.yml:307`, `:1154` | § 8. |
| `shared/owner-claim-vectors.json` | DELETE (its one reader, `runTemp.test.mjs:179`, goes; C# stopped reading it in S3: `src_mcp/tests/OwnerClaimVectorsTests.cs:15` says so). Update that C# comment line to say S4 deleted the file. |
| `src_vs_code/src/test/runTemp.test.mjs` | DELETE (replaced by `runTests.test.mjs`; plan `PLAN:1100`). |
| `src_vs_code/src/test/leftoversSweep.test.mjs` | L5 (`:315`) extended to the new Windows step's command: the runner's import graph and each listed test file's graph are `node:` modules and repository files only. |
| `research/module_tests.md`, `research/module_runners.md` | § 11. |

## 4. Tests (RED first; expected RED on the current head and how to see it)

At the RED commit the guard does not exist yet, so the NEW test files in that commit do NOT import `inRunFolder.cjs` (they get it in
the guard commit with every other file). Run one file with `cd src_vs_code && npm run compile && nice -n 10 timeout 900 node --test src/test/<file>` at the RED commit;
after the guard commit only the runner form works: `nice -n 10 timeout 900 node scripts/run-tests.mjs src/test/<file>`.

Child processes in every test: start from `{ ...process.env }`, DELETE `NODE_TEST_CONTEXT` (or the child's own `node --test` sees itself
nested and skips every file — `runTemp.test.mjs:117-125` learned this), DELETE `COAI_TEST_RUN_FOLDER` and `TESTSTODELETE_RUN` unless the
case sets them, set TEMP/TMP/TMPDIR to the case's private root. Timeout 60 s per child (120 s for a child that runs a whole runner on a
fixture tree). Private roots: `owned.ownedDir(t, '<what>')` in the run folder (K13) — never `mkdtempSync` in a new file.

### 4.1 `runTests.test.mjs` (the extension runner) — replaces `runTemp.test.mjs`

Fixture tree (in the private root): `ext/out/test/*.test.js` and `ext/src/test/` as `runTemp.test.mjs:95-114` builds it; the fixture
tests append `who|os.tmpdir()|existsSync(os.tmpdir()+'.owner')|COAI_TEST_RUN_FOLDER` to `$SEEN`; the runner runs with `cwd = ext`.

| Name | Asserts | RED now |
|---|---|---|
| R1 `the runner's children and grandchildren share ONE run folder of the owner form, and it is gone with its claim after the run — pass or fail` | as `runTemp.test.mjs` X2 (`:128-153`), plus: basename starts with `owned.ownedPrefix('run')`, the rest matches `/^[A-Za-z0-9]{6}$/`; the children saw `COAI_TEST_RUN_FOLDER` = the folder; root holds nothing after | basename is `testsToDelete-coai-run-<6 hex>` (legacy); marker undefined |
| R2 `the claim is in the contract's grammar and names the runner, not an inherited TESTSTODELETE_RUN` (#15 Node half) | child env `TESTSTODELETE_RUN = owned.formatOwner(owned.thisOwner())` (THIS test process: live, same user, so the reference would adopt it); a fixture test copies the claim text to `$SEEN`; `owned.parseOwner(text)` is an owner; `.pid === result.pid` (the spawned runner) and `!== process.pid`; `.owner === owned.thisOwnerToken()`; `.host === owned.thisOwner().host` (tag `linux`/`macos`/`windows`, never `win32`/`darwin`) | `parseOwner` answers `{ unreadable: true }` (legacy text `<host>@linux:<pid>:<start>`) |
| R3 `a root that cannot take the folder fails before any test` | TEMP/TMP/TMPDIR = `<root>/missing`; exit 78; stderr is ONE line starting `coai tests: no test ran — ` and naming `<root>/missing`; the sentinel test never ran; `<root>/missing` still absent | stdout `run-tests: no folder of its own could be made (…); the tests share the machine's temp as before` (`run-tests.mjs:324`), the tests run, exit 0 |
| R4 `no compiled tests: exit 1, and nothing is left` | as X3 (`runTemp.test.mjs:155-178`) | green on arrival except the legacy-name check; keep it |
| R5 `file arguments run exactly those files, inside the run folder` | `node run-tests.mjs <ext>/src/test/a.test.mjs` runs `a` only (SEEN has one row) and `b` not; a missing or non-test path ⇒ exit 2, one line `run-tests: '<arg>' is not a test file …`, NOTHING made (validation is the first step, before the sweep) | arguments are ignored: both run |
| R6 `a killed runner's folder is collected by the next sweep` | a fixture test reads the runner's pid from the claim (`COAI_TEST_RUN_FOLDER + '.owner'`), kills it (`process.kill(pid, 'SIGKILL')`) and exits; the folder and claim remain; then `(await owned.loadStartOfRunSweep())({ repo, tmp: root, now: Date.now() + 11*60_000, probe: (pid) => pid === runnerPid ? { state: 'dead' } : owned.processProbe(pid) })` removes both (Linux, macOS); on Windows both stay and the result lists them (F16 interim) | no marker, legacy claim: the sweep lists it as `legacy:` and removes nothing |
| R7 `the runner removes only through the wrap` | source read: `run-tests.mjs` has no `rmSync`, `mkdirSync`, `writeFileSync`, `mkdtemp` | `:278`, `:266`, `:305` |

### 4.2 `owned.test.mjs` (the wrap)

| Name | Asserts | RED now |
|---|---|---|
| W1 `the token and row are the coai row of the Tokens table` | `token === 'coai'`, `REPO_ROW` equals the third column of the `` `coai` `` row of `.agents/conventions/common/test-leftovers-detail.md` (found by content, `:326` at the pin) | green on arrival (pins S2-B); say so |
| W2 `no Node file but owned.mjs calls ownedFor` | scan `src_vs_code/{src,scripts}`, `scripts`, `http` (not `node_modules`, `out`, `.agents`) | expected green; if red, fix the caller |
| W3 `beginOwnedRun claims first, exclusively, then makes the folder` | in-process, `env` = a plain object, `onExit` = a recorder, `root` = a private folder; the CJS `fs.writeFileSync`/`fs.mkdirSync` wrapped as RECORDERS (call through) + `syncBuiltinESMExports()`, restored in `finally`; recorded: `[claim <f>.owner flag wx, mkdir <f>]` in that order, same `<f>` | `beginOwnedRun` not exported (SyntaxError at import) |
| W4 `the claim names THIS process; an inherited TESTSTODELETE_RUN is withheld and put back` | `process.env.TESTSTODELETE_RUN` = a live record of ANOTHER process of this user (`process.ppid` with `owned.processProbe(process.ppid).start`, as probe p3); claim `.pid === process.pid`; after: the variable has its old value; with it absent before, it is absent after | not exported |
| W5 `a claim that cannot be written is a refusal, and nothing is left` | `COAI_RUNTEMP_SELFTEST=fail-claim` (via the `selfTest` parameter) ⇒ throws `RunFolderRefused` whose message names the root and `EACCES`; root empty; `fs.writeFileSync` works again after (write a `.owner` file in the root and remove it) | not exported |
| W6 `a missing root is a refusal naming it` | root `<p>/missing` ⇒ `RunFolderRefused`; nothing created | not exported |
| W7 `the variables and the marker name the folder; each mismatch self-test moves that ONE variable to the root` | env object: TEMP=TMP=TMPDIR=marker=folder; `mismatch-tmp` ⇒ only TMP = root | not exported |
| W8 `end removes the folder, then its claim; twice is a no-op` | after `end()`: neither exists; second `end()` throws nothing | not exported |
| W9 `exit-after-begin exits 3 after the folder exists, and the exit removes it` | child: `node -e` importing `ownedRun.mjs`, `startOwnedRun({ what: 'w9' })`, env `COAI_RUNTEMP_SELFTEST=exit-after-begin`, TEMP… = private root ⇒ exit 3, root empty | not exported |
| W10 `the run folder is mine under the root as given and as resolved` (#12, Node half for S4) | root given through a symlinked alias of a private folder (skip with the reason where a link cannot be made); `path.dirname(folder) === root as given`; `owned.isMine(folder)` and `owned.isMine(fs.realpathSync.native(folder))` both true; the claim parses the same both ways. On macOS ALSO with `root = os.tmpdir()` of the test process before any change (the premise `os.tmpdir() !== fs.realpathSync.native(os.tmpdir())` asserted first) | not exported |
| W11 `ownedScratch claims for this process, leaves TEMP alone, and is removed at exit` | as W3/W4/W8 for `ownedScratch('w11', { root, onExit })`; `process.env.TEMP` unchanged | not exported |
| W12 `startOwnedRun with no mount is one line and exit 2` | `importOwned` rejects ⇒ `error` got `mountMissingLine`'s sentence (names `git submodule update --init .agents/conventions`), `exit(2)`, nothing made | not exported |

### 4.3 `directStartRefuses.test.mjs` (the guard)

| Name | Asserts | RED now |
|---|---|---|
| G1 `refusal(): the table` | cases: unset; relative; missing; a FILE at the path; no claim; the claim a directory; TEMP / TMP / TMPDIR each different alone; all good ⇒ `null`. Each reason names what failed | module missing |
| G2 `a compiled test started with node --test refuses before its tests` | `cd src_vs_code; node --test out/test/runtimeSinceIsAhead.test.js`, no marker, TEMP… = private root ⇒ exit 1; the output holds EXACTLY ONE line starting with the prefix; no `ℹ pass` count above 0; the private root is empty | exit 0, its tests pass |
| G3 `a compiled test started with plain node refuses with exit 78` | `node out/test/runtimeSinceIsAhead.test.js` ⇒ exit 78, stderr one line | runs |
| G4 `a scripts/lib test started directly refuses` | from the repository root, `node --test scripts/lib/kimi.test.mjs`, as G2 | exit 0 |
| G5 `every test file imports the guard first` | the set = `discover(pkg)` mapped back to sources (`out/test/X.test.js` → `src/test/X.test.ts`) ∪ `src/test/*.test.mjs` ∪ `src/test/*.contract.ts` ∪ every `scripts/**/*.test.mjs`; for each, the first line that starts an `import` (after comments, a shebang, `'use strict'`) is EXACTLY `import '<relative>/inRunFolder.cjs';` with the right relative path, and no `require(`/`import(` comes before it; ALSO each `out/test/*.test.js` and `*.contract.js`: the first `require(` line is `require("../../scripts/inRunFolder.cjs");`. The failure lists every file that fails (count first) | every file fails (≈ 550) |
| G6 `nothing but a test file imports the guard` | no file under `src_vs_code/src` (except `*.test.*`, `*.contract.ts`), `src_vs_code/scripts`, `scripts` (except `*.test.mjs`), `http` mentions `inRunFolder.cjs` (the guard module's own header excepted) | green on arrival |

### 4.4 `runnerScripts.test.mjs` (the entry scripts)

`ENTRIES` is a frozen list (§ 6.4 table: file + `what`). For each entry except `run-tests.mjs` and `run-host.mjs` (their own files test them):

| Name | Asserts | RED now |
|---|---|---|
| E1 `every runner, live and measure script is in the list` | every `src_vs_code/scripts/*.mjs` named `run-*`, `live-*`, `measure-*` or `render-page.mjs`, and `http/run-contracts.mjs`, `scripts/live-consult-all.mjs`, `scripts/measure-idle-server.mjs`, is in `ENTRIES` | green (the list is new) |
| E2 `each entry starts its owned run before anything else` | source read: the first statement after the last static `import` is `const run = await startOwnedRun({ what: '<what of the table>'` (`prefix` optional) | fails for every entry |
| E3 `each entry, stopped right after its run began, leaves nothing` | ONLY for entries that passed E2 (never spawn an entry that failed E2: the live scripts call paid vendors). Spawn `node <entry>` with TEMP… = private root, `COAI_RUNTEMP_SELFTEST=exit-after-begin` ⇒ exit 3; private root empty; stderr empty | not run (E2 failed) — the E2 failure is the RED |

### 4.5 `workflowsStartTestsThroughARunner.test.mjs`

| Name | Asserts | RED now |
|---|---|---|
| F1 `sonarcloudRunsThroughTheRunner` | every non-comment line of every `.github/workflows/*.yml` and of `src_vs_code/package.json` `scripts` that starts `node --test` (also after `c8 …`) is a failure naming file:line; the SonarCloud extension coverage step contains `node scripts/run-tests.mjs` after `c8` | `sonarcloud.yml:302`, `ci.yml:379` |
| F2 `the Windows step runs the Node proofs through the runner` | the `windows` job has the step of § 10 with that exact command | absent |
| F3 `release.yml makes its temp folders in RUNNER_TEMP` | every `mktemp` line in `release.yml` holds `"$RUNNER_TEMP/` | `:307`, `:1154` |

### 4.6 `runHost.test.mjs` (#14)

`run-host.mjs` exports `main({ launch, bundle, env, root, onExit })` and runs only when started as a script (`process.argv[1]` resolves to it).

| Name | Asserts | RED now |
|---|---|---|
| H0 `run-host.mjs does nothing when imported` | source read FIRST (before any import): an argv guard exists and no `mkdtemp`/`main()` call is at module level; only then `import()` it | RED from the source read: `run-host.mjs` makes two folders and starts `main()` at import (`:39`, `:49`, `:114`) — the test must NOT import it while that holds |
| H1 `a launch that resolves: both launches got the folders, and after main the run folder and its claim are gone` | `launch` recorder resolves; `bundle` no-op; `env` a plain object; `root` private. Launch 1: `launchArgs` = `[<folder>/ws, '--disable-extensions', '--user-data-dir=<folder>/profile']`, `COAI_DATA_DIR = <folder>/data`; launch 2: `COAI_DATA_DIR = <folder>/data/named-but-missing`, `COAI_HOST_MISSING_DATA_ROOT = '1'`; main returns 0; root empty | (H0) |
| H2 `a launch that rejects: main returns 1 and still leaves nothing` | `launch` rejects; returns 1; root empty | (H0) |

### 4.7 `scripts/run-lib-tests.test.mjs` (extended)

| Name | Asserts | RED now |
|---|---|---|
| L-a `the run folder is made after the sweep and the count, and before the spawn` | with seams: order `sweep → count → begin → spawn`; the spawn's env (inherited `process.env` at that time) has TEMP=TMP=TMPDIR=marker=folder | no folder |
| L-b `a refused begin is one line, exit 78, and nothing is spawned` | `beginOwnedRun` throws `RunFolderRefused` ⇒ 78, spawn never called | runs anyway |
| L-c `file arguments are passed instead of the glob` | `runLibTests({ files: ['scripts/lib/kimi.test.mjs'] })` ⇒ spawn args `['--test', 'scripts/lib/kimi.test.mjs']`; a bad file ⇒ exit 2 before the sweep | glob always |

## 5. The two proof files (S6 marks them; S4 writes them)

Each file holds, together (the baseline rule wants the child runs IN the proof file, `test-leftovers-baseline.md:57-61`):

**P-T `TEMP names the run folder`** (in-process): `process.env.TEMP === TMP === TMPDIR === COAI_TEST_RUN_FOLDER`; `os.tmpdir() === COAI_TEST_RUN_FOLDER`;
`owned.classifyName(basename)` is `{ form: 'owner', owner: owned.thisOwnerToken(), token: 'coai' }` and the basename starts with
`owned.ownedPrefix('run')`; the claim parses (`owned.parseOwner`), its owner and host are this process's, its pid is NOT `process.pid` (it is the
runner's) and `owned.ownerState(record)` is `'live'`. RED now: the marker is undefined.

**P-1…P-5 the five forced child runs.** Each spawns the scope's RUNNER, never `node --test`:
- `src_vs_code/src/test/runFolder.test.mjs`: `node <pkg>/scripts/run-tests.mjs` with `cwd` = a fixture `ext` whose `out/test/sentinel.test.js` (no guard) writes `$COAI_S4_SENTINEL` at load.
- `scripts/runFolder.test.mjs`: `node <repo>/scripts/run-lib-tests.mjs <private>/sentinel.test.mjs` (K12; same sentinel body as ESM).

| Run | Env | Expect |
|---|---|---|
| P-1 folder | TEMP=TMP=TMPDIR=`<root>/missing` | exit 78; stderr exactly ONE line, prefix `coai tests: no test ran — `, names `<root>/missing`; sentinel absent; `<root>/missing` absent |
| P-2 claim | TEMP…=`<root>`, `COAI_RUNTEMP_SELFTEST=fail-claim` | exit 78; one line naming `claim` (the reference's `could not claim`) ; sentinel absent; `<root>` empty |
| P-3 TEMP | TEMP…=`<root>`, `COAI_RUNTEMP_SELFTEST=mismatch-temp` | exit 78; one line holding `TEMP='<root>'`; sentinel absent; `<root>` empty (the folder and claim the run made are removed) |
| P-4 TMP | `mismatch-tmp` | as P-3 with `TMP=` |
| P-5 TMPDIR | `mismatch-tmpdir` | as P-3 with `TMPDIR=` |

`<root>` is `owned.ownedDir(t, 'proof-<n>')` (inside this run's folder). Stdout may hold the sweep's and the count's lines; stderr holds ONLY the refusal.
RED now (on `run-tests.mjs`; `run-lib-tests.mjs` alike): P-1 runs the tests "as before", exit 0, sentinel written; P-2…P-5 the self-test is
ignored, the sentinel is written, exit 0. These five ARE the "fails before discovery" proof: the runner spawns no `node --test` until its gate passes.

## 6. The code

### 6.1 `owned.mjs` additions (sketch; keep it small and exact)

```js
import fs from 'node:fs';
import { createRequire, syncBuiltinESMExports } from 'node:module';
import guard from './runFolderGuard.cjs';
export const SELF_TEST_VARIABLE = 'COAI_RUNTEMP_SELFTEST';
export class RunFolderRefused extends Error { constructor(reason, options) { super(reason, options); this.name = 'RunFolderRefused'; } }
const firstLine = (text) => String(text).split('\n')[0].trimEnd();

/** A scope the reference accepts (`after`) whose close is SYNCHRONOUS — it runs from an `exit` listener. LIFO; a failing step is one stderr line. */
function exitScope() { /* { after(fn), closeSync() } */ }

/** Runs `make` with TESTSTODELETE_RUN taken out of process.env (the reference reads it there, owned.mjs:438) and put back EXACTLY (absent stays absent). */
function withInheritedRunWithheld(make) { /* … */ }

/** fail-claim: the CommonJS fs.writeFileSync throws EACCES for '*.owner'; syncBuiltinESMExports() so the reference sees it; restored in finally. */
function withClaimFailure(make) { /* … */ }

/** Make + verify: ownedDir in `root`, claim verified = parseOwner(text) names process.pid and thisOwnerToken(); on any failure the scope is closed and RunFolderRefused thrown. */
function claimedFolder({ what, root, selfTest }) { /* returns { folder, end } */ }

export function beginOwnedRun({ what, root = owned.tempRoot(), env = process.env, onExit = (fn) => process.on('exit', fn),
  exit = (code) => process.exit(code), selfTest = process.env[SELF_TEST_VARIABLE] } = {}) {
  // 1 claimedFolder (withheld; fail-claim when selfTest says so)  2 onExit(end)  3 env.TEMP = env.TMP = env.TMPDIR = env[guard.MARKER] = folder
  // 4 mismatch-temp|-tmp|-tmpdir: that ONE variable = root   5 exit-after-begin: exit(3)   6 return { folder, root, end }
}
export function ownedScratch(what, { root = owned.tempRoot(), onExit = …, selfTest = … } = {}) { /* 1 and 2 only; returns { path, end } */ }
```

Messages (the `reason` of `RunFolderRefused`): `the run folder could not be made in '<root>' (<first line of the cause>)`; `the claim beside '<folder>' does not name this process`.

### 6.2 `run-tests.mjs` `main`

```
1  files = filesFrom(process.argv.slice(2))        // K12; a bad argument: one line, exit 2, nothing made
2  owned = await loadOwned()                       // exit 2 on a missing mount (unchanged)
3  await startOfRunSweep({ owned }); reportLeftoversFirst()   // unchanged: the real root, before anything is made
4  try { run = owned.beginOwnedRun({ what: 'run', root: REAL_TEMP_ROOT }) }
   catch (RunFolderRefused e) { stderr: `coai tests: no test ran — ${e.message}. Make '${REAL_TEMP_ROOT}' writable, or point TMPDIR, TEMP and TMP at a folder you can write, then run node scripts/run-tests.mjs again.`; exit 78 }
5  gate = guard.refusal() ?? nameRefusal(run.folder, owned)   // K7
   if (gate) { run.end(); stderr: guard.line(gate) (its tail names the runner commands); exit 78 }
6  status = 1; try { status = files ? runFiles(files) : runAll() } finally { run.end() }
7  process.exit(status)
```
`runFiles(files)`: compiled (`*.test.js`) in one batch, each `*.test.mjs` its own batch — the rule of `batches()` (`:70-73`); keep `nodeTestArgs`.

### 6.3 `run-lib-tests.mjs`

`runLibTests({ files, loadOwned, sweep, sweepOptions, spawn, log, error, repoRoot, root, mode, begin })`: validate `files` (exit 2) → load (exit 2) →
`root ??= owned.owned.tempRoot()` → sweep → count → `begin` (default `owned.beginOwnedRun({ what: 'run', root })`; refused ⇒ the line, 78) →
gate (as 6.2 step 5, the command named is `node scripts/run-lib-tests.mjs`) → spawn `['--test', ...(files ?? ['scripts/**/*.test.mjs'])]` → `end()` →
return the status. The CLI block: `process.exit(await runLibTests({ files: argv }))`.

### 6.4 Entry scripts (K8): `startOwnedRun` as the first statement after the static imports

```js
import { startOwnedRun } from './ownedRun.mjs';              // ../src_vs_code/scripts/ownedRun.mjs from scripts/ and http/
const run = await startOwnedRun({ what: '<what>' });          // run.folder; TEMP/TMP/TMPDIR now name it
```
A script that removed its own temp folder at the end may keep doing so (it is inside); one that exits through `process.exit` needs nothing
more (K11). Do NOT change their `mkdtempSync(join(tmpdir(), …))` lines in `src_vs_code/scripts` (inside the run folder now; no guard scans that
folder today — § 13 P13). In `scripts/` (scanned by the Node guard's `NODE_TREE`, `leftovers-check.mjs:203`) replace them (below).

| Entry | `what` | Note |
|---|---|---|
| `src_vs_code/scripts/run-seam.mjs` | `seam` | before the top-level `await import(...)` at `:32-73`; its 12 legs (`seam-*.mjs`) make their folders inside |
| `run-contract.mjs` | `contract` | `:136` stays (inside); its own `exit` listener (`:68`) runs after ours — a file a still-running server holds stays claimed for the next sweep (K11) |
| `run-parity.mjs` | `parity` | makes nothing in temp itself; its .NET child may |
| `live-agent.mjs` | `live-agent` | |
| `live-api-settings-compat.mjs` | `compat-api` | |
| `live-chat.mjs` | `live-chat` | |
| `live-claude-models.mjs` | `live-models` | |
| `live-close-consult-compat.mjs` | `compat-close` | |
| `live-command-models-compat.mjs` | `compat-cmd-models` | |
| `live-feature-schema-compat.mjs` | `compat-schema` | |
| `live-feature-vendor-compat.mjs` | `compat-vendor` | |
| `live-fresh.mjs` | `live-fresh` | |
| `live-gauge-compat.mjs` | `compat-gauge` | |
| `live-paths-per-side-compat.mjs` | `compat-paths` | |
| `live-security-settings-compat.mjs` | `compat-security` | |
| `live-vibe-runtime-compat.mjs` | `compat-vibe` | |
| `measure-stream.mjs` | `measure-stream` | |
| `measure-image.mjs` | `measure-image` | |
| `measure-append.mjs` | `measure-append` | `:156` `join(chosen ?? tmpdir(), …)` lands inside unless the person names a folder |
| `measure-consultant-skew.mjs` | `measure-skew` | `:71` (a fixed name in `tmpdir()`) lands inside |
| `measure-help-clicks.mjs` | `measure-clicks` | via `browserLayout.mjs` |
| `measure-failure-layout.mjs` | `measure-failure` | via `browserLayout.mjs` |
| `measure-answer-code-layout.mjs` | `measure-code` | via `browserLayout.mjs` |
| `render-page.mjs` | `render-page` | via `browserLayout.mjs` |
| `http/run-contracts.mjs` | `http-contracts` | `:127` → `path.join(run.folder, 'data')` + `mkdirSync` |
| `scripts/live-consult-all.mjs` | `live-consult` | `:25` → `mkdirSync(join(run.folder, \`live-${vendor.id}\`))` (unique per vendor; check the loop) — no `mkdtemp`, no `tmpdir` |
| `scripts/measure-idle-server.mjs` | `measure-idle` | `:32` → `join(run.folder, 'idle-arm')` + `mkdirSync`; keep its `rmSync` `:114` |

Every `what` is at most 17 characters (the reference's budget is 25 under the longest owner, `owned.mjs:127-136`).

### 6.5 The guard line in every test file (its own commit)

Insert, as the FIRST import of the file (after a header comment, a shebang or `'use strict'`; before every other import):

| Files | Line |
|---|---|
| `src_vs_code/src/test/*.test.ts` (512), `*.contract.ts` (4), `*.test.mjs` (16 + the new ones) | `import '../../scripts/inRunFolder.cjs';` |
| `scripts/lib/*.test.mjs`, `scripts/native/*.test.mjs` | `import '../../src_vs_code/scripts/inRunFolder.cjs';` |
| `scripts/*.test.mjs` (`run-lib-tests.test.mjs`, `runFolder.test.mjs`) | `import '../src_vs_code/scripts/inRunFolder.cjs';` |

Do it with a script you keep in the scratchpad (not committed); re-read the counts: the suite's test totals before and after this commit are
EQUAL (only the import changed). `npm run lint` and `npm run typecheck` green.

### 6.6 `run-host.mjs`

`export async function main({ launch = runTests, bundle = () => execFileSync('npm', …), env = process.env, root, onExit } = {})`:
1 delete the POISONED names from `env`; 2 `run = owned.beginOwnedRun({ what: 'host', env, root, onExit })` (load the wrap with `loadOwned`,
exit 2 on a missing mount); 3 `ws`, `data`, `profile` = `mkdirSync(join(run.folder, …))`; 4 `try { bundle(); launch 1; launch 2; return failures ? 1 : 0 }
finally { run.end() }` with the launch arguments of H1 (`--user-data-dir=<profile>` makes the comment at `:87-88` true); 5 the CLI block (argv guard):
`const code = await main(); console.log/console.error as today; process.exit(code)`. Record in `module_tests.md` the default profile folder of the
installed `@vscode/test-electron` (`PLAN:333-335` asks for it).

## 7. The sites (the list used, and each site's new container)

**The list used.** The plan's S4 lists (`PLAN:1133-1151`) and the `scripts/` lines of § Facts (`PLAN:155-161`), each RE-READ on `d3791c00`
(all line numbers below are verified there), checked against
`git grep -nE 'mkdtemp|tmpdir\(|os\.tmpdir|testsToDelete|teststodelete|mktemp|/tmp\b|\$TMPDIR|RUNNER_TEMP|process\.exit' -- src_vs_code/scripts http scripts .github/scripts`
(417 lines; the earlier scratch classification is lost and was NOT used). The grep found the plan's lines and the extra sites marked NEW.

### 7.1 `scripts/lib` (run by `ci.yml:198` through `run-lib-tests.mjs`, now inside its run folder) — owner form through the wrap

Module-level `mkdtempSync` becomes an owned folder made in a hook, never at module load (rule §2; `PLAN:1097-1098`):
`import { owned } from '../../src_vs_code/scripts/owned.mjs'; const scope = owned.cleanupScope(); let base; before(() => { base = owned.ownedDir(scope, '<what>'); }); after(() => scope.close());`
Inside a test, use the test's context: `owned.ownedDir(t, '<what>')`. Any module-level use of `base` (a path computed at load, a `skip` that reads it)
moves into the hook or a function. The folder lands in the run folder (`ownedDir`'s parent is `tempRoot()` = `os.tmpdir()` = the run folder).

| Site | Today | New |
|---|---|---|
| `host-facts.test.mjs:17` | `testsToDelete-host-facts-test-` | `ownedDir(scope, 'host-facts')` |
| `kimi-d19.test.mjs:67`, `:84` (inside tests) | `testsToDelete-d19-arms-` | `ownedDir(t, 'd19-arms')` |
| `kimi-home.test.mjs:18` | `testsToDelete-kimi-home-test-` | `'kimi-home'` |
| `kimi-schedule.test.mjs:12` | `testsToDelete-kimi-schedule-` | `'kimi-schedule'` |
| `kimi-shim.test.mjs:24` | `testsToDelete-kimi-shim-test-` | `'kimi-shim'` |
| `kimi.test.mjs:14`, `:225` | `testsToDelete-kimi-scratch-test-`, `-kimi-acp-` | `'kimi-scratch'`, `'kimi-acp'` (`:100`, `:280` stay: `mkdtempSync(join(base, …))` inside) |
| `proc.test.mjs:15` | `testsToDelete-proc-test-` | `'proc'` |
| `vibe-manifest.test.mjs:12` | `testsToDelete-vibe-manifest-test-` | `'vibe-manifest'` |
| `vibe.test.mjs:18` | `testsToDelete-vh-vibelib-` | `'vh-vibelib'` |
| `caller-transcripts.test.mjs:18` | `testsToDelete-caller-transcripts-` | `'caller-transcripts'` |
| `caller-transcripts.test.mjs:284`, `:295` | `doesNotMatch(…, /…\|testsToDelete/u)` — an owner-blind pattern | the alternative becomes the escaped `path.basename(base)` (build the RegExp at run time); the test keeps its meaning (no folder name reaches the output) — both sides with `:18` (`PLAN:1137-1138`) |
| `caller-transcripts.test.mjs:2` (prose) | spells the bare prefix | reword: "lives in an owned folder of the wrap inside the run folder, removed in …" — no prefix literal |

Not S4 (inside the run folder, no bare prefix; S6 baseline or S7): `transcript-sanitise.test.mjs:21`, `old-coai-leftovers.test.mjs` (S5 fixtures),
`run-lib-tests.test.mjs:24`.

### 7.2 `scripts/native/native.test.mjs:26` (run by `ci.yml:198`) — owner form

`ROOT` (module level, Linux only) → `before(() => { ROOT = LINUX ? owned.ownedDir(scope, 'native-test') : ''; })`. The name fits the series-root
pattern of `native-run.mjs:24` (`[A-Za-z0-9_-]`). `:112` and `:420` (series names INSIDE `ROOT`) follow the series shape: § 7.4.

### 7.3 The two extension sites reached by `sonarcloud.yml:302` — drop the prefix (contained after § 9)

| Site | New |
|---|---|
| `src_vs_code/src/test/kimiLinuxInstall.test.ts:60` `'testsToDelete-kimi linux install '` | `'kimi linux install '` (keep the trailing SPACE: a path with a space is the test's meaning) |
| `kimiLinuxInstall.test.ts:18` (prose) | "a temporary folder whose name carries a SPACE" |
| `src_vs_code/src/test/runtimeSinceIsAhead.test.ts:147` `'testsToDelete-runtimeSince-'` | `'runtimeSince-'` |

Check each file for a second spelling of the old name (a cleanup, a glob, a `startsWith`); change both sides together.

### 7.4 The native measurement SERIES — WAITS FOR § 12 D-a

`scripts/lib/native-run.mjs:18` (`SERIES_SHAPE`), `:31` (comment); `scripts/native/abort.sh:31`, `make-series.sh:30`, `remove-series.sh:22` (the same
literal; `native.test.mjs` pins all four equal); `scripts/lib/native-run.test.mjs:9`, `:31`, `:32`, `:33`, `:68`, `:69`; `scripts/native/native.test.mjs:112`,
`:420`. A series is a person's multi-phase measurement folder in `/tmp`, made and later removed by root-run shell scripts; it outlives the
script that made it. Neither D10 option fits (an owned folder dies with its process; an owner-form claim of a dead pid is swept 10 minutes later).
**Default if D-a is recorded as recommended:** drop the bare prefix in all of these together: `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}` (and the
test strings follow); the native docs that quote the shape follow.

### 7.5 Person-run probes under `scripts/`

| Site | Container |
|---|---|
| `scripts/dotenv-name-vectors.mjs:133` (removes its scratch at `:143` unless `--scratch` is given) | `given || startOwnedScratch({ what: 'dotenv-vectors' })` path; `:143` becomes the scratch's `end()` (folder, then claim) — owner form |
| `scripts/probe-native-linux-host.mjs:93` (removes at `:109` when it made it) | same, `what: 'native-facts'`; `:109` → `end()` |
| `scripts/probe-kimi-confinement.mjs:132`, `scripts/probe-kimi-tree-hold.mjs:99`, `scripts/probe-vibe-confinement.mjs:92`, `scripts/vibe-home-manifest.mjs:71` | **WAIT FOR § 12 D-b.** They KEEP the default scratch for a later `--reparse`/`--from`. Default if recorded as recommended: `startOwnedScratch({ what: 'kcp' \| 'kimi-mk15' \| 'vcp' \| 'vhm' })` removed at exit, plus one line at the end `scratch was <path>, removed at exit — pass --scratch <dir> to keep it for --reparse`; the usage text says the same |
| `scripts/probe-agy-*.mjs`, `scripts/probe-claude-consultant-confinement.mjs` | NO change: their root is a folder the person names on the command line (`probe-agy-allow-rule.mjs:19`, …), not the machine temp. Name them in the PR inventory. |

The kimi probes' privacy check (`scripts/lib/kimi.mjs:176` `scratchRefusal`) must still pass for the owned scratch; run it once by hand where you can,
else say so.

### 7.6 Inside the extension tests

`src_vs_code/src/test/chatTestsLeaveNoTemp.test.mjs:30` makes the child's private temp with `mkdtempSync(…'testsToDelete-chatleak-')` and runs a
GUARDED test file in it (`:34-37`): after S4 that child would refuse. New: `const privateTemp = owned.ownedDir(t, 'chatleak');` and the child env sets
`TEMP`, `TMP`, `TMPDIR` AND `COAI_TEST_RUN_FOLDER` to it (its claim is beside it, so the guard passes); the `finally` `rmSync` goes (the scope removes).
This line is on S5's eligible list (`PLAN:1225`): S4 takes it (§ 13 P9).

## 8. `release.yml`

| Line | Today | Outcome |
|---|---|---|
| `:307` (`mcp-binaries`, Linux, macOS and Windows runners, `shell: bash`) | `DB=$(mktemp -d)` | `DB=$(mktemp -d "$RUNNER_TEMP/coai-smoke-db.XXXXXX")` — job-scoped. The template form works on GNU, BSD (macOS) and Git-for-Windows `mktemp`; `"$RUNNER_TEMP/…"` in bash on those runners is already used at `:724-726` |
| `:1154` (`bugs-binaries`, Linux) | `COAI_BUGS_DATA="$(mktemp -d)"` | `COAI_BUGS_DATA="$(mktemp -d "$RUNNER_TEMP/coai-bugs-data.XXXXXX")"` (keep the assign-then-export comment `:1152-1155`) |
| `:544` `npm test` | the runner | contained, fails closed (§ 6.2) |
| `:109`, `:121`, `:162`, `:1034` `node .github/scripts/changelog-*.mjs` | make nothing in temp (`git grep -nE 'mkdtemp|tmpdir' -- .github/scripts` finds nothing); write to `$RUNNER_TEMP` via `--out` | no change; named in the PR |
| `:515` pin-check, `:527` rules check, `:537` `node -p` | conventions tools / an expression; no temp (the mount's `tools/pin-check.mjs`, `rules.mjs` and `lib/*.mjs` have no `tmpdir`/`mkdtemp` but in comments) | no change; named |
| C# executables `:264`, `:353`, `:694`, `:1110` | S3's | not S4 |

Check with `actionlint` (CI job "workflows · actionlint"). Only a release run proves `:307`/`:1154` on the runners (§ 14).

## 9. SonarCloud coverage through the runner

`sonarcloud.yml:302` becomes the line in § 3. Why it still covers: c8 12.0.0 (the locked version) sets `process.env.NODE_V8_COVERAGE = argv.tempDirectory`
(`node_modules/c8/bin/c8.js:27`), and `tempDirectory` defaults to `resolve(reportsDir, 'tmp')` — an ABSOLUTE path (`node_modules/c8/lib/parse-args.js:169-171`);
`run-tests.mjs` spawns `node --test` with the inherited environment (`:94-97`, no `env` option), and `node --test` hands it to every file child, so every
test process writes its V8 data there, as today one level deeper. The runner moves TEMP, not `NODE_V8_COVERAGE`. The sonar job mounts the
conventions (`sonarcloud.yml:38-39`), so the wrap loads. **Measure it locally (§ 11 commands)**: the `SF:` set of `lcov.info` before (at the RED commit,
`node --test "out/test/"*.test.js`) and after (`node scripts/run-tests.mjs out/test/*.test.js`); record both counts and the difference in the PR. New entries
for the runner's own modules (`scripts/run-tests.mjs`, `owned.mjs`, `runFolderGuard.cjs`, …) are expected; a LOST entry under `src/` is a defect.

## 10. CI steps

1. `ci.yml` `windows` job (`:377-379`) — replace the direct start with:
   ```yaml
   - name: The Node sweep lists and changes nothing on Windows (F16, Q6); the Node run folder and its refusals
     working-directory: src_vs_code
     run: node scripts/run-tests.mjs src/test/leftoversSweep.test.mjs src/test/runFolder.test.mjs src/test/owned.test.mjs src/test/runTests.test.mjs
   - name: The scripts' run folder and its refusals
     run: node scripts/run-lib-tests.mjs scripts/runFolder.test.mjs
   ```
   No `npm ci`: every file of both import graphs is `node:` or the repository's (L5 extended, § 3). Keep the existing comment, extended.
2. `ci.yml` `macos` job (`:262-327`) — add `actions/setup-node@820762786026740c76f36085b0efc47a31fe5020 # v7.0.0` with `node-version: 22` (as the
   windows job, `:368-371`) after the submodule step, and the SAME two steps (must-not-copy #12's macOS proof: W10's `/var` arm and S2-B's N-S8 macOS
   arm, which no CI leg has run so far). Mind the job's 30-minute budget: these steps take seconds.
3. `ci.yml` `build-test` `:197-198` and `extension` `:448-456`: unchanged commands (they already use the runners).
4. `sonarcloud.yml:302`: § 9.
5. `release.yml`: § 8.

## 11. Order of work, commands, docs

**Commits** (one branch; the PR squash-merges once; follow the branch's message style, e.g. `test(leftovers): RED — E5 S4: …`):

1. `test(leftovers): RED — E5 S4: …` — every test of § 4 and § 5 (WITHOUT the guard import), `run-lib-tests.test.mjs` extensions. Run each new
   file; paste the first failure line of each into the commit body (the RED column says what to expect). Nothing else.
2. `feat(scripts): E5 S4 — the wrap makes the run folder, claimed first by this process; both runners fail closed behind one gate` — `owned.mjs`,
   `runFolderGuard.cjs`, `inRunFolder.cjs`, `ownedRun.mjs`, `run-tests.mjs`, `run-lib-tests.mjs`; delete `runTemp.test.mjs` and
   `shared/owner-claim-vectors.json` (+ the C# comment). § 4.1, 4.2, 4.7, § 5 GREEN.
3. `feat(tests): E5 S4 — every test file refuses outside its run (one import line)` — § 6.5 and § 7.6. § 4.3 GREEN; totals unchanged.
4. `feat(scripts): E5 S4 — every runner and person-run script owns its folder` — § 6.4, § 6.6, `http`, § 7.1-7.3, § 7.5 (rows not waiting).
   § 4.4, § 4.6 GREEN.
5. `ci: E5 S4 — SonarCloud and the Windows step through the runner; the Node proofs on Windows and macOS; release temp in RUNNER_TEMP` — § 8-10. § 4.5 GREEN.
6. `feat(scripts): E5 S4 — …` for § 7.4 and the waiting rows of § 7.5 — ONLY after § 12 records the decisions.
7. `docs: E5 S4 — module_tests.md and module_runners.md: …` — below.

Then the break-its (§ 11.3), the Opus review (rule §1–§3, D1, D3, decision 11, #12, #14, #15 — `PLAN:1085-1086`) and the coai code gate
(`.agents/rules/common/review-gate.md`: the round is fed the SCOPE as well as the diff).

**Commands** (WSL only. P1: no run of the new runners on the Windows side of an unrecorded machine; Windows evidence comes from CI —
`PLAN:1042-1045` for C#, the same barrier logic holds for owner-form Node folders). Before the first local run: a fresh barrier scan as S3's note asks
(`node .agents/conventions/tools/sweeper-scan.mjs --root <the checkouts folder>`, `PLAN:1528-1529`). Every run: lowered, a short PRIVATE temp, a timeout.

```bash
cd <the producer worktree>
export T=/tmp/c4s4 && mkdir -p -m 700 "$T"            # short: socket paths of the editor and of vendor CLIs stay short
export TMPDIR="$T" TEMP="$T" TMP="$T"
npm ci --ignore-scripts --prefix .agents/conventions
node .agents/conventions/tools/rules.mjs check --repo .
(cd src_vs_code && npm ci && nice -n 10 timeout 600 npm run lint && nice -n 10 timeout 600 npm run typecheck)
(cd src_vs_code && nice -n 10 timeout 3600 npm test)                       # whole extension suite through the runner
nice -n 10 timeout 1800 node scripts/run-lib-tests.mjs                      # whole scripts suite
(cd src_vs_code && nice -n 10 timeout 900 node scripts/run-tests.mjs src/test/runFolder.test.mjs src/test/runTests.test.mjs src/test/owned.test.mjs)
nice -n 10 timeout 900 node scripts/run-lib-tests.mjs scripts/runFolder.test.mjs
ls -a "$T"                                                                  # after each whole run: no testsToDelete-* entry, no coai-host-*
# c8, before (RED commit) and after (commit 3+):
(cd src_vs_code && nice -n 10 timeout 3600 ./node_modules/.bin/c8 --reporter=lcovonly --report-dir="$T/c8-before" --src=src node --test "out/test/"*.test.js)
(cd src_vs_code && nice -n 10 timeout 3600 ./node_modules/.bin/c8 --reporter=lcovonly --report-dir="$T/c8-after" --src=src node scripts/run-tests.mjs out/test/*.test.js)
diff <(grep '^SF:' "$T/c8-before/lcov.info" | sort) <(grep '^SF:' "$T/c8-after/lcov.info" | sort)
# the editor host (needs a VS Code download; if the network or xvfb is not there, say so — CI's extension-host job is then the evidence):
(cd src_vs_code && ls -a "$T"; nice -n 10 timeout 1800 xvfb-run -a npm run test:host; ls -a "$T")
# contract and seam (need the .NET builds):
nice -n 10 dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
(cd src_vs_code && nice -n 10 timeout 1800 npm run test:contract && nice -n 10 timeout 1800 npm run test:seam)
# informational: the shipped Node guard on the S4 sites (S6 lands the baseline; here only the legacy-name and owner-blind counts of § 7's files)
node .agents/conventions/tools/leftovers-check.mjs --repo . --profile node --token coai
actionlint    # if installed; else the CI job
```

**11.3 Break-its** (each planted once, seen RED, reverted; quoted in the PR): (B1) remove the `TESTSTODELETE_RUN` withhold ⇒ R2 and W4 red (the
claim's pid is the inherited one); (B2) put back a catch that goes on "as before" ⇒ R3 and P-1 red (sentinel written, exit 0); (B3) skip the gate ⇒
P-3…P-5 red; (B4) remove the guard line from one compiled test ⇒ G5 red naming it, and G2 on that file passes its tests; (B5) let `refusal()` accept a
missing claim ⇒ G1 red; (B6) put `node --test` back in `sonarcloud.yml` ⇒ F1 red; (B7) in `run-host.mjs` call `process.exit` before the `finally` ⇒
H1 red (the run folder left) — #14's RED-GREEN-RED; (B8) in one entry, move `startOwnedRun` after a module-level `mkdtempSync` ⇒ E2 and E3 red;
(B9) in the wrap, `mkdirSync` the folder before `ownedDir` ⇒ W3 red; (B10) write the claim's `<os>` as `process.platform` (`linux` stays, so plant
on a `darwin`/`win32` leg, or plant `'win32'` literally) ⇒ R2 red — #15's RED-GREEN-RED; (B11) make `beginOwnedRun` resolve its root with
`realpathSync` ⇒ W10's `path.dirname(folder) === root as given` red — #12's RED-GREEN-RED.

**Docs.** `research/module_tests.md`: a new dated section after `## E5 S2-B …` (`:6002`), `## E5 S4 — the Node producers wrap the reference: one run
folder per runner and script, claimed first, refused outside it (2026-11-0x)`, with: the wrap's API (`beginOwnedRun`, `ownedScratch`,
`startOwnedRun`), the marker and the guard (K1, K5, K6), the self-test values (K4), the entry table (§ 6.4), the D10 invocation inventory with each row's
outcome (§ 12's table copy), every S5-deferred site with its new container (§ 7), the RED records, the break-its, the c8 `SF:` comparison, the
`test:host` before/after listing, the counts (test files added/removed; totals of `npm test` and `run-lib-tests` before/after each commit), and one
sentence: **every older `node --test …` command quoted in this file now refuses; start the same files with `node scripts/run-tests.mjs <file>`
(in `src_vs_code`, after `npm run compile`) or `node scripts/run-lib-tests.mjs <file>`**. Do NOT rewrite older sections (records are append-only).
`research/module_runners.md`: a dated note on the runner's file arguments and the refusal. `module_tests.md:131` and `:4453` describe CURRENT
commands — add a dated note beside each, do not delete the text.

## 12. Decisions that must be recorded before the waiting work (the main session fills this in)

| Id | Question | Recommended | Recorded |
|---|---|---|---|
| D-a | The native SERIES name (§ 7.4) | drop the bare prefix; shape `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}` | — |
| D-b | The four probes that keep their default scratch (§ 7.5) | owned scratch removed at exit; `--scratch <dir>` to keep it (alternative: default scratch in a git-ignored folder of the checkout, outside temp) | — |
| D-c | run-host: one run folder with three plain sub-folders (K9) instead of three top-level owned folders | as K9 | built as K9 unless overruled |

**The D10 invocation inventory after S4** (copy it into the PR and `module_tests.md`):

| Invocation | Defined at | Temp root after S4 | Outcome |
|---|---|---|---|
| `npm test` → `run-tests.mjs` | `package.json` `test`; `ci.yml:450`, `:456` (package); `release.yml:544` | run folder `…-coai-run-<id>` | contained; fails closed (78) |
| compiled tests through c8 | `sonarcloud.yml:302` | the runner's run folder | contained |
| one source test on Windows | `ci.yml:379` | the runner's run folder | contained |
| `node --test <compiled or source test>`, `node <test>`, an IDE | a person | — | REFUSED by the first import (exit 1 under `--test`, 78 plain) |
| `scripts/**/*.test.mjs` | `ci.yml:198` (`run-lib-tests.mjs`) | run folder `…-coai-run-<id>` | contained; fails closed |
| `node --test scripts/…` | a person; `module_tests.md` text | — | REFUSED |
| `test:host` | `ci.yml:502` (xvfb); a person | run folder `…-coai-host-<id>` | contained |
| `test:contract`, `test:seam`, `test:parity` | `ci.yml:118`, `:125`, `:146` | run folders `contract`, `seam`, `parity` | contained (the four `*.contract.ts` are guarded too) |
| `live-*`, `measure-*`, `render-page` | `package.json`; a person | their own run folder | contained |
| `http/run-contracts.mjs` | a person (`http/`) | run folder `http-contracts` | contained |
| `scripts/live-consult-all.mjs`, `scripts/measure-idle-server.mjs` | a person | their own run folder | contained |
| `scripts/dotenv-name-vectors.mjs`, `scripts/probe-native-linux-host.mjs` | a person | owned scratch (owner form) | contained |
| the four keeping probes | a person | — | D-b |
| `scripts/native/*.sh` | a person (root) | `/tmp` series | D-a |
| `scripts/probe-agy-*.mjs`, `probe-claude-consultant-confinement.mjs` | a person | a folder the person names | not machine temp; S6 decides the `raw-temp` class of their `mkdtemp` lines |
| `release.yml:307`, `:1154` | release | `$RUNNER_TEMP` | job-scoped |

## 13. Plan problems (wrong, contradictory or impossible — not fixed here; each needs a plan entry or an owner answer)

- **P1. "Every direct invocation ... refuses" means EVERY test file.** `PLAN:1153-1162` says "a test file that needs the run folder" checks the marker; which file
  needs it cannot be decided cheaply (a product spawn writes `coai-*` into temp without a raw-temp line). The brief guards all ≈ 550 files plus the four
  `*.contract.ts`. A large mechanical diff; the plan should say so.
- **P2. "The 31 runner scripts (one owned folder each)"** (`PLAN:1094-1096`, `:222-223`): 13 of those files are LIBRARIES — 12 `seam-*.mjs` legs (the plan
  counts 11) and `browserLayout.mjs` — imported by entries and by tests. Containment is per ENTRY (25 in `src_vs_code/scripts`, plus `http/run-contracts.mjs`
  and two in `scripts/`). K8.
- **P3. run-host: the specific paragraph contradicts the generic rule and the goal.** Three top-level owned folders with TEMP unmoved (`PLAN:1107-1111`) leave
  the editor's and the product's temp files in the machine temp; the generic runner rule (`PLAN:1094-1096`) moves TEMP. K9 builds one run folder with three
  sub-folders (D-c).
- **P4. `npm run test:host` "once on Windows"** (`PLAN:1121-1122`) conflicts with P1 of the parent (S3's revised Commands, `PLAN:1042-1045`: Windows
  evidence from CI only until P1 is recorded), and CI has no Windows `test:host` job. S4 cannot produce that evidence; move it after P1 or drop it.
- **P5. The native series (D-a).** The plan's two options (`PLAN:1129-1131`) do not fit a root-run, multi-phase folder that outlives its creator.
- **P6. The four probes that keep their scratch (D-b).** An owned folder removes the evidence a later `--reparse`/`--from` reads; an owner-form claim of a dead
  pid is swept ten minutes later. A behaviour change for a person's tool needs an answer.
- **P7. `mktemp -d -p "$RUNNER_TEMP"`** (`PLAN:1099`): `-p` on the macOS runner's BSD `mktemp` is not verified; the brief uses the template form (§ 8), which
  the repository's own bash steps already rely on. Implementation detail, recorded so the reviewer does not read it as a deviation.
- **P8. `TESTSTODELETE_RUN` is not exported by either coai runner.** The detail says "The top of a run exports its record ONCE as `TESTSTODELETE_RUN`"
  (`common/test-leftovers-detail.md:120`). The C# runner withholds it and does not export it (`RunTemp.cs:204-217`); S4 mirrors that (K2): in coai nothing a child
  makes lies outside the run folder, so no child claim needs the run's record. The parent should confirm this reading or record the deviation.
- **P9. S5's eligible list goes stale through S4.** S4 rewrites `chatTestsLeaveNoTemp.test.mjs:30` (§ 7.6) and deletes `runTemp.test.mjs` (eligible lines
  `:26`, `:29`, `:45`, `:146`, `:149`, `PLAN:1225-1226`); S5's input must drop them. The S6 helper-own lines `run-tests.mjs:106`, `:116`, `:124`, `:125`
  (`PLAN:1252-1253`, `:1356`) are gone after S4 (the legacy writer is deleted); what remains of that kind is the quarantine pattern `run-tests.mjs:166`.
- **P10. Line numbers moved.** `run-tests.mjs` "`:114-209`", "`:210-220`", "`:221-230`" (`PLAN:1090-1092`, `:1154`) are now `:221-327` (S2-B); the D10 table's
  `ci.yml:373`, `:379` for `npm test`/package (`PLAN:149`) are now `:450`, `:456`; the `coai` Tokens row is `detail.md:326`, not `:320` (`PLAN:69`). Harmless; noted.
- **P11. Root `scripts/` raw-temp sites the S4 text does not name.** `scripts/live-consult-all.mjs:25` and `scripts/measure-idle-server.mjs:32` (machine temp)
  are covered only by the Facts row "measurement and probe scripts" (`PLAN:153`); the brief includes them. `probe-agy-*`/`probe-claude-…` use a person-named root,
  but the Node guard flags any `mkdtemp(` (`leftovers-check.mjs:283-291`): S6 must decide them.
- **P12. #12 for Node has never run on macOS.** The `macos` job has no Node (`ci.yml:262-327`); S2-B's N-S8 macOS arm and S4's W10 run nowhere in CI. The brief
  adds a macOS Node step (§ 10.2) — a CI change the plan's S4 text does not list.
- **P13. No guard scans `src_vs_code/scripts/*.mjs`.** The shipped Node profile reads only root `scripts/`, `test/`, `tests/` and `*.test.*` files
  (`leftovers-check.mjs:202-203`); S6's C# guard lists `.github/scripts` and `http` (`PLAN:1296-1298`). The runners' raw temp lines there are inside their run
  folders after S4 but are judged by nothing. For S6 (and F3).

## 14. What only CI (or a release) can prove

- Node 22 (CI) behaviour of an early-exit first import under `node --test` (probed only on Node 26 here).
- Windows: the runner's run folder, fail-closed and the five forced runs (§ 10.1); the guard's env comparison; the leftovers sweep test through the runner (F16
  still list-only).
- macOS: W10's `/var` vs `/private/var` arm, S2-B's N-S8 macOS arm, the proofs (§ 10.2).
- SonarCloud: the lcov produced through the runner and the gate's new-code coverage (§ 9; the local `SF:` diff is the preview).
- `test:host` under xvfb with the run folder (socket paths under the moved TMPDIR) — the extension-host job.
- `release.yml:307`, `:1154`: only a release run (actionlint on the PR checks the syntax).

## 15. NOT in S4

The S5 renames (the eligible list, `PLAN:1219-1236`, less P9's lines); S6 (both guards, the baseline file and its scopes, the `testsToDelete-baseline-proof:`
marker lines in the two proof files, the audit, the exemption inventory); S7 (the `mkdtemp` lines that stay inside run folders); any C# change but the one
comment of § 3; the product's sweep; any change in `dew_flow_conventions` or `dew_flow_mcp`; the F3/F24 pins; the § 7.4 and waiting § 7.5 sites until § 12
records D-a and D-b; any run on the Windows side of an unrecorded machine (P1).

## 16. Definition of Done (S4)

- [ ] Every § 4 and § 5 test RED at commit 1 (first failure line quoted) and GREEN after; the break-its of § 11.3 seen red and reverted; #12, #14, #15
      RED-GREEN-RED recorded.
- [ ] `beginRun`'s fallback gone: no folder or no claim ⇒ one line, exit 78, no `node --test` spawned (both runners); the claim written first by the reference,
      naming this process; `TESTSTODELETE_RUN` withheld and restored.
- [ ] Every test file's first import is the guard (G5 green); a direct start refuses (G2–G4); `sonarcloud.yml:302` and `ci.yml:379` through the runner; c8's
      `SF:` set compared and recorded.
- [ ] Every § 6.4 entry starts its owned run first (E2, E3); `run-host` leaves nothing and passes `--user-data-dir` (H1, H2).
- [ ] Every § 7 site handled (or waiting on § 12 and named so), each in the PR with its invocation and new container; the Node guard's `legacy-name` and
      `owner-blind` counts for § 7's files before/after in the PR.
- [ ] `release.yml` uses `$RUNNER_TEMP` (F3 test); the other release Node lines listed.
- [ ] The mount missing ⇒ exit 2 with the sentence, for the runners and the entries (W12).
- [ ] No Node file outside `src_vs_code/scripts/owned.mjs` calls `ownedFor` (W2).
- [ ] `npm test`, `run-lib-tests`, lint, typecheck green locally (WSL); the private temp empty after each whole run; CI green on `build-test`, `windows`,
      `macos`, `extension`, `extension-host`, actionlint, SonarCloud.
- [ ] Counts: test files added/removed listed; totals before/after each commit; commit 3 changes no total.
- [ ] `module_tests.md` and `module_runners.md` per § 11; the D10 inventory (§ 12) in the PR.
- [ ] Opus review and the coai code gate reached `proceed`.

## 16. Decisions recorded at the parent's epoch-5 re-review (2026-11-04) — THEY WIN over §§ 2, 6, 7, 12 and 13 where they differ

The parent (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md` § *Epoch 5*, approved by the owner 2026-11-04) and
E5's stories document (coai #945, every `*[epoch 5, 2026-11-04]*` note in § S4, D8, D10 and the DoD) are the source; read
them first. In short:

- **K2 is REVERSED (P8).** `run-tests.mjs`, `run-lib-tests.mjs` and every runner/entry script EXPORT their own record as
  `TESTSTODELETE_RUN` to their children (the detail `common/test-leftovers-detail.md:120`). A runner withholds an
  INHERITED value only around its own run folder's claim (the claim names THIS process), then exports its own. A child
  test process is a LEAF of its runner's run scope: its `ownedDir` folders are made inside the run folder and their
  claims name the run; it makes no run folder. Tests: the claim of a nested runner names its own pid; a leaf's claim
  names the run's record. K1 (the marker `COAI_TEST_RUN_FOLDER`) is unchanged.
- **K9 is REPLACED (D-c).** `run-host.mjs` gets the generic runner folder (`what` `host`, TEMP/TMP/TMPDIR and the marker
  moved there) AND, inside it, its three OWNED folders from the wrap, `what` = `host-ws`, `host-data`, `host-profile`, each
  with its claim; `--user-data-dir=<host-profile>`; the `try/finally` before exit. Both texts as written. Check VS Code's
  IPC socket path length with the deeper `--user-data-dir` (creds E2.1 hit macOS's 103-byte limit); if too long, keep the
  profile folder short and say how in the PR.
- **K10 / D-b.** The four probes that keep their scratch (`probe-kimi-confinement.mjs`, `probe-kimi-tree-hold.mjs`,
  `probe-vibe-confinement.mjs`, `vibe-home-manifest.mjs`) put their DEFAULT scratch in an owned folder of the wrap, removed
  at exit, and print one line `scratch was <path>, removed at exit — pass --scratch <dir> to keep it for --reparse`; the
  existing `--scratch <dir>` is unchanged (a folder the person names). `scratchRefusal` (kimi) must still pass.
- **§ 7.4 / D-a — the native measurement series is the ONE bounded exception.** The name becomes
  `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}` (no prefix, no owner, no claim) in `scripts/lib/native-run.mjs:18`
  (`SERIES_SHAPE`, `seriesOf`, `mkdirArgv`, `removeArgv`, …), `scripts/native/make-series.sh`, `remove-series.sh`,
  `abort.sh`, their tests and the native documents (the script headers, `research/module_tests.md`, and the dated notes
  already in `todo/PLAN_kimi_and_vibe_on_native_linux.md` and `_E1.md`). The LEAD creates it (`mkdirArgv`), `make-series.sh`
  fills it, `remove-series.sh` removes it; nothing collects it. Evidence, ALL in this commit, each named in the PR record:
  (1) survival between phases (a private root swept in between); (2) NEW `remove-series.sh` refusal tests — old prefix,
  wrong kind, date width, upper-case hex, separator in the leaf, outside the root — plus the JS `seriesOf` cases moved to
  the new shape; `LC_ALL=C` set in the scripts before every `=~` shape test; (3) the guard's reading of the series lines
  (S6 lists the series' line and the old-prefix FIXTURE in the expected-exemption inventory if the guard flags them);
  (4) SAFETY — the CALLER checks with `stat` over ssh BEFORE it runs anything from a series folder (`removeArgv`,
  `abortArgv`, the phase launcher): the folder and `run/` are directories, not symlinks, uid 0, mode 700; the script it
  runs is a regular file, uid 0, not group/world-writable; the series root is uid 0 and sticky when world-writable; any
  mismatch refuses with one line and runs nothing; unit tests with a stubbed stat result; the copy's own checks stay
  (defence in depth: also refuse a folder not uid 0 or group/world-writable, and a non-sticky world-writable root); the
  expected uid is the test seam `COAI_NATIVE_SERIES_OWNER_UID` (default 0) beside `COAI_NATIVE_SERIES_ROOT`, so CI runs
  the tests as its own user; (5) a phase whose series is gone stops with one line (the series `<path>` is gone; start a
  new one with `make-series.sh`) and a non-zero exit, before it writes anything; (6) the SIZE measured FIRST, before the
  rename (a real series made by `native.test.mjs` with a full phase, `du -sh`, plus the native plan's projection "under
  150 MB"), written in this brief's PR record and the native documents; (7) the documents say what the series is, who
  removes it, how a forgotten one is found (`ls -d <root>/native-*`), that a reboot or a tmp cleaner ends it, the bound,
  and that a series in progress under the OLD name is finished with the old checkout.
- **P1.** Every test file (not only those "that need the run folder") imports the guard as its FIRST static import;
  imported helpers must not create anything at load (check them; move any into a hook and name it).
- **P4.** `npm run test:host` once on a deliberately started hosted `windows-latest` run (throwaway branch or targeted job),
  counts of `coai-host-*` and `testsToDelete-*` before/after, recorded before the runner is discarded. ALSO (H): S2-B's
  `janitor.sh --token coai --dry-run` under Git for Windows' bash on a hosted `windows-latest` run, recorded — an open
  item of the producer PR (the same throwaway run may carry both).
- **P12.** A targeted macOS Node CI step for must-not-copy #12 (the wrap's `isMine` with `/var` → `/private/var`).
- **P2.** The real count: 25 entry scripts + 13 library modules = 38 files (the plan's "31" is wrong); only entries own folders.
- **P13** stays for S6 (no guard reads `src_vs_code/scripts/*.mjs`).
- **Merge condition (c)** for the WSL machine is still open (one administrator scan); it gates the PR's MERGE, not this build.
