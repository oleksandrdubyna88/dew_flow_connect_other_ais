# RESULTS — the claude reviewer and `--safe-mode`: what is known, and what the lead's paid run must still observe

> Status: **record, 2026-10-23 — the facts of E5.S1, and the lead's canary and behaviour run (E5.S4, §5) on claude 2.1.309: the branch's
> hook did not run under `--safe-mode` and did run without it.** §6 (`--restricted`) is still open. For epic E5 of
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
* **Sent is not observed.** When the installed `claude --version` is not one of `ClaudeSafeMode.ObservedVersions` (`2.1.309`, §5), the flag is still sent and the
  launch carries a note: *`--safe-mode` is sent; its effect … was observed on claude 2.1.309 only, not on <version>* — or, when the version could not be read,
  that the release is not known so the effect is not observed for it. The release is asked with ONE `--version` per round per executable, only where the help
  declared the flag (the roster's probe; the Team server uses the version its catalog already saw and spawns none).
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

## 5. Canary and behaviour run — run by the lead, 2026-10-23, claude 2.1.309 (Windows)

**What was run.** Two paid turns, `--model sonnet`, each in its own fresh `testsToDelete-safemode-canary-{A,B}` git folder (cwd = that folder,
`--add-dir` = that folder, as `ClaudeRuntime.Build` sends it), arm A with `--safe-mode`, arm B without, both exit 0, `is_error: false` (cost
0.167 and 0.080 USD). Two deviations from the setup below, said plainly: (a) rows 5 and 8 were NOT run against the person's USER settings — the lead
does not edit the owner's `~/.claude/settings.json`; both arms instead got a scratch `--settings <file>` holding `permissions.deny` for
`Read(./DENIED.txt)` and a `PreToolUse` hook (matcher `Read`) that exits 2 for any path naming `HOOKDENIED`; (b) one prompt asked for all reads and
the write, so the write row is what plan mode did with it. Row 7 (the fast-mode file) was not run separately; the `--settings` file of (a) stands in
for "a `--settings` file is still read".

| # | observation | arm A (`--safe-mode`) | arm B (control) |
|---|---|---|---|
| 1 | `canary-marker.txt` after the turn | **absent** | **present** — the control fired |
| 2 | the turn answered, sign-in intact | yes | yes |
| 3 | `NOTE.txt` read | `note-content-alpha` | `note-content-alpha` |
| 4 | `outside-add-dir/ADD.txt` read | `add-content-beta` | `add-content-beta` |
| 5′ | `permissions.deny` from a `--settings` file | **enforced** — "File is in a directory that is denied by your permission settings." | enforced, same sentence |
| 6 | create `WRITE-TEST.txt` | not attempted (plan mode), no file | not attempted (plan mode), no file |
| 8′ | a read denied ONLY by a `PreToolUse` hook in a `--settings` file | **readable** — the hook did not run | denied — "PreToolUse:Read hook error: … blocked by the canary PreToolUse hook" |

**Which source carried which hook — said once, so row 1 is not read as more than it is.**

| what | where it lived | rows |
|---|---|---|
| the `SessionStart` canary hook (writes `canary-marker.txt`) | the scratch worktree's OWN `.claude/settings.json` — the project path, the one a reviewed branch controls. Arm B's marker being **present** proves that path fires in a headless run here; arm A's marker being **absent** is therefore the flag's effect on a branch's own hook | 1 |
| `permissions.deny` for `Read(./DENIED.txt)` and the `PreToolUse` hook (matcher `Read`) | a scratch `--settings <file>` given to both arms — NOT the branch's project path, NOT the owner's user settings | 5′, 8′ |

So the claim "a branch's hook does not run under `--safe-mode`" rests on row 1 alone (project path, `SessionStart`). Rows 5′ and 8′ say what safe mode does to the
`--settings` channel — they do not say it for a project-path `PreToolUse` hook, which was not run.

**What it shows, on 2.1.309 only.** The reviewed branch's project hook does not run in a `--safe-mode` reviewer (row 1), and nothing the review
needs was lost (rows 2–4, 6). Permission rules survive safe mode (5′). Hooks do NOT — not even a hook handed in by `--settings` (8′); a user-settings
hook was not tried and is assumed to go the same way, which is the trade the card states.

**Observed on one release, so the launch says so on every other.** The list of releases whose effect was observed is ONE constant,
`ClaudeSafeMode.ObservedVersions` (`runners/Reviewers/ClaudeSafeMode.cs`, typed `ClaudeRelease` values), currently `2.1.309` — this section, observed for project-path `SessionStart` and `PreToolUse` hooks. A release is added to it only by a new run recorded
here; a version bump is not evidence. A launch whose claude declares `--safe-mode` always SENDS it (never a refusal), and carries no note only when the installed
`claude --version` is on that list. On any other release the launch carries the note *`--safe-mode` is sent; its effect (the reviewed branch's hooks and CLAUDE.md do
not apply) was observed on claude 2.1.309 only, not on <version>*; when the version could not be read, the note says the installed release is not known, so the
effect is not observed for it. coai's read-only claim therefore rests on plan mode and
`--disallowedTools`, never on a hook. Both scratch folders were removed afterwards; nothing else was.

### Second run — a project-path `PreToolUse` hook, 2026-10-23, claude 2.1.309 (Windows)

Second run, 2026-10-23, claude 2.1.309 (Windows), `--model sonnet`: a PreToolUse hook (matcher Read) registered in the scratch worktree's OWN .claude/settings.json (the project path a reviewed branch controls) that appends a marker and exits 2 for a path naming HOOKDENIED. Arm B (control, coai's argv without --safe-mode): marker PRESENT, the read of HOOKDENIED.txt refused with 'PreToolUse:Read hook error: … blocked by the project PreToolUse hook'. Arm A (coai's argv with --safe-mode): marker absent, both reads succeeded. A first attempt of this run had an invalid control (the hook script used require() in a .mjs file and failed, so neither arm fired); it was discarded and rerun with a .cjs hook. Cost: 0.064 and 0.039 USD.

**Conclusion.** On 2.1.309 both project-path hook types tried (SessionStart and PreToolUse) are off under `--safe-mode`; other hook events (e.g. PostToolUse, Stop, UserPromptSubmit) were not tried. This closes the gap the first run left open (rows 5′ and 8′ ran the `PreToolUse` hook through `--settings`, not the project path), for the one hook event named here and for 2.1.309 only.

### The setup the plan wrote before the run

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

* That the effect holds under coai's invocation — §5 does, on 2.1.309 only, and for a project-path `SessionStart` hook (row 1).
* That a user-settings hook, or a project-path hook of any event other than `SessionStart` and `PreToolUse` (e.g. `PostToolUse`, `Stop`, `UserPromptSubmit`), is switched off — §5's second run saw the project-path `PreToolUse` hook off, the first run the `SessionStart` one; no other event was tried.
* That another claude version behaves like 2.1.309 — 2.1.258 and 2.1.197 *declare* the flag in their help, and that is all that is known of them; a launch on them (or on any
  unread release) sends the flag with a note that the effect is not observed there.
* That the Team server's claude declares the flag — `POST_DEPLOY.md` has the one-line check.
* Anything about codex and agy: they have no such flag, and their card note is E5.S3's.
