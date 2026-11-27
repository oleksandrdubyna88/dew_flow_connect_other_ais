# PLAN — the terminal panel shows the AI accounts of the open ssh hosts, and their usage

> Status: **In progress, 2026-11-27 — epoch 7 is approved by the owner; E1 to E4 are released as extension 2.1.0; of E5, the gate R12 is not run; E6, the `jq` installer, is parked (6.1); the tab is to read agy's quota and account (6.2) and Codex's account, plan and limits from Codex itself (6.3) — plan only here, the code is a pull request of its own.**
> How the plan came here is in the ledger below; the status as it said so before: the appendix, § K.24,
> § K.34, § K.42 and § K.45. **Epoch 6 took three changes on 2026-11-27**, all the owner's — 6.1 (E6 is
> parked), 6.2 (agy: `agy -p /quota`, and one line of agy's own log for the account) and 6.3 (Codex:
> `codex app-server` in place of the session files) — **and the owner approved epoch 7 the same day with NO
> re-review** (*Re-review for epoch 7*; how 6.3 is counted is marked there for the owner). **Epoch 7 has one
> change**, 7.1 — as root a vendor program in a folder that another user can write is run with a warning,
> not refused; **one more stops the plan**. Extension 2.1.0 (2026-11-27; #1055, #1057) holds E2 to E4;
> 2.1.1 is a wording fix. E5: R7 passed in WSL and on the VM, R10 is recorded, R12 is open. No code of E6
> exists. The script of 6.2 and 6.3 is revision 3 (§ 4), on the branch `fix/accounts-agy-tab`. Scope: `src_vs_code` only — the icon in the terminal panel's title bar; ONE permission to read, asked once; a tab with a table — by host, or by Claude account or by user (4.1) — of the Claude Code, Codex and
> Antigravity accounts and their limits; a round every 10 minutes over the hosts of the open
> `Creds: <host> / <session>` terminals, over ssh, and over the Linux users of the WSL distros of this PC, root
> among them. **2.1.0 only reads**: where `jq` is missing the tab shows the install command with a copy button; the installer, E6, is parked (decision 17; entry 6.1). No change to
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

> Epoch 4 · approved 2026-11-26 · by owner · state: closed

- 4.1 · 2026-11-26 · owner · scope, contract — the tab's table is grouped by server, by Claude account or by user, by a switch, and every column header sorts
  (The owner, after seeing the picture of the tab — § 1; the appendix, § K.8 — in Russian, translated: "let the
  webview open, with proper tables, and so that it can be sorted — by Claude accounts, by servers, by users."
  What it changes: § 1 gains the switch *Group by*, the sorting headers and the tab's own state for both; so
  do E2.S2, E2.S3 and E3.S3, the test plan, the Definition of Done, R15 and R13. Nothing is removed: the tab,
  the default view by server and every rule of the page stay. The first change of epoch 4: one more stops
  the plan.)
- 4.2 · 2026-11-26 · owner · merge-conditions, build-order, scope, epics — the release in 24 hours: one real-window check on the combined build, E6 to the next release, E5 once
  (The owner wants the whole feature released within 24 hours — in Russian, translated: "we are not building
  a spaceship" — and answered the planner's questions on 2026-11-26; the answers, in the planner's words.
  ONE real-window check of a VSIX, at the end, on the combined build of E2, E3 and E4, in place of one for
  each epic: Q4 as answered that morning is changed. E2, E3 and E4 land as one pull request or as several,
  merged after that one check; the usage of E4 may be in E3's pull request. E6, the `jq` installer, moves to
  the next release, under a later plan or epic of its own: this release shows the install command with a
  copy button only. E5's gates run once, on the release build of E2 to E4; R18 goes with E6. The second
  change of epoch 4.)
- 4.3 · 2026-11-26 · owner · dod — residual 9 is accepted: an *Allow* window and a withdrawing window that both end at once can leave the grant standing, and nobody is told
  (A review of pull request #1052 asked how residual 9 stands beside the Definition of Done, which said that
  a withdrawal always wins. The owner's answer of the same day: residual 9 is accepted as it is, with no cure
  in this release. The Definition of Done now says "always, but for residual 9". The third change of epoch 4.)
  (The check refuses two entries in an epoch that is not stopped, so the commit of these two entries closes
  epoch 4. The owner chose both changes and approved epoch 5 at once, as with epoch 4. The record below says
  what was not run.)

### Re-review for epoch 5

Narrowed: yes — E6, the `jq` installer, with its gate R18, left this release; ONE real-window check on the combined build stands in place of one for each of E2, E3 and E4; E5's gates run once

| Work | Where | Verdict | Action |
|---|---|---|---|
| The stories document of E2 and the security spec of E2.S1 | pushed, unmerged (`feat/terminal-accounts-e2`) | fits | kept; the real-window rows of E2 are run in the one check of 4.2 |
| The code of E2.S1, in progress | `feat/terminal-accounts-e2` | fits | kept; it goes on |
| The results of W6, W7 and W8 on a VM, and of W9 | pushed, unmerged (this branch) | fits | kept; the record of W7 now says why it passes, and that of W8 where it looked |
| Product code of E3 to E6 | none was written | — | nothing was built, so nothing is cut |

  (THIS RECORD IS NOT A RE-REVIEW, as the one for epoch 4 was not: none was run, by the owner's word. Of *The
  full stop*, step 1 is the commit of this record, which holds the stop and the approval together: no commit
  stood with the state `stopped`. Steps 2 to 7 were NOT run — no halt, no consultation of the stop, no
  revision of the whole plan, no Opus judge, no cut, no re-split. Of step 8 there is the owner's approval
  only. No judge and no coai round read this text; the verdicts of the table are the planner's.)
  (Epoch 5 is narrowed, so the owner approves it; the owner chose these changes himself, in his answers.
  The texts reworded for them, as approved for epoch 4: the appendix, § K.34 to § K.38, word for word. The
  cornerstone quota is counted again for five epics.)
  (Also in this commit, from the review of pull request #1052, baseline and no entry: *Growth surfaces*
  counts *Refresh now* in the bound of the usage calls, and the record of W7 says why it passes.)

> Epoch 5 · approved 2026-11-26 · by owner · state: closed

  (The owner answered the six questions of E6's document, its § 11, on 2026-11-27, in one answer: every
  recommendation is taken. Entries 5.1 to 5.4 are four of those answers. Entries 5.5 to 5.9 are the places
  where E6's spec, after gate E6-G1 and round 1 of its plan round, goes past what § K.38 says is revised
  when the epic starts: the consent in place of the setting, and a WSL place.)

- 5.1 · 2026-11-27 · owner · epics, scope — E6, the `jq` installer, is the sixth epic of this plan again, not a plan of its own; its spec is the third cornerstone
  (The owner's answer O1. Entry 4.2 had moved E6 "under a later plan or epic of its own". The cornerstone
  quota is counted again for six epics, Q = ⌈2·6/5⌉ = 3, and E6.S1 takes the third mark, for area (f), as
  § K.38 planned. With the epic come its items of the Definition of Done, its merge conditions and its steps
  of *Build order*. The plan stays in `todo/` until E6 ships. This entry was first written on `5c15d213` in
  one entry with what is now 5.2. Gate E6-G1 found two independent causes in it, so this commit counts them
  apart: the count goes up. The first wording: the appendix, § K.41.)
- 5.2 · 2026-11-27 · owner · merge-conditions, dod — extension 2.1.0 was released before R12 and the VM half of R7 had a result; the run of R12 on E6's build stands for 2.1.0 too
  (The owner's answer O5. The condition for the release was "the gates R7 and R12 passed, once, on the head
  that is tagged". 2.1.0 was tagged on 2026-11-27 at 03:42 UTC, with R7 passed in WSL only. The VM half of R7
  passed on 2.1.0 itself at 11:00 UTC, after the release. R12 is not run: the owner's word of that night was
  "R12 not by hand". It is a gate of E6's build, and its result there is the one for 2.1.0. A release gate
  got smaller, so the record below says `Narrowed: yes`. This second change stopped the plan.)
- 5.3 · 2026-11-27 · owner · build-order — E6.S1 and E6.S2 may be built before it is known that a place of the owner would get an install; a gate stands before the code of E6.S3
  (The owner's answer O2, with finding 6 of gate E6-G1. § K.38 said "Depends on. E5's R10". Now S1 and S2,
  which touch no machine, may start. Before any code of S3 the rows of R10 and the fact of R10 (f) are on
  record; with no place of the owner that would get an install, the work stops after S2 and the owner is
  asked. As measured on 2026-11-27 there is no such place: E6's document, § 1.)
- 5.4 · 2026-11-27 · owner · contract — *Withdraw the permission* of reading withdraws the install "yes" too
  (The owner's answer O4. § 3 and § K.38 had two withdrawals with no tie between them. Now that one press
  makes the install withdrawal first and then the one of reading, and an *Allow reading* given later brings
  no install back: E6's document, § M2.)
- 5.5 · 2026-11-27 · agent · contract — the record of installs is a file of the side under the claim of `refused.json`, not a `globalState` key
  (E6's spec, its J6, against § E: "`globalState` key `coai.terminalAccountsInstalls`". The cause is that of
  entry 1.2: a stale window that writes `globalState` could put an old record back, and the "once" guard
  with it.)
- 5.6 · 2026-11-27 · agent · contract — the install reply has no word `locked`
  (E6's spec, its J3, against § E: "`failed <word>` — `locked`, `timeout` or `exit`". The manager's exit
  code does not say that a lock was held, and its text never leaves the target. A held package lock is
  `failed exit` or `failed timeout`.)
- 5.7 · 2026-11-27 · agent · contract — the install script takes one of two operand words, not one fixed operand
  (E6's spec, its J7, against § E: "one fixed operand, `install-jq`". The second word is a press on an old
  lock; the script checks the 200 s itself. The judge of gate E6-G1 offered ONE word as a cut. It was not
  taken: with one word a timer's call too would take an old lock away, against finding 2 of § E.)
- 5.8 · 2026-11-27 · reviewer · contract — what an install can change besides `jq` is said as it is: a package that `jq` needs can be updated, and nothing is removed
  (Findings 3 and 14 of gate E6-G1 and finding c3 of E6's plan round: one cause. § E's "no upgrade of
  anything else" and the pass of R10 (a), "nothing else installed, upgraded or removed", were not true of a
  package manager. Now `apt-get` gets `--no-remove`, `dnf` gets no weak dependencies, the question and
  `SECURITY.md` say the sentence above, and R10 (a) passes on "`jq` and the packages it needs, and no
  removal".)
- 5.9 · 2026-11-27 · reviewer · contract — the record holds one row for each attempt, with an id, not one row for a host that is rewritten
  (Finding c4 of E6's plan round, codex, round 1: a late `end` could land on a newer attempt of the same
  place. Now `end` rewrites only the row of its own id, and the cap of 1000 rows counts attempts. Finding c5
  of that round — a damaged record is kept under a stamped name and never replaced — only satisfies finding
  6 of § E as written, and is no entry. Neither are the other findings of the gate and of the round: they
  change E6's document where no approved text said otherwise; its § 12 lists them.)
  (The check refuses two entries in an epoch that is not stopped, so the commit of these entries closes
  epoch 5. The owner was told that the entry of `5c15d213` held several changes, and chose the answer "Epoch
  6 at once": epoch 6 is approved in the same commit with no re-review, as epochs 4 and 5 were. The record
  below says what was not run.)

### Re-review for epoch 6

Narrowed: yes — the release gate "R7 and R12 passed, once, on the head that is tagged" did not hold for extension 2.1.0: it was released before R12 and the VM half of R7 had a result, and the run of R12 on E6's build stands for it (entry 5.2)

| Work | Where | Verdict | Action |
|---|---|---|---|
| Extension 2.1.0: E2, E3 and E4 (#1055 `da9760fe`; the put-back of the scenarios, #1058 `aaf2d139`; the release, #1057 `9060cb87`) | main, released 2026-11-27 | fits | kept; R12 is owed on E6's build (entry 5.2) |
| Extension 2.1.1: the words of a usage cell while its place is read (#1061 `cdcfa0b9`; #1063 `5801b846`) | main, tagged | fits | kept |
| The post-deploy record of 2.1.0, and the results of E5 (#1059 `999e9bea`; #1060 `ec08996e`) | main | fits | kept |
| The results of E5: the VM rows of R7 and R10 (`2ad6e54e`) | pushed, unmerged (`docs/terminal-accounts-e6`) | fits | kept; they are the input of the gate before E6.S3 (entry 5.3) |
| The stories and the spec of E6 (`6306d0f2`; the owner's answers, `5c15d213`; this commit) | pushed, unmerged (`docs/terminal-accounts-e6`) | fits | kept; this commit revises them for gate E6-G1 and for round 1 of E6's plan round |
| A first draft of the install script | local, not committed | — | it is not product code; E6.S1 writes the text from the spec |
| Product code of E6 | none was written | — | nothing was built, so nothing is cut |

  (THIS RECORD IS NOT A RE-REVIEW, as the ones for epochs 4 and 5 were not: none was run, by the owner's
  word. Of *The full stop*, step 1 is the commit of this record, which holds the stop and the approval
  together: no commit stood with the state `stopped`. Steps 2 to 7 were NOT run — no halt, no consultation
  of the stop, no revision of the whole plan, no Opus judge of the done work, no cut, no re-split. Of step 8
  there is the owner's approval only. The verdicts of the table are the planner's.)
  (What did read a part: gate E6-G1, Opus, 2026-11-27, read E6's document against this plan, and found the
  bundled entry. Round 1 of E6's plan round, codex, read E6's document. Neither read the whole plan, and
  neither read this text or E6's document as they stand after this commit.)
  (Epoch 6 is narrowed, so the owner approves it. The owner chose the changes 5.1 to 5.4 himself, in his
  answers. Entries 5.5 to 5.9 were counted by the planner in this commit: the owner's "Epoch 6 at once" was
  given on the judge's word that the changes are several, not on this list. The texts reworded for the
  entries, as approved for epoch 5: the appendix, § K.42, word for word. Moved for room, word for word: the
  new tests of the test plan (§ K.43) and the sections of E2, E3 and E4 in *Epics* (§ K.44), which are of
  the epics that 2.1.0 holds.)
  (Baseline of epoch 6, and so no entry: the owner's two other answers of that day. O3 — in WSL, CoAI does
  not install as root of a distro for a user who has no rights: decision 24 stands. O6 — the checks on real
  machines run on a VM and a distro that can be lost; each still needs the owner's go for its exact
  commands.)
  (Not known to this record: whether `review_feature`, step 9 of *Build order*, ran before extension 2.1.0
  was tagged. The Definition of Done keeps that box open.)

> Epoch 6 · approved 2026-11-27 · by owner · state: closed

- 6.1 · 2026-11-27 · owner · epics, scope — E6 is parked: as measured, it would install `jq` on no place of the owner
  (R10 and R10 (f) of the E5 results. The owner, asked, chose to park it. Its document stays as written; no code.
  It comes back when a place lacks `jq` and its login user is root or has `sudo` with no password.)
- 6.2 · 2026-11-27 · owner · scope, contract, dod — Antigravity: the tab shows agy's quota and its account; the script, revision 3, starts `agy -p /quota` and reads one line of agy's own log
  (The owner showed agy's own screen "Models & Quota" and asked for the account and for "what is left, when
  it renews". Measured that day, agy 1.5.18: `agy -p /quota --output-format json` prints the quota groups —
  "Gemini Models" and "Claude and GPT models" — each with a 5-hour and a weekly bucket (`remaining_fraction`,
  `reset_time`), in about 3 s, with `num_turns` 0 and no conversation. An UNKNOWN slash command is sent to
  the model. So the script takes a reply only when it shows no turn and the command `usage`; else that host
  is not asked again at that agy version, as with Claude's usage command.)
  (The account is the last line `applyAuthResult: email=` of agy's own log, `~/.gemini/antigravity-cli/cli.log`.
  The owner chose this, knowing that the form of a log is not a contract; with no such line the tab says
  "account not read". agy's token file stays unopened.)
  (It reverses § 7 of the VM results — "agy states no usage", of `cli.log` "The plan does not read it", agy
  "with no usage source and with its account not read" — and texts of this plan, kept as they stood in
  the appendix, § K.45. In *Security* one edge changes and one is new: `agy -p /quota` calls Google with
  agy's own sign-in, and one line of agy's log is read. The code is a pull request of its own,
  `fix/accounts-agy-tab`, which also updates the script block of the E3.S2 spec. This second change of
  epoch 6 stopped the plan.)
- 6.3 · 2026-11-27 · owner · scope, contract, dod — Codex: the tab shows Codex's account, plan and fresh limits, read from `codex app-server` and no longer from the last session files
  (The owner, in Russian, translated: "Codex shows everything too … the account, the subscription and what
  is left." Measured that day, codex-cli 0.171.0: `codex app-server` answers the JSON-RPC calls
  `account/read` and `account/rateLimits/read` with the email, the plan type and the primary and secondary
  windows (`usedPercent`, `windowDurationMins`, `resetsAt`), with no model call, in about 1.5 s. Its stdin
  must stay open for about 2 s, or it exits without an answer.)
  (It replaces the read of the last Codex session files — "from a Codex session … ago" — and "account name
  not shown by Codex", both of § 6 of the VM results, and this plan's "No live Codex read": § K.45. One edge
  of *Security* is new: `codex app-server` calls OpenAI with Codex's own sign-in. The script still never
  reads `~/.codex/auth.json`. The code lands with that of entry 6.2, in the same revision 3 of the script.)
  (The check refuses two entries in an epoch that is not stopped, so the commit of these two entries closes
  epoch 6. The owner was asked a question that said: this is the second change of epoch 6, and it needs
  epoch 7. The owner chose the answer "Yes, epoch 7 at once". So epoch 7 is approved in this commit with no
  re-review, as epochs 4, 5 and 6 were. The record below says what was not run.)
  (FOR THE OWNER TO CONFIRM OR TO REFUSE. That question named the Antigravity quota only. The Codex change
  came in a later message of the owner, in the same hour. The lead counts it in the same stop and under the
  same answer: this is the lead's reading, and the owner did not say it. If the owner reads it otherwise,
  the Codex change is the first change of epoch 7, its entry 7.1, and one more change stops the plan.)

### Re-review for epoch 7

Narrowed: yes — E6, the `jq` installer, is parked (entry 6.1): no install is built until a place of the owner needs one; entries 6.2 and 6.3 add to the scope, and take back two promises of *What this plan does NOT do*

| Work | Where | Verdict | Action |
|---|---|---|---|
| Extensions 2.1.0 and 2.1.1, as the record for epoch 6 sorted them; the post-deploy record of 2.1.1 (#1064 `72e2784d`) | main | fits | kept; what they show of agy (no usage, no account) and of Codex (the session files, no account name) is replaced by entries 6.2 and 6.3, in a later release |
| The stories and the spec of E6; R10 (f) (`60be211d`); entry 6.1 (`dab8a8bd`) | pushed, unmerged (`docs/terminal-accounts-e6`, #1062) | fits | kept; E6's document stays as written while it is parked |
| The width of the *Read* column of the Antigravity table in the tab (`a70f5fbc`) | pushed, unmerged (`fix/accounts-agy-tab`) | fits | kept |
| Code of entries 6.2 and 6.3: revision 3 of the script and its parser | local, not committed (the worktree of `fix/accounts-agy-tab`) | — | no commit holds it; it is the work that these entries plan |
| Product code of E6 | none was written | — | nothing was built, so nothing is cut |

  (THIS RECORD IS NOT A RE-REVIEW, as the ones for epochs 4, 5 and 6 were not: none was run, by the owner's
  word. Of *The full stop*, step 1 is the commit of this record, which holds the stop and the approval
  together: no commit stood with the state `stopped`. Steps 2 to 7 were NOT run — no halt, no consultation
  of the stop, no revision of the whole plan, no Opus judge of the done work, no cut, no re-split. Of step 8
  there is the owner's approval only. The verdicts of the table are the planner's. No judge and no coai
  round read this text, the three entries of epoch 6 or the plan as it stands after this commit.)
  (Epoch 7 is narrowed, so the owner approves it. The owner chose all three changes of epoch 6. How the
  third is counted is the lead's reading, marked above for the owner.)
  (The texts reworded for entries 6.2 and 6.3, as approved for epoch 6: the appendix, § K.45, word for word.
  Moved for room, word for word, and holding as written: what is E6's while it is parked — its steps of
  *Build order*, its merge conditions and its section of *Epics* (§ K.46); the rows of E5 (§ K.47); the
  grouping and the sort of § 1 (§ K.48); the upgrade table of § 3 (§ K.49); § 5 on the four measurements
  (§ K.50); the three steps of *Which places* in § 2 (§ K.51); the rules of the withdrawal's stamp in § 3
  (§ K.52); three rules of the page in § 1 (§ K.53); the short list of the owner's answers (§ K.54); and
  the four changes of revision 2 with the users script, of § 4 (§ K.55). The plan keeps a line or two of
  each.)
  (Open, and decided by no entry. Whether a "yes" given to the permission's text of 2.1.0, which names
  `claude` and `agy` as started, is asked again now that `codex` is started too and two more vendors are
  called. Which merge conditions the pull request of entries 6.2 and 6.3 answers to: the plan names those
  of E2 to E4 and those of E6. And no security judge was asked on the new edges: their text in *Security*
  is the planner's. The measurements of 2026-11-27 stand in the entries as the lead gave them; no RESULTS
  file holds them yet.)

> Epoch 7 · approved 2026-11-27 · by owner · state: open

- 7.1 · 2026-11-27 · owner · contract, dod — as UID 0 a vendor program (`claude`, `codex`, `agy`) whose path fails the check of owner and mode is run all the same, and its row carries a warning that names the folder; the basic tools keep the refusal
  (At the real window of VSIX 2.1.901 the Codex rows of the two root places of the owner's VMs said: "not
  read: /root/.codex/packages/standalone/releases/…/bin is writable by another user". The owner, in Russian,
  translated: "it is done on purpose, so that I log in to Codex and Antigravity once, not three times. Let
  it read.")
  (Decided: the refusal stays for the basic tools — `timeout`, `tail`, `sed`, `grep`, `sleep`, `sha256sum`,
  `jq` — and goes for the three vendor programs, whose row then warns and names the folder. It narrows the
  rule that came with decision 24, "nothing that another user can write is started as root", to the basic
  tools. What the owner accepts with it: a user who can write that folder can replace a program that root
  then runs — residual 15. That rule was a condition of the judge of § K.9, and no judge was asked on this
  change. The rules have no kind for security: the kinds are `contract` and `dod`.)
  (The first change of epoch 7, on the lead's reading of entry 6.3: one more stops the plan. The paragraph
  marked for the owner, above, gives the number 7.1 to the Codex change if the owner reads that one as a
  change of epoch 7. Then this entry is the SECOND change of epoch 7, and it stops the plan.)
  (Its code: #1066. As root the worst path is 29 s with the connect, 1 s under the ceiling, not 2: each
  vendor's `#!` line is read, up to 1 s. A test holds 29.)
  (Moved for room in this commit, word for word, § K.56 to § K.59: the merge conditions of what is merged
  and released, steps 1 to 10 of *Build order*, four bullets of § 2, and T9 and T8 of § 3.)

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
    **Narrowed on 2026-11-27 (entry 7.1), "Let it read":** root's vendor programs are started even where
    another user can write their folder, with a warning in the row.
25. **2026-11-26, with the approval of epoch 4: the login session of a WSL read is accepted.** On a distro with
    systemd, WSL opens a login session for a user that is read, root included: it runs that user's profile —
    root's as root — makes two empty files at a first login and stays until the distro stops (in full: the
    appendix, § K.40). The owner was shown it (W9) and chose to accept it, not to read fewer users.
    So a read is named for what it is: the permission's text (§ 3) and *Security* say it.
26. **2026-11-27: the answers to the six questions of E6's document, every recommendation taken.** E6 is the
    sixth epic of this plan (entry 5.1). Its S1 and S2 may start, and a gate stands before its S3 (5.3). In
    WSL nothing is installed as root of a distro for a user who has no rights: 24 stands. *Withdraw the
    permission* withdraws the install "yes" too (5.4). R12 on E6's build stands for 2.1.0 (5.2). The real
    checks run on a VM and a distro that can be lost. Then, told that these are several changes: "Epoch 6
    at once".
27. **2026-11-27: agy and Codex are read as they show themselves** (entries 6.2 and 6.3). agy: the quota of
    its own screen "Models & Quota", and the account from its log — chosen knowing that a log's form is no
    contract. Codex: "the account, the subscription and what is left", from Codex itself. Then, told that
    this is the second change of epoch 6: "Yes, epoch 7 at once" (how 6.3 is counted: the ledger).
28. **2026-11-27: the "yes" given to 2.1.0 stands for revision 3; not asked again.**

**The owner's answers** to Q1 to Q4, of 2026-11-25 evening and of 2026-11-26: the appendix, § K.25, word
for word; this plan's short list of them, with what entries 4.2 and 4.3 changed: § K.54.

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
  Ctrl+F. `retainContextWhenHidden` keeps the scroll place; the page keeps no other state but the grouping and
  the sort (entry 4.1, below). The QuickPick of E1, its rows and its item buttons are removed.
- **The page is grouped by HOST** — the grouping *Server*, the default of the switch below. A toolbar, then one
  block for each scope host of the live `Creds:` terminals, in
  name order. The block of `wsl` is headed *wsl — this PC* with the count of its distros; under it one line or
  table for each distro and user (*Ubuntu · user-a*), and a distro that is not running has the line *not
  running, so not read*. **The picture of the page: the appendix, § K.8** — it shows *Server*; the stories
  document of E2 draws the other two groupings.
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
    whole account text are equal; an account that is not known is never shared. **Codex (entry 6.3):** the
    account and, muted, the plan, as `codex app-server` states them in that round; no session file is read,
    so the row carries no file's age. **agy (entry 6.2):** the account of the last sign-in line of agy's own
    log (§ 4), or *account not read*.
  - *A window cell:* the first line is `<n>% left`; under it `resets in <time>`. A missing value is `—`, never
    `0%` (decision 5). Claude's weekly windows for single models are sub-lines of the weekly cell. A Codex window
    is placed by its length — 300 minutes in the 5-hour column, 10 080 in the weekly one; another length is named
    in its cell. **agy (entry 6.2):** the 5-hour and the weekly bucket of each quota group that agy prints —
    measured: "Gemini Models", "Claude and GPT models" — each as `% left` with its reset; how the groups are
    laid out is the code's.
  - *Usage that was not read* spans both window cells and says why: *Usage not read: CoAI does not ask Claude Code
    for usage in this build*; *Usage unavailable: Claude Code 2.1.326 printed no usage line this version
    understands*; for agy and for Codex a sentence of the same kind, worded with the code — *agy states no
    usage* is gone (entry 6.2). It is never a row of dashes with no reason.
- **Group by: Server | Claude account | User, and every column header sorts** (the owner, entry 4.1; in
  2.1.0). What each grouping shows, the sort keys, the dash that sorts LAST and never as a zero, the state
  that is of this tab only, and the ONE pure module: the appendix, § K.48, word for word. It holds as
  written, but for one reason given there. Of the sections *Codex* and *Antigravity* of the grouping *Claude
  account* it says "their accounts cannot be told apart, so none is joined". Since entries 6.2 and 6.3 a
  row there shows its account. They stay sections of their own: only Claude accounts are joined.
- **The toolbar**: *Group by*; *Refresh now*; *Stop the round* (it becomes *Start the round* while stopped); and the summary
  *last round <age> (<clock time>) · <k> of <n> read*, or *reading host-c… 5 of 16* while a round runs.
- **Without the permission the page holds one block**: what would be read, and the one button *Allow reading…*
  (§ 3). With it, a last line says when it was given and has *Withdraw the permission*.
- **Each place is drawn when its call lands; times stay true while the tab is open** — the page writes the
  ages and the *resets in* texts again every 15 s from its own clock; **narrow widths** stack the columns.
  These three rules in full: the appendix, § K.53, word for word.
- **Nothing from a VM can become markup.** The page is built by its script from the model: every value that came
  from a VM, from WSL or from a terminal's name is set as `textContent`. No HTML is assembled from data, and a
  `$(name)` is plain text. The CSP is strict: `default-src 'none'`, a script and a style by nonce only, no remote
  source, `localResourceRoots: []`. The one road that makes VM text plain (`plainValue`: control and format
  characters out, the length cut) stays in front of the model.
- **A message from the page names, it does not carry.** `ready`, `refresh`, `stop`, `start`, `allow`, `withdraw`,
  `save-now`, `refuse <scope host>`, `read-again <scope host>`, `copy-install <place>`. The host checks every name
  against its own model and drops a message it does not know. The switch and the headers post none. **The two messages that widen what is read,
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
  the distros and which of them run; the Linux users of each running distro, by the users script (§ 4) as
  root — root itself among them (decision 24); and for each such user the accounts script **as that
  user**, root as itself with root's own `HOME`. **A distro that is not running is listed and not started**
  (Q2): the running list is read again right before every call to a distro. The three steps with their
  argv, classes and reasons: the appendix, § K.51, word for word; who runs as whom: *Security*.
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
- **The usage call names the account it is for, and the SCRIPT checks it** (the plan round's finding 2):
  § 4, item 3.
- **The round JOINS two replies** — the usage call reads Claude only (§ 4), and today a place's whole reading is
  replaced by its last reply (`publishedState`). The join is E3.S2's. The Codex and agy parts of the first reply
  stay. *The same account:* its usage is shown and counted for it. *Account changed:* the Claude part is the
  account the second reply names, with no usage of its own — the figures of this round's read of that account,
  if there is one — and the account the call was for is read by its next place; no third call is made. *A second
  call that fails or cannot be read:* the first reading stays whole and says why the usage is not read.
- **The usage policy** is the one constant `USAGE_POLICY`, `off` until E4. Its other values are the rows of the
  W6 table (the appendix, § K.17).
- ***Refresh now*** starts a round at once, but not within 60 s of the last one. **The timer never asks a
  person.** These two, the size of a round, the toolbar's summary and the one notice: § K.58.

### 3. The consent — ONE permission, a "no" for one host, and the Stop

**What E1 built and this plan keeps** — the refusals file of each side, its lock, its markers and the copy-in:
the appendix, § I, *3. The consent*, the S1 spec, and the short list in § K.12.

**What changes: nothing is read before one explicit "yes", and that "yes" is one for all hosts** (decision 22;
`cons` `78e6d44b`).

- **The permission** is one record of the side, `grant: { utc, stamp }`, in the SAME locked file. `globalState`
  cannot keep it for the reason of entry 1.2: a stale window that sends its whole copy could put back a permission
  the person withdrew. A build that does not know the key keeps it through a write, as the file's rule already says.
- **A withdrawal wins like a "no" — and it is a file of its own**, `withdrawn-<stamp>.json` beside
  `refused.json`, made by an exclusive create. **The permission is in force only when the grant's stamp is
  HIGHER than the stamp of every withdrawal file**, so a stale rename of `refused.json` brings back a grant
  that is not in force. The rules of the stamp in full — what an *Allow* and a withdrawal write and remove,
  and the copy in `globalState`: the appendix, § K.52, word for word, and the spec of E2.S1.
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
    own cache files on a usage read** (decision 13); **from revision 3 the text also says that `codex` is
    started, and that `agy` and `codex` each call their vendor with their own sign-in** (entries 6.2 and 6.3)
    — a "yes" given before stands and is not asked again (decision 28);
  - the Claude session of a terminal, when a chat door of that terminal is pressed (Q1);
  - that CoAI itself installs and writes nothing there; and how to stop: *Not this host*, *Stop the round*, *Withdraw the
    permission*.
- **Withdrawn by a person's press**, in the tab and in the hosts list. The readings are dropped and the timer is
  cleared at once, **and the chat doors read nothing from then on.** A withdrawal file that the disk did not take
  is held by its window and copied to `globalState` at once; while it waits, no window of the side makes a call
  of either kind. **A withdrawal can also answer `busy`** (the spec of E2.S1, gate E2-G1): its file is made, and
  its read-back is not final while another window holds the claim of the hosts file — its window says `waiting`
  until that write ends, or until *Save now*. An *Allow* that the file did not take changes nothing, and the
  person is told. **From E6 on, that press withdraws the install "yes" too, and first** (entry 5.4; E6's
  document, § M2).
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
- **T9 and T8, taken up**: the session road asks that function before EVERY spawn of a press, and
  "something waits unsaved" is an answer of its own, `waiting`. In full: § K.59.
- **What is retired** (each is in main since #1047 and in no release): the automatic "yes" with its cap rule,
  its setting, its two notices and its fields; the timer's use of a session "yes"; the door answers for a new
  host; the per-host question and its *Read* button. The parser still KNOWS a "yes" row and `how: 'auto'`: a
  list that holds one is not damaged, and neither is an answer. Every key, file and field: the appendix, § K.26.
- **The upgrade starts with NO permission, whatever is stored**: a person's "yes" for a host is not
  honoured and an automatic one is no answer; a "no" and the Stop stay; the retired setting is not read.
  The table, row by row: the appendix, § K.49, word for word.

- **The sides.** The permission is of one side, like the file: a WSL or remote window has its own, and asks its
  own question. A withdrawal is not of one side: its copy in `globalState` stops every side that reads it, and
  each such side writes it into its own folder. A copy never gives a permission. The owner uses a second side for the chat only (2026-11-24): the first press of a chat door there
  asks the one question once, for that side.
- **What a person can and cannot choose** follows from these rules; the list: the appendix, § K.27.

### 4. The scripts — revision 2, and the users script

E1's script, its reply and its parser stay as built and as specified (the appendix, § I, *4. The remote script and
its reply*, and the S2 spec; the short list of what stays: § K.12). **Revision 2 changes four things, in ONE new
pinned text with one new sha256** (E3.S2, the cornerstone):

1. **The script sets its own `PATH`**: `$HOME/.local/bin`, then a closed list of absolute system folders,
   none that a user who is not root can write; and as UID 0 it checks, before it starts a program, the owner
   and the mode of its file and of every folder above it (*Security*). **Since entry 7.1 a check that
   fails stops a basic tool only: a vendor program is started, and its row warns.**
2. **The mode `claude-usage` reads Claude only**, and the round joins the two replies (§ 2).
3. **That mode takes the account it is for** — a sha256 of the account lines, never the email — and runs the
   usage command only when the account it finds is the same.
4. **The time table counts the KILL second (T46)**: each mode's worst path stays at least 2 s under the
   ceiling of 30 s, held by a test.

The four in full, in 2.1.0: the appendix, § K.55, word for word, and the E3.S2 spec.

**Revision 3 (entries 6.2 and 6.3) — plan only here.** ONE new pinned text with one new sha256. The text
and its time table are the E3.S2 spec's, updated in the pull request of the code (`fix/accounts-agy-tab`).
All of it is in a place's FIRST call: the mode `claude-usage` still reads Claude only, and starts neither
`agy` nor `codex`.

1. **Codex is read from `codex app-server`**, not from session files: the calls `account/read` and
   `account/rateLimits/read`, its stdin held open for about 2 s. The script reads no session file any more,
   and it still never opens `~/.codex/auth.json`.
2. **agy's quota is read by `agy -p /quota --output-format json`**, after the version command. **A reply
   counts only when it shows no turn and the command `usage`**: an unknown slash command is sent to the
   model. A reply that fails this is dropped, and that place gets no quota command again while its agy has
   that version. How the script is told so is the spec's — by a fixed word, never by text from a place.
3. **agy's account is ONE line of agy's own log**: the last line `applyAuthResult: email=` of
   `~/.gemini/antigravity-cli/cli.log`, and of that line the address only, by a pattern the spec fixes. No
   other text of the log is printed. agy's token file is never opened.
4. **Each new start has its own limit, with its KILL second**; the sums are counted again: 2 s under the
   ceiling, held by a test — as root 1 s (entry 7.1).

**The users script** (E3.S1, the security spec): a second fixed text with its own sha256 and no operand. It
opens `/etc/passwd` and no other file, starts no program, and prints the name and the UID of each user that
§ 2 names: UID 1000 to 59 999, or 0 under the name `root` (decision 24). In full: § K.55.

**The operands** of the accounts script: one of two fixed words, and with `claude-usage` the digest, which this
side makes and which must fit `[0-9a-f]{64}` here and again in `sh`. No text from a terminal's name, from a VM or
from a distro is ever an operand as it is; a distro's and a user's name are arguments of `wsl.exe` only.

### 5. What is still to be measured

**All four are run**, on 2026-11-26: W6, W7 and W8 on a VM
([RESULTS § 13](../research/RESULTS_accounts_and_usage_on_a_vm.md)), W9 on this PC
([RESULTS_wsl_on_this_pc.md](../research/RESULTS_wsl_on_this_pc.md)). W7 passed, W6 selects "every round",
W8 is a record, and W9 is the gate that E3 stood on. **"Read-only" is promised for WSL only as far as W9
(c) showed**, and beside it stands the login session of decision 25. The section as it stood — who gave
which go, what W9 showed part by part, what was not measured: the appendix, § K.50, word for word.

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
- **No install in extension 2.1.0** (entry 4.2): nothing is written or installed on a VM or in a distro by
  a script of ours. **E6, the next release, installs ONE package, `jq`, after one more "yes"** (entry 5.1);
  what it never does: its document, § M4, rule 10.
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
- No opening of `~/.codex/auth.json`, of agy's token file, of `~/.claude.json` or of another credential file
  by our script; no `codex login status`. `codex` is started as `codex app-server` only, for the two reads of
  entry 6.3; `agy` with its version flag and as `agy -p /quota` only (entry 6.2); of agy's log, one line.
  **No road (b).** (Until epoch 7 this item said "No live Codex read" and "no start of `agy` but with its
  version flag": the appendix, § K.45.)
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

**The edges** were five, and E6 adds one (word for word: the appendix, § K.23): ssh calls by a timer, now
only after the permission; local `wsl.exe` calls by it — **with the login session that WSL opens for a user
that is read**; a start of Claude Code on a usage read; a start of `agy` every round; VM text in a webview.
**Revision 3 changes the fourth and adds two (entries 6.2 and 6.3).** The start of `agy` is now also
`agy -p /quota`, which **calls Google with agy's own sign-in**. A start of `codex app-server`, which **calls
OpenAI with Codex's own sign-in**. And a read of **one line of agy's own log**: a log can hold other text,
so of that line the address only is printed, and the tab draws it as text (§ 1). Our script still holds no
token and opens no credential file. **No judge was asked on these three**: their text is the planner's.

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

- **Root's own `claude` and `agy` are started as root, by our timer** (decision 24), on an ssh host whose
  login user is root as well; from revision 3 root's own `codex` too (entry 6.3). **The rule: as UID 0 the script starts nothing that another user can write,
  and it checks that before every start** — the file and every folder above it; if not, that tool's row says
  *not read: <path> is writable by another user* (the spec of E3.S1; a test holds it; residual 13).
  **Since entry 7.1 this rule holds for the basic tools only** — `timeout`, `tail`, `sed`, `grep`, `sleep`,
  `sha256sum`, `jq`. A vendor program — `claude`, `codex`, `agy` — whose check fails is started all the
  same, and its row carries a warning that names the folder (the owner: "Let it read"; residual 15).
- **A program that a Linux user owns is started by our timer**, as that user and never as root; in WSL such
  a process can start Windows programs as the Windows user (interop). **The inherited `PATH`** is not used
  (§ 4). **A kill** leaves no process of the call (W9e). These three, word for word: the appendix, § K.19.
- **A read of a WSL user opens a login session of that user, by WSL** (decision 25): whoever can write a
  user's profile decides what runs as that user at our first read — root's as root; the check above does not
  reach it. The first and this one, word for word: the appendix, § K.40.

**The tab.** Its rules are those of § 1: text only, a strict CSP, messages that name, a modal question in the
extension host for the two buttons that widen what is read, a fixed string for the clipboard. The grouping
and the sort (entry 4.1) change what is drawn, never what is read: they post no message, and no judge has read them. The judgement
on it: the appendix, § K.10, word for word.

**What an attacker gains**: the list of the stopped plan holds (the appendix, § I, *Security*), with the
changes of § K.22 and § K.16, word for word. In short: a terminal's name gains nothing before the permission,
and after it what F1 says; no reply reaches the extension; a second window or a reload cannot undo a refusal,
the Stop or a withdrawal (the file); **another Linux user of a distro whose root is read** gains nothing by a
tool's file or a folder above it that they can write, because the script checks both as UID 0 — what is left,
root's profile among it, is residual 13. **Since entry 7.1 that holds for the basic tools only: a user who
can write the folder of root's `claude`, `codex` or `agy` can replace a program that our timer then runs
as root, on a VM as in a distro. The owner accepts it (residual 15).** **Logs**: a failed read logs the place, how it ended and the
runner's own last line; a script's stderr goes to `/dev/null`; stdout is never logged.

**Residuals.** Those of the stopped plan hold (the appendix, § I, *Security*: a person's `ControlMaster`; a login
every 10 minutes on every host of an open terminal; an email on screen; what Claude Code does on a start; a reset
in another zone shown as a dash; and the eight residuals of the refusals file), but for the third of the file: a
person's "yes" that a stale write puts back allows nothing any more.
**The spec of E2.S1 holds the eight as they stand after its story (its § P14); gate E2-G1 added a case to 5(b)
and to 7.** New are residuals 9 to 13. All of it in full: the appendix, § K.28, word for word. In short:

9. **A withdrawal is files on one disk.** What can undo it: a disk that takes no withdrawal file, a program
   that deletes files as the person, and two windows that both end while an *Allow* is written (gate E2-G1):
   the grant can then stand, and nobody is told. **The owner accepted it (entry 4.3)**; the Definition of
   Done names it.
10. **The permission is GIVEN for one side, and a withdrawal reaches every side that sees its copy** — but for
    a stale write of the shared state that lowers or erases the copy before that side has read it.
11. **20 hosts are 20 logins every 10 minutes**, 40 with the usage on; two windows double it (candidate C6).
12. **A stopped distro is never shown with numbers** (Q2); one that stops between the list call and its call
    is started by that call (W9f).
13. **What the check before a start as UID 0 leaves** (decision 24): root's own programs run as root, and the
    check stands on `/bin/sh` and on the tool it reads modes with. **The login session is outside the check**
    (decision 25).
14. **agy's log is no contract** (entry 6.2, the owner's choice; not in § K.28): another agy can write
    that line in another form, and then the account is not read. **And an unknown slash command reaches
    the model**: the check of § 4, revision 3, item 2, is all that stands between an agy that changed and
    a model call in every round.
15. **A vendor program of root in a folder that another user can write is run as root** (entry 7.1, the
    owner's choice; not in § K.28): whoever can write that folder decides what our timer runs as root in
    the next round. The row's warning names the folder, and nothing else stands against it.

**Judgements.** Those of 2026-11-25 are in the appendix, § K.5, and the one on root as a place, of 2026-11-26,
in § K.9, both word for word. The judge's conditions are rules above: the detail of the question (§ 3); root
reads only as itself, with its own `HOME`; nothing that another user can write is started as root; the
permission's text says it. **On the login session (decision 25) no judge was asked**: its text here is the
planner's. **Since entry 7.1 the third condition holds for the basic tools only; no judge was asked.**

`SECURITY.md` is rewritten by E2, E3 and E4, each for what it builds; the list: the appendix, § K.32.

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
- **The tab**: one webview for each window, kept while it is open; its model is the readings; its state
  is one grouping and one sort. One 15 s interval of the page, gone with the tab.
- **Children**: one `ssh` or `wsl.exe` alive at a time for each window, killed at 30 s, stdout capped at 64 KiB.
  For `wsl` in a round: 2 list calls, one users call for each running distro, one or two calls for each user,
  root among them, and one list call more before each of these.
  **Interrupted:** a reload leaves nothing on this side, and a killed `wsl.exe` no process of its call in the
  distro (W9e). **In a distro with systemd, by WSL:** one login session for each user that a call named, 16 at
  most, until the distro stops, and two empty files in that user's home at a first login (decision 25).
- **The timer**: one for each window, cleared when nothing may be read, disposed with the extension.
- **Log lines**: one when a place starts to fail and one when it reads again.
- **On a VM or in a distro, by our scripts**: nothing in 2.1.0. **E6 adds** a record of installs on this
  side (1000 rows at most, kept for ever), a lock folder in a home on the target for the time of an install,
  and the package `jq` with what its manager adds for it: sizes, who retires each and what an interruption
  leaves are in its document, § 10.
- **There, by Claude Code on a usage read**: four cache files, rewritten in place; and `~/.claude/backups/`, where
  **Claude Code keeps five files, about 0.9 MB, and deletes the older ones — seen on 2026-11-26 (W6)**, so it
  does not grow, and the policy is "every round" (§ 5). This plan deletes no file of Claude Code.
- **There, by `agy` on a start**: not measured (W8). Once a round for each place, since revision 2.
- **There, by `agy -p /quota` and by `codex app-server`** (revision 3): at most one start of each in a
  round for each place that has the tool. Measured: no conversation of agy, no model call of either.
  **What else either writes or fetches there was not measured.**
- **Calls to the vendor**, by Claude Code, none by our code: with the policy "every round", one usage call for
  each Claude account in each round of a window. The timer alone gives 6 rounds an hour, so 6 calls an hour
  for each account and window. *Refresh now* can start a round 60 s after the last one ended (§ 2), so a
  person who presses it again and again reaches up to about 60 an hour for each account and window. None
  while the policy is `off`. **From revision 3 also by agy, to Google, and by Codex, to OpenAI:** at most
  one of each in a round for each place that has the tool, so by the same count 6 an hour for a place and
  a window by the timer, and up to about 60 with *Refresh now*.

## Boundaries with other plans

The table, word for word: the appendix, § K.20. In short: **this revision replaces the per-host consent of
the terminal-door plan** (shipped in 0.87.0; § 3, *The upgrade*) and keeps its store key, its "no" rows and
*Not this host*; the todo T46 is closed by the stories document of E3, and T8 and T9 by that of E2; nothing
is changed in the plan of the timed-out ssh call, in the gauge of coai's own rounds or in the agent-models
mod, whose word rules are carried over and tested against its cases.

The todo files' own side: the appendix, § K.33.

## Build order

Steps 1 to 10, as written: the appendix, § K.57, word for word. In short: 1, the cut (merged, #1051); 2,
the approval of epoch 3; 3, E2; 4, the measurements; 5, E3; 6, E4; 7, ONE real-window check on the combined
build; 8, E5; 9, `review_feature`; 10, the extension release and the promotion.

**Where this order stands, 2026-11-27.** The release of step 10 is cut, as extension 2.1.0 — before R12 of
step 8 and the VM half of R7 had a result (entry 5.2). Its promotion waits for E6 (entry 5.1).
**Entries 6.2 and 6.3 are built outside these steps**, in a pull request of their own (`fix/accounts-agy-tab`).

**E6 — the installer — is parked (entry 6.1).** Its steps 11 to 16 hold as written for the day it comes
back: the appendix, § K.46, word for word. In short: 11, gate E6-G1 and E6's plan round, before any code;
12, E6.S1 and E6.S2; 13, the gate before any code of E6.S3 (entry 5.3); 14, E6.S3 to S5; 15, the owner's
rows on ONE VSIX, with R18, R12 and R7 as gates; 16, `review_feature`, the release, the promotion.

**Why this order:** the appendix, § K.2, word for word; for steps 11 to 16, E6's document, § 7.

## Test plan

**E1's tests stay** for what stays (the appendix, § I, *Test plan*; the short list: § K.12). **Deleted with what
they hold:** the tests of the automatic "yes", of the cap's eviction, of the setting at both values
(`terminalAccountsSetting.test.ts`), of the two notices, of the QuickPick's rows and its host scenario.

**The new tests of E2, E3 and E4 are in the appendix, § K.43, word for word**; extension 2.1.0 holds them.
They are: the permission, with the acceptance of entry 2.3; every door; a withdrawal and its races; the
upgrade; the session door (T8, T9); places; the WSL road; revision 2 of the script, with the join and the
account check; the table's model, with the three groupings and the order rule; the page, by RUNNING it; the
host scenario; the manifest and the help.

**The tests of E6** — the script and its container cases, the install "yes" and the record, the install in
the round, the tab, the documents — are in its document, § 6. Its rule for all of them: `npm test` never
gives the install script a legal operand, and no test installs anything on a real machine.

**The tests of revision 3** (entries 6.2 and 6.3) are written with its code, in the E3.S2 spec. They hold
at least what its item of the Definition of Done names, and that each new start ends under its limit.

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
      **But for residual 9, which the owner accepted (entry 4.3):** when an *Allow* window and a withdrawing
      window both end at once, the grant can stand, and nobody is told.
- [ ] The automatic allow, `coai.terminalAccountsAllowNewHosts`, its notices and its rows are gone from the code,
      the manifest and the help. **A per-host "yes" allows nothing**: no chat door reads a host before the
      permission or after a withdrawal, and the per-host question is gone.
- [ ] **A withdrawal cannot be undone by a stale writer**: it is a file of its own with a higher stamp; the test
      "withdraw, close the window, a stale rename, reload" ends withdrawn, and R12 (d) runs it. Left: residual 9.
- [ ] The session door asks before every spawn and has its own sentence for a change that waits (T9, T8).
- [ ] **The tab can be read.** The icon opens ONE tab, grouped by host by default: for each host its latest read result with
      its clock time, and one row for each installed tool with the account, the 5-hour window and the weekly
      window as "% left" with its reset; a shared account says so; usage that was not read says why; a failed
      host says the failure in full; a host without `jq` has the command and a copy button. Ages change while the
      tab stays open. Ctrl+F works. Every value from a VM is text, under a strict CSP.
- [ ] **The table can be grouped and sorted** (entry 4.1): *Group by* shows the same readings by server, by
      Claude account — its usage once, its places under it — and by user, and none hides a reading; every
      column header sorts both ways, a dash always last; the choice is in the tab's own state and nowhere else.
- [ ] **WSL is read on this PC**: every running distro and every Linux user in it, root among them, each as that
      user, each a reading of its own. **Root is read as itself** (decision 24): with its own `HOME`, never for
      another user; as root, of ours, only the users script and root's own read run, and our script starts
      nothing as root that another user can write — since entry 7.1: no basic tool; a vendor program that
      fails that check is started, and its row warns and names the folder. **The login session that WSL opens for a user it reads, with
      that user's profile, is named in the permission's text, the help and `SECURITY.md`** (decision 25). `wsl`
      is never given to `ssh` on Windows; with `wsl` refused nothing is
      enumerated. W9 has a recorded result, and what the documents promise as read-only is
      what W9 showed.
- [ ] **The script is revision 2**: its own `PATH`; a usage call that reads Claude only and runs the usage
      command only for the account it was sent for, so no account is asked twice in a round; each mode's worst path,
      KILL seconds counted, at least 2 s under the ceiling, held by a test; one new hash in the spec and in a
      test. No secret is in an argument, the environment, a reply or a log; the person's activity is not in a
      reply.
- [ ] **The script is revision 3** (entries 6.2 and 6.3): agy's row shows its quota — the 5-hour and the
      weekly bucket of each group, with its reset — and the account of agy's log, or says why not; Codex's
      row shows the account, the plan and the windows that `codex app-server` states, and no session file
      is read; a quota reply that shows a turn, or another command, is never taken, and that place is not
      asked again at that agy version; of agy's log the address of one line is printed and no other text;
      no credential file is opened; one new hash in the E3.S2 spec and in a test; the permission's text,
      the help and `SECURITY.md` name the two new starts and the vendors they call.
- [ ] **The Claude usage is on**, with the policy W6 selected, after W7 passed — or it is off and the release
      says why. No build ran the usage command by itself before W6 and W7 had recorded results.
- [ ] Nothing of a reading is persisted. Road (b) is not in the code.
- [ ] The real-window rows of E2, E3 and E4 have the owner's recorded result from ONE check, on a VSIX of the
      combined build, before any of the three merged (entry 4.2). The rows of E5 have a recorded result: R7
      passed on 2.1.0 — in WSL before its tag, on the VM after it — and **R12 passes on E6's build, which
      stands for 2.1.0 too** (entry 5.2). W6, W7, W8 and W9 are in the two RESULTS files.
- [ ] Docs with each epic: the help in five languages; `research/module_extension.md`, `research/module_tests.md`,
      `research/architecture.md`; `SECURITY.md`; the `POST_DEPLOY.md` question asked.
- [ ] **Extension 2.1.0 installs nothing** (entry 4.2): a place without `jq` shows the install command with a
      copy button, and no more.
- [ ] **E6 — nothing is installed without ONE more "yes"** (entry 5.1): asked once for every place, by a
      person's press; never given by the permission to read; its own withdrawal and *Withdraw the
      permission* (entry 5.4) each stop every later install, also against a stale window and a stale lock
      holder (R18). Where the login user has no rights, nothing is run and the command is shown. No setting
      for it exists.
- [ ] **E6 — one install at a time for a place, one deadline on the target, and a record from which no row
      goes.** The question and `SECURITY.md` say what an install can change on a machine (entry 5.8). Every
      item of the Definition of Done of E6's document, § 13, holds.
- [ ] **E6 — the gate before its S3 has a recorded answer** (entry 5.3); **R18, R12 and R7 passed on E6's
      build** before its merge.
- [ ] `review_feature` ran over the built epics before the tag of the plan's last release — E6's (entry
      5.1) — and every finding is resolved; that release is cut and verified, as 2.1.0 was; the plan is
      promoted with its deviations.

## Merge conditions

**For the pull request of the code epics E2, E3 and E4** (merged, #1055), **for E4, for the cut**
(merged, #1051) **and for the release 2.1.0**: the conditions as written: the appendix, § K.56, word for
word. What did not hold of them: 2.1.0 was released before R12 and the VM half of R7 had a result; R12 is
a gate of E6's build, and its result there stands for 2.1.0 (entry 5.2).

**For the pull request of E6, and for its release** (entries 5.1 and 5.3; parked, entry 6.1): six
conditions and the release's, which hold as written — the appendix, § K.46, word for word. In short: the
parent is not stopped, and gate E6-G1 and E6's plan round come before any code; the gate of entry 5.3
before any code of E6.S3; its tests and the container run green on the head that merges; the code gate and
CI; the owner's rows on ONE VSIX, with R18, R12 and R7 passed; `SECURITY.md` and the help.

## Epics

**Six epics. E1 to E5 are the release 2.1.0 — but for E5's R12, which runs on E6's build (entry 5.2); E6 is the next release (entries 4.2 and 5.1).**
The split is Fable's, after both consultations of the stop (`78e6d44b`, `7315feb0`).
The consultants' rule of 2026-11-19 holds for each, and it is why the split is as it is (the appendix, § K.3).
The rows keep the numbers of the stopped plan where their
meaning is kept (the appendix, § I, *E2*); R11, the row of the retired setting, is gone; R13 to R17 are new.

Stories documents are written by Opus when an epic starts:
`todo/PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E<n>.md`. Cornerstone quota, counted again
for six epics (entry 5.1): Q = ⌈2·E/5⌉ = ⌈2·6/5⌉ = 3. The cornerstone areas Fable names, each with its
reason in the appendix, § K.14: **(a)** what a
script touches there and what it prints; **(b)** the reply's frame and the parser; **(c)** who is contacted and
when; **(d)** the different results of a read, and a missing number that is never a zero; **(e)** who runs as
whom on this PC; **(f)** a change of a machine (E6, so the next release's). Areas (c) and (e) are
authorisation: their specs are Fable's by the security row, outside the quota. Marks made by the stories
documents: E3.S2 for (a) and (b); E6.S1 for (f) (entry 5.1).

Cornerstones: 3 of 3 — E1.S2, E3.S2, E6.S1

### E1 — The read-only feature, end to end · DONE, merged as #1047 (`d0241e45`)

The round, the script with its reply and parser, the runner's second caller, the refusals file, a list, the
documents. Its text: the appendix, § I, *E1*; its stories:
[PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E1.md).
What is kept, reworked and retired of it: *Re-review for epoch 3*, above.

### E2 — One permission, and a table that can be read · RELEASED in extension 2.1.0

- **Goal.** After one "yes" the owner reads, in a tab, every ssh host of the open terminals: its tools, their
  accounts and the limits that this build reads. Before that "yes" nothing is contacted.
- **Its text**, word for word — its scope, what it depends on, its stories (E2.S1 the permission, a SECURITY
  SPEC by Fable, outside the quota; S2 the table's model; S3 the tab; S4 the round under the permission; S5
  the documents) and its rows R1, R14, R15, R5, R6 and R8: the appendix, § K.44, with § K.39.

### E3 — WSL on this PC, and revision 2 of the script · RELEASED in extension 2.1.0

- **Goal.** A `Creds: wsl / …` terminal shows every running distro of this PC and every Linux user in it, root
  among them, each read as that user — root as root; and the script that every place runs is the one the usage
  read can be turned on with.
- **Its text** — with its stories (E3.S1 the WSL road, a SECURITY SPEC by Fable, outside the quota; E3.S2
  the scripts, revision 2, with the round's join, the CORNERSTONE; S3 places; S4 the documents) and its rows
  R13, R16, R17 and R9a: § K.44, with § K.39.
- **After the release — revision 3 of the script** (entries 6.2 and 6.3): agy's quota and account, and
  Codex from `codex app-server`. It changes the cornerstone E3.S2, whose spec is updated with the code.

### E4 — The Claude usage, on · RELEASED in extension 2.1.0

- **Goal.** The Claude windows show numbers.
- **Its text** — ONE constant, `USAGE_POLICY`, set to the row W6 selected — and its rows R3, R4, R2 and R9b:
  § K.44. The three epics merged as #1055 (`da9760fe`).

### E5 — The long runs before the release

- **Goal.** Know, on the build that will be released, what only time and two windows show.
- **Scope.** No product code. A row that fails is fixed by a new pull request and run again.
- **Depends on.** E2, E3 and E4 merged after their one check — or E2 and E3 merged and E4 recorded as off.
- **Done when.** Each row has a recorded result; **R7 and R12 are gates**, run ONCE, on the release build of
  E2 to E4 (entry 4.2). **As it went (entry 5.2):** R7 passed on 2.1.0 and R10 is recorded
  ([RESULTS_accounts_e5_long_runs.md](../research/RESULTS_accounts_e5_long_runs.md)); W8: § 5; R12 runs
  on E6's build.
- **The rows R7, R12, R10 and W8, each with its pass**: the appendix, § K.47, word for word. R12 is the
  run with two windows; a "no", a Stop or a withdrawal that is lost there fails the gate.

### E6 — The `jq` installer · PARKED (entry 6.1)

**Parked by the owner on 2026-11-27 (entry 6.1): as measured, it would install `jq` on no place of the
owner. No code of it exists.** It is the sixth epic of THIS plan (entry 5.1), for a release after 2.1.0.

- **Goal.** Decision 17: where `jq` is missing and needed, it is installed when the login user may; where
  the user may not, the tab shows the command and says why.
- **Its stories and spec**, ONE document:
  [PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E6.md](PLAN_the_terminal_panel_shows_the_accounts_and_their_usage_E6.md).
- **Its section as it stood** — the consent (Q3; entry 5.4), the five stories with the cornerstone E6.S1,
  what it depends on with the gate of entry 5.3, and done-when: the appendix, § K.46, word for word.

### Candidates

C2 to C9, one line each with what would justify it: the appendix, § K.11, word for word. Taking one up is a
change of this plan (kind `epics`), recorded in the ledger. C2 is taken (it is E2) and so is C9 (decision 24,
with the approval of epoch 3); C4a is gone. Open: C3, C4b, C5, C6, C7 and C8.
