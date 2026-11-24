# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **plan only, nothing implemented yet, 2026-11-19.** The plan gate ran on `653140c3` on 2026-11-19 (session
> `bce68565`): verdict `proceed`, with ONE of two reviewers — codex answered; antigravity did not receive the plan
> whole, at 154 KB. Its 7 findings were accepted and are folded into this text. So are the reply's commands and the
> answer of both question consultants on the split (consult `f4af076c`): at most two epics, and the installer only
> after the read-only path is proven. Scope: `src_vs_code` only — an icon in the terminal panel's title bar, a list of
> the Claude Code, Codex and Antigravity accounts on the hosts of the open `Creds: <host> / <session>` terminals with
> their usage, and a round over those hosts every 10 minutes that starts by itself. **Epics E1 and E2 only read.** The
> install of `jq` (owner decisions 17, 19 and 20) stands and ships in the same release; it is built after E2, as
> candidate C1. No change to `src_mcp`, `src_server` or CredsForDevs. Step 0 and the measurements W1–W5 are done
> ([../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md)). The owner
> answered O1–O7, N1–N5, Q1–Q2 and decision 20; no question for the owner was open before the stop below. This text was cut from 154 KB:
> the decision history, the findings F1 and F2 in full, the step 0 tables and the installer's design are in
> [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md),
> word for word where they were moved. **Epoch 2 open, approved by the owner 2026-11-24, no E1 code yet. Before it: STOPPED at 1.2; re-review `4082dc2c` `proceed` (5 findings folded into the
> revision); the owner approved epoch 2.** Epoch 1 is closed: its first change was 1.1
> (K5 of E1, gate G2), and its second, 1.2, moves the person's refusals and the Stop out of `globalState` into a file
> that is merged under a lock. The text of § 3, § *Security*, § *Growth surfaces*, the residuals, R12 and C4 below is
> REVISED to entry 1.2, and with them the lines of § 2, the test plan, the Definition of Done, R8 and the scope of E1
> that follow from it. **The re-review ran on 2026-11-19** (coai session `4082dc2c`, on `beab3c65`): `proceed`, with
> ONE of two reviewers — codex answered; antigravity again did not receive the plan whole, at 115 KB. Its five
> findings were accepted and are folded into this text. They are the baseline of the next epoch and not ledger
> entries; *Re-review for epoch 2*, below, says where each went. The revised text is not approved: no code of any
> epic of this plan is written, and no pull request that carries its work is merged, until the owner has approved
> epoch 2.

Related: [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md) (step 0
and W1–W5, measured 2026-11-19), [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md)
(its E2 built the read-only ssh reader and the per-host consent this plan reuses),
[../research/RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md) (where each vendor states its
quota gauge, measured 2026-10-10),
[../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) (the gauge of coai's own rounds).

## Plan changes

> Epoch 1 · approved 2026-11-19 · by reviewers · state: closed

  The gate's round 1 on `653140c3`: `proceed`, codex only; antigravity did not receive the plan whole. Its 7
  findings, the reply's commands and the consultants' answer on the split are folded into this approved text. They
  are not entries.

- 1.1 · 2026-11-19 · reviewer · approach — a Forget given in another window does not lift a refusal while the window that gave it is open: that window cannot tell a Forget from a lost "no", so it writes the refusal again and says so (K5 of E1, gate G2)
  (J2, W3, W9, X9 and the run condition of R8 share this one cause. The first change of epoch 1: one more stops the plan.)
- 1.2 · 2026-11-19 · reviewer · contract, approach — a write of ANY `globalState` key by a window with a stale copy puts back that window's whole copy, so it can erase a "no" or the Stop for good: both move to a small file under `globalStorageUri`, merged under an exclusive-create lock, and only a person's press removes one
  (This second change stopped the plan. The finding is the E1 plan gate's, coai session `a4a016fb`: "S1's
  read-merge-write does not make refusals atomic across windows". The planner first rejected it, citing the repair
  loop, C4 and R12. The cadence consultation `e319fe2e` showed that the rejection does not hold: if the window that
  gave a "no" is disposed before it repairs, a stale window's automatic write erases the refusal for good, and the
  Stop has the same race with no repair at all. The cause was then read in this repository's own record:
  `research/PLAN_auto_never_asks_a_person.md:683-686` and V1–V3 of
  `research/PLAN_auto_never_asks_a_person_E2_S2_spec.md:119-121`. Splitting the keys does not help, because the
  whole object is what a window sends.)
  (Entry 1.1 is SUPERSEDED by this entry, and it stays in the ledger as it was written. With the refusals in a merged
  file no window writes a lost "no" again, so the repair of 1.1, its notice and its run condition of R8 are gone, and
  a *Forget* given in any window of the side that holds the refusal lifts it. What is new instead: the file is kept
  for each SIDE of the machine, § 3.)
  (What C4 was: this entry takes its first half before E2, not after a failed R12. § *Candidates after E2* says what
  is left of it.)

### Re-review for epoch 2

Narrowed: no — nothing left the scope, the Definition of Done, the merge conditions or the release; the revision adds to each

| Work | Where | Verdict | Action |
|---|---|---|---|
| Product code of any epic of this plan | none was written | — | nothing was built, so nothing is sorted or cut |

  (The re-review of the revised plan: coai session `4082dc2c` on `beab3c65`, 2026-11-19, verdict `proceed`. Codex
  answered; antigravity did not receive the 115 KB text whole, so one of two reviewers read it. All five findings
  were accepted. By `plan-change-control.md`, *The full stop*, step 4, what a re-review changes is the baseline the
  next epoch is approved on and never an entry: none of the five is one, and epoch 2 starts at no changes.)
  (Finding 0, security — a refusal whose write failed was known to one window only. Now the copy in `globalState` is
  written at once on every path, and a marker that every window of the side reads stops their automatic calls:
  § 3, *A refusal the file did not take* and *The unsure marker*; residual 5 of the file; the test plan.)
  (Finding 1, security — a "no" given in a window of an older build after the one-time migration never reached the
  file. Now every read copies the "no" rows of `globalState` into the file: § 3, *The copy-in*; residuals 2, 4
  and 8; candidate C4b.)
  (Finding 2, reliability — the pre-release would have run the usage command before W6 and W7. Now E1 and the first
  pre-release ship with the Claude usage read off, and a second build turns it on after both gates: § 2, § 5,
  *Build order*, E2 with R2, R3, R4, R7 and R9.)
  (Finding 3, reliability — a lock broken by its age let a former holder rename a stale file over a newer refusal.
  Now a lock is taken over only when its owner is shown to be gone or by a person's press, and the file carries
  `gen`: § 3, *The lock is never taken over by its age* and *A file that went back*; residual 5; the test plan.)
  (Finding 4, reliability — a host whose account changed could read a second time the usage of an account already
  read. Now every host's first call reads no usage, and the one usage call of an account follows the reply: § 2;
  the test plan; R2.)
  (The commits of epoch 1 are this plan's own documents — the stories document of E1, its two specs and the record
  of gate G2 — and the revision rewrote them in place. No code exists for a judge to sort.)
  (Approved by the owner on 2026-11-24, as revised, with one answer on the per-side question: a second side of the
  machine (a WSL window beside the Windows one) is used for the chat of a `Creds:` terminal only, not for the
  accounts list. So the stated justification of candidate C4b is not met, and C4b stays a candidate.)

> Epoch 2 · approved 2026-11-24 · by owner · state: open

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

Each decision is one line here. The owner's words, the planner's recommendation beside each answer, and the questions
as they were asked are in the appendix, § A, word for word. Where the owner decided against the planner's
recommendation, the plan follows the owner.

1. An icon beside the chat icon in the terminal panel's title bar opens the list.
2. No real time: a round over ssh every 10 minutes.
3. Claude usage: Claude Code itself makes the call on the VM, so our script never touches the token.
4. Several hosts may share one account: the list groups by account, and usage is read once for each account in a round.
5. The same numbers as the agent-models mod line: "left", a dash for a missing value, never a zero.
6. **O1:** the `curl` road (b) is allowed as a fallback. Road (a) works, so (b) is not built.
7. **O2:** full email addresses are shown, in the list only.
8. **O3:** no separate consent: the session reader's per-host answer is the answer for the accounts read.
9. **O4:** the interval is a fixed 10 minutes.
10. **O5:** the passive Codex read is enough.
11. **O6:** the icon is always visible.
12. **O7:** step 0 ran on the host `ai2-root`, Linux user `root`, on 2026-11-19.
13. **N1, "everything is acceptable":** the four cache files Claude Code writes on a read are accepted, and so is a
    rewrite of `~/.claude.json`, even on every read. The answer is about writes. It does not cover a live session
    that is disturbed, and — the gate's finding 4 — it does not cover files that grow without bound.
14. **N2:** the round runs by itself over every distinct host of the open `Creds:` terminals, from the start of the
    extension. No first-press switch.
15. **N3:** answered by the run of W1–W5.
16. **N4, "with the version":** `agy --version` runs in every round.
17. **N5:** where `jq` is missing it is installed, when the ssh user may; where the user may not, the list shows the
    exact command to run and runs nothing.
18. **Q1, "new hosts are allowed by themselves":** a host with no answer is read with no modal and no press; a daily
    notice names such hosts; a "no" is never overridden.
19. **Q2, "install at once":** the install needs no press, also on an old "yes".
20. **"Yes, but on by default":** two user-level settings, both on by default —
    `coai.terminalAccountsAllowNewHosts` and `coai.terminalAccountsInstallMissingTools`. Off, they restore ask-first
    and no-install.

**How decisions 17, 19 and 20 stand after the re-cut.** They are not reopened. The installer is not in E1 or E2: it
is candidate C1 of the *Epics* section, built after E2 and shipped in the same release. In E1 and E2 a host without
`jq` shows the fixed command to run. The second setting arrives with the installer, because a setting with no code
behind it is a control that does nothing.

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

**Background that was moved.** How the agent-models mod reads the usage, what coai measured about Codex and agy on
2026-10-10, and the first look at `claude --help` on this machine are in the appendix, § F. They matter for road (b)
and for the history; the design below stands on step 0.

### What step 0 and W1–W5 measured — 2026-11-19, on `ai2-root`

One host, one Linux user (`root`, Ubuntu), one account for each vendor, Claude Code 2.1.326, codex-cli 0.162.1, agy
1.5.12. The record is the RESULTS file; the plan's own tables of it (U1–U11, M1–M8, the six conditions of road (a),
W1–W5) are in the appendix, § D. What the design uses:

- `sh` is `dash`. `claude` and `agy` are in `~/.local/bin`, which a shell that is not a login shell does not have on
  its `PATH`. `jq`, `timeout` and `stat` are in `/usr/bin`.
- `claude auth status --json` names the account in 0.3 s with no secret, and writes no file (W3).
- `claude -p "/usage" --output-format json --no-session-persistence --strict-mcp-config --setting-sources ""` prints
  the usage in 3.6 s with **no model call**, also from the working folder `/` (W1). It writes no transcript. It
  rewrites four cache files of Claude Code's own, with no growth (W4).
- **Not settled:** during one read of four, `~/.claude.json` was rewritten with a new backup file, and it is not
  proven whether the read or a live session wrote it (W6). One sample showed no child process of `claude`; one young
  `node` process of unknown origin was seen after a read (W7).
- Codex: the last `token_count` record of a session file holds the limits; `primary` was the WEEKLY window. The record
  sat 991 bytes from the end of its file (W5). `codex login status` names no account.
- agy has no usage, quota or account command. `agy --version` printed 1.5.12.
- **Still open:** U10, whether a QuickPick label draws `$(name)` as an icon — E1's host scenario measures it. **New,
  from the gate's finding 0:** U12, how soon one VS Code window sees a `globalState` write of another — E2 measures
  it (R12).

## The design

### 1. The view

- **A new command `coai.terminalAccounts`**, title "AI accounts and usage", icon `$(account)`. In `menus.view/title`:
  `when: view == terminal`, `group: navigation@91`, so it sits right after *Ask other AI* (`navigation@90`). It is in the
  command palette. It has no chord, so the skip-shell list does not change.
- **The icon is always there**, like the chat icon (O6). With no `Creds:` terminal open the list says one sentence: *"No
  `Creds: <host> / <session>` terminal is open, so there is no host to read."*
- **The view is a QuickPick instance (`createQuickPick`), not a webview.** It is a short read-only list, and it needs
  what `conversationPickerCommand.ts` already has: title buttons and new rows without closing. A webview is a new
  bundled page with its own CSP, nonce and a test that runs the page, for colour and layout nobody asked for. If R3
  shows that the rows cannot be read, a webview is candidate C2. (The four reasons in full: the appendix, § H.)
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
  ai2-dev2 — allowed automatically on 2026-11-20. Choose this row to change that.
  ai2-root — turned off: your answer for this host is “Not this host”.
  ai2-dev3 — jq is missing on this host. Run there: sudo apt-get install -y jq     [copy]
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
  - **Every host has its own sentence in the *Hosts* part**: turned off; *not asked yet*, with a press to ask, where
    new hosts are not allowed by themselves (the setting of § 3 is off); never read yet; the last read failed (with
    the runner's own sentence and its age); and, for a host the round allowed by itself, *allowed automatically on
    <day>* — that one is shown also when the host has a reading. A host whose last read failed keeps its older numbers
    in its account group, and the group's second line says the later read failed.
  - **Usage that was not read says why**, in its own words: *usage not read: Claude Code 2.1.326 on this host printed
    no usage line this version understands*, *agy states no usage*. It is never a row of dashes with no reason.
  - **A host without `jq` says so, with the exact command to run there** (§ 4). The command is a fixed string from
    a closed table, chosen by the package manager the host has. The row has a copy button (a QuickPick item button)
    that puts that string on the clipboard. The extension never runs it, and in E1 and E2 it installs nothing.
  - **Choosing a host's row** in the *Hosts* part opens that host's three choices — *Read*, *Not this host*, *Forget*
    — as *CoAI: hosts read over ssh* does, so a host can be forbidden from where it is seen (§ 3). Choosing any other row
    does nothing.
  - **A setting that a workspace or a folder tried to set** is said in one row at the top of the list: *This
    workspace sets `coai.terminalAccountsAllowNewHosts`. It is ignored: only your own user settings count.* (§ 3).
  - **Two title buttons**: *Refresh now*, and *Stop the accounts round* (§ 3). While the round is stopped the list
    holds one row: *The accounts round is stopped. Choose this row to start it.*
- **Text from a VM is made plain at one road in**: control and format characters are taken out, the length is cut, and
  `$(` is broken so the list never draws an icon a VM chose (U10). The existing pick list of the session reader passes
  VM text with `$(` intact (`src/remoteRead.ts:213`, `:220-226`). This plan does not change it; whether it should use
  the same function is a question for the owner after U10 is measured.

### 2. The round

- **Which hosts — every distinct one that is not refused, by itself (N2, Q1).** The distinct hosts of the live (not
  exited) terminals whose name gives `kind: 'session'`, read through `liveTerminals`. Several terminals of one host
  are one read. A host whose answer is "no" is refused and is never contacted. A host with a "yes" is read. **A host
  with no answer is allowed by the round itself**: the round writes its "yes", marked as automatic, and reads it in
  the same round (§ 3) — while the setting `coai.terminalAccountsAllowNewHosts` is on, as it is by default. With it
  off, such a host is not contacted and waits for a press. In this plan "an allowed host" is a host with a "yes", however it was written.
- **One host at a time.** A round takes the hosts in name order, starting one host later each round, and awaits each read
  before the next. Two reads never run at once. A failed host is recorded with its sentence and the round goes on.
- **The answer is read again just before every call**, from the refusals file on the disk and from the store (§ 3,
  entry 1.2), as the session reader checks the terminal's
  name again before every spawn. A "no" given while a round runs stops the next call to that host. A *Forget* does not
  stop anything any more: the host then has no answer, and the round allows it again (§ 3).
- **One ssh call for each host in a round, and a second for the one host that reads an account's usage (below)**:
  the pinned options of `SSH_OPTIONS`, unchanged, then `-- <host> sh -s -- <mode>`, the script on stdin. `<mode>` is
  one of two fixed words (below).
- **Every 10 minutes, counted from the END of a round** to the start of the next, so two rounds never overlap. The
  value is a constant, `600 000 ms`, not a setting (O4).
- **Usage once for each account in a round — the account first** (decision 4; revised at the re-review, finding 4).
  Every host's FIRST call in a round has the mode `no-claude-usage`: the script reads the accounts and no Claude
  usage. Its reply names the Claude account the host has NOW. Only then does the round decide. When that account has
  no usage reading in this round, the policy allows one (below) and the host is not blocked (below), the round calls
  that host ONCE more, with `claude-usage`, before it goes on to the next host. So the one usage call of an account
  goes to the first host of the round that answers with that account; each account gets at most one usage read in a
  round, and each host at most two calls. If an account's usage read fails on that host, the next host of the
  account reads it in the same round. Codex usage is a local file read with no cost, so it is read on every host, in
  its first call.
- **Why the account comes first (the gate's finding 3, then finding 4 of the re-review).** The text before chose a
  host's mode from the account it had at its LAST read. A host whose account had changed to one already read in
  this round then read that account's usage a second time. Nothing remembered chooses the mode now: this round's own
  reply does. The price is one more login in each round, on one host of each Claude account. An earlier revision
  named "a second ssh call for a host in a round" as outside the plan; the gate's finding 3 brought it in for one
  case, and it is now how every usage read is made. One case is left and is named: an account that changes on a host
  BETWEEN its two calls, seconds apart, can be an account already read in this round. That reading is kept, and no
  third call is made.
- **How often the usage command runs follows from W6 (the gate's finding 4).** The owner accepted what a read writes
  (N1); a file for each read that nobody retires is growth, which N1 was not asked about. The accounts are read
  every 10 minutes in any case. The usage command runs by this table:

  | What W6 shows about `~/.claude/backups/` | The usage command runs |
  |---|---|
  | A read makes no backup (a live session made the one that was seen), or one only at a host's first read from `/` | In every round |
  | A read makes a backup each time, and Claude Code keeps a fixed number of them | In every round; that number is the bound, and `SECURITY.md` states it |
  | A read makes a backup each time, and nothing retires them | **Not by a timer in every round.** At most once an hour for each account, and only under a budget the owner approves, in files and megabytes a day (§ Growth has the numbers). Until the owner approves one: only on a press of *Refresh now* |

  The policy is one constant of the round's planner. It has four values: the three of this table, and `off`.
  **E1 ships it `off`, and so does the pre-release build that E2 starts with (revised at the re-review, finding 2).**
  With `off` no call has the mode `claude-usage`, on a timer or on a press: the list shows the Claude account and
  says that its usage is not read. The account, Codex and agy reads are on from that first build. The Claude usage
  read is turned on only in a NEW build, made after W6 and W7 both have recorded results (§ 5), and that build's
  policy is the row W6 selected — or still `off`, when W7 failed. The text before had the pre-release read in every
  round, which ran the usage command by a timer before either gate.
- **A usage read that went wrong is not repeated blindly.** When a host's reply says the usage command made a model
  call (`:turns` is not 0) or printed no line the pattern knows, that host gets no usage call from then on — its
  first call only — until the window is reloaded or the host reports another Claude Code version. So a Claude Code
  version that sends `/usage` to the model costs one small call for each window, not six an hour.
- **The first round starts with the extension, with no press** (N2): 30 s after activation, when the window has a
  `Creds:` terminal of a host that is not refused; else 30 s after the first such terminal opens. The 30 s let the
  window restore its terminals first. It is not started by a reload alone with no such host.
- **No install step in E1 and E2.** A host whose reply says `jq` is missing shows the command to run (§ 4). The
  installer is candidate C1.
- **It stops when no `Creds:` terminal is open.** With no live terminal of a host that is not refused the timer is cleared, not
  left idle. `onDidOpenTerminal` arms it again; `onDidCloseTerminal` checks again. A host whose last terminal closed is
  dropped from the next round, and its reading is dropped from memory. A read in flight is not killed by a close; its
  result is dropped.
- **The timer never asks a person.** It does not need to: it allows a new host by itself (Q1). The shared question is
  raised only by a press on one of the session reader's doors, for a host that has no row yet.
- ***Refresh now*** is a title button of the list. It starts a round at once, unless one is running (the list shows it is
  busy) or the last round ended less than 60 s ago (the mod's own floor). Opening the list draws what memory holds; it
  starts a round only when a host that is not refused has no reading yet.
- **Results are in memory only.** Nothing of a reading is written to disk, to `globalState`, to a ledger or to a log:
  the readings hold email addresses and the usage of a person's accounts, they are 10 minutes old at most, and after a
  reload the next round brings them back within a minute. What is persisted is the shared consent, which exists
  today, one small row of state, and — since entry 1.2 — the refusals file of each side with its markers (§ 3).
  None of them holds anything of a reading.
- **Limits of one read call**: killed at 30 s (the tree, as today), stdout capped at 64 KiB. Both are ceilings. The
  ceiling was 20 s before `agy` was started (N4). Measured on the VM: the account command 0.3 s, the usage command
  3.6 s with the flags (6.3 s without), whole calls between 3 and 10 s. Inside the script each command has its own
  limit: `claude --version` 2 s, the account command 3 s, the usage command 8 s, `agy --version` 4 s (§ 4); with ssh's
  5 s to connect that stays under 30 s. The time of `agy --version` and the time of a whole call from Windows with the
  pinned options were not measured; R9 measures both. A round over five hosts is at most ten calls, 300 s at the
  ceiling, when each host has an account of its own; with one account on all five it is six calls. With the policy
  `off` it is five.
- **A failed read is logged once, not every round.** The runner's one log line is written when a host's failure
  sentence changes, and once when it reads again. A host that is down for a day is two lines, not 144.
- **Not serialized with the session reader.** A press of *take the question* can run while the round reads the same
  host: two ssh children to one host, both read-only. Each VS Code window runs its own round, so two windows with
  terminals of one host read it twice in 10 minutes. Both are named, not prevented.

### 3. The consent — one shared store, a "no" that always wins, and a new host allowed by itself (O3, N2, Q1)

**This section is revised at the stop (ledger entry 1.2, 2026-11-19).** The paragraphs that changed say so. They
are the baseline for the re-review, not approved text.

**What the store is from this release (revised, entry 1.2).** `coai.terminalHosts` held the answers to a question,
and no ssh child started before a "yes". From this release there are two places, and each holds what it can keep:

| What | Where it is kept | What losing it would do |
|---|---|---|
| **The hosts the person refused** ("no"), **and the Stop** of the accounts round | **A file of this extension, on the disk of the side that runs it**, written under a lock (*The refusals file*, below). `globalState` holds a copy of each | It would contact a host the person forbade, or start a round the person stopped. So it is not left to `globalState` |
| The hosts that are read: a person's "yes", and the automatic "yes" with its marker | `globalState`, the key `coai.terminalHosts`, as today | A new automatic allow, or a new question. Neither contacts a host the person forbade |
| The times of the two notices | `globalState`, the row `coai.terminalAccountsState` | A notice is shown once more |

For the accounts read only a "no" stops a host.

| The host's row | What the round does |
|---|---|
| "yes", written by a press (*Read*) | Reads it, from the first round after the extension starts. No press, no question |
| none — a new host, or a host after *Forget* | **Allows it by itself**, while `coai.terminalAccountsAllowNewHosts` is on (the default): writes a "yes" row marked as automatic, with the time, and reads the host in the same round. **With the setting off:** nothing; the host says *not asked yet*, and a press on its row asks |
| "no" (*Not this host*) — in the refusals file, or as a copy in `globalState` | Nothing. No ssh call of any kind. **Never overridden**: not by the round, not by another terminal or another window, not by the cap, not by a reload, and not by any write of `globalState` |

**The row.** `{ host, answer, askedUtc }` as today; a row the round wrote has `how: 'auto'`. `consentOf` stays the
one function that says whether a host may be contacted, so a "no" is looked up in one place. It is given the file
and the list, and **a "no" in either is a "no"**. In `globalState` a host has one row, so a "no" and an automatic
"yes" cannot both exist there; and the person's three choices act on an automatic row as on any other. From this
release a "no" row in `globalState` is the COPY of a refusal; the refusal itself is the file's. The appendix, § G,
has the longer argument for one list of rows; it was written before entry 1.2.

**Why `globalState` cannot keep a refusal (entry 1.2).** VS Code keeps ONE value for each extension, not one for each
key. `Memento.update` changes the window's own copy and then sends the extension's WHOLE `globalState` object as that
window holds it; another window replaces its whole copy when the change reaches it. (Read in VS Code's source on
2026-11-17 for another plan of this repository, and verified there:
`research/PLAN_auto_never_asks_a_person_E2_S2_spec.md`, V1–V3. It is not pinned to the editor a person runs.) How
long a change takes to reach another window was measured only as an upper bound, 20 s and 23 s
(`research/RESULTS_side_registry_reaches_the_other_window.md`). So, inside that lag, a write of ANY key by ANY
feature of coai in a window that has not yet received a "no" puts the old list back, and the "no" is gone with no
sign. Three things do not cure it. A re-read at the write (rule 1 below) reads the window's own copy. A repair by
the window that gave the "no" — the first text of this section, and entry 1.1 — works only while that window lives.
A key of its own does not help: the whole object is sent. The Stop had the same race and no repair at all.

**The refusals file (entry 1.2).**

- **Where.** `<globalStorageUri>/terminal-hosts/refused.json`, with its lock `refused.lock` beside it.
  `globalStorageUri` is a folder on the disk of the extension host that is running (`src/coaiInstall.ts:303-309`): a
  Windows window and a WSL window of one profile have two such folders. So **the file is kept for each SIDE of the
  machine** (*What a side means*, below).
- **The format.** Versioned JSON, UTF-8, one object:
  `{ "v": 1, "gen": <n>, "by": "<token>", "migratedUtc": "<UTC>", "refused": [ { "host": "<host>", "utc": "<UTC>" } ], "stop": { "utc": "<UTC>" } }`.
  `stop` is absent while the round is not stopped. Every time is UTC. `gen` is a whole number that grows by one with
  every write, and `by` names the write that made this file (*A file that went back*, below). `migratedUtc` is the
  instant this side first took the "no" rows of `globalState`; it is a record and gates nothing. A key this build
  does not know is kept through a write and not read.
- **The size.** At most 100 refusals — the cap of the list — and at most 64 KiB. A full file is about 8 KB with host
  names of 30 characters. A write that would pass either cap is not made, and the person is told that the answer was
  not kept. A file of more than 64 KiB is not read: it is damaged.
- **One function writes it, and it takes a CHANGE, never a content**: refuse a host, lift a host, stop, start, the
  copy-in, and the writing back of a file that went back. It (1) claims the file by an exclusive create of
  `refused.lock` — the pattern of `src/chatStoreLock.ts`, reused and not copied; (2) reads the file; (3) applies the
  change to what it just read, with `gen` one higher and its own token as `by`; (4) writes beside the file and
  renames over it (`src/atomicFile.ts`); (5) lets go of the claim only when the lock still reads as its own;
  (6) reads the file once more: when `gen` and `by` are not the ones it wrote, another writer wrote since, and it
  looks for its own change in the file as it now is — and when that is missing it applies the change again to THAT
  file, three times at most. A merge only adds refusals, and only a person's *Read* or *Forget* takes one out, so
  a second merge cannot lose a refusal.
- **The lock is never taken over by its age (revised at the re-review, finding 3).** The lock's note names its
  owner: the process number, the machine, and — where the platform has an exact one — the id of the machine's
  present boot. A lock that is held is waited on. It is taken over without a person in ONE case: the owner is shown
  to be gone on this same machine — the boot id differs, or the system says that no process has that number
  (`process.kill(pid, 0)`). A note that cannot be read, another machine's lock, a process that lives, or any other
  answer is NOT "gone". Then only a person's press takes the lock over: *Save now*, on the notice of a write that
  did not land and on the row of the list that says so. The text before broke a lock that was 30 s old. A holder
  that had only slept then woke up and renamed its stale file over a refusal given meanwhile. `src/chatStoreLock.ts`
  keeps its 30 s for the chat store; its claim gains a rule for when a held lock may be taken, and this file passes
  the rule above.
- **A file that went back.** After a person's take-over the former holder may still be alive and may still rename.
  So every window remembers the newest file it has read or written: its `gen`, its `by`, its refusals and its Stop.
  At every read, a file whose `gen` is LOWER than the remembered one, a file of the SAME `gen` with another `by`,
  and a file that is gone where one was read all mean that a writer wrote over a newer file. The window then
  writes back the union — the refusals of both and the Stop of either — before the decision answers, and counts
  the remembered refusals as in force until that write lands. It only adds. A *Read* or a *Forget* that was written
  over comes back as a refusal; that errs to not contacting, and the list shows it.
- **A read takes no claim.** A rename is atomic, so a reader sees the old file or the new one, never half. The round
  reads the file from the disk just before EVERY call, and the session door before every read. No window answers
  from a copy it kept, so a window cannot be stale about a refusal of its own side. (What a window remembers of the
  newest file is used for one thing only: to ADD back what a stale writer removed — *A file that went back*, below.)
- **Who removes one.** A refusal is removed only by a person's press — *Read*, or *Forget*, in the hosts list, in a
  host's row of the accounts list or in the question — written to the file. The Stop is removed only by a person's
  start. Nothing automatic removes or replaces either: not the round, not the cap, not the copy-in, not a reload.
- **A file that is damaged fails closed.** Text that is not JSON, a `v` this build does not know, a key of the wrong
  shape, more than 64 KiB, or a file that cannot be opened: from this side no host is contacted and no host is
  allowed by itself. A host whose copy in `globalState` is a "no" says *turned off*; every other host says that the
  file cannot be read. Nothing automatic writes over it. **The person mends it by an answer for one host in the
  change question that exists** — no notice and no question of its own, as gate G2 fitted for a list that cannot be
  read. That answer writes a new file: the refusals that `globalState` still holds, those of the newest file this
  window remembers, those of the markers that can be read, the answer itself, and **the round stopped**, because the
  damaged file may have held the Stop. The question says how many refusals can still be read, and that a refusal
  kept only in the file may be lost. The person then starts the round.
- **A refusal the file did not take (revised at the re-review, finding 0).** The claim is still held after its
  wait, the disk refuses, or the change is not there after three tries. The answer is never reported as kept. For a
  "no" or a Stop, three things hold the person's word meanwhile. (1) **The copy in `globalState` was written
  already**: it is written at once, in the step of the press and before the file's write is awaited, on every path.
  (2) **The window that took the press treats it as in force, contacts NO host, and tries the write again before
  every call.** (3) **It sets the unsure marker of the side** (next point), so that the other windows of the side
  stop too — also after the pressing window is closed. One notice names the host and the reason, and has the press
  *Save now*. For a *Read*, a *Forget* or a start that the file did not take: nothing changes, the refusal or the
  Stop stands, and the person is told. The text before stopped only the window that took the press, and closing
  that window dropped even that.
- **The unsure marker.** A small file beside `refused.json`, `unsure-<token>.json`, made by an exclusive create —
  which needs no lock, so a lock that is held does not stop it. It holds the change that waits: a refusal with its
  host and time, or the Stop with its time. At most 1 KiB each, at most 32 in the folder. **While a marker's change
  is not in the file, no window of the side makes an automatic call and none allows a host by itself**; the session
  door reads nothing either, and a host named in a marker is refused. Every window reads the folder before every
  call, tries to land each marker's change — its own and another window's — and removes a marker only after it has
  read that change in the file. So a refusal that one window could not write is written by the next window that
  can, and it does not go with the window that took the press. A person's *Read* or *Forget* for a host removes
  that host's markers first, and a start removes a Stop's. A marker that cannot be read stops the side like any
  other; only *Save now* removes it, and that press leaves the round STOPPED, because the marker may have held the
  Stop. The person is told in every window: the list's row for each host says that a refusal is not saved on this
  side and offers *Save now*. **What the marker cannot do** is in residual 5 of the file: it is a file on the same
  disk.
- **The copy-in, at every read (revised at the re-review, finding 1; it was "the migration, once for each side").**
  At every read of the store — before every call, at every press, and when the hosts are listed — every "no" row
  of `coai.terminalHosts` that can be read and that the file does not hold is copied into the file, under the
  claim. The first copy-in sets `migratedUtc`; no later one is held back by it. Nothing is removed from
  `globalState`. A "no" in either place counts, and the file only ever gains one this way. A copy-in that the file
  does not take is a refusal the file did not take, above. **Why not once:** a window that still runs an older
  build writes a "no" to `globalState` only. Copied once, that "no" stayed a copy for ever, and a copy can be
  erased (the next paragraph). **What it costs:** a window whose view of `globalState` has not caught up still
  holds the "no" row of a refusal that a person has just lifted, and its next read copies that row back into the
  file. The host is then refused again, and one more press lifts it (residual 4 of the file).
- **The copy in `globalState`.** Every refusal is also written as the "no" row of today, and the Stop as `stoppedUtc`
  of the state row. The copy is how another side of the machine learns of it, and how a window that still runs an
  older build does. For a "no" and for a Stop the copy is written FIRST and at once, whatever the file then answers
  — but when the newest file this window has read already holds 100 other refusals: then nothing is written to
  either place, and the person is told. For a *Read*, a *Forget* or a start the file is written first, and the copy
  is changed only when the file took the change.

**What a side means, and what it costs (entry 1.2).** `globalState` belongs to the client: every window of a profile
shares it, a WSL or remote window too. The file belongs to one extension host's disk. So:

- On the side where the person pressed, the refusal is in the file, and nothing but a person's press on that side
  removes it.
- Another side honours the copy in `globalState` while it stands, and copies it into its OWN file at its next read
  (the copy-in). From then on that side keeps the refusal too. Inside the lag after the press a stale write can
  still erase the copy before that side has read it, and then that side never learns of the refusal. It is the old
  defect, left on the far side only and only until its next read; it is a residual (*Security*) and part of what is
  left of candidate C4.
- A *Read* or a *Forget* pressed on another side changes the copy and that side's own file, and lifts nothing on the
  side that keeps the refusal. A refusal reaches the file of every side that reads while its copy stands, so it is
  lifted on each. The change question says on which side a refusal is kept. The Stop is not copied into another
  side's file: there it holds through its copy only.

**Every write of `globalState` is one commit that re-reads and merges (the gate's finding 0).** Two windows can each
hold a copy of the list. If a window wrote a whole list it had read earlier, it would erase a row that the other
window wrote in between. So, for the rows that stay in `globalState`:

1. **One function writes the store**, and nothing else does. It takes a CHANGE for one host — never a list. At the
   moment of the write it reads the stored list again, applies the change to that fresh list, and writes the result.
   No list that was read before an `await` is ever written.
2. **The merge rule, for one host:** a person's "no" beats everything. A person's "yes" beats an automatic "yes". An
   automatic "yes" is written ONLY when the fresh list has no row for that host — and only after the file, read just
   before, showed no refusal for it; it never replaces a row. Only a person's own press (*Read*, *Forget*) changes a
   "no".
3. **(Superseded by entry 1.2.)** The first text of this point had a window write a missing "no" again, and entry
   1.1 followed from it. No window does that any more: a refusal is the file's, and a missing copy in `globalState`
   is not put back by anybody.
4. **The round reads the file and the store again just before every call** (§ 2). A "no" given in another window of
   the same side stops the next call with no lag, because it is read from the disk.
5. **What is not measured (U12):** how soon one window sees another window's `globalState` write. Since entry 1.2 it
   no longer decides whether a refusal holds on the side that keeps it. It still bounds three things: how soon
   ANOTHER side stops calling a refused host; how long a lifted refusal's copy still counts in another window; and
   — since the copy-in — for how long another window can carry a lifted refusal back into the file. E2 measures it
   with two windows (R12).

**The cap protects the person's answers.** 100 rows. An automatic row never pushes out a row a person wrote: at the
cap the oldest AUTOMATIC row goes; with none to drop, the new host is not allowed and not contacted, and the list
says that the list of hosts is full. Else a program that opens a hundred terminals with invented names could push a
"no" out of the store.

**The automatic "yes" covers the session read too.** One row, one answer: on a host the round allowed, *take the
question* and *add the question* read the session without asking. A session read still starts only on a press in
that terminal. The cost: while the round runs, the question *"Read the Claude session…"* is in practice no longer
asked; it is asked only when a door is pressed on a host that has no row yet.

**What a host with a "yes" of any kind gets, with no press:** a read by a timer every 10 minutes while a `Creds:`
terminal of it is open; the names of the accounts signed in there; a start of Claude Code, which asks Anthropic with
that host's own login; a start of `agy` for its version. Until this release a "yes" covered one read that a press
starts. (With candidate C1, an install of `jq` is added to this list.)

**What the person is told — every one of these comes after the first read, not before it.**

- **A daily notice of the hosts that were allowed by themselves.** At most one in 24 hours, only when there is such a
  host, shown at once for the first. No question. It lists those hosts, says where to forbid one (*CoAI: hosts read
  over ssh*, *Not this host*), names the setting `coai.terminalAccountsAllowNewHosts` and that it is on, and has one
  button, *Open settings*, which decides nothing.
- **One notice, once for each installation of the extension**, at its first accounts round, for the hosts with a
  "yes" from before: their accounts are now read every 10 minutes, and where to stop it.
- **The question's words**, where it is still asked: same code (`read-remote-session`), same buttons, same store; the
  title is *"Read the Claude session and the AI accounts on “x” over ssh?"* and the detail lists what a "yes" covers.
- ***CoAI: hosts read over ssh***: *the Claude session and the AI accounts are read*; *allowed automatically on
  <day>: …*; or *not read*. Beside *Forget* it says that a forgotten host is allowed again by itself, and — since
  entry 1.2 — on which side of the machine a refusal is kept.
- **The help in five languages**, **`SECURITY.md`** and the release notes (the Security section).

**The setting `coai.terminalAccountsAllowNewHosts`** (boolean, default `true`, owner decision 20). Off, it restores
ask-first: no host is contacted without a person's "yes", and an automatic row counts as no answer — for the accounts
read and for the session read — until a person presses *Read*; the row is kept.

- **The name** is flat camelCase, like the 68 other `coai.*` settings; none is a dotted group.
- **Only the person's own user-level value counts.** It follows `SAFETY_KEYS` in `src/modelKeys.ts`: the read goes
  through the user-layer door (`inspect().globalValue`, else the default), never the merged value; and the manifest
  gives it `"scope": "application"`, so a workspace cannot set it and a WSL or remote window does not fall back to
  another machine's value. What a remote window really reads is not measured; E2 observes it (R11). A workspace or
  folder value is ignored, and the list says so in one row.
- **Registered in** `package.json`, `src/settingsShape.ts`, `SAFETY_KEYS`, `NEVER_TRANSFERRED` of
  `src/configTransfer.ts` (an imported file cannot decide it), and the help in five languages
  (`helpCoverage.test.ts`). It is read before every call.
- The second setting of decision 20, `coai.terminalAccountsInstallMissingTools`, arrives with the installer (C1).
- The appendix, § G, has the arguments for the name and the scope in full.

**The stop button is kept.** *Stop the accounts round* is one title button of the list. It clears the timer and drops
the readings; while it is stopped no host is contacted for accounts and no host is allowed by itself. It is the only
way to keep the session read of a host and stop the accounts read, and the way out when a read does harm on a VM. It
does not come before the first round. **Where the Stop is kept (revised, entry 1.2):** in the refusals file, as
`stop`, written and removed by the same function as a refusal. The state row of `globalState`,
`coai.terminalAccountsState`: `{ toldUtc, lastAutoNoticeUtc, stoppedUtc }`, keeps `stoppedUtc` as the COPY of the
Stop and is written by the commit of this section. The round is stopped when the file says so OR the copy says so,
and a state row that cannot be read is a stop (gate G2). A Stop is written to the copy at once and then to the file;
one that the file did not take leaves its marker, like a refusal. Only a person's start removes the Stop: it is
written to the file first, and then the copy is cleared. A stale window's write of the state row can put an old
`stoppedUtc` back or drop a new one; the first stops a round that the person started, and it is said in the list;
the second changes nothing on the side whose file holds the Stop.

**How a person forbids a host.** ***Not this host*** — in the hosts list, in a host's row of the accounts list, or
in the question — stops every ssh call to it, and its reading is dropped at once. ***Forget* no longer stops the
accounts read**: it removes the refusal and the row, and the next round allows the host again. **The case of ledger
entry 1.1 is gone (superseded by entry 1.2):** no window writes a refusal again, so a *Forget* or a *Read* given in
any window of the side that keeps the refusal lifts it, at once and for every window of that side. **One thing can
undo it (the copy-in, finding 1 of the re-review):** another window whose view of `globalState` has not caught up
still holds the old "no" row, and when it reads inside that lag it copies the row into the file again. The host is
then refused again and shown so, and one more press lifts it. What is also different now is the SIDE: a *Forget* or
a *Read* pressed on another side of the machine does not lift a refusal that this side's file keeps (*What a side
means*, above). A read that is already running ends within its 30 s and its result
is dropped.

**What a person can no longer choose.** They cannot allow the session read on a host and refuse the accounts read on
the same host. With the setting on, they are not asked before a host is contacted for the first time.

### 4. The remote script and its reply — the cornerstone

- **Fixed text, sent whole, pinned.** A new constant, the accounts script, in the shape of `REMOTE_SCRIPT`: POSIX `sh`
  (the VM's `sh` is `dash`, measured), one function read to its end and only then called with
  `</dev/null 2>/dev/null`, a begin line first and an end line last, so a script cut on the way does nothing. Its
  sha256 is in its spec, and this time a test holds the hash too: this script runs a vendor program, so an edit of the
  text without the spec must turn a test red.
- **No write by the accounts script, no `cd` to a folder a person owns, no background job, no temporary file, no
  here-document.** Every tool it needs is checked first; a missing tool is said, never read as "nothing found". The
  install of `jq` is not in this script, and not in E1 or E2 (candidate C1).
- **The `PATH`.** `ssh <host> sh -s` is not a login shell, and on the measured VM its `PATH` has no `~/.local/bin`,
  where `claude` and `agy` are. The script's first line inside the function puts `$HOME/.local/bin` in front of the
  `PATH`, as that VM's `~/.profile` does for a login shell. Nothing else of a profile is read.
- **The tools.** `jq`, `timeout`, `tail`, `ls`, `sed`, `grep` and `command`. `jq` was found on the measured VM; it is
  not known for the others. **`jq` is required, and the reason is safety by construction**: a field is printed because
  it was named, so nothing beside it in a JSON document can leave the VM. Cutting JSON with `sed` would be a pattern
  next to private text (the person's activity in `/usage`, a transcript line of Codex), and one wrong pattern there is
  a leak with no error. A host without `jq` gets the state `unreadable no-jq` for Claude and Codex. **With `jq` missing, the script
  also says which package manager the host has**: one closed word, from the existence of four fixed paths
  (`/usr/bin/apt-get`, `/usr/bin/dnf`, `/usr/bin/yum`, `/sbin/apk`), or `none`. The parser takes that word only from
  this list, and the row shows the fixed string that belongs to it. **No text from a VM is ever part of a command
  that is shown**, and the extension runs none of them:

  | Manager word | The row shows |
  |---|---|
  | `apt-get` | `sudo apt-get install -y jq` |
  | `dnf` | `sudo dnf install -y jq` |
  | `yum` | `sudo yum install -y jq` |
  | `apk` | `apk add jq`, to be run as root |
  | `none` | no command: *Install `jq` on this host. CoAI found none of apt-get, dnf, yum and apk there.* |

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
  - **The working folder is `/`.** The script may not write, and a temporary folder is a write that a kill leaves
    behind, on every host, every 10 minutes; `$HOME` is wrong, because Claude Code would take `~/.claude` for a
    project's folder. W1 measured `/`: the same numbers, the same four cache files, nothing left in `/`. (The
    argument in full, and what W1 saw of `~/.claude.json`: the appendix, § H.)
  - **`TZ=UTC` is set for this one command**, as a request. Its effect on a VM in another zone was not observed, so
    the parser reads a reset only when the text ends in `(UTC)`.
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

### 5. What is still to be measured — in E2, on the owner's hosts

Step 0 and W1–W5 are done; their tables are in the appendix, § D. Three measurements are owed. Each needs the
owner's go for its exact text. They are pass criteria of E2, and **two of them are gates for the release**. **No
build runs the Claude usage command by itself before W6 and W7 have recorded results** (finding 2 of the re-review):
both are run by hand on a VM and need no window, and the build that E2 starts with has the Claude usage read off
(§ 2).

| # | What is measured | What the result does |
|---|---|---|
| W6 | **Backups: who makes them, and are they retired** (the gate's finding 4). As a Linux user with **no live Claude Code session**: the time of `~/.claude.json`, the number and total size of the files in `~/.claude/backups/`, before and after each of 12 usage reads from `/`; whether `~/.claude.json` has an entry for the folder `/` (a key name, no value); and, on a host with a long history, how many backup files Claude Code keeps | **A gate for GROWTH only.** The owner accepted the writes (N1), not files that pile up for ever. The usage policy of § 2 follows from the result, before ANY build turns automatic usage reads on — the owner's pre-release among them |
| W7 | **A trace of every program start** (the gate's finding 5). One usage read is run under `strace -f -e trace=execve` (or a tool of the same kind that is already on the VM), with stdin `/dev/null` and from `/`. `-f` follows children that live for milliseconds and children that detach. The trace is read for every path that was started. If no such tool is on a VM, W7 runs on a VM that has one; nothing is installed for it | **A gate.** Any program that is not Claude Code's own — a hook's command, an MCP server's command — fails the condition "starts nothing of the person's": the Claude usage read is then off and the list shows the account only, until the owner decides. It is off already in the build E2 starts with, so a failed W7 changes no build: the read stays off. **Until W7 has run, this plan does not claim that condition.** A list of a process group, which the earlier text proposed, does not prove it: it misses a child that is gone between two samples and a child that left the group |
| W8 | `agy --version` from `/` with stdin `/dev/null`: its time; the files under `~/.gemini` and the agy program itself that are newer than a marker; under the same trace as W7, what it starts | **A record** (N4: the owner chose the version knowing the risk). If a start with the version flag downloads or replaces the program, the owner is told with the size |

Within the plan as written: the field names, the limits under the ceilings, the usage policy that W6 selects.
Outside it, and so a change: anything installed on a VM before candidate C1 is taken up; a third kind of ssh call; a
file with a secret that this plan does not name; a write by the accounts script; road (b).

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs.
- **No install in epics E1 and E2.** Nothing is written or installed on a VM by our script. The installer of owner
  decisions 17, 19 and 20 is candidate C1; its design is in the appendix, § E.
- No real time, no push from the VM, no setting for the interval.
- No colour for the numbers (the mod colours them), and no webview.
- No history, no chart, no ledger of the readings, no alert when a limit runs low.
- No reading of a host that has no open `Creds:` terminal, and none of this machine's own accounts.
- No reading of a host whose answer is "no", ever. No question raised by the timer.
- No setting that a workspace or a folder can set for the person. No setting that turns the whole round off: that is
  the stop button.
- No live Codex read (`codex app-server`), no `codex login status`, and no opening of `~/.codex/auth.json`, ever.
- No start of `agy` but with its version flag, no reading of agy's log, and no opening of its sign-in file.
- No opening of `~/.claude.json` or of the Claude credential file by our script.
- **No road (b).** The owner allowed it (O1); this plan does not build it.
- No separate consent for accounts, and no question for several hosts at once.
- No refresh of any login by our script. An expired login is said as it is.
- No grouping of accounts that are not known, no grouping by host alias, and no guess that two hosts share an account
  from equal numbers.
- No change to `SSH_OPTIONS`. Connection sharing is still not pinned, as `SECURITY.md` says today.
- No lock between VS Code windows for the round: each window with `Creds:` terminals runs its own round. (The consent
  is different: the writes of the store merge, and the refusals file is written under a lock, § 3.)
- No file shared between two sides of the machine. A refusal reaches another side through `globalState` only, and
  that side copies it into its own file at its next read (§ 3, *What a side means*). No lift is carried to another
  side, and no Stop is copied into another side's file.
- No change to the session reader's pick list (the `$(` note in § 1) without the owner's word.

## Security

This section is the security judgement of the plan (Fable, `subagent-models.md`: the security row). The specs of
E1.S1 and E1.S2 repeat it for the exact store rules and the exact script text, before any code.

**The new edges.** (1) A read-only ssh call that a TIMER starts, with no press, from the start of the extension, to
every host of an open `Creds:` terminal that the person has not refused — for a new host, the terminal's NAME is
enough. (2) On each usage read, a start of Claude Code on the VM, which calls Anthropic with the VM's own Claude
login. (3) In every round, a start of `agy` on the VM, for its version. (4) Text from a VM drawn in a list. Candidate
C1 adds a fifth, an ssh call that changes the VM.

**Road (a) is taken: no secret passes through our script.** The script opens no credential file, holds no token in a
variable and gives none to any program. Claude Code reads its own login inside its own process.

| What | Where it is | What our script does with it |
|---|---|---|
| The Claude OAuth access token and refresh token | `~/.claude/.credentials.json` on the VM (mode 600) | Never opened. With a valid login the file was not among the files a usage read changed. With an expired access token Claude Code may refresh it and write the file, as on any start; that case was not measured |
| `~/.claude.json` (every project's entries and the MCP servers of the user) | the VM | Never opened by our script. With the three flags it was not written in three reads and was rewritten, with a new backup, during one; who wrote it is not proven (W6). The owner accepted the write (N1) |
| The person's activity: requests, top subagents, top MCP servers | the `result` text of `/usage`, after the usage lines | Dropped **on the VM**. Only lines that fit three anchored patterns leave it; a marker test holds that |
| `~/.codex/auth.json` | the VM | Never opened. Only tested for existence |
| Codex session files (a transcript: private, not a credential) | the VM | A bounded tail of at most three files is read. `jq` prints named numbers of the last `token_count` record and nothing else |
| agy's sign-in file and its log (the log holds an email address) | `~/.gemini/antigravity-cli/` on the VM | Never opened by our script. `agy` is started with its version flag; of its output only a version of digits and dots leaves the VM |
| The person's ssh keys and agent | this machine | Used by `ssh` as today; `ForwardAgent=no` |
| The account email and organization name (personal data, not a secret) | the reply | In memory and in the list (O2). Never logged, never stored, never sent on |

**What Claude Code writes on each usage read — measured, and not ours to prevent.** Four cache files of its own, in
each of the four reads whose changed files were listed: `~/.claude/plugins/store/cc-plugin-agents-md_builtin-*.json`,
`~/.claude/policy-limits.json.stamp.json`, `~/.claude/policy-limits.json.signature.json` and
`~/.claude/remote-settings.json.signature.json`. They do not grow (W4). The account and version commands write no file
(W3). **`~/.claude.json` and a backup of it were written during one read; by whom is not proven.** The owner accepted
those writes (N1). Two things N1 does not cover, and this plan holds both: **growth without bound** — a backup file
for each read with nobody to retire it — which W6 measures and the usage policy of § 2 answers; and **a live session
that is disturbed** — it asks to sign in again, loses its settings or MCP servers, or stops — which R7 holds.

**"Starts no hook, no MCP server and no plugin of the person" is NOT claimed yet.** Step 0 concluded it from the
flags; W2 saw no child in one sample and one `node` process of unknown origin afterwards. Neither proves it. W7's
trace of every program start does, and it is a gate (§ 5).

**A start of `agy` in every round (N4).** What agy does on a start beside printing its version was not measured (W8).
The known risk is a self-update: agy has updated itself on another machine (`research/RESULTS_agy_read_file.md`). If
a start with the version flag can set that off, our timer decides when a 269 MB program on a VM is replaced, under
live agy sessions too. The owner chose the version knowing this. The start has a 4 s limit; R7 notes the time and
size of the agy program over two hours.

**Finding F1, for the owner: the consent no longer comes first** — in full in the appendix, § B. An old "yes" is used
with no press (N2), and a host nobody was asked about is allowed by a terminal's name (Q1); because the row is
shared, the session reader's question is in practice no longer asked while the round runs. No rule of `.agents/`
forbids it. It reverses `SECURITY.md` (*"No ssh child is started before a "yes""*, *"a name is a request, never
proof"*) and the terminal-door plan (*The person agrees to each host once*), and this plan changes both texts openly.
**What a terminal's name can set off:** any program that can open a terminal in the window can call it
`Creds: <host> / x`; within 30 s to 10 minutes the extension logs in to that host as the person and runs the fixed
script. What limits it: the name chooses only the host, from an alphabet with no option and no shell character; ssh
refuses a host whose key is not in the person's `known_hosts`, asks for no password and forwards no agent; nothing of
the reply goes back to whoever opened the terminal. What does not: every host the person has ever logged in to with a
key can be reached this way. **On by default, as the owner chose (decision 20); turning
`coai.terminalAccountsAllowNewHosts` off restores ask-first. The planner's recommendations — a separate consent, then
a round that waits for one press, then the question kept for a new host, then the setting off by default — stay
recorded; the owner decided otherwise each time.**

**Finding F2, for the owner: the install changes the machine, as root** — in full in the appendix, § B. It belongs to
candidate C1 and is not in E1 or E2. It stands as written for the day the installer is built: on by default, as the
owner chose; the planner recommended a press on an old "yes" and the setting off by default.

**What an attacker gains.**

- **Someone who is that Linux user on the VM, or root.** They already hold every file the script reads. They can make
  the list show wrong accounts or numbers (a false `claude` or `jq` on the `PATH`, an edited file). They cannot reach
  this machine through the reply: it is bounded (30 s, 64 KiB, 64 lines), parsed by a closed vocabulary, never run,
  never a path, shown with control characters and `$(` taken out.
- **Another Linux user on the same VM** (two aliases can be one machine). They can list processes and read every
  argument. **No secret is ever an argument.** The script writes no file, so nothing is left in a shared folder.
- **Someone on the network.** ssh with `StrictHostKeyChecking=yes` between this machine and the VM; TLS by Claude
  Code itself between the VM and the vendor.
- **A program that opens a terminal named `Creds: evil / x`.** A name is enough (F1). It chooses a host and nothing
  else. If `evil` is its own server and has one of the person's public keys, it sees a login and the text of the
  fixed script; it gets no agent and no password. If the name is one of the person's own servers, it has made the
  extension log in there and read. It cannot push a person's "no" out of the store (the cap), and it cannot undo one
  through a second window: a refusal is in a file that only a person's press changes (§ 3). It cannot write that
  file through the extension either: no command takes a host or an answer. A program that can write files as the
  person can edit it, as it can edit `known_hosts`; that is code already running as the person, and not a boundary
  of this plan.
- **A second window, or a reload, racing a refusal or the Stop** (the gate's finding 0; the E1 gate's finding and
  the cadence consultation `e319fe2e`; ledger entry 1.2). A stale window sends its whole copy of `globalState` on any
  write, so `globalState` cannot hold either. Both are in the refusals file of § 3: written under an exclusive-create
  lock, read from the disk before every call, removed only by a person's press. A write that does not land is in the
  copy at once and leaves a marker that every window of the side reads, and the lock is never taken over by its age
  (the re-review's findings 0 and 3). The earlier answer — the window that wrote a "no" writes it again — is gone:
  it protected nothing once that window was closed.
- **A hostile reply.** It can only become text in a list, or `unreadable`. The credential-shape rule of the parser is
  a net, not the measure; the measure is that the script has no line that opens a credential file.
- **Logs.** A failed read logs the host, how it ended and ssh's own last line. The script's stderr goes to
  `/dev/null`. stdout is never logged.

**Judgements.** *Road (a):* yes, on what is measured — no model call, no transcript, no secret through our script —
and with W7 as the gate that can still fail it. *Road (b):* allowed by the owner, not built, and the planner still
recommends against it; its conditions are in the appendix, § C. *The consent:* the owner's decisions, with F1; what
is left of a gate is the refusal, and this plan makes that one hold — one road in, a file that only a person's press
changes, read from the disk before every call, and a cap that cannot drop a refusal. *The refusals file (entry 1.2):*
the first judgement of this section trusted a repair by the window that gave the refusal; that was wrong, and the
E1 gate said so before the planner did. What may stay in `globalState` is what is safe to lose: a "yes" of either
kind and the notice times. *The re-review (session `4082dc2c`):* three of its five findings are about that file, and
each is a case this judgement had not thought of — a refusal whose write failed was known to one window only; a "no"
from a window of an older build stayed a copy for ever; a lock broken by its age let a writer that woke up write
over a newer refusal. This judge accepted all three. § 3 closes them as far as a file on one disk can, and residual
5 says where that ends. *The setting:* it makes the automatic allow something the person can take back, and
nobody else. *Persistence:* nothing of a reading is stored. *Codex:* the passive file read. *agy:* no file is opened;
the program is started with its version flag only, by the owner's choice.

**Residuals, named.** A person's `ControlMaster` can make the call a persisting master on Linux or macOS, as today.
Every host of an open `Creds:` terminal gets a login every 10 minutes, whether an AI CLI is there or not, until the
person refuses it; a VM's ssh log shows it. The list shows an email on screen (O2). Each usage read is a start of
Claude Code, with what Claude Code does on a start: its caches, its own network calls, possibly its own update check.
A VM that prints the reset in another zone than UTC shows the percent and a dash for the reset.

**The residuals of the refusals file (entry 1.2), which replace "one more call until another window's refusal is
visible" (U12).**

1. **A call that is already running** when a refusal or the Stop is written runs to its end, 30 s at most, and its
   result is dropped. A call that starts afterwards is not made, in any window of that side.
2. **Another side of the machine** learns of a refusal or of the Stop through its copy in `globalState` only. It
   copies a refusal into its own file at its next read, and keeps it from then on. Inside the lag after the press a
   stale write can erase the copy before that side has read it; that side then goes on reading the host, and nobody
   is told. The Stop is not copied in: on another side it holds only while its copy stands. The side where the
   person pressed is not affected. To be sure of a host on two sides, the person presses *Not this host* on each;
   the list of each side shows how the host stands there.
3. **A person's "yes" that was withdrawn can come back.** With `coai.terminalAccountsAllowNewHosts` off, a *Forget*
   of a person's "yes" puts the host back to *not asked yet*; a stale write inside the lag can put the old "yes" row
   back, and the host is then read again with no question. It needs a host the person once allowed, a *Forget*, the
   setting off and a stale write in the lag. *Not this host* is the press that holds. With the setting on a
   forgotten host is allowed again in any case.
4. **A lifted refusal can come back — since the copy-in, also into the file.** After a *Read* or a *Forget*,
   another window still holds the old "no" row until its view catches up. When it reads in that time it copies the
   row into the file of its side; and a stale write can put the row itself back. Either way the host is refused
   again until the person presses once more, on each side that took it. It needs a second window that reads or
   writes inside the lag. It errs to not contacting, and the list says so. The cure is a record of what was lifted,
   which is what is left of candidate C4b. The same holds for an old `stoppedUtc`: the round is stopped again, and
   the list says so.
5. **The lock, the marker and the writer (revised at the re-review, findings 0 and 3).** The lock is never taken
   over by its age. (a) **A lock whose owner cannot be shown to be gone** blocks every write of that side until a
   person presses *Save now*: the process lives and does not finish; the note cannot be read; the lock was made on
   another machine over a shared folder; or the machine was restarted and gave the old process number to another
   program, where no boot id can be read. Meanwhile a "no" is in force in every window of the side through its
   marker, and no host is read by itself. It fails closed, and it needs a person. (b) **After such a press the
   former holder can still wake up and rename its stale file over the new one.** Every window that read the newer
   file sees that the file went back, and writes the union again. A refusal is then lost from the file only when
   no window that saw the newer file is still open; its copy in `globalState` still counts and is copied in again,
   unless a stale write erased that too. A *Read* or a *Forget* can be written over in the same way, which errs to
   refusing. (c) **Two windows that both take over the lock of an owner that is gone** can both write, and the last
   rename wins. The writer whose change is missing sees it at its read-back, or its window sees a file of the same
   `gen` by another writer at its next read; what is left is a window that ends in between. (d) **A disk that takes
   neither the file nor the marker** — full, or read-only. The refusal is then in force in the window that took the
   press and as its copy, and nowhere else. If that window is closed, the copy is all that is left, and a stale
   write can erase it: the defect of entry 1.2, on a disk that cannot be written. The notice says that this window
   must stay open. A folder that cannot be READ stops every window of the side, like a damaged file.
6. **A damaged file** stops every accounts read of that side until a person mends it. The mend keeps only the
   refusals that `globalState` still holds; one that was kept only in the file is lost, and the question says so. No
   notice says that the file is damaged: a person learns it from a host's row, the session door or the hosts list.
7. **A file that is deleted from outside** reads as a file that was never written. Every open window that had read
   it writes back what it remembers, and the copy-in copies back the copies in `globalState`. With no window open,
   the refusals stand only as their copies until the next read.
8. **A window that still runs an older build** writes a "no" to `globalState` only, and reads only that. A "no"
   given there counts on every side as a copy, and a window of this build copies it into the file of its side at
   its next read (finding 1 of the re-review). Until such a window has read, it is a copy only. The older window
   itself never reads the file: on a press, its session door reads a host that is refused only there.

`SECURITY.md`: the section *The extension's one outward ssh call* becomes two kinds of call in this release's first
epics, and three with C1. Its sentences *"No ssh child is started before a "yes""* and *"The first read for a host
asks once"* are rewritten: a host is contacted unless it was refused. It gains: the timer; how a new host is allowed,
stored and shown, and that only *Not this host* stops a host; the merge of the store; **the refusals file (entry
1.2): where it is, that it is kept for each side, that only a person's press removes a refusal or the Stop, what a
damaged file does, what a write that does not land does — the copy, the marker and *Save now* — that the lock is
never taken over by its age, that a "no" seen in `globalState` is copied into the file, and why `globalState` alone
cannot keep a refusal**; the setting
and that only a user-level value counts; the daily notice; the stop button; road (a) and what the VM then calls; what
Claude Code writes on a read; the start of `agy`; the person's activity dropped on the VM; and the residuals above,
the eight of the file among them. What it says about W6 and W7 is written after E2, from what was seen.

## Growth surfaces

- **The readings**: in memory, one for each host with an open `Creds:` terminal, a few hundred bytes each, dropped when
  the host's last terminal closes, when its answer becomes "no", when the round is stopped, and with the window.
- **The consent in `globalState`**: `coai.terminalHosts` stays capped at 100 rows. The round writes rows too; at the
  cap the oldest automatic row goes, never a person's answer. So the store cannot grow past its cap, and a flood of
  terminal names cannot empty it of refusals.
- **The refusals file (entry 1.2) — a new surface, one for each side.** `terminal-hosts/refused.json` under
  `globalStorageUri`. **Projected size:** at most 100 refusals and one Stop; about 8 KB with host names of 30
  characters, and never more than 64 KiB, because a write that would pass either cap is not made. A terminal's name
  cannot grow it: only a person's "no" adds a refusal. **Who retires it:** a refusal goes by a person's *Read* or
  *Forget*, the Stop by a person's start; at 100 refusals the next "no" is not kept and the person is told. The file
  itself is kept for ever: 64 KiB at most on each side, in the extension's own storage folder. Whether VS Code
  removes that folder with the extension was not checked. **When a write is interrupted:** the rename is atomic, so the file is the old one or the new one. A
  crash can leave `refused.lock`, which the next writer takes over when it can show that the owner is gone, and one
  beside-file of at most 64 KiB; every write, while it holds the claim, removes the beside-files of that folder that
  are older than 60 s. The host that does both is the window that writes next; no timer exists for it. A lock whose
  owner cannot be shown to be gone stays until a person's *Save now* (residual 5a).
- **The unsure markers (the re-review's finding 0) — a new surface, in the same folder.** `unsure-<token>.json`,
  one for each refusal or Stop that the file did not take. **Projected size:** at most 1 KiB each and at most 32 in
  the folder, so 32 KiB; past 32 no marker is made, and the side is stopped by the others already. Only a person's
  "no", a person's Stop or a "no" row to copy in makes one; a terminal's name cannot. **Who retires it:** the write
  that lands its change, in any window of the side, after it has read the change in the file; a person's *Read*,
  *Forget* or start for what it holds; *Save now* for one that cannot be read. None goes by its age. **When it is
  interrupted:** a window that ends between the create of a marker and its write leaves one that cannot be read; it
  stops the side until *Save now*.
- **The state of the accounts round**: one `globalState` row, under 150 bytes.
- **The ssh child**: one for each host in a round, and one more for the one host that reads an account's usage
  (§ 2), killed at 30 s with its tree, stdout capped at 64 KiB. At most one alive at a time for each window.
- **The timer**: one for each window, cleared when no `Creds:` terminal of a host that is not refused is open or the
  round is stopped, disposed with the extension. A round interrupted by a reload leaves nothing.
- **Log lines**: one when a host starts to fail and one when it reads again — not one for each round.
- **On the VM, by the accounts script**: nothing.
- **On the VM, by Claude Code on a usage read**: four cache files, rewritten in place; they do not grow (W4).
- **On the VM, `~/.claude/backups/` — the one surface that may grow without bound (the gate's finding 4).** One backup
  of `~/.claude.json` appeared with the one rewrite that was seen. That file was 164 969 bytes on the measured VM. If
  every usage read makes one and nothing retires them, a host that reads an account's usage gains 144 files and about
  24 MB a day at one read in 10 minutes, and 24 files and about 4 MB a day at one read an hour. **Who retires them:**
  not known. W6 measures who makes a backup and whether Claude Code keeps a fixed number. The policy of § 2 says what
  follows from each result. This plan does not delete a file of Claude Code.
- **On the VM, by `agy` on a start**: not measured (W8). A self-update replaces a program of 269 MB; whether agy keeps
  the old one is not known. R7 notes the size of agy's files.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, made by Claude Code, and none while
  the usage policy is `off`. None by our code.

## Boundaries with other plans

This plan takes no slice of another plan's open work. It builds on code that shipped, and with O3 it **changes one
thing that the terminal-door plan built**: what its per-host answer covers, and the words that say so. The table says
what is whose, so it is read as complete:

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, *take*, *add*, *Asked* | [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0) | This plan widens the runner so a call needs no session operand. The session read itself does not change |
| The per-host consent: the store `coai.terminalHosts`, the question `read-remote-session`, *CoAI: hosts read over ssh* | the same plan | **This plan changes its meaning (O3, N2, Q1; with candidate C1 also N5 and Q2)**: the same answer also allows the accounts read, by a timer with no press — and, once C1 is built, one install of `jq`; and a host with no answer is allowed by the round itself, for the session read too, so the question is in practice no longer asked first. This plan rewrites the question's title and detail, the rows of the hosts list, the help paragraph — with its sentence that the doors only read and change nothing on that machine — and `SECURITY.md`. The store's key, the question's code and the buttons do not change; a row gains the marker `how`, and the cap gains the rule that an automatic row never pushes out a person's answer, and every write becomes a commit that re-reads and merges. **Since entry 1.2:** a refusal is kept in a file of this plan, `terminal-hosts/refused.json`, one for each side, and the "no" row of that store becomes its copy; the session read asks the file too, so a host refused in the file is not read by a press either |
| The gauge of coai's own rounds on this machine and on a Team server (`gaugeWords.ts`, the Team server's capture) | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing. That gauge counts coai's rounds; this list shows a person's accounts on other machines. No shared store, no shared reader |
| The account and limits line inside Claude Code | `dew_flow_conventions · mods/agent-models` | Nothing is changed there. This plan carries its word rules over and tests them against its cases |

Order: this plan starts after the terminal-door plan's last release is cut, so the runner is not widened and the
question is not reworded under an open pull request of that plan.

**The other side of this boundary is not written yet.** The terminal-door plan does not say that a later plan widens
its consent. It is open, in epoch 3, with one change recorded, so a second ledger entry there is a full stop. Whether
a boundary note is a change of that plan is not this revision's call. It is owed: at that plan's promotion at the
latest, its text names this plan beside *The person agrees to each host once*.

## Build order

1. **Done 2026-11-19:** step 0 and W1–W5; the owner's answers; the plan round (`proceed`, codex only); the question
   consultants' answer on the split (consult `f4af076c`).
2. **E1 — the read-only feature, end to end.** The stories by Opus; the two specs by Fable before their code (E1.S1
   by the security row, E1.S2 as the cornerstone); the code gate; merged.
3. A pre-release VSIX from E1's merged head, **with the Claude usage read off** (`USAGE_POLICY` `off`, § 2; finding 2
   of the re-review), installed by the owner (no real VS Code can start on the build machine). The account, Codex
   and agy reads are on in it.
4. **E2 — run it for real** on the owner's hosts, in this order. (a) W6 and W7, by hand on a VM, each with the
   owner's go. (b) On the first VSIX: every criterion that needs no Claude usage. (c) Only when W6 and W7 both have
   recorded results: **a second pre-release VSIX, from one small PR that sets the usage policy to the row W6
   selected** — when W7 failed, no such build is made and the read stays off. (d) On that second VSIX: the criteria
   that need Claude usage (the table of E2 says which). Each of R1–R12 and W6–W8 has a recorded result. A gate that
   fails is not worked around: its stated consequence is applied, and a fix that changes the product is a new PR
   and a new pre-release VSIX.
5. **Candidate C1, the installer**, is taken up as this plan's next epic: its stories, its cornerstone spec by Fable,
   its code gate, a new pre-release VSIX and its own real-host checks (the appendix, § E).
6. `review_feature` over the built epics — three with C1, so it is owed — every finding resolved.
7. The extension release, one for the whole plan: the tag pushed alone and its run observed; `POST_DEPLOY.md` run;
   the plan promoted with its deviations, its appendix and its stories documents.

## Test plan

- **The accounts script, run for real** (`npm test`, Linux CI): the exported text run by `sh -s` (`dash`) and by
  `bash --posix` against a temporary `HOME` with false `claude`, `codex` and `agy` programs, as
  `remoteScriptRuns.test.ts` does with `tmux` and `ps`; `jq` is the real one. Cases: each vendor found, absent, signed
  out; each usage state; each operand; a wrong operand; `jq` missing, with each of the four manager words and with
  none; a vendor command that hangs, and one that starts a child and hangs (the limit ends both); a cut script does
  nothing; `claude` only in `$HOME/.local/bin` is found; the false `claude` and `agy` record their arguments, working
  folder (`/`), stdin (empty) and `TZ`, and the test holds them to § 4 word for word; `agy` is started with nothing
  but its version flag, and `codex` is never started.
- **No secret leaves, seen and not assumed.** The temporary `HOME` holds false credential files (`.credentials.json`,
  `.claude.json`, `auth.json`, agy's sign-in file and log) with a marker token. The test fails if the marker is in any
  argument, in any environment, in stdout or stderr, or in a log line. It is seen RED once with a line that prints a
  file.
- **The person's activity does not leave.** The false `claude` prints the measured JSON with a second marker inside
  the *"What's contributing to your usage?"* section and inside a line that starts with `Current ` but fits no
  pattern. The same for Codex: transcript lines that hold a marker and the word `rate_limits`. Seen RED once with the
  filter removed.
- **The version is the only thing kept.** A false `agy` and a false `claude` print a version line with other words, a
  banner before it and a marker after it. The reply holds the digits and dots only. A version of 40 characters, of
  letters, or with no dot is "not known", and the vendor is still found.
- **The hash**: a test holds the sha256 of the script text; the spec holds the same value.
- **The parser**: a banner before the begin line; every marker and key; an unknown marker, an unknown key, a line too
  long, too many lines, an exit code that is not 0 — each `unreadable` with its reason; a percent of 101, `-1`, `1e3`,
  `NaN`; a value with control characters, with `$(`, of 10 KB; a credential-shaped value is dropped; a manager word
  outside the closed list. A missing number is `null`, and a test fails if any path turns it into 0.
- **The Claude usage lines**, as a table: the three measured lines; `3pm`, `3am`, `2:59am`; 12am and 12pm; a reset on
  1 January read on 30 December; a reset more than eight days ahead, in the past, with another zone, with no zone —
  each a dash with the percent kept; no session line; two model lines; no matched line (`failed format`); `:turns 1`
  (`failed model-call`, and the next call to that host is `no-claude-usage`).
- **The Codex record**: `primary` weekly and `secondary` null; both windows; a 300-minute window; another length; a
  reset that has passed; no record; a record further from the end than the tail; a time with milliseconds and `Z`;
  the record's time and the file's time; of two files the newer record wins.
- **The words and the rows**: the mod's cases as a table — `5h 87% left`, the dash with no `% left`, `resets in —`,
  `1h 0m`, `<1m`, a reset that has come; Codex windows named by length; the grouping (one account on two hosts is
  one group; two unknown accounts are two; equal numbers and different accounts are two); each host sentence; for a
  host without `jq`, the row shows exactly the fixed string of § 4's table for its manager word, the copy button
  gives that same string, and **a test fails if any text that came from the reply is part of the shown command**.
- **The round** (pure, with seams for the clock, the call and the terminals): one call at a time; the start moves
  one host each round; a failed host does not stop the round; three terminals of one host are one read; no call to
  a host with a "no"; no timer with no terminal; a closed host is dropped with its reading; the 60 s floor of
  *Refresh now*; the first round starts with no press, 30 s after activation. **The account first (the gate's
  finding 3; finding 4 of the re-review):** every host's first call is `no-claude-usage`, whatever is remembered of
  that host; host A answers account X, which has no usage reading in this round — A is called once more, with
  `claude-usage`, before the next host; host B answers X too — no second call; two hosts of one account are three
  calls and ONE usage read, in the first round and in every later one; **host B was last seen with account Y and
  now answers X, which was already read in this round — no second call, and X's usage is read once**; B answers a
  new account Z — one second call, in the SAME round; never a third call to a host. **The usage policy:** with
  `off`, the value E1 ships, no call is `claude-usage`, on a tick or on a press; with the hourly constant, the usage
  command is asked at most once in six rounds for an account; with "on a press", never by a tick.
- **The consent store's commit and merge (the gate's finding 0)** — pure, with two views of one backing store, of
  which one can be stale: a "no" in window A against an automatic "yes" in window B, in both orders, and with B
  holding a list from before A's write — after both commits the store holds the "no", and B makes no call once the
  "no" is visible to it; B's whole-list write is impossible by construction (a test fails if the write seam is ever
  given a list that was not read in the same step); a person's "yes" replaces an automatic one; an automatic "yes"
  never replaces any row; only a person's press changes a "no". *(Revised, entry 1.2: "a "no" that goes missing is
  written again by the window that wrote it" is gone; the next row replaces it.)*
- **The refusals file (entry 1.2)** — pure, over a fake disk with an exclusive create and a rename, and views of
  one `globalState` of which each can be STALE and writes its WHOLE copy. **The two checks of the cadence
  consultation:** (a) window A commits a "no"; A is disposed; window B, stale, commits an automatic admission — a
  whole-object write; both views synchronize and are built again as after a reload; the host is admitted again →
  **refused, with no call**; (b) the same with the Stop: A stops, A is disposed, stale B writes the state row for a
  notice → after the reload the round is stopped and makes no call. Then: two writers at once — one is admitted,
  the other waits, and the file holds both changes; a writer whose change was written over sees it at its read-back
  and writes again. **The lock (finding 3 of the re-review):** a lock whose owner is gone — no process of that
  number, or another boot id — is taken over; a lock an hour old whose owner lives is NOT; a note that cannot be
  read is not "gone"; *Save now* takes any lock. **The pause after the ownership check:** writer A claims, reads,
  passes its last look at its claim and is held there by the test; *Save now* in window B takes the lock and writes
  a refusal; A is let go and renames its stale file over B's; B's next read sees a file that went back and writes
  the union — the file holds B's refusal AND A's change, and no call to that host was made in between. **A write
  that fails, with two windows (finding 0):** the lock is held; a "no" in window A is not reported as kept, its copy
  is in `globalState` before the file's write is awaited, and its marker is on the disk; A is disposed, and a stale
  window erases the copy; window B makes NO automatic call to any host, allows none by itself, and says so; when
  the lock is free, B's next read lands the refusal and removes the marker, that host is refused and the others
  are read again; a marker that cannot be read stops the side until *Save now*, which leaves the round stopped; a
  disk that takes neither the file nor the marker leaves the refusal in the pressing window and in the copy, and
  the notice says that this window must stay open. A file
  that is damaged (not JSON, an unknown `v`, a wrong shape, more than 64 KiB, unreadable) contacts nobody and is
  written over only by a person's answer for a host, which leaves the round stopped. **The copy-in (finding 1):**
  every readable "no" row is copied at the first read; a "no" written to `globalState` alone AFTER `migratedUtc`,
  as a window of an older build writes it, is in the file at the next read; a "no" in either place refuses. The
  cap: 100 refusals, the next is not kept; a *Forget* and a *Read* in a second window of the same side lift a
  refusal at once, and a stale window that reads inside the lag brings it back (residual 4, asserted so that it is
  seen); nothing but those two and a start ever shortens the file (a test fails if any other change does).
- **The automatic "yes", the cap, the stop button and the notices**: a host with no row gets the marked row before
  its call is spawned and is read in the same round; a "no" is never replaced — not by a round, a reload, or a second
  terminal; *Forget* removes the row and the next round allows the host again — from any window of the side that
  keeps the refusal (entry 1.2; the case of entry 1.1, a *Forget* undone by the window that gave the "no", is
  gone); **the cap**: with 100 rows a new host
  drops the oldest automatic row and never a person's, and with 100 rows of a person it is not allowed and not
  contacted (100 invented hosts and one "no": the "no" holds); the session door on an automatically allowed host
  reads without asking, and on a host with no row it asks; the timer raises no question (a test fails if the ask seam
  is called from a tick); the stop button clears the timer, drops the readings, holds through a reload AND through a
  stale window's write of the state row (the Stop is in the file, entry 1.2), and stops the automatic "yes" too; the daily notice is shown at once for the first such host, then at most once in 24 hours,
  names exactly the hosts allowed since the last one, and is not shown with none; the first-round notice is shown
  once.
- **The setting, at both values, user layer only**: on, a host with no row is allowed and read; off, it is not
  contacted, says *not asked yet*, a press asks, and an automatic row counts as no answer for the round and for the
  session door, while a person's "yes" still counts; on again, the automatic row counts again; a workspace or folder
  value never decides (the four mixes of a user value and a workspace value), and the ignored layer gives its one
  row; an imported config file is refused the key by name; the key is in the manifest with `"scope": "application"`
  and a default of `true`, in `SAFETY_KEYS`, and in the help of five languages.
- **The runner**: the accounts argv is the pinned options, `--`, the host, `sh -s --` and the mode, with no `-n`; a
  host that fails the host class never reaches ssh; the session reader's argv and sentences are unchanged.
- **The manifest and the help**: the button with `view == terminal` and its group; the command, the setting and the
  changed paragraph on the per-host answer in the help of five languages; `notification-sites.json` generated again;
  the log scan (`remoteLogScan.test.ts`'s rule) covers the new modules.
- **Host scenario** (`npm run test:host`): with two terminals of two hosts and the call replaced through the test
  door, the false call is made once for each host, one after the other; with the terminals closed no call follows.
  U10 is measured here: what a label with `$(` draws.
- **Webview pages are not changed**; if one must be, it is tested by running it (`bundledPage.test.ts`).
- The installer's tests are with its design, in the appendix, § E.

## Definition of Done

- [x] `research/RESULTS_accounts_and_usage_on_a_vm.md` records step 0 and W1–W5 on one VM (2026-11-19).
- [ ] **E1.** The icon opens a list of the Claude Code, Codex and Antigravity accounts on the hosts of the open
      `Creds:` terminals, grouped by account, each group naming its hosts; usage as "left" in the mod's words, a
      missing value never a zero, usage that was not read saying why; Antigravity with its version.
- [ ] Every host says how it stands in its own sentence: turned off, allowed automatically and when, not asked yet
      (the setting off), never read, failed, or `jq` missing with the exact command to run.
- [ ] A round runs by itself from the start of the extension, every 10 minutes, one host at a time, one call for each
      distinct host that is not refused, and one more on the one host that reads an account's usage (§ 2), so each
      account's usage is read at most once in a round; a host with no answer is allowed by itself,
      stored with its marker and named in a notice at most once a day; it stops with the last `Creds:` terminal and
      with the stop button; the timer asks nobody.
- [ ] **A "no" always wins, and so does the Stop** (revised, entry 1.2): both are kept in the refusals file of the
      side, written under its lock and read from the disk before every call; only a person's press removes one; no
      round, reload, second terminal, second window, full store, damaged file or write of `globalState` removes a
      refusal or the Stop on the side that keeps it — shown by the file's tests, the two checks of the cadence
      consultation among them, and by R12. A refusal or a Stop that the file did not take is in the copy at once
      and stops every window of the side through its marker; the lock is never taken over by its age; a "no" from
      a window of an older build is in the file at the next read (the re-review's findings 0, 3 and 1). Every write
      of the store that stays in `globalState` re-reads and merges.
- [ ] `coai.terminalAccountsAllowNewHosts` exists, is on by default, is read from the user layer only, and is in the
      help of five languages; off, it restores ask-first.
- [ ] The accounts script is fixed, its hash is in the spec and in a test, and it writes and installs nothing on the
      VM. No secret is in an argument, the environment, the reply or a log; the person's activity is not in the
      reply; of a version command only the version is.
- [ ] Road (a) with the pinned command of § 4. Road (b) is not in the code. Nothing of a reading is persisted.
- [ ] The session reader behaves as before, but for the words of its question and of its hosts list.
- [ ] Docs with the change: the help in five languages; `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md` (the new edge from a VM to a vendor); `SECURITY.md`; the `POST_DEPLOY.md` question
      asked.
- [ ] **E2.** R1–R12 and W6–W8 each have a recorded result in this plan and in the RESULTS file. The gates W6
      (growth) and W7 (program starts) passed, or their stated consequence is in the release build. No build ran the
      Claude usage command by itself before W6 and W7 had recorded results. `SECURITY.md` says what was seen.
- [ ] **C1**, the installer of owner decisions 17, 19 and 20, is built by its own stories and spec and passed its
      own checks (the appendix, § E); `coai.terminalAccountsInstallMissingTools` exists and is on by default.
- [ ] `review_feature` ran before the tag and every finding is resolved; one extension release is cut and verified;
      the plan is promoted with its deviations.

## Epics

**Two epics, by the gate's command and the question consultants' answer** (consult `f4af076c`, both consultants). The
earlier cut — a reader and an installer, then a round and a list — was layers: nothing ran end to end until the last
story. Now **E1 is the smallest thing that runs end to end on real input**, and **E2 runs it for real and tests what
this plan believes**, with pass criteria written here, before any code. What comes after is a list of candidates,
one line each, with the E2 result that would justify it.

**Step 0 is not an epic any more.** It was E0 in the earlier revisions. It had no code and is done; it is recorded in
*What step 0 and W1–W5 measured* above, in the RESULTS file, and — with what did not go as written — in the appendix,
§ D. The ceiling of two epics is why it is a section and not a heading here.

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md` (`subagent-models.md`, pipeline step 3).
Cornerstone quota: Q = ⌈2·E/5⌉ = ⌈2·2/5⌉ = 1. The cornerstone areas Fable names: **(a)** what the script touches on
the VM and what it prints — the only place a secret or the person's activity can leak, and a leak gives no error;
**(b)** the reply's frame and the parser — the only place wrong or hostile data can arrive and look right; **(c)** who
is contacted and when — a refused host is never contacted and never allowed again by anything, every write of the
store merges, the cap never trades a person's answer for an automatic one; **(d)** absent, signed out, no usage
source, skipped, failed and unreadable are different results, and a missing number is never a zero. Area (c) is
authorisation: its spec is Fable's by the security row of `subagent-models.md`, outside the quota.

Cornerstones: 1 of 1 — E1.S2

### E1 — The read-only feature, end to end

- **Goal.** The icon, the list, and the round that fills it by itself, reading real hosts — with nothing installed
  and nothing written on a VM by our code.
- **Scope.** `src/remoteSsh.ts` (the runner widened for a second caller, `SSH_OPTIONS` untouched);
  `src/remoteFrame.ts` (the framing extracted); `src/remoteRead.ts` and `src/remoteSessionHost.ts` (the store's commit
  and merge, the marker, the cap, the question's words, the hosts list); a NEW module for the refusals file and its markers, and
  `src/chatStoreLock.ts` widened so that its claim serves a second file and is given the rule for when a held lock
  may be taken over (entry 1.2; the re-review's finding 3); NEW pure modules for the accounts script,
  its reply and typed result, the usage lines, the round, the words and rows; a NEW host module with the `vscode`
  seams (the timer, the terminal events, the QuickPick with its buttons, the state row, the notices, the test door);
  `package.json` (the command, its icon, the `view/title` entry, the setting with its scope); `src/settingsShape.ts`,
  `src/modelKeys.ts` (`SAFETY_KEYS`), `src/configTransfer.ts`; the help in five languages; `notification-sites.json`;
  the tests and the host scenario; the module documents, `research/architecture.md`, `SECURITY.md`; the
  `POST_DEPLOY.md` question (the file is at its cap of twelve, so a new item needs a named one to leave).
- **Depends on.** The terminal-door plan's last release cut.
- **Done when.** Every row of the test plan is green; the marker tests were seen RED once; the hash is in the spec and
  in a test; the session reader's tests are green, changed only where they hold the question's words; the code gate
  passed; the PR is merged; a pre-release VSIX of the merged head is built, with the Claude usage read off
  (`USAGE_POLICY` `off`; the re-review's finding 2).
- **Stories** — written by Opus on 2026-11-19 in
  [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md).
  - **E1.S1 — the shared store: a "no" that always wins, the automatic "yes", the setting (SECURITY SPEC by Fable,
    outside the quota).** The one commit function that re-reads and merges; the merge rule; **the refusals file —
    its format with `gen`, its lock that is never taken over by its age, its one writer, a damaged file, a write that
    does not land with its copy and its marker, a file that went back, the copy-in at every read, the sides
    (revised, entry 1.2 and the re-review's findings 0, 1 and 3; "the refusal that is repaired" is gone)**; the marker `how`; the cap that never drops a
    person's answer; the user-layer read of `coai.terminalAccountsAllowNewHosts`; the question's new words and the
    hosts list. Fable writes the spec and the scenarios before any code; Sonnet codes; Opus reviews. The story keeps
    its name and its number; whether the file is a story of its own is the re-review's to say.
  - **E1.S2 — the accounts script, its reply and its parser (CORNERSTONE, `model: fable`).** The exact script text
    and its sha256; the pinned Claude commands with their flags, folder and limits; the `agy` version command and the
    rule that keeps only a version; the three line patterns; the Codex record rule; the manager word for a host
    without `jq`; the reply's grammar; the typed result; the parser's rules with the reset parse; the scenarios; and
    the security judgement on the exact text. Sonnet codes; Opus reviews.
  - **E1.S3 — the runner and the round.** One argv builder and one run for both callers; the pure planner: the
    distinct hosts, the order, the account-first rule — every host's first call reads no usage, and the one usage
    call of an account follows the reply (the re-review's finding 4) — the usage policy's constant, which E1 ships
    `off` (finding 2), start and stop.
  - **E1.S4 — the list and the icon.** The words and rows, the grouping, the host sentences, the fixed command for a
    host without `jq` with its copy button, the notices, the two title buttons, the command and its manifest entry.
  - **E1.S5 — the documents.** The help, the module documents, `research/architecture.md`, `SECURITY.md` but for
    what E2 has to see first.

### E2 — Run it for real, on the owner's hosts

- **Goal.** Know, from the owner's own hosts and a real VS Code window, whether what this plan believes is true —
  before anybody else gets the build.
- **Scope.** No product code but ONE constant. **Two pre-release builds (revised at the re-review, finding 2).**
  *The first build* is E1's merged head, with the Claude usage read off. *The second build* comes from one small PR
  that sets `USAGE_POLICY` to the row W6 selected, with the tests that hold the constant; it is made only after W6
  and W7 both have recorded results, and not at all when W7 failed. The plan orders that PR, so it is not a change
  of the plan. Also: the owner's window on Windows and the owner's hosts; the measurements below, each with the
  owner's go where a command runs on a VM; the results written into this plan and into the RESULTS file; the last
  sentences of `SECURITY.md`. A criterion that fails is fixed by a new PR and measured again.
- **Depends on.** E1 merged, and its pre-release VSIX installed by the owner.
- **The order.** W6 and W7 first: both are run by hand on a VM and need no window. Then the first build: R1, R2,
  R5, R6, R8, R10, R11, R12 and W8, the first run of R7, and the halves of R3 and R9 that need no Claude usage.
  Then the second build: R4, the second run of R7, and the Claude halves of R2, R3 and R9.
- **Done when.** Every row below has a recorded result. **W6, W7 and R12 are gates:** each passed, or its stated
  consequence is in the build. When W7 failed there is no second build, and the rows that need one record that.

| # | What is run | Pass |
|---|---|---|
| R1 | The icon in the terminal panel's title bar | It sits beside *Ask other AI* and opens the list; with no `Creds:` terminal the list says so |
| R2 | The first start after the install, with no press — on the first build, and again on the second | Within 60 s: exactly 1 ssh login on each host that is not refused (the VM's ssh log), also for a host with two terminals — and on the second build exactly 1 more, on ONE host of each Claude account and on no other; 0 on a refused host; the first-round notice shown once; a host that was never asked is read and shown as *allowed automatically*; the daily notice names it once, and not again the next day |
| R3 | The numbers, on at least 3 hosts | **On the second build:** each Claude percent equals `/usage` run on that VM in the same minute, and the mod line within 1 point, with the same dashes. **On either build:** the Codex percent equals the record; the agy version equals `agy --version` there; no row is cut at the owner's window width. On the first build each Claude account says that its usage is not read |
| R4 | Two hosts of one account, 6 rounds, on the second build | One group naming both hosts; in each round `policy-limits.json.stamp.json` changes on exactly one of the two hosts, and that host has 2 logins and the other 1 |
| R5 | A host that cannot be reached | Its own sentence within 30 s, and every other host is still read in that round |
| R6 | Stopping | Every `Creds:` terminal closed: 0 logins in the next 20 minutes. *Stop the accounts round*: 0 logins in 20 minutes, also after a reload; started again, a round follows within 60 s |
| R7 | Two hours, at least 12 rounds, on a host WITH live sessions — run TWICE: on the first build (the account, Codex and agy reads) and on the second (with the Claude usage read) | In each run: **0** live Claude, Codex or agy sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart. The time and size of the agy program before and after: recorded |
| R8 | The session door and the hosts list | *Take the question* still works, and on an automatically allowed host it reads without asking. *Not this host*: 0 logins in 20 minutes, also after a reload and after the terminal is opened again. *Forget*, pressed in ANY window of the side that keeps the refusal — and at least once in a window other than the one that gave it (revised, entry 1.2; the run condition of entry 1.1 is gone): the host is read in the next round, and the list says the host was allowed again. A lift that another window undoes inside the lag (residual 4 of the file) is RECORDED with its times and is not a failure of this row: the host is then shown as turned off, and one more press lifts it |
| R9 | The time of a read (the plan's M7): 10 reads of each host from Windows with the pinned options — the first call on the first build, the usage call on the second | None is killed at 30 s; the slowest is under 20 s; `agy --version` is under 4 s; a reply is at most 64 lines and 8 KiB. The median is recorded. A host of an account with no 5-hour window shows `5h —` |
| R10 | **Which hosts lack `jq`** — every host of the owner's `Creds:` terminals | A table: `jq` there or not, the manager word, root or `sudo` without a password. A host without `jq` shows the fixed command for its manager; after the owner runs it, the next round reads the accounts. The count of hosts without `jq` is recorded for C1 |
| R11 | The setting off, in the user settings | 0 logins to a new terminal's host in 20 minutes, and it says *not asked yet*; 0 logins to a host that was allowed automatically, until *Read* is pressed; a workspace `settings.json` that says the opposite changes nothing; the same in a WSL or remote window, if the owner uses one |
| R12 | **Two windows (the gate's finding 0, U12; revised, entry 1.2).** Four runs, each with the VM's ssh log read. **(a)** *Not this host* is pressed in window A while window B's round is running. **(b) The first check of the cadence consultation:** *Not this host* in A; A is closed at once; B, which was open throughout, goes on for three rounds and is made to write `globalState` in them (a new host's terminal is opened, so B writes an automatic row); both are reloaded. **(c) The second check:** *Stop the accounts round* in A; A is closed at once; B goes on for three rounds; both are reloaded. **(d) Two sides, where the owner uses two** (a Windows window and a WSL or remote window, each with a terminal of that host): *Not this host* on one side | **Gate.** (a) At most 1 login to that host after the press — a call that was already running — and 0 from then on, in both windows. (b) 0 logins to that host from the press on, the refusal is in the file and shown in both windows after the reload. (c) 0 logins to any host from the press on, and both windows show the round stopped after the reload. In (a)–(c) the file is read back and holds the refusal or the Stop. (d) is a RECORD, not a gate: how many logins the other side made after the press, how long it took to stop, and whether the copy in `globalState` was still there after the three rounds. The lag until B's `globalState` showed A's write: recorded (U12). **A lost "no" or a lost Stop in (a)–(c) fails the gate:** the build is not released, and the cure is a change of this plan. A copy lost in (d) is the evidence for what is left of candidate C4 |
| W6 | Backups (§ 5), by hand, before any build reads Claude usage by itself | **Gate for growth.** The row of § 2's policy table that the result selects is the policy of the second build and of the release |
| W7 | The trace of every program start (§ 5), by hand, before any build reads Claude usage by itself | **Gate.** No program is started that is not Claude Code's own. Else the Claude usage read stays off until the owner decides, and no second build is made |
| W8 | A start of `agy` (§ 5) | A record |

### Candidates after E2

One line each. A candidate becomes an epic only with the result named beside it. Taking one up is a change of this
plan (kind `epics`), recorded in the ledger — **but for C1, which this approved text already schedules**.

- **C1 — the `jq` installer** (owner decisions 17, 19 and 20; the setting `coai.terminalAccountsInstallMissingTools`,
  on by default). **The owner's decision stands, and the installer ships in the same release.** It is built after E2
  only so that the read-only path is proven first; R10's table says on how many hosts it will act. Its design, with
  the gate's findings 1, 2 and 6 folded in, is in the appendix, § E. It is a cornerstone when it is built: a change
  of a machine with root, spec by Fable under the security row, finding F2 beside it.
- **C2 — a webview instead of the QuickPick.** Justified by R3 failing on "no row is cut".
- **C3 — hosts with no AI CLI are read once an hour, not every 10 minutes.** Justified by R10 or R2 showing such
  hosts among the owner's terminals, and by their logins being a nuisance in the ssh log.
- **C4 — the consent store in one file with an exclusive lock.** Justified by R12 failing. **Partly taken by ledger
  entry 1.2, before E2:** the person's refusals and the Stop are in such a file from E1 on. **What remains a
  candidate, in two parts.** *C4a — the rest of the store in the file:* the person's "yes", the automatic rows and
  the notice times. Justified by R12 or R8 showing a withdrawn "yes" that came back with the setting off (residual 3
  of the file), or lost rows that make the daily notice a nuisance. *C4b — a lift that is recorded, and the Stop
  carried into the file of every side.* The re-review's finding 1 took the first half of what C4b was: each side
  now copies a refusal it sees into its own file. What is left: a record of what was lifted, so that a stale copy
  is not taken again (residual 4 of the file) and a lift on one side can reach the others; and the same carrying
  for the Stop. Justified by R8 or R12 showing a lifted refusal that came back, by run (d) of R12 showing a copy
  that was lost, or by the owner working on two sides with terminals of the same hosts.. The owner answered on
  2026-11-24 that a second side is used for the chat only, so this justification is not met.
- **C5 — the Claude usage only on a press, or road (b), or the account only.** Justified by W7 failing; the planner
  recommends the account only.
- **C6 — one lock between VS Code windows for the round.** Justified by R2 or R9 showing that two windows doubling
  the logins matters.
