# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **In progress, 2026-11-26 — epoch 4 is approved by the owner; E1 and the cut are merged; E2 may start.**
> Epic E1 is merged (#1047, `d0241e45`): a read-only round every 10 minutes and a list, with the Claude usage read
> off. The owner's real-window check of its pre-release VSIX 2.0.901 gave the ledger entries 2.2 and 2.3, which
> stopped the plan on 2026-11-25. This text is the revision which that stop ordered (step 4 of *The full stop*,
> `plan-change-control.md`). **The owner approved it as epoch 3 on 2026-11-26**, with three answers that belong to
> the approved baseline and are no ledger entry: root of a WSL distro is read too, as itself (*Owner decisions*,
> 24); Q4 is "yes"; and the go for the measurements W6, W7 and W9, which were run that day. **Epoch 3 then took
> two changes, 3.1 and 3.2** — the edges of a withdrawal, found by the security spec of E2.S1 — **and the second
> stopped it. The owner approved epoch 4 the same day, at once, with NO re-review** (*Re-review for epoch 4* says
> what was not run), and with one more answer, which is baseline: **the login session that WSL opens for a user
> it reads is accepted** (decision 25; § 5). Epoch 4 has no change yet. The cut that step 6
> of the stop ordered is merged (#1051, `b1ec467c`): no build from main reads a host by itself. **E2 may start**;
> no code of E2 to E6 is written yet. Scope: `src_vs_code` only — the icon in the terminal panel's title bar; ONE permission to read, asked once; a tab with a table, by host, of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC, root
> among them. The epics E2 to E5 only read; the install of `jq` (owner decision 17, under a "yes" of its own) is the last epic, E6. No change to
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

> Epoch 2 · approved 2026-11-24 · by owner · state: closed

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
| E1.S1 — the session question's wider words ("the Claude session and the AI accounts"), #1047 | main | cut | corrected in E2: the per-host question is retired, and the one permission question takes its place |
| E1.S1 — the list of hosts itself (`remoteHosts.ts`): its parser, its merge and rank, the one commit, `consentOf` as the one door, #1047 | main | fits | kept; reworked in E2.S1 (the door asks the permission; an automatic row counts as no answer) |
| E1.S1 — the session door honours an automatic row: `consentOf` and `yesOf` answer `yes` for `how: 'auto'` while the setting is on (`remoteHosts.ts:126`, `:144`), #1047 | main | cut | corrected in the cut of step 1: such a row counts as no answer at the session door |
| E1.S1 — a waiting marker answers `damaged` at the session door (`remoteKeeper.ts:399`, T8), #1047 | main | cut | corrected in E2.S1: the answer `waiting` with its own sentence |
| E1.S1 — the session road reads the consent once for a press (`remoteRead.ts`, `readOnce`, T9), #1047 | main | cut | corrected in E2.S1: the door is asked before every spawn |
| E1.S1 — the rows and sentences of the hosts list for the setting (`remoteSessionHost.ts`, `remoteHostsSaid.ts`): "This workspace sets …", and the *Forget* sentence that the next round allows the host again by itself, #1047 | main | cut | corrected in E2 with the setting |
| E1.S1 — `claimFile` of `chatStoreLock.ts` with its rule for when a held lock may be taken, #1047 | main | fits | kept |
| E1.S2 — the accounts script, its reply and its parser (`remoteAccountsScript.ts`, `remoteAccountsReply.ts`, `remoteAccountsUsage.ts`, `remoteFraming.ts`), #1047 | main | fits | kept; reworked in E3 as revision 2 of the script: its own `PATH`, a usage call that reads Claude only, the time table of T46 |
| E1.S3 — the runner's accounts call and its limits (`remoteSsh.ts`), #1047 | main | fits | kept; E3 adds a second road beside it |
| E1.S3 — the round (`remoteAccountsRound.ts`): one call at a time, the account first, the checks before a call and before a result, the usage policy `off`, #1047 | main | fits | kept; reworked in E2 (the door asks for the permission) and in E3 (a reading for each place, not for each host) |
| E1.S3 — `ssh` to a host named `wsl` from a Windows window, #1047 | main | cut | corrected in the cut of step 1 (no call to `wsl`), then replaced by E3 |
| E1.S4 — the list as a QuickPick, its rows grouped by account, its host scenario and the two notices (`remoteAccountsHost.ts`, `remoteAccountsRows.ts`), #1047 | main | cut | replaced in E2 by the tab; the `jq` table and the one road for VM text move to the tab's model |
| E1.S4 — the window's one timer, the wiring of Stop and Start and the test door (`remoteAccountsHost.ts`), #1047 | main | fits | kept; reworked in E2.S4 (no timer without the permission) |
| E1.S4 — the words of usage (`usageWords.ts`), #1047 | main | fits | kept; the cells of the table use them |
| E1.S5 — the help in five languages, `SECURITY.md` and the module documents, #1047 | main | cut | rewritten by E2 where they describe the automatic allow and the list; the rest is kept |
| The pre-release VSIX 2.0.901, built from `d0241e45` | the owner's machine | cut | never published; replaced by the pre-release of E2 |
| The documents of epoch 2: the stories document of E1, its two specs, the record of gate G2 | main | fits | kept as the record of E1; Opus rewrites what changes when E2 and E3 start |
| The `jq` step of `ci.yml` for the tests that run the script, #1047 | main | fits | kept |
| The todo files that #1047 filed: T8, T9, T46, the ssh call's process tree, the session script's call line | main | fits | kept; T8 and T9 are closed by the stories of E2, T46 by those of E3; the other two stay todos of their own |
| The stop's own commits on this branch: the stop `07370c49`, the revision `f26ab19e`, the owner's answers `be97b7bf` | pushed, unmerged | fits | kept as plan text |

  (The verdicts were the planner's, written with the revision on 2026-11-25. The Opus judge of step 5 sorted them the
  same day: it split the row of E1.S4 and added the rows of the session door, of the list of hosts, of the lock, of
  the CI step and of the todo files. The table is as the judge left it.)
  (The consultations of the stop: `cons` `78e6d44b` on entries 2.2 and 2.3, and `consask` `7315feb0`, both question
  consultants, on the view. Their answers are folded into the revision.)
  (By `plan-change-control.md`, *The full stop*, step 4, what a re-review changes is the baseline the next epoch is
  approved on and never an entry. So none of these is one: the tab in place of the QuickPick; a reading for each
  place; revision 2 of the script with the time table of T46; the door answer `waiting` of T8 and the re-read before
  every spawn of T9; the WSL check W9; the epics E2 to E6 and their order; the merge conditions.)
  (The plan round on `3a42eebc`: coai session `96ba2a3f`, `proceed`, codex. Its five findings were accepted and are
  folded in, as baseline and not as entries: every door needs the permission and a per-host "yes" allows nothing;
  a withdrawal is a file of its own with a stamp; the usage call checks its account in the script; the gates run
  again on E6's build; R14 counts the logins when the round has ended.)
  (Epoch 3 is narrowed, so the owner approves it. Approved by the owner on 2026-11-26, as revised, with three
  answers: root of a WSL distro is read too, as itself (decision 24, which takes candidate C9 into this
  baseline); Q4 is "yes"; and the go for the measurements W9, W6 and W7. As at epoch 2's approval, an answer
  given WITH the approval is baseline and never an entry: epoch 3 starts at no changes. The plan round on
  `3a42eebc` did not read the text on root; the security spec of E3.S1 and E3's plan round read it before any
  code of E3.)

> Epoch 3 · approved 2026-11-26 · by owner · state: closed

- 3.1 · 2026-11-26 · owner · contract — a withdrawal file that cannot be read no longer blocks every later Allow: an Allow removes it under the claim and leaves one readable file one stamp below the grant, and a withdrawal takes a stamp above the highest it sees (K4 of the E2.S1 spec, its rows A4 to A6)
  (Found by the security spec of E2.S1, section "Parent text to amend": the parent said what an unreadable
  withdrawal file counts as, and not how that ever ends. The first change of epoch 3.)
- 3.2 · 2026-11-26 · owner · contract — a withdrawal is not of one side: its copy in `globalState` stops every side that reads it, and a copy only narrows, it never gives a permission (K5 of the E2.S1 spec, its rows A1 to A3; residual 10 as approved said it stays on its side)
  (Found by the same spec, which read both as one cause. The coai consultants — `consask` `0be8d85d`, both rows —
  counted TWO independent changes, and by `plan-change-control.md` the second change is a full stop.)
  (The owner chose both changes on 2026-11-26. Rows A7 and A8 of the spec are repaired wording and no entry.)
  (This second change stopped the plan: the check refuses two entries in an epoch that is not stopped. The
  owner was shown that the same day and approved epoch 4 at once — in Russian, translated: "without a stop
  and a re-review". The record below says what was not run.)

### Re-review for epoch 4

Narrowed: no — nothing left the scope, the Definition of Done, the merge conditions or the release; the promise "nothing as root but our two reads" is reworded to what W9 measured (decision 25)

| Work | Where | Verdict | Action |
|---|---|---|---|
| The stories document of E2 (`8416ff5c`, with the rows of gate E2-G1 in `254fd2db`) | pushed, unmerged (`feat/terminal-accounts-e2`) | fits | kept; it takes 3.1 and 3.2 as the owner decided |
| The security spec of E2.S1 (`6c9179ad`, with the fixes of gate E2-G1 in `71b5c212`) | pushed, unmerged (`feat/terminal-accounts-e2`) | fits | kept; 3.1 and 3.2 are its K4 and K5 |
| The results of W6, W7 and W8 on a VM (`7ed8ff40`, `ee7242e9`) | pushed, unmerged (this branch) | fits | kept |
| The results of W9, WSL on this PC (`ee7242e9`) | pushed, unmerged (this branch) | fits | kept; its one open item is answered by decision 25 |
| Product code of E2 to E6 | none was written | — | nothing was built, so nothing is cut |

  (THIS RECORD IS NOT A RE-REVIEW: none was run, by the owner's word. Of *The full stop*, step 1 is the commit
  of this record, which holds the stop and the approval together: no commit stood with the state `stopped`.
  Steps 2 to 7 were NOT run — no halt, no consultation of the stop, no revision of the whole plan, no Opus
  judge, no cut, no re-split. Of step 8 there is the owner's approval only. No judge and no coai round read
  this text; the verdicts of the table are the planner's.)
  (Baseline of epoch 4, and so no entry. The owner's answer on W9's open item, given with the approval: the
  login session that WSL opens for a user it reads is accepted — decision 25, with § 2, § 3, § 5, *Security*,
  *What this plan does NOT do* and the Definition of Done worded for it. § 5 as approved for epoch 3 called
  that choice a change of this plan; given with an approval it is baseline, as decision 24 was. And what gate
  E2-G1 fixed in the E2.S1 spec, where the parent lists it: one export of `chatStoreLock.ts` in E2's scope, a
  withdrawal that answers `busy`, residual 9, and 5(b) and 7 of the file's residuals.)
  (No plan round has read the text on root, entries 3.1 and 3.2 or decision 25: the round `96ba2a3f` read
  `3a42eebc`. The spec of E3.S1 and E3's plan round read the WSL text before any code of E3.)

> Epoch 4 · approved 2026-11-26 · by owner · state: open

## The goal

The owner, 2026-11-19 (in Russian, translated): *"Next I want to add an icon, like the chats one. It should show a list
of all Claude Code, Antigravity and Codex accounts used in the open SSH connections in the console, and their usage.
Can we?"* Then: *"Real time is not needed. We can go over ssh every 10 minutes, in a round, and ask for the data."*

The terminal panel holds many terminals named `Creds: <host> / <session>`, for example `Creds: host-a / build`,
`Creds: host-b / gate`, `Creds: wsl / notes`. Each ssh host is a Linux user on a VM with its own `~/.claude`; `wsl` is
this PC's own WSL. Nothing in VS Code says which account each of them is signed in to, or how much of its limit is
left. To learn it the owner opens each session and asks there.

**What the owner said at the real window, 2026-11-25**, about the build of E1: three sentences — entries 2.2
and 2.3 of the ledger hold two, and the appendix, § K.13, all three.

So the goal of epoch 3 is one sentence: **after ONE "yes", the owner opens a tab and reads, for each host and each
Linux user of this PC's WSL, which AI programs are there, which account each is signed in to, how much of each
limit is left and when it resets — and can trust the time each line was read.**

## Owner decisions

Decisions 1–20 are of 2026-11-19: the owner's words are in the appendix, § A, and the list with how each stands
after the stop in § J. They stand, but for 4, 8, 14, 18, 19 and 20, which 22, 23 and the answer to Q3
replace or change: the appendix, § K.21, word for word, with the gloss of the ones cited below (5, 7, 13, 16, 17).

21. **2026-11-25, entry 2.2.** A `Creds: wsl / …` terminal is a local WSL terminal. WSL is read on this PC: every
    distro of `wsl -l` and every Linux user in it ("all distros, all users"), read-only, not over ssh. **One
    limit of this text:** a distro that is not running is not started (Q2, answered). The second limit of
    2026-11-25 — root of a distro is not read — was lifted by the owner at the approval: 24.
22. **2026-11-25, entry 2.3.** One permission, once: "may I read?" — a "yes" reads every host, now and later. A
    *Not this host* for one host stays.
23. **2026-11-25, the same check.** The view is redone so that it can be read: which host, which AIs, which
    accounts, the limits, when they reset.
24. **2026-11-26, at the approval of epoch 3: "root too".** Root of a running distro is a place of its own, read
    **as itself**, with root's own `HOME`; root's `claude` and `agy` are then started as root. Still never: root
    with another user's `HOME`, or a user's read as root (§ 2, *Security*). It was candidate C9; it is in E3.
25. **2026-11-26, with the approval of epoch 4: the login session of a WSL read is accepted.** On a distro with
    systemd, the first `-u <user>` call after the distro starts makes WSL open a login session for that user,
    root included: `/bin/login -f`, `systemd --user` and a login shell (`bash` in W9), which runs that user's
    profile (`~/.bash_profile` in W9) — root's as root. A first login makes two empty files in that home
    (`.motd_shown`, `.cache/motd.legal-displayed`), and the session stays until the distro stops. With
    `systemd=false` there is none. The owner was shown it (W9) and chose to accept it, not to read fewer users.
    So a read is named for what it is: the permission's text (§ 3) and *Security* say it.

**The owner's answers, 2026-11-25 evening:** Q1 **yes** (one "yes" for everything read from a host, the chat's
session read included); Q2 **not started** (a stopped distro is never started for a read); Q3 **one more one-time
"yes"** for "install `jq` where it is missing", asked once for all hosts; without rights the command is shown. Q4 was
not asked that evening. The owner also gave the go for the cut of *Build order* step 1, over the PR cap.

**The owner's answers, 2026-11-26, with the approval of epoch 3:** root is read too (24); Q4 **yes** — the owner
checks a VSIX built from the head of each of E2, E3 and E4 in a real window BEFORE that epic merges (merge
condition 5); and **the go for the measurements** W9, on this PC, and W6 and W7, on a VM (§ 5).

The four questions as they were asked, with the line the text took for each, are in the appendix, § J.

## What the code does today — verified on `d0241e45` (main, after #1047)

**The section, with every file and line, and what was measured, is in the appendix, § J, word for word.** No
extension release holds this code: the newest tag is `extension-v2.0.0`, cut before #1047. The short list of what
the design below stands on, with its files and lines, is in the appendix, § K.1.

- **Measured since, 2026-11-26:** W6, W7 and W8 on a VM, and W9, WSL on this PC (§ 5).

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
  name order. The block of `wsl` is headed *wsl — this PC* with the count of its distros; under it one line or
  table for each distro and user (*Ubuntu · user-a*), and a distro that is not running has the line *not
  running, so not read*. **The picture of the page: the appendix, § K.8.**
- **A host's heading** holds the host's name and its latest read result, one of: *read <age> (<clock time>)*;
  *never read*; *not allowed*, with the reason; the failure IN FULL — the runner's own sentence — with its age; or
  the missing-`jq` explanation. *Not this host* is on the heading of every host that is not refused; *Read this
  host again* on a refused one; *Copy install command* beside the missing-`jq` explanation. A host whose last read
  failed keeps its older table under the heading, marked with the time of that read.
- **One row for each tool that is installed there**, in the fixed columns **Tool | Account | 5-hour window | Weekly
  window**. A tool that is absent has no row; a place with no tool says so in one line. **But root:** every
  distro has one, so a root place with no tool has no line. A root read that failed says so.
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
  against its own model and drops a message it does not know. **The two messages that widen what is read,
  `allow` and `read-again`, change nothing by themselves:** each raises a modal question in the extension host,
  and only the person's answer there gives the permission or lifts the refusal. `refuse`, `stop` and `withdraw`
  only narrow, and act at once. **The clipboard is written by the extension host**
  (`vscode.env.clipboard`), with the fixed string of the closed `jq` table for that place's manager word (§ 4):
  no text from the page and none from a VM is ever copied.
- ***CoAI: hosts read over ssh*** stays as it is, a pick list with the three choices for one host. It gains one
  first row for the permission (§ 3).

### 2. The round — places, the permission, and WSL on this PC

What E1 built stays unless a line here changes it (the appendix, § I, *2. The round*; its short list: § K.12).

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
     that is not running is listed and not started** (Q2). A list call starts nothing, but any `-d` call
     starts a stopped distro, and an idle one stops about 15 s after its last call (W9f). **So the running
     list is read again right before every call to a distro, and a distro that is not in it gets no call.**
  2. the Linux users of each running distro: the pinned users script (§ 4), run **as root**. It names the users
     with a UID from 1000 to 59 999 whose home folder exists and whose shell is not `nologin` or `false`, **and
     root itself — UID 0 under the name `root` — on the same two conditions** (decision 24). A UID from 1 to 999
     is a system account and is never named; neither is a second name with UID 0.
  3. for each such user, the pinned accounts script **as that user**:
     `wsl.exe -d <distro> -u <user> --cd / --exec /bin/sh -s -- <mode>`, the script on stdin. `--exec` starts no
     shell of the user, and the process it starts reads no profile (W9b). **But on a distro with systemd
     WSL itself opens a login session for that user at the first such call, and it runs the user's profile:
     accepted (decision 25; *Security*).** **Root is a place of its own, (distro, `root`), read as itself**:
     the same argv with `-u root`, the same script and limits, and root's own `HOME`, the one of its passwd
     entry. That starts root's `claude` and `agy` as root, which the owner chose (*Security*). **Never root
     with another user's `HOME`**: that would run that user's own `~/.local/bin/claude` as root (`cons`
     `78e6d44b`). **And never a user's read as root**: the `-u` word of an accounts call is the user of its
     place, and nothing of ours names a `HOME`.
  The caps: 16 distros, 16 users in a distro, root among them, 32 WSL places in a round; what is past a cap is
  counted in the heading, never read. In a window that does not run on Windows, `wsl` says *WSL is read from a Windows window
  only* and no call of any kind goes to it. `wsl` is never given to `ssh` on Windows.
- **`wsl.exe` is called as `ssh` is**: one absolute path, `<SystemRoot>\System32\wsl.exe`, never the `PATH`; an
  argument array, `shell` off; the script on stdin; 30 s and 64 KiB, the tree killed. The distro and the user in
  an argv come from the enumeration and pass their class first; neither can start with `-`. **Its
  environment holds no `WSLENV`**, so no variable of the Windows side crosses into a distro. `HOME` crosses in
  no case: `wsl.exe` sets it from the passwd entry of the `-u` user, whatever the caller sends (W9b).
- **A reading belongs to a place.** The memory is keyed by place — `ssh:<host>`, `wsl:<distro>/<user>` — so two
  users of one distro never overwrite each other, and root's reading is never a user's. The enumeration's own result (the distros, which run, a failure)
  belongs to the scope host `wsl`.
- **Usage once for each account in a round — the account first.** As E1 built it, now over places: every place's
  first call reads no Claude usage; the first place of the round that answers with an account whose usage is due
  is called once more, with `claude-usage`. A WSL user and an ssh host with the same account share one usage read.
  **No usage call is ever made for an account whose usage was read in this round.**
- **The usage call names the account it is for, and the SCRIPT checks it** (the plan round's finding 2; § 4). The
  call carries a digest of the account the first reply named. The script reads the account again and runs the
  usage command only when the digest is the same; else it answers *account changed* and runs no usage command.
  So a place that changed to an account already read in this round cannot ask for it a second time.
- **The round JOINS two replies** — the usage call reads Claude only (§ 4), and today a place's whole reading is
  replaced by its last reply (`publishedState`). The join is E3.S2's. The Codex and agy parts of the first reply
  stay. *The same account:* its usage is shown and counted for it. *Account changed:* the Claude part is the
  account the second reply names, with no usage of its own — the figures of this round's read of that account,
  if there is one — and the account the call was for is read by its next place; no third call is made. *A second
  call that fails or cannot be read:* the first reading stays whole and says why the usage is not read.
- **The usage policy** is the one constant `USAGE_POLICY`, `off` until E4. Its other values are the rows of the
  W6 table (the appendix, § K.17).
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

**What E1 built and this plan keeps** — the refusals file of each side, its lock, its markers and the copy-in:
the appendix, § I, *3. The consent*, the S1 spec, and the short list in § K.12.

**What changes: nothing is read before one explicit "yes", and that "yes" is one for all hosts** (decision 22;
`cons` `78e6d44b`).

- **The permission** is one record of the side, `grant: { utc, stamp }`, in the SAME locked file. `globalState`
  cannot keep it for the reason of entry 1.2: a stale window that sends its whole copy could put back a permission
  the person withdrew. A build that does not know the key keeps it through a write, as the file's rule already says.
- **A withdrawal wins like a "no" — and it is a file of its own.** A withdrawal does not delete the record: it
  makes `withdrawn-<stamp>.json` beside `refused.json`, by an exclusive create, as a marker is made. **The
  permission is in force only when the grant's stamp is HIGHER than the stamp of every withdrawal file.** An
  *Allow* writes a grant whose stamp is one above the highest withdrawal it reads, and a withdrawal takes a stamp
  one above the highest stamp it sees — of a grant, of a withdrawal file or of the copy; a grant-write with a
  lower stamp is refused. So a former lock holder that wakes up and renames its stale `refused.json` over the new one brings
  back a grant with an OLD stamp, which is not in force: no writer of `refused.json` ever renames over a withdrawal
  file. The stamp is a whole number that only grows. A write of *Allow* removes the withdrawal files it
  cannot read and the ones below the newest, and leaves one readable file whose stamp is one below the grant's.
  An *Allow* goes above only the withdrawals that were there when it began: one that appears later ends it as
  not given. The copy of the newest withdrawal stamp in the state row of `globalState` tells another
  window at once and is copied into the folder at every read, as a "no" row is.
- **Given only by a person's press.** The first press of the icon without a permission opens the tab and asks ONE
  modal question through the one door (`notifyAndAsk`, code `read-accounts-permission`). The state row of
  `globalState` remembers that it was asked; a row that is lost asks once more. Later it is asked by
  *Allow reading…* in the tab, by the first row of *CoAI: hosts read over ssh*, and **by a press of a chat door
  while there is no permission** — that press reads nothing; after an *Allow* the person presses again. Buttons: *Allow reading*,
  *Not now*. The detail says all of what a "yes" covers:
  - every host of an open `Creds: <host> / <session>` terminal, **the ones open now and the ones opened later**,
    with no further question;
  - a login over ssh with the person's own keys every 10 minutes while such a terminal is open, and, in a Windows
    window with a `Creds: wsl / …` terminal, a read of every Linux user of every running WSL distro of this PC,
    **root among them — root's own `claude` and `agy` are then started as root; and that in a distro with
    systemd such a read opens a login session of that user, which runs that user's profile, root's as root**
    (decision 25);
  - what is read: which of Claude Code, Codex and Antigravity are there, the account each is signed in to, and
    their usage; that `claude` and `agy` as found there are started for it, and that **Claude Code rewrites its
    own cache files on a usage read** (decision 13);
  - the Claude session of a terminal, when a chat door of that terminal is pressed (Q1);
  - that CoAI itself installs and writes nothing there; and how to stop: *Not this host*, *Stop the round*, *Withdraw the
    permission*.
- **Withdrawn by a person's press**, in the tab and in the hosts list. The readings are dropped and the timer is
  cleared at once, **and the chat doors read nothing from then on.** A withdrawal file that the disk did not take
  is held by its window and copied to `globalState` at once; while it waits, no window of the side makes a call
  of either kind. **A withdrawal can also answer `busy`** (the spec of E2.S1, gate E2-G1): its file is made, and
  its read-back is not final while another window holds the claim of the hosts file — its window says `waiting`
  until that write ends, or until *Save now*. An *Allow* that the file did not take changes nothing, and the
  person is told.
- **Who may be contacted — EVERY door needs the permission** (Q1: one "yes" for everything read; the plan round's
  finding 0). One function, asked before every call and before every result, and by the session door before
  every spawn:

  | Who asks | It may contact the host when |
  |---|---|
  | The accounts round | the folder can be read, AND nothing waits unsaved, AND this side holds the permission, AND the scope host is not refused, AND the round is not stopped |
  | The session read of a press | the folder can be read, AND nothing waits unsaved, AND this side holds the permission, AND the host is not refused |

  **A per-host "yes" of `coai.terminalHosts` allows nothing any more**, for either door: such a row is kept as it
  is stored, never honoured and never written again, and the per-host question `read-remote-session` is retired —
  the one question takes its place. The Stop stops the round and not the session read, as today.
- **T9, taken up: the session road asks that function before EVERY spawn of a press**, not once. A later spawn of
  a press never asks a person: when the answer is no longer "yes" it stops with the sentence of that answer.
- **T8, taken up: "something waits unsaved" is an answer of its own, `waiting`**, beside `yes`, `no`, `no-permission` and
  `damaged`. The session door and the tab then say: *A “Not this host”, a stop or a withdrawal is not saved yet on
  this side, so nothing was read. Choose “Save now”.* The sentence "the list cannot be read" is for a list that
  cannot be read.
- **What is retired** (each is in main since #1047 and in no release): the automatic "yes" and the write of it; the
  cap rule that drops an automatic row; the timer's use of a "yes" that was given for a session read; the setting
  `coai.terminalAccountsAllowNewHosts` in the manifest, `SAFETY_KEYS` (`src/modelKeys.ts`), `NEVER_TRANSFERRED`
  (`src/configTransfer.ts`), `src/sideConfig.ts`, the words of `src/remoteHostsSaid.ts` and the help; the row *This workspace sets …*; the daily notice `accounts-hosts-allowed`; the
  first-round notice `accounts-read-every-ten-minutes`; the fields `toldUtc` and `lastAutoNoticeUtc` of the state
  row; the door answers `not-asked` and `full` for a new host; the per-host question and its *Read* button, in
  the road and in the hosts list, which keeps *Not this host* and *Read this host again*. The parser still KNOWS a
  "yes" row and `how: 'auto'`, so a list that holds one is not damaged; neither is an answer.
- **The upgrade starts with NO permission, whatever is stored.**

  | Stored before | After the upgrade |
  |---|---|
  | A person's "yes" for a host (from 0.87.0 on) | **Not honoured.** The first press of a chat door asks the one question and reads nothing; after *Allow reading* every host that is not refused is read, by both doors. This changes a released behaviour, and the release notes say so |
  | An automatic "yes" (only the pre-release 2.0.901 wrote one) | No answer. Nothing is read |
  | A "no", in the file or as a row | A refusal, as before, on every side that reads it |
  | The Stop | Stays. A permission given later does not start a stopped round |
  | `coai.terminalAccountsAllowNewHosts` in a settings file | Not read |

- **The sides.** The permission is of one side, like the file: a WSL or remote window has its own, and asks its
  own question. A withdrawal is not of one side: its copy in `globalState` stops every side that reads it, and
  each such side writes it into its own folder. A copy never gives a permission. The owner uses a second side for the chat only (2026-11-24): the first press of a chat door there
  asks the one question once, for that side.
- **What a person can and cannot choose.** One "yes" for everything read; a "no" for any one host; a stop of the
  round; a withdrawal, which stops both doors. Not: the session read without the permission; a withdrawal on one
  side of the machine only; the accounts read on
  a few hosts only, without refusing each of the others; and not the session read of one host without its
  accounts read while the round reads the other hosts — *Stop the round* stops the accounts read of every host
  and keeps the session read.

### 4. The scripts — revision 2, and the users script

E1's script, its reply and its parser stay as built and as specified (the appendix, § I, *4. The remote script and
its reply*, and the S2 spec; the short list of what stays: § K.12). **Revision 2 changes four things, in ONE new
pinned text with one new sha256** (E3.S2, the cornerstone):

1. **The script sets its own `PATH`; it does not add to the one it was given.** `$HOME/.local/bin`, then a fixed
   list of system folders that the spec names. On WSL the inherited `PATH` holds the Windows folders (W9b), and a
   `claude` of the Windows side found there would be started through interop and read as the Linux user's. Over
   ssh the list is what a shell that is not a login shell has today. A test holds that a `claude` which is
   reachable only through the inherited `PATH` is not started. **The list holds no folder that a user who is not
   root can write**: a place may be root (§ 2), and what root's read finds by its `PATH` runs as root. Every
   entry is absolute and of the spec's closed list — no `/tmp`, nothing under `/home` or `/mnt`, no empty or
   relative entry — and a test holds it. **The list is not trusted alone: as UID 0 the script checks, before
   it starts a program, the owner and the mode of its file and of every folder above it** (*Security*).
2. **The mode `claude-usage` reads Claude only**: the account, the version and the usage. It reads no Codex file
   and does not start `agy`: the first call of that place did both seconds before. So a place that reads an
   account's usage starts `agy` once in a round, not twice. The reply of that mode holds the Claude part only, the
   parser reads a reply against the mode that was sent (as today), and the round joins the two replies, with its
   check of the account (§ 2). **The join is part of this story, E3.S2**: a script whose usage reply holds no
   Codex and no agy part must not merge without the round that keeps those parts from the first reply.
3. **The mode `claude-usage` takes the account it is for, and checks it before the usage command** (the plan
   round's finding 2). Its second operand is 64 hex digits: the sha256, made on this side, of the account lines of
   the first reply exactly as the script printed them. The script reads the account, makes the same digest of the
   lines it is about to print (`sha256sum`, checked first with the other tools), and **runs the usage command
   only when the two are equal**. Else the reply holds the account it found and `COAI-USAGE claude skipped
   account-changed`, and no usage command ran. A digest, not the email: an argument can be read by every user of
   that machine. What is left is an account that changes between the script's own two commands, a second apart.
4. **The time table counts the KILL second (T46).** A vendor command gets a TERM at its limit and a KILL one
   second later (`timeout -k 1`). Where the reader's limit equals the vendor's — the two version commands — that
   second is added:

   | Mode | The stages, at their latest | Sum | With ssh's 5 s to connect |
   |---|---|---|---|
   | `no-claude-usage` | account 4 s; version 2 + 1 s; the Codex files 1 + 1 + 3 s; `agy` 4 + 1 s | 17 s | **22 s** |
   | `claude-usage` | account 4 s; version 2 + 1 s; usage 9 s | 16 s | **21 s** |

   Both are 8 s or more under the runner's ceiling of 30 s, and no limit is shortened. **The test sums, for each
   mode, every stage's reader limit, one KILL second for each stage that can outlive its reader, and the connect
   time, and asserts the total is at most the ceiling minus 2 s**; and a run with a vendor program that ignores
   TERM ends inside that sum. What was not taken: the appendix, § K.4.

**The users script** (new; E3.S1, the security spec): a second fixed text with its own sha256, in the same shape
— one function, called only at its end, a begin line `COAI-USERS 1` and an end line. It opens `/etc/passwd` and no
other file, starts no program, and prints for each entry that § 2 names one line with the user's name and UID. A
name counts, there and on this side, only when it fits `[a-z_][a-z0-9_-]{0,31}`; at most 16 lines. A UID counts,
there and on this side, only when it is from 1000 to 59 999 under a name that is not `root`, **or 0 under the
name `root`** (decision 24). Never printed and, if a reply holds one, never read: a UID from 1 to 999; UID 0
under another name; the name `root` with another UID. It has no operand.

**The operands** of the accounts script: one of two fixed words, and with `claude-usage` the digest, which this
side makes and which must fit `[0-9a-f]{64}` here and again in `sh`. No text from a terminal's name, from a VM or
from a distro is ever an operand as it is; a distro's and a user's name are arguments of `wsl.exe` only.

### 5. What is still to be measured

Each needs the owner's go for its exact commands. **W9, W6 and W7 have the owner's go (2026-11-26); W8 has none
yet.** **W-numbers here are measurements** (the S1 spec uses W-numbers for its sentences). **Run 2026-11-26:** W7
passed, W6 → every round (retirement seen), W8 recorded:
[RESULTS § 13](../research/RESULTS_accounts_and_usage_on_a_vm.md); **W9:**
[RESULTS_wsl_on_this_pc.md](../research/RESULTS_wsl_on_this_pc.md).

**The table of the four as they were set, and what W9 showed part by part: the appendix, § K.17, word for
word.** What holds of it: W6 is the gate for growth, W7 the gate for the usage read, W8 a record (decision
16), and **W9 the gate for E3**. The WSL road is specified on what W9 showed: no `HOME` or `WSLENV` of the
caller changes the `HOME` of a call, for root too (b); no script changed a file in a home (c); a killed call
leaves no process of its own (e); a call starts a stopped distro (f). **"Read-only" is promised for WSL only
as far as (c) showed**, and beside it stands what WSL's login session does in the home of its user (decision
25). *Not measured:* the lists with no distro and with none running; a read with a real vendor tool; a
Windows process as the caller of every call.

**The login session — accepted (decision 25).** W9 left this one item open (§ K.17, its last paragraph), and
the owner chose with the approval of epoch 4. **The spec of E3.S1 specifies the mechanics** — what a read may
assume of a session, and the words for it in the tab, the help and `SECURITY.md`.

Within the plan as written: the field names; the limits under the ceilings; the policy that W6 selects; the exact
argv words of `wsl.exe` that W9 confirms; root or the default user for the users script; the exact form of the
check that holds the rule "nothing that another user can write is started as root" — the owner and the mode of
a program's file and of every folder above it, in the script, before a start as UID 0 (the spec of E3.S1).
Outside it, and so a change:
anything installed on a VM or in a distro before E6; a third kind of script; a write by a script of ours; a
start, on purpose, of a distro that is not running; a call as root that reads another user or runs with another
user's `HOME`; a read of a system account; road (b).

## What this plan does NOT do

- No change to `src_mcp`, `src_server` or CredsForDevs.
- No read of any kind before the permission, and none of a refused host, ever. No question raised by a timer.
- No automatic allow of a host, and no setting for it. No setting that a workspace can set for the person.
- **No install before E6.** Nothing is written or installed on a VM or in a distro by a script of ours.
- No `ssh` to `wsl` on Windows. No read of WSL from a window that does not run on Windows. A distro that is
  not running is never started on purpose (Q2); the race is residual 12. No read of this PC's Windows-side
  accounts.
- No script run as root for another user, and nothing OF OURS as root but the users script and the read of
  root's own place (decision 24). **Not prevented, as WSL starts it and nothing of ours does: the login session
  of a user that is read, with that user's profile — root's as root** (decision 25). No read of a system account —
  a UID from 1 to 999 — and none of a second name with UID 0.
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

**The edges** are five, and E6 adds a sixth (word for word: the appendix, § K.23): ssh calls by a timer, now
only after the permission; local `wsl.exe` calls by it — **with the login session that WSL opens for a user
that is read**; a start of Claude Code on a usage read; a start of `agy` every round; VM text in a webview.

**Finding F1 is answered, not gone** (the appendix, § B). **Before the permission nothing is contacted,
whatever is stored and whatever a terminal is called.** After it, a terminal's name chooses a host again, and
nothing else, by the person's own word: the question says "and the ones opened later". The judgement, word
for word: the appendix, § K.18.

**The permission is in front of EVERY door, and a withdrawal wins like a "no"** (the plan round's findings 0 and
1). Its rules are those of § 3; the judgement on them: the appendix, § K.15, word for word. What is left is
residual 9.

**No secret passes through our scripts** (road (a), as built; the table of what is opened and what is not: the
appendix, § I, *Security*). It holds for a WSL user as for a VM: the script opens no credential file, holds no
token and gives none to a program.

**WSL — who runs as whom.** The accounts script runs as the Linux user it reads, with that user's `HOME`, through
`--exec`, so the process we start reads no shell profile (the login session that WSL adds on a distro with
systemd is the fifth thing named below). Of ours, root runs two things and no other: the users script — fixed text that opens
`/etc/passwd` — and the accounts script **for root's own place**, with root's own `HOME` (decision 24). **Root
never reads for another user**: the `-u` word of an accounts call is the user of its place, nothing of ours
names a `HOME`, and `WSLENV` is dropped. **A test fails on any argv that has `-u root` with a mode word for a
place that is not (distro, `root`), and on any call that runs a mode as root with a `HOME` that is not root's
own.** A distro's and a user's name reach an argv only after their class, and neither is ever part of a script
or an operand. Five things are named, not prevented:

- **Root's own `claude` and `agy` are started as root, by our timer**: with full rights in that distro, every
  round, with nobody at the keyboard. The owner chose it (decision 24), and the permission's text says it. What
  these programs do on a start, they do as root (W7, W8). An ssh host whose login user is root is read the same
  way; step 0 was measured on one. **The rule: as UID 0 the script starts nothing that another user can write, and
  it checks that before every start.** The program's file — as found, and as each link on the way resolves —
  and every folder above it up to `/` must be owned by UID 0 and not writable by group or others (as ssh's
  `StrictModes` and sudo's `secure_path` ask). When one is not, that tool is not started in root's place, and
  its row says *not read: <path> is writable by another user*. The spec of E3.S1 specifies the check, and a
  test holds it. What is left is residual 13. The person at this PC gains nothing: `wsl.exe -u root` asks the
  Windows user for no password.
- **A program that a Linux user owns is started by our timer**, as that user and never as root; in WSL such
  a process can start Windows programs as the Windows user (interop). **The inherited `PATH`** is not used
  (§ 4). **A kill** leaves no process of the call (W9e). These three, word for word: the appendix, § K.19.
- **A read of a WSL user opens a login session of that user, by WSL** (decision 25, which says what it is;
  W9b). **So whoever can write a user's profile decides what runs as that user when our timer first reads
  the distro, and root's profile runs as root.** WSL starts it before any line of ours, so the check before
  a start as UID 0 does not reach it (residual 13), and a kill of our call does not end it. It is the profile
  that every login of that user runs; what is new is that our timer brings the login about, with nobody at
  the keyboard. The owner accepted it, and the permission's text says it.

**The tab.** Its rules are those of § 1: text only, a strict CSP, messages that name, a modal question in the
extension host for the two buttons that widen what is read, a fixed string for the clipboard. The judgement
on it: the appendix, § K.10, word for word.

**What an attacker gains**: the list of the stopped plan holds (the appendix, § I, *Security*), with the
changes of § K.22 and § K.16, word for word. In short: a terminal's name gains nothing before the permission,
and after it what F1 says; no reply reaches the extension; a second window or a reload cannot undo a refusal,
the Stop or a withdrawal (the file); **another Linux user of a distro whose root is read** gains nothing by a
tool's file or a folder above it that they can write, because the script checks both as UID 0 — what is left,
root's profile among it, is residual 13. **Logs**: a failed read logs the place, how it ended and the
runner's own last line; a script's stderr goes to `/dev/null`; stdout is never logged.

**Residuals.** Those of the stopped plan hold (the appendix, § I, *Security*: a person's `ControlMaster`; a login
every 10 minutes on every host of an open terminal; an email on screen; what Claude Code does on a start; a reset
in another zone shown as a dash; and the eight residuals of the refusals file), but for the third of the file: a
person's "yes" that a stale write puts back allows nothing any more. **The spec of E2.S1 holds the eight as they stand after its story (its
§ P14), and gate E2-G1 added a case to two:** 5(b) — the stale rename of a former holder that was an *Allow*
can put a grant above the withdrawal of a window that had not seen the copy, and it stands when the *Allow*'s
window ends before its read-back; 7 — a file made by hand with a stamp at the top of the count restarts the
count, and across that restart an older, higher grant beats a later withdrawal. New:

9. **A withdrawal is files on one disk.** A stale writer of `refused.json` cannot undo it (§ 3). What can: a disk
   that takes no withdrawal file (then it holds in the window that took the press and as its copy, as residual
   5d of the file, and the notice says so), and a program that deletes files as the person. R12 runs the stale
   writer. **And two windows that both end** (gate E2-G1): a withdrawal of a window that had not seen the copy,
   made while another window's *Allow* was being written, is below that grant until one of the two read-backs
   has run. When the *Allow*'s window ends after its write AND the withdrawing window ends before its read-back
   is final — its door said `waiting` until then — the grant stands, and nobody is told.
10. **The permission is GIVEN for one side, and a withdrawal reaches every side that sees its copy.** An *Allow*
    on one side allows nothing on another. A withdrawal on one side stops each other side at that side's next
    read of the shared state, and that side reads again only after an *Allow* pressed there. What is left: a
    stale write of the shared state that lowers or erases the copy — after the withdrawal and before the other
    side has read it, or before the withdrawal while the other side has not read since (as residual 2 for a
    refusal). That side then reads on until the copy is put back by a read of the withdrawing side, or until
    it is withdrawn there.
11. **20 hosts are 20 logins every 10 minutes**, and 40 with the usage on and no shared account; two windows
    double it (candidate C6).
12. **A stopped distro is never shown with numbers** (Q2). Any call to a stopped distro starts it (W9f), so the
    running list is read right before every call; a distro that stops between that list call and the call is
    started by it, in 3 to 5 s, and stops again about 15 s after its last call.
13. **What the check before a start as UID 0 leaves** (decision 24). A program that root itself put in root's
    `~/.local/bin` or in a system folder runs as root: root's own choice, and nobody's raised right. A distro
    where a file or a folder on a tool's path is another user's, or open to group or others, is refused for
    that tool, not read. The check itself stands on `/bin/sh`, which WSL or sshd starts before any line of
    ours, and on the tool it reads owners and modes with (the spec of E3.S1 names it). And root's tools run
    with full rights every round: a fault of one is a fault as root. **The login session is outside the
    check** (decision 25): a profile that another user can write runs with its owner's rights at our first
    read after the distro starts — root's as root — as at any login of that owner.

**Judgements.** Those of 2026-11-25 are in the appendix, § K.5, and the one on root as a place, of 2026-11-26,
in § K.9, both word for word. The judge's conditions are rules above: the detail of the question (§ 3); root
reads only as itself, with its own `HOME`; nothing that another user can write is started as root; the
permission's text says it. **On the login session (decision 25) no judge was asked**: its text here is the
planner's.

`SECURITY.md`: the section on the extension's ssh calls is rewritten by E2 — its sentence *"A host is contacted
unless the person refused it"* becomes *"No host is contacted before the person's one permission"* — and gains the
permission, where it is kept and what it covers, the tab and its CSP; by E3 the WSL road, who runs as whom, root's
own place with what is started as root, the login session of a read, and the interop residual; by E4 what W6
and W7 showed.

## Growth surfaces

- **The readings**: in memory, one for each place, a few hundred bytes each; dropped when the scope host's last
  terminal closes, when it is refused, when the round is stopped, when the permission is withdrawn, and with the
  window. At the caps: the hosts of the open terminals and 32 WSL places.
- **The WSL enumeration**: in memory, at most 16 distro names and 16 user names each, replaced every round.
- **The permission**: one key in `refused.json`, under 100 bytes. The file's caps stand: 100 refusals, 64 KiB.
  **Interrupted:** the file is the old one or the new one (a rename).
- **The withdrawal files** — new: `withdrawn-<stamp>.json`, under 200 bytes each. **Who retires them:** each
  *Allow* removes all but the newest, so one is left; past 8 in the folder a withdrawal first removes the oldest.
  **Interrupted:** a file that cannot be read counts as a withdrawal with the highest stamp, until a person's
  *Allow* removes it.
- **The markers**: as built, at most 32 of 1 KiB.
- **`coai.terminalHosts`**: 100 rows at most. Nothing automatic writes a row any more, so it grows only by a
  person's answers; automatic rows of the pre-release go at the next write.
- **The state row**: the copy of the Stop, the time the one notice was shown, the time the question was first
  asked and the copy of the newest withdrawal stamp; under 150 bytes.
- **The tab**: one webview for each window, kept while it is open; its model is the readings. One 15 s interval of
  the page, gone with the tab.
- **Children**: one `ssh` or `wsl.exe` alive at a time for each window, killed at 30 s, stdout capped at 64 KiB.
  For `wsl` in a round: 2 list calls, one users call for each running distro, one or two calls for each user,
  root among them, and one list call more before each of these.
  **Interrupted:** a reload leaves nothing on this side, and a killed `wsl.exe` no process of its call in the
  distro (W9e). **In a distro with systemd, by WSL:** one login session for each user that a call named, 16 at
  most, until the distro stops, and two empty files in that user's home at a first login (decision 25).
- **The timer**: one for each window, cleared when nothing may be read, disposed with the extension.
- **Log lines**: one when a place starts to fail and one when it reads again.
- **On a VM or in a distro, by our scripts**: nothing.
- **There, by Claude Code on a usage read**: four cache files, rewritten in place; and `~/.claude/backups/`, where
  **Claude Code keeps five files, about 0.9 MB, and deletes the older ones — seen on 2026-11-26 (W6)**, so it
  does not grow, and the policy is "every round" (§ 5). This plan deletes no file of Claude Code.
- **There, by `agy` on a start**: not measured (W8). Once a round for each place, since revision 2.
- **Calls to the vendor**: at most 6 an hour for each Claude account and window, by Claude Code; none while the
  policy is `off`. None by our code.

## Boundaries with other plans

The table, word for word: the appendix, § K.20. In short: **this revision replaces the per-host consent of
the terminal-door plan** (shipped in 0.87.0; § 3, *The upgrade*) and keeps its store key, its "no" rows and
*Not this host*; the todo T46 is closed by the stories document of E3, and T8 and T9 by that of E2; nothing
is changed in the plan of the timed-out ssh call, in the gauge of coai's own rounds or in the agent-models
mod, whose word rules are carried over and tested against its cases.

The three todo files say on their side that this revision takes them up and that epoch 3 is approved
(2026-11-26); each is built by the story that closes it.

## Build order

1. **The cut, during the stop** (*The full stop*, step 6: merged work that contradicts the revised plan is
   corrected in a new pull request; the owner gave the go on 2026-11-25). **Done: merged as #1051,
   `b1ec467c`.** Its four parts as they were planned: the appendix, § K.6, word for word.
2. **The owner approves epoch 3**, with the answer to Q4, after the re-review round of this text. **Done,
   2026-11-26:** with Q4 "yes", root read too (decision 24) and the go for W6, W7 and W9.
3. **E2 — the permission and the table.** Its stories by Opus; the security spec of E2.S1 by Fable before its
   code; the code gate; a VSIX from the pull request's head; the owner's rows; merged.
4. **Alongside E2 and E3, by hand, each with the owner's go:** W9 on this PC — before the spec of E3.S1 is final —
   and W6, W7 and W8 on a VM. **All four were run on 2026-11-26** (§ 5).
5. **E3 — WSL on this PC, and revision 2 of the script.** E3.S2 — the script, its parser and the round's join of
   the two replies, together — may merge as its own pull request first, so that E4 waits for nothing else of E3.
6. **E4 — the Claude usage on.** Only when revision 2 is merged, W7 passed and W6 selected the policy. When W7
   failed, the read stays off and E4 records that.
7. **E5 — the long runs on the read-only build.** No product code.
8. **E6 — the installer** (candidate C1 of the stopped plan), with its own stories, its cornerstone spec and its
   own real-host checks, under its own one-time "yes" (Q3). **It ends with E5's gates run again on its own
   build** (R7 and R12, with the installer's consent and its withdrawal): the build that is released is E6's.
9. `review_feature` over the built epics, every finding resolved.
10. The extension release, one for the whole plan: the tag pushed alone and its run observed; `POST_DEPLOY.md`
    run; the plan promoted with its deviations, its appendix and its stories documents.

**Why this order:** the appendix, § K.2, word for word.

## Test plan

**E1's tests stay** for what stays (the appendix, § I, *Test plan*; the short list: § K.12). **Deleted with what
they hold:** the tests of the automatic "yes", of the cap's eviction, of the setting at both values
(`terminalAccountsSetting.test.ts`), of the two notices, of the QuickPick's rows and its host scenario. New:

- **The permission — the acceptance of entry 2.3** (pure, over the fake disk and stale views of E1's tests). A
  store seeded with a person's "yes", an automatic "yes" of the pre-release, one refusal and 20 live hosts, `wsl`
  among them: **zero calls** of any runner, by ticks and by presses, before the permission. After *Allow*: one
  call for each host that is not refused, **none to the refused host**, and a host whose terminal opens later is
  read in the next round with no question. With `wsl` refused: **no call to `wsl.exe` at all**, the list calls
  among them. The timer raises no question (a test fails if the ask seam is called from a tick). The modal is
  raised by the first press of the icon and not by the second.
- **Every door (finding 0).** A person's "yes" for host h and NO permission: a press of a chat door on h asks the
  one question and **runs nothing**; *Not now* → still nothing; *Allow* → the next press reads. After a
  withdrawal: a press on h runs nothing, and neither does a spawn of a press that was in flight.
- **Withdrawal and its races (finding 1).** A withdrawal clears the timer and the readings; a result in flight is
  dropped. A withdrawal in window A; A is disposed; a stale window B writes `globalState`; both are built again
  as after a reload → no call. **Withdraw; close the window that saw it; a former lock holder renames its stale
  `refused.json`, which holds the grant, over the new one; reload → still withdrawn, no call of either door.** A
  grant-write whose stamp is not above the newest withdrawal is refused. *Allow* after a withdrawal reads again,
  and leaves one withdrawal file. A withdrawal file that cannot be read is a withdrawal. A withdrawal the disk did
  not take → no call in any window of the side; *Save now* lands it. A stopped round stays stopped through *Allow*.
- **The upgrade**, as the table of § 3, row by row; a list with a "yes" row or a `how: 'auto'` row is not damaged.
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
  running. A distro that is not running gets no call, and neither does one that left the running list after
  the round began. The caps of 16, 16 and 32, root counted as a user. The
  users script: the users from 1000 to 59 999, and root as UID 0 under the name `root`, and no other — **a UID
  from 1 to 999, UID 0 under another name and `root` with another UID are not printed, and a reply that holds
  one gives no place for it**; `nologin`, `false` and a home that is missing are
  left out, for root as for a user; its hash is held. **Root as a place (decision 24): `-u root` with a mode
  word is allowed only for the place (distro, `root`).** A test fails if the `-u` word of an accounts call is
  not the user of its place — so on a user's read as root; if an argv or the environment of a call names a
  `HOME` or holds `WSLENV`; and, with the real script run under a `HOME` that stands for root's, if a `claude`
  or an `agy` in another user's `~/.local/bin` or outside the script's own `PATH` is started. The `PATH` is held
  to its closed list. **The check before a start as UID 0** (the real script; the spec fixes how a run that is
  not root stands for UID 0): a `claude` or an `agy` whose file, whose link's target or a folder above either is
  another user's, or writable by group or others, records NO start, and the reply names that path; with all of
  them root's and closed, it starts. A root place with no tool has no line; a root read that failed has one. Not on Windows: `wsl` gets no call of either runner.
- **Revision 2 of the script.** A `claude` reachable only through the inherited `PATH` is not started. In
  `claude-usage` the false `agy` records no start and no Codex file is opened; the reply holds the Claude part
  only. **The join:** after the second call the place still holds the Codex and agy parts of the first reply; the
  same account → its usage is shown and counted for it; a second call that fails → the first reading whole, with
  the reason. **The account check (finding 2), with the real script:** the digest fits → the false `claude`
  records ONE usage start; another digest, or one that fails its class → `skipped account-changed` and NO usage
  start. **In the round:** place A read the usage of X; place B first answers Y and, at its usage call, has X →
  the script skips, X's usage command ran once in the round, B shows X with A's figures, and no third call is
  made; a call for an account already in the round's usage set is never built. **T46:** the sum of each mode with its KILL seconds and the
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
- [x] **The cut** is merged before any extension release: no build from main reads a host by itself, by the
      round or — on an automatic row — by a chat door; its help and `SECURITY.md` say so; the setting says *no
      effect in this build* (#1051, `b1ec467c`).
- [ ] **Nothing is read before ONE permission.** It is asked once, by a person's press, in words that name the
      later hosts, the WSL users with root, what is read and what is started; it is kept in the locked file of the side;
      a withdrawal stops every read at once. The acceptance of entry 2.3 is a test and a real-window row: an old
      "yes", an automatic "yes", one refusal and the owner's hosts give zero logins before the permission and none
      to the refused host after it.
- [ ] **A "no" for one host, the Stop and a withdrawal always win** — on the side that keeps them, against a
      round, a reload, a second window, a stale write and a damaged file — shown by the file's tests and by R12.
- [ ] The automatic allow, `coai.terminalAccountsAllowNewHosts`, its notices and its rows are gone from the code,
      the manifest and the help. **A per-host "yes" allows nothing**: no chat door reads a host before the
      permission or after a withdrawal, and the per-host question is gone.
- [ ] **A withdrawal cannot be undone by a stale writer**: it is a file of its own with a higher stamp; the test
      "withdraw, close the window, a stale rename, reload" ends withdrawn, and R12 (d) runs it.
- [ ] The session door asks before every spawn and has its own sentence for a change that waits (T9, T8).
- [ ] **The tab can be read.** The icon opens ONE tab, grouped by host: for each host its latest read result with
      its clock time, and one row for each installed tool with the account, the 5-hour window and the weekly
      window as "% left" with its reset; a shared account says so; usage that was not read says why; a failed
      host says the failure in full; a host without `jq` has the command and a copy button. Ages change while the
      tab stays open. Ctrl+F works. Every value from a VM is text, under a strict CSP.
- [ ] **WSL is read on this PC**: every running distro and every Linux user in it, root among them, each as that
      user, each a reading of its own. **Root is read as itself** (decision 24): with its own `HOME`, never for
      another user; as root, of ours, only the users script and root's own read run, and our script starts
      nothing as root that another user can write. **The login session that WSL opens for a user it reads, with
      that user's profile, is named in the permission's text, the help and `SECURITY.md`** (decision 25). `wsl`
      is never given to `ssh` on Windows; with `wsl` refused nothing is
      enumerated. W9 has a recorded result, and what the documents promise as read-only is
      what W9 showed.
- [ ] **The script is revision 2**: its own `PATH`; a usage call that reads Claude only and runs the usage
      command only for the account it was sent for, so no account is asked twice in a round; each mode's worst path,
      KILL seconds counted, at least 2 s under the ceiling, held by a test; one new hash in the spec and in a
      test. No secret is in an argument, the environment, a reply or a log; the person's activity is not in a
      reply.
- [ ] **The Claude usage is on**, with the policy W6 selected, after W7 passed — or it is off and the release
      says why. No build ran the usage command by itself before W6 and W7 had recorded results.
- [ ] Nothing of a reading is persisted. Road (b) is not in the code.
- [ ] The real-window rows of E2, E3 and E4 each have the owner's recorded result before that epic merged; the
      rows of E5 have one before the tag, and R7, R12 and R18 passed on the build that is tagged. W6, W7, W8 and
      W9 are in the two RESULTS files.
- [ ] Docs with each epic: the help in five languages; `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md`; `SECURITY.md`; the `POST_DEPLOY.md` question asked.
- [ ] **E6**, the installer of decision 17, is built by its own stories and spec and passed its own checks:
      nothing is installed before ONE more "yes", asked once for all hosts and withdrawn by a press; where the
      user has no rights the command is shown. No setting for it exists (decision 20).
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
5. **The owner's real-window rows of the epic have a recorded result on a VSIX built from that head** (Q4,
   answered "yes" on 2026-11-26). A row
   that fails is fixed on the same pull request and run again.
6. `SECURITY.md` and the help say what the head does.

**For E4 also:** W6 and W7 are recorded, W7 passed, and revision 2 of the script is on main.
**For the cut of step 1:** conditions 2 to 4 and 6, a test that no runner is called by a tick or by a press, and a
test that the session door asks its question for a host whose only row is an automatic one.
**For the release:** E5's rows recorded; **the gates R7, R12 and R18 passed on the head that is tagged** — E6's
build, or E5's when E6 is not in the release; `review_feature` resolved; no extension release from main between #1047 and the cut.

## Epics

**Six epics; one is done.** The split is Fable's, after both consultations of the stop (`78e6d44b`, `7315feb0`).
The consultants' rule of 2026-11-19 holds for each, and it is why the split is as it is (the appendix, § K.3).
The rows keep the numbers of the stopped plan where their
meaning is kept (the appendix, § I, *E2*); R11, the row of the retired setting, is gone; R13 to R17 are new.

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`. Cornerstone quota: Q = ⌈2·E/5⌉ =
⌈2·6/5⌉ = 3. The cornerstone areas Fable names, each with its reason in the appendix, § K.14: **(a)** what a
script touches there and what it prints; **(b)** the reply's frame and the parser; **(c)** who is contacted and
when; **(d)** the different results of a read, and a missing number that is never a zero; **(e)** who runs as
whom on this PC; **(f)** a change of a machine (E6). Areas (c) and (e) are authorisation: their specs are
Fable's by the security row, outside the quota. Planned marks, made by the stories documents: E3.S2 for (a)
and (b), and E6's spec for (f).

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
  `waiting`, the retirement of the automatic row); ONE export of `src/chatStoreLock.ts` — its reader of a lock
  as it is, for a withdrawal's read-back, with no rule of the lock changed (gate E2-G1); `src/remoteRead.ts` and `src/remoteSessionHost.ts` (the door
  before every spawn, the per-host question out, the first row of the hosts list); `src/remoteAccountsRound.ts` and
  `src/remoteAccountsHost.ts` (no call without the permission, the summary, the one notice; the QuickPick out);
  a NEW pure module for the table's model and a NEW page with its panel; `src/remoteAccountsRows.ts` reduced to
  what the model keeps; `package.json`, `src/modelKeys.ts`, `src/configTransfer.ts`, `src/sideConfig.ts` and
  `src/remoteHostsSaid.ts` (the setting out); the help in five languages; `notification-sites.json`; the tests; the
  module documents and `SECURITY.md`. In this epic `wsl` on Windows says *WSL on this PC is not read by this
  build* and gets no call.
- **Depends on.** The cut merged; epoch 3 approved. Both hold.
- **Stories** (named here; written by Opus). **E2.S1 — the permission (SECURITY SPEC by Fable, outside the
  quota):** the record in the file, the withdrawal files and their stamps, the one door for the round and for the
  session read, the per-host "yes" and its question retired, the upgrade, T8 and T9. **E2.S2 — the table's model** (pure). **E2.S3 — the tab:** the
  panel, the page run by its tests, the messages, the ages that tick, the clipboard. **E2.S4 — the round under
  the permission.** **E2.S5 — the documents.**
- **Done when.** The merge conditions hold, with these rows in the owner's window, on the owner's ssh hosts:

| # | What is run | Pass |
|---|---|---|
| R1 | The icon | It sits beside *Ask other AI* and opens ONE tab; a second press shows the same tab; Ctrl+F finds a host; with no `Creds:` terminal the tab says so |
| R14 | **The permission.** The profile holds a person's "yes", the automatic rows of 2.0.901 and one *Not this host*; the owner's terminals are open | Before the permission: **0** ssh logins on any host in 20 minutes (the VMs' ssh logs). The question is asked once. After *Allow reading*: the first login within 60 s; and when the toolbar says the round has ended — one host at a time, up to 30 s each — exactly 1 login on each host that is not refused and 0 on the refused one. A chat door pressed before the permission, on the host with the "yes": the question, and 0 logins. A terminal of a new host, opened later: read in the next round, no question. *Withdraw*: 0 logins in 20 minutes, by the round or by a chat door, also after a reload |
| R15 | **The table.** At least 3 hosts, one of them with a Codex record | For each host the owner reads, without help: the host, its tools, each account, the two windows with "% left" and the reset. The Codex figure equals the record on that VM. A Claude account says why its usage is not read. **The tab stays open 12 minutes untouched: the age of each read grows and matches the clock time beside it.** A hover shows the exact time. A narrow tab stacks the fields |
| R5 | A host that cannot be reached | Its failure in full under its heading within 30 s; the other hosts are read in that round |
| R6 | Stopping | Every `Creds:` terminal closed: 0 logins in the next 20 minutes. *Stop the round*: 0 logins in 20 minutes, also after a reload; started again, a round follows within 60 s |
| R8 | The session door | *Take the question* works with the permission and asks no per-host question. *Not this host* on a heading: 0 logins to that host in 20 minutes, also after a reload, and the chat door there refuses. *Read this host again*: read in the next round |

### E3 — WSL on this PC, and revision 2 of the script

- **Goal.** A `Creds: wsl / …` terminal shows every running distro of this PC and every Linux user in it, root
  among them, each read as that user — root as root; and the script that every place runs is the one the usage
  read can be turned on with.
- **Scope.** A NEW module for the WSL road beside `src/remoteSsh.ts` (the absolute `wsl.exe`, the two list calls
  and their decoding, the users call, the accounts call as a user and as root for root's own place, the caps); the users script as a second pinned
  constant; `src/remoteAccountsScript.ts`, `src/remoteAccountsReply.ts` and the S2 spec (revision 2);
  `src/remoteAccountsRound.ts` (places; the usage reply joined to the first); the table's model and the page (the
  `wsl` block); the tests; the documents and `SECURITY.md`.
- **Depends on.** E2 merged. **W9 has a recorded result before the spec of E3.S1 is final**: it has one, and
  the owner chose on its one open item: the login session is accepted (decision 25).
- **Stories.** **E3.S1 — the WSL road (SECURITY SPEC by Fable, outside the quota):** the exact argv, the
  enumeration and its decoding, the users script and its hash, who runs as whom, **root's own place — its
  `HOME`, and the check of owner and mode before a start as UID 0, by which nothing that another user can
  write is started as root —**, the caps, what W9 showed, **and the login session of decision 25: its
  mechanics, and the words for it**.
  **E3.S2 — the scripts, revision 2 (CORNERSTONE, `model: fable`):** the `PATH`, the usage mode that reads Claude
  only, the time table with its KILL seconds, the new hash, the parser by mode, **and the round's join of the
  usage reply with the first reply, with its check that the account is unchanged** (`remoteAccountsRound.ts`,
  `publishedState`; § 2) — the script and the join are one pull request, never two; it may merge first, as its own
  pull request, on merge conditions 1 to 4 and 6 — it has no row of its own, and R9a runs with the rest of E3.
  **E3.S3 — places:** the identity, the memory, the sharing of an account across places, the `wsl`
  block of the table. **E3.S4 — the documents.**
- **Done when.** The merge conditions hold, with these rows in the owner's window on this PC:

| # | What is run | Pass |
|---|---|---|
| R13 | **Two WSL users, and root.** A distro with two Linux users, each signed in to a tool, and a distro whose root is signed in to a tool; a `Creds: wsl / …` terminal | Both users are under `wsl`, each with its own tools and accounts, and each account is the one that user is signed in to. **Root is listed under its distro, read as root, with the account root is signed in to**; in a distro where root has no tool there is no line for root. A second running distro is listed with its users; one that is not running says so and stays stopped. No ssh login to a host `wsl` is tried |
| R16 | **A known sample** (the consultants' criterion) | One figure whose value the owner knows from the tool itself — a Codex window, or a Claude window once E4 is in — shows that value; a window with no reading says why; the age of the read stays true while the tab is open |
| R17 | *Not this host* on `wsl` | No `wsl.exe` call of this window in 20 minutes, watched on the PC by the method the stories document fixes; also after a reload |
| R9a | The time of a read, 10 calls for each place, ssh and WSL | None is killed at 30 s; the slowest is under 20 s; the medians are recorded |

### E4 — The Claude usage, on

- **Goal.** The Claude windows show numbers.
- **Scope.** ONE constant, `USAGE_POLICY`, set to the row W6 selected, with the tests that hold it; the sentences
  of `SECURITY.md` on W6 and W7. A result of W6 or W7 that needs another flag or another script line is a change
  of this plan.
- **Depends on.** E3.S2 on main — the script of revision 2 AND the round's join, which that story owns; W6 and W7
  recorded, W7 passed. It does not wait for E3's other stories.
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
- **Done when.** Each row has a recorded result; **R7 and R12 are gates**, and both run again on E6's build.

| # | What is run | Pass |
|---|---|---|
| R7 | Two hours, at least 12 rounds, on a VM AND in a distro, each WITH live Claude Code, Codex or agy sessions | **Gate. 0** live sessions that ask to sign in again, lose their settings or MCP servers, or stop or restart. The time and size of the agy program before and after: recorded |
| R12 | **Two windows.** (a) *Not this host* in window A while B's round runs. (b) *Not this host* in A; A is closed at once; B goes on for three rounds and is made to write `globalState`; both are reloaded. (c) The same with *Stop the round*. (d) The same with *Withdraw the permission*, and then a chat door is pressed in B | **Gate.** (a) At most 1 login to that host after the press, 0 from then on. (b) 0 logins to that host from the press on. (c) and (d) 0 logins to any host from the press on. In each the folder is read back and holds the change. A lost "no", Stop or withdrawal fails the gate: the build is not released, and the cure is a change of this plan |
| R10 | Which places lack `jq` | A table: `jq` there or not, the manager word, root or `sudo` without a password. A place without `jq` shows the fixed command; after the owner runs it, the next round reads the accounts. The count is the input of E6 |
| W8 | A start of `agy` (§ 5) | A record |

### E6 — The `jq` installer

- **Goal.** Owner decision 17: where `jq` is missing it is installed, when the user may; where the user may not,
  the tab shows the command.
- **The consent (Q3, answered 2026-11-25).** One more "yes", asked ONCE for all hosts — *"May CoAI install `jq`
  where it is missing?"* — by a person's press, kept in the locked file of the side beside the permission to
  read, and withdrawn by a press. Nothing is installed without it, and it is never given by the permission to
  read. **The setting `coai.terminalAccountsInstallMissingTools` of decision 20 is not built**: the withdrawal is
  the off switch, and a second switch in a settings file would be one a person did not press.
- **Scope.** As designed in the appendix, § E, with the gate's findings 1, 2 and 6 — revised when the epic starts
  for this consent in place of the setting, and for a WSL place.
- **Depends on.** E5's R10. **Cornerstone** when it starts: a change of a machine as root, spec by
  Fable, finding F2 beside it (the appendix, § B).
- **Done when.** Its own stories, tests and real-host checks, written when it starts — **and, on its build, the
  gates again** (the plan round's finding 3): R12 (a) to (d); R7, two hours with a live session on a place where
  an install ran; and **R18**, a gate: the install "yes" is withdrawn in window A, A is closed at once, a stale B
  writes `globalState` and a stale lock holder writes its file, both are reloaded → 0 installs from the press on;
  and with the permission to read withdrawn, 0 installs and 0 reads.

### Candidates

C2 to C9, one line each with what would justify it: the appendix, § K.11, word for word. Taking one up is a
change of this plan (kind `epics`), recorded in the ledger. C2 is taken (it is E2) and so is C9 (decision 24,
with the approval of epoch 3); C4a is gone. Open: C3, C4b, C5, C6, C7 and C8.
