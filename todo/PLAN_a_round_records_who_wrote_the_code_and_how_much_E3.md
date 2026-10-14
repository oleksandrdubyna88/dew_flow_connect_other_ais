# PLAN — E3: the caller's own transcript, read text-free (`CallerTranscript` → `ObservedSession`)

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E3 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
> Scope: `src_mcp/src/Store` — a new reader keyed by the caller's session id, the text-free read helpers lifted out of
> `AgentLog.cs` (its behaviour kept), and one helper in `src_mcp/runners` widened. No schema step, no call site in
> `RoundEngine.cs`/`PanelService.cs`, nothing persisted: E5 stores, E4 attributes. Five stories; ONE cornerstone,
> **E3.S4**, spec by Fable.

## 1. The goal, and where this epic stops

The parent's §5 asks for an `ObservedSession`: what the caller's Claude session and every subagent it launched
actually did — messages, models, effort, tokens, launches, parent links, writes — read from the session's own transcript
files, keyed by the session id, never by `cwd`, and never holding a byte of prompt, description or message text.

| Item | Built here | Not here — whose |
|---|---|---|
| Which session, which file, and why none | E3.S2 | — |
| Counting: messages, tokens, launches, models, effort, states | E3.S4 (cornerstone) | — |
| A write and the checkout it landed in | E3.S3 | Whether that checkout IS the gate's `repo_path`: E4 |
| Windows, association, ambiguous totals, contributors | — | E4 (`RoundAttribution`) |
| The budget's numbers (2 s / 64 MB), the call before `Projection.Write`, columns, `authors_reader` column | the budget MECHANISM and `ReaderVersion` | E5 |
| Fixtures with every text field a sentinel | — (consumed) | E1 |
| Backfill over stored `rounds.caller` ids | the `stored` id source | E7 |

E3.S1–S3 build on synthetic files written by their own tests, so they **do not wait for E1**. E3.S4 and E3.S5 run
over E1's fixtures and start when E1.S5 has landed them.

## 2. What the reader is built on

### 2.1 In the code (verified on this branch, 2026-10-14)

| What | Where | How E3 uses it |
|---|---|---|
| Shared-mode, never-throwing line read | `src_mcp/src/Store/AgentLog.cs:183-207` | lifted to `TranscriptLines` (S1) |
| Torn-line tolerance | `AgentLog.cs:209-231` (the `JsonException` catch at `:227-230`) | the parse half lifted (S1) |
| Day gate | `AgentLog.cs:103-112` (`Days`), `:157-173` (`Mentions`) | lifted (S1); the reader uses it under a lower bound (S4) |
| Last-write gate | `AgentLog.cs:121-130` | the same test, applied to agent files (S4) |
| Default transcript root | `AgentLog.cs:39-40` (`DefaultProjectsDir`) | moved to `ClaudeHome.Default` (S2), `AgentLog` delegating |
| Override | `src_mcp/src/Server/PanelSettings.cs:386` (`AgentLogDir`), `:972` (`COAI_AGENT_LOG_DIR`) | `ClaudeHome.With(agentLogDir)` (S2) |
| The env ladder today | `src_mcp/src/Server/CallerSessions.cs:44-53` | **not reused as is**: it puts `COAI_CALLER_SESSION` first, and the parent (§16 D9) wants that alone to be `unobserved: stated id` (S2) |
| Vendor | `src_mcp/src/Server/CallerDeclaration.cs:90-95,106-108` | handshake outranks env; the reader is told the vendor, never infers it (S2) |
| What `rounds.caller` holds | `src_mcp/src/Server/Rounds/RoundCommands.cs:34-35` | the `stored` id source for E7 (S2) |
| Path key normalisation | `src_mcp/runners/Context/RepositoryIdentity.cs:24` (`Normalised`) | the key of a checkout root (S3) |
| "Holds a `.git` file or folder" | `src_mcp/runners/Worktrees/SubmodulePopulator.cs:133-134` (`HasGit`, `internal`; `runners` has no `InternalsVisibleTo` for `CoaiMcp`, `CoaiMcp.Runners.csproj:40`) | widened to `public` and called (S3) — reuse move 1, no copy |
| `agent_log`'s window | `src_mcp/src/Server/Rounds/RoundEngine.cs:892-902` | untouched; the parent's §3 keeps `agent_log`'s behaviour |
| `AgentLog`'s guards | `src_mcp/tests/AgentLogTests.cs` (14 tests; `Days` at `:125-133`) | stay green; the `Days` test moves with the code (S1) |
| Clock seam | `TimeProvider`, `src_mcp/tests/FixedClock.cs` | the budget's time (S4) |
| Temp dirs | `src_mcp/tests/TempDir.cs` | every test here |

No code reads the Claude session registry today: there is no `CLAUDE_PID` anywhere under `src_mcp`. S2 adds that read.

### 2.2 In one transcript folder (this machine, 2026-10-14; field names and counts only, no content)

Read from one Claude Code session folder: main transcript 14.8 MB, `subagents/` **63 MB** across 92 agents.

- **`agent-<id>.meta.json`** keys: `agentType`, `description`, `toolUseId`, `spawnDepth` (1 on 90, 2 on 2),
  `requestShape` (`background` on all), `requestNonInteractive`, `model` (alias: `sonnet` 60, `opus` 25, `fable` 7),
  `parentAgentId` (on the 2 nested).
- **Assistant line** keys that matter: `message.id`, `message.model`, `message.usage` (`input_tokens`,
  `cache_creation_input_tokens`, `cache_read_input_tokens`, `output_tokens`, plus non-token keys), `requestedModel`,
  `effort`, `perTurnEffort`, `isSidechain`, `agentId`, `timestamp`, `cwd`, `sessionId`, `version`.
- **One message, several lines**: ≈2 400 distinct ids over ≈4 500 subagent assistant lines. Within one id the three
  input terms are identical on every line, `output_tokens` never decreases, and the LAST line holds the maximum; on
  **162** ids the lines carry DIFFERENT `output_tokens`. So "once per `message.id`" needs a rule for which value — summing
  over-counts, the first line under-counts. No id appeared in two files.
- **Agent files**: the first line of every `agent-<id>.jsonl` is `type: user` and carries `agentId` = the file's id (92 of
  92); every assistant line in it carries the same `agentId`.
- **Requested vs observed**: subagent `requestedModel` equals `message.model` here; the main session's
  `requestedModel` carries a `[1m]` suffix that `message.model` does not.
- **Effort**: present on every assistant line here (`medium`, `high`, `xhigh`). `perTurnEffort` also exists; what
  it means is not established, so it is not read.
- **The `Agent` tool**: 90 `Agent` tool_uses in the main transcript, 92 metas (2 launched by subagents). The input keys
  include `prompt`, `description`, `model`, `subagent_type`, `run_in_background`. The tool_result's `toolUseResult`
  holds `agentId` and `resolvedModel` beside text fields, so it is NOT read (§3.4).
- **Write tools seen**: `Edit` (`file_path`, plus `old_string`/`new_string`), `Write` (`file_path`, `content`). Writes
  also go through `Bash` and `PowerShell`, and those stay `Unknown` (parent §5).
- No `isSidechain: true` line in the main transcript; one `compact_boundary` system line.

E1 measures the same things on both sides and per Claude Code version. Where E1 finds a different shape, the RESULTS
file wins over this section, and the story it touches records the change.

## 3. The contract E4 reads — `ObservedSession`

These names are the interface between this epic and E4. E3.S4's spec may ADD members but may not rename or remove one
without updating E4's stories document. **Realigned 2026-10-14 to E3.S4's spec**, which wins where the two differ:
the block below is a copy of its contract; members the spec added are marked *added*.

```csharp
// src_mcp/src/Store/ObservedSession.cs
public sealed record ObservedSession(
    CallerSessionId Session,                         // E3.S2
    ObservationState State,
    DateTime? FirstLineUtc,                          // the main file's first parseable line WITH a timestamp, any type
    IReadOnlyList<ObservedAgent> Agents,             // "main" first, then by launch
    IReadOnlyList<ObservedMessage> Messages,         // one per message.id, in range of fromUtc
    IReadOnlyList<ObservedWrite> Writes,             // one per ANSWERED, non-error write call
    IReadOnlyList<UnobservedLaunch> UnobservedLaunches, // added: the Agent calls and metas with no transcript, with instants
    ReadDiagnostics Diagnostics)                     // added: counts only, never a string
{
    public const int ReaderVersion = 1;              // bumped by ANY change to a counting rule (E5's authors_reader)
    public bool IsObservedZero => State.Kind == ObservationKind.Observed && Agents.Count == 1;
}

public sealed record ObservedAgent(string Key /* "main" | agentId (from the FILE name) */, string ParentKey /* "main" | agentId | "" */,
    string ToolUseId, string AgentType, string RequestedAlias /* meta.model | "" */,
    DateTime? LaunchedUtc /* the agent file's first parseable line; null for main and for a meta without a file */,
    DateTime? CalledUtc   /* added: the launching Agent call's message instant, when that call was parsed */,
    bool TranscriptPresent /* added: false for a meta whose agent-<id>.jsonl is missing */);

public sealed record ObservedMessage(string AgentKey, string MessageId, DateTime Utc /* earliest line */,
    string Requested /* meta alias, else the earliest line's requestedModel, else "" */, string Model /* message.model */,
    string Effort /* the earliest line's effort, else "unknown" */, TokenUsage Tokens /* per-term MAX over the id's lines */,
    bool UsageSeen /* added: false when no line of the id carried a usage object; Tokens are then zeros, not evidence */);

public sealed record TokenUsage(long Input, long CacheCreation, long CacheRead, long Output);
public sealed record ObservedWrite(string AgentKey, DateTime Utc /* its message's instant */, string Tool, WriteTarget Target);
public sealed record UnobservedLaunch(string ParentKey, string ToolUseId, DateTime? Utc, string Reason); // added
public sealed record ReadDiagnostics(int FilesRead, long CharsRead, int LinesSkippedByDayGate, int TornTails,
    int SyntheticLines, int WriteCallsRefused, int WriteCallsUnanswered, int MessagesWithoutUsage);    // added

public sealed record ObservationState(ObservationKind Kind, IReadOnlyList<ObservationReason> Reasons)
{ public string Render() => …; }   // "observed" | "partial: <code>[ ×N][, <code>…]" | "unobserved: <code>"
public enum ObservationKind { Observed, Partial, Unobserved }
public sealed record ObservationReason(string Code, int Count);

public sealed record ReadBudget(long MaxBytes, TimeSpan MaxTime, TimeProvider Clock)
{ public static readonly ReadBudget Unbounded = new(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System); }

public static class ObservationReasons               // THE closed vocabulary — E3.S5 reflects over this one class
{
    // unobserved — E3.S2's, re-exposed by const aliasing so one class holds every code
    public const string NoSessionId = CallerTranscriptLocation.NoSessionId;
    public const string StatedId = CallerTranscriptLocation.StatedId;
    public const string TranscriptNotFound = CallerTranscriptLocation.TranscriptNotFound;
    public const string TranscriptAmbiguous = CallerTranscriptLocation.TranscriptAmbiguous;
    public static string NoReaderFor(string vendor) => CallerTranscriptLocation.NoReaderFor(vendor);
    public const string TranscriptEmpty = "transcript empty";               // added: a main file with no parseable timestamped line
    // partial
    public const string AgentCallWithoutTranscript = "agent call without transcript";
    public const string MetaWithoutTranscript = "meta without transcript";
    public const string AgentWithoutMeta = "agent without meta";
    public const string MetaWithoutModel = "meta without model";
    public const string TornLine = "torn line";
    public const string ReadBudget = "read budget";
    public const string InconsistentTranscript = "inconsistent transcript";  // one code, the four triggers below
    public const string SidechainWithoutAgentId = "sidechain without agent id";
}
```

### 3.1 The states, all four distinguishable

| State | When | Example text |
|---|---|---|
| `Observed`, with subagents | read complete | `observed` |
| `Observed`, **zero** | read complete and `Agents` holds only `main` | `observed` — an `IsObservedZero` property says it; it is never "unknown" |
| `Partial` | read on past a gap; each reason with its count | `partial: agent call without transcript ×2, read budget` (`ObservationState.Render()`, never pluralised) |
| `Unobserved` | no transcript read at all | `unobserved: transcript not found` |
| *(empty)* | a round from before E5's step | not E3's: E5 renders *not recorded* |

### 3.2 Reasons are a closed vocabulary

Every `ObservationReason.Code` is a `const` in one static class, `ObservationReasons`, and the only variable part anywhere is a vendor name
from `CallerDeclaration`'s own closed list (`CallerDeclaration.cs:106-108`). Nothing read from a transcript can
become a reason. E3.S5 tests this by reflection.

### 3.3 Identities stay apart (parent §4)

The reader knows a Claude session id and a folder of files. It does not know a gate session, a branch or a
`repo_path`, and it never reads `cwd`. A write's checkout root is a fact about the path. Whether that root is the
gate's repository is E4's question.

### 3.4 Never text

From a transcript line the reader may read: `type`, `timestamp`, `message.id`, `message.model`, the four usage numbers,
`requestedModel`, `effort`, `isSidechain`, `agentId`, a content block's `type`, a `tool_use` block's `name` and `id`,
a `tool_result` block's `tool_use_id` and `is_error`, and from a write tool's `input` ONLY `file_path` /
`notebook_path`. From a meta it may read: `agentType`, `toolUseId`, `parentAgentId`, `spawnDepth`, `model`. From the
registry it reads only `sessionId`. Everything else — `message.content` text, `thinking`, `description`, `prompt`,
`toolUseResult`, any other tool input — is never materialised as a string.

## 4. The stories

Cornerstone mark for this epic: **E3.S4** only (the counting rules and the states, the area the parent named). The
parent's ledger reads `Cornerstones: 3 of 3 — E2.S1, E3.S4, E4.S2` (parent §16 D21). Every other story is briefed
fully below by Opus.

Commands every story uses (repository root; never `dotnet test`):

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug -m:4
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-class "*<TheClass>*"
node .agents/conventions/tools/low-priority.mjs ./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe   # whole suite before done
```

Every story is test-first: write the tests, run them and watch them fail **for the stated reason**, not because
something does not compile. A missing type is a compile error, not a RED: create the empty type first, then watch
the assertion fail. Then implement, watch the tests go green, run each break-it (delete or alter the named production
line, watch the named test fail, restore it), and report both observations.

---

### E3.S1 — The transcript-read helpers lifted out of `AgentLog`, behaviour kept

- **Goal.** One text-free unit for reading a transcript that its CLI is still appending to, shared by `AgentLog` and the
  new reader, so the two cannot drift. `agent_log` behaves exactly as before (parent §3).
- **Scope.**
  - New `src_mcp/src/Store/TranscriptLines.cs` (`internal static class`, ≤ 120 lines).
  - `src_mcp/src/Store/AgentLog.cs`: remove `ReadLines` (`:183-207`), `Days` (`:103-112`) and `Mentions`
    (`:157-173`), and call the new unit. `Parse` (`:209-231`) stays in `AgentLog`, because it builds `AgentLog`'s
    `Entry`, but its JSON step goes through the new `TryParse`.
  - `src_mcp/tests/AgentLogTests.cs:125-133`: the `Days` test moves to the new class unchanged. Nothing else in that
    file changes.
  - New `src_mcp/tests/ATranscriptIsReadWhileItIsBeingWrittenTests.cs`.
- **Brief (Sonnet coder).**
  1. `TranscriptLines.ReadShared(string file) → IEnumerable<string>`: this is `AgentLog.ReadLines` as it stands,
     `FileShare.ReadWrite | FileShare.Delete`. `IOException` or `UnauthorizedAccessException` on open ends the
     sequence silently. Keep the remark that explains why.
  2. `TranscriptLines.TryParse(string line, out JsonDocument document) → bool`: false on `JsonException` (a torn line),
     never throws. The caller owns and disposes the document. Do not pre-read any property here.
  3. `TranscriptLines.Days(DateTime fromUtc, DateTime toUtc)` and `TranscriptLines.Mentions(string line,
     IReadOnlyList<string> days)`: moved verbatim, remarks included. A lower bound is `Days(fromUtc, now)`; it needs
     no overload.
  4. `TranscriptLines.WrittenSince(string file, DateTime fromUtc) → bool`: the `File.GetLastWriteTimeUtc(file) >= fromUtc`
     test from `AgentLog.cs:125`, with `AgentLog.Files` calling it.
  5. `AgentLog` calls all of these. Its public surface (`Slice`, `DefaultProjectsDir`) is unchanged, and so is
     everything it renders.
  6. Style: follow `AgentLog.cs` — remarks that explain why, `private static` pure helpers, no new dependencies.
- **Test scenarios** (`ATranscriptIsReadWhileItIsBeingWrittenTests`):
  - `A_file_held_open_for_writing_by_another_handle_is_still_read`: open the file with `FileShare.Read` and a write
    handle, then read it. Every whole line comes back.
  - `A_file_that_cannot_be_opened_is_no_lines_not_an_exception`: missing file, and a directory passed as a file.
  - `A_torn_line_is_reported_as_torn_and_the_next_whole_line_still_parses`: `TryParse` returns false, then true on the
    next line.
  - `Only_the_days_a_window_touches_are_listed` (moved from `AgentLogTests.cs:125-133`, unchanged assertions).
  - `A_file_last_written_before_the_bound_is_skipped`: set the time with `File.SetLastWriteTimeUtc`.
  - All 14 `AgentLogTests` stay green **unchanged** apart from the moved test. That is the behaviour-kept proof.
- **Break-its.** (a) In `ReadShared`, drop `FileShare.ReadWrite` → the held-open test goes red. (b) Make `TryParse`
  rethrow → the torn-line test goes red, and so does `AgentLogTests.AHalfWrittenLastLineIsSkipped_NotAFailure` (`:173`).
  (c) Make `WrittenSince` return true → the last-write test goes red.
- **Done when.** The new class and all `AgentLogTests` are green; the three break-its were observed red and restored;
  `AgentLog.cs` no longer contains a `FileStream` or a `JsonException` catch of its own; the whole suite is green.
- **Depends on.** Nothing. It may run beside E3.S2 and E3.S3.
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

---

### E3.S2 — Which session and which file, and why none was found

- **Goal.** Turn "who is calling" into either a located transcript or a named reason it cannot be observed. The id
  comes from the Claude session registry, then `CLAUDE_CODE_SESSION_ID`, then a stored id, and the source is recorded.
  The file is found as `<projects>/*/<sessionId>.jsonl` with no slug computed and no recursion.
- **Scope.**
  - New `src_mcp/src/Store/CallerTranscriptLocator.cs` (≤ 250 lines) with `ClaudeHome`, `CallerSessionId`,
    `CallerTranscriptLocation` and the locator.
  - `AgentLog.cs:39-40`: `DefaultProjectsDir => ClaudeHome.Default.ProjectsDir`, so there is one default.
  - **The test home** (parent §16 D17): a test-assembly fixture (the `[assembly: AssemblyFixture]` precedent,
    `src_mcp/tests/TempDirsAreSwept.cs:5`) points `ClaudeHome.Default` — through ONE seam the fixture sets, never by
    rewriting the process's profile variables — at an empty temp directory for the whole run, so neither this reader
    nor `AgentLog` (whose FakeCli round tests read the real `~/.claude/projects` today, `RoundEngine.cs:897`) ever
    opens a developer's transcripts. E5.S4 adds only the clearing of the caller-identity variables on top of it.
  - New `src_mcp/tests/TheCallersTranscriptIsFoundByItsSessionIdTests.cs`.
  - No call site: E5 wires it from `RoundEngine` the way `RoundEngine.cs:897` picks the projects folder.
- **Brief (Sonnet coder).**
  1. `public sealed record ClaudeHome(string ProjectsDir, string SessionsDir)`:
     - `Default` is `<UserProfile>/.claude/projects` and `<UserProfile>/.claude/sessions`.
     - `With(string agentLogDir)` replaces only `ProjectsDir` when `agentLogDir` is non-empty, which is what
       `PanelSettings.AgentLogDir` (`PanelSettings.cs:386`) means. The registry folder does not move.
     - `CLAUDE_CONFIG_DIR` is NOT honoured in this epic (`AgentLog` does not honour it either). Say so in a remark.
  2. `public readonly record struct CallerSessionId(string Id, string Source)` with `const`s `Registry = "registry"`,
     `Environment = "environment"` and `Stored = "stored"`. Add a `None` value whose `Id` is empty.
  3. `CallerTranscriptLocation` is a closed hierarchy:
     - `Found(CallerSessionId Session, string MainFile, string SessionFolder)`. `SessionFolder` is `<dir>/<id>` and may
       not exist.
     - `NotObservable(CallerSessionId Session, string Reason)`.
     - Reason constants: `NoSessionId = "no session id"`, `StatedId = "stated id"` (parent §16 D9),
       `TranscriptNotFound = "transcript not found"`,
       `TranscriptAmbiguous = "transcript in two folders"`, and `NoReaderFor(vendor) => "no reader for " + vendor`.
  4. `CallerTranscriptLocator.Locate(string vendor, Func<string, string?> env, ClaudeHome home)`. Pure apart from the
     file reads named below. In order:
     - If `vendor` is a known vendor that is not `claude` (any value other than `claude`, `unknown` or empty, taken from
       `CallerDeclaration.Vendor`) → `NoReaderFor(vendor)`. Use the vendor text only when it matches `^[a-z][a-z-]{0,19}$`;
       otherwise use `NoReaderFor("this vendor")`.
     - Registry: if `env("CLAUDE_PID")` is 1–10 ASCII digits, read **exactly** `<SessionsDir>/<pid>.json` (shared read,
       refuse files over 64 KiB), parse it, and take `sessionId` only. **Never list `SessionsDir`**: a `<pid>.<hash>.key`
       sits beside every registry file. If the id is valid → source `registry`.
     - Otherwise, if `env("CLAUDE_CODE_SESSION_ID")` holds a valid id → source `environment`. `COAI_CALLER_SESSION`,
       `CODEX_SESSION_ID` and `GEMINI_CLI_SESSION_ID` are not sources here.
     - Otherwise, if `env("COAI_CALLER_SESSION")` is set → `StatedId`: a stated id names no transcript (parent §16 D9).
     - Otherwise → `NoSessionId`.
     - "Valid" means `Guid.TryParseExact(id.Trim(), "D", out _)`. Anything else, including `../x`, is `NoSessionId`, so an
       id is never put into a path unvalidated.
  5. `LocateStored(string id, ClaudeHome home)`: the same validation and lookup with source `stored`. This is E7's entry
     for `rounds.caller` (`RoundCommands.cs:34-35`). A `repo:…` value is `NoSessionId`.
  6. The lookup, for a valid id:
     - One `Directory.EnumerateDirectories(ProjectsDir)` (top level only). Test `File.Exists(<dir>/<id>.jsonl)` in each.
     - One hit → `Found`. None, or `ProjectsDir` missing → `TranscriptNotFound`. Two or more → `TranscriptAmbiguous`;
       never pick one.
     - An `IOException` or `UnauthorizedAccessException` while listing → `TranscriptNotFound`, never a throw.
  7. The registry and the variable disagreeing is the `/clear` case (the variable is inherited stale). The registry wins
     and `Source` says so. Nothing else is recorded about the loser.
- **Test scenarios** (`TheCallersTranscriptIsFoundByItsSessionIdTests`, all in a `TempDir`):
  - `The_registry_wins_over_the_variable_and_says_so`: `<pid>.json` names A, the variable names B, and both files exist
    → `Found` for A, source `registry`.
  - `An_unreadable_or_absent_registry_falls_back_to_the_variable`: no `<pid>.json`; a torn `<pid>.json`; one with no
    `sessionId` → each is B, source `environment`.
  - `The_registry_is_chosen_by_the_pid_never_by_a_listing`: `SessionsDir` holds `<otherpid>.json` naming A and no
    `<pid>.json` → source `environment` (B). A listing implementation would find A.
  - `A_stated_session_alone_is_a_stated_id`: only `COAI_CALLER_SESSION` set → `stated id`, and no file is opened.
  - `A_codex_caller_has_no_reader`: vendor `codex` with a valid Claude variable present → `no reader for codex`. The
    vendor decides first.
  - `An_id_that_is_not_a_uuid_never_reaches_a_path`: `..\\..\\x`, `a/b` and empty → `no session id`, and no file outside
    `ProjectsDir` is opened. Assert the second half with a sentinel file placed where a naive path would land.
  - `The_transcript_is_found_in_any_project_folder_without_a_slug`: a folder name unrelated to any cwd still finds it.
  - `Two_project_folders_holding_the_id_is_ambiguous_not_a_guess`.
  - `Persistence_off_is_transcript_not_found`: the id is valid and no file exists.
  - `COAI_AGENT_LOG_DIR_moves_the_transcripts_not_the_registry`: through `ClaudeHome.With`.
  - `A_stored_id_is_found_with_source_stored`, and `A_repo_caller_is_no_session_id`.
  - `No_test_reads_the_developers_profile` (parent §16 D17): with the fixture in place, `ClaudeHome.Default` and
    `AgentLog.DefaultProjectsDir` both name the temp home; a sentinel transcript planted there is what a default read
    finds.
  - Positive anchor (testing.md, *a file full of negatives needs one positive*): the first test above must assert
    `Found`, so a fixture the locator rejects cannot pass every test.
- **Break-its.**
  - (a) Swap the registry and variable order → the registry test goes red.
  - (b) Replace the pid-named read with a listing → the by-pid test goes red.
  - (c) Drop the uuid check → the path test goes red.
  - (d) Use `CallerIdentity.From` (`CallerSessions.cs:53-64`) as the ladder → the stated-session test goes red. This is
    the refuted approach; name a test for it: `Reusing_the_round_callers_ladder_would_read_a_stated_id_and_that_is_the_point`.
- **Done when.** All scenarios and break-its are observed; `AgentLog.DefaultProjectsDir` delegates; no
  `EnumerateFiles(…, AllDirectories)` in the new file; the whole suite is green.
- **Depends on.** Nothing. It may run beside E3.S1 and E3.S3.
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

---

### E3.S3 — A write is known by its path alone, and by the checkout it landed in

- **Goal.** From one `tool_use` block, decide whether it is a write and, if so, which checkout received it. Read
  nothing but the path. Every other tool, `Bash` and `PowerShell` included, is `Unknown` and never a write (parent §5,
  §13).
- **Scope.**
  - New `src_mcp/src/Store/WriteEvidence.cs` (≤ 200 lines) with `WriteTools`, `WriteTarget` and `CheckoutRoots`.
  - `src_mcp/runners/Worktrees/SubmodulePopulator.cs:133-134`: `HasGit` goes from `internal` to `public`. Extend its
    remark to name the third caller.
  - New `src_mcp/tests/AWriteIsKnownOnlyByItsPathTests.cs`.
- **Brief (Sonnet coder).**
  1. `WriteTools.Names` = `["Edit", "Write", "MultiEdit", "NotebookEdit"]` (ordinal, case-sensitive), plus — parent
     §16 D18 — `Artifact`-style file writers as table entries (tool name → its ONE path property), counted only when
     the path resolves to a `Checkout`; none is added without an E1 fixture showing it on disk.
     `WriteTools.TryPath(string toolName, JsonElement input, out string path)`:
     - `Edit`, `Write` and `MultiEdit` read `file_path`; `NotebookEdit` reads `notebook_path`. Each reads ONE property,
       through `TryGetProperty`, and only when it is `JsonValueKind.String`.
     - Any other name, a missing property or a non-string → false.
     - Never enumerate `input`, and never read `content`, `old_string`, `new_string` or `edits`.
  2. `public sealed record WriteTarget(WriteTargetKind Kind, string Where)` with
     `enum WriteTargetKind { Checkout, OutsideAnyCheckout, Unresolved }`:
     - `Checkout`: `Where` is the checkout root as `RepositoryIdentity.Normalised` (`RepositoryIdentity.cs:24`), a KEY,
       never handed back to the filesystem.
     - `OutsideAnyCheckout`: `Where` is empty. A scratchpad, a temp file or a memory file is not "another repository".
     - `Unresolved`: `Where` is the normalised DIRECTORY of the file when the path was absolute and that directory no
       longer exists (a removed worktree), else empty.
  3. `public sealed class CheckoutRoots` holds a per-instance `Dictionary<string, WriteTarget>` keyed by directory; one
     instance serves one read. `Resolve(string path) → WriteTarget`:
     - Not `Path.IsPathFullyQualified` → `Unresolved("")`. Never resolve against this process's cwd, which is not the
       agent's.
     - The file's directory does not exist → `Unresolved(normalised directory)`. Do NOT walk up from a missing
       directory: a removed worktree would walk up into whatever contains it and answer wrongly.
     - Otherwise walk up from the directory. The FIRST directory where `SubmodulePopulator.HasGit` is true →
       `Checkout(that directory)`. Reaching the root → `OutsideAnyCheckout`.
     - A submodule or a nested checkout therefore resolves to the INNER root. Whether an inner root under the gate's
       `repo_path` counts as "here" is E4's rule; say so in the remark.
     - A linked worktree resolves to the worktree, not to the common dir: two worktrees are two checkouts.
       `RepositoryIdentity.CommonDirArgs` is the wrong tool for this.
  4. No git process, no file content read. Only `Directory.Exists` and `File.Exists`.
- **Test scenarios** (`AWriteIsKnownOnlyByItsPathTests`; build the trees in a `TempDir` with `.git` folders and files;
  no git needed):
  - `Each_write_tool_yields_its_path`: four blocks, four paths. `NotebookEdit` uses `notebook_path`.
  - `A_shell_command_that_writes_a_file_is_not_a_write`: `Bash` with `echo x > f`, `PowerShell` with `Set-Content`, and
    an `mcp__…` tool carrying a `file_path` → all false.
  - `A_path_that_is_not_a_string_is_not_a_write`.
  - `Nothing_but_the_path_is_read`: put a sentinel in `content`, `old_string` and `new_string`; `TryPath` returns only
    the path. Also assert the path itself does not contain the sentinel.
  - `A_write_lands_in_the_nearest_checkout`: `repo/.git/` folder, write at `repo/src/a.cs` → `Checkout(repo)`.
  - `A_worktree_is_its_own_checkout`: `wt/.git` FILE → `Checkout(wt)`.
  - `A_submodule_write_resolves_to_the_submodule_root`: `repo/sub/.git` file → `Checkout(repo/sub)`.
  - `A_scratch_file_is_outside_any_checkout`: first assert the precondition that no `.git` exists above the temp root,
    and fail naming it if one does. Then expect `OutsideAnyCheckout`.
  - `A_write_into_a_removed_worktree_is_unresolved_not_its_parent`: the path's directory is gone, and its parent folder
    has a `.git` two levels up → `Unresolved(dir)`, NOT `Checkout(parent)`.
  - `A_relative_path_is_unresolved_never_joined_to_our_cwd`.
  - `Backslashes_and_case_give_one_key`: `D:\\X\\repo` and `d:/x/repo/` give one `Where`. Windows only:
    `Assert.SkipUnless(OperatingSystem.IsWindows())`.
- **Break-its.**
  - (a) Add `"Bash"` to `Names` → the shell test goes red.
  - (b) Walk up from a missing directory → the removed-worktree test goes red.
  - (c) Test only `Directory.Exists(".git")` → the worktree and submodule tests go red.
  - (d) Resolve relative paths with `Path.GetFullPath` → the relative-path test goes red.
- **Done when.** All scenarios and break-its are observed; `HasGit` is public and is the only `.git` test in the new file;
  the whole suite is green.
- **Depends on.** Nothing. It may run beside E3.S1 and E3.S2.
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

---

### E3.S4 — The counting rules and the states · **Cornerstone — spec by Fable**

> **Spec by Fable, 2026-10-14**, over the parent's §5 and §16 (D3, D4, D6–D9, D17, D18 and the accepted resolutions
> E3/9, E3/11, E4/4), this document's §2–§3, E1.S5's fixture list, `AgentLog.cs` as it stands, and one real transcript
> folder read for field names and counts only (§*What the folder showed*). Sonnet codes it, Opus reviews it. Where this
> spec and §3's code block differ, **this spec wins** and §3 is to be realigned by the main session; every addition to
> the §3 contract is marked *added*. Every downstream figure sums what this story counts, and a double count has no
> symptom — so the rules below are stated to be tested, not interpreted.

- **Goal.** `CallerTranscript.Read(CallerTranscriptLocation where, ReadBudget budget, DateTime? fromUtc) →
  ObservedSession`. It reads the main transcript, every `subagents/agent-<id>.jsonl` and every `.meta.json` under the
  session folder, and counts each message, token, launch and write exactly once. It keeps requested apart from
  observed and unknown apart from any default, and it states which of the four states it reached and why — in a closed
  vocabulary, with nothing read from a transcript ever becoming a reason.
- **Scope.**
  - New `src_mcp/src/Store/ObservedSession.cs` (≤ 150 lines): the contract below, `ObservationReasons`, `ReadBudget`,
    `ReadDiagnostics`.
  - New `src_mcp/src/Store/CallerTranscript.cs` (≤ 250): the entry point, the file order, the budgeted line
    enumerator, the state assembly.
  - New `src_mcp/src/Store/TranscriptMessages.cs` (≤ 150): the per-`message.id` table and the model / effort /
    requested rules.
  - New `src_mcp/src/Store/TranscriptLaunches.cs` (≤ 200): metas, agent files' first lines, parent links, the
    Agent-count check.
  - New `src_mcp/src/Store/TranscriptWrites.cs` (≤ 100): write candidates and their results.
  - New tests: `src_mcp/tests/CallerTranscriptFixture.cs` (the materialiser, reused by E3.S5, E4.S5, E5.S5),
    `src_mcp/tests/SteppingClock.cs` (≤ 15 lines), and the four test classes of §*Tests*.
  - Uses E3.S1 (`TranscriptLines`), E3.S2 (`CallerTranscriptLocation`, `CallerTranscriptLocator.LocateStored`,
    `ClaudeHome`) and E3.S3 (`WriteTools`, `CheckoutRoots`). No call site, no schema, nothing persisted.
  - Complexity ≤ 4 per function (the family's `coding-style`): every rule below is its own `private static` function
    over already-parsed values; the only mutable objects are the per-read `ReadMeter`, the message table and S3's
    `CheckoutRoots`.

#### What the folder showed (2026-10-14; one Claude Code session folder, names and counts only — no content)

- Main-file line `type` values: `assistant`, `user`, `system`, `attachment`, `queue-operation`, `last-prompt`, `ai-title`,
  `atis-latch`, `pr-link`, `file-history-snapshot`, `file-history-delta`. Only `assistant` lines carry `message.id`,
  `message.model`, `message.usage`, `requestedModel` and `effort` (1 703 of 1 703 here); `tool_result` blocks sit in
  `user` lines. The FIRST line of the main file was a `queue-operation` line: a `timestamp`, no message.
- `requestedModel` on main lines is `claude-opus-5-5[1m]`; `message.model` is `claude-opus-5-5`. In the one agent file
  read, both are `claude-sonnet-5-5`; its `effort` is `medium` while main's is `high`; `perTurnEffort` exists on both.
- 686 ids over 1 703 assistant lines (≈ 2.5 lines per id). In THIS folder every id's lines carried identical
  `output_tokens`; the 162-id growing shape of §2.2 belongs to other sessions. The maximum rule costs nothing where
  lines agree and is right where they do not.
- 94 `Agent` tool_uses in main + 2 in agent files = 96 metas = 96 agent files; 2 metas carry `parentAgentId`
  (`spawnDepth` 2). Every agent file's first line is `type: user` with `agentId` equal to the file's id and a
  `timestamp`; every agent-file line is `isSidechain: true`; no main line is.
- `is_error` on `tool_result` blocks: present on 408 of 765, `true` on 19. **Absent means not an error.**
- `message.usage` carries sub-objects (`cache_creation.*`, `iterations[]`) beside the four terms. None is read.
- Neither file had a torn tail at the instant of reading (both end with a newline): a torn tail is a RACE between the
  CLI's append and our read, not a steady state of a live file. This is the measured fact behind the torn-tail rule.
- A `tool-results/` folder of `.txt` files sits beside `subagents/`. It is text; the reader never opens it.
- Some lines carry an object whose KEYS are real file paths (`snapshot.trackedFileBackups`). The reader never
  enumerates object keys anywhere; E1's cutter renames such keys.

#### The contract — §3 with this story's additions

```csharp
// src_mcp/src/Store/ObservedSession.cs
public sealed record ObservedSession(
    CallerSessionId Session,                         // E3.S2
    ObservationState State,
    DateTime? FirstLineUtc,                          // the main file's first parseable line WITH a timestamp, any type
    IReadOnlyList<ObservedAgent> Agents,             // "main" first, then by launch
    IReadOnlyList<ObservedMessage> Messages,         // one per message.id, in range of fromUtc
    IReadOnlyList<ObservedWrite> Writes,             // one per ANSWERED, non-error write call
    IReadOnlyList<UnobservedLaunch> UnobservedLaunches, // added: the Agent calls and metas with no transcript, with instants
    ReadDiagnostics Diagnostics)                     // added: counts only, never a string
{
    public const int ReaderVersion = 1;              // bumped by ANY change to a counting rule (E5's authors_reader)
    public bool IsObservedZero => State.Kind == ObservationKind.Observed && Agents.Count == 1;
}

public sealed record ObservedAgent(string Key /* "main" | agentId (from the FILE name) */, string ParentKey /* "main" | agentId | "" */,
    string ToolUseId, string AgentType, string RequestedAlias /* meta.model | "" */,
    DateTime? LaunchedUtc /* the agent file's first parseable line; null for main and for a meta without a file */,
    DateTime? CalledUtc   /* added: the launching Agent call's message instant, when that call was parsed */,
    bool TranscriptPresent /* added: false for a meta whose agent-<id>.jsonl is missing */);

public sealed record ObservedMessage(string AgentKey, string MessageId, DateTime Utc /* earliest line */,
    string Requested /* meta alias, else the earliest line's requestedModel, else "" */, string Model /* message.model */,
    string Effort /* the earliest line's effort, else "unknown" */, TokenUsage Tokens /* per-term MAX over the id's lines */,
    bool UsageSeen /* added: false when no line of the id carried a usage object; Tokens are then zeros, not evidence */);

public sealed record TokenUsage(long Input, long CacheCreation, long CacheRead, long Output);
public sealed record ObservedWrite(string AgentKey, DateTime Utc /* its message's instant */, string Tool, WriteTarget Target);
public sealed record UnobservedLaunch(string ParentKey, string ToolUseId, DateTime? Utc, string Reason); // added
public sealed record ReadDiagnostics(int FilesRead, long CharsRead, int LinesSkippedByDayGate, int TornTails,
    int SyntheticLines, int WriteCallsRefused, int WriteCallsUnanswered, int MessagesWithoutUsage);    // added

public sealed record ObservationState(ObservationKind Kind, IReadOnlyList<ObservationReason> Reasons)
{ public string Render() => …; }   // "observed" | "partial: <code>[ ×N][, <code>…]" | "unobserved: <code>"
public enum ObservationKind { Observed, Partial, Unobserved }
public sealed record ObservationReason(string Code, int Count);

public sealed record ReadBudget(long MaxBytes, TimeSpan MaxTime, TimeProvider Clock)
{ public static readonly ReadBudget Unbounded = new(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System); }

public static class ObservationReasons               // THE closed vocabulary — E3.S5 reflects over this one class
{
    // unobserved — E3.S2's, re-exposed by const aliasing so one class holds every code
    public const string NoSessionId = CallerTranscriptLocation.NoSessionId;
    public const string StatedId = CallerTranscriptLocation.StatedId;
    public const string TranscriptNotFound = CallerTranscriptLocation.TranscriptNotFound;
    public const string TranscriptAmbiguous = CallerTranscriptLocation.TranscriptAmbiguous;
    public static string NoReaderFor(string vendor) => CallerTranscriptLocation.NoReaderFor(vendor);
    public const string TranscriptEmpty = "transcript empty";               // added: a main file with no parseable timestamped line
    // partial
    public const string AgentCallWithoutTranscript = "agent call without transcript";
    public const string MetaWithoutTranscript = "meta without transcript";
    public const string AgentWithoutMeta = "agent without meta";
    public const string MetaWithoutModel = "meta without model";
    public const string TornLine = "torn line";
    public const string ReadBudget = "read budget";
    public const string InconsistentTranscript = "inconsistent transcript";  // one code, the four triggers below
    public const string SidechainWithoutAgentId = "sidechain without agent id";
}
```

`Render()` never pluralises: a count above one is written `agent call without transcript ×2`. §3.1's example text
(`2 agent calls without transcript`) is superseded — grammar over a closed vocabulary is text manipulation, and E3.S5
checks that every code is a `const`. E5 renders the card from the same method.

**Mapping onto E4's core records** (E4.S1, `CoaiMcp.Core.Authors`): `FirstLineAt = LaunchedUtc ?? CalledUtc`;
`TranscriptPresent` as is; E4's `UnobservedLaunch` list is `UnobservedLaunches` as is; `Effort` `"unknown"` as is;
`TokenTerms` from `TokenUsage` term by term. Nothing in the mapping decides anything.

#### Decisions this spec closes

| # | Decision | Why |
|---|---|---|
| S4-1 | **A torn LAST line is not `partial`.** The last line a file yields that fails `TryParse` is a *torn tail*: counted in `Diagnostics.TornTails`, no reason recorded, the state unaffected. Any OTHER unparseable line is `partial: torn line`. The policy is one named constant, `CallerTranscript.TornTailCountsAsPartial = false`; E1.S4 measures how often a live read sees a tail, and the constant is flipped — with a `ReaderVersion` bump — only if the measurement shows tails that are NOT transient (see the remark the constant must carry) | A torn tail is the CLI's append racing our read (observed: both files whole at the instant of reading; `AgentLog.cs:229` calls it normal because `agent_log` reads during the main agent's own turn). The bytes it holds belong to a line being written NOW — at round completion, after round N's start — so under the evidence cutoff (parent §16 D6) that line is never evidence for the round being attributed. Marking most live rounds `partial` for a line that could not have counted would make `partial` useless as a signal. A torn line in the MIDDLE of a file is corruption, not a race, and lines after it were lost to nothing we can name → partial. The one case the rule reads as a tail wrongly — a dead session whose final line was never finished — loses one line after every round of that session |
| S4-2 | **The in-flight message is counted from the lines seen.** The torn tail's `message.id` is unknowable (it does not parse); the preceding parsed lines of whatever message is in flight stay counted at their per-term maximum | Dropping "the last parsed message" would be a guess that is wrong whenever the tail is a NEW message's first line. Evidence, not inference. Stated limitation: for a background agent still writing at the read instant, a live read and a later backfill can differ by that ONE message's output tokens; E7's idempotence check (backfill vs backfill) is unaffected |
| S4-3 | **A budget hit drops the id of the line it stopped at** and skips every cross-file consistency check (step 7) | Acceptance 11: no message counted from some of its lines. And after a budget stop, unread metas and files would read as "agent call without transcript" — a false reason. `read budget` already says the picture is incomplete |
| S4-4 | **The file wins over a line's `agentId`**, and the disagreement is `partial: inconsistent transcript` | The file is the unit of launch; a line that claims another agent makes the per-agent token figure uncertain, which is what `partial` is for (principle below) |
| S4-5 | **`ParentKey` = the file holding the `toolUseId` join, else `parentAgentId`, else `"main"` when a meta exists, else `""`.** The join and `parentAgentId` disagreeing is `partial: inconsistent transcript` | `parentAgentId` is present only on nested metas (2 of 96); its absence on a meta IS the meta's statement that main launched it — a fact read, not a guess. Under a `fromUtc` bound the launching call may sit in a skipped line, so the meta's own statement must be enough |
| S4-6 | **An agent file with no meta is `partial: agent without meta`**; its launch counts, `RequestedAlias` and `AgentType` are empty, `ParentKey` is `""`, its messages' `Requested` falls back to the line's `requestedModel` | The parent link is an input to E4's rule 2 (child-of association); losing it changes a number. A Claude Code version that wrote no metas makes every session of that version partial — correct and honest, and E1.S4 Q2/Q7 say whether such a version is on disk |
| S4-7 | **A write call whose `tool_result` carries `is_error: true` is not a write; a write call with no `tool_result` is not a write either.** Both are counted (`WriteCallsRefused`, `WriteCallsUnanswered`). **Deviation from acceptance 8** ("every write-tool tool_use becomes one write") | A hook-refused `Edit` (the family's guards refuse writes outside their folders), an `old_string` not found, a missing file — none changed a byte, and counting them would make an agent that was refused every write `wrote here`. An unanswered call (interrupted, or torn at the tail) has an unknown outcome and is no evidence. Evidence of contribution is a write that happened |
| S4-8 | **Every block's instant is its message's instant** (the id's earliest line): writes, Agent calls, tool results | Keeps a write in the same window as the message that made it (E4 places both by instant); a message's blocks can be minutes apart on a long thinking turn |
| S4-9 | **The day gate is widened by `StraddleGuard = 1 h` below the bound**: `Days(fromUtc − 1 h, max(now, fromUtc))`; the instant test is still exact | Acceptance 12 needs the EARLIEST line of a message to be parsed to know the message started before the bound. A message that begins on day D−1 and continues past a bound set in the first minutes of day D would otherwise be counted by its tail — its first line skipped unparsed by the day gate. One message never spans an hour; `max(now, fromUtc)` is because `TranscriptLines.Days` throws on a negative span (a fixture dated after the test clock) |
| S4-10 | **A main file with no parseable timestamped line is `unobserved: transcript empty`** | Nothing was read; "observed zero" would claim a complete read of nothing |
| S4-11 | **A `message.model` that is empty or begins with `<` is a synthetic line**: no message, no tokens, `Diagnostics.SyntheticLines++`, not partial | Acceptance 15. The CLI writes `<synthetic>` for an interrupted or failed call; nothing was spent and nothing was lost |
| S4-12 | **A main-file line with `isSidechain: true`** belongs to the agent named by its `agentId`; without one it is dropped and `partial: sidechain without agent id` | The pre-`subagents/` layout kept subagent lines in the main file. Counting them as main's would misattribute every subagent token; the rule is stated so an older fixture (E1.S4 Q9) has one |
| S4-13 | **The in-range test is `earliestLine >= fromUtc`**; `fromUtc == null` reads everything; `Agents`, `UnobservedLaunches` and `FirstLineUtc` are never filtered by the bound; writes follow their message | Acceptance 12; a launch belongs to the window of its first line and E4 needs every launch to tile windows |
| S4-14 | **Model, Requested and Effort come from the id's EARLIEST line**; a later line of the same id differing on any of the three is `partial: inconsistent transcript` | One API response has one model and one effort; a difference means the layout assumption broke and a counted value is uncertain |

**The principle behind `partial`**: a reason is recorded when something was LOST (a line, a child, a parent link) or a
COUNTED VALUE is uncertain; a diagnostics count is enough when the reader could choose between readings and lost
nothing (a torn tail, a synthetic line, a refused write).

#### The read algorithm

0. **Unobservable location.** `where` is `NotObservable(session, reason)` → `Unobserved(reason)`, `Session` carried
   through, every list empty, `FirstLineUtc` null, diagnostics zero. **Nothing is opened.**
1. **The meter.** `ReadMeter(budget)` records `started = Clock.GetUtcNow()`. Every line yielded by
   `TranscriptLines.ReadShared` adds `line.Length` to `CharsRead` (UTF-16 chars — a line's UTF-8 bytes are at least
   that; `MaxBytes` is a ceiling against a runaway read, not an accounting, and its remark says so). After EVERY line:
   `Exhausted = CharsRead > MaxBytes || Clock.GetUtcNow() − started > MaxTime`. On exhaustion the enumeration ends,
   the `message.id` of the line it stopped at is removed from the table (S4-3), `ReadBudget` is recorded once, and
   step 7's checks are skipped.
2. **The day gate.** `days = fromUtc is null ? [] : TranscriptLines.Days(fromUtc − StraddleGuard, max(now, fromUtc))`.
   A line that fails `TranscriptLines.Mentions(line, days)` is skipped unparsed (`LinesSkippedByDayGate++`) — except
   the FIRST line of every file, which is always parsed.
3. **The main file**, line by line: `TryParse`; a torn line → step 5. The first parseable line with a parseable
   `timestamp` sets `FirstLineUtc` whatever its `type`. Then by `type`: `assistant` → step 6 and the block scan (steps 7a,
   8); `user` → the block scan for `tool_result` only; every other type → nothing more. A `timestamp` is parsed as
   `AgentLog.Parse` does (`AdjustToUniversal | AssumeUniversal`); an `assistant` line without one is
   `inconsistent transcript` and dropped.
4. **The agent files.** `<SessionFolder>/subagents/agent-*.jsonl` and `agent-*.meta.json`, each list sorted ordinal; a
   missing folder means a main-only session. Key = the name between `agent-` and the extension. Per file: the first line
   is parsed always → `LaunchedUtc`, and its `agentId` must equal the key (else `inconsistent transcript`); if
   `fromUtc` is set and `!TranscriptLines.WrittenSince(file, fromUtc)` the file ends there; otherwise it is read as
   step 3 with the file's key as the agent. The main file is read first, then agent files in name order — the order a budget hit truncates in.
5. **Torn lines** (S4-1): `TryParse` false. The LAST line a file yielded → `TornTails++`, read ends normally. Any other
   → `TornLine` count++, read on.
6. **The message table** (`TranscriptMessages`): per `message.id`, across every file — earliest `timestamp`; the
   agent key (the file's; S4-4, S4-12); `Model`, `Requested`, `Effort` from the earliest line (S4-14; `Effort` is the
   line's `effort` when it is a non-empty string, else `unknown`; `perTurnEffort` is never read; `Requested` for a
   subagent is replaced by the meta's alias in step 7 when a meta with `model` exists); `Tokens` as the per-term
   maximum over the id's lines, a missing term reading 0; `UsageSeen` true once any line carried a `usage` object. A
   synthetic line (S4-11) is not absorbed. The table is keyed by id alone, so an id seen in two files is one message.
7. **Launches, parents, the Agent-count check** (`TranscriptLaunches`; skipped after a budget hit):
   - `Agents` = `main` + every agent FILE key + every META key that has no file (`TranscriptPresent = false`,
     `LaunchedUtc = null`).
   - From a meta: `agentType`, `toolUseId`, `parentAgentId`, `model` → `RequestedAlias`. No `model` →
     `MetaWithoutModel`. A file with no meta → `AgentWithoutMeta` (S4-6). `spawnDepth` is not needed and not read.
   - `ParentKey` per S4-5. `CalledUtc` = the message instant of the parsed `Agent`/`Task` tool_use whose `id` equals
     the meta's `toolUseId`, else null.
   - 7a. The check, over every `tool_use` block named `Agent` or `Task` parsed in any file, in range or not: joined to
     a meta by `toolUseId` → *launched*. Else its `tool_result` (same file, `tool_use_id` == the block's `id`) has
     `is_error: true` → *refused*: nothing recorded. Else → *missing*: `AgentCallWithoutTranscript` count++ and
     `UnobservedLaunch(the file's key, id, the call's instant, AgentCallWithoutTranscript)`. A meta with no file →
     `MetaWithoutTranscript` count++ and `UnobservedLaunch(ParentKey, ToolUseId, CalledUtc, MetaWithoutTranscript)`.
8. **Writes** (`TranscriptWrites`, S4-7, S4-8): in an `assistant` line, each `tool_use` block for which
   `WriteTools.TryPath(name, input, out path)` is true is a candidate `(agentKey, messageInstant, tool, path, id)`. When
   the file is done: no `tool_result` for `id` → `WriteCallsUnanswered++`; `is_error: true` → `WriteCallsRefused++`;
   else `ObservedWrite(agentKey, instant, tool, CheckoutRoots.Resolve(path))`. ONE `CheckoutRoots` per read. Only a
   candidate whose message is in range (S4-13) becomes a write.
9. **The state**: `Unobserved(TranscriptEmpty)` when the main file yielded no parseable timestamped line (S4-10);
   else `Partial(reasons)` when any count is above zero, reasons ordered by code (ordinal); else `Observed`.
10. **Ordering** (acceptance 14): `Agents` — `main`, then by (`LaunchedUtc ?? CalledUtc ?? DateTime.MaxValue`, Key
    ordinal); `Messages` — (Utc, MessageId ordinal); `Writes` — (Utc, AgentKey ordinal, position in its file);
    `UnobservedLaunches` — (Utc ?? MaxValue, ToolUseId ordinal).

What is read from a line is exactly §3.4's list plus `isSidechain` as a boolean; `cwd`, `gitBranch`, `attachment`,
`toolUseResult`, `message.content[].input` beyond the one path property, every `text`/`thinking`, and every object
key outside `message.content[]` are never materialised. `tool-results/` is never opened. `sessions/` is E3.S2's.

#### Worked examples — E1.S5 fixture → `ObservedSession`

`M`, `A`, `K`, `C` are the fixture manifest's `counts` (`distinctMessageIds`, `agentFiles`, `metas`, `agentToolUses`);
`Unbounded`, `fromUtc = null`, the location from `CallerTranscriptLocator.LocateStored(id, home)` unless said otherwise.

| Fixture (or mutation) | State | Agents | Messages / tokens | Launches, parents | Writes | Also |
|---|---|---|---|---|---|---|
| `message-as-several-lines` | `observed` | `1 + A` | `M` messages, each id once; `Tokens` = per-term max of its lines (= the last line); `Output` < the sum over lines for every multi-line id | — | — | `FirstLineUtc` = the first line's stamp |
| `model-switch-in-window` | `observed` | — | one agent key has messages with two distinct `Model` values; `Requested` unchanged across the switch | — | — | — |
| `resumed-agent` | `observed` | the resumed key appears ONCE, `LaunchedUtc` = its first line | its messages span the gap in `rounds.json` | one launch | — | — |
| `background-agent-across-round-start` | `observed` | one key, `LaunchedUtc` before the round start in `rounds.json` | messages on both sides of that start | one launch | — | E4 splits them; E3 does not |
| `nested-agents` | `observed` | depth-1 keys: `ParentKey = "main"`; the nested key: `ParentKey` = its parent's key, `CalledUtc` = the parent's call instant | — | `C` launched; `UnobservedLaunches` empty | — | — |
| `missing-child-transcripts` | `partial: agent call without transcript, meta without transcript` | the meta-only key present with `TranscriptPresent = false`, `LaunchedUtc = null`, `CalledUtc` set | `M` | `UnobservedLaunches.Count == 2`, each with its instant and reason | — | reading went on: every other agent and message is present |
| `refused-agent-call` | `observed` | the refused call has no agent | — | `A` launches, not `C`; `UnobservedLaunches` empty | — | the break-it's target |
| `meta-without-model` | `partial: meta without model` | that key's `RequestedAlias == ""` | that key's messages: `Requested` = the lines' `requestedModel`, `Model` from the lines | — | — | — |
| `no-effort-lines` | `observed` | — | every message of the stripped lines has `Effort == "unknown"`; none `high` | — | — | — |
| `write-into-submodule-and-scratchpad` | `observed` | — | — | — | one `Checkout(<submodule root>)`, one `OutsideAnyCheckout`; no `Tool == "Bash"` | roots re-pointed by the materialiser |
| `writes-only-elsewhere` | `observed` | — | — | — | every `Target.Kind == Checkout` with `Where` = the other root | — |
| `two-repositories-one-session` | `observed` | — | — | — | two distinct `Where` values | — |
| `persistence-off` (no transcript) | `unobserved: transcript not found` (from S2) | empty | empty | — | — | `FirstLineUtc == null`; nothing opened |
| `torn-last-line` (if E1 ships it) and *mutation: last line truncated, no newline* | `observed` | — | the in-flight message, if any, counted from its parsed lines | — | — | `Diagnostics.TornTails == 1` (S4-1) |
| *mutation: a MIDDLE line truncated* | `partial: torn line` | — | `M − 1` or `M` (the torn line's id may survive on its other lines) | — | — | lines after it are read |
| *mutation: a meta deleted* | `partial: agent without meta` | that key: `ParentKey == ""`, `RequestedAlias == ""` | its `Requested` = the lines' `requestedModel` | the launch counts | — | — |
| *mutation: an assistant line copied from main into an agent file, same id* | `partial: inconsistent transcript` (agentId ≠ file) | — | that id ONCE, agent = the main file | — | — | the break-it's target for per-file dedup |
| *mutation: `output_tokens` growing across one id's lines* | `observed` | — | that id's `Output` = the largest line's value | — | — | the 162-id shape |
| *mutation: `message.model` = `<synthetic>` on one line* | `observed` | — | that line absent; `SyntheticLines == 1` | — | — | — |
| *mutation: an `Edit`'s result given `is_error: true`; another's result deleted* | `observed` | — | — | — | neither is in `Writes`; `WriteCallsRefused == 1`, `WriteCallsUnanswered == 1` | S4-7 |
| *mutation: `subagents/` removed* | `observed` | only `main` | main's messages | — | — | `IsObservedZero == true` |
| *mutation: every `cwd` rewritten to another fixture root* | identical to the unmutated read, member for member | | | | | the cwd test |
| *mutation: main file emptied / only a torn line* | `unobserved: transcript empty` | empty | empty | — | — | S4-10 |
| `message-as-several-lines`, `fromUtc` strictly between a message's first and second line | `observed` | — | that id ABSENT; every id whose first line ≥ `fromUtc` present whole | all launches present | — | S4-13 |
| any fixture, `MaxBytes` = the char offset of the middle of a multi-line message | `partial: read budget` | only what was read | the cut id absent; every present id complete | no `agent call without transcript` reason | — | S4-3 |

#### Tests

RED first: the coder creates `CallerTranscript.Read` returning `Observed`, empty lists, `FirstLineUtc = null`, so every
test below fails on its ASSERTION (`Expected Messages to contain M item(s), but found 0`, `Expected Kind to be Partial
but found Observed`), never on a compile error. Fixtures are materialised by `CallerTranscriptFixture.Materialise(case,
TempDir)`: it copies `fixtures/caller-transcripts/<case>/projects/**` into `<home>/projects/`, creates one directory with
a `.git` marker per fixture root named in the manifest and rewrites that root's prefix in every `fixture-path` value
(ordinal, forward slashes, so the JSON stays valid), returns the `ClaudeHome` and the `rounds.json`. Its mutation helpers
— `TruncateLastLine`, `TruncateLine(n)`, `DeleteMeta(id)`, `DeleteAgentFile(id)`, `RemoveMetaKey(id, key)`,
`RewriteEveryCwd(root)`, `SetModelOnLine(n, value)`, `SetResultError(toolUseId)`, `DeleteResult(toolUseId)`,
`CopyLineInto(n, agentId)`, `GrowOutputAcross(id)` — are the ONLY way a test alters a fixture, so E3.S5 can enumerate
the mutations it must cover (E3/11). `SteppingClock(start, step)` is a `TimeProvider` that advances `step` per
`GetUtcNow()` call — `FixedClock` is read-only by design and the time budget needs a clock that moves mid-read.

**`TheCallerTranscriptCountsEachThingOnceTests`** (fixture → guarantee; RED = the count or value the stub cannot give):

1. `A_message_written_as_several_lines_is_one_message_with_its_final_usage` — `message-as-several-lines`; `Messages.Count == M`; for a multi-line id, `Tokens` = the per-term max over its raw lines (the test computes it from the fixture).
2. `Summing_every_line_of_a_message_counts_its_output_again_and_that_is_the_point` — same fixture; for each multi-line id, the test sums `output_tokens` over the raw lines and asserts `Output < sum` and `Output == max`. The refuted approach, named.
3. `A_message_whose_output_grows_across_its_lines_is_counted_by_its_largest_line` — `GrowOutputAcross`; `Output` = the last line's.
4. `The_same_message_id_in_two_files_is_one_message_and_the_file_wins` — `CopyLineInto`; the id once, `AgentKey == "main"`, state `partial: inconsistent transcript ×1`.
5. `A_resumed_agent_is_one_launch_with_later_messages` — `resumed-agent`; one `ObservedAgent` with that key; its messages span the gap.
6. `A_background_agent_running_across_a_round_start_is_one_launch` — `background-agent-across-round-start`; one launch; messages before and after the `rounds.json` start.
7. `Requested_and_observed_are_two_fields_and_mains_1m_suffix_shows_it` — any fixture with main lines; `Requested` ends with `[1m]`, `Model` does not, `Model != Requested`.
8. `A_subagents_requested_model_is_the_metas_alias` — `nested-agents`; `Requested` of a subagent message equals its meta's `model` and is not `Model`.
9. `A_model_switch_inside_one_agent_yields_messages_of_both_models` — `model-switch-in-window`.
10. `No_effort_on_a_line_is_unknown_not_high` — `no-effort-lines`.
11. `Effort_is_never_carried_from_the_previous_line` — mutation: alternate lines with and without `effort`; the stripped ones read `unknown`.
12. `A_nested_agent_names_its_parent_and_a_depth_one_agent_names_main` — `nested-agents`; `CalledUtc` set on both.
13. `A_write_call_is_one_write_at_its_messages_instant_in_its_checkout` — `write-into-submodule-and-scratchpad`; `Checkout(submodule)` + `OutsideAnyCheckout`; `Utc` = the message's earliest line, not the block's.
14. `A_shell_command_is_never_a_write` — any fixture with `Bash`; no `Tool` outside `WriteTools.Names`.
15. `A_refused_or_unanswered_write_is_not_a_write` — `SetResultError`, `DeleteResult`; `Writes` shorter by two, both diagnostics counts 1 (S4-7).
16. `A_synthetic_line_is_no_message_and_no_tokens` — `SetModelOnLine(n, "<synthetic>")`.
17. `Reading_the_same_files_twice_gives_equal_sessions_in_a_stable_order` — two reads, `Should().BeEquivalentTo(…, strict ordering)`; and the order rule itself: `Agents[0].Key == "main"`, agents ascending by launch, messages by (Utc, id).
18. `Rewriting_every_cwd_changes_nothing` — `RewriteEveryCwd`; member-for-member equality with the unmutated read.

**`TheCallerTranscriptSaysWhatItDidNotSeeTests`**:

19. `An_observed_session_with_no_subagents_is_observed_zero` — `subagents/` removed; `IsObservedZero`.
20. `Every_agent_call_is_launched_refused_or_missing` — `missing-child-transcripts`: `Partial` with `AgentCallWithoutTranscript ×1` and `MetaWithoutTranscript ×1`; `UnobservedLaunches.Count == 2` with instants; the other agents all present. The positive anchor of this class.
21. `A_refused_agent_call_is_not_a_launch_and_not_partial` — `refused-agent-call`: `Observed`; `Agents.Count == 1 + A`.
22. `A_meta_without_its_transcript_is_listed_as_not_present` — `missing-child-transcripts`: that key `TranscriptPresent == false`, `LaunchedUtc == null`, `CalledUtc != null`.
23. `An_agent_file_without_a_meta_is_a_launch_with_no_parent_and_partial` — `DeleteMeta`; `ParentKey == ""`, `partial: agent without meta`.
24. `A_meta_without_model_is_partial_and_the_model_still_comes_from_the_lines` — `meta-without-model`.
25. `A_torn_middle_line_is_partial_and_the_rest_is_read` — `TruncateLine(n)` with n before the last; `partial: torn line ×1`; lines after n counted.
26. `A_torn_last_line_is_a_tail_not_partial` — `TruncateLastLine`; `Observed`; `Diagnostics.TornTails == 1` (S4-1). Also over `torn-last-line` when E1 ships it.
27. `A_line_whose_agent_id_disagrees_with_its_file_counts_to_the_file_and_marks_the_transcript_inconsistent` — the first-line `agentId` of an agent file rewritten.
28. `A_parent_link_that_contradicts_the_meta_marks_the_transcript_inconsistent` — `nested-agents`, the nested meta's `parentAgentId` pointed at another agent; `ParentKey` = the join's file; `inconsistent transcript ×1`.
29. `A_main_transcript_with_no_parseable_line_is_unobserved_transcript_empty` — main file replaced by one torn line; empty lists.
30. `An_unobservable_location_is_carried_through_and_nothing_is_opened` — a `NotObservable(StatedId)` with a sentinel transcript present in the home; `Unobserved(StatedId)`, `FirstLineUtc == null`, `Diagnostics.FilesRead == 0`.
31. `The_read_stops_at_the_byte_budget_and_says_so` — `MaxBytes` = the char offset into a multi-line message (computed from the raw file); `partial: read budget`; the cut id absent; every present id has all its lines (the test checks each present id's raw line count against the lines before the cut).
32. `The_read_stops_at_the_time_budget_and_says_so` — `SteppingClock(start, 1 s)` with `MaxTime = 2 s`; `partial: read budget` after the third line.
33. `A_budget_hit_never_invents_a_missing_transcript` — budget exhausted inside the main file of `nested-agents`; the reasons are exactly `[read budget]`.
34. `A_sidechain_line_in_the_main_file_belongs_to_its_agent_id` — mutation: a main line given `isSidechain: true` and an `agentId`; its message's `AgentKey` is that id; without the `agentId` → `partial: sidechain without agent id`.
35. `Rendering_a_state_never_pluralises` — `Render()` of two reasons with counts 1 and 2 → `partial: agent call without transcript, meta without transcript ×2`; of `Observed` → `observed`.

**`TheCallerTranscriptReadsFromTheBoundTests`**:

36. `A_message_straddling_the_bound_is_not_counted_by_its_tail` — `fromUtc` strictly between a message's first and second line; absent.
37. `A_message_at_or_after_the_bound_is_counted_whole` — the positive twin; `fromUtc` equal to a first line's stamp; present with all terms.
38. `A_message_straddling_midnight_and_the_bound_is_not_counted_by_its_tail` — mutation: a two-line message stamped `23:59:50` and `00:00:10`, `fromUtc = 00:00:05`; absent (S4-9; red when the guard is dropped).
39. `An_agent_file_last_written_before_the_bound_yields_its_launch_and_no_messages` — `File.SetLastWriteTimeUtc(file, fromUtc − 1 h)`; the agent in `Agents` with `LaunchedUtc`, none of its messages.
40. `The_first_line_instant_is_read_under_any_bound` — `fromUtc` after every line; `FirstLineUtc` unchanged, `Messages` empty, `Agents` complete.
41. `A_bound_after_the_clock_does_not_throw` — `FixedClock` dated before the fixture, `fromUtc` after it; a normal read (S4-9's `max`).
42. `Launches_and_unobserved_launches_are_never_filtered_by_the_bound` — `missing-child-transcripts` with `fromUtc` past every line; `UnobservedLaunches.Count == 2`, `Agents` complete.

**`TheReaderVersionIsPinnedToTheFixturesTests`**:

43. `The_reader_version_is_pinned_to_the_fixtures_totals` — over EVERY fixture (enumerated from the manifest, count asserted > 0): Σ messages, Σ each token term, Σ launches, Σ writes, and the state kind per fixture, each a constant the coder fills from the first green run after hand-checking against the manifest's `counts`. The failure message reads: *a counting rule changed — bump `ObservedSession.ReaderVersion` and update these totals in the same change*.

#### Break-its (plant the mutation, watch the named test go red, restore, report both messages)

| Plant (production code) | Red test(s) |
|---|---|
| Sum every line's usage per id | 1, 2 |
| Take the first line's usage | 3 |
| Key the message table by (file, id) | 4 |
| Count a launch per `WrittenSince` pass, per reopen, or per message gap | 5, 6 |
| `Effort` defaults to `"high"`, or to the agent's previous line | 10, 11 |
| `Requested = Model` when the meta has no alias | 7, 24 |
| A refused `Agent` call recorded as `AgentCallWithoutTranscript` | 21 |
| A missing `Agent` call (no result, no meta) recorded as refused | 20 |
| `earliest >= fromUtc` replaced by "any line ≥ fromUtc" | 36 |
| Drop `StraddleGuard` from the day gate | 38 |
| Return `Observed` after a budget hit | 31, 32 |
| Run step 7 after a budget hit | 33 |
| `TornTailCountsAsPartial = true` | 26 |
| A middle torn line counted as a tail | 25 |
| Count a write whose result is `is_error: true` | 15 |
| A write stamped with its block's line instead of its message's instant | 13 |
| Read `cwd` into any member | 18 |
| `Unobserved(TranscriptEmpty)` replaced by `Observed` | 29, and 19 must still be green |
| Skip the first line of a file under the bound | 40, 39 |
| `ParentKey` from `parentAgentId` before the join | 28 |
| Ordinal file order replaced by `EnumerateFiles` order | 17 (on a platform whose enumeration order is not ordinal; on Windows NTFS it is — the summary says which was observed) |

- **Done when.** Every numbered test is green over E1's fixtures and the listed mutations through
  `CoaiMcp.Tests.exe`; every break-it row was observed red and restored, with both messages in the summary; the
  contract in §3 of this document is realigned to the block above by the main session; `ObservationReasons` holds
  every code the reader can produce (E3.S5 will reflect over it); `ReaderVersion == 1` with its totals filled; no
  `Directory.EnumerateFiles(…, AllDirectories)`, no `cwd`, no `description`, no `prompt`, no `toolUseResult` in any new
  file; the whole suite is green.
- **Depends on.** E1.S5 (fixtures and the manifest), E1.S4 (RESULTS: Q2 effort/meta presence per version, Q3 the usage
  rule, Q7 the mismatch kinds, Q9 the key sets — and the torn-tail frequency that may flip S4-1), E3.S1, E3.S2, E3.S3.
- **Model.** Spec (scenarios + brief): **Fable** (this entry) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

---

### E3.S5 — Nothing leaves the reader as text, and the reader is documented

- **Goal.** An independent guard, designed by the judge and not by the cornerstone's spec writer. Over every E1 fixture,
  no string anywhere in an `ObservedSession` carries a byte of text, and every reason is from the closed vocabulary.
  `research/module_server.md` describes the reader as it is.
- **Scope.**
  - New `src_mcp/tests/NoTextLeavesTheCallerReaderTests.cs`.
  - `research/module_server.md`: a new subsection right after the `rounds.agent_log` paragraphs (`:3007-3021`). No
    product code.
- **Brief (Sonnet coder).**
  1. **Fixtures are enumerated, never listed.** Find every fixture folder E1 created under `src_mcp/tests/fixtures/`
     (the folder name is in E1's RESULTS file) with `Directory.EnumerateDirectories`. Assert the count is greater than
     zero AND equals the number E1's own sentinel test enumerates. That is the companion that keeps the scan alive
     (testing.md, *a structural test that matches nothing passes forever*).
  2. **The sentinel is E1's constant**, referenced and never re-typed. A copy would be a second list that drifts.
  3. **The positive half first.** For each fixture, assert the RAW bytes contain the sentinel at least once. This proves
     the fixture would leak if the reader read text.
  4. **The guard.** Materialise each fixture through E3.S4's `CallerTranscriptFixture.Materialise(case, TempDir)` — the
     one materialiser, never a second — and read it with `ReadBudget.Unbounded`. Walk the
     resulting graph by reflection: every public instance property, recursing into records and `IEnumerable`, and
     collecting every `string`. Assert none contains the sentinel. Do the same for `ObservationState`'s rendered text
     and for `ObservedSession.ToString()`.
  5. **The vocabulary.** Collect every `ObservationReason.Code` produced over all fixtures and every mutation helper of
     `CallerTranscriptFixture` (`TruncateLastLine`, `TruncateLine(n)`, `DeleteMeta`, `DeleteAgentFile`, `RemoveMetaKey`,
     `RewriteEveryCwd`, `SetModelOnLine`, `SetResultError`, `DeleteResult`, `CopyLineInto`, `GrowOutputAcross` — found by
     reflection, so a helper added later is covered without editing this test). Assert each one is a `const` of
     `ObservationReasons` — the ONE reason class, found by reflection — or `"no reader for " + <vendor>` from
     `CallerDeclaration`'s list.
  6. **The doc.** Write about 25 lines in the style of the surrounding section: the reader is keyed by session id
     (registry → variable → stored) and never by `cwd`. Cover the four states, the counting rules in one sentence each,
     what is read and what never is (§3.4), and the lower bound and budget. State that `agent_log` is unchanged and that
     nothing here is persisted until E5. Cite `CallerTranscript.cs` and `CallerTranscriptLocator.cs` by path.
- **Test scenarios.** `Every_fixture_holds_the_sentinel_in_its_raw_bytes`;
  `No_string_in_an_observed_session_holds_the_sentinel`; `Every_reason_is_from_the_closed_vocabulary`;
  `The_fixture_scan_found_every_fixture_E1_cut`.
- **Break-its.** (a) Temporarily set `ObservedAgent.AgentType` from the meta's `description` → the no-string test goes
  red on the fixtures with a meta. (b) Temporarily build a reason from a transcript string → the vocabulary test goes
  red. (c) Point the enumeration at an empty folder → the companion test goes red.
- **Done when.** All four tests pass; the three break-its were observed and restored; the module doc paragraph exists
  and matches the code; `node .agents/conventions/tools/plan-lifecycle.mjs` is clean; the whole suite is green.
- **Depends on.** E3.S4, E1.S3 (`allowlist.json` and its sentinel), E1.S5 (fixtures).
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

## 5. Order, and the epic's done-when

```
E3.S1 ─┐
E3.S2 ─┼─► E3.S4 (Fable spec; needs E1.S5) ─► E3.S5
E3.S3 ─┘
```

One branch and ONE commit for the epic. The coai gate runs once, over the whole epic (parent, `## Epics`).

| Parent's E3 done-when | Met by |
|---|---|
| a three-line message counts once | E3.S4 tests 1–3 |
| a resumed agent is one launch | E3.S4 test 5 |
| meta without transcript, `Agent` call with neither, a torn MIDDLE line, meta with no `model` → `partial: <why>`, reading on; a torn LAST line → NOT partial, counted in `Diagnostics.TornTails` (S4-1, parent §16 D22) | E3.S4 tests 20, 22, 24, 25, 26 |
| no `effort` → `unknown` | E3.S4 tests 10, 11 |
| persistence off → `unobserved: transcript not found` | E3.S2, carried through by E3.S4 test 30 |
| the registry beats the variable, recorded | E3.S2 |
| no sentinel in any rendering | E3.S5 (E5 adds the `.db` bytes and `--log`) |
| helpers lifted, `AgentLog` behaviour kept | E3.S1 |

## 6. Defects found in the parent plan (recorded, not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 and 10 → D7; 2 → D8; 3 → D4; 4 and 5 → D3; 7 and 8 → D9 (and the
> stated id is its own reason, `stated id`); 9 and 11 accepted as resolved here; 6 stays open — E3.S4's spec states the
> torn-LAST-line rule on E1.S4's measured frequency.

1. **The budget does not fit a real session.** E5's cap is 64 MB, and §11 step 0 measures read time "over a 14 MB
   transcript". This session's main transcript is 14.8 MB, but its `subagents/` folder is **63 MB**, 77 MB in all. A
   whole-session read on every round would end `partial: read budget` on exactly the long orchestrator sessions the
   owner's question is about. E3 adds a lower bound `fromUtc` (E3.S4 item 12). E5 must pass it (the earliest window
   start E4 needs), and E1 should measure the session TOTAL, not the main file.
2. **"Summed once per `message.id`" does not say which line's usage.** Lines of one id carry different
   `output_tokens` (162 of ≈2 400 ids here). E3.S4 takes the per-term maximum. The parent's §5 should say so.
3. **Writes as "a boolean per (agent, repository)" (§5) cannot serve E4's per-window association, and do not match
   `write_calls` (§11 step 4).** A session-wide boolean would associate an agent with a window in which it wrote
   nothing. E3 keeps one timestamped `ObservedWrite` per call.
4. **A write outside any checkout has no kind in §6.** Scratchpad, temp and memory files are written constantly. If they
   count as "another repository", rule 4's `same-session` (exactly one repository written) almost never applies.
   E3.S3 gives them `OutsideAnyCheckout`; E4 must decide how they count.
5. **"Tested against `repo_path`" leaves submodules and worktrees open.** E3.S3 resolves to the nearest checkout root.
   A submodule under `repo_path` (`.agents/conventions` here) resolves to its own root, and E4 must count "a root at or
   under `repo_path`" as here. A removed worktree is `Unresolved` with its directory, so E4 can still prefix-test it.
6. **A torn LAST line is the normal state of a live transcript** (`AgentLog.cs:229`'s own comment). Reading it as
   `partial: torn line` (§12, E3 done-when) may make most live rounds partial. E1 should measure how often a live read
   sees one; E3.S4's spec states its treatment.
7. **The id source has no column.** §5 says "which id source was used is recorded", and §12 says "the record says
   which", but step 23's columns (`authors_state`, `authors_session`, `authors_reader`) carry no source. E5 needs one.
8. **`CallerSessions.cs:44-53` is cited as the id source, but its ladder puts `COAI_CALLER_SESSION` first.** §12 wants
   that variable alone to be `unobserved: no session id`. The reader needs its own ladder (E3.S2), and E7 must not take
   a stated `rounds.caller` as a transcript key.
9. **The Agent-count check ignores refused launches.** The family's PreToolUse model guard refuses `Agent` calls,
   leaving a `tool_use` with an error result and no meta. Counted naively, every refused launch would read as `partial`
   (E3.S4 item 7). Older versions named the tool `Task`.
10. **The day gate has no use without a lower bound.** The parent lifts it for the reader, but §5 gives the reader no
    window (`before-open` starts at the transcript's first line). Defect 1 supplies one.
11. **E1's fixture list (§11 step 0) has no case for E3's partial causes** (meta without transcript, `Agent` call with
    neither, torn line, meta without `model`, refused call). E3 derives them by mutating E1's fixtures. Only E1 can
    supply the real shape of an older version's transcript without metas.
