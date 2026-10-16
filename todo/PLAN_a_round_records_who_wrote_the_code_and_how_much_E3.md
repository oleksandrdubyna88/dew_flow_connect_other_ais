# PLAN — E3: the caller's own transcript, read text-free (`CallerTranscript` → `ObservedSession`)

> Status: **plan only, nothing implemented yet, 2026-10-14.** Epic E3 of [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](PLAN_a_round_records_who_wrote_the_code_and_how_much.md).
> Scope: `src_mcp/src/Store` — a new reader keyed by the caller's session id, the text-free read helpers lifted out of
> `AgentLog.cs` (its behaviour kept), and one helper in `src_mcp/runners` widened. No schema step, no call site in
> `RoundEngine.cs`/`PanelService.cs`, nothing persisted: E5 stores, E4 attributes. Five stories; ONE cornerstone,
> **E3.S4**, spec by Fable.
>
> **Revised 2026-10-16 against main** (`59463d73`: E1 shipped in #828, E2 in #829, the plan-gate fix in #837/#841). Opus
> made this revision. It changes documents only. What changed:
> 1. **E1's measurements are in.** The new §2.3 holds RESULTS Q1–Q9, the folder facts and the shape of the twenty shipped
>    fixtures `c01`–`c20`. Where §2.3 and §2.2 differ, §2.3 wins. §2.2's "the LAST line holds the maximum" is marked
>    refuted (Q3).
> 2. **Every `file:line` was checked again on main.** Citations that moved are updated: `PanelSettings.cs`,
>    `CallerSessions.cs`, `CallerDeclaration.cs`, `CoaiMcp.Runners.csproj`, `RoundEngine.cs`, `AgentLogTests.cs` (13
>    tests, not 14) and `module_server.md`. New §2.1 rows name what has shipped: E1's artefacts,
>    `RoundRecord.Size`/`Gitlinks`/`PlanSource`, and schema steps 22 and 23. E5's step is **24**.
> 3. **E3.S1 gets the skip-before-parse half of the read (Q6).** A byte-level gate tests each line's exact stamp and never
>    decodes a line that is before the bound. A head rule always yields the file's first lines. A meter keeps bytes
>    *scanned* apart from bytes *in the slice*, so the budget can apply to the slice (parent D7). `AgentLog`'s day gate
>    does not change.
> 4. **E3.S2, E3.S3 and E3.S5 get the facts from Q4, Q5 and Q8**, and the fixture cases that their checks use. E3.S5
>    reads the sentinel from `allowlist.json` and the case count from `manifest.json`, because E1's own lists are
>    private.
> 5. **E3.S4 (Fable's spec) is not edited.** It gets only a revision box. §7, *For Fable to confirm*, lists every point
>    where the spec disagrees with E1's measurements or fixtures. Coding E3.S4 waits for Fable's answers.
> 6. **No test uses a real date.** The fixtures use a synthetic clock (2000-01-01, one second per rank), and every
>    clock in a test is taken from the fixture's own instants, never from `DateTime.UtcNow` (§4).
> 7. **§6 records three new defects in the parent** (items 12–14). The parent itself is not edited here.

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
| The budget's numbers (2 s / 64 MB, applied to the SLICE — parent D7), the call before `Projection.Write`, schema step 24's columns, `authors_reader` column | the budget MECHANISM, the skip before parsing (E3.S1) and `ReaderVersion` | E5 |
| Fixtures with every text field a sentinel | — (consumed: `c01`–`c20`, shipped in #828) | E1 |
| Backfill over stored `rounds.caller` ids | the `stored` id source | E7 |

E3.S1–S3 build on synthetic files written by their own tests. E3.S4 and E3.S5 run over E1's fixtures. **E1 has
landed them** (E1.S5, #828), so no story waits for E1 now. E3.S4 waits only for the answers to §7.

## 2. What the reader is built on

### 2.1 In the code (verified on this branch, 2026-10-14; re-verified on main `59463d73`, 2026-10-16)

| What | Where | How E3 uses it |
|---|---|---|
| Shared-mode, never-throwing line read | `src_mcp/src/Store/AgentLog.cs:183-207` | lifted to `TranscriptLines` (S1) |
| Torn-line tolerance | `AgentLog.cs:209-231` (the `JsonException` catch at `:227-230`) | the parse half lifted (S1) |
| Day gate | `AgentLog.cs:103-112` (`Days`), `:157-173` (`Mentions`) | lifted (S1) and kept for `AgentLog`. *Revised 2026-10-16:* the new reader skips by S1's exact-stamp byte gate, not by `Mentions` (Q6, §2.3; §7 F2) |
| Last-write gate | `AgentLog.cs:121-130` | the same test, applied to agent files (S4) |
| Default transcript root | `AgentLog.cs:39-40` (`DefaultProjectsDir`) | moved to `ClaudeHome.Default` (S2), `AgentLog` delegating |
| Override | `src_mcp/src/Server/PanelSettings.cs:421` (`AgentLogDir`), `:1009` (`COAI_AGENT_LOG_DIR`) | `ClaudeHome.With(agentLogDir)` (S2) |
| The env ladder today | `src_mcp/src/Server/CallerSessions.cs:44-50` (`Variables`), `:53-64` (`From`) | **not reused as is**: it puts `COAI_CALLER_SESSION` first, and the parent (§16 D9) wants that alone to be `unobserved: stated id` (S2) |
| Vendor | `src_mcp/src/Server/CallerDeclaration.cs:90-94` (`VendorFor`), `:106-108` (`VendorOf`) | handshake outranks env; the reader is told the vendor, never infers it (S2) |
| What `rounds.caller` holds | `src_mcp/src/Server/Rounds/RoundCommands.cs:34-35` | the `stored` id source for E7 (S2) |
| Path key normalisation | `src_mcp/runners/Context/RepositoryIdentity.cs:24` (`Normalised`: trims, folds `\` to `/`, drops a trailing `/`, lower-cases) | the key of a checkout root (S3) — it already folds the separators and the drive case that Q8 measured |
| "Holds a `.git` file or folder" | `src_mcp/runners/Worktrees/SubmodulePopulator.cs:133-134` (`HasGit`, `internal`; `runners` gives `InternalsVisibleTo` only to `CoaiMcp.Tests` and `FakeCli`, `CoaiMcp.Runners.csproj:44-48`, not to `CoaiMcp`) | widened to `public` and called (S3) — reuse move 1, no copy |
| `agent_log`'s window | `src_mcp/src/Server/Rounds/RoundEngine.cs:898-907` (`WhatTheCallerWasDoing`; the projects folder picked at `:902`) | untouched; the parent's §3 keeps `agent_log`'s behaviour |
| `AgentLog`'s guards | `src_mcp/tests/AgentLogTests.cs` (**13** tests; `Days` at `:125-133`; `AHalfWrittenLastLineIsSkipped_NotAFailure` at `:173-182`) | stay green; the `Days` test moves with the code (S1) |
| Clock seam | `TimeProvider`, `src_mcp/tests/FixedClock.cs` (read-only by design) | the budget's time (S4) |
| Temp dirs | `src_mcp/tests/TempDir.cs` (`TempDir.For(prefix)`) | every test here; the prefix starts `testsToDelete-` (the family's `test-leftovers` rule) |
| **Shipped by E1 (#828)** — the measurements | `research/RESULTS_caller_transcripts_on_both_sides.md` (Q1–Q9 at `:39-47`, folder facts at `:53-59`, open items at `:80-84`) | §2.3; wins over §2.2 |
| **Shipped by E1** — the fixtures | `src_mcp/tests/fixtures/caller-transcripts/c01`…`c20`, each with `projects/**` (except `c10`) and `rounds.json`; `c10` and `c12` also have `sessions/<pid>.json`; `manifest.json` (name → dir, `counts`, `fixtureRoots`, `pathStyle`, `hasTranscript`); `allowlist.json` (`sentinel` = the string every text field became) | E3.S2 (cross-checks), E3.S4, E3.S5; copied to the test output folder as `fixtures/caller-transcripts` (`ACallerTranscriptFixtureCarriesNoTextTests.cs:98`) |
| **Shipped by E1** — the guard | `src_mcp/tests/ACallerTranscriptFixtureCarriesNoTextTests.cs` (the pinned case list `:39-61`, the pinned dirs `:68-90`; both `private`) | E3.S5 does not re-type its lists: it reads `manifest.json` and `allowlist.json` |
| **Shipped by E1** — the harness | `scripts/lib/caller-transcripts.mjs` (Node; counts only; `timeSession` at `:594-623` and `parsePass` at `:563-582` timed the Q6 figures) | not reused by the C# reader. It shows how Q6 was measured: every file of the session, every line decoded to a string, no last-write gate |
| **Shipped by E2 (#829) and the plan-gate fix (#837/#841)** | `RoundRecord` at `src_mcp/src/Server/SessionStore.cs:78` (`Size` `:202`, `Gitlinks` `:211`, `PlanSource` `:223`); `src_mcp/src/Server/Rounds/RoundSizer.cs`; schema step 22 (`Schema.cs:619`) and step 23 (`:655`) | E3 touches none of them. E5's AUTHORS step is **24** |

No code reads the Claude session registry today: there is no `CLAUDE_PID` in any `.cs` file under `src_mcp` (checked
again 2026-10-16). S2 adds that read. Nothing of E3 exists on main yet (no `TranscriptLines`, `CallerTranscriptLocator`
or `ObservedSession`).

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
  *Revised 2026-10-16 (E1, Q3):* the inputs-identical fact holds. "The LAST line holds the maximum" is **refuted**: on WSL,
  16 of 6 745 ids with different outputs have a last line BELOW the maximum. The rule is the per-term maximum, never
  the last line. Different outputs occur on ~10 % of ids (11.2 % Windows, 9.2 % WSL), not on a few.
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
file wins over this section, and the story it touches records the change. *(2026-10-16: E1 has measured; §2.3.)*

### 2.3 What E1 measured, and what it changes here (added 2026-10-16)

Source: `research/RESULTS_caller_transcripts_on_both_sides.md`, measured 2026-10-15 on both sides from inside each one
(Windows: 405 code rounds, 21 caller sessions, 799 files, 528 k lines; WSL: 516 rounds, 44 sessions, 901 files,
486 k lines), on Claude Code 2.1.283–2.1.300. Counts only.

| Q | Measured | What it changes for E3 | Where |
|---|---|---|---|
| Q1 | The layout holds on both sides. Every agent file has its meta and every meta has its file (778/778, 857/857). The old 2.1.197 CLI left no session in the window | nothing changes; the metaless older layout (S4-6, S4-12) is still not on disk | — |
| Q2 | `effort` is on ≥ 99.5 % of assistant lines in every version. Metas WITH `model`: 100 % only on 2.1.283–2.1.285 and 2.1.300; elsewhere 70–98 % (Windows) and as low as 32 % (WSL 2.1.284). A launch without a `model` argument writes a meta without one | The meta names only the REQUEST (`RequestedAlias`, often `""`). The observed model is `ObservedMessage.Model`, from the agent file's own lines. **Boundary:** E3 gives the model per message, keyed by `AgentKey`. An agent-level "model of this agent" is E4's work over those messages. E3 does not give one. `partial: meta without model` would mark a large share of real rounds | §7 F6 |
| Q3 | 2.18 (Windows) and 2.41 (WSL) lines per `message.id`; at most 31 and 44 lines. Only `output_tokens` ever differs, on 11.2 % and 9.2 % of ids. On WSL, 16 ids have a last line below the maximum | per-term **MAXIMUM**, never the last line (parent D8 holds). The fixture `c03` (`message-last-line-below-max`) is that case | §2.2, §7 F11 |
| Q4 | The headless probes (2.1.289 Windows, 2.1.300 WSL) give the same results. The child sees `CLAUDE_CODE_SESSION_ID` and `CLAUDE_PID`, and the registry `sessionId` equals the variable. A nested `claude -p` gets its OWN id. `--resume` appends to the same file and replays no message id. `--no-session-persistence` leaves a registry entry and no `.jsonl`. **One message's lines span** p50 1.7 s, p99 50 s, **max 2.01 h**, with 3 ids over 1 h on each side. The lines are not written together. `/clear` is still OPEN (it needs an attended session) | S2's ladder holds as designed. Persistence off is `transcript not found` (S2). A resumed agent is one launch (S4 test 5). The message instant is its FIRST line (S4-8 holds). **`StraddleGuard = 1 h` and "one message never spans an hour" (S4-9) are refuted.** The budget cut (S4-3) cannot assume that a message's lines are adjacent | §7 F3, F4 |
| Q5 | 100 % of the Claude-called rounds have a transcript on both sides (399/405 Windows; the 6 not found have a codex caller; 516/516 WSL) | `no reader for codex` is a real, frequent state (S2), not a theoretical one | S2 |
| Q6 | The largest session is **650 MB** (273 files, 187 k lines). A full parse takes p50 **30.5 s**. The day-gated parse takes **19.9 s**, and the gate keeps 14 % of the lines. The largest WSL session is 235 MB: 9.6 s full, 7.8 s gated. The p99 session is 91 MB (Windows) and 124 MB (WSL); 4 and 5 sessions are ≥ 100 MB. *(Node, every file of the session, every line decoded: `scripts/lib/caller-transcripts.mjs:563-582`. E1/5: Node's time is not the C# reader's; E5 measures again.)* | A gate that still reads and decodes every line saves a third at best. The reader must skip BEFORE it decodes. File-level: the last-write time. Line-level: the stamp, tested on raw bytes, with no `JSON.parse` and no string. The byte budget meets these sessions every time, so it is charged on the slice only (parent D7). In E3.S4 step 1 the budget is charged on every line, before the gate: that is the known defect | E3.S1; §7 F1, F2 |
| Q7 | Windows: 587 `Agent` tool uses and 778 metas. The 191 extra metas are the nested launches, at depth 2–3. WSL: 844 uses, 857 metas, 17 nested, 4 uses with no meta (refused launches) | S4-5 and step 7a hold. Depth 3 exists, so no rule may assume depth ≤ 2 | — |
| Q8 | Every write path is absolute on both sides. Windows: 90 % backslash, a lower-case drive on 219 subagent writes. WSL: `/` only. A write under the session's `cwd`: 0 on Windows (where decidable), 5 % on WSL | S3 holds (`Normalised` folds both). `cwd` is never read. The test with mixed separators and drive case stays | S3 |
| Q9 | 21 top-level line types (`api-request-blob` and others are new to §2.2), and the content types `text`, `thinking`, `tool_use`, `tool_result`, `tool_addition`, `image`. Unparseable lines: 1 on Windows and 3 on WSL; one of them is at a file's end (a line that was being written). A file's first line is a `queue-operation` on 64 of 65 files | An unknown type means "nothing more" (step 3 holds). Torn MIDDLE lines exist in real data, so `partial: torn line` can occur | — |
| Folder | `is_error` is absent on 43 % (Windows) and 34 % (WSL) of `tool_result`s. A main file's first line is a stamped `queue-operation` on 20 of 21 Windows files; the 21st starts with an `ai-title`, which has **no timestamp**. In 0 of 9 379 samples did a live read see a torn last line | absent means "not an error" (S4 holds). The first-line exemption must reach the first STAMPED line (§7 F5). S4-1 and D22 stay as designed | §7 F5 |

**The shipped fixtures** (measured on main 2026-10-16 by Opus, from the files; counts and shapes only):

| dir | name (manifest) | main · agent files · metas · `Agent` uses | Notes that matter to E3 |
|---|---|---|---|
| c01 | `background-agent-across-round-start` | 1 · 1 · 1 · 1 | one `<synthetic>` line (its `message.id` is the sentinel) |
| c02 | `message-as-several-lines` | 1 · 1 · 1 · 1 | 3 multi-line ids; main `requestedModel` is the sentinel |
| c03 | `message-last-line-below-max` | 1 · 1 · 1 · 1 | 1 id whose last line is below its maximum (Q3) |
| c04 | `meta-without-model` | 1 · 1 · 1 · 1 | the one meta has no `model` |
| c05 | `missing-child-transcripts` | 1 · 1 · 2 · 3 | one meta without a file; one call with neither; one `<synthetic>` line |
| c06 | `model-switch-in-window` | 1 · 0 · 0 · 0 | two `message.model` values; 1 write (backslash path, into `scratch`) |
| c07 | `nested-agents` | 1 · 2 · 2 · 3 | the depth-2 meta has `parentAgentId` and **no `model`**; one agent file (clipped) starts with an `assistant` line |
| c08 | `no-effort-lines` | 1 · 1 · 1 · 1 | the only effort-less line is the `<synthetic>` one |
| c09 | `no-evidence-two-repositories` | 1 · 0 · 0 · 0 | no write |
| c10 | `persistence-off` | — · — · — · — | no `projects/`; `sessions/41017.json` names the caller id |
| c11 | `refused-agent-call` | 1 · 0 · 0 · 1 | the call's `tool_result` has `is_error: true` |
| c12 | `registry-and-variable-disagree` | 1 · 0 · 0 · 0 | `sessions/52044.json` names an id that has NO transcript; 2 writes, 1 without a `tool_result` |
| c13, c15, c16 | `replay-same-round-twice`, `second-gate-session-same-caller`, `skipped-round-between-recorded` | 1 · 0 · 0 · 0 | rounds-file cases (E4's); 1 answered write each |
| c14 | `resumed-agent` | 1 · 1 · 1 · 1 | 21 ids; the `Agent` call has no `tool_result`, but its meta joins it |
| c17 | `two-repositories-one-session` | 1 · 0 · 0 · 0 | 2 writes (`repo-a`, `repo-b`); the `repo-b` write has **no `tool_result`** |
| c18 | `work-before-open` | 1 · 0 · 0 · 0 | 1 answered write into `repo-a` |
| c19 | `write-into-submodule-and-scratchpad` | 1 · 0 · 0 · 0 | 2 assistant lines only: **neither write has a `tool_result`**; the submodule dir `repo-a/p1000` is not a manifest root |
| c20 | `writes-only-elsewhere` | 1 · 0 · 0 · 0 | 1 answered write into `scratch` |

The facts that hold across all twenty, and that every E3 test must respect:

- **The clock is synthetic.** Every instant is `2000-01-01T00:00:00Z` plus one second per rank of the real instants.
  One fixture spans 88 s at most. Midnight, a 2 h span and a multi-day window cannot appear without a mutation. A test
  takes its bound and its clock from the fixture's own instants (its lines or `rounds.json`). It never uses a literal
  real date and never uses `DateTime.UtcNow`. A copied file's last-write time is the copy time (the real clock), so it
  says nothing about the synthetic stamps.
- **Numbers are ranks.** Each token term is the RANK of its value at that path (0 stays 0). Order is kept, so
  "maximum" and "sum > maximum" can still be tested. Totals are not real token counts.
- **Stamps are not always in file order.** In 11 of 19 cases, 12 of 406 stamped lines are earlier than the line before
  them. E1 did not measure order over whole sessions. A skip that needs stamps in order (bisection, "stop at the first
  line past the bound", a per-file LAST-stamp skip) is therefore not safe.
- **The top-level `timestamp` comes after `message`** in 237 of 240 lines that have both. A "prefix" read must find the
  key inside the line. It is not at the line's start.
- **Every fixture is ASCII.** No line holds a multibyte character, so a byte-versus-char test needs a mutation.
- **13 of 20 cases use `backslash+slash` paths.** Every write path in c06, c12–c18 and c20 is a backslash path
  (`D:\fixture\…`, JSON-escaped). The manifest's `fixtureRoots` include `D:/fixture/scratch`, which is the scratchpad.
- **A text field is the sentinel.** That includes a line `type` off the allowlist (c04–c06, c08, c09, c12, c13, c15, c16,
  c18 and c20 have such lines), a synthetic line's `message.id`, and the main lines' `requestedModel`
  (`claude-opus-5-5[1m]` is not in `allowlist.json`'s `model-ref` list): it is the sentinel in c02, c03, c04, c07, c11,
  c12, c14 and c17. In the other fixtures (versions 2.1.284–2.1.288) no line has a `requestedModel`.

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
public sealed record ReadDiagnostics(int FilesRead, long BytesRead, int LinesSkippedByDayGate, int TornTails,
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

Two rules for every test in this epic (added 2026-10-16):

- **No real clock, no real date.** A test that reads a fixture takes its bound and its `FixedClock`/`SteppingClock`
  from that fixture's own instants (§2.3: the synthetic clock starts at 2000-01-01). A test that writes its own lines
  picks its instants freely, but it never calls `DateTime.UtcNow` and never depends on today's date.
- **Temp folders are `TempDir.For("testsToDelete-coai-<what>-")`** (the family's `test-leftovers` rule; 46 call sites
  already use the prefix).

Every story is test-first: write the tests, run them and watch them fail **for the stated reason**, not because
something does not compile. A missing type is a compile error, not a RED: create the empty type first, then watch
the assertion fail. Then implement, watch the tests go green, run each break-it (delete or alter the named production
line, watch the named test fail, restore it), and report both observations.

---

### E3.S1 — The transcript-read helpers lifted out of `AgentLog`, behaviour kept

- **Goal.** One text-free unit for reading a transcript that its CLI is still appending to, shared by `AgentLog` and the
  new reader, so the two cannot drift. `agent_log` behaves exactly as before (parent §3). *Revised 2026-10-16:* the unit
  also gives the new reader a way to **skip before it decodes** (items 7–9). E1 measured that a gate which still reads
  and decodes every line saves only a third of a 30.5 s read (Q6, §2.3). The budget can then apply to the slice
  (parent D7), not to the whole file.
- **Scope.**
  - New `src_mcp/src/Store/TranscriptLines.cs` (`internal static class`, ≤ 220 lines; was ≤ 120 before items 7–9).
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
  7. *(added 2026-10-16)* `TranscriptLines.StampAtOrAfter(ReadOnlySpan<byte> line, DateTime boundUtc) → bool`. Pure.
     - Find EVERY occurrence of the ASCII bytes `"timestamp":` in the line, then skip optional spaces and one `"`.
     - Read the next 19 bytes as `yyyy-MM-ddTHH:mm:ss`. Compare them, ordinal, with the bound in the same form, with the
       bound floored to the second.
     - Return true when ANY occurrence is at or after the bound. Return false when every occurrence is before it, or
       when the key does not occur. When the 19 bytes are not in that form, treat them as "at or after", so the line is
       parsed and the JSON decides.
     - Remark why it checks every occurrence: the top-level `timestamp` comes AFTER `message` (237 of 240 fixture lines,
       §2.3). So the first occurrence can be a key in a nested object, and the last can be one inside `toolUseResult`.
       With "any occurrence", a nested key can cost one extra parse. It can never cause the line's own stamp to be
       skipped.
  8. *(added 2026-10-16)* `TranscriptLines.ReadSharedFrom(string file, DateTime boundUtc, ScanMeter meter) →
     IEnumerable<string>`. It opens the file exactly as `ReadShared` does (shared, never throws). Then:
     - It reads RAW BYTES in blocks of 64 KiB and splits them on `\n` with `Span.IndexOf`. A line longer than a block
       makes the buffer grow, so the line stays one line. A `\r` before the `\n` is dropped from the yielded text.
     - **The head:** every line up to and including the first line whose bytes contain `"timestamp":` is yielded,
       whatever its stamp. This is the line that gives `FirstLineUtc` and `LaunchedUtc`. A file can start with an
       `ai-title`, which has no timestamp (1 of 21 Windows main files, §2.3).
     - **The tail:** a last line with no `\n` is always yielded. A torn tail usually ends before its stamp (the stamp
       comes after `message`), and S4-1 must still see it.
     - Every other line is yielded only when `StampAtOrAfter(line, boundUtc)` is true. Otherwise
       `meter.LinesSkipped++`, and its bytes are **never decoded**.
     - A yielded line is decoded with UTF-8. `meter.BytesYielded` grows by the line's raw byte count plus its
       terminator. These are encoded bytes, so nothing is encoded again and no char count is used.
       `meter.BytesScanned` grows by every byte read.
     - `meter.Stop` (a `Func<bool>`) is asked after every yielded line and after every 1 MiB scanned. When it returns
       true, the sequence ends. This is the budget's hook. E3.S4 decides what `Stop` checks (§7 F1).
     - It never stops early on a stamp. Each line is judged alone, because stamps are not always in file order (§2.3).
  9. *(added 2026-10-16)* `internal sealed class ScanMeter { long BytesScanned; long BytesYielded; int LinesSkipped;
     Func<bool> Stop; }`. One instance serves one read, across every file of that read. **Considered and rejected,
     with the reason:**
     - A byte-offset bisection, a per-file LAST-stamp skip, and "the first line past the bound ends the file" all need
       a file's stamps to be in order. They are not in order in 11 of 19 fixtures, and E1 did not measure order on
       whole sessions.
     - `WrittenSince` (item 4) needs no order: a file that was last written before the bound holds no later line. It is
       the file-level skip.
     - `AgentLog` keeps `Days`/`Mentions` unchanged (parent §3). Moving it to item 8 is a separate decision, not part
       of E3.
- **Test scenarios** (`ATranscriptIsReadWhileItIsBeingWrittenTests`):
  - `A_file_held_open_for_writing_by_another_handle_is_still_read`: open the file with `FileShare.Read` and a write
    handle, then read it. Every whole line comes back.
  - `A_file_that_cannot_be_opened_is_no_lines_not_an_exception`: missing file, and a directory passed as a file.
  - `A_torn_line_is_reported_as_torn_and_the_next_whole_line_still_parses`: `TryParse` returns false, then true on the
    next line. The lines are synthetic: real unparseable lines exist (Q9: 1 on Windows, 3 on WSL), but no fixture holds
    one, because E1 shipped no `torn-last-line` case.
  - `Only_the_days_a_window_touches_are_listed` (moved from `AgentLogTests.cs:125-133`, unchanged assertions).
  - `A_file_last_written_before_the_bound_is_skipped`: set the time with `File.SetLastWriteTimeUtc`.
  - All **13** `AgentLogTests` stay green **unchanged** apart from the moved test. That is the behaviour-kept proof.
  - *(added 2026-10-16, items 7–9; synthetic lines, instants chosen by the test, never the real clock)*
    - `A_line_stamped_before_the_bound_is_skipped`: three lines before the bound and two at or after
      it. Expect two yielded, `LinesSkipped == 3`, and `BytesYielded` equal to the two lines' byte lengths plus their
      terminators.
    - `Any_stamp_at_or_after_the_bound_keeps_the_line`: a nested `"timestamp":` before the top-level one, and one after
      it. When either is at or after the bound, the line is yielded. When both are before it, the line is skipped.
    - `A_stamp_written_with_a_space_is_still_seen`: `"timestamp": "…"`.
    - `The_head_is_yielded_up_to_the_first_stamped_line`: line 1 is an `ai-title` with no stamp, line 2 is a
      `queue-operation` stamped before the bound, and line 3 is stamped before the bound. Expect lines 1 and 2 yielded
      and line 3 skipped.
    - `An_unterminated_last_line_is_always_yielded`: the last line has no `\n` and no stamp.
    - `Lines_out_of_order_are_judged_one_by_one`: stamps 10:00, 12:00, 09:00, 11:00 and a bound of 10:30. Expect 12:00
      and 11:00 yielded. The read does not stop early.
    - `Bytes_in_the_slice_are_bytes_not_chars`: one yielded line holds a 3-byte character. Expect `BytesYielded` to be
      its UTF-8 length plus 1, not its char count.
    - `A_line_longer_than_a_read_block_is_one_line`: a 200 KiB line.
    - `A_CRLF_ends_a_line_and_is_not_yielded`.
    - `The_stop_hook_ends_the_read`: `Stop` returns true after the first yielded line. Expect exactly one line.
    - `The_gate_never_drops_a_line_a_full_parse_places_at_or_after_the_bound` (the positive anchor, over E1's
      fixtures). Enumerate every case in `manifest.json` that has `hasTranscript: true` (19 cases; `c10` has none), and
      assert the count is above zero. Take every `.jsonl` file from the test output folder (read only, no copy). Use as
      the bound the median of the file's own stamps. Expect every line that `JsonDocument` gives a top-level
      `timestamp` at or after the bound to be yielded. The 11 cases whose stamps are out of order (§2.3) are inside
      this set.
- **Break-its.** (a) In `ReadShared`, drop `FileShare.ReadWrite` → the held-open test goes red. (b) Make `TryParse`
  rethrow → the torn-line test goes red, and so does `AgentLogTests.AHalfWrittenLastLineIsSkipped_NotAFailure` (`:173`).
  (c) Make `WrittenSince` return true → the last-write test goes red.
  *(added 2026-10-16)* (d) `StampAtOrAfter` reads only the FIRST occurrence → the any-stamp test goes red. (e) End the
  file at the first skipped line after a yielded one → the out-of-order test and the fixture anchor go red. (f) Count
  `line.Length` → the bytes test goes red. (g) Drop the head rule → the head test goes red. "A skipped line is never
  decoded" cannot be seen from outside: a decode of bad bytes does not throw, it substitutes. So the reviewer checks
  that the one UTF-8 decode call comes after the gate, and E5 measures the time.
- **Done when.** The new class and all `AgentLogTests` are green; the break-its (a)–(g) were observed red and restored;
  `AgentLog.cs` no longer contains a `FileStream` or a `JsonException` catch of its own; the review confirms that
  `ReadSharedFrom` decodes only yielded lines; the whole suite is green.
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
    nor `AgentLog` (whose FakeCli round tests read the real `~/.claude/projects` today, `RoundEngine.cs:902`) ever
    opens a developer's transcripts. E5.S4 adds only the clearing of the caller-identity variables on top of it.
  - New `src_mcp/tests/TheCallersTranscriptIsFoundByItsSessionIdTests.cs`.
  - No call site: E5 wires it from `RoundEngine` the way `RoundEngine.cs:902` picks the projects folder.
- **Brief (Sonnet coder).**
  1. `public sealed record ClaudeHome(string ProjectsDir, string SessionsDir)`:
     - `Default` is `<UserProfile>/.claude/projects` and `<UserProfile>/.claude/sessions`.
     - `With(string agentLogDir)` replaces only `ProjectsDir` when `agentLogDir` is non-empty, which is what
       `PanelSettings.AgentLogDir` (`PanelSettings.cs:421`) means. The registry folder does not move.
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
  - *(added 2026-10-16)* **Two checks over E1's fixtures.** They go through `CallerTranscriptFixture.Materialise`, which
    E3.S4 adds; the epic is one commit, so they are written when it exists. They need it to copy `sessions/` too
    (§7 F9).
    - `The_registry_wins_even_when_only_the_variables_transcript_exists` — `c12` (`registry-and-variable-disagree`).
      `CLAUDE_PID=52044`, and `CLAUDE_CODE_SESSION_ID` is the `rounds.json` `callerSessionId`, which is the id that has
      a transcript. The registry names another id. Expect `NotObservable(TranscriptNotFound)` with source `registry`.
      This is the `/clear` shape that rule 7 decides.
    - `Persistence_off_is_transcript_not_found_from_a_real_registry_entry` — `c10` (`persistence-off`).
      `CLAUDE_PID=41017`, and `projects/` is absent. Expect `TranscriptNotFound` with source `registry`.
  - *(added 2026-10-16, E1 Q4/Q5)* What the probes settled: in a headless session the registry `sessionId` EQUALS the
    variable, a nested `claude -p` gets its own id, `--resume` keeps the id and the file, and
    `--no-session-persistence` leaves a registry entry and no file. **`/clear` is still open** (RESULTS `:82`). The
    registry-first order of parent D9 stays the design until an attended session measures `/clear`. The Q5 coverage
    makes `no reader for codex` real: 6 of 405 Windows rounds have a codex caller.
- **Break-its.**
  - (a) Swap the registry and variable order → the registry test goes red.
  - (b) Replace the pid-named read with a listing → the by-pid test goes red.
  - (c) Drop the uuid check → the path test goes red.
  - (d) Use `CallerIdentity.From` (`CallerSessions.cs:53-64`; still these lines on main 2026-10-16) as the ladder → the stated-session test goes red. This is
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
    remark (`:132`, which names `Snapshots/SnapshotListing` as the second caller) to name the third.
  - New `src_mcp/tests/AWriteIsKnownOnlyByItsPathTests.cs`.
- **Brief (Sonnet coder).**
  1. `WriteTools.Names` = `["Edit", "Write", "MultiEdit", "NotebookEdit"]` (ordinal, case-sensitive), plus — parent
     §16 D18 — `Artifact`-style file writers as table entries (tool name → its ONE path property), counted only when
     the path resolves to a `Checkout`; none is added without an E1 fixture showing it on disk. *(2026-10-16: E1's
     fixtures hold no `Artifact` write with a path, and `allowlist.json` keeps only `input.file_path` and
     `input.notebook_path`. So the table ships with the four names alone. E1's harness uses the same four,
     `scripts/lib/caller-transcripts.mjs:17`.)*
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
  - *(added 2026-10-16, E1 Q8: on Windows 90 % of write paths use backslashes, and 219 subagent writes have a
    lower-case drive; on WSL every path uses `/`)* `A_mixed_separator_lower_case_drive_path_finds_its_checkout`: the
    tree is built at the temp root, and the write path is given as the temp root with a lower-case drive and `\` and `/`
    mixed. Expect `Checkout(the root)`. Windows only. `A_windows_shaped_path_on_linux_is_unresolved`: on a non-Windows
    host, `D:\x\a.cs` is not fully qualified, so expect `Unresolved("")`. Skip on Windows. (E1 found no
    Windows-shaped path on WSL. The test fixes the outcome in case one appears.)
  - The fixture cases with writes (`c06`, `c12`–`c20`) are read through E3.S4's materialiser, not here. S3's trees
    stay synthetic. The CACHE key in `CheckoutRoots` is the `Normalised` directory, so `D:\X` and `d:/x` share one
    entry.
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

> **Revision box, 2026-10-16 (Opus; the spec below is Fable's and is NOT edited).** It was checked against main
> `59463d73` and against E1's RESULTS and fixtures (§2.3). The citations it holds still resolve: `AgentLog.cs:229`
> (the torn-tail comment), `AgentLog.Parse`'s timestamp styles (`:216`), parent D6–D9 and D17/D18. Several rules and
> worked examples **disagree with the measurements or with the shipped fixtures**. Each one is listed, with the reason
> and a proposed answer, in **§7 *For Fable to confirm*** (F1–F12):
> - the budget, the day gate and the guard — steps 1–2, S4-3, S4-9, `ReadDiagnostics`;
> - the first-line exemption — step 2, S4-10;
> - `meta without model` as `partial` — step 7, the `nested-agents` row;
> - the materialiser and its helpers;
> - six worked-example rows and tests 1, 7, 8, 10, 13, 31 and 38.
>
> A fixture named below is a manifest NAME. Its folder is `c01`–`c20` (§2.3). **The coder does not start E3.S4 until
> Fable has answered §7.**

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
public sealed record ReadDiagnostics(int FilesRead, long BytesRead, int LinesSkippedByDayGate, int TornTails,
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
   `TranscriptLines.ReadShared` adds `Encoding.UTF8.GetByteCount(line)` plus the line terminator to `BytesRead` — encoded
   bytes, never `line.Length` (UTF-16 chars undercount non-ASCII text, so a multibyte transcript would pass 64 MiB before
   the limit fired). After EVERY line:
   `Exhausted = BytesRead > MaxBytes || Clock.GetUtcNow() − started > MaxTime`. On exhaustion the enumeration ends,
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
| any fixture, `MaxBytes` = the BYTE offset of the middle of a multi-line message | `partial: read budget` | only what was read | the cut id absent; every present id complete | no `agent call without transcript` reason | — | S4-3 |

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
31. `The_read_stops_at_the_byte_budget_and_says_so` — `MaxBytes` = the BYTE offset into a multi-line message (computed from the raw file; one fixture line carries multibyte text, so a char count would not fire); `partial: read budget`; the cut id absent; every present id has all its lines (the test checks each present id's raw line count against the lines before the cut).
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
  - `research/module_server.md`: a new subsection right after the `rounds.agent_log` paragraphs (`:3180-3194` on main
    2026-10-16; `:3007-3021` when this was written). No product code.
- **Brief (Sonnet coder).** *(Items 1–3 revised 2026-10-16 to match what E1 shipped.)*
  1. **Fixtures are enumerated, never listed.** E1's folder is `src_mcp/tests/fixtures/caller-transcripts/`, copied to
     `AppContext.BaseDirectory/fixtures/caller-transcripts` (`ACallerTranscriptFixtureCarriesNoTextTests.cs:98`). Find
     the case folders (`c01`–`c20`) with `Directory.EnumerateDirectories`. Assert the count is greater than zero AND
     equals the number of `cases` in `manifest.json` (20 today). E1's own pinned lists (`RequiredCases`, `RequiredDirs`)
     are `private`, and the manifest is the public form of the same list: the guard compares the two both ways. That is
     the companion that keeps the scan alive (testing.md, *a structural test that matches nothing passes forever*).
  2. **The sentinel is E1's value**, read from `allowlist.json`'s `sentinel` field, exactly as E1's guard reads it, and
     never re-typed. (It is a JSON value, not a C# constant. A copy would be a second list that drifts.)
  3. **The positive half first.** For each case with `hasTranscript: true` (19 cases; `c10` has no transcript), assert
     that the RAW bytes of its main file contain the sentinel at least once. This proves that the fixture would leak if
     the reader read text. E1's `TheSentinelIsActuallyPresent` asserts the same, so this is a second check by design.
     Do not reuse that test.
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
  *(2026-10-16)* The no-string test **will be red as the spec stands**. The fixtures' `requestedModel` is the sentinel
  in c02, c03, c04, c07, c11, c12, c14 and c17, and `ObservedMessage.Requested` copies it (§7 F7). The guard is right.
  The fix belongs to E3.S4 or to E1, never to this test. The synthetic lines' `message.id` is also the sentinel
  (c01, c05, c08). It stays out of `ObservedSession` only because S4-11 drops synthetic lines (§7 F10).
- **Break-its.** (a) Temporarily set `ObservedAgent.AgentType` from the meta's `description` → the no-string test goes
  red on the fixtures with a meta. (b) Temporarily build a reason from a transcript string → the vocabulary test goes
  red. (c) Point the enumeration at an empty folder → the companion test goes red.
- **Done when.** All four tests pass; the three break-its were observed and restored; the module doc paragraph exists
  and matches the code; `node .agents/conventions/tools/plan-lifecycle.mjs` is clean; the whole suite is green.
- **Depends on.** E3.S4, E1.S3 (`allowlist.json` and its sentinel), E1.S5 (fixtures). E1.S3 and E1.S5 have shipped
  (#828).
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

## 5. Order, and the epic's done-when

```
E3.S1 ─┐
E3.S2 ─┼─► E3.S4 (Fable spec; E1.S5 shipped; waits for §7) ─► E3.S5
E3.S3 ─┘
```

One branch and ONE commit for the epic. The coai gate runs once, over the whole epic (parent, `## Epics`).

| Parent's E3 done-when | Met by |
|---|---|
| a three-line message counts once | E3.S4 tests 1–3, plus a test over `c03` if Fable accepts F11 |
| a resumed agent is one launch | E3.S4 test 5 (`c14`) |
| meta without transcript, `Agent` call with neither, a torn MIDDLE line, meta with no `model` → `partial: <why>`, reading on; a torn LAST line → NOT partial, counted in `Diagnostics.TornTails` (S4-1, parent §16 D22) | E3.S4 tests 20, 22 (`c05`), 24 (`c04`), 25, 26 (mutations). *"meta with no `model` → partial" is in question: §7 F6* |
| no `effort` → `unknown` | E3.S4 tests 10, 11. *`c08` cannot show it: §7 F10* |
| persistence off → `unobserved: transcript not found` | E3.S2, carried through by E3.S4 test 30 |
| the registry beats the variable, recorded | E3.S2 |
| no sentinel in any rendering | E3.S5 (E5 adds the `.db` bytes and `--log`) |
| helpers lifted, `AgentLog` behaviour kept | E3.S1 |

## 6. Defects found in the parent plan (recorded, not fixed here)

> **Settled 2026-10-14** in the parent's §16: 1 and 10 → D7; 2 → D8; 3 → D4; 4 and 5 → D3; 7 and 8 → D9 (and the
> stated id is its own reason, `stated id`); 9 and 11 accepted as resolved here; 6 stays open — E3.S4's spec states the
> torn-LAST-line rule on E1.S4's measured frequency.
>
> **2026-10-16, against E1's RESULTS:**
> - 6 is closed by D22, and E1 saw no torn tail in 9 379 live samples.
> - 1 and 10 are measured. The largest session is 650 MB, not 77 MB, and the day-substring gate does not make the bound
>   cheap (Q6). That is new item 12, and E3.S1 and §7 F1/F2 carry the change.
> - 2 is strengthened by Q3: the last line is not always the maximum.
> - 11 is partly closed: E1 shipped `c04`, `c05` and `c11` for three of the partial causes. A torn line is still a
>   mutation.
> - Items 12–14 are new.

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
   which", but the AUTHORS step's columns (`authors_state`, `authors_session`, `authors_reader`) carry no source. E5
   needs one. (Settled by D9's `authors_id_source`. The AUTHORS step is now **24**: step 22 is E2's size, #829, and
   step 23 is the plan-gate fix's `PlanSource`, #837/#841.)
8. **`CallerSessions.cs:44-64` is cited as the id source, but its ladder puts `COAI_CALLER_SESSION` first.** §12 wants
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
12. *(2026-10-16)* **§5 says "the day-substring gate makes the bound cheap (§16 D7)". Q6 refutes this.** At 650 MB the
    gated read takes 19.9 s against 30.5 s for a full one. The bound is cheap only when a line is skipped before it is
    decoded (last-write time per file, then the exact stamp on raw bytes, E3.S1). §5 and D7's reason should say so.
    E5's budget must apply to the slice: D7 already says this, and E3.S4 step 1 does not do it (§7 F1).
13. *(2026-10-16)* **The parent's E3 done-when lists "a meta with no `model` → `partial`".** Q2 shows that up to 68 % of
    the metas of one version have no `model`, because the launch gave none. Nothing is lost, because the model comes from the
    agent file's lines. Pending Fable (§7 F6), the done-when and §5's "requested (the meta's alias, else
    `requestedModel`)" should say the empty alias is normal.
14. *(2026-10-16)* **The parent's E1 goal still reads "the read time over 14 MB … ≤ 300 ms p50".** It was measured
    over 650 MB, and the measurement refuted it by two orders of magnitude. The E1 epic entry should cite the result.

## 7. For Fable to confirm (added 2026-10-16)

E3.S4 is Fable's spec, so Opus did not change it. Each item gives what the spec says, the measured fact that disagrees
(the RESULTS Q or the fixture, §2.3), why the two cannot both hold, and ONE proposed answer. A rule that is accepted
and changes a count bumps `ReaderVersion`. It is still 1, and nothing has shipped.

| # | The spec says | Measured | Why it cannot stand | Proposed |
|---|---|---|---|---|
| F1 | Step 1: every line that `ReadShared` yields adds its bytes, and the budget is checked after EVERY line. The day gate is step 2, so the bytes are charged before old lines are skipped. `ReadDiagnostics.BytesRead` | Q6: sessions of 650 / 235 MB; the p99 session is 91 / 124 MB; 4 / 5 sessions ≥ 100 MB | The parent's D7 says *"the 64 MiB / 2 s budget applies to the slice"*. Charged on every line, every long orchestrator session ends `partial: read budget`, and that is exactly the case the parent wanted to keep | Charge `MaxBytes` on `ScanMeter.BytesYielded` (the slice, E3.S1 item 8). Check `MaxTime` over the WHOLE read, skipping included, through `ScanMeter.Stop` after each yielded line and each 1 MiB scanned, because the round pays for that time. `ReadDiagnostics`: replace `BytesRead` with `BytesScanned` + `BytesInSlice`, and `LinesSkippedByDayGate` with `LinesSkippedBeforeParse`. E5 sets the numbers from a C# measurement (E1/5) |
| F2 | Step 2 and S4-9: the day gate is `TranscriptLines.Mentions(line, Days(fromUtc − 1 h, max(now, fromUtc)))` over decoded lines | Q6: the substring gate still costs 19.9 s of 30.5 s. `Days` returns `[]` for a span of more than 7 days (`AgentLog.cs:106-109`), and then the gate is off. Fixtures: the stamps are on 2000-01-01, so with a real `now` the span is > 7 days in every fixture test | The gate does not make the read cheap (Q6). In tests it is off, so the break-it *"drop `StraddleGuard` → test 38 red"* cannot go red unless the clock is pinned. Also, a torn tail usually ends before its stamp, so a day gate skips it and `TornTails` never counts it under a bound | Use E3.S1's `ReadSharedFrom(file, fromUtc − StraddleGuard, meter)`: the exact stamp tested on raw bytes, no list of days, no clock in the gate, the head and the unterminated tail always yielded. `max(now, fromUtc)` is then not needed |
| F3 | S4-9: `StraddleGuard = 1 h` — *"One message never spans an hour"* | Q4: the lines of one message span up to **2.01 h** on both sides, and 3 ids per side span more than 1 h | A message that starts more than 1 h before the bound has its first line skipped. It is then counted by its tail, which acceptance 12 forbids | `StraddleGuard = 3 h` (1.5 × the measured maximum), with Q4 in its remark. Test 38 then needs a stamp-setting mutation (F9e) |
| F4 | S4-3: a budget hit drops only *"the id of the line it stopped at"*. Test 31: *"every present id has all its lines"* | Q4: the lines of one message are not adjacent and can be 2 h apart | Any id already seen in the file where the read stopped can have more lines after the stop. In real data, "present ⇒ complete" is not guaranteed (only in the 88 s fixtures) | On a stop, drop every id of the stopped file whose latest seen line is within `StraddleGuard` of the stop line's stamp. (Or drop every id first seen in that file: simpler, and it loses more.) Test 31 asserts this rule |
| F5 | Step 2: *"except the FIRST line of every file, which is always parsed"*. `FirstLineUtc` = the first parseable line WITH a timestamp | Folder facts: 1 of 21 Windows main files starts with an `ai-title`, which has no timestamp | Under a bound, the first STAMPED line can be gated out. `FirstLineUtc` then becomes the first in-slice line, or null. The worked example and test 40 do not see this, because every fixture's main file starts stamped | The exemption reaches the first stamped line (E3.S1's head). A mutation puts a stamp-less line first (F9e) |
| F6 | `meta without model` → `partial` (step 7, contract). The `nested-agents` row: `observed` | Q2: metas with `model` are 32–98 % in most versions. `c07`'s depth-2 meta has no `model` | By the spec's own principle (*a reason only when something was LOST or a counted value is uncertain*), nothing is lost: `Requested` falls back, and `Model` comes from the lines. Most real rounds would read `partial`. `c07` reads `partial: meta without model`, which contradicts its row | Make it a count, `Diagnostics.MetasWithoutModel`, not a reason. `RequestedAlias` stays `""`. The parent's done-when changes (§6 item 13) |
| F7 | `Requested` = the meta alias, else the earliest line's `requestedModel`. Test 7: main's `Requested` ends with `[1m]` | Fixtures: main `requestedModel` is the SENTINEL in c02, c03, c04, c07, c11, c12, c14 and c17 (`claude-opus-5-5[1m]` is not in `allowlist.json`'s `model-ref` list). In the other cases it is absent | The sentinel enters `ObservedMessage.Requested`, so E3.S5's no-string test goes red. No fixture can pass test 7 | Either (a) E1 widens `model-ref` with the `claude-…[1m]` ids (a privacy decision: the pinned `ApprovedAllowlistSha256` changes, and the fixtures are cut again), or (b) the reader admits `requestedModel` only when it matches a closed shape, `^(claude-[a-z0-9-]+\|[a-z]+)(\[1m\])?$` or `<synthetic>`, and otherwise uses `""`. (b) is a read-side shape check in the spirit of §3.4, and it needs no re-cut. Test 7 then needs (a) or a mutation |
| F8 | Rows `write-into-submodule-and-scratchpad` (one `Checkout(submodule)` + one `OutsideAnyCheckout`), `two-repositories-one-session` (two `Where`) and `writes-only-elsewhere`; test 13 | `c19`: neither write has a `tool_result`. `c17`: the `repo-b` write has none. `c12`: 1 of 2 has none. `c19`'s submodule dir `repo-a/p1000` is not a manifest root. `D:/fixture/scratch` IS a manifest root | Under S4-7, an unanswered write is not a write. So `c19` gives zero writes and `c17` gives one. The materialiser puts `.git` only on manifest roots, so the submodule write resolves to `repo-a`, and the scratchpad write becomes `Checkout(scratch)`, not `OutsideAnyCheckout` | Ask E1 to re-cut `c12`/`c17`/`c19` with their `tool_result` lines kept, and to say in the manifest which roots are checkouts (not `scratch`) and where a submodule `.git` goes. Until then, the rows assert the S4-7 outcome on the fixtures as they are |
| F9 | The materialiser: copies `fixtures/caller-transcripts/<case>/projects/**`; rewrites the root prefix *"ordinal, forward slashes"*; the closed list of helpers | (a) Cases are folders `c01`–`c20`; the name is in `manifest.json`. (b) `c10` and `c12` hold `sessions/<pid>.json`. (c) 13 of 20 cases are `backslash+slash`, and every write path in c06 and c12–c18 and c20 is `D:\fixture\…`. (d) A copied file's last-write time is the copy time. (e) No fixture is multibyte | (a) `<case>` does not resolve. (b) E3.S2's fixture checks need the registry. (c) An ordinal forward-slash prefix misses backslash paths, which then point at the real `D:\fixture`. On Linux, a `D:\` path is `Unresolved`. (d) `WrittenSince` is never exercised. (e) Tests 11, 27, 28, 34, 38 and F5 alter fixtures with no helper, and test 31's multibyte line does not exist. E3.S5 finds the helpers by reflection, so the list must be complete | Resolve the name to the folder through `manifest.json`. Copy `sessions/` as well. Match roots with the separators folded (as `allowlist.json`'s `fixture-path` rule does), and write the temp root in the platform's form. Set each file's last-write time from its last stamp. Add the helpers `SetEffort(n, value\|absent)`, `SetAgentId(file, n, id)`, `SetMetaKey(id, key, value)`, `SetSidechain(n, bool, agentId?)`, `SetStamp(n, instant)`, `PrependLine(file, json)` and `PutMultibyte(n)` |
| F10 | The `no-effort-lines` row and test 10: every message of the stripped lines reads `Effort == "unknown"` | `c08`'s manifest: *"the only assistant lines without an effort field are synthetic ones"*. The synthetic lines' `message.id` is the sentinel (c01, c05, c08) | S4-11 drops synthetic lines, so `c08` yields no `unknown` message, and test 10 observes nothing. S4-11 is also what keeps the sentinel out of `MessageId`, and no test pins that | Test 10 strips `effort` from a real line with `SetEffort` (F9). Add a test: *a synthetic line's id never reaches `Messages`* (c01, c05, c08) |
| F11 | The `message-as-several-lines` row: *"`Tokens` = per-term max (= the last line)"*. Test 1 is named `…_with_its_final_usage` | Q3: on WSL, 16 ids have a last line below the maximum. Fixture `c03` is that case. Tokens are RANKS (0 stays 0) | "= the last line" is refuted. No test or break-it catches "take the LAST line" on a shipped shape | Drop the parenthesis and rename test 1. Add `A_message_whose_last_line_is_below_its_maximum_is_counted_by_its_maximum` (c03), with the break-it "take the last line's usage". Test 2 asserts `Output < sum` only for ids whose output rank is above 0 |
| F12 | Test 8 and the `nested-agents` row: a subagent's `Requested` equals its meta's `model` | `c07`'s depth-2 meta has no `model` (F6). Its lines' `requestedModel` is a real id (`claude-sonnet-5-5`/`claude-fable-5-1`) | Test 8 holds only for the depth-1 agent of `c07` | Test 8 names the depth-1 key. The depth-2 key asserts the fallback to the lines' `requestedModel` |

Nothing in Q1, Q5, Q7, Q8, Q9 or the `is_error` facts disagrees with the spec. S4-1 and D22 (a torn tail is not
`partial`) stand: E1 saw 0 torn tails in 9 379 samples.
