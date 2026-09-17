---
requires: ["README.md","research/architecture.md"]
---
# Project instructions — ConnectOtherAIs

Local rules live in `.agents/rules/common/`; [review-gate.md](rules/common/review-gate.md) governs how this product's own gate is called — a code round is fed the SCOPE as well as the diff, and the server refuses one without it. [vendor-routing.md](rules/common/vendor-routing.md) fixes which CLI each model runs on — a Claude model goes through the `claude` CLI and never through `agy` or `codex`, which is a rule because breaking it is invisible in the output and cost this project three cells of a measurement.

Shared family rules are mounted at `.agents/conventions` (the `dew_flow_conventions` submodule) and
apply here exactly as local rules would. Fresh clone: `git submodule update --init .agents/conventions`.

## What this repository is

**ConnectOtherAIs** — a multi-model review gate. The main AI (Claude Code or Codex) writes the plan and the
code; secondary vendor models (Codex, Gemini, DeepSeek via Codex's custom provider) review both, in
rounds, until the count of blocking+major findings after de-duplication drops under a threshold — or
a human is called. Three parts, each with its own release line (`mcp-v*`, `extension-v*`, `server-v*`):

| Part | Name | Role |
|---|---|---|
| `src_mcp` | `coai-mcp` (client id `coai`) | Native-AOT stdio MCP server: round state machine, CLI fan-out, finding normalisation, verdicts |
| `src_vs_code` | ConnectOtherAIs extension | settings UI, rounds view + log, human-escalation modal, "Install the MCP server…" button |
| `src_server` | `coai-server` (the **Team server**) | Optional, opt-in: an HTTP service a company deploys so reviewers run on one box behind Entra sign-in. Native-AOT binaries **and** a multi-arch image; its deploy is manual (`deploy/README.md`) |

The Team server is optional — nothing needs it — but it is a shipped product, not a sample, so a wire
field added on one side must be measured against the OLD other side before it ships.

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
node .agents/conventions/tools/plan-lifecycle.mjs
node .agents/conventions/tools/pin-check.mjs
```

## Non-negotiables inherited from the design

- **stdout carries JSON-RPC — on the PROTOCOL path.** The server's console logging goes to stderr;
  one stray stdout line while serving stdio is a protocol corruption that looks like a protocol bug.
  What is sanctioned is not a fixed list of two flags but a shape: a **one-shot CLI mode**, selected
  by `args[0]` before any transport is opened, that answers and exits and never speaks JSON-RPC at
  all. Those are `--help`, `--version`, `--log`, `--findings`, `--findings-many`, `--ask-local`,
  `--ask-remote`, `--providers`, `--bugs-json`, `--normalize`, `--collect-bugs`, `--pairs-json` and
  `--pairs-keep`, `--upload-pairs`, `--requeue-refused` and `--close-consult`, and their stdout
  is their entire interface — `--log` has been read from stdout by
  the panel since the rounds-log page shipped (`roundsDbRead.ts`), `--findings` since the log
  stopped carrying every round's findings in that list (2026-09-09), and `--findings-many` since a
  bulk export stopped being one spawn per round (2026-09-14). That last one is a MODE rather than a
  flag on `--findings` deliberately: a server too old for it must exit **64** so the extension can
  fall back, and a flag on an existing mode would have been accepted and answered **69** — "no such
  round" — which an export would have written down as five hundred clean rounds. The other half of
  that rule: **a binary that KNOWS a one-shot mode must never exit 64**, whatever is wrong with the
  request. `--findings-many` answers a keys file it cannot read with **65 (EX_DATAERR)**, because a
  request fault that presented as an old binary would send the client down the fallback and hide the
  fault behind a successful-looking export. This paragraph used to name only `--help` and
  `--version`, which the code had already outgrown by three flags; a reviewer read it literally on
  2026-09-07 and was right to; a code round read it literally again on 2026-09-15, when the corpus
  collector had added three modes and named none of them here.
  **Adding a one-shot mode means adding it here — in THIS file.** Inside `ServeAsync`
  the rule is unchanged and absolute.

  The rule names **64 and only 64**: a mode whose ARGUMENTS are wrong answers another non-zero
  code (`--upload-pairs` answers 65 for a missing `--server`), because 64 means *never heard of
  that mode* and is how a caller detects an old binary. A 2026-09-16 round read it as banning all
  non-zero codes, and as binding only `coai-mcp`; neither is what it says. It binds **every**
  binary here — that round found `coai-bugs` answering 64 for a missing `--id`, which is the
  defect the rule exists to prevent.

- **`coai-bugs` one-shot modes**, same shape, chosen before Kestrel is built: `--issue-key`
  (prints the key once, stores only its hash), `--revoke --id`, `--promote --entry`,
  `--waiting [--limit n] [--skip n]`. Without them a deployment is an empty key table and a
  quarantine nothing leaves. Configured by environment only: `COAI_BUGS_SECRET` (required; 78
  without it), `COAI_BUGS_DATA`, `COAI_BUGS_KEYWORDS`, `COAI_BUGS_RATE_PER_MINUTE` (per KEY, never
  per address; `10` unset, `0` off, 78 past `1000`), `COAI_BUGS_ADMIN_KEYS` (**base64 of the key
  list**, one line, no whitespace — `base64 -w0`; inside it, one key per line with `#` comments
  ignored. The raw list is REFUSED with 78, never guessed at, because the two shapes overlap and a
  fallback would configure the wrong administrators. **Absent or empty is legitimate** — every
  `/admin/*` call is then 401, and only the startup log says which) and `COAI_BUGS_ADMIN_RATE_PER_MINUTE` (per ADMINISTRATOR, its own setting;
  `120` unset, `0` off, 78 past `1000`); the client’s key is `COAI_BUGS_KEY` or
  `--key-file`, **never** `--key`. Details: `deploy/bugs/README.md`.
- **`coai-bugs` has an admin API, and its refusals are deliberately uninformative.** `/admin/keys`,
  `/admin/audit` and `/admin/active` behind a bearer admin key; an absent variable and a wrong
  credential answer identically so the surface is not an oracle for whether administration is
  enabled. Paging is **keyset** (`?limit&before`, `nextBefore` doubles as "there is more") because an
  offset is not insert-stable; an illegal `limit` is a 400 naming what was legal and **`limit=0` is
  refused rather than meaning everything**. Only the single successful issuance ever carries a key.
  **Removing an admin key takes a successful redeploy** — the set is immutable for the process's
  lifetime, which is what makes an administrator's own upload safe without an in-force re-check. A
  `nextBefore` is an **opaque token** a client passes back verbatim, never composes. And a line in
  `COAI_BUGS_ADMIN_KEYS` that is also an issued contributor key makes the server **exit 78**: one
  string cannot be both, or revoking the key would GRANT administration.
- **A webview page is tested by RUNNING it.** A page is assembled as a template literal and
  handed to VS Code as text, so a substring assertion over that text cannot see a control wired to
  the wrong branch — the string contains everything it was supposed to contain. This repository has
  hit that twice: `roundsLog.ts`'s tick-box and Export branches each need an early `return` or the
  control also opens the row it sits in, and no source assertion can see a missing `return`.
  `bundledPage.test.ts` bundles the page, runs its script against a DOM shim and asserts on the
  result. **A new behavioural assertion over page source text is refused** (operator ruling,
  2026-09-14); the backlog of existing ones is `todo/PLAN_the_page_tests_run_the_page.md`.
  Source assertions stay legitimate where there is no program to run — a nonce, a CSP header, a
  value appearing escaped. And executing is not sufficient on its own: ask what the assertion would
  SEE if the behaviour were deleted, because this rule's own first tests stayed green when it was.
- **Reviewers are read-only, in a worktree pinned to a SHA** — one worktree per round, outside the
  repository, pruned on `open`, removed in `finally`.
- **No secret ever reaches argv or a log line.** Vendor keys come from one CredsForDevs `config`
  entry, read once at startup via `creds config <key>`.
- **Logging** per `.agents/conventions/common/logging-serilog.md`: coloured ANSI console (stderr in
  stdio mode) + one file per run under `logs/{yyyy-MM-dd}/`, everything UTC.
- `.claude/settings.json` and `.claude/hooks/load-instructions.mjs` are byte-identical copies of
  the family reference (`.agents/conventions/settings/`) — never edit either independently.
  `adapter-check.mjs` fails CI when a copy drifts, is unwired or is missing.
- **The Claude host adapter loads instructions through a hook, and that is not a preference.**
  `validateInstructions` in `.agents/conventions/tools/lib/rule-cli.mjs` refuses a `CLAUDE.md`
  that is anything but `@AGENTS.md` and refuses a non-empty `.claude/rules` — the two doors
  Claude Code loads project instructions through on its own. Closing them is what makes one
  source for two hosts true, and it also left a session holding 561 bytes telling it to go and
  read the rules. The hook runs the same canonical resolver Codex is told to run, at the same
  pin, and prints what applies to every task. Restoring instructions the obvious way instead —
  imports in `CLAUDE.md`, or files under `.claude/rules` — makes `rules check` report
  INCOMPLETE, which by ENTRY.md blocks edits. `claudeAdapter.test.mjs` guards both halves.
