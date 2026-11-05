# Build brief — coai E5 story S6: the two guards, the audit, the `raw-temp` baseline, the CI legs (the last commits of the producer PR)

> Status: **plan only, nothing implemented yet; revised 2026-11-05 by Opus after the owner approved epoch 6. The decisions are taken (below). A Sonnet coder builds from this brief alone. § 4 step 0 lists the preconditions.**

First written 2026-11-05 by Opus at head `524df50e`. Revised at head `2c339086` of branch `feat/test-leftovers-e5-producer` in `<the producer worktree>`: `main` is merged and the pin is `bd795847` (F3, F24 and #130). Every count below was measured again at `e8355d2b` with the guard at the pin. `git diff --name-only 524df50e 2c339086` holds only `.agents/conventions` and files under `todo/`, so no code line moved and the numbers of the first version stand. § 3 corrects one number that was wrong before (`raw-temp` 649).

The plan is `todo/PLAN_test_leftovers_E5.md`. Its operative text for S6 is § *S6*, "Current statement". The parent is `dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`, § *Epoch 6*, with entry 6.1 of its ledger (the parent's branch `docs/rollout-stop-epoch5`, PR #132). This brief cites both by section name, because their line numbers move. The template is `dew_flow_mcp` at `197cc76af7c3c1d28f50e57533fa99d34682466d`.

You (the coder) build from this brief alone. If this brief and the plan differ, § 2 says so and says why. If you find a difference that this brief does not name, stop and report it to the coordinator. Do not write around it. **Epoch 6 has used its one change (entry 6.1). The next accepted change is a full stop of the whole plan.** So a row that does not fit, or a test that cannot do what this brief says, goes to the coordinator at once.

---

## The way of working: each condition, its implementation, its invocation, its check

The parent's § *Epoch 6*, item 8 makes this table the opening of every brief. It maps each merge condition of D8 (as read for epoch 6) and each test the plan promises to the file, the invocation and the command. I read each row against the tree at the pin. No row is empty.

`M` is `./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests`. `S` is `./src_server/tests/bin/Release/net10.0/CoaiServer.Tests`. `B` is `./src_bugs/tests/bin/Release/net10.0/CoaiBugs.Tests`. `H` is `./src_bench/CoaiBench.Tests/bin/Release/net10.0/CoaiBench.Tests`. `R` is `cd src_vs_code && node scripts/run-tests.mjs`.

| Condition or promise | Implementation (file) | Invocations it covers | Executable check | State at this head |
|---|---|---|---|---|
| (a) S2-A on `main` | #940 `d02ed40e` | both runners | the characterization test of S2-A, green on `main` | done |
| (b) the repository-wide checks | `src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs`, `src_vs_code/src/test/testLeftovers.test.mjs` | `CoaiMcp.Tests` in `build-test`, `macos`, `windows`, `sonarcloud.yml`, `release.yml` `mcp-binaries`; `run-tests.mjs` in `extension`, `macos`, `windows` | `M --filter-class "*LeftoversRepositoryGuardTests"`; `R src/test/testLeftovers.test.mjs`; the plants of § 13 | to build |
| (b) the inventory equals the guard's list, and each row holds its site's hash | `shared/leftovers-exemptions.json`, `src_mcp/tests/Leftovers/LeftoversInventory.cs`, N3 of § 7.4 | as above | `M --filter-class "*LeftoversInventoryTests"`; `R src/test/testLeftovers.test.mjs`; R10 | to build |
| (b) each `raw-temp` row is proved by its kind | § 10's column "proved by"; E9 and E10 in `src_vs_code/src/test/runnerScripts.test.mjs`; `RunTemp.RequireChildTempInRunFolder` called in the four test classes of kind 5 | `npm test`; the server and bugs suites | `R src/test/runnerScripts.test.mjs`; `S --filter-class "*TheImageChecksEachInstallerTests"`; `S --filter-class "*TheImageInstallsAgyFromItsManifestTests"`; `S --filter-class "*TheCanaryFallbackRunsTests"`; `B --filter-class "*TheRootHelperFramesTwoRecordsTests"` | to build |
| (c) the barrier, per machine | `research/security/machines.md`; the Windows machine is met by the owner's statement (#124) | the WSL machine | `sudo node .agents/conventions/tools/sweeper-scan.mjs --system` once, its output in the record | **open, the owner's; it gates the merge, not this build** |
| (d) the legacy clean-up, per machine | `scripts/old-coai-leftovers.mjs` (S2-A's tool) | a person, per machine | `node scripts/old-coai-leftovers.mjs list`, its output on the PR | WSL: `0 entries in /tmp` on 2026-11-05 (§ 18) |
| (e) the gates and the three legs | the coai sessions; `.github/workflows/ci.yml` | every job | `proceed` on both gates; every job green on the head | the code gate comes at S6's end |
| (f), (g), (i) the rule, F3 and F24 pinned | the gitlink at `bd795847` | the guard with `--baseline`, `--profile node` | `node .agents/conventions/tools/rules.mjs check --repo .`; `node .agents/conventions/tools/pin-check.mjs` | done |
| (h) every other condition | the PR description: one evidence line per row of this table | the PR | a reviewer reads it; a row with no evidence blocks the merge | the main session |
| (j) the adapted copy's diff check | `src_mcp/tests_leftovers/template-manifest.json`, `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs` | the `Leftovers` filter, three legs | `M --filter-class "*TheAdaptedCopyTests"`, 52 entries after S6 | green with 40 entries |
| (k) the suite × OS gates | `ci.yml` steps | each cell | each cell green on the head | green; S6 adds its steps (§ 12) |
| (l) F24's second acceptance check | § 16; `LeftoversBaselineTests.TheProofCheck_ClosesForEachScope` (new code of S6) | the four C# scopes through the test; the two Node scopes through the released guard | the record on #945; `M --filter-method "*TheProofCheck_ClosesForEachScope*"`: 4 `proof missing`, then 0 | Node half and forced runs recorded on #945; C# half to build |
| (m) D11's rollback text | the PR description | a reviewer | a reviewer reads it | **open, the main session** |
| (n) the native series' evidence | S4 on #945; S6 adds item (3): no finding, no row | the Linux leg | the S4 tests green; the PR record names the seven items | built; the record is the main session's |
| (o) the audit reads the run folder before the run ends | `template-manifest.json`, the fourth op of `LeftoversSweepFixture.cs`; `src_mcp/tests_leftovers/RunTemp.cs`, `RunTempFixture.cs`; the plant in each of four suites (§ 11) | the four C# executables | `M`, `S`, `B`, `H`, each `--filter-method "*AnOrphanClaim_IsReportedByTheAudit*"`; R16 | to build, RED first |
| (p) the smoke script's trap and name | `src_server/smoke-image.sh`, `src_server/tests/TheImageSmokeUsesTheServerTests.cs` (§ 9.4) | the test with a stub on PATH; `server-image.yml:88`; a person | `S --filter-class "*TheImageSmokeUsesTheServerTests"`; R19 | to build, RED first |
| (q) the baseline's final content | by hand, § 12.4 | the creation PR's last head, in a full clone | the commands of § 12.4, exit 0, recorded on the PR; both wrappers green in that head's CI run | to do last |
| Must-not-copy #1, the audit's sibling | the template's test; `LeftoversCoaiLayoutTests.TwoChildSuitesUnderOneRun_TheFirstToFinish_LeavesTheSecondsFolder` | the `Leftovers` filter | R4 | to build |
| Must-not-copy #2, C# | the template's `BreakIt` rows | the `Leftovers` filter | R2 | to build |
| Must-not-copy #2, Node half as narrowed | N8 of § 7.4 | `npm test`; `macos`, `windows` file lists | `R src/test/testLeftovers.test.mjs`; R15, by hand, once | to build |
| The audit self-test, both values | § 12's three steps | `build-test`, `macos`, `windows` | the steps' run ids; R6 | to build |
| The baseline: multiset, second pass, moved line | `src_mcp/tests/Leftovers/LeftoversBaseline.cs`, `LeftoversBaselineTests.cs`, N7 | the `Leftovers` filter; `npm test` | R8, R9, R13 | to build |
| The baseline only shrinks; the one creation; no flag left | § 12.4; `workflowsStartTestsThroughARunner.test.mjs` | `build-test` on later PRs; one temporary run on #945 | N6, B7; `R src/test/workflowsStartTestsThroughARunner.test.mjs`; the run id on the PR | to build |
| The baseline is found from both folders | `LeftoversCoaiLayoutTests.TheBaselineIsFound_FromBothPlaces` | the `Leftovers` filter | R12 | to build |
| One planted site per root; break-its | by hand on the real tree | the roots of § 13 | § 13, § 15 | to do |

**Row (d) in full.** The parent's item 3 says the condition stands. The record is per machine and on the PR. WSL's record is in § 18. The owner's Windows machine does not build or test coai, by the owner's statement.

## Decisions taken

The first version of this brief held five questions. They are closed. **Who decided: the coordinator, on the consultants' advice; the owner approved the narrowing (2026-11-05, the parent's § *Epoch 6*, item 7).**

| # | Decision | What it means here |
|---|---|---|
| 1 | Option C: strike the `.mjs` half of row 2 as written | No coai-own folding reader. § 7.4's `foldedPrefix.mjs` and rows R3 and N8 of the first version are gone. N8 is now the positive test of the parent's item 4.1, with one sentence of what the guard does not detect. F30 owns the rest (date 2026-11-19, accepted by the owner). |
| 2 | Inline exemptions for the lines no scope can carry, with the kinds of reason of item 4.2 and entry 6.1 | § 10. `src_vs_code/scripts/` stays in the guard. **The inventory's count at creation binds. 77 `raw-temp` rows is a ceiling, not an allowance.** A row only goes away after that. |
| 3 | The standing shrink step skips PR 945 by number, and one temporary commit does the creation, as written. Added: (q) at the last head | § 12.4 |
| 4 | "The guard's proof check for each scope" means the guard the scope names, as written | § 7.2, § 16 |
| 5 | Replaced. `smoke-image.sh` moves its trap before `mktemp` and takes an owner-form name | § 9.4. The first version's reason "its EXIT trap removes this folder" is withdrawn. |

**Entry 6.1 (the coordinator, 2026-11-05).** Tracing § 10 showed that four kinds did not fit 11 rows. The answers: kind 4's standing test keeps "names" and holds a pinned list of pairs (§ 10.6). A fifth kind covers a line a test reaches whose product caller is not a test run, with a required pin (§ 10.7). `smoke-image.sh:73` is the goal's second bounded exception and is not a kind. `TempDir.cs:35` is kind 1 in its C# form.

---

## 0. Read first (in this order)

1. `AGENTS.md`, `.agents/conventions/ENTRY.md`, `.agents/PROJECT.md`; then
   `node .agents/conventions/tools/rules.mjs check --repo .` and
   `node .agents/conventions/tools/rules.mjs explain --repo . --task implement --task test --task docs --file src_mcp/tests_leftovers/LeftoversGuard.cs --file src_vs_code/src/test/testLeftovers.test.mjs --file raw-temp-baseline.json --file .github/workflows/ci.yml`,
   and `read` every selected rule (`common/test-leftovers.md`, `common/test-leftovers-detail.md`, `common/test-leftovers-baseline.md`,
   `common/local-cpu-budget.md`, the coai review-gate rule, `csharp/dotnet-build.md`, and the others it names).
2. The plan: § *S6*, "Current statement", in full; D2, D6, D8 with its note "D8 read for epoch 6" and conditions (o), (p), (q), D9, D10, D11; § *Must not copy back in*, rows 1 and 2 with row 2's note; the epoch-6 box of § *Definition of Done*. Read them from the file.
3. This branch's work: the manifest `src_mcp/tests_leftovers/template-manifest.json` and its check
   `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs`; the fixture `src_mcp/tests_leftovers/LeftoversSweepFixture.cs`; the audit
   `src_mcp/tests_leftovers/LeftoversAudit.cs`; `src_mcp/tests_leftovers/RunTemp.cs`, `RunTempFolder.cs`, `RunTempFixture.cs`,
   `TempDir.cs`; the proof files of § 8.2; `research/module_tests.md` § *E5 S5* (`:6318`); the coordinator's record of § 16 on #945.
4. The template at `197cc76`, read with `git -C <dew_flow_mcp checkout> show 197cc76:tests/Mcp.Tests/Leftovers/<name>.cs`, the 12
   files of § 6.1, the template's `LeftoversRepositoryGuardTests.cs` for its shape, and its CI `git -C <dew_flow_mcp checkout> show 197cc76:.github/workflows/ci.yml`, lines 48-71, 103-133, 186-210.
5. The guard at the pin: `.agents/conventions/tools/leftovers-check.mjs`, `tools/lib/raw-temp-baseline.mjs`.

## 1. Goal

One guard per language keeps every tree of the repository so: red on every class of rule §7 outside the helper, red on an
unregistered token, red on a scan of nothing. `raw-temp` is ratcheted by the ONE baseline file the released rule authorises. The
end-of-run audit is on, bounded to the run, and it runs BEFORE the run folder ends. The self-tests run on the three legs. After S6, on the whole repository:
`owner-blind`, `legacy-name`, `exemption`, `token`, `docker-*`, `shared-store`, `ryuk-off` are at zero; `raw-worktree` and every
inline exemption are exactly the inventory, each row with its site's hash; and the only `raw-temp` lines not exempted are the baseline's.
On Node, `legacy-name` and `owner-blind` at zero mean zero WHOLE literals, F30 open.

S6 adds no producer, changes no sweep, and drains nothing (that is S7).

## 2. What is current in the plan, and why

The plan's § *S6*, "Current statement", decides. This table repairs what that statement leaves to the brief. None of these is a change.

| Point | Text I read as current | Why |
|---|---|---|
| The baseline's form | `common/test-leftovers-baseline.md` at the pin: `{ class, scopes[{root,proof,guard}], entries[{file,sha256,line}] }`, one entry per occurrence, **no `count`** | The statement and the rule say so. |
| One file | `raw-temp-baseline.json` at the repository root | The statement. The rule reads only that path. |
| The proof | F24 at the pin: the marker, the TEMP assertion and the five child runs IN the proof file; no order | The statement, D8 (l). |
| The wrapper | coai's `LeftoversRepositoryGuardTests.cs` calls `Scan`, then applies the baseline | D2's "an ADAPTED copy" for `LeftoversGuard.cs`; § 7.2 says how it applies the baseline. |
| Commit place | S6 is the fifth and last part of the PR: S3 → S2-B → S4 → S5 → S6 | The statement. The order inside S6 follows the parent's item 5 (§ 4 below). |
| The inventory's row | `file`, `line`, `sha256`, `class`, `guards`, `proof`. **`line` is the SITE's line, and `sha256` is the hash of the SITE's trimmed line** | The statement's Files bullet still says "the marker's line" beside "the sha256 of its SITE's trimmed line". The coordinator's instruction and the parent's item 4.7.4 decide: the site. The marker sits on the line above, so its line is `line - 1`. A stale reference, repaired here. |
| Helper-own lines | Only lines that a guard flags get a marker. `startOfRun.mjs:19`, `:28` of the statement are gone (S2-B); `run-tests.mjs` flags only `:103`, a read | Measured (§ 3). Lines move, and the PR re-reads each one. |
| The native series | No row: neither guard flags `make-series.sh` (§ 3). The old-prefix fixture has a row | The statement says "if the guard classes" it. |
| The count "29" | 23 `worktree add` lines: 19 real and 4 that only name the words | Measured. The statement gives both numbers. |
| The exclusion list | Binds the **C#** guard only, as an op on `ExcludedFiles`. The Node guard has no exclusion option and reads every `*.sh`, so those 11 files' lines take markers (§ 10) | The statement, entry 6.1. "The guard's own fixtures" need no entry: 12 copied files scanned, 0 findings. |
| Read-only roots | `GetTempPath`/`tmpdir` reads are migrated, not baselined (§ 9.3). Node: per FILE, as the plan's word "imports" says | The statement. |
| `src_vs_code/scripts/` | The C# guard reads this folder | P13; the statement's Files bullet. |
| Product C# | Test trees only. Coai has no root `src/`, so the template's `Source` kind matches nothing and stays in the text. With product trees mapped to `Source`: 2 more `owner-blind` hits on real words (`CandidatePath.cs:39`, `EndCauses.cs:46`). Not S6. | The statement's Files bullet. |
| The command | `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "the multi-model review extension and server"` | The statement. |
| `TheProofCheck_ClosesForEachScope` | New code of S6, in `LeftoversBaselineTests` (§ 16) | The statement, D8. |

## 3. Facts measured (at `e8355d2b`, 2026-11-05). Verify what you use

**How.** The Node guard: `.agents/conventions/tools/leftovers-check.mjs` at the pin, `--profile node`, no baseline (627 files discovered). The C# guard: the 8 template files of § 6.1 with the ops of § 6.3, the frozen list included, in a scratch console project outside the repository, run on this tree (1114 files scanned).

| What | Node guard, `--profile node`, no baseline | C# guard, with the frozen list |
|---|---|---|
| `raw-temp` | **252**: `src_vs_code/src/test/` 224, `scripts/` 16, `deploy/**` 9, `src_server/*.sh` 3 | **638**: `src_mcp/tests/` 487, `src_server/tests/` 58, `src_bugs/tests/` 26, `src_bench/CoaiBench.Tests/` 8, `src_vs_code/scripts/` 54, `.github/workflows/release.yml` 2, `src_server/smoke-image.sh` 1, `src_mcp/tests_leftovers/` 1, `src_mcp/tests_fakecli/` 1 |
| `legacy-name` | 3 (`scripts/lib/old-coai-leftovers.mjs:35`, `:37`, `:139`) | 0 |
| `owner-blind` | 0 | 7, all "a piece of the prefix word" in a literal (§ 9.1) |
| `raw-worktree` | 0 | 23 (§ 10.2) |
| `shared-store` | 0 | 2 (§ 10.3) |
| `docker-run` | 0 | 1 (§ 10.3) |
| `token`, `exemption`, `docker-name`, `ryuk-off` | 0 | 0 |

The first version said C# `raw-temp` 649. That figure did not apply the frozen list (638 + 11). With the list, as you build it, the count is 638.

**The inventory, re-measured: 112 rows, of which 77 are `raw-temp`** (§ 10). **The baseline, re-measured: 776 entries**, 547 C# and 229 Node (§ 8.1). Each row of § 10's tables and each line of § 9.3's C# list is at the stated line with the stated class, except the rows that § 9.2 or an existing marker changes.

**The markers on disk today: 5.** `scripts/lib/old-coai-leftovers.test.mjs:26`, `:28`, `:98`, `src_vs_code/src/test/leftoversSweep.test.mjs:57`, `src_mcp/tests_leftovers/Owned.cs:119`. Each sits on the line above its site.

**`--print-baseline` at the pin.** It reads no `.cs` and prints `scopes: []`. Under `--profile node` it lists 252 entries today; without `--profile node`, 12 (`deploy/`, `src_server/`). It has one use left: the Node half of the one creation (§ 8.3, step 1). It is not the (q) check.

The 12 template files of § 6.1, with their folders repaired, scanned in a scratch tree: 12 files read, 0 findings. None of them holds a whole marker; their fixtures build it in pieces.

**How a run finds `yaml`.** The shipped guard needs `yaml@2` only when the tree has a workflow; coai has. It looks in two places
(`YAML_RESOLVERS`, `leftovers-check.mjs:179-182`): first `createRequire(<the guard's own file>)("yaml")`, which walks
`.agents/conventions/tools/node_modules`, then `.agents/conventions/node_modules`; then `createRequire(<repo>/noop.js)("yaml")`,
which walks the repository's root and up. coai has no root `package.json`, so the FIRST place is the one that works: **the mount
needs `npm ci --ignore-scripts --prefix .agents/conventions`**, in CI and locally. Measured with no `node_modules` in a mount: exit 2 and one sentence,
"the `yaml` package (version 2) is needed to read workflows and cannot be found — add `yaml@2` as a devDependency of the repository
being scanned". For coai that sentence's cure is wrong; the cure is the `npm ci` above. Do NOT add a root `package.json`.

| Job | Has the mount's `npm ci` today |
|---|---|
| `ci.yml` `build-test` | yes, `:38` |
| `ci.yml` `extension` | yes, `:442` (this job runs `npm test`, so it runs the Node guard) |
| `ci.yml` `macos`, `windows` | no; S6 adds it (§ 12) |
| `sonarcloud.yml` `sonar` | yes, `:45` |
| `release.yml` `extension` | yes, `:691` |

**The classifier needs node and the mount, not `npm ci`.** Measured: `tools/lib/sweeper-scan.mjs` and `tools/leftovers-check.mjs`
import with no `node_modules` present. So the C# guard (`GuardScripts`) needs only `node` on PATH and the submodule.
`release.yml` `mcp-binaries` has the mount (`:254`) and no `setup-node`; it runs the whole mcp suite (`:274`) on a hosted image's
own `node`. `OwnerClaimVectorsTests.cs:52` already starts `node` there, so the guard adds no new need. Release evidence only.

**The proof files.** All six hold the TEMP assertion and the five forced child runs, today, without the marker:

| Scope root | Proof file | TEMP assertion | Folder / claim / each variable |
|---|---|---|---|
| `src_mcp/tests/` | `src_mcp/tests/RunTempTests.cs` | `:63`, `:67`, `:70` | `:260`, `:381`, the theory `:391-395` |
| `src_server/tests/` | `src_server/tests/RunFolderTests.cs` | `:62` | `:104`, `:115`, the theory `:125-129` |
| `src_bugs/tests/` | `src_bugs/tests/RunFolderTests.cs` | the same lines; the file differs from the server's only in its namespace (measured with `diff`) | the same |
| `src_bench/CoaiBench.Tests/` | `src_bench/CoaiBench.Tests/RunFolderTests.cs` | the same | the same |
| `src_vs_code/src/test/` | `src_vs_code/src/test/runFolder.test.mjs` | `:27` P-T | `:70` P-1, `:78` P-2, `:85` P-3 to P-5 |
| `scripts/` | `scripts/runFolder.test.mjs` | `:26` P-T | `:68` P-1, `:76` P-2, `:83` P-3 to P-5 |

So the three other C# suites are NOT a gap. Their `Main` is the same gated one (`TestEntryPoint.cs`, differing only in the namespace).
Note what the matrix proves: bugs runs on Linux only and bench not on Windows (D8's table), and that is accepted.

**What a guard does not see.**
- `scripts/native/make-series.sh`: no finding in either guard. So no series row in the inventory; say so in the PR (S4's evidence item 3).
- The 8 `.teststodelete-quarantine-` lines of `scripts/lib/old-coai-leftovers.test.mjs` (`:200`, `:254`, `:355`, `:414`, `:468`, `:587`, `:721`,
  `:729`), `src_vs_code/scripts/run-tests.mjs:166` and `src_vs_code/scripts/startOfRun.mjs:25`: no finding in either guard. They take no
  marker; list them in the PR by name (§ 10.5).
- The bare constant `const OLD_PREFIX = '<prefix>-';` in a `.mjs`: no finding in the Node guard (§ 9.2 does not depend on this).

**The shipped applier can judge a `csharp` scope.** Measured in a private git repository: `loadBaselineAt(repo)` +
`createApplier(loaded, repo, BASELINE_FILE, new Set(discovered), "csharp")` + `take(file, siteSha256(line))` + `finalFindings()`
passes a key's first occurrence, leaves the second, and reports `proof missing` when the proof is not discovered. § 7.2 uses it.

## 4. Order of the work

Commit early: a restart empties `/tmp`. Add commits, amend nothing that is pushed. Do not push; the coordinator decides when a CI run may start. Every build with `-m:4` and lowered (`nice -n 19`; `node .agents/conventions/tools/low-priority.mjs`), one run at a time.

The order follows the parent's item 5.

| Step | What | Commit |
|---|---|---|
| 0 | Preconditions: `git -C .agents/conventions rev-parse HEAD` starts `bd79584`; `rules.mjs check` is clean; `npm ci --ignore-scripts --prefix .agents/conventions`. Read the record of § 16 on #945. Re-measure § 3 with the pinned guard. A number that moved goes in `module_tests.md` with why. | none |
| 1 | The smoke script: § 9.4. RED (R19), then the move. | `test(leftovers): RED — E5 S6: the smoke script leaves its folder on an early failure`, then `fix(tests): E5 S6 — the smoke script's trap before mktemp, the owner-form name` |
| 2 | The audit: § 11. The copy of `LeftoversAuditTests.cs` and `LeftoversSelfTest.cs` and their manifest entries; the plant in the four suites; RED (R16) with the audit wired and the old end of the run; then the ONE ordered call. Fix each leak the audit finds in the test that made it. Add the audit steps of § 12. | `test(leftovers): RED — E5 S6: an orphan claim is not seen`, then `feat(leftovers): E5 S6 — the ONE ordered call; the audit is on, on the three legs` |
| 3 | RED: the guard's copy (§ 6), the wrapper, the Node wrapper, their tests, the pins of kind 5, E9 and E10. Run `*LeftoversRepositoryGuardTests*`: record the counts per class. | `test(leftovers): RED — E5 S6: the guards and their wrappers` |
| 4 | Site work: § 9.1, § 9.2, § 9.3. | `feat(tests): E5 S6 — the owner-blind literals, the two fixtures, the read-only roots` |
| 5 | The inline exemptions of § 10, the pair list and the inventory file, written last. | `feat(tests): E5 S6 — the named exemptions and their inventory` |
| 6 | The rest of § 12, except the shrink step. | `ci: E5 S6 — the legs` |
| 7 | The proof markers (§ 8.2). F24's C# half (§ 16), recorded, **before** step 8. | `docs: E5 S6 — F24's second acceptance check, the C# half` |
| 8 | The baseline (§ 8.3), in its own commit, then the shrink step and the creation run (§ 12.4). | `feat(leftovers): E5 S6 — the raw-temp baseline, one file, six scopes` |
| 9 | The planted violations (§ 13), RED-GREEN-RED (§ 14), break-its (§ 15); all reverted; `module_tests.md`; the PR record. | `docs: E5 S6 — the record` |
| 10 | The main session, at the LAST head: (q), § 12.4. Any new commit after it means run it again. | none: a PR comment |

## 5. Files

New:

| File | What |
|---|---|
| `src_mcp/tests_leftovers/LeftoversGuard.cs`, `LeftoversGuardDocker.cs`, `LeftoversGuardExemptions.cs`, `LeftoversGuardFiles.cs`, `LeftoversGuardLiterals.cs`, `LeftoversGuardScripts.cs`, `LeftoversGuardText.cs`, `LeftoversGuardTokens.cs` | The guard, copied with the ops of § 6.3. No `[Fact]`, so the library (D2). |
| `src_mcp/tests/Leftovers/LeftoversGuardTests.cs`, `LeftoversGuardScriptsTests.cs`, `LeftoversAuditTests.cs`, `LeftoversSelfTest.cs` | Their tests, copied (§ 6.4). |
| `src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs` | **coai's own**, not the template's (§ 7.1). It holds the same class name as the template's one-assertion test, so `--filter-class` works alike. |
| `src_mcp/tests/Leftovers/LeftoversBaseline.cs` | coai's own: applies the baseline through the shipped library (§ 7.2). |
| `src_mcp/tests/Leftovers/LeftoversInventory.cs` | coai's own: compares the markers with the inventory (§ 7.3). |
| `src_mcp/tests/Leftovers/LeftoversBaselineTests.cs` | coai's own (§ 14), with **`TheProofCheck_ClosesForEachScope`, new code of S6 (§ 16)**. |
| `src_mcp/tests/Leftovers/LeftoversInventoryTests.cs`, `LeftoversCoaiLayoutTests.cs` | coai's own tests (§ 7.3, § 14). |
| `src_vs_code/src/test/testLeftovers.test.mjs` | The Node wrapper (§ 7.4), with the positive detection test N8. First import: `import '../../scripts/inRunFolder.cjs';`. |
| `src_vs_code/src/test/tempRoot.ts` | One export, the one read of the temp root for two `.ts` tests (§ 9.3). The name is S7's (the plan's § *S7*). |
| `shared/leftovers-exemptions.json` | The inventory (§ 10). |
| `shared/candidate-path-vectors.json` | The four paths of § 9.1. |
| `raw-temp-baseline.json` | The baseline, at the repository root, a regular file (§ 8). |

Changed:

| File | Change |
|---|---|
| `src_mcp/tests_leftovers/template-manifest.json` | 12 new entries; 1 changed op, the fourth of `LeftoversSweepFixture.cs` (§ 6, § 11.1). |
| `src_mcp/tests_leftovers/LeftoversSweepFixture.cs` | That op's text, in the copy (§ 11.1). |
| `.gitattributes` | One `-text` line for each of the 12 new copied files, by name, beside the others. `TheAdaptedCopyTests` compares raw bytes. Without these lines a Windows checkout fails the check. Never a glob: the coai-owned files keep the normal rule. |
| `src_mcp/tests_leftovers/RunTemp.cs` | `RegistryWithoutTheRunFolder()` (§ 11.2), `RequireChildTempInRunFolder` (§ 10.7), its comment. coai's own file. |
| `src_mcp/tests_leftovers/RunTempFixture.cs` | `Dispose` and `IDisposable` removed (§ 11.2). coai's own file. |
| `src_mcp/tests/RunTempChildren.cs`, `src_mcp/tests/RunTempTests.cs`, and `RunFolderTests.cs` of `src_server/tests`, `src_bugs/tests`, `src_bench/CoaiBench.Tests` | The orphan-claim plant, per suite (§ 11.4). |
| `src_server/smoke-image.sh`, `src_server/tests/TheImageSmokeUsesTheServerTests.cs` | The trap move, the owner-form name, RED first (§ 9.4). |
| `src_vs_code/src/test/runnerScripts.test.mjs` | E9 and E10 (§ 7.5). |
| `src_server/tests/TheImageChecksEachInstallerTests.cs`, `TheImageInstallsAgyFromItsManifestTests.cs`, `TheCanaryFallbackRunsTests.cs`, `src_bugs/tests/ShellScript.cs` | The pin of kind 5 (§ 10.7). |
| `src_mcp/tests/ClaudeConsultantArgvTests.cs` | One comment reworded (§ 10.4). |
| `src_vs_code/src/test/workflowsStartTestsThroughARunner.test.mjs` | `no workflow holds the creation flag` (§ 12.4). |
| `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs` | Add to `CoaiOwn`: `LeftoversRepositoryGuardTests.cs`, `LeftoversBaseline.cs`, `LeftoversInventory.cs`, `LeftoversBaselineTests.cs`, `LeftoversInventoryTests.cs`, `LeftoversCoaiLayoutTests.cs`. |
| the six proof files | One marker line each (§ 8.2). |
| the files of § 9 and § 10 | As listed there. |
| `.github/workflows/ci.yml` | § 12. |
| `.github/workflows/release.yml` | Two comment lines (§ 10.4). |
| `research/module_tests.md` | § *E5 S6*, appended. Never trim an older section. |
| `todo/README.md` | The index line of this brief exists and says "to be revised". The revision touched no other file, by instruction, so the first S6 commit (or the coordinator) changes that status. |

Not changed: any file in `.agents/conventions` or `dew_flow_mcp`; product code; `sonarcloud.yml` (it already has node, the mount and `npm ci`, and
runs both wrappers through the suites; check that with the first CI run); the `.csproj` files (an SDK glob takes the new `.cs` files); `.slnx`.

## 6. The C# guard's adaptation, as manifest ops

The template wins. Every difference is one exact-replace op with its `count`, and a `reason` when it is more than a path. The check
is `TheAdaptedCopyTests`: it undoes the ops on the raw bytes, compares the git blob SHA, and applies them forward again.

### 6.1 The new manifest entries

`repository` and `commit` stay. `blob` is the output of `git -C <dew_flow_mcp checkout> rev-parse 197cc76:<upstream>`; compare it with this table.

| Upstream, under `tests/Mcp.Tests/Leftovers/` | Target | Blob |
|---|---|---|
| `LeftoversGuard.cs` | `src_mcp/tests_leftovers/LeftoversGuard.cs` | `8a92a79b032c5d1849d30048c49115f5bcfc0612` |
| `LeftoversGuardDocker.cs` | `src_mcp/tests_leftovers/LeftoversGuardDocker.cs` | `4c686d18a1a1f0f6410a8083549a4d82f3ce6483` |
| `LeftoversGuardExemptions.cs` | `src_mcp/tests_leftovers/LeftoversGuardExemptions.cs` | `156fe549cf1012b626e7e8a78149671b18df0f5b` |
| `LeftoversGuardFiles.cs` | `src_mcp/tests_leftovers/LeftoversGuardFiles.cs` | `bd5549c5420b934f9b015d291b656a824a8c3566` |
| `LeftoversGuardLiterals.cs` | `src_mcp/tests_leftovers/LeftoversGuardLiterals.cs` | `f1df96b7d192b676cb945f2a1e77c558176a7069` |
| `LeftoversGuardScripts.cs` | `src_mcp/tests_leftovers/LeftoversGuardScripts.cs` | `c8b3340af5d9f99fb351e2a0ccfee0467d1700e8` |
| `LeftoversGuardText.cs` | `src_mcp/tests_leftovers/LeftoversGuardText.cs` | `85de20435e978094c49a009b266f4f509d4dd86d` |
| `LeftoversGuardTokens.cs` | `src_mcp/tests_leftovers/LeftoversGuardTokens.cs` | `0691387bc91feb81228623e4fbefeb2147708a38` |
| `LeftoversGuardTests.cs` | `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` | `943e36d598ff4e3c1defead061baa9d9a63293f3` |
| `LeftoversGuardScriptsTests.cs` | `src_mcp/tests/Leftovers/LeftoversGuardScriptsTests.cs` | `9726f4fe8eef95bc865fa220a69024b70ac26f77` |
| `LeftoversAuditTests.cs` | `src_mcp/tests/Leftovers/LeftoversAuditTests.cs` | `72d421d227bd667fb230bdb0ecd3b67897148db0` |
| `LeftoversSelfTest.cs` | `src_mcp/tests/Leftovers/LeftoversSelfTest.cs` | `b1ebd5592ad3da828f1dae58d444b73ff2580c79` |

`LeftoversRepositoryGuardTests.cs` (`956f18ff…`) is NOT copied: D2 makes that file coai's own. With it the template's 51 files are all accounted for: 38 + 12 in the manifest, 1 replaced.

### 6.2 Every entry

The first op of every new entry: `namespace Mcp.Tests.Leftovers;` → `namespace CoaiMcp.Tests.Leftovers;`, count 1. Files with no other op: `LeftoversGuardDocker.cs`,
`LeftoversGuardExemptions.cs`, `LeftoversGuardLiterals.cs`, `LeftoversGuardText.cs`, `LeftoversGuardScriptsTests.cs`.

### 6.3 The guard's ops (line numbers are the template's)

| File and line | Old | New | Reason |
|---|---|---|---|
| `LeftoversGuard.cs:45` | `ExcludedFiles { get; } = [];` | the frozen list of § 6.5 | the plan's frozen list. Also op the doc comment `:43-44`, which says "none". |
| `:47` | `HelperFile = "tests/Mcp.Tests/Leftovers/Owned.cs"` | `"src_mcp/tests_leftovers/Owned.cs"` | coai's layout (D2) |
| `:48` | `private const string HelperFolder = "tests/Mcp.Tests/Leftovers/";` | `private static readonly string[] HelperFolders = ["src_mcp/tests_leftovers/", "src_mcp/tests/Leftovers/"];` | coai has TWO helper folders (D2) |
| `:63-65` | the three allowances | `"src_mcp/tests_leftovers/LeftoversGuard.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardTests.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardScriptsTests.cs"` | coai's layout. **The list stays at four files.** |
| `:280` | `source.Relative.StartsWith(HelperFolder, StringComparison.Ordinal)` | `HelperFolders.Any(folder => source.Relative.StartsWith(folder, StringComparison.Ordinal))` | follows `:48` |
| `LeftoversGuardFiles.cs:28` | `["bin", "obj", "node_modules", ".git", ".claude", ".http-artifacts", "logs"]` | add `".agents"` after `".claude"` | the mount is at `.agents/conventions` |
| `:31` | `ScriptFolders = [".github/scripts/", "http/"]` | add `"src_vs_code/scripts/"` | P13, Decision 2 |
| `:63-64` | `relative.StartsWith("tests/", …) ? SourceKind.Tests : relative.StartsWith("src/", …) ? SourceKind.Source : null` | `TestRoots.Any(root => relative.StartsWith(root, StringComparison.Ordinal)) ? SourceKind.Tests : relative.StartsWith("src/", StringComparison.Ordinal) ? SourceKind.Source : null` and, above `:28`, `private static readonly string[] TestRoots = ["tests/", "src_mcp/tests/", "src_mcp/tests_leftovers/", "src_mcp/tests_fakecli/", "src_server/tests/", "src_bugs/tests/", "src_bench/CoaiBench.Tests/"];` | The roots of the statement's Files bullet. `tests/` and `src/` stay, so the template's ~120 fixture rows at `tests/X/A.cs` and `src/Y/Y.cs` stay byte-identical. Neither folder exists in coai. |
| `LeftoversGuardScripts.cs:20` | `.claude/rules/shared/tools/lib/sweeper-scan.mjs` | `.agents/conventions/tools/lib/sweeper-scan.mjs` | the mount |
| `LeftoversGuardTokens.cs:11` | `TokenFile = "tests/Mcp.Tests/Leftovers/OwnedToken.cs"` | `"src_mcp/tests_leftovers/OwnedToken.cs"` | layout |
| `:98` | `Path.Combine(repoRoot, ".claude", "rules", "shared")` | `Path.Combine(repoRoot, ".agents", "conventions")` | the mount |
| `:126` | `git submodule update --init .claude/rules/shared` | `git submodule update --init .agents/conventions` | the sentence of the break-it (§ 15) |

Read each line before you write the op; the op's `old` is the exact bytes. The exact-count rule of the check catches a wrong one.

### 6.4 The tests' ops

| File and line | Change |
|---|---|
| `LeftoversAuditTests.cs:220`, `:229`, `:230`, `:247`, `:268`, `:273` | `mcp tests:` → `coai tests:` |
| `:223` | `bash .claude/rules/shared/tools/janitor.sh --token mcp --dry-run` → `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` |
| `LeftoversSelfTest.cs:5`, `:14` | `MCP_LEFTOVERS_SELFTEST` → `COAI_LEFTOVERS_SELFTEST` (count 2) |
| `LeftoversGuardTests.cs:125`, `:164` | `tests/Mcp.Tests/Leftovers/Owned.cs` → `src_mcp/tests_leftovers/Owned.cs` |
| `:129`, `:180` | `tests/Mcp.Tests/Leftovers/X.cs` → `src_mcp/tests_leftovers/X.cs` |
| `:165` | `tests/Mcp.Tests/Leftovers/LeftoversGuardTests.cs` → `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` |
| `:166` | `tests/Mcp.Tests/Leftovers/LeftoversSweep.cs` → `src_mcp/tests_leftovers/LeftoversSweep.cs` |
| `:264` | `.claude/rules/shared/tools/x.sh` → `.agents/conventions/tools/x.sh` |
| `:299` | `tests/Mcp.Tests/Leftovers/LeftoversGuard.cs` → `src_mcp/tests_leftovers/LeftoversGuard.cs` |
| `:350`, `:352`, `:474`, `:557`, `:607` | `".claude", "rules", "shared"` → `".agents", "conventions"` |
| `:394` | `RepositoryIdentity.Row` → `OwnedToken.RepoRow` |
| `:579` | `.claude/rules/shared/tools/leftovers-check.mjs` → `.agents/conventions/tools/leftovers-check.mjs` |
| `:621` | `TokenFile = "tests/Mcp.Tests/Leftovers/OwnedToken.cs"` → `"src_mcp/tests_leftovers/OwnedToken.cs"` |
| `:649-652` | The body names two folders: `const string lib = "src_mcp/tests_leftovers/"; const string own = "src_mcp/tests/Leftovers/";` and `Equal(lib + "Owned.cs", lib + "LeftoversGuard.cs", own + "LeftoversGuardTests.cs", own + "LeftoversGuardScriptsTests.cs")`. |
| `:658` | `LeftoversGuard.ExcludedFiles.Should().BeEmpty();` → `.Should().Equal(<the list of § 6.5>)`, so the list is frozen. The method name `No_file_is_excluded_…` stays: renaming it is not needed and an op is a cost. |
| `:659` | `tests/Mcp.Tests/Leftovers/LeftoversGuardTests.cs` → `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` |

The fixture strings that hold `mcp` as a token (`:389`, `:400`, `:412-443`, `:460-530`, `:626-641`) stay: they are pure inputs to a function. The file's fixtures `{P}-u1000-mcp-…` stay too. A test that reads the REAL mount or the REAL helper is what the ops above change.

### 6.5 The frozen exclusion list

Exact paths, as `Read` and `IsScannable` compare with `Contains`. Eleven files. A comment beside each group gives the reason.

```csharp
public static IReadOnlyList<string> ExcludedFiles { get; } =
[
    // product deploy scripts, run on a server by a person; no test or run folder reaches them
    "deploy/backup.sh", "deploy/bugs/deploy-cmd.sh", "deploy/bugs/first-key.sh", "deploy/bugs/helper-protocol.sh", "deploy/bugs/install-env.sh",
    "deploy/bugs/release.sh", "deploy/coai-deploy-cmd.sh", "deploy/systemd-release.sh", "deploy/update.sh",
    // product installers, run while the image is built
    "src_server/install-agy.sh", "src_server/install-cli.sh",
];
```

`deploy/**` of the plan is written as exact paths so that a NEW script under `deploy/` is read and judged. **This list binds the C# guard only.** The shipped Node guard reads these 11 files anyway and reports `raw-temp` on their lines (measured, exit 1), so those lines take markers (§ 10.4).

### 6.6 Formatting, and a check that does not apply

The copied files keep the template's bytes, so they must pass `dotnet format --verify-no-changes` as the S3 copies do. If `Formatted` (`ci.yml:48-49`) is red on a copied file, report it. Do not reformat a copy.

`TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests` reads every `src_mcp/tests/Leftovers/*.cs` for the temp APIs. Your new files must not spell them. Its summary says S6's guard supersedes it; it stays.

## 7. The wrappers

### 7.1 `LeftoversRepositoryGuardTests.cs` (coai's own)

```csharp
[Fact] public void This_repository_passes_the_guard()
```
1. `var report = LeftoversGuard.Scan(RepositoryRoot.Path, RepositoryRoot.Path);`
2. `report.FilesScanned` is above 0; the failure text is `report.Describe(root)`.
3. Every root of the scan has at least one file: use `GuardFiles.Discover(root)` (internal; the library has `InternalsVisibleTo` for this project). The roots: the six of `TestRoots` that exist, `.github/workflows/`, `.github/scripts/`, `http/`, `src_vs_code/scripts/`. A root with no file fails with `scanned nothing under <root> — discovery broke; the repository is not clean`.
4. `LeftoversBaseline.Apply(report, root, root)`: § 7.2.
5. The rest is empty; the failure message lists each as `file:line — class — text`.
6. `LeftoversGuard.RegisteredTokenFindings(root, OwnedToken.RepoToken, OwnedToken.RepoRow)` and `ConstantFindings(root, OwnedToken.RepoToken)` are empty.
7. `LeftoversInventory.Differences(root)` is empty: § 7.3.

This test needs the mount and `node`. A missing one is red, with the guard's own sentence. Never skip.

### 7.2 `LeftoversBaseline.cs`: no port

The C# guard's baseline is not a port of the rule. It starts `node` ONCE, as `GuardScripts` does, and calls the shipped `tools/lib/raw-temp-baseline.mjs` with `guard = "csharp"`. Reason: two implementations of a ratchet drift, and the hash must be the shipped `siteSha256` (JavaScript's `trim` and C#'s `Trim` differ on U+FEFF and U+0085). Same shape as the template's `GuardScripts`: nothing interpolated into the program; paths go in `process.argv`; the request goes on stdin.

The program (a raw string in the `.cs` file):

```js
import { readFileSync } from "node:fs";
import { join } from "node:path";
const [module, repo] = process.argv.slice(1);
const { BASELINE_FILE, createApplier, loadBaselineAt, locateBaseline, siteSha256 } = await import(module);
let input = "";
for await (const chunk of process.stdin) input += chunk;
const request = JSON.parse(input);
const lines = new Map();
const lineOf = (file, number) => {
  if (!lines.has(file)) lines.set(file, readFileSync(join(repo, file), "utf8").split(/\r?\n/));
  return lines.get(file)[number - 1];
};
let answer = { left: request.hits, findings: [] };
if (!locateBaseline(repo, { gitRoot: true }).absent) {
  const applier = createApplier(loadBaselineAt(repo), repo, BASELINE_FILE, new Set(request.discovered), "csharp");
  const left = request.hits.filter((hit) => !applier.take(hit.file, siteSha256(lineOf(hit.file, hit.line))));
  answer = { left, findings: applier.finalFindings() };
}
answer.hashes = (request.sites ?? []).map((site) => siteSha256(lineOf(site.file, site.line) ?? "")); // § 7.3: the inventory's site hashes, from the shipped function
console.log(JSON.stringify(answer)); // no process.exit: on Windows a pipe is written asynchronously, and an early exit can cut the answer
```

- `module` is the file URL of `<mount>/.agents/conventions/tools/lib/raw-temp-baseline.mjs`, as `GuardScripts.StartInfo` makes its URL.
- `request.discovered` is `GuardFiles.Discover(root).Select(file => file.Relative)`.
- `request.hits` is every `raw-temp` finding of the report, in the report's order (file ordinal, then line). That is "in line order" of the rule.
- `Apply` returns: every non-`raw-temp` finding; each hit in `left` as its original `raw-temp` finding; each item of `findings` as `new Finding(file, line, "baseline", text)`.
- A non-zero exit, no `node`, or an answer that is not this JSON is ONE finding `baseline` naming the cause. Fail closed, never an exception, never a pass. Exit code 1 with an unhandled throw (a usage error of `loadBaselineAt`: a folder at the path, a run not at the git root) lands here.

- The request goes on stdin, never in the arguments: `discovered` has more than 1000 paths, and a Windows command line holds 32 KB. Set `RedirectStandardInput`, write the JSON, close the stream, and drain stdout and stderr at the same time as `GuardScripts.Execute` does, with its two-minute bound.
- No baseline file yet (before step 8): the `absent` branch answers "no baseline", so every hit is left. After step 8 that branch is never taken; `The_baseline_file_exists` in § 14 pins it. `loadBaselineAt` alone would throw `ENOENT`.

**Windows.** `locateBaseline` compares `git rev-parse --show-toplevel` with the given folder by real path. A private fixture repository under `C:\Users\RUNNER~1\…` may spell the two differently. So a fixture never passes its own folder: it asks git and passes the answer (`git -C <folder> rev-parse --show-toplevel`, slashes as git prints them). The real repository's root has no short name on the hosted runner; the `windows` job's `Test` step proves it.

### 7.3 `LeftoversInventory.cs`

The statement's contract: the markers on disk equal the inventory, exactly, and a line cannot change under its marker. The file is `shared/leftovers-exemptions.json`:

```json
{
  "about": "Every inline test-leftovers exemption of this repository: S6's expected-exemption inventory (todo/PLAN_test_leftovers_E5.md). A new marker, a removed one, a moved line or a changed site is a visible diff here.",
  "rows": [
    { "file": "deploy/bugs/release.sh", "line": 59, "sha256": "<64 hex>", "class": "raw-temp", "guards": ["node"], "proof": "unreached" }
  ],
  "namedBy": [
    { "file": "deploy/bugs/release.sh", "testFile": "src_vs_code/src/test/install.test.ts", "why": "a comment names it; no test runs it" }
  ]
}
```

| Field | Meaning |
|---|---|
| `file` | Repository-relative, forward slashes. |
| `line` | **The SITE's line**, the line the guard flags. The first version said the marker's line. The marker is on `line - 1`. |
| `sha256` | **The hash of the SITE's trimmed line**, from the shipped `siteSha256` (`tools/lib/raw-temp-baseline.mjs`), as a baseline entry holds it. Never the marker's line. |
| `class` | The class the marker names. |
| `guards` | The guards that read the file: `csharp`, `node`, or both. |
| `proof` | Only on `raw-temp` rows: `entry`, `library`, `read`, `unreached`, `reached` or `bounded-exception` (§ 10). E9, E10 and the count read it. |
| `namedBy` | The pinned list of § 10.6. The C# comparer ignores it. |

The reason is not repeated: it is in the marker.

**How a marker finds its site.** `GuardExemptions.Read` and `readExemptions` give the marker's line `M`. All markers in this tree stand on their own line, so the site is `M + 1`. A marker that shares a line with code has the site `M`: the comparer tells the two apart by whether the text before the comment is empty.

**The hash in C#.** Never hash in C#: JavaScript's `trim` and C#'s `Trim` differ on U+FEFF and U+0085. `LeftoversInventory` sends the sites to the node program of § 7.2 (`sites`) and reads `hashes` back.

- C#: for each file of `GuardFiles.Discover(root)`, `GuardExemptions.Read(file, GuardText.SplitLines(text), out var bad)`, giving (file, site line, class) for each valid marker. Compare with the rows whose `guards` holds `csharp`, and compare the hashes. One line per difference: `in the tree, not in the inventory: …`, `in the inventory, not in the tree: …`, or `the site changed under its marker: <file>:<line> — the inventory holds <a>, the line hashes to <b>`.
- Node (§ 7.4): the same with `discover(repo, 'node')`, `readExemptions(file, lines)` and `siteSha256`, all exported at the pin.
- A row whose file is absent is a difference in both. A row of a file its named guard does not discover is a difference.
- Read a file as its guard does: the Node side skips a path that is gone and a file with a NUL byte in its first 8 KiB (`scanTexts`, `isBinary`).

The markers in `.sh` and `.cs` files are read by the same grammar in both guards: `common/test-leftovers-detail.md` § 7.

**The count binds.** `LeftoversInventoryTests.TheRawTempRows_AreNotMoreThanAtCreation` holds one constant, `RawTempRowsAtCreation`, set to the count at creation (77 if no row changes). It asserts `rows with class raw-temp <= RawTempRowsAtCreation`. Lower the constant when a row goes. Never raise it: a raise is a plan change. The test only makes a raise a visible diff; the review refuses it.

Expect this test to be red whenever a line above a marker moves. That is the plan's "visible diff". The message says what to update. A site whose text changes is red too, and that is the point.

### 7.4 `testLeftovers.test.mjs`

First import `inRunFolder.cjs`. It runs under `run-tests.mjs` (compiled tests are not involved; a `.mjs` in `src/test/` runs from source).

```
const repo = git rev-parse --show-toplevel, from this file's folder
const guard = join(repo, '.agents/conventions/tools/leftovers-check.mjs')
const IDENTITY = 'the multi-model review extension and server'
```

| Test | What it asserts |
|---|---|
| N1 `the shipped guard is clean on this tree` | `spawnSync(node, [guard, '--repo', repo, '--baseline', '--profile', 'node', '--token', 'coai', '--identity', IDENTITY])`: exit 0. On exit 1 or 2 the assertion message is the tool's own stdout and stderr, whole. |
| N2 `exit 1 and exit 2 are red with the tool's sentence` | The test's helper, on a private git repository: a planted `mkdtempSync` in `scripts/x.mjs` ⇒ exit 1 naming `scripts/x.mjs:1`; a repository with no scanned file ⇒ exit 2 with "scanned nothing". |
| N3 `the Node half of the inventory` | § 7.3, the hashes included. |
| N4 `every root has a file` | `discover(repo, 'node')` holds at least one file under `src_vs_code/src/test/`, `scripts/` and `.github/workflows/`. |
| N5 `the token is a registered row and written once` | On private repositories, through the exported `scanRepository`: the real mount and `coai` ⇒ no `token` finding; a C# `RepoToken = "coai"` beside a Node `REPO_TOKEN = 'other'` ⇒ the finding "token constants differ" naming both (measured: 2 `token` findings in all, the second says `other` is no row; the plan's "⇒ 1" is the first); the token `Coai` ⇒ one. Import the guard in the test's own process; a program that passes the guard's path as `process.argv[1]` starts its `main`. |
| N6 `the baseline only shrinks` | A private repository with a committed baseline, through `--baseline-shrinks --base HEAD`: a new key, a raised count, a new scope, a changed proof, a changed guard ⇒ exit 1, each with its sentence; a removed entry ⇒ exit 0; no `--allow-create` ⇒ a creation is exit 1. |
| N7 `the baseline's second pass` | A private repository with a `node` scope: a second identical line ⇒ `raw-temp`; an entry swapped under the same total ⇒ a `raw-temp` and a `baseline`; a stale entry ⇒ `baseline`; a proof with no marker ⇒ `baseline` naming "proof missing"; a moved line ⇒ clean. |
| N8 `what the shipped guard detects in a .mjs` (row 2's Node half, narrowed) | The exported `scanText` on the exact fixture lines below. Each line answers exactly the classes shown. |

**N8's fixture lines.** Measured at the pin with `scanText('scripts/zz.mjs', line, [], noYaml)`, where `noYaml` is a function that throws. The answer's class field is `cls`.

| The line (`PREFIX` is `testsToDelete-`) | Classes |
|---|---|
| `` `const name = '${PREFIX}coai-x-abc123';` `` | `['legacy-name']` |
| `` `for (const f of fs.globSync('${PREFIX}*')) rmSync(f, { recursive: true });` `` | `['owner-blind']` |
| `` `const name = '${PREFIX}u1000-coai-x-abc123';` `` | `[]` |

Take `PREFIX` from the mount: `import { PREFIX } from '../../../.agents/conventions/tools/lib/owned.mjs';`. So the file holds no whole marker or literal, and the Node guard's read of this file stays clean.

**Pin what is detected, never what is not.** The guard's answer depends on the whole line (F30's note: a `?` glob with `rmSync` on the line is `owner-blind`, and with no removal it is `[]`). That is why the test names exact lines. When F30 ships, a spelling that is `[]` today becomes a finding, and this test must stay green. So no row asserts `[]` for a split spelling.

**The one sentence for the PR, as written:** "The shipped Node guard finds a whole legacy literal and, on the lines F30 names, a bare-word glob or an anchored regex over the word; it does not find a word split over `+`, `.concat`, `.join('')`, a template hole, a `\u` or `\x` escape, a one-character class or half of the word; the count depends on the line (F30's note), so this repository's zero for `legacy-name` and `owner-blind` is zero WHOLE literals, F30 open."

**No assumption that the Node guard folds.** No test, no reason and no sentence in this PR may read as if it does.

All private repositories come from `owned.ownedDir(t, 'guard')`, inside the run folder, with `git init`. Pass `--repo` the answer of `git rev-parse --show-toplevel`, never the folder as you built it: the guard compares the two by real path, and on the hosted Windows runner the temp path holds a short name (`RUNNER~1`). `test` names and comments are free.

### 7.5 The two standing tests: E9 and E10, in `runnerScripts.test.mjs`

Both read `shared/leftovers-exemptions.json`. Neither holds a file name of a row as a literal, so the file names no fourth-kind file. Both use `git ls-files -z` from the repository's root.

**E9 `an importer of an exempted library is an entry with the wrap or a test under the runner`.** Beside E1. For each distinct `file` of the rows with `proof: "library"`:

1. Its base name is `L`. Search each tracked `.mjs`, `.cjs`, `.js`, `.ts`, `.mts`, `.cts` outside `.agents/` and other than `L` itself, with this regex: ``(?:\bfrom\s+|\bimport\s*\(\s*|\bimport\s+|\brequire\s*\(\s*)['"`][^'"`]*/<L, escaped>['"`]``.
2. An importer passes when it is in `ENTRIES` (E1 and E2 prove its wrap), or when its name ends `.test.<ext>` and its first `import` line holds `inRunFolder.cjs`.
3. Any other importer fails, with the file and the library named: "one caller outside a run folder makes the line a finding, not an exemption".
4. A library with no importer also fails: the row has nothing left to prove.

Measured on today's tree with this search: 13 libraries; `browserLayout.mjs` has the four entries `measure-answer-code-layout`, `measure-failure-layout`, `measure-help-clicks`, `render-page`; each `seam-*.mjs` has `run-seam.mjs`; `seam-ledger-fields.mjs` also has `ledgerFieldsSeam.test.mjs`; `seam-refusal.mjs` and `seam-security-text.mjs` also have `seamLegs.test.mjs`. All pass. `git grep` finds no workflow, manifest or document that starts a library by its name.

**E10 `no test file names a fourth-kind file, but the pinned pairs`.** For each distinct `file` of the rows with `proof: "unreached"`:

1. The test files are the tracked files that match `\.(test|spec)\.[a-z]+$`, or that lie under `src_mcp/tests/`, `src_mcp/tests_leftovers/`, `src_mcp/tests_fakecli/`, `src_server/tests/`, `src_bugs/tests/`, `src_bench/CoaiBench.Tests/` or `src_vs_code/src/test/` (1726 files today).
2. A test file names the file when its text matches the base name with no name character before it: `(?<![\w.-])<base name, escaped>`. This keeps `release.sh` from matching `systemd-release.sh`.
3. The set of (file, testFile) found must EQUAL the `namedBy` list. A pair not on the list is red: a person must read that test. A pair on the list that no longer exists is red too.
4. Every `namedBy` row has a non-empty `why`. Every `namedBy.file` has at least one `unreached` row.

**The limit, stated:** a new spawn inside a test file already on the list is not seen. E10 reads names, not what a test does with a name. So the PR says that the list holds this limit, and a reviewer of any change to a listed test reads for a new start of the file.

**RED for each** (R21, R22).

## 8. The baseline

### 8.1 The file

ONE file, `raw-temp-baseline.json` at the repository root, a regular file, never a link. The schema is the rule's. Six scopes, each with its proof and its guard:

```json
{
  "about": "raw-temp sites inside the owned run folder; only shrinks — common/test-leftovers-detail.md §7",
  "class": "raw-temp",
  "scopes": [
    { "root": "src_mcp/tests/", "proof": "src_mcp/tests/RunTempTests.cs", "guard": "csharp" },
    { "root": "src_server/tests/", "proof": "src_server/tests/RunFolderTests.cs", "guard": "csharp" },
    { "root": "src_bugs/tests/", "proof": "src_bugs/tests/RunFolderTests.cs", "guard": "csharp" },
    { "root": "src_bench/CoaiBench.Tests/", "proof": "src_bench/CoaiBench.Tests/RunFolderTests.cs", "guard": "csharp" },
    { "root": "src_vs_code/src/test/", "proof": "src_vs_code/src/test/runFolder.test.mjs", "guard": "node" },
    { "root": "scripts/", "proof": "scripts/runFolder.test.mjs", "guard": "node" }
  ],
  "entries": [ { "file": "…", "sha256": "…", "line": 1 } ]
}
```

| Scope | Expected entries after § 9 and § 10 | Today | Taken out |
|---|---|---|---|
| `src_mcp/tests/` | **456** | 487 | 31 read-only sites (§ 9.3) |
| `src_server/tests/` | **57** | 58 | 1 read-only site |
| `src_bugs/tests/` | **26** | 26 | |
| `src_bench/CoaiBench.Tests/` | **8** | 8 | |
| `src_vs_code/src/test/` | **219** | 224 | 5 read-only sites |
| `scripts/` | **10** | 16 | 6 inline (§ 10.4) |
| **Total** | **776** | 819 | |

Re-measured at `e8355d2b`: the same numbers. A number that differs after a later merge of `main` is not wrong by itself. Explain each difference by file in the PR. An entry outside these six roots is impossible: the guard reports it.

`csharp` scopes are valid here: none has a root under `scripts/`, `test/` or `tests/` at the repository root (measured: `src_mcp/tests/` is not `tests/`), so the Node guard does not report them.

### 8.2 The proof marker

One line in each proof file, a comment, in this exact form (the root, then nothing else):

- `src_mcp/tests/RunTempTests.cs`: `// testsToDelete-baseline-proof: src_mcp/tests/`, directly above the class's `/// <summary>`.
- `src_server/tests/RunFolderTests.cs`, `src_bugs/tests/RunFolderTests.cs`, `src_bench/CoaiBench.Tests/RunFolderTests.cs`: the same place, each with its own root (`src_server/tests/`, `src_bugs/tests/`, `src_bench/CoaiBench.Tests/`).
- `src_vs_code/src/test/runFolder.test.mjs`: `// testsToDelete-baseline-proof: src_vs_code/src/test/`, directly above the file's `/**` header, below the imports (the guard import stays first).
- `scripts/runFolder.test.mjs`: `// testsToDelete-baseline-proof: scripts/`, the same place.

A comment is skipped by both guards' line classes. Measured for the Node guard with this exact line: no finding. `declaredRoot` needs the root right after the marker and only whitespace to the end of the line.

Measured with a scratch baseline (the record on #945): no marker ⇒ 2 `proof missing`, a marker ⇒ 0, only under `--profile node`. Without `--profile node` the proof files are not discovered at all, so **every Node guard command in this brief has `--profile node`**.

### 8.3 Generated once, never again

The rule: "No command regenerates the file; the one writer is a person, in a reviewed diff that removes lines." So the writer is a throwaway. **Nothing you use here is committed.**

Write these in your scratch directory with the Write tool (a Bash heredoc that quotes a build line is stopped by a hook):

1. `node .agents/conventions/tools/leftovers-check.mjs --repo . --print-baseline --profile node --token coai --identity "the multi-model review extension and server"`: stdout is `{ scopes: [], entries }` for the Node sites. This is the one use `--print-baseline` has left. It reads no `.cs`, and it never checks a later head (§ 12.4). Expect 229 after §§ 9 and 10 (252 today). It lists every `raw-temp` finding of the scan, so if a file outside the two Node roots appears, a marker of § 10 is missing. Stop and fix it.
2. A scratch console project with a `ProjectReference` to `src_mcp/tests_leftovers/CoaiMcp.Tests.Leftovers.csproj`: `LeftoversGuard.Scan(root, root)`, and print `file<TAB>line` for each `raw-temp` finding. Expect 547. A file outside the four C# roots: stop and fix it.
3. A scratch Node script: import `siteSha256` from the mount, and for each line of (2) read that line of the file and print `{ file, sha256, line }`. The hash is the shipped function, so it is the same one the wrapper uses.
4. Merge: `about`, `class`, the six scopes of § 8.1, and all entries sorted by `file` (ordinal), then `line`. One entry per line of the JSON, so that S7's diffs remove whole lines.
5. Check: `node …/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "…"` exits 0, and `*LeftoversRepositoryGuardTests` is green.
6. Delete the scratch project and scripts. Write the commands in the PR and in `module_tests.md`, so that a reader sees how it was made.

**After this commit no line is added.** A later edit of a baselined line removes its entry and moves the site to the helper; it never re-keys. Everything in § 9 and § 10 comes first because of this.

## 9. The site work, before the baseline

### 9.1 The seven `owner-blind` lines: rewritten, because the class takes no exemption

The C# guard reads a literal that holds a piece of the prefix word as `owner-blind` (`PieceHits`, the folded reader). These seven are real words. Rule §7 and the statement's checks: such a line is rewritten or lives in a helper's own file. The list of files that may spell pieces (`FoldingAllowances`) stays at four.

| Site | The piece | The rewrite |
|---|---|---|
| `src_mcp/tests/Leftovers/StartOfRunSweepRaceAndRootTests.cs:330` | `TestSt` in `"*SweepChild_…_WhenTheProcessTestStartsIt"` | `"*" + nameof(SweepChild_SweepsTheGivenRootOnce_WhenTheProcessTestStartsIt)`. No rename, so `research/module_tests.md:6309` stays true. coai's own file: no manifest op. |
| `src_mcp/tests/OwnerClaimVectorsTests.cs:41`, `:58` | `odeLe` in the JSON key `"nodeLegacy"` | Rename the key to `nodeOldForm` in `shared/owner-record-cross-vectors.json` (the comment `:7` and every row) and in this file (`:27`, `:41`, `:58`). `git grep nodeLegacy` shows no other reader. |
| `src_mcp/tests/CandidatePathTests.cs:20`, `:21`, `:52`, `:53` | `toDelete`, `todelete` in test paths | These are test data for the product's word `"todelete"` (`src_mcp/core/Collecting/CandidatePath.cs:39`): the meaning needs the word. Move the four paths to `shared/candidate-path-vectors.json` (`{ "transient": [ … ], "merelyStartsTheSameWay": [ … ] }`, the same strings), read through `RepoPaths.Root()` as `OwnerClaimVectorsTests` does, and add `[MemberData]` beside the `[InlineData]` rows that stay. The same test values, so the same count. Reconcile by name in the PR. |

### 9.2 The two split fixtures: plain literals with the inline exemption

The plan's § *S6* and row 2's note: these two fixtures build the old prefix from pieces, and the shipped Node guard cannot see them, because it has no folding reader (F30). So they were invisible to it, not only a defect of form. In this brief `<prefix>` stands for the word `testsToDelete`; the file holds the whole word. **Do not assume the guard folds: it does not.**

- `scripts/lib/native-run.test.mjs:42-43`. Delete the `OLD_PREFIX` line. The refusal line becomes one literal, with the marker above it:
  ```js
  // <prefix>-exempt: legacy-name — the old prefixed series name is the fixture this refusal test is about
  refusal(() => rendered({ series: '/tmp/<prefix>-native-vibe-20261021-a1b2c3' }), 'the old prefixed name');
  ```
- `scripts/native/native.test.mjs:506`. The trailing comment goes too:
  ```js
  // <prefix>-exempt: legacy-name — the old prefixed series name is the fixture the remover must refuse
  const OLD_PREFIXED = '<prefix>-native-vibe-20261021-a1b2c3';
  ```

Measured with the Node guard: the literal alone is `legacy-name`, and with the marker it is clean.

**The split-piece scan.** Run it on the whole repository: `'testsTo'`, `"tests", "To"`, `.join('')` and `string.Concat` beside `Delete`. Put its output in the PR, **labelled as a search for the spellings a person thought of, not a proof that no other spelling exists**. What may remain: the detector words in `LeftoversGuard*.cs` and the copied tests, and the quarantine word `"teststo" + "delete-quarantine-"` of the library (`research/module_tests.md:6385`). Those are not a prefix of a name.

### 9.3 The read-only roots: migrated, not baselined (the statement's baseline bullet)

**C#: per site, `Path.GetTempPath()` → `TempDir.Root`.** The value is the same string (`TempDir.Root` is `Owned.TempRoot()`, which is the template's one read), so no behaviour changes. Each of these only READS the root: it passes a string that a pure builder, a fake or an assertion uses. 32 lines:

`src_mcp/tests/`: `ADeniedCommandIsAskedAgainTests.cs:36`, `AVibeRowReviewsInTheRoundsSnapshotTests.cs:192`, `AntigravityReadOnlyTests.cs:187`, `AntigravityStreamContinueTests.cs:27`, `:34`, `ApiRuntimeTests.cs:28`, `AskHumanServiceTests.cs:372`, `ClaudeCapabilityTests.cs:40`, `:248`, `ConsultantTurnTests.cs:30`, `ConsultationStoreTests.cs:461`, `EpicTwoReviewThreadsTests.cs:33`, `EveryPaidLaunchIsWrittenTests.cs:102`, `JsonlLedgerTests.cs:122`, `:151`, `KimiAcpProfileTests.cs:99`, `KimiMonthlyQuotaTests.cs:95`, `KimiQuotaIsNotASignInTests.cs:37`, `KimiRuntimeTests.cs:229`, `LocalReasoningEffortTests.cs:74`, `:87`, `LocalRuntimeTests.cs:131`, `QuestionAdmissionTests.cs:30`, `:39`, `QuestionRowLookupScenarioTests.cs:170`, `ResolvedDataDirTests.cs:76`, `SnapshotBudgetTests.cs:114`, `ThePromptSaysWhatTheReviewerHasTests.cs:30`, `:51`, `TheShippedPromptsTests.cs:89`, `TheStderrLineIsQuotedSafelyTests.cs:250`. `src_server/tests/`: `ReviewLauncherTests.cs:503` (add `using CoaiMcp.Tests;` if it is missing).

**Stay in the baseline, do not touch** (20 lines; they are not read-only reads, or they prove the API itself):
`RunFolderTests.cs:66` (three suites) and `RunTempTests.cs:38`, `:47`, `:51`, `:67`, the proof that the API itself moved; `AVibeHomesNeverLiveUnderTempTests.cs:48`, the same; `NoTestMovesTempButTheRunTempInitializerTests.cs:50`, `TheNeverUnderTempGuardsTests.cs:25`, `:76`, `:92`, `:94`, `:97`, a sentence or a fixture that names the API; `ProcessTrackingTests.cs:20`, `RemoteReviewerTests.cs:414`, `src_server/tests/VendorCatalogTests.cs:15`, a `Combine` continued from the line above, S7's shape; `TheRoundSaysWhereTheDocumentWentTests.cs:29`, `KimiShimHoldTests.cs:102`, `TheFeatureRoundAnswersInItsOwnSchemaTests.cs:56`, where a product type takes the root and I cannot prove from the line that nothing is written under it. Read one of these and find it read-only? Leave it; S7 takes it. A `TempDir.Root` over a real write would hide that write from the guard.

**Node: per FILE, as the plan's word "imports" says.** A file whose only `raw-temp` sites read the root. Five lines in three files:

| File | Change |
|---|---|
| `src_vs_code/src/test/ledgerFieldsSeam.test.mjs:70`, `:78`, `:82` | Drop `import { tmpdir } from 'node:os'` (`:3`); import `owned` from `../../scripts/owned.mjs`; `join(owned.tempRoot(), …)`. |
| `src_vs_code/src/test/pathFamily.test.ts:141` | `path.join(tempRoot(), …)` with `import { tempRoot } from './tempRoot';`. Drop `import os` (`:7`) if nothing else uses it. |
| `src_vs_code/src/test/processLauncher.test.ts:230` | `tempRoot()`; drop `import { tmpdir }` (`:5`). |

New `src_vs_code/src/test/tempRoot.ts`: `import { tmpdir } from 'node:os'; export const tempRoot = (): string => tmpdir();`, with one comment line saying why. It is not a `*.test.*` file, so the Node guard does not read it and it is no baseline entry. S7's text "that the baseline names" (the plan's § *S7*) cannot hold for such a file; note it in the PR for S7.

The other Node files with a `tmpdir()` read also hold a `mkdtemp`, so their import is not "used only to read": they stay in the baseline. So do `owned.test.mjs`, `runFolder.test.mjs` (two), `runTests.test.mjs`, which prove the variable, and `wslNetwork.test.ts:187`, which writes.

### 9.4 `src_server/smoke-image.sh`: the trap before `mktemp`, and an owner-form name (D8 (p))

**Today.** `work=$(mktemp -d)` at `:73`; `mkdir`, `chmod 0777`, `seed`, `new_key` and the mask line run under `set -e`; `finish()` starts at `:85`; `trap finish EXIT` is at `:102`. A failure in between exits before the trap line, and the folder stays.

**The new text of `main`,** in this order, right after the globals line `work='' id='' logs_pid='' ok=false` (`:71`):

```bash
    finish() {
        # … unchanged …
    }
    trap finish EXIT

    owner="u$(id -u)"
    # testsToDelete-exempt: raw-temp — image smoke script a person may run outside a run folder; the trap above removes this folder on every exit except a kill -9 or the hard kill of a CI cancel, and that leaves it owner-named, listed as unmarked and never removed
    work=$(mktemp -d "${TMPDIR:-/tmp}/testsToDelete-$owner-coai-smoke-XXXXXX")
    mkdir "$work/data"
```

`finish` already takes empty globals: each of its branches tests its variable first. Do not change its body.

**Why two lines.** A literal `testsToDelete-u` on the `mktemp` line is a `legacy-name` as well as a `raw-temp`. With the variable, the guard says `raw-temp` only. Measured at the pin: in the Node guard, one `raw-temp`; in the C# guard, one `raw-temp`, and the marker clears it.

**RED first (R19).** Add to `TheImageSmokeUsesTheServerTests.cs`, using its `Stub` and `Bash` helpers:

```csharp
[Fact]
public void AFailureBeforeTheContainer_LeavesNoFolder()
```

1. `var tmp = Directory.CreateDirectory(Path.Combine(_dir, "tmp")).FullName;`
2. A `chmod` stub first on PATH: `#!/bin/sh`, then a line that appends `$2` to `<_dir>/chmod-saw`, then `exit 1`.
3. `Bash` with `exec bash '<Script>' 'coai-server:smoke'` and the environment `TMPDIR = ShellScripts.Posix(tmp)`, `SMOKE_PORT = 1`, `SMOKE_WAIT_SECONDS = 2`, `GITHUB_ACTIONS = false`.
4. Exit code is not 0.
5. The premise: `chmod-saw` exists and is not empty, so the script did reach `chmod` with a folder under `tmp`. Without this the test is green over nothing.
6. `Directory.GetFileSystemEntries(tmp).Should().BeEmpty(...)`.

Today the last check is red: the folder stays. After the move it is green.

**The name (R20).** In `TheImageSmokeUsesTheServerTests`, the existing run with the `docker` stub records the `-v` argument. A new assertion, on POSIX only (`if (!OperatingSystem.IsWindows())`): the `-v` value's folder leaf starts with `Owned.NameBase + "smoke-"`. On Windows `id -u` under Git Bash is no Windows owner token, so the name is not judged there. Use `Owned.NameBase`; never spell the prefix in this file.

**What stays open. Say it in the PR.** A `kill -9`, or the hard kill that ends a CI cancel, at any moment of the run leaves one owner-named folder per killed run. No trap sees that signal. It has no claim. The sweep and the janitor list it as `unmarked` and never remove it. Measured: `janitor.sh --token coai --dry-run` with a two-hour-old `testsToDelete-u1000-coai-smoke-abc123` answers `unmarked: folder testsToDelete-u1000-coai-smoke-abc123` and removes nothing. This is the goal's second bounded exception, approved by the owner, and it is permanent: a person reads and removes such a folder. A test's run has `TMPDIR` in its run folder, and a hosted runner is discarded, so the case is a person's run. A shell port of the helper is not asked for.

## 10. The expected-exemption inventory

**112 rows. The count at creation binds; 77 `raw-temp` rows is a ceiling, not an allowance.** The tables give the SITE's line today and the marker's text. The marker goes on the line above, in one form: `// <prefix>-exempt: <class> — <reason>` (or `#` in a `.sh` or `.yml`). A marker shifts the lines under it, so write `shared/leftovers-exemptions.json` last in step 5, from the tree, with the SITE's final line and hash (§ 7.3). Check it against this table by count:

| Class | Rows | |
|---|---|---|
| `legacy-name` | 9 | 4 exist, 5 new |
| `raw-worktree` | 23 | all new |
| `shared-store` | 2 | new |
| `docker-run` | 1 | new |
| `raw-temp` | 77 | 1 exists (`Owned.cs:119`, the template's), 76 new: 22 in § 10.4, 54 in § 10.5 |

Rule of the reason: at least ten characters and three words, no placeholder start (*needed*, *see above*, *because*, *todo*), and `lock:` for `shared-store`. The wording below passes. A `raw-worktree` reason names the folder; before you write it, read the line that makes that folder. If it is not what the table says, fix the reason and tell the reviewer.

The guards column is `guards` in the JSON: which guard reads the file. `.cs`, `src_vs_code/scripts/*`: `csharp`. Root `scripts/**`, `*.test.*`: `node`. A `*.sh` not on the frozen list, a workflow: both. The frozen list's 11 files: `node` only, because the C# guard never reads them.

**The kinds of reason for `raw-temp` (the parent's item 4.2 and entry 6.1). Each row has ONE. Do not widen a kind to make a row fit: a row that fits none goes to the coordinator at once.**

| `proof` | Kind | What keeps it true |
|---|---|---|
| `entry` | 1. An entry with the wrap: `startOwnedRun` is its first statement after the imports, or, in C#, the gated `Main` of a test assembly | `runnerScripts.test.mjs` E1, E2, E3; `runFolder.test.mjs:33` (`os.tmpdir()` answers the run folder under the wrap). C#: the gate and the forced runs of the proof files |
| `library` | 2. A library, through every importer | E9 (§ 7.5) |
| `read` | 3. A read that allocates nothing | The line itself: the read record below |
| `unreached` | 4. A line no test run reaches. A line a test run can reach is never this kind | E10 and its pinned pairs (§ 7.5, § 10.6) |
| `reached` | 5. A line a test under the gate reaches, whose product caller is not a test run | The pin in each test class (§ 10.7) |
| `bounded-exception` | Not a kind: `smoke-image.sh:73`, the goal's second bounded exception (§ 9.4) | D8 (p), R19 |

### 10.1 `legacy-name`

| File | Line of the site today | Class | Guards | Reason (the marker's text) |
|---|---|---|---|---|
| `scripts/lib/old-coai-leftovers.test.mjs` | 27 (marker :26, exists) | `legacy-name` | node | the clean-up reads old run folders, so this fixture must be the legacy name itself |
| `scripts/lib/old-coai-leftovers.test.mjs` | 29 (marker :28, exists) | `legacy-name` | node | the same text as :26 |
| `scripts/lib/old-coai-leftovers.test.mjs` | 99 (marker :98, exists) | `legacy-name` | node | the listing of old run folders is the test, so its four fixtures are the legacy names |
| `src_vs_code/src/test/leftoversSweep.test.mjs` | 58 (marker :57, exists) | `legacy-name` | node | the sweep lists legacy run folders, so this fixture must be the legacy name itself |
| `scripts/lib/old-coai-leftovers.mjs` | 35 | `legacy-name` | node | the one-off clean-up names the old run folders; helper-own, it is the only tool that may |
| `scripts/lib/old-coai-leftovers.mjs` | 37 | `legacy-name` | node | the same clean-up reads its own quarantine of an old run folder; helper-own |
| `scripts/lib/old-coai-leftovers.mjs` | 139 | `legacy-name` | node | the same clean-up reports a folder of the old shape; helper-own |
| `scripts/lib/native-run.test.mjs` | 42-43, one line after § 9.2 | `legacy-name` | node | the old prefixed series name is the fixture this refusal test is about |
| `scripts/native/native.test.mjs` | 506 | `legacy-name` | node | the old prefixed series name is the fixture the remover must refuse |

### 10.2 `raw-worktree`

The four rows that say "no worktree is made here" only name the words. The plan's "naming the owned folder" has no folder to name there.

| File | Line of the site today | Class | Guards | Reason (the marker's text) |
|---|---|---|---|---|
| `src_mcp/tests/ACallerTranscriptFixtureCarriesNoTextTests.cs` | 712 | `raw-worktree` | csharp | the words are in an assertion message, no worktree is made here |
| `src_mcp/tests/AReviewTreeIsGivenBackTests.cs` | 270 | `raw-worktree` | csharp | inside this test's own Temp folder `theirs`, under the run folder |
| `src_mcp/tests/AReviewTreeIsGivenBackTests.cs` | 315 | `raw-worktree` | csharp | inside this test's own Temp folder `theirs`, under the run folder |
| `src_mcp/tests/AReviewTreeTests.cs` | 221 | `raw-worktree` | csharp | inside this test's own Temp folder `linked`, under the run folder |
| `src_mcp/tests/AReviewTreeTests.cs` | 262 | `raw-worktree` | csharp | the words are an expected argv fragment, no worktree is made here |
| `src_mcp/tests/AReviewTreeWithALongPathTests.cs` | 132 | `raw-worktree` | csharp | inside `_root`, this test's own temp folder under the run folder |
| `src_mcp/tests/AVibeDiskRowExpandsAParentFolderTests.cs` | 431 | `raw-worktree` | csharp | `at` is under the caller's `parent`, a test's own folder under the run folder |
| `src_mcp/tests/AVibeDiskRowReadsAParentFolderThroughTheServiceTests.cs` | 59 | `raw-worktree` | csharp | inside `parent`, this test's own folder under the run folder |
| `src_mcp/tests/AVibeDiskRowSaysWhatItSkippedTests.cs` | 116 | `raw-worktree` | csharp | inside `parent`, this test's own folder under the run folder |
| `src_mcp/tests/CadenceStoreTests.cs` | 162 | `raw-worktree` | csharp | `linked` is directly in the run folder, the temp root of this process |
| `src_mcp/tests/FilesystemInvariantTests.cs` | 190 | `raw-worktree` | csharp | `linked` is directly in the run folder, the temp root of this process |
| `src_mcp/tests/FilesystemInvariantTests.cs` | 220 | `raw-worktree` | csharp | `linked` is directly in the run folder, the temp root of this process |
| `src_mcp/tests/FilesystemInvariantTests.cs` | 416 | `raw-worktree` | csharp | `linked` is directly in the run folder, the temp root of this process |
| `src_mcp/tests/RepositoryFactsTests.cs` | 80 | `raw-worktree` | csharp | inside `_dir`, this test's own folder under the run folder |
| `src_mcp/tests/RepositoryFactsTests.cs` | 116 | `raw-worktree` | csharp | inside `_dir`, this test's own folder under the run folder |
| `src_mcp/tests/TreeGitTests.cs` | 43 | `raw-worktree` | csharp | an expected argv in a pure test, no worktree is made here |
| `src_mcp/tests/TreeGitTests.cs` | 49 | `raw-worktree` | csharp | an expected argv in a pure test, no worktree is made here |
| `src_mcp/tests/WorktreeManagerTests.cs` | 118 | `raw-worktree` | csharp | inside `human-wt-`, this test's own temp folder under the run folder |
| `src_mcp/tests/WorktreeManagerTests.cs` | 259 | `raw-worktree` | csharp | inside `_storage`, this test's own temp folder under the run folder |
| `src_mcp/tests/WorktreeManagerTests.cs` | 427 | `raw-worktree` | csharp | inside `_storage`, this test's own temp folder under the run folder |
| `src_mcp/tests/WorktreeManagerTests.cs` | 452 | `raw-worktree` | csharp | inside `_storage`, this test's own temp folder under the run folder |
| `src_mcp/tests/WorktreeManagerTests.cs` | 495 | `raw-worktree` | csharp | inside `real`, this test's own temp folder under the run folder |
| `src_mcp/tests/WorktreeManagerTests.cs` | 526 | `raw-worktree` | csharp | inside `storage`, this test's own temp folder under the run folder |

### 10.3 `shared-store` and `docker-run`

| File | Line of the site today | Class | Guards | Reason (the marker's text) |
|---|---|---|---|---|
| `src_bugs/tests/TheAuditTests.cs` | 190 | `shared-store` | csharp | a private SQLite file in this test's own folder; lock: none, one connection |
| `src_mcp/tests/BackfillAuthorsBackupTests.cs` | 148 | `shared-store` | csharp | hostile input text for a refusal test, no SQL runs; lock: none, nothing is opened |
| `src_server/tests/TheImageSmokeUsesTheServerTests.cs` | 102 | `docker-run` | csharp | the words are in an assertion message, no container is started here |

### 10.4 `raw-temp` outside the six scopes, 23 rows

| File | Line of the site today | Guards | `proof` | Reason (the marker's text) | Proved by |
|---|---|---|---|---|---|
| `deploy/bugs/deploy-cmd.sh` | 109 | node | `unreached` | a product deploy script on a server; no test run reaches this line, pinned by E10 | `install.test.ts` reads the text; a comment in `TheHostsHelperIsTheOneThisDeployNeedsTests.cs:24`. No test starts the file. § 10.6 |
| `deploy/bugs/deploy-cmd.sh` | 165 | node | `unreached` | the same text as :109 | the same |
| `deploy/bugs/release.sh` | 59 | node | `unreached` | a product release script on a server; no test run reaches this line, pinned by E10 | a comment at `install.test.ts:1356` names it. No test starts the file. § 10.6 |
| `deploy/bugs/release.sh` | 171 | node | `unreached` | the same text as :59 | the same |
| `deploy/coai-deploy-cmd.sh` | 166 | node | `unreached` | a product deploy script on a server, in a root-only folder; no test run reaches this line, pinned by E10 | `install.test.ts:487` runs the file with inputs that are all refused at the `case` block, before this line (exit 90); the rest read the text. § 10.6 |
| `deploy/systemd-release.sh` | 137 | node | `unreached` | a product release script on a server; no test run reaches this line, pinned by E10 | in `preflight_token`. `TheCanaryFallbackRunsTests.cs:120` sources the file and calls only `canary`; `install.test.ts` reads the text. § 10.6 |
| `deploy/systemd-release.sh` | 302 | node | `unreached` | the same text as :137 | in `stage_from`, under its `$target` folder; the same two tests |
| `.github/workflows/release.yml` | 317 | csharp, node | `unreached` | job-scoped under RUNNER_TEMP, which rule 7 says is no finding, and the C# pattern fires anyway; no test run reaches this line, pinned by E10 | the workflow runs only on a hosted runner. Ten test files read its text or name it in a comment. § 10.6. `workflowsStartTestsThroughARunner.test.mjs` F3 pins the `"$RUNNER_TEMP/` form |
| `.github/workflows/release.yml` | 1319 | csharp, node | `unreached` | the same text as :317 | the same |
| `scripts/probe-agy-allow-rule.mjs` | 26 | node | `unreached` | a probe a person runs, under the scratch root named on its command line; no test run reaches it, pinned by E10 | no test file names it. The folder comes from argv (`:19`) |
| `scripts/probe-agy-consult-follow-up.mjs` | 31 | node | `unreached` | the same text as `probe-agy-allow-rule.mjs` | no test file names it |
| `scripts/probe-agy-write-block.mjs` | 72 | node | `unreached` | the same text | no test file names it |
| `scripts/probe-agy-write-block.mjs` | 84 | node | `unreached` | the same text | no test file names it |
| `scripts/probe-claude-consultant-confinement.mjs` | 29 | node | `unreached` | the same text | after the reword below, no test file names it |
| `src_server/install-cli.sh` | 22 | node | `reached` | an image installer; a test under the gate reaches this with TMPDIR in the run folder, and the image build is not a test run | § 10.7 |
| `src_server/install-agy.sh` | 61 | node | `reached` | the same text | § 10.7 |
| `deploy/bugs/install-env.sh` | 101 | node | `reached` | the root helper of a deploy; a test under the gate reaches this with TMPDIR in the run folder, and a deploy is not a test run | § 10.7 |
| `deploy/systemd-release.sh` | 161 | node | `reached` | the canary of a deploy; a test under the gate reaches this with TMPDIR in the run folder, and a deploy is not a test run | § 10.7 |
| `src_mcp/tests_fakecli/Serving.cs` | 140 | csharp | `read` | reads the path of a steering file and allocates nothing | `:140` only combines a path; `:141` is `File.Exists` and `File.ReadAllText`. Nothing is made here |
| `src_mcp/tests_leftovers/Owned.cs` | 120 (marker :119, exists, the template's) | csharp | `read` | the helper is the one place a test learns the temp root | `return Path.GetTempPath();`. The marker's text is the template's and stays. Nothing is made here |
| `scripts/lib/kimi.mjs` | 176 | node | `read` | reads the temp root to refuse a scratch under it; nothing is made | a default parameter of `scratchRefusal`. The function compares paths and returns a string |
| `src_mcp/tests_leftovers/TempDir.cs` | 35 | csharp | `entry` | a helper of the gated test assemblies; the gated Main moved TEMP into the run folder before this runs, and the forced runs prove the gate | it allocates on every test. Kind 1 in its C# form. The four projects that reference the library (`CoaiMcp.Tests`, `CoaiServer.Tests`, `CoaiBugs.Tests`, `CoaiBench.Tests`) each have `RunTempStart.cs` and the gated `TestEntryPoint.cs`. `TheEntryPoint_IsTheGatedOne` and the five forced runs in each proof file (§ 3). The fake CLI does not reference the library |
| `src_server/smoke-image.sh` | 73, moves (§ 9.4) | csharp, node | `bounded-exception` | the text of § 9.4 | not a kind: the goal's second bounded exception. D8 (p), R19, R20 |

**The reword.** `src_mcp/tests/ClaudeConsultantArgvTests.cs:16` names `probe-claude-consultant-confinement.mjs` in a doc comment. So the fact "no test names `scripts/probe-*`" was false for this one file. Change the sentence to: the evidence of EFFECT is the live probe recorded in `research/RESULTS_claude_consultant_confinement.md`. That record's line 4 names the script, so no meaning is lost. Do not add a pair for it.

**A read record, for the PR.** Copy the "proved by" cell of each `read` row into the PR, with `run-tests.mjs:103` and `ownedRun.mjs:34`, `:36` of § 10.5.

### 10.5 `raw-temp` in `src_vs_code/scripts/`, 54 rows

One row per file here; each line is one marker and one inventory row. Guards: `csharp` for all of them.

**Entries (18 files, 32 lines).** `proof: entry`. Reason: `runs inside this script's own run folder, startOwnedRun moved TEMP there`. Proved by: E1 lists the file, E2 proves that the wrap is the first statement after the imports, and E3 runs it.

| File | Lines of the sites today | The wrap |
|---|---|---|
| `live-agent.mjs` | 78, 79, 80 | `:24` |
| `live-api-settings-compat.mjs` | 93 | `:43` |
| `live-chat.mjs` | 61 | `:25` |
| `live-close-consult-compat.mjs` | 186 | `:42` |
| `live-command-models-compat.mjs` | 20 | `:14` |
| `live-feature-schema-compat.mjs` | 122 | `:46` |
| `live-feature-vendor-compat.mjs` | 113, 164 | `:50` |
| `live-fresh.mjs` | 174 | `:69` |
| `live-gauge-compat.mjs` | 81, 93 | `:36` |
| `live-paths-per-side-compat.mjs` | 114 | `:46` |
| `live-security-settings-compat.mjs` | 21 | `:14` |
| `live-vibe-runtime-compat.mjs` | 61 | `:39` |
| `measure-append.mjs` | 160 | `:68` |
| `measure-consultant-skew.mjs` | 75, 121, 181, 182 | `:48` |
| `measure-image.mjs` | 284 | `:37` |
| `measure-stream.mjs` | 485 | `:69` |
| `run-contract.mjs` | 148 | `:43` |
| `run-seam.mjs` | 143, 285, 311, 370, 409, 439, 487, 569 | `:27` |

**Libraries (13 files, 19 lines).** `proof: library`. Reason: `a library; every importer is an entry with the wrap or a test under the runner, pinned by runnerScripts E9`. Proved by: E9, and this list of callers in the PR.

| File | Lines of the sites today | Its importers (`git grep -l`, measured) |
|---|---|---|
| `browserLayout.mjs` | 51, 79 | `measure-answer-code-layout.mjs`, `measure-failure-layout.mjs`, `measure-help-clicks.mjs`, `render-page.mjs` (entries) |
| `seam-catalog.mjs` | 69 | `run-seam.mjs` |
| `seam-cli-path-other-side.mjs` | 36, 83 | `run-seam.mjs` |
| `seam-consultant-row.mjs` | 48 | `run-seam.mjs` |
| `seam-gauge.mjs` | 77 | `run-seam.mjs` |
| `seam-ledger-fields.mjs` | 73, 91, 222 | `run-seam.mjs`, `src/test/ledgerFieldsSeam.test.mjs` |
| `seam-missing-named-root.mjs` | 100 | `run-seam.mjs` |
| `seam-path-missing.mjs` | 31, 32 | `run-seam.mjs` |
| `seam-qconsult-roots.mjs` | 44, 45 | `run-seam.mjs` |
| `seam-refusal.mjs` | 39 | `run-seam.mjs`, `src/test/seamLegs.test.mjs` |
| `seam-security-text.mjs` | 78 | `run-seam.mjs`, `src/test/seamLegs.test.mjs` |
| `seam-security.mjs` | 50 | `run-seam.mjs` |
| `seam-usage-counts.mjs` | 165 | `run-seam.mjs` |

**Reads (2 files, 3 lines).** `proof: read`. Reason: `reads the root and allocates nothing; the run folder is made through the wrap, not here`.

| File | Lines of the sites today | Proved by |
|---|---|---|
| `ownedRun.mjs` | 34, 36 | `:34` passes `realpathSync.native(tmpdir())` to `ownedScratch`, which makes the owned folder; `:36` puts the path in a message |
| `run-tests.mjs` | 103 | `export const REAL_TEMP_ROOT = tmpdir();` |

Not in the inventory, because no guard flags them (so they take no marker; the PR names them, from the statement's inventory bullet, so that a later guard change cannot make them silent findings): the 8 quarantine lines of `old-coai-leftovers.test.mjs` (`:200`, `:254`, `:355`, `:414`, `:468`, `:587`, `:721`, `:729`), `run-tests.mjs:166`, `startOfRun.mjs:25`, `make-series.sh`, the C# library's quarantine word.

### 10.6 The pinned pairs: the `namedBy` list of kind 4

These are the pairs that exist at creation. The first 16 exist today (measured with E10's search). The last 8 come with S6's own copy, because the frozen list of § 6.5 names those files and `LeftoversGuardTests` pins that list. `why` is one line that says why the LINE is not reached. E10 fails when the set found differs from this list. If your tree finds another pair at creation, read that test: list it only if the line is not reached, and tell the reviewer.

| `file` | `testFile` | `why` |
|---|---|---|
| `deploy/bugs/deploy-cmd.sh` | `src_vs_code/src/test/install.test.ts` | the text is read; no test runs it |
| `deploy/bugs/deploy-cmd.sh` | `src_bugs/tests/TheHostsHelperIsTheOneThisDeployNeedsTests.cs` | a comment names it; no test runs it |
| `deploy/bugs/release.sh` | `src_vs_code/src/test/install.test.ts` | a comment names it; no test runs it |
| `deploy/coai-deploy-cmd.sh` | `src_vs_code/src/test/install.test.ts` | the file is run and refused before this line; otherwise the text is read |
| `deploy/systemd-release.sh` | `src_server/tests/TheCanaryFallbackRunsTests.cs` | it is sourced and only `canary` is called; `canary`'s own line is a `reached` row |
| `deploy/systemd-release.sh` | `src_vs_code/src/test/install.test.ts` | the text is read |
| `.github/workflows/release.yml` | `src_bugs/tests/ShellScript.cs` | a comment names it |
| `.github/workflows/release.yml` | `src_bugs/tests/TheArchiveCheckTests.cs` | a comment names it |
| `.github/workflows/release.yml` | `src_bugs/tests/ThePublishOutputCheckTests.cs` | a comment names it |
| `.github/workflows/release.yml` | `src_mcp/tests/TheWordListIsLoadedBeforeTheTransportTests.cs` | a comment names it |
| `.github/workflows/release.yml` | `src_vs_code/src/test/changelogNamesTheRelease.test.ts` | the text is read |
| `.github/workflows/release.yml` | `src_vs_code/src/test/changelogSection.test.ts` | a comment names it |
| `.github/workflows/release.yml` | `src_vs_code/src/test/draftReleaseNotes.test.ts` | the text is read |
| `.github/workflows/release.yml` | `src_vs_code/src/test/install.test.ts` | the text is read |
| `.github/workflows/release.yml` | `src_vs_code/src/test/windowsPackageIsSigned.test.ts` | the text is read |
| `.github/workflows/release.yml` | `src_vs_code/src/test/workflowsStartTestsThroughARunner.test.mjs` | the text is read |
| `deploy/bugs/deploy-cmd.sh` | `src_mcp/tests_leftovers/LeftoversGuard.cs` | the frozen list names it; the guard never runs it |
| `deploy/bugs/deploy-cmd.sh` | `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` | the test pins the frozen list; it never runs the file |
| `deploy/bugs/release.sh` | `src_mcp/tests_leftovers/LeftoversGuard.cs` | the frozen list names it; the guard never runs it |
| `deploy/bugs/release.sh` | `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` | the test pins the frozen list; it never runs the file |
| `deploy/coai-deploy-cmd.sh` | `src_mcp/tests_leftovers/LeftoversGuard.cs` | the frozen list names it; the guard never runs it |
| `deploy/coai-deploy-cmd.sh` | `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` | the test pins the frozen list; it never runs the file |
| `deploy/systemd-release.sh` | `src_mcp/tests_leftovers/LeftoversGuard.cs` | the frozen list names it; the guard never runs it |
| `deploy/systemd-release.sh` | `src_mcp/tests/Leftovers/LeftoversGuardTests.cs` | the test pins the frozen list; it never runs the file |

Before you write a `why`, open that test and confirm it. A pair where a test does reach the line is a row that is not kind 4: stop and report it.

**The limit:** a new spawn inside a test file on this list is not seen (§ 7.5).

### 10.7 Kind 5: the pin in each test class (new code of S6)

These four lines are reached by a test under the gate. **The child's `TMPDIR` is inherited today. No test sets it and none asserts it**, so the pin is new code.

**A person's run of these scripts through their product caller is not covered, because it is not a test run.** The image build runs the two installers; a deploy runs `install-env.sh` as root and `canary` on the server. They use the machine's temp there, and each script has its own `trap` for its file. Say this in the PR.

In `src_mcp/tests_leftovers/RunTemp.cs` (coai's own):

```csharp
/// <summary>The pin of the inventory's kind 5: the child this start makes has TEMP, TMP and TMPDIR inside this run's folder (E5 S6, entry 6.1).</summary>
public static void RequireChildTempInRunFolder(ProcessStartInfo start)
{
    foreach (var name in RunTempFolder.Variables)
    {
        var value = start.Environment.TryGetValue(name, out var set) ? set : null;
        var inside = value is not null
            && (string.Equals(value, Folder, StringComparison.Ordinal) || value.StartsWith(Folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        if (!inside)
        {
            throw new InvalidOperationException($"{name}='{value}' is outside this run's folder '{Folder}': a script of the inventory's kind 5 must be given a temp inside it.");
        }
    }
}
```

Add `using System.Diagnostics;`. It spells no temp variable, because `RunTempFolder.Variables` holds them.

| The test class | The call goes | The script and line |
|---|---|---|
| `src_server/tests/TheImageChecksEachInstallerTests.cs` | in `Run`, on the line before `Process.Start(start)` (`:130`) | `install-cli.sh:22` |
| `src_server/tests/TheImageInstallsAgyFromItsManifestTests.cs` | in `Bash`, on the line before `Process.Start(start)` (`:268`) | `install-agy.sh:61` |
| `src_server/tests/TheCanaryFallbackRunsTests.cs` | on the line before `Process.Start(start)` (`:127`) | `systemd-release.sh:161` |
| `src_bugs/tests/TheRootHelperFramesTwoRecordsTests.cs`, through `src_bugs/tests/ShellScript.cs` | in `ShellScript.Fed`'s path: `Launch` makes the start with `StartInfo(...)` and starts it through `StartOrExplain`. Take the start in a local, call the pin, then pass it on. This covers every script `ShellScript` starts, and that is correct: they all run under the gate | `install-env.sh:101` |

**RED by hand, once, for one class (R24).**

## 11. The audit (D9, D8 (o))

**The first trap.** The template wires `() => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())`. Copied as it stands, this is red on every run in coai: the template has no run folder, and coai's IS an `OwnedDir` (`RunTempFolder.Create`, `OwnedDir.Create("run", realRoot, …)`), so `Unreclaimed()` holds it while it is alive. The audit would dispose it and report "never disposed". And if it read the machine temp, `Discover` would find the run folder by its claim (`WithholdInheritedRun` makes the claim name this process) and remove it.

**The second trap: the order.** `LeftoversAudit.Discover` returns `[]` once the run folder is gone (`LeftoversAudit.cs:41`). The registry still reports a leaked `OwnedDir`. But a claim-only leftover, an orphan claim of a folder a test removed itself and never registered, is seen only by `Discover`. xUnit 4.0.1 disposes assembly fixtures in creation order, and all four `RunTempStart.cs` list `RunTempFixture` first. So today's order would miss it. The first version of this brief said the order "must not matter". That was wrong. Do not swap the attributes: that runs the real-root sweep before `RunTempFixture`'s constructor, the fail-closed fallback.

### 11.1 The ONE ordered call

The audit, then `RunTemp.EndOfRun` in `finally`, in the sweep fixture's disposal. It lands in the **fourth op's `new` text** of the entry `src_mcp/tests_leftovers/LeftoversSweepFixture.cs` in `src_mcp/tests_leftovers/template-manifest.json`. The op's `old` stays `() => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())`, count 1. Its `new` is exactly this, on one line:

```csharp
() => { try { RealAudit(RunTemp.Folder, RunTemp.RegistryWithoutTheRunFolder()); } finally { RunTemp.EndOfRun(); } }
```

Apply the same text in the copy, in place of `audit: null /* the end-of-run audit is wired in E5 S6 (D9); until then this fixture's disposal does nothing */`. Keep one line: the copy is compared as raw bytes. Measured: `dotnet format whitespace --verify-no-changes` with this repository's `.editorconfig` passes on this line.

**Ending the run is a seam that only the assembly fixture's own constructor sets.** That lambda exists only in the public, parameterless constructor, and xUnit is the only caller of that constructor. Every other caller uses the internal seam constructor, `LeftoversSweepFixture(Func<SweepResult> sweep, Action<string> report, Action? audit = null)`, and gives its own `audit` or none. `DisposeAsync` only calls `_audit()`, and it does not change.

This is why the seam matters: the copied `LeftoversAuditTests.The_assembly_fixture_fails_the_run_with_the_audits_message` builds two fixtures through the seam constructor (template `:268`, `:269`) and calls that disposal directly (`:271`, `:274`). Those calls must not end the real run in the middle of the suite. With this wiring they do not. `LayoutGateTests.cs:24` and `LeftoversSweepTests.cs:517`, `:532`, `:545`, `:564`, `:565` use the seam constructor too. R17 pins this.

### 11.2 The changes in coai's own files

`src_mcp/tests_leftovers/RunTemp.cs`. Add:

```csharp
/// <summary>What the end-of-run audit reads: every folder the helper still holds, without this run's own folder, which <see cref="EndOfRun"/> ends right after the audit (D9).</summary>
internal static IReadOnlyList<OwnedDir> RegistryWithoutTheRunFolder() =>
    [.. OwnedDir.Unreclaimed().Where(dir => !ReferenceEquals(dir, _run.Dir))];
```

Also change the `<para><b>The end.</b>` of the class comment: `EndOfRun` runs from the sweep fixture's disposal, after the audit, and from the process-exit handler. `RunTemp.Start`'s process-exit handler (`RegisterRestore` in `Seams`) stays. Do not touch it.

`src_mcp/tests_leftovers/RunTempFixture.cs`. Remove `public void Dispose() => RunTemp.EndOfRun();` and `: IDisposable`. It stays an assembly fixture: the fallback refusal. Correct its comments. In `src_mcp/tests/RunTempTests.cs` and the three `RunFolderTests.cs`, change the text `"the run folder's fallback refusal and teardown"` to `"the run folder's fallback refusal"`.

`Restore` is idempotent (`RunTempFolder.cs:124`, its `Interlocked.Exchange`). The existing test `RunTempTests.RestoreIsIdempotent_AndPutsTheOldValuesBack` pins that. It stays. Name it in the PR.

`LeftoversAudit.cs` and `LeftoversAuditException.cs` are unchanged (their manifest entries stand). The other two assembly fixtures of `CoaiMcp.Tests` need nothing: `TempDirsAreSwept` has no disposal, and `TestClaudeHome`'s `TempDir` takes a folder that is already gone (`ATempDirThatIsAlreadyGone_DisposesQuietly`), as it does today.

### 11.3 What each way out does

| The way out | What happens |
|---|---|
| A normal end | The audit reads the run folder while it exists: a test's own owned folder and a claim, depth 1. Never the run folder itself, which sits in the parent, and never in the registry list. Then `EndOfRun`. |
| The audit throws | `finally` ends the run. The run fails with the audit's message. |
| The run has a leaked `OwnedDir` | Red through the registry, as before. |
| The run has an orphan claim | **Red through `Discover`, because the audit runs first.** This is condition (o). |
| A child under an inherited `TESTSTODELETE_RUN` | `Owner.RunRecord(env, …) != Owner.This`: `Discover` returns nothing. The registry covers this process's own. Row 1's fix, from the template. |
| The sweep fixture's constructor threw, a host aborted before disposal, or `RunTempFixture` refused | The process-exit handler ends the folder. No audit runs. That run has already failed or been killed. |
| The run was killed | One folder plus its claim. The next run's sweep collects it. |
| The run folder cannot be removed at the end | It stays with its claim and one warning (S3's contract), and it is not a red run. The next sweep judges it. |

### 11.4 The self-test per suite: the orphan-claim plant (R16)

One test per suite. A child run plants an orphan claim inside its own run folder, absent from the registry, with **no inherited `TESTSTODELETE_RUN`**, and the audit reports it.

| Suite | The parent test | The child class |
|---|---|---|
| `CoaiMcp.Tests` | `src_mcp/tests/RunTempTests.cs`, `AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds` | `OrphanClaimChild` in `src_mcp/tests/RunTempChildren.cs` |
| `CoaiServer.Tests` | `src_server/tests/RunFolderTests.cs`, the same name | `OrphanClaimChild` in the same file, beside `DiscoverySentinel` |
| `CoaiBugs.Tests` | `src_bugs/tests/RunFolderTests.cs`, the same name | the same |
| `CoaiBench.Tests` | `src_bench/CoaiBench.Tests/RunFolderTests.cs`, the same name | the same |

The child:

```csharp
public sealed class OrphanClaimChild
{
    [Fact]
    public void PlantsAClaimWhoseFolderIsMissing()
    {
        if (Environment.GetEnvironmentVariable("COAI_S6_ORPHAN_CLAIM") != "1")
        {
            Assert.Skip("child-only: AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds starts the suite on this class with COAI_S6_ORPHAN_CLAIM=1");
        }

        File.WriteAllText(Path.Combine(RunTemp.Folder, Owned.NameFor("orphan") + ".owner"), Owner.This.Format() + "\n");
    }
}
```

The parent:

```csharp
[Fact]
public void AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds()
{
    var root = _owned.Dir("s6-orphan");
    var env = AllThreeAt(root);
    env["COAI_S6_ORPHAN_CLAIM"] = "1";
    env["TESTSTODELETE_RUN"] = null; // SuiteChild removes a variable given as null: the child's own record is what the audit reads

    var result = SuiteChild.Run(["--filter-class", "*OrphanClaimChild"], env, ChildTimeout);

    result.TimedOut.Should().BeFalse(result.StdErr);
    result.ExitCode.Should().NotBe(0, "the audit fails the run");
    (result.StdOut + result.StdErr).Should().Contain("tests: this run left 1 folder(s) it claimed").And.Contain("1 claimed in the temp root");
    Directory.GetFileSystemEntries(root).Should().BeEmpty("the audit removes what it names, and the run's end removes the rest");
}
```

The plant's spelling comes from the template's `A_run_that_left_a_folder_throws_one_exception_and_leaves_the_temp_root_clean`. These new lines use no temp API, so they add no baseline entry.

**RED before the call, shown once, in `CoaiMcp.Tests`.** In step 2, first set the fourth op and the copy to `() => RealAudit(RunTemp.Folder, RunTemp.RegistryWithoutTheRunFolder())` and leave `RunTempFixture.Dispose` as it is. Run `M --filter-method "*AnOrphanClaim_IsReportedByTheAudit*"`. It is red: the child exits 0, because `RunTempFixture` disposes first and `Discover` answers `[]`. Quote the failure in `module_tests.md`. Then apply § 11.1 and § 11.2 and run all four. Each must be green.

**Expect it to find leaks.** This turns on a check that was off through S3, S2-B, S4 and S5, on four suites, and `Discover` now reads inside the run folder. A red `coai tests: this run left <n> folder(s) it claimed` names a real leak: an `OwnedDir` nobody disposed, or a claim. **Fix each one in the test that made it, before the PR merges. Never weaken the audit. Never add an entry to skip it.** Record each one found in `module_tests.md`.

**D9's self-test.** `LeftoversSelfTest.cs` leaks on purpose only under `COAI_LEFTOVERS_SELFTEST`. CI asserts that the run FAILS with the audit's first line (§ 12). This is the proof that xUnit fails a run on a fixture-cleanup exception here.

## 12. The CI steps, per job

`.github/workflows/ci.yml` at this head. `release.yml` gets only the two comment lines of § 10.4. `sonarcloud.yml` gets nothing. Run `actionlint`.

### 12.1 `build-test`

Node `:32` and the mount's `npm ci` `:38` are already before the build. The wrapper runs in `Test` (`:58`).

| Order | Step | Needs |
|---|---|---|
| after `Test` `:57-58`, before `Test the bench` | NEW `The audit fails a run that leaves a claimed folder` (below) | the build |
| after `Lint suppressions only shrink` `:174-189` | NEW `The raw-temp baseline only shrinks` (§ 12.4) | the mount, `npm ci` |

```yaml
      # The end-of-run audit has teeth (E5 S6, plan D9; the template's step). LeftoversSelfTest leaks a folder on purpose, ONLY when COAI_LEFTOVERS_SELFTEST
      # is set; this step is green only on a NON-zero exit with the audit's first line and the folder's name on its own line, and with nothing of the case left.
      - name: The audit fails a run that leaves a claimed folder
        run: |
          set -o pipefail
          owner="u$(id -u)"
          for entry in "1:never disposed:selftest" "failed-dispose:disposed but not removed:selftest-failed"; do
            IFS=: read -r value words what <<< "$entry"
            if COAI_LEFTOVERS_SELFTEST="$value" ./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversSelfTest" 2>&1 | tee "$RUNNER_TEMP/selftest.log"; then
              echo "the end-of-run audit did not fail the run ($value)"; exit 1; fi
            first=$(grep -E "(^|[[:space:]]:[[:space:]])coai tests: this run left [0-9]+ folder\(s\) it claimed.*[0-9]+ $words" "$RUNNER_TEMP/selftest.log" | grep -v "testsToDelete-[uw][0-9]" || true)
            [ -n "$first" ] || { echo "the run failed, but not with the audit's first line (counts and '$words', no path) ($value)"; exit 1; }
            grep -Eq "^[[:space:]]+$words: .*/testsToDelete-[uw][0-9]{1,10}-coai-$what-[A-Za-z0-9]{6}" "$RUNNER_TEMP/selftest.log" \
              || { echo "the audit's message does not name the folder on a line of its own under '$words' ($value)"; exit 1; }
            left=$(find "${TMPDIR:-/tmp}" -maxdepth 1 \( -iname "teststodelete-${owner}-coai-$what-*" -o -iname "teststodelete-${owner}-coai-run-*" \)) \
              || { echo "the temp root could not be listed"; exit 1; }
            [ -z "$left" ] || { echo "the run left this behind: $left"; exit 1; }
          done
```

The `coai-run-*` arm is coai's: here the case's folder lives inside the run folder, so the run folder is what could stay. CI has no second run of this user in that moment, so the arm is exact.

### 12.2 `macos`

| Order | Step | Needs |
|---|---|---|
| after `setup-node` `:277-279` | NEW `Shared instruction dependencies`: `npm ci --ignore-scripts --prefix .agents/conventions` | the statement's CI legs; `yaml` for the Node guard |
| `:282` | Add `src/test/testLeftovers.test.mjs` to the file list of `run-tests.mjs` | the mount, `npm ci` |
| `:305` | `The stat layout self-test` stays the FIRST dotnet test step. Unchanged. | |
| `:315`, `:319` | `Leftovers`, `Test`. Unchanged. The filter `CoaiMcp.Tests.Leftovers` now takes the guard's tests too. | `node` on PATH (`:279`), the mount (`:273`) |
| after `Test` `:319-321` | NEW the audit step, the bash above, with `if: ${{ !cancelled() && steps.build.outcome == 'success' }}`. macOS has bash 3.2 and BSD `grep` and `find`; the commands above are the template's portable subset. | |

### 12.3 `windows`

| Order | Step | Needs |
|---|---|---|
| after `setup-node` `:380-382` | NEW `Shared instruction dependencies`, as above | |
| `:394` | Add `src/test/testLeftovers.test.mjs` to the file list. Its header comment (`:383-387`) says "It needs no `npm ci`": that sentence is about the sweep test; say what is true now. | |
| `Test` `:405-406` | Unchanged. It runs the wrapper. | `node`, the mount |
| after `Test`, before `Run folder — Team server` | NEW the PowerShell audit step (below) | |

```yaml
      # The same teeth in PowerShell (the template's step). Both values; a listing error is red, never ignored.
      - name: The audit fails a run that leaves a claimed folder
        shell: pwsh
        run: |
          $ErrorActionPreference = 'Continue'
          $exe = './src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests.exe'
          $cases = @(
            @{ Value = '1'; Words = 'never disposed'; What = 'selftest' },
            @{ Value = 'failed-dispose'; Words = 'disposed but not removed'; What = 'selftest-failed' }
          )
          foreach ($case in $cases) {
            $env:COAI_LEFTOVERS_SELFTEST = $case.Value
            $log = (& $exe --filter-class '*LeftoversSelfTest' 2>&1 | Out-String)
            $code = $LASTEXITCODE
            Remove-Item Env:COAI_LEFTOVERS_SELFTEST
            Write-Host $log
            if ($code -eq 0) { throw "the end-of-run audit did not fail the run ($($case.Value))" }
            $firstLines = @($log -split "`r?`n" | Where-Object { $_ -match ('coai tests: this run left \d+ folder\(s\) it claimed.*\d+ ' + [regex]::Escape($case.Words)) -and $_ -notmatch 'testsToDelete-[uw][0-9]' })
            if ($firstLines.Count -eq 0) { throw "the run failed, but not with the audit's first line (counts and '$($case.Words)', no path) ($($case.Value))" }
            $named = '(?m)^\s*' + [regex]::Escape($case.Words) + ': .*[\\/]testsToDelete-[uw][0-9]{1,10}-coai-' + [regex]::Escape($case.What) + '-[A-Za-z0-9]{6}\s*$'
            if ($log -notmatch $named) { throw "the audit's message does not name the folder on a line of its own under '$($case.Words)' ($($case.Value))" }
            $left = @(Get-ChildItem -Force -LiteralPath $env:TMP -ErrorAction Stop | Where-Object { $_.Name -match ('^testsToDelete-[uw][0-9]{1,10}-coai-(' + [regex]::Escape($case.What) + '|run)-') })
            if ($left.Count -gt 0) { throw "the run left this behind: $($left.FullName -join ', ')" }
          }
          # Actions appends `exit $LASTEXITCODE` to a pwsh step: the self-test's EXPECTED non-zero exit would leak out as the step's result
          # (measured on dew_flow_mcp PR #58). Every check above throws on failure, so reaching here is green.
          exit 0
```

### 12.4 The shrink step, the one creation, and (q)

Three runs of one command. They differ so:

| | The standing step | The one creation | (q), the final content |
|---|---|---|---|
| Where | `build-test`, every later PR | one temporary step on #945 | by hand, a full clone, #945's last head |
| `--base` | the PR's base SHA | the PR's base SHA | **the creation commit** |
| `--allow-create` | never | yes, only here | never |
| The file at the base | present | absent | present |
| What it refuses | growth against `main` | a re-creation | growth on this PR after the creation: `new key`, a raised count, a new scope, a changed proof or guard |
| Clone | depth 1 is enough, because the base has the file | `--unshallow`: a creation needs the path's history | **full**: at depth 1 `git log --diff-filter=A` answers HEAD, and a grown baseline passes (measured below) |
| Recorded | the job's result | the run id, the log line, the head SHA, in the PR and `module_tests.md` | a PR comment |

**The standing step,** in `build-test`, in the repository's own shape for a ratchet (the base's SHA, because `origin/main` does not exist in a shallow checkout; `:152-166`):

```yaml
      # The raw-temp baseline only shrinks (E5 S6; common/test-leftovers-baseline.md § The ratchet). Never `--allow-create` here. PR 945 is the one creation, recorded there.
      - name: The raw-temp baseline only shrinks
        if: github.event_name == 'pull_request' && github.event.pull_request.number != 945
        env:
          BASE_SHA: ${{ github.event.pull_request.base.sha }}
        run: |
          set -euo pipefail
          git fetch --depth 1 origin "$BASE_SHA"
          node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline-shrinks --base "$BASE_SHA"
```

S7's first PR removes the `number != 945` clause.

**The one creation.** One commit adds, under that step, this TEMPORARY step. The coordinator pushes it. The run id, the step's log line (`raw-temp-baseline.json: the one creation, allowed — it never had history behind …`) and the head SHA go in the PR and in `module_tests.md`. The next commit removes the step. At the merge `ci.yml` holds no `--allow-create`, and a test pins that: `workflowsStartTestsThroughARunner.test.mjs` gets `no workflow holds the creation flag`. Write that test so that it does not spell the flag as a whole literal in a line a workflow check reads; it reads the workflow files and looks for the flag.

```yaml
      - name: The one creation of the raw-temp baseline (TEMPORARY, E5 S6)
        if: github.event_name == 'pull_request'
        env:
          BASE_SHA: ${{ github.event.pull_request.base.sha }}
        run: |
          set -euo pipefail
          git fetch --unshallow origin
          git fetch origin "$BASE_SHA"
          node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline-shrinks --base "$BASE_SHA" --allow-create
```

**(q), D8 (q): the final content, by hand, at the LAST head before the squash merge.** No workflow step: nothing of it stays on `main`, and S7 has nothing to remove. The main session runs it after the last commit is pushed and CI is green on that head. A later commit means run it again.

In a fresh full clone, in a new scratch folder:

```bash
git clone https://github.com/oleksandrdubyna88/dew_flow_connect_other_ais.git coai-q
cd coai-q
git checkout feat/test-leftovers-e5-producer
git submodule update --init .agents/conventions
npm ci --ignore-scripts --prefix .agents/conventions

test "$(git rev-parse --is-shallow-repository)" = false
HEAD_SHA=$(git rev-parse HEAD)
test "$HEAD_SHA" = "$(gh pr view 945 --repo oleksandrdubyna88/dew_flow_connect_other_ais --json headRefOid -q .headRefOid)"
test "$(git log --format=%H --diff-filter=A -- raw-temp-baseline.json | wc -l)" -eq 1   # added once: a second add would hide growth
CREATION=$(git log -1 --format=%H --diff-filter=A -- raw-temp-baseline.json)
test -n "$CREATION"
node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline-shrinks --base "$CREATION"; echo "exit $?"
```

Green is exit 0 and the line `raw-temp-baseline.json: only shrinks against <CREATION>`. Compare `$CREATION` with the commit you recorded for the creation run. Then record one PR comment: `HEAD_SHA`, `CREATION`, the tool's line, the exit code, and the id of the CI run on `HEAD_SHA` in which both wrappers are green (`LeftoversRepositoryGuardTests` in `build-test`, `macos`, `windows`; `testLeftovers.test.mjs` in `extension`, `macos`, `windows`). Delete the clone. Exit 1 is a stop: the baseline grew after its creation. Remove the entry and its site; do not re-key.

Measured in a private repository at the pin. A baseline grown after its creation, in a full clone: `git log` answers the creation commit, and the check says `new key … is not in <creation>`, exit 1. A shrunk one: `only shrinks against <creation>`, exit 0. The same grown baseline at depth 1: `git log` answers HEAD, and the check says `only shrinks against <HEAD>`, exit 0. That last line is why (q) needs the full clone. `--print-baseline` cannot do this: it reads no `.cs` and prints `scopes: []`. `--baseline` already reconciles entries and sites both ways; what is open is growth after the creation.

## 13. The planted violations, per root

By hand on the real tree, recorded in the PR, each reverted (the statement's seeded violations). A plant is one new untracked file, so a revert is one `rm`. The C# guard reads the tree when the test runs, so no rebuild: after one build run
`./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversRepositoryGuardTests"`. The Node guard: the command of § 17. Each must be red and name the plant's file and line.

| Root | The plant (file, content) | Guard | Class |
|---|---|---|---|
| `src_mcp/tests/` | `ZzPlant.cs`: `var d = Directory.CreateTempSubdirectory("x");` | C# | `raw-temp` |
| `src_mcp/tests_leftovers/` | the same. `EveryLeftoversFile_IsInTheManifestOrIsCoaisOwn` is red too, as expected. | C# | `raw-temp` |
| `src_mcp/tests_fakecli/` | the same | C# | `raw-temp` |
| `src_server/tests/` | the same | C# | `raw-temp` |
| `src_bugs/tests/` | the same | C# | `raw-temp` |
| `src_bench/CoaiBench.Tests/` | the same | C# | `raw-temp` |
| `src_vs_code/src/test/` | `zzPlant.test.mjs`: `const d = mkdtempSync(join(tmpdir(), 'x-'));` | Node | `raw-temp` |
| `scripts/` | `zzPlant.mjs`: the same line | Node | `raw-temp` |
| `.github/workflows/` | `zz-plant.yml`: a job with `run: d=$(mktemp -d)` | C# | `raw-temp` |
| `.github/workflows/` | `zz-plant.yml`: a job with `run: rm -rf /tmp/<prefix>-*` | both | `owner-blind` |
| `.github/scripts/` | `zz-plant.mjs`: `const d = mkdtempSync(join(tmpdir(), 'x-'));` | both | `raw-temp` |
| `http/` | `zz-plant.mjs`: the same | C# | `raw-temp` |
| `src_vs_code/scripts/` | `zz-plant.mjs`: the same | C# | `raw-temp` |
| a `.sh` in a new folder `deploy/zz/` | `zz.sh`: `d=$(mktemp -d)` | both (not on the frozen list) | `raw-temp` |

Two more, each a `baseline` finding, in each guard once (a `csharp` scope, then a `node` scope):

- **A proof that is missing:** delete the marker line of `src_server/tests/RunFolderTests.cs`; then of `scripts/runFolder.test.mjs`. Red naming `proof missing`; every entry of that scope then also stands as `raw-temp`.
- **A stale entry:** change one line of a baselined site in `src_bugs/tests`, then in `src_vs_code/src/test`, so its hash matches nothing. Red: the new line `raw-temp`, and the entry `baseline` "no line".

## 14. RED-GREEN-RED

The tests are written first and seen red for the right reason. Quote the first failure line in `module_tests.md`. A planted fault never stays.

| # | What | Observed RED: the exact step | Then |
|---|---|---|---|
| R1 | `This_repository_passes_the_guard` | Step 3, before any site work. Expect `owner-blind` 7, `raw-worktree` 23, `shared-store` 2, `docker-run` 1, `raw-temp` 638 (§ 3), and more only if steps 1 and 2 added a site, which they must not. Record the counts. | Green at step 8. |
| R2 | **Must-not-copy #2, C#.** The template's `BreakIt` rows `:150-166` (split over `+`, `string.Concat`, holes, `\u`, `\x`, a character, a one-character class, a half, a middle piece) each ONE finding | Plant: in `IsFolded` return `false`. Red: those rows (0 findings). This is on the copy, so the diff check is red too, as expected. | Revert, green. |
| R4 | **Must-not-copy #1, the audit's sibling.** The template's `A_sibling_childs_folder_survives_the_audit_of_a_run_whose_record_it_shares` (`:95`), and coai's process test in `LeftoversCoaiLayoutTests`: `TwoChildSuitesUnderOneRun_TheFirstToFinish_LeavesTheSecondsFolder`. Two `SuiteChild` runs of a child-only class, both with `TESTSTODELETE_RUN` set to one live record of this user, both making an `OwnedDir` in ONE private root. Child B makes its folder and waits on a go-file; child A makes, disposes and ends, so its audit runs. Assert A exit 0, B's folder and claim byte-identical; then go; B exit 0. | Plant in `LeftoversAudit.Discover`: drop the `Owner.RunRecord(env, _ => { }) != Owner.This` clause and compare with the run's record; and make the fixture scan the private root. Red: B's folder is gone. | Revert, green. |
| R5 | The audit wiring: `AGreenRun_ReportsNoLeak_AndEndsTheRunFolder` (a child run on a class of one passing test: exit 0, no `this run left` line; after the run the run folder and its claim are gone) | Plant: the fourth op's lambda with the template's `RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())` inside the `try`. Red: exit not 0, `never disposed: …-coai-run-…`. | Revert, green. |
| R6 | The self-test's teeth | Run § 12.1's loop by hand with the audit set back to `audit: null`: "the end-of-run audit did not fail the run". | Wire it, loop green, on Linux. |
| R7 | The audit reads only the run: `LeftoversCoaiLayoutTests.TheRegistry_LeavesOutTheRunFolder_AndKeepsALeak` | Plant: `RegistryWithoutTheRunFolder` returns all. Red. | Revert, green. |
| R8 | The second pass, C#, in `LeftoversBaselineTests`, on private git repositories: a second identical line ⇒ `raw-temp` (round-2 finding 1); an entry swapped under the same total ⇒ one `raw-temp` and one `baseline`; a stale entry; an entry outside every scope; a proof absent; a proof with no marker; a proof with the marker of another root; `class` not `raw-temp`; a malformed file; a moved line keeps its entry; another guard's scope is not judged (a `node` scope with a stale entry: nothing from this guard) | Written before `LeftoversBaseline.Apply` has a body: every case red on `Apply` returning the report unchanged. | Green. |
| R9 | `LeftoversBaseline`: no answer, a bad answer | `node` set to a missing name ⇒ ONE `baseline` finding, not an exception. Plant: catch nothing. Red with `Win32Exception`. | Revert, green. |
| R10 | The inventory, both wrappers | Add a valid marker to a real file: red, "in the tree, not in the inventory". Remove a row: the reverse. Insert a blank line above a marker: red, both messages. **Change the text of a site under its marker: red, "the site changed under its marker", in the C# comparer and in N3.** | Revert, green. |
| R11 | The coai roots: `The_scan_sees_the_coai_roots` in `LeftoversCoaiLayoutTests`, in-process with the template's `Tree` shape: one violation at `src_mcp/tests_leftovers/X.cs`, `src_mcp/tests_fakecli/X.cs`, `src_server/tests/X.cs`, `src_bugs/tests/X.cs`, `src_bench/CoaiBench.Tests/X.cs`, `src_vs_code/scripts/x.mjs` ⇒ one finding each; at `.agents/conventions/x.sh`, `.claude/hooks/x.sh` ⇒ none; a frozen-list path (`src_server/install-cli.sh`, so that this test adds no pair to § 10.6) ⇒ none; a product `src_mcp/core/X.cs` ⇒ none | Written before the ops of § 6.3: red on `FilesScanned`. | Green with the ops. |
| R12 | `TheBaselineIsFound_FromBothPlaces` (the re-review's finding e2), in `LeftoversCoaiLayoutTests`: a `SuiteChild.StartInfoFor` start with `WorkingDirectory` set to the project folder, then to `AppContext.BaseDirectory`; a child-only test writes `File.Exists(Path.Combine(RepositoryRoot.Path, "raw-temp-baseline.json"))` to an out file | Before step 8: both say `False`, so the test is red. | Green at step 8. It pins that `RepositoryRoot` walks up from the binary, whatever the working folder is. |
| R13 | N6, N7 | Each planted growth is red in the test by construction; first run before the wiring: red on the missing helper. | Green. |
| R14 | `The_baseline_file_exists`: `File.Exists`, and a regular file (`!FileAttributes.ReparsePoint`) | Before step 8: red. | Green at step 8. |
| R15 | **Must-not-copy #2, Node half, as narrowed: N8. Its RED is by hand, once, against a patched copy of the guard**, because there is no reader to switch off, and the mount must stay clean | In a scratch folder: copy `.agents/conventions/tools` and `common`, and link its `node_modules`. In the copy's `leftovers-check.mjs` change `legacyNameHits(line)` at `:367` to `[]`, then `verdicts[index] === "broad"` at `:376` to `false`. For the length of the run point `testLeftovers.test.mjs`'s `guard` at the copy. Red: N8's first row, then its second. Quote both in the PR. | Restore the test's line, delete the copy, green. Never commit it. |
| R16 | The orphan-claim plant, four suites (§ 11.4) | Once, in `CoaiMcp.Tests`, with the audit wired and `RunTempFixture.Dispose` still ending the run: red, the child exits 0. | The ONE ordered call. All four green. |
| R17 | The seam: `LeftoversCoaiLayoutTests.DisposingAFixtureBuiltThroughTheSeam_DoesNotEndTheRun`: `new LeftoversSweepFixture(() => SweepResult.Empty, _ => { }, () => { })`, `await DisposeAsync()`, then `Directory.Exists(RunTemp.Folder)` and `RunTemp.Refusal()` is null | Plant: `RunTemp.EndOfRun()` in `DisposeAsync`. Red here, and the rest of that run fails as well. Run this one filter only. | Revert, green. |
| R18 | `RunTempFixture` ends nothing: `typeof(RunTempFixture).GetInterfaces()` holds no `IDisposable`, in `LeftoversCoaiLayoutTests` | Written before § 11.2: red. | Green. |
| R19 | `AFailureBeforeTheContainer_LeavesNoFolder` (§ 9.4) | Before the move: red, the folder stays in `tmp`, and the premise holds. | The move, green. |
| R20 | The smoke folder's owner-form name (§ 9.4), POSIX | Before the move: red, the leaf is `tmp.XXXXXX`. | Green. |
| R21 | E9 | Plant: a new `src_vs_code/scripts/zz-importer.mjs` that imports `./seam-catalog.mjs`. Red, naming both. | `rm`, green. |
| R22 | E10 | Plant: one comment naming `probe-agy-write-block.mjs` in an existing `.cs` test. Red, a pair not on the list. Then remove a row of `namedBy`: red. Then add a row no test holds: red. | Revert, green. |
| R23 | `TheRawTempRows_AreNotMoreThanAtCreation` (§ 7.3) | Add one `raw-temp` row and marker on a real line with the constant unchanged: red. | Revert, green. |
| R24 | The kind 5 pin, **by hand, once, for one class** (§ 10.7) | In `TheImageChecksEachInstallerTests.Run`, before the pin, set `start.Environment[RunTempFolder.Variables[2]] = "/elsewhere";`. Run `S --filter-class "*TheImageChecksEachInstallerTests"`. Red: `TMPDIR='/elsewhere' is outside this run's folder`. Quote it in the PR. | Revert, green. |

`*RunTempTests` keeps `RestoreIsIdempotent_AndPutsTheOldValuesBack`: the plan's pin that `Restore` is safe to run twice.

**The rows of § S6's scenarios and where they are:** every class red and green with a valid exemption: the template's `BreakIt`; the token: template `:390-620` and N5; a mount without the detail's table: template `MountedSelectorPick` and its tests; a scan of nothing: template `:334-346`, § 7.1 step 3, N2, N4; the frozen list pinned: template `:655-665` as changed; `release.yml` ⇒ 0: § 13 and the real tree.

## 15. The break-it list

By hand on the real tree, recorded in the PR with the first line of each failure, reverted (the statement's break-its).

| # | Do | Expect |
|---|---|---|
| B1 | One `Directory.CreateTempSubdirectory("x")` in an existing `src_server/tests` test | wrapper red: `src_server/tests/<file>:<line> — raw-temp — …` |
| B2 | One `mkdtempSync(join(tmpdir(), 'x'))` in an extension test | N1 red, the file and line in the tool's output |
| B3 | One `<prefix>-x-` literal in a `src_mcp/tests` test | red `legacy-name` and `owner-blind`; in a `.mjs` test under `scripts/`: red `legacy-name` |
| B4 | `git submodule deinit -f .agents/conventions` (restore: `git submodule update --init .agents/conventions`, then `npm ci --ignore-scripts --prefix .agents/conventions`; no network is needed for a commit already fetched) | wrapper red with "an uninitialised submodule registers nothing, run `git submodule update --init .agents/conventions`"; no exception |
| B5 | A second identical `raw-temp` line beside a baselined one | red on the new line only |
| B6 | Remove one entry from the baseline | red `raw-temp` on that site; `--baseline-shrinks` passes (it is a shrink): the site must go too |
| B7 | Add a new key to the baseline after the creation; raise a count; add a scope; change a proof | `--baseline-shrinks --base HEAD` exit 1, each with its sentence |
| B8 | Replace the baseline by a symlink to a copy (POSIX) | wrapper and Node guard: `baseline` "is a symlink"; shrink check exit 2 |
| B9 | Delete the `RunTempFixture` line of one `RunTempStart.cs` and rebuild | the proof stands or falls as S3 says; report what you see (nothing in S6 should weaken it) |
| B10 | A marker with reason `needed` on a real site | red `exemption` and the site's class in both guards |
| B12 | An exemption added without its inventory row | red, the inventory check (R10) |
| B11 | A marker for `owner-blind` | red `exemption`, and the line stays red |

## 16. F24's second acceptance check

D8 (l); the parent's item 3, row (l), and item 4.7.6. It runs after coai #947 and after `main` is merged into this branch, and before the commit that creates the baseline.

**Where it goes.**

- **The PR:** a comment on #945. The coordinator ran the check on this head and recorded it there (the comment "F24's acceptance check, second run (the one that gates S6)", 2026-11-05, head `e8355d2b`, the pin `bd795847`, the WSL machine). It holds: the Node proof check 2 → 0; `RunTempTests` 21 of 21 with the five forced runs; both Node proof files 6 of 6.
- **`research/module_tests.md`:** a section "F24's second acceptance check", with the head SHA, the pin SHA, the machine and a link to that comment. You write this section in step 7. It is part of the record the plan asks for.

**Do not run the Node half or the mcp forced runs again.** Read the comment in step 0. If it is not there, stop and ask the coordinator.

**What is left, and it is yours: the C# half of the proof check.** The record says so under "Limits". `TheProofCheck_ClosesForEachScope` is new code of S6, in `src_mcp/tests/Leftovers/LeftoversBaselineTests.cs` (§ 5). It is a test, not a by-hand step, and it stays:

- A theory over the four `csharp` scopes: `src_mcp/tests/` with `src_mcp/tests/RunTempTests.cs`, and `src_server/tests/`, `src_bugs/tests/`, `src_bench/CoaiBench.Tests/` each with its `RunFolderTests.cs`.
- For each scope, a private git repository from `_owned` that holds a copy of the REAL proof file at its real relative path, and a `raw-temp-baseline.json` with that one scope, `guard: csharp`, and no entries. Fixtures ask git for the root (§ 7.2).
- `LeftoversBaseline.Apply` with the proof file discovered and the marker line taken out of the copy ⇒ one `baseline` finding naming `proof missing`.
- The real copy, marker in place ⇒ no finding.

Command: `M --filter-method "*TheProofCheck_ClosesForEachScope*"`. Before step 7 it is red on the marker (4 `proof missing`). After step 7 it is green (0). Quote both. Add a comment to #945 and the same lines to the section above, before step 8.

**The other three C# suites' forced runs** run in CI: `--filter-class "*RunFolderTests"` on server, bugs and bench, in the jobs D8's table names. Name that run's id in the section. **On red, stop.** Do not create the baseline. It is a fault in the release or in the host, and it goes to the coordinator.

For the record, the five forced runs on the real C# host, each exit 78, ONE stderr line starting `coai tests: no test ran — `, the sentinel untouched: the folder (`TEMP`, `TMP`, `TMPDIR` at a missing path); `COAI_RUNTEMP_SELFTEST=fail-claim`; `mismatch-temp`; `mismatch-tmp`; `mismatch-tmpdir`. `DiscoverySentinel`'s `MemberData` (`src_mcp/tests/RunTempChildren.cs:28`) writes the sentinel at discovery, so "untouched" means "before discovery".

## 17. Commands

```bash
# after the pin bump
git -C .agents/conventions rev-parse HEAD        # bd79584…
node .agents/conventions/tools/rules.mjs check --repo .
npm ci --ignore-scripts --prefix .agents/conventions

# build (one at a time, lowered)
nice -n 19 dotnet build dew_flow_connect_other_ais.slnx -c Release -m:4

# the narrow runs first
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*TheAdaptedCopyTests"
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversGuard*"
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversAudit*"
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversBaseline*"
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*LeftoversRepositoryGuardTests"
./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-namespace "CoaiMcp.Tests.Leftovers"

# the four executables, through the lowered runner (never `dotnet test`)
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests
node .agents/conventions/tools/low-priority.mjs ./src_server/tests/bin/Release/net10.0/CoaiServer.Tests
node .agents/conventions/tools/low-priority.mjs ./src_bugs/tests/bin/Release/net10.0/CoaiBugs.Tests
node .agents/conventions/tools/low-priority.mjs ./src_bench/CoaiBench.Tests/bin/Release/net10.0/CoaiBench.Tests

# the Node side
(cd src_vs_code && npm ci && npm run lint && npm test)
node scripts/run-lib-tests.mjs
node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "the multi-model review extension and server"
# the one creation and (q): § 12.4, never from this list

# the family checks and the workflows
node .agents/conventions/tools/plan-lifecycle.mjs
node .agents/conventions/tools/pin-check.mjs
actionlint
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
```

A private short `TMPDIR` as S5 used it (`TMPDIR=/tmp/s6t`), empty after each whole run except node's own `node-compile-cache`. Check that it is empty, and say so.

## 18. Definition of Done, one to one

Every line of the plan's S6 DoD and of D8's conditions. A box stays open until its evidence is in the PR.

**§ *S6*'s DoD (epoch 6).**

| Plan's line | How it is met | Where it is proved |
|---|---|---|
| Every class red once and green with a valid exemption | the template's `BreakIt` rows | `*LeftoversGuardTests`, three legs |
| The checks green on the whole tree, both guards, three legs | § 9, § 10 | `This_repository_passes_the_guard`; N1 |
| The inventory equal to the guard's list; each row holds its site's hash; the count at creation binds | § 7.3, § 10 | `*LeftoversInventoryTests`, N3, R10 |
| Every `raw-temp` exemption's reason proved on the PR | § 10's kinds, the callers' lists, the read records, the pairs, the pins | E9, E10, the kind 5 pins, the PR text |
| The positive Node detection test green and the one sentence in the PR (row 2's Node half, narrowed) | N8, § 7.4 | `testLeftovers.test.mjs`; R15; PR text |
| The two split fixtures plain | § 9.2 | N1; the search's output in the PR, labelled a search |
| The orphan-claim plant green in each of the four suites through the ONE ordered call, its RED shown once | § 11 | R16; the four filters |
| The smoke script's trap moved with its RED observed | § 9.4 | R19 |
| The audit self-test red for the right reason on three legs | § 12 | the three steps' run ids |
| F24's second check recorded | § 16 | #945; `module_tests.md`; the test's two results |
| The baseline created once, its creation step removed, no workflow holding the flag | § 8, § 12.4 | the creation run's id; `no workflow holds the creation flag` |
| The shrink check against the creation commit green at the last head | § 12.4 | the PR comment |
| Every S5 deferral listed with where it was handled | `research/module_tests.md` § *Deferred sites*, repeated in the PR | PR text |
| The squash merge carries S3, S2-B, S4, S5 and S6 with `TheProducerAndTheSweep_LandTogether` green | no change; it passes on all four suites | `*RunFolderTests`, `*RunTempTests` |
| The pin at `bd795847` | done | `rules.mjs check`, `pin-check.mjs` |
| `module_tests.md` says all of it | § 5 | `module_tests.md` § *E5 S6* |

**The stories' § *Definition of Done*, S6's other boxes.**

| Box | How | Where |
|---|---|---|
| A multiset; the second-identical-line test RED then GREEN | § 8; R8, N7 | `module_tests.md` |
| Every must-not-copy row has its RED-GREEN-RED recorded | #1: R4. #2, C#: R2. #2, Node half: as narrowed, a positive test with its RED by hand, R15 | `module_tests.md` |
| ONE file, one scope per tree; every scope's proof holds under every invocation of its tree. **S7 reads "the two baseline files" as this one file with scopes** | § 8; the D10 inventory of S4 (`module_tests.md:6206`), re-read: S4 routed `sonarcloud.yml:302` | R14; PR text |
| Created only after F24 is released and pinned; seeded violations red in every actual root; a missing proof and a stale entry each a `baseline` finding | § 4 step 0; § 13 | PR record |
| The `RepositoryRoot` baseline test from both folders | R12 | `module_tests.md` |

**D8's conditions.** S6 proves only some; the others are named so that nobody thinks S6 met them.

| Condition | State after S6 | Whose |
|---|---|---|
| (a) S2-A on `main` | done (#940) | — |
| (b) one PR, every S5 deferral in it, the repository-wide checks green, as reworded for epoch 6 | S6's wrappers | S6 |
| (c) the barrier: the WSL machine's administrator scan recorded; the owner's Windows machine by statement | **open; gates the MERGE, not this build** | the owner |
| **(d) the legacy clean-up record, per machine. This IS a condition.** | **WSL's record: `node scripts/old-coai-leftovers.mjs list` answered `0 entries in /tmp` on 2026-11-05, nothing to remove. It goes on the PR as that machine's record.** The owner's Windows machine is not tested on, by the owner's statement | the main session puts it on the PR |
| (e) the plan gate, the code gate, the legs | the code gate on this PR | the main session |
| (f) the baseline rule released and pinned; F16 (b) pinned | the pin at `bd79584` | done |
| (g) F3 released and pinned | the pin at `bd79584`; S6's N1 is the first Node declaration, "zero whole literals, F30 open" | done; N1 is S6's |
| (h) the parent's other conditions | one evidence line per row of the opening table | the main session |
| (i) F24 released and pinned before S6 creates the baseline | checked in § 4 step 0 | done |
| (j) the diff check green | `TheAdaptedCopyTests` with 52 entries | S6 |
| The merge-gate table, replacing (k) | unchanged cells, plus S6's new wrapper in each mcp cell | CI |
| (l) F24's acceptance check before the baseline | § 16 | the coordinator's record, then S6's C# half |
| (m) D11's rollback text in the PR description | S2-B built the switch. The description still lists it as to do | the main session |
| (n) the native series' evidence | S4's; S6 adds item (3), "no finding, no row" | S6 for item (3) |
| **(o) the audit reads the run folder before the run ends** | the ONE ordered call; the plant in four suites; RED shown once | S6: § 11, R16, R17 |
| **(p) the smoke script's trap before `mktemp`; the reason names the kill window** | § 9.4 | S6: R19, R20 |
| **(q) the baseline's final content at the last head** | `--baseline-shrinks --base <the creation commit>` in a full clone, exit 0, recorded on the PR, both wrappers green | the main session: § 12.4 |

## 19. What only CI can prove, and how each Windows difference is proved

A fixture that passed on Linux has failed only on hosted Windows before. So each difference has a named step.

| Behaviour that differs on Windows | Proved by |
|---|---|
| Line endings: on the hosted runner `*.mjs` check out with CRLF. `.gitattributes` fixes `*.sh`, `*.cs` and one JSON. The hash strips `\r`; the C# `SplitLines` and the JS split agree on the line number. The new copied `.cs` files are `-text` (§ 5). | the wrapper against the COMMITTED baseline and inventory in `windows` `Test`: an entry or row that did not match would be red. N1 and N3 in the same job for the Node side. |
| Backslash paths in `Discover`, the classifier's answer, `createApplier` | the same two steps |
| `git rev-parse --show-toplevel` against a `RUNNER~1` path (§ 7.2) | `LeftoversBaselineTests` in `windows` `Test`; fixtures ask git |
| A disposal that gives up: a held handle on Windows, a read-only folder on POSIX | the PowerShell audit step, value `failed-dispose` |
| `Get-ChildItem $env:TMP` is the real temp root after the run | the PowerShell audit step |
| The ONE ordered call, on Windows, where a held handle can keep the run folder | the `windows` job's `Test` and the plant in `CoaiMcp.Tests`; S3's contract (a folder that stays keeps its claim and one warning) |
| The plant on the other suites | each suite only on the OSes D8's table names: bugs on Linux only, bench not on Windows. Accepted (D8) |
| The smoke stub and the kind 5 pins under Git Bash | `CoaiServer.Tests` in `windows` where bash is present; a test that skips for no bash says so |
| `A_path_with_quotes_…`: no quote in a Windows name (the template's branch) | `windows` `Test` |
| `An_unreadable_candidate_…` skips on Windows (`chmod`) | not proved on Windows; the same message path on Linux and macOS |
| The symlink test of R8 and B8: POSIX only | not proved on Windows; `locateBaseline` is shipped code, tested in `dew_flow_conventions` |
| `kimiWindowsInstallRuns.test.ts`, any `OnWindows_*` you touch: skip on POSIX | `windows` `Test`; you touch none of them on purpose |
| Lengths: no new test may assert a path length; a run folder name is long on Windows (`w` + a SID's digits) | review; the private trees in `LeftoversGuardTests` are the template's and passed on hosted Windows on `dew_flow_mcp` #58 |
| Release jobs (`release.yml` `mcp-binaries`, Windows x64 and arm64, macOS, Linux) now run the guard | release evidence only (D8); first seen at the next tag. They depend on the image's own `node`, as `OwnerClaimVectorsTests.cs:52` does already. |

The first CI run is also the first run of: Node 22 for `testLeftovers.test.mjs` on Windows and macOS; the audit on all four suites on all legs; `sonarcloud.yml` with both wrappers under coverage.

## 20. What is NOT S6

- **S7's drain.** No baseline entry is removed by moving its site, beyond § 9.3. No `CreateTempSubdirectory` becomes `TempDir.For`. The inventory's inline rows stay after S7, permanently (the owner's answer of 2026-11-05).
- **F30.** No folding reader, in coai or anywhere. F30 is the conventions repository's.
- **E2 and E6.** Nothing of `dew_flow_creds_for_devs`, nothing of E6's or E7's repositories. Their guards and pins are theirs.
- **The conventions and the template.** No edit in `.agents/conventions`, `dew_flow_conventions`, or `dew_flow_mcp`. A defect you find there goes to the coordinator.
- **Product code.** No `src_mcp/src`, `core`, `runners`, `src_server/src` or `src_vs_code/src/*.ts` outside `src/test/`. The exceptions: comments in `deploy/*.sh`, `src_server/install-*.sh` and `release.yml`, which change no command; and `src_server/smoke-image.sh`, a test-support script, as § 9.4 says.
- **S3, S2-B, S4, S5 re-work.** Their tests stay. A leak the audit finds is fixed in the test that leaks, and named.
- **The merge conditions that are not S6's** (§ 18), the plan ledger, the parent's entries, the coai feature review, the status lines of the plan.
- **No push.** The coordinator says when.

## 21. Assumptions

1. Facts are at the head in the status line. A later merge of `main` can move lines and counts; § 3, the sizes table and § 10's lines are read again after it. A number that moved is not wrong by itself: explain it by file in the PR.
2. **There is no assumption that the Node guard folds.** It does not (F30). `testLeftovers.test.mjs` pins what it detects on exact lines, and F30's note says the count depends on the line. The RED of that test is by hand, once, against a patched copy (R15). The Node half of row 2 has no standing RED, and no reader can be switched off.
3. A `raw-temp` hit in a literal that makes nothing (the 38 `/tmp` strings of `LocalRuntimeTests` and others, the 7 detector lines of `TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests`) is a site like any other for the baseline, inside its scope. S7 decides how each ends.
4. `src_mcp/tests_fakecli/Serving.cs:140` is a read on its line and the next. Its kind does not depend on who starts the fake CLI.
5. The hosted images keep a `node` on PATH for the release jobs. If not, `OwnerClaimVectorsTests` is red there already.
6. The `raw-worktree` reasons of § 10.2 are from the lines that name each folder, read when this brief was first written, except `AVibeDiskRowExpandsAParentFolderTests.cs:431`, where `at` comes from its caller `:443`, and `WorktreeManagerTests.cs:495`, `:526`, where `real` and `storage` were read only by name. Read them before you write the marker.
7. Exact-line inventory: the row's `line` and `sha256` are kept as exact, with its cost: an edit above a marker makes the test red until the row moves. I did not soften it.
8. `src_vs_code/src/test/tempRoot.ts` is compiled by `npm run compile` and not picked up by `discover` in `run-tests.mjs`, as it is not `*.test.*`. Check it in step 4.
9. `RepoPaths.Root()` and `RepositoryRoot.Path` both give the repository's root on every leg; the first is used by the coai tests that read `shared/`.
10. The orphan-claim child and the kind 5 pin add no `raw-temp` site. If a guard reports one, the line is wrong: rewrite it. Do not add a baseline entry for it.
11. Not run when this brief was written: the code of §§ 7, 9.4, 11 and 16. The measured parts are what §§ 3, 7.4, 7.5, 9.4 and 12.4 say was measured. The template's `:268`-`:274` were read at `197cc76`, and the copy keeps those line numbers.
