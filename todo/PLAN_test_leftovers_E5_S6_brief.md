# Build brief — coai E5 story S6: the two guards, the audit, the `raw-temp` baseline, the CI legs (the last commits of the producer PR)

> Status: **plan only, nothing implemented yet, 2026-11-05 — the build brief of E5.S6 by Opus. It holds five decisions for
> the coordinator at the top. The coder starts only after the coordinator records an answer to each, under "The answers".**

Written 2026-11-05 by Opus, read-only, against the worktree `<the producer worktree>`, branch
`feat/test-leftovers-e5-producer`, head `524df50e`. The conventions release is `bd795847` (F3 + F24). This branch still pins
`cc24f9e`; the coordinator merges `main` (the pin bump) into the branch before the coder starts. Every guard and rule fact
below was read at `bd795847` (`git -C <conventions> show bd795847:<path>`) and every guard count was measured with the guard
of `bd795847` on this tree.

The plan is `todo/PLAN_test_leftovers_E5.md` (cited as `PLAN:<line>`). The parent is
`dew_flow_conventions · todo/PLAN_test_leftovers_rollout.md` at `origin/main` (cited as `ROLLOUT:<line>`). The template is
`dew_flow_mcp` at `197cc76af7c3c1d28f50e57533fa99d34682466d`.

You (the coder) build from this brief alone. Where this brief and the plan differ, § 2 says so and says why. A difference that
you find and that this brief does not name is a plan problem for the main session. Stop and report it. Do not write around it.

---

## Decisions the coordinator must take

The rollout's epoch 5 has one change on its ledger (entry 5.1, `ROLLOUT:329`). The next accepted change is a FULL STOP of the
whole plan (`common/plan-change-control.md`). So each item below says whether I think it is a change.

### Decision 1 — The `.mjs` half of must-not-copy row 2 cannot be met by the shipped Node guard. I think this IS a change.

**The text.** `PLAN:751` row 2: "plus coai's own: the same twelve in a `.mjs` under the `node` profile. RED when the folding
reader is switched off". `PLAN:1561` (§ S6 scenarios): "the twelve planted spellings in C# and in `.mjs` (#2)".

**The facts.** The folding reader exists only in the C# guard (`LeftoversGuardLiterals.cs`, and `IsFolded` limits it to `.cs`,
`LeftoversGuard.cs:221-222`). The shipped Node guard at `bd795847` has none. I put 15 spellings through its `scanText` with the
path `scripts/zz.mjs`:

| Spelling in a `.mjs` line | Finding of the shipped Node guard |
|---|---|
| a glob by the bare word and `*`, passed to `fs.globSync` and removed | `owner-blind` |
| a whole legacy literal (`<prefix>-coai-x-abc123`) | `legacy-name` |
| the same glob with `?`; a regex string over the word and `[-_]`; a regex literal; the word plus a separator in two literals; a template hole; the bare word; the bare prefix; the word split over `+`; `.concat`; `['a','b'].join('')`; a `\u` escape; a `\x` escape; a one-character class; half of the word | none |

So 2 of 15. Row 2 is the E4 defect itself (item 26: "3 of 12 planted spellings caught"), and it stands in the shipped guard.
This also means the two split fixtures of § 9.2 were invisible to it.

**The options.**

- **A (smallest).** A coai-own folding check for the Node trees: `src_vs_code/scripts/foldedPrefix.mjs`, about 70 lines, called
  by `testLeftovers.test.mjs` over the files of the shipped guard's own `discover(repo, 'node')`. It joins literals over `+`,
  `.concat`, `.join('')` and template holes, decodes `\u` and `\x` escapes and one-character classes, and then looks for the
  whole word with no owner segment after it, and for a piece that crosses the join (holds `sto` or `tod`). The plan's row is then
  met as written: 12 spellings, each one finding, RED when the reader is switched off. Measured false positives on today's tree
  for that piece rule: zero, after the two fixtures of § 9.2 are plain literals. (A piece rule without `sto`/`tod`, as the C#
  guard has, gives 6 false positives on real product names: `chatModelEdits`, `dealCodeLenses`.) The findings of this check are
  red in `npm test` only; it does not change the shipped guard.
- **B.** A follow-up in `dew_flow_conventions` (the next free F number) adds a folding reader to the shipped guard, then one more
  release and one more pin bump before S6. New prerequisite; other consumers get it too. It costs at least one more release.
- **C.** Narrow the row: the `.mjs` test pins what the shipped guard does today (2 of 15), and file the rest as a follow-up. The
  row's RED cannot be observed, because no reader exists to switch off.

**Why I think it is a change.** A adds detection patterns of coai's own, and D6 (`PLAN:502`) withdrew "one Node guard … with
discovery and patterns of its own". B adds a prerequisite. C narrows a contract. If you read A as "a fix that only satisfies the
plan as written", it is not a change, and § 7.4 stands as written. **§ 7.4 is written for option A. If you choose B or C, strike
§ 7.4 and rows R3 and N8; nothing else in this brief depends on it.**

### Decision 2 — P13 brings 54 `raw-temp` lines that no baseline can carry. I think this is NOT a change.

**The text.** `ROLLOUT:1564`, P13: S6's guard must include `src_vs_code/scripts/*.mjs`, "or S6's zero is false for the
runners … the folder list is a repaired reference". `PLAN:561`, D8 (b): "only the authorised `raw-temp` baseline left".

**The facts.** With `src_vs_code/scripts/` added to the C# guard's `ScriptFolders`, it finds 54 `raw-temp` lines there: 32 in
18 entry scripts, 19 in 13 libraries and 3 in the two runner files (§ 10.5). The shipped Node guard does not read this folder. A baseline scope needs a
proof that is a test file under its root (`scopeProblem`, `isTestFile`), and `git ls-files src_vs_code/scripts` holds no test
file, and these scripts are not one runner with one gate. So they cannot be a scope without a new proof file that would not
prove them.

**The smallest option.** Each of the 54 lines takes the rule's own inline form, `testsToDelete-exempt: raw-temp — <reason>`, and
stands in the expected-exemption inventory. An exempted line is not "left" under D8 (b). **This brief is written so.** The
alternative is a new `src_vs_code/scripts/` scope and a new proof file; that needs a re-review of what the proof means, so it
would be a change.

### Decision 3 — The creation run and the squash merge. I think this is NOT a change.

**The text.** `common/test-leftovers-baseline.md` § The ratchet: CI runs `--baseline-shrinks --base <target>` "on every pull
request, without `--allow-create` … the creation pull request's own run is the one exception"; "Never leave `--allow-create` in a
standing CI command".

**The facts.** No consumer has done this yet: `git grep` finds `baseline-shrinks` in no workflow of the family. The workflow file
is part of the same PR. On this PR the standing command exits 1 ("a creation"), and `--allow-create` in a shallow clone exits 2.
A squash merge keeps the head's `ci.yml`, so a flag present at the head stays on `main`.

**The smallest option.** The standing step has `if: github.event_name == 'pull_request' && github.event.pull_request.number != 945`
(a PR number is never used again, unlike a branch name), and `--allow-create` never enters `ci.yml`. The creation run is ONE
temporary commit on the PR that adds a step with `git fetch --unshallow` and `--allow-create`; its run id and
its output go in the PR and in `research/module_tests.md`; the next commit removes it. S7's first PR removes the `number != 945`
clause. **This brief is written so** (§ 12.4).

### Decision 4 — "The guard's proof check for each scope". I think this is NOT a change.

**The text.** `PLAN:1629`: "against the RELEASED guard … a run under the gate passes the guard's proof check for each scope".

**The facts.** The shipped Node guard judges only the scopes that name `node` (rule § What a guard does with it; `createApplier`).
A `csharp` scope is never judged by it. That is the rule, not a defect.

**The smallest option.** Read "the guard" as "the guard the scope names": the two `node` scopes through the released
`leftovers-check.mjs`, the four `csharp` scopes through coai's wrapper, which calls the released library's `createApplier` with
`guard = "csharp"` (§ 7.2, § 16). A repaired reference. **This brief is written so.**

### Decision 5 — `src_server/smoke-image.sh:73`. I think this is NOT a change.

**The facts.** `work=$(mktemp -d)` in a test script, in the machine temp, with no owner form. Its `EXIT` trap removes it
(`:99`, `:102`), so only a killed run leaves it. The plan's exclusion list (`PLAN:1568-1569`) names `deploy/**`, `install-agy.sh` and
`install-cli.sh`, and not this file. `TheImageSmokeUsesTheServerTests` starts the script with a `docker` stub.

**The smallest option.** One inline `raw-temp` exemption with the reason "image smoke script; its EXIT trap removes this folder",
and one inventory row. **This brief is written so.** The other option is a shell port of the helper, which no plan asks for.

### The answers

(The coordinator writes here before the coder starts. An empty line means the coder waits.)

- Decision 1:
- Decision 2:
- Decision 3:
- Decision 4:
- Decision 5:

---

## 0. Read first (in this order)

1. `AGENTS.md`, `.agents/conventions/ENTRY.md`, `.agents/PROJECT.md`; then, AFTER the pin bump,
   `node .agents/conventions/tools/rules.mjs check --repo .` and
   `node .agents/conventions/tools/rules.mjs explain --repo . --task implement --task test --task docs --file src_mcp/tests_leftovers/LeftoversGuard.cs --file src_vs_code/src/test/testLeftovers.test.mjs --file raw-temp-baseline.json --file .github/workflows/ci.yml`,
   and `read` every selected rule (`common/test-leftovers.md`, `common/test-leftovers-detail.md`, `common/test-leftovers-baseline.md`,
   `common/local-cpu-budget.md`, the coai review-gate rule, `csharp/dotnet-build.md`, and the others it names).
2. The plan: § S6 `PLAN:1519-1643` in full; D2 `PLAN:401-449`; D6 `PLAN:502-534`; D8 `PLAN:542-611`; D9 `PLAN:612-621`;
   D10 `PLAN:622-644`; D11 `PLAN:645-665`; must-not-copy rows 1, 2 `PLAN:739-751`; the DoD `PLAN:1677-1775`.
3. This branch's work: the manifest `src_mcp/tests_leftovers/template-manifest.json` and its check
   `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs`; the fixture `src_mcp/tests_leftovers/LeftoversSweepFixture.cs`; the audit
   `src_mcp/tests_leftovers/LeftoversAudit.cs`; `src_mcp/tests_leftovers/RunTemp.cs`, `RunTempFolder.cs`, `RunTempFixture.cs`,
   `TempDir.cs`; the proof files of § 8.2; `research/module_tests.md` § *E5 S5* (`:6318`).
4. The template at `197cc76`, read with `git -C <dew_flow_mcp checkout> show 197cc76:tests/Mcp.Tests/Leftovers/<name>.cs`, the 12
   files of § 6.1, the template's `LeftoversRepositoryGuardTests.cs` for its shape,, and its CI `git -C <dew_flow_mcp checkout> show 197cc76:.github/workflows/ci.yml`, lines 48-71, 103-133, 186-210.
5. The guard at the pin: `.agents/conventions/tools/leftovers-check.mjs`, `tools/lib/raw-temp-baseline.mjs`.

## 1. Goal

One guard per language keeps every tree of the repository so: red on every class of rule §7 outside the helper, red on an
unregistered token, red on a scan of nothing. `raw-temp` is ratcheted by the ONE baseline file the released rule authorises. The
end-of-run audit is on, bounded to the run. The self-tests run on the three legs. After S6, on the whole repository:
`owner-blind`, `legacy-name`, `exemption`, `token`, `docker-*`, `shared-store`, `ryuk-off` are at zero; `raw-worktree` and every
inline exemption are exactly the inventory; and the only `raw-temp` lines not exempted are the baseline's.

S6 adds no producer, changes no sweep, and drains nothing (that is S7).

## 2. What is current in the plan, and why

Later notes override earlier text. This is how I read each point.

| Point | Text I read as current | Why, and what it overrides |
|---|---|---|
| The baseline's form | `common/test-leftovers-baseline.md` at `bd795847`: `{ class, scopes[{root,proof,guard}], entries[{file,sha256,line}] }`, one entry per occurrence, **no `count`** | The rule says "No `count` field". It overrides `{ file, sha256, line, count }` of `PLAN:1549`. The multiset meaning stays. D6's own note of 2026-11-03 (`PLAN:529`) says "the released form" wins. |
| One file | "One baseline file" (`PLAN:1602`), at the repository root | Overrides `src_mcp/tests/Leftovers/raw-temp-baseline.json` and `src_vs_code/src/test/raw-temp-baseline.json` in `PLAN:1541` and D6. |
| The proof | F24 at the pin: the marker, the TEMP assertion and the five child runs IN the proof file; no order | "S6 waits for F24" (`PLAN:1613`) overrides "ordered first". Epoch 5 (`ROLLOUT:1297`) and the review of F24 (`ROLLOUT:1018-1022`) make five forced runs, not two: `PLAN:1630`'s "a forced folder failure and a forced claim failure" is read as all five. |
| The only-shrinks tests | `--baseline-shrinks` of the shipped guard, in CI, for all scopes; coai adds tests of the wiring, not a second check | The rule ("whatever language the baselined tree is in"). It overrides "their only-shrinks tests" per file (`PLAN:1542`). |
| The wrapper | coai's `LeftoversRepositoryGuardTests.cs` calls `Scan`, then applies the baseline | D2's revision "an ADAPTED copy" (`PLAN:419`) overrides "byte-identical" for `LeftoversGuard.cs`; the wrapper part of D2 stands. How it applies the baseline is § 7.2. |
| Commit place | S6 is the LAST part of the PR: S3 → S2-B → S4 → S5 → S6 | `PLAN:576` overrides "commit 4" in the § S6 heading. |
| The exemption contract | `PLAN:1521-1527`: zero UNEXEMPTED `owner-blind` and `legacy-name`; the inventory equals the guard's list exactly; `owner-blind` takes no inline exemption | Stands. It overrides nothing. |
| Helper-own lines | the list of `PLAN:1586-1596`, re-read by `research/module_tests.md:6385`: the plan's `src_vs_code/scripts/startOfRun.mjs:19`, `:28` code lines are gone (S2-B); `run-tests.mjs:166` replaces `:106`, `:116`, `:124` | "Lines move: the PR re-reads each line number". § 10 holds today's lines. Only lines that a guard flags get a marker. |
| The native series | epoch 5 note (`PLAN:1637`): series' line "if the shipped guard classes" it; the old-prefix fixture is in the inventory | Measured: neither guard flags `make-series.sh` (§ 3). So no series row. The fixtures: § 9.2. |
| The count "29" | "the 29 `worktree add` lines" (`PLAN:1569`) | Measured by content: **23** lines on this tree, 19 real and 4 that only name the words. S3 and S5 moved them. A count is a repaired reference. |
| The exclusion list | `PLAN:1568` "the frozen exclusion list": applies to the **C#** guard as written, as an op on `ExcludedFiles` | The shipped Node guard has no exclusion option and reads every `*.sh`, so the same files also carry inline exemptions for it (§ 10). "The guard's own fixtures" need no entry: the template's tests are spelled in pieces, and I measured 12 copied files scanned, 0 findings. |
| Read-only roots | `PLAN:1557`: "`GetTempPath`/`tmpdir` imports used only to READ the root are migrated, not baselined" | No later note withdraws it, so S6 does it. S7's list (`PLAN:1648-1653`) has the same shape, and finds none of these left. § 9.3 has the exact lines. Node: read per FILE, as the word "imports" says. |
| `src_vs_code/scripts/` | P13 (`ROLLOUT:1564`): the C# guard reads this folder | Overrides § S6's folder list `PLAN:1535-1537`, which omits it. See Decision 2. |
| Product C# | The list `PLAN:1535`: test trees only | Coai has no root `src/`, so the template's `Source` kind matches nothing and stays in the text. Measured for the record: with the product trees mapped to `Source`, 2 more `owner-blind` hits on real words (`src_mcp/core/Collecting/CandidatePath.cs:39`, `src_mcp/runners/Reviewers/EndCauses.cs:46`). Not S6. |
| The command | `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "the multi-model review extension and server"` | `PLAN:1575` lacks `--identity` (any other token fails closed without it) and `--baseline`. A repaired reference. |

## 3. Facts measured (2026-11-05). Verify what you use

**How.** The Node guard: an extract of `bd795847` with the mount's `node_modules` beside it. The C# guard: the 8 template files of
§ 6.2 with the ops of § 6.3, in a scratch console project outside the repository, run on this tree (1125 files scanned). Both
scans are on head `524df50e`.

| What | Node guard, `--profile node`, no baseline | C# guard |
|---|---|---|
| `raw-temp` | 252: `src_vs_code/src/test/` 224, `scripts/` 16, `deploy/**` 9, `src_server/*.sh` 3 | 649: `src_mcp/tests/` 487, `src_server/tests/` 58, `src_bugs/tests/` 26, `src_bench/CoaiBench.Tests/` 8, `src_vs_code/scripts/` 54, `deploy/**` 9, `src_server/*.sh` 3, `.github/workflows/release.yml` 2, `src_mcp/tests_leftovers/` 1, `src_mcp/tests_fakecli/` 1 |
| `legacy-name` | 3 (`scripts/lib/old-coai-leftovers.mjs:35`, `:37`, `:139`) | 0 |
| `owner-blind` | 0 | 7, all "a piece of the prefix word" in a literal (§ 9.1) |
| `raw-worktree` | 0 | 23 (§ 10.2) |
| `shared-store` | 0 | 2 (§ 10.3) |
| `docker-run` | 0 | 1 (§ 10.3) |
| `token`, `exemption`, `docker-name`, `ryuk-off` | 0 | 0 |

The 12 template files of § 6.1, with their folders repaired, scanned in a scratch tree: 12 files read, 0 findings. None of them holds a whole marker; their fixtures build it in pieces.

**How a run finds `yaml`.** The shipped guard needs `yaml@2` only when the tree has a workflow; coai has. It looks in two places
(`YAML_RESOLVERS`, `leftovers-check.mjs:179-182`): first `createRequire(<the guard's own file>)("yaml")`, which walks
`.agents/conventions/tools/node_modules`, then `.agents/conventions/node_modules`; then `createRequire(<repo>/noop.js)("yaml")`,
which walks the repository's root and up. coai has no root `package.json`, so the FIRST place is the one that works: **the mount
needs `npm ci --ignore-scripts --prefix .agents/conventions`**, in CI and locally (`node_modules` is ignored inside the mount and
survives a pin bump, but run `npm ci` again after the bump). Measured with no `node_modules` in a mount: exit 2 and one sentence,
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
Note what the matrix proves: bugs runs on Linux only and bench not on Windows (D8's table, `PLAN:591-596`), and that is accepted.

**What a guard does not see.**
- `scripts/native/make-series.sh`: no finding in either guard. So no series row in the inventory; say so in the PR (`PLAN:1637`, S4 evidence 3).
- The 8 `.teststodelete-quarantine-` lines of `scripts/lib/old-coai-leftovers.test.mjs` (`:200`, `:254`, `:355`, `:414`, `:468`, `:587`, `:721`,
  `:729`), `src_vs_code/scripts/run-tests.mjs:166` and `src_vs_code/scripts/startOfRun.mjs:25`: no finding in either guard. They take no
  marker; list them in the PR by name (`PLAN:1586-1596`).
- The bare constant `const OLD_PREFIX = '<prefix>-';` in a `.mjs`: no finding in the Node guard (§ 9.2 does not depend on this).

**The shipped applier can judge a `csharp` scope.** Measured in a private git repository: `loadBaselineAt(repo)` +
`createApplier(loaded, repo, BASELINE_FILE, new Set(discovered), "csharp")` + `take(file, siteSha256(line))` + `finalFindings()`
passes a key's first occurrence, leaves the second, and reports `proof missing` when the proof is not discovered. § 7.2 uses it.

## 4. Order of the work

Commit early: a restart empties `/tmp`. Add commits, amend nothing that is pushed. Do not push; the coordinator decides when a CI
run may start. Every build with `-m:4` and lowered (`nice -n 19`; `node .agents/conventions/tools/low-priority.mjs`), one run at a time.

| Step | What | Commit |
|---|---|---|
| 0 | Preconditions: the answers above are written; `git -C .agents/conventions rev-parse HEAD` starts `bd79584`; `rules.mjs check` is clean; `npm ci --ignore-scripts --prefix .agents/conventions`. Re-measure § 3 with the pinned guard; a number that moved goes in `module_tests.md` with why. | none |
| 1 | RED: the copy (§ 6), the wrapper, the Node wrapper, their tests. Run `*LeftoversRepositoryGuardTests*`: record the counts per class (the plan's "RED … with the counts per class recorded"). | `test(leftovers): RED — E5 S6: …` |
| 2 | Site work: § 9.1, § 9.2, § 9.3. | `feat(tests): E5 S6 — the owner-blind literals, the two fixtures, the read-only roots` |
| 3 | The inline exemptions of § 10 and the inventory file. | `feat(tests): E5 S6 — the named exemptions and their inventory` |
| 4 | The audit: § 11, then the CI legs of § 12 except the shrink step. | `feat(leftovers): E5 S6 — the end-of-run audit is on, bounded to the run; the three legs` |
| 5 | The proof markers (§ 8.2). The F24 second acceptance check (§ 16), recorded in `research/module_tests.md`, **before** step 6. | `docs: E5 S6 — F24's second acceptance check` |
| 6 | The baseline (§ 8.3), in its own commit, then the shrink step and the creation run (§ 12.4). | `feat(leftovers): E5 S6 — the raw-temp baseline, one file, six scopes` |
| 7 | The planted violations (§ 13), RED-GREEN-RED (§ 14), break-its (§ 15); all reverted; `module_tests.md`; the PR record. | `docs: E5 S6 — the record` |

## 5. Files

New:

| File | What |
|---|---|
| `src_mcp/tests_leftovers/LeftoversGuard.cs`, `LeftoversGuardDocker.cs`, `LeftoversGuardExemptions.cs`, `LeftoversGuardFiles.cs`, `LeftoversGuardLiterals.cs`, `LeftoversGuardScripts.cs`, `LeftoversGuardText.cs`, `LeftoversGuardTokens.cs` | The guard, copied with the ops of § 6.3. No `[Fact]`, so the library (D2). |
| `src_mcp/tests/Leftovers/LeftoversGuardTests.cs`, `LeftoversGuardScriptsTests.cs`, `LeftoversAuditTests.cs`, `LeftoversSelfTest.cs` | Their tests, copied (§ 6.4). |
| `src_mcp/tests/Leftovers/LeftoversRepositoryGuardTests.cs` | **coai's own**, not the template's (§ 7.1). It holds the same class name as the template's one-assertion test, so `--filter-class` works alike. |
| `src_mcp/tests/Leftovers/LeftoversBaseline.cs` | coai's own: applies the baseline through the shipped library (§ 7.2). |
| `src_mcp/tests/Leftovers/LeftoversInventory.cs` | coai's own: compares the markers with the inventory (§ 7.3). |
| `src_mcp/tests/Leftovers/LeftoversBaselineTests.cs`, `LeftoversInventoryTests.cs`, `LeftoversCoaiLayoutTests.cs` | coai's own tests (§ 14). |
| `src_vs_code/src/test/testLeftovers.test.mjs` | The Node wrapper (§ 7.4). First import: `import '../../scripts/inRunFolder.cjs';`. |
| `src_vs_code/scripts/foldedPrefix.mjs` | Decision 1, option A only (§ 7.4). |
| `src_vs_code/src/test/tempRoot.ts` | One export, the one read of the temp root for two `.ts` tests (§ 9.3). The name is S7's (`PLAN:1655`). |
| `shared/leftovers-exemptions.json` | The inventory (§ 10). |
| `shared/candidate-path-vectors.json` | The four paths of § 9.1. |
| `raw-temp-baseline.json` | The baseline, at the repository root, a regular file (§ 8). |

Changed:

| File | Change |
|---|---|
| `src_mcp/tests_leftovers/template-manifest.json` | 12 new entries; 1 changed op (§ 6, § 11). |
| `src_mcp/tests_leftovers/RunTemp.cs` | `RegistryWithoutTheRunFolder()` (§ 11). coai's own file. |
| `src_mcp/tests/Leftovers/TheAdaptedCopyTests.cs` | Add to `CoaiOwn`: `LeftoversRepositoryGuardTests.cs`, `LeftoversBaseline.cs`, `LeftoversInventory.cs`, `LeftoversBaselineTests.cs`, `LeftoversInventoryTests.cs`, `LeftoversCoaiLayoutTests.cs`. |
| the six proof files | One marker line each (§ 8.2). |
| the files of § 9 and § 10 | As listed there. |
| `.github/workflows/ci.yml` | § 12. |
| `.github/workflows/release.yml` | Two comment lines (§ 10.4). |
| `research/module_tests.md` | § *E5 S6*, appended. Never trim an older section. |

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
| `LeftoversGuard.cs:45` | `ExcludedFiles { get; } = [];` | the frozen list of § 6.5 | `PLAN:1568`. Also op the doc comment `:43-44`, which says "none". |
| `:47` | `HelperFile = "tests/Mcp.Tests/Leftovers/Owned.cs"` | `"src_mcp/tests_leftovers/Owned.cs"` | coai's layout (D2) |
| `:48` | `private const string HelperFolder = "tests/Mcp.Tests/Leftovers/";` | `private static readonly string[] HelperFolders = ["src_mcp/tests_leftovers/", "src_mcp/tests/Leftovers/"];` | coai has TWO helper folders (D2) |
| `:63-65` | the three allowances | `"src_mcp/tests_leftovers/LeftoversGuard.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardTests.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardScriptsTests.cs"` | coai's layout. **The list stays at four files.** |
| `:280` | `source.Relative.StartsWith(HelperFolder, StringComparison.Ordinal)` | `HelperFolders.Any(folder => source.Relative.StartsWith(folder, StringComparison.Ordinal))` | follows `:48` |
| `LeftoversGuardFiles.cs:28` | `["bin", "obj", "node_modules", ".git", ".claude", ".http-artifacts", "logs"]` | add `".agents"` after `".claude"` | the mount is at `.agents/conventions` |
| `:31` | `ScriptFolders = [".github/scripts/", "http/"]` | add `"src_vs_code/scripts/"` | P13, Decision 2 |
| `:63-64` | `relative.StartsWith("tests/", …) ? SourceKind.Tests : relative.StartsWith("src/", …) ? SourceKind.Source : null` | `TestRoots.Any(root => relative.StartsWith(root, StringComparison.Ordinal)) ? SourceKind.Tests : relative.StartsWith("src/", StringComparison.Ordinal) ? SourceKind.Source : null` and, above `:28`, `private static readonly string[] TestRoots = ["tests/", "src_mcp/tests/", "src_mcp/tests_leftovers/", "src_mcp/tests_fakecli/", "src_server/tests/", "src_bugs/tests/", "src_bench/CoaiBench.Tests/"];` | The roots of `PLAN:1535`. `tests/` and `src/` stay, so the template's ~120 fixture rows at `tests/X/A.cs` and `src/Y/Y.cs` stay byte-identical. Neither folder exists in coai. |
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

`deploy/**` of the plan is written as exact paths so that a NEW script under `deploy/` is read and judged. The shipped Node guard reads these 11 files anyway, so their `raw-temp` lines also take markers (§ 10.4).

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
let answer = { left: request.hits, findings: [] };
if (!locateBaseline(repo, { gitRoot: true }).absent) {
  const applier = createApplier(loadBaselineAt(repo), repo, BASELINE_FILE, new Set(request.discovered), "csharp");
  const lines = new Map();
  const lineOf = (file, number) => {
    if (!lines.has(file)) lines.set(file, readFileSync(join(repo, file), "utf8").split(/\r?\n/));
    return lines.get(file)[number - 1];
  };
  const left = request.hits.filter((hit) => !applier.take(hit.file, siteSha256(lineOf(hit.file, hit.line))));
  answer = { left, findings: applier.finalFindings() };
}
console.log(JSON.stringify(answer)); // no process.exit: on Windows a pipe is written asynchronously, and an early exit can cut the answer
```

- `module` is the file URL of `<mount>/.agents/conventions/tools/lib/raw-temp-baseline.mjs`, as `GuardScripts.StartInfo` makes its URL.
- `request.discovered` is `GuardFiles.Discover(root).Select(file => file.Relative)`.
- `request.hits` is every `raw-temp` finding of the report, in the report's order (file ordinal, then line). That is "in line order" of the rule.
- `Apply` returns: every non-`raw-temp` finding; each hit in `left` as its original `raw-temp` finding; each item of `findings` as `new Finding(file, line, "baseline", text)`.
- A non-zero exit, no `node`, or an answer that is not this JSON is ONE finding `baseline` naming the cause. Fail closed, never an exception, never a pass. Exit code 1 with an unhandled throw (a usage error of `loadBaselineAt`: a folder at the path, a run not at the git root) lands here.

- The request goes on stdin, never in the arguments: `discovered` has more than 1000 paths, and a Windows command line holds 32 KB. Set `RedirectStandardInput`, write the JSON, close the stream, and drain stdout and stderr at the same time as `GuardScripts.Execute` does, with its two-minute bound.
- No baseline file yet (before step 6): the `absent` branch answers "no baseline", so every hit is left. After step 6 that branch is never taken; `The_baseline_file_exists` in § 14 pins it. `loadBaselineAt` alone would throw `ENOENT`.

**Windows.** `locateBaseline` compares `git rev-parse --show-toplevel` with the given folder by real path. A private fixture repository under `C:\Users\RUNNER~1\…` may spell the two differently. So a fixture never passes its own folder: it asks git and passes the answer (`git -C <folder> rev-parse --show-toplevel`, slashes as git prints them). The real repository's root has no short name on the hosted runner; the `windows` job's `Test` step proves it.

### 7.3 `LeftoversInventory.cs`

The contract of `PLAN:1521-1527`: the markers on disk equal the inventory, exactly. The file is `shared/leftovers-exemptions.json`:

```json
{
  "about": "Every inline test-leftovers exemption of this repository: S6's expected-exemption inventory (todo/PLAN_test_leftovers_E5.md). A new marker, a removed one or a moved line is a visible diff here.",
  "rows": [
    { "file": "deploy/bugs/deploy-cmd.sh", "line": 109, "class": "raw-temp", "guards": ["node"] }
  ]
}
```

`line` is the MARKER's line, `guards` the guards that read the file (`csharp`, `node`, or both). The reason is not repeated: it is in the marker.

- C#: for each file of `GuardFiles.Discover(root)`, `GuardExemptions.Read(file, GuardText.SplitLines(text), out var bad)`, giving (file, line, class) for each valid marker. Compare with the rows whose `guards` holds `csharp`. Report each difference as one line: `in the tree, not in the inventory: …` or `in the inventory, not in the tree: …`.
- Node (§ 7.4): the same with `discover(repo, 'node')` and `readExemptions(file, lines)`, both exported by the guard.
- A row whose file is absent is a difference in both. A row of a file its named guard does not discover is a difference.
- Read a file as its guard does: the Node side skips a path that is gone and a file with a NUL byte in its first 8 KiB (`scanTexts`, `isBinary`).

The markers in `.sh` and `.cs` files are read by the same grammar in both guards: `common/test-leftovers-detail.md` § 7.

Expect this test to be red whenever a line above a marker moves. That is the plan's "visible diff". The message says what to update.

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
| N3 `the Node half of the inventory` | § 7.3. |
| N4 `every root has a file` | `discover(repo, 'node')` holds at least one file under `src_vs_code/src/test/`, `scripts/` and `.github/workflows/`. |
| N5 `the token is a registered row and written once` | On private repositories, through the exported `scanRepository`: the real mount and `coai` ⇒ no `token` finding; a C# `RepoToken = "coai"` beside a Node `REPO_TOKEN = 'other'` ⇒ the finding "token constants differ" naming both (measured: 2 `token` findings in all, the second says `other` is no row; the plan's "⇒ 1" is the first); the token `Coai` ⇒ one. Import the guard in the test's own process; a program that passes the guard's path as `process.argv[1]` starts its `main`. |
| N6 `the baseline only shrinks` | A private repository with a committed baseline, through `--baseline-shrinks --base HEAD`: a new key, a raised count, a new scope, a changed proof, a changed guard ⇒ exit 1, each with its sentence; a removed entry ⇒ exit 0; no `--allow-create` ⇒ a creation is exit 1. |
| N7 `the baseline's second pass` | A private repository with a `node` scope: a second identical line ⇒ `raw-temp`; an entry swapped under the same total ⇒ a `raw-temp` and a `baseline`; a stale entry ⇒ `baseline`; a proof with no marker ⇒ `baseline` naming "proof missing"; a moved line ⇒ clean. |
| N8 `the twelve spellings` (Decision 1, option A) | The rows of the table below, each one finding from `foldedPrefix.mjs`; `foldedHits` over every file of `discover(repo,'node')` is empty. |

All private repositories come from `owned.ownedDir(t, 'guard')`, inside the run folder, with `git init`. Pass `--repo` the answer of `git rev-parse --show-toplevel`, never the folder as you built it: the guard compares the two by real path, and on the hosted Windows runner the temp path holds a short name (`RUNNER~1`). Never spell the marker or the prefix as a whole literal in this file: build them from `owned` or in pieces, or they count as a marker and the guard reads them. `test` names and comments are free.

**`foldedPrefix.mjs` (Decision 1, A).** `export function foldedHits(file, text)` → `[{ line, text }]`. It skips comment lines as the guard's `isComment` does. For each other line: (1) decode `\uXXXX`, `\xXX` in the literals; (2) join `'a' + 'b'`, `'a'.concat('b')`, `['a', 'b'].join('')` and a `${'a'}` hole; (3) replace a one-character class `[X]` by `X`; (4) a line whose folded text holds the word, case-insensitive, with no owner segment after it (`[-_][uw][0-9]{1,10}[-_]`, and not `-exempt`, not `-quarantine`, not `-baseline-proof:`, not the upper-case env form) is a hit; (5) a literal that holds a piece of at least five letters holding `sto` or `tod`, and not the whole word, is a hit. The word itself is spelled in two pieces in this file. It is a Node reader only; it knows nothing of baselines, and no marker clears it (the class is `owner-blind` in spirit).

The rows, each ONE hit: (1) the word and `*` in a glob; (2) the word and `?`; (3) a regex string over the word and `[-_]`; (4) word and separator in two literals; (5) a template hole; (6) the bare word; (7) split over `+`; (8) `.concat`; (9) `.join('')`; (10) a `\u` escape; (11) a `\x` escape; (12) a one-character class. And none on: an owner-form name, the quarantine word, the upper-case env name, `chatModelEdits`, `dealCodeLenses`.

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

A number that differs after the merge of `main` is not wrong by itself. Explain each difference by file in the PR. An entry outside these six roots is impossible: the guard reports it.

`csharp` scopes are valid here: none has a root under `scripts/`, `test/` or `tests/` at the repository root (measured: `src_mcp/tests/` is not `tests/`), so the Node guard does not report them.

### 8.2 The proof marker

One line in each proof file, a comment, in this exact form (the root, then nothing else):

- `src_mcp/tests/RunTempTests.cs`: `// testsToDelete-baseline-proof: src_mcp/tests/`, directly above the class's `/// <summary>`.
- `src_server/tests/RunFolderTests.cs`, `src_bugs/tests/RunFolderTests.cs`, `src_bench/CoaiBench.Tests/RunFolderTests.cs`: the same place, each with its own root (`src_server/tests/`, `src_bugs/tests/`, `src_bench/CoaiBench.Tests/`).
- `src_vs_code/src/test/runFolder.test.mjs`: `// testsToDelete-baseline-proof: src_vs_code/src/test/`, directly above the file's `/**` header, below the imports (the guard import stays first).
- `scripts/runFolder.test.mjs`: `// testsToDelete-baseline-proof: scripts/`, the same place.

A comment is skipped by both guards' line classes. Measured for the Node guard with this exact line: no finding. `declaredRoot` needs the root right after the marker and only whitespace to the end of the line.

Measured with a scratch baseline (the coordinator's note): no marker ⇒ 2 `proof missing`, a marker ⇒ 0, only under `--profile node`. Without `--profile node` the proof files are not discovered at all, so **every Node guard command in this brief has `--profile node`**.

### 8.3 Generated once, never again

The rule: "No command regenerates the file; the one writer is a person, in a reviewed diff that removes lines." So the writer is a throwaway. **Nothing you use here is committed.**

Write these in your scratch directory with the Write tool (a Bash heredoc that quotes a build line is stopped by a hook):

1. `node .agents/conventions/tools/leftovers-check.mjs --repo . --print-baseline --profile node --token coai --identity "the multi-model review extension and server"`: stdout is `{ scopes: [], entries }` for the Node sites. Expect 229. It lists every `raw-temp` finding of the scan, so if a file outside the two Node roots appears, a marker of § 10 is missing. Stop and fix it.
2. A scratch console project with a `ProjectReference` to `src_mcp/tests_leftovers/CoaiMcp.Tests.Leftovers.csproj`: `LeftoversGuard.Scan(root, root)`, and print `file<TAB>line` for each `raw-temp` finding. Expect 547. A file outside the four C# roots: stop and fix it.
3. A scratch Node script: import `siteSha256` from the mount, and for each line of (2) read that line of the file and print `{ file, sha256, line }`. The hash is the shipped function, so it is the same one the wrapper uses.
4. Merge: `about`, `class`, the six scopes of § 8.1, and all entries sorted by `file` (ordinal), then `line`. One entry per line of the JSON, so that S7's diffs remove whole lines.
5. Check: `node …/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "…"` exits 0, and `*LeftoversRepositoryGuardTests` is green.
6. Delete the scratch project and scripts. Write the commands in the PR and in `module_tests.md`, so that a reader sees how it was made.

**After this commit no line is added.** A later edit of a baselined line removes its entry and moves the site to the helper; it never re-keys. Everything in § 9 and § 10 comes first because of this.

## 9. The site work, before the baseline

### 9.1 The seven `owner-blind` lines: rewritten, because the class takes no exemption

The C# guard reads a literal that holds a piece of the prefix word as `owner-blind` (`PieceHits`, the folded reader). These seven are real words. Rule §7 and `PLAN:1526`: such a line is rewritten or lives in a helper's own file. The list of files that may spell pieces (`FoldingAllowances`) stays at four.

| Site | The piece | The rewrite |
|---|---|---|
| `src_mcp/tests/Leftovers/StartOfRunSweepRaceAndRootTests.cs:330` | `TestSt` in `"*SweepChild_…_WhenTheProcessTestStartsIt"` | `"*" + nameof(SweepChild_SweepsTheGivenRootOnce_WhenTheProcessTestStartsIt)`. No rename, so `research/module_tests.md:6309` stays true. coai's own file: no manifest op. |
| `src_mcp/tests/OwnerClaimVectorsTests.cs:41`, `:58` | `odeLe` in the JSON key `"nodeLegacy"` | Rename the key to `nodeOldForm` in `shared/owner-record-cross-vectors.json` (the comment `:7` and every row) and in this file (`:27`, `:41`, `:58`). `git grep nodeLegacy` shows no other reader. |
| `src_mcp/tests/CandidatePathTests.cs:20`, `:21`, `:52`, `:53` | `toDelete`, `todelete` in test paths | These are test data for the product's word `"todelete"` (`src_mcp/core/Collecting/CandidatePath.cs:39`): the meaning needs the word. Move the four paths to `shared/candidate-path-vectors.json` (`{ "transient": [ … ], "merelyStartsTheSameWay": [ … ] }`, the same strings), read through `RepoPaths.Root()` as `OwnerClaimVectorsTests` does, and add `[MemberData]` beside the `[InlineData]` rows that stay. The same test values, so the same count. Reconcile by name in the PR. |

### 9.2 The two split fixtures: plain literals with the inline exemption

`PLAN:1599`: a prefix built from pieces is a defect. In this brief `<prefix>` stands for the word `testsToDelete`; the file holds the whole word.

- `scripts/lib/native-run.test.mjs:42-43`. Delete the `OLD_PREFIX` line. The refusal line becomes one literal, with the marker above it:
  ```js
  // <prefix>-exempt: legacy-name — the old prefixed series name is the fixture this refusal test is about
  refusal(() => rendered({ series: '/tmp/<prefix>-native-vibe-20261021-a1b2c3' }), 'the old prefixed name');
  ```
- `scripts/native/native.test.mjs:506`:
  ```js
  // <prefix>-exempt: legacy-name — the old prefixed series name is the fixture the remover must refuse
  const OLD_PREFIXED = '<prefix>-native-vibe-20261021-a1b2c3';
  ```

Measured with the Node guard: the literal alone is `legacy-name`, and with the marker it is clean. Then run the review's search (`PLAN:1599`) on the whole repository and put its output in the PR: `'testsTo'`, `"tests", "To"`, `.join('')` and `string.Concat` beside `Delete`. What may remain: the detector words in `LeftoversGuard*.cs` and the copied tests, and the quarantine word `"teststo" + "delete-quarantine-"` of the library (`research/module_tests.md:6385`). Those are not a prefix of a name.

### 9.3 The read-only roots: migrated, not baselined (`PLAN:1557`)

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

New `src_vs_code/src/test/tempRoot.ts`: `import { tmpdir } from 'node:os'; export const tempRoot = (): string => tmpdir();`, with one comment line saying why. It is not a `*.test.*` file, so the Node guard does not read it and it is no baseline entry. S7's text "that the baseline names" (`PLAN:1655`) cannot hold for such a file; note it in the PR for S7.

The other Node files with a `tmpdir()` read also hold a `mkdtemp`, so their import is not "used only to read": they stay in the baseline. So do `owned.test.mjs`, `runFolder.test.mjs` (two), `runTests.test.mjs`, which prove the variable, and `wslNetwork.test.ts:187`, which writes.

## 10. The expected-exemption inventory

**112 rows.** The tables give the SITE's line today and the marker's text. The marker goes on the line above, with one exemption form:
`// <prefix>-exempt: <class> — <reason>` (or `#` in a `.sh` or `.yml`). A marker shifts the lines under it, so `shared/leftovers-exemptions.json` holds the MARKER's final line. Write the JSON last in step 3, from the tree, and check it against this table by count per class:

| Class | Rows | |
|---|---|---|
| `legacy-name` | 9 | 4 exist, 5 new |
| `raw-worktree` | 23 | all new |
| `shared-store` | 2 | new |
| `docker-run` | 1 | new |
| `raw-temp` | 77 | 1 exists (`Owned.cs:119`, the template's), 76 new: 22 in § 10.4, 54 in § 10.5 |

Rule of the reason: at least ten characters and three words, no placeholder start (*needed*, *see above*, *because*, *todo*), and `lock:` for `shared-store`. The wording below passes. A `raw-worktree` reason names the folder (`PLAN:1569`); before you write it, read the line that makes that folder. If it is not what the table says, fix the reason and tell the reviewer.

The guards column is `guards` in the JSON: which guard reads the file. `.cs`, `src_vs_code/scripts/*`: `csharp`. Root `scripts/**`, `*.test.*`: `node`. A `*.sh` not on the frozen list, a workflow: both. The frozen list's 11 files: `node` only, because the C# guard never reads them.

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

### 10.4 `raw-temp` outside the six scopes

`.github/workflows/release.yml`: the C# guard's `mktemp` pattern fires though the path is under `RUNNER_TEMP`, which the rule does not call raw temp; the `#` comment goes above the line inside the `run:` block, and the F3 test (`workflowsStartTestsThroughARunner.test.mjs:61`) still passes. This is the plan's "`release.yml` ⇒ 0". `scripts/probe-*`: S4's P11 said "S6 must decide them"; they are not under any runner, so no baseline. The folder comes from the command line (`scripts/probe-agy-allow-rule.mjs:19`).

| File | Line of the site today | Class | Guards | Reason (the marker's text) |
|---|---|---|---|---|
| `deploy/bugs/deploy-cmd.sh` | 109 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/bugs/deploy-cmd.sh` | 165 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/bugs/install-env.sh` | 101 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/bugs/release.sh` | 59 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/bugs/release.sh` | 171 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/coai-deploy-cmd.sh` | 166 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/systemd-release.sh` | 137 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/systemd-release.sh` | 161 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `deploy/systemd-release.sh` | 302 | `raw-temp` | node | a product deploy script on a server, not a test run |
| `src_server/install-agy.sh` | 61 | `raw-temp` | node | a product installer in the image build, not a test run |
| `src_server/install-cli.sh` | 22 | `raw-temp` | node | a product installer in the image build, not a test run |
| `src_server/smoke-image.sh` | 73 | `raw-temp` | csharp, node | image smoke script; its EXIT trap removes this folder |
| `.github/workflows/release.yml` | 317 | `raw-temp` | csharp, node | a job-scoped folder under RUNNER_TEMP; the runner discards it with the job |
| `.github/workflows/release.yml` | 1319 | `raw-temp` | csharp, node | a job-scoped folder under RUNNER_TEMP; the runner discards it with the job |
| `src_mcp/tests_fakecli/Serving.cs` | 140 | `raw-temp` | csharp | reads a steering file only; the fake CLI is a test's child, so temp is the run folder |
| `src_mcp/tests_leftovers/TempDir.cs` | 35 | `raw-temp` | csharp | the helper makes a test's folder; TEMP names the run folder, so it lands inside |
| `src_mcp/tests_leftovers/Owned.cs` | 120 (marker :119, exists, the template's) | `raw-temp` | csharp | the helper is the one place a test learns the temp root |
| `scripts/probe-agy-allow-rule.mjs` | 26 | `raw-temp` | node | under the scratch root the person names on the command line, never the machine temp |
| `scripts/probe-agy-consult-follow-up.mjs` | 31 | `raw-temp` | node | under the scratch root the person names on the command line, never the machine temp |
| `scripts/probe-agy-write-block.mjs` | 72 | `raw-temp` | node | under the scratch root the person names on the command line, never the machine temp |
| `scripts/probe-agy-write-block.mjs` | 84 | `raw-temp` | node | under the scratch root the person names on the command line, never the machine temp |
| `scripts/probe-claude-consultant-confinement.mjs` | 29 | `raw-temp` | node | under the scratch root the person names on the command line, never the machine temp |
| `scripts/lib/kimi.mjs` | 176 | `raw-temp` | node | reads the temp root to refuse a scratch under it; nothing is made |

### 10.5 `raw-temp` in `src_vs_code/scripts/` (Decision 2), 54 rows

One row per file here; each line is one marker and one inventory row.

| File | Lines of the sites today | Class | Guards | Reason (the marker's text) |
|---|---|---|---|---|
| `src_vs_code/scripts/browserLayout.mjs` | 51, 79 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/live-agent.mjs` | 78, 79, 80 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-api-settings-compat.mjs` | 93 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-chat.mjs` | 61 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-close-consult-compat.mjs` | 186 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-command-models-compat.mjs` | 20 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-feature-schema-compat.mjs` | 122 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-feature-vendor-compat.mjs` | 113, 164 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-fresh.mjs` | 174 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-gauge-compat.mjs` | 81, 93 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-paths-per-side-compat.mjs` | 114 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-security-settings-compat.mjs` | 21 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/live-vibe-runtime-compat.mjs` | 61 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/measure-append.mjs` | 160 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/measure-consultant-skew.mjs` | 75, 121, 181, 182 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/measure-image.mjs` | 284 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/measure-stream.mjs` | 485 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/ownedRun.mjs` | 34, 36 | `raw-temp` | csharp | the runner reads the real temp root before it makes its run folder |
| `src_vs_code/scripts/run-contract.mjs` | 148 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/run-seam.mjs` | 143, 285, 311, 370, 409, 439, 487, 569 | `raw-temp` | csharp | runs inside this script's own run folder, startOwnedRun moved TEMP there |
| `src_vs_code/scripts/run-tests.mjs` | 103 | `raw-temp` | csharp | the runner reads the real temp root before it makes its run folder |
| `src_vs_code/scripts/seam-catalog.mjs` | 69 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-cli-path-other-side.mjs` | 36, 83 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-consultant-row.mjs` | 48 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-gauge.mjs` | 77 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-ledger-fields.mjs` | 73, 91, 222 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-missing-named-root.mjs` | 100 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-path-missing.mjs` | 31, 32 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-qconsult-roots.mjs` | 44, 45 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-refusal.mjs` | 39 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-security-text.mjs` | 78 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-security.mjs` | 50 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |
| `src_vs_code/scripts/seam-usage-counts.mjs` | 165 | `raw-temp` | csharp | a library; the entry or the test that imports it runs inside a run folder |

Not in the inventory, because no guard flags them (so they take no marker; the PR names them, `PLAN:1586-1596`): the 8 quarantine lines of `old-coai-leftovers.test.mjs`, `run-tests.mjs:166`, `startOfRun.mjs:25`, `make-series.sh`, the C# library's quarantine word.

## 11. The audit (D9)

**The trap.** The template wires `() => RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())`. Copied as it stands, this is red on every run in coai: the template has no run folder, and coai's IS an `OwnedDir` (`RunTempFolder.Create`, `OwnedDir.Create("run", realRoot, …)`), so `Unreclaimed()` holds it while it is alive. The audit would dispose it and report "never disposed". And if it read the machine temp, `Discover` would find the run folder by its claim (`WithholdInheritedRun` makes the claim name this process) and remove it. The disposal order of the two assembly fixtures must not matter either.

**The wiring, bounded to the run.** In `template-manifest.json`, the fourth op of `LeftoversSweepFixture.cs` keeps its `old`; its `new` becomes:

```csharp
() => RealAudit(RunTemp.Folder, RunTemp.RegistryWithoutTheRunFolder())
```

with the reason "D9: the audit is bounded to the run: it scans inside this run's folder, and the run folder is ended by RunTempFixture, not by the audit". In `RunTemp.cs` (coai's own):

```csharp
/// <summary>What the end-of-run audit reads: every folder the helper still holds, without this run's own folder, which <see cref="EndOfRun"/> ends (D9).</summary>
internal static IReadOnlyList<OwnedDir> RegistryWithoutTheRunFolder() =>
    [.. OwnedDir.Unreclaimed().Where(dir => !ReferenceEquals(dir, _run.Dir))];
```

Why it holds in each order:

| Case | What the audit sees |
|---|---|
| `LeftoversSweepFixture` disposes first | `RunTemp.Folder` exists. `Discover` scans depth 1 inside it: a test's own owned folder and claim. Never the run folder, which sits in the parent. |
| `RunTempFixture` disposes first | The folder is gone. `Discover` returns nothing (`!Directory.Exists`). The registry still holds a leaked `OwnedDir`, so a leak is still red. |
| A child under an inherited `TESTSTODELETE_RUN` | `Owner.RunRecord(env, …) != Owner.This`: `Discover` returns nothing. The registry covers this process's own. Row 1's fix, from the template. |
| The run folder cannot be removed at the end | It stays with its claim and one warning (S3's contract), and it is not a red run. The next sweep judges it. |

`LeftoversAudit.cs` and `LeftoversAuditException.cs` are unchanged (their manifest entries stand).

**Expect it to find leaks.** This turns on a check that was off through S3, S2-B, S4 and S5, on four suites. A red `coai tests: this run left <n> folder(s) it claimed` names a real leak: an `OwnedDir` nobody disposed. Fix the test that made it. Never weaken the audit, and never add an entry to skip. Record each one found in `module_tests.md`.

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
| after `setup-node` `:277-279` | NEW `Shared instruction dependencies`: `npm ci --ignore-scripts --prefix .agents/conventions` | `PLAN:1544`; `yaml` for the Node guard |
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

### 12.4 The shrink step and the one creation (Decision 3)

The standing step, in `build-test`, in the repository's own shape for a ratchet (the base's SHA, because `origin/main` does not exist in a shallow checkout; `:152-166`):

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

The one creation: one commit adds, under that step, this TEMPORARY step; the coordinator pushes it; the run id, the step's log line (`raw-temp-baseline.json: the one creation, allowed — it never had history behind …`) and the head SHA go in the PR and in `module_tests.md`; the next commit removes it. At the merge `ci.yml` holds no `--allow-create`, and a test pins that: `workflowsStartTestsThroughARunner.test.mjs` gets `no workflow holds the creation flag`.

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

## 13. The planted violations, per root

By hand on the real tree, recorded in the PR, each reverted (`PLAN:1618-1623`). A plant is one new untracked file, so a revert is one `rm`. The C# guard reads the tree when the test runs, so no rebuild: after one build run
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
| R1 | `This_repository_passes_the_guard` | Step 1, before any site work. Expect `owner-blind` 7, `raw-worktree` 23, `shared-store` 2, `docker-run` 1, `raw-temp` 649. Record the counts. | Green at step 6. |
| R2 | **Must-not-copy #2, C#.** The template's `BreakIt` rows `:150-166` (split over `+`, `string.Concat`, holes, `\u`, `\x`, a character, a one-character class, a half, a middle piece) each ONE finding | Plant: in `IsFolded` return `false`. Red: those rows (0 findings). This is on the copy, so the diff check is red too, as expected. | Revert, green. |
| R3 | **Must-not-copy #2, `.mjs`** (Decision 1, A): N8 | Written before `foldedPrefix.mjs` exists: red on the import. Then plant: `foldedHits` skips the fold. Red: rows 4, 5, 7-12. | Revert, green. |
| R4 | **Must-not-copy #1, the audit's sibling.** The template's `A_sibling_childs_folder_survives_the_audit_of_a_run_whose_record_it_shares` (`:95`), and coai's process test in `LeftoversCoaiLayoutTests`: `TwoChildSuitesUnderOneRun_TheFirstToFinish_LeavesTheSecondsFolder`. Two `SuiteChild` runs of a child-only class, both with `TESTSTODELETE_RUN` set to one live record of this user, both making an `OwnedDir` in ONE private root. Child B makes its folder and waits on a go-file; child A makes, disposes and ends, so its audit runs. Assert A exit 0, B's folder and claim byte-identical; then go; B exit 0. | Plant in `LeftoversAudit.Discover`: drop the `Owner.RunRecord(env, _ => { }) != Owner.This` clause and compare with the run's record; and make the fixture scan the private root. Red: B's folder is gone. | Revert, green. |
| R5 | The audit wiring: `AGreenRun_ReportsNoLeak_AndLeavesTheRunFolderToItsOwnEnd` (a child run on a class of one passing test: exit 0, no `this run left` line; after the run the run folder and its claim are gone) | Plant: the template's `RealAudit(Owned.TempRoot(), OwnedDir.Unreclaimed())`. Red: exit not 0, `never disposed: …-coai-run-…`. | Revert, green. |
| R6 | The self-test's teeth | Run § 12.1's loop by hand with the audit set back to `audit: null`: "the end-of-run audit did not fail the run". | Wire it, loop green, on Linux. |
| R7 | The audit reads only the run: `LeftoversCoaiLayoutTests.TheRegistry_LeavesOutTheRunFolder_AndKeepsALeak` | Plant: `RegistryWithoutTheRunFolder` returns all. Red. | Revert, green. |
| R8 | The second pass, C#, in `LeftoversBaselineTests`, on private git repositories: a second identical line ⇒ `raw-temp` (round-2 finding 1); an entry swapped under the same total ⇒ one `raw-temp` and one `baseline`; a stale entry; an entry outside every scope; a proof absent; a proof with no marker; a proof with the marker of another root; `class` not `raw-temp`; a malformed file; a moved line keeps its entry; another guard's scope is not judged (a `node` scope with a stale entry: nothing from this guard) | Written before `LeftoversBaseline.Apply` has a body: every case red on `Apply` returning the report unchanged. | Green. |
| R9 | `LeftoversBaseline`: no answer, a bad answer | `node` set to a missing name ⇒ ONE `baseline` finding, not an exception. Plant: catch nothing. Red with `Win32Exception`. | Revert, green. |
| R10 | The inventory, both wrappers | Add a valid marker to a real file: red, "in the tree, not in the inventory". Remove a row: the reverse. Insert a blank line above a marker: red, both messages. | Revert, green. |
| R11 | The coai roots: `The_scan_sees_the_coai_roots` in `LeftoversCoaiLayoutTests`, in-process with the template's `Tree` shape: one violation at `src_mcp/tests_leftovers/X.cs`, `src_mcp/tests_fakecli/X.cs`, `src_server/tests/X.cs`, `src_bugs/tests/X.cs`, `src_bench/CoaiBench.Tests/X.cs`, `src_vs_code/scripts/x.mjs` ⇒ one finding each; at `.agents/conventions/x.sh`, `.claude/hooks/x.sh` ⇒ none; a frozen-list path ⇒ none; a product `src_mcp/core/X.cs` ⇒ none | Written before the ops of § 6.3: red on `FilesScanned`. | Green with the ops. |
| R12 | `TheBaselineIsFound_FromBothPlaces` (`PLAN:1633-1635`, e2), in `LeftoversCoaiLayoutTests`: a `SuiteChild.StartInfoFor` start with `WorkingDirectory` set to the project folder, then to `AppContext.BaseDirectory`; a child-only test writes `File.Exists(Path.Combine(RepositoryRoot.Path, "raw-temp-baseline.json"))` to an out file | Before step 6: both say `False`, so the test is red. | Green at step 6. It pins that `RepositoryRoot` walks up from the binary, whatever the working folder is. |
| R13 | N6, N7 | Each planted growth is red in the test by construction; first run before the wiring: red on the missing helper. | Green. |
| R14 | `The_baseline_file_exists`: `File.Exists`, and a regular file (`!FileAttributes.ReparsePoint`) | Before step 6: red. | Green at step 6. |

**The rows of § S6's scenarios and where they are:** every class red and green with a valid exemption: the template's `BreakIt`; the token: template `:390-620` and N5; a mount without the detail's table: template `MountedSelectorPick` and its tests; a scan of nothing: template `:334-346`, § 7.1 step 3, N2, N4; the frozen list pinned: template `:655-665` as changed; `release.yml` ⇒ 0: § 13 and the real tree.

## 15. The break-it list

By hand on the real tree, recorded in the PR with the first line of each failure, reverted (`PLAN:1570-1573`).

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
| B11 | A marker for `owner-blind` | red `exemption`, and the line stays red |

## 16. F24's second acceptance check

`PLAN:1629-1630`, `ROLLOUT:1297-1303`, D8 (l). **It runs after the pin carries `bd79584` and before the commit that creates the baseline, and its output is recorded in this PR and in `research/module_tests.md`** (a section "F24's second acceptance check", with the head SHA, the pin SHA and the machine). The first run was at the F24 sha; this is the one on the record. Against the RELEASED guard (the mount at the pin) and THIS repository's real hosts:

**(a) The proof check, both ways, for each scope** (Decision 4). A SCRATCH `raw-temp-baseline.json` at the repository root with the six scopes of § 8.1 and no entries, not committed. Each command with no marker, then with the marker lines of § 8.2:

- Node scopes: `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "the multi-model review extension and server"`: with no marker, 2 `proof missing`; with them, 0 of that class. (Other classes are not asked for here.)
- C# scopes: `./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-method "*TheProofCheck_ClosesForEachScope*"`, a test in `LeftoversBaselineTests` that calls `LeftoversBaseline.Apply` on the real tree with that file: 4 `proof missing`, then 0.

Remove the scratch file. Keep the marker lines.

**(b) The five forced runs on the real C# host.** Each must exit 78, print ONE stderr line starting `coai tests: no test ran — `, and leave the sentinel untouched. From the repository root, with `S=<a scratch path>`:

| Run | Command |
|---|---|
| Folder | `TEMP=$S/none TMP=$S/none TMPDIR=$S/none COAI_S3_SENTINEL=$S/s ./src_mcp/tests/bin/Release/net10.0/CoaiMcp.Tests --filter-class "*DiscoverySentinel"` |
| Claim | `COAI_RUNTEMP_SELFTEST=fail-claim COAI_S3_SENTINEL=$S/s …` the same executable and filter |
| TEMP | `COAI_RUNTEMP_SELFTEST=mismatch-temp …` |
| TMP | `COAI_RUNTEMP_SELFTEST=mismatch-tmp …` |
| TMPDIR | `COAI_RUNTEMP_SELFTEST=mismatch-tmpdir …` |

`DiscoverySentinel`'s `MemberData` (`src_mcp/tests/RunTempChildren.cs:28`) writes the sentinel at discovery, so "untouched" means "before discovery". Then the same five inside the suites: `--filter-class "*RunTempTests"` on mcp and `--filter-class "*RunFolderTests"` on server, bugs, bench, and `node scripts/run-tests.mjs src/test/runFolder.test.mjs` and `node scripts/run-lib-tests.mjs scripts/runFolder.test.mjs`.

**(c) A run under the gate** starts and passes: one small filter of each executable, exit 0.

**On red, stop.** Do not create the baseline. It is a fault in the release or in the host, and it goes to the coordinator.

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
node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline-shrinks --base origin/main --allow-create   # by hand, step 6 only; recorded; needs a full clone

# the family checks and the workflows
node .agents/conventions/tools/plan-lifecycle.mjs
node .agents/conventions/tools/pin-check.mjs
actionlint
dotnet format dew_flow_connect_other_ais.slnx --verify-no-changes --no-restore
```

A private short `TMPDIR` as S5 used it (`TMPDIR=/tmp/s6t`), empty after each whole run except node's own `node-compile-cache`. Check that it is empty, and say so.

## 18. Definition of Done, one to one

Every line of the plan's S6 DoD and of D8's conditions, in the plan's order. A box stays open until its evidence is in the PR.

**§ S6's DoD (`PLAN:1576-1580`).**

| Plan's line | How it is met | Where it is proved |
|---|---|---|
| Every class red once and green with a valid exemption | the template's `BreakIt` rows | `*LeftoversGuardTests`, three legs |
| `owner-blind`, `legacy-name`, `exemption`, `token` at zero, `raw-worktree` at zero unexempted, on the real tree | § 9, § 10 | `This_repository_passes_the_guard`; N1 |
| the baseline's entries recorded (membership, hash) | § 8; the sizes table in the PR | `raw-temp-baseline.json`; wrappers |
| the audit self-tests green on the three legs | § 11, § 12 | the three audit steps' run ids |
| the producer PR's squash merge carries S3, S2-B, S4 and S6 together, `TheProducerAndTheSweep_LandTogether` green | no change; it passes on all four suites | `*RunFolderTests`, `*RunTempTests` |
| the pin at the release that carries the baseline rule | the coordinator's pin bump | `rules.mjs check`, `pin-check.mjs` |

**The addition at the epoch-3 re-review (`PLAN:1609-1611`).**

| Plan's line | How | Where |
|---|---|---|
| zero `owner-blind`, `legacy-name` outside the helpers' own files and the named exemptions | § 10 | wrappers |
| no prefix built from pieces | § 9.2; Decision 1 | the search's output in the PR; N8 |
| the PR lists every S5 deferral and where it was handled | `research/module_tests.md` § *Deferred sites*, repeated in the PR | PR text |
| the baseline is one file at the root | § 8.1 | R14; the Node guard reads only that path |

**The stories DoD (`PLAN:1677-1775`), S6's boxes.**

| Box | How | Where |
|---|---|---|
| S6: both guards red on every class, on an unregistered token, on a scan of nothing; four classes at zero; the baseline in the released form; audit bounded (#1); self-tests on three legs | as above | above; R4, R5 |
| *[round-2 finding 1]* a multiset; the second-identical-line test RED then GREEN | § 8; R8, N7 | `module_tests.md` |
| Every must-not-copy row has its RED-GREEN-RED recorded | S6's: #1 R4, #2 R2 and R3 | `module_tests.md` |
| *[epoch 3]* ONE file, one scope per tree; every scope's proof holds under every invocation of its tree | § 8; the D10 inventory of S4 (`module_tests.md:6206`), re-read: S4 routed `sonarcloud.yml:302` | R14; PR text |
| *[epoch 4]* created only after F24 is released and pinned; seeded violations red in every actual root; a missing proof and a stale entry each a `baseline` finding | § 4 step 0; § 13 | PR record |
| *[re-review plan gates]* F24's acceptance check recorded before S6's baseline; the `RepositoryRoot` baseline test from both folders | § 16; R12 | `module_tests.md`, commit order |
| `module_tests.md` describes … the two guards and the baseline's drain | § 5 | `module_tests.md` § *E5 S6* |

**D8's conditions (`PLAN:542-611`).** S6 proves only some; the others are named so that nobody thinks S6 met them.

| Condition | State after S6 | Whose |
|---|---|---|
| (a) S2-A on `main`, as read after the re-review gate | done (#940) | — |
| (b) one PR, S3 → S2-B → S4 → S5 → S6, every S5 deferral in it, the repository-wide checks green | S6's wrappers | S6 |
| (c) the barrier: the WSL machine's administrator scan recorded; the owner's Windows machine by statement | **open; gates the MERGE, not this build** | the operator |
| (d) corrected; no condition on this PR | — | — |
| (e) the plan gate, the code gate, the legs | the code gate on this PR | the main session |
| (f) the baseline rule released and pinned; F16 (b) pinned | the pin at `bd79584` | the coordinator |
| (g) F3 released and pinned, as this may be the family's first Node declaration | the pin at `bd79584` | the coordinator; S6's N1 is the declaration |
| (h) the parent's other conditions | re-read at merge | the main session |
| (i) F24 released and pinned, a pin bump alone, before S6 creates the baseline | § 4 step 0 | the coordinator, checked in step 0 |
| (j) the diff check green | `TheAdaptedCopyTests` with 52 entries | S6 |
| the merge-gate table, replacing (k) | unchanged cells, plus S6's new wrapper in each mcp cell | CI |
| (l) F24's acceptance check before the baseline | § 16 | S6 |
| (m) D11's rollback text in the PR description | S2-B built the switch | the main session; check it is there |
| (n) the native series' evidence | S4's; S6 adds item (3), "no finding, no row" | S6 for item (3) |

## 19. What only CI can prove, and how each Windows difference is proved

Today's lesson: a fixture that passed on Linux failed only on hosted Windows. So each difference has a named step.

| Behaviour that differs on Windows | Proved by |
|---|---|
| Line endings: on the hosted runner `*.cs` and `*.mjs` check out with CRLF (`.gitattributes` fixes only `*.sh` and one JSON). The hash strips `\r`; the C# `SplitLines` and the JS split agree on the line number. | the wrapper against the COMMITTED baseline in `windows` `Test`: an entry that did not match would be red. N1 in the same job for the Node scopes. |
| Backslash paths in `Discover`, the classifier's answer, `createApplier` | the same two steps |
| `git rev-parse --show-toplevel` against a `RUNNER~1` path (§ 7.2) | `LeftoversBaselineTests` in `windows` `Test`; fixtures ask git |
| A disposal that gives up: a held handle on Windows, a read-only folder on POSIX | the PowerShell audit step, value `failed-dispose` |
| `Get-ChildItem $env:TMP` is the real temp root after the run | the PowerShell audit step |
| `A_path_with_quotes_…`: no quote in a Windows name (the template's branch) | `windows` `Test` |
| `An_unreadable_candidate_…` skips on Windows (`chmod`) | not proved on Windows; the same message path on Linux and macOS |
| The symlink test of R8 and B8: POSIX only | not proved on Windows; `locateBaseline` is shipped code, tested in `dew_flow_conventions` |
| `kimiWindowsInstallRuns.test.ts`, any `OnWindows_*` you touch: skip on POSIX | `windows` `Test`; you touch none of them on purpose |
| Lengths: no new test may assert a path length; a run folder name is long on Windows (`w` + a SID's digits) | review; the private trees in `LeftoversGuardTests` are the template's and passed on hosted Windows on `dew_flow_mcp` #58 |
| Release jobs (`release.yml` `mcp-binaries`, Windows x64 and arm64, macOS, Linux) now run the guard | release evidence only (D8); first seen at the next tag. They depend on the image's own `node`, as `OwnerClaimVectorsTests.cs:52` does already. |

The first CI run is also the first run of: Node 22 for `testLeftovers.test.mjs` on Windows and macOS; the audit on all four suites on all legs; `sonarcloud.yml` with both wrappers under coverage.

## 20. What is NOT S6

- **S7's drain.** No baseline entry is removed by moving its site, beyond § 9.3. No `CreateTempSubdirectory` becomes `TempDir.For`.
- **E2 and E6.** Nothing of `dew_flow_creds_for_devs`, nothing of E6's or E7's repositories. Their guards and pins are theirs.
- **The conventions and the template.** No edit in `.agents/conventions`, `dew_flow_conventions`, or `dew_flow_mcp`. A defect you find there goes to the coordinator. (Decision 1, option B, would be the coordinator's call.)
- **Product code.** No `src_mcp/src`, `core`, `runners`, `src_server/src` or `src_vs_code/src/*.ts` outside `src/test/`. Comments in `deploy/*.sh` and `src_server/*.sh` are the one exception, and they change no command.
- **S3, S2-B, S4, S5 re-work.** Their tests stay. A leak the audit finds is fixed in the test that leaks, and named.
- **The merge conditions that are not S6's** (§ 18), the plan ledger, the parent's entries, the coai feature review, the status lines of the plan.
- **No push.** A run is in progress on this branch. The coordinator says when.

## 21. Assumptions

1. The coordinator merges `main` with the pin bump into the branch before step 0, and the guard at the pin is `bd795847`'s. Measured counts come from an extract of that SHA on head `524df50e`. A merge of `main` can move lines and counts; § 3, the sizes table and § 10's lines are re-read after it.
2. Decisions 1-5 take the option the brief is written for. Decision 1 may stop the plan.
3. A `raw-temp` hit in a literal that makes nothing (the 38 `/tmp` strings of `LocalRuntimeTests` and others, the 7 detector lines of `TheHelpersTestsMakeNothingOutsideAnOwnedScopeTests`) is a site like any other for the baseline, inside its scope. S7 decides how each ends.
4. `src_mcp/tests_fakecli/Serving.cs:140` runs only as a child of a test, with TEMP inherited, and only reads. I read the line and its method; I did not trace every start of the fake CLI. Its reason is checked by the Opus review.
5. The hosted images keep a `node` on PATH for the release jobs. If not, `OwnerClaimVectorsTests` is red there already.
6. The `raw-worktree` reasons of § 10.2 are from the lines that name each folder, read today, except `AVibeDiskRowExpandsAParentFolderTests.cs:431`, where `at` comes from its caller `:443`, and `WorktreeManagerTests.cs:495`, `:526`, where `real` and `storage` were read only by name. Read them before you write the marker.
7. Exact-line inventory: the plan's `(file, line, class)` is kept as written, with its cost: an edit above a marker makes the test red until the row moves. I did not soften it.
8. Option A of Decision 1 is a test-time check in `npm test` only. Nothing in CI's C# legs reads `.mjs` folding.
9. `src_vs_code/src/test/tempRoot.ts` is compiled by `npm run compile` and not picked up by `discover` in `run-tests.mjs`, as it is not `*.test.*`. Check it in step 2.
10. `RepoPaths.Root()` and `RepositoryRoot.Path` both give the repository's root on every leg; the first is used by the coai tests that read `shared/`.
