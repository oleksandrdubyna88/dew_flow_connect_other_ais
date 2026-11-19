# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **plan only, nothing implemented yet, 2026-11-19.** Step 0 (epic E0, a measurement with no code) ran on one VM
> on 2026-11-19 and the owner answered the open points O1–O7 the same day; both are folded in below. Scope:
> `src_vs_code` only — one command with an icon in the terminal panel's title bar, a list of the Claude Code, Codex and
> Antigravity accounts on the hosts of the open `Creds: <host> / <session>` terminals with their usage, and a background
> round over those hosts every 10 minutes (one read-only ssh call per host, one host at a time). The consent is the
> session reader's own per-host answer, with no second question (the owner's O3). The Claude usage is printed by Claude
> Code itself on the VM (road (a), measured). The round runs by itself from the start of the extension, over each
> distinct host of those terminals whose answer is "yes" (the owner's N2). No change to `src_mcp`, `src_server` or
> CredsForDevs. The accounts script writes nothing on a VM. **One thing is installed there: `jq`, from the host's own
> package manager, when it is missing and the ssh user may install it** (the owner's N5); where the user may not, the
> list shows the command to run. Three epics (E0 is the measurement), one extension release. **Not approved yet:** no
> plan round has run. The owner answered the questions N1–N5 on 2026-11-19 and the answers are folded in, as are the
> measurements W1–W5 (§ 5). **The text is ready for the question consultants and the plan round.** Still owed, and
> named so: the consultants' answer on what is important before the epic split is final; the plan round; two
> questions for the owner that do not block it (Q1, Q2); the process check W7 before the E1.S2 spec is final; and
> the measurements W6 and W8, which are recorded and stop nothing.
> The change ledger (the `Plan changes` section of `.agents/conventions/common/plan-change-control.md`) opens
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

**The owner's answers to the new questions N1–N5 (2026-11-19; given in Russian, translated here).** The questions are
kept in *Open points for the owner*, with the answer beside each.

13. **N1 — "Everything is acceptable."** The four cache files are accepted, and so is a rewrite of `~/.claude.json` by
    a read, even on every read. So W6 is a measurement to record, not a gate. The answer is about what is WRITTEN.
    That the live sessions on a host stay undisturbed is not a write question; it is the product working, and
    real-window check R7 (b) still holds it.
14. **N2 — "The round goes automatically over all unique ssh sessions in the terminal."** The round runs by itself
    over every distinct host of the open `Creds:` terminals, from the start of the extension. The first-press switch
    of the earlier revision is removed. "Unique" is one read for each host, however many terminals name it. The
    consent stays shared (O3): the owner did not say to read a host that nobody allowed. So a host is read when its
    answer is "yes", and not when it is "no" or forgotten. A host with no answer is not asked by the timer; whether it
    should be is question Q1.
15. **N3 — answered by the run** of W1–W5.
16. **N4 — "With the version."** The list shows the Antigravity version: the script starts `agy` with its version flag
    in every round. The risk the planner named, a self-update on a start, is recorded in Security and Growth.
17. **N5 — "Install what is needed yourself, and that is all"; then: "and only if there are no rights, write to the
    person: run this command".** Where `jq` is missing, the extension installs it, when the ssh user may. Where the
    user may not, the list tells the person the exact command to run, and runs nothing. **This reverses the earlier
    line "nothing is installed on a VM".** The design is in § 4 and the judgement is finding F2.

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
| U2 | The folder layout under `~/.codex/sessions`, and how to find the newest file without listing them all | `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`; 60 files; the newest is 527 525 bytes, 2 days old, with 7 such lines. W5: the file has 65 lines, the last record is line 64, with 991 bytes after it; its `timestamp` is ISO 8601 with milliseconds and `Z` | **resolved** |
| U3 | `codex login status` exists and names an account without a secret | It exists and prints only `Logged in using ChatGPT`. It names **no** account | **resolved**: the script does not run it |
| U4 | `~/.claude.json` holds `oauthAccount` with `emailAddress` and an organization name | True, by key name. Not needed, because of U5 | **resolved**: the script opens no Claude file |
| U5 | Whether `claude auth status --json` prints an organization, and whether it prints anything secret | Exit code 0 in 278 ms. It prints `loggedIn`, `email`, `orgId`, `orgName`, `subscriptionType` and nine more keys, and no token or secret. W3: it writes no file | **resolved** |
| U6 | Whether a non-interactive Claude Code command prints the usage, whether it makes a model call, what it starts and what it writes | `claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""` prints it in 3.6 s with **no model call** (`num_turns` 0, cost 0), also from the working folder `/` (W1). No transcript in any read with the three flags. **It rewrites four cache files of Claude Code's own on every read**, the same four, with no growth (W4). **In one read of four, `~/.claude.json` was rewritten too, with a backup; who wrote it is not proven.** `claude --help` has no usage subcommand; `--bare` never reads the login | **resolved, road (a) taken**; two points are not settled: the `~/.claude.json` write (W6) and one process of unknown origin (W7) |
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
  ── Antigravity 1.5.12 · ai1-dev2 ──
  agy states no usage, and its account is not read
  ── Hosts ──
  ai1-root — the last read failed 3 min ago: “ai1-root” did not answer over ssh within 30 s.
  ai2-dev2 — not asked yet. Choose this row to answer for this host.
  ai2-root — turned off: your answer for this host is “Not this host”.
  ai2-dev3 — jq is missing, and this ssh user may not install it. Run there: sudo apt-get install -y jq     [copy]
  ── Changes CoAI made on a host ──
  ai1-dev3 — installed jq on this host, 2026-11-20 (apt-get, as root)
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
    no usage line this version understands*, *agy states no usage*. It is never a row of dashes with no reason.
  - **A host without `jq` has its own sentences** (§ 4): that `jq` was installed; or that it is missing and the ssh
    user may not install it, with the exact command to run there; or that the install failed, with the reason and the
    same command. The command is a fixed string from a closed table. The row has a copy button (a QuickPick item
    button) that puts that string on the clipboard. The extension never runs it for the person.
  - **The part *Changes CoAI made on a host*** lists every installation the extension made: the host, the day, the
    package manager, and whether it ran as root or through `sudo`. It comes from a stored record, so it is still
    there after a reload.
  - **Choosing the row of a host that was not asked** raises the shared question for that host (§ 3). Choosing the
    row of a failed install tries it once more. Choosing any other row does nothing. There is no question for
    several hosts at once.
  - **Two title buttons**: *Refresh now*, and *Stop the accounts round* (§ 3). While the round is stopped the list
    holds one row: *The accounts round is stopped. Choose this row to start it.*
- **Text from a VM is made plain at one road in**: control and format characters are taken out, the length is cut, and
  `$(` is broken so the list never draws an icon a VM chose (U10). The existing pick list of the session reader passes
  VM text with `$(` intact (`src/remoteRead.ts:213`, `:220-226`). This plan does not change it; whether it should use
  the same function is a question for the owner after U10 is measured.

### 2. The round

- **Which hosts — every distinct one, by itself (N2).** The distinct hosts of the live (not exited) terminals whose
  name gives `kind: 'session'`, read through `liveTerminals`. Several terminals of one host are one read. Of these
  only the hosts whose answer in the shared consent is "yes" (§ 3). A host with no answer or a "no" is never
  contacted.
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
- **The first round starts with the extension, with no press** (N2): 30 s after activation, when the window has a
  `Creds:` terminal of an allowed host; else 30 s after the first such terminal opens. The 30 s let the window restore
  its terminals first. It is not started by a reload alone with no allowed host.
- **An install step, once for each host** (N5, § 4). When a host's reply says `jq` is missing and the extension has
  not tried that host yet, the round makes one install call to it right after the read, and reads the host once more
  when `jq` was installed. That is three ssh calls to one host in one round, once in that host's life.
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
  today; one small row of state (§ 3); and the record of what was installed on which host (§ 4).
- **Limits of one read call**: killed at 30 s (the tree, as today), stdout capped at 64 KiB. Both are ceilings. The
  ceiling was 20 s before `agy` was started (N4). Measured on the VM: the account command 0.3 s, the usage command
  3.6 s with the flags (6.3 s without), whole calls between 3 and 10 s. Inside the script each command has its own
  limit: `claude --version` 2 s, the account command 3 s, the usage command 8 s, `agy --version` 4 s (§ 4); with ssh's
  5 s to connect that stays under 30 s. The time of `agy --version` and the time of a whole call from Windows with the
  pinned options were not measured; R9 measures both. A round over five hosts is at most 150 s. The install call has
  limits of its own (§ 4).
- **A failed read is logged once, not every round.** The runner's one log line is written when a host's failure
  sentence changes, and once when it reads again. A host that is down for a day is two lines, not 144.
- **Not serialized with the session reader.** A press of *take the question* can run while the round reads the same
  host: two ssh children to one host, both read-only. Each VS Code window runs its own round, so two windows with
  terminals of one host read it twice in 10 minutes. Both are named, not prevented.

### 3. The consent — shared with the session reader (O3), and used by the round with no press (N2)

**Decisions (the owner, 2026-11-19).** O3: there is no separate consent; the per-host answer of the session reader is
the answer for the accounts read too. N2: the round runs by itself over every distinct host of the open `Creds:`
terminals. N5: where `jq` is missing, it is installed.

**Which host is contacted, by its answer in `coai.terminalHosts`:**

| The host's answer | What the round does |
|---|---|
| "yes" (*Read*) | Reads it, from the first round after the extension starts. No press and no question. Installs `jq` there once when it is missing and the ssh user may |
| "no" (*Not this host*) | Nothing. No ssh call of any kind |
| none (never asked, or *Forget*) | Nothing. **The timer never raises a question.** The list shows the host as *not asked yet*, and a press on that row raises the shared question |

The owner said "all unique ssh sessions". This plan reads that inside O3: a host nobody allowed is not read. Whether
the round should ask once, by a modal, when it first meets a host with no answer is question Q1. The default here is
no: a question appears only after a press.

**What a "yes" covers from this release — said plainly.** Until this release the question was *"Read the Claude
session on “x” over ssh?"*. A "yes" covered one read that a press starts and that prints a transcript. From this
release the same "yes" also covers, **with no further press**:

- a read that a timer starts, from the start of the extension, every 10 minutes while a `Creds:` terminal of that
  host is open;
- the names of the accounts signed in there;
- a start of Claude Code on that host, which asks Anthropic with that host's own login;
- a start of `agy` on that host, for its version;
- **one installation of `jq` on that host**, when it is missing and the ssh user is root or has `sudo` without a
  password.

For the owner these are the decisions O3, N2, N4 and N5. For another person who installed the extension and answered
*Read* before this release, each of them is wider than what they were asked. The Security section holds this as
findings F1 and F2.

**What the person is told, and where.**

- **The question's words change, for every host asked from this release on.** Same code (`read-remote-session`), same
  buttons (*Read*, *Not this host*), same store. The title becomes *"Read the Claude session and the AI accounts on
  “x” over ssh?"*. The modal's detail says what a "yes" covers: the session of a terminal, when you press *take* or
  *add*; and, by itself, every 10 minutes while a `Creds:` terminal of that host is open, which Claude Code, Codex
  and Antigravity accounts are signed in there and how much of their limits is left — for which Claude Code is
  started on that host and asks Anthropic with that host's own login; and that `jq` is installed there once from the
  host's own package manager when it is missing and your ssh user may install software. By fixed scripts, as your
  ssh user. The words "read-only" are no longer true without that last clause, and the detail does not use them
  without it.
- **One notice, once for each installation of the extension**, when its first accounts round is about to contact a
  host. It names the hosts, says that their AI accounts are now read every 10 minutes and that `jq` is installed
  where it is missing, and says where to stop it. It is information: it asks nothing and the round does not wait for
  it. It does not turn an old "yes" into a new one.
- **A notice for every installation**, when it was made and when it failed, through the one door, so it is kept in
  the notifications ledger like every notice. The list keeps it too (*Changes CoAI made on a host*).
- ***CoAI: hosts read over ssh*** names it in each row: *the Claude session and the AI accounts are read*, or *not
  read*. Its three choices stay: *Read*, *Not this host*, *Forget*.
- **The accounts list** shows, for each open host, its answer and where to change it (the *Hosts* part of § 1).
- **The help, in five languages**: the new command; the paragraph on the per-host question, which today says the
  first read of a host asks once; and the sentence *"They only read: they change nothing on that machine"*, which
  stops being true and is rewritten with the one exception.
- **`SECURITY.md`** and the release notes say the same (the Security section).

**The first-press switch is gone (N2). The stop button is kept, and this is why.** The round is on by default and
nobody has to press anything for it: that is the owner's answer. *Stop the accounts round* is one title button of the
list. It clears the timer, drops the readings from memory, and no host is contacted for accounts or for an install
until the person starts the round again from the list. The reasons to keep it:

1. It is the only way to keep the session read of a host and stop the accounts read. The per-host answer cannot be
   split, and *Not this host* stops both.
2. It is the way out when a read does harm on a VM — a Claude Code or agy update that disturbs the live sessions
   (R7 (b)) — without uninstalling the extension or closing the terminals.
3. It costs little: one button, one stored row, three tests. It is a title button and not a command or a setting,
   because each of those needs its own help entry in five languages, and a setting a control.

What it does **not** do: it does not protect an old "yes" at the first start after the update. The first round, and
an install with it, start 30 s after the extension starts, before anyone can press a button (F2).

**The state** is one `globalState` row, key `coai.terminalAccountsState`: `{ toldUtc, stoppedUtc }`. `toldUtc` is
when the one notice was shown; `stoppedUtc` is there only while the round is stopped.

**How a "no" or a *Forget* stops the accounts read.**

- ***Not this host*** — in the question, or later in *CoAI: hosts read over ssh*: no ssh call of any kind goes to that
  host: no session read, no accounts read, no install. Its reading is dropped from memory at once.
- ***Forget***: the host has no answer. It is not contacted. Its row in the accounts list says *not asked yet*, and a
  press on that row raises the shared question again.
- A change made while a round runs takes effect before the next call (§ 2). A read that is already running ends
  within its 30 s, and its result is dropped. An install that is already running is not killed: stopping a package
  manager half-way is worse than letting it end (§ 4).

**What a person can no longer choose.** They cannot allow the session read on a host and refuse the accounts read or
the install on the same host. The per-host answer is one answer. The only finer control is the stop button, for all
hosts at once.

**No second store, no reach number.** The first draft had a second `globalState` key and a "reach" for each row. Both
are gone with O3. The store stays `coai.terminalHosts`, with its row `{ host, answer, askedUtc }`, its cap of 100 rows
and its pure functions (`consentOf`, `withAnswer`, `withoutHost`, `answersOf`).

### 4. The remote script and its reply — the cornerstone

- **Fixed text, sent whole, pinned.** A new constant, the accounts script, in the shape of `REMOTE_SCRIPT`: POSIX `sh`
  (the VM's `sh` is `dash`, measured), one function read to its end and only then called with
  `</dev/null 2>/dev/null`, a begin line first and an end line last, so a script cut on the way does nothing. Its
  sha256 is in its spec, and this time a test holds the hash too: this script runs a vendor program, so an edit of the
  text without the spec must turn a test red.
- **No write by the accounts script, no `cd` to a folder a person owns, no background job, no temporary file, no
  here-document.** Every tool it needs is checked first; a missing tool is said, never read as "nothing found". The
  one thing the extension changes on a VM, the install of `jq`, is a second script with its own text and hash
  (below), so this one stays read-only and its tests stay about reading.
- **The `PATH`.** `ssh <host> sh -s` is not a login shell, and on the measured VM its `PATH` has no `~/.local/bin`,
  where `claude` and `agy` are. The script's first line inside the function puts `$HOME/.local/bin` in front of the
  `PATH`, as that VM's `~/.profile` does for a login shell. Nothing else of a profile is read.
- **The tools.** `jq`, `timeout`, `tail`, `ls`, `sed`, `grep` and `command`. `jq` was found on the measured VM; it is
  not known for the others. **`jq` is required, and the reason is safety by construction**: a field is printed because
  it was named, so nothing beside it in a JSON document can leave the VM. Cutting JSON with `sed` would be a pattern
  next to private text (the person's activity in `/usage`, a transcript line of Codex), and one wrong pattern there is
  a leak with no error. A host without `jq` gets the state `unreadable no-jq` for Claude and Codex. The accounts script
  does not install it; the extension then makes the install call, once (N5, below).
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
  :as-of 2026-11-17T19:32:36.474Z record   # the record's own time, or the file's time and the word "file"
  COAI-VENDOR agy found               # found | absent
  :version 1.5.12
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
- **Claude Code — the version.** `claude --version` (measured: `2.1.326 (Claude Code)`), under `timeout` (2 s), so a
  usage that cannot be read names the version beside it. Of its output only the version is kept, by the rule written
  under Antigravity below.
- **Neither of these two commands writes a file** (W3, measured: after both, no file under `~/.claude` and not
  `~/.claude.json` was newer than a marker made before them). So they can run on every allowed host in every round.
- **Claude Code — the usage: road (a), taken.** Only on the mode `claude-usage`. The command, with the flags measured
  in step 0 and no others:

  ```
  claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""
  ```

  - **stdin is `/dev/null`**, and the command runs under `timeout` (8 s; measured 3.6 s). A command that is killed is
    `failed timeout`.
  - **The working folder is `/`.** The reasons: the script may not write, and a temporary folder is a write that a
    kill at the time limit leaves behind, on every host, every 10 minutes; `$HOME` is wrong, because Claude Code would
    take `~/.claude` for a project's folder; `/` exists on every VM and holds no project. **W1 measured `/`, and it
    stays.** From `/` the command gave the numbers with no model call, rewrote the same four cache files as from a
    temporary folder, and left nothing in `/`. In that one read `~/.claude.json` was rewritten too; the two reads
    right after it, from the same `/`, did not rewrite it, and neither did step 0's read from a temporary folder. So
    the folder does not cause that write on every read, and who made it is not proven (the Security section, W6). One
    reading, the planner's and not proven either: Claude Code records a folder the first time it starts there. If so,
    a FIXED folder costs one such write for each host, and a temporary folder would cost one on every read. No
    reading of the point argues for a temporary folder.
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
  passed is shown as *no reading since this window was reset*, not as an old percent. No record is `failed none`.
  - **The tail is the last 1 MiB (1 048 576 bytes) of each file.** W5 measured the newest file: 65 lines, the last
    record on line 64, with 991 bytes after it. 1 MiB is about a thousand times that distance and twice that whole
    file (527 525 bytes). The margin is for a turn that is still running: its lines, which can be large, come after
    the last record. A record that is further from the end than 1 MiB is not found, and the list then says that no
    usage record is near the end of the newest Codex sessions.
  - **The time rule.** The record's `timestamp` is ISO 8601 with milliseconds and `Z` (measured:
    `2026-11-17T19:32:36.474Z`). *From a Codex session <age> ago* is this machine's clock minus that time. Among the
    files, the record with the newest `timestamp` wins. A `timestamp` that does not parse falls back to the file's
    own time, and the reply says which of the two it sent.
  - The live read, `codex app-server`, is **not taken** (O5).
- **Codex — the account.** `~/.codex/auth.json` holds tokens and is never opened; the script only tests that it
  exists (`:sign-in-file`). `codex login status` names no account (U3), and whether it writes or refreshes a login was
  not measured, so the script does not run it. The list shows *account name not shown by Codex* and the plan type.
- **Antigravity — the version (N4).** agy 1.5.12 states no usage and has no account command (U8). The script tests
  that `agy` is found and that its sign-in file exists, and starts it once in every round, for its version:
  `agy --version`, with stdin from `/dev/null`, the working folder `/`, its stderr dropped, under `timeout` (4 s). Step
  0 ran that command in a shell that is not a login shell, by its full path, and it printed 1.5.12. No other agy
  command is run: not `models`, not the print mode, nothing that needs the login or asks for the network.
  - **The version is the only thing kept.** On the VM: of the first line of the output, the first run of digits and
    dots, at most 32 bytes. On this side: the parser takes it only when it is two to four numbers with dots between
    them; anything else is "version not known", and agy is still shown as found. A banner, an update notice or any
    other line agy prints does not leave the VM. A test holds this with a false `agy` that prints a marker beside its
    version.
  - **What a start of agy does beside printing is not measured (W8)**: whether it writes a file, contacts the
    network or looks for an update. `--version` asks for none of that, but what agy does on a start is agy's choice.
    On another machine agy updated itself (`research/RESULTS_agy_read_file.md`); what set that off is not recorded
    there. The owner chose the version knowing this risk (N4). It is in Security and in Growth.
  - **The account is not taken from `cli.log`**: an email address appears there, but a log's form is no contract and
    a log holds other text. The list says *agy states no usage, and its account is not read*.

**Installing `jq` — a second script, the only one that changes a VM (the owner's N5).**

- **When.** A host's read says `unreadable no-jq`; the host's answer is "yes"; the round is not stopped; and the
  install record has no row for that host. Then the round makes ONE install call to that host, right after its read,
  and reads the host once more when the reply says `installed`.
- **Once for each host, and the record is written first.** `globalState` key `coai.terminalAccountsInstalls`, rows
  `{ host, state, manager, as, utc }`, at most 100. The row is written as `started` BEFORE the ssh call is spawned, and
  rewritten with the outcome. So a reload or a crash in the middle does not start a second install: a row that is
  still `started` is shown as *an install was started on <day>; how it ended is not known*. Whatever the outcome, the
  timer never calls the install for that host again. Only a press on the host's row tries once more.
- **Fixed text, its own constant, its own hash.** POSIX `sh`, in the shape of the accounts script, with one fixed
  operand, `install-jq`, the same pinned ssh options, and a sha256 that is in its spec and in a test. Its limits are
  its own: the extension waits 180 s and reads at most 4 KiB. A stop of the round or a changed answer does not kill
  an install that is running.
- **What it does, in this order.**
  1. `jq` is found: the reply is `present`, and nothing is done.
  2. **The package manager** is the first that exists of four fixed absolute paths: `/usr/bin/apt-get`, `/usr/bin/dnf`,
     `/usr/bin/yum`, `/sbin/apk`. It is never looked up on the `PATH`, and this script does not put `~/.local/bin` on
     its `PATH`: a program that will run as root is not taken from a folder a user can write. None of the four: the
     reply is `no-manager`.
  3. **The rights.** `id -u` is 0: the command runs as it is. Else `sudo -n true` succeeds: the command runs through
     `sudo -n`. Else the reply is `no-rights <manager>` and **nothing is run**. `-n` and a stdin of `/dev/null` mean a
     password is never asked for.
  4. **One command**, from the table below, with its output dropped on the VM, non-interactive, under `timeout`
     (150 s). For `apt-get` it waits up to 60 s for the package lock instead of failing at once.
  5. If that fails with `apt-get` or `apk`, the package lists are refreshed once (`apt-get update`, `apk update`) and
     the same command is tried once more. `dnf` and `yum` refresh their lists by themselves. This is the planner's
     reading of "install what is needed": without it a VM with old or empty lists fails every time.
  6. `jq` is looked for again. The reply is `installed <manager> <root|sudo>` **only when `jq` is found now**. Else it
     is `failed <word>` — `locked`, `timeout` or `exit` — with the manager's exit code as a number.
- **What it never does.** No installer piped from `curl`, no downloaded binary, no build from source. No new
  repository, key or source, and no option that turns off the manager's signature check. No package but `jq` (and
  what the manager itself adds as its dependencies). No upgrade of anything else, no removal. No retry by a timer.
- **The table is closed.** The left column is the only word of the reply that chooses anything. The parser takes it
  only from this list, and the strings in the other columns are constants of the extension. **No text from a VM is
  ever part of a command that is shown or run.**

  | Manager | What the script runs (as root, or through `sudo -n`) | What the list shows when the user has no rights |
  |---|---|---|
  | `apt-get` | `apt-get install -y --no-install-recommends jq` | `sudo apt-get install -y jq` |
  | `dnf` | `dnf install -y jq` | `sudo dnf install -y jq` |
  | `yum` | `yum install -y jq` | `sudo yum install -y jq` |
  | `apk` | `apk add jq` | `apk add jq`, to be run as root |
  | none of the four | nothing | no command: *Install `jq` on this host. CoAI found none of apt-get, dnf, yum and apk there.* |

- **No rights: the person gets the exact command, and the extension runs nothing** (the owner's second sentence on
  N5). The host's row says that `jq` is missing, that this ssh user is not root and has no `sudo` without a password,
  and the command of the table for the manager that was found. A copy button on the row puts that fixed string on the
  clipboard. The extension never runs it for the person and never asks for a password. When the install failed
  although the user had the rights, the row gives the reason and the same command.
- **What the list and the log say.** *Changes CoAI made on a host* gets the row *installed jq on this host*, with the
  day, the manager and "as root" or "through sudo". One notice says it at once (§ 3). The extension's log gets one
  line: the host, the manager, the outcome and the exit code. The package manager's own output never leaves the VM: it
  can hold the addresses of private repositories.
- **None of this is measured.** The measured VM has `jq`. Every sentence above about what a package manager does is
  what we ask of it. It is observed in the container tests of the test plan and in real-window check R10, before the
  release.

### 5. Step 0 — the measurement: done 2026-11-19; W1–W5 measured the same day; one check and two records owed

Step 0 ran on `ai2-root` on 2026-11-19, with the owner approving each exact command in the Creds dialog. Nothing was
installed. No value of a secret file was printed: key names, sizes, modes and times only.

| # | What was to be measured | Result |
|---|---|---|
| M1 | The shell and the tools under `ssh <host> sh -s` (U9, U11) | **Measured**: `dash`, a builtin `printf`, `claude` and `agy` only in `~/.local/bin`, the versions |
| M2 | `claude auth status --json`: the key names, the time, the exit code, an organization or not, and whether a file changed (U5) | **Measured**, but for the last part, which step 0 did not record. W3 measured it later: no file changes |
| M3 | Road (a): `claude --help`, what `-p` does with a slash command, and the six conditions (U6) | **Measured**: two runs. See the table of conditions below |
| M4 | The key names of `~/.claude.json` and its account object (U4); the credential file only if road (a) failed (U7) | **Measured**, key names only. The credential file's key names were listed although road (a) held; the RESULTS file records that as a deviation |
| M5 | Codex: the layout, the newest file, the rate-limit records, `codex login status` (U1–U3) | **Measured** |
| M6 | agy: its version, its help, the file names, the keys that could name the account (U8) | **Measured** |
| M7 | The draft script from Windows with the pinned ssh options: the time of one read, twice, and the size of the reply | **Not run.** It is real-window check R9 |
| M8 | `ps` from a second Linux user shows no secret in any argument | **Not run.** On road (a) the script holds no secret, so its arguments are the fixed words of § 4. That is a reason, not an observation; the marker-token test shows it for the exact text |

**The six conditions of road (a), as measured** (the command of § 4: one read in step 0, and the reads of W1, W2 and W4):

| Condition | Result |
|---|---|
| It prints the numbers | Met |
| It makes no model call | Met: `num_turns` 0, cost 0, API time 0 |
| It starts no hook, no MCP server and no plugin of the person | **Not settled.** Step 0 reported it as met, from the flags, the shorter time and the shorter list of changed files. The VM has a `PreToolUse` hook, the plugin `feature-dev` and three MCP servers at user level. W2 took one sample, 1.5 s after the start: the `claude` process had **no child**. That is one moment; a child that lived for less is not seen (W7) |
| It writes no session file and leaves no process | No transcript in any of the four reads with the flags. **Not met as first written ("nothing written"): four cache files of Claude Code's own are rewritten on every read** — the same four, with no growth (W4). **`~/.claude.json` was rewritten, with a backup, during one read of four; who wrote it is not proven** (W6). After the W2 read ended, one `node` process, 1 s old, was seen: its origin is not known and it is **not attributed** to the read or to anything else (W7) |
| It ends within the time limit with no terminal and an empty stdin | Met in the `creds_exec` calls: 3.6 s, stdin `/dev/null`; the fifth call, with at least three usage reads in it, took 12.1 s. From Windows over the pinned ssh call it is R9 |
| A strict pattern can read the output | Met: JSON, with one fixed line for each window. The reset has no year (§ 4) |

**W1–W5, measured 2026-11-19 at about 12:40 UTC** on `ai2-root`, in one `creds_exec` call whose exact text the owner
approved (12 094 ms). The record is § 11 of the RESULTS file. The middle column is what this plan wrote, before the
run, that a bad result would mean.

| # | What was measured | If the result is bad | Result |
|---|---|---|---|
| W1 | The usage command from the working folder `/`: the same numbers, the same four changed files, nothing written in `/` | The folder goes to the owner as a question. No silent fallback to a temporary folder | **`/` passes, with one point not settled.** Exit code 0, no model call, three usage lines (80 %, 81 % and 52 % used: the numbers had moved since step 0, so the read is live). The same four cache files; nothing new in `/`. **`~/.claude.json` was rewritten too, with a new backup file.** The two reads right after it, from the same `/`, did not rewrite it. Not proven who wrote it → W6 |
| W2 | A list of processes while the usage command runs and after it: no MCP server, no hook process, nothing left | Road (a) fails that condition: Claude usage is off for every host (road (c)) until the owner decides | **Not settled — neither bad nor proven good.** One sample at 1.5 s: no child of the `claude` process, at any depth. After the read: one `node` process, 1 s old, among the processes younger than 15 s; its parent was not checked. It started after the sample, so the sample does not rule the read out, and nothing ties it to the read either: the host has live Claude Code sessions. **Not attributed** → W7 |
| W3 | Whether `claude auth status --json` and `claude --version` change any file | The files are named in the Security section and put to the owner with N1. If a file of the person changes (`~/.claude.json`, a transcript), that command is not run | **Passes.** After both commands, nothing under `~/.claude` and not `~/.claude.json` was newer than the marker |
| W4 | Two usage reads in a row: is `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json` ONE file written again, or a new file each time | A new file each time is growth of 144 files a day for each account's host. Road (a) then fails, as for W2 | **Passes.** Both reads rewrote the same file (the same name, with one fixed 12-character part). The store held 2 such files before and 2 after. These two reads wrote only the four cache files |
| W5 | Codex: how far from the end of a session file its last `token_count` line is, and the form of its `timestamp` | Sets the tail's bound and the time rule in the spec. No bad result: with no usable `timestamp` the file's own time is used and named | **Measured.** The newest file has 65 lines; the last record is line 64, with 991 bytes after it; `timestamp` is `2026-11-17T19:32:36.474Z`. The tail is 1 MiB and the time rule is in § 4 |

**The `~/.claude.json` point, written as it is.** Four reads with the three flags have a list of changed files: step
0's (a temporary folder), W1's (`/`), and W4's two (`/`). One of the four, W1's, shows `~/.claude.json` rewritten with
a new `~/.claude/backups/.claude.json.backup.<n>`. The list of changed files says THAT a file changed during the read,
not WHO changed it. Three readings, none proven:

1. **A live session wrote it.** The owner has Claude Code sessions running in tmux on this host, and they rewrite
   `~/.claude.json` on their own.
2. **Claude Code rewrites it on some starts**, for a reason of its own.
3. **Claude Code recorded the folder `/`** the first time it started there (the planner's reading). It fits the order:
   W1 was the first read from `/`, and the two after it wrote nothing. It is not shown: whether step 0's read used a
   new temporary folder is not recorded.

This plan takes none of them as true. The owner has since accepted the write whoever makes it, even on every read
(N1). So the point decides nothing any more. W6 still measures it, for the record and for `SECURITY.md`.

**Three measurements are owed.** Each needs the owner's go for its exact text. They run after the plan round. **Only
W7 can stop anything**; this text says now what its bad result means, so that it is not a surprise then.

| # | What is measured | What the result does |
|---|---|---|
| W6 | As a Linux user with **no live Claude Code session**: the time of `~/.claude.json` and the number of files in `~/.claude/backups/`, before and after each of several usage reads from `/`; and whether `~/.claude.json` has an entry for the folder `/` (a key name, no value) | **A record, not a gate** (N1: the owner accepted the write, also on every read). The result goes into the RESULTS file, and `SECURITY.md` then says which reading holds. One thing N1 was not asked about is reported to the owner with its number if it is seen: backups that rise with every read (144 a day for each account's host) |
| W7 | **A stronger process check than one sample.** The usage command is run under `timeout`, which by its documentation puts the command in a process group of its own; the processes of that group are listed several times a second while it runs, and once more after it ended. Where `strace` is already on the VM, one read is also run with every program start traced. Nothing is installed for this | **A gate, before the E1.S2 spec is final.** A process in the read's own group that is not Claude Code itself — an MCP server, a hook — or a process of the group that outlives the read: road (a) fails that condition, and the Claude usage read is off until the owner decides. N1 was about what is written, not about what is started, so it does not cover this |
| W8 | `agy --version` from `/` with stdin `/dev/null`: its time; the files under `~/.gemini` and the agy program itself that are newer than a marker; the processes it leaves | **A record, not a gate** (N4: the owner chose the version knowing the risk). If a start with the version flag downloads or replaces the program, the owner is told with the size |

**Does the spec need the stronger check? Yes.** One sample at one moment cannot show that nothing was started, and a
list filtered by process name cannot say whose a process is. The process group says whose. The shipped script relies on
the same thing: `timeout` is documented to signal the whole group, so a child that stays in the group should die with
a killed read. That is what we ask of `timeout`, not what we have seen: the script test with a false `claude` that
starts a child and hangs observes it.

Within the plan as written: the field names, the limits under the ceilings, the bounds of W5, the exact options of
the four install commands. Outside it, and so a change once the plan is approved: anything installed on a VM **but
`jq` by the install script of § 4**; any other package manager or way to install; a third kind of ssh call; a file
with a secret that this plan does not name; a write by the accounts script; road (b).

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs.
- **Nothing is installed on a VM but `jq`**, and that only by the host's own package manager, only where the ssh user
  is root or has `sudo` without a password, and once for each host (N5). No installer from the network, no downloaded
  binary, no new repository, no other package, no upgrade, no removal, never a password prompt. The command that is
  shown to a person without the rights is never run for them.
- Nothing is written on a VM by the accounts script.
- No real time, no push from the VM, no setting for the interval.
- No colour for the numbers (the mod colours them), and no webview.
- No history, no chart, no ledger of the readings, no alert when a limit runs low.
- No reading of a host that has no open `Creds:` terminal, and none of this machine's own accounts.
- No reading of a host whose answer is not "yes", and no question raised by the timer (Q1 asks whether it should).
- No switch that a first press must turn on: the round runs by itself (N2).
- No live Codex read (`codex app-server`), no `codex login status`, and no opening of `~/.codex/auth.json`, ever.
- No start of `agy` but with its version flag, no reading of agy's log, and no opening of its sign-in file.
- No opening of `~/.claude.json` or of the Claude credential file by our script.
- **No road (b).** The owner allowed it (O1); this plan does not build it.
- No separate consent for accounts, and so no way to allow the session read and refuse the accounts read or the
  install on one host.
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

**The new edges.** (1) A read-only ssh call that a TIMER starts, with no press, from the start of the extension, to
hosts whose shared answer is "yes". (2) On each usage read, a start of Claude Code on the VM, which calls Anthropic
with the VM's own Claude login. (3) In every round, a start of `agy` on the VM, for its version. (4) **Once for each
host that has no `jq`: an ssh call that CHANGES the VM** — a package manager run as root or through `sudo`, which
fetches a package from the host's repositories and installs it. (5) Text from a VM drawn in a list.

**Road (a) is taken: no secret passes through our script.** The script opens no credential file, holds no token in a
variable and gives none to any program. Claude Code reads its own login inside its own process, as it does when the
person starts it by hand.

**Every secret and private thing the script comes near, and where it goes:**

| What | Where it is | What our script does with it |
|---|---|---|
| The Claude OAuth access token and refresh token | `~/.claude/.credentials.json` on the VM (mode 600) | Never opened. Claude Code reads it in its own process. With a valid login the file was not among the files changed by a usage read (step 0, run 2). With an expired access token Claude Code may refresh it and write the file, as on any start; that case was not measured |
| `~/.claude.json` (holds every project's entries and the MCP servers of the user) | the VM | Never opened by our script. The plain command wrote it, with a backup — that is why the flags are part of the pinned command. With the flags it was **not** written in three reads and **was rewritten, with a new backup, during one** (W1). Who wrote it then is not proven: that read, or a live Claude Code session on the host. This plan does not claim the file is untouched. The owner accepted the write whoever makes it (N1); W6 records which |
| The person's activity: requests, top subagents, top MCP servers | the `result` text of `/usage`, after the three usage lines | Passes through a pipe on the VM, inside the person's own ssh login, and is **dropped on the VM**. Only lines that fit the three anchored patterns leave it. The marker-token test puts a marker in that section and fails if it is in the reply |
| `~/.codex/auth.json` | the VM | Never opened. Only tested for existence |
| Codex session files (a transcript: private, not a credential) | the VM | A bounded tail of at most three files is read. `jq` prints named numbers of the last `token_count` record and nothing else; a transcript line prints nothing |
| agy's sign-in file and its log (the log holds an email address) | `~/.gemini/antigravity-cli/` on the VM | Never opened by our script. The sign-in file is only tested for existence. `agy` itself is started with its version flag (N4) and reads what it reads on a start; of its output only a version of digits and dots leaves the VM |
| The package manager's output (it can hold the addresses of private repositories, and a token inside one) | the install call, on the VM | Dropped on the VM. The reply holds closed words and one exit code |
| Root on the VM | the install call | Used for one fixed command from a closed table, by a fixed absolute path, only when the ssh user already is root or has `sudo` without a password. Our script holds no password and asks for none |
| The person's ssh keys and agent | this machine | Used by `ssh` as today; `ForwardAgent=no` |
| The account email and organization name (personal data, not a secret) | the reply | In memory and in the list (O2). Never logged, never stored, never sent on |

**What Claude Code writes on the VM on each usage read — measured, and not ours to prevent.** Our script writes
nothing. The usage command, with the three flags, rewrote exactly these four files in each of the four reads whose
changed files were listed (step 0, W1, and W4's two):

- `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json`
- `~/.claude/policy-limits.json.stamp.json`
- `~/.claude/policy-limits.json.signature.json`
- `~/.claude/remote-settings.json.signature.json`

They are Claude Code's own caches, in its own folder, owned by the same Linux user. The names say Claude Code fetched
its policy limits and its remote settings on that start; those calls were not observed. **They do not grow** (W4):
the first file keeps its name from read to read, and the store held the same number of files before and after two
reads. **How often:** once for each Claude account in a round, on the one host of that account that reads the usage —
at most 6 times an hour for each account and window. The account command and the version command run on every
allowed host in every round, and **they write no file** (W3).

**`~/.claude.json` was rewritten during one of those four reads, and it is not proven by whom.** In W1's read the
file changed and a new backup appeared in `~/.claude/backups/`. In the other three reads it did not change. The host
has live Claude Code sessions, which rewrite that file on their own; a list of changed files cannot tell their write
from the read's. § 5 holds the three readings. If the read wrote it, then our timer makes Claude Code rewrite a file
that holds the person's projects and MCP servers, while live sessions use the same file. Claude Code does that on
every start by hand too, and keeps a backup; whether two starts at once can lose a change of one of them was not
measured.

**The first draft's condition was "nothing written". It is not met, and the owner accepted that (N1, "everything is
acceptable"):** the four cache files, and a rewrite of `~/.claude.json` by a read, even on every read. The planner had
recommended to accept the second only if it is rare; the owner's answer is wider, and it stands. Two things follow.
W6 is a record: it says which reading holds, and nothing stops on it. **And the answer is about writes.** A live
session that is DISTURBED by a read — it asks to sign in again, loses its settings or its MCP servers, or stops — is
not a write the owner accepted; it is the product breaking what the person works in. R7 (b) holds that, and a
disturbed session stops the usage read until the owner decides.

**A start of `agy` in every round (N4).** `agy --version` is asked for one line. What else agy does when it starts is
agy's choice and was not measured: a file written, a call to the network, a look for an update (W8). The known risk is
a self-update: agy has updated itself on another machine (`research/RESULTS_agy_read_file.md`). If a start with the version flag can set an
update off, then our timer, not the person, decides when the 269 MB program on a VM is replaced — under live agy
sessions too. The owner chose the version knowing this. What this plan does about it: the start has a 4 s limit, and
`timeout` is asked to end its whole group, so a download should be cut short and not left running (what a cut
download leaves behind is not known);
R7 notes the time and size of the agy program over an hour; W8 records what one start touches.

**Finding F1, for the owner: the shared consent widens answers that were given to another question — and with N2 an
old "yes" is now used by itself, with no new press.**

- **No rule of `.agents/` forbids it.** `common/security.md` has no rule on consent. The conflict is with two texts of
  this repository, and this plan changes both openly:
  1. `SECURITY.md`, *The extension's one outward ssh call*: *"Read-only, to a host the person agreed to. The first read
     for a host asks once (a modal naming the host)"*. The modal named the Claude session, and nothing else.
  2. The terminal-door plan, *The person agrees to each host once*, with the question *"Read the Claude session on
     “x” over ssh?"*.
- **Every "yes" stored before this release answered that question.** After the update the same row allows, from the
  first start of the extension and with no press: a read by a timer every 10 minutes, the names of the signed-in
  accounts, a start of Claude Code on the VM that asks Anthropic with that host's login, and a start of `agy`. The
  earlier revision kept the round off until the first press of the icon, so that an old "yes" was used only after the
  person had used the list. The owner removed that (N2), and it is removed.
- **For the owner's own installation, O3 and N2 are that consent**, given on 2026-11-19 knowing what they cover. The
  extension is public. For any other person who answered *Read* before, nobody has said yes to the wider act, and
  after the update it happens without their doing anything.
- **What is left to tell them, since nothing asks them:** one notice at the first round, which names the hosts and
  where to stop; the list, which names each host and its answer; the stop button; and the question's words, the hosts
  list, the help in five languages, `SECURITY.md` and the release notes (§ 3). A notice can be missed, and none of
  these comes before the first read.
- **The planner's first judgement was a separate consent with a reach; the second was a round that waits for one
  press. The owner decided otherwise both times, and this plan follows the owner.**

**Finding F2, for the owner: the install makes a feature that reads change the machine, as root.**

- **No rule of `.agents/` forbids installing software on a remote host.** `common/security.md` has no rule on it. Two
  of its rules bear on HOW it is done:
  1. *External processes run with a time limit that kills the process tree.* A package manager that is killed
     half-way can leave the package database in need of a repair (`dpkg --configure -a`). So this one process has a
     long limit (150 s on the VM, 180 s here), it is not killed by a stop of the round, and a kill is the rare case —
     named, and reported as `failed timeout` with that sentence.
  2. *A measure you have not observed working is a comment.* Nothing of the install was measured: the measured VM has
     `jq`. Every sentence of § 4 about what `apt-get`, `dnf`, `yum`, `apk` or `sudo -n` do is what we ask of them,
     until the container tests and R10 have seen it.
- **The conflict is with three texts that say the extension only reads**, and this plan changes them openly:
  1. `SECURITY.md`: *"Read-only, to a host the person agreed to"*, and *"The remote script is one function that reads,
     writes and starts nothing in the background, and leaves nothing on the VM."*
  2. The help, in five languages: *"They only read: they change nothing on that machine."*
  3. This plan's own earlier line, *"nothing is installed on a VM"*, and step 0's list of what is outside the plan.
- **Together with F1 it is the widest step of this plan.** A "yes" that was given to *"Read the Claude session…"*
  becomes, with no press, the permission to install a package as root. For the owner's VMs, N5 is the decision. For
  another person: at the first start after the update, if a host of theirs has no `jq` and their ssh user is root or
  has `sudo` without a password, the extension installs software there. Nobody agreed to that. The stop button is too
  late for it: the first round starts 30 s after the extension.
- **What the install can do beyond adding a small program.** It runs the package's own scripts as root. It can run
  the distribution's hooks; on some systems a hook restarts services that were waiting for a restart. Refreshing the
  package lists changes what a later upgrade by the person sees. On a machine with several Linux users — and two
  aliases can be one machine — an install through one alias changes the machine for every user of it. The package
  comes from whatever repositories the host is set up with, checked as the host's package manager checks them; we add
  no repository and switch no check off.
- **What this plan does inside the owner's decision:** the smallest install there is — one package, the host's own
  manager by a fixed path, a fixed command from a closed table, no password ever, once for each host, a record
  written before the call; a notice and a list row for every install; the new words of the question, the help and
  `SECURITY.md`; and, where the user has no rights, a command for the person and nothing run.
- **What the planner would add, and puts to the owner as question Q2:** on a host whose "yes" is older than this
  release, the install waits for one press on the host's row. It is not the default here, because the owner's answer
  is "install it yourself".

**What an attacker gains.**

- **Someone who is that Linux user on the VM, or root.** They already hold every file the script reads. What is new: they
  can make the list show wrong accounts or numbers (a false `claude` or `jq` in `~/.local/bin` or on the `PATH`, an
  edited file). They cannot reach this machine through the reply: it is bounded (30 s, 64 KiB, 64 lines), parsed by a
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
- **The install, seen from each of them.** *That Linux user, or root:* the install gives them nothing; it runs only
  where they already are root or have `sudo` without a password. A false `jq` on their `PATH` only hides that `jq` is
  missing. *Another Linux user of the machine:* the manager is run by a fixed absolute path, never from the `PATH`, so
  a file they plant in a folder they can write is not run as root. *Someone on the network, or whoever controls a
  repository the host trusts:* they are in the position they are in whenever the person installs a package by hand;
  the install is one more such moment, at a time the person did not choose. *A hostile VM:* its reply can only name
  one of four managers or one of a few states. That chooses among fixed strings of the extension; it cannot put a
  word into the command the list shows. It can say "no `jq`" on every read; the install is still called once.
- **Logs.** A failed read logs the host, how it ended and ssh's own last line. An install logs the host, the manager,
  the outcome and the exit code. The scripts' stderr goes to `/dev/null`, so no vendor or package-manager text
  reaches ssh's stderr. stdout is never logged.

**Judgement on road (a): yes.** It runs the vendor's own client with the person's own login on the person's own VM —
what the person does by hand. Our script handles no credential. Measured: no model call (`num_turns` 0 in each of
the three reads whose JSON was recorded) and no transcript (in each of the four reads with the flags); from the
working folder `/` as well (W1); the plugin store does not grow (W4); the account and version
commands write nothing (W3). Not met as first written: nothing written (the four cache files, N1). **Not settled, and
not assumed away:** who rewrote `~/.claude.json` during one read (W6), and whether a read starts or leaves a process
— no child was seen in one sample, and one young `node` process of unknown origin was seen after it (W7). Not
measured: a login that needs a refresh. The judgement stands on what is measured. Of the two open points, the first no longer decides
anything: the owner accepted the write (N1), and W6 only records it. The second, W7, is the one way the road can still
fail: a hook or an MCP server of the person started by our timer is not worked around; the usage read is then off and
the list shows the account only. A live session disturbed by a read (R7 (b)) stops the usage read in the same way.

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

**Judgement on the consent: the owner's decisions — shared (O3), and used with no press (N2) — with finding F1
above.** **Judgement on the install: the owner's decision (N5), built as the smallest install there is, with finding
F2 above; the planner would not start it on an old "yes" without a press (Q2).** **Judgement on persistence: nothing
of a reading is stored; the shared answers exist today; added are one row of state and the record of installs, which
holds host names and no secret.** **Judgement on Codex: the passive file read**, because the live read can refresh a
login that live sessions share, and that case was never measured. **Judgement on agy: no file is opened; the program
is started with its version flag only, by the owner's choice (N4), with its risk named above.**

**Residuals, named.** A person's `ControlMaster` can make the call a persisting master on Linux or macOS, as today. The
timer makes one ssh login for each host every 10 minutes while a terminal is open, which a VM's ssh log will show. The
list shows an email on screen, so a screenshot shows it (O2). Each usage read is a start of Claude Code, with whatever
Claude Code does on a start: its caches above, its own network calls, and possibly its own update check — none was
seen in step 0's file list, and none is ours to switch off. A VM that prints the reset in another zone than UTC shows
the percent and a dash for the reset. A Claude Code version that changes the three lines shows *usage not read* with
the version. Each round starts `agy` once on each allowed host; a self-update set off by that start is possible and
not measured. An install can leave more than `jq`: its dependencies, refreshed package lists, and whatever the
distribution's hooks do; a package manager that is cut off can leave its database in need of a repair. A host where
the install was made through one alias has `jq` for every Linux user of that machine.

`SECURITY.md`: the section *The extension's one outward ssh call* becomes three kinds of call, and it no longer says
"read-only" for the third. It gains: the timer, which starts by itself; that the per-host answer now covers the
session read, the accounts read and the install, and what that widens (F1, F2); the stop button; road (a) and what
the VM then calls; the four files Claude Code writes on a read, and what is known about `~/.claude.json`; the start
of `agy`; the person's activity dropped on the VM; **the install — when, by which fixed commands, with which rights,
once, what it never does, and the command a person without the rights is shown**; and the residuals above. Its
sentence that the remote script leaves nothing on the VM is rewritten to be true of the two reading scripts only.

## Growth surfaces

- **The readings**: in memory, one for each host with an open `Creds:` terminal, a few hundred bytes each, dropped when
  the host's last terminal closes, when its answer is no longer "yes", when the round is stopped, and with the window.
  Bounded by the number of open hosts; today five.
- **The consent**: no new store. `coai.terminalHosts` stays as it is, capped at 100 rows.
- **The state of the accounts round**: one `globalState` row, `{ toldUtc, stoppedUtc }`, under 100 bytes. It does not
  grow.
- **The record of installs**: one `globalState` list, a row for each host where an install was tried, about 100 bytes
  a row, capped at 100 rows; at the cap the oldest row goes, and that host can then be tried once more. Under 12 KiB.
  A row left at `started` by a reload or a crash is not swept: it is shown as it is, and a press tries again.
- **The ssh child**: one for each host in a round, killed at 30 s with its tree, stdout capped at 64 KiB. At most one
  alive at a time for each window. The install child: one in a host's life (and one for each press on its row), up to
  180 s, 4 KiB.
- **The timer**: one for each window, cleared when no `Creds:` terminal of an allowed host is open or the round is
  stopped, disposed with the extension. A round that is interrupted by a reload leaves nothing: its child is killed
  with the extension host's tree, and memory is gone.
- **Log lines**: one when a host starts to fail and one when it reads again — not one for each round.
- **On the VM, by the accounts script**: nothing.
- **On the VM, by the install**: the package `jq` and what its manager adds as dependencies — a few megabytes, not
  measured — once for each machine, and refreshed package lists where the first try failed. Nobody retires them: they
  stay until the person removes the package. *Changes CoAI made on a host* is the record of it.
- **On the VM, by `agy` on a start**: not measured (W8). If a start with the version flag can set off a self-update,
  each update replaces a program of 269 MB; whether agy keeps the old one is not known. R7 notes the size of agy's
  files over an hour.
- **On the VM, by Claude Code on a usage read**: four cache files (the Security section). **They do not grow** (W4,
  measured): two reads in a row rewrote the same four names, and the plugin store held 2 such files before and after.
  This plan does not delete a file of Claude Code.
- **On the VM, `~/.claude/backups/`**: one new backup file appeared with the one rewrite of `~/.claude.json` (W1). If
  our read causes such rewrites, each one adds a backup file. **Not known:** whether our read causes them, and whether
  Claude Code retires old backups. W6 counts the backups before and after several reads. The owner accepted the
  rewrite (N1), so nothing stops on it; a count that rises with every read would be 144 files a day for each
  account's host with nobody to retire them, and is reported to the owner with that number.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, made by Claude Code. None by our code.

## Boundaries with other plans

This plan takes no slice of another plan's open work. It builds on code that shipped, and with O3 it **changes one
thing that the terminal-door plan built**: what its per-host answer covers, and the words that say so. The table says
what is whose, so it is read as complete:

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, *take*, *add*, *Asked* | [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0) | This plan widens the runner so a call needs no session operand. The session read itself does not change |
| The per-host consent: the store `coai.terminalHosts`, the question `read-remote-session`, *CoAI: hosts read over ssh* | the same plan | **This plan changes its meaning (O3, N2, N5)**: the same answer also allows the accounts read, by a timer with no press, and one install of `jq`. This plan rewrites the question's title and detail, the rows of the hosts list, the help paragraph — with its sentence that the doors only read and change nothing on that machine — and `SECURITY.md`. The store, the code, the buttons and the cap do not change |
| The gauge of coai's own rounds on this machine and on a Team server (`gaugeWords.ts`, the Team server's capture) | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing. That gauge counts coai's rounds; this list shows a person's accounts on other machines. No shared store, no shared reader |
| The account and limits line inside Claude Code | `dew_flow_conventions · mods/agent-models` | Nothing is changed there. This plan carries its word rules over and tests them against its cases |

Order: this plan starts after the terminal-door plan's last release is cut, so the runner is not widened and the
question is not reworded under an open pull request of that plan.

**The other side of this boundary is not written yet.** The terminal-door plan does not say that a later plan widens
its consent. It is open, in epoch 3, with one change recorded, so a second ledger entry there is a full stop. Whether
a boundary note is a change of that plan is not this revision's call. It is owed: at that plan's promotion at the
latest, its text names this plan beside *The person agrees to each host once*.

## Build order

1. **Done 2026-11-19:** the owner answered the open points O1–O7 and the new questions N1–N5. Still owed from this
   step: the question consultants are asked what is important before the epic split is final. The two open questions
   Q1 and Q2 go to them first and then to the owner; they do not block the plan round.
2. **E0 — step 0. Done 2026-11-19** on `ai2-root`: the RESULTS file is written and folded in here.
3. **Done 2026-11-19:** W1–W5, in one call the owner approved; the results are in the RESULTS file (§ 11) and in § 5.
   Owed: the gate W7 before the E1.S2 spec is final, and the records W6 and W8.
4. The plan round (`review_plan`) and our own Opus review, at the same time. The first passing verdict opens epoch 1.
5. **E1 — the remote accounts reader.** The stories by Opus; the cornerstone spec by Fable before its code; one PR.
6. **E2 — the round and the list.** The stories by Opus; one PR. E1's code has no door until E2 merges.
7. A pre-release VSIX from the merged head; the owner's real-window checks R1–R10 on it, before the tag, as change 1.1
   of the terminal-door plan did (no real VS Code can start on the build machine); a failed check is fixed first.
8. `review_feature` over the three epics, every finding resolved; an accepted fix that changes the product is a new PR
   and a new pre-release VSIX.
9. The extension release: the tag pushed alone and its run observed; `POST_DEPLOY.md` run; the plan promoted with its
   deviations and its stories documents.

## Test plan

- **The script, run for real** (`npm test`, Linux CI): the exported text run by `sh -s` (`dash`) and by `bash --posix`
  against a temporary `HOME` with false `claude`, `codex` and `agy` programs, as `remoteScriptRuns.test.ts` does with
  `tmux` and `ps`; `jq` is the real one. Cases: each vendor found, absent, signed out; each usage state; each operand;
  a wrong operand; `jq` missing; a vendor command that hangs (the time limit ends it), and one that starts a child and hangs (neither is left); a cut script does nothing;
  `claude` only in `$HOME/.local/bin` is found; the false `claude` records its arguments, its working folder (`/`),
  its stdin (empty) and `TZ`, and the test holds them to § 4 word for word. The false `agy` records the same, and the
  test fails if it was started with anything but its version flag.
- **The version is the only thing kept.** A false `agy` and a false `claude` print a version line with other words on
  it, an update banner before it, and more lines after it with a marker. The reply holds the digits and dots of the
  version and nothing else of that output. A version of 40 characters, of letters, or with no dot is "not known" on
  this side, and the vendor is still found. An `agy` that hangs is ended by its limit and the round goes on.
- **No secret leaves, seen and not assumed.** The temporary `HOME` holds false credential files (`.credentials.json`,
  `.claude.json`, `auth.json`, agy's sign-in file and log) with a marker token. The false programs record their
  arguments and environment. The test fails if the marker is in any argument, in any environment, in the script's
  stdout or stderr, or in a log line; and if `codex` was started at all. The test is seen RED once with a
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
  another length; a reset that has passed; no record; a record further from the end than the tail; a time with milliseconds and `Z`
  (the measured form); the record's time and the file's time; of two files the newer record wins.
- **The words**: the mod's cases carried over as a table — `5h 87% left`, the dash with no `% left`, `resets in —`,
  `1h 0m`, `<1m`, a reset that has come; Codex windows named by length; the grouping (two hosts with one account are one
  group; two unknown accounts are two groups; two hosts with equal numbers and different accounts are two groups);
  each host sentence.
- **The round** (pure, with seams for the clock, the call and the terminals): one call at a time, never two; the start
  moves one host each round; a failed host does not stop the round; usage is asked once for each account in a round and
  moves to the next host when the first fails; no call to a host with no "yes"; no timer with no terminal; a closed
  host is dropped with its reading; the 60 s floor of *Refresh now*; a result that lands after its host closed is dropped;
  three terminals of one host are one read; the first round starts with no press, 30 s after activation.
- **The consent, the stop button and the notice**: a session "yes" allows the accounts read with no question and no
  press; a "no" starts no ssh of any kind; *Forget* stops the accounts read and the row then asks again; an answer
  changed during a round stops the next call; a host with no answer is never contacted and the timer raises no
  question (a test fails if the ask seam is called from a tick); the stop button clears the timer, drops the readings
  and holds through a reload, and no read or install follows until it is started again; the one notice is shown once
  for an installation and names the hosts; the question's title and detail name the accounts read and the install.
- **The install, decided on this side** (pure, with a seam for the call and for the store): no install call without a
  `no-jq` reply, without a "yes", or while the round is stopped; the row is `started` in the store BEFORE the call
  seam is entered (a test fails on the other order); with any row for the host, a timer never calls the install
  again — after `installed`, after `no-rights`, after `failed`, after a row left at `started`; a press on the row
  calls it once; after `installed` the host is read once more in the same round; the cap of 100 rows.
- **The install reply and the shown command**: every state and every manager word; an unknown manager word, a state
  with extra text, a command-looking value in the reply — each `unreadable`, and none of that text is in a row; for
  each of the four managers the row shows exactly the fixed string of the table in § 4; with no manager it shows no
  command; the copy button puts that same string on the clipboard; **a test fails if any string that came from the
  reply is part of the shown command**, and if the extension starts any process for a `no-rights` reply.
- **The install script, run for real, in containers** (its own run on CI's Linux runner, with the network; not part of
  `npm test`): a Debian or Ubuntu image, a Fedora image and an Alpine image, each without `jq`. As root: `jq` is
  there afterwards and the reply says `installed` with the right manager. As a user with no `sudo`: `no-rights` with
  the manager's word, and no package changed. With `jq` already there: `present`, and the package manager was not
  started. With empty package lists on the Debian image: the one refresh, then installed. With the package lock
  held: `failed locked` after the wait, and nothing half-installed. A `yum` image is run if one is still published;
  if none is, the summary says that `yum` was not run. What is seen here is what § 4 only asks for.
- **The install script's hash**: a test holds its sha256; its spec holds the same value.
- **The runner**: the accounts argv is the pinned options, `--`, the host, `sh -s --` and the mode, with no `-n`; a host
  that fails the host class never reaches ssh; the session reader's argv and sentences are unchanged (its tests stay green).
- **The manifest and the help**: the button with `view == terminal` and its group; the command, and the changed
  paragraph on the per-host answer and the sentence that the doors only read, in the help of all five languages;
  `notification-sites.json` generated again with the new notices; the
  log scan (`remoteLogScan.test.ts`'s rule) covers the new modules.
- **Host scenario** (`npm run test:host`): with two terminals of two hosts and the call replaced through the test door,
  the false call is made once for each host, one after the other; with the terminals closed no call follows; the list
  holds the rows of both. U10 is measured here: what a label with `$(` draws.
- **Webview pages are not changed**; if one must be, it is tested by running it (`bundledPage.test.ts`).

## Real-window checks (the owner, on the pre-release VSIX, recorded in this plan when measured)

| # | Check |
|---|---|
| R1 | The icon sits beside *Ask other AI* in the terminal panel's title bar and opens the list. With no `Creds:` terminal the list says so |
| R2 | After the update, with no press: the one notice appears and names the allowed hosts; about 30 s after the start the first round reads each allowed host once (the VM's ssh log), also when two terminals name one host. A host that was never asked is not contacted and says *not asked yet*; choosing it raises the question with its new words. *Not this host*: no ssh to it of any kind |
| R3 | A round runs, one host after the other. The Claude numbers equal the built-in `/usage` on that VM, and the mod line within one point — the same dashes. The Antigravity group shows the version `agy --version` prints on that VM. Every row can be read at the owner's window width |
| R4 | Two hosts that share an account are one group that names both hosts, and its usage was read once in the round |
| R5 | A host that cannot be reached says so in its own sentence, and the other hosts are still read |
| R6 | All `Creds:` terminals closed: no further ssh login on the VM (its ssh log). One opened again: a round follows. *Stop the accounts round*: no further login either, also after a reload; started again from the list, a round follows |
| R7 | Left open for an hour: rounds about 10 minutes apart. Two parts. **(a) A record, on a Linux user with NO live Claude Code session** (it is W6 on the real build): the time of `~/.claude.json` and the number of files in `~/.claude/backups/` before and after each usage read; no new session file; the plugin store still holds the same number of files; the time and size of the agy program and of its folder; the processes of that user left after a read. The owner accepted the writes (N1), so this part is written down and stops nothing — but for a leftover process or a started hook or MCP server, which is W7's condition. **(b) A condition, on a host WITH live sessions** (as `ai2-root` has): the live Claude, Codex and agy sessions go on undisturbed — no session asks to sign in again, none loses its settings or MCP servers, none stops or restarts. A disturbed session stops the usage read (or the start of agy, if that is the cause) until the owner decides |
| R8 | *Take the question* in a `Creds:` terminal still works (the widened runner). *CoAI: hosts read over ssh* shows the new words; *Forget* there stops the accounts read of that host |
| R9 | The plan's M7: the time of one whole read from Windows with the pinned ssh options, twice, the time of `agy --version` in it, and the size of the reply; a host of an account with no 5-hour window shows `5h —` |
| R10 | **The install, on real hosts.** (a) A VM or container without `jq`, reached as root or with `sudo` without a password: within the first round `jq` is installed once; the notice and the row *installed jq on this host* appear; the next read shows the accounts; the VM's package log shows one install of `jq` and nothing else installed, upgraded or removed; no service was restarted; no second install call in the next rounds (the ssh log). (b) A Linux user with no rights: nothing is installed; the row shows the exact command for that VM's manager; the copy button gives that string; after the owner runs it there, the next round reads the accounts. (c) After a reload in each state: no new install call |

## Definition of Done

- [x] `research/RESULTS_accounts_and_usage_on_a_vm.md` records step 0 on one VM, and every row U1–U11 is answered or
      says what was checked and what was not (written 2026-11-19).
- [x] W1–W5 are measured and recorded in the same file, or each says why it was not (measured 2026-11-19, § 11 of
      that file; W1 and W2 each left a point that is not settled).
- [ ] W7 is measured and recorded there before the E1.S2 spec is final; W6 and W8 are recorded there.
- [ ] The icon in the terminal panel's title bar opens a list of the accounts of Claude Code, Codex and Antigravity on
      the hosts of the open `Creds:` terminals, grouped by account, each group naming its hosts; Antigravity shows its
      version.
- [ ] Usage is shown as "left" with the mod's words and its dash; a missing value is never a zero; usage that was not
      read says why.
- [ ] Every host with nothing to show says why in its own sentence: not asked, turned off, never read, failed, or `jq`
      missing — with the exact command where the ssh user may not install it.
- [ ] A round runs by itself from the start of the extension, every 10 minutes, one host at a time, one read for each
      distinct host with the pinned options, only for hosts whose shared answer is "yes"; it stops with the last
      `Creds:` terminal and with the stop button; a failed host does not stop it; the timer asks nobody.
- [ ] The accounts script is fixed, its hash is in the spec and in a test, and it writes nothing on the VM. No secret
      is in an argument, the environment, the reply or a log; the person's activity is not in the reply; of a version
      command only the version is — shown by the marker tests.
- [ ] `jq` is installed only by the install script: one package, the host's own manager by a fixed path, a fixed
      command, never a password prompt, once for each host with the record written first; every install is in the
      list and in a notice; a user without the rights is shown a fixed command and nothing is run. Seen in the
      container run and in R10.
- [ ] The Claude usage road is road (a), with the pinned command of § 4. Road (b) is not in the code.
- [ ] Nothing of a reading is persisted. The consent is the session reader's own answer; its question, the hosts list,
      the help and `SECURITY.md` say that it covers the accounts read and the install; a "no" or a *Forget* stops both.
- [ ] The session reader behaves as before, but for the words of its question and of its hosts list (its tests green, R8).
- [ ] Docs with the change: the help in five languages, with the sentence "they only read" made true again;
      `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md` (the new edges: a VM to a
      vendor, and a change of a VM); `SECURITY.md`, which no longer says "read-only" of every call; the
      `POST_DEPLOY.md` question asked.
- [ ] R1–R10 recorded here; `review_feature` run before the tag and every finding resolved; one extension release cut and
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

**N1–N5 are answered (2026-11-19).** They came from step 0 and from O3. The table keeps each question with the
planner's recommendation and the owner's answer. Where the two differ, the plan follows the owner.

| # | The question | The planner recommended | The owner answered |
|---|---|---|---|
| N1 | **What Claude Code writes on the VM on a usage read**, at most 6 reads an hour for each account. The first draft's condition was "nothing written". **(a)** Four cache files of its own, on every read: the same four names, rewritten in place, no growth (measured in four reads, W4). **(b)** During one of those four reads `~/.claude.json` was rewritten too, with a new backup file in `~/.claude/backups/`. It is **not proven** who wrote it: that read, or one of the live Claude Code sessions on the host. Is (a) accepted? And is (b) accepted, if W6 or R7 shows that our read itself rewrites `~/.claude.json`? | **(a) Yes.** They are the vendor's own caches, and every start by hand writes them too. **(b) Yes only if it is rare** — once for each host, at the first read from `/`, or as seldom. **No if it is every read**: then the Claude usage read is off and the list shows the account only, until the owner decides. Until W6 is measured this plan does not claim that `~/.claude.json` is untouched, and R7 holds the live sessions to "undisturbed" | **"Everything is acceptable."** (a) and (b) are accepted, (b) also on every read. W6 is a record and stops nothing. R7 (b), live sessions undisturbed, still holds: it is not a write question |
| N2 | **Answers given before this release.** With O3, a "yes" that was given to *"Read the Claude session…"* also allows the accounts read. May the accounts round stay off until the first press of the icon, with one button in the list to stop it again? It asks nothing. The other way: the round starts with the update for every host with a "yes", and the person is told by one notice | **Yes: off until the first press, with the stop button.** The owner presses the icon once and never sees a difference. Another person's old "yes" is then not used by a timer they never heard of | **No: "the round goes automatically over all unique ssh sessions in the terminal."** It starts with the extension, with no press, one read for each distinct host. The switch is removed; the notice of "the other way" and the stop button are kept (§ 3). The consent stays shared: only a host with a "yes" is read |
| N3 | May W1–W5 (§ 5) run now on `ai2-root`, each command approved as in step 0, before the plan round? | **Yes.** Else they run at the E1.S2 spec, and a bad result is a change of an approved plan | **Answered by the run:** the owner approved the one call and it ran on 2026-11-19 (§ 5) |
| N4 | **Antigravity shows no version**, because the script does not start `agy`: a start was not measured, and agy updated itself on a start on another machine. Is "installed, no usage, account not read" enough? | **Yes.** A version is not worth a start of that program on every host every 10 minutes | **No: "with the version."** `agy --version` runs in every round, with a limit, an empty stdin and the folder `/`. The risk is recorded in Security and Growth, and W8 measures a start. (The question's words "on a start" said more than the source: it records that agy updated itself, not what set it off) |
| N5 | **A host without `jq`** shows *`jq` is not installed on this host* for Claude and Codex, and we install nothing. Is that accepted, or should the script cut JSON with `sed` where `jq` is missing? | **Accepted, `jq` only.** Naming a field is safe by construction; a `sed` pattern next to private text is where a leak with no error starts | **Neither: "install what is needed yourself, and that is all"; "and only if there are no rights, write to the person: run this command".** `jq` stays the only reader, and it is installed where it is missing (§ 4, finding F2). Without the rights, the list shows the command and runs nothing |

**Open questions for the owner — two, and neither blocks the plan round.** Each goes to the question consultants
first. This text takes the default written beside each; another answer after the round is a change.

| # | The question | The default in this text | The planner's recommendation |
|---|---|---|---|
| Q1 | **A host with no answer yet.** Should the round ask once, by a modal, when it first meets a host of an open `Creds:` terminal that has no answer? | **No.** The timer never raises a question. The list shows the host as *not asked yet*, and a press on its row asks | **Keep the default.** A modal that a terminal's NAME can set off, with no press, is what the terminal-door plan avoided: any program can open a terminal called `Creds: x / y`. And with five hosts it is five modals at the start |
| Q2 | **An install on an old "yes"** (finding F2). On a host whose "yes" was given before this release — to *"Read the Claude session…"* — should the install of `jq` wait for one press on that host's row? The extension can tell: such a row's `askedUtc` is older than the day the one notice was shown | **No: it installs by itself**, as N5 says, on every allowed host where the ssh user may | **Yes, wait for the press on an old "yes".** For the owner that is at most one press for each host without `jq`, once. For another person it is the difference between software installed as root on their server after an update, and a row that offers it. A "yes" given to the new words installs by itself either way |

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
read again before every call, the stop button, and a timer that never asks; **(d)** absent, signed out, no usage
source, skipped, failed and unreadable are different results, and a missing number is never a zero; **(e)** the one
place the extension changes a VM — what the install runs as root, that it runs once, and that the command a person is
shown is never made from VM text. A mistake there is made on somebody's server, with root, and cannot be taken back
by a release.

Cornerstones: 2 of 2 — E1.S2, E1.S4

### E0 — Step 0, the measurement · done 2026-11-19

- **Goal.** Know, from one real VM, what each CLI stores and prints, what can be read with no secret, which Claude usage
  road holds, and what one read costs in time and bytes.
- **Result.** [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md):
  step 0 on `ai2-root`, four read-only calls and a fifth for W1–W5, each approved by the owner. Road (a) is taken. U1–U6, U8, U9 and U11
  are resolved; U7 is half open and not needed; U10 waits for E2's host scenario; M7 is real-window check R9; M8 was not run. The owner's answers to O1–O7
  are recorded above.
- **What did not go as this epic was written.** No E0 stories document was written: the commands were approved one by
  one in the Creds dialog. The credential file's key names were listed although road (a) held. Five points were left
  open (W1–W5, § 5); they were measured later the same day in a fifth call, and two of them left a point that is not
  settled (W6, W7). The RESULTS file and the first revision were committed as `2edfe1c7`; the W1–W5 results were
  folded in after that commit.
- **Cornerstone areas.** None: it is a measurement.

### E1 — The remote accounts reader and the install · depends on E0

- **Goal.** One call that reads the accounts and usage of one host over ssh and gives a typed result, and one call
  that installs `jq` there and says how it ended — with nothing that calls either yet.
- **Scope.** `src/remoteSsh.ts` (the runner widened: one argv builder and one run for its callers, the sentence's
  subject and the limits as parameters, `SSH_OPTIONS` untouched); `src/remoteFrame.ts` (the framing extracted for its
  callers); NEW pure modules for the accounts script, its frame and typed result, the Claude usage lines and the mode
  operand; NEW pure modules for the install script, its reply, the closed table of managers and commands, and the
  install record; the shared consent's new words (`src/remoteSessionHost.ts`: the question's title and detail, the
  rows of *CoAI: hosts read over ssh*); their tests and the container run; `research/module_extension.md`,
  `research/module_tests.md`.
- **Depends on.** E0's RESULTS file; W7 measured before the E1.S2 spec is final; the terminal-door plan's last
  release cut.
- **Done when.** The script, version, parser, usage-line, Codex-record, runner, consent and install rows of the test
  plan are green; the marker tests were seen RED once; both hashes are in their specs and in tests; the install script
  was seen installing, refusing and failing in the containers; the session reader's tests are green, changed only
  where they hold the question's words; the code gate passed; the PR is merged.
- **Cornerstone areas.** (a) and (b) together — the first cornerstone; (e) — the second; (c) the shared answer; (d)
  the result's arms.
- **Stories.**
  - **E1.S1 — the runner serves its callers.** `runRemote` keeps its checks and its words; a shared run and argv
    builder under it; the accounts call and the install call beside it, each with its own limits. By Opus's brief,
    coded by Sonnet.
  - **E1.S2 — the accounts script, its reply and its parser (CORNERSTONE, `model: fable`).** Fable writes the spec
    before any code: the exact script text and its sha256, the tools it needs, the pinned Claude commands with their
    flags, folder and limits, the `agy` version command and the rule that keeps only a version, the three line
    patterns, the Codex record rule, the reply's grammar and limits, the typed result with one arm for each state,
    the parser's rules with the reset parse, the scenarios (the test plan's script, version, marker, hash, parser,
    usage-line and Codex rows), the results of W1–W8, and the security judgement on the exact text — outside the
    quota, by the security row. Sonnet codes; Opus reviews.
  - **E1.S3 — the shared consent says what it covers.** The question's new title and detail, the rows of the hosts
    list, and the tests that hold a "no" and a *Forget* to the session read, the accounts read and the install.
  - **E1.S4 — the install of `jq` (CORNERSTONE, `model: fable`).** Fable writes the spec before any code: the exact
    script text and its sha256, the four fixed paths, the rights check, the one command for each manager with its
    exact options, the one refresh of the lists, the limits, the reply's grammar, the closed table of shown commands,
    the record and its "written first" rule, the sentences of each state, the scenarios (the test plan's install
    rows and the container run), and the security judgement on the exact text. Sonnet codes; Opus reviews.

### E2 — The round and the list · depends on E1

- **Goal.** The icon, the list grouped by account, and the 10-minute round that fills it by itself.
- **Scope.** NEW pure modules for the round (the distinct hosts, the order, the mode for each host, the table in
  memory, start and stop, the install step) and for the words and rows (the mod's rules, the grouping, the host
  sentences, the install sentences with their fixed commands, the plain-text road for VM text); a NEW host module with
  the `vscode` seams (the timer, the terminal events, the QuickPick with its two title buttons and the copy button of
  a row, the state and the install record in `globalState`, the notices, the test door) bound in `src/extension.ts`;
  `package.json` (the command, its icon, the `view/title` entry); the help in five languages; `notification-sites.json`;
  the host scenario; `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md`,
  `SECURITY.md`; the `POST_DEPLOY.md` question (the file is at its cap of twelve, so a new item needs a named one to
  leave).
- **Depends on.** E1 merged.
- **Done when.** The words, round, stop-button, notice, install-decision, manifest, help and host-scenario rows of the
  test plan are green; the Definition of Done's items hold; the PR is merged; R1–R10 are recorded on the pre-release
  VSIX of the merged head; `review_feature` ran from the merged head and every finding is resolved; the release is cut
  and verified; the plan is promoted.
- **Cornerstone areas.** (c) the timer never asks, contacts only allowed hosts, and does nothing while the round is
  stopped; (e) the install is called once and its record is written first; (d) the sentences. No cornerstone story is
  recommended here: the rules are pure, small and fixed by this plan, and both quota places are used in E1.
- **Stories.**
  - **E2.S1 — the round.** The pure planner, its table in memory, the stop state and the install step.
  - **E2.S2 — the words and the rows.** The mod's rules, the grouping by account, the host sentences, the install
    sentences and the part *Changes CoAI made on a host*.
  - **E2.S3 — the host and the door.** The timer and the terminal events, the list, *Refresh now*, *Stop the accounts
    round*, the copy button, the notices, the question raised from a host's row, the command and its icon.
  - **E2.S4 — the documents and the release.** The help, the module documents, `SECURITY.md`, the pre-release VSIX,
    R1–R10, `review_feature`, the tag, the promotion.
