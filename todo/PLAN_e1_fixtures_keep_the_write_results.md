# PLAN — the E1 caller-transcript fixtures keep the result of every write they keep

> Status: **plan, with its code on branch `fix/e1-fixture-results` under review, 2026-10-17.** Scope: the cutter
> `scripts/lib/transcript-sanitise.mjs`, its node tests, the committed fixtures under
> `src_mcp/tests/fixtures/caller-transcripts/`, and their guard `ACallerTranscriptFixtureCarriesNoTextTests`. No product code.
>
> Parent: [PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md)
> (epic E1). Asked by [PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md](PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md)
> §8, items 1 and 2, which says this lands as E1's own pull request, merged before E3.S4 is coded.

## 1. The symptom

The fixtures are published data that E3's transcript reader is tested against. Two of the things they show are wrong:

1. **A kept write has no result.** The cutter keeps the lines inside a round's window, plus the line that launched each
   kept agent. When the window ends between a write-tool call (`Write`, `Edit`, `MultiEdit`, `NotebookEdit`) and the `user`
   line that holds its `tool_result`, the call is kept and its answer is dropped. A reader that counts a write only when
   it was answered cannot tell such a write from a refused one. Measured on the fixtures at `2d98f2f8`: c12
   (`registry-and-variable-disagree`) has 1 of 2 write calls unanswered, c17 (`two-repositories-one-session`) 1 of 2,
   c19 (`write-into-submodule-and-scratchpad`) 2 of 2.
2. **The manifest does not say which roots are checkouts.** A case lists its `fixtureRoots`, but not which of them is a
   git checkout when the fixture is materialised. c19 is named for a write into a submodule, and nothing in the manifest
   names the submodule root `D:/fixture/repo-a/p1000`.
3. **`requestedModel` on the main lines is the sentinel.** The real value is a long-context model id such as
   `claude-opus-5-5[1m]`. The `model-ref` vocabulary in `allowlist.json` has the `[1m]` form of every alias
   (`opus[1m]` …) but not of the full ids, so the sanitiser replaced the value with the sentinel in eight cases
   (c02, c03, c04, c07, c11, c12, c14, c17). E3.S4 test 7 wants to read the real value from the shipped c02 file.

## 2. What must be true when it is done

1. **The cutter rule.** In each file (main or agent), the `user` line whose `tool_result.tool_use_id` answers a KEPT
   write-tool call of that same file is pinned into the cut, exactly as a launching line is pinned
   (`scripts/lib/transcript-sanitise.mjs:404-426`, used at `:566-567`). A write call outside the window is not kept, so its
   answer is not pulled in. An answer already inside the window is kept once. A clipped agent file (`--clip-agents`)
   keeps its answers too. Nothing else about the cut changes.
2. **The re-cut.** c12, c17 and c19 are re-cut with the same salt and gain exactly their answering lines (c12 +1, c17 +1,
   c19 +2). Because instants are re-drawn as ranks on the synthetic clock, the later instants of those cases move, and
   their `rounds.json` instants move with them. The timeline stays synthetic (`2000-01-01T…`).
3. **The checkouts.** `manifest.json` carries per case `"checkouts": [...]`: each `repo-<x>` and `other` fixture root of
   the case, and never `D:/fixture/scratch`. c19 also lists `D:/fixture/repo-a/p1000`.
4. **The model ids.** `allowlist.json`'s `model-ref` vocabulary gains the seven long-context ids as an EXACT list —
   `claude-opus-5-5[1m]`, `claude-opus-4-8[1m]`, `claude-opus-4-1[1m]`, `claude-fable-5-1[1m]`, `claude-sonnet-5-5[1m]`,
   `claude-sonnet-5[1m]`, `claude-haiku-4-5-20251001[1m]` — never a pattern. The eight cases are re-cut; only their
   `requestedModel` values change. `ApprovedAllowlistSha256`
   (`src_mcp/tests/ACallerTranscriptFixtureCarriesNoTextTests.cs:32`) is updated in the same diff.
5. **The guards** in `ACallerTranscriptFixtureCarriesNoTextTests`:
   - `EveryKeptWriteCallHasItsResult` (`:679`): over every main and agent file of every case, each write-tool block's
     `id` is answered by a `tool_result` in the same file.
   - `EveryCheckoutIsAFixtureRootOrUnderOne` (`:675`): every checkout matches
     `^[A-Za-z]:/fixture/(repo-[a-z]|other)(/p[0-9]+)*\z` (so `scratch` is refused), sits under a root the case uses,
     and every non-scratch root of the case is listed.
   - The c19 relation test asserts the submodule checkout (`:785`).
   - The private-looking probes (`:819`) add `claude-opus-5-5[1m] please` and `D:/x[1m]`; every vocabulary still
     refuses them.

## 3. Constraints

- The fixture repository is PUBLIC. Nothing real may enter: no real path, id, timestamp, token count, name or text.
  Ids stay remapped, instants stay on the synthetic clock, counts stay ranks.
- The allowlist widening is an exact list. A suffix or prefix pattern would let a private suffix through.
- No product code changes. `TheManifestCountsWhatTheFilesHold` is unchanged and still passes.
- Every method stays within cyclomatic complexity 4 (the guard's existing rule).

## 4. Build order

1. Cutter rule and its node tests (red against the old cutter, then green).
2. The allowlist entries, the hash pin, and the probes.
3. Re-cut the eight + three cases with the same salt; add `checkouts` to the manifest.
4. The two guard tests and the c19 assertion (red against the old fixtures, then green).
5. The E1 plan's S3 entry and `research/module_tests.md` record the change.

## 5. Test plan

- `scripts/lib/transcript-sanitise.test.mjs`: `aKeptWriteCallKeepsTheLineThatAnswersItEvenWhenTheWindowEndsFirst`,
  `aWriteCallOutsideTheWindowIsNotKeptSoItsAnswerIsNotPulledIn`, `anAnswerThatIsAlreadyInsideTheWindowIsKeptOnceAndNothingElseMoves`,
  `aClippedAgentFileKeepsTheAnswerToItsWriteToo`.
- `ACallerTranscriptFixtureCarriesNoTextTests` (the whole class) via the MTP executable, never `dotnet test`.
- Break-its, each seen red then green after restore: drop one answering line from c19; put `D:/fixture/scratch` in a
  checkout list; remove c19's `p1000`; turn the exact list into a pattern; remove one long-context id.
- A privacy scan over all 62 fixture files: every string is an allowlisted value, a fixture path, a remapped id or a
  synthetic instant; no number above the synthetic ranks.

## 6. Definition of Done

- [ ] Every kept write call in every fixture file has its `tool_result` in the same file.
- [ ] `manifest.json` names each case's checkouts; c19 names `D:/fixture/repo-a/p1000`; `scratch` is in none.
- [ ] `model-ref` admits exactly the seven long-context ids; the hash pin matches; the probes are still refused.
- [ ] The node tests and the C# guard class pass; the break-its were seen red.
- [ ] The privacy scan finds nothing real.
- [ ] The E1 plan and `research/module_tests.md` record the change; this plan is promoted when the PR ships.
