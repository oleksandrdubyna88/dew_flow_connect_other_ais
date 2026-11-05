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
`LeftoversGuard.cs:222-223`). The shipped Node guard at `bd795847` has none. I put 15 spellings through its `scanText` with the
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
temporary commit on the PR that adds a step with `fetch-depth`-safe `git fetch --unshallow` and `--allow-create`; its run id and
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
4. The template at `197cc76`, read with `git -C <dew_flow_mcp checkout> show 197cc76:tests/Mcp.Tests/Leftovers/<name>.cs`, the 13
   files of § 6.1, and its CI `git -C <dew_flow_mcp checkout> show 197cc76:.github/workflows/ci.yml`, lines 48-71, 103-133, 186-210.
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

The 13 template files of § 6.1, with their folders repaired, scanned in a scratch tree: 12 files read, 0 findings.

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
| `src_vs_code/src/test/` | `src_vs_code/src/test/runFolder.test.mjs` | `:27` P-T | `:75` P-1, `:82` P-2, `:88-94` P-3 to P-5 |
| `scripts/` | `scripts/runFolder.test.mjs` | `:26` P-T | `:68` P-1, `:76` P-2, `:83` P-3 to P-5 |

So the three other C# suites are NOT a gap. Their `Main` is the same gated one (`TestEntryPoint.cs`, differing only in the namespace).
Note what the matrix proves: bugs runs on Linux only and bench not on Windows (D8's table, `PLAN:565-577`), and that is accepted.

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
| `src_vs_code/src/test/tempRoot.ts` | One export, the one read of the temp root for two `.ts` tests (§ 9.3). The name is S7's (`PLAN:1652`). |
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
| `LeftoversGuard.cs:46` | `ExcludedFiles { get; } = [];` | the frozen list of § 6.5 | `PLAN:1568`. Also op the doc comment `:44-45`, which says "none". |
| `:48` | `HelperFile = "tests/Mcp.Tests/Leftovers/Owned.cs"` | `"src_mcp/tests_leftovers/Owned.cs"` | coai's layout (D2) |
| `:49` | `private const string HelperFolder = "tests/Mcp.Tests/Leftovers/";` | `private static readonly string[] HelperFolders = ["src_mcp/tests_leftovers/", "src_mcp/tests/Leftovers/"];` | coai has TWO helper folders (D2) |
| `:64-66` | the three allowances | `"src_mcp/tests_leftovers/LeftoversGuard.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardTests.cs"`, `"src_mcp/tests/Leftovers/LeftoversGuardScriptsTests.cs"` | coai's layout. **The list stays at four files.** |
| `:281` | `source.Relative.StartsWith(HelperFolder, StringComparison.Ordinal)` | `HelperFolders.Any(folder => source.Relative.StartsWith(folder, StringComparison.Ordinal))` | follows `:49` |
| `LeftoversGuardFiles.cs:28` | `["bin", "obj", "node_modules", ".git", ".claude", ".http-artifacts", "logs"]` | add `".agents"` after `".claude"` | the mount is at `.agents/conventions` |
| `:31` | `ScriptFolders = [".github/scripts/", "http/"]` | add `"src_vs_code/scripts/"` | P13, Decision 2 |
| `:63-64` | `relative.StartsWith("tests/", …) ? SourceKind.Tests : relative.StartsWith("src/", …) ? SourceKind.Source : null` | `TestRoots.Any(root => relative.StartsWith(root, StringComparison.Ordinal)) ? SourceKind.Tests : relative.StartsWith("src/", StringComparison.Ordinal) ? SourceKind.Source : null` and, above `:28`, `private static readonly string[] TestRoots = ["tests/", "src_mcp/tests/", "src_mcp/tests_leftovers/", "src_mcp/tests_fakecli/", "src_server/tests/", "src_bugs/tests/", "src_bench/CoaiBench.Tests/"];` | The roots of `PLAN:1535`. `tests/` and `src/` stay, so the template's ~120 fixture rows at `tests/X/A.cs` and `src/Y/Y.cs` stay byte-identical. Neither folder exists in coai. |
| `LeftoversGuardScripts.cs:20` | `.claude/rules/shared/tools/lib/sweeper-scan.mjs` | `.agents/conventions/tools/lib/sweeper-scan.mjs` | the mount |
| `LeftoversGuardTokens.cs:12` | `TokenFile = "tests/Mcp.Tests/Leftovers/OwnedToken.cs"` | `"src_mcp/tests_leftovers/OwnedToken.cs"` | layout |
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
| `:657` | `LeftoversGuard.ExcludedFiles.Should().BeEmpty();` → `.Should().Equal(<the list of § 6.5>)`, so the list is frozen. The method name `No_file_is_excluded_…` stays: renaming it is not needed and an op is a cost. |
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
const { BASELINE_FILE, createApplier, loadBaselineAt, siteSha256 } = await import(module);
const request = JSON.parse(readFileSync(0, "utf8"));
const applier = createApplier(loadBaselineAt(repo), repo, BASELINE_FILE, new Set(request.discovered), "csharp");
const lines = new Map();
const lineOf = (file, number) => {
  if (!lines.has(file)) lines.set(file, readFileSync(join(repo, file), "utf8").split(/\r?\n/));
  return lines.get(file)[number - 1];
};
const left = request.hits.filter((hit) => !applier.take(hit.file, siteSha256(lineOf(hit.file, hit.line))));
console.log(JSON.stringify({ left, findings: applier.finalFindings() }));
```

- `module` is the file URL of `<mount>/.agents/conventions/tools/lib/raw-temp-baseline.mjs`, as `GuardScripts.StartInfo` makes its URL.
- `request.discovered` is `GuardFiles.Discover(root).Select(file => file.Relative)`.
- `request.hits` is every `raw-temp` finding of the report, in the report's order (file ordinal, then line). That is "in line order" of the rule.
- `Apply` returns: every non-`raw-temp` finding; each hit in `left` as its original `raw-temp` finding; each item of `findings` as `new Finding(file, line, "baseline", text)`.
- A non-zero exit, no `node`, or an answer that is not this JSON is ONE finding `baseline` naming the cause. Fail closed, never an exception, never a pass. Exit code 1 with an unhandled throw (a usage error of `loadBaselineAt`: a folder at the path, a run not at the git root) lands here.

No baseline file yet: `loadBaselineAt` reads the path and throws `ENOENT`. Before step 6 the wrapper is therefore red on that: treat an absent file as "no baseline", `left` = all hits, through `File.Exists`-free `locate` in the program: use `locateBaseline(repo, { gitRoot: true }).absent` first, and answer `{ left: request.hits, findings: [] }` when it is true. After the baseline exists this branch is never taken, and `The_baseline_file_exists` in § 14 pins that.

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
| N5 `the token is a registered row and written once` | On private repositories, through the exported `scanRepository`: the real mount and `coai` ⇒ no `token` finding; a C# `RepoToken = "coai"` beside a Node `REPO_TOKEN = 'other'` ⇒ one; the token `Coai` ⇒ one. |
| N6 `the baseline only shrinks` | A private repository with a committed baseline, through `--baseline-shrinks --base HEAD`: a new key, a raised count, a new scope, a changed proof, a changed guard ⇒ exit 1, each with its sentence; a removed entry ⇒ exit 0; no `--allow-create` ⇒ a creation is exit 1. |
| N7 `the baseline's second pass` | A private repository with a `node` scope: a second identical line ⇒ `raw-temp`; an entry swapped under the same total ⇒ a `raw-temp` and a `baseline`; a stale entry ⇒ `baseline`; a proof with no marker ⇒ `baseline` naming "proof missing"; a moved line ⇒ clean. |
| N8 `the twelve spellings` (Decision 1, option A) | The rows of the table below, each one finding from `foldedPrefix.mjs`; `foldedHits` over every file of `discover(repo,'node')` is empty. |

All private repositories come from `owned.ownedDir(t, 'guard')`, inside the run folder, with `git init`. Never spell the marker or the prefix as a whole literal in this file: build them from `owned` or in pieces, or they count as a marker and the guard reads them. `test` names and comments are free.

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

- C# (3 suites + mcp), directly above the class's `/// <summary>`… or anywhere: `// testsToDelete-baseline-proof: src_server/tests/`
- Node: `// testsToDelete-baseline-proof: scripts/`

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

