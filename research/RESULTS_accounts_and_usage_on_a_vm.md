# RESULTS — what a VM states about its AI accounts and their usage, read with no secret (2026-11-19)

> Status: **record, 2026-11-19.** Step 0 (epic E0) of
> [../todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md](../todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage.md),
> measured before any code. Read-only on one VM. Nothing was installed. No value of a credential file was read: key
> names, sizes, modes and times only. This file is public, so it holds no email address, no organization name or id, no
> address of a machine and no person's name; where a value was one of these it is written as `<email>`, `<org>` or `<ip>`.
>
> Subject: host alias `ai2-root`, Linux user `root`, Ubuntu. Claude Code **2.1.326**, codex-cli **0.162.1**,
> agy **1.5.12**. Times of day are UTC.
>
> How it was run: by the main session, through CredsForDevs' `creds_exec`, in **four** read-only calls. The owner
> approved the exact text of each call in the Creds dialog before it ran. The commands whose flags decide something
> are quoted word for word below. The small probe lines around them (`command -v`, `stat`, the lists of changed files,
> the key-name listings) are described, not quoted. The numbers and file lists are the main session's report of what
> the calls printed; this file adds no measurement of its own.
>
> **Added later the same day:** a fifth call measured five follow-up points (W1–W5). It is § 11. The sections before
> it are kept as they were written; where § 11 measured something they call open, a note says so.

**One host, one Linux user, one account for each vendor, one run of each command form.** Every result below is true of
that, and of no more. What was not measured is listed in § 9.

## 1. The answers in one table

The left column is what the plan had not verified (its rows U1–U11). "Expected" is what the plan wrote before the run.

| # | Expected by the plan | Measured | State |
|---|---|---|---|
| U1 | A Codex rollout file holds `token_count` events with `rate_limits` (`primary` and `secondary`: `used_percent`, `window_minutes`, `resets_at`) | Yes, with these names. `primary` was the **weekly** window and `secondary` was `null` (§ 6) | answered |
| U2 | The layout under `~/.codex/sessions` is not known | `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`, 60 files (§ 6) | answered; how far from the end of a file the last record sits was not measured (measured later: § 11, W5) |
| U3 | `codex login status` may exist and name an account with no secret | It exists and prints only `Logged in using ChatGPT`. It names no account (§ 6) | answered |
| U4 | `~/.claude.json` holds `oauthAccount` with `emailAddress` and an organization name | Yes, by key name (§ 5). Not needed: U5 gives the account | answered |
| U5 | Whether `claude auth status --json` prints an organization, and anything secret | It prints the email, the organization id and name and the subscription type. No token and no secret (§ 3) | answered; whether the command writes a file was not recorded (measured later: § 11, W3 — it writes none) |
| U6 | Whether a non-interactive Claude Code command prints the usage, with no model call, nothing started and nothing written | `claude -p "/usage" --output-format json` prints it with **no model call**. With three more flags it writes no transcript and no `~/.claude.json`; it still writes four cache files of Claude Code's own (§ 4) | answered; three points open (§ 9). Later, § 11: `/` and the plugin store pass; a write of `~/.claude.json` during one read and one process of unknown origin are not settled |
| U7 | The path and key names of the Claude credential file; the headers the usage endpoint needs | `~/.claude/.credentials.json`, mode 600, key names recorded (§ 5). The headers were not measured | half answered; needed for road (b) only, which is not built |
| U8 | Where agy keeps the account, and whether anything states usage | No usage, quota or account command, and no file with a quota. The account appears only in a log file (§ 7) | answered |
| U9 | Whether the tools are found by `ssh <host> sh -s`, which `sh`, whether `printf` is a builtin | `sh` is `dash`; `printf` is a builtin; `claude` and `agy` are **not** on the `PATH` of a shell that is not a login shell (§ 2) | answered |
| U10 | A QuickPick label draws `$(name)` as an icon | Not part of step 0 | open — the host scenario of E2 measures it |
| U11 | The Claude Code version on the VMs and its flags | 2.1.326 on this host; every flag used below was accepted (§ 4) | answered for this host |

## 2. M1 — the shell and the tools

**Expected.** Not known whether a shell that is not a login shell finds the CLIs; the owner's creds line had needed a
login shell for another tool.

**Measured.**

- `/bin/sh` is `/usr/bin/dash`. `printf` is a shell builtin.
- In a shell that is not a login shell, the `PATH` has no `~/.local/bin`. `claude` and `agy` were "not found" there.
- `claude` is `/root/.local/bin/claude`, a symbolic link to `/root/.local/share/claude/versions/2.1.326`. It prints
  `2.1.326 (Claude Code)`.
- `agy` is `/root/.local/bin/agy`, version 1.5.12, a binary of 269 MB.
- `codex` is `/usr/local/bin/codex` (codex-cli 0.162.1). A second `codex` is in `~/.local/bin`.
- `curl`, `timeout`, `stat`, `jq`, `node` and `tmux` are in `/usr/bin`.
- `~/.profile:11` and `~/.bashrc:102` both run `export PATH="/root/.local/bin:$PATH"`. That is why a login shell
  finds the CLIs and `sh -s` does not.

**Decided by it.** The script puts `$HOME/.local/bin` in front of its own `PATH`, as the login shell does. The session
reader's ssh call runs `sh -s`, which is not a login shell, and the accounts call uses the same argv shape. `jq` is on
this host; whether it is on the other hosts is not known, so the script checks for it and says when it is missing.

## 3. M2 — `claude auth status --json`

**Expected.** The agent-models mod reads `loggedIn` and `email` from it. Whether it names an organization, and whether
it prints anything secret, was not known.

**The command.** `claude auth status --json`

**Measured.** Exit code 0, **278 ms**. The keys: `loggedIn`, `authMethod` (`"claude.ai"`), `apiProvider`
(`"firstParty"`), `analyticsDisabled`, `projectsDirectory`, `configDirectory`, `loginExpiresAt`, `forcedLoginMethod`,
`email`, `orgId`, `orgName`, `subscriptionType` (`"team"`), `storedClaudeAiLogin`, `planTier` (`"other"`). No token and
no secret is in it. The plain form (without `--json`) prints the same JSON.

**Not recorded.** The plan's M2 also asked whether the credential file or `~/.claude.json` changed during this command
(`stat` before and after). The report holds no answer to that.

**Decided by it.** The account comes from this command: `loggedIn`, `email`, `orgName`. The script opens no Claude
file for the account.

## 4. M3 — road (a): Claude Code prints its own usage

**Expected.** Not known. The plan asked for a command that passes six conditions: it prints the numbers; it makes no
model call; it starts no hook, no MCP server and no plugin of the person; it writes no session file and leaves no
process; it ends within the time limit with no terminal and an empty stdin; its output can be read by a strict pattern.

**`claude --help`.** It lists no `usage` and no `status` subcommand. The commands it lists include `agents`, `attach`,
`auth`, `auto-mode`, `doctor`, `gateway`, `import`, `install`, `logs`, `mcp`, `plugin`, `purge`, `respawn`, `rm`,
`setup-token` and `stop`.

**`--bare` cannot be used.** It never reads the OAuth login.

### Run 1 — plain

```
claude -p "/usage" --output-format json
```

Run in a temporary working folder, with stdin from `/dev/null`.

- Exit code 0, **6277 ms**.
- The JSON: `type` `result`, `subtype` `success`, `is_error` `false`, `num_turns` **0**, `total_cost_usd` **0**,
  `duration_api_ms` **0**. So there was **no model call**: `/usage` is answered by Claude Code itself.
- The `result` text starts with these lines:

  ```
  You are currently using your subscription to power your Claude Code usage

  Current session: 56% used · resets Nov 19, 3pm (UTC)
  Current week (all models): 77% used · resets Nov 22, 3am (UTC)
  Current week (Fable): 49% used · resets Nov 22, 2:59am (UTC)
  ```

  After them comes a section headed "What's contributing to your usage?": requests in the last 24 hours and 7 days, the
  top subagents and the top MCP servers. That section is the person's activity. Its content is not copied here.
- Files that changed during the run: a transcript `~/.claude/projects/-tmp-tmp-XXXX/<uuid>.jsonl`; `~/.claude.json`,
  with a backup in `~/.claude/backups/`; plugin and skill sync files; `policy-limits.json.*` stamps;
  `remote-settings.json.signature.json`; the model-catalog cache.

### Run 2 — with three more flags

```
claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
```

Again with stdin from `/dev/null`.

- Exit code 0, **3554 ms**. The same numbers, `num_turns` 0, cost 0.
- Files that changed during the run, the whole list:
  - `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json`
  - `~/.claude/policy-limits.json.stamp.json`
  - `~/.claude/policy-limits.json.signature.json`
  - `~/.claude/remote-settings.json.signature.json`

  These four are Claude Code's own caches. **No transcript was written, and `~/.claude.json` was not written.** The
  credential file is not in the list.
- What this host has that a start could load: user settings with a `PreToolUse` hook and the plugin `feature-dev`, and
  in `~/.claude.json` the MCP servers `access-server`, `coai` and `creds`. The main session reports that with
  `--setting-sources ""` and `--strict-mcp-config` none of them is loaded: hooks come from the settings, and MCP
  servers are skipped. **What was recorded for that** is the flags, the shorter time (3554 ms against 6277 ms), and the
  shorter list of changed files. A list of processes during the run was not taken, so "no MCP server process and no
  hook process started" is concluded, not observed (§ 9).

### The six conditions, one by one

| Condition | Run 2 |
|---|---|
| It prints the numbers | Yes: three lines, one for each window |
| No model call | Yes: `num_turns` 0, `total_cost_usd` 0, `duration_api_ms` 0 |
| It starts no hook, no MCP server and no plugin of the person | Reported so, from the flags and the file list; not observed in a process list |
| It writes no session file and leaves no process | No transcript and no `~/.claude.json` write. **It does write the four cache files above.** The command ended with exit code 0; a process left behind was not looked for |
| It ends in time with no terminal and an empty stdin | 3554 ms with stdin from `/dev/null`, in a `creds_exec` call. The same from Windows with the pinned ssh options (the plan's M7) was not run |
| A strict pattern can read the output | A JSON object whose `result` is text with one fixed line for each window. The reset is in a form for people: month, day, hour, sometimes minutes, and `(UTC)`. It has no year |

**Decided by it.** Road (a) is taken, with these flags and `--output-format json`, stdin from `/dev/null` and a time
limit. The condition "nothing written" is **not** met as the plan first wrote it: four of Claude Code's own cache
files are written on every read. The plan now says so, and asks the owner whether that is accepted. Only the three
kinds of line above leave the VM; the script drops everything after them on the VM.

## 5. M4 — key names of the Claude files

**Expected.** `~/.claude.json` holds `oauthAccount` with `emailAddress` and an organization name. The credential
file's path and key names were to be read only if road (a) failed and the owner wanted road (b) weighed.

**Measured — key names only, no value.**

- `~/.claude.json`: mode 600, 164 969 bytes. Its `oauthAccount` has the keys `accountCreatedAt`, `accountUuid`,
  `billingType`, `ccOnboardingFlags`, `claudeCodeTrialDurationDays`, `claudeCodeTrialEndsAt`, `displayName`,
  `emailAddress`, `fullName`, `hasExtraUsageEnabled`, `organizationName`, `organizationRateLimitTier`,
  `organizationRole`, `organizationType`, `organizationUuid`, `profileFetchedAt`, `seatTier`, `subscriptionCreatedAt`,
  `userRateLimitTier`, `workspaceRole`.
- `~/.claude/.credentials.json`: mode 600, 559 bytes. Key names: `claudeAiOauth`, `accessToken`, `refreshToken`,
  `expiresAt`, `refreshTokenExpiresAt`, `rateLimitTier`, `scopes`, `subscriptionType`, `subscriptionTypeSource`.

**A deviation from the plan, recorded as it happened.** The credential file's key names were listed although road (a)
held, so the plan's condition for reading them was not met. Key names only were printed, never a value, and the owner
approved the exact call.

**Decided by it.** Neither file is needed. `claude auth status --json` gives the account, and road (a) keeps the
credential inside Claude Code. The script opens neither file.

## 6. M5 — Codex

**Expected.** Session files at `~/.codex/sessions/…/rollout-*.jsonl`; a rate-limit record with snake-case names was a
claim with no fixture in this repository; `codex login status` was not known.

**Measured.**

- The layout is `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`. There are 60 files.
- The newest file is 527 525 bytes and 2 days old. 7 of its lines carry `rate_limits`.
- The last such line has the top keys `ordinal`, `payload`, `timestamp`, `type`, with `type` `"event_msg"`. Its
  `payload` has `type` `"token_count"` and the keys `info`, `rate_limits`, `type`. `info` has the keys
  `last_token_usage`, `model_context_window`, `total_token_usage`: counts only. **The line holds no text of a
  conversation.**
- Its `rate_limits`, whole:

  ```json
  {"limit_id":"codex","limit_name":null,"primary":{"used_percent":14.0,"window_minutes":10080,"resets_at":1795518850},"secondary":null,"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"individual_limit":null,"spend_control_reached":null,"plan_type":"prolite","rate_limit_reached_type":null}
  ```

  `resets_at` is in epoch seconds. **`primary` was the weekly window here (10 080 minutes) and `secondary` was
  `null`.** So `primary` does not mean "5 hours".
- `codex login status` prints only `Logged in using ChatGPT`. It prints no email.
- `~/.codex/auth.json` exists: mode 600, 3894 bytes. **It was not read.**

**Decided by it.** The Codex usage is the last `token_count` record of a session file: its windows named by
`window_minutes`, never by the slot they are in, and `plan_type`. The Codex account is shown as its plan type and the
words "account name not shown by Codex". `auth.json` stays unopened.

## 7. M6 — Antigravity (agy 1.5.12)

**Expected.** On agy 1.3.3 (2026-10-10, [RESULTS_vendor_quota_gauges.md](RESULTS_vendor_quota_gauges.md)) there was no
status, usage or quota command and no quota key in a file. Where the account is kept was not known.

**Measured.**

- The help lists a print mode and the subcommands `acp`, `agent(s)`, `changelog`, `help`, `install`, `mcp`,
  `mic-serve`, `models` and `plugin(s)`. **There is no usage, quota or account command.**
- `agy models` lists model ids and names. It states no quota.
- `~/.gemini/antigravity-cli/` holds `antigravity-oauth-token` (mode 600, 566 bytes, **not read**), `settings.json`
  (its only key is `trustedWorkspaces`), `cli.log`, and folders for conversations and for `brain`, among others.
- An email address appears in `cli.log`.

**Decided by it.** agy states no usage, so the list says so. The account could be taken from `cli.log`, but that is a
scrape of a log file: its form is not a contract, and a log can hold other text. The plan does not read it. agy is
shown as installed, with no usage source and with its account not read.

## 8. M7, M8 and the timings

- **M7 (the draft script run from Windows with the pinned ssh options, twice, and the size of the reply): not run.**
  It stays owed, and the plan moves it to its real-window checks.
- **M8 (`ps` from a second Linux user shows no secret in an argument): not run.** On road (a) the script holds no
  secret, so no argument of its commands can carry one: the arguments are the fixed words quoted in § 3 and § 4. That
  is a reason, not an observation. The plan's marker-token test is what shows it for the exact script text.
- **Timings measured:** `claude auth status --json` 278 ms; `claude -p "/usage"` 3554 ms with the three flags and
  6277 ms without them; the four whole calls took between 3 and 10 seconds each.

## 9. What was not measured

Each line is a condition outside this run. A conclusion in the plan may not be wider than the runs above.

- Any other host, any other Linux user, any other account. Other VMs can have another Claude Code version, no `jq`,
  another time zone.
- An account with no 5-hour window. The owner's line in the agent-models mod shows `5h —` on another account; what
  `/usage` prints there (a line with no number, or no line) is not known.
- A VM whose time zone is not UTC. The reset text ended in `(UTC)` here; whether another zone's name is printed
  there, and whether `TZ=UTC` in the environment changes it, is not known.
- The working folder `/`. Both runs used a temporary folder. *(Measured later: § 11, W1.)*
- The raw bytes of the JSON: whether it is one line, and whether `·` is in it as the character or as an escape.
- A list of processes during run 2 (no MCP server, no hook process), and a process left behind after it. *(Sampled
  later: § 11, W2 — not settled.)*
- Whether `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json` is one file written again on every read, or a
  new file each time. The second would grow by one file for each read. *(Measured later: § 11, W4 — one file.)*
- Whether `claude auth status --json` and `claude --version` write a file. *(Measured later: § 11, W3 — neither.)*
- A login whose access token has expired. Claude Code may then refresh it and write the credential file, as on any
  start. How that meets a live session that uses the same login was not measured.
- Whether a live Claude Code session on the host notices the four cache files changing.
- For Codex: how far from the end of a session file its last `rate_limits` line is; the form of the record's
  `timestamp` value; whether `codex login status` writes a file or refreshes the login. *(The first two measured
  later: § 11, W5.)*
- For agy: whether starting `agy` (for its version) writes a file, contacts the network or updates itself. On another
  machine agy updated itself on a start ([RESULTS_agy_read_file.md](RESULTS_agy_read_file.md)).
- The headers the Claude usage endpoint needs from a client that is not Claude Code (road (b), not built).

## 10. A note on host aliases (the owner's vault, read 2026-11-19)

In the owner's vault, the entries `ai2-dev2` and `ai2-dev3` have the same host address as the entries `ai1-root`,
`ai1-dev2` and `ai1-dev3`. The entry `ai2-root` has another address. So **the alias in a terminal's name is not proof
of which machine it is**: two aliases that look like two machines can be two Linux users of one machine, and an `ai2-`
alias can be on the machine of the `ai1-` aliases. The addresses are not written here.

What follows for the plan: the list groups by **account**, as it already does, and never by alias or by a guess about
machines. Each alias is still its own read, because each Linux user has its own `~/.claude`, `~/.codex` and `~/.gemini`.

## 11. W1–W5 — five follow-up points, measured later the same day

The plan listed five points that §§ 2–8 left open, and wrote down before the run what a bad result of each would
mean. They were measured on 2026-11-19 at about 12:40 UTC, on the same host and Linux user, in **one** `creds_exec`
call. The owner approved its exact text in the Creds dialog. The whole call took **12 094 ms**. It installed nothing.
It made one temporary folder of its own (`mktemp -d`) for a marker file, and listed the files newer than that marker
(`find … -newer`). As in the sections above, the numbers and lists are the main session's report of what the call
printed.

The order below is the order of the run.

### W3 — do the two small commands write a file?

**Expected.** Not known; the plan's M2 had asked and step 0 did not record it (§ 3).

**The commands.** `claude --version`, then `claude auth status --json </dev/null`, with the marker made before them.

**Measured.** `find ~/.claude ~/.claude.json -newer <marker>` listed **nothing**. Neither command writes a file.

**Decided by it.** Both can run on every allowed host in every round.

### W4 — does the plugin store grow?

**Expected.** Not known whether `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json` is one file written
again or a new file for each read. A new file each time would be 144 files a day.

**Measured.** Before: `ls ~/.claude/plugins/store/ | grep -c cc-plugin` gave **2**, and the file is
`cc-plugin-agents-md_builtin-b049b47b3618.json`. Two usage reads in a row, from the working folder `/`: each rewrote
the **same** file, with the same name. After: the count is still **2**. These two reads wrote only the four cache
files; `~/.claude.json` was not written.

**Decided by it.** No growth. W4 passes.

### W1 — the usage command from the working folder `/`

**Expected.** The same numbers, the same four changed files, and nothing written in `/`. Step 0 had used a temporary
folder.

**The command**, run with the working folder `/`, stdin from `/dev/null`, under `timeout 60`:

```
claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
```

**Measured.**

- Exit code 0, `is_error` `false`, `num_turns` **0**, `total_cost_usd` **0**.
- The lines:

  ```
  Current session: 80% used · resets Nov 19, 3pm (UTC)
  Current week (all models): 81% used · resets Nov 22, 3am (UTC)
  Current week (Fable): 52% used · resets Nov 22, 3am (UTC)
  ```

  In step 0 they were 56 %, 77 % and 49 %. The numbers moved, so the read is live and not a cached answer. The reset
  of the model window was printed as `2:59am` in step 0 and as `3am` here: the same moment can be printed a minute
  apart.
- Files newer than the marker:
  - the four cache files: `plugins/store/cc-plugin-agents-md_builtin-b049b47b3618.json`,
    `policy-limits.json.stamp.json`, `remote-settings.json.signature.json`, `policy-limits.json.signature.json`;
  - **and `~/.claude.json`, with a new `~/.claude/backups/.claude.json.backup.<n>`**;
  - the folders `/root` and `/tmp`, whose own time changed. `/tmp` is the measurement's own `mktemp -d`. `/root` is
    the home folder, and its time changes when `~/.claude.json` is written again.
- Nothing else in `/` (`find` with `-maxdepth 1`).

**The `~/.claude.json` write is not explained.** Four reads with the three flags now have a list of changed files:

| Read | Working folder | `~/.claude.json` |
|---|---|---|
| step 0, run 2 (§ 4) | a temporary folder | not written |
| W1 | `/` | **written, with a new backup** |
| W4, first read (right after W1) | `/` | not written |
| W4, second read | `/` | not written |

So the working folder does not cause the write on every read. The list of changed files says that the file changed
during the read. It does not say who changed it. Readings, **none of them proven**:

1. The owner has live Claude Code sessions in tmux on this host (a terminal `Creds: ai2-root / orc-user-profile` is
   one). Those sessions rewrite `~/.claude.json` on their own, and one of them did so during W1's read.
2. Claude Code rewrites the file on some starts, for a reason of its own.
3. Claude Code recorded the folder `/` the first time it was started there. This fits the order of the four reads.
   It is not shown: whether step 0's run 2 used a new temporary folder or the one of run 1 is not recorded.

**Decided by it.** The working folder `/` behaves like the temporary folder for the numbers, the model call and the
four cache files, and nothing is left in `/`. The plan keeps `/`. The `~/.claude.json` point stays open: the plan
does not claim the file is untouched by a read, puts the point to the owner, and names the check that can settle it
(a Linux user with no live Claude Code session).

### W2 — processes during and after a read

**Expected.** No MCP server process, no hook process, and nothing left after the read. Step 0 had concluded this from
the flags and the file list (§ 4), without a list of processes.

**Measured.**

- 1.5 s after the start, the `claude` process had **no child process** (`pgrep -P`, followed down: none).
- After it ended, `ps -eo pid,etimes,comm`, filtered to processes younger than 15 s whose name holds `claude`, `node`,
  `mcp`, `creds` or `coai`, showed **one `node` process, 1 s old**. Its parent was not checked.

**What this does and does not show.** The read started no MCP server and no hook process that the one sample saw. The
`node` process was 1 s old, so it started after that sample: the sample does not rule the read out as its origin.
Nothing ties it to the read either. The host has live Claude Code sessions, and other things run on it. So: **no child
seen; one young `node` process of unknown origin; not attributed.**

**Decided by it.** The condition "starts nothing, leaves nothing" is neither broken nor shown. One sample at one
moment misses a short-lived child, and a list filtered by name cannot say whose a process is. The plan asks for a
stronger check before the script's spec is final: the processes of the read's own process group, listed several times
during the read and once after it.

### W5 — the Codex record: its place in the file and its time

**Expected.** Not known how far from the end of a session file its last `rate_limits` line is, nor the form of the
record's `timestamp` (§ 9).

**Measured.** The newest Codex file has **65 lines**. The last `rate_limits` line is **line 64**, with **991 bytes**
after it. Its `timestamp` is `"2026-11-17T19:32:36.474Z"`: ISO 8601 with milliseconds and `Z`. Its `ordinal` is 63.

**Decided by it.** The script reads the last 1 MiB of a file: about a thousand times the measured distance, and twice
the whole measured file (527 525 bytes). The margin is for a turn that is still running, whose lines come after the
last record. The list says "from a Codex session <age> ago" from the record's `timestamp`.

### What § 11 leaves open

- Who rewrote `~/.claude.json` during W1's read.
- Whether a read starts a short-lived process, and whose the `node` process was.
- Whether Claude Code retires old files in `~/.claude/backups/`.
- Everything else of § 9 that carries no "measured later" note.

## 12. Decisions that changed after these measurements (the owner, 2026-11-19)

Nothing in this section is a measurement. The "Decided by it" lines above say what the plan decided on the day of
each run. The owner then answered the plan's questions, and three of those lines no longer say what the plan does.
They are kept as written; this section says what replaced them.

- **§ 4 and § 11, W1 — a write of `~/.claude.json` by a read.** The owner accepted it, also on every read. The check
  that can say who wrote the file (a Linux user with no live Claude Code session) is still run, as a record. It stops
  nothing.
- **§ 7 — Antigravity.** § 7 decided to show agy "as installed" and not to start it. The owner wants the version, so
  the script starts `agy --version` in every round. § 2 measured that command once: it printed 1.5.12 in a shell that
  is not a login shell. **What that start writes, fetches or updates was not measured.** § 9 says agy updated itself
  "on a start" on another machine; the source ([RESULTS_agy_read_file.md](RESULTS_agy_read_file.md)) says only that
  it updated itself, not what set it off.
- **§ 2 — a host without `jq`.** § 2 decided that the script says when `jq` is missing. The owner wants it installed:
  from the host's own package manager, where the ssh user is root or has `sudo` without a password; where the user
  has no rights, the list shows the command to run. **Nothing of that was measured here**: this host has `jq`, and no
  package manager was run in any of the five calls. **What was built (E1):** E1 and E2 install nothing; the list shows the
  command to run there, and the installer comes after E2 (parent decision 17).

## 13. W6, W7 and W8 — measured 2026-11-26

Another VM than § 1–11: the host of the vault entries `ai1-…`, Linux user `dev3`, Claude Code **2.1.332**, agy
**1.5.17**, `strace`, `jq` and `timeout` present. Each through `creds_exec`, which ends a call at 30 s. The same user had
about six live Claude Code sessions running the whole time. The owner gave the go for W6, W7 and W9 on 2026-11-26.
**W8 was run together with W7 before it had a go of its own** (it starts only `agy --version`, which the shipped
script starts every round, decision 16); the owner was told.

The usage read in every run below: `cd / && TZ=UTC timeout -k 1 <s> claude -p "/usage" --output-format json
--no-session-persistence --strict-mcp-config --setting-sources "" </dev/null`, its output sent to `/dev/null`.

### W7 — every program start of one usage read: PASSED

Run under `strace -f -qq -e trace=execve`, twice (the second with `-s 200` for the full argv). Exit 0; 5652 ms under
strace; 6 processes traced. The programs started:

- `timeout`; `~/.local/bin/claude` → `~/.local/share/claude/versions/2.1.332`, twice (Claude Code itself);
- the same binary with argv0 `rg`: `rg --version`, and `rg --no-config --files --hidden --no-ignore --max-depth 4
  --glob .orphaned_at ~/.claude/plugins/cache` — Claude Code's own ripgrep, looking for orphaned plugin caches;
- `/bin/sh -c "ps -o command= -p <pid>"`, where the pid was one of this user's live `claude` processes: Claude Code
  reads the command line of another live session. Read-only.

No hook command, no MCP server, no plugin process. Two seconds after the read, the user's processes younger than 40 s
were the ssh session, its `bash`, `ps` and `awk` only: nothing outlived the read. **Verdict: nothing was started that
is not Claude Code's own; W7 passes.**

### W6 — what a usage read writes: a fixed number of backups → "every round"

**Not as the plan wrote it**: the plan asks for a user with no live session, and every VM user with Claude Code that
the vault opens (`ai1-dev2`, `ai1-dev3`, `ai2-root`) has live sessions. The owner chose `dev3` with control windows.

- Control A, 20 s with no read: `~/.claude.json` changed 0 times (its time polled once a second).
- 12 reads from `/`, 11:09:50–11:11:25 UTC, three calls: every one exit 0, 2493–3065 ms (the first 3043 ms, the rest
  about 2.5–2.7 s). `~/.claude.json` changed after read 1 and read 11 only — 2 of 12.
- Control B, 20 s with no read: 0 changes.
- `~/.claude.json` has no project entry for `/` before or after (`.projects | has("/")` is `false`).
- **The backups are dot files**, `~/.claude/backups/.claude.json.backup.<epoch ms>`; a first count with a plain `ls`
  said 0 and was wrong. With `ls -A`: **5 files**, stamped 09:13:53, 09:50:18, 10:35:58, 11:06:59 and 11:10:26 UTC,
  176–180 KB each. The first three are before any read of ours (live sessions); 11:06:59 is near the W7 reads;
  11:10:26 is inside the read window, but no read around it changed `~/.claude.json` (reads 4–8 reported no change),
  so who wrote it is not proven.

**Decided by it.** Weeks of live sessions leave exactly five backups, so Claude Code keeps a fixed number. At
11:11 UTC this was inferred from the count; a deletion had not been seen happen, and the note here was "to
confirm: the count is five again on a later day". The row of § 5 that holds: **"Claude Code keeps a fixed number
→ every round"**.

**The retirement was then seen, 40 minutes later.** At 11:51:35 UTC the same day, the same user's
`~/.claude/backups/` (`ls -A`) held 5 files again, stamped 11:26:15, 11:28:08, 11:42:47, 11:46:24 and 11:48:52
UTC. All five files of the listing above (09:13:53, 09:50:18, 10:35:58, 11:06:59, 11:10:26) were gone. So
Claude Code keeps five backups and deletes the older ones: observed, no longer inferred. With live sessions the
backups are frequent — five in 25 minutes — and every one of these five is later than the last read of ours
(11:11:25). Not measured in this second listing: the sizes of the five files, and which process wrote each.

### W8 — `agy --version`: a record

`cd / && timeout -k 1 25 agy --version </dev/null`: printed `1.5.17`, exit 0, **886 ms**. The program's size was
278 438 600 bytes before and after; nothing under `~/.gemini` and not the program was newer than a marker made just
before; no process was left.
