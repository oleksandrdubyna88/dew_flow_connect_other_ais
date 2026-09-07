# Claude Code — ConnectOtherAIs

Local rules live in `.claude/rules/common/`; [review-gate.md](.claude/rules/common/review-gate.md) governs how this product's own gate is called — a code round is fed the SCOPE as well as the diff, and the server refuses one without it. [vendor-routing.md](.claude/rules/common/vendor-routing.md) fixes which CLI each model runs on — a Claude model goes through the `claude` CLI and never through `agy` or `codex`, which is a rule because breaking it is invisible in the output and cost this project three cells of a measurement.

Shared family rules are mounted at `.claude/rules/shared` (the `dew_flow_conventions` submodule) and
apply here exactly as local rules would. Fresh clone: `git submodule update --init .claude/rules/shared`.

## What this repository is

**ConnectOtherAIs** — a multi-model review gate. The main AI (Claude Code) writes the plan and the
code; secondary vendor models (Codex, Gemini, DeepSeek via Codex's custom provider) review both, in
rounds, until the count of blocking+major findings after de-duplication drops under a threshold — or
a human is called. Two halves:

| Half | Name | Role |
|---|---|---|
| `src_mcp` | `coai-mcp` (client id `coai`) | Native-AOT stdio MCP server: round state machine, CLI fan-out, finding normalisation, verdicts |
| `src_vs_code` | ConnectOtherAIs extension | settings UI, rounds view, human-escalation modal, "Install the MCP server…" button |

The full design is `research/PLAN_connect_other_ais.md` (master plan) and `research/PLAN_epic_0*.md` (the
build order, each with its deviations recorded). **Session start:** read `research/architecture.md` first, then the epic you are working.

## Commands

```bash
# Build everything
dotnet build dew_flow_connect_other_ais.slnx -c Debug

# Tests — ALWAYS the MTP executable, NEVER `dotnet test` (no VSTest host here; it aborts)
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe --filter-method "*SomeTest*"

# Extension (once src_vs_code exists)
cd src_vs_code && npm ci && npm test

# Family checks (what CI runs)
node .claude/rules/shared/tools/plan-lifecycle.mjs
node .claude/rules/shared/tools/pin-check.mjs
```

## Non-negotiables inherited from the design

- **stdout carries JSON-RPC — on the PROTOCOL path.** The server's console logging goes to stderr;
  one stray stdout line while serving stdio is a protocol corruption that looks like a protocol bug.
  What is sanctioned is not a fixed list of two flags but a shape: a **one-shot CLI mode**, selected
  by `args[0]` before any transport is opened, that answers and exits and never speaks JSON-RPC at
  all. Those are `--help`, `--version`, `--log`, `--ask-local`, `--ask-remote` and `--providers`, and
  their stdout is their entire interface — `--log` has been read from stdout by the panel since the
  rounds-log page shipped (`roundsDbRead.ts`). This paragraph used to name only `--help` and
  `--version`, which the code had already outgrown by three flags; a reviewer read it literally on
  2026-09-07 and was right to. **Adding a one-shot mode means adding it here.** Inside `ServeAsync`
  the rule is unchanged and absolute.
- **Reviewers are read-only, in a worktree pinned to a SHA** — one worktree per round, outside the
  repository, pruned on `open`, removed in `finally`.
- **No secret ever reaches argv or a log line.** Vendor keys come from one CredsForDevs `config`
  entry, read once at startup via `creds config <key>`.
- **Logging** per `.claude/rules/shared/common/logging-serilog.md`: coloured ANSI console (stderr in
  stdio mode) + one file per run under `logs/{yyyy-MM-dd}/`, everything UTC.
- `.claude/settings.json` is a byte-identical copy of the family reference
  (`.claude/rules/shared/settings/settings.json`) — never edit it independently.
