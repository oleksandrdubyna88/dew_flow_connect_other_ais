# RESULTS — the caller's transcripts on both sides, measured before any reader is built

> Status: **predictions only, 2026-10-15 — written before any harness read a transcript or a store.** Epic E1 of
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
