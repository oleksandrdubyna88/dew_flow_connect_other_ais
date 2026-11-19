# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **plan only, nothing implemented yet, 2026-11-19.** Scope: `src_vs_code` only — one command with an icon in the
> terminal panel's title bar, a list of the Claude Code, Codex and Antigravity accounts on the hosts of the open
> `Creds: <host> / <session>` terminals with their usage, a background round over those hosts every 10 minutes (one
> read-only ssh call per host, one host at a time), and a per-host consent of its own. No change to `src_mcp`,
> `src_server` or CredsForDevs; nothing is installed on a VM. Three epics (E0 is a measurement with no code), one
> extension release. **Not approved yet:** the question consultants have not been asked, the owner has not answered the
> open points, and no plan round has run. The change ledger (the `Plan changes` section of
> `.agents/conventions/common/plan-change-control.md`) opens here, directly under this block, with its epoch 1 line in
> the commit of the first approval; until then the section is not written, because a ledger with no epoch line is a
> finding of `plan-lifecycle.mjs`.

Related: [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (its E2 built the read-only
ssh reader this plan reuses), [../research/RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md)
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

**Read on this machine, 2026-11-19, as a hint only** (`claude --help`, Claude Code 2.1.328; the VMs' version is not
known): there is no `usage` subcommand; `claude auth status` takes `--json`; `-p` has `--output-format json`,
`--no-session-persistence`, `--strict-mcp-config`, `--setting-sources` and `--max-budget-usd`; `--bare` never reads
the OAuth login, so it cannot be used for a usage read.

### Not verified — step 0 measures each of these before any code

| # | The claim | Why it is not verified |
|---|---|---|
| U1 | A Codex rollout file holds `token_count` events with `rate_limits` (`primary` and `secondary`: `used_percent`, `window_minutes`, `resets_at`) | No Codex code or fixture in this repository has these names. The repository's own measurement has the camelCase names of the app-server read only. `src_mcp/tests/UsageParserTests.cs:58` has a `token_count` event with `usage` and no rate limit |
| U2 | The folder layout under `~/.codex/sessions`, and how to find the newest file without listing them all | Only the path shape `~/.codex/sessions/…/rollout-*.jsonl` is recorded |
| U3 | `codex login status` exists and names an account without a secret | Not in this repository |
| U4 | `~/.claude.json` holds `oauthAccount` with `emailAddress` and an organization name | Not in this repository. The mod uses `claude auth status --json` instead |
| U5 | Whether `claude auth status --json` prints an organization, and whether it prints anything secret | The mod reads two fields only |
| U6 | Whether a non-interactive Claude Code command prints the usage (`claude -p "/usage"` or another), whether it makes a model call, what it starts (hooks, MCP servers, plugins) and what it writes | Not measured anywhere |
| U7 | The path and the key names of the Claude credential file on a Linux VM, and the headers the usage endpoint needs from a client that is not Claude Code | The file is off limits here, and the mod sets `Accept` only. Needed for road (b) only |
| U8 | Where agy keeps the account on the VM's version, and whether any file or command states usage | The measurement of 2026-10-10 found none on 1.3.3 |
| U9 | Whether `claude`, `codex`, `agy`, `curl` and `timeout` are found by `ssh <host> sh -s` (no login shell, no terminal), which `sh` the VM has, and whether its `printf` is a builtin | The owner's creds line needed a login shell for another tool |
| U10 | That a QuickPick label draws `$(name)` as an icon | Known from the VS Code API, not from a test here |
| U11 | The Claude Code version on the VMs and the flags it has | Only this machine's 2.1.328 was read |

## The design

### 1. The view

- **A new command `coai.terminalAccounts`**, title "AI accounts and usage", icon `$(account)`. In `menus.view/title`:
  `when: view == terminal`, `group: navigation@91`, so it sits right after *Ask other AI* (`navigation@90`). It is in the
  command palette. It has no chord, so the skip-shell list does not change.
- **The icon is always there**, like the chat icon. With no `Creds:` terminal open the list says one sentence: *"No
  `Creds: <host> / <session>` terminal is open, so there is no host to read."* A button that comes and goes would move
  its neighbours (open point O6).
- **The view is a QuickPick instance (`createQuickPick`), not a webview.** The reasons:
  1. It is a short read-only list: today five hosts and three vendors, at most a few dozen rows.
  2. It needs what `conversationPickerCommand.ts` already needs: a title button (*Refresh now*) and new rows without
     closing, when a host's read lands. That pattern is in the code and reads well.
  3. A webview is a new bundled page with its own CSP, nonce and a test that runs the page. That cost buys only colour
     and layout, which the owner did not ask for.
  4. What a QuickPick cannot do is named under *What this plan does NOT do*: no colour for each number, and a long
     row is cut in a narrow window. If the real-window check R3 shows the rows cannot be read, a webview is a plan change.
- **The rows are grouped by account** (decision 4). A separator names the vendor and the account; under it one row holds
  the usage line, and the row's second line names the hosts and the age of the reading:

  ```
  ── Claude Code · name@example.com · Example Org ──
  5h — · resets in — · week 80% left · Fable 85% left
      ai1-dev2, ai1-dev3 · read 4 min ago
  ── Codex · account unknown · ai1-dev2 ──
  week 60% left · resets in 3d 17h
      from a Codex session 2 h ago · read 4 min ago
  ── Antigravity · ai1-dev2 ──
  not measured
  ── Hosts ──
  ai1-root — the last read failed 3 min ago: “ai1-root” did not answer over ssh within 20 s.
  ai2-dev2 — not asked yet. Choose this row to allow reading its accounts.
  ai2-root — reading accounts is turned off for this host.
  ```

  - **The account** is an email or a name, never a secret. Two hosts are one group only when the vendor and the whole
    account text are equal. An account that is not known is never grouped: it is one group for each host.
  - **The usage line** uses the mod's words: `<window> <n>% left`, `resets in <time>`, `·` between the parts; a missing
    value is `—` with no `% left`, never `0%` (decision 5). The rules are the mod's own (`percentLeft`, `resetText`,
    `minutesLeft`), written once in a new pure module here and held to the mod's cases by a table of tests. The reset
    text is made when the list is drawn, from the reset moment. A Codex window is named by its length: 300 minutes is
    `5h`, 10 080 is `week`; another length is said in hours.
  - **When two hosts of one account were read at different times**, the group shows the newest reading and its age.
  - **Every host has its own sentence in the *Hosts* part** when it has no reading to show: not asked yet, turned off,
    never read, or the last read failed (with the runner's own sentence and its age). A host whose last read failed
    keeps its older numbers in its account group, and the group's second line says the later read failed.
  - **Usage that was not read says why**, in its own words: *usage not read: Claude Code on this host printed no usage*,
    *usage off*, *not measured*. It is never a row of dashes with no reason.
  - **Choosing a row** of a host that was not asked opens the consent for that host. A row *Allow all N hosts…* opens
    one question that names every host. Choosing any other row does nothing.
- **Text from a VM is made plain at one road in**: control and format characters are taken out, the length is cut, and
  `$(` is broken so the list never draws an icon a VM chose (U10). The existing pick list of the session reader passes
  VM text with `$(` intact (`src/remoteRead.ts:213`, `:220-226`). This plan does not change it; whether it should use
  the same function is a question for the owner after U10 is measured.

### 2. The round

- **Which hosts.** The distinct hosts of the live (not exited) terminals whose name gives `kind: 'session'`, read through
  `liveTerminals` — and of these only the hosts with a "yes" in the accounts consent (§ 3). A host with no answer or a
  "no" is never contacted.
- **One host at a time.** A round takes the hosts in name order, starting one host later each round, and awaits each read
  before the next. Two reads never run at once. A failed host is recorded with its sentence and the round goes on.
- **One ssh call for each host in a round**: the pinned options of `SSH_OPTIONS`, unchanged, then
  `-- <host> sh -s -- <mode>`, the script on stdin. `<mode>` is one of two fixed words (below).
- **Every 10 minutes, counted from the END of a round** to the start of the next, so two rounds never overlap. The
  value is a constant, `600 000 ms`, not a setting: the owner said 10 minutes, and a setting costs a help entry in five
  languages and a control (open point O4).
- **Usage once for each account in a round** (decision 4). The extension remembers, in memory, which Claude account each
  host had at its last read. When a host's account already has a usage reading from this round, that host is called
  with the mode `no-claude-usage` and the script reads the account only; else the mode is `claude-usage`. A host never
  read is called with `claude-usage`. If the first host of an account fails, the next host of that account reads the
  usage in the same round. If a host turns out to have another account than last time, its usage waits for the next
  round. Codex usage is a local file read with no cost, so it is read on every host.
- **The first round** starts 30 s after the window has a `Creds:` terminal of an allowed host — at activation or when
  the first such terminal opens. It is not started by a reload alone with no allowed host.
- **It stops when no `Creds:` terminal is open.** With no live terminal of an allowed host the timer is cleared, not
  left idle. `onDidOpenTerminal` arms it again; `onDidCloseTerminal` checks again. A host whose last terminal closed is
  dropped from the next round, and its reading is dropped from memory. A read in flight is not killed by a close; its
  result is dropped.
- **The timer never asks a person.** A consent question is raised only by a press in the list. A round over hosts that
  were not asked reads nothing and shows nothing.
- ***Refresh now*** is a title button of the list. It starts a round at once, unless one is running (the list shows it is
  busy) or the last round ended less than 60 s ago (the mod's own floor). Opening the list draws what memory holds; it
  starts a round only when an allowed host has no reading yet.
- **Results are in memory only.** Nothing of a reading is written to disk, to `globalState`, to a ledger or to a log:
  the readings hold email addresses and the usage of a person's accounts, they are 10 minutes old at most, and after a
  reload the next round brings them back within a minute. The only thing persisted is the consent (§ 3).
- **Limits of one call**: killed at 20 s (the tree, as today), stdout capped at 64 KiB. Both are ceilings; step 0
  measures the real time and size and the spec may lower them. A round over five hosts is at most 100 s.
- **A failed read is logged once, not every round.** The runner's one log line is written when a host's failure
  sentence changes, and once when it reads again. A host that is down for a day is two lines, not 144.
- **Not serialized with the session reader.** A press of *take the question* can run while the round reads the same
  host: two ssh children to one host, both read-only. Each VS Code window runs its own round, so two windows with
  terminals of one host read it twice in 10 minutes. Both are named, not prevented.

### 3. The consent — a separate one

**Decision: the accounts read has its own per-host consent. A "yes" to reading a Claude session is not a "yes" to this.**

- The question a person answered for the session reader is *"Read the Claude session on “x” over ssh?"*. It covers a read
  that a press starts and that prints a transcript. The accounts read is another act: a timer starts it with no press,
  around the clock while a terminal is open; it reads who is signed in; and on road (a) or (b) it makes the VM call the
  vendor with the VM's own login. A consent covers what its question said. Reusing the old answer would widen a past
  "yes" in silence.
- The two answers are independent. A "no" for sessions does not block the accounts question, and the other way round.
  A person who wants no ssh to a host at all says "no" twice, once for each.
- **The question** is a modal through `notifyAndAsk`, code `read-remote-accounts`, raised only by a press in the list:
  *"Read the AI accounts and their usage on “x” over ssh?"* with a detail that says what will run: a fixed read-only
  script, as your ssh user, every 10 minutes while a `Creds:` terminal of that host is open; it prints the account name
  and the usage numbers of Claude Code, Codex and Antigravity; and — when road (a) is in use — it starts Claude Code on
  that host once for each account in a round, which asks Anthropic with that host's own login. Buttons *Read* and *Not
  this host*; Escape writes nothing. The row *Allow all N hosts…* raises one question that lists every host by name
  (code `read-remote-accounts-all`).
- **The store** is a second `globalState` key, `coai.terminalAccountHosts`, with the same row shape, the same pure
  functions (`consentOf`, `withAnswer`, `withoutHost`, `answersOf`) and the same cap of 100 rows. Each row also holds
  the **reach** it was given for: a number that names what the script touches. A release that makes the script touch
  more (for example road (b) after road (a)) raises the number, and a "yes" for a lower reach reads as "ask".
- ***CoAI: hosts read over ssh*** lists both kinds of answer, one row for each host and kind, and changes or forgets one
  as today.

### 4. The remote script and its reply — the cornerstone

- **Fixed text, sent whole, pinned.** A new constant, the accounts script, in the shape of `REMOTE_SCRIPT`: POSIX `sh`,
  one function read to its end and only then called with `</dev/null 2>/dev/null`, a begin line first and an end line
  last, so a script cut on the way does nothing. Its sha256 is in its spec, and this time a test holds the hash too: this
  script may run a vendor program, so an edit of the text without the spec must turn a test red.
- **No write, no `cd` to a folder a person owns, no background job, no temporary file, no here-document.** Every tool
  it needs is checked first; a missing tool is said, never read as "nothing found".
- **The operand** is one of two fixed words, `claude-usage` or `no-claude-usage`, checked by a closed list on this side
  and again in `sh`. No value from a terminal's name or from a VM is ever an operand.
- **The reply is line-based, fixed and small.** A marker line names a vendor and a state from a closed list; a data
  line starts with `:` and holds one key from a closed list and its value. The shape (the spec fixes the exact words
  after step 0):

  ```
  COAI-ACCOUNTS 1
  COAI-VENDOR claude found            # found | absent | signed-out | unreadable
  :version 2.1.328
  :email name@example.com
  :org Example Org
  COAI-USAGE claude read              # read | skipped | off | failed <word>
  :five-hour-used -
  :five-hour-resets -
  :week-used 20
  :model Fable 15
  COAI-VENDOR codex found
  :account -
  :plan prolite
  COAI-USAGE codex read
  :window 40 10080 1791964091         # used percent, window minutes, reset (epoch seconds)
  :as-of 1791900000
  COAI-VENDOR agy not-measured
  COAI-ACCOUNTS-END
  ```

  The script sends "used", as the vendors state it; "left" is made on this side by the one rule. At most 64 lines, each
  at most 512 bytes; the script cuts every value itself.
- **The script cuts, the extension decides.** `sed` on the VM only cuts a named field out of a program's output or a
  file. Whether a value is a number, in range, an email or a time is decided here, by the parser.
- **The parser** (`readAccounts`, pure) is the one road in. The frame is the last begin line and the first end line
  after it, through ONE framing function shared with `remoteFrame.ts` (extracted, not copied). An unknown marker, an
  unknown key, a line that is neither, a line past the limits or an exit code that is not 0 makes the whole answer
  `unreadable` with a reason from a closed list. A percent is digits with at most two decimals, 0–100; an epoch is
  digits; an ISO time needs an offset; a text value loses control and format characters and is cut. **A value that looks
  like a credential is dropped and its vendor is `unreadable`**: 40 or more characters of letters, digits, `_` and `-`
  with no space, `@` or `.`, or a known token prefix. Nothing of a reply is run, used as a path, or put in a log or an
  error's text.
- **The typed result** has one arm for each state, so "absent", "signed out", "not measured", "usage off", "usage
  failed" and "unreadable" are different values with different sentences. A number that is not there is `null`, never 0.

**What the script reads, for each vendor:**

- **Claude Code — the account.** `claude auth status --json`, the command the mod uses; the script keeps `loggedIn`,
  `email` and, if step 0 finds one, the organization. Our script opens no Claude file on this road. If the command is
  not there or does not answer on the VM, the fallback is two named fields of `~/.claude.json` (U4), cut by `sed`,
  nothing else of that file printed. Step 0 says which.
- **Claude Code — the usage.** Three roads, in this order (decision 3):
  - **(a) Claude Code makes the call — recommended.** The script runs one non-interactive Claude Code command under a
    time limit and cuts the usage numbers out of what it prints. No secret passes through our script: the token stays
    inside Claude Code, as it does in the mod. Step 0 must find a command that passes ALL of these, or road (a) fails:
    it prints the numbers; it makes **no model call** (a `/usage` that is sent to the model as a prompt would spend the
    limit it reads); it starts no hook, no MCP server and no plugin of the person (the owner's VMs have MCP servers at
    user level, and a start every 10 minutes on every host is not acceptable); it writes no session file and leaves no
    process; it ends within the time limit with no terminal and an empty stdin; its output has a shape a strict
    pattern can read. `--bare` cannot be the answer: it never reads the OAuth login. When the output is not in the shape
    the pattern knows, the usage is `failed format` with the Claude Code version beside it, never a guess.
  - **(b) The script reads the VM's Claude credential and calls the endpoint with `curl`.** Only with the owner's yes
    (open point O1) and under every condition of the Security section. Not recommended there.
  - **(c) The account only.** Claude shows the account and *usage off*.
- **Codex — the usage.** The last rate-limit record of the newest session file under `~/.codex/sessions` (U1, U2): a
  bounded tail of one file, the last matching line, and of it only the numbers of its windows and its time. No network,
  no login, no write. The numbers are as old as the last Codex turn on that host, and the list says so (*from a Codex
  session 2 h ago*). When no file has such a record the usage is `failed none`. The live read, `codex app-server`, is
  **not taken**: it writes into the Codex home and can refresh the login while live Codex sessions use the same one
  (open point O5).
- **Codex — the account.** `~/.codex/auth.json` holds tokens and is never opened. If step 0 finds a command that names
  the account with no secret (U3), the script runs it; else the account is *unknown* and the plan type, when the record
  has one, is shown instead.
- **Antigravity.** Not known where agy keeps the account, and on 1.3.3 it stated no usage at all. Until step 0, agy is
  `not-measured`. If step 0 finds a file with the account and no secret, the script reads that one field. If it finds no
  usage source, the list says *agy states no usage; it says when it resets only when it refuses*, and that is the result.

### 5. Step 0 — the measurement, before any code

On ONE VM, with the owner present. A draft of the script and a list of single commands are written into the E0 stories
document first. Each is run by the owner, or by the session only after the owner said go for that exact text. Nothing is
installed. Values of secret files are never printed: key NAMES, sizes, times and `stat` only.

| # | What is measured |
|---|---|
| M1 | The shell and the tools under `ssh <host> sh -s`: which `sh`, whether `printf` is a builtin, where `claude`, `codex`, `agy`, `curl`, `timeout` and `stat` are, and each CLI's version (U9, U11) |
| M2 | `claude auth status --json`: the key names, the time, the exit code, whether it names an organization, and whether the credential file or `~/.claude.json` changed (`stat` before and after) (U5) |
| M3 | Road (a): `claude --help` on the VM; what `-p` does with a slash command; each candidate command (`claude -p "/usage"`, and any `claude` subcommand that prints usage or status); for each one the six conditions of § 4 — the numbers, a model call or not, what started, what was written (`find -newer`), the time, the output's shape — with no terminal and an empty stdin (U6) |
| M4 | The top-level key names of `~/.claude.json` and of its account object (U4). Only if road (a) failed and the owner wants road (b) weighed: the credential file's path, mode and key names, never a value (U7) |
| M5 | Codex: the layout of `~/.codex/sessions`, the newest file's size, how many rate-limit records it has, the key names and value types of the last one, whether that line holds any text of a conversation, and what `codex login status` prints (U1–U3) |
| M6 | agy: its version, its help (any status, account or usage command), the file names under `~/.gemini`, and the key names of the files that could name the account (U8) |
| M7 | The draft script, run from Windows with the pinned ssh options: the time of one read, twice, and the size of the reply |
| M8 | From a second Linux user of the same VM, or a second terminal: `ps` while the draft runs shows no secret in any argument |

The results go into `research/RESULTS_accounts_and_usage_on_a_vm.md`, with the expectation written before each run, and
are folded into this plan BEFORE its plan round. So the plan that is approved holds measured facts, and step 0's findings
are not changes of an approved plan. Within the plan as written: which of the roads (a), (b) or (c) is taken, in that
order; the field names; the flags; limits under the ceilings. Outside it, and so a change once the plan is approved:
anything installed on a VM, a second ssh call for a host in a round, a file with a secret that this plan does not name.

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs. Nothing is installed or written on a VM by our script.
- No real time, no push from the VM, no setting for the interval.
- No colour for the numbers (the mod colours them), and no webview.
- No history, no chart, no ledger of the readings, no alert when a limit runs low.
- No reading of a host that has no open `Creds:` terminal, and none of this machine's own accounts.
- No live Codex read (`codex app-server`), and no opening of `~/.codex/auth.json`, ever.
- No refresh of any login by our script. An expired login is said as it is.
- No grouping of accounts that are not known, and no guess that two hosts share an account from equal numbers.
- No change to `SSH_OPTIONS`. Connection sharing is still not pinned, as `SECURITY.md` says today.
- No lock between VS Code windows: each window with `Creds:` terminals runs its own round.
- No change to the session reader's pick list (the `$(` note in § 1) without the owner's word.

## Security

This section is the security judgement of the plan (Fable, `subagent-models.md`: the security row). The cornerstone
spec repeats it for the exact script text, and it must stand before any E1 code.

**The new edges.** (1) A read-only ssh call that a TIMER starts, to hosts the person allowed for this. (2) On road (a) or
(b), a call from the VM to `api.anthropic.com`, made with the VM's own Claude login. (3) Text from a VM drawn in a list.

**Every secret and private thing the script comes near, and where it goes:**

| What | Where it is | Road (a) | Road (b) | Road (c) |
|---|---|---|---|---|
| The Claude OAuth access token | the VM's Claude credential file (U7) | Read by Claude Code inside its own process. Our script never opens the file | Read by our script into a shell variable, given to `curl` on stdin, sent over TLS to the one fixed URL | Not touched |
| The Claude refresh token | the same file | Claude Code may use it as on any start (M2 and M3 measure whether the file changes) | Passes through `sed`'s input and is dropped. Never sent, never used | Not touched |
| `~/.claude.json` (can hold MCP server values) | the VM | Not opened by our script when `claude auth status` works | the same | the same; the fallback cuts two named fields |
| `~/.codex/auth.json` | the VM | Never opened, on any road | | |
| Codex session files (a transcript: private, not a credential) | the VM | A bounded tail is read; only the numbers of the last rate-limit record leave the VM | | |
| agy's sign-in files | under `~/.gemini` on the VM | Not opened until step 0 names one file that holds the account and no secret | | |
| The person's ssh keys and agent | this machine | Used by `ssh` as today; `ForwardAgent=no` | | |
| The account email and organization (personal data, not a secret) | the reply | In memory and in the list. Never logged, never stored, never sent on | | |

**What an attacker gains.**

- **Someone who is that Linux user on the VM, or root.** They already hold every file the script reads. What is new: they
  can make the list show wrong accounts or numbers (a false `claude` on the `PATH`, an edited file). They cannot reach
  this machine through the reply: it is bounded (20 s, 64 KiB, 64 lines), parsed by a closed vocabulary, never run, never
  a path, shown with control characters and `$(` taken out. On road (a) the script runs `claude` by its name every 10
  minutes; a false `claude` there already runs whenever the person types it.
- **Another Linux user on the same VM** (the owner has several users on one VM). They can list processes and read every
  argument. So **no secret is ever an argument**: on road (a) the arguments are a fixed command; on road (b) they are
  `curl`'s fixed options and the fixed URL. They cannot read another user's files, variables or pipes. The script
  writes no file, so nothing is left in a shared folder.
- **Someone on the network.** Between this machine and the VM: ssh, with `StrictHostKeyChecking=yes`, so an unknown or
  changed host key is a refusal. Between the VM and the vendor: TLS, by Claude Code (a) or by `curl` with the VM's own
  certificate store (b); never `-k`, no redirect followed.
- **A program that opens a terminal named `Creds: evil / x`.** A name is not proof, which is why the consent names the
  host and only a press raises it. The timer contacts only hosts with a "yes" for this reach. `BatchMode=yes` makes a
  password prompt a refusal.
- **A hostile reply.** It can only become text in a list, or `unreadable`. The credential-shape rule of the parser is
  a net under the script: if a defect ever printed a token, it would not reach the screen. It is a net, not the
  measure; the measure is that the script has no line that prints a file.
- **Logs.** A failed read logs the host, how it ended and ssh's own last line. The script's stderr goes to
  `/dev/null`, so no vendor error text — which can quote a request — reaches ssh's stderr. stdout is never logged.

**Judgement on road (a): yes, if step 0's six conditions hold.** It runs the vendor's own client with the person's own
login on the person's own VM — what the person does by hand. Our script handles no credential. What must be measured
and not assumed: no model call, nothing of the person's configuration started, nothing written, and no harm to the
live sessions that share the login (the credential file and `~/.claude.json` unchanged by the read, or changed only
as any `claude` start changes them). A condition that fails is not worked around; the road fails and the next one is weighed.

**Judgement on road (b): it can be contained, and it is still not recommended.** Contained means all of these, each
with a test: `printf` is a shell builtin on that VM (checked by the script, else the usage is off); the token goes from
a shell variable that is not exported to `curl -q -K -` on stdin, never an argument (another user's `ps` reads those),
never the environment, and not a header file either, because a file is a write on the VM that a killed call can leave behind; `-q` so a `~/.curlrc` cannot reshape the call; https only, the one fixed URL, no redirect, a time limit and a size
limit; the script cuts the numbers out and never prints the body; **never a refresh** — an expired token is `failed
expired`, and Claude Code refreshes it on its own next use; the consent's reach is raised, so every host is asked again
with a text that says the script reads the Claude login. What cannot be contained: it is a client that is not Claude
Code, calling an internal endpoint with a subscription login, every 10 minutes, around the clock, on every account. The
asset at risk is the account itself, and the vendor can change or refuse that endpoint without notice
(`hooks/limits.tsx:4-5`). It also puts a credential into our script, so one defect in a `sed` line becomes a leak. **If
road (a) fails, the recommended answer is road (c).** Road (b) is built only on the owner's explicit yes, given after
reading this paragraph.

**Judgement on the consent: separate, per host, with a reach** (§ 3). **Judgement on persistence: nothing but the
consent is stored.** **Judgement on Codex: the passive file read**, because the live read can refresh a login that live
sessions share, and that case was never measured.

**Residuals, named.** A person's `ControlMaster` can make the call a persisting master on Linux or macOS, as today. The
timer makes one ssh login for each host every 10 minutes while a terminal is open, which a VM's ssh log will show. The
list shows an email on screen, so a screenshot shows it (open point O2).

`SECURITY.md`: the section *The extension's one outward ssh call* becomes two calls. It gains the timer, the separate
consent, the road taken for Claude usage and what the VM then calls, and the residuals above.

## Growth surfaces

- **The readings**: in memory, one for each host with an open `Creds:` terminal, a few hundred bytes each, dropped when
  the host's last terminal closes and with the window. Bounded by the number of open hosts; today five.
- **The accounts consent**: one `globalState` list, the session reader's row plus one number, capped at 100 rows; at
  the cap the oldest answer goes and that host is asked again. Under 12 KiB at the cap.
- **The ssh child**: one for each host in a round, killed at 20 s with its tree, stdout capped at 64 KiB. At most one
  alive at a time for each window.
- **The timer**: one for each window, cleared when no `Creds:` terminal of an allowed host is open, disposed with the
  extension. A round that is interrupted by a reload leaves nothing: its child is killed with the extension host's
  tree, and memory is gone.
- **Log lines**: one when a host starts to fail and one when it reads again — not one for each round.
- **On the VM**: our script writes nothing. Road (a) is taken only if step 0 shows the Claude Code command writes no
  session file and leaves no process; if it must write, that road fails. Road (b) writes nothing.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, on roads (a) and (b); none on (c).

## Boundaries with other plans

This plan takes no slice of another plan. It builds on code that shipped. The table says what is whose, so it is read
as complete:

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, the session consent, *take*, *add*, *Asked* | [PLAN_the_chat_opens_from_a_terminal.md](PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0) | This plan widens the runner so a call needs no session operand, and adds a second consent beside the first. It changes no behaviour of the session reader |
| The gauge of coai's own rounds on this machine and on a Team server (`gaugeWords.ts`, the Team server's capture) | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing. That gauge counts coai's rounds; this list shows a person's accounts on other machines. No shared store, no shared reader |
| The account and limits line inside Claude Code | `dew_flow_conventions · mods/agent-models` | Nothing is changed there. This plan carries its word rules over and tests them against its cases |

Order: this plan starts after the terminal-door plan's last release is cut, so the runner is not widened under an open
pull request of that plan.

## Build order

1. The question consultants are asked what is important; the owner answers the open points; this text is revised.
2. **E0 — step 0.** The owner's go for the measurement, the run on one VM, the RESULTS file, the results folded in here.
3. The plan round (`review_plan`) and our own Opus review, at the same time. The first passing verdict opens epoch 1.
4. **E1 — the remote accounts reader.** The stories by Opus; the cornerstone spec by Fable before its code; one PR.
5. **E2 — the round and the list.** The stories by Opus; one PR. E1's code has no door until E2 merges.
6. A pre-release VSIX from the merged head; the owner's real-window checks R1–R8 on it, before the tag, as change 1.1
   of the terminal-door plan did (no real VS Code can start on the build machine); a failed check is fixed first.
7. `review_feature` over the three epics, every finding resolved; an accepted fix that changes the product is a new PR
   and a new pre-release VSIX.
8. The extension release: the tag pushed alone and its run observed; `POST_DEPLOY.md` run; the plan promoted with its
   deviations and its stories documents.

## Test plan

- **The script, run for real** (`npm test`, Linux CI): the exported text run by `sh -s` and by `bash --posix` against a
  temporary `HOME` with false `claude`, `codex`, `agy` and `curl` programs on the `PATH`, as `remoteScriptRuns.test.ts`
  does with `tmux` and `ps`. Cases: each vendor found, absent, signed out; each usage state; each operand; a wrong
  operand; a missing tool; a vendor command that hangs (the time limit ends it); a cut script does nothing.
- **No secret leaves, seen and not assumed.** The temporary `HOME` holds false credential files with a marker token.
  The false programs record their arguments and environment. The test fails if the marker is in any argument, in any
  environment, in the script's stdout or stderr, or in a log line. It runs on every road, so a script that reads a file
  it should not is seen even on road (a). The test is seen RED once with a line that prints the file.
- **The hash**: a test holds the sha256 of the script text; the spec holds the same value.
- **The parser**: a banner before the begin line; every marker and key; an unknown marker, an unknown key, a line too
  long, too many lines, an exit code that is not 0 — each `unreadable` with its reason; a percent of 101, `-1`, `1e3`,
  `NaN`; a reset with no offset; a value with control characters, with `$(`, of 10 KB; a credential-shaped value is
  dropped. A missing number is `null`, and a test fails if any path turns it into 0.
- **The words**: the mod's cases carried over as a table — `5h 87% left`, the dash with no `% left`, `resets in —`,
  `1h 0m`, `<1m`, a reset that has come; Codex windows named by length; the grouping (two hosts with one account are one
  group; two unknown accounts are two groups); each host sentence.
- **The round** (pure, with seams for the clock, the call and the terminals): one call at a time, never two; the start
  moves one host each round; a failed host does not stop the round; usage is asked once for each account in a round and
  moves to the next host when the first fails; no call to a host with no "yes"; no timer with no terminal; a closed
  host is dropped with its reading; the 60 s floor of *Refresh now*; a result that lands after its host closed is dropped.
- **The consent**: a session "yes" does not allow an accounts read; a "no" starts no ssh; Escape writes nothing; a
  lower reach reads as "ask"; the cap of 100; the timer raises no question (a test fails if the ask seam is called from
  a tick).
- **The runner**: the accounts argv is the pinned options, `--`, the host, `sh -s --` and the mode, with no `-n`; a host
  that fails the host class never reaches ssh; the session reader's argv and sentences are unchanged (its tests stay green).
- **The manifest and the help**: the button with `view == terminal` and its group; the command in the help of all five
  languages; `notification-sites.json` generated again with the new questions; the log scan
  (`remoteLogScan.test.ts`'s rule) covers the new modules.
- **Host scenario** (`npm run test:host`): with two terminals of two hosts and the call replaced through the test door,
  the false call is made once for each host, one after the other; with the terminals closed no call follows; the list
  holds the rows of both. U10 is measured here: what a label with `$(` draws.
- **Webview pages are not changed**; if one must be, it is tested by running it (`bundledPage.test.ts`).

## Real-window checks (the owner, on the pre-release VSIX, recorded in this plan when measured)

| # | Check |
|---|---|
| R1 | The icon sits beside *Ask other AI* in the terminal panel's title bar and opens the list. With no `Creds:` terminal the list says so |
| R2 | First open: every host is "not asked yet". Choosing one raises the question that names the host. *Not this host*: no ssh to it, and the list says it is turned off |
| R3 | After *Read*: a round runs, one host after the other. The Claude numbers equal the mod line and the built-in `/usage` on that VM — the same "left" numbers and the same dashes. Every row can be read at the owner's window width |
| R4 | Two hosts that share an account are one group that names both hosts, and its usage was read once in the round |
| R5 | A host that cannot be reached says so in its own sentence, and the other hosts are still read |
| R6 | All `Creds:` terminals closed: no further ssh login on the VM (its ssh log). One opened again: a round follows |
| R7 | Left open for an hour: rounds about 10 minutes apart. On the VM: no new session files, no leftover process, the live Claude and Codex sessions undisturbed |
| R8 | *Take the question* in a `Creds:` terminal still works (the widened runner) |

## Definition of Done

- [ ] `research/RESULTS_accounts_and_usage_on_a_vm.md` records step 0 on one VM, and every row U1–U11 is answered or
      says what was checked and what was not.
- [ ] The icon in the terminal panel's title bar opens a list of the accounts of Claude Code, Codex and Antigravity on
      the hosts of the open `Creds:` terminals, grouped by account, each group naming its hosts.
- [ ] Usage is shown as "left" with the mod's words and its dash; a missing value is never a zero; usage that was not
      read says why.
- [ ] Every host with nothing to show says why in its own sentence: not asked, turned off, never read, or failed.
- [ ] A round runs every 10 minutes, one host at a time, one read-only ssh call for each host with the pinned options,
      only for hosts with a "yes" in the accounts consent; it stops with the last `Creds:` terminal; a failed host does
      not stop it; the timer asks nobody.
- [ ] The script is fixed, its hash is in the spec and in a test, and it writes nothing on the VM. No secret is in an
      argument, the environment, the reply or a log — shown by the marker-token test.
- [ ] The Claude usage road is the one this plan's order gives after step 0; road (b) exists only with the owner's
      recorded yes.
- [ ] Nothing of a reading is persisted. The consent is separate from the session reader's and carries its reach.
- [ ] The session reader behaves as before (its tests green, R8).
- [ ] Docs with the change: the help in five languages, `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md` (the new edge from a VM to a vendor), `SECURITY.md`; the `POST_DEPLOY.md` question asked.
- [ ] R1–R8 recorded here; `review_feature` run before the tag and every finding resolved; one extension release cut and
      verified; the plan promoted with its deviations.

## Open points for the owner

Each goes to the question consultants first, and reaches the owner with their answers beside it.

| # | The question | Recommended answer |
|---|---|---|
| O1 | **Claude usage.** Allow the script to make the VM ask `api.anthropic.com` with the VM's Claude login, returning only the percentages? By which road: (a) Claude Code itself makes the call; (b) our script reads the login and calls with `curl`; (c) the account only | **(a), yes** — if step 0 shows a command that prints the usage with no model call, nothing started and nothing written. If (a) fails: **(c)**, not (b). (b) only on an explicit yes after the Security section's paragraph on it |
| O2 | May the list show the full email of each account? | **Yes**, in the list only. It is never logged, stored or sent on. It is the point of the list: to tell the accounts apart |
| O3 | The consent: a separate question for each host for the accounts read, independent of the session reader's answer, raised only by a press; and one question for all open hosts at once is allowed? | **Yes** to both. A timer that reads accounts is not what the first question asked |
| O4 | The interval: a fixed 10 minutes, or a setting? | **Fixed.** A setting costs a help entry in five languages and a control, and nobody asked for another value |
| O5 | Codex usage is as old as the last Codex turn on that host (a local file, no network). Is that enough, or is the live read wanted? | **Enough.** The live read writes into the Codex home and can refresh a login that live sessions share |
| O6 | The icon: always in the title bar, or only while a `Creds:` terminal is open? | **Always**, like the chat icon |
| O7 | Which VM and which Linux user is step 0 run on, and when? | The owner's choice; a host where all three CLIs are signed in |

## Epics

Three epics in the build order above. E0 has no code. E1 and E2 are one PR each and ship in ONE extension release.
Stories documents are not written now: one for each epic, `todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`,
written by Opus when that epic starts (`subagent-models.md`, pipeline step 3). The stories named below are this plan's
proposal for that writer. Cornerstone quota: Q = ⌈2·E/5⌉ = ⌈2·3/5⌉ = 2.

*The split is a draft until the question consultants were asked what is important* (`subagent-models.md`, pipeline
step 2). The areas Fable names as cornerstones: **(a)** what the script touches on the VM and what it prints — the only
place a secret can leak, and a leak gives no error; **(b)** the reply's frame and the parser — the only place wrong or
hostile data can arrive and look right; **(c)** who is contacted and when — the consent with its reach, and a timer
that never asks; **(d)** absent, signed out, not measured, off, failed and unreadable are different results, and a
missing number is never a zero.

Cornerstones: 1 of 2 — E1.S2

### E0 — Step 0, the measurement · depends on the owner's go

- **Goal.** Know, from one real VM, what each CLI stores and prints, what can be read with no secret, which Claude usage
  road holds, and what one read costs in time and bytes.
- **Scope.** No product code. The E0 stories document with the draft script and the single commands (M1–M8), each with
  its expectation; the run with the owner present; `research/RESULTS_accounts_and_usage_on_a_vm.md` and its row in
  `research/README.md`; this plan revised with the results; the owner's answers to O1–O7 recorded here.
- **Depends on.** The owner's go, and a VM chosen (O7).
- **Done when.** Every row U1–U11 is answered or says what was checked and what was not; the Claude usage road is
  chosen by the order (a), (b), (c) with the six conditions of (a) each measured; the reply's fields and the limits are
  fixed for the spec; the RESULTS file is committed before the plan round.
- **Cornerstone areas.** None: it is a measurement. Its draft script is thrown away; the spec writes the real one.
- **Stories.** E0.S1 the draft and the commands; E0.S2 the run and the RESULTS file; E0.S3 the plan revised and the open
  points answered.

### E1 — The remote accounts reader · depends on E0

- **Goal.** One call that reads the accounts and usage of one host over ssh and gives a typed result, with nothing
  that calls it yet.
- **Scope.** `src/remoteSsh.ts` (the runner widened: one argv builder and one run for both callers, the sentence's
  subject and the limits as parameters, `SSH_OPTIONS` untouched); `src/remoteFrame.ts` (the framing extracted for two
  callers); NEW pure modules for the accounts script, its frame and typed result, and the mode operand; the consent's
  second store and its reach over the functions of `src/remoteRead.ts`; *CoAI: hosts read over ssh* listing both kinds
  (`src/remoteSessionHost.ts`); their tests; `research/module_extension.md`, `research/module_tests.md`.
- **Depends on.** E0's RESULTS file and the owner's answer to O1 and O3. The terminal-door plan's last release cut.
- **Done when.** The script, parser, runner and consent rows of the test plan are green; the marker-token test was seen
  RED once; the hash is in the spec and in a test; the session reader's tests are unchanged and green; the code gate
  passed; the PR is merged.
- **Cornerstone areas.** (a) and (b) together — the cornerstone; (c) the consent's reach; (d) the result's arms.
- **Stories.**
  - **E1.S1 — the runner serves two callers.** `runRemote` keeps its checks and its words; a shared run and argv
    builder under it; the accounts call beside it. By Opus's brief, coded by Sonnet.
  - **E1.S2 — the accounts script, its reply and its parser (CORNERSTONE, `model: fable`).** Fable writes the spec
    before any code: the exact script text and its sha256, the tools it needs, the reply's grammar and limits, the typed
    result with one arm for each state, the parser's rules, the scenarios (the test plan's script, marker-token, hash
    and parser rows), and the security judgement on the exact text — outside the quota, by the security row. Sonnet
    codes; Opus reviews.
  - **E1.S3 — the accounts consent.** The second store, the reach, the question's text, both kinds in the hosts list.

### E2 — The round and the list · depends on E1

- **Goal.** The icon, the list grouped by account, and the 10-minute round that fills it.
- **Scope.** NEW pure modules for the round (the hosts, the order, the mode for each host, the table in memory, start
  and stop) and for the words and rows (the mod's rules, the grouping, the host sentences, the plain-text road for VM
  text); a NEW host module with the `vscode` seams (the timer, the terminal events, the QuickPick with its title
  button, the two questions, the test door) bound in `src/extension.ts`; `package.json` (the command, its icon, the
  `view/title` entry); the help in five languages; `notification-sites.json`; the host scenario;
  `research/module_extension.md`, `research/module_tests.md`, `research/architecture.md`, `SECURITY.md`; the
  `POST_DEPLOY.md` question (the file is at its cap of twelve, so a new item needs a named one to leave).
- **Depends on.** E1 merged.
- **Done when.** The words, round, manifest, help and host-scenario rows of the test plan are green; the Definition of
  Done's items hold; the PR is merged; R1–R8 are recorded on the pre-release VSIX of the merged head; `review_feature`
  ran from the merged head and every finding is resolved; the release is cut and verified; the plan is promoted.
- **Cornerstone areas.** (c) the timer never asks and contacts only allowed hosts; (d) the sentences. No cornerstone
  story is recommended: the rules are pure, small and fixed by this plan. One quota place stays free.
- **Stories.**
  - **E2.S1 — the round.** The pure planner and its table in memory.
  - **E2.S2 — the words and the rows.** The mod's rules, the grouping by account, the host sentences.
  - **E2.S3 — the host and the door.** The timer and the terminal events, the list, *Refresh now*, the questions, the
    command and its icon.
  - **E2.S4 — the documents and the release.** The help, the module documents, `SECURITY.md`, the pre-release VSIX,
    R1–R8, `review_feature`, the tag, the promotion.
