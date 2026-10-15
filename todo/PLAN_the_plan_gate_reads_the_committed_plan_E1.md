# PLAN — E1 stories: review a committed plan safely, and record what was reviewed

> Status: **plan only, nothing implemented yet, 2026-10-15.** Epic E1 of
> [PLAN_the_plan_gate_reads_the_committed_plan.md](PLAN_the_plan_gate_reads_the_committed_plan.md) (its `## Epics`).
>
> Scope: `src_mcp` only — `runners/Collecting` (`GitHistory` widened, the new `CommittedPlanReader`), pure units in
> `core/Rounds`, a new plan-input unit in `src/Server/Stages`, the D12 fold in `src/Server/Rounds`, `Tools.cs`, call sites
> in `PanelService.cs` / `RoundEngine.cs`, `RoundRecord.PlanSource`, `research/module_server.md`. No `rounds` column, no
> `--log`, nothing in `src_vs_code` (all E2). **Cornerstone: E1.S2** — the reader's integrity proof (parent §4 steps 3–5,
> D5–D7); the parent's ledger reads `Cornerstones: 1 of 1 — E1.S2`.

## 1. Goal, before any solution

A caller names a committed plan instead of pasting 60–100 KB that a classifier cuts (parent §1). The server reads the blob
at ONE pinned commit, proves the capture is that blob, refuses every wrong read before a round number is taken, gives every
reviewer the whole text with an end line, does not count a reviewer that says the plan did not reach it, and records in the
session file what was reviewed. `planText` callers see only two new prompt lines. The silent failure this epic must not
ship: a capture that is not the blob, accepted and recorded under the blob's id — so that part is the cornerstone.

## 2. What exists — verified at `7de47482`, 2026-10-15

| Fact | Where |
|---|---|
| `ReviewPlanAsync`: two overloads (`:612`, `:618`), a synchronous `Task` return; `NeedsWorktree: false … ReadsCheckout: false` | `src_mcp/src/Server/PanelService.cs:612-699`, `:635` |
| The whole plan view: `RulesText.Section(...) + $"## The plan under review\n\n{planText}"`; the log line | `PanelService.cs:676-677`; `:649-656` |
| `RefuseBeforeBuilding(loaded, sha)` — the cadence check under the claim | `PanelService.cs:692-696` |
| Catching `WorktreeException` from `ResolveShaAsync` into `Error(...)` (the `open` precedent) | `PanelService.cs:482-490` |
| The code stage appends what it did not look at through `RoundWork.Unreviewed` | `PanelService.cs:869`; `src_mcp/src/Server/Rounds/RoundWork.cs:38` |
| `Error` → `Refusal.Answer` (a recorded refusal) | `PanelService.cs:1847-1848` |
| The tool: `planText` required; cadence args optional with C# defaults; the false "plus a read-only checkout" | `src_mcp/src/Tools.cs:78-110` (`:81-83`, `:90`) |
| `planText is required` — shared by every stage through `from` | `src_mcp/src/Server/Rounds/RoundEngine.cs:213-217` |
| The engine resolves `stage.Head` when set; `StageRun.Head` | `RoundEngine.cs:304`; `src_mcp/src/Server/Rounds/StageRun.cs:99` |
| `results` is a local of `(Invocation, Outcome)` tuples, read by the summary, merge, decision, finish, spent prompts | `RoundEngine.cs:409`, `:434`, `:445`, `:451`, `:483`, `:592`; `runners/Reviewers/BoundedScheduler.cs:194` |
| The record is stamped beside `Sha`/`CadenceNote`/`AdmittedBy`; plan rounds keep `RecordsSha: false` | `RoundEngine.cs:483-491`; `src_mcp/core/Rounds/Stages.cs:75` |
| A skipped round is recorded through `SkippedOrOwed` | `RoundEngine.cs:332`, `:384`, `:688` |
| `ResolveShaAsync` = `rev-parse --verify <x>^{commit}`, THROWS `WorktreeException` | `src_mcp/runners/Worktrees/WorktreeManager.cs:91-97` |
| `GitAnswer(Ran, Ok, Out)`; `Lines` TRIMS and splits on `\n` — useless for `-z` or a padded size | `src_mcp/runners/Collecting/GitHistory.cs:14-21` |
| `IsCommitish` (exactly 40 hex), `IsRepoRelative`, `Budget` 30 s, `RunAsync` drops `Truncated` | `GitHistory.cs:72`, `:93`, `:44`, `:264-274` |
| `ProcessResult.Truncated`; stdout decoded with lossy `Encoding.UTF8` | `runners/Processes/ProcessLauncher.cs:127-139`, `:227` |
| Re-encoding a valid UTF-8 capture gives git's byte count — the argument D7 rests on | `runners/Feature/CatFile.cs:30-39` |
| The LFS header, private to the snapshot planner | `runners/Snapshots/SnapshotPlanner.cs:58-60` |
| A `status --porcelain=v1 -z` parser that consumes a rename's second path | `runners/Consultation/FilesystemInvariant.cs:313-337` (`PorcelainReader`) |
| `CredentialFiles.WhichPattern`, used by `review_feature`'s plan read | `src_mcp/core/Feature/CredentialFiles.cs:31`; `src/Server/Stages/FeaturePlan.cs:37` |
| `DocumentRules.MaxBytes` (256 KiB) and `ArtifactIdOf` (16 hex of SHA-256) | `src_mcp/core/Rounds/DocumentRules.cs:31`, `:117` |
| `ReviewerOutcome.Unparseable(Reason, Usage)`; `Ok(NormalisedReview Review, …)`; `Finding.Title` | `runners/Reviewers/ReviewerExecutor.cs:201`, `:106`; `core/Findings/Finding.cs:54-62` |
| Zero answered → `call_human` | `src_mcp/core/Rounds/RoundMachine.cs:404` |
| `RoundRecord.Caller` — nullable, never normalised (the precedent for `PlanSource`) | `src_mcp/src/Server/SessionStore.cs:173-193` |
| `status` = `SessionAnswer.Rounds`; the context omits nulls | `src/Server/ServerJsonContext.cs:143-151`, `:339-345` |
| Harnesses: real git `TempGitRepo` (text writes only); a real `review_plan` round through FakeCli; prompts captured by `FAKECLI_RECORD_DIR` (stdin last); tools/list | `tests/TempGitRepo.cs:50-52`; `tests/FakeCliRoundTests.cs:21-47`; `tests_fakecli/Program.cs:152-157`, `tests/ADocumentIsReviewedEndToEndTests.cs`; `tests/McpContractTests.cs:46` |
| Fifteen test files call `ReviewPlanAsync(repo, branch, planText[, cadence])` | `PanelServiceTests.cs`, `SplitOrderTests.cs`, `CadenceGateScenarioTests.cs`, … |
| Over the 800-line ceiling: `PanelService.cs` 1 849, `RoundEngine.cs` 910 | — |

## 3. Conventions every story follows

- **Tests first, RED on the assertion** — against a stub that compiles and answers the wrong value; never a compile error.
- **Build and run** (`.agents/PROJECT.md` § Commands): `dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4`, then
  `node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<Class>*"`,
  then the whole executable. **Never `dotnet test`.**
- **Break-its**: change PRODUCTION code, run the named test, see it red with the real symptom, restore, see it green; both
  runs go in the story's summary. Never edit, skip or disable a test. B1–B9 are the parent's; B12+ are this document's.
- **No logic in `PanelService.cs` or `RoundEngine.cs`** beyond call sites and one stamp line; new units in new files.
- **Every git argument is an array element**; a revision passes `IsCommitish` first; paths ride `--literal-pathspecs`
  after `--`. New `GitHistory` reads use `GitAnswer.Out` raw, never `Lines`.
- **No refusal starts a round**: every refusal of this epic is answered before `RunStageAsync`, through `Error(...)`.
- Files LF; xUnit v3 + FluentAssertions in `src_mcp/tests/`, named as sentences. Docs per story where it says so.

## 4. Shared contracts — fixed here so S1 and S3–S6 can be briefed now

Fable's spec for E1.S2 may rename a member of C1/C2. If it does, it edits THIS section in the same commit as the spec, and
no dependent story starts before that commit.

**C1 — what was reviewed** (`src_mcp/core/Rounds/PlanSource.cs`, created by E1.S2):

```csharp
public static class PlanOrigin { public const string Commit = "commit"; public const string Text = "text"; }
/// Commit: the 40-hex commit read (filled by the engine for a text round). Path/Blob: empty for a text round.
/// Bytes: the blob's size (committed) or the UTF-8 byte count of planText (text). TextId: ArtifactIdOf(the text sent).
public sealed record PlanSource(string Origin, string Commit, string Path, string Blob, long Bytes, string TextId);
```

**C2 — the reader's answer** (`runners/Collecting/CommittedPlanReader.cs`):

```csharp
public abstract record PlanRead
{
    public sealed record Accepted(PlanSource Source, string Text, string Note) : PlanRead; // Note: D8's "not compared" line, or ""
    public sealed record Refused(string Sentence) : PlanRead;                             // names the cure
}
```

**C3 — decisions closed here** (the parent left them open): `expectedCommit` without `committedPlan` is REFUSED (an
argument that would be silently ignored is a trap); `committedPlan` whitespace-only counts as absent; D8's "not compared"
line rides `RoundWork.Unreviewed` (`PanelService.cs:869` precedent), so it lands on the reviewer line the caller reads.

## 5. Stories

### E1.S1 — `GitHistory` reads an entry, a blob, the root and a path's status, and says when a capture was cut

- **Goal.** Four bounded, non-throwing git reads for the reader, and `Truncated` carried out of `RunAsync` — no existing
  caller's behaviour changes (parent §8).
- **Scope.** `runners/Collecting/GitHistory.cs`: `GitAnswer` gains `bool Truncated = false` (additive, positional default;
  `Broken` unchanged); `RunAsync` (`:264-274`) sets it from `ProcessResult.Truncated`. New methods, each `Refused` without
  a process when an argument fails its guard: `EntryAtAsync(repo, sha, path)` = `git --literal-pathspecs ls-tree -l -z
  --full-tree <sha> -- <path>`; `BlobAsync(repo, oid)` = `git cat-file blob <oid>` (oid behind `IsCommitish`);
  `RootAndHeadAsync(repo)` = `git rev-parse --show-toplevel HEAD`; `PathStatusAsync(root, path)` = `git
  --no-optional-locks --literal-pathspecs status --porcelain=v1 -z --untracked-files=no -- <path>`.
- **Brief.** Mirror `HasPathAsync`/`FileAtAsync` (`GitHistory.cs:158-195`) for guards and shape. No parsing here — the
  `ls-tree` grammar is E1.S2's, the porcelain is `PorcelainReader`'s. No new caller reads `Truncated` in this story.
- **Test scenarios** (`GitHistoryReadsByEntryTests.cs`, real git through `TempGitRepo`; RED: stubs answer `Broken`):
  `AnEntry_IsOneZTerminatedLine_WithModeTypeOidAndPaddedSize` · `AMissingPath_IsRanOkWithNothing` ·
  `ABlob_ComesBackByItsOid` · `RootAndHead_AreTwoLines` · `AStagedEdit_ShowsInThePathsStatus` ·
  `AValueThatIsNotAnOid_StartsNoProcess` (counting launcher) · `ACutCapture_SaysTruncated` (fake launcher returning
  `Truncated: true`, exit 0 — the `ClaudeCapabilityTests.cs:234` shape). `TheFileAtRevisionTests` stays green unchanged.
- **Break-its.** **B12** drop the `Truncated` assignment in `RunAsync` → `ACutCapture_SaysTruncated` red.
- **Done when.** Scenarios green; whole executable green; no existing test edited.
- **Depends on.** Nothing. **Size.** ≈ 220 lines (≈ 70 code, ≈ 150 tests). **Coder: sonnet** · review: opus.

### E1.S2 — The reader's integrity proof: the entry rules, the cut capture, the blob id (CORNERSTONE)

**Cornerstone — spec by Fable, written 2026-10-15, in its own document because this one is sent whole to the gate:**
[PLAN_the_plan_gate_reads_the_committed_plan_E1_S2_spec.md](PLAN_the_plan_gate_reads_the_committed_plan_E1_S2_spec.md)
(§S2.0 measurements · §S2.1 units · §S2.2 types · §S2.3–§S2.7 the rules · §S2.8 algorithm · §S2.9 examples · §S2.10 tests ·
§S2.11 break-its · §S2.12 fixtures · §S2.13 decisions). Area: parent §4 steps 3–5 as one unit — the `ls-tree -z` entry
rules (mode, type, size, exactly one entry), the `Truncated` refusal, and the blob-id recomputation with its BOM and U+FFFD
order (D5–D7), plus step 5's empty and LFS refusals. The coder starts from that document; it is committed.

- **Goal (fixed before the spec).** `CommittedPlanReader.ReadAsync(git, repoPath, sha, canonicalPath, ct)` → C2, doing
  steps 3–5 and 7 over an already-normalised path; the pure parts in `core/` (entry parser, blob id, the proof's decision,
  the LFS test) so they are tested without git. Accepted ⇔ the text handed on re-encodes to the recorded blob.
- **Parent rows it owns.** T6, T7, T8, T9, T19 (the reader half: a valid plan holding a literal U+FFFD is accepted), T21.
  Break-its **B1** (drop the `Truncated` refusal → T7), **B4** (skip the blob-id proof → T8), **B8** (refuse on U+FFFD
  before the id check → T19). B2 is run in E1.S3, where T2 exists.
- **The nine questions this section used to list are closed in the spec's §S2.13**, each against a measurement (§S2.0:
  git 2.55, .NET 10 — the launcher's decoder DROPS a leading BOM; a symlink's `ls-tree` type is `blob`, its mode decides; a
  SHA-256 repository hashes its blobs with SHA-256, so a 64-hex sha is refused first). C1 and C2 (§4) are **confirmed
  unchanged**. Units: `core/Collecting/{TreeEntry,BlobProof,LfsPointer}.cs` (the LFS header moves there from
  `SnapshotPlanner.cs:60` — one constant), `core/Rounds/{PlanEntry,PlanSource}.cs`, `runners/Collecting/CommittedPlanReader.cs`.
- **Break-its.** B1, B4, B8 (above) and the spec's **B16–B23** (§S2.11): mode-vs-type, padded size, `Bytes` = blob size,
  `TextId` over the text handed on, ceiling before `cat-file`, exactly one entry, ordinal path, LFS prefix.
- **Done when.** The spec's three test files green; B1, B4, B8, B16–B23 observed red then green; `ReviewSnapshotTests` and
  `ArchitectureTests` green unchanged; the whole executable green.
- **Depends on.** E1.S1 (the git reads). **Size.** ≈ 520 lines (≈ 170 code incl. ≈ 20 in `TempGitRepo.cs`, ≈ 350 tests).
- **Cornerstone — spec by Fable** · **Coder: sonnet** · review: opus.

### E1.S3 — The reader's guards: path shape, credential shape, and a clean checkout

- **Goal.** Steps 1, 2 and 6 around E1.S2's proof: a bad or credential-shaped path is refused before any process; a path
  edited or staged in the checkout is refused when HEAD is the pinned sha, and "not compared" is said when it is not.
- **Scope.** `CommittedPlanReader`: a pure `PlanPath.Canonical(string)` in `core/Rounds/` — trim, `\` → `/` (as
  `CadenceDesk.cs:143`), refuse a trailing `/` and anything `GitHistory.IsRepoRelative` refuses; `CredentialFiles.WhichPattern`
  (sentence as `FeaturePlan.cs:41-43`); step 2 `RootAndHeadAsync` → root + "HEAD is the sha"; step 6 `PathStatusAsync`
  from the root, parsed by `PorcelainReader.Read`, refused naming **staged** (`X ≠ ' '`) or **uncommitted** (`Y ≠ ' '`);
  HEAD ≠ sha → `Note` = one line naming both commits and saying the working tree was not compared. D8's two blind spots
  (`skip-worktree`/`assume-unchanged`; a branch checked out in another worktree) go in the reader's remarks, not code.
- **Test scenarios** (`ACommittedPlanMustBeCleanTests.cs`, real git): T2 (edited → "uncommitted", no round), T3 (staged →
  "staged"), T4 (`core.autocrlf=true`, CRLF working copy of an LF blob → **accepted**), T5 (`../outside.md`, `/etc/x`,
  `C:\x`, `a\0b`, `todo/` → refused, counting launcher saw no process; `todo\PLAN.md` → read as `todo/PLAN.md`), T16 (HEAD on another commit, file dirty →
  accepted with the note), and the reader half of T18 (`.env`, `secrets/id_rsa` committed → refused, no process).
- **Break-its.** **B2** read the working file instead of the blob → T2 and T8 red. **B9** run the status even when HEAD ≠
  sha → T16 red. **B13** drop the `\` → `/` step → T5's `todo\PLAN.md` case red.
- **Done when.** Scenarios green; E1.S2's tests still green. **Depends on.** E1.S2's code.
- **Size.** ≈ 300 lines (≈ 80 code, ≈ 220 tests). **Coder: sonnet** · review: opus.

### E1.S4 — Every plan reviewer sees where the plan ends, and one that did not is not an answer

- **Goal.** D12 on every plan round, `planText` rounds included (that is where the hollow pass happened): the prompt
  carries the instruction and `END OF PLAN <TextId>`; an answer that is ONLY the `PLAN_INCOMPLETE <TextId>` finding becomes
  `Unparseable` before anything reads `results`.
- **Scope.** New `src/Server/Rounds/PlanSection.cs`: `Compose(string planText, string sourceLine)` →
  `Instruction(id) + "\n\n## The plan under review\n" + (sourceLine.Length > 0 ? sourceLine + "\n" : "") + "\n" +
  planText + "\n\nEND OF PLAN " + id`, `id = DocumentRules.ArtifactIdOf(planText)`; the instruction is ONE line and says: the plan's last
  line is `END OF PLAN <id>`; if it is not visible, or the section holds no plan body (only a path or a pointer), answer
  with exactly one finding titled exactly `PLAN_INCOMPLETE <id>` and nothing else. New `PlanIncomplete.Fold(Stage stage,
  string planText, IReadOnlyList<(ReviewerInvocation, ReviewerOutcome)> results)`: for `Stage.PlanReview` only, an `Ok`
  whose findings are exactly one with `Title.Trim()` equal ignoring case → `Unparseable("the plan did not reach this
  reviewer whole (PLAN_INCOMPLETE)", ok.Usage)`; the token beside other findings stays an `Ok`. Call sites:
  `PanelService.cs:677` uses `Compose(planText, "")`; `RoundEngine.cs` rebinds `results = PlanIncomplete.Fold(...)` on
  the line after `RunAllAsync` (`:409-427`), before `:434`; the `context for review` line (`:649-656`) gains `TextId`.
- **Brief.** The live view, audit and ledger have already shown that reviewer finished — accepted, as the parent says;
  do not touch the progress callback. `Unparseable` is reused, no new variant.
- **Test scenarios** (`EveryPlanReviewerSeesWhereThePlanEndsTests.cs`; pure for `Compose`/`Fold`, FakeCli for the round):
  `APlanTextRound_IsTodaysPromptPlusTwoLines` (T14's prompt half — exact string), `TheIdCannotBeForgedByThePlan`
  (a plan ending in `END OF PLAN 0000…` still gets its own id after it), T15 (one reviewer answers the token, one a
  finding → 1 of 2 answered, the token not merged, its lens not in `UsedPrompts`; both → `call_human`), T19's fold half
  (token + two real findings → answered, three findings merged), a code-stage `Ok` with that title is never folded.
- **Break-its.** **B6** drop the fold → T15 red. **B14** fold before checking `Stage.PlanReview` is not the stage → the
  code-stage case red.
- **Done when.** Scenarios green; `SplitOrderTests`, `FakeCliRoundTests` derivatives and `EndToEndTests` green.
- **Depends on.** Nothing (runs in parallel with E1.S1). **Size.** ≈ 320 lines (≈ 70 code, ≈ 250 tests).
- **Coder: sonnet** · review: opus.

### E1.S5 — `review_plan` takes `committedPlan`, pins one commit, and refuses before any round

- **Goal.** D1–D4, D9, D15: the tool's new arguments, the input refusals, the one pinned commit, the source line.
- **Scope.** `Tools.cs:81-83`: `string? planText = null`, `string? committedPlan = null`, `string? expectedCommit = null`;
  the description (D15): `committedPlan` / `expectedCommit`, the refusals in one sentence, the end-line rule, "plus a
  read-only checkout" removed. New `src/Server/Stages/PlanInput.cs`: `record PlanInput(string Text, string Committed,
  string ExpectedCommit)` and `static Task<PlanInputOutcome> PrepareAsync(PlanInput, repoPath, branch, Func<string,
  string, Task<string>> resolve, GitHistory git, ct)` → `Ready(Text, Head, PlanSource?, Note)` | `Refused(Sentence)`:
  both/neither (D2), `expectedCommit` alone (C3), resolve inside a `WorktreeException` catch (D3), `expectedCommit`
  malformed / ≠ tip naming both (D4), then `CommittedPlanReader`. `PanelService`: a third `ReviewPlanAsync(repo, branch,
  PlanInput, CadenceArgs, ct)` becomes `async`; the two string overloads delegate with `PlanInput(planText, "", "")`, so
  no test call site changes; `Refused` → `Error(...)`; `Ready` → `RunStageAsync(text, … { Head = head, PlanSource = source })` (`StageRun` gains `public PlanSource?
  PlanSource { get; init; }` beside `Head`, `StageRun.cs:99`; nothing reads it before E1.S6), the source
  line `(read by the server from <path> at commit <sha12>, blob <oid12>, <bytes> bytes)` into `Compose`, `Note` into
  `RoundWork.Unreviewed`. A `planText` round takes no resolve, exactly as today.
- **Test scenarios** (`ReviewPlanTakesACommittedPlanTests.cs`, FakeCli + real git; `McpContractTests` gains one case):
  T1 (100 KB committed plan with a unique last line — every recorded prompt holds it and `END OF PLAN <TextId>`), T10,
  T11 (malformed; stale → names tip and expected), `ExpectedCommitAlone_IsRefused`, T12 (the `resolve` seam returns A
  while the branch tip is B → `head_sha` = A and A's text was reviewed), T18 end to end (no reviewer launched, no round
  number taken), T20 (unresolvable branch → an `Error` answer, not a tool exception), and tools/list publishing only
  `repoPath`, `branch` as required for `review_plan`. Every refusal case asserts the session file unchanged.
- **Break-its.** **B3** accept both inputs → T10 red. **B5** drop `Head = head` → T12 red. **B7** drop the credential
  refusal → T18 red.
- **Done when.** Scenarios green; the fifteen existing `ReviewPlanAsync` callers green unchanged;
  `TheInstructionsFitWhatAClientKeepsTests` green; `module_server.md` tool row (`:13`) and the `review_plan` text updated.
- **Depends on.** E1.S3 (the whole reader), E1.S4 (`Compose`). **Size.** ≈ 420 lines (≈ 130 code, ≈ 290 tests).
- **Coder: sonnet** · review: opus.

### E1.S6 — The round records what it reviewed, and `status` shows it

- **Goal.** D10: every completed plan round carries `PlanSource` in the session file; a skipped round has none; a round
  from before the field reads absent, never empty.
- **Scope.** `SessionStore.cs` beside `Caller` (`:193`): `public PlanSource? PlanSource { get; init; }`, remarks as
  `Caller`'s. The stamp at `RoundEngine.cs:483-491`: for `Stage.PlanReview`, `PlanSource = (stage.PlanSource ??
  new(PlanOrigin.Text, "", "", "", UTF-8 bytes of planText, ArtifactIdOf(planText))) with { Commit = sha }` — one call to
  a pure `PlanSources.Stamp(stage, planText, sha)` in new `src/Server/Rounds/PlanSources.cs`, one line in the engine. Never on
  `SkippedOrOwed`. `ServerJsonContext`: `[JsonSerializable(typeof(PlanSource))]` (E2 writes the column with it).
- **Test scenarios** (`APlanRoundRecordsWhatItReviewedTests.cs`, FakeCli + real git): T13's session-file and `status`
  half (commit, path, blob, bytes, `TextId` of the committed read), T14's record half (`Origin = text`, `Commit` = the
  round's sha, `Path`/`Blob` empty), a skipped plan round has no `planSource`, a session file without the field loads
  and re-saves without one, a code round has none.
- **Break-its.** **B15** stamp `PlanSource` without `Commit = sha` → T14's record half red.
- **Done when.** Scenarios green; `module_server.md` describes `PlanSource` beside the `Caller` record paragraph.
- **Depends on.** E1.S5. **Size.** ≈ 230 lines (≈ 45 code, ≈ 185 tests). **Coder: sonnet** · review: opus.

## 6. Build order

```
S1 git reads ──► S2 spec (Fable) ──► S2 code ──► S3 guards ──┐
S4 sentinel (parallel from the start) ───────────────────────┴─► S5 arguments + pin ──► S6 record
```

S2's spec may be written while S1 is coded. The epic lands as ONE pull request on its own branch, the coai gate once
(parent `## Epics`); every commit green on its own. The consultant's fallback cut, if the epic proves too large: S4 alone.

## 7. Test plan — the parent's §7 rows this epic owns

| Rows | Story | Kind |
|---|---|---|
| T6, T7, T8, T9, T19 (reader), T21 | S2 | pure + real git |
| T2, T3, T4, T5, T16, T18 (reader) | S3 | real git |
| T14 (prompt), T15, T19 (fold) | S4 | pure + FakeCli |
| T1, T10, T11, T12, T18 (round), T20 | S5 | FakeCli + real git |
| T13 (session file, `status`), T14 (record) | S6 | FakeCli + real git |
| B1, B4, B8 (S2) · B2, B9 (S3) · B6 (S4) · B3, B5, B7 (S5) · B12–B15 (this document) | each story | red → restore |

T13's `--log` and old-database rows and T17 are E2's.

## 8. Growth

`RoundRecord.PlanSource` ≈ 250 B per plan round in its session file, retired with the file; no directory, cache or
long-lived process; five git processes per committed read, each ≤ 30 s, ended with the call (parent §9).

## 9. Definition of Done — epic E1

- [ ] E1.S2's spec written by Fable and committed into its section before its code; the parent's ledger reads
      `Cornerstones: 1 of 1 — E1.S2`.
- [ ] E1.S1–E1.S6 landed, each with its scenarios green and its break-its red-then-green in its summary.
- [ ] `review_plan` takes `committedPlan` (+ `expectedCommit`), exclusive with `planText`; old callers unchanged; tools/list
      publishes the three as optional; the description no longer claims a checkout.
- [ ] Every refusal answered before a round number is taken; a `planText` prompt is today's plus two lines.
- [ ] `CoaiMcp.Tests.exe` green as a whole; `node .agents/conventions/tools/plan-lifecycle.mjs` clean;
      `research/module_server.md` updated.
- [ ] This document is promoted with the parent and E2's, not before.
