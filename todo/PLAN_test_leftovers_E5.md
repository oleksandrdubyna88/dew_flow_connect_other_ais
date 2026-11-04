# PLAN — Test leftovers, E5: coai's run folder takes the owner form, both test sweeps judge by owner, and the product's `coai-*` directories stay untouched

> Status: **revised 2026-11-04 at the parent's epoch-5 re-review (entries 4.1 and 4.2 of the parent's ledger: 4.1 found
> writing the S2-B spec, 4.2 found writing S4's brief); epoch 5 waits for the owner's approval (the record says
> `Narrowed: yes`), and no E5 story takes new code until then.** Epoch 4 was approved by the owner (2026-11-03), and this
> document was merged for it as #943 (`8dc0765c`). The producer PR #945 (draft) holds S3 and S2-B at `6ec356a1`, CI green
> on Linux, macOS and Windows; S4 is a brief only; S5, S6 and S7 are not started. Two changes: (4.1) the promise that a
> killed OLD coai run (a legacy `testsToDelete-coai-run-*` folder) is collected by the next extension run is withdrawn —
> only S2-A's one-off clean-up removes those folders (D4, corrected on this branch at `89494366`); (4.2) the native
> measurement series (`scripts/lib/native-run.mjs` `SERIES_SHAPE`, `scripts/native/*.sh`) LEAVES the prefix family as
> ONE bounded exception — its new shape `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}`, made only by `make-series.sh`,
> removed only by `remove-series.sh`, never collected (§ S4, *[epoch 5]*; the parent's § *Epoch 5*, item 4). P1 is
> closed by the owner's written statement (the parent's PR #124); merge condition (c) for the WSL machine waits for one
> administrator scan. This document keeps no ledger: its changes are entries of the parent. What epoch 5 supersedes
> carries a dated note *[epoch 5, 2026-11-04]* in place; the S4 implementation notes of the same date are NOT changes.
>
> Was: **revised 2026-11-03 at the parent's epoch-4 re-review (entries 3.1 and 3.2 of the parent's ledger, found
> writing S3's build brief); epoch 4 waits for the owner's approval (the record says `Narrowed: yes`), and no E5 story
> takes new code until then.** Epoch 3 was approved by the owner (2026-11-03), and this document was merged for it as
> #942 (`004c55fb`). The producer branch has no code; S3's build brief exists. Two changes: D2 becomes an ADAPTED copy of
> the template (a pinned manifest, permitted transformations per file, a diff check), and the DoD names the suite × OS
> matrix coai's CI runs. A new prerequisite: the conventions' F24 (the baseline proof rule) is released and pinned before
> S6 creates its baseline. S3's fail-closed mechanism is the gated custom `Main` (exit 78). This document keeps no ledger:
> its changes are entries of the parent (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`). What epoch 4
> supersedes carries a dated note *[epoch 4, 2026-11-03]* in place.
> *After the two re-review plan gates (2026-11-03):* each OS's proofs are named CI steps (D8); a rollback procedure is
> D11; F24 needs an acceptance check against the released guard and the real test host before S6; S3 adds a fallback
> check behind the gate, lets pure information flags pass, and keeps the diff check's golden source in coai; S2-B proves
> its sweep runs before any test collection. Gate finding e3 was rejected (the custom `Main` was built and run in the
> S3 brief's probe).

> Was: **revised 2026-11-03 at the parent's epoch-3 re-review (entry 2.2 of the parent's ledger); epoch 3 waits for
> the owner's approval (the record says `Narrowed: yes`), and no E5 story takes new code until then.** S1 is done (#936;
> the pin is at `cc24f9e` since #941). S2-A is done (#940, `d02ed40e`, 2026-11-02). S5 started on 2026-11-03 on three
> branches and stopped with the plan (local work only). S5 is NARROWED here (§ *S5 as revised at the epoch-3
> re-review*): its deferred sites move to S3 and S4, and E5's final Done-when is not smaller. S3, S2-B, S4, S6 and S7
> are not built. This stories document keeps no ledger: a change to it is an entry in the parent's ledger
> (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md` § *Plan changes*). The text below keeps every decision
> and measurement; a statement the revision supersedes carries a dated note *[epoch 3, 2026-11-03]* in place.
> *After the re-review gate (2026-11-03, findings 0–3, all accepted):* S5 has NO stage-A part. Its renames are commit 4
> of the producer PR (S3 → S2-B → S4 → S5 → S6); the eligible-site list of § *S5 as revised* is that commit's input.
> Was: **plan only, nothing implemented yet — the 2026-11-01 re-split against release `105d53c` (wave 2 of the
> rollout, written the way wave 1 re-split E3 and E4 against the released contract and the merged C# helper). First
> split 2026-10-16; plan-gate round 1 applied 2026-10-17; the risk decision of 2026-10-17 applied; the E1 release it
> waited for became `115bbf8` (R5) and then `105d53c` (R7). S1, the pin, is DONE: coai #936 (`df375eb`) pinned
> `105d53c`, merged alone (R6 owner decision 1). Every other story is unbuilt. This re-split is the whole re-review
> the family's change-control rule asks of a plan changed twice after approval. Its coai plan gate, round 1
> (2026-11-01, session `55ddb13b`): verdict *revise*, eight findings, all accepted and applied below (each marked
> *[round-1 finding n]*). Round 2 (same day, same session): verdict **PROCEED**, two findings, both accepted and
> applied (marked *[round-2 finding n]*); the gate also ruled that this plan is a piece of a split already under way
> and is NOT split again. The producer PR DEPENDS on a conventions rule change (round-1 finding 7): stage A and S5
> may start now; the producer stage waits for that rule's release and a pin bump.**
> Stories of epic E5 of `dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md` (§ *Epics*, E5; § *Wave 1*).
> Scope: the run folder both runners already make (`src_mcp/tests/RunTemp.cs`, `RunTempFolder.cs`, `OwnerClaim.cs`;
> `src_vs_code/scripts/run-tests.mjs`), the two test sweeps (`src_vs_code/scripts/sweepTemp.mjs` through
> `run-tests.mjs`; the assembly fixture `src_mcp/tests/TempDirsAreSwept.cs`) and their rule file
> `shared/temp-sweep.json`, one C# owned-resource library (a copy of dew_flow_mcp's `tests/Mcp.Tests/Leftovers/`) shared
> by the four .NET suites, the Node wrap of the shipped reference (`tools/lib/owned.mjs`, `tools/lib/leftovers-sweep.mjs`,
> `tools/leftovers-check.mjs --profile node`), the test runners and script tests that run OUTSIDE a run folder
> (`src_vs_code/scripts/*.mjs`, `scripts/lib/*.test.mjs`, `http/run-contracts.mjs`, `.github/workflows/release.yml`),
> the guard, and the CI legs. The PRODUCT's sweep (`PanelService.PruneOldScratchDirs` and its callers) is out of scope
> and is proven unchanged.
>
> Split by Opus, 2026-10-16; re-split by Fable, 2026-11-01. **One cornerstone: S2** (the sweep, in its two halves),
> `model: fable` — its spec is a separate document, `todo/PLAN_test_leftovers_E5_S2_spec.md`, written by Fable before
> S2 is coded. Models per story below.

**Committed together with this document** (one commit, the shared `common/planning-docs.md` § *A plan split into
epics and stories*): its row in `todo/README.md` under *Currently open* (the row's text is the README's).

The parent's ledger line is unchanged by this re-split and by the epoch-3 revision (the last line of the parent's
`## Epics`; it was `todo/PLAN_test_leftovers_rollout.md:433` when this was written):

```
Cornerstones: 3 of 3 — E1.S1 (the contract text, model: fable), E5.S2 (coai's test sweep: temp-sweep.json, both sweeps, owner liveness, model: fable), E7.S2 (rag_qln's corpus ownership and drop protocol, model: fable).
```

**Parent, contract and templates are cited as paths, never links** (the shared `common/planning-docs.md` § *Across
repositories*): the parent `dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`; the remainder plan with the
owner decisions `dew_flow_conventions · todo/PLAN_test_leftovers_merged_core_release.md` (R6, R7); the follow-ups
`dew_flow_conventions · todo/PLAN_test_leftovers_merged_core_followups.md` (F16–F20); the contract at `105d53c`
(mounted here, the pin): the core `.agents/conventions/common/test-leftovers.md`, the detail
`common/test-leftovers-detail.md` (its `## Tokens` table at `:304-322`, the `coai` row at `:320`), the janitor's
`common/test-leftovers-janitor.md`; the Node reference `tools/lib/owned.mjs`, `tools/lib/leftovers-sweep.mjs`,
`tools/leftovers-check.mjs`, `tools/janitor.sh`; the C# template `dew_flow_mcp · tests/Mcp.Tests/Leftovers/` at
`197cc76` with its promoted plan `dew_flow_mcp · research/PLAN_test_leftovers_E3.md`; the Rust record
`dew_flow_sidecar_rust · research/PLAN_test_leftovers_E4.md` at `e58be09`. *Rule §n* below is a section of the core,
read with the detail (where the two differ the detail decides).

## What changed from the October version, and why

The October document (2026-10-16/17) was written against E1 — `testsToDelete-coai-<what>-<id>`, a sweep of its own
per repository, a claim grammar without an owner segment. Three things moved under it:

| What moved | Where it is now | What it does to this document |
|---|---|---|
| **The contract** (`115bbf8` → `105d53c`) | the name is `testsToDelete-<owner>-<repo>-<what>-<id>` with `<owner>` of `^[uw][0-9]{1,10}$`; the claim is `<owner>:<host>@<os>:<pid>[:<start>[:<ns>]]`, written FIRST by an exclusive create, then the cleanup registered, then an exclusive `mkdir`; the sweep deletes on two keys (scope AND `gone`), by identity through a quarantine rename, lists legacy names; the guard gains `legacy-name` and `owner-blind`; the barrier splits every consumer into a sweep-only stage and a producer stage; the `## Tokens` table lives in the detail | D1 (the name), the whole of S2's liveness and S3's claim order are rewritten; the stages are new; the October `testsToDelete-coai-<what>-<id>` form is now a LEGACY name the guard refuses (rule §1) |
| **Decision 11 of the merged core** (`dew_flow_conventions · research/PLAN_test_leftovers_merged_core.md:128-129`) and R7 | Node consumers WRAP and RUN the shipped `owned.mjs` / `leftovers-sweep.mjs` / `leftovers-check.mjs --profile node`, never port them; F17–F20 were fixed in the reference first so that a wrap inherits the fixes | D3 (an `owned.cjs` port) is withdrawn; S4 is a wrap; D6's guard for the Node trees is the shipped tool under its `node` profile |
| **Wave 1 merged** (E3 `dew_flow_mcp` #58 `4feb245`, E4 `dew_flow_sidecar_rust` #64 `5478109`, 2026-10-31) and the Darwin path (`dew_flow_mcp` `197cc76`, 2026-11-01) | a tested C# helper, sweep, audit and guard exist, with a macOS path measured on a macOS leg; the parent's § *Wave 1* lists the defects the next C# ports must not copy back in | D2 becomes a COPY (byte-identical except the namespace line, `OwnedToken.cs` rewritten), not a design; the *must not copy back in* list below is new; every one of its items has a RED-GREEN-RED test here |
| **coai's own `main`** (`1fe25d19` → `df375eb4`): epic E2 of `todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md` landed as #897 (`215170c4`, 2026-10-20); the pin moved to `105d53c` as #936 (`df375eb4`); the reviewer-rules budget is 90 KB (`src_mcp/runners/Context/RuleFiles.cs:181`) | every `src_mcp` run and every extension run already makes ONE folder `testsToDelete-coai-run-<6 hex>` in the machine's real temp root with a sibling `.owner` claim, points `TEMP`/`TMP`/`TMPDIR` at it, and removes it at the end — so everything a test or the product under test makes in "temp" lives INSIDE it (measured: 0 entries left in a private machine temp after a whole run, `research/module_tests.md:5100-5104`) | the October S4/S5 bulk rename of ~650 sites to `testsToDelete-coai-<what>-` is WITHDRAWN as a rename: under rule §1 *what lives inside an owned resource carries no prefix of its own*. The producer is the run folder, which takes the owner form; the sites inside it keep their `what`. What remains of S4/S5 is (a) the bare-prefix literals that the guard's `owner-blind`/`legacy-name` classes refuse, (b) every raw temp API moving into the helper, and (c) the runners and script tests that run OUTSIDE a run folder. S1 is done. D5 (the product scratch-root seam) is superseded for `src_mcp` and the extension — the three variables ARE the seam — and becomes true for the other three C# suites when they get the run folder (S3) |

*[epoch 3, 2026-11-03] Corrected — the last row's "everything ... lives INSIDE it".* That is true only for the
invocations that run through a runner that makes a run folder: the `src_mcp/tests` executable (every CI and coverage
invocation of it) and the extension's tests under `scripts/run-tests.mjs`. It is NOT true for the compiled extension
tests that `sonarcloud.yml:302` runs directly (`c8 … node --test "out/test/"*.test.js`, no run folder), for
`scripts/**/*.test.mjs`, which `ci.yml:197` runs as a bare `node --test`, or for `src_server/tests`, `src_bugs/tests` and
bench, which have no run folder until S3. Also, the #897 run folder has a legacy name, not the owner form (the Facts
below say so). **Containment is a property of each invocation, not of the file** (D10). The S5 build found this; it is
entry 2.2 of the parent.

Everything the October document measured is kept below, with its date and commit; what it decided and still holds is
kept; what it decided and no longer holds is marked *withdrawn* with the reason.

## Goal

Every folder a coai test run, test script or test-run CI step creates in a shared namespace is the run's own owned
resource, named `testsToDelete-<owner>-coai-<what>-<id>` by one helper per language (C#: the copied library; Node: the
shipped reference wrapped with the token `coai`), claimed first beside itself in the contract's grammar, removed by its
run on every exit path, and — when the run was killed — collected by that user's next run on the two keys of rule §4.
What a test makes INSIDE the run folder carries no prefix (rule §1). Both test sweeps judge by owner and liveness,
never by age; **no test sweep matches a `coai-*` name at any age** (risk decision 2026-10-17), so the product's own
`coai-*` working directories — a live chat, a round's answers, a Team server job, and every product prefix not invented
yet — are never reachable by a test sweep, and the product's six-hour sweep is byte-for-byte unchanged.

**Done when:** the pin carries `105d53c` (S1, done); both test sweeps have stopped deleting on age and the
`PruneOldScratchDirs` characterization test is on `main` (S2-A, stage A); every bare `testsToDelete-` literal outside
the helper is gone from the test trees (S5, before the producer); the C# library is a copy of the template with the
`coai` token, the run folder is on the owner form in all four C# suites and the claim grammar is the contract's (S3);
the owner-aware sweep runs at the start of every C# suite and of the extension runner, lists legacy names and never
fails the run (S2-B); every Node runner, script test and the editor-host run wraps the reference and leaks nothing
(S4); the guard is red on every class of rule §7 and on a scan of nothing, with `owner-blind`, `legacy-name` and
`raw-worktree` at zero and a `raw-temp` baseline in the form a released shared rule authorises — membership plus
content hash, only shrinking (S6, *[round-1 finding 7]*); the baseline drains to zero (S7).

*[epoch 3, 2026-11-03]* The S5 clause above is narrowed. Before the producer PR, S5 removes the bare prefix only from
the ENUMERATED eligible sites (§ *S5 as revised at the epoch-3 re-review*). The deferred sites lose it inside the
producer PR (S3 and S4), and S6's repository-wide zero for `owner-blind` and `legacy-name` is unchanged. So what must be
done BEFORE the producer got smaller; E5's final Done-when did not.
*[after the re-review gate, 2026-11-03]* Narrower still: NOTHING of S5 lands before the producer PR. Both runners fall
back to the machine temp today (`RunTemp.cs:33-47`, `run-tests.mjs:221-230`), the run folder keeps its legacy name
until S3, and a person can start a test file directly; by D10 no S5 site is contained until S3 and S4. So S5's
renames are a commit of the producer PR, after S3, S2-B and S4.
*[epoch 5, 2026-11-04]* The S4 clause ("every Node runner, script test and the editor-host run wraps the reference and
leaks nothing") has ONE bounded exception: the native measurement series is not wrapped and is not a leak — it is a
durable folder with its own maker and remover (§ S4, *[epoch 5]*). And the S2-B clause's "lists legacy names" is read
with D4 as corrected for the parent's entry 4.1: a legacy `testsToDelete-coai-run-*` folder is listed by both sweeps and
removed ONLY by S2-A's one-off clean-up; nothing collects it.

## Facts the split rests on

### Read on `origin/main` `a3b73534` (2026-11-03) — the S5 build and the re-review

- **The pins and stage A.** coai #939 (`2221559a`) pinned `5863c60` (the `raw-temp` baseline rule, #108; the F16 interim
  (b), #107; F21, #109); #941 (`a3b73534`) pinned `cc24f9e` (F22, #112: the reference sweep takes the consumer's token).
  S2-A merged as #940 (`d02ed40e`). The E5 stories #850 merged as `ee7a7b70`; #938 merged as `83f40529`.
- **The released baseline rule** (`.agents/conventions/common/test-leftovers-baseline.md` at `cc24f9e`) fixes ONE file,
  `raw-temp-baseline.json` at the repository root, a regular file, with `scopes` (`root`, `proof`, `guard`: `node` or
  `csharp`) and a multiset of `entries` (`file`, `sha256` of the trimmed line, `line` a hint). A scope's proof is three
  things: the runner makes the run folder before the first test; a run that cannot make it fails before any test; a
  first-ordered test per assembly or package asserts that `TEMP` names the run folder and carries
  `testsToDelete-baseline-proof: <root>`.
- **The invocations that run the test trees** (the inventory of D10):

  | Invocation | Defined at | The temp root it gets | Owner, cleanup, sweep | What a dropped prefix needs first |
  |---|---|---|---|---|
  | `CoaiMcp.Tests` executable | `ci.yml:58` (Linux), `:282` (macOS), `:317` (Windows); `sonarcloud.yml` "Test with coverage" (`dotnet-coverage collect --`); `release.yml:264`, `:353` | the run folder (the `[ModuleInitializer]`, `RunTemp.cs:33`) — except when it cannot make it: then the machine temp, with one warning (`RunTemp.cs:45`, `CreationWarning`) | the run folder's teardown; no test sweep since S2-A until S2-B | nothing for the normal path; the fallback goes in S3 (sc. 3a) |
  | `CoaiServer.Tests`, `CoaiBugs.Tests`, `CoaiBench.Tests` | `ci.yml:77`, `:88`, `:64`; `:285`, `:288` (macOS); `sonarcloud.yml` "Test with coverage"; `release.yml:694`, `:1110` | the machine temp (no run folder) | each test's own `Dispose`, if any | S3 (the run folder in each of the three) |
  | `npm test` → `scripts/run-tests.mjs` | `src_vs_code/package.json` `test`; `ci.yml:373`; again in `npm run package` (`ci.yml:379`); `release.yml:544` | the run folder — except when it cannot make it: then the machine temp (`run-tests.mjs:221-227`, "the tests share the machine's temp as before") | the runner's `finally` | nothing for the normal path; the fallback goes in S4 |
  | the compiled extension tests, directly | `sonarcloud.yml:302` (`ubuntu-latest`) | the runner VM's real `/tmp` | each test's own `after()`; the VM is discarded | S4: the job goes through the runner, or gets a run folder of its own |
  | `npm run test:host`, `test:contract`, `test:seam`, `test:parity` | `package.json`; `ci.yml:425`, `:118`, `:125`, `:146` | the machine temp (each runner script) | the script; `run-host.mjs` leaks (must-not-copy #14) | S4 |
  | `node --test "scripts/**/*.test.mjs"` | `ci.yml:197` | the machine temp | each test's `after()` | S4 (and the start-of-run sweep of S2-B item 2) |
  | the measurement and probe scripts, run by a person | `scripts/*.mjs`, `scripts/native/*.sh` | the machine temp, or `/tmp` on a Linux box | the script | S4 |
  | *[epoch 5, 2026-11-04]* the native measurement SERIES, a person's root-run `make-series.sh` → the phases → `remove-series.sh` (a KEPT line of its own: the parent's § *Epoch 5*, item 4) | `scripts/native/make-series.sh`, `abort.sh`, `remove-series.sh`; `scripts/lib/native-run.mjs:18` `SERIES_SHAPE` | the series root: `/tmp`, or `COAI_NATIVE_SERIES_ROOT` | nobody collects it: `make-series.sh` makes it, `remove-series.sh` removes it (the shape validated, anything else refused); no claim, no sweep, no janitor | nothing is dropped: the name LEAVES the prefix family (`native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}`) in S4, with the survival and grammar tests |

- **The bare-prefix lines, counted for S5** (`git grep -c -i teststodelete`): `src_mcp/tests` 263 lines in 120 files;
  `src_vs_code` 21 lines in 8 files; `scripts/lib` 49 lines in 15 files; `src_server/tests` 1 (`GaugeCaptureTests.cs:30`);
  `http` 0. The shipped Node guard (`--profile node --token coai`) also finds 12 `legacy-name` lines in `scripts/`
  OUTSIDE `scripts/lib`, which no S5 PR covered: `scripts/native/abort.sh:31`, `make-series.sh:30`, `remove-series.sh:22`,
  `scripts/native/native.test.mjs:26`, `:112`, `:420`, `scripts/dotenv-name-vectors.mjs:133`,
  `scripts/probe-kimi-confinement.mjs:132`, `scripts/probe-kimi-tree-hold.mjs:99`, `scripts/probe-native-linux-host.mjs:93`,
  `scripts/probe-vibe-confinement.mjs:92`, `scripts/vibe-home-manifest.mjs:71`.

### Read on `origin/main` `df375eb4` (2026-11-01) — the re-split

- **The run folder exists, in the OLD grammar.** `src_mcp/tests/RunTempFolder.cs:44` `Prefix = "testsToDelete-coai-run-"`;
  `MakeFreeFolder` `:101-112` draws up to `Tries = 5` (`:46`) names and creates with `Directory.CreateDirectory` after an
  existence check (two calls — the file's own remark names the window); `Create` `:78-99` records the three variables,
  makes the folder, registers the restore, moves the variables, and writes the claim LAST (`seams.WriteClaim`). The claim
  is written by `RunTemp.WriteClaim` (`src_mcp/tests/RunTemp.cs:77`, the one writer) as `<host>@<os>:<pid>[:<start>]` —
  and the `<os>` tag is **Node's spelling `win32|linux|darwin`** (`src_mcp/tests/OwnerClaim.cs:38`, the shape regex),
  not the contract's `linux|macos|windows`. `RunTemp.Start` (`RunTemp.cs:34-47`) is a `[ModuleInitializer]`;
  `RunTemp.Real` is the machine's temp root, `RunTemp.Folder` the run's. The Node runner's twin: `run-tests.mjs:114`
  `RUN_PREFIX`, `claimText` `:120`, `linuxStartSeconds` `:130`, `makeRunTemp` `:155` (exclusive `mkdirSync`, claim
  written AFTER the folder), `removeRunTemp` `:185`, `beginRun` `:210`, `main` `:241-251` (`removeRunTemp(folder)` in
  `finally` at `:249`, BEFORE `process.exit(status)` at `:251` — the trap this plan recorded for `run-host.mjs` was
  fixed there). Under rule §1 both names are **legacy names** (second segment `coai`, not of the owner grammar).
- **Only two of the five suites have the run folder.** `src_server/tests`, `src_bugs/tests` and `src_bench/CoaiBench.Tests`
  have no `RunTemp` and no module initializer (`grep -rl 'RunTemp\|ModuleInitializer'` finds nothing there); their
  tests still write `coai-*` into the machine's real temp (24, 4 and 7 `CreateTempSubdirectory` lines), and the Team
  server suite's product code under test makes about a thousand `coai-answers-*`/`coai-repair-*`/`coai-noworkspace-*`
  there per run (`research/module_tests.md:421-423`).
- **The age-only test sweep is still enabled in both runners.** `sweepTemp.mjs:30-37` `leftover` (directory ∧
  `startsWith(prefix)` ∧ not a version cache `:25-27` ∧ not `neverSwept` ∧ last write older than `keepMinutes`),
  `toSweep` `:44-46`, `sweep` `:79-86`, called by `sweepFirst` (`run-tests.mjs:103-111`) as the first line of `main`
  (`:242`); `TempDirsAreSwept` constructor `:76-85` calls `PanelService.PruneOldScratchDirs(RunTemp.Real, now −
  Rule.Keeps, [$"{Rule.Prefix}*"], Rule.NeverSwept)` (`:83-84`). No owner, no liveness in either. The barrier scan
  (`tools/sweeper-scan.mjs` at `115bbf8`, R6 2026-10-31) listed coai's stage A as EMPTY because it looks for the
  `testsToDelete` prefix: the age-only sweep matches `coai-`, and so escaped the scan — but the contract's barrier
  names "a sweeper that ... removes on age" too, and the risk decision of 2026-10-17 already retired it.
- **`shared/temp-sweep.json`** (34 lines): `prefix` `"coai-"` (`:22`), `keepMinutes` 10 (`:23`), `neverSwept` (`:24-33`)
  = `coai-answers-`, `coai-repair-`, `coai-noworkspace-`, `coai-plan-`, `coai-server-job-`, `coai-chat-`,
  `coai-question-`, `coai-check-`; its `why` (`:2-21`) already says the run folder "this prefix never matches".
- **The product's sweep** (unchanged since October, lines moved): `ScratchPrefixes` `src_mcp/src/Server/PanelService.cs:1389-1390`
  (now four: `coai-answers-*`, `coai-repair-*`, `coai-noworkspace-*`, `coai-question-*`), the six-hour call `:1442`,
  `PruneOldScratchDirs` `:1475-1489`, `SweepOne` `:1491`, `DeleteIfDue` `:1514` (with `FolderLock.RemoveIfFree` for a
  question row at `:1526`), `KimiHomesDisposed` `:1544`. Product prefixes in source, all in `neverSwept`:
  `coai-answers-` (`RosterBuilder.cs:280`, `SecurityRoster.cs:154`, `VibeRuntime.cs:173`, `KimiRuntime.cs:199`,
  `RoundScratch.cs:82` by `Path.Combine`), `coai-noworkspace-` (`SecurityRoster.cs:155`, `RosterAssembly.cs:42`),
  `coai-repair-` (`RosterBuilder.cs:282`), `coai-plan-` (`Escalations.cs:373`), `coai-server-job-`
  (`src_server/src/Jobs/ReviewLauncher.cs:150`), `coai-question-` (a constant, `QuestionRowLaunch.cs:95`, `:101` —
  invisible to the literal scans), `coai-check-` (`ConsultCheckScratch.cs:42`), `coai-chat-fast-` (`chatFastSettings.ts:44`,
  covered by `coai-chat-`). NOT in `neverSwept` and not under the temp root: `coai-review-` (`ReviewTreeRoot.cs:26`),
  `coai-wt-`/`coai-wt-trash-` (`WorktreeManager.cs:66-67`, the worktree storage root).
- **The pinning tests** (the October table, re-read): TEST-prefix pins `TempDirsAreSwept.cs:46` (`Prefixes = ["coai-*"]`,
  used at `:289`), `:90` (`Rule.Prefix.Should().Be("coai-")`), `:83-84`, `:137` (`[$"{Rule.Prefix}*"]`),
  `sweepTemp.test.mjs:32` (`rule.prefix === 'coai-'`), its fixtures `:54-73`, `:99-130`, `:170`, `:80`
  (`sweeptest-`); PRODUCT-prefix scans `TempDirsAreSwept.cs:116` (the `CreateTempSubdirectory("coai-…")` regex over
  `src`, `runners`, `core`; the test at `:104-109` asserts `neverSwept ⊇ made`), `sweepTemp.test.mjs:46`,
  `src_server/tests/TheTestSweepLeavesAJobAloneTests.cs:27` (reads only `neverSwept`, `:33-37`). The product-prefix
  scans stay `coai-` and green through every story.
- **Bare-prefix literals** (what the guard's `owner-blind` and `legacy-name` classes will refuse, R6 decision 3):
  246 lines with `testsToDelete-` in `src_mcp/tests/*.cs`, of which **157 are not `testsToDelete-coai-`** (`-kimi-`,
  `-vh-`, `-junc-`, `-sweep-`, `-kimilock-`, `-vibe-`, `-wsl-`, `-statx-`, `-profile-owned`, `-profile-bin`, …) and
  the rest are `testsToDelete-coai-…` (the run prefix and E2's own tests); 32 more outside `testsToDelete-coai-`
  across `src_vs_code/src/test`, `scripts/lib`, `src_server/tests` (`GaugeCaptureTests.cs:30`) and `http`;
  `scripts/lib/native-run.mjs:18` builds `testsToDelete-native-(vibe|kimi)-…`. Repo-wide outside `.agents`: 501
  lines, 143 with the `coai` segment. Every one of them is a finding under rule §7 once the guard lands, and every
  `testsToDelete-coai-…` literal is a `legacy-name` (no owner segment).
- **Raw temp APIs in the test trees** (what `raw-temp` will flag; lines, not folders): `src_mcp/tests` 276
  `CreateTempSubdirectory`, 346 `TempDir.For`, 189 `GetTempPath`, 2 `tmpdir`; `src_server/tests` 24 / 32;
  `src_bugs/tests` 4 / 16; bench 7 / 0; `src_vs_code/src/test` 243 `mkdtemp`, 266 `tmpdir`; `src_vs_code/scripts` 83 /
  89 (31 runner scripts use `mkdtemp`: `run-host`, `run-seam`, `run-contract`, `browserLayout`, 12 `live-*`, 3
  `measure-*`, 11 `seam-*`); `scripts/lib` 26 / 26; `http/run-contracts.mjs:127` (`coai-contracts-`);
  `.github/workflows/release.yml:307`, `:1154` (`mktemp -d` on a hosted runner). `worktree add`: 29 lines in
  `src_mcp/tests`.
- **The runners outside a run folder.** `run-host.mjs:39` `mkdtempSync(join(tmpdir(), 'coai-host-ws-'))`, `:49`
  `'coai-host-data-'`, `launchArgs` `:89` without `--user-data-dir`; `.then`/`.catch` call `process.exit` (`:117`,
  `:122`) before `.finally` (`:124-127`) — **both folders still leak on every `npm run test:host`** (unchanged since
  October). `scripts/lib/*.test.mjs` run bare under `node --test "scripts/**/*.test.mjs"` in CI (`ci.yml:196-197`), with
  no runner and no run folder; module-level `mkdtempSync` at `caller-transcripts.test.mjs:18`, `vibe-manifest.test.mjs:12`,
  `host-facts.test.mjs`, and others.
- **CI.** `build-test` (`ci.yml:18`, ubuntu): the mount's `npm ci` and `rules check` `:36-39`, `src_mcp` `:57-58`,
  bench `:63`, Team server `:76`, ingest `:87`, plan lifecycle `:190-191`, probe script tests `:196-197`, pin check
  `:209-210`, adapter check `:214`; `macos` (`:260-262`, `macos-latest`, 30 min) runs `src_mcp` `:281`, the Team server
  `:284` and bench `:287`; `windows` (`:297-299`, `windows-latest`, 45 min) runs `src_mcp` only (`:316`); `extension`
  (`:319-321`) installs the mount's dependencies `:346-348` then `npm test` `:371`; `extension-host` (`:390-392`,
  `:414`, `:423`). The macOS and Windows jobs install nothing from the mount. No step runs `leftovers-check.mjs` or
  `janitor.sh`. The Windows leg's runner is elevated (E4's measurement; E3's `build-test-windows` self-test cases).
- **The pin and the mount.** `.agents/conventions` at `105d53c` (`git submodule status`); `common/test-leftovers-detail.md`
  carries `## Tokens` with `| coai | dew_flow | the multi-model review extension and server |` (`:320`);
  `tools/janitor.sh` takes `--token <t>` (usage `:112`, case `:117`); `tools/lib/owned.mjs` exports `ownedFor({ token, row })`
  (`ownedPrefix`, `ownedPattern`, `legacyPattern`, `ownedName`, `ownedDir(scope, what, options)`,
  `ownedWorktree`, and the token-free functions: `tempRoot`, `thisOwner`, `writeOwner`, `readOwner`, `ownerState`,
  `legacyOwnerState`, `processProbe`, `isMine`, `removeQuietly`, `cleanupScope`, `isAskablePid`);
  `tools/lib/leftovers-sweep.mjs` exports `sweepLeftovers({ repo, tmp, now, minAgeMs, probe, mine, hooks, platform })`
  (`:588`) and `MIN_AGE_MS` (`:39`); `tools/leftovers-check.mjs` takes `--repo --token --identity --rule --allowlist
  --profile node` (`:32`), its `node` profile adds a consumer's tests and scripts (`:173-202`), and its `owner-blind`
  class passes only an allowlist entry by file, line and sha256 (`:251`, `:416`, `ALLOWLIST_FILE` `:86`).
- **The template's shape** (`dew_flow_mcp` `197cc76`, `tests/Mcp.Tests/Leftovers/`, 54 files): the helpers
  `OwnedToken.cs` (`RepoToken = "mcp"` `:5`, `RepoRow` `:7`), `OwnerToken.cs` (`u<uid>` / `w<sha256 of the lower-cased
  account name>`, the `TESTSTODELETE_OWNER` override validated `:13-25`), `Owner.cs` (`Dns.GetHostName()` with
  `~<WSL_DISTRO_NAME>` `:28-31`, `:62`; tag `linux|macos|windows`; start and namespace on Linux only `:64-67`; the
  parse grammar `:73`; `TESTSTODELETE_RUN` honoured only while live `:18-26`), `Owned.cs` (`Prefix` `:57`,
  `MaxName=63, IdLength=6, StartToleranceSeconds=2, MaxClaimBytes=512, MaxDraws=3` `:59`, `LegacyPattern` `:69`,
  `Pattern(owner)` `:75-80`, `Classify` `:89-100`), `OwnedDir.cs` (`TryClaim` with `FileMode.CreateNew`/`FileShare.None`
  `:67`, identity checked before and after the write `:82-92`; `Create` claims first then
  `Native.MakeDirectoryExclusively` `:28-48`, `DarwinLayout.Require()` first `:30`), `OwnedScope.cs`, `Scope.cs`
  (`WindowsIsMine` = inside the profile's final path AND the owner SID equals `WindowsIdentity.GetCurrent().User`
  `:59-67`), `Liveness.cs` (`ProbeState {Live, Dead, Unknown}`, `OwnerState {Live, Gone, Unknown}`; `State` `:69-96`;
  `kill(pid, 0)` on POSIX with `ESRCH` the only `Dead` `:177-182`; Windows `OpenProcess`/`GetExitCodeProcess`/
  `WaitForSingleObject` `Native.cs:269-288`; `HidepidActive` `:99`), `Native.cs` (Linux `statx`, `renameat2(RENAME_NOREPLACE)`
  with a reserve-then-rename fallback `:157-204`; Windows one handle `CreateFileW` + `GetFinalPathNameByHandleW` +
  `GetSecurityInfo` `:388-405`, `:425-436`; `FILE_ID_INFO`), `NativeDarwin.cs` (`libSystem.dylib` by full path,
  `lstat`/`fstat` with `$INODE64` on X64 `:89`, `renamex_np(RENAME_EXCL)` that FAILS CLOSED on `ENOTSUP`/`EINVAL`/`ENOSYS`
  `:112-135`, the claim read `open(O_NONBLOCK|O_NOFOLLOW|O_CLOEXEC)` then `fstat` `:138-146`, no start time), `Errno.cs`
  (`Errno.For(isLinux, isMacOS)` throws elsewhere `:26`), `DarwinLayout.cs` (the stat-layout self-test against
  `/usr/bin/stat -f`, `Require()` a no-op off macOS), `Removal.cs` (`Tries = 5`, `Backoff = 100 ms`; `Quietly` never
  throws, the claim deleted last; `ClaimIfOurs` swallows not-found), `LeftoversSweep.cs` (607 lines: `MinAge` 10
  minutes `:81`; `Judge` `:269-291`; removal by identity through `.<name>.testsToDelete-quarantine-<pid>-<hex8>`
  `:307-331`, `:380-383`; quarantine restore `:138-155`; orphan claims `:205-241`; legacy listed with the exact `rm`
  command, never collected; `.git` FILE kept as "other clone" `:170-175`), `LeftoversSweepFixture.cs`,
  `LeftoversAudit.cs` (never-disposed / disposed-not-removed / claimed-in-temp-root ⇒ `LeftoversAuditException`),
  the guard `LeftoversGuard*.cs` (classes `raw-temp`, `raw-worktree`, `docker-run`, `docker-name`, `shared-store`,
  `ryuk-off`, `legacy-name`, `owner-blind`, `exemption`, `token`; `ExcludedFiles = []` `:45`; the folding
  allowances `:60-66`; `owner-blind` `:111-127`, `:268-288` with `AllowedAfterWord` `:118`; scripts and workflows
  judged by the shipped `sweeper-scan.mjs` through `node` `GuardScripts.cs:39`), and the tests (`OwnedTests` 472,
  `LeftoversSweepTests` 860, `LeftoversGuardTests` 697, `LivenessTests`, `NativeTests`, `NativeDarwinTests`,
  `DarwinLayoutTests`, `LeftoversAuditTests`, `LeftoversJanitorTests`, `RunnerFactsTests`, …). Its promoted plan's
  copy rule (`research/PLAN_test_leftovers_E3.md:1363-1385`): every file but `OwnedToken.cs` copied byte-identical
  except the namespace line; `OwnedToken.cs` rewritten from the copy's own row. Its CI: a macOS leg whose first step
  asserts `uname -m` = `arm64`, then `DarwinLayoutTests`, then the `Mcp.Tests.Leftovers` namespace, then the suite,
  then the audit self-test (`ci.yml:144-190` there); a Windows leg with the audit self-test in `pwsh` (`:84-106`).

### Read on `origin/main` `1fe25d19` (2026-10-16) — kept from the October version, with what changed

- **The shared rule and the two test sweeps** were as above except the lines: `readRule` `:20-22` (now `:20`),
  `leftover` `:30-36` (now `:30-37`), `entriesOf` `:53-62`, `removed` `:65-72`, `sweep` `:79-86`; `sweepFirst`
  `run-tests.mjs:102-110` and its call at `:113` (now `:103-111`, `:242`); `TempDirsAreSwept` constructor `:58-60`
  (now `:76-85`); `PanelService.PruneOldScratchDirs` `:1449-1462` → `SweepOne` `:1465-1480` → `DeleteIfDue`
  `:1488-1494` → `GitScratch.DeleteEvenIfReadOnly`, `KimiHomesDisposed` `:1502-1512` (now `:1475`, `:1491`, `:1514`,
  `:1544`); `ScratchPrefixes` `:1363-1364` = three prefixes (now `:1389-1390`, four). `SweepRule.cs:13-25` reads
  `prefix`, `keepMinutes` and `neverSwept`, climbing five folders (unchanged). **So liveness cannot be added to the C#
  test sweep without either changing the product's sweep or giving the tests a sweep of their own** — S2-B gives them
  the copied one.
- **What pins what — the parent's "three places" are two different kinds** (the October table; the line numbers are
  the re-read ones above): the four TEST-prefix pins move in S2-A; the three PRODUCT-prefix scans stay `coai-` — they
  prove `neverSwept` covers the product, which the product's own sweep still reads nothing of, but which documents what
  the one-off clean-up must leave alone. The three scans were and are incomplete (they see only a literal in the one
  call shape; `RoundScratch.cs`, `ConsultCheckScratch.cs`, `QuestionRowLaunch.cs` and `chatFastSettings.ts` are
  invisible to them); nothing here depends on them being complete.
- **The helper that exists.** `src_mcp/tests/TempDir.cs`: `TempDir.For(prefix)` (`:31-32`) =
  `Directory.CreateTempSubdirectory(prefix)` — any prefix; `Dispose` (`:41-54`) deletes through the product's
  `Runners.Files.GitScratch.DeleteEvenIfReadOnly` and swallows `IOException` / `UnauthorizedAccessException` — except
  `LinkTargetLostException`, which it re-throws on purpose. No claim, no sibling marker — **and under E2 it needs none:
  what it makes is inside the run folder.** `src_bugs/tests/Scratch.cs:18-35` is a second, smaller helper (clears
  SQLite pools, reports a failed delete through `TestContext.SendDiagnosticMessage`).
- **The sites, counted on `1fe25d19`** (lines, not distinct folders; the re-count on `df375eb4` is above):
  `src_mcp/tests` 270 `CreateTempSubdirectory`, 119 `TempDir.For`, 182 `GetTempPath`; 337 `coai-…`, 46
  `testsToDelete-<no token>-…`, 1 `human-wt-` (`WorktreeManagerTests.cs:116`); `src_server/tests` 24 / 32, 23 `coai-…`;
  `src_bugs/tests` 4 / 16; bench 7; `src_mcp/tests_fakecli` `Program.cs:471`, `Serving.cs:104` — FIXED file names in
  `GetTempPath()`; `src_vs_code/src/test` 236 `mkdtemp`, 210 `tmpdir()`, 161 `coai-…`, 4 token-less, ~24 foreign
  prefixes (`baselinegrows-` `baselineOnlyGrows.test.ts:32`, `:97`, `:191`, `:223`; `changelogguard-`
  `changelogNamesTheRelease.test.ts:52`, `:180`, `:396`; `changelogsection-` `changelogSection.test.ts:31`, `:178`,
  `:297`; `anchors-` `releaseAnchors.test.ts:46`, `:127`, `:138`; `suppressions-` `suppressionsOnlyShrink.test.ts:36`,
  `:135`; `draftrelease-`/`draftchain-` `draftReleaseNotes.test.ts:50`, `:218`, `:269`; `docsonly-`
  `docsOnlyTitle.test.ts:23`, `:88`; `resolver-` `pathResolver.test.ts:34`, `:51`; `sweeptest-`
  `sweepTemp.test.mjs:80`); `src_vs_code/scripts` 83 lines; `scripts/lib` 13 `mkdtempSync` (3 token-less);
  `http/run-contracts.mjs:127`; `release.yml:307`, `:1154`. Under E2 every site in `src_mcp/tests` and
  `src_vs_code/src/test` is INSIDE the run folder; the foreign-prefix names there need no rename.
- **No cleanup at all, file level** (October): 43 C# test files and 2 Node files (`dataDirWriters.ts`,
  `testDiscovery.test.mjs`) with no removal call — an upper bound; E2 made the question moot for `src_mcp` and the
  extension (the run folder goes whole) and the per-file fix stays worth doing only where a test asserts its own
  folder's contents.
- **Linked worktrees as top-level leaves of the temp root** (October): `FilesystemInvariantTests.cs:189`, `:219`,
  `:415` (`coai-linked-<guid8>`), `CadenceStoreTests.cs:161` (`coai-cadence-wt-<guid8>`); `AReviewTreeTests.cs:219`,
  `AReviewTreeIsGivenBackTests.cs:268`, `:313`, `WorktreeManagerTests.cs:116`, `:392-402`, `:420-434`; twelve `worktree
  add` call sites then (29 lines now). Under E2 they are inside the run folder (rule §1: no name of their own); the
  guard's `raw-worktree` class still names each line, so each carries an exemption naming the owned folder it lives in.
- **Product-named directories made by product code under test**: about a thousand per server-suite run
  (`research/module_tests.md:421-423`), named by `RosterBuilder.cs`, `RosterAssembly.cs`, `SecurityRoster.cs`, not by
  any test. Under E2 they land inside the `src_mcp` run folder (`RunTemp.cs:14-15`, "Children inherit it"); for the
  Team server suite they land in the real temp until S3 gives that suite the run folder.
- **The editor-host run** (`run-host.mjs`): unchanged since October — see the re-read above. `@vscode/test-electron`'s
  default profile is a persistent folder under the package's `.vscode-test/` cache (the coder confirms the default in
  the installed version and records it).
- **Owner-liveness precedents in this repository (shape, not reuse)** — now joined by E2's `OwnerClaim.Judge`
  (`OwnerClaim.cs:62`, two-valued `pidAlive`): `WorktreeManager.cs:33` `TreeOwner(Pid, StartedUtc)`, `WriteOwner`
  `:431-440` (a temp name renamed over — a move REPLACES a claim), `OwnerIsAlive` `:446-458` (an unreadable marker reads
  as a DEAD owner — the opposite of rule §3); `ProcessTracking.StartedAt(pid)` (`ProcessTracking.cs:160-173`) returns
  `null` for "no such process" AND "access denied"; `OrphanSweep.Same` (`OrphanSweep.cs:109`). None is the shape; the
  copied `Liveness.cs` is.
- **Tests, CI, the pin** (October): C# tests run as MTP executables, never `dotnet test` (`.agents/PROJECT.md`);
  `.claude/settings.json` and `.claude/hooks/*` are byte-identical copies of `.agents/conventions/settings/` checked
  by `adapter-check.mjs`; the reviewer-rules canary `StageRulesTests.cs:142-178` measures the real mounted corpus —
  re-measured against the test-leftovers core by #936 (its subject). `RuleFiles.DefaultBudgetBytes` was `80_000`
  (`:171`) and PR #848 raised it to 90 KB; it is `90_000` at `:181` now. The October note that E1's guard
  (`leftovers-check.mjs`) could not be run over this repository as is (its discovery read the conventions' layout; its
  `RAW_TEMP` had no `CreateTempSubdirectory`) is **superseded**: the tool has a `node` profile for the Node trees
  (decision 11), and the C# trees are the copied C# guard's.
- **The memory lesson this plan is built on:** coai tests share the temp root with live product data; a test sweep
  that selects by prefix alone has to be kept away from it by a deny-list (`neverSwept`) that only covers what someone
  remembered to add. The owner form removes the overlap instead of listing it, and the run folder removes most of the
  sites from the shared root altogether.

## Decisions

**D1 — The name.** The run folder is `testsToDelete-<owner>-coai-run-<id>` (C# and Node), and every other owned folder a
runner or script test makes outside a run folder is `testsToDelete-<owner>-coai-<what>-<id>`; the token `coai` is
written once per helper (C#: `OwnedToken.RepoToken` in the copied library; Node: the one `ownedFor({ token: 'coai',
row: 'the multi-model review extension and server' })` call) and is the `coai` row of the detail's `## Tokens` table
(`:320`). `<what>` obeys rule §1 (no path separator, NUL, control character or leading `-`; the helper budgets the
owner before `<what>` and refuses, never truncates). **What lives inside the run folder keeps its name** — `coai-panel-`,
`baselinegrows-`, a nested worktree — and carries no prefix and no claim (rule §1). *Withdrawn from October:*
`testsToDelete-coai-<what>-<id>` for every test folder (a legacy name under `105d53c`, and unnecessary under E2).

**D2 — The C# library is a copy of the template.** A new test-support project `src_mcp/tests_leftovers/CoaiMcp.Tests.Leftovers.csproj`
(`IsTestProject=false`, `IsPackable=false`; namespace `CoaiMcp.Tests.Leftovers`), holding every file of
`dew_flow_mcp · tests/Mcp.Tests/Leftovers/` at `197cc76` byte-identical except the namespace line — the Darwin path and
the Windows SID path included — with `OwnedToken.cs` rewritten (`RepoToken = "coai"`, `RepoRow = "the multi-model
review extension and server"`); its tests (`*Tests.cs`) copied into `src_mcp/tests/Leftovers/` the same way.
`src_mcp/tests`, `src_server/tests`, `src_bugs/tests` and `src_bench/CoaiBench.Tests` reference it; added to
`dew_flow_connect_other_ais.slnx`. `RunTemp`, `RunTempFolder`, `RunTempFixture`, `OwnerClaim` and `TempDir` move into
it with their namespace `CoaiMcp.Tests` kept (the 346 `TempDir.For` callers compile unchanged). The template puts its
helper inside the one test project; coai has four, so one library. *Loses:* linked source files (four compilations,
and the bench project does not reference `Runners`); four copies — the drift `SweepRule.cs:9-11` was written against.
The template's `RepositoryRoot` and `GuardFiles` know mcp's layout: `GuardFiles.cs` and `RepositoryRoot.cs` are
edit points beyond the namespace, and S6 lists every edited line in its PR. **The edit points, complete** *[round-1
findings 0 and 1]*: `GuardFiles.cs`, `RepositoryRoot.cs`, and `LeftoversSweepFixture.cs` (its sweep root becomes
`RunTemp.Real`, finding 1); NOTHING else — `LeftoversGuard.cs`, the scanner, stays byte-identical: `Scan` returns
findings, and the `raw-temp` baseline filter of D6 lives in coai's OWN wrapper test
`src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs` (coai's file, not a copy of the template's one-assertion
test), which calls `Scan`, subtracts the baseline's entries, and asserts the rest is empty. *Kept from October* in its
shape; *changed*: a copy, not a design.
*[epoch 4, 2026-11-03]* **Revised: an ADAPTED copy** (the parent's entry 3.1 and its epoch 4, item 4; re-review consult `8d309111`).
Byte identity "except the namespace line", and "`LeftoversGuard.cs` stays byte-identical", cannot hold: the template
hard-codes mcp's mount path (`LeftoversGuardScripts.cs:20`, `.claude/rules/shared/tools/lib/sweeper-scan.mjs`;
`OwnerVectors.cs:11`), its helper paths (`LeftoversGuard.cs:47-48`, `tests/Mcp.Tests/Leftovers/…`; `:60-66`) and an mcp
type (`RepositoryIdentity.Row`), and coai cannot mount the rules under `.claude/rules` (`.agents/PROJECT.md`). The copy is:

- **A pinned file manifest**, from `dew_flow_mcp` `197cc76`: the 51 files of `tests/Mcp.Tests/Leftovers/` (not 54), plus
  `tests/Mcp.Tests/DirectoryLinks.cs` (used at `DarwinLayout.cs:130`) and `tests/Mcp.Tests/RepositoryRoot.cs` (used at
  `OwnerVectors.cs:11`). Each entry says where the file goes: the library `src_mcp/tests_leftovers/`, or the test
  project `src_mcp/tests/Leftovers/` when it holds `[Fact]`s — the `*Tests.cs` files AND `DarwinFifoChild.cs` and
  `LeftoversSelfTest.cs`, which hold `[Fact]`s without that suffix.
- **Per file, the permitted transformations**: the namespace; `OwnedToken.cs` rewritten; the mount path
  `.claude/rules/shared` → `.agents/conventions`; mcp's helper paths and folders → coai's (coai has TWO helper folders,
  the library and the test project); `RepositoryIdentity.Row` → coai's identity; and the edits S3's and S6's briefs list,
  each by file and line. The known sites (S3 brief § 14 item 2, spot-checked on `197cc76`): `OwnedTests.cs:24`
  (`-mcp-pg-`), `OwnerVectors.cs:11`, `LeftoversSweepFixture.cs:3` (a fully qualified type), `LeftoversAuditException.cs:35`;
  for S6, `LeftoversGuard.cs:47-48`, `:60-66`, `LeftoversGuardScripts.cs:20`, `LeftoversGuardTokens.cs:98`, `:126`,
  `LeftoversGuardFiles.cs:28`, `:63-64`, `LeftoversJanitorTests.cs:17`, `:32`, `LeftoversAuditTests.cs:223`,
  `LeftoversGuardTests.cs:264`, `:350-352`, `:394`, `:474`, `:557`, `:579`, `:607`.
- **A diff check** (a test or a script in the PR) that compares every manifest file with the template at `197cc76`
  and fails on any change the manifest does not permit, naming file and line.
- **The ownership, liveness and deletion algorithms unchanged** (`Owned`, `OwnedDir`, `Owner`, `Liveness`, `Scope`,
  `Native*`, `Removal`, `LeftoversSweep`, `LeftoversAudit`), unless a change is separately reviewed.

Also settled for D2: `RunTemp.cs` moves into the library (no `[ModuleInitializer]` there any more), and each of the four
test projects gets its own `RunTempStart.cs` (the initializer) and `TestEntryPoint.cs` (the gated `Main`, S3); the
library references `CoaiMcp.Runners`, because `TempDir` keeps `GitScratch.DeleteEvenIfReadOnly` (S3), so the bench tests
get it transitively — the "*Loses:* … the bench project does not reference `Runners`" reason above no longer holds.
Making the template parameterised upstream is NOT done now; it is a possible later follow-up if the same adaptations
recur in E2, E6 or E7.

**D3 — The Node side wraps the reference** (decision 11, R7). `run-tests.mjs`, `run-host.mjs`, every runner in
`src_vs_code/scripts`, `http/run-contracts.mjs` and `scripts/lib/*.test.mjs` import `ownedFor` from the mounted
`.agents/conventions/tools/lib/owned.mjs` through ONE module of this repository, `src_vs_code/scripts/owned.mjs`
(ESM; the compiled CommonJS tests under `out/test/` run INSIDE the run folder and need no helper), which holds the
`ownedFor({ token: 'coai', row })` call and re-exports its result; the start-of-run sweep is `sweepLeftovers` of the
mounted `leftovers-sweep.mjs`; the guard for the Node trees is `leftovers-check.mjs --profile node --token coai`. The
mount missing ⇒ one line naming `git submodule update --init .agents/conventions` and exit 2 (the extension job already
installs the mount's dependencies, `ci.yml:346-348`). *Withdrawn from October:* `owned.cjs` + `owned.d.cts` as a port.

**D4 — The old names are cleaned up once, by a person; no sweep collects them by age** (risk decision 2026-10-17,
unchanged): no test sweep matches a `coai-` name after S2-A; the one-off command a person runs LISTS first and removes
only the exact names picked. *Added 2026-11-01:* the E2 run folders `testsToDelete-coai-run-*` with their old-grammar
claims are **legacy names** under rule §1: the copied C# sweep lists them with the exact `rm` command (the template
never collects a legacy name); the Node reference's sweep collects one only where the kernel owner is the caller AND
its old-grammar record is `gone` (rule §4, `legacyOwnerState`) — so a killed old run of either runner is collected by
the next extension run of the same user, and a C# run lists it. The janitor matches `^teststodelete[-_]` names and
lists legacy ones; nothing is delegated to it. **The command re-checks before it removes** *[round-1 finding 6]*: the
person picks names from the listing, and the removing command, just before EACH removal, checks that the path is still
present, is unchanged since the listing (same identity — device and inode, or the Windows file id — and same last-write
time), is not a protected product prefix or the version cache, and has no live owner (a sibling `.owner` whose record
is `live` or `unknown` refuses; a `coai-*` name has no record, so the person's pick plus the unchanged check is the
whole authority there); anything else is REFUSED with one line naming the path and the check that failed, and the
command goes on to the next pick. *[round-2 finding 0]* **A re-check narrows the race but does not close it** (the
check and the removal are two calls; a folder swapped in between is removed by a name read earlier — the thing rule §4
forbids). So the removal is the sweep's own shape: an **EXCLUSIVE rename** of the selected folder to a sibling
quarantine name `.<name>.teststodelete-quarantine-<pid>-<hex8>` (POSIX `renameat2(RENAME_NOREPLACE)` / `renamex_np`,
Windows `MoveFileEx` with no flags — the reference's `isTakenRename`, never a plain rename), performed ONLY while the
path still has the listed identity (device + inode, or the Windows file id, re-read on the entry immediately before
the rename and once more on the quarantined object after it), then the removal of the QUARANTINED object by that
identity; a changed identity, a taken quarantine name or a failed rename ⇒ the pick is REFUSED and reported, nothing
removed. S2-A item 5 documents and scripts the command in that shape, with the swap test. **Known residual risk**
(unchanged): a
checkout on pre-S2-A code keeps its age-only sweeper; mitigation: update or rebase it, or run with a private temp
root. The old-grammar claim's
`<os>` tag `win32|darwin` (`OwnerClaim.cs:38`) is read by the legacy clause of the reference (`<host>@<os>:<pid>[:<start>]`)
only when `<os>` is `linux|macos|windows`: a `win32`/`darwin` legacy claim is **unreadable ⇒ `unknown` ⇒ kept** and
listed — the safe side; the one-off clean-up covers them.
*[rollout entry 4.1, 2026-11-03]* **Corrected — the legacy-collection promise cannot hold at the pin.** "a killed old run of
either runner is collected by the next extension run of the same user" is WITHDRAWN (the S2-B spec, B.10 item 1; the
question consultants `08beb732`, `76d09c26`; the parent's entry 4.1, dew_flow_conventions #121). At `cc24f9e` the Node
reference never collects a legacy coai folder: on a Linux reader `legacyOwnerState` is always `unknown` (`owned.mjs:580-587`,
a legacy record has no `<ns>`); on Windows the sweep lists and changes nothing (F16 interim); and E2's claims carry
`win32`/`darwin`, which `parseLegacyOwner` does not read. The C# copy LISTS a legacy name with the exact command and never
reads its claim. **The only removal of E2's `testsToDelete-coai-run-*` folders is S2-A's one-off clean-up**
(`scripts/old-coai-leftovers.mjs`, a person's tool). Both runners print the sweep's `legacy:` line per folder (S2-B spec B.1 § 3).

**D5 — Product-named directories under test.** *Superseded for `src_mcp` and the extension by E2* (the three variables
are the seam: `RunTemp.cs:14-15`); *becomes true for the Team server, ingest-server and bench suites in S3* when they
get the run folder. The follow-up plan `todo/PLAN_the_product_scratch_root_is_a_seam.md` the October S4 was to
extract is NOT written: there is nothing left for it. The measured count stays: about a thousand per server-suite run
(`research/module_tests.md:421-423`, 2026-09-23).

**D6 — Two guards, one `raw-temp` baseline — authorised by a shared rule change FIRST** *[round-1 finding 7;
decided with the question consultant, consult `abcaae48`, both answers: change the shared rule first]*. The C# trees,
scripts and workflows are the copied C# guard's (`LeftoversGuard`, byte-identical — finding 0 — called by coai's own
wrapper `LeftoversRepositoryGuardTests`); the Node trees are the shipped tool's under `--profile node`, run by `npm
test` through one wrapper test (`src_vs_code/src/test/testLeftovers.test.mjs`) that fails on exit 1 and on exit 2 with
the tool's own sentence. **`owner-blind`, `legacy-name`, `raw-worktree` (exemptions counted), `docker-*`,
`shared-store`, `ryuk-off`, `exemption` and `token` are at ZERO when the guard lands.** `raw-temp`: rule §7 at
`105d53c` allows ONLY the inline exemption form, so a coai-only allowlist would waive the released rule — it is NOT
done. Instead this plan **depends on a conventions rule change** (to be written next, in `dew_flow_conventions`,
as its own plan) that authorises a **`raw-temp` baseline** under exactly these terms: ONLY for sites proven to run
inside an owned run folder (the proof is the suite's run-folder test of S3 sc. 2, named by the baseline file);
matched by **exact membership plus the content hash of the line** — never by a count ceiling (coai's own
`src_vs_code/src/test/suppressionsOnlyShrink.test.ts` is the record of why a falling total is not enough: one
entry can be swapped for another under a constant count); **only shrinking** — an entry may be removed, never added
or changed; the **line number is a hint, not a key** (a line that moves keeps its entry by hash; an entry whose hash
matches no line of its file is stale and red). *[round-2 finding 1]* **The baseline is a MULTISET keyed by file +
content hash**: an entry carries a COUNT of occurrences (or is repeated once per occurrence); a scan that finds MORE
occurrences of a key than the baseline holds is red — so a second identical raw-temp line pasted into a file that
already has a baselined one cannot hide behind the first one's hash; fewer occurrences than the baseline holds is
the drain (the entry shrinks). The baseline files (`src_mcp/tests/Leftovers/raw-temp-baseline.json`,
`src_vs_code/src/test/raw-temp-baseline.json`) are the ratchet S7 drains to zero, and a NEW raw temp call is red the
day it is written. **The producer PR may merge only after that rule is released and coai's pin moved to it** (a pin
bump alone, R6 decision 1); the C# wrapper and the Node wrapper read the baseline in the form the released rule
fixes. Stage A and S5 do not depend on it. *Withdrawn from October:* one Node guard scanning the whole repository
with discovery and patterns of its own. *Withdrawn from the first re-split text:* an allowlist as a coai-only
deviation from §7.
*[epoch 3, 2026-11-03]* The rule is released (`5863c60`, #108) and pinned (#939, then #941 at `cc24f9e`), so the
dependency above is met. The released form differs from this paragraph in one point: it allows ONE baseline file,
`raw-temp-baseline.json` at the repository root (never a symlink), with one scope per tree (`root`, `proof`, `guard`:
`csharp` or `node`). The two files named above become two scopes of that one file. A scope's proof must hold for
EVERY invocation that runs its tree (D10): for `src_vs_code/src/test/`, S4 must first route `sonarcloud.yml:302`
through the runner (or give that job a run folder), because that job runs the compiled tests with no run folder.

**D7 — Stage A is the retirement of the age-only sweep, nothing else.** The barrier (rule § *The barrier*) names "a
sweeper that matches the prefix without an owner segment, OR removes on age"; coai's two sweeps remove on age. R6
recorded coai's stage A as empty because `sweeper-scan.mjs` looks for the prefix; this plan records the age-only
`coai-` sweep as coai's stage A and retires it as ONE small PR that no producer waits in — so a rollback of the
producer PR never restores age-based deletion (rule § *The barrier*: restoring owner-blind deletion is never a
rollback). The ported owner-aware sweep lands with the producers (stage B), because it IS the helper's code.

**D8 — The producer stage is ONE squash-merged PR** (R6 decision 1 as applied by E4 #64 and E3 #58): S3 → S2-B → S4 → S6
as reviewable commits on one branch `feat/test-leftovers-e5`, never merged apart, after S2-A and S5 are on `main`.
**The invariant** *[round-1 finding 4]*: **no commit on `main` carries the owner-form producer (S3's run folder)
without the owner-aware sweep (S2-B), or the sweep without the producer** — a producer with no sweep leaves every
killed run's folder for ever; a sweep with no producer sweeps nothing and hides that. So S3 and S2-B are in the SAME
squash-merged PR, the merge condition checks that BOTH are present (the PR's own test
`TheProducerAndTheSweep_LandTogether`: a run folder of the owner form is made AND `LeftoversSweepFixture` is the
assembly fixture of every suite that makes one), and a revert of the PR reverts both — never one of them. It merges
only when (1) the barrier record of the remainder plan's R6 names every machine that builds or tests coai — the
operator's WSL machine (open, 2026-10-31) AND the owner's Windows machine (R6: "not tested") — with a clean rescan;
(2) every consumer's sweep-only stage is recorded (R6 lists all seven); (3) the coai plan gate on this document and
the code gate on the PR reached `proceed`; (4) the macOS, Windows and Linux legs are green; (5) *[round-1 finding
7]* the `raw-temp` baseline rule (D6) is released in `dew_flow_conventions` and coai's pin has moved to that release
by a pin bump merged alone.
*[rollout entry 4.1, 2026-11-03]* Q6's line "its Windows CI leg asserts that a planted gone-owner folder survives the Node
sweep" is met AS WRITTEN (the question consultants on the S2-B spec's B.10 item 3): the `windows` job gets a step that runs
the extension's Node sweep test file through the actual wrapper and the pinned reference, with an otherwise collectible
fixture (S2-B spec B.1 § 5, N-S9, B.5). The `platform: 'win32'` seam test on Linux is an extra, not a substitute.
*[epoch 3, 2026-11-03] Revised merge condition of the producer PR.* The PR merges only when ALL of these hold:
(a) S2-A (#940, done) and the REVISED S5 (§ *S5 as revised*) are on `main`; (b) S3, S2-B, S4 and S6 squash-merge
together in this one PR, and the PR INCLUDES every site S5 deferred (each listed in S3 or S4 below), with S6's
repository-wide checks green: `owner-blind` and `legacy-name` at zero outside the helpers' own files and S6's named
exemptions, and only the authorised `raw-temp` baseline left; (c) condition (1) above, the barrier record of the
owner's Windows machine (the parent's P1), still open; (d) condition (2) is corrected: R6's record of 2026-10-31 was
incomplete — the barrier scan missed coai's age-only sweep (D7) and rag_qln's owner-blind `qlntest_*` drop. coai's
stage A is now landed (#940). rag_qln's stage A (E7) reaches only rag_qln's own store, not a temp folder of this
repository, so it is not a condition of this PR (the parent's epoch 3, § 3 (b)); (e) conditions (3) and (4), the
gates and the three legs, unchanged; (f) condition (5) is met (`5863c60`, pinned by #939; `cc24f9e` by #941), and the
release that carries F16 (b) is pinned (Q6); (g) if this PR is the family's first compliance declaration under the
shipped Node guard (before creds' E2.5), F3 of the follow-ups (the `node` profile reads `package.json` scripts) is
released and pinned first (the parent's epoch 3, § 4); (h) every other barrier, release and review condition of the
parent's § 3 still holds.
*[after the re-review gate, 2026-11-03, findings 0 and 3] Condition (a) changes.* "the REVISED S5 on `main`" no longer
holds: S5 has no stage-A part. The producer PR merges only when S2-A (#940) is on `main`, and the ONE squash-merged PR
carries, in this commit order, S3 → S2-B → S4 → **S5** → S6: S5 is the renames of every eligible site, and the PR also
carries every site S5 deferred (S3, S4). Conditions (b)–(h) stand; S6's check is the exemption contract of S6 (zero
UNEXEMPTED `owner-blind` and `legacy-name`, and the expected-exemption inventory equal to the guard's list).
*[epoch 4, 2026-11-03]* **Three conditions added, one read anew** (the parent's epoch 4, item 3): (i) F24 of the conventions' follow-ups
(the baseline proof rule accepts a process-wide gate before discovery) is released and coai's pin carries it — a pin bump
alone — BEFORE S6 creates the baseline; (j) the adapted copy's diff check (D2) is green; (k) condition (4), "the macOS,
Windows and Linux legs are green", is read as the suite × OS matrix of the DoD: every suite green on every leg CI runs
it on, and the ownership and startup proofs (the run folder, fail closed, the refusal) green for every suite on all
three OSes; the counts reconciled by name. P1 still holds: until it is recorded, Windows evidence comes from CI only.
*[re-review plan gates, 2026-11-03: the parent's session `7c66b50d`, findings r0–r5; this document's session `7a6707ad`, findings e0–e2]* **(k) is replaced by named merge gates** (findings r0, r3). The run-folder LIBRARY is shared code: its proofs
(owner-form folder, claim first, fail closed with exit 78, the refusal) run on all three OSes through the suites that
run there. Each suite's OWN wiring (`RunTempStart.cs`, `TestEntryPoint.cs`, its `RunFolderTests`) is proven where a CI
step runs that suite. Each cell with a step is a merge gate: green on the PR's head (`ci.yml` lines at `004c55f`; NEW =
added by S3):

| What is proven | Linux | macOS | Windows |
|---|---|---|---|
| the library (via `CoaiMcp.Tests`) and mcp's wiring | `build-test` `Test` (`:58`) | `macos`: NEW `The stat layout self-test`, NEW `Leftovers`, then `Test` (`:282`) | `windows`: NEW `Step 0` first, then `Test` (`:317`) |
| server's wiring | `:77` | `:285` | NEW filtered step after `Test`: `CoaiServer.Tests.exe --filter-class "*RunFolderTests"` |
| bench's wiring | `:64` | `:288` | — (bench never runs on Windows) |
| bugs' (ingest) wiring | `:88` | — | — (Linux only: `:258-259`; `release.yml` `bugs-binaries` is Linux too) |

`release.yml` `server-binaries` (`:619`, test line `:694`) runs the server suite on Windows x64 and arm64 and macOS arm64
at release time: release evidence, not a merge gate. **(l) NEW (finding r2):** before S6 creates the baseline, F24's
acceptance check has passed against the RELEASED guard and THIS repository's real test host (S6's note). **(m) NEW
(finding r1):** the rollback procedure of D11 is written in the PR description before it merges.

**D9 — The end-of-run audit is copied, and bounded to the run folder.** `LeftoversAudit` (never-disposed,
disposed-not-removed, claimed-in-temp-root) runs at the end of every C# suite as in the template — but coai's four
suites run one after another in one CI job (`build-test`, the macOS job) and on one developer machine, so the audit
reads ONLY the entries whose claim record is THIS process's, never every entry under the shared `TESTSTODELETE_RUN`
record (wave 1 defect A: the first child to finish deleted its siblings' folders). The audit self-test steps of the
template's CI (`selftest`, `selftest-failed`) are copied to all three legs.
*[epoch 4, 2026-11-03]* The audit SOURCE (`LeftoversAudit.cs`, `LeftoversAuditException.cs`) lands in S3, unwired, because S3's
`LayoutGateTests` (#10) needs `LeftoversSweepFixture`, which needs it; its tests, its wiring and the self-test steps stay
in S6.

**D10 — Containment is a property of each invocation, not of the file** *[epoch 3, 2026-11-03; the parent's epoch 3,
§ 4]*. A site may lose its bare prefix (rule §1: inside an owned resource a name needs none) only when EVERY
invocation that runs it gives it an owned folder. An invocation is a command or a job this repository defines: a CI
workflow job (any workflow, not only `ci.yml`), a `package.json` script, a runner script, or a command a document
tells a person to run. Before a story drops a prefix, it records an **invocation inventory** in its PR: each
invocation that runs the files → the temp root it actually gets → who provides the owner, the cleanup and the sweep →
what must exist first. A site with even one uncontained invocation keeps its prefix, and the deferral names that
invocation, the missing containment and the story that adds it. A runner's fallback to the machine temp is an
uncontained path; S3 (sc. 3a) and S4 (`beginRun`) withdraw both fallbacks. The inventory on `a3b73534` is in § *Facts*
(the first table). Found by the S5 build: `sonarcloud.yml:302` runs the compiled extension tests with no run folder,
and `ci.yml:197` runs `scripts/**/*.test.mjs` with none.
*[after the re-review gate, 2026-11-03, finding 2] A direct start is an invocation too.* For each tree the inventory
also lists how a person starts one test file or one suite outside the runner: `node --test <compiled test>` or
`node --test <source test>` (Node); `dotnet test`, an IDE or the test executable (C#). For each such start the story
either makes it REFUSE — a test that needs the run folder checks the runner's marker and, when it is absent, fails with
one line naming the command to use, before it creates anything — or names it as uncontained, and the site keeps its
prefix. S3 does this for C#, S4 for Node. A runner's fallback is uncontained too, so S3 and S4 also make both runners
fail closed. Until both are done, NO S5 site is contained.
*[epoch 5, 2026-11-04]* The inventory has ONE line that drops nothing and is backed by no owned folder: the native
measurement series (§ *Facts*, the last row of the table; § S4, *[epoch 5]*). It leaves the prefix family by a rename
the inventory records; it is made and removed only by its own scripts and never collected. The parent's merge condition
(i) is read so (its § *Epoch 5*, item 3).

**D11 — Rolling back the producer PR** *[re-review plan gate, 2026-11-03, the parent's session `7c66b50d`, finding r1]*. A
plain revert of the squash merge is NOT the first step: it brings back the legacy producers while owner-form folders
already exist on machines. The order:

1. **Switch off the destructive start-of-run sweep** (S2-B) with a one-line, reviewed change (the sweep lists and
   removes nothing; its fixture stays registered). Owner-form creation and teardown stay ON, so every run still removes
   what it made.
2. **Account for what exists:** `bash .agents/conventions/tools/janitor.sh --token coai --dry-run` on every machine
   that ran the suites (and the CI legs' own logs) lists the owner-form folders and claims already created.
3. **Then revert or repair** the faulty part in a NEW PR, with its own gates; the listed folders are removed by the
   janitor or by the next good sweep, never by hand by name alone.

**Growth surface** (the shared planning rule § *A plan that creates something that GROWS*). Owned folders: ONE run
folder per runner invocation (five C# suites and the extension: six per full run on a machine; plus one per runner
script, 31 of them, each run by hand) with one claim of ≤ 512 bytes; everything else lives inside them. Retired by
the run's own teardown on every path; after a kill by the next run of the same user (10 minutes, owner `gone`); by
`janitor.sh --token coai` (one hour) when no suite runs again. A quarantine `.<name>.testsToDelete-quarantine-<pid>-<hex8>`
a killed sweep leaves is restored or listed by the next sweep (rule §4). The `raw-temp` baseline (D6) only shrinks.
The old `coai-` test leftovers retire only through D4's one-off clean-up a person runs — no sweep and not the
janitor (they never carried the prefix).

## Stages and order

**The table as revised at the epoch-3 re-review (2026-11-03).** It replaces the table after it, which is kept as the
record of 2026-11-01.

| Stage | Story | Model (spec · code · review) | Depends on | Lands |
|---|---|---|---|---|
| the pin | **S1 — the pin** | — | — | **DONE**: #936 (`105d53c`), then #939 (`5863c60`) and #941 (`cc24f9e`), each merged alone |
| A — sweep-only | **S2-A — the age-only `coai-` sweep is retired (cornerstone, first half)** | **Fable** (spec) · Sonnet · Opus | S1 | **DONE**: #940 (`d02ed40e`), 2026-11-02 |
| the rule, then the pin | the `raw-temp` baseline rule, released; coai's pin on it | — | — | **DONE**: `5863c60` (#108), pinned by #939; now `cc24f9e` (#941) |
| A — prep | **S5 (revised) — the bare prefix leaves the ENUMERATED eligible sites only** (sites contained under every invocation that runs them, D10) | Opus · Sonnet · Opus | epoch 3 approved by the owner | before the producer PR; one PR per tree; no sweep and no producer |
| B — the producer PR | S3 — the C# library, the run folder in all four C# suites, the claim grammar, **plus S5's deferred C# sites** | Opus · Sonnet · Opus | S2-A and the revised S5 on `main`; the barrier record (D8) | commit 1 — never merged without S2-B |
| B | S2-B — the owner-aware sweep (cornerstone, second half) | **Fable** (spec) · Sonnet · Opus | S3 | commit 2 |
| B | S4 — the Node producers wrap the reference, **plus S5's deferred Node sites and `sonarcloud.yml:302` routed through the runner** | Opus · Sonnet · Opus | S3 | commit 3 |
| B | S6 — the two guards, the audit, the baseline, the CI legs; **repository-wide zero for `owner-blind` and `legacy-name`, with the named exemptions** | Opus · Sonnet · Opus | S3, S4; F3 released and pinned if this PR is the family's first Node compliance declaration (D8 (g)) | commit 4; the PR squash-merges once, deferred sites included |
| C — the drain | S7 — the `raw-temp` baseline drains to zero | Opus · Sonnet (one coder per suite) · Opus | S6 merged | after B; one PR per suite; not a producer |

**After the re-review gate (2026-11-03, findings 0–3).** The table above changes in four rows; the rest stands:

| Stage | Story | Model (spec · code · review) | Depends on | Lands |
|---|---|---|---|---|
| B — the producer PR | S3 — as above, AND the run folder fails closed, AND a test started without its run folder refuses | Opus · Sonnet · Opus | S2-A on `main`; the barrier record (D8) | commit 1 — never merged without S2-B |
| B | S4 — as above, AND `beginRun` fails closed, AND a direct `node --test` refuses | Opus · Sonnet · Opus | S3 | commit 3 |
| B | **S5 — the renames of every eligible site** (the list of § *S5 as revised*; the legacy fixtures as plain literals with the inline `legacy-name` exemption) | Opus · Sonnet · Opus | S3, S2-B, S4 in the same branch | commit 4 — no stage-A part any more |
| B | S6 — as above | Opus · Sonnet · Opus | S3, S4, S5; F3 as above | commit 5; the PR squash-merges once |

Why S5 is commit 4: its renames are safe only once S3 and S4 have given every site an owner-form run folder that fails
closed, under every invocation, with a refusal for a direct start; and S6's guard asserts the repository-wide zero, so
it must read the tree after the renames. The squash merge makes the five commits one commit on `main`; the order is for
the review and a bisect on the branch. The section headings below keep their old commit numbers (S6 says "commit 4").

*[epoch 4, 2026-11-03]* Two rows read anew: **S3** is the ADAPTED copy of D2 (a manifest, permitted transformations, a diff check) with
the gated custom `Main` (exit 78); **S6** also depends on F24 of the conventions (the baseline proof rule) released and
pinned — a pin bump alone — before it creates the baseline. S3, S2-B, S4 and S5 do not wait for F24. The DoD of every
commit is the suite × OS matrix of the DoD, not "the three legs".

*The table of 2026-11-01 — superseded 2026-11-03 by the table above* (its S5 row said "any time before the producer
PR" with a DoD of zero bare-prefix lines outside the two helper files; the S5 build showed that this cannot be done
before S3 and S4 give the outside sites an owned folder — the parent's entry 2.2):

| Stage | Story | Model (spec · code · review) | Depends on | Lands |
|---|---|---|---|---|
| the pin | **S1 — the pin bump to `105d53c`** | — | — | **DONE**, coai #936 (`df375eb`), merged alone 2026-11-01 |
| A — sweep-only | **S2-A — the age-only `coai-` sweep is retired in both runners (cornerstone, first half)** | **Fable** (spec) · Sonnet · Opus | S1 | first; ONE PR, merges alone; no owner-form producer in it |
| A — prep | S5 — the bare-prefix literals leave the test trees | Opus · Sonnet · Opus | nothing (no sweep, no producer) | any time before the producer PR; one PR per tree |
| the rule, then the pin *[round-1 finding 7]* | the `raw-temp` baseline rule in `dew_flow_conventions` (its own plan there, written next), released; then coai's pin bump to that release, alone | — (not E5's to build; the pin bump is the main session's commit, as S1 was) | the conventions' release | before the producer PR; stage A and S5 do not wait for it |
| B — the producer PR | S3 — the C# library (a copy), the run folder on the owner form in all four C# suites, the contract's claim grammar | Opus · Sonnet · Opus | S2-A, S5 on `main`; the baseline rule released and pinned; the barrier record (D8) | commit 1 of the PR — **never merged without S2-B** (D8's invariant, finding 4) |
| B | **S2-B — the owner-aware sweep at the start of every C# suite and of the extension runner (cornerstone, second half)** | **Fable** (spec) · Sonnet · Opus | S3 | commit 2 |
| B | S4 — the Node producers wrap the reference: `run-tests.mjs`, `run-host.mjs`, the 31 runner scripts, `http/run-contracts.mjs`, `scripts/lib` tests, `release.yml` | Opus · Sonnet · Opus | S3 (the claim grammar both runners write) | commit 3 |
| B | S6 — the two guards, the audit, the `raw-temp` baseline, the CI legs | Opus · Sonnet · Opus | S3, S4; the baseline rule released and pinned | commit 4; the PR squash-merges once |
| C — the drain | S7 — the `raw-temp` baseline drains to zero, suite by suite | Opus · Sonnet (one coder per suite) · Opus | S6 merged | after B; one PR per suite; not a producer |

S2's spec decides; where this document and S2's spec differ, the spec wins and the difference is a defect of this
document. Where this document and the contract differ, the contract wins: report it to the parent, do not resolve it
in code. Where this document and the template differ, the template wins unless the *must not copy back in* list below
names the difference.

### Boundary with the conventions repository and with E3 (both sides — the shared planning rule § *A boundary between two plans*)

| Item | Built by | The other's part |
|---|---|---|
| The contract text, the `## Tokens` table (`coai` row), `tools/janitor.sh`, the Node reference (`owned.mjs`, `leftovers-sweep.mjs`, `leftovers-check.mjs` with its `node` profile), F17–F20 | the conventions (E1, the merged core, R7) | E5 reads them through the mount and RUNS the Node reference; never edits or ports them here |
| The C# helper, sweep, audit, guard and their tests, the Darwin path, the Windows SID path | E3 (`dew_flow_mcp`) | E5 COPIES them byte-identical except the namespace, rewrites `OwnedToken.cs`, edits exactly three files for its layout — `GuardFiles.cs`, `RepositoryRoot.cs`, `LeftoversSweepFixture.cs` (sweep root `RunTemp.Real`, *[round-1 finding 1]*) — keeps `LeftoversGuard.cs` byte-identical and puts its `raw-temp` baseline filter in its OWN wrapper `LeftoversRepositoryGuardTests.cs` (*[round-1 finding 0]*), and reports every defect it finds back to E3's owner as a note — never fixes it here alone |
| The `raw-temp` baseline rule (membership + content hash, only shrinking, run-folder proof) *[round-1 finding 7]* | the conventions (a plan of its own, written next) | E5 waits for its release and pins it; its two baseline files and wrappers read the form that rule fixes |
| coai's run folder, its four C# suites' wiring, the Node wrap module, the runners, the baseline files, the CI legs | E5 | — |
| The pin bump | E5 (S1) — done | R7 released |
| A contract finding from this epic | — | goes back to the parent as a note (§ *Notes for the parent*), not into the mounted copy |

Disjoint otherwise: E5 changes nothing in `dew_flow_conventions` or in `dew_flow_mcp`.
*[epoch 4, 2026-11-03]* The E3 row's "byte-identical except the namespace … edits exactly three files … keeps `LeftoversGuard.cs`
byte-identical" is superseded by D2's adapted copy (a manifest, permitted transformations, a diff check). F24 is a new
row of the conventions' side: the baseline proof rule, built and released there, pinned here before S6.

## Must not copy back in — wave 1 and the Darwin follow-ups, each with its RED-GREEN-RED test here

Every row is a defect found in the E3/E4 builds or reviews (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`
§ *Wave 1*; `dew_flow_mcp · research/PLAN_test_leftovers_E3.md` § *Deviations*, review fixes A–N, rounds 2–3, and
§ *Darwin follow-ups* 1–6; `dew_flow_sidecar_rust · research/PLAN_test_leftovers_E4.md` § *Deviations* 9–11, 17–20,
25–38). The copy carries the fix; the test is written FIRST against the copy (GREEN proves the copy carries the fix),
then the defect is re-planted by hand once and the test is observed RED, then the plant is reverted (RED-GREEN-RED,
recorded in the PR; a planted violation never stays in CI). Story: where the test lives.

| # | Defect (where found) | The test here | Story |
|---|---|---|---|
| 1 | An end-of-run audit matching folders by the shared `TESTSTODELETE_RUN` record deletes live siblings' folders (E3 review fix A) | two child suites started under one `TESTSTODELETE_RUN`; the first to finish runs its audit; the second's claimed folder is untouched and the second's audit passes. RED when the audit matches by record instead of by its own process | S6 (D9) |
| 2 | A guard that reads only whole literals is fooled by split, escaped or concatenated spellings (E3 fix C; E4 item 26: 3 of 12 planted spellings caught) | the template's `LeftoversGuardTests` planted-spelling fixtures (`"testsTo" + "Delete-"`, `$"{"testsToDelete"}-"`, `testsToDelete-`, a glob, a regex over a class, pieces joined by `concat`) ⇒ each ONE finding; plus coai's own: the same twelve in a `.mjs` under the `node` profile. RED when the folding reader is switched off | S6 |
| 3 | A rename that is not exclusive replaces an empty directory (F17; E4 item 9 on Windows, item 27) | an EMPTY directory pre-created at the drawn quarantine name through the hooks seam; the sweep draws another name, the pre-created directory is still there and still empty; on Windows the copy's `MoveFileEx` path, on Linux `renameat2(RENAME_NOREPLACE)` and its reserve-then-rename fallback (`EINVAL`/`ENOSYS` injected), on macOS `renamex_np` FAILING CLOSED on `ENOTSUP` (the folder is kept and listed, never moved by a plain rename). RED with a plain `Directory.Move`/`rename` | S2-B |
| 4 | A quarantine suffix that is not hex (E4 item 25) | 5 000 draws of the quarantine name every one matches `^\.(.+)\.teststodelete-quarantine-[0-9]+-[0-9a-f]{8}$`; the sweep's own restore pattern recognises every one. RED with a `[a-z0-9]` alphabet | S2-B |
| 5 | A FIFO swapped in at a claim path blocks the read (F20; E3 round 3; Darwin follow-up 4) | a FIFO at the claim path (POSIX; the Darwin child test asserts `(Mode & 0xF000) == 0x1000` so it cannot pass for the wrong reason) ⇒ the read returns `unreadable` within 5 s, the folder is kept; the claim identity is `fstat`-checked on the opened descriptor. RED with a plain `File.ReadAllText` | S2-B |
| 6 | The hosted Windows runner is elevated; the default owner of a new file differs by how it is created (E4 items 10, 36; E3's `WindowsOwnership`) | Step 0 on `windows-latest`, BEFORE any helper code: the owner SID of a new temp file, the caller SID, `EqualSid` and elevation printed and asserted; then the helper's own claim is `mine`, a `BUILTIN\Administrators`-owned fixture (`WindowsOwnership.ForeignToCaller`) is `unknown`/kept, and both keys (profile AND SID) are required: a folder inside the profile owned by another SID is kept. RED with the profile-only check | S3 |
| 7 | The quarantine pass asks the probe about any pid (F18) | `-0-`, `-007-`, `-9007199254740993-`, `-1-` on Windows in a quarantine name ⇒ the probe is never called, the quarantine stays. RED when `isAskable` is skipped | S2-B |
| 8 | The orphan-claim pass deletes the claim of a folder that sits in a quarantine (F19; E4 item 37) | a claim whose folder is in a sibling quarantine ⇒ kept `quarantined`. RED when the orphan pass does not look for the quarantine | S2-B |
| 9 | `Errno.Current` guessed Linux on any non-macOS system (Darwin follow-up 5) | `Errno.For(isLinux: false, isMacOS: false)` throws `PlatformNotSupportedException`; on each of the three legs the table in use is the leg's. RED with the old guess | S3 |
| 10 | The stat-layout self-test did not guard the destructive paths (Darwin follow-up 2) | `DarwinLayout.Require()` is the first call of `OwnedDir.Create`, `Removal.Quietly`, the guarded sweep and the guarded audit (asserted by a hooks seam that records the order); a failing layout ⇒ nothing is created or removed, one warning. On the macOS leg the self-test step runs first (`DarwinLayoutTests`), as in the template's CI. RED with the `Require()` removed from one of the four | S3, S2-B |
| 11 | A helper's tests are the helper's callers (E4 item 18a, 34; Darwin follow-up 3): a test of the helper made an unprefixed file in the raw temp root | every test under `src_mcp/tests/Leftovers/` makes its probe files inside an `OwnedScope`/`DarwinLayout.FreshOwnedScope()`; the audit's `claimedInTempRoot`/unmarked count after the Leftovers namespace alone is zero. RED with one test using `Path.GetTempPath()` directly | S3 |
| 12 | macOS resolves `/var` to `/private/var`; a path compare that walks from `/` costs a hop and compares the wrong root (Darwin follow-ups 1 and 6 — mcp's reader; coai has no `SandboxedFileReader`, but `os.tmpdir()` on macOS IS `/var/folders/…`) | the Node wrap's `isMine`/profile compare and the C# `Scope` compare are fed the macOS temp root as `os.tmpdir()` returns it and as `realpath` returns it, and judge the run folder `mine` both ways; the sweep's listing and the claim identity agree. RED when the real root is not resolved once | S2-B, S4 |
| 13 | **coai's own, found in October:** the age-only `coai-` test sweep reaches live product data when one exclusion is missing (risk decision 2026-10-17) | the RED-first scenario of S2-A: an unmarked `coai-chat-live` with sentinel bytes, 11 minutes old, `coai-chat-` OMITTED from the injected exclusions, survives the complete startup sweep byte for byte in C# AND Node. RED on today's selector (the consultant ran it: `coai-panel-live` at 11 minutes is selected; without `coai-chat-` in the rule, `coai-chat-live` is selected) | S2-A |
| 14 | **coai's own:** `.then(process.exit).finally(cleanup)` never cleans (`run-host.mjs:115-127`) | `runHost.test.mjs` with an injected launch that resolves and one that rejects: the owned folders and claims are gone after `main` settles. RED on `main` today | S4 |
| 15 | **coai's own:** a claim whose `<os>` is Node's `win32`/`darwin` (`OwnerClaim.cs:38`) is foreign to every reader of the contract's grammar | the C# and Node writers emit `windows`/`macos`/`linux`; the copied `Owner.Parse` reads the C# claim and the reference's `parseOwner` reads it too (the vectors file of the template, `OwnerVectors.cs`, extended by coai's cross-reader rows). RED with the old tag | S3, S4 |
| 16 | **coai's own:** a move that replaces a claim (`WorktreeManager.WriteOwner` shape) and a two-valued owner (`OwnerIsAlive`, `OwnerClaim.Judge`) | `AClaimThatAlreadyExists_IsNeverOverwritten` (October sc. 5a, kept): the helper handed a taken name throws, removes only what it made, the existing claim keeps its bytes; the three-valued `Liveness.State` table of the template (`LivenessStateTests`) copied. RED with a `File.Move`/`WriteAllText` claim | S3 |

*[epoch 4, 2026-11-03]* Row 15's "the vectors file of the template, `OwnerVectors.cs`, extended by coai's cross-reader rows" cannot be done
in the copy: `OwnerVectors.cs` reads the MOUNT's file (`OwnerVectors.cs:11`, another repository's). coai's cross-reader
rows go in a coai-owned file, `shared/owner-record-cross-vectors.json` (the name of S3's brief), read by coai's own test.

## S1 — The pin bump to `105d53c` — DONE

coai #936 (`df375eb4`, 2026-11-01): `chore(rules): pin shared conventions to release 105d53c; the rules-budget canaries
re-measured against the test-leftovers core`. Merged alone, as R6 decision 1 allows (coai had no owner-form producer and
no prefix sweeper on `main` — the age-only `coai-` sweep is not a prefix sweeper, D7). The canary numbers are in that
PR. Two CodeRabbit review-body notes were read after the merge; the one doc figure they found is fixed in coai #938
(R7's record). Nothing of S1 is built in this epic; the October S1 text (the `e29883f` → E1 move, #848's order) is
history: #848 merged first (`DefaultBudgetBytes = 90_000`, `RuleFiles.cs:181`), then #936 re-measured the canary.

## S2 — The sweep, in two halves — **cornerstone, `model: fable`**

- **Models.** Spec by Fable in [PLAN_test_leftovers_E5_S2_spec.md](PLAN_test_leftovers_E5_S2_spec.md) (its own row in
  `todo/README.md`; the shape of `PLAN_the_plan_gate_reads_the_committed_plan_E1_S2_spec.md`), covering BOTH halves —
  the S2-A half written 2026-11-02, the S2-B half added to the same file after S2-A merges; code by a Sonnet coder from
  the spec; review by Opus against the spec, rule §3–§4 and the template's `LeftoversSweep.cs`.
- **Goal.** (A) Neither test runner deletes anything on age any more, and the invariant **every `coai-*` name is
  outside the test sweep** holds structurally — so being wrong here can never delete a live chat, a round's answers, a
  Team server job or a person's credentials behind a junction. (B) Every C# suite and the extension runner starts with
  the owner-aware sweep of rule §4 over `^testsToDelete-<owner>-coai-` — scope and `gone`, by identity, through the
  quarantine — and lists everything else with its reason; the product's own sweep is byte-for-byte unchanged.

### S2-A — stage A: the age-only `coai-` sweep is retired (one PR, merges alone, first)

- **Files.** `shared/temp-sweep.json`; `src_vs_code/scripts/sweepTemp.mjs`, `run-tests.mjs` (`sweepFirst`);
  `src_mcp/tests/TempDirsAreSwept.cs` (the constructor), `SweepRule.cs`; `src_vs_code/src/test/sweepTemp.test.mjs`;
  new `src_mcp/tests/PruneOldScratchDirsCharacterizationTests.cs`; `research/module_tests.md` (`:410-426`).
- **What the Fable spec must settle (A):**
  1. **The shape of `temp-sweep.json`** once no test sweep removes by it: whether `prefix` and `keepMinutes` go or stay
     as documentation (two runners and old checkouts read the file — an old checkout's `SweepRule` reading a renamed key
     must not crash or widen); `neverSwept` stays, unchanged in value, as the documented list the three product-prefix
     scans keep complete (`TempDirsAreSwept.cs:104-116`, `sweepTemp.test.mjs:46`, `TheTestSweepLeavesAJobAloneTests.cs:27`
     stay `coai-` and green) and the list the one-off clean-up leaves alone. The `why` text is rewritten to say the file
     documents and no test sweep deletes by it.
  2. **The sweeps stop deleting.** `sweepFirst` (`run-tests.mjs:103-111`) and the `TempDirsAreSwept` constructor
     (`:76-85`) no longer remove anything: `TempDirsAreSwept` stops calling `PanelService.PruneOldScratchDirs`; the Node
     `sweep` is removed or reduced to a LISTING that reports counts of `coai-*` entries older than ten minutes outside
     `neverSwept` (the spec decides which; a listing line is what the barrier scan passes). The four TEST-prefix pins
     (`TempDirsAreSwept.cs:46`, `:83-84`/`:137`, `:90`; `sweepTemp.test.mjs:32` and its fixtures) move or go; the spec
     names which fixture tests are rewritten and which, pinning only the retired age-only selection, are deleted (none
     becomes an "old-branch" test).
  3. **The invariant, stated and tested, in C# and Node:** EVERY `coai-*` name is outside the test sweep, at any age,
     whatever `neverSwept` holds — each protected product prefix (`coai-answers-`, `coai-repair-`, `coai-noworkspace-`,
     `coai-plan-`, `coai-server-job-`, `coai-chat-`, `coai-question-`, `coai-check-`, `^coai-[0-9]`), an old test name
     (`coai-panel-x`) and an invented future prefix (`coai-futurething-x`) fed to both selectors at ten days old with
     `neverSwept` injected EMPTY ⇒ none selected. **The RED-first scenario (must-not-copy #13)**, in BOTH
     implementations, observed failing before the change: a private fake temp root; an UNMARKED `coai-chat-live` folder
     holding a file with sentinel bytes, last write 11 minutes ago; the rule injected with `coai-chat-` OMITTED; the
     COMPLETE startup sweep run (Node: as `sweepFirst` calls it; C#: the fixture's entry point with root and rule
     injected) ⇒ the sentinel survives byte for byte. Positive control: nothing — stage A has no removal to prove; the
     control moves to S2-B.
  4. **"Provably unchanged" as a check, not a sentence** (plan-gate round 1, kept): the CHARACTERIZATION test of
     `PanelService.PruneOldScratchDirs` (`PanelService.cs:1475-1489`) lands on `main` FIRST, before any other S2-A
     change: a private root with controlled inputs — the default `ScratchPrefixes` and an explicit prefix list, an
     `except` list, a version cache, a folder each side of the cutoff set by `Directory.SetLastWriteTimeUtc`, a
     read-only file — driven through the function's own `cutoff` argument, asserting exactly which names remain. It stays
     green, unchanged, in every E5 PR, beside `git diff --exit-code origin/main -- src_mcp/src/Server/PanelService.cs`
     and the product sweep's own tests (`ScratchDirsTests`, `TheScratchSweepNeverDeletesAKimiHomeUnderALiveTreeTests`,
     `ConsultCheckSweepTests`, `ConsultantHomeSweepTests`, `AScratchSweepRunsOnTheConsultationBeatTests`,
     `chatStoreSweep.test.ts`). Scope control, not safety: item 3 is the safety.
  5. **The one-off clean-up of old-prefix test leftovers** (D4) written into `research/module_tests.md`: a command a
     person runs that first LISTS the temp root's `coai-*` directories with their last-write times and identities,
     leaving out every protected product prefix and the version cache, and a second command that removes only the
     exact names the person picked — and, *[round-1 finding 6]*, **re-checks each pick just before its removal**:
     still present, unchanged since the listing (identity and last-write time equal to the listing's), not a protected
     product prefix or the version cache, and no live owner (`.owner` record `live` or `unknown` ⇒ refuse); any failed
     check REFUSES that path with one line and moves on — run with no coai session or VS Code window open on that
     machine; and, NEW, the listing of legacy `testsToDelete-coai-run-*` folders and their `.owner` files with the
     `rm` line the sweep prints. The spec writes the command as a script in `scripts/` with a test over a private
     root (a path changed after the listing ⇒ refused; a product prefix ⇒ refused; a live claim ⇒ refused; an
     unchanged pick ⇒ removed). *[round-2 finding 0]* The removal step is an EXCLUSIVE rename to a quarantine name
     while the listed identity still holds, then the removal of the quarantined object (D4); **test: the folder is
     SWAPPED after the listing** — the listed folder is renamed away and another folder (or a link) is put at the
     same name between the listing and the removal ⇒ the identity differs ⇒ refused, the swapped-in object keeps its
     bytes, nothing is renamed; and a quarantine name pre-created at the drawn name ⇒ the exclusive rename fails ⇒
     refused. RED with a plain `rm -rf`/`Directory.Delete` by name. Never wired into a runner, never a sweep, never
     delegated to the janitor. The residual risk of old checkouts recorded.
  6. **Break-its**, each observed red then reverted: a `coai-` match restored in either selector ⇒ item 3 red;
     `neverSwept` emptied ⇒ the three product-prefix scans red (the invariant test stays GREEN — that is the point of
     it); the characterization test's cutoff moved ⇒ red.
- **Commands.** `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
  --filter-class "*TempDirsAreSwept" --filter-class "*PruneOldScratchDirs*"` then the whole suite; `cd src_vs_code &&
  npm test`; `node .agents/conventions/tools/sweeper-scan.mjs --root .` before and after (both list no deleting line;
  the after-listing is recorded in the PR as coai's stage-A record for R6).
- **DoD (A).** The characterization test merged first and green unchanged; the RED-first scenario observed red on
  today's selector in C# AND Node, then green; the invariant test green with `neverSwept` empty; no test sweep deletes
  anything; the three product-prefix scans unchanged and green; `module_tests.md` describes the retired sweep, the
  invariant, the one-off clean-up and the residual risk; the stage-A record written to the PR for the parent's R6.

### S2-B — stage B: the owner-aware sweep, both runners (commit 2 of the producer PR)

- **Files.** The copied `LeftoversSweep.cs`, `LeftoversSweepFixture.cs` and their tests (S3 brought them); the four
  C# test projects' `[assembly: AssemblyFixture]` wiring; `src_vs_code/scripts/run-tests.mjs` (`sweepFirst` becomes
  `sweepLeftovers` of the mount); `src_vs_code/scripts/owned.mjs` (D3); `src_vs_code/src/test/sweepTemp.test.mjs` →
  `leftoversSweep.test.mjs`; `research/module_tests.md`.
  *[2026-11-03, S2-B spec B.10 item 4]* `sweepTemp.test.mjs` was deleted by S2-A (#940); its survivors are
  `src_vs_code/src/test/startOfRun.test.mjs`. S2-B ADDS `src_vs_code/src/test/leftoversSweep.test.mjs` beside it and
  renames nothing; `startOfRun.test.mjs` loses the legacy-count tests (N7, N10, N8's arms).
  *[epoch 4, 2026-11-03]* "(S3 brought them)" is corrected: the copied `LeftoversSweepTests` take the fixture as a constructor
  parameter, so they land HERE, when the fixture is wired. S2-B registers `LeftoversSweepFixture` as the assembly fixture
  in EACH of the four test assemblies (each its own `[assembly: AssemblyFixture(...)]` line; the template's line is a
  fully qualified mcp type, `LeftoversSweepFixture.cs:3`). S3 brings the sweep's source only.
  *[re-review plan gate, 2026-11-03, session `7a6707ad`, finding e1]* **The order must be PROVEN, not assumed.** S2's
  spec writes a test on the pinned xUnit v3 version (4.0.1) that shows the start-of-run sweep FINISHES before any test
  collection runs: the fixture records the sweep's end, the earliest test of every collection records its start, and
  the test asserts the end is before every start. If the pinned version does not guarantee it, the sweep moves into the
  gate (`RunTemp.Gate`, before discovery), and the spec says so.
- **What the Fable spec must settle (B):**
  1. **Where the C# sweep runs and who calls it.** The copied `LeftoversSweepFixture` as an assembly fixture in EACH of
     the four C# suites (rule §4: every run that creates the kind sweeps it at its start — all four make a run folder
     after S3). *[round-1 finding 1]* **Copied byte-identical it would sweep `Path.GetTempPath()`, which `RunTemp`
     has already redirected to the run folder — so it would never see a killed run's folder.** `LeftoversSweepFixture.cs`
     is therefore an EDIT POINT of the copy (D2, the boundary table): its sweep root is `RunTemp.Real` (the machine's
     root — the run folder's PARENT, where the run folders of killed runs are), never the run folder itself. Test
     `TheSweepRoot_IsTheMachineTempRoot`: with `TEMP`/`TMP`/`TMPDIR` pointed at a run folder, the fixture's sweep is
     handed `RunTemp.Real` (asserted through the hooks seam that records the root), and a killed run's folder planted
     in `RunTemp.Real` is found while one planted inside the run folder is not looked at. RED with the template's
     unedited root. The order with `RunTemp.Start` (the module initializer makes the run folder BEFORE any fixture: the
     sweep's ten-minute age keeps it off the folder just made, and its claim names a live process — asserted).
  2. **The Node sweep is the reference.** `run-tests.mjs` calls `sweepLeftovers({ repo: <root>, tmp: <the REAL temp
     root, read before `beginRun` moves the variables>, ... })` with the `coai` wrap's patterns; its report lines go to
     stdout in `sweepFirst`'s place; it never fails the run (a throw is one line, as today's `sweepFirst` catch).
     `run-host.mjs` and the runner scripts do NOT sweep (one sweeper per runner invocation of the main suites is the
     family's figure; a runner script's own folder is collected by the next `npm test` or C# run). *[round-1 finding
     5]* **The direct `node --test "scripts/**/*.test.mjs"` command of `scripts/lib` (`ci.yml:196-197`, and the same
     command on a developer machine) DOES sweep**: it is a test run that creates the kind, with no runner in front of
     it — so it gets a start-of-run sweep through a preload, `node --import ./scripts/lib/leftovers-preload.mjs --test
     …` (or a tiny runner `scripts/run-lib-tests.mjs` that calls the sweep and then `node --test`; the spec picks one),
     which calls the reference's `sweepLeftovers` on the REAL temp root before the first test file loads, prints its
     report, and never fails the run. Test `scriptsLibRun.test.mjs`: a killed run's owner-form folder with a `gone`
     claim (injected probe) planted in a private root; the command's entry point run with that root; the folder is
     collected before the first test; the `package.json`/CI command is pinned to carry the preload. RED on `main`
     (no sweep in front of `node --test`).
     *[2026-11-03, S2-B spec B.1 § 7, B.10 item 9 — not a change: this item already allows the runner]* **The tiny
     runner is picked: `scripts/run-lib-tests.mjs`**, with its test `scripts/run-lib-tests.test.mjs` and `ci.yml:197`
     running it. Not the `--import` preload: `--import` sits in `process.execArgv`, which `node --test` hands to every
     test-file child, so a preload would sweep once per test file, concurrently, over the real temp root. Every
     "preload" below (S4 Files, S4's direct-start note, S4's scenarios, Q5) reads as this runner.
  3. **Legacy names** (D4): the C# copy lists `testsToDelete-coai-run-*` of the old grammar with the exact `rm`
     command; the Node reference collects one only on kernel owner AND old-grammar `gone`; a legacy claim whose `<os>`
     is `win32`/`darwin` reads as `unknown` and is kept — asserted in both.
     *[rollout entry 4.1, 2026-11-03]* Read with D4's correction: at the pin the Node reference's "collects on kernel
     owner AND `gone`" never fires (Linux: always `unknown`; Windows: list-only; `win32`/`darwin` tags unreadable), so
     BOTH runners list and neither collects a legacy name; the one-off clean-up is the only remover. The spec asserts the
     listing in both (B7, N-S2) and nothing more.
  4. **Two sweeps at once** (plan-gate round 1 item 7, kept): a gone owner's folder removed by another sweep between
     the listing and the delete (an injected deleter that removes it first, and once two real sweeps over one private
     root at once) ⇒ no failure, no report line for the other's removal, no throw; a quarantine the OTHER sweep made
     ⇒ `quarantine-conflict`, listed. Never the REAL probe on a dead child's pid (Windows hands exited pids to the next
     process — E1 met it mid-suite): a kill is simulated with an injected probe.
  5. **The scenarios with their RED symptoms** (the template's `LeftoversSweepTests` copied give most; coai adds): a
     `testsToDelete-<owner>-coai-run-…` folder OLDER than the window whose claim names a live process (this process)
     survives; one with a foreign-host claim, a foreign-owner segment, an unreadable claim and a claim of another token
     (`testsToDelete-<owner>-mcp-x-abc123`) survive, listed; a probe-`gone` owner's folder and claim go through the
     quarantine; an unmarked owner-form folder is listed, never removed; an orphan claim with a `gone` owner goes, with
     a `live`/`unknown`/foreign owner stays; a prefixed folder holding a `.git` FILE is "other clone"; a FILE with the
     prefix is skipped; `TESTSTODELETE-COAI-x` upper-case and `teststodelete_u1000_coai_x` are matched; a junction
     inside a gone owner's folder is removed by its own name and its target keeps its bytes; `coai-chat-…`,
     `coai-answers-…`, `coai-0.58.0` and `coai-panel-…` of any age are never looked at (the invariant of S2-A, now
     structural: the pattern is `^teststodelete[-_]<owner>[-_]coai[-_]`); must-not-copy #3, #4, #7, #8, #12 above.
  6. **Break-its**, each observed red: the owner segment dropped from the pattern ⇒ `owner-blind` in the guard AND the
     foreign-owner scenario red; `unknown` mapped to `gone`; the age used to authorise (a live claim's folder older than
     the window removed) ⇒ red; the quarantine rename replaced by `Directory.Move` onto an existing name ⇒ #3 red; the
     sweep run on the run folder instead of its parent ⇒ the "fresh run folder survives" scenario red.
- **Commands.** The four executables through `low-priority.mjs` (filtered to `*Leftovers*` first, then whole); `cd
  src_vs_code && npm test`; on Windows, inside WSL, and the macOS leg; after a full run of every suite `bash
  .agents/conventions/tools/janitor.sh --token coai --dry-run` on Windows (Git for Windows' bash) and in WSL lists only
  folders of a live run.
- **DoD (B).** Every scenario RED then GREEN on Windows, Linux and macOS; the break-its red; `PanelService.cs` and the
  characterization test unchanged; the product sweep's tests green; legacy names listed (C#) / collected on `gone` only
  (Node); concurrent sweeps silent on not-found; `module_tests.md` describes the one sweep in two languages.
  *[epoch 5, 2026-11-04]* "collected on `gone` only (Node)" is read with the parent's entry 4.1: at the pin the Node
  reference collects NO legacy coai folder (`legacyOwnerState` answers `unknown` on Linux; Windows only lists; E2's
  `win32`/`darwin` tags do not parse), so the Node sweep LISTS each one, exactly as the C# one; S2-A's one-off clean-up
  is the only removal. S2-B as built (#945) already does this (N-S2, the C# `LegacyNames…` tests).
- **The claim-first race (2026-11-03, re-review gate, finding 1).** S3's initializer claims and creates the run folder
  before S2-B's sweep runs in the assembly fixture. S2-B adds a scenario where a sweep runs AFTER this process's claim is
  written and BEFORE its folder exists (a hook between the two): the sweep never removes or quarantines a claim or folder
  of the current process's own record, on every OS, and S2's spec states the startup order it relies on.

## S3 — The C# library (a copy), the run folder on the owner form, the contract's claim grammar (commit 1 of the producer PR)

- **Models.** Brief by Opus (this section); code by a Sonnet coder; review by Opus against rule §1–§3, D1, D2, the
  template's files (a `diff` of every copied file against `dew_flow_mcp` `197cc76` minus the namespace line is in the PR
  and is empty), and the must-not-copy rows #6, #9, #10, #11, #15, #16.
- **Goal.** One C# way to own a folder in a shared namespace, copied from the template; the run folder of all four
  C# suites made through it (claim first, exclusive create, owner form); every C# claim in the contract's grammar.
- **Files.** New `src_mcp/tests_leftovers/` (D2) with the copied helper files; `src_mcp/tests/Leftovers/` with the copied
  tests; `src_mcp/tests/RunTemp.cs`, `RunTempFolder.cs`, `RunTempFixture.cs`, `OwnerClaim.cs`, `TempDir.cs` moved into
  the library; `src_bugs/tests/Scratch.cs` calls into the library's `Removal.Quietly`; the four `.csproj` files and the
  `.slnx`; `src_server/tests`, `src_bugs/tests`, `src_bench/CoaiBench.Tests` gain the run folder — *[round-1 finding
  2]* **a `[ModuleInitializer]` runs only when its OWN assembly loads**, so a referenced library's initializer is not
  the test assembly's: EACH of the three suites gets its own one-line `[ModuleInitializer]` (`RunTempStart.cs`) that
  calls `RunTemp.Start()` (made idempotent: a second call is a no-op), with a test per assembly, first in the
  assembly's own order, that `TEMP`, `TMP` and `TMPDIR` already name the run folder before the first test
  (`src_mcp/tests` keeps its own initializer in `RunTemp.cs` the same way); the coai wrapper
  `src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs` *[round-1 finding 0]*;
  `src_mcp/tests/OwnerClaimTests.cs`, `OwnerClaimVectorsTests.cs`, `RunTempTests.cs`, `RunTempHardeningTests.cs`
  rewritten against the new grammar; `research/module_tests.md`.

**The change.** `RunTempFolder.Create` makes the run folder through `OwnedDir.Create(scope, "run")` of the library:
the name `testsToDelete-<owner>-coai-run-<id>` (six `[A-Za-z0-9]`, `RandomNumberGenerator`), the claim
`<owner>:<host>@<os>:<pid>[:<start>[:<ns>]]` written FIRST with `FileMode.CreateNew`/`FileShare.None` (identity checked
before and after), then the restore registered, then the exclusive `mkdir`, then the three variables moved — the
order E2 tested (`RunTempFolder.cs:78-99`) changes in exactly one place: the claim moves from last to first, and
E2's order test is rewritten to the contract's order. `RunTemp.Real` stays the machine's root. `OwnerClaim` becomes a
thin reader over the copied `Owner.Parse` and `Liveness.State`; its `win32|linux|darwin` shape (`:38`) goes, with the
vectors file extended by cross-reader rows (#15). `TempDir.For(prefix)` keeps its signature and its in-run plain names,
and gains `TempDir.Root` = `Owned.TempRoot()` (the library's ONE `Path.GetTempPath()`; a test that needs the machine's
root uses `RunTemp.Real`). `Removal.Quietly` is the one deleter for the library, `TempDir.Dispose` and
`Scratch.Delete` (which keeps its SQLite pool clearing). The Windows path keeps both keys (R6 decision 2; #6); the
Darwin path is copied whole (`NativeDarwin.cs`, `DarwinLayout.cs`, `Errno.cs`, the FIFO child) because coai's macOS
leg runs three C# suites (`ci.yml:281-287`).

**Scenarios** (RED first; the template's `OwnedTests`, `OwnerTests`, `LivenessTests`, `LivenessStateTests`,
`NativeTests`, `NativeDarwinTests`, `DarwinLayoutTests`, `ScopeTests`, `ErrnoTests`, `RunnerFactsTests` copied give the
helper's; coai adds):

1. `TheRunFolder_IsOwnerForm_AndClaimedFirst` — a private root through `RunTempSeams`: the recorded order is claim →
   restore registered → mkdir → variables; the name matches `^testsToDelete-[uw][0-9]{1,10}-coai-run-[A-Za-z0-9]{6}$`;
   the claim parses to this process with the tag of this OS (`linux|macos|windows`). RED on `main` (claim last, legacy
   name, `win32`/`darwin`).
2. `AllFourSuites_RunInsideARunFolder` — in each of the four suites, one test, ordered first in that assembly,
   asserts `Path.GetTempPath()` is a run folder of the owner form inside `RunTemp.Real` and that `TEMP`, `TMP`,
   `TMPDIR` name it BEFORE the first test *[round-1 finding 2]*. RED in the three suites that have none today.
3. `ATakenName_IsNeverAdopted` (#16): a claim pre-created at the drawn name ⇒ redraw; a folder pre-created at the
   claimed name ⇒ warn once, own claim removed only while unchanged, redraw.
3a. *[round-1 finding 3]* **`ARunThatCannotMakeItsFolder_FailsBeforeAnyTest`** — after `MaxDraws`, or on an
   unwritable root, the run does NOT continue in the machine's temp: the initializer writes ONE line naming the root
   and the cause and the suite FAILS before any test runs (a non-zero exit from the entry point, never a throw from
   the initializer that would look like a crash — the line says what to do). **E2's never-throw machine-temp fallback
   (`RunTemp.cs:34-47`, `CreationWarning`, "the run CONTINUES … WITHOUT isolation") is WITHDRAWN**: a run without its
   owned folder is a run whose every leftover is unowned and unsweepable. Test: a private read-only root (Linux: a
   parent made read-only, the premise asserted; Windows: a root whose ACL denies create) ⇒ the suite's exit is
   non-zero, zero tests ran, the line is on stderr. RED on `main` (the run continues).
4. Step 0 on Windows (#6), `Errno.For` (#9), `Require()` first (#10), the helper's tests inside an owned scope (#11).
5. `OldAndNewClaims_AreReadByBothReaders` (#15): the vectors.
6. `ALostLinkTarget_StillFailsTheTest` — the existing junction case keeps re-throwing (the E4.1 guarantee of the
   Vibe-and-Kimi plan survives the move; `TempDir.cs:49-53`).
7. **A killed run is collected** — a child suite executable (as E4's harness; with a filter and an env flag) makes its
   run folder and `Environment.FailFast`s; the folder and its claim remain and name the child's pid; S2-B's sweep with an
   injected probe `gone` and `now + 11 min` removes both through the quarantine; with `live`, keeps both.

- **Commands.** `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`; the four executables through
  `low-priority.mjs`; `dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes`; on Windows (CRLF) and in CI.
- **DoD.** Every scenario RED then GREEN; the copy diff empty but the namespace; the 346 `TempDir.For` callers compile
  unchanged; all four suites green on the three legs with the same test counts (before/after in the PR); the private
  machine-temp measurement of E2 repeated for each of the four suites: 0 entries after a whole run.

**S5's deferred C# sites** *[epoch 3, 2026-11-03]*. S3 takes the C# sites that S5 may not rename because an invocation
runs them outside a run folder (D10), and drops their bare prefix in the same commit that gives them the owned folder:

- `src_server/tests/GaugeCaptureTests.cs:30` — `Path.Combine(Path.GetTempPath(), "testsToDelete-gauge-" + Guid…)`, a
  top-level folder in the machine temp, removed in `Dispose`. After S3, `src_server/tests` runs in a run folder under
  every invocation (`ci.yml:77`, `:285`, `sonarcloud.yml`), and the name drops the prefix (`gauge-…`).
- every `src_mcp/tests` site that the S5 `src_mcp/tests` PR lists as created OUTSIDE the run folder — a path under
  another root that the three variables do not redirect (for example `/tmp` inside a WSL distro, the user profile,
  `LocalAppData`, a fixed path, a path handed in from outside). The re-review's grep of `a3b73534` (`/tmp`, `mktemp`,
  `$TMPDIR` beside a bare-prefix name) found none; the S5 PR's classification is the record, and each site it lists
  is named here when that PR is written.
- **`RunTemp.Start` does not fail closed today** (the Opus judge's advice at the re-review): when it cannot make the
  folder it catches the `IOException`/`UnauthorizedAccessException`, writes `CreationWarning` (`RunTemp.cs:45`, "the
  run goes on WITHOUT isolation") and leaves `Folder = Real`, so every `src_mcp/tests` site then runs in the machine's
  temp. S3 MUST change it to fail closed: sc. 3a (one line, a non-zero exit, zero tests run). The same holds for the
  three new initializers and, in S4, for `run-tests.mjs` `beginRun` (`:221-230`, which catches the error and goes on
  "as before"). Until then this fallback is the one uncontained path of an eligible S5 site, and the S5 `src_mcp/tests`
  PR names it.
- **How a C# executable fails closed is S3's spec to define and test (2026-11-03, re-review gate, finding 0).** A module
  initializer has no supported way to set the test host's exit code, and a bare throw from it crashes the host instead of
  failing the run. S3's spec names the mechanism for each MTP executable (for example: the initializer records the
  failure, and a startup hook or assembly fixture fails the run before any test runs), its observed non-zero exit code and
  its one-line message, and tests it on the three OSes.
- **Fails closed, AND a test started without its run folder refuses** *[after the re-review gate, 2026-11-03,
  finding 2]*. In C# the run folder is made by the test assembly's own module initializer, so every start of the
  assembly (the executable, `dotnet test`, `dotnet-coverage`, an IDE) runs it. S3 adds the refusal for a start that
  skips or loses it: a process-wide check, run before any test (in the module initializer or an assembly fixture),
  verifies that `TEMP`, `TMP` and `TMPDIR` name an owner-form run folder this process made. Otherwise it fails the run
  with one line naming the command to use. A test-level check cannot guarantee that no other test created resources
  first: the suites run collections in parallel (`maxParallelThreads: 8`) with no enforced order [PR review, CodeRabbit]. Test: the suite started with the initializer's
  folder removed from the variables ⇒ that line, a non-zero exit, nothing created in the machine temp. RED on `main`.

Scenario: `S5DeferredCSharpSites_AreInsideTheRunFolder` — for each listed site, the suite that runs it asserts that
the site's path is inside `Path.GetTempPath()` = the run folder; the guard (S6) reports zero `legacy-name` for the
file. DoD addition: every listed site renamed, each in an invocation that the inventory shows as contained.

**S3 as revised at the epoch-4 re-review (2026-11-03).** S3's build brief (by Opus, 2026-11-03, against this document
at `004c55fb`) found the parent's entries 3.1 and 3.2 and the items below. Each is settled here; where this block and
the text above differ, this block wins.

- **Review.** Opus reviews against D2's MANIFEST and its diff check, not against "a diff empty but the namespace".
- **Files, added or changed.** The manifest and the diff check (D2). `RunTemp.cs` moves into the library;
  `src_mcp/tests` does NOT keep its initializer in `RunTemp.cs`: each of the FOUR test projects gets `RunTempStart.cs`
  (its `[ModuleInitializer]`) and `TestEntryPoint.cs` (the gated `Main`). `[Fact]` files without a `*Tests` name
  (`DarwinFifoChild.cs`, `LeftoversSelfTest.cs`) go to the test project. The audit source lands unwired (D9's note).
  `sonarcloud.yml:254`'s `sonar.coverage.exclusions` gains `src_mcp/tests_leftovers/**`, which `**/tests/**` does not
  match. A coai-owned cross-reader vectors file, `shared/owner-record-cross-vectors.json` (#15's note).
- **The order of the run folder.** `OwnedDir.Create` writes the claim (exclusively, first) and makes the folder in ONE
  call; the restore of the three variables is registered AFTER it returns (the template's own shape,
  `dew_flow_mcp · tests/Mcp.Tests/Leftovers/OwnedDir.cs:6-9`). A restore registered between the claim and the folder
  could, after a redraw past a squatter, remove by a name the run never made. Scenario 1's recorded order becomes:
  claim → folder (one `Create` call) → restore registered → variables. S2-B's hook between the claim and the folder is
  `RunTempSeams.AfterClaim`, forwarded to `OwnedDir.Create(…, afterClaim:)` (`OwnedDir.cs:136` there).
- **`TempDir` keeps its deleter.** `TempDir.Dispose` keeps `GitScratch.DeleteEvenIfReadOnly` (the read-only clearing that
  `TempDir` and E2's run folder rely on; `RunTempFolder.cs:18`) and keeps re-throwing `LinkTargetLostException`
  (`TempDir.cs:49`; scenario 6). "`Removal.Quietly` is the one deleter for … `TempDir.Dispose`" (the change, above) is
  WITHDRAWN: `Removal.Quietly` never throws and does not clear read-only bits. It stays the deleter of the library's
  owned folders.
- **The fail-closed mechanism: a gated custom `Main`.** Each test project sets
  `<XunitAutoGeneratedEntryPoint>false</XunitAutoGeneratedEntryPoint>` and has its own `Main` in `TestEntryPoint.cs`:
  xUnit's generated body, UNCHANGED, behind `RunTemp.Gate`. The module initializer runs first and makes the folder. The
  gate refuses when the folder could not be made, the claim could not be written (fail closed covers the claim too),
  the macOS layout does not hold (`DarwinLayout.Require`), the initializer did not run, the folder is gone, or `TEMP`,
  `TMP` and `TMPDIR` do not all name it. A refused run: exit **78** (EX_CONFIG; outside MTP's 0-13; coai already uses 78
  for a configuration fault, `src_mcp/src/Api/ProbeApiMode.cs:41`), stdout empty, ONE stderr line that starts
  `coai tests: no test ran — ` and names the executable to run. `Main` runs before discovery, any fixture, any
  `MemberData` and any test, for every start of the assembly (the executable, MTP `dotnet test`, `dotnet-coverage
  collect --`, an IDE). This settles the two notes above ("How a C# executable fails closed", "Fails closed, AND a test
  started without its run folder refuses") and scenario 3a. Measured by the brief on a scratch build (xunit.v3 4.0.1,
  Microsoft.Testing.Platform 2.4.0): refused with exit 78 before discovery; a throw from `ITestPipelineStartup.StartAsync`
  or from the module initializer aborts the host (exit 134); an assembly fixture runs after discovery; the platform's
  lifetime hooks give no supported way to set the exit code. This revision did not re-run that measurement.
- **Scenario 2's "ordered first"** cannot hold: the suites run collections in parallel (`maxParallelThreads: 8` in all
  four `xunit.runner.json`). The guarantee is the gate in `Main`. The per-assembly test that asserts `TEMP` names the
  run folder stays (it carries the baseline's proof marker for S6) and needs no order once F24 is pinned.
- **Commands.** "on Windows (CRLF)" is superseded: until P1 is recorded, this branch's C# suites do not run on the
  Windows side of an unrecorded machine; Windows evidence comes from the `windows-latest` CI leg only. Local runs: WSL,
  after a fresh barrier scan. The macOS steps are `ci.yml:282`, `:285`, `:288` on `004c55f` (the change above says
  `:281-287`).
- **DoD (S3, revised).** Every scenario RED then GREEN; the diff check green against the manifest; the suite × OS
  matrix: every suite green on every leg CI runs it on — Linux: all four (`ci.yml:58`, `:64`, `:77`, `:88`); macOS: mcp,
  server, bench (`:282`, `:285`, `:288`); Windows: mcp (`:317`); the ingest suite is Linux-only by design (`:258-259`);
  and at release time `release.yml` server-binaries runs the server suite on Windows (x64, arm) and macOS as well, so the
  server suite DOES run on Windows (checked job by job at 004c55f, [PR review on the stop, CodeRabbit]) —
  AND the run-folder, fail-closed and refusal tests of EVERY suite green on all three OSes (filtered steps on the legs
  that do not run the whole suite, or a small cross-OS job where a suite cannot run there); the counts reconciled BY
  NAME (each test added, removed or skipped by platform, listed in the PR), not "the same counts"; the private
  machine-temp measurement of E2 repeated per suite on Linux: 0 entries after a whole run.

*[re-review plan gates, 2026-11-03: the parent's session `7c66b50d`, findings r0–r5; this document's session `7a6707ad`, findings e0–e2]* Four additions to S3, and one rejection:

- **The CI steps are D8's merge-gate table** (findings r0, r3). S3 adds: in job `windows`, `Step 0` first and, after
  `Test`, the filtered `CoaiServer.Tests.exe --filter-class "*RunFolderTests"`; in job `macos`, `The stat layout
  self-test` and `Leftovers` before `Test`. The S3 brief's filtered `CoaiBugs.Tests` and `CoaiBench.Tests` steps on
  Windows, and `CoaiBugs.Tests` on macOS, are WITHDRAWN: the bugs suite runs on Linux only, and bench never runs on
  Windows. The DoD above ("the run-folder, fail-closed and refusal tests of EVERY suite green on all three OSes") is read
  as that table: the library's proofs on all three OSes; each suite's wiring where its step runs.
- **Pure information flags pass** (finding e0). `RunTemp.Gate` lets `--help`, `--version` and `--info` through: they run
  no test and no discovery. The module initializer has already made, claimed and registered the run folder before
  `Main` runs (on every start of the assembly), so these flags run INSIDE it and the folder is removed at exit like any
  run's; the gate does not refuse them for a missing folder [PR review, CodeRabbit: the first text said "with no run
  folder", which the initializer makes impossible]. `--list-tests` is gated like a test run, because discovery runs
  `MemberData`. A test for both: each information flag exits as the platform does, and its run folder is gone after it;
  `--list-tests` with the folder forced to fail exits 78 with the one line, and the `MemberData` sentinel is untouched.
- **A fallback behind the gate** (finding r4). Besides the gated `Main`, a check at the first point any test can reach
  (the `RunTempFixture` assembly fixture) fails the run with the SAME one line when the gate did not run (a host that
  bypassed `Main`). The module initializer still makes the folder; the fallback refuses when `RunTemp.IsActive` is
  false. Test: the fixture started with the gate flag unset ⇒ the line, and no test body runs.
- **The diff check's golden source** (finding r5). A committed manifest in coai (next to the library) holds, per copied
  file, the upstream path at `dew_flow_mcp` `197cc76`, the upstream git blob SHA and the expected list of
  transformations. The check reads the copy, undoes the listed transformations, and compares the result's blob SHA
  with the manifest's. It needs no network and keeps NO vendored copy of the upstream files; when it is red, a reviewer
  fetches the named blob from dew_flow_mcp.
- *Rejected (finding e3):* that `XunitAutoGeneratedEntryPoint=false` with a custom `Main` may not build or run. The S3
  brief built and ran it (xunit.v3 4.0.1, Microsoft.Testing.Platform 2.4.0): a refused run exits 78 before discovery.

## S4 — The Node producers wrap the reference (commit 3 of the producer PR)

- **Models.** Brief by Opus; code by a Sonnet coder; review by Opus against rule §1–§3, D1, D3, decision 11 and
  must-not-copy #12, #14, #15.
- **Goal.** Every Node runner, script test and the editor-host run that runs OUTSIDE a run folder owns its folder
  through the shipped `owned.mjs` with the token `coai`, in the contract's grammar, and leaks nothing.
- **Files.** New `src_vs_code/scripts/owned.mjs` (D3: the one `ownedFor` call, re-exported); `run-tests.mjs`
  (`makeRunTemp`/`removeRunTemp`/`claimText`/`linuxStartSeconds` `:114-209` replaced by the wrap: `ownedDir` with a
  scope whose `after` is the runner's `finally`; `beginRun`'s "the tests share the machine's temp as before" fallback
  (`:210-220`) is WITHDRAWN *[round-1 finding 3]*: a run that cannot make its folder prints one line and exits
  non-zero before the first batch — test in `runTests.test.mjs`, RED on `main`); `scripts/lib/leftovers-preload.mjs`
  (the sweep in front of the bare `node --test`, S2-B item 2, *[round-1 finding 5]*); `run-host.mjs`; the 31
  runner scripts in `src_vs_code/scripts` (one owned folder each, `TMPDIR`/`TEMP`/`TMP` handed to every child, cleanup in
  a `finally` BEFORE any `process.exit` — the reviewer checks every runner for the `then(exit)…finally` order, #14);
  `http/run-contracts.mjs:127`; `scripts/lib/*.test.mjs` (module-level `mkdtempSync` ⇒ `ownedDir(t, …)` inside a
  `describe`/`before` hook whose `after` pairs it — never at module load, rule §2; `native-run.mjs:18`);
  `.github/workflows/release.yml:307`, `:1154` ⇒ `mktemp -d -p "$RUNNER_TEMP"` (job-scoped, no finding under rule §7);
  new `src_vs_code/src/test/owned.test.mjs`, `runHost.test.mjs`, `runTests.test.mjs` (E2's `runTemp.test.mjs` rewritten);
  `research/module_tests.md`.
  *[2026-11-03, S2-B spec B.10 item 9]* `scripts/lib/leftovers-preload.mjs` above is read as **`scripts/run-lib-tests.mjs`**
  (S2-B builds it: the sweep, then `node --test "scripts/**/*.test.mjs"`); S4 adds to that runner the owned run folder of
  the wrap and the marker its direct-start refusal reads, and `src_vs_code/scripts/owned.mjs` already exists from S2-B
  (the one `ownedFor` call, `REPO_TOKEN`, `loadStartOfRunSweep`) — S4 extends it, never a second module.

**The change — `run-host.mjs`.** `workspace`, `dataHome` and a NEW `userData` come from the wrap (`what` = `host-ws`,
`host-data`, `host-profile`); `launchArgs` gains `--user-data-dir=<userData>` (the comment at `:87-88` becomes true);
`main()` is awaited in a `try/finally` whose `finally` removes the three folders and their claims, and only then the
process exits with the code. The second launch keeps its `named-but-missing` child of `dataHome`. No scenario may
depend on a previous run's profile.

**Scenarios** (RED first): the wrap's own (name of the owner form, claim first, invalid `what` refuses, throw inside the
scope removes, a held file on Windows leaves it prefixed and claimed with one line, a killed `node -e` child's folder
collected by S2-B's sweep on `gone`) — most come from the reference's tests and are NOT duplicated here (decision 11):
coai asserts only that its wrap passes the token and row of the `## Tokens` table and that the run folder's name and
claim are what the C# reader reads (#15); `runHost.test.mjs` (#14); `runTests.test.mjs` (the runner's folder, the
variables, the sweep's report line, and `removeRunTemp`'s never-throw replaced by the reference's `removeQuietly`);
every runner script started with a stub that exits non-zero leaves no folder; `release.yml` under `actionlint`.

- **Commands.** `cd src_vs_code && npm ci && npm test`; `node --test "scripts/**/*.test.mjs"`; `npm run test:host` once
  on Windows and once under `xvfb-run` in WSL, listing `coai-host-*` and `testsToDelete-*` in the temp root before
  and after (counts in the PR); `npm run test:contract`, `test:seam` once each.
- **DoD.** Every scenario RED then GREEN; the mount missing ⇒ exit 2 with the sentence; no Node file outside
  `src_vs_code/scripts/owned.mjs` calls `ownedFor`; `npm run test:host` leaves nothing and passes `--user-data-dir`;
  `module_tests.md` names the wrap.

**S5's deferred Node sites** *[epoch 3, 2026-11-03]*. S4 takes the Node and shell sites that S5 may not rename
because an invocation runs them outside a run folder (D10; the inventory in § *Facts*). Each one either moves into an
owned folder of the wrap (and then drops the bare prefix), or takes the owner form through the wrap (`ownedDir`, a
name `testsToDelete-<owner>-coai-<what>-<id>`); the PR says which, per site:

- **`scripts/lib`, 15 lines** run by `ci.yml:197` (bare `node --test`, the machine temp): the 11 top-level
  `mkdtempSync(join(realpathSync.native(tmpdir()), 'testsToDelete-<what>-'))` folders — `host-facts.test.mjs:17`,
  `kimi-d19.test.mjs:67`, `:84`, `kimi-home.test.mjs:18`, `kimi-schedule.test.mjs:12`, `kimi-shim.test.mjs:24`,
  `kimi.test.mjs:14`, `:225`, `proc.test.mjs:15`, `vibe-manifest.test.mjs:12`, `vibe.test.mjs:18` — and the four lines
  of `caller-transcripts.test.mjs` (`:18` the folder; `:2` the prose; `:284`, `:295` the `doesNotMatch(/…testsToDelete/u)`
  assertions about that folder's NAME, which change with `:18`, both sides together).
- **`scripts/lib/native-run.mjs:18`** (`SERIES_SHAPE`, the series folder `/tmp/testsToDelete-native-(vibe|kimi)-…` on a
  Linux box) and `:31` (its comment), with **`scripts/lib/native-run.test.mjs:9`, `:31`, `:32`, `:33`, `:68`, `:69`**
  (strings the validator under test requires; they follow `SERIES_SHAPE`).
- **the 12 `legacy-name` lines in `scripts/` outside `scripts/lib`**: `scripts/native/abort.sh:31`, `make-series.sh:30`,
  `remove-series.sh:22` (the same series shape as `SERIES_SHAPE`), `scripts/native/native.test.mjs:26`, `:112`, `:420`
  (run by `ci.yml:197`), `scripts/dotenv-name-vectors.mjs:133`, `scripts/probe-kimi-confinement.mjs:132`,
  `scripts/probe-kimi-tree-hold.mjs:99`, `scripts/probe-native-linux-host.mjs:93`, `scripts/probe-vibe-confinement.mjs:92`,
  `scripts/vibe-home-manifest.mjs:71` (scripts a person runs; their scratch folder is in the machine temp).
- **the two extension sites reached by `sonarcloud.yml:302`**: `src_vs_code/src/test/kimiLinuxInstall.test.ts:60` (and
  its prose `:18`) and `runtimeSinceIsAhead.test.ts:147`. `sonarcloud.yml:302` runs `out/test/*.test.js` directly, with
  no run folder, on `ubuntu-latest`. S4 routes that coverage run through `scripts/run-tests.mjs` (or gives the job a
  run folder made by the wrap), and then the two names drop the prefix. That routing is also what makes the baseline's
  `src_vs_code/src/test/` scope true for every invocation (D6).
- the runner's fallback to the machine temp (`run-tests.mjs:221-227`) goes, as above.
- **`beginRun` fails closed, AND a direct `node --test` refuses** *[after the re-review gate, 2026-11-03, findings 0
  and 2]*. `run-tests.mjs` `beginRun` (`:221-230` on `a3b73534`) catches the error and lets the tests "share the
  machine's temp as before"; S4 makes it print one line and exit non-zero before the first batch. And a test file that
  needs the run folder, started directly (`node --test out/test/x.test.js`, `node --test src/test/x.test.mjs`, or
  `node --test scripts/lib/x.test.mjs` without S2-B's preload), refuses: it checks a marker the runner (or the preload)
  sets — S4's brief names the variable — and, when it is absent, fails with one line naming `npm test` (or the
  preload command), before it creates anything. Tests: `runTests.test.mjs` (a root that cannot take the folder ⇒ one
  line, a non-zero exit, no batch ran) and a direct `node --test` of one renamed extension test and one `scripts/lib`
  test ⇒ that line, a non-zero exit, nothing created. RED on `main`. `sonarcloud.yml:302` goes through the runner first,
  or this refusal would fail that job.
- *[epoch 4, 2026-11-03]* **Every direct invocation enters containment or refuses.** Each row of D10's inventory that
  starts a Node test or script outside the runner — `sonarcloud.yml:302` (the coverage run), `ci.yml:197`, the runner
  scripts, a person's direct `node --test` — either runs inside an owned folder of the wrap, or refuses with one line
  before it creates anything. The S4 commit lists each row with its outcome.
- *[epoch 5, 2026-11-04]* **The native measurement series — the ONE bounded exception (the parent's entry 4.2 and its
  § *Epoch 5*, item 4; question consultants `b5c30429`, re-review consult `d7ad1ace`).** The two bullets above that
  put `scripts/lib/native-run.mjs:18` (`SERIES_SHAPE`) and `scripts/native/abort.sh:31`, `make-series.sh:30`,
  `remove-series.sh:22` under "an owned folder of the wrap, or the owner form through the wrap" are superseded for
  the SERIES: neither option fits. The series is a root-run, multi-phase folder: `make-series.sh` creates it, each
  phase (a root-run script) must find it again, `abort.sh` stops a phase, and `remove-series.sh` removes it when the
  person is done — it outlives every script that writes it, on purpose. An owned folder of the wrap dies with its
  script (rule §2); an owner-form claim names a pid that is gone when the script exits, and the next run's sweep would
  remove the series after ten minutes (rule §4), destroying a measurement in progress.
  **Decision:** the series LEAVES the prefix family. **Grammar:** `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}` under the
  series root (`/tmp`, or `COAI_NATIVE_SERIES_ROOT`): no `testsToDelete-` prefix, no owner segment, no claim.
  `SERIES_SHAPE` stays the ONE literal the four files share, and `native.test.mjs` still fails unless all of them hold
  exactly that text. It is still made ONLY by `make-series.sh` and removed ONLY by `remove-series.sh`, which keeps
  validating the whole shape and refuses anything else (exit 78, one line); `abort.sh` keeps the same shape. **Nothing
  collects it automatically** — not the test sweeps, not `run-lib-tests.mjs`, not the janitor (it matches
  `^teststodelete[-_]`), not a next run. Its durable purpose, its two scripts and its grammar are written in the native
  documents (the scripts' own headers and the native rows of `research/module_tests.md`, `:4514-4521` today) and it
  stays in D10's inventory as a line of its own. The tests follow the shape: `native-run.test.mjs:9`, `:31-33`, `:68-69`; `native.test.mjs:26`
  (the test's private root keeps its `testsToDelete-native-test-` name only if it is made through the wrap — else it
  moves into an owned folder of the wrap like the other `scripts/lib` sites), `:112`, `:420`.
  **Evidence (RED first, on the Linux leg — the series exists only on a Linux box):** (1) *survival between phases* —
  a test makes a series with `make-series.sh` under a private root, runs one phase script to completion, then starts a
  second phase: the folder and its phase-1 contents are still there, unchanged; the private root is swept in between
  (S2-B's sweep, on purpose) and the series survives it. (2) *the remover's grammar* — `remove-series.sh` accepts a
  valid NEW name (`native-kimi-20261104-0a1b2c`) and removes it; it refuses, with one line and exit 78, each malformed
  one: the OLD prefixed name (`testsToDelete-native-vibe-…`), a wrong kind (`native-other-…`), a wrong date width,
  upper-case hex, a path outside the series root, a leaf with a separator — the existing refusal cases of
  `native-run.test.mjs:31-33` and `native.test.mjs:420` moved to the new shape, plus the old-prefix case. (3) *the
  guard* — the name carries no `testsToDelete-` prefix, so rule §7's `legacy-name` class does not see it, and it is
  outside every run folder, so it is never a `raw-temp` baseline entry; if the shipped guard classes `make-series.sh`'s
  `mkdir` under another class, S6 names the line with the inline exemption and the reason `the native measurement
  series — the parent's § Epoch 5 item 4`, and the expected-exemption inventory lists it. (4) *the remover's
  ownership check* — `remove-series.sh` also refuses (exit 78, one line) a folder whose owner is not uid 0 or that is
  group- or world-writable, a test for each; a Fable security read of the four native scripts is part of S4's Opus
  review. It also refuses a series ROOT not owned by uid 0, or world-writable without the sticky bit: under a sticky,
  root-owned root no other user can rename or replace a uid-0 folder, so what was checked is what `rm -rf` removes
  (the parent's re-review gate, finding 0; a test per refusal). *Tests as a non-root user* (the interim document gate,
  finding 3): the expected owner uid is a test seam beside `COAI_NATIVE_SERIES_ROOT` — `COAI_NATIVE_SERIES_OWNER_UID`,
  default 0, read only by the native scripts — so the tests run as the CI user: the accepted case sets it to the test's
  own uid under a private root of mode `1777`; the refusals use a mismatched uid, a root of mode `0777` without the
  sticky bit, and a group-writable series folder. (5) *a phase whose series is gone* — after a reboot
  that cleared `/tmp` or a removal, the next phase stops with one line (the series `<path>` is gone; start a new one
  with `make-series.sh`) and a non-zero exit before it writes anything; a test (finding 1). *Size and a forgotten series:* BEFORE the rename lands, S4's first step measures a real series — the one
  `native.test.mjs` makes under a private root with a full phase, and, where the person has one, a past series of a
  real measurement (`du -sh`) — and writes the figure and its assumptions (phases, repeats) in S4's brief and the
  native documents (the interim document gate, finding 0); it is measured again at the first real use after S4; a forgotten series is found with `ls -d <root>/native-*` and removed with
  `remove-series.sh` — nothing else removes it; a reboot that clears `/tmp` clears it too, and the documents say so.
  **Bound:** one shape in one family of scripts; a second durable resource anywhere is a new plan change, never a
  second use of this note.
- *[epoch 5, 2026-11-04]* **Implementation notes of S4 — classified as NOT changes at the epoch-5 re-review** (fixes
  that satisfy this document as written, or approved constraints read more exactly; the coder and the reviewers read
  them with the bullets above):
  - **P1 — the first import refuses.** Every test file that needs the run folder gets a FIRST import (before any
    import that can create a resource) that checks the runner's marker and refuses a direct `node --test` with one
    line, before anything is created. The epoch-4 bullet above already says every direct invocation enters
    containment or refuses; the order of the import is how it is done, and the reviewer checks that no
    resource-creating dependency is imported above it.
  - **D-b — the four person-run probes keep `--scratch`.** `probe-kimi-confinement.mjs:132`,
    `probe-kimi-tree-hold.mjs:99`, `probe-vibe-confinement.mjs:92` and `vibe-home-manifest.mjs:71` already take
    `--scratch <dir>`. Their DEFAULT scratch moves into an owned folder of the wrap (the first option of the bullet
    above), removed at exit, with one printed line saying so. A person who keeps a run for `--reparse`/`--from` names a
    folder with `--scratch` — a folder the person names, outside the machine temp's automatic lifecycle; the script's
    usage line says so.
  - **D-c — `run-host.mjs`'s three owned folders live inside the runner's run folder.** The paragraph *The change —
    `run-host.mjs`* stands (`host-ws`, `host-data`, `host-profile` from the wrap, `--user-data-dir`, the
    `try/finally`), AND the generic runner rule stands (one owned folder per runner, `TMPDIR`/`TEMP`/`TMP` handed to
    every child): the three are made INSIDE the runner's own run folder, with the three variables moved there. Both
    texts as written; nothing is dropped.
  - **P4 — "`test:host` once on Windows" is a hosted run.** The owner's Windows machine does not test E5 (the parent's
    PR #124). The Windows run of the commands line is a deliberately started, recorded run of `npm run test:host` on a
    hosted `windows-latest` runner (a targeted job or a throwaway branch), with the before/after counts of `coai-host-*`
    and `testsToDelete-*` in the record, taken BEFORE the runner is discarded.
  - **P8 — the Node runners EXPORT `TESTSTODELETE_RUN`.** The detail (`common/test-leftovers-detail.md:120`): the top
    of a run exports its record once, and a helper in a child uses it when it names a live run of the caller. So
    `run-tests.mjs`, `run-lib-tests.mjs` and the runner scripts export their own record, and withhold an INHERITED one
    only around their own claim (S2-B `6695ab36`: the run folder's claim names ITS process, never an inherited record).
    *[the interim document gate, 2026-11-04, finding 2]* The scope, exactly: a child test process of a runner (every
    `node --test` file under `run-tests.mjs` or `run-lib-tests.mjs`) is a LEAF inside that runner's run scope. Its
    `ownedDir` folders are made inside the run folder (its temp variables point there) and their claims name the run's
    record, as the detail asks; it makes no run folder of its own and is not a runner. Only a process that IS a runner —
    a test that starts `run-tests.mjs` or `run-lib-tests.mjs` as a child (`runTests.test.mjs`) — makes a run folder,
    and it withholds the inherited record around that one claim, then exports its own to its children.
  - **P1 in ES modules** *[the interim document gate, finding 4]*. The guard is the FIRST static import of every test
    file (`.mjs` and compiled `.js` alike): ES module evaluation runs the imports in source order, depth first, so the
    first import and its dependencies evaluate before any later import. And no test helper or library a test imports
    may make a folder or file when it is loaded (rule §2); the S4 commit checks the imported helpers for load-time
    side effects and names any it moves into a hook.
  - **P12 — a targeted macOS Node CI step**, so that must-not-copy #12 (`/var` → `/private/var`; the wrap's `isMine`
    both ways) runs on macOS for the Node side too; row 12 already names S4.
  - **P2, P9, P10, P11 — counts and references.** The "31 runner scripts" of the Files bullet are 25 entry scripts plus
    13 library modules that get no folder of their own (only an entry script owns a folder); stale line numbers are
    re-read at the commit; two script sites the lists above do not name are added: `scripts/live-consult-all.mjs:25`
    (`coai-live-<vendor>-`) and `scripts/measure-idle-server.mjs:32` (`coai-idle-arm-`) — both person-run, both move
    into an owned folder of the wrap like the probes.
  - **P13 — for S6 (and F3's manifest scan):** no guard scans `src_vs_code/scripts/*.mjs` today; S6's Node guard must
    include that folder, or its zero is false for the runners. S6's Files bullet is read with this note.

Scenarios: the guard (`leftovers-check.mjs --profile node --token coai`) on the whole repository reports zero
`legacy-name` and `owner-blind` for every listed line; `sonarcloudRunsThroughTheRunner` — a test that reads
`sonarcloud.yml` and fails when a step runs `node --test` on the extension's tests without the runner (or without the
wrap's run folder); for the `scripts/lib` and `scripts/native` sites, the preload sweep of S2-B item 2 runs in front of
`node --test`. DoD addition: every listed site handled and named in the PR with its invocation and its new owner.

## S5 — The bare-prefix literals leave the test trees (stage A prep; before the producer PR)

*[epoch 3, 2026-11-03] The goal, change, scenarios and DoD of this section are SUPERSEDED by § *S5 as revised at the
epoch-3 re-review* below.* They are kept as the record of 2026-11-01. They said these sites "run inside it ... or move
under S4's owned folder" and also asked for zero lines before the producer PR. Both cannot be true: a site that moves
under S4's folder loses its prefix only in S4. The Models line and the three-PR split (Q9) still hold.

- **Models.** Brief by Opus; code by a Sonnet coder per tree (one PR per tree: `src_mcp/tests`, the extension,
  `scripts/lib` + `src_server/tests` + `http`); review by Opus against rule §1, §7 and R6 decision 3.
- **Goal.** No literal, constant, glob or regex of the bare prefix (`testsToDelete`/`teststodelete` not followed by an
  owner segment) remains in any test tree, script or workflow outside the helper's own files — so the guard's
  `owner-blind` and `legacy-name` classes land at zero in S6 and no producer is in this story.
- **The change.** The 157 + 32 non-`coai` lines (`testsToDelete-kimi-` → `kimi-`, `-vh-` → `vh-`, `-junc-`, `-sweep-`,
  `-kimilock-`, `-vibe-`, `-wsl-`, `-statx-`, the fixed `testsToDelete-profile-owned`/`-profile-bin`, `native-run.mjs:18`,
  `GaugeCaptureTests.cs:30`, …) drop the prefix: inside the run folder a name needs none (rule §1), and these sites run
  inside it (`src_mcp/tests`, `src_vs_code/src/test`) or move under S4's owned folder (`scripts/lib`, `http`). The
  `testsToDelete-coai-…` literals of E2's own tests (`RunTempTests.cs`, `RunTempHardeningTests.cs`, `runTemp.test.mjs`)
  and the token-less `TempDir.For("testsToDelete-sweep-kh-")` of `TempDirsAreSwept.cs:212` are rewritten to build their names from the helper's constants or are deleted with the
  code they pin (S3 rewrites them anyway; this story takes the ones that would survive S3 unchanged). The run prefix
  itself (`RunTempFolder.cs:44`, `run-tests.mjs:114`) is NOT touched here — it is the producer's constant and moves in
  S3/S4 (a prefix in the helper's own file is allowed).
- **Scenarios.** Before: `git grep -c -i 'teststodelete' -- <tree>` recorded per tree; after: zero outside
  `RunTempFolder.cs`, `run-tests.mjs:114`, this plan and `research/`. The guard (S6) run locally on the branch reports
  zero `owner-blind`/`legacy-name` on that tree. Every test keeps its meaning and count (before/after in the PR).
- **DoD.** Zero bare-prefix lines outside the two helper files per tree; suites green on the three legs; no sweep and
  no producer in the diff (the reviewer asserts the diff touches no `.owner` writer and no sweep).

### S5 as revised at the epoch-3 re-review (2026-11-03)

*[after the re-review gate, 2026-11-03, findings 0 and 3] No stage-A part.* By D10 no site below is contained today:
both runners fall back to the machine temp, the run folder keeps its legacy name until S3, and a direct start has no
run folder. So S5 is not a set of PRs before the producer PR. Its renames are **commit 4 of the producer PR**, after
S3, S2-B and S4, and the eligible-site list below is that commit's input (the S5 build's classification of 2026-11-03,
kept). The goal, the lists, the two SonarCloud sites and the legacy-fixture exemption form stand; "before the producer
PR" and "one PR per tree" in them are read as "a commit of the producer PR". The local S5 branches are material for
that commit, after the judge's cuts.

Decided as option A of consult `c137f641`, confirmed by the re-review consult `3ebddd39`; the parent's epoch 3, § 1
and § 3. Measured by the S5 build on `a3b73534` (three branches `feat/test-leftovers-e5-s5-{mcp,ext,scripts}`, local
only at the stop).

- **Goal.** The bare prefix (`testsToDelete`/`teststodelete` with no owner segment after it) leaves every ENUMERATED
  ELIGIBLE site before the producer PR. A site is eligible only when every invocation that runs it gives it an owned
  folder (D10). Every other site is DEFERRED, by name, to the S3 or S4 commit that gives it one. No producer and no
  sweep are in S5.
- **The eligible sites** (the enumeration; each PR lists its sites with their class, as the S5 classification did):
  - **`src_mcp/tests`** — the 263 lines in 120 files, less the producer's own constant (`RunTempFolder.cs:44` and its
    doc comments `:43`, `:75`) and the lines of E2's run-temp tests that S3 rewrites anyway (named in the PR as "left
    for S3"). Eligible because `CoaiMcp.Tests` runs in the run folder under every invocation (`ci.yml:58`, `:282`,
    `:317`; `sonarcloud.yml` "Test with coverage" runs the same executable, so its module initializer runs). A site the
    PR finds under ANOTHER root (a WSL distro's `/tmp`, the user profile, `LocalAppData`, a fixed path) is deferred to S3.
  - **The extension** — `src/test/chatTestsLeaveNoTemp.test.mjs:30`, `runTemp.test.mjs:26`, `:29`, `:45`, `:146`,
    `:149`, `startOfRun.test.mjs:144`, `:145`, `:166`, `:204`, `:206` (`.test.mjs` files that only `run-tests.mjs`
    runs), and `kimiWindowsInstallRuns.test.ts:62` (Windows only; `sonarcloud.yml:302` runs on `ubuntu-latest`, where the
    test makes nothing). The `runTemp`/`startOfRun` lines are not renames: they take the run prefix from the runner's
    exported `RUN_PREFIX`, or they write the legacy name as a plain literal with the inline exemption of rule §7 (the
    *Legacy and bare names that are a test's meaning* bullet below).
  - **`scripts/lib`** — `old-coai-leftovers.test.mjs:91`, `:352`, `:356`, `:362`, `:378`, `:510`, `:615`, `:618`, `:619`,
    `:628`, `:767`, `:861`, `:897` (legacy run-folder fixtures INSIDE `privateRoot(t)`, the test's own folder: the name
    is the test's meaning, so it stays a plain literal with the inline `legacy-name` exemption, not a rename); and
    `transcript-sanitise.test.mjs:686` (a vacuous `!stderr.includes('testsToDelete')` clause: the test's folder, `:21`,
    has no prefix; the clause goes).
  - `http/`: no line.
- **The deferred sites** (each names its invocation, the missing containment and its owner; the lists are in S3 and
  S4 above):
  - to **S3**: `src_server/tests/GaugeCaptureTests.cs:30` (the `src_server/tests` executable, `ci.yml:77`, `:285`,
    `sonarcloud.yml`: no run folder until S3); any `src_mcp/tests` site the PR finds outside the run folder.
  - to **S4**: the 15 `scripts/lib` lines and `native-run.mjs:18`, `:31`, `native-run.test.mjs:9`, `:31`, `:32`, `:33`,
    `:68`, `:69` (`ci.yml:197`, a bare `node --test` in the machine temp; or `/tmp` on a Linux box); the 12 `legacy-name`
    lines in `scripts/` outside `scripts/lib` (`ci.yml:197` and scripts a person runs); and the extension's
    `kimiLinuxInstall.test.ts:60` (with its prose `:18`) and `runtimeSinceIsAhead.test.ts:147` (`sonarcloud.yml:302`,
    no run folder). The extension PR's two local commits renamed these two: they go back to `testsToDelete-kimi linux
    install ` and `testsToDelete-runtimeSince-`, and leave the PR's new `research/module_tests.md` sentence, before the
    branch is pushed; the PR lists them as deferred (the Opus judge's cut 2 of the re-review).
- **Helper-own lines, never renamed in S5** (S6 exempts them by name): `scripts/lib/old-coai-leftovers.mjs:35-37` and
  `:139` (the one-off clean-up reads legacy names; that is its job); `src_vs_code/scripts/startOfRun.mjs:19` and `:28`
  (the runner's count of legacy run folders); the 8 `.teststodelete-quarantine-` lines of
  `scripts/lib/old-coai-leftovers.test.mjs` (`:192`, `:246`, `:347`, `:406`, `:460`, `:579`, `:713`, `:721`, the
  conventions reference's quarantine marker); the run prefix `run-tests.mjs:125` and `RunTempFolder.cs:44`, with the
  runner's own lines that name it (`run-tests.mjs:106`, `:116`, `:124`).
- **The change.** As in the brief of 2026-11-03: prove the site is inside the run folder under every invocation; check
  that no cleanup, glob, regex or `StartsWith` still looks for the old name (a rename changes both sides together);
  keep a unique part in the name; drop the prefix and keep the `what`. No `.owner` writer, no sweep, no product code.
- **Legacy and bare names that are a test's meaning** *[corrected 2026-11-03 by the re-review; the Opus judge's cuts
  3–5]*. A test whose meaning is a legacy name (for example the legacy run-folder fixtures of `startOfRun.test.mjs` and
  `old-coai-leftovers.test.mjs`, and those of `src_mcp/tests`) writes the name as a PLAIN LITERAL, with the one
  exemption form of rule §7 on that line or the line above: `testsToDelete-exempt: legacy-name — <reason>` (at least
  ten characters and three words, no placeholder). It NEVER builds the prefix from pieces (`['testsTo',
  'Delete'].join('')`, `string.Concat("tests", "To", "Delete", "-")`) to keep the guard from seeing it: rule §7 defines
  `legacy-name` as "a name built without the owner segment", so a split constant is still that class, and hiding it
  would make S6's zero false. One literal per language may serve many tests (a shared constant is fine; a split one is
  not). Each exempted line is listed in the PR with its reason, and S6 counts it as exempted, never as absent.
  `owner-blind` takes no exemption: a line of that class is rewritten (as `runTemp.test.mjs:149` was, to
  `startsWith(RUN_PREFIX)`). The S5 brief of 2026-11-03 said "assemble from pieces once"; that was wrong, and the three
  local `legacyNames` modules (`src_vs_code/src/test/legacyNames.mjs`, `src_mcp/tests/LegacyNames.cs`,
  `scripts/lib/legacyNames.mjs`) are rewritten that way before anything is pushed.
- **Scenarios.** Before and after per tree: `git grep -c -i teststodelete -- <tree>` per file; for `.mjs`/`.ts`, the
  shipped guard (`leftovers-check.mjs --repo . --profile node --token coai`, after `npm ci --prefix
  .agents/conventions`) with its `owner-blind` and `legacy-name` counts; after: zero on the eligible sites, and every
  remaining line is in the deferred or helper-own list above. Every suite keeps its test count, locally and per CI leg
  (the PR names the `main` run it compares with). The PR carries the invocation inventory (D10) of the tree.
- **DoD.** Zero bare-prefix findings on the ENUMERATED eligible sites, except the listed lines that carry the inline
  `legacy-name` exemption because the name is the test's meaning; no prefix built from pieces; every deferred site named with its invocation,
  its missing containment and its owning S3 or S4 change; no producer and no sweep in the diff; the suites green with
  the same counts; `research/module_tests.md` updated where it quotes a renamed name.

## S6 — The two guards, the audit, the `raw-temp` baseline, the CI legs (commit 4 of the producer PR)

*The exemption contract (2026-11-03, re-review gate, finding 3).* The pinned rule core at `cc24f9e`
(`common/test-leftovers.md`, the § 7 paragraph, lines 96-98) has ONE exemption form, a comment on the line or the line
above, `testsToDelete-exempt: <class> — <reason>`; `owner-blind` and `ryuk-off` take none, so `legacy-name` takes it.
S6's CI assertion is therefore: zero UNEXEMPTED `owner-blind` and `legacy-name` findings, AND an expected-exemption
inventory (file, line, class) that the guard's exemption list must equal exactly, so a new exemption is a visible diff.
A site that the guard would classify `owner-blind` cannot be exempted inline: it lives in a helper file the guard
knows, or it is rewritten.

- **Models.** Brief by Opus; code by a Sonnet coder; review by Opus against rule §7, D6, D9, must-not-copy #1, #2.
- **Goal.** One guard per language keeps every tree of the repository so: red on every class of rule §7 outside the
  helper, red on an unregistered token, red on a scan of nothing; `raw-temp` ratcheted by the baseline the released
  rule authorises (D6, *[round-1 finding 7]*) — membership plus content hash, only shrinking; the end-of-run audit
  bounded to the run; the three CI legs carry the self-tests. **Depends on** the baseline rule's release and the pin
  bump to it (D8 condition 5).
- **Files.** The copied `LeftoversGuard*.cs` with `GuardFiles.cs` edited for coai's layout (C# under `src_mcp/tests/`,
  `src_mcp/tests_leftovers/`, `src_mcp/tests_fakecli/`, `src_server/tests/`, `src_bugs/tests/`, `src_bench/CoaiBench.Tests/`;
  workflows; `**/*.sh`, `**/*.ps1`; the `.mjs` under `.github/scripts/` and `http/`) and `RepositoryRoot.cs`;
  coai's own `LeftoversRepositoryGuardTests.cs` (the wrapper that calls the byte-identical `Scan`, subtracts the
  baseline, asserts the rest empty — *[round-1 finding 0]*); the copied `LeftoversAudit.cs` bounded per D9 and
  `LeftoversAuditTests.cs`; `src_vs_code/src/test/testLeftovers.test.mjs` (the `node` profile wrapper, which passes the
  baseline the same way); `src_mcp/tests/Leftovers/raw-temp-baseline.json` and
  `src_vs_code/src/test/raw-temp-baseline.json` with their only-shrinks tests (*[round-1 finding 7]*); `.github/workflows/ci.yml` (the
  audit self-test steps on `build-test`, `macos`, `windows`; the `DarwinLayoutTests` step first on `macos`; `npm ci
  --prefix .agents/conventions` on the `macos` and `windows` jobs so the scripts classifier has `node` and the mount —
  the C# guard shells to `sweeper-scan.mjs`); `research/module_tests.md`.
- **The baseline** (D6, *[round-1 finding 7]*, in the form the released rule fixes): generated once from the real tree
  (every `raw-temp` finding inside `src_mcp/tests`, `src_server/tests`, `src_bugs/tests`, bench, `src_vs_code/src/test`
  that S3/S4 did not move — trees whose run-folder proof is S3 sc. 2, named in the file), written as `{ file, sha256,
  line, count }` where `line` is a HINT and `count` the occurrences *[round-2 finding 1]*; the wrapper passes the
  findings of a key (file + content hash) only up to the baseline's count for that key — the (count + 1)-th
  occurrence is a finding (**a multiset, exact membership per occurrence**, never a total — the
  `suppressionsOnlyShrink.test.ts` lesson: under a constant total one entry can be swapped for another); a test
  asserts the baseline **only shrinks** against the committed one (no key added, no count raised, none changed — a
  diff of the two multisets), that every entry's hash still matches at least one line of its file (a stale entry is
  red until removed), that a moved line keeps its entry (the hint updated, the hash the key), and *[round-2 finding
  1]* that **a second identical raw-temp line added to a file that already has a baselined one is red** (the
  multiset holds one; the scan finds two). `GetTempPath`/`tmpdir` imports used only to READ the root are migrated,
  not baselined (`TempDir.Root`/`RunTemp.Real`; October plan-gate item 6).
- **Scenarios** (RED first; the template's `LeftoversGuardTests`, `LeftoversGuardScriptsTests`, `LeftoversAuditTests`
  copied give the classes; coai adds): `this repository is clean` on the real tree (RED before S5 with the counts per
  class recorded; GREEN after S5 with the baseline); the twelve planted spellings in C# and in `.mjs` (#2); the
  audit's sibling scenario (#1); `a scan of nothing is red` (a discovery that finds no file in `src_mcp/tests`, in
  `src_vs_code/src/test` or in `.github/workflows` ⇒ exit 2 with the sentence); `the token is a registered row and
  written once` (the real mount: `coai` ⇒ none; the C# constant and the Node wrap's token different ⇒ 1; `Coai` ⇒ 1;
  a mount without the detail's table ⇒ the resolver picks by CONTENT, E4 item 41); the baseline's only-shrinks,
  membership-not-total and stale-entry tests (an entry swapped for another under the same total ⇒ red; a second
  identical line beside a baselined one ⇒ red, *[round-2 finding 1]*);
  the frozen exclusion list pinned (the guard's own fixtures; `deploy/**`, `src_server/install-agy.sh`,
  `src_server/install-cli.sh` — product installers, each with its reason); the 29 `worktree add` lines each carry an
  exemption naming the owned folder they live in; `release.yml` ⇒ 0 after S4. **Break-it on the real tree** (by hand,
  recorded in the PR, reverted): one `Directory.CreateTempSubdirectory("x")` in a `src_server` test ⇒ red naming
  `file:line`; one `mkdtempSync(join(tmpdir(), 'x-'))` in an extension test ⇒ red; one `testsToDelete-x-` literal ⇒
  red `legacy-name`; the submodule de-initialised ⇒ red with the submodule sentence.
- **Commands.** The four executables (`*LeftoversGuard*`, `*LeftoversAudit*` first); `cd src_vs_code && npm test`;
  `node .agents/conventions/tools/leftovers-check.mjs --repo . --profile node --token coai`; `actionlint`.
- **DoD.** Every class red once and green with a valid exemption; `owner-blind`, `legacy-name`, `exemption`, `token`
  at zero and `raw-worktree` at zero unexempted on the real tree; the baseline's entries recorded (membership, hash);
  the audit self-tests green on the three legs; the producer PR's squash merge carries S3, S2-B, S4 and S6 together,
  with D8's `TheProducerAndTheSweep_LandTogether` green *[round-1 finding 4]*; the pin at the release that carries the
  baseline rule.

**S6 as revised at the epoch-3 re-review (2026-11-03).** S6 keeps the REPOSITORY-WIDE zero: after S3 and S4 have
handled every deferred site of S5, `owner-blind` and `legacy-name` are at zero on the whole repository, in both guards.
Only the authorised `raw-temp` baseline remains. Three changes to the text above:

- **Named exemptions for helper-own lines**, each listed in the PR with its reason, and carrying the inline form of
  rule §7 (`testsToDelete-exempt: <class> — <reason>`) only where the guard reports a finding of a class that takes one:
  `scripts/lib/old-coai-leftovers.mjs:35-37` and `:139` (the one-off clean-up's legacy-name and quarantine readers);
  `src_vs_code/scripts/startOfRun.mjs:19` and `:28` (the runner's count of legacy run folders); the 8
  `.teststodelete-quarantine-` fixture lines of `scripts/lib/old-coai-leftovers.test.mjs` (`:192`, `:246`, `:347`,
  `:406`, `:460`, `:579`, `:713`, `:721` — the conventions reference's quarantine marker, which it does not export; the
  S5 build measured that the shipped guard does not flag them today, and they are named so that a later guard
  change cannot make them silent findings). `owner-blind` takes no inline exemption (rule §7): a line of that class
  must be rewritten or be in a helper's own file. Lines move: the PR re-reads each line number on its base.
  If S4 leaves a line in `run-tests.mjs` that names the legacy form (today `:106`, `:116`, `:124`), it is helper-own
  in the same way and is added to this list in the PR, with its reason.
- **Test fixtures whose meaning is a legacy name** (the S5 PRs list them: the legacy run-folder fixtures of
  `src_vs_code/src/test/startOfRun.test.mjs`, `scripts/lib/old-coai-leftovers.test.mjs` and `src_mcp/tests`) carry the
  inline `testsToDelete-exempt: legacy-name — <reason>` and are counted as exempted findings. A prefix built from pieces
  anywhere in the repository is a defect, not a pass; the review checks for it (`'testsTo'`, `"tests", "To"`,
  `.join('')`, `string.Concat` beside `Delete`) as the S5 build's split-piece search did.
- **One baseline file.** The released rule allows one `raw-temp-baseline.json` at the repository root (D6, the note of
  2026-11-03). The two files named in Files and in § *The baseline* above become two (or more) `scopes` of that file,
  one per tree, each with its `proof` and its `guard` (`csharp` or `node`). A scope's proof must hold under every
  invocation of its tree (D10); `src_vs_code/src/test/` qualifies only after S4 routes `sonarcloud.yml:302`.
- **F3.** If this PR is the family's first compliance declaration under the shipped Node guard (`--profile node`),
  F3 of the follow-ups (the profile reads `package.json` scripts) is released and pinned before it merges (D8 (g)).

DoD addition: zero `owner-blind` and `legacy-name` findings on the whole repository outside the helpers' own files and
the named exemptions (helper-own lines and the listed inline `legacy-name` exemptions of test fixtures); no prefix
built from pieces; the PR lists every S5 deferral and where it was handled; the baseline is one file at the root.

*[epoch 4, 2026-11-03]* **S6 waits for F24.** F24 of the conventions' follow-ups changes the baseline proof rule
(`common/test-leftovers-baseline.md:19` at `cc24f9e` asks for "a test per assembly or package, ordered first"): a
process-wide gate that enforces containment before discovery — S3's gated `Main` — satisfies "a run that cannot make
it fails before any test", and the marked assertion test stays without an order. S6 creates the baseline only after a
release carrying F24 is pinned here, by a pin bump alone. Its guard edits are the manifest's (D2: `LeftoversGuard.cs:47-48`,
`:60-66` for coai's two helper folders, and the rest of D2's list). **Seeded violations in every actual test root**:
one planted site per root — `src_mcp/tests`, `src_mcp/tests_leftovers`, `src_mcp/tests_fakecli`, `src_server/tests`,
`src_bugs/tests`, `src_bench/CoaiBench.Tests`, `src_vs_code/src/test`, `scripts/`, `.github/workflows`, and every other
root the two guards scan (`.github/scripts/`, `http/`) — each red,
naming file and line, then reverted; plus a scope whose proof file is missing ⇒ a `baseline` finding, and a stale
entry ⇒ a `baseline` finding. The repository-wide guard test lands here, never in S3 (before S5 and S6 it fails on
the whole tree).

*[re-review plan gates, 2026-11-03: the parent's finding r2; this document's finding e2]* Two more checks, both before
S6 creates the baseline:

- **F24's acceptance check** (r2), against the RELEASED guard (the pin carrying F24) and THIS repository's real C# test
  host: a run under the gate passes the guard's proof check for each scope; a forced folder failure and a forced claim
  failure each exit 78 before discovery, with the S3 brief's `MemberData` sentinel untouched. The output is recorded
  in the PR.
- **The baseline is found from both places** (e2): a companion test that `RepositoryRoot.cs` finds
  `raw-temp-baseline.json` at the repository root both when started from the project folder and when started from the
  binary output folder (`bin/Release/net10.0/`), as CI starts the executables.

## S7 — The drain: the `raw-temp` baseline to zero (stage C, after the producer PR)

- **Models.** Brief by Opus (one brief per suite, listing the files and the helper call for each shape); code by one
  Sonnet coder per suite, one PR each; review by Opus against the October S4 table of shapes.
- **The change** (the October S4/S5 shapes, minus the rename — each site keeps its `what`): `Directory.CreateTempSubdirectory("coai-x-")`
  with or without a hand-written delete ⇒ `using var dir = TempDir.For("coai-x-")` (inside the run folder, no claim);
  `Path.Combine(Path.GetTempPath(), "<fixed or guid name>")` ⇒ `dir.At("…")` inside the test's own `TempDir`;
  `Path.GetTempPath()` passed as a root the product only READS ⇒ `TempDir.Root`; a linked worktree as a top-level leaf
  ⇒ inside the test's own `TempDir` (rule §1); `src_bugs` `Scratch.Delete` callers ⇒ `TempDir`; `tests_fakecli`'s fixed
  file names in `GetTempPath()` ⇒ a path from an environment variable the test sets to a file inside its `TempDir`, the
  fake CLI refusing to start without it; `fs.mkdtempSync(path.join(os.tmpdir(), '<x>-'))` in a compiled extension test ⇒
  the ONE `tmpdir()` read of the test tree lives in a tiny `src/test/tempRoot.ts` that the baseline names, or the site
  moves to the wrap where it runs outside the run folder.
- **Scenarios.** Per suite: the baseline entries of that suite removed, the guard green, the suite green on the three
  legs with the same count; `NoTestLeavesAFolderBehind` (October S4 sc. 1) repeated for a sample of the 43 no-cleanup
  classes inside a private machine temp.
- **DoD.** The two baseline files empty and deleted with their only-shrinks tests, the guard then red on ANY raw temp
  call outside the helpers; `module_tests.md` says so.

## Test plan

| The parent's "proven by" (E5 entry) | Where |
|---|---|
| Concurrent-owner survival: an old `testsToDelete-…` with a live owner, a foreign-host and an unreadable claim survive | S2-B item 5 |
| A fresh `testsToDelete-…` and a `coai-chat-…` of any age survive; an old `testsToDelete-…` with a gone owner goes; an old `coai-panel-…` — **corrected 2026-10-17:** does NOT go (no test sweep takes a `coai-` name; D4's one-off clean-up does, by a person) | S2-A item 3 (the invariant, the RED-first `coai-chat-live`), S2-B item 5 |
| The product dirs' own sweep is separate and unchanged | S2-A item 4 (the characterization test, the diff check, the product sweep's own tests) |
| The three pinning tests go RED on the config change and GREEN with the list — corrected: four TEST-prefix pins move (S2-A item 2); the three PRODUCT-prefix scans stay green throughout | S2-A items 1, 2, 6 |
| The guard's break-it on an extension test with a new un-prefixed `mkdtemp` | S6 (the real-tree break-it) |
| A `run-host` test that the profile directory it passed is gone after the run | S4 (`runHost.test.mjs`, #14) — and the two folders it leaks today |
| (contract) claim first, unwind, open handle, a killed run collected | S3 sc. 1, 3, 7; S4 |
| (parent § Test plan) the janitor after a full suite finds only prefixed names of a live run | S2-B DoD (`janitor.sh --token coai --dry-run`) |
| (wave 1) every must-not-copy row RED-GREEN-RED | the table above; S2-B, S3, S4, S6 |

## Definition of Done

- [ ] S1: the pin at `105d53c` (done, #936); the copied settings and hooks byte-identical; the canary green.
- [ ] S2-A: no test sweep deletes anything; the invariant proven in C# and Node with `neverSwept` empty; the RED-first
      `coai-chat-live` scenario observed red then green; the characterization test landed first and stays green;
      `PanelService.cs` unchanged; the stage-A record written for the parent's R6.
      *[epoch 3, 2026-11-03]* Merged as #940 (`d02ed40e`, 2026-11-02); the box is ticked by the S2-A record's reviewer,
      not by this revision.
- [ ] S5: zero bare-prefix lines outside the two helper files; no producer in it.
      *[epoch 3, 2026-11-03] Superseded by the next line.*
- [ ] S5 (revised): zero bare-prefix findings on the ENUMERATED eligible sites (§ *S5 as revised*), except the listed
      fixtures that keep a plain legacy literal with the inline `legacy-name` exemption; no prefix built from pieces;
      every deferred site named with its invocation, the missing containment and its owning S3 or S4 change; no
      producer and no sweep.
      *[after the re-review gate, 2026-11-03]* S5 is commit 4 of the producer PR, not a PR before it: "no producer and
      no sweep" is read as "no producer or sweep code in the S5 commit itself".
- [ ] *[epoch 3]* D10: every story that drops a prefix (S5, S3, S4) carries its invocation inventory in its PR.
- [ ] S3: the library is a byte-identical copy but the namespace, `OwnedToken.cs`, `GuardFiles.cs`, `RepositoryRoot.cs`;
      the run folder of all four C# suites is the owner form, claimed first, in the contract's grammar; Step 0 on
      Windows measured; the Darwin layout self-test runs first on the macOS leg; the E2 measurement (0 entries) repeated
      per suite.
      *[epoch 4, 2026-11-03] Superseded by the next line* (the copy is adapted, and the legs are the matrix).
- [ ] *[epoch 4]* S3 (revised): the adapted copy's manifest and diff check are green (D2); `RunTempStart.cs` and the gated
      `TestEntryPoint.cs` in each of the four test projects; a refused run exits 78 with one `coai tests: no test ran — `
      line, before discovery; the suite × OS matrix green (S3's revised DoD); the run-folder, fail-closed and refusal
      tests of every suite green on all three OSes; the counts reconciled by name.
- [ ] S2-B: every C# suite and the extension runner sweep at their start on two keys, by identity, through the
      quarantine; legacy names listed (C#) or collected on `gone` only (Node); concurrent sweeps never fail; the sweep
      never fails the run. *[epoch 5, 2026-11-04]* Legacy names are LISTED by both languages and collected by
      neither (the parent's entry 4.1); the S2-A clean-up is their only removal.
- [ ] S4: every Node runner, script test and the editor-host run owns its folder through the ONE wrap module; nothing
      leaks; `release.yml` uses `RUNNER_TEMP`.
      *[epoch 5, 2026-11-04]* Read with ONE exception: the native measurement series (§ S4, *[epoch 5]*) owns no folder
      through the wrap — it leaves the prefix family as `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}`, made only by
      `make-series.sh`, removed only by `remove-series.sh`, never collected. Its evidence, every item green on the Linux
      leg and each result named in the PR record (the interim document gate, finding 1): (1) the survival test between
      phases; (2) the remover's grammar tests (a valid new name accepted, each malformed one refused, the old prefix
      included); (3) the guard's reading of the series lines (or the inline exemption and its inventory line, in S6);
      (4) the remover's ownership refusals (folder uid, group/world-writable folder, root uid, non-sticky world-writable
      root) through the owner-uid seam; (5) a phase on a series that is gone stops with its one line and a non-zero
      exit; (6) the size figure measured before the rename, with its assumptions; (7) the native documents say what the
      series is, who removes it, how a forgotten one is found, that a reboot clearing `/tmp` ends it, and the bound. The S4 implementation notes
      (P1, D-b, D-c, P4, P8, P12, P2/P9/P10/P11) are each handled and named in the S4 commit; P13 in S6.
- [ ] S6: both guards red on every class, on an unregistered token and on a scan of nothing; `owner-blind`,
      `legacy-name`, `exemption`, `token` at zero; the `raw-temp` baseline in the released rule's form (membership +
      hash, only shrinking); the audit bounded to the run (#1); the self-tests on the three legs.
- [ ] *[round-1 findings 1–3, 5]* the sweep root is `RunTemp.Real` (tested); each C# suite has its own initializer and
      its first-ordered run-folder test; a run that cannot make its folder fails before any test (both runners); the
      bare `node --test` of `scripts/lib` sweeps first (tested).
      *[2026-11-03, S2-B spec B.10 item 10]* "its first-ordered run-folder test" is superseded by S3 as revised at the
      epoch-4 re-review: the collections run in parallel and no order exists; the gated `Main` (exit 78 before discovery)
      is the guarantee, and the per-assembly run-folder test stays WITHOUT an order (F24 for the baseline's proof rule).
      Read the line as: each C# suite has its own initializer, its gated entry point and its run-folder test.
- [ ] *[round-1 finding 6, round-2 finding 0]* the one-off clean-up script re-checks every pick, removes only through
      an exclusive rename to a quarantine name while the listed identity holds, refuses otherwise; the swap test is
      RED then GREEN.
- [ ] *[round-2 finding 1]* the `raw-temp` baseline is a multiset by file + content hash; the second-identical-line
      test is RED then GREEN.
- [ ] Every must-not-copy row has its RED-GREEN-RED recorded in a PR.
- [x] *[round-1 finding 7]* the `raw-temp` baseline rule is released in `dew_flow_conventions` and coai's pin moved to
      it by a pin bump merged alone, BEFORE the producer PR. *Done:* `5863c60` (#108), pinned by #939 (`2221559a`).
- [ ] The producer PR merged once, squash, with S3 and S2-B both present (`TheProducerAndTheSweep_LandTogether`,
      *[round-1 finding 4]*), after S2-A and S5, the barrier record for both machines, the plan gate and the code gate
      at `proceed`, and green macOS, Windows and Linux legs.
      *[epoch 3, 2026-11-03]* Read with D8's revised merge condition: after S2-A (#940) and the REVISED S5; S3, S2-B, S4
      and S6 squash-merged together INCLUDING every deferred site of S5 (the lists in S3 and S4); S6's repository-wide
      checks green (`owner-blind` and `legacy-name` at zero outside the helpers' own files and S6's named exemptions,
      only the authorised `raw-temp` baseline left); F3 first if this is the family's first Node compliance
      declaration; every other barrier, release and review condition still met.
      *[after the re-review gate, 2026-11-03]* "after … the REVISED S5" is superseded: the PR's commits are S3 → S2-B →
      S4 → S5 → S6; S2-A alone is on `main` before it.
- [ ] *[after the re-review gate, 2026-11-03]* S3 and S4: both runners fail closed, and a test that needs the run
      folder refuses when it is started without it (C#: every start of the assembly; Node: a direct `node --test`);
      each tested RED then GREEN.
- [ ] *[epoch 3]* S6: the `raw-temp` baseline is ONE file at the repository root, with one scope per tree; every scope's
      proof holds under every invocation of its tree.
- [ ] *[epoch 4]* S6: created only after F24 is released and pinned here (a pin bump alone); seeded violations red in
      every actual test root; a missing proof and a stale entry each a `baseline` finding.
- [ ] *[epoch 4]* S2-B: the sweep fixture registered in each of the four test assemblies; the copied sweep tests landed
      with it.
- [ ] *[re-review plan gates, 2026-11-03]* D8's merge-gate table green (each named step on the PR's head); the S2-B
      order test (the sweep ends before any collection starts) or the sweep moved into the gate; the gate's information
      flags pass and `--list-tests` is refused, both tested; the fallback check behind the gate tested; the diff check
      reads the committed manifest (upstream path, blob SHA, transformations) with no network; F24's acceptance check
      recorded before S6; the `RepositoryRoot` baseline test from both folders; D11's rollback procedure in the PR.
- [ ] S7: the baseline drained to zero and deleted.
- [ ] `research/module_tests.md` describes the run folder in its owner form, the sweep in two languages, the one-off
      clean-up, the residual risk, the two guards and the baseline's drain.
- [ ] When the last story merges, this document's status becomes `E5 shipped, <date>; promotes with its parent` —
      never *implemented* while it sits in `todo/` — and the parent's E5 entry gets the same sentence. Parent and stories
      documents promote together, after E7. The S2 spec promotes with them.

## Open questions for the owner (the re-split could not settle these from the sources)

**Decided at plan-gate round 1 (2026-11-01):**

- **Q1 — `raw-temp` over the ~1,200 in-run sites** — DECIDED *[round-1 finding 7]*, with the question consultant
  (consult `abcaae48`, both answers agreeing): rule §7 allows only the inline exemption form, so a coai-only allowlist
  would waive the released rule; **the shared rule changes FIRST**. A conventions plan (written next, in
  `dew_flow_conventions`) authorises a `raw-temp` baseline ONLY for sites proven to run inside an owned run folder,
  matched by exact membership plus the line's content hash (never a count ceiling), only shrinking, the line number a
  hint and not a key. The producer PR merges only after that rule is released and coai's pin moved to it (a pin bump
  alone, R6 decision 1). Stage A and S5 do not depend on it and may start now. D6, D8, S6, S7, the stages table and
  the DoD carry it. (The first re-split text's "documented deviation from §7" is withdrawn.)

**Blocking** (the producer PR cannot be built as specified without the answer; stage A and S5 can start now):

- **Q2 — The barrier record for the owner's Windows machine.** R6 records the operator's WSL machine open (2026-10-31)
  and the owner's Windows machine "not tested"; coai's suites run on it (E2's measurements were on Windows). D8 makes
  the producer merge wait for its scan. *Recommended:* run `tools/sweeper-scan.mjs --root <checkouts>` from Git for
  Windows' bash there and record the result in R6 before the producer PR; stage A and S5 do not wait for it.

**Non-blocking** (a recommended answer is written into the stories; the owner can overrule at the plan gate):

- **Q3 — Legacy `testsToDelete-coai-run-*` folders: list only (the C# copy) or collect on `gone` (the Node reference)?**
  *Recommended:* keep the copy byte-identical (list only) and let the Node wrap collect under rule §4's legacy clause;
  the one-off clean-up covers the `win32`/`darwin`-tagged claims nobody reads (D4). Reason: a byte-identical copy is
  the whole value of the template; the Node reference already implements the clause with its tests.
  *[rollout entry 4.1, 2026-11-03]* **DECIDED by the S2-B spec (B.1 § 3, B.10 items 1 and 12):** list only, in BOTH
  runners. The copy is an adapted copy now (D2, epoch 4), so "byte-identical" is no longer the reason; the reason is that
  nothing coai adds collects a legacy name, and the Node reference's clause never fires at the pin (D4's correction).
  The one-off clean-up is the only remover of E2's folders.
- **Q4 — `src_server/tests`, `src_bugs/tests` and bench get the run folder (S3) rather than per-test owned folders.**
  DECIDED *[round-1 finding 2]*: yes, and **each of the three assemblies gets its OWN `[ModuleInitializer]`** that
  calls `RunTemp.Start` — a module initializer runs only when its own assembly loads, so the library's cannot serve
  them — with a test per assembly, ordered first, that `TEMP` points at the run folder before the first test. It makes
  D5 true for the Team server's thousand product-named directories per run with no product change. (The first
  re-split text's "referencing the library is enough; the coder measures when" is withdrawn.)
- **Q5 — `scripts/lib/*.test.mjs` run bare under `node --test` in CI with no runner.** DECIDED in part *[round-1
  finding 5]*: the command gets a start-of-run sweep in front of it (a `--import` preload or a tiny runner that calls
  the reference's sweep on the real temp root before the tests; S2-B item 2, tested). Still recommended for the sites
  themselves: per-test `ownedDir(t, …)` through the wrap inside hooks (rule §2).
  *[2026-11-03, S2-B spec]* The runner, `scripts/run-lib-tests.mjs` (the S2-B item 2 note says why not the preload).
- **Q6 — The Node sweeper on Windows checks the profile only (F16 open for Node; the C# side checks the SID too).**
  **Decided 2026-11-01 (the parent's epoch 2, § 4; consult `3310f6ba`): F16 (b).** Until F16's Fix, the reference sweep on
  Windows LISTS and changes nothing (`unverified:` lines); the C# sweep, which checks both keys, collects. coai's producer
  PR merges only after the release carrying that change is pinned, and its Windows CI leg asserts that a planted gone-owner
  folder survives the Node sweep. (Superseded recommendation: accept and record.)
  *[rollout entry 4.1, 2026-11-03]* Met as written by S2-B: a NEW step of the `windows` job runs the extension's Node sweep
  test file (the real wrapper, the pinned reference, an otherwise collectible fixture) — S2-B spec B.1 § 5, N-S9, B.5.
- **Q7 — `temp-sweep.json`: keep `prefix`/`keepMinutes` as documentation or delete them.** *Recommended:* keep the
  file with `neverSwept` and a rewritten `why`; delete `prefix` and `keepMinutes` (nothing reads them after S2-A; an old
  checkout's `SweepRule` must be shown to fail soft — the spec's item 1).
- **Q8 — The audit's bound (D9).** The template's audit was corrected to its own process (fix A); coai's four suites in
  one job make the sibling case the normal one. *Recommended:* copy the corrected audit unchanged and ADD the sibling
  scenario (#1) rather than re-design it.
- **Q9 — One PR per tree for S5, or one PR.** *Recommended:* three PRs (the `src_mcp/tests` one alone touches ~160
  lines across dozens of files); each has no sweep and no producer, so the order between them is free.
  *[epoch 3, 2026-11-03]* Still three PRs, each over its ENUMERATED eligible sites only (§ *S5 as revised*). For the
  third tree (`scripts/lib` + `src_server/tests` + `http`) that is 14 lines (13 legacy fixtures that keep a plain
  literal with the inline `legacy-name` exemption, and one vacuous clause), none of them a rename; the rest of that tree
  is deferred to S3 and S4.
  *[after the re-review gate, 2026-11-03]* Superseded: S5 is ONE commit of the producer PR (commit 4), not three PRs;
  the three trees' classifications are its input.

## Plan-gate round 1 of the re-split (2026-11-01, coai session `55ddb13b`)

Verdict *revise*; eight findings, ALL accepted and applied (each marked *[round-1 finding n]* where it landed):

0. The scanner `LeftoversGuard.cs` stays byte-identical; `Scan` returns findings and the `raw-temp` baseline filter lives
   in coai's OWN `LeftoversRepositoryGuardTests` wrapper — named as an edit point in D2, S3, S6 and the E3 boundary table.
1. A byte-identical `LeftoversSweepFixture` would sweep `Path.GetTempPath()`, which `RunTemp` redirects to the run folder;
   the sweep root becomes `RunTemp.Real` — an edit point in D2 and the boundary table; test `TheSweepRoot_IsTheMachineTempRoot`
   (S2-B item 1).
2. A `[ModuleInitializer]` runs only when its own assembly loads: `src_server/tests`, `src_bugs/tests` and
   `src_bench/CoaiBench.Tests` each get their own, calling `RunTemp.Start`, with a first-ordered test per assembly that
   `TEMP` names the run folder before the first test — S3 (Files, sc. 2); Q4's answer replaced.
3. A run that cannot make its owned folder FAILS the suite before any test; E2 (#897)'s never-throw machine-temp
   fallback is withdrawn in both runners, RED test — S3 sc. 3a, S4 (`beginRun`).
4. S3 and S2-B land in ONE squash-merged producer PR: the invariant (no producer without the sweep, no sweep without the
   producer), the merge condition (`TheProducerAndTheSweep_LandTogether`), a revert reverts both — D8, the stages
   table, S6 DoD, the DoD.
5. The bare `node --test` of `scripts/lib` gets a start-of-run sweep (a preload or a runner calling the reference's sweep
   on the real temp root before the tests), with a test — S2-B item 2, S4 (Files), Q5.
6. D4's one-off clean-up re-checks each pick just before removal (present, unchanged since the listing, not a product
   prefix, owner not alive) and refuses otherwise; the command is documented and scripted that way — D4, S2-A item 5.
7. D6/Q1 decided with the question consultant (consult `abcaae48`): rule §7 allows only the inline exemption form, so a
   coai-only allowlist would waive the released rule; the shared rule changes FIRST — a conventions plan authorises a
   `raw-temp` baseline only for sites proven inside an owned run folder, by exact membership plus content hash (not a
   count ceiling — `suppressionsOnlyShrink.test.ts`), only shrinking, the line a hint; the producer PR waits for that
   release and a pin bump alone; stage A and S5 do not — D6, D8 (condition 5), S6, S7, Q1, the stages table, the DoD,
   the status line, the boundary table, note 14.

## Plan-gate round 2 of the re-split (2026-11-01, coai session `55ddb13b`)

Verdict **PROCEED**; two findings, both accepted and applied (marked *[round-2 finding n]*). The gate also ruled: this
plan is a piece of a split already under way (the rollout's E5), so it is NOT split again.

0. D4's one-off clean-up: a re-check narrows the race but does not close it. The removal is an EXCLUSIVE rename of the
   selected folder to a quarantine name, only while the path still has the listed identity (device + inode / file id),
   then the removal of the quarantined object; otherwise it refuses and reports. Test: the folder is swapped after the
   listing — D4, S2-A item 5, the DoD.
1. D6's baseline is a MULTISET keyed by file + content hash: a scan with more occurrences of a key than the baseline
   holds is red; line numbers are hints only. Test: a second identical raw-temp line added to a file that already has a
   baselined one — D6, S6 (the baseline, the scenarios), the DoD.

## Plan-gate round 1 (2026-10-17) — kept as the record of the October version

The coai plan gate over the October document; the judge (Opus) accepted seven findings and rejected five. What each
became in this re-split is in brackets.

**Accepted:**

1. Every token-less `testsToDelete-<x>-` creation site (~53 then; 157 + 32 lines now) moves before the guard lands;
   the guard flags token-less names. [S5, now as the contract's `owner-blind`/`legacy-name` classes; the names DROP the
   prefix instead of taking the token, because they live inside the run folder.]
2. A characterization test pins `PanelService.PruneOldScratchDirs` on `main` before S2 changes anything, through its
   `cutoff` argument, and stays green. [S2-A item 4, unchanged.]
3. The old `coai-` branch is proven equal to today's by a test with an injected clock. [Moot since 2026-10-17; the
   injected-clock discipline carries over to S2-A item 3.]
4. The old-prefix end date is a verifiable condition. [SUPERSEDED 2026-10-17 by the risk decision; the age-only branch
   does not ship — S2-A.]
5. The C# claim is written with `FileMode.CreateNew`/`FileShare.None`. [The template does exactly that, with the
   identity checked before and after; S3, #16.]
6. The guard also refuses importing `tmpdir` from `os`/`node:os` under any alias outside the helper, and
   `Path.GetTempPath` outside the helper library. [The shipped `node` profile and the copied C# guard do; the sites are
   baselined (D6, under the shared rule change of round-1 finding 7) until S7 — except the read-only-root ones,
   migrated in S6.]
7. Sweep deletions swallow not-found silently, so two concurrent sweeps never fail. [S2-B item 4; the template's
   `ClaimIfOurs` and `DeleteClaimIfStill` do.]

**Rejected:**

1. Claim before the folder exists (no unmarked window). [REVERSED by the contract at `115bbf8`: claim FIRST is rule
   §2 now — the template and S3 do it.]
2. A Node start time off Linux. [Still rejected; the contract makes the start optional off Linux; the Darwin path has
   none.]
3. WSL and Windows confusing one host. [Still rejected; the owner carries `@<os>` and, under WSL, `~<distro>`.]
4. A timeout on the liveness probe. [Still rejected; the probes never spawn.]
5. A `Dispose` that throws on a leak. [Still rejected; the template's `Removal.Quietly` never throws.]

## Risk decision (2026-10-17) — kept

The coordinator's decision from the gate-ordered risk consultation on S2 (coai consultation `a70b0f8c…`), applied in
D4, S2-A (goal, items 1–6), the Test plan and the DoD:

- **The age-only legacy `coai-` test branch does not ship.** Both test sweeps take ONLY prefixed, owner-form names,
  judged by owner; the legacy branch is removed (S2-A). Old-prefix test leftovers are left for an explicit clean-up — a
  documented one-off command a person runs, listing first. The janitor matches only `^teststodelete[-_]` names, so
  nothing is delegated to it.
- **The invariant:** every `coai-*` name is outside the test sweep — structurally, not through `neverSwept` — so a
  product prefix invented tomorrow is covered on the day it is invented. Stated and tested in S2-A item 3; structural in
  S2-B's pattern.
- **The RED-first scenario, in C# and Node:** an unmarked `coai-chat-live` with sentinel bytes, 11 minutes old, with
  `coai-chat-` omitted from the injected exclusions, survives the complete startup sweep byte for byte. Today's
  selector fails it.
- **Known residual risk:** old checkouts and agent worktrees on pre-S2-A code keep their old age-only sweepers.
  Mitigation: update or rebase them, or run their suites with a private temp root.
- **Scope control is not safety:** the `PanelService.PruneOldScratchDirs` characterization test stays, and proves
  only that the product's sweep did not move.

## Notes for the parent (`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md`, E5 entry; and R6)

The October notes 1–12 are kept below with what changed; 13–18 are new.

1. **The "three tests that pin the literal" are two kinds.** TEST-prefix pins: `TempDirsAreSwept.cs:46`, `:83-84`,
   `:90`, `:137`, `sweepTemp.test.mjs:32`; PRODUCT-prefix scans: `TempDirsAreSwept.cs:116`, `sweepTemp.test.mjs:46`,
   `TheTestSweepLeavesAJobAloneTests.cs:27` — they must stay `coai-` and green.
2. **The C# test sweep is the product's sweep** (`TempDirsAreSwept.cs:83-84` calls `PanelService.PruneOldScratchDirs`,
   the same function as the six-hour sweep at `PanelService.cs:1442`). S2-A stops the call; S2-B gives the tests the
   copied sweep.
3. `run-tests.mjs:103-111` is `sweepFirst` plus the call at `:242`; `sweepTemp.mjs:30-37` has a FOURTH filter the
   entry omits: the downloaded-server cache `coai-<digit>…` (`:25-27`; C# `PanelService.cs:1396`).
4. **The token-less `testsToDelete-` sites** are 157 lines in `src_mcp/tests` and 32 elsewhere now; under `105d53c`
   EVERY `testsToDelete-coai-…` literal is a legacy name too. They drop the prefix (S5) because they live inside the
   run folder.
5. **`run-host.mjs` leaks more than the profile** — unchanged since October (`:115-127`); S4 fixes it.
6. **Product code under test names its own directories** (~1 000 per server-suite run). *Changed:* under E2 they land
   inside the `src_mcp` run folder; the Team server suite's do so after S3. No product seam is needed.
7. **Three of the five suites have no start-of-run sweep** — and no run folder: S3 gives them the run folder, S2-B the
   sweep.
8. **Worktrees** (29 `worktree add` lines) live inside the run folder; each carries a `raw-worktree` exemption naming
   it.
9. **For the conventions:** `leftovers-check.mjs`'s `RAW_TEMP` gap for `CreateTempSubdirectory`/`GetTempFileName` is
   moot for coai (the C# guard is the copied one); rule §4's old-prefix clause — unchanged note: coai cannot use it
   because its old prefix IS the product's; E5 retires the age-only sweep as its stage A (D7). **New:** the barrier
   scan (`sweeper-scan.mjs`) does not see an age-based sweeper of a NON-prefix name, so coai's stage A was recorded as
   empty while an age-based sweeper was enabled; this plan records the correction and S2-A's PR is the stage-A record.
10. **"~49 sites with no cleanup"** — 43 C# files and 2 Node files at file level on `1fe25d19`; moot for the two suites
    under E2's run folder; the per-site figure is S7's.
11. **The owner host** — settled by the contract (DNS host name, `~<distro>` under WSL); coai's existing owner markers
    (`WorktreeManager.cs:431-458`, product code; E2's `OwnerClaim.cs:38` with Node's `win32|darwin` tags) are named here
    so nobody copies them as the shape. **New:** E2's claims are legacy under `105d53c` and their `<os>` tag is
    unreadable to the contract's legacy clause — kept and listed, never collected; the one-off clean-up covers them.
12. **Cornerstone:** E5.S2 stays marked (3 of 3; the ledger line unchanged); the re-split keeps S2 as the sweep in two
    halves so the ledger needs no edit.
13. **E5's stages for R6:** stage A = S2-A (the age-only `coai-` sweep retired) plus S5 (literals, no producer); the pin
    = #936 (done); the producer PR = S3 + S2-B + S4 + S6 squash-merged once; stage C = S7 (not a producer). The barrier
    record must name the owner's Windows machine before the producer merges (Q2).
    *[epoch 3, 2026-11-03]* Stage A landed as #940. S5 is narrowed to the eligible sites; the producer PR carries every
    site S5 deferred (S3, S4). R6's record of 2026-10-31 called coai's stage A empty; that was wrong (D7) and is
    corrected by #940.
19. **Containment is per invocation (D10)** *[epoch 3]*: the S5 build found that a file is not "inside the run folder";
    an invocation is. `sonarcloud.yml:302` and `ci.yml:197` run test files with no run folder. The parent adds an
    invocation inventory to every remaining prep or rename story (its epoch 3, § 4).
20. **The template is mcp-shaped (the parent's entry 3.1), and the DoD's legs were not coai's CI (entry 3.2)**
    *[epoch 4]*: D2 is an adapted copy (a manifest, permitted transformations, a diff check); the DoD names the suite ×
    OS matrix; S6 waits for F24 (the baseline proof rule). Upstream parameterisation of the template is a possible later
    follow-up for dew_flow_mcp's owner, if E2, E6 or E7 need the same adaptations.
21. **The native measurement series is a durable resource outside the contract's form (the parent's entry 4.2)**
    *[epoch 5, 2026-11-04]*: it leaves the prefix family as `native-(vibe|kimi)-[0-9]{8}-[0-9a-f]{6}`, made only by
    `make-series.sh`, removed only by `remove-series.sh`, never collected; the parent's § *Epoch 5*, item 4 is the one
    bounded exception, with its evidence on the producer PR. And for entry 4.1: nothing collects a legacy
    `testsToDelete-coai-run-*` folder at the pin; S2-A's one-off clean-up is the only removal (D4 as corrected). For the
    conventions: the goal of the family's rollout needs a sentence for a resource that is durable on purpose; this is
    the first one. The parent's P1 is closed by the owner's statement (its PR #124); merge condition (c) for the WSL
    machine waits for one administrator scan.
14. **A shared rule change is needed before E5's producer PR** *[round-1 finding 7, consult `abcaae48`]*: rule §7's
    `raw-temp` class needs a baseline form — only for sites proven inside an owned run folder, exact membership plus
    content hash, only shrinking, the line number a hint — written as its own plan in `dew_flow_conventions`, released,
    and pinned here by a pin bump alone; coai's ~1,200 in-run sites drain under it in S7. Not a coai-only deviation.
15. **The parent's E5 goal sentence** "the startup sweep collects both the new prefix and, for the migration window,
    the old `coai-` test literals" is superseded twice: by the risk decision (no age-only collection) and by `105d53c`
    (a legacy name is listed and collected only on kernel owner AND `gone`).
16. **The parent's E5 scope sentence** "`TempDir.cs` is already the helper … it gains a fixed one" is superseded: the
    helper is the COPIED library; `TempDir.For` stays as the in-run helper with no prefix at all.
17. **Two sweepers of unequal strictness on Windows** until F16 (Q6) — resolved by F16 (b): the Node one only lists there.
18. **The Darwin path is exercised here on three C# suites** (`ci.yml:281-287`), more than the template's one; any
    Darwin defect found here goes back to E3's owner as a note (the boundary table).
19. **A defect in the TEMPLATE's orphan-claim pass (dew_flow_mcp `LeftoversSweep.cs`, found by the S2-B Opus review, 2026-11-03) — for the template, NOT fixed in coai's copy** (the copy is byte-identical;
    the plan's rule: the template wins, and a defect goes back as a note). `SweepOrphanClaims` reads the quarantine names ONCE (`var quarantined = QuarantinedNames()`), and `SweepOrphanClaim`
    decides on that snapshot (`Native.IdentityOf(folder) is not null` → return; `quarantined.Contains(name)` → return). **The interleaving:** sweep A's folder pass sees `X` and keeps it (its verdict
    was `young` or a transient `Unknown`, or an mtime crossed the ten-minute line between the two sweeps' reads); sweep B (another suite, the Node runner) then judges `X` removable and renames it to the
    sibling quarantine; A's orphan pass now finds `X.owner` with no folder at `X` and no `X` in its (older) snapshot, judges the claim by its gone owner, and DELETES `X.owner`; B's `IntactAt` check then
    fails (the claim at its original path changed), so B restores `X` under its name WITHOUT a claim: `X` stays `unmarked` for ever (must-not-copy row 8's outcome, reached through a race instead of a missing
    check). **The fix** (a few lines in `SweepOrphanClaim`/`RemoveOrphanClaim`): just before `File.Delete`, re-check that the folder is still absent AND take a FRESH `QuarantinedNames` scan — a claim
    whose folder sits in a sibling quarantine now is not an orphan. The Node reference has the same shape (`sweepOrphanClaims`); worth the same check there. Recorded in coai's `research/module_tests.md` as a
    known template gap; coai changes nothing until the template does (a manifest operation then follows the template's blob).
