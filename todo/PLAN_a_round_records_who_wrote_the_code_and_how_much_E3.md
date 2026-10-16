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
> 5. **E3.S4 (Fable's spec) was amended by Fable on 2026-10-16** against §7 F1–F12, each decided from the fixture bytes
>    and the RESULTS rows (the table *Fable's decisions on §7* inside E3.S4). §3's contract copy is realigned to it. Coding
>    E3.S4 waits for E1's re-cut PR (§8 item 1, branch `fix/e1-fixture-results`) to be merged — the coordinator's
>    decision of 2026-10-16 (G15); there is no interim. The plan gate's items G1–G15 (§9) are decided inside E3.S4 too.
> 6. **No test uses a real date.** The fixtures use a synthetic clock (2000-01-01, one second per rank), and every
>    clock in a test is taken from the fixture's own instants, never from `DateTime.UtcNow` (§4).
> 7. **§6 records three new defects in the parent** (items 12–14). The parent itself is not edited here. *(Applied to the
   parent by the main session on 2026-10-16, with the rest of §8.2 and the E1 follow-up recorded in its E1 entry.)*

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
landed them** (E1.S5, #828). §7 and §9 are decided (Fable, 2026-10-16); E3.S4 is coded after E1's re-cut PR (§8 item 1,
`fix/e1-fixture-results`) is merged (G15, the coordinator's decision) — no interim.

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
| "Holds a `.git` file or folder" | `src_mcp/runners/Worktrees/SubmodulePopulator.cs:133-134` (`HasGit`, `internal`; `runners` gives `InternalsVisibleTo` only to `CoaiMcp.Tests` and `FakeCli`, `CoaiMcp.Runners.csproj:44-48`, not to `CoaiMcp`) | *Revised 2026-10-16:* `SubmodulePopulator` itself is an `internal sealed class` (`SubmodulePopulator.cs:24`), so a `public` method on it still does not reach `CoaiMcp`. The body moves to a new `public static class GitMarker` in `src_mcp/runners/Worktrees/GitMarker.cs`, and both callers (`SubmodulePopulator.cs:97`, `Snapshots/SnapshotListing.cs:115`) call it (S3) — reuse move 2, extract the shared half, no copy |
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
| Q6 | The largest session is **650 MB** (273 files, 187 k lines). A full parse takes p50 **30.5 s**. The day-gated parse takes **19.9 s**, and the gate keeps 14 % of the lines. The largest WSL session is 235 MB: 9.6 s full, 7.8 s gated. The p99 session is 91 MB (Windows) and 124 MB (WSL); 4 and 5 sessions are ≥ 100 MB. *(Node, every file of the session, every line decoded: `scripts/lib/caller-transcripts.mjs:563-582`. E1/5: Node's time is not the C# reader's; E5 measures again.)* | A gate that still reads and decodes every line saves a third at best. The reader must skip BEFORE it decodes. File-level: the last-write time. Line-level: the stamp, tested on raw bytes, with no `JSON.parse` and no string. The byte budget meets these sessions every time, so it is charged on the slice only (parent D7). E3.S4 step 1 charged it on every line, before the gate; Fable's amendment of 2026-10-16 (F1, F2) charges it on the slice and uses E3.S1's byte gate | E3.S1; §7 F1, F2 |
| Q7 | Windows: 587 `Agent` tool uses and 778 metas. The 191 extra metas are the nested launches, at depth 2–3. WSL: 844 uses, 857 metas, 17 nested, 4 uses with no meta (refused launches) | S4-5 and the Agent-count check (step 8a after the 2026-10-16 renumbering) hold. Depth 3 exists, so no rule may assume depth ≤ 2 | — |
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
without updating E4's stories document. **Realigned 2026-10-14 to E3.S4's spec, and again 2026-10-16 to Fable's
amendment** (F1, F4, F6, F7): the block below is a copy of the spec's contract; members the spec added are marked
*added*. What changed on 2026-10-16: `ReadDiagnostics` has new members and lost `BytesRead`/`LinesSkippedByDayGate`;
`meta without model` is a COUNT, not a reason; `Requested`, `Model` and `Effort` are shape-gated.

```csharp
// src_mcp/src/Store/ObservedSession.cs
public sealed record ObservedSession(
    CallerSessionId Session,                         // E3.S2
    ObservationState State,
    DateTime? FirstLineUtc,                          // the main file's first parseable line WITH a timestamp, in FILE order, any type
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
    string ToolUseId, string AgentType, string RequestedAlias /* meta.model when it is on the alias shape | "" */,
    DateTime? LaunchedUtc /* the agent file's first parseable line WITH a timestamp; null for main and for a meta without a file */,
    DateTime? CalledUtc   /* added: the launching Agent call's message instant, when that call was parsed */,
    bool TranscriptPresent /* added: false for a meta whose agent-<id>.jsonl is missing */);

public sealed record ObservedMessage(string AgentKey, string MessageId, DateTime Utc /* earliest line */,
    string Requested /* meta alias, else the earliest line's requestedModel when it is on the model-ref shape, else "" */,
    string Model /* message.model when it is on the model-ref shape, else "" */,
    string Effort /* the earliest line's effort when it is one of low|medium|high|xhigh|max, else "unknown" */,
    TokenUsage Tokens /* per-term MAX over the id's lines */,
    bool UsageSeen /* added: false when no line of the id carried a usage object; Tokens are then zeros, not evidence */);

public sealed record TokenUsage(long Input, long CacheCreation, long CacheRead, long Output);
public sealed record ObservedWrite(string AgentKey, DateTime Utc /* its message's instant */, string Tool, WriteTarget Target);
public sealed record UnobservedLaunch(string ParentKey, string ToolUseId, DateTime? Utc, string Reason); // added
public sealed record ReadDiagnostics(                                                                   // added; all counts
    int FilesRead, int FilesSkippedByLastWrite, long BytesScanned, long BytesInSlice, int LinesSkippedBeforeParse,
    int LinesTooLong, int ReadFailures,                                                                 // G2: mirrored from ScanMeter; each also a reason
    int TornTails, int SyntheticLines, int MetasWithoutModel, int ValuesOffShape,
    int WriteCallsRefused, int WriteCallsUnanswered, int MessagesWithoutUsage, int MessagesDroppedAtBudget);

public sealed record ObservationState(ObservationKind Kind, IReadOnlyList<ObservationReason> Reasons)
{ public string Render() => …; }   // "observed" | "partial: <code>[ ×N][, <code>…]" | "unobserved: <code>"
public enum ObservationKind { Observed, Partial, Unobserved }
public sealed record ObservationReason(string Code, int Count);

public sealed record ReadBudget(long MaxBytes /* charged on the SLICE */, TimeSpan MaxTime /* over the whole read */, TimeProvider Clock)
{ public long MaxLineBytes { get; init; } = 64 * 1024 * 1024;   // G2: handed to ScanMeter.MaxLineBytes; a test lowers it to plant a too-long line
  public static readonly ReadBudget Unbounded = new(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System); }

public static class ObservationReasons               // THE closed vocabulary — E3.S5 reflects over this one class
{
    // unobserved — E3.S2's, re-exposed by const aliasing so one class holds every code
    public const string NoSessionId = CallerTranscriptLocation.NoSessionId;
    public const string StatedId = CallerTranscriptLocation.StatedId;
    public const string TranscriptNotFound = CallerTranscriptLocation.TranscriptNotFound;
    public const string TranscriptAmbiguous = CallerTranscriptLocation.TranscriptAmbiguous;
    public static string NoReaderFor(string vendor) => CallerTranscriptLocation.NoReaderFor(vendor);
    public const string TranscriptEmpty = "transcript empty";               // added: a main file with no parseable timestamped line
    // partial — `meta without model` is NOT here any more (F6): it is Diagnostics.MetasWithoutModel
    public const string AgentCallWithoutTranscript = "agent call without transcript";
    public const string MetaWithoutTranscript = "meta without transcript";
    public const string AgentWithoutMeta = "agent without meta";
    public const string TornLine = "torn line";
    public const string TornMeta = "torn meta";                             // G1: a meta file that does not parse
    public const string LineTooLong = "line too long";                      // G2: a line over MaxLineBytes, skipped by E3.S1
    public const string ReadFailed = "read failed";                         // G2: a file whose read failed after the open
    public const string ReadBudget = "read budget";
    public const string InconsistentTranscript = "inconsistent transcript";  // one code, the FIVE triggers listed in §3.2
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

**The five triggers of `inconsistent transcript`** (one code; each is a layout assumption that broke, so a counted
value is uncertain — G10): (1) an `assistant` line that carries an `agentId` different from its file's key (S4-4);
(2) one `message.id` seen in two FILES — kept once, keyed to the file that saw it first in read order, main first then
agent files ordinal (S4-21); (3) the `toolUseId` join and a meta's `parentAgentId` naming different parents (S4-5);
(4) an `assistant` line with no parseable `timestamp` (step 4); (5) two PRESENT values of `message.model`,
`requestedModel` or `effort` differing across one id's lines (S4-14; an absent value never differs, G14).

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

*What "never materialised" covers (2026-10-16, plan gate finding 6).* A yielded line is decoded and parsed whole, so
for the length of one `TryParse` the line's bytes — text included — sit in that line's buffer and its `JsonDocument`.
That buffer is transient: it is never kept past the line, never put in a member, never logged, and never handed to a
caller. The guarantee §3.4 makes, and that E3.S5 tests, is about what LEAVES the reader: no property, rendering,
`ToString()` or reason carries text, and no text-valued property of the document is read with `GetString()`
(only the names above are). The cheaper form that also removes the full-line `string` — `TryParse` over the raw UTF-8
bytes (`JsonDocument.Parse(ReadOnlyMemory<byte>)`), so `ReadSharedFrom` yields bytes and decodes nothing — **is the
decided form (Fable, G4)**: `ReadSharedFrom` yields `ReadOnlyMemory<byte>`, E3.S4 parses the bytes, and no full-line
`string` exists in the new reader at all. `AgentLog` keeps its `string` path through `ReadShared`.

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
  - `src_mcp/tests/AgentLogTests.cs:125-133`: the `Days` test moves to the new class with its three assertions
    unchanged; it calls `TranscriptLines.Days` and inlines the class's `Opened`/`PlanRound` instants
    (`AgentLogTests.cs:130-132` uses them), because they do not exist in the new class. Nothing else in that file
    changes.
  - New `src_mcp/tests/ATranscriptIsReadWhileItIsBeingWrittenTests.cs`.
- **Brief (Sonnet coder).**
  1. `TranscriptLines.ReadShared(string file) → IEnumerable<string>`: this is `AgentLog.ReadLines` as it stands,
     `FileShare.ReadWrite | FileShare.Delete`. `IOException` or `UnauthorizedAccessException` on open ends the
     sequence silently. Keep the remark that explains why.
  2. `TranscriptLines.TryParse(string line, out JsonDocument document) → bool`: false on `JsonException` (a torn line),
     never throws. The caller owns and disposes the document. Do not pre-read any property here. *(G4, Fable
     2026-10-16)* A second overload, `TryParse(ReadOnlyMemory<byte> line, out JsonDocument document)`, parses the raw
     UTF-8 bytes (`JsonDocument.Parse(ReadOnlyMemory<byte>)`) with the same contract; it is the one E3.S4 calls. The
     `string` overload stays for `AgentLog.Parse`.
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
     - Return true when ANY occurrence is at or after the bound. Return false only when every occurrence is before it.
       When the key does not occur, return TRUE: the line is parsed and the JSON decides. *(Revised 2026-10-16, own
       review: the top-level stamp comes after `message`, so a line torn in the middle usually has no key at all;
       skipping it would hide every torn MIDDLE line under a bound, and `partial: torn line` could never fire. Lines
       with no stamp — `last-prompt`, `ai-title`, `mode` — are few and small.)* When the 19 bytes are not in that form,
       treat them as "at or after" too.
     - Remark why it checks every occurrence: the top-level `timestamp` comes AFTER `message` (237 of 240 fixture lines,
       §2.3). So the first occurrence can be a key in a nested object, and the last can be one inside `toolUseResult`.
       With "any occurrence", a nested key can cost one extra parse. It can never cause the line's own stamp to be
       skipped.
  8. *(added 2026-10-16)* `TranscriptLines.ReadSharedFrom(string file, DateTime boundUtc, ScanMeter meter) →
     IEnumerable<ReadOnlyMemory<byte>>` *(G4: bytes, not strings — the yielded line is the raw UTF-8 of one line
     without its terminator, in a buffer the enumerator owns until the next `MoveNext`; where a bullet below says "decoded",
     read "yielded as bytes"; `BytesYielded` is the buffer's length plus the terminator)*. It opens the file exactly as `ReadShared` does (shared, never throws). Then:
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
     - `meter.Stop` (a `Func<bool>`) is asked after every 1 MiB scanned, and for a line that passes the gate it is asked
       AFTER the line's bytes are charged to `BytesYielded` and BEFORE the line is decoded or yielded; when it returns
       true, that line is not yielded and the sequence ends. So the slice never holds a line past the byte budget, and
       no line is decoded after the budget is spent. This is the budget's hook. E3.S4 decides what `Stop` checks (§7
       F1). *(Revised 2026-10-16 after plan gate finding 8: asking only after the yield let one line overshoot the
       budget by its whole length.)*
     - **A line longer than `meter.MaxLineBytes`** (default 64 MiB, a field of the meter) is never buffered whole: once
       the line under assembly passes the cap, the rest of it is scanned to the next `\n` without being kept, it is not
       yielded, and `meter.LinesTooLong++`. The scan bytes still count in `BytesScanned`. *(Plan gate finding 5: a
       growing buffer had no ceiling.)* How E3.S4 states such a line is listed for Fable (§9).
     - **A read that fails after the open** (an `IOException` or `UnauthorizedAccessException` from a block read) ends
       that file's sequence; `meter.ReadFailures++`. It never throws. *(Plan gate finding 9.)* `ReadShared` (item 1)
       stays `AgentLog.ReadLines` as it stands, so `AgentLog`'s behaviour does not change.
     - It never stops early on a stamp. Each line is judged alone, because stamps are not always in file order (§2.3).
  9. *(added 2026-10-16)* `internal sealed class ScanMeter { long BytesScanned; long BytesYielded; int LinesSkipped;
     int LinesTooLong; int ReadFailures; long MaxLineBytes = 64 MiB; Func<bool> Stop; }`. One instance serves one read, across every file of that read. **Considered and rejected,
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
  - `Only_the_days_a_window_touches_are_listed` (moved from `AgentLogTests.cs:125-133`, unchanged assertions, the
    two instants inlined).
  - `A_file_last_written_before_the_bound_is_skipped`: set the time with `File.SetLastWriteTimeUtc`.
  - All **13** `AgentLogTests` stay green **unchanged** apart from the moved test. That is the behaviour-kept proof.
  - *(added 2026-10-16, items 7–9; synthetic lines, instants chosen by the test, never the real clock)*
    - `A_line_stamped_before_the_bound_is_skipped`: line 1 at or after the bound (it is the head), then three lines
      before the bound, then one at or after it. Expect two yielded, `LinesSkipped == 3`, and `BytesYielded` equal to
      the two lines' byte lengths plus their terminators. *(Revised 2026-10-16: with a stale line first, the head rule
      yields it, and the old shape could not give "two yielded".)*
    - `A_line_without_any_stamp_is_yielded`: after the head, a line with no `"timestamp":` key, and a MIDDLE line cut
      before its stamp → both yielded, so a torn middle line reaches `TryParse`.
    - `A_line_longer_than_the_cap_is_skipped_not_buffered`: `MaxLineBytes` = 1 KiB, a 4 KiB in-range line between two
      short ones → the two short lines yielded, `LinesTooLong == 1`.
    - `The_stop_hook_is_asked_before_a_line_is_yielded`: `Stop` returns true once `BytesYielded` exceeds the first
      line's bytes → exactly one line yielded, and the second line is charged but not yielded.
    - `Any_stamp_at_or_after_the_bound_keeps_the_line`: a nested `"timestamp":` before the top-level one, and one after
      it. When either is at or after the bound, the line is yielded. When both are before it, the line is skipped.
    - `A_stamp_written_with_a_space_is_still_seen`: `"timestamp": "…"`.
    - `The_head_is_yielded_up_to_the_first_stamped_line`: line 1 is an `ai-title` with no stamp, line 2 is a
      `queue-operation` stamped before the bound, and line 3 is stamped before the bound. Expect lines 1 and 2 yielded
      and line 3 skipped.
    - `An_unterminated_last_line_is_always_yielded`: the last line has no `\n` and no stamp.
    - `Lines_out_of_order_are_judged_one_by_one`: stamps 11:00 (the head), 10:00, 12:00, 09:00, 10:45 and a bound of
      10:30. Expect 11:00, 12:00 and 10:45 yielded, `LinesSkipped == 2`. The read does not stop early. *(Revised
      2026-10-16: the old first line 10:00 was the head and is always yielded.)*
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
  `line.Length` → the bytes test goes red. (g) Drop the head rule → the head test goes red. (h) Return false when the
  key does not occur → the no-stamp test goes red. (i) Ask `Stop` only after the yield → the stop-before-yield test
  goes red. (j) Drop the line cap → the cap test goes red. "A skipped line is never
  decoded" cannot be seen from outside: a decode of bad bytes does not throw, it substitutes. So the reviewer checks
  that the one UTF-8 decode call comes after the gate, and E5 measures the time.
- **Done when.** The new class and all `AgentLogTests` are green; the break-its (a)–(j) were observed red and restored;
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
  - `src_mcp/runners/Worktrees/SubmodulePopulator.cs:131-134`: `HasGit` moves to a new
    `src_mcp/runners/Worktrees/GitMarker.cs` (`public static class GitMarker { public static bool HasGit(string path) }`,
    the same one-line body). `SubmodulePopulator.cs:97` and `Snapshots/SnapshotListing.cs:115` call `GitMarker.HasGit`;
    the remark at `:132` moves with it and names all three callers. *(Revised 2026-10-16, own review: the class is
    `internal sealed` at `:24`, and `CoaiMcp.Runners.csproj:44-48` gives `InternalsVisibleTo` only to the tests and
    FakeCli, so making the method `public` would not compile from `CoaiMcp`.)*
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
     - Otherwise walk up from the directory. The FIRST directory where `GitMarker.HasGit` is true →
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
- **Done when.** All scenarios and break-its are observed; `GitMarker.HasGit` is public, both old callers use it, and it is the only `.git` test in the new file;
  the whole suite is green.
- **Depends on.** Nothing. It may run beside E3.S1 and E3.S2.
- **Model.** Brief: Opus (this document) · code + tests: Sonnet · review: Opus · fixes: Sonnet.

---

### E3.S4 — The counting rules and the states · **Cornerstone — spec by Fable**

> **Revision box, 2026-10-16 (Opus).** The spec was checked against main `59463d73` and against E1's RESULTS and
> fixtures (§2.3). The citations it holds still resolve: `AgentLog.cs:229` (the torn-tail comment), `AgentLog.Parse`'s
> timestamp styles (`:216`), parent D6–D9 and D17/D18. Several rules and worked examples disagreed with the measurements
> or with the shipped fixtures; §7 lists them as F1–F12.
>
> **Amended by Fable, 2026-10-16, in place.** Every F is decided in the table *Fable's decisions on §7* below, from the
> fixture BYTES (read case by case, not from §2.3's summary) and the RESULTS rows. The amendment changes: the budget
> (step 1, S4-3), the gate (step 2, S4-9), the head rule (S4-10), `meta without model` (S4-17), the shape gate on
> `Requested`/`Model`/`Effort` (S4-18), two rules the fixtures forced (S4-15 orphan results, S4-16 repeated block ids),
> the materialiser, six worked-example rows, tests 1, 2, 7, 8, 10, 13, 24, 31, 38, 40, 41 and the new tests 44–51. §3
> is realigned. `ReaderVersion` stays 1: nothing has shipped. **Two defects are E1's and are owed before coding starts
> (§8)**; everything else is coded as written here.
>
> **Amended again by Fable, 2026-10-16, after the E3 plan gate** (§9 G1–G15; the table *Fable's decisions on §9*
> below): three new reasons (`torn meta`, `line too long`, `read failed`; S4-19, S4-20), the id-in-two-files trigger
> (S4-21) and the five triggers listed in §3.2, `AgentType` as its own shape, `TranscriptShapes` internal, the reader
> parses bytes (G4), one metered gate for bounded and unbounded reads (G6), the materialiser creates every write's
> folder (G7), `BeyondTheGuard` for every "past every line" test (G8), S4-13 narrowed and the last-write skip for agent
> files only (G12), present-values-only for S4-14 (G14), tests 33/42/51 reshaped and 52–54 added, and — decided by the
> coordinator (G15) — E1's re-cut as E1's own PR merged before E3.S4 is coded (§8: +4, no interim).
>
> A fixture named below is a manifest NAME. Its folder is `c01`–`c20` (§2.3).

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

The contract is the block in §3, which is this story's and was realigned on 2026-10-16 (F1, F4, F6, F7; G1, G2, G10).
It is not repeated here: one copy, in §3, is what E4 and E5 read. Three shapes it names are fixed here, as
`internal static` members (G13: `TranscriptMessages` and `TranscriptLaunches` call them) of `TranscriptShapes` in
`ObservedSession.cs`, and are the ONLY way a transcript string becomes a member (S4-18):
`ModelRef = ^(claude-[a-z0-9-]{1,40}|[a-z]{2,12})(\[1m\])?\z` (a model id, an alias, either with the long-context
suffix); `Efforts = { low, medium, high, xhigh, max }`; and *(G11)* `AgentType = ^[A-Z]?[a-z0-9][a-z0-9:._-]{0,62}\z` —
at most one leading capital, then lower-case name characters, so `general-purpose`, `Explore`, `Plan`, `claude`,
`statusline-setup`, `claude-code-guide`, `custom-1` and a plugin-prefixed `feature-dev:code-reviewer` pass, while a
sentence (a space) and `COAI-FIXTURE-SENTINEL` (a second capital) fail. A `message.model` that begins with `<` is the
synthetic marker (S4-11) and is tested before the shape.

`Render()` never pluralises: a count above one is written `agent call without transcript ×2`. §3.1's example text
(`2 agent calls without transcript`) is superseded — grammar over a closed vocabulary is text manipulation, and E3.S5
checks that every code is a `const`. E5 renders the card from the same method.

**Mapping onto E4's core records** (E4.S1, `CoaiMcp.Core.Authors`): `FirstLineAt = LaunchedUtc ?? CalledUtc`;
`TranscriptPresent` as is; E4's `UnobservedLaunch` list is `UnobservedLaunches` as is; `Effort` `"unknown"` as is;
`TokenTerms` from `TokenUsage` term by term. Nothing in the mapping decides anything.
*(2026-10-16)* **`FirstLineUtc` is positional, not a minimum.** It is the first stamped line in FILE order, because
under a bound only the head is read whole. Six fixtures start with a line stamped AFTER the next one (c06 L1 is `:04`,
L2 is `:01`; also c09, c13, c15, c16, c20). E4's before-open window must therefore open at
`min(FirstLineUtc, Messages[0].Utc)`, never at `FirstLineUtc` alone; E4.S2 is told so in its own amendment.

#### Decisions this spec closes

| # | Decision | Why |
|---|---|---|
| S4-1 | **A torn LAST line is not `partial`.** The last line a file yields that fails `TryParse` is a *torn tail*: counted in `Diagnostics.TornTails`, no reason recorded, the state unaffected. Any OTHER unparseable line is `partial: torn line`. The policy is one named constant, `CallerTranscript.TornTailCountsAsPartial = false`; E1.S4 measures how often a live read sees a tail, and the constant is flipped — with a `ReaderVersion` bump — only if the measurement shows tails that are NOT transient (see the remark the constant must carry) | A torn tail is the CLI's append racing our read (observed: both files whole at the instant of reading; `AgentLog.cs:229` calls it normal because `agent_log` reads during the main agent's own turn). The bytes it holds belong to a line being written NOW — at round completion, after round N's start — so under the evidence cutoff (parent §16 D6) that line is never evidence for the round being attributed. Marking most live rounds `partial` for a line that could not have counted would make `partial` useless as a signal. A torn line in the MIDDLE of a file is corruption, not a race, and lines after it were lost to nothing we can name → partial. The one case the rule reads as a tail wrongly — a dead session whose final line was never finished — loses one line after every round of that session |
| S4-2 | **The in-flight message is counted from the lines seen.** The torn tail's `message.id` is unknowable (it does not parse); the preceding parsed lines of whatever message is in flight stay counted at their per-term maximum | Dropping "the last parsed message" would be a guess that is wrong whenever the tail is a NEW message's first line. Evidence, not inference. Stated limitation: for a background agent still writing at the read instant, a live read and a later backfill can differ by that ONE message's output tokens; E7's idempotence check (backfill vs backfill) is unaffected |
| S4-3 | **A budget hit drops every message of the stopped file that could still have a line after the stop** — every id of that file whose latest seen stamp is after `stopStamp − StraddleGuard`, where `stopStamp` is the largest stamp parsed in that file — counts them in `Diagnostics.MessagesDroppedAtBudget`, drops that file's write candidates uncounted, and skips the Agent-count check (step 8a). Files and metas are listed BEFORE any line is read (step 3), so `Agents`, `UnobservedLaunches` from metas and `TranscriptPresent` are complete even after a stop. *(Amended 2026-10-16, F4; was "the id of the line it stopped at".)* | Acceptance 11: no message counted from some of its lines. Q4: a message's lines are NOT adjacent — p99 50 s apart, max 2.01 h — so any id already seen in the stopped file may have more lines after the stop; "present ⇒ complete" held only in the 88-second fixtures. The guard is the same 3 h bound as S4-9, so the two rules share one assumption. An id whose latest line is older than that cannot have a further line without spanning more than the measured maximum. After a stop, unread lines would have made every unmatched meta an "agent call without transcript" — a false reason; `read budget` already says the picture is incomplete |
| S4-4 | **The file wins over a line's `agentId`**, and the disagreement — a PRESENT `agentId` that differs from the file's key — is `partial: inconsistent transcript`; a line with no `agentId` (every main line) never disagrees (G10) | The file is the unit of launch; a line that claims another agent makes the per-agent token figure uncertain, which is what `partial` is for (principle below) |
| S4-5 | **`ParentKey` = the file holding the `toolUseId` join, else `parentAgentId`, else `"main"` when a meta exists, else `""`.** The join and `parentAgentId` disagreeing is `partial: inconsistent transcript` | `parentAgentId` is present only on nested metas (2 of 96); its absence on a meta IS the meta's statement that main launched it — a fact read, not a guess. Under a `fromUtc` bound the launching call may sit in a skipped line, so the meta's own statement must be enough |
| S4-6 | **An agent file with no meta is `partial: agent without meta`**; its launch counts, `RequestedAlias` and `AgentType` are empty, `ParentKey` is `""`, its messages' `Requested` falls back to the line's `requestedModel` | The parent link is an input to E4's rule 2 (child-of association); losing it changes a number. A Claude Code version that wrote no metas makes every session of that version partial — correct and honest, and E1.S4 Q2/Q7 say whether such a version is on disk |
| S4-7 | **A write call whose `tool_result` carries `is_error: true` is not a write; a write call with no `tool_result` is not a write either.** Both are counted (`WriteCallsRefused`, `WriteCallsUnanswered`). **Deviation from acceptance 8** ("every write-tool tool_use becomes one write") | A hook-refused `Edit` (the family's guards refuse writes outside their folders), an `old_string` not found, a missing file — none changed a byte, and counting them would make an agent that was refused every write `wrote here`. An unanswered call (interrupted, or torn at the tail) has an unknown outcome and is no evidence. Evidence of contribution is a write that happened |
| S4-8 | **Every block's instant is its message's instant** (the id's earliest line): writes, Agent calls, tool results | Keeps a write in the same window as the message that made it (E4 places both by instant); a message's blocks can be minutes apart on a long thinking turn |
| S4-9 | **The gate is E3.S1's exact-stamp byte gate, widened by `StraddleGuard = 3 h` below the bound**: `TranscriptLines.ReadSharedFrom(file, fromUtc − StraddleGuard, meter)`; the in-range test (S4-13) is still exact; `fromUtc == null` reads every line. No day list, no clock in the gate. *(Amended 2026-10-16, F2 and F3; was `Days(fromUtc − 1 h, max(now, fromUtc))`.)* | Acceptance 12 needs the EARLIEST line of a message to be parsed to know the message started before the bound. Q4 refutes "one message never spans an hour": the lines of one id span up to 2.01 h, with 3 ids over 1 h on each side; 3 h is 1.5 × the measured maximum. Q6 refutes the day gate as a saving: it still decodes every line and keeps 19.9 s of 30.5 s. `AgentLog.Days` also returns `[]` past a 7-day span (`AgentLog.cs:106-109`), which is every fixture read against a real clock — the gate would have been OFF in every test and the break-it could never go red. The exact gate has no clock, so `max(now, fromUtc)` and test 41's reason are gone |
| S4-10 | **A main file with no parseable timestamped line is `unobserved: transcript empty`.** The head of every file — every line up to and including the first one whose bytes hold `"timestamp":` — is always yielded and parsed (E3.S1 item 8), so `FirstLineUtc` and `LaunchedUtc` come from the first STAMPED line, not from line 1. *(Amended 2026-10-16, F5; was "the FIRST line of every file is always parsed".)* | Nothing was read; "observed zero" would claim a complete read of nothing. Folder facts: 1 of 21 Windows main files starts with an `ai-title`, which has no timestamp; under a bound the old exemption would have parsed that line and gated out the first stamped one, so `FirstLineUtc` would have moved to the first in-slice line or to null. c04 L4–L7, c05 L8–L11 and the other main files show stamp-less `last-prompt`/`ai-title`/`mode` lines in the body too; they parse and contribute nothing |
| S4-11 | **A `message.model` that is empty or begins with `<` is a synthetic line**: no message, no tokens, `Diagnostics.SyntheticLines++`, not partial | Acceptance 15. The CLI writes `<synthetic>` for an interrupted or failed call; nothing was spent and nothing was lost |
| S4-12 | **A main-file line with `isSidechain: true`** belongs to the agent named by its `agentId`; without one it is dropped and `partial: sidechain without agent id` | The pre-`subagents/` layout kept subagent lines in the main file. Counting them as main's would misattribute every subagent token; the rule is stated so an older fixture (E1.S4 Q9) has one |
| S4-13 | **The in-range test is `earliestLine >= fromUtc`**; `fromUtc == null` reads everything; writes follow their message. Under a bound, `Agents` (every file and meta), `FirstLineUtc`/`LaunchedUtc` (the head) and every META-derived `UnobservedLaunch` are complete whatever the bound; a CALL-derived one (`agent call without transcript`) exists only for an `Agent` block in a yielded line — a call stamped before `fromUtc − StraddleGuard` whose meta is also missing is not seen, and that is the stated limit of the bound. The last-write skip (`WrittenSince`) applies to AGENT files only; the main file is always read through the gate. *(Narrowed 2026-10-16, G12.)* | Acceptance 12; a launch belongs to the window of its first line and E4 needs every launch to tile windows. The main file holds `FirstLineUtc` and the launching calls, and skipping it would make test 42's second half read 1 launch where 2 exist |
| S4-14 | **Model, Requested and Effort come from the id's EARLIEST line**; a later line of the same id carrying a PRESENT value that differs from an earlier PRESENT value of the same field is `partial: inconsistent transcript`; an absent field never differs (G14: tests 10 and 11 strip `effort` from one line of an id, and that is `Observed`) | One API response has one model and one effort; a difference means the layout assumption broke and a counted value is uncertain. Every multi-line id in the twenty fixtures agrees on all three (checked 2026-10-16: 44 multi-line ids, 0 disagreements) |
| S4-15 | *(added 2026-10-16)* **A `tool_result` whose `tool_use_id` matches no parsed `tool_use` block of the same file is ignored**: no write, no launch, no diagnostics count, not `partial` | It answers a call the reader did not see — before the bound, or outside a cut. It is evidence of nothing the reader counts. Fixtures: c05 main L4 (`…2acc10`) and c17 main L2 (`…3dba83`) are such results; a rule that counted them as unanswered or refused would invent a diagnostic |
| S4-16 | *(added 2026-10-16)* **`tool_use` blocks are keyed by their `id`, per file; a repeated id is ONE call** — one launch join, one write candidate, its instant the earliest line that carried it | c07 main L1 and L5 carry the same `message.id` (`msg_0c3b…`) and the same `Agent` block id (`toolu_…e31154`) on two lines that differ only in `cwd` and one renamed key — a real re-logged line. Counted per block, that launch would be two "launched" calls against one meta; a write re-logged the same way would be two writes. The manifest's `agentToolUses` for c07 is 3 because it counts BLOCKS; the reader's count is 2 |
| S4-17 | *(added 2026-10-16, F6)* **A meta without `model` is a count, `Diagnostics.MetasWithoutModel`, never `partial`.** `RequestedAlias` is `""`, and that agent's messages fall back to their lines' `requestedModel` (shape-gated, S4-18). `ObservationReasons.MetaWithoutModel` is removed | Q2: metas WITH `model` are 100 % only on 2.1.283–2.1.285 and 2.1.300; elsewhere 70–98 % on Windows and as low as 32 % on WSL (2.1.284: 21 of 65). A launch without a `model` argument writes a meta without one — the model was never requested by alias, so nothing was LOST, and the observed model comes from the agent file's own lines (`message.model`, c04 L10 `claude-sonnet-5-5`, c07 L10 `claude-sonnet-5-5`). By this spec's own principle (below) that is a diagnostic. As a reason it would have marked a large share of real rounds `partial` and emptied the word. The parent's E3 done-when and §5 change (§6 item 13; §8.2) |
| S4-18 | *(added 2026-10-16, F7)* **A transcript string becomes a member only through a closed shape.** `requestedModel` and `message.model` must match `TranscriptShapes.ModelRef`; `effort` must be one of `TranscriptShapes.Efforts`; a meta's `model` must match the alias half of `ModelRef`. Off-shape → `""` (`Effort` → `"unknown"`) and `Diagnostics.ValuesOffShape++`, not `partial` | §3.4 says what may be read; it did not say what may be KEPT. The fixtures show why that matters: the main lines' `requestedModel` is the sentinel in c02, c03, c04, c07, c11, c12, c14 and c17 (`claude-opus-5-5[1m]` is off `allowlist.json`'s `model-ref` list), and c02/c03's agent lines carry it too (L13–L27). Without the gate the sentinel reaches `ObservedMessage.Requested` and E3.S5's no-string test is red; with it the reader is text-free by construction, whatever a future CLI writes into those fields. Grouping by an off-shape model id would also group by text. Nothing is lost: the value was not a model |

| S4-19 | *(added 2026-10-16, G1)* **A meta file that does not parse is `partial: torn meta`** and reads as ABSENT: its file, if present, is keyed with `ParentKey = ""`, `RequestedAlias = ""`, `AgentType = ""` and gets NO second `agent without meta` reason; without a file nothing else is recorded (its `toolUseId` is unknowable) | The parent link and the alias are LOST, so a reason; one loss, one reason. Reading the file as `agent without meta` too would count the same loss twice |
| S4-20 | *(added 2026-10-16, G2)* **A line over `MaxLineBytes` is `partial: line too long`; a file whose read fails after the open is `partial: read failed`.** Both counts come from `ScanMeter` (`LinesTooLong`, `ReadFailures`) at step 10 and are mirrored in `ReadDiagnostics`. `ReadBudget.MaxLineBytes` (default 64 MiB) is handed to the meter | Both LOSE lines the reader never saw (a skipped line may hold a message, a write or a launch); `torn line` would hide the cause, and the cause decides the remedy (a cap is a policy, a read failure is the disk) |
| S4-21 | *(added 2026-10-16, G10)* **One `message.id` seen in two FILES is `partial: inconsistent transcript`**; the message is kept once, keyed to the file that saw it first in read order (main, then agent files ordinal); its tokens are still the per-term maximum over every line | §2.2 and Q4: no id appears in two files and `--resume` replays none, so a second file holding an id is a layout break and the agent of that message is uncertain. Test 4's `CopyLineInto` is this trigger; main lines carry no `agentId`, so S4-4 alone could never have fired on it |

**The principle behind `partial`**: a reason is recorded when something was LOST (a line, a child, a parent link) or a
COUNTED VALUE is uncertain; a diagnostics count is enough when the reader could choose between readings and lost
nothing (a torn tail, a synthetic line, a refused write, a meta that never named a model, a value off its shape).

#### Fable's decisions on §9 (the plan-gate items G1–G15, 2026-10-16)

| # | Decision | Reason | Evidence |
|---|---|---|---|
| G1 | **Accepted** as S4-19: `partial: torn meta`, the meta absent, no second reason | the parent link is lost once | gate finding 3 |
| G2 | **Accepted, two codes** (S4-20): `line too long`, `read failed`; `ReadDiagnostics` mirrors `LinesTooLong`/`ReadFailures`; `ReadBudget.MaxLineBytes` reaches the meter so test 54 can lower it | a lost line is a reason; the cause must stay visible | E3.S1 item 8, gate findings 5 and 9 |
| G3 | **Accepted**: step 1 says `Stop` is asked after a line is charged and before it is yielded; the stop line is never seen, so S4-3's drop rule is unchanged and `BytesInSlice ≤ MaxBytes` always (test 31 asserts it) | the slice never overshoots | E3.S1 item 8, gate finding 8 |
| G4 | **Accepted**: `ReadSharedFrom` yields `ReadOnlyMemory<byte>`, `TryParse(ReadOnlyMemory<byte>)` parses them; no full-line `string` in the new reader; `AgentLog` keeps `ReadShared` | §3.4's transient buffer shrinks to the parser's own | §3.4 note; E3.S1 items 2 and 8 edited |
| G5 | **Accepted**: step 2 restated (a line with no stamp key is yielded); new test 52 — a middle line cut before its stamp, under a bound → `partial: torn line` | without it a torn middle line could never be seen under a bound, and E5 always passes one | E3.S1 item 7; 237 of 240 fixture lines put the stamp after `message` |
| G6 | **Accepted**: `ReadSharedFrom(file, DateTime.MinValue, meter)` for every file, bounded or not; `ReadShared` is `AgentLog`'s alone | one path, one meter, CRLF handled once | E3.S1 item 1 |
| G7 | **Accepted**: the materialiser creates the parent directory of every rewritten `file_path`/`notebook_path`; a root's `.git` placement is unchanged | S3 says a missing directory is `Unresolved`, never walked up | c14 agent L54 `D:\fixture\repo-a\p2\p3\p4.ts`, c17 L1 `repo-a\p1\p2\p3\p4.ts` and L9 `repo-b\p1\p2\p3\p6.ts`, c18 L1 `repo-a\p1\p2.md` |
| G8 | **Accepted**: "past every line" is defined as `Fixture.BeyondTheGuard` = largest stamp + `StraddleGuard` + 1 s; tests 40, 42, 48 use it; test 51 moves the agent file's last message 4 h later with `SetStamp` (helpers re-stamp the file's last-write time) so the slice is a strict subset and `WrittenSince` holds; the worked-example row is rewritten | inside the guard the gate yields everything in an 88-second fixture, so nothing could go red | §2.3 fixture span 88 s; S4-9 |
| G9 | **Accepted**: test 33 → c11 with the budget stop between L1 and L2 (without the 8a skip the refused call reads `agent call without transcript`); the `Requested = Model` row names 7 and 8 (depth-1 `fable` ≠ `claude-fable-5-1`), not 24 | a break-it that cannot go red proves nothing | c07: every `Agent` block has a meta; c04 agent L10–L25 `requestedModel` == `message.model`; c11 L2 `is_error: true` |
| G10 | **Accepted**: trigger (2) stated as S4-21, the tie "main first, then ordinal key", the five triggers listed in §3.2; S4-4 says a line without `agentId` never disagrees | test 4's trigger was undefined | main lines carry no `agentId` (every fixture) |
| G11 | **Accepted** with a stricter shape than proposed: `^[A-Z]?[a-z0-9][a-z0-9:._-]{0,62}\z` — the proposed `[A-Za-z][A-Za-z0-9:_-]` would admit the sentinel itself | a shape that admits `COAI-FIXTURE-SENTINEL` is no gate | fixture metas: `custom-1` (c01, c04, c05 ×2, c07 depth-2), `general-purpose` (c02, c03, c07, c08, c14); `allowlist.json` `agent-type` |
| G12 | **Accepted** (S4-13 narrowed): the skip is for agent files only; a call-derived `UnobservedLaunch` exists only for a yielded line; test 42 split into "inside the guard → 2" and "beyond it → 1, `Agents` complete" | the limit of a bound must be stated, not hidden | c05: the missing call `…dbe7d8` is main L3 at `:03` |
| G13 | **Accepted**: `internal static` | callers are other classes of the same assembly | — |
| G14 | **Accepted** (S4-14): only two present values differ; test 10 asserts `Observed` | an absent field is "not written", not a value | tests 10, 11 |
| G15 | **Decided by the coordinator**: the E1 re-cut (c12/c17/c19 results kept, manifest `checkouts`, the two guard tests, `model-ref` widening) is E1's OWN PR, branch `fix/e1-fixture-results`, merged BEFORE E3; §8 item 1 reads **+4** (c12 +1, c17 +1, c19 +2); the c17/c19 rows and tests 7 and 13 depend on that PR being merged, so the interim (`AnswerCall` as a stand-in, test 13a) is dropped — `AnswerCall` stays as a mutation helper | one source of fixtures | §8 |

#### Fable's decisions on §7 (2026-10-16)

One line per item: the decision, its reason, and the evidence it rests on (a fixture path under
`src_mcp/tests/fixtures/caller-transcripts/`, or a RESULTS row). "Accepted" means Opus's proposed answer in §7 is taken
as written; a deviation is spelled out.

| # | Decision | Reason | Evidence |
|---|---|---|---|
| F1 | **Accepted.** `MaxBytes` is charged on `ScanMeter.BytesYielded` (the slice); `MaxTime` runs over the whole read through `ScanMeter.Stop`; `ReadDiagnostics` carries `BytesScanned` + `BytesInSlice` and `LinesSkippedBeforeParse` | Parent D7 says the budget applies to the slice; charged before the gate, every long orchestrator session ends `partial: read budget` — the case the parent wanted to keep. The round pays for scan TIME, so time is charged on everything | RESULTS Q6: 650 MB / 235 MB largest sessions, p99 91 / 124 MB, 4 / 5 sessions ≥ 100 MB |
| F2 | **Accepted.** The day gate is replaced by `ReadSharedFrom(file, fromUtc − StraddleGuard, meter)`; `max(now, fromUtc)` and the clock leave the gate | The substring gate saves a third at best and still decodes every line; `Days` is `[]` past 7 days, so against a real clock it was OFF in every fixture test and break-it "drop the guard" could never go red; a torn tail ends before its stamp and a day gate would hide it | RESULTS Q6 (19.9 s of 30.5 s); `AgentLog.cs:106-109`; fixtures stamped 2000-01-01 |
| F3 | **Accepted: `StraddleGuard = 3 h`.** Test 38 becomes a `SetStamp` mutation with a 2.5 h span | A message that starts more than the guard before the bound is counted by its tail; 3 h is 1.5 × the measured maximum | RESULTS Q4: span p99 50 s, max **2.01 h**, 3 ids > 1 h per side |
| F4 | **Accepted, the first variant made exact** (S4-3): drop every id of the stopped file whose latest seen stamp is after `stopStamp − StraddleGuard`; count them; list files and metas before any line is read so a stop never leaves `Agents` incomplete | The simpler "drop every id first seen in that file" loses the main file's whole contribution on a stop in main, which is the common stop. The guard reuses S4-9's assumption rather than adding one | RESULTS Q4 (non-adjacent lines); in the fixtures every id of a stopped file is inside 3 h, so test 50 needs a `SetStamp` mutation to show an old id surviving |
| F5 | **Accepted.** The head rule of E3.S1 item 8 replaces the first-line exemption; `LaunchedUtc` and `FirstLineUtc` are the first STAMPED line; the `agentId` check (step 4) is made on the first parsed line that carries one | A stamp-less first line is real and the exemption would have gated out the stamped line behind it | Folder facts: 1 of 21 Windows main files starts with an `ai-title` (no timestamp); every fixture main file starts stamped, so test 48 is a `PrependLine` mutation |
| F6 | **Accepted** (S4-17): a count, not a reason; `RequestedAlias == ""` is normal; c04 and c07 read `observed` | Nothing is lost; the model comes from the lines; as a reason it marks most real rounds | RESULTS Q2 (32–98 % of metas have `model`); `c04/…/agent-7f003cca131bbfbf6.meta.json` and `c07/…/agent-2553a2f49d265a4e3.meta.json` have no `model`; their files' lines carry `message.model` |
| F7 | **Both halves, with different standing.** (b) is a READER rule and mandatory (S4-18): `Requested`, `Model`, `Effort` and `RequestedAlias` pass a closed shape or become `""`/`"unknown"`. (a) is an E1 follow-up (§8, item 2) so that test 7 can one day observe `[1m]` on a shipped shape; until then test 7 runs over a `SetRequestedModel` mutation | A reader that copies any string from a transcript into a member is text-free only while the fixtures happen to be; the gate makes it structural. Widening `model-ref` is harmless (the `alias` vocabulary already admits `opus[1m]`) but is a privacy decision E1 owns, and the eight fixtures must be re-cut | `requestedModel` is the sentinel on c02 main L1, c03 L1, c04 L1–L2, c07 L1/L5, c11 L1, c12 L1/L9, c14 L1, c17 L1/L6/L9, and on c02/c03 agent L13–L27; `allowlist.json` `model-ref` lacks `claude-…[1m]` |
| F8 | **Confirmed as a fixture defect; the rows assert the intended outcomes and E1 owes the re-cut (§8, item 1).** Interim: `AnswerCall(file, toolUseId)` is added to the materialiser so E3.S4 can derive the answered shape from c19/c17/c12 as they stand if the re-cut is late; test 43's totals are filled on whatever is on disk the day it goes green | Under S4-7/D23 an unanswered write is not a write, so c19 shows ZERO writes and c17 ONE — the opposite of what their `serves` lines claim. The materialiser cannot know that `repo-a/p1000` is a submodule root, and `scratch` must never get a `.git` marker | `c19/…/7fbc9bdd-….jsonl`: 2 lines, both `tool_use`, no `tool_result`; `c17/…/7d5bce54-….jsonl` L9 `Edit` into `repo-b`, no result; `c12/…/13389a71-….jsonl` L9 `Write`, no result; `manifest.json` c19 `fixtureRoots` = other, repo-a, scratch |
| F9 | **Accepted, all five**, plus two helpers Opus did not list (`SetRequestedModel`, `AnswerCall`) and one rule for Linux: a backslash remainder is folded to `/` only on a non-Windows host (Q8: WSL writes `/` only, so nothing real is lost), and kept as written on Windows so the mixed-separator fact is exercised. Each copied file's last-write time is set to its own largest stamp | A materialiser that misses the backslash paths points 13 cases at the real `D:\fixture`; a copied file's mtime is the copy time, so `WrittenSince` would never skip | `manifest.json` `dir` field; `c10/sessions/41017.json`, `c12/sessions/52044.json`; every write path in c06, c12–c18, c20 is `D:\fixture\…`; no fixture byte is non-ASCII |
| F10 | **Accepted.** Test 10 strips `effort` from REAL lines with `SetEffort`; new test 44 pins that a synthetic line's id never reaches `Messages` (c01, c05, c08) | S4-11 drops the only effort-less lines c08 has, so the fixture shows nothing; S4-11 is also what keeps the sentinel out of `MessageId`, and nothing pinned it | `c08/…/agent-a8830f6c7cb17b699.jsonl` L13: `message.model` `<synthetic>`, `message.id` = the sentinel, no `effort`; the same shape at c01 agent L33 and c05 agent L36; the manifest's `distinctMessageIds` excludes them (c01: 5, c05: 6, c08: 1) |
| F11 | **Accepted.** "(= the last line)" is dropped; test 1 is renamed `…_with_its_maximum_usage`; new test 45 over c03 with the break-it "take the last line's usage"; test 2 asserts `Output < sum` only where the id's maximum output rank is above 0 | The last line is below the maximum on 16 WSL ids; the fixtures hold that shape | RESULTS Q3; `c03/…/agent-f6f8c73bdde437cb3.jsonl` L22–L23: id `msg_872a…` outputs 3 then 1 |
| F12 | **Accepted.** Test 8 names the depth-1 key (`207bc560f3fb5a452`, meta alias `fable`, `Model` `claude-fable-5-1`); the depth-2 key (`2553a2f49d265a4e3`) asserts the fallback `Requested == "claude-sonnet-5-5"` from its lines | The depth-2 meta has no `model`, so the alias rule holds only for one of the two agents | `c07/…/agent-207bc560f3fb5a452.meta.json` (`model: fable`), `c07/…/agent-2553a2f49d265a4e3.meta.json` (no `model`, `parentAgentId` = the depth-1 key), c07 agent L10 `requestedModel` `claude-sonnet-5-5` |

#### The read algorithm

*(Rewritten 2026-10-16 for F1, F2, F4, F5, F6, F7 and S4-15/S4-16. The order changed: files and metas are listed
BEFORE any line is read, so a budget stop never leaves a launch unlisted.)*

0. **Unobservable location.** `where` is `NotObservable(session, reason)` → `Unobserved(reason)`, `Session` carried
   through, every list empty, `FirstLineUtc` null, diagnostics zero. **Nothing is opened.**
1. **The meter.** `ReadMeter(budget)` wraps ONE E3.S1 `ScanMeter` for the whole read and records
   `started = Clock.GetUtcNow()`. Its `Stop` is
   `() => meter.BytesYielded > MaxBytes || Clock.GetUtcNow() − started > MaxTime` — bytes are charged on the SLICE
   (what the gate yielded, raw bytes plus terminators, counted by E3.S1 item 8, never a char count), time on the
   whole read, skipping included. `meter.MaxLineBytes = budget.MaxLineBytes` (S4-20). E3.S1 asks `Stop` after every
   1 MiB scanned and, for a line that passes the gate, AFTER its bytes are charged and BEFORE it is yielded (G3): the
   line that trips the budget is never seen, so `BytesInSlice ≤ MaxBytes` always. When it says stop: the enumeration
   ends, S4-3 drops the stopped file's ids inside the guard and its write candidates, `ReadBudget` is recorded once,
   step 8a is skipped. `Diagnostics`: `BytesScanned`, `BytesInSlice` (= `BytesYielded`), `LinesSkippedBeforeParse`
   (= `LinesSkipped`), `LinesTooLong`, `ReadFailures`, `FilesRead`, `FilesSkippedByLastWrite`.
2. **The gate.** `bound = fromUtc is null ? DateTime.MinValue : fromUtc − StraddleGuard` (3 h, S4-9). EVERY file,
   bounded or not, is read through `TranscriptLines.ReadSharedFrom(file, bound, meter)` (G6; `ReadShared` is
   `AgentLog`'s), which yields raw UTF-8 lines as `ReadOnlyMemory<byte>` (G4). E3.S1 decides what is yielded: the HEAD
   (every line up to and including the first whose bytes hold `"timestamp":`), the unterminated tail, every line with
   ANY stamp at or after `bound`, and every line with NO stamp key at all (G5 — a `last-prompt`/`ai-title`/`mode` line,
   or a line torn before its stamp, which is what lets `partial: torn line` fire under a bound). A line over
   `MaxLineBytes` is never yielded (S4-20). Nothing here lists days and nothing here looks at a clock.
3. **The listing** (`TranscriptLaunches`, before any line): `<SessionFolder>/subagents/agent-*.jsonl` and
   `agent-*.meta.json`, each list sorted ordinal; a missing folder means a main-only session. Key = the name between
   `agent-` and the extension. Every meta is parsed now (they are a few hundred bytes each; 273 at most in the largest
   session) through `TryParse` over its bytes; one that does not parse → `TornMeta` count++ and it reads as absent
   (S4-19, G1). From a parsed meta: `agentType` through `TranscriptShapes.AgentType` (G11; else `""` and
   `ValuesOffShape++`), `toolUseId`, `parentAgentId`, `model` → `RequestedAlias` through the alias half of `ModelRef`
   (S4-18). No `model` → `MetasWithoutModel++` (S4-17). A meta with no file → `MetaWithoutTranscript` count++. A file
   with no parsed meta → `AgentWithoutMeta` count++ (S4-6), unless its meta exists and is torn (then `torn meta` is the
   one reason). `spawnDepth` is not read. `Agents` = `main` + every FILE key + every parsed-META key with no file
   (`TranscriptPresent = false`, `LaunchedUtc = null`). For each AGENT file — never the main file (G12) — with `fromUtc`
   set and `!TranscriptLines.WrittenSince(file, fromUtc)`: `FilesSkippedByLastWrite++` and only its HEAD is read in step
   5 (so `LaunchedUtc` is still known).
4. **The main file**, line by line as step 2 yields them: `TryParse(ReadOnlyMemory<byte>)` (G4); a torn line → step 6. The first parseable line
   with a parseable `timestamp` sets `FirstLineUtc` whatever its `type` (S4-10; positional — six fixtures start with a
   line stamped after the next one). Then by `type`: `assistant` → step 7 and the block scan (steps 8a, 9); `user` →
   the block scan for `tool_result` only; every other type, a sentinel `type` included → nothing more. A `timestamp`
   is parsed as `AgentLog.Parse` does (`AdjustToUniversal | AssumeUniversal`); an `assistant` line without one is
   `inconsistent transcript` and dropped.
5. **The agent files**, in name order after main (the order a budget stop truncates in). Per file: the first parsed
   line WITH a timestamp → `LaunchedUtc` (F5); the first parsed line that carries an `agentId` must equal the key (else
   `inconsistent transcript`; c07's clipped agent file starts with an `assistant` line that carries it, Q1's files start
   with a `user` line that does). A file skipped by last-write ends after its head. Otherwise it is read as step 4 with
   the file's key as the agent.
6. **Torn lines** (S4-1): `TryParse` false. The LAST line a file yielded → `TornTails++`, read ends normally. Any other
   → `TornLine` count++, read on. **Lost lines** (S4-20): after every file, `meter.LinesTooLong` and `meter.ReadFailures`
   grown since the file began become `LineTooLong` and `ReadFailed` counts.
7. **The message table** (`TranscriptMessages`): per `message.id`, across every file — earliest `timestamp`; the
   agent key (the file's; S4-4, S4-12); `Model`, `Requested`, `Effort` from the earliest line through the shapes of
   S4-18 (`Effort`: one of the five levels, else `unknown`; `perTurnEffort` is never read; `Requested` for a subagent
   is the meta's `RequestedAlias` when it is non-empty, else the line's shape-checked `requestedModel`, else `""`);
   `Tokens` as the per-term maximum over the id's lines, a missing term reading 0; `UsageSeen` true once any line
   carried a `usage` object. A synthetic line (S4-11: `message.model` empty or starting with `<`) is not absorbed. The
   table is keyed by id alone, so an id seen in two files is one message — keyed to the first file that saw it, and
   `InconsistentTranscript` count++ once per such id (S4-21, G10). The table also keeps, per file, the largest
   stamp parsed so far and each id's latest stamp — what S4-3 needs at a stop.
8. **Blocks** (`TranscriptLaunches`, `TranscriptWrites`): every `tool_use` block of an `assistant` line is keyed by its
   `id` per file; a repeated id is one call at its earliest instant (S4-16). Every `tool_result` of a `user` line is
   keyed by `tool_use_id`; one that matches no block of the same file is ignored (S4-15).
   - `ParentKey` per S4-5. `CalledUtc` = the message instant of the parsed `Agent`/`Task` block whose `id` equals the
     meta's `toolUseId`, else null.
   - 8a. The check (skipped after a budget stop), over every `Agent`/`Task` block parsed in any file, in range or not:
     joined to a meta by `toolUseId` → *launched* (a missing `tool_result` does not matter: c01, c07 and c14's main
     calls have none). Else its `tool_result` has `is_error: true` → *refused*: nothing recorded. Else → *missing*:
     `AgentCallWithoutTranscript` count++ and `UnobservedLaunch(the file's key, id, the call's instant,
     AgentCallWithoutTranscript)`. Every meta with no file (step 3) →
     `UnobservedLaunch(ParentKey, ToolUseId, CalledUtc, MetaWithoutTranscript)`.
9. **Writes** (S4-7, S4-8): each `tool_use` block for which `WriteTools.TryPath(name, input, out path)` is true is a
   candidate `(agentKey, messageInstant, tool, path, id)`. When the file is done: no `tool_result` for `id` →
   `WriteCallsUnanswered++`; `is_error: true` → `WriteCallsRefused++`; else
   `ObservedWrite(agentKey, instant, tool, CheckoutRoots.Resolve(path))`. ONE `CheckoutRoots` per read. Only a
   candidate whose message is in range (S4-13) becomes a write. After a budget stop the stopped file's candidates are
   dropped without a count.
10. **The state**: `Unobserved(TranscriptEmpty)` when the main file yielded no parseable timestamped line (S4-10);
    else `Partial(reasons)` when any REASON count is above zero, reasons ordered by code (ordinal); else `Observed`.
    Diagnostics never make a state.
11. **Ordering** (acceptance 14): `Agents` — `main`, then by (`LaunchedUtc ?? CalledUtc ?? DateTime.MaxValue`, Key
    ordinal); `Messages` — (Utc, MessageId ordinal); `Writes` — (Utc, AgentKey ordinal, position in its file);
    `UnobservedLaunches` — (Utc ?? MaxValue, ToolUseId ordinal).

What is read from a line is exactly §3.4's list plus `isSidechain` as a boolean; `cwd`, `gitBranch`, `attachment`,
`toolUseResult`, `message.content[].input` beyond the one path property, every `text`/`thinking`, and every object
key outside `message.content[]` are never materialised. What is KEPT is narrower still (S4-18). `tool-results/` is
never opened. `sessions/` is E3.S2's.

#### Worked examples — E1.S5 fixture → `ObservedSession`

`M`, `A`, `K`, `C` are the fixture manifest's `counts` (`distinctMessageIds`, `agentFiles`, `metas`, `agentToolUses`);
`Unbounded`, `fromUtc = null`, the location from `CallerTranscriptLocator.LocateStored(id, home)` unless said otherwise.

| Fixture (or mutation) | State | Agents | Messages / tokens | Launches, parents | Writes | Also |
|---|---|---|---|---|---|---|
| `message-as-several-lines` (c02) | `observed` | `1 + A` = 2 | `M` = 4 messages, each id once; `Tokens` = per-term max of its lines; `Output` < the sum over lines for every multi-line id whose maximum output rank is above 0 (three of them: L13–14, L22–23, L26–27) | one launch | — | `FirstLineUtc` = main L1's stamp; main's `Requested` is `""` (its `requestedModel` is the sentinel and fails the shape, S4-18; `ValuesOffShape == 1` for main's id); the agent's `Requested` is `opus` (the meta's alias) |
| `message-last-line-below-max` (c03) *(added 2026-10-16, F11)* | `observed` | 2 | id `msg_872a…` (agent L22–L23): outputs 3 then 1 → `Output == 3`; inputs identical across both lines | one launch | — | the shape Q3 saw on 16 WSL ids; the one fixture a "last line" rule fails on |
| `model-switch-in-window` (c06) | `observed` | 1 only (`IsObservedZero`) | two messages with distinct `Model` values (`claude-opus-5-5`, `claude-fable-5-1`); `Requested == ""` on both (2.1.286 writes no `requestedModel`) | — | one `Write` into `scratch` → `OutsideAnyCheckout` (answered at L5) | `FirstLineUtc` = L1's stamp `:04`, although L2 is stamped `:01` — positional, by design |
| `resumed-agent` (c14) | `observed` | the resumed key appears ONCE, `LaunchedUtc` = its first line | 21 messages; its messages span the gap in `rounds.json` | one launch (the main `Agent` call has no `tool_result`; the meta joins it) | one `Edit` into `repo-a` (L54, answered) | `Bash` results with `is_error: true` (L62) are not writes and not counted |
| `background-agent-across-round-start` (c01) | `observed` | one key, `LaunchedUtc` before the round start in `rounds.json` | 5 messages on both sides of that start; agent L33 is synthetic → `SyntheticLines == 1`, its sentinel id absent | one launch | — | E4 splits them; E3 does not |
| `nested-agents` (c07) | `observed` *(was `partial: meta without model` by the old step 7; S4-17)* | depth-1 key `207bc…`: `ParentKey = "main"`, `RequestedAlias = "fable"`; depth-2 key `2553a…`: `ParentKey` = `207bc…`, `CalledUtc` = that agent's call instant (`:02`), `RequestedAlias = ""` | 7 messages; depth-1's `Requested == "fable"` ≠ `Model == "claude-fable-5-1"`; depth-2's `Requested == "claude-sonnet-5-5"` (the lines' `requestedModel`, through the shape) | `K` = 2 launched — main L1 and L5 carry ONE block id (S4-16), so the manifest's `agentToolUses` 3 is 2 calls; `UnobservedLaunches` empty; `MetasWithoutModel == 1` | — | — |
| `missing-child-transcripts` (c05) | `partial: agent call without transcript, meta without transcript` | the meta-only key `953030…` present with `TranscriptPresent = false`, `LaunchedUtc = null`, `CalledUtc` set | `M` = 6; agent L36 synthetic | `UnobservedLaunches.Count == 2` (`…dbe7d8` missing; `…2f5916`'s meta has no file), each with its instant and reason | — | main L4 answers `…2acc10`, a block the cut did not keep → ignored (S4-15); reading went on: every other agent and message is present |
| `refused-agent-call` (c11) | `observed` | `main` only → `IsObservedZero == true` | 1 | 0 launches, not `C` = 1; `UnobservedLaunches` empty | — | the break-it's target |
| `meta-without-model` (c04) | `observed` *(was `partial: meta without model`; S4-17)* | that key's `RequestedAlias == ""`; `MetasWithoutModel == 1` | that key's messages: `Requested == "claude-sonnet-5-5"` (the lines', through the shape), `Model == "claude-sonnet-5-5"` | one launch | — | main L9 is a sentinel-typed line WITH a stamp: parsed, contributes nothing |
| `no-effort-lines` (c08) | `observed` | 2 | 1 message (main's); the agent's only assistant line is synthetic → no `unknown` effort is observable on this fixture (F10) | one launch | — | test 10 strips `effort` by mutation; test 44 pins the synthetic id out |
| `write-into-submodule-and-scratchpad` (c19), **after E1's re-cut PR (§8 item 1, merged before E3)** | `observed` | 1 | 1 | — | one `Checkout(<repo-a/p1000>)`, one `OutsideAnyCheckout`; no `Tool == "Bash"` | **As shipped on main today: neither call has a `tool_result`, so `Writes` is EMPTY and `WriteCallsUnanswered == 2`** (F8); this row depends on that PR (G15) |
| `writes-only-elsewhere` (c20) | `observed` | 1 | 1 | — | one `Write` into `scratch` → `OutsideAnyCheckout`; the `Bash` call is not a write | *(the old row said `Checkout(other root)`: `scratch` is a fixture root but never a checkout — F8)* |
| `two-repositories-one-session` (c17), **after E1's re-cut PR** | `observed` | 1 | 3 | — | two distinct `Where` values (`repo-a`, `repo-b`); both directories exist because the materialiser creates every write's parent folder (G7) | **As shipped on main today: ONE write (`repo-a`, L1, answered at L3); the `repo-b` `Edit` at L9 has no result → `WriteCallsUnanswered == 1`**; L2's result `…3dba83` is orphan → ignored |
| `registry-and-variable-disagree` (c12), read by its transcript id | `observed` | 1 | 2 | — | as shipped ONE write (`scratch`, L1); L9's `Write` has no result | the registry case itself is E3.S2's |
| `persistence-off` (c10, no transcript) | `unobserved: transcript not found` (from S2) | empty | empty | — | — | `FirstLineUtc == null`; nothing opened |
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
| *mutation: `SetStamp` makes one two-line message span 2.5 h; `fromUtc` 10 s before its second line* | `observed` | — | that id ABSENT (its first line is inside the 3 h guard, so it is parsed and places the message before the bound) | — | — | S4-9, F3; red at `StraddleGuard = 1 h` |
| any fixture, `MaxBytes` = the BYTE offset of the middle of a multi-line message (in `BytesInSlice`) | `partial: read budget` | complete — listed before any line | every id of the stopped file whose latest stamp is inside 3 h of the file's largest parsed stamp ABSENT (in an 88-second fixture: all of them); every id of a finished file complete; `MessagesDroppedAtBudget` = their number | no `agent call without transcript` reason; metas without files still listed | the stopped file's candidates absent and uncounted | S4-3, F4 |
| *the same, after `SetStamp` moves one finished message of the stopped file 4 h earlier* | `partial: read budget` | — | that old id PRESENT and complete; the recent ones absent | — | — | the positive half of F4 (test 50) |
| c14, `SetStamp` moves the agent file's last message 4 h later, `fromUtc` = its new stamp, `MaxBytes` = the SLICE's bytes + 1 (head + that message + tail), far below the file's total | `observed` | — | that one message | — | — | F1/G8: the budget is charged on the slice (test 51); inside the 3 h guard every line of an 88-second fixture is in the slice, so the row needs the mutation |
| *mutation: `PrependLine` puts a stamp-less `ai-title` line first, under a bound past every line* | `observed` | complete | empty | — | — | `FirstLineUtc` unchanged (F5, test 48) |
| *mutation: `SetRequestedModel` puts a sentence into a main line's `requestedModel`* | `observed` | — | that id's `Requested == ""`, `ValuesOffShape == 1` | — | — | S4-18 (test 49); and `claude-opus-5-5[1m]` IS admitted (test 7) |

#### Tests

RED first: the coder creates `CallerTranscript.Read` returning `Observed`, empty lists, `FirstLineUtc = null`, so every
test below fails on its ASSERTION (`Expected Messages to contain M item(s), but found 0`, `Expected Kind to be Partial
but found Observed`), never on a compile error.

**The materialiser** (`CallerTranscriptFixture.Materialise(name, TempDir)`; *rewritten 2026-10-16, F9*):

- `name` is a manifest NAME; the folder is `manifest.json`'s `dir` for it (`c01`–`c20`). An unknown name throws with
  the known names listed — never a silent empty home.
- It copies `fixtures/caller-transcripts/<dir>/projects/**` into `<home>/projects/` and `<dir>/sessions/**` into
  `<home>/sessions/` (c10 and c12 have one; E3.S2's two fixture checks need it), and returns the `ClaudeHome`, the
  parsed `rounds.json`, and the case's manifest entry.
- **Roots and folders.** Every manifest `fixtureRoot` becomes a directory under the temp root. A `.git` marker (an
  empty FILE, as a worktree has) is placed in each directory the manifest's `checkouts` list names (E1's re-cut PR,
  §8 item 1 — it is what makes `repo-a/p1000` a checkout for c19 and keeps `scratch` out). *(G7)* The PARENT directory
  of every rewritten `file_path`/`notebook_path` is created too (`repo-a/p2/p3` for c14, `repo-a/p1/p2/p3` and
  `repo-b/p1/p2/p3` for c17, `repo-a/p1` for c18): E3.S3 answers `Unresolved` for a missing directory and never walks
  up from one, so without this the c14/c17/c18 writes could not resolve to a `Checkout`.
- **`Fixture.BeyondTheGuard`** (G8) = the case's largest stamp + `StraddleGuard` + 1 s: the one value a test uses for
  "a bound past every line", because inside the guard the gate yields every line of an 88-second fixture and nothing
  about the gate can be observed.
- **Path rewrite.** Every `fixture-path` value (`cwd`, `file_path`, `notebook_path`) whose SEPARATOR-FOLDED, case-folded
  form starts with a fixture root has that prefix replaced by the temp root's real path. On Windows the remainder keeps
  its separators and drive-letter case exactly as written (13 cases are `backslash+slash`; Q8's mixed forms are the
  point). On a non-Windows host the remainder's `\` are folded to `/` (Q8: WSL writes `/` only, so nothing real is
  lost; left alone, `D:\fixture\…` would be `Unresolved` on Linux). The rewrite is done on the parsed JSON value and
  re-serialised, so escaping stays valid.
- **Last-write times.** Each copied `.jsonl` gets `File.SetLastWriteTimeUtc(file, its own largest parsed stamp)`, so
  `WrittenSince` is real: a bound after a fixture skips its files (test 39), a bound inside it does not. Every mutation
  helper that changes a file re-applies this after writing it (G8: `SetStamp` moves the largest stamp).
- **The mutation helpers are the ONLY way a test alters a fixture**, so E3.S5 can enumerate them by reflection
  (E3/11). Each takes a `TranscriptFile` selector first (`Main`, or `Agent(key)`), then its arguments; `n` is a 1-based
  line number of that file: `TruncateLastLine(f)`, `TruncateLine(f, n)`, `DeleteMeta(id)`, `DeleteAgentFile(id)`,
  `RemoveMetaKey(id, key)`, `SetMetaKey(id, key, value)`, `RewriteEveryCwd(root)`, `SetModelOnLine(f, n, value)`,
  `SetRequestedModel(f, n, value)`, `SetEffort(f, n, value | null)` (null removes the key), `SetAgentId(f, n, id)`,
  `SetSidechain(f, n, bool, agentId?)`, `SetStamp(f, n, instant)`, `PrependLine(f, json)`, `PutMultibyte(f, n)` (adds a
  3-byte character to a renamed `k<n>` string value, never to a structural one), `SetResultError(f, toolUseId)`,
  `DeleteResult(f, toolUseId)`, `AnswerCall(f, toolUseId)` (appends a `user` line with a `tool_result` for the id,
  stamped one second after the file's largest stamp), `CopyLineInto(n, agentId)`, `GrowOutputAcross(id)`,
  `TruncateBeforeStamp(f, n)` (G5: cuts line `n` just before its `"timestamp":` key, so the line has no stamp and does
  not parse), `CorruptMeta(id)` (G1: overwrites the meta with half of its bytes), `PutLongLine(f, n, bytes)` (G2:
  pads a renamed `k<n>` string value of line `n` to the given byte length). Every helper edits the parsed JSON of one
  line and re-serialises it (the two truncating helpers and `CorruptMeta` cut bytes); none writes a string a test typed
  into a text field.
- `SteppingClock(start, step)` is a `TimeProvider` that advances `step` per `GetUtcNow()` call — `FixedClock` is
  read-only by design and the time budget needs a clock that moves mid-read. Every clock and bound in these tests is
  derived from the fixture's own stamps (§4).

**`TheCallerTranscriptCountsEachThingOnceTests`** (fixture → guarantee; RED = the count or value the stub cannot give):

1. `A_message_written_as_several_lines_is_one_message_with_its_maximum_usage` — `message-as-several-lines`; `Messages.Count == M`; for a multi-line id, `Tokens` = the per-term max over its raw lines (the test computes it from the fixture). *(Renamed 2026-10-16, F11: "final" implied the last line.)*
2. `Summing_every_line_of_a_message_counts_its_output_again_and_that_is_the_point` — same fixture; for each multi-line id whose maximum output rank is above 0, the test sums `output_tokens` over the raw lines and asserts `Output < sum` and `Output == max`; it asserts that at least one such id exists. The refuted approach, named.
3. `A_message_whose_output_grows_across_its_lines_is_counted_by_its_largest_line` — `GrowOutputAcross`; `Output` = the last line's.
4. `The_same_message_id_in_two_files_is_one_message_and_the_first_file_wins` — `CopyLineInto(n, agentId)` copies a main `assistant` line into an agent file; the id once, `AgentKey == "main"` (main is read first), `Tokens` the per-term max over both copies, state `partial: inconsistent transcript ×1` (trigger 2, S4-21 — the copied line carries no `agentId`, so trigger 1 is not what fires; G10).
5. `A_resumed_agent_is_one_launch_with_later_messages` — `resumed-agent`; one `ObservedAgent` with that key; its messages span the gap.
6. `A_background_agent_running_across_a_round_start_is_one_launch` — `background-agent-across-round-start`; one launch; messages before and after the `rounds.json` start.
7. `Requested_and_observed_are_two_fields_and_mains_1m_suffix_shows_it` — `message-as-several-lines` with `SetRequestedModel(Main, 1, "claude-opus-5-5[1m]")` (the shipped value is the sentinel, F7; when §8 item 2 lands the mutation is dropped and the shipped line is used); `Requested` ends with `[1m]`, `Model` does not, `Model != Requested`.
8. `A_subagents_requested_model_is_the_metas_alias` — `nested-agents`, the depth-1 key `207bc560f3fb5a452`; `Requested == "fable"` (its meta's `model`) and `Model == "claude-fable-5-1"`. The depth-2 key `2553a2f49d265a4e3` (no `model` in its meta) reads `Requested == "claude-sonnet-5-5"`, its lines' `requestedModel`. *(F12)*
9. `A_model_switch_inside_one_agent_yields_messages_of_both_models` — `model-switch-in-window`.
10. `No_effort_on_a_line_is_unknown_not_high` — `message-as-several-lines`, `SetEffort(Agent(key), n, null)` on the first line of two real ids; those two messages read `unknown`, every other one `high`; the state is `Observed` (G14: the later lines' `high` is a present value against an absent one, not a difference). *(F10: `no-effort-lines`' only effort-less line is synthetic and is dropped by S4-11.)*
11. `Effort_is_never_carried_from_the_previous_line` — mutation: alternate lines with and without `effort`; the stripped ones read `unknown`.
12. `A_nested_agent_names_its_parent_and_a_depth_one_agent_names_main` — `nested-agents`; `CalledUtc` set on both.
13. `A_write_call_is_one_write_at_its_messages_instant_in_its_checkout` — `write-into-submodule-and-scratchpad` as re-cut by E1's PR (§8 item 1; `checkouts` names `repo-a/p1000`); `Checkout(repo-a/p1000)` + `OutsideAnyCheckout`; `Utc` = the message's earliest line (`:01`), the second block's line is `:02`. *(G15: no interim — E3.S4 is coded after that PR is merged.)*
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
24. `A_meta_without_model_is_counted_not_partial_and_the_model_comes_from_the_lines` — `meta-without-model`: `Observed`, `MetasWithoutModel == 1`, that key's `RequestedAlias == ""`, its messages' `Model == "claude-sonnet-5-5"` and `Requested == "claude-sonnet-5-5"`. *(F6)*
25. `A_torn_middle_line_is_partial_and_the_rest_is_read` — `TruncateLine(n)` with n before the last; `partial: torn line ×1`; lines after n counted.
26. `A_torn_last_line_is_a_tail_not_partial` — `TruncateLastLine`; `Observed`; `Diagnostics.TornTails == 1` (S4-1). Also over `torn-last-line` when E1 ships it.
27. `A_line_whose_agent_id_disagrees_with_its_file_counts_to_the_file_and_marks_the_transcript_inconsistent` — the first-line `agentId` of an agent file rewritten.
28. `A_parent_link_that_contradicts_the_meta_marks_the_transcript_inconsistent` — `nested-agents`, the nested meta's `parentAgentId` pointed at another agent; `ParentKey` = the join's file; `inconsistent transcript ×1`.
29. `A_main_transcript_with_no_parseable_line_is_unobserved_transcript_empty` — main file replaced by one torn line; empty lists.
30. `An_unobservable_location_is_carried_through_and_nothing_is_opened` — a `NotObservable(StatedId)` with a sentinel transcript present in the home; `Unobserved(StatedId)`, `FirstLineUtc == null`, `Diagnostics.FilesRead == 0`.
31. `The_read_stops_at_the_byte_budget_and_says_so` — `resumed-agent`, `PutMultibyte(Agent(key), n)` on a line before the cut (no fixture is multibyte, F9e — a char count would then not fire), `MaxBytes` = the BYTE offset into the middle of a multi-line message of the agent file (computed from the raw bytes); `partial: read budget`; every id of the agent file ABSENT (all inside 3 h of the file's largest stamp, S4-3), `MessagesDroppedAtBudget` = their number; main's id present and complete; `Agents` complete. *(F4)*
32. `The_read_stops_at_the_time_budget_and_says_so` — `SteppingClock(start, 1 s)` with `MaxTime = 2 s`; `partial: read budget` after the third yielded line.
33. `A_budget_hit_never_invents_a_missing_transcript` — `refused-agent-call` (c11), `MaxBytes` = the bytes of main L1 (so the stop falls between the `Agent` call at L1 and its `is_error: true` result at L2); the reasons are exactly `[read budget]` — without the 8a skip the call would read `agent call without transcript`. Also over `nested-agents` with the stop inside main: `Agents` still lists both agent keys with `TranscriptPresent == true`. *(G9: c07 alone could not go red, every `Agent` block there has a meta.)*
34. `A_sidechain_line_in_the_main_file_belongs_to_its_agent_id` — mutation: a main line given `isSidechain: true` and an `agentId`; its message's `AgentKey` is that id; without the `agentId` → `partial: sidechain without agent id`.
35. `Rendering_a_state_never_pluralises` — `Render()` of two reasons with counts 1 and 2 → `partial: agent call without transcript, meta without transcript ×2`; of `Observed` → `observed`.

**`TheCallerTranscriptReadsFromTheBoundTests`**:

36. `A_message_straddling_the_bound_is_not_counted_by_its_tail` — `fromUtc` strictly between a message's first and second line; absent.
37. `A_message_at_or_after_the_bound_is_counted_whole` — the positive twin; `fromUtc` equal to a first line's stamp; present with all terms.
38. `A_message_starting_long_before_the_bound_is_not_counted_by_its_tail` — `SetStamp` moves the FIRST line of a two-line message 2.5 h earlier than its second; `fromUtc` = 10 s before the second line; absent (S4-9 at 3 h parses the first line and places the message before the bound; red at `StraddleGuard = 1 h`, when only the tail is seen). *(F3; was the midnight shape, which the exact gate does not care about.)*
39. `An_agent_file_last_written_before_the_bound_yields_its_launch_and_no_messages` — the materialiser has set the file's last-write time to its largest stamp; `fromUtc` = that stamp + 1 s; the agent in `Agents` with `LaunchedUtc`, none of its messages, `FilesSkippedByLastWrite == 1`.
40. `The_first_line_instant_is_read_under_any_bound` — `fromUtc = BeyondTheGuard` (G8); `FirstLineUtc` unchanged, `Messages` empty, `Agents` complete, `LinesSkippedBeforeParse > 0`.
41. `A_bound_before_every_line_reads_what_the_unbounded_read_reads` — `fromUtc` = the fixture's first stamp − 1 day; member-for-member equal to the `fromUtc = null` read except `Diagnostics`. *(Replaced 2026-10-16: the gate has no clock any more, so "a bound after the clock" has nothing to throw; this is the gate's positive anchor instead.)*
42. `Agents_and_meta_launches_survive_any_bound_and_a_gated_call_does_not` — `missing-child-transcripts` (G12, two halves): with `fromUtc` = the largest stamp + 1 s (inside the guard) → `UnobservedLaunches.Count == 2`, `Agents` complete; with `fromUtc = BeyondTheGuard` → `Agents` complete, the meta-derived launch (`…2f5916`, `meta without transcript`) present, the call-derived one (`…dbe7d8`, main L3 at `:03`) ABSENT, `UnobservedLaunches.Count == 1`, state `partial: meta without transcript` only. The second half is the stated limit of S4-13, pinned.
43a. *(numbering continues below test 43 with the tests added 2026-10-16.)*

**`TheReaderVersionIsPinnedToTheFixturesTests`**:

43. `The_reader_version_is_pinned_to_the_fixtures_totals` — over EVERY fixture (enumerated from the manifest, count asserted > 0): Σ messages, Σ each token term, Σ launches, Σ writes, and the state kind per fixture, each a constant the coder fills from the first green run after hand-checking against the manifest's `counts` (`distinctMessageIds` excludes synthetic ids, so Σ messages equals Σ M; `agentToolUses` counts blocks, so c07's 3 is 2 calls). The failure message reads: *a counting rule changed — bump `ObservedSession.ReaderVersion` and update these totals in the same change*. The totals are filled on the fixtures as they are on disk the day the test goes green; the E1 re-cut (§8) changes Σ writes for c12/c17/c19 and is the one expected update.

**Added 2026-10-16 (F4, F5, F7, F10, F11, S4-15, S4-16, F1)** — in the class each belongs to:

44. `A_synthetic_lines_id_never_reaches_Messages` (*counts once*) — c01, c05, c08: no `MessageId` equals `allowlist.json`'s `sentinel`, `SyntheticLines == 1` in each, and the synthetic line's zero usage is in no total. The positive half: the raw agent file DOES hold the sentinel as a `message.id`.
45. `A_message_whose_last_line_is_below_its_maximum_is_counted_by_its_maximum` (*counts once*) — c03, id `msg_872a…`: `Output == 3` (its lines read 3 then 1), inputs as on either line. Break-it: take the last line's usage.
46. `A_tool_result_that_answers_no_parsed_call_is_ignored` (*says what it did not see*) — c05 (`…2acc10`) and c17 (`…3dba83`): `Observed`, `WriteCallsUnanswered`, `WriteCallsRefused` and every reason count unchanged against the same read with that result line deleted.
47. `A_repeated_tool_use_block_is_one_call` (*counts once*) — c07: launched calls == 2 (`Agents` holds 2 agent keys, `UnobservedLaunches` empty, no `inconsistent transcript`); and a mutation that duplicates c14's `Edit` line (same block id, result kept) still yields ONE write.
48. `A_stampless_first_line_does_not_hide_the_first_instant` (*reads from the bound*) — `PrependLine(Main, {"type":"ai-title"})` then `fromUtc = BeyondTheGuard` (G8: inside the guard every line is yielded and the head rule is invisible): `FirstLineUtc` equals the unmutated read's and `LinesSkippedBeforeParse > 0`.
49. `A_requested_model_off_the_closed_shape_is_empty_and_counted` (*says what it did not see*) — `SetRequestedModel(Main, 1, "a sentence with spaces")`: that id's `Requested == ""`, `ValuesOffShape == 1`, `Observed`; then `SetModelOnLine(Main, 1, "a sentence with spaces")` (no leading `<`, so not synthetic): `Model == ""`, `ValuesOffShape` counted again, the message still present with its tokens. The positive twin is test 7 (`claude-opus-5-5[1m]` admitted).
50. `An_old_message_survives_a_budget_stop_and_a_recent_one_does_not` (*says what it did not see*) — test 31's cut after `SetStamp` moves one finished two-line message of the stopped file 4 h before the file's other stamps: that id present and complete, every other id of that file absent.
51. `The_budget_is_charged_on_the_slice_not_on_what_was_skipped` (*reads from the bound*) — `resumed-agent`, `SetStamp` moves the agent file's last message (L88, one line) 4 h later; `fromUtc` = its new stamp (the file's last-write time follows it, so `WrittenSince` holds); the slice is the head, that line and the stamp-less lines, a strict subset; `MaxBytes` = the slice's bytes + 1 (computed from the raw lines the gate yields): `Observed`, `Messages` holds that one message, `BytesInSlice <= MaxBytes`, `BytesScanned > MaxBytes`. *(G8.)*

**Added 2026-10-16 after the plan gate (G1, G2, G5):**

52. `A_middle_line_torn_before_its_stamp_is_partial_under_a_bound` (*says what it did not see*) — `message-as-several-lines`, `TruncateBeforeStamp(Agent(key), n)` on a middle assistant line, `fromUtc = BeyondTheGuard`: `partial: torn line ×1` — the torn line has no stamp key, so the gate yields it (E3.S1 item 7) and `TryParse` refuses it. Break-it: skip a line with no stamp key → `Observed`.
53. `A_meta_that_does_not_parse_is_partial_torn_meta_and_its_file_still_counts` (*says what it did not see*) — `nested-agents`, `CorruptMeta(207bc560f3fb5a452)`: `partial: torn meta ×1` and no `agent without meta`; that key present with `ParentKey == ""`, `RequestedAlias == ""`; its messages counted; the depth-2 agent's `ParentKey` still `207bc…` (its own meta names it).
54. `A_line_over_the_cap_is_a_lost_line_and_says_so` (*says what it did not see*) — `resumed-agent`, `PutLongLine(Agent(key), n, 4 KiB)` on an assistant line, `ReadBudget { MaxLineBytes = 1 KiB }`: `partial: line too long ×1`, `Diagnostics.LinesTooLong == 1`, that line's id absent (or counted from its other lines), every other message present.

#### Break-its (plant the mutation, watch the named test go red, restore, report both messages)

| Plant (production code) | Red test(s) |
|---|---|
| Sum every line's usage per id | 1, 2 |
| Take the first line's usage | 3 |
| Key the message table by (file, id) | 4 |
| Count a launch per `WrittenSince` pass, per reopen, or per message gap | 5, 6 |
| `Effort` defaults to `"high"`, or to the agent's previous line | 10, 11 |
| `Requested = Model` when the meta has no alias | 7, 8 (depth-1: `fable` ≠ `claude-fable-5-1`); not 24, where c04's lines give `requestedModel == message.model` (G9) |
| Treat an id seen in two files as one message silently | 4 (`Observed` instead of `partial`) |
| Count an absent `effort` as a differing value | 10 (`Partial` instead of `Observed`) |
| Yield the line that trips the budget | 31 (`BytesInSlice > MaxBytes`) |
| Use `ReadShared` for the unbounded read | 31–33 (`BytesInSlice == 0`, the budget never fires) |
| Apply `WrittenSince` to the main file | 42 first half (1 launch, not 2), 40 |
| Skip a line with no stamp key under a bound | 52 |
| Parse the meta without `TryParse` | 53 (throws) |
| Buffer a too-long line whole / count it nowhere | 54 |
| Shape-check `agentType` with the alias shape | 8, 12 (`AgentType == ""` for `general-purpose`) |
| A refused `Agent` call recorded as `AgentCallWithoutTranscript` | 21 |
| A missing `Agent` call (no result, no meta) recorded as refused | 20 |
| `earliest >= fromUtc` replaced by "any line ≥ fromUtc" | 36 |
| `StraddleGuard = 1 h` | 38 |
| Return `Observed` after a budget hit | 31, 32 |
| Run step 8a after a budget hit | 33 (c11: `[read budget, agent call without transcript]`) |
| At a budget stop drop only the stop line's id | 31 (an id of the stopped file with a line after the cut is present) |
| At a budget stop drop every id of the stopped file | 50 |
| Charge `MaxBytes` on `BytesScanned` | 51 |
| `TornTailCountsAsPartial = true` | 26 |
| A middle torn line counted as a tail | 25 |
| Count a write whose result is `is_error: true` | 15 |
| A write stamped with its block's line instead of its message's instant | 13 |
| Read `cwd` into any member | 18 |
| `Unobserved(TranscriptEmpty)` replaced by `Observed` | 29, and 19 must still be green |
| Parse only line 1 of a file under the bound (drop the head rule) | 48, 40 |
| Skip the file entirely when `WrittenSince` is false | 39 (`LaunchedUtc` null) |
| `ParentKey` from `parentAgentId` before the join | 28 |
| `MetaWithoutModel` recorded as a reason | 24, and 12's `nested-agents` read must still be `Observed` |
| Take the last line's usage | 45 |
| Copy `requestedModel` / `message.model` without the shape | 49 (and E3.S5's no-string test on the shipped c02) |
| Count a `tool_result` with no parsed block as unanswered | 46 |
| Key `tool_use` blocks by position instead of `id` | 47 |
| Absorb a synthetic line into the table | 44, 16 |
| Ordinal file order replaced by `EnumerateFiles` order | 17 (on a platform whose enumeration order is not ordinal; on Windows NTFS it is — the summary says which was observed) |

- **Done when.** Every numbered test (1–54) is green over E1's fixtures and the listed mutations through
  `CoaiMcp.Tests.exe`; every break-it row was observed red and restored, with both messages in the summary;
  `ObservationReasons` holds every code the reader can produce and no longer holds `MetaWithoutModel` (E3.S5 will
  reflect over it); `TranscriptShapes` (`internal static`) is the only place a transcript string is admitted into a
  member; no `string` holds a transcript line in any new file (the reader parses `ReadOnlyMemory<byte>`, G4);
  `ReaderVersion == 1` with its totals filled; no `Directory.EnumerateFiles(…, AllDirectories)`, no `cwd`, no
  `description`, no `prompt`, no `toolUseResult`, no `DateTime.UtcNow`, no `Days(`/`Mentions(` in any new file; the
  whole suite is green.
- **Depends on.** E1.S5 (fixtures and the manifest) **plus E1's re-cut PR (§8, branch `fix/e1-fixture-results`),
  merged before E3.S4 is coded** — the c17/c19 rows, tests 7 and 13 and test 43's totals are written against it (G15);
  E1.S4 (RESULTS: Q2–Q4, Q6, Q7, Q9); E3.S1 (items 7–9 are what steps 1–2 call, with the byte-yielding form of G4);
  E3.S2; E3.S3.
- **Model.** Spec (scenarios + brief): **Fable** (this entry; amended by Fable 2026-10-16) · code + tests: Sonnet ·
  review: Opus · fixes: Sonnet.

---

### E3.S5 — Nothing leaves the reader as text, and the reader is documented

- **Goal.** An independent guard, designed by the judge and not by the cornerstone's spec writer. Over every E1 fixture,
  no string anywhere in an `ObservedSession` carries a byte of text, and every reason is from the closed vocabulary.
  `research/module_server.md` describes the reader as it is.
- **Scope.**
  - New `src_mcp/tests/NoTextLeavesTheCallerReaderTests.cs`.
  - `research/module_server.md`: a new subsection right after the `rounds.agent_log` paragraphs (`:3180-3194` on main
    2026-10-16; `:3007-3021` when this was written).
  - `src_mcp/src/Server/CallerDeclaration.cs:106-108`: the vendor array lifted to `internal static readonly
    KnownVendors` (item 5). No other product code.
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
     `CallerTranscriptFixture` (E3.S4's materialiser list — no copy of it here). Reflection FINDS the helpers but cannot
     choose their arguments, so this test holds one table, `name → (fixture, invocation)`, with one sample call per
     helper, and a first assertion that the set of public mutation helpers found by reflection EQUALS the table's keys —
     a helper added later turns this test red until it gets a row. Assert each produced code is a `const` of
     `ObservationReasons` — the ONE reason class, found by reflection — or `"no reader for " + v` where `v` is in
     `CallerDeclaration.KnownVendors` or is `"this vendor"` (E3.S2's fallback). `KnownVendors` is the inline array of
     `CallerDeclaration.VendorOf` (`CallerDeclaration.cs:106-108`) lifted to an `internal static readonly` field that
     `VendorOf` reads — the one product edit of this story (reuse move 1; the tests have `InternalsVisibleTo`).
     *(Revised 2026-10-16, own review: the old list was 11 of the materialiser's 20 helpers, reflection alone could not
     invoke them, and the vendor list was a private inline array.)*
  6. **The doc.** Write about 25 lines in the style of the surrounding section: the reader is keyed by session id
     (registry → variable → stored) and never by `cwd`. Cover the four states, the counting rules in one sentence each,
     what is read and what never is (§3.4), and the lower bound and budget. State that `agent_log` is unchanged and that
     nothing here is persisted until E5. Cite `CallerTranscript.cs` and `CallerTranscriptLocator.cs` by path.
- **Test scenarios.** `Every_fixture_holds_the_sentinel_in_its_raw_bytes`;
  `No_string_in_an_observed_session_holds_the_sentinel`; `Every_reason_is_from_the_closed_vocabulary`;
  `The_fixture_scan_found_every_fixture_E1_cut`.
  *(2026-10-16)* The no-string test **would have been red as the spec stood**: the fixtures' `requestedModel` is the
  sentinel in c02, c03, c04, c07, c11, c12, c14 and c17, and `ObservedMessage.Requested` copied it (§7 F7). Fable's
  S4-18 (the closed shape on `Requested`/`Model`/`Effort`/`RequestedAlias`) makes it green by construction, and that is
  the break-it this test owns: remove the shape and the shipped c02 goes red here. The synthetic lines' `message.id` is
  also the sentinel (c01, c05, c08); S4-11 keeps it out, and E3.S4's test 44 pins that.
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
E3.S2 ─┼─► E3.S4 (Fable spec; E1.S5 shipped; §7 decided; §8 item 1 for c17/c19) ─► E3.S5
E3.S3 ─┘
```

One branch and ONE commit for the epic. The coai gate runs once, over the whole epic (parent, `## Epics`).

| Parent's E3 done-when | Met by |
|---|---|
| a three-line message counts once | E3.S4 tests 1–3 and 45 (`c03`, F11 accepted) |
| a resumed agent is one launch | E3.S4 test 5 (`c14`) |
| meta without transcript, `Agent` call with neither, a torn MIDDLE line → `partial: <why>`, reading on; a torn LAST line → NOT partial, counted in `Diagnostics.TornTails` (S4-1, parent §16 D22); **a meta with no `model` → `observed`, counted in `Diagnostics.MetasWithoutModel`** (S4-17, F6 — the parent's done-when said `partial`; edited 2026-10-16, §8.2) | E3.S4 tests 20, 22 (`c05`), 24 (`c04`), 25, 26 (mutations) |
| no `effort` → `unknown` | E3.S4 tests 10 (a `SetEffort` mutation — `c08`'s only effort-less line is synthetic, F10), 11, 44 |
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
> - Items 12–14 are new. **Settled 2026-10-16** by §8.2's edits to the parent: 12 → §5 and D7; 13 → §5, the §12 test
>   row and the E3 done-when; 14 → the E1 epic goal.

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
    E5's budget must apply to the slice: D7 already says this, and E3.S4 step 1 did not do it (§7 F1; amended
    2026-10-16).
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

> **Decided 2026-10-16 by Fable** — the table *Fable's decisions on §7* inside E3.S4 holds the decision, reason and
> evidence per item; the spec is amended in place. This table stays as the record of the questions as they were asked.

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

## 8. E1 follow-up owed before E3.S4 is coded (Fable, 2026-10-16)

Two things E3 cannot fix by reading differently, because they are about what the fixtures CLAIM to show. Each names
the case, the change, the tool that makes it, and the test that proves it. **How it lands (decided by the coordinator,
2026-10-16, G15): as E1's OWN pull request — branch `fix/e1-fixture-results`, in progress — merged to main BEFORE
E3.S4 is coded, never inside E3's one commit.** E3.S4's c17/c19 rows, tests 7 and 13 and test 43's totals are written
against that PR's fixtures; there is no interim. Fixtures are published data: the Opus
reviewer reads every re-cut file before the commit, as E1.S5 requires. The `ApprovedAllowlistSha256` pin in
`ACallerTranscriptFixtureCarriesNoTextTests.cs:32` changes only for item 2 and is updated in the same diff, deliberately.

1. **Writes without their results, and roots without their kind** (F8). *Blocking* for the c17/c19 rows as intended.
   - **What.** Re-cut `c12` (`registry-and-variable-disagree`), `c17` (`two-repositories-one-session`) and `c19`
     (`write-into-submodule-and-scratchpad`) so that every write-tool `tool_use` the cut keeps has its `tool_result`
     line kept too. Rule for the cutter (`scripts/lib/transcript-sanitise.mjs`, beside `launchLines` at `:403-404`):
     the `user` line answering a kept `Edit`/`Write`/`MultiEdit`/`NotebookEdit` block is PINNED into the window exactly
     as a launching line is, so the answered shape survives a clip. Same salt, so every other byte of the three cases
     is unchanged.
   - **And.** `manifest.json` gains per case `"checkouts": [...]` — the directories that are a checkout when
     materialised, each a `fixtureRoot` or a `p<n>`-segment path under one, never `D:/fixture/scratch`. For c19 that
     is `["D:/fixture/other", "D:/fixture/repo-a", "D:/fixture/repo-a/p1000"]` — the submodule root the case is named
     for. Every other case lists its `repo-*` and `other` roots.
   - **Guard.** `ACallerTranscriptFixtureCarriesNoTextTests` gains `EveryKeptWriteCallHasItsResult` (over every main and
     agent file: each write-tool block's `id` is answered by a `tool_result` in the same file unless the manifest's
     `serves` declares an unanswered write — none does today) and `EveryCheckoutIsAFixtureRootOrUnderOne` (shape
     `^[A-Za-z]:/fixture/(repo-[a-z]|other)(/p[0-9]+)*\z`; `scratch` refused). `TheManifestCountsWhatTheFilesHold` is
     unchanged. Expected red before the re-cut: c12 (1 of 2 unanswered), c17 (1 of 2), c19 (2 of 2).
   - **Proof on the E3 side.** E3.S4 tests 13 (`Checkout(repo-a/p1000)` + `OutsideAnyCheckout`) and the c17 row (two
     `Where` values) pass on the merged files; against the fixtures on main today (`59463d73`) test 43's Σ writes is
     **+4** after the PR: c12 +1, c17 +1, c19 +2 (G15; the earlier "+3 minus the interim" was wrong arithmetic and the
     interim is gone).
2. **`requestedModel` on the main lines** (F7). *Not blocking*: S4-18 keeps the reader text-free whatever the fixture
   holds, and test 7 runs over a mutation.
   - **What.** Widen `allowlist.json`'s `model-ref` vocabulary with the long-context form of each listed model id —
     `claude-opus-5-5[1m]`, `claude-opus-4-8[1m]`, `claude-opus-4-1[1m]`, `claude-fable-5-1[1m]`,
     `claude-sonnet-5-5[1m]`, `claude-sonnet-5[1m]`, `claude-haiku-4-5-20251001[1m]` — the same seven the `alias`
     vocabulary already carries as `opus[1m]` etc. Nothing private: a model id is a product name. Re-cut the eight
     cases whose main lines carry it (c02, c03, c04, c07, c11, c12, c14, c17); with the same salt only those values
     change. Update `ApprovedAllowlistSha256`.
   - **Guard.** The existing probe that every vocabulary REJECTS private-looking strings is re-run; `model-ref` must still
     refuse `claude-opus-5-5[1m] please` and `D:/x[1m]`. `TheSentinelIsActuallyPresent` stays green (the Agent input's
     `description`/`prompt` keep the sentinel on every re-cut main file).
   - **Proof on the E3 side.** E3.S4 test 7 drops `SetRequestedModel` and asserts the shipped c02 main L1:
     `Requested == "claude-opus-5-5[1m]"`, `Model == "claude-opus-5-5"`.

Not asked of E1: a `torn-last-line` fixture (it cannot pass E1's JSON guard; S4-1 stays a mutation), a multibyte
fixture (`PutMultibyte` covers it), an older metaless layout (Q1: not on disk).

### 8.2 Parent-plan edits Fable endorses (recorded here; the parent is edited by the main session)

*Applied to the parent 2026-10-16, all five rows; the §12 test row "a meta with no `model`" was changed with them, and
the E1 follow-up of §8 is recorded in the parent's E1 entry.*

| Where in the parent | Today | Change to | Why |
|---|---|---|---|
| §5, the reader paragraph: *"the day-substring gate makes the bound cheap (§16 D7)"*; D7's reason | the day gate is the saving | *"the bound is cheap because a line is skipped BEFORE it is decoded — last-write time per file, then the exact stamp on raw bytes (E3.S1); the 64 MiB / 2 s budget is charged on the slice, time on the whole read"* | Q6: the substring gate keeps 19.9 s of 30.5 s at 650 MB; §6 item 12 |
| E3 epic *Done when*: *"a meta with no `model` → `partial: <why>`"*; §5 *States*: the `partial` causes | `partial` | *"a meta with no `model` → `observed`, `Diagnostics.MetasWithoutModel`; `requested` is the meta's alias when it gave one, else the line's `requestedModel`, else empty — an empty alias is the normal case on most versions"* | Q2: 32–98 % of metas carry `model`; S4-17; §6 item 13 |
| E1 epic *Goal*: *"the read time over 14 MB … ≤ 300 ms p50"* | the prediction | *"measured: the largest session is 650 MB (273 files); a full parse is 30.5 s p50 and a day-gated one 19.9 s — the prediction was refuted by two orders of magnitude (RESULTS Q6); E5 measures the C# reader's slice read again"* | §6 item 14; the goal line still reads as if it held |
| §5 *Tokens*: *"outputs only grow across the lines of one message"*; D8's reason | last line = maximum implied | *"only `output_tokens` differs across a message's lines (≈ 10 % of ids); the last line is NOT always the maximum (16 WSL ids) — the per-term maximum is the rule"* | Q3; S4-14; c03 |
| §5 *Writes*, D3: a write's checkout | as is | add: *"a `tool_result` that answers no parsed call is ignored; a repeated `tool_use` id is one call"* | S4-15/S4-16; c05, c17, c07 |

## 9. For Fable — E3.S4 items from the E3 plan gate (2026-10-16)

The coai plan gate ran on this document at `7e5348bd` (verdict `good_enough`; codex, vibe and one Gemini preset answered,
kimi was cancelled at the 10-minute limit), beside an independent Opus review that re-read the document against the
code and the fixture bytes. Items that touch E3.S1–S3, S5 and §3.4 were applied above. The items below touch E3.S4,
Fable's cornerstone, so Opus did not rewrite the spec: each names the defect, the evidence and ONE proposed answer.

> **Decided 2026-10-16 by Fable** — every item is answered in E3.S4's table *Fable's decisions on §9* (G11 with a
> stricter shape than proposed; G15 as the coordinator decided); this table stays as the record of the questions.

| # | Defect | Evidence | Proposed |
|---|---|---|---|
| G1 | Step 3 parses every meta but does not say what an unparseable meta is | plan gate finding 3 (accepted) | the meta reads as ABSENT (its file is then `agent without meta`, S4-6), plus a reason — something is lost (the parent link) — e.g. `partial: torn meta` as a new `const` |
| G2 | E3.S1 now skips a line over `ScanMeter.MaxLineBytes` (64 MiB) and ends a file on a mid-read I/O failure (`LinesTooLong`, `ReadFailures`); S4 does not say what either means | plan gate findings 5, 9 (accepted); E3.S1 item 8 | both LOSE lines → reasons: a too-long line `partial: torn line` (or its own code), a failed read `partial: read failed`; `ReadDiagnostics` gains `LinesTooLong` |
| G3 | Step 1 says E3.S1 asks `Stop` "after every yielded line"; E3.S1 now charges the line and asks BEFORE it yields, so the slice never passes `MaxBytes` | plan gate finding 8 (accepted) | align step 1's sentence; the `Stop` formula `BytesYielded > MaxBytes` is unchanged; test 31's cut then falls before the line that crosses the budget |
| G4 | `TryParse(string)` holds a full-line `string`; the cheaper form parses the UTF-8 bytes | plan gate finding 6 (accepted as §3.4's clarification) | `TranscriptLines.TryParse(ReadOnlyMemory<byte>)`, `ReadSharedFrom` yields bytes, no decode at all; `AgentLog` keeps its `string` path |
| G5 | Step 2 says the gate yields "the head, the tail, and every line with any stamp at or after `bound`"; E3.S1 item 7 now also yields a line with NO stamp key | own review: the top-level stamp comes after `message` (237 of 240 lines), so a line torn in the middle has no key and was skipped silently — `partial: torn line` could never fire under a bound, and E5 always passes one | align step 2's sentence; add a mutation test: `TruncateLine` cutting before the stamp, under a bound → `partial: torn line` |
| G6 | An unbounded read calls `ReadShared(file)`, which takes no meter, so tests 31–33 (unbounded) have no byte count without a second encode (and lose CRLF) | step 2; E3.S1 item 1 | when `fromUtc` is null call `ReadSharedFrom(file, DateTime.MinValue, meter)` |
| G7 | The materialiser makes only the ROOT folders; writes in c14 (`repo-a\p2\p3\…`), c17 (`repo-a\p1\p2\…`, `repo-b\p1\p2\…`) and c18 (`repo-a\p1`) go into folders never created, and by S3 a missing folder is `Unresolved` — the c14/c17 rows cannot give `Checkout` | own review, fixture bytes; E3.S3 brief item 3 | create the parent folder of every rewritten `file_path`/`notebook_path` |
| G8 | With the 3 h guard, "`fromUtc` past every line" skips NOTHING in an 88-second fixture: test 48 stays green with the head rule dropped (its break-it cannot go red), test 51 cannot pass (`BytesScanned` ≈ `BytesInSlice`), and the worked-example row "`MaxBytes` just above the SLICE" has the same flaw | own review | use `fromUtc ≥ largest stamp + StraddleGuard`; test 51 adds a `SetStamp` mutation so the slice is a strict subset while `WrittenSince` still holds |
| G9 | Two break-its cannot go red: "run 8a after a budget hit → 33" (every `Agent` block in c07 has a meta, so 8a adds nothing); "`Requested = Model` → 7, 24" (in c04 the lines' `requestedModel` equals `model`, agent L10–L25) | own review, fixture bytes | 33 → c11 with the stop between L1 and its error result; drop 24 from the second row |
| G10 | Test 4 and its row expect `inconsistent transcript` from `CopyLineInto` (main → agent file), but main lines carry no `agentId`, and step 7 makes an id in two files one message; the tie rule for `AgentKey` (same stamp on both copies) is unstated; §3's `InconsistentTranscript` says "the four triggers below" and lists none | own review; S4-4, step 7 | state the trigger (an id seen in two FILES is inconsistent) and the tie (main file wins, then ordinal key); list the four triggers in §3 |
| G11 | `agentType` is shape-checked against the ALIAS shape `[a-z]{2,12}`, which every value of `allowlist.json`'s `agent-type` vocabulary fails (`general-purpose`, `Explore`, `claude-code-guide`, `custom-<n>`) | step 3; `allowlist.json` `vocabularies.agent-type` | its own closed shape in `TranscriptShapes` (e.g. `^[A-Za-z][A-Za-z0-9:_-]{0,63}\z`, or the vocabulary itself) |
| G12 | S4-13 "never filtered by the bound" is too strong: an `Agent` call with neither meta nor file more than 3 h before `fromUtc` is never parsed. Step 3 does not say whether the last-write skip applies to the MAIN file; if it does, test 42 (c05) reads only main's head and finds 1 unobserved launch, not 2 | own review | state that the skip is for agent files only, and narrow S4-13 to "metas and agent files are never filtered; an `Agent` call before `bound` is not seen" |
| G13 | `TranscriptShapes` members are `private static`, so `TranscriptMessages` and `TranscriptLaunches` cannot call them | §*The contract* | `internal static` |
| G14 | Tests 10/11 strip `effort` from an id's first line while its later lines keep `high`; under S4-14 that difference is `inconsistent transcript` unless an ABSENT value is excluded | own review; S4-14 | state that only two PRESENT values that differ are inconsistent; test 10 asserts `Observed` |
| G15 | §8 item 1: "Σ writes moves by +3 (c12 +1, c17 +1, c19 +2 minus the interim)" adds to 4; and it is not said whether the E1 re-cut lands inside E3's ONE commit or as E1's own commit before it | own review | write the sum as +4 against the shipped files; say the re-cut is E1's own commit, before E3's |

Rejected in the gate round, with reasons recorded through `resolve`: a "20-byte stamp" (the 19 bytes are the stamp to
the second), a prerequisites section, `File.Exists` throwing (it never does), a check for the E1 fixtures' presence
(they are on this branch, and E3.S5's companion test fails when they are not), and the 3 h guard as unsafe (a stated,
measured assumption; the alternative is the whole-session read the bound exists to avoid).
