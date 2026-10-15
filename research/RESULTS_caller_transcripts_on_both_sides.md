# RESULTS — the caller's transcripts on both sides, measured before any reader is built

> Status: **predictions written 2026-10-15 before any harness read a transcript or a store; E1.S4's observations
> added the same day; Q4's probes, the folder facts and the three-round check run the same day on both sides. ONE item stays open: `/clear`, which exists only in an interactive session and needs an attended one.** Epic E1 of
> [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](../todo/PLAN_a_round_records_who_wrote_the_code_and_how_much.md).

- **Subject:** `dew_flow_connect_other_ais` at `6159b7f4` (origin/main when E1 started).
- **Harnesses:** `scripts/export-code-rounds.py` (one read-only query per store) and `scripts/measure-caller-transcripts.mjs`
  (counts only — never a value that could carry text).
- **Sides:** Windows (`%LOCALAPPDATA%\coai-mcp`, `~/.claude` of the operator) and WSL (`~/.local/share/coai-mcp`, the WSL
  `~/.claude`), each measured from inside itself.
- **Claude Code versions on disk (2026-10-15, from the harness):** Windows 2.1.283-2.1.289, 2.1.291, 2.1.292, 2.1.294-2.1.296, 2.1.300 (13 versions);
  WSL 2.1.283-2.1.287, 2.1.289, 2.1.291, 2.1.292, 2.1.294-2.1.296, 2.1.300 (12). Probes ran on Windows CLI 2.1.289 and WSL CLI 2.1.300. A WSL PATH CLI
  that was 2.1.197 until 2026-10-14 left no session in the window.

## Predictions

| Q | Question | Predicted | Why |
|---|---|---|---|
| Q1 | Parent §2's layout (main file, `subagents/agent-<id>.jsonl`, `.meta.json`) holds on both sides | holds for every session written by ≥ 2.1.28x on both sides; sessions from the old WSL PATH CLI may have agent files without metas | the layout was read on Windows only; the old CLI predates parts of it |
| Q2 | Share of assistant lines with `effort`, of metas with `model`, per version | ≥ 95 % of assistant lines and 100 % of metas on 2.1.28x+; markedly lower (possibly 0 %) on 2.1.197 | `effort` and alias metas are recent; WSL Sonnet logged `medium` on 2.1.296 |
| Q3 | Lines per `message.id`; are the four `usage` terms identical across a message's lines | ≈ 1.9 lines per id; inputs identical always; `output_tokens` grows on a few % of ids, the LAST line carrying the final figure | 1 992 / 1 041 measured; E3's reading of one folder found growth on ~162 of ~2 400 ids |
| Q4 | `/clear`, `--resume`, `--no-session-persistence` vs registry `sessionId`, inherited `CLAUDE_CODE_SESSION_ID`, transcript | `/clear` → a NEW session id and file, registry follows, a child started before keeps the old variable; `--resume` → the same id and file appended; no-persistence → registry and variable present, no file | 2026-09-12: every child inherits the variable and `CLAUDE_PID`; the registry is per pid |
| Q5 | Coverage: code rounds since 2026-09-30 whose caller has a transcript reaching the round's `started_utc` | **Windows: observed 392 / 398 rounds have a transcript (an observation, not a prediction).** Predicted: of those, ≥ 95 % reach the round; WSL ≥ 85 % covered, ≥ 90 % of those reach the round | WSL callers mix VS Code sessions with old-CLI and harness runs (no persistence) |
| Q6 | Whole-session sizes per side; read time over the largest: full parse p50, day-gated p50 | largest Windows session > 100 MB main + subagents, WSL > 50 MB; full parse p50 1–3 s; day-gated p50 ≤ 300 ms | one session was 14.8 MB + 63 MB before today's orchestration grew it |
| Q7 | `Agent` tool_uses vs metas vs agent files — disagreements and why | metas > tool_uses by the nested launches; tool_uses > metas by refused launches (the model guard's denials) | 82 vs 84 explained by 2 nested; the guard now refuses model-less launches |
| Q8 | Write-tool `file_path` forms; a subagent's `cwd` vs where it writes | always absolute; on Windows both `D:\` and `d:/`/mixed separators, drive-letter case as typed; `cwd` = the session's folder, writes elsewhere | observed in this session's own work across worktrees |
| Q9 | Top-level and `message.content[]` keys per version | a stable core (`type`, `message`, `timestamp`, `sessionId`, `cwd`, `agentId`, `isSidechain`, …) plus version-specific extras; no key carries text outside known text fields | the sanitiser's allowlist input; unknown until measured |

Observations are E1.S4's and go below this table, each against its prediction.

## Observations — E1.S4, 2026-10-15

Measured once per side from inside itself (Windows: node 24.18, 405 code rounds since 2026-09-30, 21 caller sessions,
799 files, 528 k lines; WSL: node 20.20, 516 rounds, 44 sessions, 901 files, 486 k lines). Counts only.

| Q | Observed | Against the prediction |
|---|---|---|
| Q1 | Layout holds on both sides: every agent file has its meta and every meta its file (Windows 778/778, WSL 857/857), no other entries. No session of the old WSL PATH CLI (2.1.197) falls in the window; versions seen are 2.1.283–2.1.300 | **holds**; the old-CLI branch was not exercised |
| Q2 | `effort` on ≥ 99.5 % of assistant lines in every version on both sides. Metas WITH `model`: 100 % on 2.1.283–2.1.285 (Windows) and on 2.1.300 (both); otherwise Windows 70–98 %, WSL as low as 32 % (2.1.284: 21 of 65) | effort **holds**; metas **refuted** — a launch without a `model` argument writes a meta without one. The model must come from the agent file's own assistant lines (`message.model`), the meta only names the REQUEST |
| Q3 | Lines per `message.id`: 2.18 (Windows), 2.41 (WSL); max 31 / 44. Only `output_tokens` ever differs, on 11.2 % / 9.2 % of ids. The last line holds the maximum on every Windows id; on WSL 16 of 6 745 differing ids it does NOT | inputs-identical **holds**; "a few %" **refuted** (~10 %); "last line carries the final figure" **refuted** on WSL — a reader takes the MAX per id, never the last line |
| Q4 | Windows (2.1.289) and WSL (2.1.300, Node 20, a Linux scratch folder under `/tmp`), identical results on both sides unless a figure names one. Headless `claude -p --model haiku` with `CLAUDE_CODE_DISABLE_BACKGROUND_TASKS=1`, a fresh folder per probe (its project folder the only one looked at), a Bash child reading `CLAUDE_CODE_SESSION_ID`, `CLAUDE_PID` and ONE `sessions/<pid>.json`. **Inheritance:** the child sees the variable and `CLAUDE_PID`; the registry `sessionId` EQUALS the variable; the `session_id` of the result EQUALS both and names the one new `.jsonl` (37 lines, 6 assistant lines, 3 message ids). A nested `claude -p` started WITH the parent's variable set still gets its OWN id (the child's value differs from the parent's), registry equal to it. **`--resume <id>`:** no new file; the same file grows 37 → 51 lines (+10 assistant lines, +5 message ids); the 3 + 5 message ids are 0 shared, so NO id is replayed; one `sessionId` in the file; registry and variable equal the ORIGINAL id. **`--no-session-persistence`:** exit 0, registry and variable present and equal, **no `.jsonl`** in the project folder (0 files). **WSL:** the same four observations (inheritance equal; `--resume` 36 → 57 lines, 2 + 2 distinct message ids, one `sessionId`; no-persistence 0 files). **`/clear`:** not measurable by a probe — it exists only in an interactive session; the plan's attended step and the WSL MCP-child environment are not run. Time span of one `message.id` over its lines, from the harness (`messages.spanMs`, `scripts/measure-caller-transcripts.mjs`, run 2026-10-15 on every caller session and agent file): Windows 70 603 multi-line ids, p50 1.7 s, p99 50 s, 498 over 60 s, 3 over 1 h, **max 2.01 h**; WSL 56 890 ids, p50 1.9 s, p99 52 s, 451 over 60 s, 3 over 1 h, **max 2.01 h** | `--resume` and no-persistence **hold**; the inheritance prediction holds for both. `/clear` **open** (needs a typed session). The span is new: lines of one message are NOT written together — a reader may not assume a message's lines are adjacent or close in time, and its attribution instant is the FIRST line's timestamp, never a window around it. **WSL MCP children** (a probe session's Bash child lists the descendants of the session process and reads `/proc/<pid>/environ` of each, names and equality only): 3 descendants, all readable, all hold `CLAUDE_CODE_SESSION_ID` equal to the session's; none holds `CLAUDE_PID` (only the tool shell has it). So a child started with the session carries the id the session had at its start — what it holds after `/clear` is the open question. Not settled: `/clear` itself, and whether a resumed session's old child keeps a stale variable |
| Q5 | Windows: 399 / 405 rounds found, none ends before the round; the 6 not found are the 6 rounds whose caller is codex (no Claude transcript exists). WSL: 516 / 516 | **holds and exceeds**: 100 % of Claude-called rounds on both sides |
| Q6 | Largest Windows session **650 MB** (273 files, 187 k lines): full parse p50 **30.5 s**, day-gated p50 **19.9 s** (the gate keeps 14 % of lines). Largest WSL 235 MB: 9.6 s full, 7.8 s gated. p99 session 91 MB / 124 MB; 4 / 5 sessions ≥ 100 MB | size **holds**; time **refuted by two orders of magnitude**. A day gate that still parses every line saves a third at best — E3's reader must skip before parsing (file mtime, a timestamp prefix read without `JSON.parse`), and E3's byte budget meets these sessions every time |
| Q7 | Windows: 587 `Agent` tool uses, 778 metas; the 191 extra are exactly the metas with a parent (nested launches, depth 2–3). WSL: 844 tool uses, 857 metas, 17 nested, 4 tool uses with no meta (refused launches). Requested shape: background 756 / 779, foreground 22 / 78 | **holds** — both directions explained |
| Q8 | Every write path absolute on both sides. Windows: backslash 90 %, lowercase drive on 219 subagent writes; WSL: `/` only. A write under the session's `cwd`: Windows 0 (where decidable), WSL 5 % | **holds** — attribution may never assume `cwd`; it normalises separators and drive case |
| Q9 | 21 top-level line types (`assistant`, `user`, `attachment`, `queue-operation`, `pr-link`, `api-request-blob`, …) and the content types `text`, `thinking`, `tool_use`, `tool_result`, `tool_addition`, `image`; 1 / 3 unparseable lines (1 at a file's end on WSL — a line being written); a file's first line is a `queue-operation` on 64 of 65 files | the allowlist's input; the sanitiser's vocabularies were cut from it (E1.S3) |

## Folder facts E3.S4's spec rests on — E1.S4, 2026-10-15

Counts only, both sides, over the caller sessions of the code rounds since 2026-09-30 (Windows 21 sessions, WSL 44).

| Fact | Windows | WSL | Reading |
|---|---|---|---|
| A main file's first line is a `queue-operation` with a `timestamp` and no message | 20 of 21 (the 21st starts with an `ai-title`, which has none) | 44 of 44 | a reader may not take line 1 for a message; it may rely on a timestamp on the first line only most of the time |
| `is_error` ABSENT on a `tool_result` | 52 857 of 122 713 results (43 %); true 4 152, false 65 704 | 31 216 of 93 079 (34 %); true 1 790, false 60 073 | absent means NOT an error; `true` is the only error |
| A `tool-results/` folder beside `subagents/` | 20 of 21 sessions, 595 entries, 593 `.txt` | 38 of 44, 420 entries, 420 `.txt` | never read by the reader, never cut into a fixture |
| `snapshot.trackedFileBackups` keys | 715 of 766 snapshots carry backups; 52 402 keys, all contain a path separator, 52 297 are absolute | 1 014 of 1 153; 17 564 keys, all with a separator, 16 910 absolute | the keys ARE real file paths: the sanitiser renames every such key to `k<n>` (E1.S3) and the guard rejects any other key |
| A live read sees a torn last line | 0 of 4 107 samples (3 live files, 20 Hz for 90 s; 2 growth steps seen) | 0 of 5 272 (4 live files; 7 growth steps seen) | too few writes happened during the sampling to settle it: the evidence is "never seen", not "cannot happen"; `TornTailCountsAsPartial` (parent §16 D22) stays as designed |

## The check of three rounds per side — E1.S4, 2026-10-15

The plan asked for three rounds per side to be confirmed by hand against the harness's coverage verdicts. What was done is weaker and said plainly:
a second, independent script (not the harness) recomputed, for rounds spread at the 10th, 50th and 90th percentile of those that have a previous round
of the same gate session, the window `[previous round's start, this round's start)` of the caller's transcript and its agent files. It was not read line by line.
Counts only; "agent files" is the number in the whole session folder, not in the window.

| Side | Round | Transcript found | Window (min) | Lines in window | Models seen | Writes seen (distinct roots) | Agent files in session |
|---|---|---|---|---|---|---|---|
| Windows | 1 | yes | 32 | 500 | 1 | 27 (3) | 43 |
| Windows | 2 | yes | 122 | 1 730 | 2 | 74 (6) | 53 |
| Windows | 3 | yes | 245 | 591 | 1 | 15 (3) | 79 |
| WSL | 1 | yes | 3 825 | 6 360 | 3 | 78 (2) | 45 |
| WSL | 2 | yes | 215 | 2 935 | 2 | 13 (1) | 32 |
| WSL | 3 | yes | 40 | 1 038 | 3 | 8 (2) | 76 |

All six agree with the harness (`found`, none ending before the round). They also show how rarely a window is clean: each of the six windows holds several models or several write roots.
A hand read of the actual lines is still not done; it is not needed for any consumer to start, and E3's own tests re-derive the windows.

## What E1 leaves open

- `/clear`: needs an attended interactive session (registry `sessionId`, the variable seen by a NEW Bash child, the variable held by an ALREADY-RUNNING MCP child, and which file the post-clear turns land in). The headless probes above settle everything else in Q4.
- Cutting a fixture from WSL data: every WSL caller session wrote into repositories outside this family's public ones, so none may be published; the `side: wsl` rows of the manifest are therefore all `windows`, and the case where a message's last line is below its maximum (16 ids on WSL) is derived from Windows data (`message-last-line-below-max`).
- The torn-tail frequency under heavy writing (above).
