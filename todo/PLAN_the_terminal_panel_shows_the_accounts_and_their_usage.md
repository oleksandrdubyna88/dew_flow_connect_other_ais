# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **plan only, nothing implemented yet, 2026-11-19.** Step 0 (epic E0, a measurement with no code) ran on one VM
> on 2026-11-19 and the owner answered the open points O1–O7 the same day; both are folded in below. Scope:
> `src_vs_code` only — one command with an icon in the terminal panel's title bar, a list of the Claude Code, Codex and
> Antigravity accounts on the hosts of the open `Creds: <host> / <session>` terminals with their usage, and a background
> round over those hosts every 10 minutes (one read-only ssh call per host, one host at a time). The consent is the
> session reader's own per-host answer, with no second question (the owner's O3). The Claude usage is printed by Claude
> Code itself on the VM (road (a), measured). No change to `src_mcp`, `src_server` or CredsForDevs; our script installs
> nothing and writes nothing on a VM. Three epics (E0 is the measurement), one extension release. **Not approved yet:**
> no plan round has run. The text is ready for it. Still owed, and named so: the owner's word on the new questions
> N1–N5 (this text takes the recommended answer of each), the question consultants' answer on what is important
> before the epic split is final, and the measurements W1–W5 of § 5, which the planner recommends running before the
> round. The change ledger (the `Plan changes` section of `.agents/conventions/common/plan-change-control.md`) opens
> here, directly under this block, with its epoch 1 line in the commit of the first approval; until then the section
> is not written, because a ledger with no epoch line is a finding of `plan-lifecycle.mjs`.

Related: [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md) (step 0
of this plan, measured 2026-11-19), [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md)
(its E2 built the read-only ssh reader and the per-host consent this plan reuses),
[../research/RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md)
(where each vendor states its quota gauge, measured 2026-10-10),
[../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) (the gauge of coai's own rounds).

## The goal

The owner, 2026-11-19 (in Russian, translated): *"Next I want to add an icon, like the chats one. It should show a list
of all Claude Code, Antigravity and Codex accounts used in the open SSH connections in the console, and their usage.
Can we?"* Then: *"Real time is not needed. We can go over ssh every 10 minutes, in a round, and ask for the data."*

What the owner sees today: the VS Code terminal panel holds many terminals named `Creds: <host> / <session>`, for
example `Creds: ai1-dev2 / alert-multiply-teams`, `Creds: ai1-root / ba-gate1`, `Creds: ai2-dev2 / orc-coai`,
`Creds: ai2-root / orc-user-profile`. That is five hosts: `ai1-dev2`, `ai1-dev3`, `ai1-root`, `ai2-dev2`, `ai2-root`.
Each host is a different Linux user with its own `~/.claude` (the terminal-door plan, owner decision 5). The panel's
title bar has a "+" and coai's *Ask other AI* icon. Nothing in VS Code says which account each host is signed in to,
or how much of its limit is left. To learn it the owner opens each session and asks there.

## Owner decisions (2026-11-19)

1. **An icon beside the chat icon** in the terminal panel's title bar opens the list.
2. **No real time.** A round over ssh every 10 minutes is enough.
3. **Claude usage: let Claude Code itself make the call on the VM**, so our script never touches the token. The
   agent-models mod does that inside Claude Code. Step 0 measures whether a non-interactive Claude Code command prints
   the usage. If none does, the fallback order is: the script reads the VM's credential and calls the endpoint itself
   (needs the owner's yes and a security judgement); then the account only, with no usage.
4. **Several hosts may share one account.** The list groups by account and shows which hosts use it. Usage is read once
   for each account in a round, where the road allows it.
5. **The same numbers as the mod line.** The owner's mod line today reads
   `5h — · resets in — · week 80% left · Fable 85% left`. The list shows the numbers as "left", with the same dash for a
   missing value and never a zero, so both views agree.

**The owner's answers to the open points O1–O7 (2026-11-19).** They settle those points. The questions themselves are
kept in *Open points for the owner*, with the answer beside each.

6. **O1 — the Claude usage road: "may also use curl".** Road (b) is **allowed** as a fallback. Step 0 then showed that
   road (a) works, so this plan takes **road (a)**. Road (b) stays written here as allowed and is **not built**.
   Building it later is a change of this plan with its own security judgement. The planner's recommendation against
   road (b) stays in the Security section, so it is read before anyone builds it.
7. **O2 — full email addresses are shown in the list.** In the list only: never logged, never stored, never sent on.
8. **O3 — the consent is shared with the session reader: no separate consent.** A host that is already allowed for the
   session read (the answer to the `read-remote-session` question of the terminal-door plan's E2) is read for accounts
   too, **without a new question**. § 3 is designed on this. The planner's first draft recommended a separate consent;
   what the decision widens is written as finding F1 in the Security section, for the owner to read, not to reopen.
9. **O4 — the interval is a fixed 10 minutes.** No setting.
10. **O5 — the passive Codex read is enough.** No live read.
11. **O6 — the icon is always visible.**
12. **O7 — step 0 ran on the host `ai2-root`** (Linux user `root`), on 2026-11-19, with the owner approving each exact
    command. Its record is [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md).

## What the code does today — verified on `a057c971` (extension 0.89.1)

All paths are under `src_vs_code/` unless they start with `research/`, `todo/`, `.agents/` or a file of the repository root.

- **The host comes from the terminal's name, by one pattern.** `credsSession(name)` (`src/credsSession.ts:65`) matches
  `^Creds: (<host>) / (<session>)$` (`:36`). The host class is `[A-Za-z0-9][A-Za-z0-9._-]*` (`:31`), so a leading `-`
  is impossible and no space, quote or shell character can be in a host. A name that fails is `other` or `malformed`.
- **The window's terminals have one mapper.** `liveTerminals(terminals)` (`src/terminalHost.ts:22`) gives each terminal
  as `{ key, name, exited }`; a terminal whose process has ended is `exited`. `bindReattach` listens to
  `onDidOpenTerminal` (`:82-83`); `bindRemoteSessions` listens to `onDidCloseTerminal` (`src/remoteSessionHost.ts:220`).
- **The ssh argv is pinned.** `SSH_OPTIONS` (`src/remoteSsh.ts:28-33`) is `-T`, `BatchMode=yes`, `ConnectTimeout=5`,
  `RequestTTY=no`, `RemoteCommand=none`, `ClearAllForwardings=yes`, `ForwardAgent=no`, `ForwardX11=no`,
  `LogLevel=ERROR`, `StrictHostKeyChecking=yes`. Its comment says a change to the list is a plan change (`:22`).
  `sshArgv` (`:44-50`) builds `[…options, '--', host, 'sh', '-s', '--', …quoted operands]` and has no `-n`.
  `sshExecutable` (`:59-67`) is one absolute path for each platform, never the `PATH`.
- **The runner is tied to a tmux session today.** `sshArgv` throws unless the operands pass `callOf`, the grammar of the
  session reader (`:45`, `src/remoteScript.ts:91-95`). `runRemote(host, session, operands, script, limits, seams)`
  (`src/remoteSsh.ts:113-129`) needs `operands[0] === session` (`:151-157`). Its refusal sentences name "the Claude
  session" (`:127`, `:160`), and its one log line holds `host`, `session`, how the call ended and ssh's stderr tail,
  never stdout (`:234-238`). So a call with no session operand needs the runner widened; it cannot be called as it is.
- **The limits of one call.** `REMOTE_LIMITS` is 15 s and 8 MiB of stdout (`src/remoteSsh.ts:75`). The child goes through
  `capture` (`src/versionProbe.ts:112`) with `maxStdoutBytes` and `killTree` (`:82`, `:84`), `shell` off, the script on stdin
  (`src/remoteSsh.ts:144-145`). Exit 255 is ssh's own failure and never an answer (`:175-176`, `:186`).
- **The script is fixed, and its hash is in its spec.** `REMOTE_SCRIPT` (`src/remoteScript.ts:137`) is one `sh` function
  read to its end and only then called with `</dev/null 2>/dev/null` (`:428`); the last line prints `COAI-END` (`:429`).
  Its doc comment gives 293 lines, 7497 bytes and the sha256, and says no test holds the hash (`:126-128`). It writes
  nothing on the VM. `REMOTE_TOOLS` lists the tools it needs; a missing tool is said, never read as "nothing" (`:30`).
- **The reply is parsed by a strict frame.** The frame starts at the LAST `COAI-BEGIN 1` and ends at the first `COAI-END`
  after it (`src/remoteFrame.ts:123-127`); an unknown marker makes the whole answer unreadable. Text from a VM loses its
  control and format characters before a person reads it (`src/remoteRead.ts:67`, `src/remoteSsh.ts:216`).
- **The consent is per host, in `globalState`.** `HostAnswer` is `{ host, answer, askedUtc }` (`src/remoteRead.ts:29-33`),
  at most 100 rows (`HOSTS_MAX`, `:38`); `consentOf` (`:130-137`), `withAnswer` (`:142-147`), `withoutHost` (`:166`) and
  `answersOf` (`:163`) are pure. The key is `coai.terminalHosts` (`src/remoteSessionHost.ts:23`). The question is a modal
  with the code `read-remote-session`: *"Read the Claude session on “x” over ssh?"*, buttons *Read* and *Not this host*
  (`:35-45`). It is asked only on a press, by the road (`src/remoteRead.ts:356-370`). *CoAI: hosts read over ssh*
  (`coai.terminalHosts`) lists the answers and changes or forgets one (`src/remoteSessionHost.ts:144-152`, `:221`).
- **The terminal panel's contributions.** `coai.askFromTerminal`, title "Ask other AI", icon `$(comment-discussion)`
  (`package.json:273-278`), is in `menus.view/title` with `when: view == terminal`, `group: navigation@90`
  (`:1214-1218`). The other terminal items are in the overflow groups `coai@1…3` (`:1219-1233`).
  `coai.terminalHosts` sits in the coai panel's overflow, group `remote@1` (`:1284-1288`).
- **A list that stays open exists once.** `src/conversationPickerCommand.ts` uses `createQuickPick` because it needs a title
  button and must rebuild its rows without closing (`:33-47`, `:197`). Every other list is a `showQuickPick`.
- **A webview page costs a running test.** `.agents/PROJECT.md:99`: a page is tested by RUNNING it
  (`bundledPage.test.ts`); a new assertion over page source text is refused.
- **Every command and every setting must be in the help**, in five languages (`src/test/helpCoverage.test.ts:14`;
  `src/helpContent.ts`, `helpRu.ts`, `helpUk.ts`, `helpDe.ts`, `helpEs.ts`). There are 68 `coai.*` settings today.
- **Every message a person sees goes through one door and is counted.** `notifyAndAsk` (`src/remoteSessionHost.ts:36`);
  `notification-sites.json` holds 185 sites and is generated (`:7`).
- **A timer pattern exists.** `everyMs(ms, tick)` returns its own stop function (`src/consultantHealthPanel.ts:214-218`).
- **How a person reads a vendor gauge here.** `src/gaugeWords.ts`: absent is absent, never 0 % (`:13`); `agoWords`
  rounds an age down (`:92-99`). It says "used"; the owner asked for "left" (decision 5).

**The Claude usage read in the agent-models mod** — read in `.agents/conventions/mods/agent-models/` (the rules
submodule at pin `5d2d5ec`; the same bytes as `dew_flow_conventions` at `61732bb`):

- The endpoint is `https://api.anthropic.com/api/oauth/usage` (`hooks/limits.tsx:17`), "the usage endpoint the built-in
  /usage reads" (`:2`). It is internal and its shape can change without notice (`:4-5`).
- The mod never sees a token. `$.session.authorize()` gives a handle, and `$.http.fetch(LIMITS_URL, { auth: auth.handle,
  headers: { Accept: 'application/json' } })` makes the call (`:157-160`). The only header the mod sets is `Accept`.
  What Claude Code adds for the handle is not visible there.
- Of the answer it keeps `five_hour.utilization`, `seven_day.utilization`, the first `weekly_scoped` entry of `limits`
  whose model is named `fable`, and `five_hour.resets_at` (`:45-56`, `:69-83`). `resets_at` counts only as ISO 8601
  with an offset (`:58-66`).
- The words: `percentLeft` is 100 minus the percent used, rounded and clamped; anything that is not a finite number is
  null (`:36-40`). A null draws `5h —` with no `% left` (`:105-106`); `resets in —` when the time is unknown or has
  come (`:86-93`, `:108-111`). The whole line: `5h 87% left · resets in 1h 23m · week 74% left · Fable 82% left` (`:117-118`).
- The mod reads the ACCOUNT with a Claude Code command, not a file: `claude auth status --json`
  (`hooks/prompts.ts:96`), of which it takes `loggedIn` and `email` (`:105-120`).
- On the owner's account the answer has no 5-hour window, and the owner chose to keep the dashes
  (`.agents/conventions/research/PLAN_agent_models_1_4_0.md:203-207`).
- A reopen fetches again only after 60 s (`LIMITS_INTERVAL_MS`, `hooks/limits.tsx:19`); one fetch was measured at 306 ms (`:18`).

**What coai already measured about the other two vendors** (`research/RESULTS_vendor_quota_gauges.md`, 2026-10-10,
codex-cli 0.160.0 and 0.154.0, agy 1.3.3 and 1.3.2):

- **Codex has a live gauge read, with a cost.** `codex app-server`, method `account/rateLimits/read`, answers in 0.7 s
  with `rateLimits.primary.usedPercent`, `windowDurationMins`, `resetsAt` (epoch seconds), `secondary` (null on that
  plan) and `planType` (`:83-116`). The read writes a log database and a models cache into `CODEX_HOME` (`:118-137`).
  Whether it rotates the refresh token when the access token has expired was not measured, so the rule there is: it runs
  only while no other `codex` of the same store is in flight (`:133-137`, `:293-301`). The raw stream carries the
  account id and machine names; a reader keeps the numbers only (`:174-178`).
- **`codex exec --json` carries no rate limit** (`:61-81`). Codex keeps session files at
  `~/.codex/sessions/…/rollout-*.jsonl` (`research/RESULTS_fast_mode_measured_2026-10-07.md:31`).
- **agy has no gauge but its refusal.** `agy --help` (1.3.3) lists no status, usage or quota command, and its state and
  settings files hold no quota keys (`:211-217`). agy keeps its state under `~/.gemini`
  (`src_server/src/Slots/SlotEnvironment.cs:54`: `~/.gemini/antigravity-cli/`;
  `research/RESULTS_agy_allow_rule.md:23`; its sign-in is there too, `research/RESULTS_agy_question_row_follow_up.md:55`).
- `~/.claude.json` is one file with every project's entries and the MCP servers of the user
  (`src/mcpClientsRead.ts:10`, `:25`), so it can hold values a person would not want printed.

**Read on this machine, 2026-11-19, as a hint only** (`claude --help`, Claude Code 2.1.328; written before step 0,
which then found 2.1.326 on the measured VM): there is no `usage` subcommand; `claude auth status` takes `--json`; `-p` has `--output-format json`,
`--no-session-persistence`, `--strict-mcp-config`, `--setting-sources` and `--max-budget-usd`; `--bare` never reads
the OAuth login, so it cannot be used for a usage read.

### What step 0 measured — 2026-11-19, on `ai2-root`

The record, with every command, file list and timing, is
[../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md). It is one
host, one Linux user (`root`, Ubuntu), one account for each vendor, Claude Code 2.1.326, codex-cli 0.162.1 and agy
1.5.12, and one run of each command form. The rows below were this plan's "not verified" list.

| # | The claim | What step 0 found | State |
|---|---|---|---|
| U1 | A Codex rollout file holds `token_count` events with `rate_limits` (`primary` and `secondary`: `used_percent`, `window_minutes`, `resets_at`) | True, with these names: a line of `type` `event_msg`, `payload.type` `token_count`, `payload.rate_limits`. **`primary` was the weekly window (10 080 minutes) and `secondary` was `null`**, so a window is named by its length, never by its slot. The line holds counts only, no text of a conversation. `rate_limits` also has `plan_type` (`prolite`) | **resolved** |
| U2 | The folder layout under `~/.codex/sessions`, and how to find the newest file without listing them all | `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`; 60 files; the newest is 527 525 bytes, 2 days old, with 7 such lines | **resolved**; how far from the end of a file the last record sits is W5 |
| U3 | `codex login status` exists and names an account without a secret | It exists and prints only `Logged in using ChatGPT`. It names **no** account | **resolved**: the script does not run it |
| U4 | `~/.claude.json` holds `oauthAccount` with `emailAddress` and an organization name | True, by key name. Not needed, because of U5 | **resolved**: the script opens no Claude file |
| U5 | Whether `claude auth status --json` prints an organization, and whether it prints anything secret | Exit code 0 in 278 ms. It prints `loggedIn`, `email`, `orgId`, `orgName`, `subscriptionType` and nine more keys, and no token or secret | **resolved**; whether it writes a file is W3 |
| U6 | Whether a non-interactive Claude Code command prints the usage, whether it makes a model call, what it starts and what it writes | `claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""` prints it in 3.6 s with **no model call** (`num_turns` 0, cost 0). No transcript and no `~/.claude.json` write. **It writes four cache files of Claude Code's own.** `claude --help` has no usage subcommand; `--bare` never reads the login | **resolved, road (a) taken**; three points are open: W1, W2, W4 |
| U7 | The path and the key names of the Claude credential file on a Linux VM, and the headers the usage endpoint needs | `~/.claude/.credentials.json`, mode 600; key names recorded. The headers were not measured | **half open, and not needed**: road (b) is not built |
| U8 | Where agy keeps the account on the VM's version, and whether any file or command states usage | agy 1.5.12 has no usage, quota or account command, and no file with a quota. An email address appears only in its log file | **resolved**: no usage; the account is not read |
| U9 | Whether `claude`, `codex`, `agy`, `curl` and `timeout` are found by `ssh <host> sh -s`, which `sh` the VM has, and whether its `printf` is a builtin | `sh` is `dash`; `printf` is a builtin. **`claude` and `agy` are not on the `PATH` of a shell that is not a login shell**: they are in `~/.local/bin`, which `~/.profile` adds. `codex` is in `/usr/local/bin`. `curl`, `timeout`, `stat` and `jq` are in `/usr/bin` | **resolved**: the script adds `$HOME/.local/bin` itself |
| U10 | That a QuickPick label draws `$(name)` as an icon | Not part of step 0 | **open**: the host scenario of E2 measures it |
| U11 | The Claude Code version on the VMs and the flags it has | 2.1.326 on this host; every flag used above was accepted | **resolved for this host**; the script reports the version of each host |

## The design

### 1. The view

- **A new command `coai.terminalAccounts`**, title "AI accounts and usage", icon `$(account)`. In `menus.view/title`:
  `when: view == terminal`, `group: navigation@91`, so it sits right after *Ask other AI* (`navigation@90`). It is in the
  command palette. It has no chord, so the skip-shell list does not change.
- **The icon is always there**, like the chat icon (O6). With no `Creds:` terminal open the list says one sentence: *"No
  `Creds: <host> / <session>` terminal is open, so there is no host to read."*
- **The view is a QuickPick instance (`createQuickPick`), not a webview.** The reasons:
  1. It is a short read-only list: today five hosts and three vendors, at most a few dozen rows.
  2. It needs what `conversationPickerCommand.ts` already needs: title buttons and new rows without closing, when a
     host's read lands. That pattern is in the code and reads well.
  3. A webview is a new bundled page with its own CSP, nonce and a test that runs the page. That cost buys only colour
     and layout, which the owner did not ask for.
  4. What a QuickPick cannot do is named under *What this plan does NOT do*: no colour for each number, and a long
     row is cut in a narrow window. If the real-window check R3 shows the rows cannot be read, a webview is a plan change.
- **The rows are grouped by account** (decision 4). A separator names the vendor and the account; under it one row holds
  the usage line, and the row's second line names the hosts and the age of the reading:

  ```
  ── Claude Code · <email> · <org> ──
  5h 44% left · resets in 2h 10m · week 23% left · Fable 51% left      week resets in 2d 14h
      ai1-dev2, ai1-dev3 · read 4 min ago
  ── Codex · account name not shown by Codex · plan prolite · ai1-dev2 ──
  week 86% left · resets in 4d 23h
      from a Codex session 2 d ago · read 4 min ago
  ── Antigravity · ai1-dev2 ──
  installed · agy states no usage, and its account is not read
  ── Hosts ──
  ai1-root — the last read failed 3 min ago: “ai1-root” did not answer over ssh within 20 s.
  ai2-dev2 — not asked yet. Choose this row to answer for this host.
  ai2-root — turned off: your answer for this host is “Not this host”.
  ```

  - **The account** is the full email and the organization's name for Claude Code (O2), never a secret. Two hosts are
    one group only when the vendor and the whole account text are equal. An account that is not known is never grouped:
    it is one group for each host. Codex states no account name (U3), so a Codex group is always one host; its
    separator shows the plan type. A host's alias is never used to group (the RESULTS file, § 10: an alias is not
    proof of the machine).
  - **The usage line** uses the mod's words: `<window> <n>% left`, `resets in <time>`, `·` between the parts; a missing
    value is `—` with no `% left`, never `0%` (decision 5). The rules are the mod's own (`percentLeft`, `resetText`,
    `minutesLeft`), written once in a new pure module here and held to the mod's cases by a table of tests. The reset
    text is made when the list is drawn, from the reset moment. The row's label is the mod's line: the 5-hour window
    with its reset, the week, and one part for each model that `/usage` names. The week's reset, which the VM states
    too, is in the row's description. A Codex window is named by its length: 300 minutes is `5h`, 10 080 is `week`;
    another length is said in hours.
  - **The numbers are those of `/usage` on the VM.** It prints whole percents "used"; the list shows 100 minus that.
    The mod rounds a fraction, so the two views can differ by one point. R3 checks equality with `/usage` and
    "within one point" with the mod line.
  - **When two hosts of one account were read at different times**, the group shows the newest reading and its age.
  - **Every host has its own sentence in the *Hosts* part** when it has no reading to show: not asked yet, turned off,
    never read, or the last read failed (with the runner's own sentence and its age). A host whose last read failed
    keeps its older numbers in its account group, and the group's second line says the later read failed.
  - **Usage that was not read says why**, in its own words: *usage not read: Claude Code 2.1.326 on this host printed
    no usage line this version understands*, *usage not read: `jq` is not installed on this host*, *agy states no
    usage*. It is never a row of dashes with no reason.
  - **Choosing the row of a host that was not asked** raises the shared question for that host (§ 3). Choosing any
    other row does nothing. There is no question for several hosts at once.
  - **Two title buttons**: *Refresh now*, and *Stop the accounts round* (§ 3). While the round is off the list holds
    one row: *The accounts round is off. Choose this row to turn it on.*
- **Text from a VM is made plain at one road in**: control and format characters are taken out, the length is cut, and
  `$(` is broken so the list never draws an icon a VM chose (U10). The existing pick list of the session reader passes
  VM text with `$(` intact (`src/remoteRead.ts:213`, `:220-226`). This plan does not change it; whether it should use
  the same function is a question for the owner after U10 is measured.

### 2. The round

- **Which hosts.** The distinct hosts of the live (not exited) terminals whose name gives `kind: 'session'`, read through
  `liveTerminals` — and of these only the hosts whose answer in the shared consent is "yes" (§ 3), and only while the
  accounts round is on. A host with no answer or a "no" is never contacted.
- **One host at a time.** A round takes the hosts in name order, starting one host later each round, and awaits each read
  before the next. Two reads never run at once. A failed host is recorded with its sentence and the round goes on.
- **The answer is read again just before every call**, from the store, as the session reader checks the terminal's
  name again before every spawn. A "no" or a *Forget* given while a round runs stops the next call to that host.
- **One ssh call for each host in a round**: the pinned options of `SSH_OPTIONS`, unchanged, then
  `-- <host> sh -s -- <mode>`, the script on stdin. `<mode>` is one of two fixed words (below).
- **Every 10 minutes, counted from the END of a round** to the start of the next, so two rounds never overlap. The
  value is a constant, `600 000 ms`, not a setting (O4).
- **Usage once for each account in a round** (decision 4). The extension remembers, in memory, which Claude account each
  host had at its last read. When a host's account already has a usage reading from this round, that host is called
  with the mode `no-claude-usage` and the script reads the account only; else the mode is `claude-usage`. A host never
  read is called with `claude-usage`. If the first host of an account fails, the next host of that account reads the
  usage in the same round. If a host turns out to have another account than last time, its usage waits for the next
  round. Codex usage is a local file read with no cost, so it is read on every host.
- **A usage read that went wrong is not repeated blindly.** When a host's reply says the usage command made a model
  call (`:turns` is not 0) or printed no line the pattern knows, the extension calls that host with `no-claude-usage`
  from then on, until the window is reloaded or the host reports another Claude Code version. So a Claude Code version
  that sends `/usage` to the model costs one small call for each window, not six an hour.
- **The first round** starts when the accounts round is turned on (the first press of the icon, § 3). In a later window
  it starts 30 s after the window has a `Creds:` terminal of an allowed host — at activation or when the first such
  terminal opens. It is not started by a reload alone with no allowed host.
- **It stops when no `Creds:` terminal is open.** With no live terminal of an allowed host the timer is cleared, not
  left idle. `onDidOpenTerminal` arms it again; `onDidCloseTerminal` checks again. A host whose last terminal closed is
  dropped from the next round, and its reading is dropped from memory. A read in flight is not killed by a close; its
  result is dropped.
- **The timer never asks a person.** The shared question is raised only by a press: on a host's row in the list, or by
  the session reader's own doors. A round over hosts that were not asked reads nothing and shows nothing.
- ***Refresh now*** is a title button of the list. It starts a round at once, unless one is running (the list shows it is
  busy) or the last round ended less than 60 s ago (the mod's own floor). Opening the list draws what memory holds; it
  starts a round only when an allowed host has no reading yet.
- **Results are in memory only.** Nothing of a reading is written to disk, to `globalState`, to a ledger or to a log:
  the readings hold email addresses and the usage of a person's accounts, they are 10 minutes old at most, and after a
  reload the next round brings them back within a minute. What is persisted is the shared consent, which exists
  today, and one switch (§ 3).
- **Limits of one call**: killed at 20 s (the tree, as today), stdout capped at 64 KiB. Both are ceilings. Measured on
  the VM: the account command 0.3 s, the usage command 3.6 s with the flags (6.3 s without), whole calls between 3 and
  10 s. Inside the script the account command has 3 s and the usage command 8 s (§ 4). The time of a whole call from
  Windows with the pinned options was not measured (the plan's M7); R9 measures it. A round over five hosts is at most
  100 s.
- **A failed read is logged once, not every round.** The runner's one log line is written when a host's failure
  sentence changes, and once when it reads again. A host that is down for a day is two lines, not 144.
- **Not serialized with the session reader.** A press of *take the question* can run while the round reads the same
  host: two ssh children to one host, both read-only. Each VS Code window runs its own round, so two windows with
  terminals of one host read it twice in 10 minutes. Both are named, not prevented.

### 3. The consent — shared with the session reader (the owner's O3)

**Decision (the owner, 2026-11-19): there is no separate consent. The per-host answer of the session reader is the
answer for the accounts read too.** A host whose answer is "yes" is read for accounts without a new question.

**What this widens — said plainly.** Until this release the question was *"Read the Claude session on “x” over ssh?"*.
A "yes" covered one read that a press starts and that prints a transcript. From this release the same "yes" also
covers: a read that a timer starts with no press, every 10 minutes while a `Creds:` terminal of that host is open; the
names of the accounts signed in there; and a start of Claude Code on that host, which asks Anthropic with that host's
own login. For the owner this is the decision of O3. For another person who installed the extension and answered
*Read* before this release, it is wider than what they were asked. The Security section holds this as finding F1.

**What the person is told, and where.**

- **The question's words change, for every host asked from this release on.** Same code (`read-remote-session`), same
  buttons (*Read*, *Not this host*), same store. The title becomes *"Read the Claude session and the AI accounts on
  “x” over ssh?"*. The modal's detail says what a "yes" covers: the session of a terminal, when you press *take* or
  *add*; and, while the accounts round is on and a `Creds:` terminal of that host is open, every 10 minutes, which
  Claude Code, Codex and Antigravity accounts are signed in there and how much of their limits is left — for which
  Claude Code is started on that host and asks Anthropic with that host's own login. Read-only, by fixed scripts, as
  your ssh user.
- ***CoAI: hosts read over ssh*** names it in each row: *the Claude session and the AI accounts are read*, or *not
  read*. Its three choices stay: *Read*, *Not this host*, *Forget*.
- **The accounts list** shows, for each open host, its answer and where to change it (the *Hosts* part of § 1).
- **The help, in five languages**: the new command, and the paragraph on the per-host question, which today says the
  first read of a host asks once. It gains what the answer now covers and how to stop the accounts round.
- **`SECURITY.md`** and the release notes say the same (the Security section).

**A stored "yes" is not used by a timer until the person has used the list.** The accounts round is **off** after
install and after the update. The first press of the icon turns it on, in that press, and the list then shows which
hosts it reads under which answer. The switch is one row in `globalState`, key `coai.terminalAccountsRound`
(`{ on, changedUtc }`). The title button *Stop the accounts round* turns it off again: the timer is cleared, the
readings are dropped from memory, and no host is contacted for accounts until the person turns it on from the list.
This is not a consent and asks nothing: it is a switch for the whole installation. It is the planner's addition to
O3, so it is question N2 for the owner; this text takes its recommended answer.

**How a "no" or a *Forget* stops the accounts read.**

- ***Not this host*** — in the question, or later in *CoAI: hosts read over ssh*: no ssh call of any kind goes to that
  host, neither for a session nor for accounts. Its reading is dropped from memory at once.
- ***Forget***: the host has no answer. It is not contacted. Its row in the accounts list says *not asked yet*, and a
  press on that row raises the shared question again.
- A change made while a round runs takes effect before the next call (§ 2). A call that is already running ends within
  its 20 s, and its result is dropped.

**What a person can no longer choose.** They cannot allow the session read on a host and refuse the accounts read on
the same host. The per-host answer is one answer. The only finer control is the switch above, for all hosts at once.

**No second store, no reach number.** The first draft had a second `globalState` key and a "reach" for each row. Both
are gone with O3. The store stays `coai.terminalHosts`, with its row `{ host, answer, askedUtc }`, its cap of 100 rows
and its pure functions (`consentOf`, `withAnswer`, `withoutHost`, `answersOf`).

### 4. The remote script and its reply — the cornerstone

- **Fixed text, sent whole, pinned.** A new constant, the accounts script, in the shape of `REMOTE_SCRIPT`: POSIX `sh`
  (the VM's `sh` is `dash`, measured), one function read to its end and only then called with
  `</dev/null 2>/dev/null`, a begin line first and an end line last, so a script cut on the way does nothing. Its
  sha256 is in its spec, and this time a test holds the hash too: this script runs a vendor program, so an edit of the
  text without the spec must turn a test red.
- **No write by our script, no `cd` to a folder a person owns, no background job, no temporary file, no
  here-document.** Every tool it needs is checked first; a missing tool is said, never read as "nothing found".
- **The `PATH`.** `ssh <host> sh -s` is not a login shell, and on the measured VM its `PATH` has no `~/.local/bin`,
  where `claude` and `agy` are. The script's first line inside the function puts `$HOME/.local/bin` in front of the
  `PATH`, as that VM's `~/.profile` does for a login shell. Nothing else of a profile is read.
- **The tools.** `jq`, `timeout`, `tail`, `ls`, `sed`, `grep` and `command`. `jq` was found on the measured VM; it is
  not known for the others. **`jq` is required, and the reason is safety by construction**: a field is printed because
  it was named, so nothing beside it in a JSON document can leave the VM. Cutting JSON with `sed` would be a pattern
  next to private text (the person's activity in `/usage`, a transcript line of Codex), and one wrong pattern there is
  a leak with no error. A host without `jq` gets the state `unreadable no-jq` for Claude and Codex, and the list says
  that `jq` is not installed on that host. We install nothing.
- **The operand** is one of two fixed words, `claude-usage` or `no-claude-usage`, checked by a closed list on this side
  and again in `sh`. No value from a terminal's name or from a VM is ever an operand.
- **The reply is line-based, fixed and small.** A marker line names a vendor and a state from a closed list; a data
  line starts with `:` and holds one key from a closed list and its value. The shape (the spec fixes the exact words):

  ```
  COAI-ACCOUNTS 1
  COAI-VENDOR claude found            # found | absent | signed-out | unreadable <word>
  :version 2.1.326
  :email <email>
  :org <org>
  :plan team
  COAI-USAGE claude read              # read | skipped | failed <word>
  :turns 0
  :line Current session: 56% used · resets Nov 19, 3pm (UTC)
  :line Current week (all models): 77% used · resets Nov 22, 3am (UTC)
  :line Current week (Fable): 49% used · resets Nov 22, 2:59am (UTC)
  :unmatched 0
  COAI-VENDOR codex found
  :sign-in-file present
  :plan prolite
  COAI-USAGE codex read               # read | failed none
  :window 14.0 10080 1795518850       # used percent, window minutes, reset (epoch seconds)
  :as-of <ISO time> record            # the record's own time, or the file's time and the word "file"
  COAI-VENDOR agy found               # found | absent
  :sign-in-file present
  COAI-USAGE agy none
  COAI-ACCOUNTS-END
  ```

  The script sends "used", as the vendors state it; "left" is made on this side by the one rule. At most 64 lines, each
  at most 512 bytes; the script cuts every value itself.
- **The script cuts, the extension decides.** On the VM, `jq` names a field and `grep` keeps a line that fits an
  anchored pattern. Whether a value is a number, in range, an email or a time is decided here, by the parser.
- **The parser** (`readAccounts`, pure) is the one road in. The frame is the last begin line and the first end line
  after it, through ONE framing function shared with `remoteFrame.ts` (extracted, not copied). An unknown marker, an
  unknown key, a line that is neither, a line past the limits or an exit code that is not 0 makes the whole answer
  `unreadable` with a reason from a closed list. A percent is digits with at most two decimals, 0–100; an epoch is
  digits; an ISO time needs an offset or `Z`; a text value loses control and format characters and is cut. **A value
  that looks like a credential is dropped and its vendor is `unreadable`**: 40 or more characters of letters, digits,
  `_` and `-` with no space, `@` or `.`, or a known token prefix. Nothing of a reply is run, used as a path, or put in
  a log or an error's text.
- **The typed result** has one arm for each state, so "absent", "signed out", "no usage source", "usage skipped",
  "usage failed" and "unreadable" are different values with different sentences. A number that is not there is `null`,
  never 0.

**What the script reads, for each vendor:**

- **Claude Code — the account.** `claude auth status --json`, the command the mod uses, under `timeout` (3 s; measured
  0.3 s). `jq` prints four named fields: `loggedIn`, `email`, `orgName`, `subscriptionType`. The organization's id and
  the other keys are not printed. `loggedIn` false is `signed-out`. Our script opens no Claude file: not
  `~/.claude.json`, not the credential file. The first draft's fallback to `~/.claude.json` is gone (U4, U5).
- **Claude Code — the version.** `claude --version` (measured: `2.1.326 (Claude Code)`), so a usage that cannot be read
  names the version beside it.
- **Claude Code — the usage: road (a), taken.** Only on the mode `claude-usage`. The command, with the flags measured
  in step 0 and no others:

  ```
  claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
  ```

  - **stdin is `/dev/null`**, and the command runs under `timeout` (8 s; measured 3.6 s). A command that is killed is
    `failed timeout`.
  - **The working folder is `/`.** The reasons: the script may not write, and a temporary folder is a write that a
    kill at the time limit leaves behind, on every host, every 10 minutes; `$HOME` is wrong, because Claude Code would
    take `~/.claude` for a project's folder; `/` exists on every VM and holds no project. Step 0 ran in a temporary
    folder, **not** in `/`, so W1 measures `/` before the spec is final. If `/` does not behave the same, the folder
    goes to the owner as a question: this plan does not fall back to a temporary folder by itself.
  - **`TZ=UTC` is set for this one command.** The reset text names its zone, `(UTC)` on the measured VM. Setting `TZ`
    is a request to Claude Code; its effect on a VM in another zone was not observed. So the parser does not trust
    it: it reads a reset only when the text ends in `(UTC)`.
  - **Only matched lines leave the VM.** `jq` prints `num_turns` and the `result` text. Of the text, `grep` keeps the
    lines that fit one of three anchored patterns over a closed alphabet, and at most eight of them:
    `Current session: <n>% used · resets <text> (<zone>)`, `Current week (all models): …`, and
    `Current week (<model>): …`. Everything else is dropped **on the VM** — first of all the section *"What's
    contributing to your usage?"*, which lists the person's requests, subagents and MCP servers. `:unmatched` is the
    COUNT of other lines that start with `Current `, never their text, so a changed format is seen and not read as "no
    such window".
  - **The parse, on this side.** `<n>% used` gives "left" as 100 minus n. `Current session` is the `5h` window,
    `Current week (all models)` is `week`, and `Current week (<model>)` is a window named by the model. The reset text
    has the form `<Mon> <day>, <hour>[:<minutes>]<am|pm> (UTC)` and no year. The moment is built in the year of this
    machine's clock (UTC); when that moment is more than one hour in the past, the next year is tried; the result
    counts only when it is between one hour ago and eight days ahead. A reset that fits no form, has another zone, or
    falls outside that range is **a dash** (`resets in —`), and the percent is still shown. A window with no line is a
    dash too (the owner's other account has no 5-hour window). No matched line at all is `failed format`, shown with
    the Claude Code version.
  - **A model call is checked, not assumed.** Step 0 measured `num_turns` 0 and cost 0: `/usage` is answered by Claude
    Code itself. The script sends `num_turns` as `:turns`. A value that is not 0 is `failed model-call`, and the
    extension stops asking that host (§ 2).
  - **What Claude Code writes on each such read** is in the Security section: four cache files of its own, measured.
- **Road (b) — allowed by the owner (O1), not built.** The script reading the VM's Claude credential and calling the
  endpoint with `curl`. No line of the script, no test and no consent text of this plan is for it. Building it is a
  change of this plan with its own security judgement. **Road (c)**, the account only, is what the list shows for a
  host whose usage cannot be read.
- **Codex — the usage.** The last `token_count` record of a session file under `~/.codex/sessions/YYYY/MM/DD/` (U1,
  U2). The script takes at most the three files changed last in the two newest day folders, reads a bounded tail of
  each, and lets `jq` keep only lines that are an `event_msg` with `payload.type` `token_count` and print, of the last
  one, the numbers of `payload.rate_limits.primary` and `.secondary` (each may be `null`), `plan_type`, and the
  record's `timestamp`. A file name counts only when it fits `rollout-<ts>-<uuid>.jsonl`. A line that is not JSON (the
  first line of a tail is usually cut) prints nothing. No network, no login, no write. The numbers are as old as the
  last Codex turn on that host, and the list says so (*from a Codex session 2 d ago*). A record whose reset moment has
  passed is shown as *no reading since this window was reset*, not as an old percent. No record is `failed none`. The
  bound of the tail and the form of `timestamp` are W5. The live read, `codex app-server`, is **not taken** (O5).
- **Codex — the account.** `~/.codex/auth.json` holds tokens and is never opened; the script only tests that it
  exists (`:sign-in-file`). `codex login status` names no account (U3), and whether it writes or refreshes a login was
  not measured, so the script does not run it. The list shows *account name not shown by Codex* and the plan type.
- **Antigravity.** agy 1.5.12 states no usage and has no account command (U8). The script tests that `agy` is found and
  that its sign-in file exists, and opens nothing. The list says *installed · agy states no usage, and its account is
  not read*. Two things are deliberately not done. **The account is not taken from `cli.log`**: an email address
  appears there, but a log's form is no contract and a log holds other text. **`agy` is not started**, not even for
  its version: a start of that 269 MB program was not measured for what it writes or fetches, and on another machine
  agy updated itself on a start (`research/RESULTS_agy_read_file.md`). So the list shows no agy version (question N4).

### 5. Step 0 — the measurement: done 2026-11-19, with five points owed

Step 0 ran on `ai2-root` on 2026-11-19, with the owner approving each exact command in the Creds dialog. Nothing was
installed. No value of a secret file was printed: key names, sizes, modes and times only.

| # | What was to be measured | Result |
|---|---|---|
| M1 | The shell and the tools under `ssh <host> sh -s` (U9, U11) | **Measured**: `dash`, a builtin `printf`, `claude` and `agy` only in `~/.local/bin`, the versions |
| M2 | `claude auth status --json`: the key names, the time, the exit code, an organization or not, and whether a file changed (U5) | **Measured**, but for the last part: whether a file changed was not recorded (W3) |
| M3 | Road (a): `claude --help`, what `-p` does with a slash command, and the six conditions (U6) | **Measured**: two runs. See the table of conditions below |
| M4 | The key names of `~/.claude.json` and its account object (U4); the credential file only if road (a) failed (U7) | **Measured**, key names only. The credential file's key names were listed although road (a) held; the RESULTS file records that as a deviation |
| M5 | Codex: the layout, the newest file, the rate-limit records, `codex login status` (U1–U3) | **Measured** |
| M6 | agy: its version, its help, the file names, the keys that could name the account (U8) | **Measured** |
| M7 | The draft script from Windows with the pinned ssh options: the time of one read, twice, and the size of the reply | **Not run.** It is real-window check R9 |
| M8 | `ps` from a second Linux user shows no secret in any argument | **Not run.** On road (a) the script holds no secret, so its arguments are the fixed words of § 4. That is a reason, not an observation; the marker-token test shows it for the exact text |

**The six conditions of road (a), as measured** (the command of § 4, one run):

| Condition | Result |
|---|---|
| It prints the numbers | Met |
| It makes no model call | Met: `num_turns` 0, cost 0, API time 0 |
| It starts no hook, no MCP server and no plugin of the person | Reported as met, from the flags, the shorter time and the shorter list of changed files. The VM has a `PreToolUse` hook, the plugin `feature-dev` and three MCP servers at user level. A list of processes during the run was not taken (W2) |
| It writes no session file and leaves no process | No transcript, no `~/.claude.json` write. **Not met as first written ("nothing written"): four cache files of Claude Code's own are written on every read.** A process left behind was not looked for (W2) |
| It ends within the time limit with no terminal and an empty stdin | Met in the `creds_exec` call: 3.6 s, stdin `/dev/null`. From Windows over the pinned ssh call it is R9 |
| A strict pattern can read the output | Met: JSON, with one fixed line for each window. The reset has no year (§ 4) |

**Owed measurements W1–W5.** Each is one read-only command on a VM and needs the owner's go for its exact text, as in
step 0. The planner recommends running them **before the plan round** (question N3), so the approved plan holds them.
If they run later, at the E1.S2 spec, a result that breaks a condition is a change of an approved plan. For each one
this plan already says what a bad result means:

| # | What is measured | If the result is bad |
|---|---|---|
| W1 | The usage command from the working folder `/`: the same numbers, the same four changed files, nothing written in `/` | The folder goes to the owner as a question. No silent fallback to a temporary folder |
| W2 | A list of processes while the usage command runs and after it: no MCP server, no hook process, nothing left | Road (a) fails that condition: Claude usage is off for every host (road (c)) until the owner decides |
| W3 | Whether `claude auth status --json` and `claude --version` change any file | The files are named in the Security section and put to the owner with N1. If a file of the person changes (`~/.claude.json`, a transcript), that command is not run |
| W4 | Two usage reads in a row: is `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json` ONE file written again, or a new file each time | A new file each time is growth of 144 files a day for each account's host. Road (a) then fails, as for W2 |
| W5 | Codex: how far from the end of a session file its last `token_count` line is, and the form of its `timestamp` | Sets the tail's bound and the time rule in the spec. No bad result: with no usable `timestamp` the file's own time is used and named |

Within the plan as written: the field names, the limits under the ceilings, the bounds of W5. Outside it, and so a
change once the plan is approved: anything installed on a VM, a second ssh call for a host in a round, a file with a
secret that this plan does not name, a write by our script, road (b).

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs. Nothing is installed or written on a VM by our script.
- No real time, no push from the VM, no setting for the interval.
- No colour for the numbers (the mod colours them), and no webview.
- No history, no chart, no ledger of the readings, no alert when a limit runs low.
- No reading of a host that has no open `Creds:` terminal, and none of this machine's own accounts.
- No live Codex read (`codex app-server`), no `codex login status`, and no opening of `~/.codex/auth.json`, ever.
- No start of `agy`, no reading of agy's log, and no opening of its sign-in file.
- No opening of `~/.claude.json` or of the Claude credential file by our script.
- **No road (b).** The owner allowed it (O1); this plan does not build it.
- No separate consent for accounts, and so no way to allow the session read and refuse the accounts read on one host.
- No question for several hosts at once.
- No refresh of any login by our script. An expired login is said as it is.
- No grouping of accounts that are not known, no grouping by host alias, and no guess that two hosts share an account
  from equal numbers.
- No change to `SSH_OPTIONS`. Connection sharing is still not pinned, as `SECURITY.md` says today.
- No lock between VS Code windows: each window with `Creds:` terminals runs its own round.
- No change to the session reader's pick list (the `$(` note in § 1) without the owner's word.

## Security

This section is the security judgement of the plan (Fable, `subagent-models.md`: the security row). The cornerstone
spec repeats it for the exact script text, and it must stand before any E1 code.

**The new edges.** (1) A read-only ssh call that a TIMER starts, to hosts whose shared answer is "yes". (2) On each
usage read, a start of Claude Code on the VM, which calls Anthropic with the VM's own Claude login. (3) Text from a VM
drawn in a list.

**Road (a) is taken: no secret passes through our script.** The script opens no credential file, holds no token in a
variable and gives none to any program. Claude Code reads its own login inside its own process, as it does when the
person starts it by hand.

**Every secret and private thing the script comes near, and where it goes:**

| What | Where it is | What our script does with it |
|---|---|---|
| The Claude OAuth access token and refresh token | `~/.claude/.credentials.json` on the VM (mode 600) | Never opened. Claude Code reads it in its own process. With a valid login the file was not among the files changed by a usage read (step 0, run 2). With an expired access token Claude Code may refresh it and write the file, as on any start; that case was not measured |
| `~/.claude.json` (holds every project's entries and the MCP servers of the user) | the VM | Never opened by our script. Not written by the usage command with the three flags (measured). The plain command did write it, with a backup — that is why the flags are part of the pinned command |
| The person's activity: requests, top subagents, top MCP servers | the `result` text of `/usage`, after the three usage lines | Passes through a pipe on the VM, inside the person's own ssh login, and is **dropped on the VM**. Only lines that fit the three anchored patterns leave it. The marker-token test puts a marker in that section and fails if it is in the reply |
| `~/.codex/auth.json` | the VM | Never opened. Only tested for existence |
| Codex session files (a transcript: private, not a credential) | the VM | A bounded tail of at most three files is read. `jq` prints named numbers of the last `token_count` record and nothing else; a transcript line prints nothing |
| agy's sign-in file and its log (the log holds an email address) | `~/.gemini/antigravity-cli/` on the VM | Never opened. The sign-in file is only tested for existence |
| The person's ssh keys and agent | this machine | Used by `ssh` as today; `ForwardAgent=no` |
| The account email and organization name (personal data, not a secret) | the reply | In memory and in the list (O2). Never logged, never stored, never sent on |

**What Claude Code writes on the VM on each usage read — measured, and not ours to prevent.** Our script writes
nothing. The usage command, with the three flags, changed exactly these four files in step 0:

- `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json`
- `~/.claude/policy-limits.json.stamp.json`
- `~/.claude/policy-limits.json.signature.json`
- `~/.claude/remote-settings.json.signature.json`

They are Claude Code's own caches, in its own folder, owned by the same Linux user. The names say Claude Code fetched
its policy limits and its remote settings on that start; those calls were not observed. **How often:** once for each
Claude account in a round, on the one host of that account that reads the usage — at most 6 times an hour for each
account and window. The account command and the version command run on every allowed host in every round; whether
they write a file is W3. Whether a live Claude Code session on that host notices these files changing was not
measured; R7 watches the live sessions for an hour. Whether the first of the four files is rewritten or added anew
each time is W4. **The first draft's condition was "nothing written". It is not met.** The planner's judgement: four
files of the vendor's own cache, which every start by hand writes too, are acceptable, if W4 shows no growth and R7
shows the live sessions undisturbed. It is the owner's VM, so it is question N1.

**Finding F1, for the owner: the shared consent widens answers that were given to another question.**

- **No rule of `.agents/` forbids it.** `common/security.md` has no rule on consent. The conflict is with two texts of
  this repository, and this plan changes both openly:
  1. `SECURITY.md`, *The extension's one outward ssh call*: *"Read-only, to a host the person agreed to. The first read
     for a host asks once (a modal naming the host)"*. The modal named the Claude session, and nothing else.
  2. The terminal-door plan, *The person agrees to each host once*, with the question *"Read the Claude session on
     “x” over ssh?"*.
- **Every "yes" stored before this release answered that question.** After it, the same row also allows a read with no
  press, the names of the signed-in accounts, and a start of Claude Code on the VM that asks Anthropic with that
  host's login.
- **For the owner's own installation, O3 is that consent**, given on 2026-11-19 knowing what it covers. The extension
  is public. For any other person who answered *Read* before, nobody has said yes to the wider act.
- **The planner's first judgement was a separate consent with a reach. The owner decided otherwise, and this plan
  follows the owner.** What it does so that no stored "yes" is used in silence: the accounts round is off until the
  first press of the icon; the list then names each host and its answer; one button stops the round for the whole
  installation; the question's words, the hosts list, the help in five languages, `SECURITY.md` and the release notes
  all say what a "yes" covers (§ 3).
- **What is left after that:** the answer cannot be split for one host, and a person who presses the icon out of
  curiosity turns the round on for every host they once allowed. The list says so on that first press.

**What an attacker gains.**

- **Someone who is that Linux user on the VM, or root.** They already hold every file the script reads. What is new: they
  can make the list show wrong accounts or numbers (a false `claude` or `jq` in `~/.local/bin` or on the `PATH`, an
  edited file). They cannot reach this machine through the reply: it is bounded (20 s, 64 KiB, 64 lines), parsed by a
  closed vocabulary, never run, never a path, shown with control characters and `$(` taken out. The script runs
  `claude` by its name every 10 minutes; a false `claude` there already runs whenever the person types it.
- **Another Linux user on the same VM** (the owner has several users on one VM, and two aliases can be one machine).
  They can list processes and read every argument. **No secret is ever an argument**: the arguments are the fixed
  words of § 4. They cannot read another user's files, variables or pipes. The script writes no file, so nothing is
  left in a shared folder.
- **Someone on the network.** Between this machine and the VM: ssh, with `StrictHostKeyChecking=yes`, so an unknown or
  changed host key is a refusal. Between the VM and the vendor: TLS, by Claude Code itself.
- **A program that opens a terminal named `Creds: evil / x`.** A name is not proof. The timer contacts only hosts with a
  stored "yes", and a "yes" is written only by a press on the question that names the host. `BatchMode=yes` makes a
  password prompt a refusal.
- **A hostile reply.** It can only become text in a list, or `unreadable`. The credential-shape rule of the parser is
  a net under the script: if a defect ever printed a token, it would not reach the screen. It is a net, not the
  measure; the measure is that the script has no line that opens a credential file.
- **Logs.** A failed read logs the host, how it ended and ssh's own last line. The script's stderr goes to
  `/dev/null`, so no vendor error text reaches ssh's stderr. stdout is never logged.

**Judgement on road (a): yes.** It runs the vendor's own client with the person's own login on the person's own VM —
what the person does by hand. Our script handles no credential. Measured: no model call, no transcript, no
`~/.claude.json` write. Reported and not observed: nothing of the person's configuration started (W2). Not met as
first written: nothing written (the four cache files, N1). Not measured: the working folder `/` (W1), growth of the
plugin store (W4), a login that needs a refresh. A condition that fails in W1–W4 is not worked around: the road fails
for the usage, and the list shows the account only.

**Judgement on road (b): the owner allowed it (O1); it is not built, and the planner still recommends against
building it.** If it is ever built, it is a change of this plan, and these are its conditions, each with a test:
`printf` is a shell builtin on that VM (measured true on `ai2-root`; checked by the script, else the usage is off); the
token goes from a shell variable that is not exported to `curl -q -K -` on stdin, never an argument (another user's
`ps` reads those), never the environment, and not a header file either, because a file is a write on the VM that a
killed call can leave behind; `-q` so a `~/.curlrc` cannot reshape the call; https only, the one fixed URL, no
redirect, a time limit and a size limit; the script cuts the numbers out and never prints the body; **never a
refresh** — an expired token is `failed expired`, and Claude Code refreshes it on its own next use; and the consent
question is weighed again, because the script would then read the Claude login. What cannot be contained: it is a
client that is not Claude Code, calling an internal endpoint with a subscription login, every 10 minutes, around the
clock, on every account. The asset at risk is the account itself, and the vendor can change or refuse that endpoint
without notice (`hooks/limits.tsx:4-5`). It also puts a credential into our script, so one defect in one line becomes
a leak. The headers that endpoint needs were not measured.

**Judgement on the consent: the owner's decision, shared (§ 3), with finding F1 above.** **Judgement on persistence:
nothing of a reading is stored; the shared answers exist today; one switch is added.** **Judgement on Codex: the
passive file read**, because the live read can refresh a login that live sessions share, and that case was never
measured. **Judgement on agy: nothing is opened and nothing is started.**

**Residuals, named.** A person's `ControlMaster` can make the call a persisting master on Linux or macOS, as today. The
timer makes one ssh login for each host every 10 minutes while a terminal is open, which a VM's ssh log will show. The
list shows an email on screen, so a screenshot shows it (O2). Each usage read is a start of Claude Code, with whatever
Claude Code does on a start: its caches above, its own network calls, and possibly its own update check — none was
seen in step 0's file list, and none is ours to switch off. A VM that prints the reset in another zone than UTC shows
the percent and a dash for the reset. A Claude Code version that changes the three lines shows *usage not read* with
the version.

`SECURITY.md`: the section *The extension's one outward ssh call* becomes two calls. It gains: the timer; that the
per-host answer now covers both reads, and what that widens; the switch of the accounts round; road (a) and what the
VM then calls; the four files Claude Code writes on a read; the person's activity dropped on the VM; and the residuals
above.

## Growth surfaces

- **The readings**: in memory, one for each host with an open `Creds:` terminal, a few hundred bytes each, dropped when
  the host's last terminal closes, when its answer is no longer "yes", when the round is stopped, and with the window.
  Bounded by the number of open hosts; today five.
- **The consent**: no new store. `coai.terminalHosts` stays as it is, capped at 100 rows.
- **The switch of the accounts round**: one `globalState` row, `{ on, changedUtc }`, under 100 bytes. It does not grow.
- **The ssh child**: one for each host in a round, killed at 20 s with its tree, stdout capped at 64 KiB. At most one
  alive at a time for each window.
- **The timer**: one for each window, cleared when no `Creds:` terminal of an allowed host is open or the round is
  stopped, disposed with the extension. A round that is interrupted by a reload leaves nothing: its child is killed
  with the extension host's tree, and memory is gone.
- **Log lines**: one when a host starts to fail and one when it reads again — not one for each round.
- **On the VM, by our script**: nothing.
- **On the VM, by Claude Code on a usage read**: four cache files (the Security section). Three have fixed names, so
  they are written again and do not grow. The fourth has a variable part in its name. **Whether it is one file or a
  new file for each read is not known (W4).** If it is new each time, that is 144 files a day for each account's host,
  with no owner to retire them, and road (a) fails. This plan does not delete a file of Claude Code.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, made by Claude Code. None by our code.

## Boundaries with other plans

This plan takes no slice of another plan's open work. It builds on code that shipped, and with O3 it **changes one
thing that the terminal-door plan built**: what its per-host answer covers, and the words that say so. The table says
what is whose, so it is read as complete:

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, *take*, *add*, *Asked* | [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0) | This plan widens the runner so a call needs no session operand. The session read itself does not change |
| The per-host consent: the store `coai.terminalHosts`, the question `read-remote-session`, *CoAI: hosts read over ssh* | the same plan | **This plan changes its meaning (O3)**: the same answer also allows the accounts read. This plan rewrites the question's title and detail, the rows of the hosts list, the help paragraph and `SECURITY.md`. The store, the code, the buttons and the cap do not change |
| The gauge of coai's own rounds on this machine and on a Team server (`gaugeWords.ts`, the Team server's capture) | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing. That gauge counts coai's rounds; this list shows a person's accounts on other machines. No shared store, no shared reader |
| The account and limits line inside Claude Code | `dew_flow_conventions · mods/agent-models` | Nothing is changed there. This plan carries its word rules over and tests them against its cases |

Order: this plan starts after the terminal-door plan's last release is cut, so the runner is not widened and the
question is not reworded under an open pull request of that plan.

**The other side of this boundary is not written yet.** The terminal-door plan does not say that a later plan widens
its consent. It is open, in epoch 3, with one change recorded, so a second ledger entry there is a full stop. Whether
a boundary note is a change of that plan is not this revision's call. It is owed: at that plan's promotion at the
latest, its text names this plan beside *The person agrees to each host once*.

## Build order

1. **Done 2026-11-19:** the owner answered the open points O1–O7. Still owed from this step: the question consultants
   are asked what is important before the epic split is final, and the new questions N1–N5 go to them first and then
   to the owner.
2. **E0 — step 0. Done 2026-11-19** on `ai2-root`: the RESULTS file is written and folded in here.
3. Recommended before the round: W1–W5, with the owner's go for each command (N3); the results are added to the RESULTS
   file and to § 5.
4. The plan round (`review_plan`) and our own Opus review, at the same time. The first passing verdict opens epoch 1.
5. **E1 — the remote accounts reader.** The stories by Opus; the cornerstone spec by Fable before its code; one PR.
6. **E2 — the round and the list.** The stories by Opus; one PR. E1's code has no door until E2 merges.
7. A pre-release VSIX from the merged head; the owner's real-window checks R1–R9 on it, before the tag, as change 1.1
   of the terminal-door plan did (no real VS Code can start on the build machine); a failed check is fixed first.
8. `review_feature` over the three epics, every finding resolved; an accepted fix that changes the product is a new PR
   and a new pre-release VSIX.
9. The extension release: the tag pushed alone and its run observed; `POST_DEPLOY.md` run; the plan promoted with its
   deviations and its stories documents.

## Test plan

- **The script, run for real** (`npm test`, Linux CI): the exported text run by `sh -s` (`dash`) and by `bash --posix`
  against a temporary `HOME` with false `claude`, `codex` and `agy` programs, as `remoteScriptRuns.test.ts` does with
  `tmux` and `ps`; `jq` is the real one. Cases: each vendor found, absent, signed out; each usage state; each operand;
  a wrong operand; `jq` missing; a vendor command that hangs (the time limit ends it); a cut script does nothing;
  `claude` only in `$HOME/.local/bin` is found; the false `claude` records its arguments, its working folder (`/`),
  its stdin (empty) and `TZ`, and the test holds them to § 4 word for word.
- **No secret leaves, seen and not assumed.** The temporary `HOME` holds false credential files (`.credentials.json`,
  `.claude.json`, `auth.json`, agy's sign-in file and log) with a marker token. The false programs record their
  arguments and environment. The test fails if the marker is in any argument, in any environment, in the script's
  stdout or stderr, or in a log line; and if `codex` or `agy` was started at all. The test is seen RED once with a
  line that prints a file.
- **The person's activity does not leave.** The false `claude` prints the measured JSON, with a second marker inside a
  *"What's contributing to your usage?"* section and inside a line that starts with `Current ` but fits no pattern.
  The test fails if that marker is in the reply. It is seen RED once with the filter removed. The same for Codex: a
  session file whose transcript lines hold a marker and the word `rate_limits`.
- **The hash**: a test holds the sha256 of the script text; the spec holds the same value.
- **The parser**: a banner before the begin line; every marker and key; an unknown marker, an unknown key, a line too
  long, too many lines, an exit code that is not 0 — each `unreadable` with its reason; a percent of 101, `-1`, `1e3`,
  `NaN`; a value with control characters, with `$(`, of 10 KB; a credential-shaped value is dropped. A missing number
  is `null`, and a test fails if any path turns it into 0.
- **The Claude usage lines**, as a table: the three measured lines; `3pm`, `3am`, `2:59am`; 12am and 12pm; a reset on
  1 January read on 30 December (the next year); a reset more than eight days ahead, in the past, with another zone,
  with no zone — each a dash with the percent kept; no session line (a dash for `5h`); two model lines; no matched
  line (`failed format`); `:turns 1` (`failed model-call`, and the next call to that host is `no-claude-usage`).
- **The Codex record**: `primary` weekly and `secondary` null (the measured case); both windows; a 300-minute window;
  another length; a reset that has passed; no record; the record's time and the file's time.
- **The words**: the mod's cases carried over as a table — `5h 87% left`, the dash with no `% left`, `resets in —`,
  `1h 0m`, `<1m`, a reset that has come; Codex windows named by length; the grouping (two hosts with one account are one
  group; two unknown accounts are two groups; two hosts with equal numbers and different accounts are two groups);
  each host sentence.
- **The round** (pure, with seams for the clock, the call and the terminals): one call at a time, never two; the start
  moves one host each round; a failed host does not stop the round; usage is asked once for each account in a round and
  moves to the next host when the first fails; no call to a host with no "yes"; no timer with no terminal; a closed
  host is dropped with its reading; the 60 s floor of *Refresh now*; a result that lands after its host closed is dropped.
- **The consent and the switch**: a session "yes" allows the accounts read with no question; a "no" starts no ssh of
  either kind; *Forget* stops the accounts read and the row then asks again; an answer changed during a round stops
  the next call; with the round off, no call follows from a "yes", from a timer or from a reload; the first press of
  the icon turns it on and the stop button turns it off and drops the readings; the timer raises no question (a test
  fails if the ask seam is called from a tick); the question's title and detail name the accounts read.
- **The runner**: the accounts argv is the pinned options, `--`, the host, `sh -s --` and the mode, with no `-n`; a host
  that fails the host class never reaches ssh; the session reader's argv and sentences are unchanged (its tests stay green).
- **The manifest and the help**: the button with `view == terminal` and its group; the command, and the changed
  paragraph on the per-host answer, in the help of all five languages; `notification-sites.json` generated again; the
  log scan (`remoteLogScan.test.ts`'s rule) covers the new modules.
- **Host scenario** (`npm run test:host`): with two terminals of two hosts and the call replaced through the test door,
  the false call is made once for each host, one after the other; with the terminals closed no call follows; the list
  holds the rows of both. U10 is measured here: what a label with `$(` draws.
- **Webview pages are not changed**; if one must be, it is tested by running it (`bundledPage.test.ts`).

## Real-window checks (the owner, on the pre-release VSIX, recorded in this plan when measured)

| # | Check |
|---|---|
| R1 | The icon sits beside *Ask other AI* in the terminal panel's title bar and opens the list. With no `Creds:` terminal the list says so |
| R2 | After the update, before any press of the icon: no accounts read of any host (the VM's ssh log). The first press turns the round on, and the list names each open host and its answer. A host that was never asked says *not asked yet*; choosing it raises the question with its new words. *Not this host*: no ssh to it of either kind |
| R3 | A round runs, one host after the other. The Claude numbers equal the built-in `/usage` on that VM, and the mod line within one point — the same dashes. Every row can be read at the owner's window width |
| R4 | Two hosts that share an account are one group that names both hosts, and its usage was read once in the round |
| R5 | A host that cannot be reached says so in its own sentence, and the other hosts are still read |
| R6 | All `Creds:` terminals closed: no further ssh login on the VM (its ssh log). One opened again: a round follows. *Stop the accounts round*: no further login either |
| R7 | Left open for an hour: rounds about 10 minutes apart. On the VM: no new session file, no leftover process, `~/.claude.json` unchanged by the reads, the plugin store not growing, the live Claude and Codex sessions undisturbed |
| R8 | *Take the question* in a `Creds:` terminal still works (the widened runner). *CoAI: hosts read over ssh* shows the new words; *Forget* there stops the accounts read of that host |
| R9 | The plan's M7: the time of one whole read from Windows with the pinned ssh options, twice, and the size of the reply; a host of an account with no 5-hour window shows `5h —` |

## Definition of Done

- [x] `research/RESULTS_accounts_and_usage_on_a_vm.md` records step 0 on one VM, and every row U1–U11 is answered or
      says what was checked and what was not (written 2026-11-19).
- [ ] W1–W5 are measured and recorded in the same file, or each says why it was not.
- [ ] The icon in the terminal panel's title bar opens a list of the accounts of Claude Code, Codex and Antigravity on
      the hosts of the open `Creds:` terminals, grouped by account, each group naming its hosts.
- [ ] Usage is shown as "left" with the mod's words and its dash; a missing value is never a zero; usage that was not
      read says why.
- [ ] Every host with nothing to show says why in its own sentence: not asked, turned off, never read, or failed.
- [ ] A round runs every 10 minutes, one host at a time, one read-only ssh call for each host with the pinned options,
      only for hosts whose shared answer is "yes" and only while the accounts round is on; it stops with the last
      `Creds:` terminal; a failed host does not stop it; the timer asks nobody.
- [ ] The script is fixed, its hash is in the spec and in a test, and it writes nothing on the VM. No secret is in an
      argument, the environment, the reply or a log, and the person's activity is not in the reply — shown by the two
      marker tests.
- [ ] The Claude usage road is road (a), with the pinned command of § 4. Road (b) is not in the code.
- [ ] Nothing of a reading is persisted. The consent is the session reader's own answer; its question, the hosts list,
      the help and `SECURITY.md` say that it covers the accounts read; a "no" or a *Forget* stops that read.
- [ ] The session reader behaves as before, but for the words of its question and of its hosts list (its tests green, R8).
- [ ] Docs with the change: the help in five languages, `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md` (the new edge from a VM to a vendor), `SECURITY.md`; the `POST_DEPLOY.md` question asked.
- [ ] R1–R9 recorded here; `review_feature` run before the tag and every finding resolved; one extension release cut and
      verified; the plan promoted with its deviations.

## Open points for the owner

**O1–O7 are answered (2026-11-19).** The table keeps each question with the owner's answer.

| # | The question | The planner recommended | The owner answered |
|---|---|---|---|
| O1 | **Claude usage.** Allow the script to make the VM ask `api.anthropic.com` with the VM's Claude login, returning only the percentages? By which road: (a) Claude Code itself makes the call; (b) our script reads the login and calls with `curl`; (c) the account only | **(a), yes** — if step 0 shows a command that prints the usage with no model call, nothing started and nothing written. If (a) fails: **(c)**, not (b). (b) only on an explicit yes after the Security section's paragraph on it | **"May also use curl"**: (b) is allowed as a fallback. Step 0 showed (a) works, so (a) is taken and (b) is not built |
| O2 | May the list show the full email of each account? | **Yes**, in the list only. It is never logged, stored or sent on. It is the point of the list: to tell the accounts apart | **Yes**: full emails, in the list only, never logged or stored |
| O3 | The consent: a separate question for each host for the accounts read, independent of the session reader's answer, raised only by a press; and one question for all open hosts at once is allowed? | **Yes** to both. A timer that reads accounts is not what the first question asked | **No: shared with the session reader.** A host allowed for the session read is read for accounts with no new question (§ 3, finding F1). The second half was not answered; with no accounts question left to ask, this revision does not build a question for several hosts |
| O4 | The interval: a fixed 10 minutes, or a setting? | **Fixed.** A setting costs a help entry in five languages and a control, and nobody asked for another value | **Fixed** |
| O5 | Codex usage is as old as the last Codex turn on that host (a local file, no network). Is that enough, or is the live read wanted? | **Enough.** The live read writes into the Codex home and can refresh a login that live sessions share | **Enough** |
| O6 | The icon: always in the title bar, or only while a `Creds:` terminal is open? | **Always**, like the chat icon | **Always** |
| O7 | Which VM and which Linux user is step 0 run on, and when? | The owner's choice; a host where all three CLIs are signed in | **`ai2-root`, user `root`, 2026-11-19.** Claude Code and Codex are signed in there; agy has a sign-in file |

**New questions, from step 0 and from O3.** Each goes to the question consultants first, and reaches the owner with
their answers beside it. This text already takes the recommended answer of each; another answer before the plan
round is a revision, and after it a change.

| # | The question | Recommended answer |
|---|---|---|
| N1 | **Claude Code writes four cache files of its own on the VM on every usage read** (the Security section names them): at most 6 times an hour for each account. The first draft's condition was "nothing written". Is that accepted? | **Yes**, if W4 shows the plugin store does not grow and R7 shows the live sessions undisturbed. They are the vendor's own caches, and every start by hand writes them too |
| N2 | **Answers given before this release.** With O3, a "yes" that was given to *"Read the Claude session…"* also allows the accounts read. May the accounts round stay off until the first press of the icon, with one button in the list to stop it again? It asks nothing. The other way: the round starts with the update for every host with a "yes", and the person is told by one notice | **Yes: off until the first press, with the stop button.** The owner presses the icon once and never sees a difference. Another person's old "yes" is then not used by a timer they never heard of |
| N3 | May W1–W5 (§ 5) run now on `ai2-root`, each command approved as in step 0, before the plan round? | **Yes.** Else they run at the E1.S2 spec, and a bad result is a change of an approved plan |
| N4 | **Antigravity shows no version**, because the script does not start `agy`: a start was not measured, and agy updated itself on a start on another machine. Is "installed, no usage, account not read" enough? | **Yes.** A version is not worth a start of that program on every host every 10 minutes |
| N5 | **A host without `jq`** shows *`jq` is not installed on this host* for Claude and Codex, and we install nothing. Is that accepted, or should the script cut JSON with `sed` where `jq` is missing? | **Accepted, `jq` only.** Naming a field is safe by construction; a `sed` pattern next to private text is where a leak with no error starts |

## Epics

Three epics in the build order above. E0 has no code and is done. E1 and E2 are one PR each and ship in ONE extension
release. Stories documents are not written now: one for each epic,
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`, written by Opus when that epic starts
(`subagent-models.md`, pipeline step 3). The stories named below are this plan's proposal for that writer. Cornerstone
quota: Q = ⌈2·E/5⌉ = ⌈2·3/5⌉ = 2.

*The split is a draft until the question consultants were asked what is important* (`subagent-models.md`, pipeline
step 2). The areas Fable names as cornerstones: **(a)** what the script touches on the VM and what it prints — the only
place a secret or the person's activity can leak, and a leak gives no error; **(b)** the reply's frame and the parser —
the only place wrong or hostile data can arrive and look right; **(c)** who is contacted and when — the shared answer
read again before every call, the round's switch, and a timer that never asks; **(d)** absent, signed out, no usage
source, skipped, failed and unreadable are different results, and a missing number is never a zero.

Cornerstones: 1 of 2 — E1.S2

### E0 — Step 0, the measurement · done 2026-11-19

- **Goal.** Know, from one real VM, what each CLI stores and prints, what can be read with no secret, which Claude usage
  road holds, and what one read costs in time and bytes.
- **Result.** [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md):
  step 0 on `ai2-root`, four read-only calls, each approved by the owner. Road (a) is taken. U1–U6, U8, U9 and U11
  are resolved; U7 is half open and not needed; U10 waits for E2's host scenario; M7 is real-window check R9; M8 was not run. The owner's answers to O1–O7
  are recorded above.
- **What did not go as this epic was written.** No E0 stories document was written: the commands were approved one by
  one in the Creds dialog. The credential file's key names were listed although road (a) held. Five points are owed
  (W1–W5, § 5). The RESULTS file and this revision are written and not yet committed; they go in one commit, with the
  row in `research/README.md`, before the plan round.
- **Cornerstone areas.** None: it is a measurement.

### E1 — The remote accounts reader · depends on E0

- **Goal.** One call that reads the accounts and usage of one host over ssh and gives a typed result, with nothing
  that calls it yet.
- **Scope.** `src/remoteSsh.ts` (the runner widened: one argv builder and one run for both callers, the sentence's
  subject and the limits as parameters, `SSH_OPTIONS` untouched); `src/remoteFrame.ts` (the framing extracted for two
  callers); NEW pure modules for the accounts script, its frame and typed result, the Claude usage lines and the mode
  operand; the shared consent's new words (`src/remoteSessionHost.ts`: the question's title and detail, the rows of
  *CoAI: hosts read over ssh*); their tests; `research/module_extension.md`, `research/module_tests.md`.
- **Depends on.** E0's RESULTS file; W1–W5 measured before the E1.S2 spec is final; the terminal-door plan's last
  release cut.
- **Done when.** The script, parser, usage-line, Codex-record, runner and consent rows of the test plan are green; both
  marker tests were seen RED once; the hash is in the spec and in a test; the session reader's tests are green, changed
  only where they hold the question's words; the code gate passed; the PR is merged.
- **Cornerstone areas.** (a) and (b) together — the cornerstone; (c) the shared answer; (d) the result's arms.
- **Stories.**
  - **E1.S1 — the runner serves two callers.** `runRemote` keeps its checks and its words; a shared run and argv
    builder under it; the accounts call beside it. By Opus's brief, coded by Sonnet.
  - **E1.S2 — the accounts script, its reply and its parser (CORNERSTONE, `model: fable`).** Fable writes the spec
    before any code: the exact script text and its sha256, the tools it needs, the pinned Claude commands with their
    flags, folder and limits, the three line patterns, the Codex record rule, the reply's grammar and limits, the typed
    result with one arm for each state, the parser's rules with the reset parse, the scenarios (the test plan's script,
    marker, hash, parser, usage-line and Codex rows), the results of W1–W5, and the security judgement on the exact
    text — outside the quota, by the security row. Sonnet codes; Opus reviews.
  - **E1.S3 — the shared consent says what it covers.** The question's new title and detail, the rows of the hosts
    list, and the tests that hold a "no" and a *Forget* to both reads.

### E2 — The round and the list · depends on E1

- **Goal.** The icon, the list grouped by account, and the 10-minute round that fills it.
- **Scope.** NEW pure modules for the round (the hosts, the order, the mode for each host, the table in memory, start
  and stop, the switch) and for the words and rows (the mod's rules, the grouping, the host sentences, the plain-text
  road for VM text); a NEW host module with the `vscode` seams (the timer, the terminal events, the QuickPick with its
  two title buttons, the switch in `globalState`, the test door) bound in `src/extension.ts`; `package.json` (the
  command, its icon, the `view/title` entry); the help in five languages; `notification-sites.json`; the host scenario;
  `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md`, `SECURITY.md`; the
  `POST_DEPLOY.md` question (the file is at its cap of twelve, so a new item needs a named one to leave).
- **Depends on.** E1 merged.
- **Done when.** The words, round, switch, manifest, help and host-scenario rows of the test plan are green; the
  Definition of Done's items hold; the PR is merged; R1–R9 are recorded on the pre-release VSIX of the merged head;
  `review_feature` ran from the merged head and every finding is resolved; the release is cut and verified; the plan
  is promoted.
- **Cornerstone areas.** (c) the timer never asks, contacts only allowed hosts, and does nothing while the round is
  off; (d) the sentences. No cornerstone story is recommended: the rules are pure, small and fixed by this plan. One
  quota place stays free.
- **Stories.**
  - **E2.S1 — the round.** The pure planner, its table in memory and the switch.
  - **E2.S2 — the words and the rows.** The mod's rules, the grouping by account, the host sentences.
  - **E2.S3 — the host and the door.** The timer and the terminal events, the list, *Refresh now*, *Stop the accounts
    round*, the question raised from a host's row, the command and its icon.
  - **E2.S4 — the documents and the release.** The help, the module documents, `SECURITY.md`, the pre-release VSIX,
    R1–R9, `review_feature`, the tag, the promotion.
