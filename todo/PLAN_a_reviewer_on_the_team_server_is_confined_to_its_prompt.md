# PLAN — a reviewer on the Team server is confined to its prompt

> Status: **plan only, nothing implemented yet, 2026-09-10.** Scope: `src_server/src/Jobs/ReviewLauncher.cs`,
> `src_mcp/runners/Processes/ProcessLauncher.cs`, `src_mcp/runners/Reviewers/ClaudeRuntime.cs`,
> `src_mcp/runners/Reviewers/ReviewerRuntime.cs` (`ReviewerSettings`). Finding 1 of
> [the product audit of 2026-09-09](../research/REVIEW_product_audit_2026-09-09.md) — the half of it that
> costs nothing to close today. The other half is [PLAN_team_server_unprivileged.md](PLAN_team_server_unprivileged.md).
>
> Related docs: [module_team_server.md](../research/module_team_server.md),
> [module_runners.md](../research/module_runners.md), [architecture.md](../research/architecture.md) —
> *How the Team server is deployed*.

## The symptom

A job on the Team server is one authorised employee's arbitrary prompt, run through a third-party agentic
CLI on a box that holds every shared vendor account. The job needs nothing but that prompt:
`ReviewLauncher.cs:89` gives it an empty temporary directory as its working directory, the diff was
shaped on the client and is already inside the prompt text, and the server has no checkout to read.
Yet the launch leaves the reviewer three ways out of that prompt:

1. **The environment.** `ProcessLauncher.cs:93-96` ADDS the request's variables to the server's own
   inherited environment rather than replacing it. The server is configured through
   `/etc/coai-server.env` (`deploy/README.md`), so whatever that file holds is in every reviewer's
   environment — and `SlotEnvironment.cs:76` puts the slot's `CLAUDE_CODE_OAUTH_TOKEN` there too, a
   token the code's own comment says is good for a year.
2. **The filesystem.** `ClaudeRuntime.cs:41-43` denies `Edit`, `Write` and `NotebookEdit`; `Read`,
   `Glob`, `Grep`, `Bash`, `WebFetch` stay allowed. `SlotRegistry.Restrict` sets the slot directories
   to `0700`, which is a boundary between OS USERS — and every job runs as the same user (root,
   `architecture.md` — *How the Team server is deployed*). So one slot's `.codex/auth.json` or
   `.claude/` is readable from a job running on another.
3. **The answer.** The finding text a reviewer writes is returned verbatim to the employee who submitted
   the prompt. Anything a tool can read, a prompt can ask to have quoted back.

Whether `claude -p --permission-mode plan` will actually execute `Bash` in a non-interactive run was
**not measured** — the audit said so and this plan does not claim otherwise. The denial below costs
nothing either way, which is why it does not wait for the measurement.

## What this plan closes, and what it does not — named on both sides

| | This plan | [PLAN_team_server_unprivileged.md](PLAN_team_server_unprivileged.md) |
|---|---|---|
| The server's own configuration in the child's environment | **closed**, all three CLIs: the child gets an allowlist, not the parent's environment | — |
| Claude reading files or running commands | **closed**: every file and shell tool denied for a confined launch | — |
| codex / antigravity reading files | **open** — `-s read-only` and `--mode plan` bound WRITES; reads are the OS's to bound | closes it: a service user per host, then per job |
| The slot's own token in its own environment | **open** — the CLI needs it to sign in | closes the cross-slot half (a job cannot read another slot); the token stays the job's own |
| Root | **open** | the whole subject |

Order: this first. It ships as a server release and changes nothing on the box; the other needs the
CLIs re-installed and every slot re-signed.

## The change

1. **`ProcessRequest.InheritsEnvironment`** (`bool`, `init`, default `true`). When false,
   `ProcessLauncher.RunAsync` clears `info.Environment` and copies from the parent only the names in
   ONE list, `ProcessEnvironment.Passthrough` (new, beside the launcher): `PATH`, `LANG`, `LC_ALL`,
   `LC_CTYPE`, `TERM`, `TZ`, `TMPDIR`, `TMP`, `TEMP`, `NO_COLOR`, the six proxy spellings, `SSL_CERT_FILE`,
   `SSL_CERT_DIR`, `NODE_EXTRA_CA_CERTS`, `XDG_RUNTIME_DIR`, and on Windows `SystemRoot`, `SystemDrive`,
   `ComSpec`, `PATHEXT`, `windir`, `USERPROFILE`, `APPDATA`, `LOCALAPPDATA`, `ProgramData`,
   `ProgramFiles`. Then the request's own variables on top, exactly as today. The default stays `true`
   because the local `coai-mcp` runs the developer's own CLIs in the developer's own environment, and
   that is correct there.
2. **`ReviewerSettings.Confined`** (`bool`, default `false`): "this reviewer was handed everything in its
   prompt and may reach nothing else". `ClaudeRuntime.Build` reads it and extends `--disallowedTools`
   with `Bash`, `Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch`, `Agent`, `Task` — the names verified
   against the installed CLI's `--help` before they are written into the adapter. The codex and
   antigravity adapters take no new flag: their sandboxes are already the strongest each CLI offers,
   and the table above says what that leaves.
3. **`ReviewLauncher.RunAsync`** sets both: `Confined = true` in the settings it builds
   (`ReviewLauncher.cs:102`) and `InheritsEnvironment = false` on the request it hands over
   (`ReviewLauncher.cs:107`). The local gate's `ReviewerExecutor` callers are untouched.
4. Docs: `module_team_server.md` (a *What a job can reach* paragraph under story 2.2),
   `module_runners.md` (the launcher's two environment modes), `architecture.md`'s deployment section
   (one sentence beside the root paragraph).

No growth surface.

### What the gate's plan round changed (2026-09-10, accepted)

- **`HOME` is not optional on Unix** (gemini, Blocking — the best finding of the round). The passthrough
  list carried the Windows home variables and none of the Unix ones, so a confined launch on Linux would
  have started every CLI with no `HOME`, and a Node runtime with no `HOME` fails in initialization,
  before it reads a prompt. `HOME`, `USER`, `LOGNAME` and `SHELL` join the list for non-Windows. On the
  Team server this was masked — `SlotEnvironment.For` sets `HOME` per slot and the request's variables are
  applied last — which is exactly why it deserved to be caught in a plan rather than in a deployment: the
  masking is a property of one caller, and the launcher's contract is for all of them. Test 1 asserts
  `HOME` present and the canary absent, on Unix.
- **A flag this CLI does not know is a launch that fails, and the version is not ours** (codex, Major).
  The plan checks flags against the CLI installed here; the box runs its own. Pinning vendor CLI versions
  is out of scope for this change (it is the deployment's, and `PLAN_team_server_unprivileged.md` already
  owns re-installing them). What this plan takes instead is the cheap half: a `POST_DEPLOY.md` item that
  runs one real review per vendor against the deployed server after a release and fails on
  `NotStarted`/`NonZeroExit`, which is the only place the installed binaries can be observed at all — and
  a note in the adapter naming the CLI version each flag was verified against, so the next reader knows
  what the claim rests on.

### What epic 1's code round added to story 2.2 (2026-09-11, accepted)

Both are about the SERVER half and belong here rather than in the launcher, which is why they were
accepted against a story that had already shipped its own half.

- **One confinement policy, not two independent flags** (codex, Major). `InheritsEnvironment` lives on
  the request and `Confined` on the reviewer settings, and nothing makes them agree. A server change
  that sets one and misses the other — on the initial launch or, more likely, on the RETRY the ladder
  performs — produces a reviewer with an isolated environment and a shell, or the reverse, and both
  look like a confined launch from every angle except the one that matters. So `ReviewLauncher`
  derives both from a single value at the launch boundary rather than setting two fields, and the
  test asserts the pair on the repair path as well as the first one.
- **A per-job `TMPDIR`** (gemini, Major). The allowlist passes `TMPDIR`/`TMP`/`TEMP` through, which on
  a shared box running as root means every reviewer sees the same `/tmp` — where other jobs' files
  and the host's sockets are. The launcher cannot fix this (a temp directory is the caller's to
  choose), and the caller already has one: `ReviewLauncher` creates `coai-server-job-…` and uses it as
  the working directory. It sets `TMPDIR`, `TMP` and `TEMP` to that directory in the request's own
  variables, which are applied last and therefore win.

## Test plan

| # | Test | Holds |
|---|---|---|
| 1 | `ProcessLauncherTests.AConfinedChildDoesNotInheritTheParentsEnvironment` — the REAL launcher, a real child that prints its environment (`cmd /c set` · `/usr/bin/env`), a canary variable set in the test process: absent in the child, `PATH` present, the request's own variable present | the allowlist, observed on a process rather than asserted on a dictionary. RED today: the canary is printed |
| 2 | `ProcessLauncherTests.AnUnconfinedChildInheritsEverythingAsBefore` | the local `coai-mcp`'s behaviour does not move — the positive companion the negative needs |
| 3 | `ClaudeRuntimeTests.AConfinedReviewerIsDeniedEveryFileAndShellTool` / `AnUnconfinedReviewerKeepsRead` | the flag list, both ways; the local code round still reads its worktree |
| 4 | `ReviewLauncherTests.TheServerLaunchesEveryReviewerConfined` — the existing `Watching` fake launcher reads `InheritsEnvironment` and the argv | the server side actually sets both. RED today |

## Definition of Done

- [ ] Tests 1–4 written, watched fail for the real symptom, passing.
- [ ] The three server-launched CLIs receive an allowlisted environment; the local `coai-mcp` is unchanged.
- [ ] The tool names were checked against the installed CLI's `--help`, and the deviation section records what was found.
- [ ] The boundary table above is mirrored into `PLAN_team_server_unprivileged.md`.
- [ ] `module_team_server.md`, `module_runners.md` and `architecture.md` updated; whole suites green.
