# ConnectOtherAIs

A multi-model review gate. Your main AI writes the plan and the code; **other vendors' models** —
Codex, Antigravity (Gemini/Claude/GPT-OSS), a second Claude, DeepSeek — review both in rounds,
until the findings that matter drop under a
threshold, or a human is called.

The value is not "more review". It is **review by a model that cannot see the author's reasoning**,
which is the only kind that catches the author's assumptions.

Two halves, the shape CredsForDevs proved:

| | What it is |
|---|---|
| `coai-mcp` | A Native-AOT MCP server over stdio. An MCP client starts it; it runs the rounds. |
| ConnectOtherAIs | A VS Code extension: settings, the install button, the rounds view. |

## The protocol

```
open → review_plan → resolve → (revise, repeat) → proceed
     → implement
     → review_code → resolve → (fix, repeat) → proceed
```

`review_code` **refuses** until a plan round has reached `proceed`. Skipped stages are impossible,
not discouraged — the honest limit of a design with no hooks: the server cannot make a model call
it, but it can make a skipped stage impossible to fake.

Seven tools, unprefixed (the client's `coai` id is the namespace): `providers`, `open`,
`review_plan`, `review_code`, `resolve`, `status`, `ask_human`.

## When it needs a person

`ask_human` — and a `call_human` verdict — put the question in front of you **in VS Code**: a
dialog, a status-bar item so a dismissed dialog loses nothing, and an open-questions section at the
top of the rounds view. The call blocks until you answer; after 30 minutes it comes back
`no_answer_yet` telling the AI to ask you in the chat instead, and the question stays open.

Still no port on either side: the server writes the question as a file into the data directory the
extension already reads, and your answer is a file beside it.

## What makes "fewer than 2 remarks" mean something

Without a rule, three verbose reviewers guarantee escalation forever. So:

1. Only `blocking` and `major` count; minors and nits are reported and never gate.
2. **De-duplication happens first** — same file, lines within ±5, same category, same remark → one
   finding listing every provider that raised it. Two vendors agreeing is stronger evidence, not
   twice the work.
3. A finding you rejected **with a reason**, re-raised with the same argument, does not count again.
   Re-raised with a genuinely new argument, it counts in full.

## Install

1. **The server**: *ConnectOtherAIs: Install the MCP Server…* downloads the release asset for your
   platform into the extension's storage (never onto `PATH`), verifies its `sha256`, and puts the
   `mcpServers` block on your clipboard. Paste it into `~/.claude.json`, a project `.mcp.json`, or
   `.vscode/mcp.json`, and restart the client.
2. **The instructions**: *ConnectOtherAIs: Copy the CLAUDE.md snippet* gives the text that teaches a
   repository's main AI when to call the tools.

Codex, Antigravity and Claude authenticate themselves — if their CLIs are signed in, no key is
needed. DeepSeek
rides the Codex CLI's custom-provider config and needs a key, which comes from **one CredsForDevs
`config` entry** read once at startup. That is the only key path: a `credential` entry cannot serve
it, because nothing in the vault's read routes returns a secret.

## Reviewers are read-only, and share one tree

Each round creates **one** detached `git worktree` pinned to a resolved SHA, outside your
repository, shared by every reviewer in that round: the main AI keeps editing while a review runs,
and six checkouts of a moving branch would be six different inputs to one comparison. Codex runs
`-s read-only --ephemeral`, Gemini `--approval-mode plan`. The tree is removed in a `finally`, and
an orphan from a killed session is pruned by the next `open`.

## Build and test

```bash
dotnet build dew_flow_connect_other_ais.slnx -c Debug
./src_mcp/tests/bin/Debug/net10.0/CoaiMcp.Tests.exe     # never `dotnet test` — MTP, no VSTest host
cd src_vs_code && npm ci && npm test
node .claude/rules/shared/tools/plan-lifecycle.mjs
```

The process-level suite drives a scriptable **fake CLI**, so CI touches no vendor and needs no
network. The wire contract is checked against the built binary and, in the release workflow,
against the **published** one.

## Documentation

`research/architecture.md` is the entry point; `module_core`, `module_runners`, `module_server` and
`module_extension` deep-dive each half. Plans live in `todo/` while open and move to `research/`
with an `IMPLEMENTED` status when they ship — a rule this repository's CI enforces.

## Prompts: a universal question, and eight narrow lenses

Each reviewer role ships a **universal** prompt and two narrow ones, and the panel can pick which
prompt each ROUND uses — or rotate through them automatically (universal first, then each lens).

| Role | Universal | Lens 1 | Lens 2 |
|---|---|---|---|
| Plan | the whole plan | assumptions & verification | the human path |
| Architecture | boundaries + evolution | boundaries & duplication | cost of the next change |
| Security & reliability | the whole surface | what it holds and leaves | attack surface |
| Performance & UX-DX | both | cost at scale | ergonomics & waiting |

**What is not claimed.** The lenses were measured against the universal prompt over three plans,
and the union of all three found roughly twice what any single one did. That result does not
survive its own control: running the SAME prompt on the SAME text three times produced 6, 4 and 5
findings whose overlaps were 3, 1 and **0**. Run-to-run variance alone explains the spread, so the
lenses are offered because they are useful to aim, not because they are proven to find more.

The measurement that matters for a gate is a different one: a finding raised by two vendors
independently is stronger evidence than one raised twice by the same prompt, and every finding
carries the `providers` that raised it.

## What each AI has used

The server appends one line per reviewer to `usage.jsonl` — vendor, model, role, stage, seconds,
tokens, cost, outcome — and the panel charts it per vendor over a day, week, month or year.
Failed reviewers are recorded too: a run that burned ninety seconds and answered nothing is
exactly what a spending record must not hide. A vendor that does not price its own runs shows a
dash rather than `$0.00`, because free and unreported are different facts.

Token accounting is each vendor's own, because a shared rule is wrong for at least one of them:
codex folds cached tokens INTO its input count, claude reports them BESIDE it, and antigravity's
thinking tokens sit inside its output count. Claude reports one run twice and the two disagree —
`usage` is the last message, `modelUsage` is the session — and the ledger reads the second.
