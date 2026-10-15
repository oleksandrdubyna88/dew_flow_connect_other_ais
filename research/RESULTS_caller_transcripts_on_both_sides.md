# RESULTS — the caller's transcripts on both sides, measured before any reader is built

> Status: **predictions written 2026-10-15 before any harness read a transcript or a store; E1.S4's observations
> added the same day (Q4 still open — its probes).** Epic E1 of
> [PLAN_a_round_records_who_wrote_the_code_and_how_much.md](../todo/PLAN_a_round_records_who_wrote_the_code_and_how_much.md).

- **Subject:** `dew_flow_connect_other_ais` at `6159b7f4` (origin/main when E1 started).
- **Harnesses:** `scripts/export-code-rounds.py` (one read-only query per store) and `scripts/measure-caller-transcripts.mjs`
  (counts only — never a value that could carry text).
- **Sides:** Windows (`%LOCALAPPDATA%\coai-mcp`, `~/.claude` of the operator) and WSL (`~/.local/share/coai-mcp`, the WSL
  `~/.claude`), each measured from inside itself.
- **Claude Code versions on disk:** unknown yet — S4 records them. Known in passing: Windows CLI 2.1.289, WSL VS Code
  extension 2.1.296, a WSL PATH CLI that was 2.1.197 until 2026-10-14 (now 2.1.300).

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
| Q4 | not measured here — needs the probes of E1.S4 (`/clear` cannot run headless; `--resume` and no-persistence by a probe session) | open |
| Q5 | Windows: 399 / 405 rounds found, none ends before the round; the 6 not found are the 6 rounds whose caller is codex (no Claude transcript exists). WSL: 516 / 516 | **holds and exceeds**: 100 % of Claude-called rounds on both sides |
| Q6 | Largest Windows session **650 MB** (273 files, 187 k lines): full parse p50 **30.5 s**, day-gated p50 **19.9 s** (the gate keeps 14 % of lines). Largest WSL 235 MB: 9.6 s full, 7.8 s gated. p99 session 91 MB / 124 MB; 4 / 5 sessions ≥ 100 MB | size **holds**; time **refuted by two orders of magnitude**. A day gate that still parses every line saves a third at best — E3's reader must skip before parsing (file mtime, a timestamp prefix read without `JSON.parse`), and E3's byte budget meets these sessions every time |
| Q7 | Windows: 587 `Agent` tool uses, 778 metas; the 191 extra are exactly the metas with a parent (nested launches, depth 2–3). WSL: 844 tool uses, 857 metas, 17 nested, 4 tool uses with no meta (refused launches). Requested shape: background 756 / 779, foreground 22 / 78 | **holds** — both directions explained |
| Q8 | Every write path absolute on both sides. Windows: backslash 90 %, lowercase drive on 219 subagent writes; WSL: `/` only. A write under the session's `cwd`: Windows 0 (where decidable), WSL 5 % | **holds** — attribution may never assume `cwd`; it normalises separators and drive case |
| Q9 | 21 top-level line types (`assistant`, `user`, `attachment`, `queue-operation`, `pr-link`, `api-request-blob`, …) and the content types `text`, `thinking`, `tool_use`, `tool_result`, `tool_addition`, `image`; 1 / 3 unparseable lines (1 at a file's end on WSL — a line being written); a file's first line is a `queue-operation` on 64 of 65 files | the allowlist's input; the sanitiser's vocabularies were cut from it (E1.S3) |
