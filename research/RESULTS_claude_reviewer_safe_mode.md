# RESULTS — the claude reviewer and `--safe-mode`: what is known, and what the lead's paid run must still observe

> Status: **record, 2026-10-23 — the facts of E5.S1 only; the canary and behaviour run (E5.S4) is pending, run by the lead.** For epic E5 of
> [PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md](../todo/PLAN_codex_agy_and_claude_reviewers_get_what_kimi_got.md). Written before the
> code of E5.S2 so that the gate reads a fact and not a claim. Nothing here was measured by running a claude CLI in this change: every line is
> either a quotation of a committed file, a record the plan carries from another repository, or a command left for the lead to run.

## 1. The question

coai's claude reviewer runs with the reviewed worktree as its working directory (`ClaudeRuntime.Build`: `--add-dir <worktree>` and the process
starts in it). A Claude Code session reads project configuration from its working directory: `CLAUDE.md`, `.claude/settings*.json` (hooks, MCP
servers, permissions), skills and plugins. So the branch that is being reviewed can configure its own reviewer. Can it run a command inside it?
And is there a flag that stops it which does not cost the owner's browser sign-in?

## 2. What is known — three sources

### 2.1 A headless session in a worktree ran that worktree's project hook (observed in another repository)

`dew_flow_conventions` · `research/RESULTS_subagent_model_tiers.md` §*E3 — the self-install observed (2026-10-14)*, item 1: a **headless** claude
session in a clean `connect_other_ais` worktree printed `subagent-model guard installed into ~/.claude (version 1)`. Before it,
`~/.claude/settings.json` had no `PreToolUse` hook; after it, exactly one. That install is done by the worktree's own project `SessionStart`
hook. So a headless claude **does** run the working directory's project hooks, and a hook can write outside the working directory.

What this does NOT establish: that coai's exact invocation (`-p --output-format json --permission-mode plan --disallowedTools … --strict-mcp-config
--add-dir <worktree>`) does the same. Nothing in this repository records a hook run under that argv (`RESULTS_claude_consultant_confinement.md` has
no "hook"). That is what E5.S4 observes (§5).

### 2.2 The two recorded `claude --help` texts declare `--safe-mode`

Fixtures in this repository: `src_mcp/tests/fixtures/claude/help-2.1.258-windows.txt` and `help-2.1.197-wsl.txt` (captured 2026-10-03 on the
measuring machine; `ClaudeCapabilityTests` reads them).

| flag | 2.1.258 (Windows) | 2.1.197 (WSL) | what the text says |
|---|---|---|---|
| `--safe-mode` | lines 198–208 | line 166 | *Start with all customizations (CLAUDE.md, skills, plugins, hooks, MCP servers, custom commands and agents, output styles, workflows, custom themes, keybindings, and more) disabled — useful for troubleshooting a broken configuration. **Admin-managed (policy) settings still apply. Auth, model selection, built-in tools, and permissions work normally.** Sets `CLAUDE_CODE_SAFE_MODE=1`.* |
| `--setting-sources <sources>` | line 211 | line 179 | comma-separated `user,project,local` |
| `--restricted` | lines 180–194 | **not declared** | removes the command-running tools and `WebFetch` unless `--tools` names them; *ignores user, project and local settings files (managed settings and `--settings` still apply; add `--strict-mcp-config` to skip MCP servers too)*; confines the file tools to the working directories (`--add-dir` included) |
| `--bare` | lines 40–44 | line 33 | *Minimal mode: skip hooks, LSP, plugin …* and it skips **keychain reads** |
| `--strict-mcp-config` | line 215 | line 183 | only MCP servers from `--mcp-config` |

Both declare `--safe-mode` with the same meaning, so one flag serves a Windows 2.1.258 and a WSL 2.1.197. A claude that does not declare it
(older, or newer and renamed) cannot be sent it: an undeclared flag is a launch that fails (`error: unknown option …`, as `--restricted` does on
2.1.197) — which is why the CLI's own `--help` is read first, fresh every round (`ClaudeCapability.SafeModeIn`).

### 2.3 The lead's `claude --help` on 2.1.309 (free, recorded in the plan §9)

The lead ran `claude --help` on **2.1.309** and reported: the same four flags (`--safe-mode`, `--setting-sources`, `--restricted`, `--bare`), the
same wording, and for `--bare` the sharper *OAuth and keychain are never read*. **No fixture of the 2.1.309 text is committed**; this line is the
lead's report as the plan carries it, not a file a test reads. A fixture is a follow-up if a version-specific rule is ever needed.

## 3. Why `--bare` is never sent

The owner signs in to every CLI through the browser (plan §9). `--bare` skips keychain reads (2.1.258 and 2.1.197 help: *skip … keychain reads*;
2.1.309: *OAuth and keychain are never read*), so a `--bare` launch cannot find that sign-in. `--safe-mode` is the flag whose text says
*Auth … work[ing] normally*. The tests pin it: `BareIsNeverSent` (every capability state, safe mode on and off).

## 4. What E5.S2 sends, and what it deliberately does not

* `--safe-mode`, placed before `--permission-mode`, when the installed claude's help declares it as a flag (not a flag that takes a value) **and**
  the row's `safeMode` setting is on (the default). Every other flag of the reviewer's argv is unchanged.
* Never `--bare`. Never `--restricted` (see §6). Never a retry without the flag after a failure.
* When the help declares no such flag: the launch goes without it and carries a **note** (never a refusal) naming the branch's `CLAUDE.md` and
  hooks. MCP servers are not named: `--strict-mcp-config` is already sent, so they do not reach the reviewer.
* When the help could not be read (timeout, non-zero exit, cut short, nothing printed): the same, with a note saying the capability could not be
  read. A help that did not come back is *unknown*, never *not declared*.
* The row's `safeMode` set to `off` is the way back: no flag, and an inherited `CLAUDE_CODE_SAFE_MODE` is removed from the child's environment too.
* The consultants' argv is untouched, and their refusal on an unreadable help (for `--restricted`) stays a refusal.

**The one trade, recorded.** `--safe-mode` also switches off the person's own **user** hooks (`~/.claude`), because it disables *all customizations*;
managed hooks stay. A person who protects a read with a user `PreToolUse` hook therefore loses that hook on a safe-mode reviewer. What keeps the
reviewer read-only is coai's own confinement — `--permission-mode plan` and `--disallowedTools` — never a user hook. The card says so next to the
setting. Whether a canary denied only by a user hook is readable under safe mode is part of S4 (§5, row 8).

## 5. Canary and behaviour run — pending, run by the lead

> **Not run.** This section is the lead's to fill: one real, paid, cheap Claude turn per arm, in a scratch folder whose name starts `testsToDelete-`.
> The claim "a branch's hook cannot run inside the reviewer" is limited to the CLI versions in the table below, and until a row says *observed*, the
> card and the docs claim the flag **is sent**, never that the effect holds.

**Setup.** A scratch git worktree `testsToDelete-safemode-canary/` with `.claude/settings.json`:

```json
{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "command", "command": "node -e \"require('fs').writeFileSync('canary-marker.txt','fired')\"" } ] } ] } }
```

and a `NOTE.txt` file beside a second folder `outside-add-dir/` holding `ADD.txt` (for the `--add-dir` read). The prompt, on stdin, for both arms:
`Reply with the single word OK, after reading NOTE.txt and outside-add-dir/ADD.txt with your file tools.`

**Arm A — coai's shipped argv with the flag** (a local, unconfined reviewer; the argv is exactly what `ClaudeRuntime.Build` produces, in this order):

```
claude -p --output-format json --safe-mode --permission-mode plan --disallowedTools Edit Write NotebookEdit --strict-mcp-config --add-dir <abs path of the scratch worktree>   # cwd = the scratch worktree
```

**Arm B — the control, the same without `--safe-mode`** (a control that does not fire makes the run prove nothing):

```
claude -p --output-format json --permission-mode plan --disallowedTools Edit Write NotebookEdit --strict-mcp-config --add-dir <abs path of the scratch worktree>   # cwd = the scratch worktree
```

Add `--model <the row's model>` to both when the row names one. Record the output of `claude --version` first.

| # | observation | arm A (`--safe-mode`) expected | arm B (control) expected | result, version |
|---|---|---|---|---|
| 1 | `canary-marker.txt` in the scratch worktree after the turn | **absent** | **present** | *pending* |
| 2 | the turn answered (not an auth failure — sign-in intact) | yes | yes | *pending* |
| 3 | an allowed file read in the worktree (`NOTE.txt`) | readable | readable | *pending* |
| 4 | a read through the `--add-dir` directory (`ADD.txt`) | readable | readable | *pending* |
| 5 | a user permission denial (`permissions.deny` in the user's settings for one file) | still enforced | still enforced | *pending* |
| 6 | a prohibited write (`Write` is in `--disallowedTools`; ask it to create a file) | refused | refused | *pending* |
| 7 | the fast-mode `--settings <file>` (add `--settings <data>/fast-mode/fast-off.json` to both) | applied | applied | *pending* |
| 8 | **the trade:** a canary file read denied ONLY by a user `PreToolUse` hook | recorded: denied or readable | denied | *pending* |

Row 8 decides what the card says: *denied before and readable after* is the recorded trade (the sentence already on the card), and coai's confinement
is what the read-only claim rests on. Only the canary's own `testsToDelete-` folder is removed afterwards.

## 6. Open — the `--restricted --strict-mcp-config` pair

`--restricted` (2.1.258 only) would also confine the file tools to `--add-dir` and remove the command tools; it would close part of the unconfined-read
finding of [PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md](../todo/PLAN_the_claude_reviewer_is_confined_by_an_allowlist.md). It is **not
sent**, because nothing records that it is compatible with `--permission-mode plan` / `--disallowedTools` or that `--add-dir` reads survive. After E5.S2
ships, the lead runs one turn of arm A's argv **plus** `--restricted --strict-mcp-config` (a second `--strict-mcp-config` is harmless; the pair is the
CLI's own advice) on a 2.1.258 and records: does it start, is the review answered, is a read through `--add-dir` still allowed, is a read outside both
refused. *Compatible* opens its own story; *not compatible* is a recorded non-item. This is not a refusal anywhere: a claude without `--restricted` is
run without it.

| observation | result, version |
|---|---|
| the argv starts and answers with `--restricted --strict-mcp-config` added | *pending* |
| a read through `--add-dir` | *pending* |
| a read outside the worktree and `--add-dir` | *pending* |
| a conflict message between `--restricted` and `--permission-mode plan` / `--disallowedTools` | *pending* |

## 7. What this record does not say

* That the effect holds under coai's invocation — §5 does.
* That another claude version behaves like 2.1.258, 2.1.197 or 2.1.309 — the flag is *declared* by the first two, and reported declared by the third.
* That the Team server's claude declares the flag — `POST_DEPLOY.md` has the one-line check.
* Anything about codex and agy: they have no such flag, and their card note is E5.S3's.
