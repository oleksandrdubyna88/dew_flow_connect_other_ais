# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **STOPPED, stopped for re-review, 2026-11-25 — revised whole as the baseline of epoch 3; NOT approved.**
> Epic E1 is merged (#1047, `d0241e45`): a read-only round every 10 minutes and a list, with the Claude usage read
> off. The owner checked its pre-release VSIX 2.0.901 in a real window on 2026-11-25. That check gave the ledger
> entries 2.2 (WSL is read on this PC, not over ssh) and 2.3 (one permission for all hosts, once), which stopped the
> plan, and it found that the list could not be read and showed no number. This text is step 4 of *The full stop*
> (`plan-change-control.md`): the goal, the scope, the design, the Definition of Done, the merge conditions, the
> epics and the build order, written again against what is known today. It is the baseline of epoch 3 and adds no
> ledger entry. **No code of any epic of this plan is written, and no pull request that carries its work is merged,
> until the owner approves epoch 3** — but for the cut that step 6 of the stop orders (*Build order*, step 1). Four
> questions for the owner are open (*Owner decisions*, Q1–Q4). Scope: `src_vs_code` only — the icon in the terminal
> panel's title bar; ONE permission to read, asked once; a tab with a table, by host, of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC. The
> epics E2 to E5 only read; the install of `jq` (owner decisions 17, 19 and 20) is the last epic, E6. No change to
> `src_mcp`, `src_server` or CredsForDevs. The plan as it stood at the stop (138 KB) is in the appendix, § I, word
> for word: [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_appendix.md).

Related: [../research/RESULTS_accounts_and_usage_on_a_vm.md](../research/RESULTS_accounts_and_usage_on_a_vm.md) (step 0
and W1–W5, measured 2026-11-19), [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md)
(its E2 built the read-only ssh reader and the per-host answer of the session read),
[../research/RESULTS_vendor_quota_gauges.md](../research/RESULTS_vendor_quota_gauges.md),
[../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md), and the stories document of
E1, [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md),
with its two specs.

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

> Epoch 2 · approved 2026-11-24 · by owner · state: stopped

- 2.1 · 2026-11-24 · owner · contract — E1.S1 module layout and seams differ from its spec; the spec's named sites follow the code
  (The owner chose to record it, 2026-11-24. The coai consultants could not be asked: the server refused every tool
  because its data folder was missing at its start. Behaviour, words, notice codes and the census of 186 are as the
  spec says. What moved is listed in the S1 spec, section "Entry 2.1, the layout as built".)
- 2.2 · 2026-11-25 · owner · scope, approach — WSL is read on this PC: every distro and every Linux user, not over ssh
  (The owner, at the real-window check of the pre-release VSIX 2.0.901 from main `d0241e45` (#1047): a `Creds: wsl / …`
  terminal is a local WSL terminal, so the round's `ssh wsl` fails every 10 minutes. The owner: "wsl must read every
  WSL on my PC, all accounts": every distro of `wsl -l` and every Linux user in it, read-only.)
- 2.3 · 2026-11-25 · owner · approach, contract — one consent for all hosts, once: a "yes" reads every host now and later
  (The owner, same check: "I may have 20 hosts in the terminal, and it asks for each. No. Once: may I read? I say yes,
  and it reads all." A "Not this host" for one host stays. These two changes stopped the plan for a whole re-review.
  The same check also found that the list shows no number at all: Claude usage is off in this build, the Codex week
  window had no reading, and "read just now" did not change in 10 minutes. The re-review takes these up as well.)

### Re-review for epoch 3

Narrowed: yes — the automatic allow of a new host, its setting, its two notices and the QuickPick list leave the scope and the Definition of Done (the owner's entry 2.3 and the same check); the permission, the table and the WSL readings are added

| Work | Where | Verdict | Action |
|---|---|---|---|
| E1.S1 — the refusals file of each side, its lock, its markers, the copy-in and the keeper (`remoteRefusals.ts`, `remoteRefusalsDisk.ts`, `remoteKeeper.ts`), #1047 `d0241e45` | main | fits | kept; E2 adds the permission to the same file and the answer `waiting` |
| E1.S1 — the automatic "yes" of a new host (`how: 'auto'`), the cap rule that drops one, the timer's use of an old "yes", the setting `coai.terminalAccountsAllowNewHosts`, the daily notice and the first-round notice, #1047 | main | cut | corrected in new pull requests: the cut of *Build order* step 1 turns the round off at once, and E2 retires each piece |
| E1.S1 — the session question's wider words ("the Claude session and the AI accounts"), #1047 | main | cut | corrected in E2: the question is the session read's again |
| E1.S2 — the accounts script, its reply and its parser (`remoteAccountsScript.ts`, `remoteAccountsReply.ts`, `remoteAccountsUsage.ts`, `remoteFraming.ts`), #1047 | main | fits | kept; reworked in E3 as revision 2 of the script: its own `PATH`, a usage call that reads Claude only, the time table of T46 |
| E1.S3 — the runner's accounts call and its limits (`remoteSsh.ts`), #1047 | main | fits | kept; E3 adds a second road beside it |
| E1.S3 — the round (`remoteAccountsRound.ts`): one call at a time, the account first, the checks before a call and before a result, the usage policy `off`, #1047 | main | fits | kept; reworked in E2 (the door asks for the permission) and in E3 (a reading for each place, not for each host) |
| E1.S3 — `ssh` to a host named `wsl` from a Windows window, #1047 | main | cut | corrected in the cut of step 1 (no call to `wsl`), then replaced by E3 |
| E1.S4 — the list as a QuickPick, its rows grouped by account, its host scenario (`remoteAccountsHost.ts`, `remoteAccountsRows.ts`), #1047 | main | cut | replaced in E2 by the tab; the `jq` table and the one road for VM text move to the tab's model |
| E1.S4 — the words of usage (`usageWords.ts`), #1047 | main | fits | kept; the cells of the table use them |
| E1.S5 — the help in five languages, `SECURITY.md` and the module documents, #1047 | main | cut | rewritten by E2 where they describe the automatic allow and the list; the rest is kept |
| The pre-release VSIX 2.0.901, built from `d0241e45` | the owner's machine | cut | never published; replaced by the pre-release of E2 |
| The documents of epoch 2: the stories document of E1, its two specs, the record of gate G2 | main | fits | kept as the record of E1; Opus rewrites what changes when E2 and E3 start |

  (The verdicts are the planner's, written with the revision on 2026-11-25. Step 5 of the stop gives the sorting to
  an Opus judge: the judge confirms or changes each row before the owner is asked.)
  (The consultations of the stop: `cons` `78e6d44b` on entries 2.2 and 2.3, and `consask` `7315feb0`, both question
  consultants, on the view. Their answers are folded into the revision.)
  (By `plan-change-control.md`, *The full stop*, step 4, what a re-review changes is the baseline the next epoch is
  approved on and never an entry. So none of these is one: the tab in place of the QuickPick; a reading for each
  place; revision 2 of the script with the time table of T46; the door answer `waiting` of T8 and the re-read before
  every spawn of T9; the WSL check W9; the epics E2 to E6 and their order; the merge conditions.)
  (Epoch 3 is narrowed, so the owner approves it. No epoch 3 line stands here until then.)

## The goal

The owner, 2026-11-19 (in Russian, translated): *"Next I want to add an icon, like the chats one. It should show a list
of all Claude Code, Antigravity and Codex accounts used in the open SSH connections in the console, and their usage.
Can we?"* Then: *"Real time is not needed. We can go over ssh every 10 minutes, in a round, and ask for the data."*

The terminal panel holds many terminals named `Creds: <host> / <session>`, for example `Creds: host-a / build`,
`Creds: host-b / gate`, `Creds: wsl / notes`. Each ssh host is a Linux user on a VM with its own `~/.claude`; `wsl` is
this PC's own WSL. Nothing in VS Code says which account each of them is signed in to, or how much of its limit is
left. To learn it the owner opens each session and asks there.

**What the owner said at the real window, 2026-11-25**, about the build of E1:

- *"wsl must read every WSL on my PC, all accounts."* (entry 2.2)
- *"I may have 20 hosts in the terminal, and it asks for each. No. Once: may I read? I say yes, and it reads all."*
  (entry 2.3)
- *"Something was read, but who knows what. It must be readable: which host, which AIs, which accounts, the limits,
  when they reset. Redo it."*

So the goal of epoch 3 is one sentence: **after ONE "yes", the owner opens a tab and reads, for each host and each
Linux user of this PC's WSL, which AI programs are there, which account each is signed in to, how much of each
limit is left and when it resets — and can trust the time each line was read.**

## Owner decisions

Decisions 1–20 are of 2026-11-19; the owner's words and the questions as asked are in the appendix, § A. Each line
says how it stands after the stop.

1. An icon beside the chat icon in the terminal panel's title bar opens the view. **Stands.**
2. No real time: a round every 10 minutes. **Stands.**
3. Claude usage: Claude Code itself makes the call on the VM, so our script never touches the token. **Stands.**
4. Several hosts may share one account; usage is read once for each account in a round. **Stands for the read.**
   The view groups by HOST now (decision 23), and a shared account says so on each host.
5. The same numbers as the agent-models mod line: "left", a dash for a missing value, never a zero. **Stands.**
6. **O1:** the `curl` road (b) is allowed as a fallback and not built. **Stands.**
7. **O2:** full email addresses are shown, in the view only. **Stands.**
8. **O3:** no separate consent for the accounts read and the session read. **Stands as "one consent"; that consent
   is the permission of decision 22 (Q1).**
9. **O4:** the interval is a fixed 10 minutes. **Stands.**
10. **O5:** the passive Codex read is enough. **Stands.**
11. **O6:** the icon is always visible. **Stands.**
12. **O7:** step 0 ran on one VM as its `root` user on 2026-11-19. **Done.**
13. **N1:** the four cache files Claude Code writes on a read are accepted, and a rewrite of `~/.claude.json`. It
    does not cover a live session that is disturbed, nor files that grow without bound. **Stands**, for a VM and
    for a WSL user.
14. **N2:** the round runs by itself from the start of the extension. **Replaced by 22:** only after the permission.
15. **N3:** answered by the run of W1–W5. **Done.**
16. **N4:** `agy --version` runs in every round. **Stands.**
17. **N5:** where `jq` is missing it is installed, when the user may; else the view shows the exact command.
    **Stands; it is E6.** Until E6 the view shows the command.
18. **Q1:** a new host is allowed by itself, with a daily notice. **Replaced by 22.**
19. **Q2:** the install needs no press. **Stands for E6, with Q3 open.**
20. Two settings, on by default. **The first, `coai.terminalAccountsAllowNewHosts`, is retired by 22.** The second,
    `coai.terminalAccountsInstallMissingTools`, arrives with E6.
21. **2026-11-25, entry 2.2.** A `Creds: wsl / …` terminal is a local WSL terminal. WSL is read on this PC: every
    distro of `wsl -l` and every Linux user in it ("all distros, all users"), read-only, not over ssh.
22. **2026-11-25, entry 2.3.** One permission, once: "may I read?" — a "yes" reads every host, now and later. A
    *Not this host* for one host stays.
23. **2026-11-25, the same check.** The view is redone so that it can be read: which host, which AIs, which
    accounts, the limits, when they reset.

**Open questions for the owner.** The facts of the stop do not settle them. The text takes the stated line for each;
an answer the other way changes the named section before epoch 3 is approved, and is no ledger entry.

| # | The question | The line this text takes |
|---|---|---|
| Q1 | Does the ONE permission also cover the read of a terminal's Claude session by the chat doors (*take the question*, *add the question*)? | **Yes** (decision 8: one consent, not two). The question's text says so. The other way: the chat keeps its per-host question, and the permission covers the accounts round only (§ 3) |
| Q2 | A WSL distro that is NOT running: a read would start it, every 10 minutes, with its services | **It is listed and never started by CoAI**; it says *not running, so not read*. The other way is candidate C7 |
| Q3 | The installer (E6) under the new consent. Decision 19 said "no press", when a per-host "yes" stood behind it. A permission worded "may I read?" cannot cover a change of a machine | **Nothing is installed under the permission to read.** E6 opens with this question: one more "yes", once, for "install `jq` where it is missing", or the setting of decision 20 alone |
| Q4 | The merge conditions ask the owner to check a VSIX built from the head of each of E2, E3 and E4 in a real window BEFORE that epic merges (the check of 2.0.901 came after the merge, and found three things) | **Yes**, three checks, each a short table of rows (*Epics*) |

## What the code does today — verified on `d0241e45` (main, after #1047)

All paths are under `src_vs_code/` unless they start with `research/`, `todo/` or `.agents/`. No extension release
holds this code: the newest tag is `extension-v2.0.0`, cut before #1047.

- **The host comes from the terminal's name.** `credsSession(name)` (`src/credsSession.ts:65`) matches
  `^Creds: (<host>) / (<session>)$` (`:36`); the host class is `[A-Za-z0-9][A-Za-z0-9._-]*` (`:31`). Nothing in the
  code knows that `wsl` is not an ssh host: `accountsArgv` (`src/remoteSsh.ts:55`) gives it to `ssh` like any other.
- **The runner.** `runAccounts(host, mode)` (`src/remoteSsh.ts:155`) sends `ACCOUNTS_SCRIPT` on stdin to
  `ssh <pinned options> -- <host> sh -s -- <mode>`, with `SSH_OPTIONS` (`:30`) unchanged, an absolute `ssh` path
  (`:82`), 30 s and 64 KiB (`ACCOUNTS_LIMITS`, `:101`) and `killTree` (`:194`). The tree is killed on Windows only
  (`src/processLauncher.ts:64`); see [PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md](PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md).
- **The script.** `ACCOUNTS_SCRIPT` (`src/remoteAccountsScript.ts:72`), 222 lines, pinned by its sha256 in a test.
  It puts `$HOME/.local/bin` IN FRONT of the inherited `PATH` (`:276`). Vendor limits 3, 2, 8 and 4 s (`:35`),
  reader limits (`:38`), the ceiling of 30 s (`:41`). The mode `claude-usage` runs every stage of the other mode
  and the usage command.
- **The round.** `runRound` (`src/remoteAccountsRound.ts:387`): the distinct hosts of the live `Creds:` terminals
  (`accountsHosts`, `:66`), one at a time; every host's first call is `no-claude-usage`; one more call on the host
  that first answers with an account whose usage is due. `USAGE_POLICY` is `off` (`:39`). The memory holds ONE
  entry for each host (`publishedState`, `:268`), so two readings of one host would overwrite each other. Before a
  call and before a result the generation, the Stop and the door are checked (`checked`, `:282`).
- **The door allows a new host by itself.** `HostKeeper.admit` (`src/remoteKeeper.ts:355`) answers `read` for a
  "yes"; for a host with no row it writes an automatic "yes" (`automatic`, `:376`; the marker `how: 'auto'`,
  `src/remoteHosts.ts:21`) while `coai.terminalAccountsAllowNewHosts` is on (`package.json:637`,
  `src/modelKeys.ts:27`, `src/sideConfig.ts:67`). `consentOf` (`src/remoteHosts.ts:126`) is the one function that
  says whether a host may be contacted. A person's "no" and the Stop are in `terminal-hosts/refused.json` of each
  side (`RefusalsFile`, `src/remoteRefusals.ts:44`), written under a lock, with markers for a change that did not
  land.
- **A waiting marker reads as `damaged` at the session door.** `waitingConsent` (`src/remoteKeeper.ts:399`); the
  door then says that the list cannot be read (todo T8).
- **A press reads the consent once.** `remoteRoad` (`src/remoteRead.ts:426`) asks the door at `:337`; the later
  spawns of the press, at most three (`CALLS_MAX`, `:305`), check only the terminal (`readOnce`, `:348`; todo T9).
- **The session question was reworded** to *"Read the Claude session and the AI accounts on “x” over ssh?"*
  (`src/remoteHostsSaid.ts:47`), code `read-remote-session` (`src/remoteSessionHost.ts:58`).
- **The view is a QuickPick** (`src/remoteAccountsHost.ts:456`), opened by `coai.terminalAccounts`
  (`package.json:288`; `view/title`, `navigation@91`, `:1046`), grouped by account (`accountsRows`,
  `src/remoteAccountsRows.ts:408`). It is drawn again only when a read lands, so *read just now*
  (`readAge`, `:105`; `agoWords`, `src/gaugeWords.ts:92`) stays as it is between two rounds. Two notices:
  `allowedNotice` and `everyTenNotice` (`src/remoteAccountsHost.ts:385`, `:402`).
- **A tab with a table exists once, and is the pattern.** `RoundsLogPanel` (`src/roundsLogPanel.ts`): one
  `WebviewPanel` for each window, revealed when it is open (`:94`, `:98`), `enableFindWidget` for Ctrl+F (`:104`), a
  nonce (`src/webviewNonce.ts`), a `ready` handshake and a record of what the page was told. A page is tested by
  RUNNING it (`.agents/PROJECT.md`; `src/test/bundledPage.test.ts`, `src/test/pageDom.ts`).

### What was measured, and what the real window showed

- **Step 0 and W1–W5, 2026-11-19, one VM** (the RESULTS file; the plan's tables: the appendix, § D). `sh` is
  `dash`; `claude` and `agy` are in `~/.local/bin`. `claude auth status --json` names the account in 0.3 s and
  writes no file. `claude -p "/usage" …` prints the usage in 3.6 s with no model call and rewrites four cache files
  of its own. Codex: the last `token_count` record of a session file holds the limits. agy states no usage.
  **Not settled:** who rewrote `~/.claude.json` during one read (W6), and what a usage read starts (W7).
- **The real window, 2026-11-25, VSIX 2.0.901.** (1) `ssh wsl` failed every 10 minutes. (2) The owner rejected the
  per-host standing of 20 hosts. (3) The list showed no number: the Claude usage is off in that build, and the
  Codex week window of one host had no reading. (4) *Read just now* did not change in 10 minutes. (5) The rows
  could not be read as "which host, which AI, which account, which limit".
- **Found by the review of #1047, as todos:** T46 — the `claude-usage` path is 31 s at worst against the 30 s
  ceiling ([PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md](PLAN_the_usage_mode_stays_under_the_30_s_ceiling.md));
  T9 ([PLAN_a_press_rereads_the_consent_before_each_spawn.md](PLAN_a_press_rereads_the_consent_before_each_spawn.md));
  T8 ([PLAN_a_waiting_marker_has_its_own_door_sentence.md](PLAN_a_waiting_marker_has_its_own_door_sentence.md)).
  This revision takes the three up: T46 in § 4, T8 and T9 in § 3.
- **Not measured: anything about WSL.** W9 (§ 5) comes before the WSL road is specified.

## The design

**A place** is what one reading belongs to: an ssh host, or one Linux user of one local WSL distro. **A scope host**
is the host word of a terminal's name. An ssh host is its own one place. The scope host `wsl`, in a Windows window,
stands for the WSL places of this PC. The consent, the terminal that makes a place live and the refusal are of the
scope host; a reading, its time and its failure are of the place.

### 1. The view — a tab with a table, by host

- **The command stays**: `coai.terminalAccounts`, "AI accounts and usage", icon `$(account)`, in `view/title` of the
  terminal panel at `navigation@91`, always visible (decisions 1 and 11), and in the command palette.
- **It opens an editor tab, not a QuickPick** (both question consultants, `7315feb0`): ONE `WebviewPanel` for each
  window, on the pattern of `RoundsLogPanel`. A second press reveals the tab that is open. `enableFindWidget` gives
  Ctrl+F. `retainContextWhenHidden` keeps the scroll place; the page keeps no other state. The QuickPick of E1, its
  rows and its item buttons are removed.
- **The page is grouped by HOST.** A toolbar, then one block for each scope host of the live `Creds:` terminals, in
  name order:

  ```
  [Refresh now] [Stop the round]            last round 3 min ago (14:03) · 14 of 16 read

  host-a      read 3 min ago (14:03)                                              [Not this host]
    Tool          Account                           5-hour window       Weekly window
    Claude Code   user@example.com · Example Org    44% left            23% left
    2.1.326       plan team · shared across 2 hosts resets in 2h 10m    resets in 2d 14h
                                                                        Fable 51% left
    Codex         account name not shown by Codex   —                   86% left
                  plan prolite                                          resets in 4d 23h
                  from a Codex session 2 d ago
    Antigravity   its account is not read           agy states no usage
    1.5.12
  host-b      the last read failed 3 min ago (14:03): “host-b” did not answer over ssh within 30 s.
    (the table of the read at 13:40, marked as older)
  host-c      not allowed: your answer for this host is “Not this host”           [Read this host again]
  host-d      jq is missing on this host, so nothing can be read there.
              Run there: sudo apt-get install -y jq                               [Copy install command]
  wsl — this PC    2 distros · read 3 min ago (14:03)                             [Not this host]
    Ubuntu · user-a    read 3 min ago (14:03)
      (its table)
    Ubuntu · root      no Claude Code, Codex or Antigravity found
    Debian             not running, so not read
  ```

- **A host's heading** holds the host's name and its latest read result, one of: *read <age> (<clock time>)*;
  *never read*; *not allowed*, with the reason; the failure IN FULL — the runner's own sentence — with its age; or
  the missing-`jq` explanation. *Not this host* is on the heading of every host that is not refused; *Read this
  host again* on a refused one; *Copy install command* beside the missing-`jq` explanation. A host whose last read
  failed keeps its older table under the heading, marked with the time of that read.
- **One row for each tool that is installed there**, in the fixed columns **Tool | Account | 5-hour window | Weekly
  window**. A tool that is absent has no row; a place with no tool says so in one line.
  - *Tool:* the name; under it, muted, the version.
  - *Account:* for Claude Code the full email and the organization (decision 7); under it, muted, the plan; and
    *shared across N hosts* when N places of this window answer with the same account — each of them then shows
    the SAME figures, those of the one usage read (§ 2). Two places are one account only when the vendor and the
    whole account text are equal; an account that is not known is never shared. Codex states no account name, so
    the cell says so and shows the plan; **the age of the Codex session file is on Codex's own row**. agy: *its
    account is not read*.
  - *A window cell:* the first line is `<n>% left`; under it `resets in <time>`. A missing value is `—`, never
    `0%` (decision 5). Claude's weekly windows for single models are sub-lines of the weekly cell. A Codex window
    is placed by its length — 300 minutes in the 5-hour column, 10 080 in the weekly one; another length is named
    in its cell.
  - *Usage that was not read* spans both window cells and says why: *Usage not read: CoAI does not ask Claude Code
    for usage in this build*; *Usage unavailable: Claude Code 2.1.326 printed no usage line this version
    understands*; *agy states no usage*. It is never a row of dashes with no reason.
- **The toolbar**: *Refresh now*; *Stop the round* (it becomes *Start the round* while stopped); and the summary
  *last round <age> (<clock time>) · <k> of <n> read*, or *reading host-c… 5 of 16* while a round runs.
- **Without the permission the page holds one block**: what would be read, and the one button *Allow reading…*
  (§ 3). With it, a last line says when it was given and has *Withdraw the permission*.
- **Each place is drawn when its call lands**, not at the end of the round: the host posts that place's part of the
  model, and the page changes that block only, so a reader's scroll place and the find bar stay.
- **Times stay true while the tab is open.** The model carries every time as an absolute instant. The page writes
  the ages and the *resets in* texts itself and writes them again every 15 s from its own clock, so an age grows
  with no message from the host. The clock time of the last read is always on the heading; a hover over an age or
  a reset shows the exact local date and time. The pure word rules (`usageWords.ts`, `agoWords`) are bundled into
  the page; there is one copy of them.
- **Narrow widths**: under a width fixed in the spec the four columns stack, each field with its label.
- **Nothing from a VM can become markup.** The page is built by its script from the model: every value that came
  from a VM, from WSL or from a terminal's name is set as `textContent`. No HTML is assembled from data, and a
  `$(name)` is plain text. The CSP is strict: `default-src 'none'`, a script and a style by nonce only, no remote
  source, `localResourceRoots: []`. The one road that makes VM text plain (`plainValue`: control and format
  characters out, the length cut) stays in front of the model.
- **A message from the page names, it does not carry.** `ready`, `refresh`, `stop`, `start`, `allow`, `withdraw`,
  `save-now`, `refuse <scope host>`, `read-again <scope host>`, `copy-install <place>`. The host checks every name
  against its own model and drops a message it does not know. **The clipboard is written by the extension host**
  (`vscode.env.clipboard`), with the fixed string of the closed `jq` table for that place's manager word (§ 4):
  no text from the page and none from a VM is ever copied.
- ***CoAI: hosts read over ssh*** stays as it is, a pick list with the three choices for one host. It gains one
  first row for the permission (§ 3).

### 2. The round — places, the permission, and WSL on this PC

What E1 built stays unless a line here changes it (the appendix, § I, *2. The round*): one call at a time, each
awaited; the order rotated by one each round; 10 minutes counted from the END of a round; the checks before every
call and before every result; results in memory only; a failed place logged once when its sentence changes; no
timer while no place may be read; a round for each window.

- **No call without the permission.** While this side holds no permission (§ 3), the timer is not armed and no
  press of the tab starts a call. The first round starts at once when the permission is given; after that, 30 s
  after activation when a `Creds:` terminal of a host that is not refused is open.
- **Which scope hosts.** The distinct hosts of the live terminals whose name gives a session. A refused host is
  never contacted. A host that appears later is read in the next round with no question: the permission covers it
  (decision 22).
- **Which places.** An ssh host is one place. For `wsl` in a Windows window (the extension host runs on Windows,
  not in a remote), the round enumerates, in every round:
  1. the distros: `wsl.exe --list --quiet`, and the running ones: `wsl.exe --list --running --quiet`. A name counts
     only when it fits `[A-Za-z0-9][A-Za-z0-9._-]{0,63}`; another line is counted and never passed on. **A distro
     that is not running is listed and not started** (Q2).
  2. the Linux users of each running distro: the pinned users script (§ 4), run **as root, and root runs nothing
     else**. It names root and the users with a UID from 1000 to 59 999 whose home folder exists and whose shell
     is not `nologin` or `false`.
  3. for each such user, the pinned accounts script **as that user**:
     `wsl.exe -d <distro> -u <user> --cd / --exec /bin/sh -s -- <mode>`, the script on stdin. `--exec` starts no
     shell of the user, so no profile is read. **Never root with another user's `HOME`**: that would run that
     user's own `~/.local/bin/claude` as root (`cons` `78e6d44b`).
  The caps: 16 distros, 16 users in a distro, 32 WSL places in a round; what is past a cap is counted in the
  heading, never read. In a window that does not run on Windows, `wsl` says *WSL is read from a Windows window
  only* and no call of any kind goes to it. `wsl` is never given to `ssh` on Windows.
- **`wsl.exe` is called as `ssh` is**: one absolute path, `<SystemRoot>\System32\wsl.exe`, never the `PATH`; an
  argument array, `shell` off; the script on stdin; 30 s and 64 KiB, the tree killed. The distro and the user in
  an argv come from the enumeration and pass their class first; neither can start with `-`.
- **A reading belongs to a place.** The memory is keyed by place — `ssh:<host>`, `wsl:<distro>/<user>` — so two
  users of one distro never overwrite each other. The enumeration's own result (the distros, which run, a failure)
  belongs to the scope host `wsl`.
- **Usage once for each account in a round — the account first.** As E1 built it, now over places: every place's
  first call reads no Claude usage; the first place of the round that answers with an account whose usage is due
  is called once more, with `claude-usage`. A WSL user and an ssh host with the same account share one usage read.
  **The usage call reads Claude only** (§ 4): its reply replaces the Claude part of that place's reading, and the
  Codex and agy parts of the first reply stay.
- **The usage policy** is the one constant `USAGE_POLICY`, `off` until E4. Its other values are the rows of the
  W6 table (§ 5).
- ***Refresh now*** starts a round at once, unless one runs or the last one ended less than 60 s ago. Opening the
  tab draws what the memory holds and starts a round only when a place that may be read has no reading.
- **The size of a round.** One call for each place and one more for each Claude account. With 20 ssh hosts of 20
  accounts and the usage on, that is 40 calls: 2 to 7 minutes at the measured 3 to 10 s a call, 20 minutes at the
  ceiling. The next round starts 10 minutes after the end, so rounds never overlap.
- **The summary** of the toolbar is of the last round that ended: its end time, and how many of its places
  answered.
- **One notice, once for each installation**, when a `Creds:` terminal is first seen without the permission:
  *CoAI can show the AI accounts and usage of the hosts of your Creds terminals.* Its one button opens the tab and
  decides nothing. **The timer never asks a person.**

### 3. The consent — ONE permission, a "no" for one host, and the Stop

**What E1 built and this plan keeps** (the appendix, § I, *3. The consent*, and the S1 spec): a person's "no" for a
host and the Stop are kept in `terminal-hosts/refused.json` of each SIDE of the machine, written by one function
under a lock that is never taken over by its age, read from the disk before every call, removed only by a person's
press; a change the file did not take is held by its window, copied to `globalState` at once and marked for every
window of the side; a damaged file contacts nobody; the "no" rows of `coai.terminalHosts` are copied in at every
read.

**What changes: nothing is read before one explicit "yes", and that "yes" is one for all hosts** (decision 22;
`cons` `78e6d44b`).

- **The permission** is one record of the side, `grant: { utc }`, in the SAME locked file. `globalState` cannot keep
  it for the reason of entry 1.2: a stale window that sends its whole copy could put back a permission the person
  withdrew. A build that does not know the key keeps it through a write, as the file's rule already says.
- **Given only by a person's press.** The first press of the icon without a permission opens the tab and asks ONE
  modal question through the one door (`notifyAndAsk`, code `read-accounts-permission`). The state row of
  `globalState` remembers that it was asked; a row that is lost asks once more. Later it is asked by
  *Allow reading…* in the tab and by the first row of *CoAI: hosts read over ssh*. Buttons: *Allow reading*,
  *Not now*. The detail says all of what a "yes" covers:
  - every host of an open `Creds: <host> / <session>` terminal, **the ones open now and the ones opened later**,
    with no further question;
  - a login over ssh with the person's own keys every 10 minutes while such a terminal is open, and, in a Windows
    window with a `Creds: wsl / …` terminal, a read of every Linux user of every running WSL distro of this PC;
  - what is read: which of Claude Code, Codex and Antigravity are there, the account each is signed in to, and
    their usage; that `claude` and `agy` as found there are started for it;
  - the Claude session of a terminal, when a chat door of that terminal is pressed (Q1);
  - that CoAI installs and writes nothing there; and how to stop: *Not this host*, *Stop the round*, *Withdraw the
    permission*.
- **Withdrawn by a person's press**, in the tab and in the hosts list. The readings are dropped and the timer is
  cleared at once. A withdrawal that the file did not take is held and marked like a refusal: while it waits, no
  window of the side makes an automatic call. An *Allow* that the file did not take changes nothing, and the
  person is told.
- **Who may be contacted** — one function, asked before every call and before every result, and by the session
  door before every spawn:

  | Who asks | It may contact the host when |
  |---|---|
  | The accounts round | the file can be read, AND nothing waits unsaved, AND the round is not stopped, AND this side holds the permission, AND the scope host is not refused |
  | The session read of a press | the file can be read, AND nothing waits unsaved, AND the host is not refused, AND (this side holds the permission OR `coai.terminalHosts` holds a person's "yes" for that host). With neither, the FIRST spawn of the press asks the per-host question as it shipped in 0.87.0, and its *Read* writes a person's "yes" for that host |

  A person's "yes" for a host is the session read's alone: it never lets the round read. The Stop stops the round
  and not the session read, as today.
- **T9, taken up: the session road asks that function before EVERY spawn of a press**, not once. A later spawn of
  a press never asks a person: when the answer is no longer "yes" it stops with the sentence of that answer.
- **T8, taken up: "something waits unsaved" is an answer of its own, `waiting`**, beside `yes`, `no`, `ask` and
  `damaged`. The session door and the tab then say: *A “Not this host”, a stop or a withdrawal is not saved yet on
  this side, so nothing was read. Choose “Save now”.* The sentence "the list cannot be read" is for a list that
  cannot be read.
- **What is retired** (each is in main since #1047 and in no release): the automatic "yes" and the write of it; the
  cap rule that drops an automatic row; the timer's use of a "yes" that was given for a session read; the setting
  `coai.terminalAccountsAllowNewHosts` in the manifest, `SAFETY_KEYS` (`src/modelKeys.ts`), `NEVER_TRANSFERRED`
  (`src/configTransfer.ts`), `src/sideConfig.ts`, the words of `src/remoteHostsSaid.ts` and the help; the row *This workspace sets …*; the daily notice `accounts-hosts-allowed`; the
  first-round notice `accounts-read-every-ten-minutes`; the fields `toldUtc` and `lastAutoNoticeUtc` of the state
  row; the door answers `not-asked` and `full` for a new host; the wider words of the session question, which is
  again *"Read the Claude session on “x” over ssh?"*. The parser still KNOWS `how: 'auto'`, so a list that holds
  such a row is not damaged: the row counts as no answer and is dropped at the next write of the list.
- **The upgrade starts with NO permission, whatever is stored.**

  | Stored before | After the upgrade |
  |---|---|
  | A person's "yes" for a host (from 0.87.0 on) | The session read of that host goes on. The accounts round reads nothing |
  | An automatic "yes" (only the pre-release 2.0.901 wrote one) | No answer. Nothing is read |
  | A "no", in the file or as a row | A refusal, as before, on every side that reads it |
  | The Stop | Stays. A permission given later does not start a stopped round |
  | `coai.terminalAccountsAllowNewHosts` in a settings file | Not read |

- **The sides.** The permission is of one side, like the file: a WSL or remote window has its own, and asks its
  own question. The owner uses a second side for the chat only (2026-11-24); there a person's "yes" rows, which
  `globalState` shares, keep the session read working with no permission.
- **What a person can and cannot choose.** One "yes" for everything read; a "no" for any one host; a stop of the
  round; a withdrawal. Not: the accounts read on a few hosts only, without refusing each of the others; and not
  the session read of one host without its accounts read while the round reads the other hosts — *Stop the round*
  stops the accounts read of every host and keeps the session read.

### 4. The scripts — revision 2, and the users script

E1's script, its reply and its parser stay as built and as specified (the appendix, § I, *4. The remote script and
its reply*, and the S2 spec): fixed text sent whole; one function read to its end and then called; `jq` names every
field that leaves; three anchored line patterns for the usage; the person's activity dropped on the VM; the Codex
record from a bounded tail; of a version command only the version; a closed manager word for a host without `jq`
and a fixed command for each; a strict frame and a closed vocabulary on this side; a missing number is `null`,
never 0. **Revision 2 changes three things, in ONE new pinned text with one new sha256** (E3.S2, the cornerstone):

1. **The script sets its own `PATH`; it does not add to the one it was given.** `$HOME/.local/bin`, then a fixed
   list of system folders that the spec names. On WSL the inherited `PATH` holds the Windows folders, and a
   `claude` of the Windows side found there would be started through interop and read as the Linux user's. Over
   ssh the list is what a shell that is not a login shell has today. A test holds that a `claude` which is
   reachable only through the inherited `PATH` is not started.
2. **The mode `claude-usage` reads Claude only**: the account, the version and the usage. It reads no Codex file
   and does not start `agy`: the first call of that place did both seconds before. So a place that reads an
   account's usage starts `agy` once in a round, not twice. The reply of that mode holds the Claude part only, the
   parser reads a reply against the mode that was sent (as today), and the round joins the two replies (§ 2).
3. **The time table counts the KILL second (T46).** A vendor command gets a TERM at its limit and a KILL one
   second later (`timeout -k 1`). Where the reader's limit equals the vendor's — the two version commands — that
   second is added:

   | Mode | The stages, at their latest | Sum | With ssh's 5 s to connect |
   |---|---|---|---|
   | `no-claude-usage` | account 4 s; version 2 + 1 s; the Codex files 1 + 1 + 3 s; `agy` 4 + 1 s | 17 s | **22 s** |
   | `claude-usage` | account 4 s; version 2 + 1 s; usage 9 s | 16 s | **21 s** |

   Both are 8 s or more under the runner's ceiling of 30 s, and no limit is shortened. **The test sums, for each
   mode, every stage's reader limit, one KILL second for each stage that can outlive its reader, and the connect
   time, and asserts the total is at most the ceiling minus 2 s**; and a run with a vendor program that ignores
   TERM ends inside that sum. *Not taken:* shortening the limits by 3 s in total gives exactly 28 s with every
   stage in one call, and it cuts `agy`'s limit, whose real time is not measured; raising the ceiling makes a dead
   host hold a round longer.

**The users script** (new; E3.S1, the security spec): a second fixed text with its own sha256, in the same shape
— one function, called only at its end, a begin line `COAI-USERS 1` and an end line. It opens `/etc/passwd` and no
other file, starts no program, and prints for each entry that § 2 names one line with the user's name and UID. A
name counts, there and on this side, only when it fits `[a-z_][a-z0-9_-]{0,31}`; at most 16 lines. It has no
operand.

**The operand** of the accounts script stays one of two fixed words. No value from a terminal's name, from a VM or
from a distro is ever an operand; a distro's and a user's name are arguments of `wsl.exe` and never of the script.

### 5. What is still to be measured

Each needs the owner's go for its exact commands. **W-numbers here are measurements** (the S1 spec uses W-numbers
for its sentences).

| # | What is measured, and where | What the result does |
|---|---|---|
| W6 | **Backups, on a VM** (unchanged; the appendix, § I, § 5). As a Linux user with no live Claude Code session: `~/.claude.json` and `~/.claude/backups/` before and after each of 12 usage reads from `/` | **Gate for growth.** It selects the usage policy of E4: a read makes no backup, or Claude Code keeps a fixed number → every round; a backup each time that nothing retires → at most once an hour for each account under a budget the owner approves, or only on *Refresh now* |
| W7 | **A trace of every program start, on a VM** (unchanged): one usage read under `strace -f -e trace=execve`, stdin `/dev/null`, from `/` | **Gate.** A program that is not Claude Code's own — a hook's command, an MCP server — and the Claude usage read stays off until the owner decides. Until W7 has run this plan does not claim "starts nothing of the person's" |
| W8 | A start of `agy --version` on a VM: its time, what it writes, what it starts | **A record** (decision 16) |
| W9 | **WSL on this PC, in a disposable distro with two users**, made for the check and removed after it. (a) What `wsl.exe --list --quiet` and `--list --running --quiet` print: the encoding (UTF-16LE, or UTF-8 with `WSL_UTF8=1`), the line ends, the text and exit code with no distro and with none running, and whether a list call starts anything. (b) As each user, through the argv of § 2: the effective UID, `HOME`, the working folder, the `PATH` as given, and whether a profile ran (a marker in each user's profile files). (c) **Before and after**: a listing — path, size, time — of both homes and of root's around the users script, around the accounts script as user A, and around a read with a real Claude Code where one is installed. (d) Whether the users script answers the same as the distro's default user. (e) After `wsl.exe` is killed at the ceiling: which Linux processes are left. (f) A stopped distro: what a call does to it and how long it then stays up. (g) The time of 10 calls | **Gate for E3.** The WSL road is specified on what W9 shows. **"Read-only" is promised for WSL only as far as (c) shows**: that a read as A changes nothing in B's home or root's, and in A's only what Claude Code itself writes (decision 13). (d) decides whether root is needed at all; (f) is the fact behind Q2 |

Within the plan as written: the field names; the limits under the ceilings; the policy that W6 selects; the exact
argv words of `wsl.exe` that W9 confirms; root or the default user for the users script. Outside it, and so a
change: anything installed on a VM or in a distro before E6; a third kind of script; a write by a script of ours;
a start of a distro that is not running; road (b).

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs.
- No read of any kind before the permission, and none of a refused host, ever. No question raised by a timer.
- No automatic allow of a host, and no setting for it. No setting that a workspace can set for the person.
- **No install before E6.** Nothing is written or installed on a VM or in a distro by a script of ours.
- No `ssh` to `wsl` on Windows. No read of WSL from a window that does not run on Windows. No start of a distro
  that is not running (Q2). No read of this PC's Windows-side accounts.
- No script run as root for another user, and nothing as root but the users script.
- No real time, no push from a VM, no setting for the interval.
- No history, no chart, no ledger of the readings, no alert when a limit runs low. No colour for the numbers.
- No reading of a host that has no open `Creds:` terminal; WSL is read only while a `Creds: wsl / …` terminal is
  open.
- No live Codex read, no `codex login status`, no opening of `~/.codex/auth.json`; no start of `agy` but with its
  version flag; no opening of `~/.claude.json` or of a credential file by our script. **No road (b).**
- No grouping of accounts that are not known, by host alias or from equal numbers.
- No change to `SSH_OPTIONS`.
- No lock between VS Code windows for the round, and no file shared between two sides of the machine.
- The session read of a `Creds: wsl / …` terminal is not made to work here: the chat doors there still fail as
  they do today, and that is the terminal-door plan's to take up.
- The process tree of a timed-out ssh call on Linux and macOS stays its own todo: the WSL road runs on Windows
  only, where the tree is killed.

## Security

This section is the security judgement of the revision (Fable, `subagent-models.md`: the security row). The specs
of E2.S1 and E3.S1 repeat it for the exact store rules and the exact argv, before any code.

**The edges.** (1) Read-only ssh calls that a TIMER starts, to every host of an open `Creds:` terminal that is not
refused — **now only after one explicit permission**. (2) New: local `wsl.exe` calls by the same timer — an
enumeration, the users script as root, the accounts script as each Linux user. (3) On a usage read, a start of
Claude Code there, which calls Anthropic with that user's own login (off until E4). (4) In every round, a start of
`agy` for its version. (5) Text from a VM or a distro drawn in a webview. E6 adds a sixth, a call that changes a
machine.

**Finding F1 is answered, not gone** (the appendix, § B). With E1 the consent no longer came first: an old "yes" was
used with no press, and a host was allowed by a terminal's name. **Before the permission nothing is contacted now,
whatever is stored and whatever a terminal is called.** After it, what F1 says of a name holds again, by the
person's own word: any program that can open a terminal named `Creds: <host> / x` makes the extension log in to
that host within 10 minutes and run the fixed script; a terminal named `Creds: wsl / x` makes it read every user
of every running distro. The name chooses the host and nothing else, from an alphabet with no option and no shell
character; ssh refuses a host whose key is not known, asks for no password and forwards no agent; nothing of a
reply goes back to whoever opened the terminal. The question says "and the ones opened later" in plain words,
because that sentence is the whole of this residual.

**The permission is kept where a withdrawal cannot be undone by a stale window**: in the locked file of the side,
given and withdrawn only by a person's press, read from the disk before every call. A withdrawal that did not land
stops every window of the side, like a refusal. What a file cannot do is residual 9 below.

**No secret passes through our scripts** (road (a), as built; the table of what is opened and what is not: the
appendix, § I, *Security*). It holds for a WSL user as for a VM: the script opens no credential file, holds no
token and gives none to a program.

**WSL — who runs as whom.** The accounts script runs as the Linux user it reads, with that user's `HOME`, through
`--exec`, so no shell profile runs. Root runs the users script and nothing else: fixed text that opens
`/etc/passwd`. A distro's and a user's name reach an argv only after their class, and neither is ever part of a
script or an operand. Three things are named, not prevented:

- **A program that a Linux user owns is started by our timer.** The script starts `claude` and `agy` as found in
  that user's `~/.local/bin`. Whoever can write there decides what runs — as that user, never as root, and never
  as another user. On a VM that ends at the VM. **In WSL it does not:** a Linux process can start Windows programs
  as the Windows user (interop). That is WSL's own boundary, and it is open to that Linux user at any time; what
  is new is that our timer starts the program with nobody at that user's keyboard. The permission's text says
  that `claude` and `agy` as found there are started.
- **The inherited `PATH`** is not used (§ 4), so a Windows program is not started by its name.
- **What a kill leaves.** Whether the Linux processes end when `wsl.exe` is killed is measured (W9e); the script's
  own limits end each vendor command in any case.

**The tab.** Text from a VM, a distro or a terminal's name can only become text: it is made plain at one road and
set as `textContent`; the page assembles no HTML from data; the CSP allows no source but the page's own script and
style by nonce. A message from the page carries names that the host checks against its own model; a hostile page
could at most press the tab's own buttons. The one button that gives consent raises the modal question in the
extension host, so no message of a page can give the permission by itself. The clipboard gets a fixed string from
a closed table.

**What an attacker gains** (the list of the stopped plan holds: the appendix, § I, *Security*), with these
changes:

- **A program that opens a terminal named `Creds: evil / x`.** Before the permission: nothing. After it: as F1,
  above. It still cannot push out or undo a "no".
- **Someone who is that Linux user, in a distro or on a VM.** They can make the tab show wrong accounts or numbers
  for their own place. They cannot reach the extension through the reply: bounded (30 s, 64 KiB, 64 lines), parsed
  by a closed vocabulary, never run, never a path, drawn as text. In WSL they could already reach the Windows side
  through interop; the timer adds no right, only a start.
- **Root in a distro** can make the users script name users that do not exist; a name must fit its class, at most
  16 are taken, and a call for a user that is not there fails as a call.
- **A second window, or a reload, racing a refusal, the Stop or a withdrawal**: the file, as built.
- **Logs**: a failed read logs the place, how it ended and the runner's own last line; a script's stderr goes to
  `/dev/null`; stdout is never logged.

**Residuals.** Those of the stopped plan hold (the appendix, § I, *Security*: a person's `ControlMaster`; a login
every 10 minutes on every host of an open terminal; an email on screen; what Claude Code does on a start; a reset
in another zone shown as a dash; and the eight residuals of the refusals file), but for the third of the file — a
withdrawn person's "yes" that a stale write puts back now lets the session read ask no question for that host, and
nothing more. New:

9. **The permission, like a refusal, is a file on one disk.** A withdrawal can be written over by a former lock
   holder that wakes up (residual 5b of the file); every window that read the newer file takes the permission as
   withdrawn and writes that back. It is lost for good only when no such window is open. It errs to reading, so
   the test plan holds the case and R12 runs it.
10. **The permission is of one side.** Withdrawn in a Windows window, it still stands in a WSL window that was
    given its own.
11. **20 hosts are 20 logins every 10 minutes**, and 40 with the usage on and no shared account; two windows
    double it (candidate C6).
12. **A stopped distro is never shown with numbers** (Q2); a distro that stops between the two list calls and the
    read is started by that read. W9f records what that costs.

**Judgements.** *Road (a):* yes, on what is measured, with W7 as the gate that can still fail it. *The consent:*
entry 2.3 puts a person's "yes" back in front of every call, which the planner recommended on 2026-11-19 and E1 did
not have; one "yes" for all hosts is the owner's width, and the judge's condition is that the question names the
later hosts, the WSL users and the programs that are started. *WSL:* per-user runs are right and root-for-all
would be wrong; the interop residual is real and is the person's to accept with the permission; nothing is
promised as read-only before W9. *The tab:* a webview is a larger surface than a QuickPick, and it is closed by
construction — text only, no remote source, names in, fixed strings out. *The installer:* outside the permission
(Q3).

`SECURITY.md`: the section on the extension's ssh calls is rewritten by E2 — its sentence *"A host is contacted
unless the person refused it"* becomes *"No host is contacted before the person's one permission"* — and gains the
permission, where it is kept and what it covers, the tab and its CSP; by E3 the WSL road, who runs as whom, and the
interop residual; by E4 what W6 and W7 showed.

## Growth surfaces

- **The readings**: in memory, one for each place, a few hundred bytes each; dropped when the scope host's last
  terminal closes, when it is refused, when the round is stopped, when the permission is withdrawn, and with the
  window. At the caps: the hosts of the open terminals and 32 WSL places.
- **The WSL enumeration**: in memory, at most 16 distro names and 16 user names each, replaced every round.
- **The permission**: one key in `refused.json`, under 100 bytes. The file's caps stand: 100 refusals, 64 KiB.
  **Who retires it:** a person's withdrawal. **Interrupted:** the file is the old one or the new one (a rename).
- **The markers**: as built, at most 32 of 1 KiB; a withdrawal that waits is one more kind.
- **`coai.terminalHosts`**: 100 rows at most. Nothing automatic writes a row any more, so it grows only by a
  person's answers; automatic rows of the pre-release go at the next write.
- **The state row**: the copy of the Stop, the time the one notice was shown and the time the question was first
  asked; under 150 bytes.
- **The tab**: one webview for each window, kept while it is open; its model is the readings. One 15 s interval of
  the page, gone with the tab.
- **Children**: one `ssh` or `wsl.exe` alive at a time for each window, killed at 30 s, stdout capped at 64 KiB.
  For `wsl` in a round: 2 list calls, one users call for each running distro, one or two calls for each user.
  **Interrupted:** a reload leaves nothing on this side; what a killed `wsl.exe` leaves in the distro is W9e.
- **The timer**: one for each window, cleared when nothing may be read, disposed with the extension.
- **Log lines**: one when a place starts to fail and one when it reads again.
- **On a VM or in a distro, by our scripts**: nothing.
- **There, by Claude Code on a usage read**: four cache files, rewritten in place; and `~/.claude/backups/`, the one
  surface that may grow without bound — 144 files and about 24 MB a day at one read in 10 minutes on the measured
  VM, if every read makes a backup and nothing retires them. W6 measures it, and the policy follows (§ 5). This
  plan deletes no file of Claude Code.
- **There, by `agy` on a start**: not measured (W8). Once a round for each place, since revision 2.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, by Claude Code; none while the
  policy is `off`. None by our code.

## Boundaries with other plans

| Item | Built by | The other plan's part |
|---|---|---|
| The pinned ssh call, the session script, the chat doors, the per-host answer `coai.terminalHosts`, the question `read-remote-session` | [PLAN_the_chat_opens_from_a_terminal.md](../research/PLAN_the_chat_opens_from_a_terminal.md) (shipped in 0.87.0; it names this plan at its line 315) | **This revision gives the per-host answer back its first meaning**: a "yes" for a host allows the session read of that host and nothing else. This plan adds beside it: the refusals file (E1), the permission, which also allows the session read (Q1), the door answer `waiting`, and the re-read before every spawn. The store's key, the question's code and its buttons do not change |
| The todo T46, the 30 s ceiling | this plan, E3.S2 | The todo file is closed by the stories document of E3 |
| The todos T8 and T9, the session door | this plan, E2.S1 | The todo files are closed by the stories document of E2 |
| A timed-out ssh call ends its whole tree | [PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md](PLAN_a_timed_out_ssh_call_ends_its_whole_tree.md) | Nothing here: it is the launcher's on Linux and macOS, and older than this plan. The WSL road needs no part of it |
| The gauge of coai's own rounds | [../research/PLAN_every_round_is_counted.md](../research/PLAN_every_round_is_counted.md) and [PLAN_the_round_counter_tail.md](PLAN_the_round_counter_tail.md) | Nothing: no shared store, no shared reader |
| The account and limits line inside Claude Code | the agent-models mod | Nothing is changed there; this plan carries its word rules over and tests them against its cases |

The three todo files say on their side that this revision takes them up, and that nothing of them is built before
epoch 3 is approved.

## Build order

1. **The cut, during the stop** (*The full stop*, step 6: merged work that contradicts the revised plan is
   corrected in a new pull request). ONE small pull request: in every build from main the accounts round makes no
   call, by a timer or by a press — the timer is not armed, the round's door admits no host, nothing goes to `wsl` — and
   the list says that reading is off until the permission of the next build. **No extension release is cut from
   main before it is merged**: main holds a build that logs in to hosts with no "yes". A revert of #1047 is not
   taken: E2 to E4 build on its modules.
2. **The owner approves epoch 3**, with the answers to Q1–Q4, after the judge's sorting and the re-review round
   of this text. The epoch line is written then.
3. **E2 — the permission and the table.** Its stories by Opus; the security spec of E2.S1 by Fable before its
   code; the code gate; a VSIX from the pull request's head; the owner's rows; merged.
4. **Alongside E2 and E3, by hand, each with the owner's go:** W9 on this PC — before the spec of E3.S1 is final —
   and W6, W7 and W8 on a VM.
5. **E3 — WSL on this PC, and revision 2 of the script.** E3.S2, the script, may merge as its own pull request
   first, so that E4 waits for nothing else of E3.
6. **E4 — the Claude usage on.** Only when revision 2 is merged, W7 passed and W6 selected the policy. When W7
   failed, the read stays off and E4 records that.
7. **E5 — the long runs before the release.** No product code.
8. **E6 — the installer** (candidate C1 of the stopped plan), with its own stories, its cornerstone spec and its
   own real-host checks; Q3 first.
9. `review_feature` over the built epics, every finding resolved.
10. The extension release, one for the whole plan: the tag pushed alone and its run observed; `POST_DEPLOY.md`
    run; the plan promoted with its deviations, its appendix and its stories documents.

**Why this order.** *The consent first:* main reads with no "yes" today, every later check runs through the door,
and the acceptance of entry 2.3 can be seen on the owner's 20 hosts at once. *The table with it:* the permission
needs a place to be asked and shown, and the table is how the owner checks every later epic — the QuickPick could
not show what was read. *WSL third:* it needs the place identity that the table's model already has, and a check
on this PC that can run while E2 is built. *The usage fourth:* it needs revision 2, which E3 pins, and two
measurements that depend on the owner's go and on a VM, not on our code; it is the smallest epic and the one the
owner will see most, so it merges as soon as its three conditions hold. *The long runs fifth:* two hours with live
sessions and the two-window gate are worth running once, on the build that will be released. *The installer
last:* it changes a machine, so the read-only path is proven first (the consultants, `f4af076c`), and its consent
is its own (Q3).

## Test plan

**E1's tests stay** for what stays (the appendix, § I, *Test plan*): the script run for real with false programs
and the real `jq`; no secret and no activity leaves, each seen RED once; the hash; the parser; the usage lines; the
Codex record; the words; the round's order and the account-first rule; the refusals file with its lock, its
markers, the copy-in and the two checks of the cadence consultation; the runner's argv. **Deleted with what they
hold:** the tests of the automatic "yes", of the cap's eviction, of the setting at both values
(`terminalAccountsSetting.test.ts`), of the two notices, of the QuickPick's rows and its host scenario. New:

- **The permission — the acceptance of entry 2.3** (pure, over the fake disk and stale views of E1's tests). A
  store seeded with a person's "yes", an automatic "yes" of the pre-release, one refusal and 20 live hosts, `wsl`
  among them: **zero calls** of any runner, by ticks and by presses, before the permission. After *Allow*: one
  call for each host that is not refused, **none to the refused host**, and a host whose terminal opens later is
  read in the next round with no question. With `wsl` refused: **no call to `wsl.exe` at all**, the list calls
  among them. The timer raises no question (a test fails if the ask seam is called from a tick). The modal is
  raised by the first press of the icon and not by the second.
- **Withdrawal and its races.** A withdrawal clears the timer and the readings; a result in flight is dropped. A
  withdrawal in window A; A is disposed; a stale window B writes `globalState`; both are built again as after a
  reload → no call. A withdrawal the file did not take → no automatic call in any window of the side; the tab
  says so; *Save now* lands it. A file that went back to one WITH a permission after a window read it without →
  the window takes it as withdrawn and writes that back. A stopped round stays stopped through *Allow*.
- **The upgrade**, as the table of § 3, row by row; a list with a `how: 'auto'` row is not damaged.
- **The session door.** Each cell of § 3's table. **T9:** a "no" saved during the first spawn of a press → no
  second spawn; consent still "yes" → the fallbacks run; a later spawn never calls the ask seam. **T8:** a held
  refusal for host h, a press on host g → the `waiting` sentence, never the sentence of a list that cannot be
  read; a damaged list → that sentence, as today.
- **Places.** Two users of one distro are two readings, and neither overwrites the other; an ssh host and a WSL
  user with one account are ONE usage read and both say *shared across 2 hosts*; a reading of a place whose scope
  host was refused in flight is dropped.
- **The WSL road** (pure, with a false `wsl.exe` seam, and one test that runs the real scripts by `sh`). The argv
  word for word: the absolute path, `-d`, `-u`, `--cd /`, `--exec /bin/sh -s --` and the mode. A distro or user
  name that fails its class never reaches an argv: a leading `-`, a space, a name of 100 characters, a line of a
  message in another language. The list output as UTF-16LE and as UTF-8, with CR LF, with no distro, with none
  running. A distro that is not running gets no call. The caps of 16, 16 and 32. The users script: root and the
  users from 1000 to 59 999; `nologin`, `false` and a home that is missing are left out; its hash is held. **A
  test fails if any argv has `-u root` with a mode word**, and if any call but the users call names root for
  another user's place. Not on Windows: `wsl` gets no call of either runner.
- **Revision 2 of the script.** A `claude` reachable only through the inherited `PATH` is not started. In
  `claude-usage` the false `agy` records no start and no Codex file is opened; the reply holds the Claude part
  only and the round joins it to the first reply. **T46:** the sum of each mode with its KILL seconds and the
  connect time is at most 28 s; a false `claude --version` and a false `agy` that ignore TERM, with every other
  stage at its limit, end inside the sum and the end line is written. The new hash is in the spec and in the test.
- **The table's model** (pure): by host; each heading sentence; a tool that is absent has no row; the cells — `44%
  left`, a dash with no `% left`, `resets in —`, a Codex window by its length, the model sub-lines; each "not
  read" and "unavailable" sentence spans the cells; a failed host keeps its older table with its time; the
  summary's counts; for a host without `jq`, the copied string is the fixed one of the table for its manager
  word, and **a test fails if any text of a reply is part of it**.
- **The page, by RUNNING it** (`bundledPage.test.ts`, the DOM shim; no assertion over page source for behaviour).
  A model whose values hold `<img src=x onerror=…>`, `$(account)` and 10 KB of text: every one is the text of a
  node, and no element exists that the model did not order. Each button posts its message and nothing else.
  **The owner's observation as a test:** the clock of the shim goes on 10 minutes with no message from the host,
  and the heading reads *read 10 min ago*; the clock time on it does not change. A place's update changes that
  block only. A message the host does not know is dropped; `copy-install` for a place with no manager word copies
  nothing. The CSP and the nonce are held by a source assertion, which is legitimate there.
- **Host scenario** (`npm run test:host`): the icon opens the tab; a second press reveals the same tab; with the
  call replaced through the test door and the permission given through the keeper, two terminals of two hosts are
  one call each, and the tab's model holds both.
- **The manifest and the help**: the command and its menu entry; the retired setting is gone from the manifest,
  `SAFETY_KEYS` and the help of five languages; `notification-sites.json` generated again; the log scan covers the
  new modules.

## Definition of Done

- [x] `research/RESULTS_accounts_and_usage_on_a_vm.md` records step 0 and W1–W5 (2026-11-19).
- [x] **E1** is merged: the read-only round, the script, the parser and the refusals file (#1047, `d0241e45`).
- [ ] **The cut** is merged before any extension release: no build from main reads a host by itself.
- [ ] **Nothing is read before ONE permission.** It is asked once, by a person's press, in words that name the
      later hosts, the WSL users, what is read and what is started; it is kept in the locked file of the side;
      a withdrawal stops every read at once. The acceptance of entry 2.3 is a test and a real-window row: an old
      "yes", an automatic "yes", one refusal and the owner's hosts give zero logins before the permission and none
      to the refused host after it.
- [ ] **A "no" for one host, the Stop and a withdrawal always win** — on the side that keeps them, against a
      round, a reload, a second window, a stale write and a damaged file — shown by the file's tests and by R12.
- [ ] The automatic allow, `coai.terminalAccountsAllowNewHosts`, its notices and its rows are gone from the code,
      the manifest and the help. The session question has its first words again.
- [ ] The session door asks before every spawn and has its own sentence for a change that waits (T9, T8).
- [ ] **The tab can be read.** The icon opens ONE tab, grouped by host: for each host its latest read result with
      its clock time, and one row for each installed tool with the account, the 5-hour window and the weekly
      window as "% left" with its reset; a shared account says so; usage that was not read says why; a failed
      host says the failure in full; a host without `jq` has the command and a copy button. Ages change while the
      tab stays open. Ctrl+F works. Every value from a VM is text, under a strict CSP.
- [ ] **WSL is read on this PC**: every running distro and every Linux user in it, each as that user, each a
      reading of its own; root runs the users script only; `wsl` is never given to `ssh` on Windows; with `wsl`
      refused nothing is enumerated. W9 has a recorded result, and what the documents promise as read-only is
      what W9 showed.
- [ ] **The script is revision 2**: its own `PATH`; a usage call that reads Claude only; each mode's worst path,
      KILL seconds counted, at least 2 s under the ceiling, held by a test; one new hash in the spec and in a
      test. No secret is in an argument, the environment, a reply or a log; the person's activity is not in a
      reply.
- [ ] **The Claude usage is on**, with the policy W6 selected, after W7 passed — or it is off and the release
      says why. No build ran the usage command by itself before W6 and W7 had recorded results.
- [ ] Nothing of a reading is persisted. Road (b) is not in the code.
- [ ] The real-window rows of E2, E3 and E4 each have the owner's recorded result before that epic merged; the
      rows of E5 have one before the tag. W6, W7, W8 and W9 are in the RESULTS file.
- [ ] Docs with each epic: the help in five languages; `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md`; `SECURITY.md`; the `POST_DEPLOY.md` question asked.
- [ ] **E6**, the installer of decisions 17, 19 and 20, is built by its own stories and spec and passed its own
      checks, under the consent that Q3 settles.
- [ ] `review_feature` ran before the tag and every finding is resolved; one extension release is cut and
      verified; the plan is promoted with its deviations.

## Merge conditions

**For the pull request of each code epic (E2, E3, E4, E6):**

1. The parent is not stopped, and the epic's stories document is written; a cornerstone or security spec is
   written by Fable before its code.
2. Every row of the test plan that belongs to the epic is green; each leak test and each "never" test of the epic
   was seen RED once with its message.
3. The code gate passed (`review_code`, fed the scope), the own Opus review ran, and every finding is resolved.
4. CI is green on the head that merges, with its checks present.
5. **The owner's real-window rows of the epic have a recorded result on a VSIX built from that head** (Q4). A row
   that fails is fixed on the same pull request and run again.
6. `SECURITY.md` and the help say what the head does.

**For E4 also:** W6 and W7 are recorded, W7 passed, and revision 2 of the script is on main.
**For the cut of step 1:** conditions 2 to 4, and a test that no runner is called by a tick or by a press.
**For the release:** E5's rows recorded, its gates passed or their stated consequence in the build;
`review_feature` resolved; no extension release from main between #1047 and the cut.

## Epics

**Six epics; one is done.** The split is Fable's, after both consultations of the stop (`78e6d44b`, `7315feb0`).
The consultants' rule of 2026-11-19 holds for each: an epic is the smallest thing that runs end to end on real
input, and it ends with the owner seeing it in a real window. That rule is why E2 carries the consent AND the
table, and why the real runs are no longer one epic at the end: the check of 2.0.901 found three faults that a
row in a real window would have shown before the merge. The rows keep the numbers of the stopped plan where their
meaning is kept (the appendix, § I, *E2*); R11, the row of the retired setting, is gone; R13 to R17 are new.

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`. Cornerstone quota: Q = ⌈2·E/5⌉ =
⌈2·6/5⌉ = 3. The cornerstone areas Fable names: **(a)** what a script touches there and what it prints — the only
place a secret or the person's activity can leak, and a leak gives no error; **(b)** the reply's frame and the
parser — the only place wrong or hostile data can arrive and look right; **(c)** who is contacted and when —
nothing before the permission, a refused host never, and no stale window undoes either; **(d)** absent, signed
out, no usage source, skipped, failed and unreadable are different results, and a missing number is never a zero;
**(e)** who runs as whom on this PC — the script as the user it reads, root for the users script only;
**(f)** a change of a machine (E6). Areas (c) and (e) are authorisation: their specs are Fable's by the security
row, outside the quota. Planned marks, made by the stories documents: E3.S2 for (a) and (b), and E6's spec for (f).

Cornerstones: 1 of 3 — E1.S2

### E1 — The read-only feature, end to end · DONE, merged as #1047 (`d0241e45`)

The round, the script with its reply and parser, the runner's second caller, the refusals file, a list, the
documents. Its text: the appendix, § I, *E1*; its stories:
[PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md).
What is kept, reworked and retired of it: *Re-review for epoch 3*, above.

### E2 — One permission, and a table that can be read

- **Goal.** After one "yes" the owner reads, in a tab, every ssh host of the open terminals: its tools, their
  accounts and the limits that this build reads. Before that "yes" nothing is contacted.
- **Scope.** `src/remoteRefusals.ts`, `src/remoteKeeper.ts`, `src/remoteHosts.ts` (the permission, the one door,
  `waiting`, the retirement of the automatic row); `src/remoteRead.ts` and `src/remoteSessionHost.ts` (the door
  before every spawn, the question's words, the first row of the hosts list); `src/remoteAccountsRound.ts` and
  `src/remoteAccountsHost.ts` (no call without the permission, the summary, the one notice; the QuickPick out);
  a NEW pure module for the table's model and a NEW page with its panel; `src/remoteAccountsRows.ts` reduced to
  what the model keeps; `package.json`, `src/modelKeys.ts`, `src/configTransfer.ts`, `src/sideConfig.ts` and
  `src/remoteHostsSaid.ts` (the setting out); the help in five languages; `notification-sites.json`; the tests; the
  module documents and `SECURITY.md`. In this epic `wsl` on Windows says *WSL on this PC is not read by this
  build* and gets no call.
- **Depends on.** The cut merged; epoch 3 approved.
- **Stories** (named here; written by Opus). **E2.S1 — the permission (SECURITY SPEC by Fable, outside the
  quota):** the record in the file, the one door for the round and for the session read, the upgrade, what is
  retired, a withdrawal that waits, T8 and T9. **E2.S2 — the table's model** (pure). **E2.S3 — the tab:** the
  panel, the page run by its tests, the messages, the ages that tick, the clipboard. **E2.S4 — the round under
  the permission.** **E2.S5 — the documents.**
- **Done when.** The merge conditions hold, with these rows in the owner's window, on the owner's ssh hosts:

| # | What is run | Pass |
|---|---|---|
| R1 | The icon | It sits beside *Ask other AI* and opens ONE tab; a second press shows the same tab; Ctrl+F finds a host; with no `Creds:` terminal the tab says so |
| R14 | **The permission.** The profile holds a person's "yes", the automatic rows of 2.0.901 and one *Not this host*; the owner's terminals are open | Before the permission: **0** ssh logins on any host in 20 minutes (the VMs' ssh logs). The question is asked once. After *Allow reading*: within 60 s exactly 1 login on each host that is not refused and 0 on the refused one. A terminal of a new host, opened later: read in the next round, no question. *Withdraw*: 0 logins in 20 minutes, also after a reload |
| R15 | **The table.** At least 3 hosts, one of them with a Codex record | For each host the owner reads, without help: the host, its tools, each account, the two windows with "% left" and the reset. The Codex figure equals the record on that VM. A Claude account says why its usage is not read. **The tab stays open 12 minutes untouched: the age of each read grows and matches the clock time beside it.** A hover shows the exact time. A narrow tab stacks the fields |
| R5 | A host that cannot be reached | Its failure in full under its heading within 30 s; the other hosts are read in that round |
| R6 | Stopping | Every `Creds:` terminal closed: 0 logins in the next 20 minutes. *Stop the round*: 0 logins in 20 minutes, also after a reload; started again, a round follows within 60 s |
| R8 | The session door | *Take the question* works with the permission and asks no per-host question. *Not this host* on a heading: 0 logins to that host in 20 minutes, also after a reload, and the chat door there refuses. *Read this host again*: read in the next round |

### E3 — WSL on this PC, and revision 2 of the script

- **Goal.** A `Creds: wsl / …` terminal shows every running distro of this PC and every Linux user in it, each
  read as that user; and the script that every place runs is the one the usage read can be turned on with.
- **Scope.** A NEW module for the WSL road beside `src/remoteSsh.ts` (the absolute `wsl.exe`, the two list calls
  and their decoding, the users call, the accounts call as a user, the caps); the users script as a second pinned
  constant; `src/remoteAccountsScript.ts`, `src/remoteAccountsReply.ts` and the S2 spec (revision 2);
  `src/remoteAccountsRound.ts` (places; the usage reply joined to the first); the table's model and the page (the
  `wsl` block); the tests; the documents and `SECURITY.md`.
- **Depends on.** E2 merged. **W9 has a recorded result before the spec of E3.S1 is final.**
- **Stories.** **E3.S1 — the WSL road (SECURITY SPEC by Fable, outside the quota):** the exact argv, the
  enumeration and its decoding, the users script and its hash, who runs as whom, the caps, and what W9 showed.
  **E3.S2 — the scripts, revision 2 (CORNERSTONE, `model: fable`):** the `PATH`, the usage mode that reads Claude
  only, the time table with its KILL seconds, the new hash, the parser by mode; it may merge first, as its own
  pull request, on merge conditions 1 to 4 and 6 — it has no row of its own, and R9a runs with the rest of E3.
  **E3.S3 — places:** the identity, the memory, the sharing of an account across places, the `wsl`
  block of the table. **E3.S4 — the documents.**
- **Done when.** The merge conditions hold, with these rows in the owner's window on this PC:

| # | What is run | Pass |
|---|---|---|
| R13 | **Two WSL users.** A distro with two Linux users, each signed in to a tool; a `Creds: wsl / …` terminal | Both users are under `wsl`, each with its own tools and accounts, and each account is the one that user is signed in to. A second running distro is listed with its users; one that is not running says so and stays stopped. No ssh login to a host `wsl` is tried |
| R16 | **A known sample** (the consultants' criterion) | One figure whose value the owner knows from the tool itself — a Codex window, or a Claude window once E4 is in — shows that value; a window with no reading says why; the age of the read stays true while the tab is open |
| R17 | *Not this host* on `wsl` | No `wsl.exe` call of this window in 20 minutes, watched on the PC by the method the stories document fixes; also after a reload |
| R9a | The time of a read, 10 calls for each place, ssh and WSL | None is killed at 30 s; the slowest is under 20 s; the medians are recorded |

### E4 — The Claude usage, on

- **Goal.** The Claude windows show numbers.
- **Scope.** ONE constant, `USAGE_POLICY`, set to the row W6 selected, with the tests that hold it; the sentences
  of `SECURITY.md` on W6 and W7. A result of W6 or W7 that needs another flag or another script line is a change
  of this plan.
- **Depends on.** E3.S2 on main; W6 and W7 recorded, W7 passed. It does not wait for E3's other stories.
- **Done when.** The merge conditions hold, with these rows:

| # | What is run | Pass |
|---|---|---|
| R3 | The numbers, on at least 3 places | Each Claude percent equals `/usage` run there in the same minute, and the mod line within 1 point, with the same dashes; an account with no 5-hour window shows `—` there |
| R4 | Two places of one account, 6 rounds | Both say *shared across 2 hosts* with the same figures; in each round that reads usage under the policy, `policy-limits.json.stamp.json` changes on exactly one of the two |
| R2 | The logins of a round | Exactly 1 on each host, and 1 more on ONE host of each Claude account and on no other; 0 on a refused host |
| R9b | The time of the usage call, 10 calls | None is killed at 30 s; the slowest is under 20 s |

### E5 — The long runs before the release

- **Goal.** Know, on the build that will be released, what only time and two windows show.
- **Scope.** No product code. A row that fails is fixed by a new pull request and run again.
- **Depends on.** E4 merged, or E3 merged and E4 recorded as off.
- **Done when.** Each row has a recorded result; **R7 and R12 are gates.**

| # | What is run | Pass |
|---|---|---|
| R7 | Two hours, at least 12 rounds, on a VM AND in a distro, each WITH live Claude Code, Codex or agy sessions | **Gate. 0** live sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart. The time and size of the agy program before and after: recorded |
| R12 | **Two windows.** (a) *Not this host* in window A while B's round runs. (b) *Not this host* in A; A is closed at once; B goes on for three rounds and is made to write `globalState`; both are reloaded. (c) The same with *Stop the round*. (d) The same with *Withdraw the permission* | **Gate.** (a) At most 1 login to that host after the press, 0 from then on. (b) 0 logins to that host from the press on. (c) and (d) 0 logins to any host from the press on. In each the file is read back and holds the change. A lost "no", Stop or withdrawal fails the gate: the build is not released, and the cure is a change of this plan |
| R10 | Which places lack `jq` | A table: `jq` there or not, the manager word, root or `sudo` without a password. A place without `jq` shows the fixed command; after the owner runs it, the next round reads the accounts. The count is the input of E6 |
| W8 | A start of `agy` (§ 5) | A record |

### E6 — The `jq` installer

- **Goal.** Owner decision 17: where `jq` is missing it is installed, when the user may; where the user may not,
  the tab shows the command.
- **Scope.** As designed in the appendix, § E, with the gate's findings 1, 2 and 6 — revised when the epic starts
  for the consent Q3 settles and for a WSL place. The setting `coai.terminalAccountsInstallMissingTools`.
- **Depends on.** E5's R10; Q3 answered. **Cornerstone** when it starts: a change of a machine as root, spec by
  Fable, finding F2 beside it (the appendix, § B).
- **Done when.** Its own stories, tests and real-host checks, written when it starts.

### Candidates

One line each. Taking one up is a change of this plan (kind `epics`), recorded in the ledger.

- **C2 — a webview instead of the QuickPick.** Taken by this revision: it is E2.
- **C3 — places with no AI tool are read once an hour.** Justified by R10 or R13 showing such places, a distro
  with no tool among them, and their reads being a nuisance.
- **C4a — the rest of the store in the file** (a person's "yes"). Justified by R12 or R8 showing a withdrawn "yes"
  that came back. **C4b — a lift that is recorded, and the Stop carried to the file of every side.** The owner
  uses a second side for the chat only, so it is not justified today.
- **C5 — the Claude usage only on a press, or road (b), or the account only.** Justified by W7 failing; the
  planner recommends the account only.
- **C6 — one lock between VS Code windows for the round.** Justified by R2 showing that two windows doubling the
  logins of 20 hosts matters.
- **C7 — a stopped distro is started for a read**, on *Refresh now* or always. Justified by the owner's answer
  to Q2 and by W9f.
- **C8 — the session read of a `Creds: wsl / …` terminal.** The terminal-door plan's; justified by the owner
  using the chat doors there.
