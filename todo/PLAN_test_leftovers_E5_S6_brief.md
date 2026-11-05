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

**The text.** `PLAN:743` row 2: "plus coai's own: the same twelve in a `.mjs` under the `node` profile. RED when the folding
reader is switched off". `PLAN:1551` (§ S6 scenarios): "the twelve planted spellings in C# and in `.mjs` (#2)".

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

**The text.** `ROLLOUT:1420`-area table, P13: S6's guard must include `src_vs_code/scripts/*.mjs`, "or S6's zero is false for the
runners … the folder list is a repaired reference". `PLAN:1619`, D8 (b): "only the authorised `raw-temp` baseline left".

**The facts.** With `src_vs_code/scripts/` added to the C# guard's `ScriptFolders`, it finds 54 `raw-temp` lines there: 34 in
20 entry scripts and 20 in 13 libraries (§ 10.5). The shipped Node guard does not read this folder. A baseline scope needs a
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

**The text.** `PLAN:1632`: "against the RELEASED guard … a run under the gate passes the guard's proof check for each scope".

**The facts.** The shipped Node guard judges only the scopes that name `node` (rule § What a guard does with it; `createApplier`).
A `csharp` scope is never judged by it. That is the rule, not a defect.

**The smallest option.** Read "the guard" as "the guard the scope names": the two `node` scopes through the released
`leftovers-check.mjs`, the four `csharp` scopes through coai's wrapper, which calls the released library's `createApplier` with
`guard = "csharp"` (§ 7.2, § 16). A repaired reference. **This brief is written so.**

### Decision 5 — `src_server/smoke-image.sh:73`. I think this is NOT a change.

**The facts.** `work=$(mktemp -d)` in a test script, in the machine temp, with no owner form. Its `EXIT` trap removes it
(`:99`, `:102`), so only a killed run leaves it. The plan's exclusion list (`PLAN:1558`) names `deploy/**`, `install-agy.sh` and
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
   D10 `PLAN:622-644`; D11 `PLAN:645-665`; must-not-copy rows 1, 2 `PLAN:739-744`; the DoD `PLAN:1677-1775`.
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
| The baseline's form | `common/test-leftovers-baseline.md` at `bd795847`: `{ class, scopes[{root,proof,guard}], entries[{file,sha256,line}] }`, one entry per occurrence, **no `count`** | The rule says "No `count` field". It overrides `{ file, sha256, line, count }` of `PLAN:1544`. The multiset meaning stays. D6's own note of 2026-11-03 (`PLAN:530`) says "the released form" wins. |
| One file | "One baseline file" (`PLAN:1602`), at the repository root | Overrides `src_mcp/tests/Leftovers/raw-temp-baseline.json` and `src_vs_code/src/test/raw-temp-baseline.json` in `PLAN:1540` and D6. |
| The proof | F24 at the pin: the marker, the TEMP assertion and the five child runs IN the proof file; no order | "S6 waits for F24" (`PLAN:1611`) overrides "ordered first". Epoch 5 (`ROLLOUT:1297`) and the review of F24 (`ROLLOUT:1018-1022`) make five forced runs, not two: `PLAN:1630`'s "a forced folder failure and a forced claim failure" is read as all five. |
| The only-shrinks tests | `--baseline-shrinks` of the shipped guard, in CI, for all scopes; coai adds tests of the wiring, not a second check | The rule ("whatever language the baselined tree is in"). It overrides "their only-shrinks tests" per file (`PLAN:1541`). |
| The wrapper | coai's `LeftoversRepositoryGuardTests.cs` calls `Scan`, then applies the baseline | D2's revision "an ADAPTED copy" (`PLAN:418`) overrides "byte-identical" for `LeftoversGuard.cs`; the wrapper part of D2 stands. How it applies the baseline is § 7.2. |
| Commit place | S6 is the LAST part of the PR: S3 → S2-B → S4 → S5 → S6 | `PLAN:596` overrides "commit 4" in the § S6 heading. |
| The exemption contract | `PLAN:1521-1527`: zero UNEXEMPTED `owner-blind` and `legacy-name`; the inventory equals the guard's list exactly; `owner-blind` takes no inline exemption | Stands. It overrides nothing. |
| Helper-own lines | the list of `PLAN:1590-1598`, re-read by `research/module_tests.md:6385`: the plan's `src_vs_code/scripts/startOfRun.mjs:19`, `:28` code lines are gone (S2-B); `run-tests.mjs:166` replaces `:106`, `:116`, `:124` | "Lines move: the PR re-reads each line number". § 10 holds today's lines. Only lines that a guard flags get a marker. |
| The native series | epoch 5 note (`PLAN:1636`): series' line "if the shipped guard classes" it; the old-prefix fixture is in the inventory | Measured: neither guard flags `make-series.sh` (§ 3). So no series row. The fixtures: § 9.2. |
| The count "29" | "the 29 `worktree add` lines" (`PLAN:1559`) | Measured by content: **23** lines on this tree, 19 real and 4 that only name the words. S3 and S5 moved them. A count is a repaired reference. |
| The exclusion list | `PLAN:1558` "the frozen exclusion list": applies to the **C#** guard as written, as an op on `ExcludedFiles` | The shipped Node guard has no exclusion option and reads every `*.sh`, so the same files also carry inline exemptions for it (§ 10). "The guard's own fixtures" need no entry: the template's tests are spelled in pieces, and I measured 12 copied files scanned, 0 findings. |
| Read-only roots | `PLAN:1549`: "`GetTempPath`/`tmpdir` imports used only to READ the root are migrated, not baselined" | No later note withdraws it, so S6 does it. S7's list (`PLAN:1647-1652`) has the same shape, and finds none of these left. § 9.3 has the exact lines. Node: read per FILE, as the word "imports" says. |
| `src_vs_code/scripts/` | P13 (`ROLLOUT`, table of notes): the C# guard reads this folder | Overrides D2's folder list `PLAN:1534-1536`, which omits it. See Decision 2. |
| Product C# | The list `PLAN:1534`: test trees only | Coai has no root `src/`, so the template's `Source` kind matches nothing and stays in the text. Measured for the record: with the product trees mapped to `Source`, 2 more `owner-blind` hits on real words (`src_mcp/core/Collecting/CandidatePath.cs:39`, `src_mcp/runners/Reviewers/EndCauses.cs:46`). Not S6. |
| The command | `node .agents/conventions/tools/leftovers-check.mjs --repo . --baseline --profile node --token coai --identity "the multi-model review extension and server"` | `PLAN:1561` lacks `--identity` (any other token fails closed without it) and `--baseline`. A repaired reference. |

